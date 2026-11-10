"""Deterministic heard-only alignment. Times: seconds, except millisecond cues.

No array represents unheard audio. Each sample is correlated only with itself;
evidence is pooled per candidate model, so noisy samples that have no peak of
their own can still vote for the true offset. Gaps have no value at all.
"""
import math
import numpy as np
from alignment import (RATE, FPS_RATIOS, cue_array, transform_cues, _intervals,
                       activity_mask, useful_reference, _valid_model)

SHIFTS = np.arange(-3000, 3001)
MIN_JOINT_Z = 4.5       # pooled peak against the spread over all shifts
MAX_RIVAL = .85         # best peak more than 1 s away, as a share of the peak
MIN_RANK = .80          # share of a sample's own shifts that score below the model
MAX_MOVE = .35          # seconds: leave-one-out tolerance
CONFLICT_Z = 6.         # a sample's own peak this clear is evidence of a different cut
CONFLICT_RIVAL = .75


def plan_windows(duration_s):
    duration = float(duration_s)
    if not math.isfinite(duration) or duration < 720:
        return []
    margin = max(90., .05 * duration)
    count = 8 if duration >= 3600 else 6
    length = min(150., max(60., .12 * duration / count))
    budget = max(.15 * duration, 360.)
    while count > 4 and count * length > budget:
        count -= 1
    span = duration - 2 * margin
    if count * length > min(budget, span):
        return []
    return [(float(margin + i * (span - length) / (count - 1)), length)
            for i in range(count)]


def plan_extra_windows(duration_s):
    """Between the planned windows, for films whose first samples miss dialogue.

    Each stays inside its gap, shortened if needed; gaps under 20 s are skipped.
    """
    planned = plan_windows(duration_s)
    extra = []
    for (a, length), (b, _) in zip(planned, planned[1:]):
        gap = b - (a + length)
        if gap < 20:
            continue
        size = min(length, gap)
        start = max(a + length, min((a + b) / 2, b - size))
        extra.append((float(start), float(size)))
    return extra


def speech_masks(chunks, passes=2):
    """One 100 Hz speech mask per chunk of 16 kHz mono s16le PCM bytes.

    WebRTC's detector adapts over minutes, so a cold start on a short sample marks most of
    it as speech. It first listens to the already-downloaded samples to adapt, which costs
    no extra data, and decides on the final pass.
    """
    import webrtcvad
    vad = webrtcvad.Vad(3)
    for _ in range(passes):
        for pcm in chunks:
            for i in range(0, len(pcm) - 319, 320):
                vad.is_speech(pcm[i:i + 320], 16000)
    return [np.array([vad.is_speech(pcm[i:i + 320], 16000) for i in range(0, len(pcm) - 319, 320)], dtype=bool)
            for pcm in chunks]


def _curve(start, speech, cues, seed):
    """Phi correlation of heard speech and clipped cue activity for every shift."""
    n = len(speech)
    scaled = cues * seed - start * 1000
    near = (scaled[:, 1] > -30000) & (scaled[:, 0] < n * 10 + 30000)
    if near.sum() < 8:
        return None
    intervals = _intervals(scaled[near])
    prefix = np.r_[0, np.cumsum(speech, dtype=np.int64)]
    a = np.clip(intervals[:, 0, None] + SHIFTS, 0, n)
    b = np.clip(intervals[:, 1, None] + SHIFTS, 0, n)
    shared = (prefix[b] - prefix[a]).sum(axis=0).astype(float)
    active = (b - a).sum(axis=0).astype(float)
    heard = float(prefix[-1])
    denominator = active * (n - active) * heard * (n - heard)
    # With almost no cue inside the heard window the correlation means nothing.
    valid = (active >= .05 * n) & (active <= .95 * n) & (denominator > 0)
    phi = np.zeros(len(SHIFTS))
    phi[valid] = (n * shared[valid] - active[valid] * heard) / np.sqrt(denominator[valid])
    return phi


def _joint(curves, usable, skip=None):
    total = np.zeros(len(SHIFTS))
    for i, curve in enumerate(curves):
        if curve is not None and i != skip:
            total += curve
    return total / max(1, usable - (0 if skip is None else 1))


def _peak(values):
    """Index of the peak, its robust z-score, and the best rival more than 1 s away."""
    best = int(np.argmax(values))
    median = float(np.median(values))
    sigma = max(1e-4, 1.4826 * float(np.median(np.abs(values - median))))
    far = np.abs(SHIFTS - SHIFTS[best]) > 100
    height = max(1e-9, float(values[best]) - median)
    return best, (float(values[best]) - median) / sigma, (float(values[far].max()) - median) / height


def _counts(samples, cues):
    counts = []
    for start, speech in samples:
        active = activity_mask(cues - start * 1000, len(speech))
        counts.append((int(np.count_nonzero(active & speech)),
                       int(active.sum()), int(speech.sum())))
    return np.asarray(counts, dtype=float).reshape(-1, 3)


def _metrics(counts):
    shared, active, speech = np.asarray(counts).T
    precision = shared / np.maximum(1, active)
    recall = shared / np.maximum(1, speech)
    f1 = 2 * shared / np.maximum(1, active + speech)
    return np.array([precision, recall, f1]).T


def align_samples(samples, cues, duration_s=None):
    """Fit one global model, or return an untouched copy with an explicit reason.

    duration_s is advisory: supplied sample anchors always determine the fit.
    Callers should supply disjoint heard intervals (as plan_windows does).
    """
    cues = cue_array(cues)
    samples = [(float(s), np.asarray(v, dtype=bool)) for s, v in samples]
    decision = dict(accepted=False, offset=0., scale=1., method='none',
                    segments=[], reason='too-little-speech', samples=[])

    def reject(reason):
        decision['reason'] = reason
        return decision, cues.copy()

    if (not len(cues) or not np.isfinite(cues).all() or
            np.any(cues[:, 1] <= cues[:, 0]) or np.any(np.diff(cues, axis=0) < 0)):
        return reject('invalid-model')
    if any(not math.isfinite(s) or s < 0 or v.ndim != 1 for s, v in samples):
        return reject('invalid-model')
    samples.sort(key=lambda sample: sample[0])
    if any(a + len(v) / RATE > b + 1e-9
           for (a, v), (b, _) in zip(samples, samples[1:])):
        return reject('invalid-model')
    useful = [useful_reference(v) for _, v in samples]
    usable = sum(useful)
    required = max(4, math.ceil(.75 * usable))
    if usable < required:
        return reject('too-little-speech')
    best = None
    for seed in FPS_RATIOS:
        curves = [_curve(s, v, cues, seed) if ok else None
                  for (s, v), ok in zip(samples, useful)]
        joint = _joint(curves, usable)
        at = int(np.argmax(joint))
        # Stable seed order breaks exact ties without hash-order dependence.
        if abs(int(SHIFTS[at])) < 2990 and (best is None or joint[at] > best[0]):
            best = (float(joint[at]), float(seed), curves, joint)
    if best is None:
        return reject('no-agreement')
    _, seed, curves, joint = best
    at, joint_z, rival = _peak(joint)
    offset = float(SHIFTS[at]) / RATE
    decision['joint'] = dict(seed=seed, offset=offset, value=float(joint[at]), z=joint_z, rival=rival)
    diagnostics, agreed, conflict, drifted = [], [], False, False
    for (start, speech), curve in zip(samples, curves):
        entry = dict(start=start, agreed=False, usable=curve is not None)
        if curve is not None:
            rank = float(np.mean(curve < curve[at]))
            own, own_z, own_rival = _peak(curve)
            entry.update(rank=rank, value=float(curve[at]), own_offset=float(SHIFTS[own]) / RATE,
                         own_z=own_z, own_rival=own_rival)
            clear = own_z >= CONFLICT_Z and own_rival <= CONFLICT_RIVAL
            if curve[at] > 0 and rank >= MIN_RANK:
                entry['agreed'] = True
                agreed.append((start + len(speech) / (2 * RATE) - offset) / seed)
                # A clean local peak near the model but off by too much means the model is wrong.
                drifted = drifted or (clear and abs(int(SHIFTS[own]) - int(SHIFTS[at])) / RATE > MAX_MOVE)
            elif (own_z >= CONFLICT_Z and own_rival <= CONFLICT_RIVAL
                  and abs(int(SHIFTS[own]) - int(SHIFTS[at])) > 100):
                conflict = True
        diagnostics.append(entry)
    decision['samples'] = diagnostics
    if joint_z < MIN_JOINT_Z or rival > MAX_RIVAL or len(agreed) < required:
        return reject('no-agreement')
    scale = seed
    if not _valid_model(offset, scale):
        return reject('invalid-model')
    if np.ptp(agreed) < .70 * np.ptp(cues / 1000):
        return reject('poor-coverage')
    if drifted:
        return reject('unstable')
    for i, curve in enumerate(curves):
        if curve is not None:
            left_out = _joint(curves, usable, skip=i)
            if abs(int(np.argmax(left_out)) - at) / RATE > MAX_MOVE:
                return reject('unstable')
    if conflict:
        return reject('conflicting-sample')
    fitted = transform_cues(cues, scale, offset)
    if (not np.isfinite(fitted).all() or np.any(fitted < 0) or
            np.any(fitted[:, 1] <= fitted[:, 0]) or np.any(np.diff(fitted, axis=0) < 0)):
        return reject('invalid-model')
    if abs(offset) < .25 and abs(scale - 1) < .0005 and np.max(np.abs(fitted - cues)) < 250:
        return reject('already-aligned')
    before, after = [_counts(samples, c) for c in (cues, fitted)]
    p0, r0, f0 = _metrics(before.sum(axis=0))
    p1, r1, f1 = _metrics(after.sum(axis=0))
    decision['metrics'] = dict(before=[float(p0), float(r0), float(f0)],
                               after=[float(p1), float(r1), float(f1)])
    if any(ok and new + .02 + 1e-9 < old
           for ok, old, new in zip(useful, _metrics(before)[:, 2], _metrics(after)[:, 2])):
        return reject('local-regression')
    if f1 - f0 < .08 or p1 - p0 < .03 or p1 < .30 or r1 < .50:
        return reject('no-improvement')
    decision.update(accepted=True, offset=float(offset), scale=float(scale), method='sampled',
                    segments=[dict(start_s=0., offset_s=float(offset))], reason='applied')
    return decision, fitted
