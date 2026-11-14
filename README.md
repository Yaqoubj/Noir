# Noir

**A place for everything you watch.**

Noir is a downloader, an organizer, a streamer and a library in one app. It finds and
downloads what you want, files it into tidy folders, plays it, and keeps a record of
everything you have watched, here or somewhere else. It runs on Windows and Android
phones, with a Google TV / Android TV app for the sofa.

[**Download Noir**](https://github.com/Yaqoubj/Noir/releases/latest) · Windows 0.18.0 · Android 0.11.0 · free and open source

![Noir Home, with cinematic artwork, Continue Watching and a status strip](docs/screenshots/home.jpg)

Home shows all four roles. A status strip under the hero lists what Noir is doing or has
waiting: downloads in progress, the next episode airing, free space, a site that stopped
syncing, and files to tidy. Your own rows come first.

## Download what you want

Noir downloads on PC and phone. A built-in torrent engine works without a debrid
account: it picks healthy releases, shows peers and speed, switches to another release
if one stalls, and shares back for a while afterwards. Interrupted downloads continue by
themselves; ones you paused stay paused. Streaming and downloads can also use your
configured sources, including AIOStreams, TorBox and Real-Debrid, with each service's own
connection and account requirements.

![Downloads, with the queue, a progress bar and the three tabs](docs/screenshots/downloads.jpg)

- Download this episode, this season, the whole show or the next few episodes, with
  quality and size limits. Episodes you already have are skipped.
- Downloads has three tabs with live numbers: Queue, On this PC (On this phone on
  Android) and Keep ready.
- Save movies and episodes for offline viewing, with downloads managed inside Noir.

## Keep it tidy

The organizer renames your videos and files them into show and season folders. You see
a preview first, choose which shows to include, set naming options, and can Undo.
It can also organize new files automatically.

![On this PC, with Organize files and Free up space](docs/screenshots/downloads-organize.jpg)

- **Free up space** finds empty folders, sample clips, leftover release files and
  duplicate copies. It keeps the larger copy and moves your progress to it. Program
  files are listed but never removed.
- **Automatic downloading and removal** follow rules you choose: how far ahead to
  download, which shows, when downloads may run, and if and when watched files are
  removed. Each show can have its own override.

## Stream and watch

Point Noir at your movie and show folders on Windows, or stream through your own
AIOStreams connection. VLC powers playback on PC, phone and TV, with saved resume
points that let you leave an episode halfway through and come back to it.

![Noir's player with the next episode waiting](docs/screenshots/player.jpg)

- Choose audio tracks and subtitles, including English and Arabic subtitle downloads
  when available. Subtitles come from several sources, show where each one came from,
  and sync quickly, including fixes for frame-rate mismatches.
- Skip known intros, recaps and credits; let Up Next take you into the following
  episode, or cancel it.
- Open any title in Harbor from its page.

## A library worth browsing

Browse your folders as a library of posters, artwork, seasons and episode pages,
with title synopses and cast, **My List** for things you want to watch,
and collections you put together yourself.

![A show in Noir, with seasons and episode details](docs/screenshots/show.jpg)

Home mixes **Continue Watching**, **Keep ready** and your lists with recommendations
drawn from what you watch, rate and favourite.

![Posters and recommendations on Home](docs/screenshots/home-rows.jpg)

Discover lets you search movies and shows beyond your library. On PC, hover a poster to
**Stream**, **Download** or add it to **My List**; the spotlight offers the same. On the
phone, a title page has one Stream button and episode rows with stream and download
icons. You can also save a title without downloading it or marking it watched.

![Discover, with Stream, Download and My List on each poster](docs/screenshots/discover.jpg)

- Find something through popular picks, genres, or suggestions based on your favourites.
- Let smart lists surface unfinished shows, comfort rewatches and shorter choices
  for tonight.
- Arrange Home's rows around the way you browse.

## Your whole viewing life

From a film at the cinema to a series on another service, Noir can be your watch
tracker without a media folder: search a title, choose when you watched it,
and log a movie, an episode or a season.

![History, with shows in progress and the next episode to watch](docs/screenshots/history.jpg)

**Up next** keeps your place across the shows you follow, with a quick check-off
for each episode, while your diary keeps viewing dates and rewatches.
History, ratings, favourites and private notes stay with you even when the video
files are gone.

- See what's coming in the episode calendar, with air dates when available and
  optional alerts.
- Look back through watch-time stats, streaks and a year in review you can save
  as story images.
- Rate or leave a note from Recently watched, whenever you feel like it.

![Recently watched titles in Noir](docs/screenshots/recently-watched.jpg)

## Bring your history with you

Bring your records into Noir and keep them alongside what you watch here,
without having to remember every episode you've seen.

- **Import exports** from Netflix, Trakt, TV Time and Letterboxd, IMDb ratings,
  Simkl backups, or a CSV of your own.
- **Connect your other libraries:** pull watched titles from Stremio, Plex,
  Jellyfin, Emby or Kodi; Nuvio can exchange watched status in both directions.
  Noir can also mark what you finish as watched in Stremio, which is off by default
  and only changes the watched state.
- **Review file imports before adding them.** Re-imports skip duplicate viewings,
  and an import can be undone.

Trakt and Serializd marks sync newest first, and the phone can send its own marks.
Imports and exports are available on Windows and phone; Kodi imports on the phone
use a paired PC. Export your diary as Noir CSV/JSON, Letterboxd CSV or Trakt JSON.

## From your desk to the sofa

Browse, track, stream and download on the phone without a PC, or pair your phone
or TV with Windows Noir over your home network to share watch history, resume points,
ratings and My List with the linked PC profile.

- **On your phone:** play completed downloads offline, or stream files from your
  PC while it is reachable.
- **On your TV:** browse and play with the remote, using your own AIOStreams link
  or a paired PC.
- **For later:** choose shows to **Keep ready**, with automatic episode downloads
  within a storage budget you set on PC or phone.

Keep Noir running on the PC for local sync and PC-file streaming.
The phone saves offline progress for the next connection; its own streams and
completed downloads can play with the PC off.

## Yours to keep

Your library, history, private ratings and notes are saved locally, with device
sync directly over your home network and no Noir account or cloud account required.
Separate profiles give each person their own library and viewing record.

- Back up your history, lists, settings and artwork in a `.noirbackup` file on
  Windows, and restore it in Noir.
- Windows can also make automatic weekly backups.
- Backups leave out video files, passwords and browser sign-ins.

Online discovery, subtitles and optional service connections use their respective providers.

## بالعربية أيضًا

**أفلامك ومسلسلاتك وسجلّ مشاهداتك، في مكان واحد.**

Choose English or Arabic, with right-to-left browsing layouts and support for Arabic subtitles.

<p>
<img src="docs/screenshots/arabic-home.jpg" width="49%" alt="Noir Home in Arabic, arranged right to left">
<img src="docs/screenshots/arabic-history.jpg" width="49%" alt="Noir watch history in Arabic">
</p>

## Get started

1. [Download Noir](https://github.com/Yaqoubj/Noir/releases/latest). On Windows,
   unzip `Noir-Windows-0.18.0.zip` and run **Noir.exe**. On a phone, install
   `Noir-Android-0.11.0.apk` (ARM64, most phones) or `Noir-Android-0.11.0-All.apk`
   (older or unusual devices). On a TV, install `Noir-TV-0.11.0.apk`.
2. Make it yours: add your media folders on Windows, search and log what you've
   watched, or import an existing watch history. The phone works without pairing.
3. Connect what you want: add your AIOStreams link in download settings for
   streaming, or use Windows **Settings → Phone → Pair a phone or TV** for
   local device sync.

[Windows guide and building](Noir/README.md) · [Android guide and building](Noir.Android/README.md) · [MIT license](LICENSE)
