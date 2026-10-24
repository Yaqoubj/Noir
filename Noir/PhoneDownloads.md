# Standalone Android downloads

Open **More → Settings → Phone download settings**. Enter the HTTPS AIOStreams installation manifest and, optionally, TorBox and Real-Debrid keys. Credential inputs are masked, never prefilled, excluded from Android view-state/autofill, and protected against screenshots. The saved settings and queued source recipes (including signed URLs and request headers) use AndroidKeyStore AES-GCM encryption with fresh IVs. No dependencies were added.

The phone searches AIOStreams directly using `AIO-Noir/0.11.0` and `stream/movie/<imdb>.json` or `stream/series/<imdb>:<season>:<episode>.json`, with the stream ID URL-encoded. HTTP(S) stream URLs become direct file choices. Optional phone keys enable native torrent file discovery/preparation: TorBox checkcached/mylist/createtorrent/requestdl and Real-Debrid addMagnet/info/selectFiles/unrestrict. File names map pack files to individual episodes; samples and unsupported file types are excluded. The planner shares pack metadata across episode searches, ranks individual files using resolution/language/episode-size preferences, and exposes alternatives for each episode.

Season scopes are missing on phone, unwatched, from episode N, next N (after the furthest watched episode, across seasons), specific episode numbers/ranges, and several selected seasons. Air dates come from the cached PC snapshot or cached/fresh Cinemeta metadata. Future episodes are excluded. The review shows known/unknown sizes, queued storage reservations and phone free space before one Download action.

Discover titles are stored locally without pairing. Movies use `discover-movie-<imdb>`; a show's first record uses `discover-series-<imdb>` and other episodes use the PC's SHA-256 episode-placeholder convention. Local records survive incoming PC snapshots. Progress operations include title identity so the PC can import unknown records or merge progress into an existing matching episode. Phone file IDs remain stable when a PC record has a different canonical ID.

Transfers use the existing foreground DownloadService, with a bounded partial wake lock for screen-off operation. The queue and partial files survive process restarts; interrupted jobs become paused. Strong ETags enable Range/If-Range resume. A full response restarts the file, and a mismatched partial response discards the old partial file before retrying. Missing ETags cause a safe restart. Interrupted/expired direct links repeat the episode search, preferring the previous release name/hash; native cloud links are resolved again. Storage is checked before transfer and while writing. Completion records the local URI through `NoirApp.data.localFile`. Existing saved PC-file downloads continue to work. Offline playback opens the local file immediately.

The destination controls offer This phone and a reachable PC library, and store the last destination. Explicit “to phone” actions select the phone. Discover supports movie, individual episode and season phone downloads. Optional PC/media-server alternatives are fetched only when requested.

## PC endpoints and consent

All routes use the existing paired-token authentication and pinned TLS channel.

- `POST /v1/phone-downloads/settings`: one pending request at a time; requires a fresh explicit PC Send click and expires after two minutes. Missing callbacks, denial, cancellation, disabled sync and revoked/unpaired tokens return no settings. Authorization is checked again after approval. Only the manifest, download keys and download preferences are shared.
- `GET /v1/phone-downloads/search/{itemId}`: optional PC/cloud/media-server alternatives with opaque release/file references.
- `POST /v1/phone-downloads/prepare`: accepts itemId and optional releaseId/fileId; prepares a cloud file or existing PC file and returns url, size, fileName, itemId and expiresAt.
- `GET /v1/phone-downloads/content/{token}`: peer-bound, ten-minute descriptor proxy supporting ranges. Descriptor URLs are relative to the paired PC. Media-server authorization and provider keys stay on the PC. These optional sources need the PC reachable during transfer; standalone phone AIOStreams/debrid downloads use no PC endpoints.

PhoneSettingsShare.cs contains the PC dialog; MobileSettings.cs only wires its callback. Production certificate storage behavior is unchanged; policy-check fixtures can explicitly use ephemeral certificates.

## Files in this change

- New Android sources: PhoneDownloadSettings.java, PhoneDownloadSettingsActivity.java, PhoneDownloadProvider.java, PhoneSeasonPlan.java, PhoneDownloadsActivity.java and PhoneScreen.java.
- Android integration: DataStore.java, DownloadStore.java, DownloadService.java, MainActivity.java, TitleActions.java, DiscoverActivity.java, PcDownloadsActivity.java, DownloadHubActivity.java, AndroidManifest.xml and res/xml/network_security.xml. Cleartext file downloads are permitted for HTTP stream URLs; manifest and debrid credential requests require HTTPS.
- New PC sources: PhoneSettingsShare.cs, PhoneCloudDownloads.cs and MobilePhoneImports.cs.
- PC integration: MobileSettings.cs, MobileSyncServer.cs and MobileSyncCore.cs.
- Checks: Noir.MobileChecks/Program.cs and project source links; Noir.Checks/MobileChecks.cs and project source links. The native playback fixture in Noir.Checks/Program.cs uses software decoding to avoid a headless hardware-decoder teardown crash.
- Android checks: existing androidTest source set, PhoneDownloadTest.java and a generated black-video/silent-audio MKV fixture. No JVM unit-test source set/dependency setup existed, so no Gradle test dependencies were added.

Files reserved for the concurrent PC planner/playback/history work were not edited by this implementation.

## Verification and remaining device/service checks

Passed: Android debug/test APK build and lint (zero errors), the PC build into outputs/codex-build-phone, 293 existing PC checks, six settings-sharing policy checks, and all nine tests on a dedicated Android emulator. The emulator tests include screen-off foreground downloading and actual offline playback with no paired PC.

Validation commands:

```powershell
powershell -ExecutionPolicy Bypass -File build_android.ps1
dotnet build Noir/Noir.csproj -o outputs/codex-build-phone --no-restore
dotnet run --project Noir.Checks/Noir.Checks.csproj --no-restore -- --offline
dotnet run --project Noir.MobileChecks/Noir.MobileChecks.csproj --no-restore -- --offline --policy-only
adb -s <test-device> shell am instrument -w -e class app.noir.mobile.PhoneDownloadTest app.noir.mobile.test/app.noir.mobile.TestRunner
```

Noir.Checks uses its existing NOIR_CHECKS_PROFILE override for the read-only real-library fixture. The Windows sandbox's JDK archive-close access error required an ignored workspace Gradle init script to fork javac and disable incremental Java compilation; repository Gradle files/dependencies were not changed for that workaround.

Nine emulator tests cover parsing/ranking, URL shape, all season scopes, stable IDs, encrypted settings/queue sources, native TorBox and Real-Debrid preparation with deterministic service fixtures, Range/If-Range resume, changed-ETag restart, expired-link re-search, foreground completion with the app backgrounded and the screen off, and actual offline playback of the downloaded synthetic MKV with no paired PC.

Policy checks exercise unpaired rejection, denial, explicit approval, fresh approval for subsequent requests, revocation and a missing callback. The transport suite also contains HTTP checks for settings refusal and descriptor privacy/ranges. Its full Windows TLS loopback run could not complete under the sandbox account's Schannel credential restrictions; the direct endpoint policy checks run without network access.

Still requiring real-device/service verification: live AIOStreams/provider variations, real TorBox/RD account limits and uncached preparation, Jellyfin/Plex transfers through the PC proxy, the visible PC consent dialog end to end, OEM battery/background restrictions, long transfers and Android foreground-service time limits, and physical-device codec behavior. Internet access is required to search/download from cloud services; completed files play without internet or the PC.
