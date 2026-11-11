# Reproducible player media

Run from the repository root (Python 3, ffmpeg and ffprobe on PATH):

```
python tools/test-media/generate_media.py work/test-media/media
python tools/test-media/check_media.py work/test-media/media
python tools/test-media/range_server.py work/test-media/media
```

`generate_media.py` copies the former TV speed recipe: 600-second 1080p24
`i-avc-aac-srt.mkv` (AVC/AAC/English SRT), `ii-hevc10-eac3-ass.mkv`
(10-bit HEVC/EAC3/ASS), and `iii-avc-aac-tail.mp4` (AVC/AAC, moov after mdat).
It also produces short AC3/DTS probes, 8-second source segments, subtitle sources,
a probe manifest, a generation log and a SHA256 of the recipe.

The phone files are `Bright pattern fixture.mp4` (480 seconds of changing,
bright AVC test pattern and AAC) and `Subtitle surface fixture.mkv` (240 seconds,
English and Arabic embedded SRT). Both subtitle tracks are visible at 4 seconds.
The 18 English reference cues start at 5+6n and end at 8+6n seconds; SubtitleTest's
7+6n / 10+6n input must be aligned by -2 seconds. The video extends more than
100 seconds beyond the final cue. The initial visible cue is extra.

MobileChecks requires ffmpeg on every startup, regenerates a cache in
`NoirValidation/android-sync/subtitle-fixture` when its recipe hash changes or
required files disappear, copies the two phone videos into the run's private
fixture library, and verifies both titles were scanned. It never silently skips.

`range_server.py <media-dir> [--port 18765]` serves only that directory through
range-capable `/lan/<file>` and rate-limited `/debrid/<file>` URLs. Logs go to
`<media-dir>/../runtime/`. `work/test-tv.ps1` validates its prerequisites, generates
missing media in ignored `work/test-media/media`, uses adb reverse for HTTP, and
pushes the AVC/SRT file into the app's external files directory. The tracks/arrows
test links a separate local item; the cold HTTP test reports its bounded outcome
through instrumentation status. Use `-AvdHome <folder>` to select the directory
containing `Noir_TV.ini`; the runner does not provision an AVD.

`check_contract.py` checks the source wiring without an emulator. `check_media.py`
checks codecs, duration, embedded cues, subtitle languages and the tail MP4 atom
order. The manager runs instrumented playback, picture, gestures and TV focus
checks; source/media checks cannot prove those runtime behaviours.

Generated files belong only under the repository's ignored `work/` or
`NoirValidation/` trees. The recipe refuses tracked output locations.
