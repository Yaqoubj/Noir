package app.noir.mobile;
import android.app.*;
import android.content.*;
import android.graphics.*;
import android.graphics.drawable.GradientDrawable;
import android.view.*;
import android.widget.*;
import org.json.JSONObject;
import java.util.*;

// Feature discovery without nagging: at most one dismissible tip on Home at the moment a feature becomes useful
// (never two on the same day, each gone for good once answered), and "Tips & shortcuts" in More listing everything else.
final class PhoneTips {
 static{String[][] w={{"TIP","نصيحة"},{"Not now","ليس الآن"},{"Got it","فهمت"},{"Set up","إعداد"},{"Add widget","أضف الأداة"},
  {"Watch offline without thinking about it","شاهد دون اتصال دون أن تفكر في ذلك"},{"Keep ready downloads the next episodes of the shows you're watching overnight, on Wi-Fi while charging.","تنزّل ميزة «جاهز للمشاهدة» الحلقات التالية من المسلسلات التي تتابعها ليلًا، عبر Wi-Fi أثناء الشحن."},
  {"Your next episode on the home screen","حلقتك التالية على الشاشة الرئيسية"},{"The Next Up widget shows what to watch next and opens it in one tap.","تعرض أداة «التالي» ما ستشاهده بعد ذلك وتفتحه بلمسة واحدة."},
  {"Pull down to sync","اسحب للأسفل للمزامنة"},{"Pull down on any page, or tap the status at the top, to sync with your PC right away.","اسحب للأسفل في أي صفحة، أو اضغط الحالة في الأعلى، للمزامنة مع الكمبيوتر فورًا."},
  {"Tips & shortcuts","نصائح واختصارات"},{"Everything Noir can do, in one place","كل ما يستطيعه Noir في مكان واحد"},{"Close","إغلاق"},
  {"⋯ on any card or row","⋯ على أي بطاقة أو صف"},{"Every action for that episode: mark watched, play queue, downloads, rating, correct match.","كل إجراءات الحلقة: تعليم كمشاهدة، قائمة التشغيل، التنزيلات، التقييم، تصحيح المطابقة."},
  {"Double-tap the video","اضغط مرتين على الفيديو"},{"Left side skips back 10 s, right side skips ahead 10 s. One tap shows or hides the controls.","الجانب الأيسر يرجع 10 ث والأيمن يتقدم 10 ث. لمسة واحدة تُظهر عناصر التحكم أو تخفيها."},
  {"Keep ready","جاهز للمشاهدة"},{"Next episodes download overnight on Wi-Fi while charging, ready to watch offline.","تُنزَّل الحلقات التالية ليلًا عبر Wi-Fi أثناء الشحن، جاهزة للمشاهدة دون اتصال."},
  {"Catch me up","ذكّرني بالأحداث"},{"A short recap of where you stopped when you return to a show after a break.","ملخص قصير لما توقفت عنده عند عودتك إلى مسلسل بعد انقطاع."},
  {"Smart lists","القوائم الذكية"},{"Unfinished business, comfort rewatches, directors you love and short picks for tonight, in Library.","حكايات لم تكتمل، وإعادة لأعمال تحبها، ومخرجون تحبهم، واختيارات قصيرة الليلة، في المكتبة."},
  {"Compare taste","قارن الذوق"},{"Swap a code with a friend and see the titles you both love.","تبادل رمزًا مع صديق وشاهد الأعمال التي تحبانها معًا."},
  {"Next Up widget","أداة «التالي»"},{"Your next episode on the home screen.","حلقتك التالية على الشاشة الرئيسية."},
  {"Edit Home","تعديل الرئيسية"},{"Reorder the rows and pin genres or collections.","أعد ترتيب الصفوف وثبّت الأنواع أو المجموعات."},
  {"New-episode alerts","تنبيهات الحلقات الجديدة"},{"A notification when a new episode of a show you follow is out.","إشعار عند صدور حلقة جديدة من مسلسل تتابعه."},
  {"On · a notification when a show you follow airs","مفعّلة · إشعار عند عرض مسلسل تتابعه"},{"Off · no notifications for new episodes","متوقفة · لا إشعارات للحلقات الجديدة"},
  {"Pin your favorites","ثبّت أعمالك المفضّلة"},{"Hold a favorite in History to replace or remove it.","اضغط مطولًا على عمل مفضّل في السجل لاستبداله أو إزالته."},
  {"A short recap when you come back to a show","ملخص قصير عند عودتك إلى مسلسل"},{"Swap a code with a friend and see what you both love","تبادل رمزًا مع صديق وشاهد ما تحبانه معًا"},
  {"More options for %s","مزيد من الخيارات لـ %s"},{"More options","مزيد من الخيارات"},{"Previews","المعاينات"},{"Plays when you rest on a title page","تعمل عند التوقف على صفحة عمل"},{"Default language and where subtitles come from","اللغة الافتراضية ومصدر الترجمة"},{"✎ Edit date or delete","✎ تعديل التاريخ أو الحذف"},{"Hold a favorite to replace or remove it","اضغط مطولًا على المفضّل لاستبداله أو إزالته"},{"Sync now","زامن الآن"},
  {"Tip: double-tap left or right to skip 10 s","نصيحة: اضغط مرتين يسارًا للرجوع أو يمينًا للتقديم 10 ث"},
  {"Keep ready is off on this phone. Saving with this show switched on turns it on (overnight, Wi-Fi, charging).","ميزة «جاهز للمشاهدة» متوقفة على هذا الهاتف. الحفظ مع تفعيل هذا المسلسل يشغّلها (ليلًا، Wi-Fi، أثناء الشحن)."},{"Keep ready is on","«جاهز للمشاهدة» مفعّلة"},{"Long-press your home screen, choose Widgets, then Noir.","اضغط مطولًا على الشاشة الرئيسية، واختر الأدوات، ثم Noir."}};for(String[] p:w)Loc.AR.putIfAbsent(p[0],p[1]);}
 static void init(){}
 static boolean seen(DataStore d,String id){JSONObject s=d.prefs().optJSONObject("tips");return s!=null&&s.has(id);}
 static void mark(DataStore d,String id){JSONObject s=d.prefs().optJSONObject("tips");if(s==null)s=new JSONObject();DataStore.put(s,id,System.currentTimeMillis());d.pref("tips",s);d.pref("tipDay",java.time.LocalDate.now().toEpochDay());}
 static boolean widgetPinnable(Context c){try{android.appwidget.AppWidgetManager m=c.getSystemService(android.appwidget.AppWidgetManager.class);return m!=null&&m.isRequestPinAppWidgetSupported()&&m.getAppWidgetIds(new ComponentName(c,NextUpWidget.class)).length==0;}catch(RuntimeException e){return false;}}
 static void pinWidget(MainActivity a){try{android.appwidget.AppWidgetManager m=a.getSystemService(android.appwidget.AppWidgetManager.class);if(m!=null&&m.isRequestPinAppWidgetSupported()&&m.requestPinAppWidget(new ComponentName(a,NextUpWidget.class),null,null))return;}catch(RuntimeException ignored){}a.toast("Long-press your home screen, choose Widgets, then Noir.");}
 // The one Home tip that fits right now, or null.
 static String homeTip(MainActivity a,List<NoirUi.Next> continuing){DataStore d=a.data;if(PhoneExperience.trackerOnly(d)||continuing.isEmpty()||d.prefs().optLong("tipDay",-1)==java.time.LocalDate.now().toEpochDay())return null;
  boolean show=continuing.stream().anyMatch(n->!n.item.optBoolean("movie")&&!n.kind.equals(NoirUi.UPCOMING));
  if(show&&!seen(d,"keepReady")&&(d.paired()||PhoneStream.available(a))&&!PhoneReadyJob.policy(d).enabled())return "keepReady";
  if(!seen(d,"widget")&&widgetPinnable(a))return "widget";
  if(!seen(d,"pullSync")&&d.paired())return "pullSync";
  return null;}
 static View home(MainActivity a,List<NoirUi.Next> continuing){String id=homeTip(a,continuing);if(id==null)return null;
  String title,body,action;Runnable act;switch(id){
   case "keepReady":title="Watch offline without thinking about it";body="Keep ready downloads the next episodes of the shows you're watching overnight, on Wi-Fi while charging.";action="Set up";act=()->a.startActivity(new Intent(a,DownloadHubActivity.class));break;
   case "widget":title="Your next episode on the home screen";body="The Next Up widget shows what to watch next and opens it in one tap.";action="Add widget";act=()->pinWidget(a);break;
   default:title="Pull down to sync";body="Pull down on any page, or tap the status at the top, to sync with your PC right away.";action=null;act=null;}
  LinearLayout card=a.column();card.setTag("tip:"+id);card.setPadding(a.dp(16),a.dp(14),a.dp(10),a.dp(12));GradientDrawable bg=MainActivity.bg(Color.rgb(26,25,27),a.dp(16));bg.setStroke(a.dp(1),Color.argb(80,228,192,132));card.setBackground(bg);
  TextView eyebrow=a.text("TIP",10,MainActivity.GOLD);eyebrow.setTypeface(null,Typeface.BOLD);eyebrow.setLetterSpacing(.12f);card.addView(eyebrow);TextView name=a.text(title,16,MainActivity.WHITE);name.setTypeface(null,Typeface.BOLD);name.setPadding(0,a.dp(4),a.dp(6),a.dp(2));card.addView(name);TextView text=a.text(body,13,MainActivity.MUTED);text.setPadding(0,0,a.dp(6),a.dp(8));card.addView(text);
  LinearLayout buttons=new LinearLayout(a);Runnable close=()->{mark(a.data,id);card.animate().alpha(0).setDuration(160).withEndAction(()->card.setVisibility(View.GONE)).start();};
  if(act!=null)buttons.addView(a.ui.small(action,()->{mark(a.data,id);act.run();card.setVisibility(View.GONE);},true));buttons.addView(a.ui.small(act==null?"Got it":"Not now",close,false));card.addView(buttons);
  LinearLayout.LayoutParams p=new LinearLayout.LayoutParams(-1,-2);p.setMargins(0,a.dp(18),0,a.dp(4));card.setLayoutParams(p);Motion.riseIn(card,0);return card;}
 // More → Tips & shortcuts: what is easy to miss, each with a way straight to it.
 static void list(MainActivity a){boolean track=PhoneExperience.trackerOnly(a.data);List<String[]> rows=new ArrayList<>();List<Runnable> acts=new ArrayList<>();Dialog[] shown={null};
  java.util.function.BiConsumer<String[],Runnable> add=(r,act)->{rows.add(r);acts.add(act);};
  add.accept(new String[]{"⋯","⋯ on any card or row","Every action for that episode: mark watched, play queue, downloads, rating, correct match."},null);
  if(!track)add.accept(new String[]{"⇆","Double-tap the video","Left side skips back 10 s, right side skips ahead 10 s. One tap shows or hides the controls."},null);
  if(a.data.paired())add.accept(new String[]{"↻","Pull down to sync","Pull down on any page, or tap the status at the top, to sync with your PC right away."},()->a.sync(true));
  if(!track)add.accept(new String[]{"⟳","Keep ready","Next episodes download overnight on Wi-Fi while charging, ready to watch offline."},()->a.startActivity(new Intent(a,DownloadHubActivity.class)));
  add.accept(new String[]{"↺","Catch me up","A short recap of where you stopped when you return to a show after a break."},null);
  if(!track)add.accept(new String[]{"✦","Smart lists","Unfinished business, comfort rewatches, directors you love and short picks for tonight, in Library."},()->{a.page="Library";a.detail="";a.render();});
  add.accept(new String[]{"♡","Compare taste","Swap a code with a friend and see the titles you both love."},()->TasteActivity.open(a));
  add.accept(new String[]{"▣","Next Up widget","Your next episode on the home screen."},()->pinWidget(a));
  if(!track)add.accept(new String[]{"▤","Edit Home","Reorder the rows and pin genres or collections."},()->{a.page="Home";a.detail="";a.render();a.homeLayout.edit();});
  add.accept(new String[]{"🔔","New-episode alerts","A notification when a new episode of a show you follow is out."},()->{a.page="History";a.detail="";a.premium.historyView="Upcoming";a.render();});
  add.accept(new String[]{"★","Pin your favorites","Hold a favorite in History to replace or remove it."},null);
  LinearLayout list=a.column();list.setPadding(a.dp(18),a.dp(4),a.dp(18),a.dp(8));list.addView(a.text("Everything Noir can do, in one place",13,MainActivity.MUTED));
  for(int n=0;n<rows.size();n++){String[] r=rows.get(n);Runnable act=acts.get(n);LinearLayout row=new LinearLayout(a);row.setGravity(Gravity.CENTER_VERTICAL);row.setPadding(a.dp(4),a.dp(10),a.dp(4),a.dp(10));TextView glyph=a.text(r[0],18,MainActivity.GOLD);glyph.setGravity(Gravity.CENTER);row.addView(glyph,new LinearLayout.LayoutParams(a.dp(32),-2));LinearLayout info=a.column();info.setPadding(a.dp(10),0,a.dp(6),0);TextView name=a.text(r[1],15,MainActivity.WHITE);name.setTypeface(null,Typeface.BOLD);info.addView(name);info.addView(a.text(r[2],12,MainActivity.MUTED));row.addView(info,new LinearLayout.LayoutParams(0,-2,1));row.setContentDescription(Loc.t(r[1]));
   // The dialog doesn't always resolve the chevron's direction, so it is chosen here and never mirrored again.
   if(act!=null){TextView go=a.text(Loc.arabic?"‹":"›",20,MainActivity.GOLD);go.setTextDirection(View.TEXT_DIRECTION_LTR);row.addView(go);row.setOnClickListener(v->{if(shown[0]!=null)shown[0].dismiss();act.run();});Motion.card(row);}list.addView(row);}
  ScrollView scroll=new ScrollView(a);scroll.addView(list);shown[0]=new AlertDialog.Builder(a).setTitle(Loc.t("Tips & shortcuts")).setView(scroll).setPositiveButton(Loc.t("Close"),null).show();}
}
