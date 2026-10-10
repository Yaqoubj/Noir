"""Local-only subtitle alignment worker. No network access or media uploads."""
import json
import logging
import math
import os
import re
import subprocess
import sys
import tempfile
from pathlib import Path

import numpy as np
import pysubs2
from ffsubsync.ffsubsync import make_parser, make_reference_pipe, try_sync, validate_and_transform_args


class ScoreCapture(logging.Handler):
    score = -math.inf

    def emit(self, record):
        match = re.fullmatch(r"score: ([+-]?[\d.]+)", record.getMessage())
        if match:
            self.score = float(match.group(1))


def synchronize(video, subtitle, output):
    tools = Path(sys.executable).parent if getattr(sys, "frozen", False) else Path(__file__).parent
    ffmpeg = tools / "ffmpeg"
    capture = ScoreCapture()
    logger = logging.getLogger("ffsubsync.ffsubsync")
    logger.setLevel(logging.INFO)
    logger.addHandler(capture)
    # Only small, plausible corrections are eligible for automatic application.
    args = [video, "-i", subtitle, "-o", output, "--ffmpeg-path", str(ffmpeg),
            "--max-offset-seconds", "30", "--skip-infer-framerate-ratio",
            "--skip-sync-on-low-quality", "--min-score", "100",
            "--quality-max-offset-seconds", "30", "--max-framerate-deviation", "0.05",
            "--output-encoding", "utf-8"]
    with tempfile.TemporaryDirectory(prefix="noir-subtitles-") as scratch:
        reference_file = video
        embedded = False
        try:
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
        if speech.sum() < 1000 or not .03 < speech.mean() < .85:
            return {"accepted": False, "offset": 0, "scale": 1}
        result = {}
        try_sync(params, pipeline, result)
        offset = float(result.get("offset_seconds") or 0)
        scale = float(result.get("framerate_scale_factor") or 1)
        accepted = (bool(result.get("sync_was_successful")) and Path(output).is_file()
                    and capture.score >= 100 and math.isfinite(offset) and math.isfinite(scale)
                    and abs(offset) <= 30 and abs(scale - 1) <= 0.05)
        def overlap(path):
            cues = pysubs2.load(path, encoding="utf-8")
            active = np.zeros(len(speech), dtype=bool)
            for cue in cues:
                start, end = max(0, int(cue.start / 10)), min(len(active), int(cue.end / 10))
                if end > start:
                    active[start:end] = True
            shared = (active & speech).sum()
            return shared / max(1, active.sum()), shared / max(1, speech.sum())
        if accepted:
            precision, recall = overlap(output)
            previous_precision, _ = overlap(subtitle)
            meaningful_change = abs(offset) > 1 or abs(scale - 1) > .003
            accepted = (precision >= (.8 if embedded else .65) and recall >= (.5 if embedded else .35)
                        and (not meaningful_change or precision - previous_precision >= .05))
        return {"accepted": bool(accepted), "offset": offset, "scale": scale}


if __name__ == "__main__":
    try:
        print(json.dumps(synchronize(*sys.argv[1:4]), allow_nan=False))
    except Exception:
        print(json.dumps({"accepted": False, "offset": 0, "scale": 1}))
        sys.exit(1)
