# Noir design rules

One page for anyone changing a screen on PC, phone or TV. The look (dark surfaces, gold accent, the Home hero, the rows, the player layout) is settled; these rules are about naming, weight and repetition.

## Principles
1. **One name per thing**, everywhere, in both languages. Use the vocabulary below; never invent a synonym.
2. **One gold action** per page header or per card. Everything else is quiet.
3. **Content before controls.** What a page is about is visible without scrolling at 1320×844 on PC.
4. **Nothing twice on one screen.** No destination both in the sidebar and in a tab row; no value drawn twice (bar and ring); no line that repeats a switch's state.
5. **One drawn icon set** on PC, phone and TV. No text characters (◉ ▶ ⌂ ≡ ↓ …) as icons.
6. **Empty takes no space.** A feature with nothing to show is not drawn.
7. **Settings apply as you change them.** No Save buttons; text fields save on Enter or on leaving the field and show a small "Saved" tick.

When a screen breaks a rule, prefer, in this order: remove, merge, rename, reorder, reweight. Adding a control or a screen is a feature, not a fix. Decisions are logged in `work/sharpen-round/ui-decisions.md` (U-numbers).

## Home
Noir is a downloader, an organizer, a streamer and a library, and Home has to show all four, not only the library.
- **Status strip** under the hero: one quiet tile per thing Noir is doing or has waiting (downloads, a website that stopped syncing, the next episode airing, files to tidy, free space). A tile with nothing to say is not drawn; each opens the place where you act on it. "Edit Home" sits at its end.
- **Your own rows first** by default: Continue watching, play queue, My List, Keep ready, Recently added, Coming up; then Top 10 and discovery; then smart rows, genres and the library. A saved layout keeps its own order.
- **No thin or repeated rows.** A row Noir fills by itself needs 3 titles (4 for a genre row). A discovery row shows titles not yet on the page before repeating one.
- **A title that is not on this PC** offers Stream now and Download where you meet it (the hero, the poster's hover actions), not only in the right-click menu.

## Downloads
- **Three tab cards**, one per job of the page: Queue (downloader), On this PC (organizer), Keep ready (automatic). Each card carries its own live line, so the numbers show whichever tab is open.
- **Tools are buttons, not menu items.** Organize files and Free up space sit on the On this PC toolbar; Clear finished shows on Queue while there is something to clear; Automatic rules opens from Keep ready.
- **Rows carry artwork**: a show's poster on its row, a film mark on the Movies group.

## History
- **The header carries the page's facts**: totals, weekly goal and where watches are synced sit in one line under the title, on every tab. Nothing that summarises the page goes below its content.
- **Import and Export are a pair** of header buttons; the three-dot menu holds the occasional tools (year story, compare taste, calendar file, shortcuts).

## Discover
- **A title you have just met can be acted on where it stands**: the spotlight and every poster's hover actions offer Stream, Download and My List; the preview panel has its actions right under the synopsis.
- **Discover shows what you do not have.** Titles already in the library keep their badge in the rows; there is no row of them.

## Phone
The phone follows the same four roles and the same decisions as the PC pages above; what changes is the shape.
- **Home**: the status strip is one sideways row of tiles under the hero (streaming setup while it is missing, downloads, the next episode, free space, Edit Home). The default row order is the PC's: your own rows first. Tile size (Small, Medium, Large) is in Edit Home. Keep ready is a row of artwork cards.
- **Downloads**: three tab cards across the top (Queue, On this phone, Keep ready), each with its live number; one part of the page shows at a time. The PC's queue and the download settings are two links under the tabs. There is no separate "Offline videos" page in the flow: it is the On this phone tab.
- **No hover on a phone**: the actions a PC poster shows on hover live in the title's sheet, with drawn icons, and one gold action pinned at the bottom.
- **One gold action per screen.** A title page has one gold Stream button; saving and downloading are quiet buttons; an episode row carries a stream icon and a download icon instead of full-width buttons. A download asks "this phone or your PC" only when a PC is paired.
- **History**: the viewing life (episodes, movies, hours) is one line under the heading and opens Stats. One "Import & export" chip opens the import, export and backup page.

## Vocabulary
| Thing | English | Arabic | Not |
|---|---|---|---|
| The next episode of a show you are watching (History tab and its section title, Home hero label, player card) | Up next | التالي | Watch next, Next |
| The Home row of things you started | Continue watching | متابعة المشاهدة | Continue Watching (capital W) |
| A title you track but have no file for, on PC | Not on this PC | ليس على هذا الحاسوب | Not on PC, Not on this PC yet, File unavailable, History kept |
| The same on the phone | Not on this phone | ليس على هذا الهاتف | Not on phone, File unavailable |
| Record one watch | Log | تسجيل المشاهدة | Add history, Add |
| Bring history from another service or file | Import | استيراد | Add history, Import history (as a button label) |
| The feature that keeps your next episodes downloaded (PC, phone, TV; the block, the sheet, the Settings group, the Home shelf button) | Keep ready | إبقاء الحلقات جاهزة | Noir Ready, Preparation centre, Ready downloads |
| Pause or resume it | Pause keep ready / Resume keep ready | إيقاف إبقاء الحلقات جاهزة مؤقتًا / استئناف إبقاء الحلقات جاهزة | Pause preparation, Resume preparation |
| Its storage limit | Keep ready budget | ميزانية إبقاء الحلقات جاهزة | Preparation budget |
| Its states | Keep ready paused / Keep ready budget reached / Keep ready check | إبقاء الحلقات جاهزة متوقف مؤقتًا / تم بلوغ ميزانية إبقاء الحلقات جاهزة / فحص إبقاء الحلقات جاهزة | Preparation paused, Preparation budget reached |
| A file Keep ready downloaded | Kept ready | محفوظ جاهزًا | Managed by Noir Ready |
| The list of what Keep ready did | Activity | النشاط | Why, Why log, Why · recent preparation activity |
| One button per state on a Keep ready file | Pin (normal), Unpin (pinned), Keep this file (scheduled for removal; pins it and cancels the removal) | تثبيت، إلغاء التثبيت، الاحتفاظ بهذا الملف | Pin file, Keep file, Cancel removal |

Sentence case for every label ("Continue watching", not "Continue Watching"). Counts use the count helpers (`Loc.Count` on PC, `Loc.f` plurals on the phone), never "1 ratings".

## Buttons
| Role | Look | Use |
|---|---|---|
| Primary | Gold fill `#E4C084`, dark text `#16120C` | The one main action of a header or card (Play, Stream, Download, Connect, Sync everything now). |
| Quiet | Surface `#1A1A1D`, 1 px edge `#33FFFFFF`, text `#F4F1EB` | Every other action. |
| Text | No fill, text `#C7C2BC`, gold on hover/focus | Links inside text, "See all", "More". |
| Choice | Pill: gold fill when chosen, `#222225` with text `#C7C2BC` otherwise, 8 px between pills (`PillOption` in `Noir/DesignSystem.cs`) | Pick one of a few: tabs, filters, Compact / Comfortable. Never joined rectangles. |
| Danger | Quiet look with text `#E5736B`; filled only inside the confirm step | Remove, delete, sign out. |

Surfaces `#0B0B0D` / `#141416` / `#161618` / `#1A1A1D`; edges `#26FFFFFF`–`#33FFFFFF`; text `#F4F1EB` / `#C7C2BC` / `#9F9A93`; radii 10–16 px. Every control has an accessible name and keyboard (PC) or D-pad focus (TV).

## Icons
- PC icons are stroked path geometry on a 24×24 grid (`Icon(geometry,size,color)` in `Noir/Shell.cs`, stroke 1.7, round joins and caps). New icons are added there as named constants.
- Android and TV use the same paths as vector drawables, so an icon looks the same on every device.
- A text character is never an icon. Arrows inside running text are fine.
