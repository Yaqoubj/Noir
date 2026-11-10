"""--plan and --sampled modes of subtitle_sync.py, in process, with synthetic PCM (loud = speech)."""
import contextlib
import io
import json
import shutil
import subprocess
import sys
import unittest
from pathlib import Path

import numpy as np
import pysubs2

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
sys.path.insert(0, str(Path(__file__).parent))
import sampled
import subtitle_sync
from test_sampled import reference, slice_samples

ROOT = Path(__file__).resolve().parents[3]
SCRATCH = ROOT / "work/subtitle-round/a23/worker-tests"


def energy_masks(chunks, passes=2):
    """Stand-in for WebRTC VAD on synthetic audio: a 10 ms frame is speech when it is loud."""
    return [np.array([np.abs(np.frombuffer(pcm[i:i + 320], dtype="<i2")).max() > 1000
                      for i in range(0, len(pcm) - 319, 320)], dtype=bool) for pcm in chunks]


def write_pcm(mask, path):
    frames = np.where(mask[:, None], 8000, 0).astype("<i2") * np.ones((1, 160), dtype="<i2")
    path.write_bytes(frames.tobytes())


def write_srt(cues, path, encoding="utf-8"):
    subs = pysubs2.SSAFile()
    for i, (a, b) in enumerate(cues):
        subs.append(pysubs2.SSAEvent(start=int(a), end=int(b), text="Line %d \u0645\u0631\u062d\u0628\u0627" % i))
    subs.save(str(path), encoding=encoding)


class WorkerTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if SCRATCH.exists():
            shutil.rmtree(SCRATCH)
        SCRATCH.mkdir(parents=True)
        cls.speech, cls.cues = reference()
        cls.samples = slice_samples(cls.speech)
        cls.pcm = []
        for i, (start, mask) in enumerate(cls.samples):
            path = SCRATCH / ("s%d.pcm" % i)
            write_pcm(mask, path)
            cls.pcm.append({"start": start, "pcm": str(path)})
        cls.job = SCRATCH / "job.json"
        cls.job.write_text(json.dumps({"samples": cls.pcm, "duration": 2400}), encoding="utf-8")
        cls.patch = (sampled.speech_masks,)
        sampled.speech_masks = energy_masks

    @classmethod
    def tearDownClass(cls):
        sampled.speech_masks = cls.patch[0]
        shutil.rmtree(SCRATCH, ignore_errors=True)

    def run_sampled(self, cues, name):
        source, output = SCRATCH / (name + ".srt"), SCRATCH / (name + ".out.srt")
        write_srt(cues, source)
        return source, output, subtitle_sync.sampled_sync(str(self.job), str(source), str(output))

    def test_plan_matches_sampled_module(self):
        for duration in (7200, 2700, 1200):
            plan = subtitle_sync.plan(duration)
            self.assertEqual(plan["windows"], [list(w) for w in sampled.plan_windows(duration)])
            self.assertEqual(plan["extra"], [list(w) for w in sampled.plan_extra_windows(duration)])
            self.assertEqual(len(plan["extra"]), len(plan["windows"]) - 1)
        self.assertEqual(subtitle_sync.plan(7200)["windows"].__len__(), 8)
        self.assertEqual(subtitle_sync.plan(600), {"windows": [], "extra": []})
        json.dumps(subtitle_sync.plan(7200), allow_nan=False)

    def test_accepts_offset_and_keeps_text(self):
        source, output, result = self.run_sampled(self.cues - 7500, "accept")
        self.assertTrue(result["accepted"], result)
        self.assertAlmostEqual(result["offset"], 7.5, delta=.15)
        self.assertEqual(result["method"], "sampled")
        self.assertEqual(result["reason"], "applied")
        self.assertEqual(set(result), {"accepted", "offset", "scale", "method", "reason"})
        fixed, original = pysubs2.load(str(output), encoding="utf-8"), pysubs2.load(str(source), encoding="utf-8")
        self.assertEqual([c.text for c in fixed], [c.text for c in original])
        np.testing.assert_allclose([(c.start, c.end) for c in fixed], self.cues, atol=150)
        self.assertNotEqual(source.read_bytes(), output.read_bytes())

    def test_rejection_copies_input(self):
        source, output, result = self.run_sampled(self.cues, "aligned")
        self.assertFalse(result["accepted"])
        self.assertEqual((result["offset"], result["scale"], result["method"]), (0, 1, "none"))
        self.assertEqual(result["reason"], "already-aligned")
        self.assertEqual(source.read_bytes(), output.read_bytes())

    def test_silence_and_real_detector(self):
        sampled.speech_masks = self.patch[0]
        try:
            silent = []
            for i, (start, mask) in enumerate(self.samples):
                path = SCRATCH / ("q%d.pcm" % i)
                path.write_bytes(bytes(len(mask) * 320))
                silent.append({"start": start, "pcm": str(path)})
            job = SCRATCH / "silent.json"
            job.write_text(json.dumps({"samples": silent, "duration": 2400}), encoding="utf-8")
            source, output = SCRATCH / "silent.srt", SCRATCH / "silent.out.srt"
            write_srt(self.cues - 7500, source)
            result = subtitle_sync.sampled_sync(str(job), str(source), str(output))
            self.assertFalse(result["accepted"])
            self.assertEqual(result["reason"], "too-little-speech")
            self.assertEqual(source.read_bytes(), output.read_bytes())
        finally:
            sampled.speech_masks = energy_masks

    def test_cli_contract(self):
        script = Path(subtitle_sync.__file__)
        done = subprocess.run([sys.executable, str(script), "--plan", "7200"], capture_output=True, text=True, timeout=120)
        self.assertEqual(done.returncode, 0, done.stderr)
        self.assertEqual(len(done.stdout.splitlines()), 1)
        self.assertEqual(len(json.loads(done.stdout)["windows"]), 8)
        missing = subprocess.run([sys.executable, str(script), "--sampled", str(SCRATCH / "nope.json"),
                                  str(SCRATCH / "nope.srt"), str(SCRATCH / "nope.out.srt")], capture_output=True, text=True, timeout=120)
        self.assertEqual(missing.returncode, 1)
        line = json.loads(missing.stdout)
        self.assertFalse(line["accepted"])
        self.assertNotIn("nope", missing.stdout)
        bad = subprocess.run([sys.executable, str(script), "--plan", "abc"], capture_output=True, text=True, timeout=120)
        self.assertEqual(bad.returncode, 1)
        self.assertFalse(json.loads(bad.stdout)["accepted"])


if __name__ == "__main__":
    unittest.main()
