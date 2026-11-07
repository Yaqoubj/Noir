"""Synthetic array tests: no ffmpeg, network, or video required."""
import sys
import unittest
from pathlib import Path
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from alignment import (activity_mask, decide_alignment, local_offsets, overlap,
                       plausible_candidate, transform_cues)


def fixture(minutes=40, seed=19):
    rng = np.random.default_rng(seed)
    cues = []
    at = 18.
    while at < minutes * 60 - 18:
        # Independent durations and gaps avoid periodic false peaks.
        at += rng.uniform(.6, 5.)
        duration = rng.uniform(.7, 3.5)
        # Keep a gap around potential cut points, in both subtitle versions.
        if any(abs(at - cut) < 12 for cut in (600, 1200, 1800)):
            at += 24
        cues.append((round(at * 1000), round((at + duration) * 1000)))
        at += duration
    cues = np.asarray(cues, dtype=float)
    return activity_mask(cues, minutes * 6000), cues


def decide(speech, cues, **kwargs):
    return decide_alignment(speech, cues, score=5000, global_success=True, **kwargs)


class AlignmentTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.speech, cls.reference = fixture()

    def test_already_synchronized_is_unchanged(self):
        original = self.reference.copy()
        decision, result = decide(self.speech, original, embedded=True)
        self.assertTrue(decision["accepted"])
        self.assertEqual(decision["method"], "global")
        self.assertAlmostEqual(decision["offset"], 0)
        np.testing.assert_array_equal(result, original)
        np.testing.assert_array_equal(original, self.reference)

    def test_audio_check_accepts_a_clear_shift_with_weak_speech_detection(self):
        # Speech detection finds only ~60% of dialogue, as on real soundtracks.
        speech = self.speech & (np.random.default_rng(7).random(len(self.speech)) < .6)
        source = self.reference - 3000
        # Ordinary audio now has a separate strong-evidence global path.
        normal, _ = decide(speech, source, offset=3.)
        self.assertTrue(normal["accepted"], normal)
        decision, result = decide(speech, source, offset=3., audio_check=True)
        self.assertTrue(decision["accepted"], decision)
        np.testing.assert_array_equal(result, self.reference)

    def test_audio_check_never_moves_a_matching_track(self):
        speech = self.speech & (np.random.default_rng(7).random(len(self.speech)) < .6)
        # A spurious 2 s proposal for an already aligned track must leave it untouched.
        decision, result = decide(speech, self.reference, offset=2., audio_check=True)
        self.assertFalse(decision["accepted"], decision)
        np.testing.assert_array_equal(result, self.reference)

    def test_constant_offset(self):
        for offset in (2.75, -3.2):
            with self.subTest(offset=offset):
                source = self.reference - offset * 1000
                decision, result = decide(self.speech, source, offset=offset, embedded=True)
                self.assertTrue(decision["accepted"])
                self.assertEqual(decision["method"], "global")
                np.testing.assert_array_equal(result, self.reference)

    def test_local_search_recovers_a_missed_constant_offset(self):
        source = self.reference - 2750
        decision, result = decide(self.speech, source, embedded=True)
        self.assertEqual(decision["method"], "piecewise", decision)
        self.assertAlmostEqual(decision["offset"], 2.75, delta=.01)
        np.testing.assert_allclose(result, self.reference, atol=10)

    def test_step_after_twenty_minutes(self):
        source = self.reference.copy()
        source[source[:, 0] > 1200000] -= 4200
        decision, result = decide(self.speech, source, embedded=True)
        self.assertTrue(decision["accepted"], decision)
        self.assertEqual(decision["method"], "piecewise")
        self.assertEqual(len(decision["segments"]), 2)
        self.assertAlmostEqual(decision["offset"], 0, delta=.02)
        self.assertAlmostEqual(decision["segments"][1]["offset_s"], 4.2, delta=.02)
        np.testing.assert_allclose(result, self.reference, atol=10)
        self.assertTrue(np.all(result[:-1, 1] <= result[1:, 0]))

    def test_negative_step(self):
        source = self.reference.copy()
        source[source[:, 0] > 1200000] += 4200
        decision, result = decide(self.speech, source, offset=-4.2, embedded=True)
        self.assertEqual(decision["method"], "piecewise", decision)
        np.testing.assert_allclose(result, self.reference, atol=10)

    def test_three_segments(self):
        source = self.reference.copy()
        source[(self.reference[:, 0] > 600000) & (self.reference[:, 0] < 1800000)] -= 4200
        source[self.reference[:, 0] > 1800000] -= 8000
        decision, result = decide(self.speech, source, offset=4.2, embedded=True)
        self.assertEqual(decision["method"], "piecewise", decision)
        self.assertEqual(len(decision["segments"]), 3)
        np.testing.assert_allclose(result, self.reference, atol=10)

    def test_25_23976_drift_already_fixed_by_global(self):
        scale = 25 / 23.976
        source = self.reference / scale
        decision, result = decide(self.speech, source, scale=scale, embedded=True)
        self.assertTrue(decision["accepted"], decision)
        self.assertEqual(decision["method"], "global")
        np.testing.assert_allclose(result, self.reference, atol=1)

    def test_25_23976_drift_missed_by_global(self):
        for scale in (25 / 23.976, 23.976 / 25):
            with self.subTest(scale=scale):
                source = self.reference / scale
                decision, result = decide(self.speech, source, embedded=True)
                self.assertEqual(decision["method"], "piecewise", decision)
                self.assertAlmostEqual(decision["scale"], scale, delta=.00005)
                np.testing.assert_allclose(result, self.reference, atol=30)

    def test_small_steady_drift(self):
        source = self.reference / 1.0015
        decision, result = decide(self.speech, source, embedded=True)
        self.assertEqual(decision["method"], "piecewise", decision)
        self.assertAlmostEqual(decision["scale"], 1.0015, delta=.0001)
        self.assertGreater(overlap(self.speech, result)[0], .96)

    def test_noise_and_silence(self):
        rng = np.random.default_rng(3)
        for speech in (np.zeros_like(self.speech), np.ones_like(self.speech),
                       rng.random(len(self.speech)) < .45):
            with self.subTest(density=speech.mean()):
                decision, _ = decide(speech, self.reference)
                self.assertFalse(decision["accepted"], decision)

    def test_candidate_without_clear_gain_is_rejected(self):
        almost_correct = self.reference + 20
        self.assertFalse(plausible_candidate(self.speech, almost_correct,
                                             almost_correct, self.reference,
                                             [0, len(self.reference)], embedded=True))

    def test_improving_only_precision_is_rejected(self):
        # Shorter cues can improve precision while leaving recall below baseline.
        baseline = self.reference.copy()
        baseline[:, 0] -= 1000
        candidate = self.reference.copy()
        candidate[:, 1] = candidate[:, 0] + 200
        self.assertFalse(plausible_candidate(self.speech, baseline, baseline, candidate,
                                             [0, len(baseline)]))

    def test_good_input_is_not_degraded_by_small_global_shift(self):
        decision, result = decide(self.speech, self.reference, offset=.2, embedded=True)
        np.testing.assert_array_equal(result, self.reference)
        if decision["accepted"]:
            self.assertAlmostEqual(decision["offset"], 0)

    def test_repair_cannot_sacrifice_an_already_good_window(self):
        source = self.reference.copy()
        source[source[:, 0] > 240000] -= 4200
        candidate = self.reference.copy()
        candidate[candidate[:, 0] < 240000] += 200
        # The whole-file scores improve substantially, but the first region loses.
        self.assertTrue(np.all(overlap(self.speech, candidate) - overlap(self.speech, source) > .05))
        self.assertFalse(plausible_candidate(self.speech, source, source, candidate,
                                             [0, len(source)], embedded=True))

    def test_cut_inside_a_window(self):
        source = self.reference.copy()
        cut = 1038000
        # Create a release-cut gap so the correction cannot cross neighbours.
        affected = source[:, 0] > cut
        source[affected] += 4200
        decision, result = decide(self.speech, source, embedded=True)
        self.assertEqual(decision["method"], "piecewise", decision)
        np.testing.assert_allclose(result, self.reference, atol=10)

    def test_audio_style_reference(self):
        # Jitter both edges and add brief non-dialogue activity; no text matching.
        rng = np.random.default_rng(51)
        audio_cues = self.reference + rng.integers(-100, 101, self.reference.shape)
        speech = activity_mask(audio_cues, len(self.speech))
        speech[::193] = True
        source = self.reference.copy()
        source[source[:, 0] > 1200000] -= 4200
        decision, result = decide(speech, source)
        self.assertEqual(decision["method"], "piecewise", decision)
        self.assertTrue(np.all(overlap(speech, result) - overlap(speech, source) >= .05))

    def test_failed_quality_gate_cannot_be_rescued(self):
        source = self.reference.copy()
        source[source[:, 0] > 1200000] -= 4200
        for kwargs in ({"score": 99, "global_success": True},
                       {"score": 1000, "global_success": False},
                       {"score": 1000, "global_success": True, "offset": 31},
                       {"score": 1000, "global_success": True, "scale": 1.06}):
            decision, _ = decide_alignment(self.speech, source, **kwargs)
            self.assertFalse(decision["accepted"])

    def test_candidate_crossing_neighbours_is_rejected(self):
        candidate = self.reference.copy()
        cut = len(candidate) // 2
        candidate[cut:, :] -= 20000
        self.assertFalse(plausible_candidate(self.speech, self.reference, self.reference,
                                             candidate, [0, cut, len(candidate)]))

    def test_activity_outside_reference_counts_against_precision(self):
        cues = np.array([[-1000, 1000], [2000, 4000]])
        p, r = overlap(np.ones(300, dtype=bool), cues)
        self.assertAlmostEqual(p, .5)
        self.assertAlmostEqual(r, 2 / 3)

    def test_sparse_windows_and_empty_cues(self):
        decision, _ = decide(self.speech, [])
        self.assertFalse(decision["accepted"])
        self.assertEqual(local_offsets(np.zeros(1000), self.reference[:1]).shape, (0, 2))


if __name__ == "__main__":
    unittest.main()

