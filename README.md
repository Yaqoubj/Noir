# Noir

A local movie and TV library for Windows, with a native Android companion. Black and gold browsing, precise resume positions, permanent viewing history, and VLC playback.

Windows: **0.8.0** · Android: **0.2.0** · Personal prototype; no hosted service.

## Features

- Resume watching and next-episode shelves, recently added titles, saved playback queues.
- Poster collection, viewing diary, monthly recap, ratings, notes, named collections and viewing status.
- Watch history and cached artwork remain when media files are removed.
- Season and episode information, spoiler controls, manual match corrections, previewed folder organization with undo.
- Embedded LibVLC playback, audio/subtitle selection, Fit/Fill, intro markers, watched-at-credits, cancelable autoplay and sleep timer.
- Android pairing, two-way progress/history synchronization, direct PC streaming and resumable offline downloads.
- Community websites in an embedded browser, including IMDb, Letterboxd, Serializd, Trakt and Reddit. Website sign-ins stay on their device.

**Reddit comments:** Noir currently opens Reddit's original website. A separate native comments reader is proposed, but has not been implemented or approved for Reddit Data API access. No Reddit scraping service or API credentials are included.

## Build Windows

Requires Windows x64 and the .NET 10 SDK. Restore downloads the dependencies declared in the project.

```powershell
dotnet build Noir/Noir.csproj -c Release
dotnet run --project Noir/Noir.csproj -c Release
dotnet run --project Noir.Checks/Noir.Checks.csproj -c Release
dotnet publish Noir/Noir.csproj -c Release -r win-x64 --self-contained true -o outputs/Windows
```

WebView2 Runtime is required for embedded community websites. The app's source is in `Noir/`; Windows regression checks are in `Noir.Checks/`.

## Build Android

Requires JDK 17, Android SDK with platform 36, and Gradle 8.11.1. Android Gradle Plugin 8.10.1 is declared in the source. Configure your SDK using `ANDROID_HOME` or an untracked `Noir.Android/local.properties`.

```powershell
cd Noir.Android
gradle --no-daemon assembleDebug lintDebug
```

The debug APK is generated under `app/build/outputs/apk/debug/`. A fresh checkout does not require the maintainer's release key. `assembleRelease` produces an unsigned release when no local signing configuration exists; use your own signing key for distribution. Never commit a signing key or password.

Android requires Android 8 or newer. Pair through Windows **Settings → Pair a phone**. The PC must be open and reachable for streaming, downloads and synchronization. Completed downloads play offline. There is no cloud relay or video upload. Networks that isolate devices may prevent connection.

`Noir.MobileChecks/` and Android instrumentation tests are integration fixtures intended for an isolated test environment, with a populated local test library. They are not required to run Noir.

## Data and privacy

Windows stores the library, artwork and browser sessions in the user's `.noir` directory. That directory, personal media, test outputs, credentials, signing keys, and generated packages are not part of this repository. Android stores pairing credentials in its private application data. Pairing uses HTTPS with a pinned PC certificate.

Metadata needs an internet connection when fetching new information; cached information remains available offline. Original media stays on your devices. Community providers apply their own terms and session policies.

## License

Noir's original source is MIT-licensed. Dependencies and third-party content retain their own licenses and rights. See `Noir/THIRD-PARTY-NOTICES.txt` and the included LGPL text for VideoLAN components. The MIT license does not license metadata, posters, reviews, or other third-party content.
