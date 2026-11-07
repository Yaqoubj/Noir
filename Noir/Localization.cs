using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace Noir;
// English is the source language. Arabic replaces exact strings from this table and mirrors the
// library layout; anything not in the table stays English rather than showing a broken mix.
static class Loc
{
    public static bool Arabic {get;private set;}
    public static void Use(string language)=>Arabic=language=="ar";
    public static CultureInfo Culture=>Arabic?CultureInfo.GetCultureInfo("ar"):CultureInfo.GetCultureInfo("en-GB");
    public static string T(string english)=>Arabic&&english.Length>0&&Ar.TryGetValue(english,out var arabic)?arabic:english;
    // "3 seasons", "49 min left": numbers stay Western digits, as most Arabic streaming apps show them.
    // Arabic counts: 1 and 2 have their own words, 3–10 take the plural, 11+ the singular accusative.
    public static string Count(int n,string one,string many,string arOne,string arTwo,string arFew,string arMany)=>!Arabic?(n==1?"1 "+one:n+" "+many):n==1?arOne:n==2?arTwo:n is >=3 and <=10?n+" "+arFew:n+" "+arMany;
    public static string MinutesLeft(int minutes)=>Arabic?$"متبقٍ {minutes} د":$"{minutes} min left";
    public static string Runtime(double seconds)=>seconds<3600?(Arabic?$"{(int)(seconds/60)} د":$"{(int)(seconds/60)}m"):Arabic?$"{(int)(seconds/3600)} س {(int)(seconds%3600/60)} د":$"{(int)(seconds/3600)}h {(int)(seconds%3600/60)}m";
    public static bool Latin(string text)=>text.Length>0&&!text.Any(c=>c>='؀'&&c<='ۿ');
    static readonly Dictionary<string,string> Ar=new()
    {
        ["Size"]="الحجم",
        ["Compact"]="مضغوط",
        ["Comfortable"]="مريح",
        ["Compact fits more on screen. Comfortable gives everything more room. Applies only to this device."]="يعرض الحجم المضغوط المزيد على الشاشة، ويمنح الحجم المريح العناصر مساحة أوسع. يسري على هذا الجهاز فقط.",
        ["Stremio subtitle addon link · optional"]="رابط إضافة ترجمة Stremio · اختياري",
        ["Use only this source"]="استخدم هذا المصدر فقط",
        ["Checking subtitle addon…"]="جارٍ التحقق من إضافة الترجمة…",
        ["Subtitle source changed. Check again."]="تغير مصدر الترجمة. تحقق مجددًا.",
        ["Default subtitle source"]="مصدر الترجمة الافتراضي",
        ["Connected:"]="متصل:",
        ["Check link"]="تحقق من الرابط",
        ["Save subtitle source"]="حفظ مصدر الترجمة",
        ["Subtitle source saved securely."]="تم حفظ مصدر الترجمة بأمان.",
        ["Couldn't save subtitle source."]="تعذر حفظ مصدر الترجمة.",
        ["Paste an HTTPS Stremio subtitle addon link without credentials or a custom port."]="الصق رابط HTTPS لإضافة ترجمة Stremio دون بيانات دخول أو منفذ مخصص.",
        ["Paste a subtitle addon link first."]="الصق رابط إضافة الترجمة أولًا.",
        ["This addon does not provide subtitles."]="هذه الإضافة لا توفر ترجمة.",
        ["Subtitle addon manifest is too large."]="ملف تعريف إضافة الترجمة كبير جدًا.",
        ["Couldn't check the subtitle addon. Check the link and connection."]="تعذر التحقق من إضافة الترجمة. تحقق من الرابط والاتصال.",
        ["The subtitle addon returned an invalid manifest."]="أعادت إضافة الترجمة ملف تعريف غير صالح.",
        ["Subtitle addon check timed out. Try again."]="انتهت مهلة التحقق من إضافة الترجمة. حاول مجددًا.",
        ["Subtitle addon"]="إضافة ترجمة",
        ["Subtitle source"]="مصدر الترجمة",
        // Navigation and frame
        ["Home"]="الرئيسية",["Discover"]="اكتشف",["Shows"]="مسلسلات",["Movies"]="أفلام",["My List"]="قائمتي",["History"]="السجل",["Upcoming"]="التقويم",
        ["Downloads"]="التنزيلات",["Settings"]="الإعدادات",["Scan library for new files"]="البحث عن ملفات جديدة",["Show or hide labels"]="إظهار القائمة أو إخفاؤها",
        ["Search movies & shows"]="ابحث عن أفلام ومسلسلات",["Search your library"]="ابحث في مكتبتك",["Search new movies & shows"]="ابحث عن أفلام ومسلسلات جديدة",["Find new movies and shows online"]="ابحث عن أفلام ومسلسلات جديدة على الإنترنت",
        ["Language"]="اللغة",["Phone paired"]="الهاتف متصل",["Tracker"]="المتتبع",["Your library"]="مكتبتك",
        ["YOUR LIBRARY"]="مكتبتك",["SEARCH"]="بحث",["FIND YOUR NEXT WATCH"]="اعثر على مشاهدتك التالية",["SAVED TO YOUR PC"]="محفوظ على جهازك",["PREFERENCES"]="التفضيلات",["YOUR VIEWING LIFE"]="مسيرتك في المشاهدة",["SAVED FOR LATER"]="محفوظ لوقت لاحق",
        ["Your evening, queued."]="أمسيتك، جاهزة.",
        // Hero
        ["CONTINUE WATCHING"]="متابعة المشاهدة",["UP NEXT"]="التالي",["TONIGHT'S PICK"]="اختيار الليلة",["YOU JUST WATCHED"]="شاهدته للتو",["YOU FINISHED THIS SHOW"]="أنهيت هذا المسلسل",["UP NEXT · NOT ON THIS PC YET"]="التالي · ليس على جهازك بعد",
        ["▶   Play"]="▶   تشغيل",["▶   Resume"]="▶   استئناف",["ⓘ   More info"]="ⓘ   مزيد من المعلومات",["↺   Start over"]="↺   من البداية",["↓  Get it on this PC"]="↓  نزّله على جهازك",["Downloading…"]="جارٍ التنزيل…",
        ["★  Rate it"]="★  قيّمه",["★  Your rating"]="★  تقييمك",["↻  Watch again"]="↻  شاهده مجددًا",["Add to My List"]="إضافة إلى قائمتي",["In My List"]="في قائمتي",["＋  Add to My List"]="＋  إضافة إلى قائمتي",["✓  In My List"]="✓  في قائمتي",
        ["Rate & note"]="التقييم والملاحظة",["Reviews & comments"]="المراجعات والتعليقات",["In My List · click to remove"]="في قائمتي · انقر للإزالة",["Year"]="السنة",["Runtime"]="مدة العرض",["More info"]="مزيد من المعلومات",
        // Home rows
        ["Continue Watching"]="متابعة المشاهدة",["Resume where you stopped or play the next episode."]="تابع من حيث توقفت أو شغّل الحلقة التالية.",
        ["Resume, play the next episode, or get it on this PC in one click."]="استأنف، أو شغّل الحلقة التالية، أو نزّلها على جهازك بنقرة واحدة.",
        ["Start a movie or episode. It will appear here when you return."]="ابدأ فيلمًا أو حلقة وسيظهر هنا عند عودتك.",
        ["Recently watched"]="شوهد مؤخرًا",["Recently added"]="أضيف مؤخرًا",["Genres"]="الأنواع",["Collections"]="المجموعات",["Because you watched…"]="لأنك شاهدت…",["Your shows"]="مسلسلاتك",["Your movies"]="أفلامك",["Your play queue"]="قائمة التشغيل",["See all"]="عرض الكل",
        ["Top 10 this week"]="أفضل 10 هذا الأسبوع",["Your services"]="خدماتك",["Connected to Noir. Click one to manage it."]="متصلة بـ Noir. انقر على أي منها لإدارتها.",["Most popular on Cinemeta right now."]="الأكثر شيوعًا على Cinemeta الآن.",
        ["Coming up"]="قريبًا",["Previous"]="السابق",["More"]="المزيد",["Remove from Continue Watching"]="إزالة من متابعة المشاهدة",
        ["Up next · Ready to play"]="التالي · جاهز للتشغيل",["You're caught up"]="أنت على اطلاع",["NEW EPISODE"]="حلقة جديدة",["NEW EPISODE SOON"]="حلقة جديدة قريبًا",["DOWNLOADING"]="جارٍ التنزيل",["↓  GET IT ON THIS PC"]="↓  نزّله على جهازك",
        ["Movie"]="فيلم",["Play"]="تشغيل",["Details & episodes"]="التفاصيل والحلقات",["Not on PC"]="ليس على الجهاز",["✓ Watched"]="✓ شوهد",["Your cinema starts here"]="سينماك تبدأ من هنا",["Add your movie and show folders in Settings."]="أضف مجلدات الأفلام والمسلسلات من الإعدادات.",["Add folders"]="إضافة مجلدات",
        ["No matches"]="لا نتائج",["Try another title."]="جرّب عنوانًا آخر.",["Your next watch"]="مشاهدتك التالية",["Use + My List on any title to keep it here."]="استخدم + قائمتي على أي عنوان لحفظه هنا.",
        // Filters
        ["All genres"]="كل الأنواع",["A–Z"]="أ–ي",["+ Create collection"]="+ إنشاء مجموعة",
        // Tracker tabs
        ["Up next"]="التالي",["Diary"]="اليوميات",["Stats"]="الإحصاءات",["Add history"]="إضافة سجل",
        // Settings sections
        ["Playback"]="التشغيل",["Library"]="المكتبة",["Phone Sync"]="مزامنة الهاتف",["Community"]="المجتمع",["Advanced"]="متقدم",["English"]="English",["العربية"]="العربية",
        ["Choose the app language. Arabic mirrors the layout right to left."]="اختر لغة التطبيق. العربية تعكس الواجهة من اليمين إلى اليسار.",
        ["Profile"]="الملف الشخصي",["Library & folders"]="المكتبة والمجلدات",["Connections"]="الاتصالات",["Phone"]="الهاتف",["Appearance & language"]="المظهر واللغة",["Backup"]="النسخ الاحتياطي",
        ["You and how you use Noir"]="أنت وطريقة استخدامك لـ Noir",["Auto-next, intros, subtitles"]="التشغيل التلقائي، المقدمات، الترجمة",["Rate, react and keep notes"]="قيّم وتفاعل واحتفظ بملاحظاتك",["Folders, artwork, organizing"]="المجلدات، الصور، التنظيم",
        ["Trakt, Nuvio, Stremio, servers"]="Trakt وNuvio وStremio والخوادم",["Sources, quality, folders"]="المصادر، الجودة، المجلدات",["Pair and sync Android"]="اقتران ومزامنة أندرويد",
        ["Language, side rail, spoilers"]="اللغة، الشريط الجانبي، الحرق",["Back up, restore, export"]="نسخ احتياطي، استعادة، تصدير",["Background, shortcuts, about"]="الخلفية، الاختصارات، حول",
        ["Language"]="اللغة",["Side rail"]="الشريط الجانبي",["Automatic"]="تلقائي",["Labels"]="مع الأسماء",["Icons only"]="أيقونات فقط",["Spoilers"]="الحرق",
        ["Labels beside the icons, or icons only. Automatic folds to icons in narrow windows."]="أسماء بجانب الأيقونات أو أيقونات فقط. الوضع التلقائي يكتفي بالأيقونات في النوافذ الضيقة.",
        ["Hide descriptions of episodes I haven't finished"]="إخفاء أوصاف الحلقات التي لم أكملها",
        ["titles"]="عنوان",["watched"]="مشاهدة",["Tracker only"]="المتابعة فقط",["Library & tracker"]="المكتبة والمتابعة",["Your stats"]="إحصاءاتك",["Your diary"]="يومياتك",
        ["connected"]="متصل",["waiting to sync"]="بانتظار المزامنة",["everything is up to date"]="كل شيء محدّث",["⟳  Sync everything now"]="⟳  زامن الكل الآن",["Waiting to sync"]="بانتظار المزامنة",
        ["Sync your watches"]="زامن مشاهداتك",["Kept in step automatically while Noir runs. Undo any import from Backup."]="تبقى متطابقة تلقائيًا أثناء تشغيل Noir. يمكنك التراجع عن أي استيراد من النسخ الاحتياطي.",
        ["Media servers"]="خوادم الوسائط",["Download original files and import what you've watched. Read-only: nothing on the server changes."]="نزّل الملفات الأصلية واستورد ما شاهدته. للقراءة فقط: لا يتغير شيء على الخادم.",
        ["Import history"]="استيراد السجل",["Bring in what you watched elsewhere. You review everything before it's added, and re-importing never duplicates."]="أحضر ما شاهدته في مكان آخر. تراجع كل شيء قبل إضافته، وإعادة الاستيراد لا تكرر شيئًا.",
        ["Downloads & discovery"]="التنزيلات والاكتشاف",["Where Noir finds releases, and what powers recommendations."]="من أين يجد Noir الإصدارات، وما الذي يشغّل الاقتراحات.",
        ["Connected"]="متصل",["Not connected"]="غير متصل",["Off"]="متوقف",["Sending"]="يرسل",["confirmed"]="مؤكد",["last"]="آخرها",["checked"]="فُحص",["not checked yet"]="لم يُفحص بعد",["in use for downloads"]="مستخدم للتنزيلات",
        ["Sign in"]="تسجيل الدخول",["Sync now"]="زامن الآن",["Send earlier history"]="أرسل السجل السابق",["Import Trakt export…"]="استيراد ملف Trakt…",["Review & import"]="مراجعة واستيراد",["Disconnect"]="قطع الاتصال",
        ["Connect Stremio"]="ربط Stremio",["Connect Nuvio"]="ربط Nuvio",["Switch profile"]="تبديل الملف",["Import watched"]="استيراد المشاهَد",["Test"]="اختبار",["Remove"]="إزالة",["Manage"]="إدارة",["Set up"]="إعداد",
        ["Choose file…"]="اختيار ملف…",["Add a media server"]="إضافة خادم وسائط",["Add Jellyfin / Emby"]="إضافة Jellyfin / Emby",["Connect Plex"]="ربط Plex",["waiting"]="بالانتظار",["tries"]="محاولات",["and"]="و",
        ["Don't send this one"]="لا ترسل هذا",["Check Stremio automatically"]="افحص Stremio تلقائيًا",["Check Nuvio automatically"]="افحص Nuvio تلقائيًا",["Mark my Noir watches in Nuvio"]="سجّل مشاهداتي في Nuvio",
        ["Stream in Nuvio"]="شاهد في Nuvio",["▶  Stream in Nuvio"]="▶  شاهد في Nuvio",["▶  Stream in Stremio"]="▶  شاهد في Stremio",["Open in Nuvio on your phone"]="افتح في Nuvio على هاتفك",["Get Nuvio"]="احصل على Nuvio",["Close"]="إغلاق",
        ["Scan with your phone's camera to open this title in Nuvio. Install Nuvio Desktop to open titles on this PC directly."]="امسح الرمز بكاميرا هاتفك لفتح هذا العنوان في Nuvio. ثبّت Nuvio لسطح المكتب لفتح العناوين على هذا الجهاز مباشرة.",
        ["Fix match"]="تصحيح المطابقة",["Refresh information"]="تحديث المعلومات",["Retry now"]="أعد المحاولة الآن",["reasons"]="أسباب",["retrying"]="إعادة المحاولة",["Open on "]="افتح على ",
        ["Correct match / episode numbers"]="تصحيح المطابقة / أرقام الحلقات",["Mark my watches on "]="سجّل مشاهداتي على ",["Syncing everything…"]="جارٍ مزامنة الكل…",
        // Library grid and Discover
        ["Sort"]="الترتيب",["Genre"]="النوع",["Watching"]="أشاهده",["Not started"]="لم يبدأ",["Finished"]="انتهى",["On this PC"]="على هذا الجهاز",["Everything"]="الكل",["of"]="من",["Jump to"]="انتقل إلى",
        ["Show more"]="عرض المزيد",["remaining"]="متبقية",["episodes"]="حلقة",["Not on this PC"]="ليس على هذا الجهاز",["Resume"]="استئناف",["Clear filters"]="مسح الفلاتر",["Nothing matches"]="لا شيء مطابق",["Try another filter."]="جرّب فلترًا آخر.",
        ["popular now"]="الرائج الآن",["popular movies"]="أفلام رائجة",["popular shows"]="مسلسلات رائجة",["Popular shows"]="مسلسلات رائجة",["Popular movies"]="أفلام رائجة",["Already in your library"]="موجود في مكتبتك",["All"]="الكل",["TV shows"]="مسلسلات",
        // Profiles
        ["Profiles"]="الملفات الشخصية",["Who's watching?"]="من يشاهد؟",["Watching now"]="يشاهد الآن",["Switch to"]="التبديل إلى",["+ Add profile"]="+ إضافة ملف",["Switch"]="تبديل",["Edit"]="تعديل",["Delete…"]="حذف…",["Delete"]="حذف",
        ["New profile"]="ملف جديد",["Starts empty"]="يبدأ فارغًا",["Name"]="الاسم",["Create"]="إنشاء",["Cancel"]="إلغاء",["Save"]="حفظ",["Remove PIN"]="إزالة الرمز",["Unlock"]="فتح",["Switch profile"]="تبديل الملف",
        ["Ask who's watching when Noir starts"]="اسأل من يشاهد عند تشغيل Noir",["last used"]="آخر استخدام",["Not used yet"]="لم يُستخدم بعد",["Enter the PIN for"]="أدخل رمز",["That PIN isn't right."]="الرمز غير صحيح.",
        ["Profile saved."]="تم حفظ الملف.",["Profile created. Switch to it whenever you like."]="تم إنشاء الملف. بدّل إليه متى شئت.",["Give the profile a name."]="أعطِ الملف اسمًا.",
        ["Each profile is completely separate: its own library folders, history, settings, downloads and connections. Only one runs at a time."]="كل ملف منفصل تمامًا: مجلداته وسجله وإعداداته وتنزيلاته واتصالاته. يعمل ملف واحد فقط في كل مرة.",
        // Streaming
        ["▶   Stream"]="▶   بث",["▶   Resume stream"]="▶   استئناف البث",["↓  Download"]="↓  تنزيل",["▶ Stream"]="▶ بث",["▶  STREAM"]="▶  بث",["▶  Stream"]="▶  بث",["▶   Stream"]="▶   بث",["↓   Download"]="↓   تنزيل",
        ["Stream now"]="بث الآن",["Choose a stream…"]="اختر بثًا…",["Choose a stream"]="اختر بثًا",["▶ Stream next"]="▶ بث التالي",["Download to this PC"]="تنزيل على هذا الجهاز",
        ["Export"]="تصدير",["How you use Noir"]="كيف تستخدم Noir",["Running in the background"]="التشغيل في الخلفية",["Keyboard shortcuts"]="اختصارات لوحة المفاتيح",
        // Genres (Cinemeta / TVmaze names)
        ["Action"]="أكشن",["Adventure"]="مغامرة",["Animation"]="رسوم متحركة",["Anime"]="أنمي",["Biography"]="سيرة ذاتية",["Comedy"]="كوميديا",["Crime"]="جريمة",["Documentary"]="وثائقي",["Drama"]="دراما",
        ["Family"]="عائلي",["Fantasy"]="فانتازيا",["Horror"]="رعب",["Music"]="موسيقى",["Musical"]="موسيقي",["Mystery"]="غموض",["Romance"]="رومانسي",["Sci-Fi"]="خيال علمي",["Science-Fiction"]="خيال علمي",
        ["Sport"]="رياضة",["Sports"]="رياضة",["Thriller"]="إثارة",["War"]="حرب",["Western"]="غربي",["Espionage"]="تجسس",["Legal"]="قانوني",["Medical"]="طبي",["Supernatural"]="خارق للطبيعة",["Reality-TV"]="تلفزيون الواقع",["Talk-Show"]="برامج حوارية",["Game-Show"]="برامج مسابقات",["Short"]="قصير",["Film-Noir"]="فيلم نوار",
    };
}
public partial class MainWindow
{
    static string T(string english)=>Loc.T(english);
    // Latin-only lines (episode labels, English summaries) keep their own word order in an Arabic
    // layout and still sit on the right edge.
    static TextBlock Bidi(TextBlock text){if(Loc.Arabic&&Loc.Latin(text.Text)){text.FlowDirection=FlowDirection.LeftToRight;text.TextAlignment=TextAlignment.Right;}return text;}
    // Applies the saved language: mirrors the library (the player keeps its own layout) and rebuilds the frame text.
    void ApplyLanguage()
    {
        Loc.Use(store.State.Language);
        LibraryRoot.FlowDirection=Loc.Arabic?FlowDirection.RightToLeft:FlowDirection.LeftToRight;
        LibraryRoot.Language=System.Windows.Markup.XmlLanguage.GetLanguage(Loc.Arabic?"ar":"en-GB");
        // Western digits everywhere, matching the numbers that come from titles and services.
        System.Windows.Media.NumberSubstitution.SetSubstitution(LibraryRoot,System.Windows.Media.NumberSubstitutionMethod.European);
        ScanNav.ToolTip=T("Scan library for new files");RailToggle.ToolTip=T("Show or hide labels");RailLogo.ToolTip=T("Home");HelpNav.ToolTip=T("Shortcuts & tips (?)","الاختصارات والنصائح (?)");
        BuildSideRail();
    }
    void SetLanguage(string language)
    {
        if(store.State.Language==language)return;
        store.State.Language=language;store.Save();ApplyLanguage();renderedContext="";Render();
    }
}
