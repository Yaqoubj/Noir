# Derived Celebration activity fixture

`celebration.npz` contains only the boolean 100 Hz audio speech mask, original
English cue time pairs, ffsubsync's proposed cue time pairs, and the proposed
offset, scale and score. It contains no subtitle text or media.

Derived from the manager's offline diagnostic for The Celebration (1998),
Danish audio, 25 fps English sidecar versus 23.976 fps video. Source diagnostic:
`scratchpad/celebration/diag.py`, `en.diag.speech.npy`, `en.diag.srt` in the
manager's supplied temporary workspace. Offset +20.58 s, scale 25/23.976,
score 119779 (rounded down from the logged 119779.183).

Tests load these arrays directly; no media files, ffmpeg, network or subtitle
parser are required. Whole-file precision/recall change from approximately
.312/.535 to .407/.727. This is also a regression case where precision and
recall trade off locally while F1 improves.
