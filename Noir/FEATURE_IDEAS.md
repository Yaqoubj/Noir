# Useful next features for Noir

Status after the 0.6.0 update: priorities 1–5 are implemented. Timeline frames are generated on demand and cached, rather than analysing whole videos while idle. Play icons, visible card menus, remaining-time labels, a Continue Watching shelf, genre filters and episode filters are also included. Priorities 6–10 remain proposals. Recurring-audio intro detection needs a separate analysis and confidence-testing step; existing chapter/manual skipping is unchanged.

| Priority | Feature | What it would do |
|---|---|---|
| 1 | Better subtitle and audio controls | Subtitle size, colour, background, timing adjustment, and remembered audio/subtitle language for each show. |
| 2 | Proper rewatch history | Keep the original watched record while replaying an episode. Save each viewing date separately, with local ratings and short notes. |
| 3 | Timeline previews | Show a small cached frame while hovering over the seek bar, making scenes easier to find. Generate previews quietly when idle. |
| 4 | Sleep timer and an autoplay limit | Stop after the current episode, after a chosen time, or after a chosen number of episodes. Avoid accidentally continuing through a season. |
| 5 | Upcoming and missing episodes | Show the next announced airdate and which released episodes are absent from the local folder. Cache information for offline use; do not download videos. |
| 6 | More capable intro/recap detection | Use recurring opening audio across episodes to suggest intro markers for files without chapters. Offer skip-recap as a separate action; keep an easy correction control. |
| 7 | Automatic folder updates | Detect finished additions/moves and refresh the library without pressing Scan. Wait for copies/downloads to settle before inspecting them. |
| 8 | Remember the preferred review site | Reopen the last chosen community tab for a show, with ratings and notes saved locally. |
| 9 | An episode matching screen | Correct ambiguous titles, specials, or alternate episode numbering without changing filenames or losing bookmarks. Show the expected title, year, season and episode before changing a match. |
| 10 | Optional short rewind on resume | Start five seconds before the saved position for context. Default stays off so exact-time resume remains unchanged. |

## Research behind the choices

- [HBO Max playback controls](https://help.hbomax.com/gb-en/Answer/Detail/000002510) distinguish skip intro from skip recap and let viewers dismiss next-episode prompts. Noir already has intro skipping, a next-episode prompt, and saved resume; recap recognition would add something new.
- [Plex player settings](https://support.plex.tv/articles/settings-android-tv/) include optional rewind on resume and controls for intro/credits skipping. Short resume rewind is useful as an option, not a replacement for Noir's exact saved position.
- [Plex's intro detection explanation](https://www.plex.tv/en-au/blog/go-ahead-and-skip-that-intro/) describes finding recurring audio across a season. This is a plausible future direction for unlabelled local files, but requires additional analysis and testing.
- [Jellyfin's media segments](https://jellyfin.org/docs/general/server/metadata/media-segments/) distinguish intro, recap, preview and outro segments; this supports treating them separately rather than applying one skip range to everything.
- [Jellyfin's client update](https://jellyfin.org/posts/state-of-the-fin-2026-05-24/) documents subtitle timing adjustments and seek preview images. These are useful player improvements for Noir's mixed local files.
- [TVmaze's official API](https://www.tvmaze.com/api) exposes episode lists, alternate ordering and upcoming-episode details. Noir already uses TVmaze, making an upcoming/missing episode view a natural extension.

## Sync behavior

Noir's sync worker runs automatically when a completed watch is recorded, on startup for eligible pending work, and on a five-minute retry timer. Opening review websites is not required for ordinary sync. If Noir is closed, pending jobs remain saved until it runs again. A genuinely expired account session or a human verification request can still require signing in once; Noir should report that accurately rather than ask for routine manual retries.

The 0.5.1 changes correct Trakt's semantic watched-date menu and Serializd's icon-prefixed watched label. The background browser closes after work is done. Requests arriving during a running batch trigger another pass immediately.

Verified on 2 October 2026: the installed 0.5.1 worker retried the existing watched Dorohedoro S01E07 job automatically after launch. Trakt and Serializd both remained watched after reloading, and both jobs were recorded as confirmed. No review window was opened by the user for that retry.
