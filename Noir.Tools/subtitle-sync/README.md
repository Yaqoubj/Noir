# Local subtitle alignment

The worker is local-only. Its command remains
`NoirSubtitleSync.exe <video> <subtitle> <output>`. A normal accepted **or
rejected** run exits 0, creates the output, and emits exactly one JSON line.
Unexpected errors retain exit code 1. Third-party stdout is redirected to stderr.
A rejected run leaves an exact copy of the input at the output path.

`alignment.py` contains the pure numpy functions. Inputs are a one-dimensional
100 Hz reference activity mask and cue pairs in milliseconds. The wrapper retains
the existing embedded-text-reference preference and otherwise uses ffsubsync's
audio pipeline. No dependency or network access was added.

`NoirSubtitleSync.exe <video> <subtitle> <output> --audio` always uses the
soundtrack as the reference (ffsubsync `--reference-stream 0:a:0`). Noir uses it
to check a subtitle track built into the video, which would otherwise be
compared with itself.

## Decision and safeguards

1. Run the existing ffsubsync global offset/frame-rate fit. Retain its success,
   score >=100, absolute offset <=30 s and scale deviation <=0.05 gates.
   Silence, <10 s of speech, and speech density outside (0.03, 0.85) are rejected.
2. Retain global precision/recall minima: 0.80/0.50 for embedded text,
   0.65/0.35 for audio. A meaningful global change (>1 s or >0.003 scale
   deviation) still needs >=0.05 absolute precision improvement over the input.
   With `--audio` the minima are 0.55/0.50 and a meaningful change needs >=0.10
   precision improvement: speech detection on real soundtracks caps precision
   lower, so the floor drops only with stronger recall and a larger gain.
   Additionally, neither precision nor recall may regress against the input,
   overall or in any eligible local window.
3. Verify non-overlapping 240 s windows, requiring >=8 fully contained cues,
   >=10 s speech and the same density limits. Exclude 15 s of edge padding
   when choosing cues, then search +/-15 s every 10 ms using reference prefix
   sums. A local peak needs the reference-specific precision minimum, precision
   >=0.20 above speech density, and >=0.03 advantage over peaks >1 s away.
   Peaks within 0.10 s of the search boundary are discarded.
4. Consistent, well-covered windows preserve a good global result. Otherwise
   propose at most three segments. Each constant-offset plateau requires >=2
   independent windows, <=0.45 s maximum scatter, and >=0.80 s difference from
   the next plateau. Offsets within 0.10 s of zero snap to zero to avoid moving
   a healthy region because of VAD edge jitter. Choose an existing cue gap
   between the windows witnessing each change, maximizing activity overlap.
   Reject boundaries that would create overlaps or cross neighbours.
5. Also fit a steady affine trend when >=4 reliable windows span >=720 s and
   maximum fit residual is <=0.35 s. Seed searches with the global scale, 1,
   and both directions of 25/23.976, 24/23.976 and 25/24. This covers missed
   frame-rate drift that would exceed the local +/-15 s range under scale 1.
   A trend may use one segment; all segments share the reported scale.
6. A proposal must improve **both overall precision AND recall by >=0.05
   absolute** over the actual global output. Five percentage points excludes
   sampling jitter and weak optimizations. It must also preserve both input
   metrics overall. Every segment needs >=16 cues, >=240 s reference duration,
   enough speech, the same precision/recall minima, and no regression against
   either the original or global result. Neither metric may regress in any
   eligible fixed local window against either baseline. Comparisons allow only
   floating-point epsilon (1e-9), not a percentage-point degradation.
7. Every segment's offset must remain within +/-30 s and the shared scale within
   +/-5%. Reject negative/non-finite/inverted times, reordered neighbours, or
   boundary overlaps; never clip or reorder cues to make a candidate fit.
   Precision counts the entire union of cue duration, including activity outside
   the reference, so moving cues off the timeline cannot inflate the score.
   If a proposal fails, retain the eligible global result; otherwise reject.

These gates deliberately prefer falling back over an uncertain repair. A sparse
or short segment, ambiguous repeating dialogue, combined cut and irregular drift,
or a reference that poorly represents dialogue may remain unrepaired. Activity
masks cannot establish semantic correctness or guarantee perfect real-world
timing; the safeguards measure non-regression against the available reference.

## JSON

`accepted`, `offset` and `scale` retain their types and app limits.
Additional fields are `method` (`global`, `piecewise`, `none`) and
`segments` (each has `start_s`, `offset_s`).

For piecewise results, `offset` is the first segment's offset. `scale` is shared;
each cue uses `output_ms = round(input_ms * scale + offset_s * 1000)`.
Segment starts identify the first affected cue's **original** start time in
seconds, with the first segment starting at 0. Rejected results have no segments.
An accepted global result reports one segment starting at 0.

## Verification

From the repository root (PowerShell 5.1):

```powershell
& work/subtitle-build/Scripts/python.exe -m unittest discover -s Noir.Tools/subtitle-sync/tests -v
& work/subtitle-build/Scripts/python.exe Noir.Tools/subtitle-sync/tests/smoke_frozen.py
```

The unit suite needs only numpy and the standard library. It tests synchronized
input, positive/negative constant offsets, positive/negative and multiple cuts,
a cut inside a window, 25/23.976 drift in both directions (including a missed
global fit), small steady drift, audio activity jitter, silence/noise, insufficient
gains, precision-only gains, local regression, neighbour crossing, quality gates,
sparse data and out-of-reference cue activity.

The optional smoke script uses the existing runtime's ffmpeg to mux an embedded
text track into Matroska, then runs the actual frozen executable for synchronized,
constant-offset, step, drift and unrelated-subtitle fixtures. It checks one JSON
line, exit 0, app limits, output existence, output quality and rejection copying.
It writes fixtures and results to `work/subtitle-smoke/`; no downloads occur.

Offline rebuild used the existing venv and an existing local FFmpeg package:

```powershell
$env:PIP_NO_INDEX='1'
$env:PIP_DISABLE_PIP_VERSION_CHECK='1'
$env:PYINSTALLER_CONFIG_DIR=Join-Path (Get-Location) 'work/subtitle-pyinstaller/cache'
$env:PATH=(Join-Path (Get-Location) 'work/release-v0.12.2-windows/Tools/subtitle-sync/ffmpeg')+';'+$env:PATH
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build_subtitle_tools.ps1
```

All requirements were already installed. The build script now writes its generated
spec to `work/subtitle-pyinstaller` alongside the other build intermediates.
The final binary is `work/subtitle-runtime/NoirSubtitleSync.exe`.

