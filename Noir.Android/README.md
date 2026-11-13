# Noir for Android 0.11.0

**Current packages:** `Noir-Android-0.11.0-Standalone.apk` (phone, ARM64), `Noir-Android-0.11.0-Standalone-All.apk` (phone, also older ARM and x86 devices) and `Noir-Android-0.11.0-TV.apk` (Google TV / Android TV). Release files are published as `Noir-Android-0.11.0.apk`, `Noir-Android-0.11.0-All.apk` and `Noir-TV-0.11.0.apk`. Settings and More show **Standalone · 2026-10-25** so you can identify this build.

**Use the phone on its own.** Discover, My List, collections, watch history, ratings, imports, backups, streaming, subtitles and phone downloads do not require pairing. Add your AIOStreams connection in phone download settings to play online. Continue Watching uses phone-discovered episodes, and playback preferences are saved on the phone. PC sync and PC-file playback are optional. PC-managed Trakt/Serializd sync, Kodi import through the PC, and automatic subtitle timing analysis still need the relevant PC connection.
**What's new in 0.11.0:** The phone has its own built-in torrent engine, so downloads need no debrid account. It picks healthy releases, shows peers and speed, switches release if one stalls and picks up interrupted downloads by itself. You can download this episode, this season, the whole show or the next few episodes, with quality and size limits. Automatic downloading and removal follow rules you choose, with overrides per show. Subtitles come from more sources and show where each came from, with faster syncing that also fixes frame-rate mismatches. The phone can send its own Trakt and Serializd watch marks, and can mark what you finish as watched in Stremio (off by default). Open in Harbor is on every title. Big libraries start, import and restore faster. Direct AIOStreams playback is available from Discover's movie and released-episode pages as well as library actions. The player looks for English or Arabic subtitles when the preferred language is missing from the stream; download either from Subtitles without downloading the video or connecting to the PC. Saved streaming subtitles are kept per release, and Off and manual timing persist. Tap **Stream** in the player to choose another release if playback fails. There is a drawn icon set and a full Arabic pass on phone and TV, and on TV dialogs follow the language direction.

**Premium pass on the phone:** Home has a status strip under the hero (downloads, the next episode, free space, streaming setup while it is missing, Edit Home) and shows your own rows first: Continue watching, play queue, My List, Keep ready, Recently added, Coming up. Edit Home has a tile size (Small, Medium, Large). Downloads has three tabs, Queue, On this phone and Keep ready, each with its live number; the PC's queue and the download settings are two links under them. History shows your totals under the heading (tap for Stats) and one Import & export chip. A Discover title page has one gold Stream button, quiet save and download buttons, season chips and compact episode rows with a stream and a download icon; a download asks "this phone or your PC" only when a PC is paired. The download sheet uses one name, "Keep this show ready".

**Earlier, in 0.5.0:** a redesigned app. Home opens on a full-width hero and browses in sideways rows. One Continue Watching row holds resumes, next episodes, shows waiting for their next air date (with the date) and released episodes that are not saved locally: stream or download them using the phone connection. New episodes are tagged. History opens with **Latest** (your recent episodes and movies with resume, play next, get next, rate and comments on each card), then **Stats** (watch time, streaks, viewing on this phone, time of day, top shows, genres and milestones), Collection and Diary. Title pages have a hero with the main action, a row of action chips, season chips and compact episode rows with thumbnails and progress; tap an episode to play it or ⋯ for options. Coming up uses artwork calendar cards. Episode options open in a rounded artwork sheet with grouped icon tiles; More has its own screen for downloads, sync and settings. The bottom bar has icons, and the launch screen is dark. Works with Windows Noir 0.12.2 or later; 0.13.0 is recommended.

The player renders embedded subtitles on a separate VLC surface. Open **Subtitles** to download English or Arabic, choose a saved file, or adjust its timing. Downloads are matched to the exact video when possible and stay on the phone for offline use. Smart sync uses Windows Noir 0.12.2 to compare against the same video's dialogue; it checks the video fingerprint first and keeps the original subtitle when timing is uncertain. Manual timing and your language choice persist after closing the player.

Discover searches movies and TV shows and caches posters and schedules. Save My List and library titles locally, stream, or download movies, episodes and seasons to this phone. When paired, PC download actions also appear; only those PC actions need Windows Noir to be reachable.

A native Java Android app with optional Windows Noir synchronization, using VideoLAN LibVLC for playback. Android 8+ is required.

## Install and get started

1. Transfer `outputs/Android/Noir-Android-0.11.0-Standalone.apk` to your phone and install it. Android may ask to allow installation from the app you use to open the APK. This is a signed personal app, not a Play Store release.
2. Use Discover and Track what I watch immediately. For streaming, save your AIOStreams link in phone download settings. No pairing is required.
3. Optional PC sync: Windows Noir → Settings → Phone Sync → Pair a phone. Scan the QR code using your camera and open it with Noir, or paste its complete pairing link into Android Noir.
4. If you chose PC sync and Windows blocks the connection, use its **Allow phone access through Windows** button and approve the Windows administrator prompt.

`Noir-Android-0.11.0-Standalone.apk` is the ARM64 phone package; `Noir-Android-0.11.0-Standalone-All.apk` also includes older ARM and x86 devices. The PC must be awake and Noir open for PC-file streaming and phone sync. Direct AIOStreams playback and phone downloads use the phone's own connection and settings, so the PC can be off. The phone retains the cached library and can play completed downloads or linked local copies offline. Home Wi-Fi sync is implemented; remote/cloud sync is not.

## Included

Black/gold phone interface, Continue Watching play icons, poster library and permanent history, full known episode counts, precise two-way resume, watched diary, My List, private rating/notes, offline changes queue, periodic Android background synchronization, PC streaming with range seeking, local phone-copy linking, VLC audio/subtitle selection, fit/crop, known intro skipping, watched-at-credits, cancelable end-of-file next-episode countdown, embedded community websites with persistent Android sessions.

## Build

Reviews opens the original community websites. The chosen Reddit discussion is saved on this device. Login cookies persist and the browser is disposed when closed. Compact/embed mode was removed. No review page loads while browsing the library.

Use JDK 17 or later, Gradle 8.11.1 and Android SDK 36. From the repository root, run `gradle -p Noir.Android assembleDebug`. Set `ANDROID_HOME` to your SDK directory; the APK appears in `Noir.Android/app/build/outputs/apk/debug/`. Android Gradle Plugin 8.10.1 and target SDK 35 are pinned. LibVLC Android 3.7.7 is fetched unchanged from Maven Central.

The release signing key and password are kept in `.signing/`, excluded from source exports. Keep that directory private and preserve it for future update compatibility. Android does not receive the Windows browser profile. Phone download credentials are encrypted on the phone; you can enter them there or explicitly transfer settings through the paired connection.

## Validation

Core synchronization regression tests: `Noir.Checks`. TLS pairing/revocation, private projection, artwork and byte-range transport checks: `Noir.MobileChecks` (uses an isolated copy, provider sync disabled). Android integration tests pair with that fixture and verify durable pairing, cached deleted posters, exact resume, offline queue, rating sync, real VLC decoding/seeking/subtitle tracks, certificate rejection, visible decoded picture, player controls, and returning to the library. No physical phone is connected during this build; physical-device installation and playback remain to be tried.

Upstream documentation: https://developer.android.com/build/releases/agp-8-10-0-release-notes , https://developer.android.com/tools/sdkmanager , https://code.videolan.org/videolan/vlc-android


## PC download commands and subtitles

A paired phone can request an episode, movie or missing-episode season plan on the PC. Compare releases, select exact files, confirm uncertain episode numbers and manage the PC queue. AIOStreams discovers torrent releases; TorBox alone supplies media downloads. The PC holds both credentials. Sources have independent result/error states, and plans exclude local, queued and future episodes. Keep PC Noir open and reachable.

PC download preferences include quality, size limit, English/Arabic subtitle downloads and local subtitle alignment. Saved sidecars are shared with the phone, and playback follows the synced English/Arabic/Off preference. Availability depends on the release or OpenSubtitles. Weak timing matches retain their original subtitles.
