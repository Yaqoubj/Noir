"""Regenerate deterministic, compact vectors: build-python tests/make_sampled_vectors.py."""
import base64
import json
from pathlib import Path
import numpy as np
from test_sampled import reference, slice_samples
from alignment import activity_mask
from sampled import align_samples, speech_masks


def rle(array):
    array = np.asarray(array)
    if not len(array):
        return []
    bounds = np.r_[0, np.flatnonzero(array[1:] != array[:-1]) + 1, len(array)]
    return [[int(array[a]), int(b - a)] for a, b in zip(bounds, bounds[1:])]


def main():
    speech, cues = reference(1200)
    samples = slice_samples(speech)
    cases = [('accepted-offset', samples, cues - 7500),
             ('accepted-fps-offset', samples, (cues - 3000) / (25 / 23.976)),
             ('already-aligned', samples, cues)]
    wrong = [(a, np.roll(s, i * 713 + 321)) for i, (a, s) in enumerate(samples)]
    cases.append(('rejected-no-agreement', wrong, cues - 7500))
    speech, cues = reference(7200)
    samples = slice_samples(speech)
    start, heard = samples[-1]
    samples[-1] = (start, activity_mask(cues - start * 1000 + 8000, len(heard)))
    cases.append(('rejected-conflicting-sample', samples, cues - 7500))
    vectors = dict(version=1, rate=100, pcm_rate=16000, alignment=[], vad=[])
    for name, samples, source in cases:
        decision, fitted = align_samples(samples, source)
        vectors['alignment'].append(dict(name=name, cues=source.tolist(),
            samples=[dict(start=a, speech=rle(s)) for a, s in samples],
            expected={k: decision[k] for k in ('accepted', 'offset', 'scale', 'method', 'reason')},
            fitted_edges=fitted[[0, -1]].tolist()))
    t = np.arange(8000) / 16000
    for name, frequency, amplitude in [('silence', 1000, 0), ('band-burst', 1000, 8000),
                                        ('low-burst', 100, 8000)]:
        pcm = (amplitude * np.sin(2 * np.pi * frequency * t) * ((t > .15) & (t < .35))).astype('<i2')
        vectors['vad'].append(dict(name=name, pcm_s16le=base64.b64encode(pcm.tobytes()).decode('ascii'),
                                   speech=rle(speech_masks([pcm.tobytes()])[0])))
    path = Path(__file__).parent / 'fixtures/sampled_vectors.json'
    path.write_text(json.dumps(vectors, separators=(',', ':'), allow_nan=False) + '\n', encoding='utf-8')
    for case in vectors['alignment']:
        print(case['name'], case['expected'])


if __name__ == '__main__':
    main()
