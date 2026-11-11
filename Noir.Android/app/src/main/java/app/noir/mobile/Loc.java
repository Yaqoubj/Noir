package app.noir.mobile;
import android.app.Activity;
import android.view.View;
import java.util.*;

// Arabic/English, matching the PC: exact-string lookup, so anything missing stays English.
// The shared table is generated from the PC (LocTable); phone-only strings are below.
final class Loc {
 static volatile boolean arabic;
 static final Map<String,String> AR=new HashMap<>();
 static{
  LocTable.fill(AR);
  String[][] phone={
   {"Photo saving permission was not enabled","لم يتم تفعيل إذن حفظ الصور"},
   {"Curious cinephile","عاشق الحكايات الفضولي"},
   {"Cinema explorer","مستكشف السينما"},{"Series storyteller","راوي حكايات المسلسلات"},{"Curious viewer","مشاهد فضولي"},{"Previous story","الحكاية السابقة"},{"Next story","الحكاية التالية"},
   {"Four favorites","أربعة مفضّلة"},{"+ Pick a favorite","+ اختر عملًا مفضّلًا"},{"Pick a favorite","اختر عملًا مفضّلًا"},{"Hold to replace or remove","اضغط مطولًا للاستبدال أو الإزالة"},{"Replace favorite","استبدال المفضّل"},{"Remove favorite","إزالة المفضّل"},{"Search your library and history","ابحث في مكتبتك وسجلّك"},
   {"♥ Like this title","♥ أحب هذا العمل"},{"Like this title","أحب هذا العمل"},{"Note · optional","ملاحظة · اختياري"},{"Rating · optional","تقييم · اختياري"},{"☆ Quick rate","☆ تقييم سريع"},{"Seasons & moods","المواسم والمشاعر"},{"Mood map","خريطة المشاعر"},{"loved","أحببته"},{"funny","مضحك"},{"wow","مذهل"},{"sad","حزين"},{"meh","عادي"},
   {"Your %d story ›","حكايتك في %d ›"},{"Save story card","حفظ بطاقة الحكاية"},{"Save card","حفظ البطاقة"},{"Tap or swipe to explore","اضغط أو اسحب للاستكشاف"},{"Your story is taking shape…","تتشكّل حكايتك…"},{"%.1f h","%.1f س"},{"%d viewings this year","%d مشاهدة هذا العام"},{"%d days","%d يومًا"},
   {"Your top show","مسلسلك الأول"},{"Your top movie","فيلمك الأول"},{"Your busiest month","أكثر شهورك مشاهدةً"},{"Your longest streak","أطول سلسلة مشاهدة"},{"Your favorite genre","نوعك المفضّل"},{"Your top rated","أعلى تقييماتك"},{"Your viewing identity","هويتك في المشاهدة"},{"To be continued","للحكاية بقية"},{"This was your %d","هكذا كان عامك %d"},{"Your next favorite awaits","عملك المفضّل القادم ينتظرك"},
   {"One more episode became a story.","حلقة أخرى صارت حكاية."},{"A little movie magic.","قليل من سحر السينما."},{"A month worth remembering.","شهر يستحق التذكّر."},{"Little moments, beautifully connected.","لحظات صغيرة تتصل بجمال."},{"The worlds you returned to.","العوالم التي عدت إليها."},{"Every story has its own rhythm.","لكل حكاية إيقاعها."},{"Watch anywhere. Keep your story here.","شاهد أينما شئت. واحتفظ بحكايتك هنا."},{"YOUR VIEWING LIFE","رحلتك في المشاهدة"},
   {"A new chapter","فصل جديد"},{"Night-owl binger","رفيق الحلقات الليلية"},{"Weekend cinephile","عاشق سينما العطلة"},{"Curious explorer","مستكشف فضولي"},{"Everyday storyteller","راوي الحكايات اليومية"},{"Saved to Pictures/Noir","تم الحفظ في الصور/Noir"},{"Couldn't save this card","تعذّر حفظ هذه البطاقة"},{"Story card saved","تم حفظ بطاقة الحكاية"},
   {"Goal paused","الهدف متوقف مؤقتًا"},{"Pause goal","إيقاف الهدف مؤقتًا"},{"Resume goal","استئناف الهدف"},{"On pace for %d this year","بوتيرتك الحالية: %d هذا العام"},{"Save a milestone card ›","احفظ بطاقة إنجاز ›"},{"Day streak","سلسلة أيام"},{"Weekly goal","الهدف الأسبوعي"},{"By decade","حسب العقد"},{"By language","حسب اللغة"},{"By people","حسب صانعي الأعمال"},{"NOIR · UP NEXT","نوار · التالي"},{"All caught up","شاهدت كل المتاح"},{"Open Noir to find your next story","افتح نوار لتجد حكايتك التالية"},
   {"Stremio subtitle addon link · optional","رابط إضافة ترجمة Stremio · اختياري"},
   {"Use only this source","استخدم هذا المصدر فقط"},
   {"Checking subtitle addon…","جارٍ التحقق من إضافة الترجمة…"},
   {"Subtitle source changed. Check again.","تغير مصدر الترجمة. تحقق مجددًا."},
   {"Default subtitle source","مصدر الترجمة الافتراضي"},
   {"Connected:","متصل:"},
   {"Check link","تحقق من الرابط"},
   {"Save subtitle source","حفظ مصدر الترجمة"},
   {"Subtitle source saved securely.","تم حفظ مصدر الترجمة بأمان."},
   {"Couldn't save subtitle source.","تعذر حفظ مصدر الترجمة."},
   {"Paste an HTTPS Stremio subtitle addon link without credentials or a custom port.","الصق رابط HTTPS لإضافة ترجمة Stremio دون بيانات دخول أو منفذ مخصص."},
   {"Paste a subtitle addon link first.","الصق رابط إضافة الترجمة أولًا."},
   {"This addon does not provide subtitles.","هذه الإضافة لا توفر ترجمة."},
   {"Subtitle addon manifest is too large.","ملف تعريف إضافة الترجمة كبير جدًا."},
   {"Couldn't check the subtitle addon. Check the link and connection.","تعذر التحقق من إضافة الترجمة. تحقق من الرابط والاتصال."},
   {"The subtitle addon returned an invalid manifest.","أعادت إضافة الترجمة ملف تعريف غير صالح."},
   {"Subtitle addon check timed out. Try again.","انتهت مهلة التحقق من إضافة الترجمة. حاول مجددًا."},
   {"Subtitle addon","إضافة ترجمة"},
   {"Subtitle source","مصدر الترجمة"},

   {"Library","المكتبة"},{"More","المزيد"},{"See all ›","عرض الكل ›"},{"Search movies and shows","ابحث عن أفلام ومسلسلات"},
   {"Episodes","الحلقات"},{"Details","التفاصيل"},{"+ My List","+ قائمتي"},{"✓ My List","✓ قائمتي"},{"‹ Back","‹ رجوع"},{"▶ Play","▶ تشغيل"},
   {"Resume or play the next episode.","استأنف أو شغّل الحلقة التالية."},{"Resume, play next, or send the next episode to your PC.","استأنف، أو شغّل التالي، أو أرسل الحلقة التالية إلى جهازك."},
   {"Latest","الأحدث"},{"Out now","متاح الآن"},{"Caught up · airing soon","أنهيتها · تُعرض قريبًا"},{"Not started yet","لم تبدأ بعد"},
   {"Still watching these?","هل ما زلت تشاهد هذه؟"},{"Keep","إبقاء"},{"Stop tracking","إيقاف المتابعة"},{"Today","اليوم"},{"Tomorrow","غدًا"},{"Tonight","الليلة"},
   {"＋ Log what you watched","＋ سجّل ما شاهدته"},{"⌕ Discover","⌕ اكتشف"},{"⇅ Import · export · backup","⇅ استيراد · تصدير · نسخ احتياطي"},{"⚡ Quick add","⚡ إضافة سريعة"},{"☰ Paste a list","☰ الصق قائمة"},
   {"Your top shows","مسلسلاتك المفضلة"},{"Your genres","أنواعك"},{"When you watch","متى تشاهد"},{"Milestones","الإنجازات"},{"TIME WATCHED","وقت المشاهدة"},
   {"Connections","الاتصالات"},{"Your PC","جهازك"},{"Paired","متصل"},{"Not paired","غير متصل"},{"Pair with my PC","اقتران بجهازي"},{"Sync now","زامن الآن"},
   {"Phone downloads","تنزيلات الهاتف"},{"Open","فتح"},{"Set up","إعداد"},{"Manage","إدارة"},{"Language","اللغة"},{"Settings","الإعدادات"},
   {"Quick add","إضافة سريعة"},{"Paste a list","الصق قائمة"},{"Match titles","طابق العناوين"},{"Add","إضافة"},{"Added","أضيف"},{"Matching…","جارٍ المطابقة…"},
   {"Tap what you've watched. Shows ask how far you got.","اضغط على ما شاهدته. المسلسلات تسألك إلى أين وصلت."},
   {"One title per line. Add a year to pick the right one, and S3 or S3E5 for how far you got.","عنوان في كل سطر. أضف السنة لاختيار العنوان الصحيح، وS3 أو S3E5 لمدى تقدمك."},
   {"Profile","الملف الشخصي"},{"This phone follows the profile open on your PC.","يتبع هذا الهاتف الملف المفتوح على جهازك."},
   {"TOP 10","أفضل 10"},{"✎ Manage","✎ إدارة"},{"Date unknown","تاريخ غير معروف"},{"Yesterday","أمس"},{"Your collections","مجموعاتك"},{"Rename","إعادة تسمية"},{"Delete the collection \"%s\"? Titles stay in your library.","حذف المجموعة \"%s\"؟ تبقى العناوين في مكتبتك."},{"Details · hold to edit date or delete","التفاصيل · اضغط مطولًا لتعديل التاريخ أو الحذف"},{"Only on this phone · tap to switch or add profiles","على هذا الهاتف فقط · اضغط للتبديل أو إضافة ملفات"},{"Linked to %s on your PC · tap to manage","مرتبط بـ %s على جهازك · اضغط للإدارة"},{"min left","د متبقية"},
   {"Stream","بث"},{"▶ Stream","▶ بث"},{"▶ Stream now","▶ بث الآن"},{"▶  STREAM","▶  بث"},{"Choose a stream…","اختر بثًا…"},{"Finding a stream…","جارٍ البحث عن بث…"},
   {"Add your AIOStreams link in phone Download settings to stream.","أضف رابط AIOStreams في إعدادات التنزيل على الهاتف للبث."},{"AIOStreams has no playable stream for this right now.","لا يوجد بث متاح لهذا الآن على AIOStreams."},
  };
  for(String[] p:phone)AR.put(p[0],p[1]);
 }
 static String t(String english){if(!arabic||english==null||english.isEmpty())return english;String v=AR.get(english);return v==null?english:v;}
 static String f(String pattern,Object... values){return String.format(Locale.getDefault(),t(pattern),values);}
 static CharSequence t(CharSequence s){return s instanceof String?t((String)s):s;}
 static void use(DataStore data){String chosen=data.prefs().optString("language","");arabic=chosen.isEmpty()?"ar".equals(Locale.getDefault().getLanguage()):chosen.equals("ar");}
 static void apply(Activity activity){activity.getWindow().getDecorView().setLayoutDirection(arabic?View.LAYOUT_DIRECTION_RTL:View.LAYOUT_DIRECTION_LTR);}
 /** Count + noun with the Arabic plural forms (one/two/3-10/11+): English uses `one`/`many` patterns with %d; Arabic reads keys "many|one", "many|two", "many|few", "many|many". */
 static String plural(long n,String one,String many){if(!arabic){return String.format(Locale.getDefault(),n==1?one:many,n);}String cat=n==1?"one":n==2?"two":n>=3&&n<=10?"few":"many";String v=AR.get(many+"|"+cat);return String.format(Locale.getDefault(),v!=null?v:t(n==1?one:many),n);}
 static void plural(String many,String one,String two,String few,String all){AR.put(many+"|one",one);AR.put(many+"|two",two);AR.put(many+"|few",few);AR.put(many+"|many",all);}
 static String minutesLeft(int minutes){return arabic?"متبقٍ "+minutes+" د":minutes+" min left";}
}
