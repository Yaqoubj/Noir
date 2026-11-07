"""Noisy speech detection: strong global evidence without relaxing piecewise gates."""
import unittest
from pathlib import Path
import numpy as np

from test_alignment import fixture, decide
from alignment import (FPS_RATIOS, _f1, _no_local_regression, _no_local_regression_f1,
                       _window_f1, activity_mask, decide_alignment, overlap, transform_cues)


class NoisyGlobalTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        activity, cls.reference = fixture()
        rng = np.random.default_rng(721)
        # Missing dialogue and false detections outside dialogue cap precision at .40.
        cls.speech = rng.random(len(activity)) < np.where(activity, .40, .08)

    def assert_global(self, source, offset, scale=1.):
        decision, result = decide(self.speech, source, offset=offset, scale=scale)
        self.assertTrue(decision["accepted"], decision)
        self.assertEqual(decision["method"], "global")
        self.assertEqual(decision["offset"], offset)
        self.assertEqual(decision["scale"], scale)
        np.testing.assert_allclose(result, self.reference, atol=1)
        self.assertTrue(np.all(result[:, 0] >= 0))

    def test_noisy_fps_ratios_and_offset(self):
        for scale in FPS_RATIOS[1:]:
            with self.subTest(scale=scale):
                self.assert_global((self.reference - 15000) / scale, 15., scale)

    def test_noisy_fps_ratios_without_offset(self):
        for scale in FPS_RATIOS[1:]:
            with self.subTest(scale=scale):
                self.assert_global(self.reference / scale, 0., scale)

    def test_noisy_plain_three_second_offset(self):
        self.assert_global(self.reference - 3000, 3.)
        self.assert_global(self.reference + 3000, -3.)

    def test_offset_with_near_unit_scale(self):
        self.assert_global((self.reference - 3000) / .9975, 3., .9975)

    def test_wrong_seven_second_shift_is_rejected(self):
        decision, result = decide(self.speech, self.reference, offset=7.)
        self.assertFalse(decision["accepted"], decision)
        np.testing.assert_array_equal(result, self.reference)

    def test_marginal_f1_gain_is_rejected(self):
        source = self.reference - 3000
        before = activity_mask(source, len(self.speech))
        after = activity_mask(self.reference, len(self.speech))
        probability = np.where(before & after, .40, np.where(after, .248, np.where(before, .22, .03)))
        speech = np.random.default_rng(90).random(len(before)) < probability
        gain = _f1(overlap(speech, self.reference)) - _f1(overlap(speech, source))
        self.assertAlmostEqual(gain, .03, delta=.005)
        decision, result = decide(speech, source, offset=3.)
        self.assertFalse(decision["accepted"], decision)
        np.testing.assert_array_equal(result, source)

    def test_noisy_timing_matches_is_unchanged(self):
        decision, result = decide(self.speech, self.reference)
        self.assertFalse(decision["accepted"], decision)
        np.testing.assert_array_equal(result, self.reference)

    def test_one_genuinely_worse_window_rejects_global(self):
        source = self.reference - 3000
        source[self.reference[:, 0] < 240000] += 3000
        candidate = transform_cues(source, offset=3.)
        scores = _window_f1(self.speech, source, candidate)
        self.assertGreater(_f1(overlap(self.speech, candidate)) - _f1(overlap(self.speech, source)), .08)
        self.assertGreaterEqual(np.mean(scores[:, 1] > scores[:, 0]), .75)
        self.assertLess(scores[0, 1] - scores[0, 0], -.02)
        self.assertFalse(_no_local_regression_f1(self.speech, source, candidate))
        decision, result = decide(self.speech, source, offset=3.)
        self.assertFalse(decision["accepted"], decision)
        np.testing.assert_array_equal(result, source)

    def test_fewer_than_three_quarters_improved_windows_is_rejected(self):
        source = self.reference - 3000
        before = activity_mask(source, len(self.speech))
        after = activity_mask(self.reference, len(self.speech))
        probability = np.where(after, .4, .08)
        early = np.arange(len(before)) < 4 * 24000
        probability[early] = np.where(before[early], .41, np.where(after[early], .4, .08))
        speech = np.random.default_rng(813).random(len(before)) < probability
        scores = _window_f1(speech, source, self.reference)
        self.assertTrue(_no_local_regression_f1(speech, source, self.reference))
        self.assertLess(np.mean(scores[:, 1] > scores[:, 0]), .75)
        self.assertGreater(_f1(overlap(speech, self.reference)) - _f1(overlap(speech, source)), .08)
        decision, result = decide(speech, source, offset=3.)
        self.assertFalse(decision["accepted"], decision)
        np.testing.assert_array_equal(result, source)

    def test_f1_window_tolerance_keeps_small_tradeoffs(self):
        speech = np.arange(24000) < 6000
        before = np.array([[0, 120000]])
        small_loss, large_loss = np.array([[0, 125000]]), np.array([[0, 130000]])
        self.assertFalse(_no_local_regression(speech, before, small_loss))
        self.assertTrue(_no_local_regression_f1(speech, before, small_loss))
        self.assertFalse(_no_local_regression_f1(speech, before, large_loss))

    def test_strong_path_does_not_relax_special_reference_thresholds(self):
        source = self.reference - 3000
        for kwargs in ({"embedded": True}, {"audio_check": True}):
            with self.subTest(kwargs=kwargs):
                decision, _ = decide(self.speech, source, offset=3., **kwargs)
                self.assertFalse(decision["accepted"], decision)

    def test_nonstandard_scale_rejects_even_large_gain(self):
        scale = 1.01
        source = (self.reference - 3000) / scale
        decision, _ = decide(self.speech, source, offset=3., scale=scale)
        self.assertFalse(decision["accepted"], decision)

    def test_negative_cues_cannot_be_accepted_or_hidden_by_clipping(self):
        source = self.reference.copy()
        source[0] = (100, 200)
        for candidate in (None, np.maximum(transform_cues(source, offset=-3.), 0)):
            decision, result = decide(self.speech, source, offset=-3., global_cues=candidate)
            self.assertFalse(decision["accepted"], decision)
            np.testing.assert_array_equal(result, source)

    def test_strong_path_still_requires_ffsubsync_quality(self):
        source = self.reference - 3000
        for kwargs in ({"score": 99, "global_success": True},
                       {"score": 5000, "global_success": False},
                       {"score": 5000, "global_success": True, "offset": 31.}):
            decision, result = decide_alignment(self.speech, source, **kwargs)
            self.assertFalse(decision["accepted"], decision)
            np.testing.assert_array_equal(result, source)

    def test_celebration_derived_arrays(self):
        with np.load(Path(__file__).parent / "fixtures/celebration.npz") as data:
            speech, source, candidate = data["speech"], data["original"], data["global_cues"]
            offset, scale, score = (float(data[key]) for key in ("offset", "scale", "score"))
        before, after = overlap(speech, source), overlap(speech, candidate)
        self.assertAlmostEqual(before[0], .312, delta=.001)
        self.assertAlmostEqual(after[0], .407, delta=.001)
        self.assertFalse(_no_local_regression(speech, source, candidate))
        self.assertTrue(_no_local_regression_f1(speech, source, candidate))
        decision, result = decide_alignment(speech, source, offset, scale, score, True,
                                             global_cues=candidate)
        self.assertEqual(decision["method"], "global", decision)
        np.testing.assert_array_equal(result, candidate)


if __name__ == "__main__":
    unittest.main()
