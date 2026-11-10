"""Array-only sampled regression tests and language-neutral shared vectors."""
import base64
import json
from pathlib import Path
import sys
import unittest
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from alignment import activity_mask, transform_cues
from sampled import plan_windows, plan_extra_windows, align_samples, speech_masks

FIXTURES = Path(__file__).parent / 'fixtures'


def reference(duration=2400):
    """Integer recurrence, no randomness, small enough to port verbatim."""
    cues, at, state = [], 20., 19
    while at < duration - 20:
        state = (1664525 * state + 1013904223) % 4294967296
        at += .6 + (state % 440) / 100
        state = (1664525 * state + 1013904223) % 4294967296
        length = .7 + (state % 280) / 100
        cues.append([round(at * 1000), round((at + length) * 1000)])
        at += length
    cues = np.asarray(cues, dtype=float)
    return activity_mask(cues, duration * 100), cues


def slice_samples(speech, windows=None):
    windows = plan_windows(len(speech) / 100) if windows is None else windows
    # Anchors reflect actual raster positions, not unrounded plan coordinates.
    return [(round(a * 100) / 100, speech[round(a * 100):round((a + n) * 100)].copy())
            for a, n in windows]


def unrle(runs, dtype=bool):
    return np.repeat(np.array([v for v, n in runs], dtype=dtype), [n for v, n in runs])


class SampledTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.speech, cls.cues = reference()
        cls.samples = slice_samples(cls.speech)

    def unchanged(self, samples, cues, reason=None):
        before = np.asarray(cues).copy()
        decision, fitted = align_samples(samples, cues)
        self.assertFalse(decision['accepted'], decision)
        self.assertEqual(decision['offset'], 0.)
        self.assertEqual(decision['scale'], 1.)
        self.assertEqual(decision['method'], 'none')
        np.testing.assert_array_equal(fitted, before)
        np.testing.assert_array_equal(cues, before)
        self.assertFalse(np.shares_memory(fitted, cues))
        json.dumps(decision, allow_nan=False)
        if reason:
            self.assertEqual(decision['reason'], reason, decision)
        return decision

    def test_plan(self):
        for duration, count, length in [(7200, 8, 108), (2700, 6, 60), (1200, 6, 60), (600, 0, 0)]:
            windows = plan_windows(duration)
            self.assertEqual(len(windows), count)
            margin = max(90, .05 * duration)
            for a, n in windows:
                self.assertEqual(n, length)
                self.assertGreaterEqual(a, margin)
                self.assertLessEqual(a + n, duration - margin + 1e-9)
            self.assertTrue(all(a + n <= b for (a, n), (b, _) in zip(windows, windows[1:])))
            self.assertLessEqual(sum(n for a, n in windows), max(.15 * duration, 360))
        for duration in (float('nan'), float('inf'), -1, 719):
            self.assertEqual(plan_windows(duration), [])

    def test_known_affine(self):
        for offset, scale in [(7.5, 1.), (-12., 1.), (0., 25 / 23.976),
                              (3., 25 / 23.976), (0., 23.976 / 25), (0., 24 / 23.976)]:
            with self.subTest(offset=offset, scale=scale):
                source = (self.cues - offset * 1000) / scale
                before = source.copy()
                decision, fitted = align_samples(self.samples, source)
                self.assertTrue(decision['accepted'], decision)
                np.testing.assert_allclose(fitted, self.cues, atol=100)
                np.testing.assert_array_equal(source, before)
                json.dumps(decision, allow_nan=False)

    def test_already_aligned_and_noisy_correct_cues(self):
        self.unchanged(self.samples, self.cues, 'already-aligned')
        # Deterministic dropped detections and false positives, no RNG.
        indices = np.arange(len(self.speech), dtype=np.int64)
        pseudo = ((indices * 1103515245 + 12345) % 2147483648) / 2147483648
        speech = pseudo < np.where(self.speech, .40, .08)
        self.unchanged(slice_samples(speech), self.cues)

    def test_different_cuts(self):
        speech, cues = reference(7200)
        samples = slice_samples(speech)
        for fraction in (.5, .8):
            source = cues.copy()
            source[source[:, 0] > fraction * 7200000] += 20000
            self.unchanged(samples, source)

    def test_conflicting_strong_sample(self):
        speech, cues = reference(7200)
        source = cues - 7500
        samples = slice_samples(speech)
        start, heard = samples[-1]
        samples[-1] = (start, activity_mask(cues - start * 1000 + 8000, len(heard)))
        self.unchanged(samples, source, 'conflicting-sample')

    def test_few_samples_and_silence(self):
        self.unchanged(self.samples[:3], self.cues - 7500)
        self.unchanged([(a, np.zeros_like(s)) for a, s in self.samples], self.cues, 'too-little-speech')

    def test_unheard_audio_and_mutation(self):
        samples = [(a, s.copy()) for a, s in self.samples]
        decision, fitted = align_samples(samples, self.cues - 7500)
        alternate = ~self.speech.copy()
        for a, s in samples:
            alternate[round(a * 100):round(a * 100) + len(s)] = s
        other, result = align_samples(slice_samples(alternate), self.cues - 7500)
        self.assertEqual(decision, other)
        np.testing.assert_array_equal(fitted, result)
        for (_, a), (_, b) in zip(samples, self.samples):
            np.testing.assert_array_equal(a, b)

    def test_repeating_dialogue(self):
        speech = np.tile(np.arange(500) < 200, 480)
        cues = np.array([[i * 5000 + 500, i * 5000 + 2500] for i in range(480)])
        decision, fitted = align_samples(slice_samples(speech), cues)
        self.assertFalse(decision['accepted'], decision)
        np.testing.assert_array_equal(fitted, cues)

    def test_marginal_gain(self):
        source = self.cues - 3000
        before = activity_mask(source, len(self.speech))
        prob = np.where(before & self.speech, .40,
                        np.where(self.speech, .248, np.where(before, .22, .03)))
        indices = np.arange(len(before), dtype=np.int64)
        pseudo = ((indices * 1664525 + 1013904223) % 4294967296) / 4294967296
        speech = pseudo < prob
        self.unchanged(slice_samples(speech), source)

    def test_celebration_existing_reference(self):
        with np.load(FIXTURES / 'celebration.npz') as data:
            self.unchanged(slice_samples(data['speech']), data['global_cues'])

    def test_celebration_planned_acceptance_requirement(self):
        # Mandatory acceptance assertion: do not skip or weaken this regression.
        with np.load(FIXTURES / 'celebration.npz') as data:
            decision, fitted = align_samples(slice_samples(data['speech']), data['original'])
            self.assertTrue(decision['accepted'], decision)
            self.assertAlmostEqual(decision['offset'], 20.58, delta=.35)
            np.testing.assert_allclose(fitted[[0, len(fitted) // 2, -1]],
                                       data['global_cues'][[0, len(fitted) // 2, -1]], atol=350)

    def test_invalid_models(self):
        for source in (self.cues / 1.01, self.cues - 40000):
            self.unchanged(self.samples, source)
        for source in (np.array([[np.nan, 1]]), np.array([[2, 1]])):
            self.unchanged(self.samples, source, 'invalid-model')

    def test_nonplanned_anchors(self):
        samples = slice_samples(self.speech, [(120 + i * 400, 90) for i in range(6)])
        decision, fitted = align_samples(samples, self.cues - 7500, duration_s=9999)
        self.assertTrue(decision['accepted'], decision)
        np.testing.assert_allclose(fitted, self.cues, atol=50)

    def test_insufficient_coverage_and_unstable_extrapolation(self):
        speech, cues = reference(7200)
        samples = slice_samples(speech, [(1200 + i * 400, 108) for i in range(8)])
        self.unchanged(samples, cues - 7500, 'poor-coverage')
        speech, cues = reference(1200)
        samples = []
        for i, (start, heard) in enumerate(slice_samples(speech)):
            offset = 7.5 + .0015 * (start + len(heard) / 200) + [-.3, -.3, -.3, .15, .15, .15][i]
            local = activity_mask(transform_cues(cues, offset=offset) - start * 1000, len(heard))
            samples.append((start, local))
        self.unchanged(samples, cues, 'unstable')

    def test_overlapping_heard_intervals_cannot_multiply_evidence(self):
        samples = [(a, s.copy()) for a, s in self.samples]
        samples.append(samples[0])
        self.unchanged(samples, self.cues - 7500, 'invalid-model')

    def test_vectors(self):
        vectors = json.loads((FIXTURES / 'sampled_vectors.json').read_text())
        for case in vectors['alignment']:
            with self.subTest(case=case['name']):
                samples = [(s['start'], unrle(s['speech'])) for s in case['samples']]
                decision, fitted = align_samples(samples, case['cues'])
                for key, value in case['expected'].items():
                    if isinstance(value, float):
                        self.assertAlmostEqual(decision[key], value, places=8)
                    else:
                        self.assertEqual(decision[key], value)
                np.testing.assert_allclose(fitted[[0, -1]], case['fitted_edges'], atol=.01)
        for case in vectors['vad']:
            pcm = np.frombuffer(base64.b64decode(case['pcm_s16le']), dtype='<i2').copy()
            np.testing.assert_array_equal(speech_masks([pcm.tobytes()])[0], unrle(case['speech']))


class SpeechMaskTests(unittest.TestCase):
    def test_adaptation_is_stable_and_deterministic(self):
        noise = ((np.arange(16000 * 30, dtype=np.int64) * 7919) % 201 - 100).astype('<i2')
        tone = (6000 * np.sin(2 * np.pi * 440 * np.arange(16000 * 2) / 16000)).astype('<i2')
        pcm = np.r_[noise[:16000 * 10], tone, noise[:16000 * 10], tone].tobytes()
        first, second = speech_masks([pcm]), speech_masks([pcm])
        np.testing.assert_array_equal(first[0], second[0])
        self.assertEqual(len(first[0]), len(pcm) // 320)

    def test_extra_windows_never_overlap_the_planned_ones(self):
        for duration in (720, 900, 1200, 1800, 2700, 3700, 7200):
            windows = sorted(plan_windows(duration) + plan_extra_windows(duration))
            self.assertTrue(all(a + n <= b + 1e-9 for (a, n), (b, _) in zip(windows, windows[1:])), duration)
            self.assertTrue(all(n >= 20 for _, n in plan_extra_windows(duration)), duration)

    def test_extra_windows_are_between_planned_ones(self):
        planned, extra = plan_windows(7200), plan_extra_windows(7200)
        self.assertEqual(len(extra), len(planned) - 1)
        for (a, n), (b, _), (c, m) in zip(planned, planned[1:], extra):
            self.assertLess(a, c)
            self.assertLess(c, b)
            self.assertEqual(n, m)


if __name__ == '__main__':
    unittest.main()
