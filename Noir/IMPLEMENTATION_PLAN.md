# Noir 0.8 / Android 0.2 work

User approved the full earlier feature list on 7 October 2026.

- [x] Remove Compact view and Letterboxd/Reddit embed controls on both platforms.
- [x] Reddit Top comments in the regular in-app page; separate extracted reader requires approved data access. Do not represent the website fallback as native scraping.
- [x] History Collection and Diary views, month navigation/recap, progress cards, quick ratings, retained unavailable artwork.
- [x] Home Resume / Next episode separation and Recently added.
- [x] Watching / Planned / Finished / Dropped labels and Caught up calculation.
- [x] Episode artwork cards, season progress, spoiler controls.
- [x] Named collections with two-way sync.
- [x] Durable local play queues; honor cancellation and sleep limits.
- [x] Match correction without renaming files or discarding history.
- [x] Coming up from cached episode dates.
- [x] Sync status with last successful time; remember the last review tab.
- [x] Android Watch now / Watch & save offline, bounded durable transfer queue, pause/resume, validation and storage checks.
- [x] Tests, visual verification, signed packages, PC installation and history preservation verification.

- [ ] Separate extracted Reddit comments reader: pending Reddit-approved data access. Current implementation is the regular website with Top sorting and saved exact discussion links, not native scraping.

Validation: 103 core/VLC checks, 13 authenticated HTTPS/byte-range checks, 6 Android tests (including downloads and review lifecycle), then 2 Android playback/UI tests after the final Diary layout change. Release lint and both APK signature checks passed. WPF and Android screenshots were inspected.

Installed PC 0.8.0; signed Android 0.2.0 APKs copied to Desktop. Compared the stable profile with the installation backup: all 50 catalog IDs, 18 bookmarks, viewing dictionaries, personal reviews, lists and cached posters remain intact.

Deferred in the approved earlier plan: recurring-audio intro analysis, picture-in-picture and cloud/remote sync.
