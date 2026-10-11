# Noir for Android 0.4.1

The player renders embedded subtitles on a separate VLC surface. Open **Subtitles** to download English or Arabic, choose a saved file, or adjust its timing. Downloads are matched to the exact video when possible and stay on the phone for offline use. Smart sync uses Windows Noir 0.12.2 to compare against the same video's dialogue; it checks the video fingerprint first and keeps the original subtitle when timing is uncertain. Manual timing and your language choice persist after closing the player.

Discover searches movies and TV shows, caches posters and episode schedules, and sends episode or season download requests to your paired PC. **More → Downloads** opens the live PC queue and your phone's offline downloads. Browse Discover without a PC connection; saving My List or requesting a PC download requires Windows Noir to be open and reachable.

A native Java Android companion to Windows Noir 0.12.2, using VideoLAN LibVLC for playback. Android 8+ is required.

## Install and pair

1. Transfer `outputs/Android/Noir-Android.apk` to your phone and install it. Android may ask to allow installation from the app you use to open the APK. This is a signed personal app, not a Play Store release.
2. Keep Noir open on the PC and connect both devices to your home Wi-Fi.
3. Windows Noir → Settings → Phone Sync → Pair a phone. Scan the QR code using your camera and open it with Noir, or paste its complete pairing link into Android Noir.
4. If Windows blocks the connection, use its **Allow phone access through Windows** button and approve the Windows administrator prompt.

The ARM64 package is about 26 MB. `Noir-Android-All.apk` includes older ARM and x86 devices. No video upload or cloud account is needed. The PC must be awake and Noir open to stream and sync. The phone retains the cached library and can play completed downloads or linked local copies offline. Home Wi-Fi sync is implemented; remote/cloud sync is not.

## Included

Black/gold phone interface, Continue Watching play icons, poster library and permanent history, full known episode counts, precise two-way resume, watched diary, My List, private rating/notes, offline changes queue, periodic Android background synchronization, PC streaming with range seeking, local phone-copy linking, VLC audio/subtitle selection, fit/crop, known intro skipping, watched-at-credits, cancelable end-of-file next-episode countdown, embedded community websites with persistent Android sessions.

## Build

Reviews opens the original community websites. The chosen Reddit discussion is saved on this device. Login cookies persist and the browser is disposed when closed. Compact/embed mode was removed. No review page loads while browsing the library.

Use JDK 17 or later, Gradle 8.11.1 and Android SDK 36. From the repository root, run `gradle -p Noir.Android assembleDebug`. Set `ANDROID_HOME` to your SDK directory; the APK appears in `Noir.Android/app/build/outputs/apk/debug/`. Android Gradle Plugin 8.10.1 and target SDK 35 are pinned. LibVLC Android 3.7.7 is fetched unchanged from Maven Central.

The release signing key and password are kept in `.signing/`, excluded from source exports. Keep that directory private and preserve it for future update compatibility. Android never receives the Windows browser profile or provider credentials.

## Validation

Core synchronization regression tests: `Noir.Checks`. TLS pairing/revocation, private projection, artwork and byte-range transport checks: `Noir.MobileChecks` (uses an isolated copy, provider sync disabled). Android integration tests pair with that fixture and verify durable pairing, cached deleted posters, exact resume, offline queue, rating sync, real VLC decoding/seeking/subtitle tracks, certificate rejection, visible decoded picture, player controls, and returning to the library. No physical phone is connected during this build; physical-device installation and playback remain to be tried.

Upstream documentation: https://developer.android.com/build/releases/agp-8-10-0-release-notes , https://developer.android.com/tools/sdkmanager , https://code.videolan.org/videolan/vlc-android


## PC download commands and subtitles

A paired phone can request an episode, movie or missing-episode season plan on the PC. Compare releases, select exact files, confirm uncertain episode numbers and manage the PC queue. AIOStreams discovers torrent releases; TorBox alone supplies media downloads. The PC holds both credentials. Sources have independent result/error states, and plans exclude local, queued and future episodes. Keep PC Noir open and reachable.

PC download preferences include quality, size limit, English/Arabic subtitle downloads and local subtitle alignment. Saved sidecars are shared with the phone, and playback follows the synced English/Arabic/Off preference. Availability depends on the release or OpenSubtitles. Weak timing matches retain their original subtitles.
