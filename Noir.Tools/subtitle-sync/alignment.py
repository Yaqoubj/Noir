"""Conservative alignment on numpy arrays, independent of media I/O.

Cue times are milliseconds; offsets are seconds; scales multiply original times.
All activity is sampled at 100 Hz. See README.md for acceptance thresholds.
"""
from itertools import combinations
import math
import numpy as np

RATE = 100
WINDOW_S = 240
MAX_SEGMENTS = 3
MIN_GAIN = .05


def cue_array(cues):
    return np.asarray(cues, dtype=float).reshape(-1, 2)


def transform_cues(cues, scale=1., offset=0.):
    return np.rint(cue_array(cues) * scale + offset * 1000)


def _intervals(cues):
    """Union, including activity outside the reference to prevent clipping gains."""
    intervals = []
    for a, b in sorted(np.floor(cue_array(cues) / 10).astype(np.int64).tolist()):
        if b <= a:
            continue
        if intervals and a <= intervals[-1][1]:
            intervals[-1][1] = max(b, intervals[-1][1])
        else:
            intervals.append([a, b])
    return np.asarray(intervals, dtype=np.int64).reshape(-1, 2)


def activity_mask(cues, length):
    mask = np.zeros(length, dtype=bool)
    for a, b in _intervals(cues):
        mask[max(0, a):max(0, min(length, b))] = True
    return mask


def overlap(speech, cues):
    speech = np.asarray(speech, dtype=bool)
    intervals = _intervals(cues)
    total = (intervals[:, 1] - intervals[:, 0]).sum()
    shared = np.count_nonzero(activity_mask(cues, len(speech)) & speech)
    return np.array([shared / max(1, total), shared / max(1, speech.sum())])


def useful_reference(speech):
    return len(speech) > 0 and np.count_nonzero(speech) >= 1000 and .03 < np.mean(speech) < .85


def _thresholds(embedded):
    return np.array([.8, .5] if embedded else [.65, .35])


def _valid_model(offset, scale):
    return math.isfinite(offset) and math.isfinite(scale) and abs(offset) <= 30 and abs(scale - 1) <= .05


def local_offsets(speech, cues, scale=1., offset=0., embedded=False):
    """Find unique local peaks within +/-15 s of an affine model, every 10 ms.

    Non-overlapping four-minute windows need >=8 cues and >=10 s speech.
    Prefix sums avoid a scipy dependency. Reject boundary and ambiguous peaks.
    """
    speech = np.asarray(speech, dtype=bool)
    fitted = transform_cues(cues, scale, offset)
    prefix = np.r_[0, np.cumsum(speech, dtype=np.int64)]
    shifts = np.arange(-1500, 1501)
    windows = []
    for start in range(0, len(speech), WINDOW_S * RATE):
        end = min(len(speech), start + WINDOW_S * RATE)
        selected = (fitted[:, 0] >= (start + 1500) * 10) & (fitted[:, 1] <= (end - 1500) * 10)
        local_speech = speech[start:end]
        if selected.sum() < 8 or not useful_reference(local_speech):
            continue
        intervals = _intervals(fitted[selected])
        a = intervals[:, 0, None] + shifts
        b = intervals[:, 1, None] + shifts
        shared = (prefix[b] - prefix[a]).sum(axis=0)
        duration = (intervals[:, 1] - intervals[:, 0]).sum()
        precision = shared / max(1, duration)
        best = int(np.argmax(precision))
        shift = shifts[best]
        rivals = precision[np.abs(shifts - shift) > 100]
        if (abs(shift) >= 1490 or precision[best] < _thresholds(embedded)[0]
                or precision[best] - local_speech.mean() < .20
                or (len(rivals) and precision[best] - rivals.max() < .03)):
            continue
        windows.append((float(np.median(cue_array(cues)[selected].mean(axis=1))) / 1000,
                        offset + shift / RATE))
    return np.asarray(windows, dtype=float).reshape(-1, 2)


def _step_models(windows):
    """Each plateau needs two independent windows; tolerate <=450 ms scatter."""
    count = len(windows)
    for segments in range(2, MAX_SEGMENTS + 1):
        for cuts in combinations(range(2, count - 1), segments - 1):
            bounds = (0,) + cuts + (count,)
            groups = [windows[a:b] for a, b in zip(bounds, bounds[1:])]
            if any(len(g) < 2 for g in groups):
                continue
            offsets = [float(np.median(g[:, 1])) for g in groups]
            # Keep an already aligned plateau unchanged despite VAD edge jitter.
            offsets = [0. if abs(off) <= .10 else off for off in offsets]
            if any(np.max(np.abs(g[:, 1] - off)) > .45 for g, off in zip(groups, offsets)):
                continue
            if any(abs(a - b) < .8 for a, b in zip(offsets, offsets[1:])):
                continue
            yield groups, offsets


def _split_at_gaps(speech, cues, scale, groups, offsets):
    """Find the best existing cue gap between the two windows witnessing a cut.

    Prefix sums of per-cue intersections locate the transition. Only gaps that
    remain gaps after correction are eligible; never clip or reorder cues.
    """
    prefix = np.r_[0, np.cumsum(speech, dtype=np.int64)]
    n = len(speech)
    scores = []
    for off in offsets:
        samples = np.floor(transform_cues(cues, scale, off) / 10).astype(np.int64)
        scores.append(np.r_[0, np.cumsum(prefix[np.clip(samples[:, 1], 0, n)]
                                        - prefix[np.clip(samples[:, 0], 0, n)])])
    cuts = [0]
    for j in range(len(offsets) - 1):
        low, high = groups[j][-1, 0], groups[j + 1][0, 0]
        gaps = []
        for i in range(cuts[-1] + 1, len(cues)):
            at = (cues[i - 1, 1] + cues[i, 0]) / 2000
            if (low <= at <= high and cues[i - 1, 1] <= cues[i, 0]
                    and cues[i - 1, 1] * scale + offsets[j] * 1000
                    <= cues[i, 0] * scale + offsets[j + 1] * 1000):
                gain = scores[j][i] - scores[j + 1][i]
                gaps.append((gain, -abs(at - (low + high) / 2), i))
        if not gaps:
            return None
        cuts.append(max(gaps)[2])
    return cuts + [len(cues)]


def _no_local_regression(speech, before, after):
    """Protect good regions as well as whole-file scores, using fixed windows."""
    masks = [activity_mask(c, len(speech)) for c in (before, after)]
    for start in range(0, len(speech), WINDOW_S * RATE):
        end = min(len(speech), start + WINDOW_S * RATE)
        ref = speech[start:end]
        if not useful_reference(ref):
            continue
        metrics = []
        for mask in masks:
            active = mask[start:end]
            shared = np.count_nonzero(active & ref)
            metrics.append(np.array([shared / max(1, active.sum()), shared / max(1, ref.sum())]))
        if np.any(metrics[1] + 1e-9 < metrics[0]):
            return False
    return True


def plausible_candidate(speech, original, global_cues, candidate, cuts, embedded=False):
    """Require clear aggregate gains and no local sacrifice for aggregate gains."""
    original, global_cues, candidate = map(cue_array, (original, global_cues, candidate))
    if (len(candidate) != len(original) or not np.isfinite(candidate).all()
            or np.any(candidate[:, 0] < 0) or np.any(candidate[:, 1] <= candidate[:, 0])
            or np.any(np.diff(candidate[:, 0]) < 0) or np.any(np.diff(candidate[:, 1]) < 0)):
        return False
    for cut in cuts[1:-1]:
        if candidate[cut - 1, 1] > candidate[cut, 0]:
            return False
    current, previous, proposed = [overlap(speech, c) for c in (global_cues, original, candidate)]
    if (np.any(proposed < _thresholds(embedded)) or np.any(proposed - current < MIN_GAIN)
            or np.any(proposed + 1e-9 < previous)):
        return False
    if not all(_no_local_regression(speech, c, candidate) for c in (original, global_cues)):
        return False
    for a, b in zip(cuts, cuts[1:]):
        # All versions are compared in the same reference interval.
        start = 0 if a == 0 else int((candidate[a - 1, 1] + candidate[a, 0]) / 20)
        end = len(speech) if b == len(candidate) else int((candidate[b - 1, 1] + candidate[b, 0]) / 20)
        start, end = max(0, start), min(len(speech), end)
        ref = speech[start:end]
        if b - a < 16 or end - start < WINDOW_S * RATE or not useful_reference(ref):
            return False
        metrics = [overlap(ref, c[a:b] - start * 10) for c in (candidate, global_cues, original)]
        if (np.any(metrics[0] < _thresholds(embedded))
                or any(np.any(metrics[0] + 1e-9 < m) for m in metrics[1:])):
            return False
    return True


def decide_alignment(speech, cues, offset=0., scale=1., score=0., global_success=False,
                     embedded=False, global_cues=None, audio_check=False):
    """Return (JSON-safe decision, chosen cues), without mutating inputs.

    Piecewise can rescue poor global overlap, but not a failed ffsubsync quality
    gate. At most three segments share a scale, with separate bounded offsets.
    """
    speech = np.asarray(speech, dtype=bool)
    cues = cue_array(cues)
    none = {"accepted": False, "offset": 0., "scale": 1., "method": "none", "segments": []}
    if (not useful_reference(speech) or not len(cues) or not np.isfinite(cues).all()
            or np.any(cues[:, 1] <= cues[:, 0]) or np.any(np.diff(cues[:, 0]) < 0)
            or not global_success or not score >= 100 or not _valid_model(offset, scale)):
        return none, cues.copy()
    fitted = transform_cues(cues, scale, offset) if global_cues is None else cue_array(global_cues)
    if len(fitted) != len(cues) or not np.isfinite(fitted).all():
        return none, cues.copy()
    before, baseline = overlap(speech, cues), overlap(speech, fitted)
    meaningful = abs(offset) > 1 or abs(scale - 1) > .003
    # A built-in track checked against the soundtrack: speech detection caps precision lower,
    # so accept a lower floor only with stronger coverage and a clearly larger gain.
    floor, gain = (np.array([.55, .5]), .10) if audio_check and not embedded else (_thresholds(embedded), .05)
    accepted = (np.all(baseline >= floor) and np.all(baseline + 1e-9 >= before)
                and (not meaningful or baseline[0] - before[0] >= gain)
                and _no_local_regression(speech, cues, fitted))
    decision = {"accepted": bool(accepted), "offset": float(offset), "scale": float(scale),
                "method": "global" if accepted else "none",
                "segments": [{"start_s": 0., "offset_s": float(offset)}] if accepted else []}
    windows = local_offsets(speech, cues, scale, offset, embedded)
    if (len(windows) >= 4 and np.max(np.abs(windows[:, 1] - offset)) <= .35 and accepted
            and np.ptp(windows[:, 0]) >= .75 * np.ptp(cues[:, 0] / 1000)
            and baseline[0] >= .9):
        return decision, fitted
    proposals = []
    # Known fps seeds recover large missed drift whose tail is beyond +/-15 s.
    # These are hypotheses only; all acceptance and scale gates still apply.
    seeds = {scale, 1., 25 / 23.976, 23.976 / 25, 24 / 23.976, 23.976 / 24, 25 / 24, 24 / 25}
    for seed in sorted(seeds):
        local = windows if seed == scale else local_offsets(speech, cues, seed, offset, embedded)
        if len(local) < 4 or np.ptp(local[:, 0]) < 3 * WINDOW_S:
            continue
        slope, intercept = np.polyfit(local[:, 0], local[:, 1], 1)
        if np.max(np.abs(local[:, 1] - (slope * local[:, 0] + intercept))) <= .35:
            proposals.append((seed + slope, [float(intercept)], [0, len(cues)]))
        for groups, offsets in _step_models(local):
            cuts = _split_at_gaps(speech, cues, seed, groups, offsets)
            if cuts is not None:
                proposals.append((seed, offsets, cuts))
    best = None
    for candidate_scale, offsets, cuts in proposals:
        if not all(_valid_model(off, candidate_scale) for off in offsets):
            continue
        candidate = np.concatenate([transform_cues(cues[a:b], candidate_scale, off)
                                    for a, b, off in zip(cuts, cuts[1:], offsets)])
        if not plausible_candidate(speech, cues, fitted, candidate, cuts, embedded):
            continue
        merit = float(overlap(speech, candidate).sum())
        if best is None or merit > best[0]:
            segments = [{"start_s": 0. if a == 0 else float(cues[a, 0] / 1000), "offset_s": float(off)}
                        for a, off in zip(cuts, offsets)]
            best = (merit, candidate, {"accepted": True, "offset": float(offsets[0]),
                                      "scale": float(candidate_scale), "method": "piecewise", "segments": segments})
    return (best[2], best[1]) if best else (decision, fitted if accepted else cues.copy())

