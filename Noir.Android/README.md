# Noir for Android 0.2.0

A native Java Android companion to Windows Noir 0.8.0, using VideoLAN LibVLC for playback. Android 8+ is required.

## Install and pair

1. Transfer `outputs/Android/Noir-Android.apk` to your phone and install it. Android may ask to allow installation from the app you use to open the APK. This is a signed personal app, not a Play Store release.
2. Keep Noir open on the PC and connect both devices to your home Wi-Fi.
3. Windows Noir → Settings → Pair a phone. Scan the QR code using your camera and open it with Noir, or paste its complete pairing link into Android Noir.
4. If Windows blocks the connection, use its **Allow phone access through Windows** button and approve the Windows administrator prompt.

The ARM64 package is about 26 MB. `Noir-Android-All.apk` includes older ARM and x86 devices. No video upload or cloud account is needed. The PC must be awake and Noir open to stream and sync. The phone retains the cached library and can play completed downloads or linked local copies offline. Home Wi-Fi sync is implemented; remote/cloud sync is not.

## Included

Black/gold phone interface, Continue Watching play icons, poster library and permanent history, full known episode counts, precise two-way resume, watched diary, My List, private rating/notes, offline changes queue, periodic Android background synchronization, PC streaming with range seeking, local phone-copy linking, VLC audio/subtitle selection, fit/crop, known intro skipping, watched-at-credits, cancelable end-of-file next-episode countdown, embedded community websites with persistent Android sessions.

## Build

Reviews opens the original community websites. The chosen Reddit discussion is saved on this device. Login cookies persist and the browser is disposed when closed. Compact/embed mode was removed. No review page loads while browsing the library.

See the repository root README for building with JDK 17, Gradle 8.11.1 and Android SDK 36. Android Gradle Plugin 8.10.1 and target SDK 35 are pinned. LibVLC Android 3.7.7 is fetched unchanged from Maven Central.

The release signing key and password are kept in `.signing/`, excluded from source exports. Keep that directory private and preserve it for future update compatibility. Android never receives the Windows browser profile or provider credentials.

## Validation

Core synchronization regression tests: `Noir.Checks`. TLS pairing/revocation, private projection, artwork and byte-range transport checks: `Noir.MobileChecks` (uses an isolated copy, provider sync disabled). Android integration tests pair with that fixture and verify durable pairing, cached deleted posters, exact resume, offline queue, rating sync, real VLC decoding/seeking/subtitle tracks, certificate rejection, visible decoded picture, player controls, and returning to the library. No physical phone is connected during this build; physical-device installation and playback remain to be tried.

Upstream documentation: https://developer.android.com/build/releases/agp-8-10-0-release-notes , https://developer.android.com/tools/sdkmanager , https://code.videolan.org/videolan/vlc-android

