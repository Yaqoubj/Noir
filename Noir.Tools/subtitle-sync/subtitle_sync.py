"""Local-only subtitle alignment worker. No network access or media uploads."""
import json
import logging
import math
import contextlib
import shutil
import re
import subprocess
import sys
import tempfile
from pathlib import Path

import numpy as np
import pysubs2
from alignment import decide_alignment, useful_reference
from ffsubsync.ffsubsync import make_parser, make_reference_pipe, try_sync, validate_and_transform_args


class ScoreCapture(logging.Handler):
    score = -math.inf

    def emit(self, record):
        match = re.fullmatch(r"score: ([+-]?[\d.]+)", record.getMessage())
        if match:
            self.score = float(match.group(1))


def synchronize(video, subtitle, output, audio_only=False):
    capture = ScoreCapture()
    logger = logging.getLogger("ffsubsync.ffsubsync")
    old_level = logger.level
    logger.setLevel(logging.INFO)
    logger.addHandler(capture)
    try:
        return _synchronize(video, subtitle, output, capture, audio_only)
    finally:
        logger.removeHandler(capture)
        logger.setLevel(old_level)


def _synchronize(video, subtitle, output, capture, audio_only=False):
    shutil.copyfile(subtitle, output)
    tools = Path(sys.executable).parent if getattr(sys, "frozen", False) else Path(__file__).parent
    ffmpeg = tools / "ffmpeg"
    # Only small, plausible corrections are eligible for automatic application.
    args = [video, "-i", subtitle, "-o", output, "--ffmpeg-path", str(ffmpeg),
            "--max-offset-seconds", "30", "--skip-infer-framerate-ratio",
            "--skip-sync-on-low-quality", "--min-score", "100",
            "--quality-max-offset-seconds", "30", "--max-framerate-deviation", "0.05",
            "--output-encoding", "utf-8"]
    if audio_only:
        # ffsubsync would otherwise pick up the built-in subtitle stream on its own.
        args += ["--reference-stream", "0:a:0"]
    with tempfile.TemporaryDirectory(prefix="noir-subtitles-") as scratch:
        reference_file = video
        embedded = False
        try:
            # Checking a built-in track against itself proves nothing, so --audio listens to the soundtrack instead.
            if audio_only:
                raise ValueError("audio reference requested")
            probe = subprocess.run([str(ffmpeg / "ffprobe.exe"), "-v", "error", "-show_streams", "-of", "json", video],
                                   capture_output=True, timeout=30, creationflags=0x08000000)
            streams = json.loads(probe.stdout).get("streams", [])
            reference = next((s for s in streams if s.get("codec_type") == "subtitle"
                              and s.get("codec_name") in {"subrip", "ass", "ssa", "webvtt", "mov_text"}
                              and not any(w in str(s.get("tags", {})).lower() for w in ("signs", "songs", "forced"))), None)
            if reference:
                extracted = Path(scratch) / "reference.srt"
                extraction = subprocess.run([str(ffmpeg / "ffmpeg.exe"), "-v", "error", "-nostdin", "-i", video,
                                             "-map", "0:" + str(reference["index"]), "-c:s", "srt", str(extracted)],
                                            capture_output=True, timeout=60, creationflags=0x08000000)
                if extraction.returncode == 0 and extracted.is_file():
                    reference_file = str(extracted)
                    embedded = True
        except (OSError, ValueError, subprocess.TimeoutExpired):
            pass
        args[0] = reference_file
        params = validate_and_transform_args(make_parser().parse_args(args))
        pipeline = make_reference_pipe(params).fit(reference_file)
        speech = np.asarray(pipeline.transform(reference_file)) > .5
        # Silence or nearly continuous noise cannot establish a trustworthy offset.
        if not useful_reference(speech):
            return {"accepted": False, "offset": 0, "scale": 1, "method": "none", "segments": []}
        result = {}
        try_sync(params, pipeline, result)
        offset = float(result.get("offset_seconds") or 0)
        scale = float(result.get("framerate_scale_factor") or 1)
        original = pysubs2.load(subtitle, encoding="utf-8")
        global_subs = pysubs2.load(output, encoding="utf-8")
        decision, aligned = decide_alignment(
            speech, [(c.start, c.end) for c in original], offset, scale,
            capture.score, bool(result.get("sync_was_successful")), embedded,
            [(c.start, c.end) for c in global_subs], audio_only)
        if decision["method"] == "piecewise":
            for cue, (start, end) in zip(original, aligned):
                cue.start, cue.end = int(start), int(end)
            original.save(output, encoding="utf-8")
        elif not decision["accepted"]:
            shutil.copyfile(subtitle, output)
        return decision


def plan(duration):
    """Sample windows for a stream of this length; empty windows mean the stream is too short to sample."""
    import sampled
    return {"windows": [[float(a), float(n)] for a, n in sampled.plan_windows(duration)],
            "extra": [[float(a), float(n)] for a, n in sampled.plan_extra_windows(duration)]}


def sampled_sync(job_path, subtitle, output):
    """Align a subtitle from short PCM samples (16 kHz mono s16le files); no video is read."""
    import sampled
    job = json.loads(Path(job_path).read_text(encoding="utf-8"))
    subs = pysubs2.load(subtitle, encoding="utf-8")
    cues = [(c.start, c.end) for c in subs]
    pcm = [Path(item["pcm"]).read_bytes() for item in job["samples"]]
    masks = sampled.speech_masks(pcm)
    decision, fitted = sampled.align_samples(
        [(float(item["start"]), mask) for item, mask in zip(job["samples"], masks)], cues, job.get("duration"))
    result = {"accepted": bool(decision["accepted"]), "offset": float(decision["offset"]), "scale": float(decision["scale"]),
              "method": str(decision["method"]), "reason": str(decision["reason"])}
    if result["accepted"]:
        # The fitted array is already rounded to whole milliseconds; text and formatting stay as pysubs2 keeps them.
        for cue, (start, end) in zip(subs, fitted):
            cue.start, cue.end = int(start), int(end)
        subs.save(output, encoding="utf-8")
    else:
        shutil.copyfile(subtitle, output)
    return result


if __name__ == "__main__":
    try:
        # Third-party progress/logging must never contaminate the JSON protocol.
        with contextlib.redirect_stdout(sys.stderr):
            if sys.argv[1:2] == ["--plan"]:
                decision = plan(float(sys.argv[2]))
            elif sys.argv[1:2] == ["--sampled"]:
                decision = sampled_sync(*sys.argv[2:5])
            else:
                decision = synchronize(*sys.argv[1:4], audio_only="--audio" in sys.argv[4:])
        print(json.dumps(decision, allow_nan=False))
    except Exception:
        if sys.argv[1:2] == ["--sampled"]:
            print(json.dumps({"accepted": False, "offset": 0, "scale": 1, "method": "none", "reason": "error"}))
            sys.exit(1)
        print(json.dumps({"accepted": False, "offset": 0, "scale": 1, "method": "none", "segments": []}))
        sys.exit(1)
