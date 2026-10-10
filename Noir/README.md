# Noir 0.12.1 / Android 0.4.0

**Discover** searches new movies and TV shows, including anime, using Cinemeta without a separate API key. Open a poster for its synopsis, year, rating and full known episode list. Save it to My List, choose a released episode, or find season downloads through your existing AIOStreams/TorBox connection. Review files before confirming. Search results, schedules and downloaded artwork remain cached for offline browsing; browsing and saving never create watched history. Initial result grids load thirty cards at a time.

Discover sits below My List. Downloads sits beside Scan Library. Android includes Discover and **More → Downloads**; new-title requests are verified on the paired PC before release selection.

**Downloads** is a permanent sidebar tab with live progress, file sizes, subtitle status, pause, cancel and resume. Its badge shows active jobs, including requests from the phone. Progress updates preserve the list and scroll position.

Windows uses a dark native title bar, correctly oriented scrollbars, smoother mouse-wheel scrolling, responsive search and episode rows, and a sidebar that scrolls on short windows. Initial windows and dialogs fit the current monitor’s work area. Leaving fullscreen restores the previous size and position.


AIOStreams and TorBox downloads are configured in Settings. Credentials are encrypted for your Windows account and stay on the PC. Noir searches AIOStreams and your TorBox library independently and shows a separate result or error for each. AIOStreams discovers torrent references; TorBox verifies and downloads their files. AIO episode results can contain season packs. “Plan missing episodes” proposes known released episodes absent from the PC or queue, combining packs and gaps where possible. Review selected files, sizes and destinations before confirming; unknown numbering requires a manual episode match. Existing files and history are retained. URL-only and Usenet results are unsupported. Provider configuration errors remain visible even if account files are available. Noir does not change your AIOStreams addons.

Choose preferred quality, an optional per-episode size limit and English/Arabic subtitle downloads in Settings. Subtitles come from the selected release or OpenSubtitles when available and are saved beside the video. Automatic alignment uses the local video's embedded text subtitles or speech timing, with confidence checks and permanent original backups. Uncertain matches retain their original timing. In the player’s Subtitles menu, “Download English / Arabic subtitles” works for existing local videos; “Auto-sync saved subtitles” retries alignment. This helper runs only when needed, without uploading audio. Paired phones receive both language sidecars and use the synced playback-language preference.

Android refreshes the PC's bookmark before playback, updates episode detail pages after sync, and avoids overwriting newer progress when an old paused session closes. Matching SRT/ASS/SSA/VTT sidecars are copied into private phone storage and added to VLC alongside embedded subtitles. The selected English or Arabic full subtitles are preferred over signs-only or forced tracks.
History now has Collection and Diary tabs, a month selector and viewing recap, progress cards, and ratings. Posters and watch dates remain after files are removed. Home separates Resume watching from Next episode and includes Recently added, a saved play queue, and announced upcoming episodes when TVmaze supplies dates.

Use a title’s menu to set Watching / Planned / Finished / Dropped, save it to a named collection, or correct a title/year/IMDb/season match. Match corrections preserve file names and viewing identity. Collections, status and corrections sync with Android; play queues and downloaded copies stay on their device.

Android: open an episode’s options for Watch now, Watch & save offline, or Save offline. Manage transfers in Settings → Downloads. Transfers run one at a time with a notification, retain their partial file, and can be paused/resumed. A size check, range validation, file-change check and storage check guard against incomplete copies. The PC must be reachable to stream or download; completed phone copies play offline. Android may stop lengthy transfers; use Resume to continue.

Review windows remember their last tab and sign-ins. Compact/embed mode has been removed. Reddit opens the selected discussion inside Noir with Top sorting; Use Reddit link saves an exact episode/movie thread. This is the normal website, not a separate scraped comments feed. Reddit-approved data access is needed for that reader.

# Noir

A Windows movie and TV library with a black interface and embedded VLC playback.

## Android companion (Windows 0.12.1 / Android 0.4.0)

Review windows open the original community websites. Noir remembers the chosen Reddit discussion per episode/movie on that device and disposes the browser on close. Compact/embed mode was removed in 0.8.0. These websites require an internet connection.

Windows 0.7.5 moved the shared profile to `C:\Users\<you>\.noir` so Desktop and Codex launches use the same library, and merges duplicate catalog groups while preserving newer progress. History has poster cards with watched counts. Settings → Organize filenames & folders previews verified titles, standardizes show season folders and movie folders, moves matching external subtitles, and keeps a durable journal for undo and interrupted moves. It never overwrites destination files or deletes extras. Archive changes retains the previous journal before another organization.

Install `Noir-Android.apk` on an Android 8+ phone (ARM64). The `Noir-Android-All.apk` package also includes older ARM and x86 devices. In Windows Noir, open **Settings → Phone Sync → Pair a phone** while both devices are on the same home Wi-Fi. Scan the QR code with the phone camera and choose Noir, or paste the complete pairing link into Android Noir's pairing screen. The link expires after ten minutes and is single-use. Pairing persists after closing either app. If the PC's address changes, pair again with the same PC; saved phone history stays intact.

Noir syncs exact resume positions, individual viewing dates, ratings, private notes and My List. Android caches the full known episode list, descriptions and artwork, including watched titles whose PC files are deleted. Newer playback positions win over older offline positions, while earlier completed viewings still join the diary. Operations have durable receipts and viewing IDs to prevent duplicate completions. Phone completions also enter the PC's existing Trakt/Serializd queue if those integrations are enabled.

Windows preserves its last successful sync time across restarts and displays it in Settings. Closing an older paused PC playback session cannot overwrite newer phone progress. New video files are detected automatically while browsing; rescans wait while an episode is open. The first VLC initialization runs in the background so opening an episode does not block the interface. Closing Noir still quits it; the phone queues changes while the PC is unavailable.

Windows Noir must remain open and the PC awake for sync and streaming. Android syncs on opening, every 15 seconds while open, and periodically in the background when Android's scheduler permits it (not guaranteed at an exact interval). Offline playback saves changes and retries when the PC is reachable. This version syncs with one PC over local networks; internet/cloud sync is not implemented. Videos stream directly from the PC without upload or transcoding. For offline viewing, choose **Link a video on my phone** in an episode/movie's options and select its local copy. Android uses VLC for playback, supports embedded audio/subtitle tracks, known intro skipping, watched-at-credits and an optional countdown to the next available episode. It does not yet include all Windows features such as preview thumbnails, manual marker editing or a sleep timer.

Connections use HTTPS with the PC certificate fingerprint embedded in the pairing link and pinned by Android. The PC stores only hashes of phone tokens, protects its certificate key with Windows user encryption, serves videos by catalog IDs, and excludes folder paths, Windows browser sessions and third-party account settings from the mobile projection. Android's private data directory stores the paired token; app backups are disabled. Pairing another PC over an existing phone library is refused to protect its saved history. **Disconnect all paired phones** revokes their access immediately.

New pairings use HTTPS TCP port 443. Port 49271 remains available for previously paired phones. To switch an existing phone to 443, pair it again with the same PC; its saved history stays intact. This can help networks that restrict ports, but does not overcome networks that block communication between devices.

If Windows blocks a phone connection, **Settings → Allow phone access through Windows** launches a small administrator-approved firewall setup for Noir's TCP ports 443 and 49271 on local network addresses. It does not change your Wi-Fi network classification or expose a port through your router. The Windows approval is required by the operating system. No cloud account or media upload is needed.

Android reviews open the original websites in an embedded browser with its own persistent login. Windows website sessions are not copied to the phone. Exact episode links already matched on the PC are reused; unresolved providers open a search or episode list.

## Using the app

For TV shows, use `TV/Show Name (Year)/Season 01/Show Name S01E01.mkv`. The year is optional but helps distinguish remakes. A season folder also supplies the season number for numbered episode files. Refresh artwork and information in Settings scans new files first, queues behind an existing update, refreshes cached art, and reports its progress and result. A title that cannot be matched safely remains unchanged.

Launch **Noir** from the Desktop or Start menu. Add your media folders on first launch. **Settings** lets you add or remove folders and scan new files. Removing a folder from the library only changes the library configuration; your media files stay on disk.

**Continue Watching** remembers the last episode you watched and its saved playback time. The app saves every two seconds, when pausing, when returning to the library, and when closing. It stores time in milliseconds. Playback resumes automatically when you open an unfinished video. A completed episode leads to the next unwatched episode available in the same show. Regular seasons and specials have separate playback sequences.

An episode is marked watched when it reaches a detected or manually saved credits marker. For unlabelled files, a conservative fallback uses the final 5% (capped at 90 seconds for episodes and three minutes for movies). Videos shorter than five minutes require the actual end. Turn **Mark watched at credits** off in Settings to require the file's end instead. Credits keep playing; auto-next starts an eight-second countdown only after the video ends. You can cancel it or disable automatic advancement in Settings.

**Intro skipping** uses imported markers and embedded chapters named OP/Opening or ED/Ending/Credits. Choose **Intro & credits** in the player to save opening start/end times and a credits start time for one episode or a whole season. A manual season range can be inaccurate when timings vary. Automatic intro skipping happens once per playback; manual seeking into the opening is allowed. Repeated forward/back clicks accumulate correctly while VLC is seeking, and seeking back after the file ends restarts playback at that position.

Noir does not fingerprint unlabelled intros automatically. Files without chapter labels or saved markers need manual times.

**History** retains watched episodes, unfinished times, dates, and details even when you remove a library root or delete its media files. Re-adding an episode under another folder recovers its bookmark by title, season, and episode; a small file fingerprint also reconnects renamed files. Ambiguous titles or completely different files cannot always be matched. Video-folder posters are copied into Noir's own artwork directory. Existing path bookmarks migrate without being deleted.

History groups episodes under show cards, with watched/in-progress/unavailable filters, movie/show filters and sorting. Click a show for its season selector and individual episodes. **My List** saves titles you want to watch; the title context menu can hide a title from Continue Watching without deleting its history. Unfinished episode descriptions stay hidden until you choose to show them, or turn off **Hide spoilers** in Settings.

As of **0.6.1**, Home, Movies and Shows browse the permanent saved catalog, including deleted files. Home includes Recently watched posters, and clicking History resets its filters to show all saved viewing history. Show posters and season pages display total known episodes, specials, the number on this PC and the number watched separately. Cached TVmaze episodes remain in the season list even if they have never been downloaded; playback is disabled for unavailable files, while their information, comments and watched controls remain accessible. If no online episode list has been fetched, the total is the episodes Noir has previously seen. Deleting media or removing a root does not reduce that saved count.

**Reviews & comments** opens IMDb, Letterboxd (movies), Serializd (TV), Trakt, Reddit and Rotten Tomatoes in Noir's own browser window, with source tabs and back/reload controls. Tabs wrap to fit narrower windows. Click an episode name for its details, then Reviews & comments, or use the Reviews button on its row. Trakt opens the exact episode or movie using its verified IMDb ID. Serializd matches the show by title and year, resolves its exact season and episode, and caches that link. If matching is uncertain, select the correct episode in its tab; verified selections are remembered. If a provider requests login and returns home, click its tab again afterward. Your WebView2 browser profile is retained across restarts and updates; websites can expire their sessions or request verification. Reddit searches discussion posts by movie title or show title plus episode-number formats; specials search for OVA/special discussions. Verify the episode and spoiler labels on the result you select. Searches do not guarantee a thread exists or has been matched correctly. These websites stay inside the review window, with Open in browser available if a provider requires verification or does not work embedded.

IMDb episode IDs are matched by series, season and episode using its public non-commercial dataset; the optional index is downloaded once (about 55 MB currently) and cached for 30 days. Known matches persist with the library. Unmatched episodes open the series season list; unknown series open a title search. Rotten Tomatoes opens a title search. Live reviews require internet, and providers may require login or human verification for some content. WebView2 starts when reviews or pending watched sync need it; it is installed on this PC. Noir does not post comments or upload media. **Connected watch history** in Settings can mark completed episodes on Serializd and completed episodes/movies on Trakt through the accounts signed in under Reviews & comments. Reaching credits, finishing a file, or manually marking watched queues one persistent job per item/provider. Sync runs automatically in the background; opening review websites is not necessary. Offline work survives restarts, and retries run every five minutes while Noir is open or after its next launch. The hidden browser closes when its work is done. Retry sync is an optional manual shortcut. Success requires the website to remain watched after reloading, not just a click. Already-watched items are left untouched, and marking unwatched in Noir never removes website history. Sync earlier watched items explicitly queues prior Noir history. Remote dates use the time of synchronization; Noir keeps its original local timestamp. Failed matches, login expiry, website verification, or changed controls stay pending and show a message in Settings. Serializd refuses a combined special when its duration does not match the listed short. Trakt specials require a confirmed provider match and stay pending for manual handling; normal season episodes sync automatically. Website controls can change; this browser adapter is experimental and does not use a registered API/OAuth integration.

**Export library backup** in Settings saves history, cached artwork, metadata, markers, preferences and My List to a ZIP. It excludes video files and browser cookies. Close Noir and extract the backup into `%LOCALAPPDATA%\Noir` to restore it. Removing video folders preserves history; deleting Noir's own data folder does not.

**Artwork and information** are fetched automatically when online and cached on this PC. TVmaze supplies show descriptions, episode names and summaries, genres and images; Wikipedia supplies movie descriptions and posters when a clear film match is available. Cached pictures and details remain available offline, including for removed media. Uncertain matches keep their existing details. Settings lets you turn online lookups off or refresh manually. No account, personal API key, browser, or media upload is required.

Use **Audio** and **Subtitles** to select embedded tracks. Noir remembers the selected track name (with a language-token fallback) for each show/movie instead of reusing a track number from another file. Subtitles also supports loading a separate subtitle file and **Timing** adjustments in half-second steps; **[ / ]** adjust timing from the keyboard. Timing is remembered per title. Settings offers small/normal/large plain-text subtitles, white/yellow text and a dark background. Appearance applies when opening the next video; styled ASS anime subtitles and image subtitles retain their own appearance.

**Continue Watching** cards have visible play icons, progress bars, saved positions and minutes remaining. Click the picture to resume, the title for details, or **⋯** for options such as restarting, viewing the diary, comments, My List or hiding the card. Overflowing Continue Watching cards form a horizontal shelf with previous/next arrows; Shift + wheel also scrolls the shelf. Movies/Shows include genre filters and genre search. Season pages offer All episodes / Unwatched / In progress filters.

**Viewing diary & my rating** keeps each completed viewing date separately, with a private five-star rating and note for the selected movie or episode. Rewatching keeps earlier completion dates and saves a new resume time. Earlier data can preserve only its existing latest watched date, labelled Earlier record; Noir cannot reconstruct viewing dates that older versions never saved. Ratings and notes are local and are not posted to communities.

**Sleep timer** in the player can pause after 30/60/90 minutes or prevent autoplay after one/two/three completed videos. It saves your place and never marks an unfinished episode watched. The timer applies to the current playback session and resets when you return to the library or close Noir.

Hover over the seek bar for a timestamp and a small scene preview. Uncached frames take a moment to decode separately and silently without moving the main player. Frames are cached in ten-second intervals; this disposable cache is bounded to 128 MB and excluded from library backups. A timestamp remains available when a frame cannot be generated.

Show details include cached upcoming air dates, when announced, and an expandable list of released regular episodes absent from your folders. Dates and numbering come from TVmaze; specials and unknown air dates are excluded from missing-episode counts. No video download occurs.

Controls float over the picture so they never shrink the video surface. Click the picture to hide or reveal the controls; moving the mouse also brings them back. During playback they hide after three seconds of inactivity. **Fill** removes black bars by cropping the picture edges without stretching it; click the Fill button to switch to **Fit** and keep the entire picture. The choice is saved.

Keyboard: **Space** play/pause; **Left/Right** seek 10 seconds; **Up/Down** change volume; **F** fullscreen; **Escape** exit fullscreen or return to library; **M** mute; **I** skip a known intro. Double-click the picture for fullscreen.

## Files and installation

- Installed app: `%LOCALAPPDATA%\Programs\Noir\Noir.exe`
- Settings and bookmarks: `%USERPROFILE%\.noir\library.json`
- Last state backup: `%USERPROFILE%\.noir\library.json.bak`
- Copied artwork and metadata: `%USERPROFILE%\.noir\art` and `metadata.json`

The app runs locally with no browser or media server required. VLC is bundled with the app, so your existing VLC installation is not required or modified. Installation is per-user and portable: remove the `Programs\Noir` folder and the shortcuts to uninstall; keep the separate data folder if you want to preserve progress.

## Building

Requires the .NET 10 SDK on Windows. VideoLAN's official NuGet packages provide LibVLC and LibVLCSharp.

```
dotnet build Noir.csproj -c Release
dotnet publish Noir.csproj -c Release -r win-x64 --self-contained true -o publish
```

Core regression tests live in the neighboring `Noir.Checks` project. They cover timestamp persistence, watched-state transitions, season ordering, extras exclusion and real native MKV playback/track selection. A desktop integration validation is compiled only with `NOIR_VALIDATE`; its isolated data folder is mandatory and it never modifies the normal library. The recorded validation output lives in `NoirValidation` in the workspace.

## Third-party components

Playback uses [VideoLAN LibVLC](https://www.videolan.org/vlc/libvlc.html) and [LibVLCSharp](https://code.videolan.org/videolan/LibVLCSharp); review pages use [Microsoft WebView2](https://learn.microsoft.com/en-us/microsoft-edge/webview2/). Their licenses are preserved with the installed binaries and license files. Metadata sources: [Cinemeta](https://github.com/Stremio/stremio-official-addons/blob/master/index.json), [TVmaze](https://www.tvmaze.com/api) (CC BY-SA), [Wikipedia](https://en.wikipedia.org/wiki/Wikipedia:Copyrights) (CC BY-SA), and [IMDb's personal-use episode index](https://data.imdb.com/non-commercial-datasets/). Links to matching metadata source pages are preserved in title details. UI choices draw on [HBO Max's browsing, My List and Continue Watching guidance](https://help.hbomax.com/tk-en/answer/detail/000002559) and [Netflix's subtitle appearance guidance](https://help.netflix.com/en/node/100267). Windows version 0.12.1.






## AIOStreams downloads and browsing, 0.12.1

Noir uses TorBox for downloads and AIOStreams for discovery. A wrapped playback URL is not a download reference. If your host hides those references, open Settings → Downloads → AIOStreams download connection and import a configuration export that includes credentials. Noir creates a separate profile on that same AIOStreams host with its documented hash formatter. The original Stremio profile remains unchanged. A retired TorBox Search preset is omitted only from the new copy; Noir still reads your TorBox account directly. Private manifests and the original connection are encrypted for your Windows account.

Release browsing starts with three suggestions, followed by expandable quality groups. Verified identical torrent references collapse into source entries; alternative encodes remain separate choices. Quality, audio language, readiness and size filters persist on desktop and Android. Unknown sizes remain available and season packs are checked against their actual file lists before selection. Choose files, review the destination and size, then queue. No release downloads merely because it appears in search.

Settings are grouped into Playback, Library, Downloads, Phone Sync, Community and Advanced. Details separate Episodes, About and Activity. History uses consistent collection cards and a compact monthly summary. Large catalogs load forty cards at a time; expanded release groups load twenty rows at a time. Artwork decoding and its cache are bounded.

Progress writes use an ordered background writer and flush when leaving playback or exiting. Pause, resolved manual seeks and credits also request a flush. Native stopping runs off the UI thread with guarded transitions. Clicking hides controls immediately; revealing them waits for the Windows double-click interval so a double click can enter fullscreen without flashing the bars. Audio/subtitle choices share one menu. Secondary player tools live in More, cached preview frames appear immediately, and credits use a small expandable next-episode chip.
