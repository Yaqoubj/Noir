# Noir: download discovery, UI refinement and calmer playback

Status: implemented and installed as Windows 0.11.0, with Android 0.3.2 packages, 10 October 2026.

## Delivered and verified

- A separate AIOStreams profile on the same host supplies documented torrent-hash metadata. It is connected to Noir with Windows account encryption. The original Stremio profile and its addons remain intact; only the Noir copy omits the retired TorBox Search preset that the host refuses to save.
- Live discovery found 84 usable Dorohedoro S02E01 references, 63 for S01E01 and 1,228 for The Matrix. TorBox returned file lists for episode releases outside the user's account, a 12-episode 1080p season pack and a single 4K movie file. No new personal download was started solely for testing.
- Both clients show suggestions and expandable quality groups, preserve independent versions, collapse verified duplicate torrent references, and remember quality/audio/readiness/size filters. Groups add twenty rows at a time. Core checks cover 6,000 source entries, 3,000 distinct choices and repeated large searches without the old 1,500-choice error.
- Ordered background saves, native-stop guards, exact resume, recognition of native Intro/Opening/OP chapters, manual seeking through intro/credits, watched-at-credits, fullscreen click behavior, return-to-library cleanup and natural-end advancement passed regression checks. Cached previews return immediately; uncached work waits for deliberate hovering and reuses one preview engine.
- Player controls use a single main row, grouped audio/subtitles, a More menu, brief keyboard feedback, quiet successful saves and an expandable credits chip.
- Details use Episodes/About/Activity, earlier episode rows, a season picker and a clear unavailable-file Download action. History uses consistent poster/progress cards, collapsed filters and a compact monthly summary. Settings are split into six categories. Search is debounced, artwork/cache decoding is bounded, and long catalogs add forty cards without rebuilding existing ones.
- Desktop layouts passed at widths 940/1320/1920. The Android emulator verified PC bookmarks, stale-write protection, subtitle delivery and a remote download order, including three initial suggestions and twenty-row lazy group expansion.
- A ten-minute 1920×1080 run decoded 28,849 frames with zero lost pictures. Ten opening/seek/pause/back cycles completed; advancing video took 0.69–0.86 seconds after the first 1.56-second opening. Dispatcher gap p95 was 47 ms; two gaps exceeded 200 ms, with a 295 ms maximum. These are measurements under the concurrent workload, not proof of zero stalls.
- Installed files match the package. Progress, viewing events, My List, roots, play queue, hidden Continue entries, phone token hashes/certificate and last-sync state were compared with the pre-install backup and preserved. Sync listens on ports 443 and 49271.

Evidence: `work/ux-core-large-final.log`, `work/aio-bridge/live-choices.log`, `work/aio-bridge/verified-pack-movie.log`, `NoirValidation/ux-20261010/player/desktop-checks.json`, `NoirValidation/ux-20261010/smoothness/player-smoothness.json`, `NoirValidation/ux-20261010/layout/ux-checks.json`, `NoirValidation/ux-20261010/downloads/download-ui-checks.json`, `work/ux-android-pc-download-polished-test.log`.

### Practical limits

A complete new personal TorBox transfer, subtitle timing on that chosen release and playback on the physical phone still require the user to select a release and try the new APK. Transfer/subtitle/queue correctness was checked with controlled fixtures; live discovery and real TorBox file metadata were checked separately. AIOStreams returns one result response; Noir reports discovery stages and lazily renders the returned list, rather than progressively streaming individual index results. Layout widths were checked at the current Windows scale; independent 125%/150% sessions were not measured. Android player preview thumbnails, marker editing and sleep timer remain outside this companion update.

The implementation plan below records the intended behavior and investigation that led to these changes.

Keep Noir's black/gold appearance, local library, permanent watch history and phone pairing. Take inspiration from Nuvio's organization and navigation, rather than copying its colors. Treat “less frenzy” as both responsiveness and quieter controls until clarified.

## 1. Correct the AIOStreams download connection first

### What the live checks established

- The saved AIOStreams profile returns results: 85 for Dorohedoro S02E01 and 64 for S01E01. Both tested client user agents return the same counts.
- Of the 85 season-two entries, 84 have wrapped playback links and one is an addon error. Noir's current extractor recovers no usable torrent hashes.
- The working links use an opaque `/playback/` route. Their tokens are not directly readable JSON; opening these links was unnecessary and was not attempted.
- This host's documented Search API returns HTTP 403 with “Search API is disabled.”
- An obsolete TorBox Search warning does not explain the failure of all the other indexes. The current error selection makes that warning look like the main failure.

This corrects the earlier broad diagnosis. Stremio can play these links while Noir still lacks the reference needed to ask TorBox to download the underlying release. More indexes alone do not resolve that difference.

### Implementation order

1. Separate discovery from download eligibility. Show release counts and a truthful explanation when download references are missing. Keep individual addon errors in source details. Do not label a successful multi-source response as a total search failure.
2. Prefer structured AIOStreams metadata or its documented Search API on deployments that support it. Preserve file index, filename, size, season/episode identity and source errors alongside the hash.
3. Test a separate Noir-only profile using the documented `{stream.infoHash}` formatter field as an explicit metadata bridge. Strip that marker from visible release descriptions. This route has now been proven on this host with live checks. Keep the existing Stremio profile and its addons intact.
4. If that host cannot supply download references, compare an API-enabled AIOStreams deployment before choosing another connection. Explain the setup and index availability tradeoffs. Do not decode playback URLs by guessing or treat a playback URL as a torrent hash.
5. Use TorBox exclusively for acquisition. Keep episode discovery distinct from season-pack selection. Offer missing episodes, a verified pack, or manual file selection; never infer a whole season solely from an episode result.

### Make large release lists easy to choose from

- Start with a short recommended selection based on the chosen episode/season, preferred quality and language, size limit and verified TorBox readiness. Explain each recommendation briefly; the user still chooses what to download.
- Group the remaining releases behind expandable quality sections such as 1080p and 4K. Within each section, show distinct versions as compact rows and keep duplicate indexer entries behind a source count, for example “1080p · English · 1.4 GB · 5 sources.”
- Deduplicate only when a reliable torrent/file identity establishes that entries refer to the same content. A matching title or similar size alone is insufficient. Preserve alternate sources and their individual status when expanding a row.
- Keep episode files, season packs and other editions distinguishable. Do not merge versions with different audio, HDR/SDR, codec or cuts into one misleading download choice. Show important differences without requiring expansion.
- Offer simple filters for quality, language, maximum size and ready-to-download releases. Remember preferences, with a clear reset and an easy “All releases” view. Keep unknown attributes explicit and available rather than silently discarding them.
- Show both distinct choices and total source entries, for example “18 releases from 84 results.” A collapsed group summarizes its count and size range; expanded rows retain exact sizes and download actions.
- Add results incrementally without moving a focused row or resetting filters and expanded groups. Load long expanded lists in batches or virtualize them, including on Android.

### Completion gates

- Discover an actual episode release absent from the user's TorBox account and obtain a valid download reference.
- Verify a movie and a season pack, including their file mapping, size and selected quality.
- Complete a user-selected download through TorBox into Noir's usual folder structure, then scan and play it.
- Verify an order sent from Android, queue persistence and English/Arabic subtitles. Report subtitle alignment uncertainty honestly.
- Test mixed working sources plus an addon error, missing hashes and the disabled API. Fixture-only tests cannot establish that live discovery is fixed.
- Test grouping with hundreds of mixed releases: true duplicates collapse, different versions stay selectable, all sources remain accessible, filters preserve selection and the interface stays responsive as results arrive.

Evidence: `work/aio-client-comparison.log`, `work/aio-link-shapes.log`.

References: [AIOStreams Search API](https://docs.aiostreams.viren070.me/apis/search/), [custom formatter fields](https://docs.aiostreams.viren070.me/reference/custom-formatter/).

## 2. Make playback responsive and calm

### Findings to measure

The player currently saves progress every two seconds. That path serializes the whole library and performs durable disk writes on the UI thread. Native stop/dispose transitions also run on that thread. Timeline hovering can create a separate preview decoder for a new preview position. These are plausible sources of pauses; profiling is needed to establish their actual contribution.

The controls occupy two dense rows. Showing controls reveals the whole header/footer, and the first click of a double click can change visibility before fullscreen is processed. The next-episode panel can also occupy substantial space during credits.

### Performance changes

- Measure episode opening, resume restoration, seeking, fullscreen and returning to the library before changing the decoder.
- Put progress persistence on one ordered background writer with immutable snapshots and revision checks. Preserve the two-second durability target. Flush at pause, completed seek, leaving playback, credits completion, exit and before a phone sync response.
- Ensure an older queued save cannot overwrite newer progress from either device. Keep watched events idempotent and preserve permanent history after local files disappear.
- Give transitions an explicit state: opening, restoring, playing, paused, seeking, ended and error. Ignore stale callbacks from an earlier episode. Respect LibVLC/WPF thread requirements rather than moving native calls blindly.
- Keep metadata refreshes, library rebuilds and subtitle alignment off the playback critical path.
- Bound preview work to one worker. Show cached frames immediately, wait for deliberate hovering before generating a new frame, and suspend expensive preview work during opening or seeking. Measure whether worker reuse or an external thumbnail process is best.

### Interaction changes

- Keep a simple title/back header and one main control area: timeline, play/pause, seek, volume, language and fullscreen.
- Move sleep timer, fit/fill and intro/credits editing into a secondary menu. Use consistent vector icons with readable tooltips.
- One click hides or reveals controls. Two clicks toggle fullscreen without a first-click flash. Keep keyboard shortcuts and focus behavior.
- Hide after idle playback; keep controls available while paused, dragging, choosing a track or handling an error. Brief seek/volume feedback should not reveal every control.
- Combine audio/subtitle selection into one organized panel with separate sections. Remember language and subtitle-delay choices appropriately.
- Save quietly and show a message only if saving fails. Replace the large credits panel with a small next-episode prompt that expands on request or at the end.
- Reaching configured credits marks the episode watched while allowing playback and seeking to continue. Automatic advancement remains tied to the chosen end-of-episode behavior.
- Restore library scroll position and focus when leaving playback. Check that the video surface and fullscreen bars are removed.

### Completion gates

Targets, not current measured results: acknowledge player input within 100 ms and eliminate repeated UI stalls over 200 ms in the measured scenarios. Test ten opening/back cycles, rapid seeking, pause/exit/resume and a ten-minute representative 1080p playback session. Record dropped frames and memory, including preview hovering. Recheck intro/outro seeking, credits completion, click hiding, fullscreen and Android playback on a physical device where available.

## 3. Refine the whole browsing experience

### Shared visual system

Use consistent typography, spacing, card proportions, image cropping, icons, focus indicators and loading placeholders. Give each screen one obvious primary action. Keep animations short and respect reduced-motion preferences. Avoid heavy autoplay backgrounds.

### Home and library

- Preserve the current hero and black/gold identity, with one clear Resume or Play action.
- Make Continue Watching a stable landscape shelf with a restrained play affordance, episode label and progress bar. Use consistent poster cards for the catalog.
- Keep shelf positions and scroll stable when progress or artwork updates. Load artwork lazily with a bounded cache and offline fallbacks.
- Debounce search and virtualize or page long lists. Show download availability without overwhelming the title and poster.

### Show/movie details

- Put Resume/Play, or Download when unavailable, beside the main title information.
- Bring season selection and episodes higher on the screen. Use Episodes, About and Activity/Reviews sections so metadata and utility controls do not push episodes far below the fold.
- Move correction tools and other secondary actions into a menu. Keep available, watched and total episode counts readable and distinguish specials.

### History

- Keep Collection and Diary. Collection uses poster cards with watch progress, last activity and a clear resume/detail action, including titles deleted from the PC.
- Use a compact monthly summary and a filter panel instead of many equally prominent statistic/filter blocks.
- Show ratings when opening a title or choosing to rate it; remove repeated five-star controls from every poster.
- Group Diary entries by date with lighter rows. Preserve full episode counts, cached artwork and permanent watch records.

### Downloads and settings

- Make downloading a clear sequence: choose episode/season scope, compare quality/audio/size/readiness, select files if needed, then queue.
- Present a few recommended releases first, followed by expandable quality groups and source details, using the release-grouping rules above.
- Update queue rows in place. Keep detailed source diagnostics available without placing raw hashes or technical fields in the normal flow.
- Split Settings into Playback, Library, Downloads, Phone Sync, Community and Advanced. Preserve focus while background status changes.
- Carry the same hierarchy to Android with native touch-sized controls, rather than shrinking desktop panels.

Reference: [Nuvio UI customization](https://nuvio.wiki/settings/ui-and-customization) and [player preferences](https://nuvio.wiki/settings/player). The proposed layouts above are tailored to Noir's existing code and features.

## Delivery order and review

1. Accurate source diagnostics and a proven live download-reference route.
2. Player measurements, reliable background persistence and episode transitions.
3. Calmer player controls, with regression checks for resume, credits and fullscreen.
4. Shared cards and spacing, then Home/details, History, Downloads and Settings.
5. Phone parity and final verification.

Review desktop layouts at 940, 1320 and 1920 pixels and at 100%, 125% and 150% scaling. Check keyboard access, offline artwork, missing local files and restoration after restart. Preserve pairing, watch history and existing Stremio configuration throughout. Ship in reviewable stages so playback and download correctness do not depend on completing the whole visual redesign.

## Delivered in Windows 0.12.0

- Native dark Windows title bars and work-area fitting for smaller/DPI-scaled screens.
- Correct horizontal/vertical scrollbar templates, smooth wheel handling, responsive search header and episode rows, scrollable sidebar on short windows, and fullscreen bounds restoration.
- Persistent Downloads navigation with active-job badge and live in-place queue updates.
- Discover via Cinemeta: popular titles, movie/TV search, year/rating/synopsis, cached posters/schedules, My List, released-episode and season downloads through the existing AIOStreams/TorBox file-review flow.
- Search alone never adds titles to the archive; explicit saves and download requests register a title without changing watched history. Duplicate IMDb identities reuse the existing collection.

Android remains 0.3.2. Discover browsing is currently on Windows; paired phones retain their existing episode/season requests and sync workflow. New online searches require a connection; cached results remain browsable offline.

## Windows 0.12.1 / Android 0.4.0

Discover moves below My List on Windows; Downloads moves into the Scan/Settings section. Android gains Cinemeta search/details, cached posters/schedules, My List saves, and episode/season requests to its paired PC. More → Downloads separates the live PC queue from phone offline downloads. Public search requires no PC credentials; new-title commands use the existing pinned pairing connection and server-verified identities/schedules.
