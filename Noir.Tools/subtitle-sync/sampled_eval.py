"""Offline comparison on read-only media. stdout contains JSON lines only."""
import argparse
import contextlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import time
import numpy as np
import pysubs2
from alignment import cue_array, transform_cues
from sampled import plan_extra_windows, plan_windows, align_samples, speech_masks

ROOT = Path(__file__).resolve().parents[2]
WORK = ROOT / 'work/subtitle-round/a1'
TMP = WORK / 'tmp'
FFMPEG = ROOT / 'work/subtitle-runtime/ffmpeg'


def load_cues(path):
    return cue_array([(c.start, c.end) for c in pysubs2.load(str(path), encoding='utf-8')])


def anchor_errors(actual, expected):
    if not len(actual) or len(actual) != len(expected):
        return None
    return (np.max(np.abs(actual[[0, len(actual) // 2, -1]] -
                          expected[[0, len(expected) // 2, -1]]), axis=1) / 1000).tolist()


def extract_samples(video, duration, windows=None):
    samples, total = [], 0
    for start, length in plan_windows(duration) if windows is None else windows:
        result = subprocess.run([str(FFMPEG / 'ffmpeg.exe'), '-v', 'error', '-nostdin',
                                 '-ss', str(start), '-t', str(length), '-i', str(video),
                                 '-map', '0:a:0', '-vn', '-sn', '-dn', '-ac', '1',
                                 '-ar', '16000', '-c:a', 'pcm_s16le', '-f', 's16le', 'pipe:1'],
                                capture_output=True, check=True, timeout=180)
        total += len(result.stdout)
        samples.append((start, np.frombuffer(result.stdout, dtype='<i2').copy()))
    return samples, total


def webrtc_speech(pcm):
    import webrtcvad
    vad = webrtcvad.Vad(3)
    raw = pcm.astype('<i2', copy=False).tobytes()
    return np.array([vad.is_speech(raw[i:i + 320], 16000)
                     for i in range(0, len(raw) - 319, 320)], dtype=bool)


class FullReferenceCache:
    """Reuse exactly the existing full reference pipeline across subtitle copies.

    No algorithm/decision change. The first run measures decoding; later calls
    still run synchronize/try_sync but reuse its immutable audio reference.
    """
    def __init__(self):
        self.pipeline = None
        self.hit = False

    def factory(self, original):
        def make(params):
            if self.pipeline is None:
                self.pipeline = original(params)
                return self.pipeline
            self.hit = True
            pipeline = self.pipeline

            class Reused:
                def fit(self, *args, **kwargs):
                    return self

                def transform(self, *args, **kwargs):
                    return pipeline.transform(*args, **kwargs)

            return Reused()
        return make


def full_analysis(video, source, output, cache=None):
    import subtitle_sync as worker
    old_validate, old_factory = worker.validate_and_transform_args, worker.make_reference_pipe
    old_popen = subprocess.Popen
    pcm_bytes = [0]

    class CountedPipe:
        def __init__(self, pipe):
            self.pipe = pipe

        def read(self, *args):
            data = self.pipe.read(*args)
            pcm_bytes[0] += len(data)
            return data

        def __getattr__(self, name):
            return getattr(self.pipe, name)

    def popen(command, *args, **kwargs):
        process = old_popen(command, *args, **kwargs)
        if isinstance(command, (list, tuple)) and 's16le' in command and process.stdout is not None:
            process.stdout = CountedPipe(process.stdout)
        return process

    def validate(args):
        args.ffmpeg_path = str(FFMPEG)
        return old_validate(args)

    worker.validate_and_transform_args = validate
    subprocess.Popen = popen
    if cache is not None:
        worker.make_reference_pipe = cache.factory(old_factory)
    try:
        with contextlib.redirect_stdout(sys.stderr):
            result = worker.synchronize(str(video), str(source), str(output), audio_only=False)
            return result, pcm_bytes[0]
    finally:
        worker.validate_and_transform_args, worker.make_reference_pipe = old_validate, old_factory
        subprocess.Popen = old_popen


def evaluate(video, subtitle, out, perturb=None, vad='webrtc', different_cut=False,
             reference=None, cache=None, extracted=None):
    TMP.mkdir(parents=True, exist_ok=True)
    tempfile.tempdir = str(TMP)
    os.environ.update(TMP=str(TMP), TEMP=str(TMP), TMPDIR=str(TMP))
    out = Path(out).resolve()
    # Confine every generated file to the task's authorized output tree.
    if not out.is_relative_to(WORK.resolve()):
        raise ValueError('--out must be under work/subtitle-round/a1')
    out.mkdir(parents=True, exist_ok=True)
    probe = subprocess.run([str(FFMPEG / 'ffprobe.exe'), '-v', 'error',
                            '-show_entries', 'format=duration', '-of', 'json', str(video)],
                           capture_output=True, check=True, timeout=30)
    duration = float(json.loads(probe.stdout)['format']['duration'])
    source, output = out / 'input.srt', out / 'full.srt'
    if Path(subtitle).resolve() in (source, output):
        raise ValueError('source subtitle must differ from generated paths')
    shutil.copyfile(subtitle, source)
    original = load_cues(source)
    expected = original if reference is None else np.asarray(reference)
    if perturb or different_cut:
        subs = pysubs2.load(str(source), encoding='utf-8')
        changed = original.copy()
        if perturb:
            offset, scale = perturb
            changed = transform_cues(changed, scale, offset)
        if different_cut:
            changed[original[:, 0] >= (original[0, 0] + original[-1, 1]) / 2] += 20000
        if np.any(changed < 0):
            raise ValueError('perturbation creates negative SRT times; refusing serializer clipping')
        for cue, (a, b) in zip(subs, changed):
            cue.start, cue.end = int(a), int(b)
        subs.save(str(source), encoding='utf-8')
    cues = load_cues(source)
    if (perturb or different_cut) and not np.array_equal(cues, changed):
        raise ValueError('subtitle serialization changed cue times or count')
    before = time.perf_counter()
    full, full_pcm_bytes = full_analysis(video, source, output, cache)
    full_wall = time.perf_counter() - before
    full_cues = load_cues(output)
    before = time.perf_counter()
    if extracted is None:
        pcm, pcm_bytes = extract_samples(video, duration)
        extraction_wall = time.perf_counter() - before
    else:
        pcm, pcm_bytes, extraction_wall = extracted
    row = dict(video=str(video), subtitle=str(subtitle), perturb=perturb,
               different_cut=different_cut, duration_s=duration, full=full,
               full_wall_s=full_wall, full_reference_cached=bool(cache and cache.hit),
               full_pcm_bytes=full_pcm_bytes,
               full_errors_truth=anchor_errors(full_cues, expected) if perturb else None,
               full_anchors=full_cues[[0, len(full_cues) // 2, -1]].tolist(),
               sampled={}, pcm_bytes=pcm_bytes, extraction_wall_s=extraction_wall,
               sampled_pcm_cached=extracted is not None,
               truth_basis='unperturbed sidecar' if reference is None else 'unperturbed full output')
    def detected(chunks):
        return [(start, mask) for (start, _), mask in zip(chunks, speech_masks([p.tobytes() for _, p in chunks]))]
    before = time.perf_counter()
    decision, fitted = align_samples(detected(pcm), cues, duration)
    if decision['reason'] in ('too-little-speech', 'poor-coverage'):
        more, extra_bytes = extract_samples(video, duration, plan_extra_windows(duration))
        row['pcm_bytes'] += extra_bytes
        decision, fitted = align_samples(detected(sorted(pcm + more, key=lambda x: x[0])), cues, duration)
        decision['extended'] = True
    row['sampled']['webrtc'] = dict(decision=decision, errors_full=anchor_errors(fitted, full_cues),
                                    errors_truth=anchor_errors(fitted, expected) if perturb else None,
                                    wall_s=time.perf_counter() - before + extraction_wall,
                                    anchors=fitted[[0, len(fitted) // 2, -1]].tolist())
    return row, full_cues, (pcm, pcm_bytes, extraction_wall)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('video', type=Path)
    parser.add_argument('subtitle', type=Path)
    parser.add_argument('--perturb', help='input copy: video_time * scale + offset (offset,scale)')
    parser.add_argument('--out', type=Path, default=WORK / 'single')
    parser.add_argument('--vad', choices=['webrtc'], default='webrtc')
    parser.add_argument('--different-cut', action='store_true')
    args = parser.parse_args()
    perturb = tuple(map(float, args.perturb.split(','))) if args.perturb else None
    if perturb and (len(perturb) != 2 or not np.isfinite(perturb).all() or perturb[1] <= 0):
        parser.error('--perturb needs finite offset,positive-scale')
    try:
        row, _, _ = evaluate(args.video, args.subtitle, args.out, perturb, args.vad, args.different_cut)
        row['status'] = 'completed'
    except Exception as error:
        row = dict(status='blocked', video=str(args.video), subtitle=str(args.subtitle),
                   error=f'{type(error).__name__}: {error}')
        print(json.dumps(row, allow_nan=False))
        return 1
    print(json.dumps(row, allow_nan=False))
    return 0


if __name__ == '__main__':
    sys.exit(main())
