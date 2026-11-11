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
