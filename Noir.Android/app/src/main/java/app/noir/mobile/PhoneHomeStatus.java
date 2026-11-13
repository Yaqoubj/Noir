package app.noir.mobile;
import android.content.Intent;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.view.*;
import android.widget.*;
import org.json.*;
import java.util.*;

/**
 * Home status strip, the phone twin of the PC's (U147): what Noir is doing and holding right now, each tile one tap
 * from its page. Downloads, the next episode on the calendar, free space, streaming setup and Edit Home sit in one
 * sideways row under the hero, so the downloader and organizer are visible from the first screen.
 */
final class PhoneHomeStatus {
 static {
  String[][] words={{"Downloads","التنزيلات"},{"Nothing saved yet","لا شيء محفوظ بعد"},{"Next episode","الحلقة التالية"},{"Free on this phone","المتاح على الهاتف"},{"Streaming","البث"},{"Set up streaming","إعداد البث"},
   {"%d downloading","%d قيد التنزيل"},{"%d waiting","%d بالانتظار"},{"%d need attention","%d تحتاج إلى انتباه"},{"Edit Home","تعديل الرئيسية"},{"Rows and tile size","الصفوف وحجم البطاقات"},{"Today","اليوم"},{"Tomorrow","غدًا"}};
  for(String[] w:words)Loc.AR.putIfAbsent(w[0],w[1]);
  Loc.plural("%d saved on this phone","فيديو واحد محفوظ على الهاتف","فيديوان محفوظان على الهاتف","%d فيديوهات محفوظة على الهاتف","%d فيديو محفوظ على الهاتف");
 }
 /** One tile: a drawn icon, a quiet label and the live value. The whole tile is the button. */
 static LinearLayout tile(MainActivity a,String key,String icon,String label,String value,boolean call,Runnable open){
  LinearLayout t=new LinearLayout(a);t.setGravity(Gravity.CENTER_VERTICAL);t.setPadding(a.dp(12),a.dp(9),a.dp(14),a.dp(9));t.setMinimumHeight(a.dp(PhoneDensity.pick(52,56)));
  GradientDrawable back=MainActivity.bg(call?0xff2c2720:MainActivity.PANEL,a.dp(14));if(call)back.setStroke(a.dp(1),0x5ae4c084);t.setBackground(back);
  t.setTag("home-status:"+key);t.setFocusable(true);t.addView(NoirIcons.icon(a,icon,20,MainActivity.GOLD));
  LinearLayout info=a.column();info.setPaddingRelative(a.dp(10),0,0,0);
  TextView name=a.text(label,11,MainActivity.MUTED);name.setLineSpacing(0,1);name.setSingleLine();info.addView(name);
  TextView shown=a.text(value,PhoneDensity.pick(13,13.5f),call?MainActivity.GOLD:MainActivity.WHITE);shown.setTypeface(null,Typeface.BOLD);shown.setLineSpacing(0,1);shown.setSingleLine();shown.setEllipsize(android.text.TextUtils.TruncateAt.END);shown.setMaxWidth(a.dp(210));shown.setTag("home-status-value");info.addView(shown);
  t.addView(info);t.setContentDescription(Loc.t(label)+": "+Loc.t(value));t.setOnClickListener(v->open.run());Motion.card(t);
  LinearLayout.LayoutParams p=new LinearLayout.LayoutParams(-2,-2);p.setMarginEnd(a.dp(8));t.setLayoutParams(p);return t;
 }
 static String downloads(){
  int down=0,wait=0,fail=0,done=0;
  for(JSONObject j:DataStore.objects(NoirApp.downloads.jobs()))switch(j.optString("status")){case "downloading":down++;break;case "failed":fail++;break;case "complete":done++;break;default:wait++;}
  if(down>0)return Loc.f("%d downloading",down)+(wait>0?" · "+Loc.f("%d waiting",wait):"");
  if(fail>0)return Loc.f("%d need attention",fail);
  if(wait>0)return Loc.f("%d waiting",wait);
  return done>0?Loc.plural(done,"%d saved on this phone","%d saved on this phone"):Loc.t("Nothing saved yet");
 }
 /** The soonest announced episode of any show you have (not dropped or hidden), up to 45 days out: {show, episode}. */
 static JSONObject[] soonest(MainActivity a){
  JSONObject schedules=a.browsingSettings().optJSONObject("schedules");if(schedules==null)return null;long now=System.currentTimeMillis(),bestAt=now+45L*86400000;JSONObject[] best=null;
  for(List<JSONObject> g:a.groups()){JSONObject first=g.get(0);if(first.optString("status").equals("Dropped")||first.optBoolean("hidden"))continue;
   for(JSONObject ep:DataStore.objects(schedules.optJSONArray(first.optString("group")))){long at=DataStore.date(ep.optString("airsAt"));if(ep.optInt("season")>0&&at>now&&at<bestAt){bestAt=at;best=new JSONObject[]{first,ep};}}}
  return best;
 }
 static String when(long at){
  long days=java.time.temporal.ChronoUnit.DAYS.between(java.time.LocalDate.now(),java.time.Instant.ofEpochMilli(at).atZone(java.time.ZoneId.systemDefault()).toLocalDate());
  return days<=0?Loc.t("Today"):days==1?Loc.t("Tomorrow"):NoirUi.date(at);
 }
 static View strip(MainActivity a){
  LinearLayout tiles=new LinearLayout(a);
  if(!PhoneStream.available(a)){LinearLayout setup=tile(a,"streaming","streaming","Streaming","Set up streaming",true,()->a.startActivity(new Intent(a,PhoneDownloadSettingsActivity.class)));setup.setTag("stream-setup");tiles.addView(setup);}
  LinearLayout down=tile(a,"downloads","downloads","Downloads",downloads(),false,()->a.startActivity(new Intent(a,DownloadHubActivity.class)));tiles.addView(down);
  // The download tile follows the queue while Home is on screen.
  TextView live=down.findViewWithTag("home-status-value");long revision=a.renderRevision;
  Runnable[] tick={null};tick[0]=()->{if(a.isDestroyed()||revision!=a.renderRevision||!(a.active||a.firstResume))return;String now=downloads();if(!now.contentEquals(live.getText())){live.setText(now);down.setContentDescription(Loc.t("Downloads")+": "+now);}a.handler.postDelayed(tick[0],2000);};
  a.handler.postDelayed(tick[0],2000);
  JSONObject[] next=soonest(a);
  if(next!=null){JSONObject show=next[0];long at=DataStore.date(next[1].optString("airsAt"));tiles.addView(tile(a,"next","calendar","Next episode",show.optString("title")+" · "+when(at),false,()->a.open(show)));}
  try{long free=new android.os.StatFs(NoirApp.downloads.directory.getAbsolutePath()).getAvailableBytes();tiles.addView(tile(a,"space","drive","Free on this phone",PhoneDownloadsManage.ltr(DownloadHubActivity.size(free)),false,()->a.startActivity(new Intent(a,DownloadHubActivity.class).putExtra("tab","phone"))));}catch(Exception ignored){}
  tiles.addView(tile(a,"edit","sliders","Rows and tile size","Edit Home",false,a.homeLayout::edit));
  HorizontalScrollView row=a.ui.row(tiles);row.setTag("home-status");LinearLayout.LayoutParams p=new LinearLayout.LayoutParams(-1,-2);p.setMargins(0,a.dp(PhoneDensity.pick(10,12)),0,0);row.setLayoutParams(p);return row;
 }
}
