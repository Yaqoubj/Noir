"""Optional end-to-end offline smoke test of the built Windows executable.

Run with the build venv python; writes fixtures only under work/subtitle-smoke.
The Matroska fixture contains an embedded text track, without video/audio payload.
"""
import json
import math
import subprocess
import sys
from pathlib import Path
import numpy as np
import pysubs2

from test_alignment import fixture
from alignment import overlap

ROOT = Path(__file__).resolve().parents[3]
RUNTIME = ROOT / "work/subtitle-runtime"
SCRATCH = ROOT / "work/subtitle-smoke"


def save(cues, path):
    subs = pysubs2.SSAFile()
    for i, (start, end) in enumerate(cues):
        subs.append(pysubs2.SSAEvent(start=int(round(start)), end=int(round(end)),
                                    text="Dialogue fixture %d" % i))
    subs.save(str(path), encoding="utf-8")


def main():
    SCRATCH.mkdir(parents=True, exist_ok=True)
    speech, reference = fixture()
    ref_path = SCRATCH / "reference.srt"
    save(reference, ref_path)
    video = SCRATCH / "reference.mkv"
    subprocess.run([str(RUNTIME / "ffmpeg/ffmpeg.exe"), "-v", "error", "-nostdin", "-y",
                    "-i", str(ref_path), "-c:s", "srt", str(video)], check=True,
                   capture_output=True, timeout=30)
    step = reference.copy()
    step[step[:, 0] > 1200000] -= 4200
    _, unrelated = fixture(seed=801)
    cases = [("unchanged", reference, "global"), ("constant", reference - 2750, "global"),
             ("step", step, "piecewise"), ("drift", reference / (25 / 23.976), None),
             ("unrelated", unrelated, "none")]
    results = []
    for name, cues, method in cases:
        source, output = SCRATCH / (name + ".srt"), SCRATCH / (name + ".out.srt")
        save(cues, source)
        completed = subprocess.run([str(RUNTIME / "NoirSubtitleSync.exe"), str(video), str(source), str(output)],
                                   capture_output=True, text=True, encoding="utf-8", timeout=180)
        (SCRATCH / (name + ".stderr.log")).write_text(completed.stderr, encoding="utf-8")
        assert completed.returncode == 0, (name, completed.returncode, completed.stderr[-1500:])
        lines = completed.stdout.splitlines()
        assert len(lines) == 1, (name, completed.stdout)
        decision = json.loads(lines[0])
        assert output.is_file(), name
        assert type(decision["accepted"]) is bool
        assert all(math.isfinite(decision[key]) for key in ("offset", "scale"))
        assert abs(decision["offset"]) <= 30 and abs(decision["scale"] - 1) <= .05
        assert len(decision["segments"]) <= 3
        if method is not None:
            assert decision["method"] == method, (name, decision)
        if name == "unrelated":
            assert not decision["accepted"], decision
            assert output.read_bytes() == source.read_bytes()
        else:
            assert decision["accepted"], (name, decision)
            aligned = np.asarray([(c.start, c.end) for c in pysubs2.load(str(output), encoding="utf-8")])
            assert len(aligned) == len(cues)
            assert np.all(np.diff(aligned[:, 0]) >= 0)
            quality = overlap(speech, aligned)
            assert np.all(quality > .97), (name, quality, decision)
            if name == "unchanged":
                np.testing.assert_array_equal(aligned, reference)
        results.append({"case": name, "exit_code": completed.returncode, "stdout_lines": len(lines), **decision})
        print(json.dumps(results[-1]))
    (SCRATCH / "contract-results.json").write_text(json.dumps(results, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()

