# Reliable discovery and downloads

Status: core implementation delivered in Windows 0.10.0 / Android 0.3.1, 10 October 2026. The original design below also contains follow-up goals; this status section distinguishes them.

Implemented: independent source searches and visible errors; configuration selectors and metadata identity resolution; source/size/cache labels; quality/size preferences; bounded season discovery; pack/gap planning with availability, queue and future checks; manual matching of unnumbered single-episode files; shared PC/phone plans and idempotent queue commands; preparation fairness; English/Arabic sidecars and local subtitle alignment with confidence checks and original backups.

Live limitation: the manifest displayed in Brave matches Noir's saved link. Resolving Dorohedoro to tt11147852 still yields the upstream “TorBox Search has been removed” error. The TorBox account lookup succeeds. Its browser addon toggle was restored to ON and no remote configuration was saved or addon removed. New AIO release discovery and a user-selected live media acquisition remain unverified; fixture transfers do not establish live success.

Follow-up: audio-language release preferences, richer codec/confidence presentation, stopping discovery early when coverage becomes sufficient, a separate connection-check report and progressive source result lists. Current UI shows per-source progress during discovery but presents the combined list after both finish. The manual file selector supplies an explicit whole-pack selection; planning defaults to missing local files. Future acquisition, automatic upgrades and other providers remain deferred.

Validation: core checks include independent errors, selectors, identity ambiguity, season coverage, duplicate commands, file safety, subtitles/encoding and queue fairness. The packaged alignment worker corrects a known 5.2-second offset using embedded reference cues. Window and Android checks use isolated fixtures.

## Outcome

Find a movie, an episode, or the missing episodes of a season through AIOStreams; let the user compare quality and size; use TorBox to acquire and deliver the selected files to the PC. The phone can issue the same commands. Files enter Noir's normal library structure and retain their viewing identity. This feature downloads, rather than streams.

## Evidence and first priority

The current code has two confirmed problems:

- `TorboxProvider.SearchSeason` returns immediately when the account contains a matching pack. It never searches AIOStreams in that case.
- `TorboxProvider.Search` drops an AIOStreams error whenever the TorBox account supplies any results. A partial success consequently looks like a complete search.

The saved AIOStreams link previously returned an error stream. The user reports their Stremio configuration works. Those observations do not establish that the user's addon must be removed. Compare the exact working installation configuration and episode request before changing anything. Preserve the user's addons, including TorBox Search. A previous temporary browser toggle must also be checked and restored to its original state if still changed; do not overwrite unrelated pending edits.

`IndexSearch` currently clears the manifest query string when constructing a request. Investigate and preserve supported configuration selectors. Do not assume this is the user's root cause without a comparison.

## 1. Make each source observable

- Always query AIOStreams and the TorBox account independently, for episodes and seasons.
- Return a search session with separate source states: searching, ready, empty, not configured, failed, or cancelled. Distinguish a valid empty response from an error stream and from unsupported release metadata.
- Show results as each source finishes. Keep useful partial results while showing the other source's error and a Retry action.
- Label provenance as **Found through AIOStreams** or **Already in your TorBox**; retain both labels when deduplicating the same torrent.
- Add a connection check that reports the requested title/episode, result count, usable release count, and a sanitized reason for rejected results. Never expose credentials, private configuration URLs, or download links in diagnostics or phone responses.
- Verify the same title and episode against the user's working Stremio installation configuration. Account for configuration variants and documented request-dependent behavior. Do not silently replace their configuration.

Acceptance: a failing AIOStreams request remains visible even with working account results; season discovery still runs when an account pack exists.

## 2. Represent releases accurately

Replace the flat choice model with a candidate containing stable title and episode identity, source provenance, torrent reference, available file identity, parsed quality/codec/languages, and matching confidence.

- Read structured AIOStreams metadata first; use explicit, tested adapters for supported wrapped torrent references.
- Do not open playback URLs during discovery just to extract a hash or accidentally initiate cloud acquisition.
- Explain unsupported references rather than silently dropping all choices. Do not promise every playable Stremio link can be converted into a TorBox download.
- Separate **selected episode size**, **pack size**, and **bytes selected for PC download**. Show Unknown instead of inventing a size.
- Model TorBox cache status as ready, not cached, or unknown. A failed cache check is not evidence of a cache miss.
- Deduplicate torrent identity for acquisition while preserving individual file choices and source labels.
- Use IMDb/TMDB identity and trusted aliases where available. Handle specials, multi-episode files and anime numbering explicitly. Uncertain mappings need review rather than an automatic rename.

Acceptance: the same release from several sources appears once with accurate provenance; an episode is not described using an unlabelled whole-pack size.

## 3. Plan a season, rather than treating it as one episode

AIOStreams' episode results can point to season packs. TorBox verifies and supplies their files; listing the account alone does not discover new releases.

1. Establish the season's known released episodes and compare them with local files and queued downloads. Keep watched status separate from file availability.
2. Offer **This episode**, **Missing episodes** (default season action), and **Choose episodes**. A whole-season selection remains available explicitly, including watched episodes if desired.
3. Query a small, bounded set of relevant episode requests, beginning with the first missing episode; reuse results and stop when coverage is sufficient. Do not fan out unlimited requests for every episode.
4. Identify possible packs and inspect file lists through supported TorBox endpoints where available. If inspection requires acquisition, label that state and require the normal user selection before starting it.
5. Compute verified episode coverage. A two-episode pack must not be labelled a complete season. Unknown or unreleased episodes must be displayed honestly.
6. Prefer a consistent pack matching the user's preferences; fill gaps with individual releases only when the user accepts that combination. Offer alternatives when a pack is too large or has the wrong audio/subtitles.
7. Preview the exact episodes, known selected bytes, destination, existing-file skips, gaps and any uncertain mappings before enqueueing.

Example: for a season with 12 released episodes and 3 already on the PC, propose the remaining 9. If a pack covers only 7 of those, show the two gaps and search for them rather than calling the pack complete.

## 4. Keep the choice screen simple

- Show a short list of strong matches, with **Show all results** available.
- Explain recommendations in ordinary terms: matches selected quality, correct audio, ready on TorBox, covers all selected episodes, or smaller download.
- Let the user set and remember preferred resolution, audio/subtitle language and optional size limit. Hard mismatches should not silently pass as recommended choices.
- Rank by exact identity and episode coverage first, then selected preferences and availability. Do not automatically choose the largest or highest-resolution release.
- Keep discovery separate from starting a download. Selecting a plan is the deliberate acquisition action.
- Phone and PC use the same plan and validation logic. The phone selects and monitors; the PC holds provider credentials and receives the media.

## 5. Finish delivery reliably

- Extend the existing durable queue rather than creating a second competing queue.
- Persist the selected episode-to-file mapping and destination before work starts. Repeated phone commands must not enqueue duplicates.
- Show separate stages: preparing on TorBox, downloading to PC, organizing, and ready to play.
- Resume interrupted transfers when supported, refresh expired links, and retry individual failed files without restarting completed episodes.
- A long cloud preparation should not prevent an already-ready job from progressing. Bound concurrent work and respect provider limits.
- Verify completed files before importing. Never overwrite an unrelated local file or lose watch history during organization.
- If the PC is offline, the phone must clearly say the command was not accepted; do not claim it is downloading.

## Delivery gates

1. **Connection and visibility:** exact-request diagnostics, independent source results, visible partial failures, and configuration preservation. Confirm live AIOStreams discovery before proceeding to elaborate season automation.
2. **Episode download choices:** candidate model, supported reference parsing, honest sizes and cache state, ranked choices and file matching.
3. **Season planner:** missing-episode coverage, bounded discovery, pack verification, gaps, and preview.
4. **Phone and durability:** shared plans, idempotent commands, progress, restart recovery and safe organization.

## Required verification

- Regression tests for hidden AIO errors and the season early return.
- Request construction tests covering supported path/query selectors and error-stream responses.
- Parser fixtures for supported AIO release shapes, unsupported links and missing metadata.
- Season cases: complete and partial packs, duplicates, missing local files, watched-but-deleted episodes, specials, anime numbering and unreleased episodes.
- Queue cases: repeated phone commands, interruption/restart, expired links and one failed episode in a season.
- Live read-only discovery of a release **not already in the TorBox account**, an episode, a season candidate and a movie. Report any remaining provider limitation honestly.
- A user-selected real download through TorBox to the PC, followed by correct naming/import and preserved history. Fixture transfers alone do not establish that the user's live integration works.

Deferred until these gates pass: automatic acquisition of future episodes, unattended quality upgrades and broad provider expansion.

## Delivered verification (10 October 2026)

- Windows 0.10.0 installed and launched; installed assembly and subtitle worker hashes match the verified package. Prior profile/settings/queue backed up. Stored progress, viewings, roots, notes, watchlist and catalog bookmarks remained unchanged. HTTPS sync still listens on port 443.
- 156 final offline core checks passed, including new timeout visibility, manual mapping and delivered-video restart recovery. Earlier online metadata checks passed before the final local fixes.
- Seven isolated PC window checks passed; download choice and file preview images inspected.
- Authenticated mobile transport checks passed with a copied profile and fixture provider. Focused Android PC download/subtitle test passed (20.978 seconds). End-to-end phone sync/decoding passed; the separate visible-player screenshot test stalled in the hidden emulator and was interrupted, so the complete seven-test Android suite was not confirmed.
- Signed ARM64 and universal Android 0.3.1 packages built and verified, copied to Desktop; no physical phone installed by this run.
- Packaged subtitle worker corrected a known +5.2 second fixture offset to -5.2 seconds, with scale 1.0, and rejected a silent reference.
