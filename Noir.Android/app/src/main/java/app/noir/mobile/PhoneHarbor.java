package app.noir.mobile;
import android.content.Intent;
import android.graphics.*;
import android.graphics.drawable.GradientDrawable;
import android.text.TextUtils;
import android.view.*;
import android.widget.*;
import org.json.*;
import java.util.*;

// The phone's Harbor-style pieces, matching the PC: your services, Top 10 with outlined numbers,
// the Connections page, and History laid out as one vertical list.
final class PhoneHarbor {
 final MainActivity a;
 static JSONArray topTen;static long topTenAt;
 PhoneHarbor(MainActivity activity){a=activity;}

 // ---- Home ----
 static final class Service{final String name;final int color;final Runnable open;final boolean on;Service(String n,int c,boolean o,Runnable r){name=n;color=c;on=o;open=r;}}
 List<Service> services(){
  List<Service> list=new ArrayList<>();JSONObject nuvio=a.data.stateObject("nuvio"),stremio=a.data.stateObject("stremio");
  Runnable connections=()->{a.page="Connections";a.detail="";a.render();};
  list.add(new Service(a.data.paired()?"Noir PC":"Pair PC",MainActivity.GOLD,a.data.paired(),connections));
  list.add(new Service("Nuvio",Color.rgb(160,140,255),nuvio.optInt("profile")>0&&!nuvio.optString("refresh").isEmpty(),connections));
  list.add(new Service("Stremio",Color.rgb(155,107,211),!stremio.optString("key").isEmpty(),connections));
  JSONObject phone=phoneSettings();
  if(!phone.optString("torBoxKey").isEmpty())list.add(new Service("TorBox",Color.rgb(5,194,143),true,connections));
  if(!phone.optString("realDebridKey").isEmpty())list.add(new Service("Real-Debrid",Color.rgb(120,187,75),true,connections));
  if(!phone.optString("manifest").isEmpty())list.add(new Service("AIOStreams",Color.rgb(122,167,255),true,connections));
  return list;
 }
 // Decrypting the phone's download settings on every Home render is wasteful; a minute-old copy is fine.
 static JSONObject phoneCache;static long phoneAt;
 JSONObject phoneSettings(){return PhoneStream.settings(a);}
 void servicesRow(LinearLayout host){
  List<View> tiles=new ArrayList<>();
  for(Service s:services()){if(!s.on)continue;TextView t=a.text(s.name,PhoneDensity.pick(15,17),s.color);t.setTypeface(Typeface.create("sans-serif-black",Typeface.BOLD));t.setGravity(Gravity.CENTER);t.setSingleLine();
   GradientDrawable b=MainActivity.bg(Color.argb(22,255,255,255),a.dp(14));b.setStroke(a.dp(1),Color.argb(26,255,255,255));t.setBackground(b);t.setContentDescription(s.name);t.setOnClickListener(v->s.open.run());
   LinearLayout.LayoutParams p=new LinearLayout.LayoutParams(a.dp(PhoneDensity.pick(120,138)),a.dp(PhoneDensity.pick(50,58)));p.setMargins(0,0,a.dp(PhoneDensity.pick(8,10)),0);t.setLayoutParams(p);tiles.add(t);}
  Runnable all=()->{a.page="Connections";a.render();};a.homeLayout.endTile(tiles,all);a.ui.rail(host,"Your services","",tiles,all);
 }
 // Large outlined numerals behind each poster; digits never mirror in Arabic.
 void topTenRail(LinearLayout host){
  LinearLayout slot=a.column();host.addView(slot);
  if(topTen!=null&&topTenAt>System.currentTimeMillis()-6*3600000L){fillTopTen(slot);return;}
  final long revision=a.renderRevision;
  a.data.network.execute(()->{try{if(a.discovery==null)a.discovery=new DiscoveryClient(a);JSONArray found=a.discovery.search("","All");JSONArray ten=new JSONArray();for(int i=0;i<Math.min(10,found.length());i++)ten.put(found.optJSONObject(i));topTen=ten;topTenAt=System.currentTimeMillis();
   a.runOnUiThread(()->{if(a.renderRevision==revision)fillTopTen(slot);});}catch(Exception ignored){}});
 }
 void fillTopTen(LinearLayout slot){
  List<View> cards=new ArrayList<>();for(int i=0;i<topTen.length();i++)cards.add(topTenCard(topTen.optJSONObject(i),i+1));
  Runnable all=()->a.startActivity(new Intent(a,DiscoverActivity.class));a.homeLayout.endTile(cards,all);a.ui.rail(slot,"Top 10 this week","Most popular on Cinemeta right now.",cards,all);
 }
 View topTenCard(JSONObject title,int rank){
  FrameLayout card=new FrameLayout(a);card.setLayoutDirection(View.LAYOUT_DIRECTION_LTR);LinearLayout.LayoutParams cp=new LinearLayout.LayoutParams(a.dp(PhoneDensity.pick(rank==10?176:150,rank==10?196:166)),a.dp(PhoneDensity.pick(180,200)));cp.setMargins(0,0,a.dp(6),0);card.setLayoutParams(cp);
  TextView number=new TextView(a);number.setText(String.valueOf(rank));number.setTextSize(PhoneDensity.pick(rank==10?94:108,rank==10?104:120));number.setTypeface(Typeface.create("sans-serif-black",Typeface.BOLD));number.setIncludeFontPadding(false);number.setLetterSpacing(-.08f);
  number.getPaint().setStyle(Paint.Style.STROKE);number.getPaint().setStrokeWidth(a.dp(2));number.setTextColor(Color.argb(115,255,255,255));
  FrameLayout.LayoutParams np=new FrameLayout.LayoutParams(-2,-2,Gravity.BOTTOM|Gravity.START);np.setMargins(0,0,0,a.dp(18));card.addView(number,np);
  ImageView poster=new ImageView(a);poster.setScaleType(ImageView.ScaleType.CENTER_CROP);poster.setBackground(MainActivity.bg(Motion.placeholder(title.optString("poster")),a.dp(10)));poster.setClipToOutline(true);
  FrameLayout.LayoutParams pp=new FrameLayout.LayoutParams(a.dp(PhoneDensity.pick(94,104)),a.dp(PhoneDensity.pick(140,156)),Gravity.TOP|Gravity.END);card.addView(poster,pp);TextView badge=a.ui.pill(StatusLabels.text("#"+rank+" this week"),MainActivity.BLACK,MainActivity.GOLD);FrameLayout.LayoutParams bp=new FrameLayout.LayoutParams(-2,-2,Gravity.TOP|Gravity.END);bp.setMargins(0,a.dp(6),a.dp(4),0);card.addView(badge,bp);
  TextView name=a.text(title.optString("name"),11,Color.rgb(199,194,188));name.setSingleLine();name.setEllipsize(TextUtils.TruncateAt.END);name.setGravity(Gravity.CENTER);FrameLayout.LayoutParams tp=new FrameLayout.LayoutParams(a.dp(PhoneDensity.pick(94,104)),-2,Gravity.BOTTOM|Gravity.END);tp.setMargins(0,0,0,a.dp(18));card.addView(name,tp);
  String url=title.optString("poster");final long revision=a.renderRevision;
  a.data.images.execute(()->{try{if(a.discovery==null)a.discovery=new DiscoveryClient(a);Bitmap b=a.discovery.art(url);if(b!=null)Motion.remember(url,b);if(b!=null)a.runOnUiThread(()->{if(a.renderRevision==revision)Motion.loaded(poster,b,url);});}catch(Exception ignored){}});
  card.setContentDescription(Loc.t("Top 10")+" "+rank+": "+title.optString("name"));
  card.setOnClickListener(v->DiscoverSheet.show(a,title,card));Motion.card(card);
  return card;
 }

 // ---- Connections ----
 void connections(){
  a.heading("Connections");a.paragraph("Connect services directly on this phone. Pairing with Windows is optional.");
  String profile=a.browsingSettings().optString("profileName");
  card("Optional PC sync"+(profile.isEmpty()?"":" · "+profile),"PC",MainActivity.GOLD,a.data.paired(),a.data.paired()?a.data.connection+(a.data.lastSync()>0?" · "+NoirUi.ago(a.data.lastSync()):"")+" · "+a.data.pendingCount()+" waiting":Loc.t("Not paired"),
   "History, resume times, ratings and My List sync over your home Wi-Fi. Stream videos from the PC.",
   a.data.paired()?new String[]{"Sync now","Pair again"}:new String[]{"Pair with my PC"},
   a.data.paired()?new Runnable[]{()->a.sync(true),a::pairInput}:new Runnable[]{a::pairInput});
  JSONObject nuvio=a.data.stateObject("nuvio");boolean nuvioOn=nuvio.optInt("profile")>0&&!nuvio.optString("refresh").isEmpty();
  card("Nuvio","Nv",Color.rgb(108,76,241),nuvioOn,nuvioOn?Loc.t("Connected")+" · "+(nuvio.optString("profileName").isEmpty()?"profile "+nuvio.optInt("profile"):nuvio.optString("profileName")):Loc.t("Not connected"),
   "Two-way: Nuvio's watches come in, and what you watch here is marked in Nuvio.",new String[]{nuvioOn?"Manage":"Connect Nuvio"},new Runnable[]{this::tools});
  JSONObject stremio=a.data.stateObject("stremio");boolean stremioOn=!stremio.optString("key").isEmpty();
  if(stremioOn)stremioCard();else{card("Stremio","S",Color.rgb(123,91,245),false,Loc.t("Not connected"),"Bring in Stremio's library and watched episodes.",new String[]{"Connect Stremio"},new Runnable[]{this::tools});a.paragraph("Stremio marks go through your PC");}
  card("Plex · Jellyfin · Emby","▤",Color.rgb(229,160,13),false,"Connect on this phone","Import watched history directly from Plex or HTTPS Jellyfin / Emby. Kodi and PC-configured servers are also available when paired.",new String[]{"Open"},new Runnable[]{this::tools});
  watchSync("Trakt","T",Color.rgb(237,34,36));watchSync("Serializd","Sd",Color.rgb(44,120,220));
  JSONObject phone=phoneSettings();boolean downloads=!phone.optString("manifest").isEmpty()||!phone.optString("torBoxKey").isEmpty()||!phone.optString("realDebridKey").isEmpty();
  List<String> names=new ArrayList<>();if(!phone.optString("torBoxKey").isEmpty())names.add("TorBox");if(!phone.optString("realDebridKey").isEmpty())names.add("Real-Debrid");if(!phone.optString("manifest").isEmpty())names.add("AIOStreams");
  if(!PhoneExperience.trackerOnly(a.data))card("Streaming & phone downloads","↓",Color.rgb(5,165,122),downloads,downloads?String.join(" · ",names):Loc.t("Not connected"),"Download straight to this phone with your own TorBox, Real-Debrid or AIOStreams.",new String[]{downloads?"Manage":"Set up"},new Runnable[]{()->a.startActivity(new Intent(a,PhoneDownloadSettingsActivity.class))});
  card("Netflix · Letterboxd · IMDb · TV Time","⇅",Color.rgb(58,56,51),false,"Import files","Import an export file, or paste a list of titles. You review everything before it's added.",new String[]{"Open"},new Runnable[]{this::tools});
 }
 void tools(){a.startActivity(new Intent(a,HistoryToolsActivity.class));}
 /* P3: this phone marks what it plays on Trakt / Serializd itself, with its own sign-in. */
 static final int GREEN=Color.rgb(123,216,143),AMBER=Color.rgb(232,170,60),GREY=Color.rgb(128,128,134);
 void watchSync(String p,String mark,int color){DataStore d=a.data;long now=WatchSync.clock.now();boolean on=WatchSync.enabled(d,p),blocked=on&&WatchSync.refused(d,p);JSONObject pr=WatchSync.provider(d,p);
  String state=!on?Loc.t("Off"):Loc.f("%d synced · %d waiting",WatchSync.count(d,p,true),WatchSync.count(d,p,false))+(pr.optLong("lastSuccess")>0?" · "+Loc.f("last success %s",NoirUi.ago(pr.optLong("lastSuccess"))):"");
  int left=WatchSync.count(d,p,false);if(on&&left>WatchSync.PER_HOUR)state+=" · "+(left<=WatchSync.PER_DAY?Loc.f("about %d h at 30 an hour",(left+WatchSync.PER_HOUR-1)/WatchSync.PER_HOUR):Loc.f("about %d days at 200 a day",(left+WatchSync.PER_DAY-1)/WatchSync.PER_DAY));
  StringBuilder what=new StringBuilder(Loc.f("Marks what you finish on this phone as watched on %s, using your sign-in here.",p));
  if(blocked){what.setLength(0);what.append(Loc.t("Serializd does not allow this from the phone · it syncs through your PC"));state=Loc.t("Syncs through your PC");}
  else if(on&&WatchSync.failingForADay(d,p,now))what.append("\n").append(Loc.f("%s hasn't accepted marks for a day. Sign in again or tap Retry.",p));
  else if(on&&pr.optLong("retryAt")>now&&!pr.optString("message").isEmpty())what.append("\n").append(Loc.t(pr.optString("message"))).append(" · ").append(Loc.f("next try %s",java.text.DateFormat.getTimeInstance(java.text.DateFormat.SHORT,Loc.locale()).format(new java.util.Date(pr.optLong("retryAt")))));
  boolean stuck=WatchSync.stuck(d,p,now),late=on&&!blocked&&WatchSync.failingForADay(d,p,now);
  boolean retry=on&&!blocked&&(stuck||WatchSync.waiting(d,p,now));
  List<String> labels=new ArrayList<>(Arrays.asList("Sign in",on?"Turn off":"Turn on"));List<Runnable> actions=new ArrayList<>(Arrays.asList(()->a.startActivity(WatchSignInActivity.intent(a,p)),()->{WatchSync.enable(d,p,!WatchSync.enabled(d,p));WatchSyncSender.wake();a.render();}));
  if(blocked){labels.remove(0);actions.remove(0);}
  if(retry){labels.add("Retry");actions.add(()->{WatchSync.retry(d,p);WatchSyncSender.wake();a.render();});}
  card(p,mark,color,on&&!blocked,blocked?GREY:late?AMBER:on?GREEN:0,state,what.toString(),labels.toArray(new String[0]),actions.toArray(new Runnable[0]));}
 static {String[][] words={{"%d synced · %d waiting","%d تمت مزامنتها · %d بالانتظار"},{"last success %s","آخر نجاح %s"},{"Marks what you finish on this phone as watched on %s, using your sign-in here.","يضع علامة المشاهدة على %s لما تنهيه على هذا الهاتف، بتسجيل دخولك هنا."},
  {"Serializd does not allow this from the phone · it syncs through your PC","لا يسمح Serializd بذلك من الهاتف · تتم المزامنة عبر الكمبيوتر"},{"%s hasn't accepted marks for a day. Sign in again or tap Retry.","لم يقبل %s العلامات منذ يوم. سجّل الدخول مجددًا أو اضغط إعادة المحاولة."},
  {"next try %s","المحاولة التالية %s"},{"about %d h at 30 an hour","نحو %d ساعة بمعدل 30 في الساعة"},{"about %d days at 200 a day","نحو %d يوم بمعدل 200 في اليوم"},{"Syncs through your PC","تتم المزامنة عبر الكمبيوتر"},
  {"Stremio marks go through your PC","تمر علامات Stremio عبر الكمبيوتر"},{"Stremio marks","علامات Stremio"},{"Marks what you finish on this phone as watched in your Stremio account, using your Stremio sign-in here.","يضع علامة المشاهدة في حساب Stremio لما تنهيه على هذا الهاتف، بتسجيل دخولك إلى Stremio هنا."},
  {"Mark watched in your Stremio account (also seen by Harbor)","علّم المُشاهَد في حساب Stremio (يراه Harbor أيضًا)"},{"Add watched titles to my Stremio library","أضف العناوين المُشاهَدة إلى مكتبتي في Stremio"},{"New: try it on one title first, then check it in Stremio.","جديد: جرّبها على عنوان واحد أولًا، ثم تحقّق منها في Stremio."},{"Bring in Stremio's library and watched episodes.","استورد مكتبة Stremio وحلقاتها المُشاهَدة."},
  {"Connect Stremio again","اربط Stremio مجددًا"},{"Restore a title","استعادة عنوان"},{"Restore","استعادة"},
  {"Puts this title back in your Stremio account as it was before Noir first changed it. If it changed in Stremio since Noir's last change, it is left as it is.","يعيد هذا العنوان في حساب Stremio كما كان قبل أول تغيير من Noir. وإن تغيّر في Stremio بعد آخر تغيير من Noir فيُترك كما هو."},{"Turn on","تشغيل"},{"Turn off","إيقاف"},{"Retry","إعادة المحاولة"}};for(String[] w:words)Loc.AR.put(w[0],w[1]);}
/* The phone's own Stremio account, one card: connection, the two mark switches, counts and Retry. Marks only with this phone's own Stremio sign-in; otherwise the PC does it. */
 void stremioCard(){DataStore d=a.data;String p=WatchSync.STREMIO;
  long now=WatchSync.clock.now();boolean on=WatchSync.enabled(d,p);JSONObject pr=WatchSync.provider(d,p);
  String state=Loc.t("Connected")+(on?" · "+Loc.f("%d synced · %d waiting",WatchSync.count(d,p,true),WatchSync.count(d,p,false))+(pr.optLong("lastSuccess")>0?" · "+Loc.f("last success %s",NoirUi.ago(pr.optLong("lastSuccess"))):""):"");
  StringBuilder what=new StringBuilder(Loc.t("Bring in Stremio's library and watched episodes."));
  boolean reconnect=on&&StremioWriter.reconnect(pr.optString("message")),late=on&&WatchSync.failingForADay(d,p,now);
  if(late)what.append("\n").append(Loc.f("%s hasn't accepted marks for a day. Sign in again or tap Retry.",p));
  else if(on&&pr.optLong("retryAt")>now&&!pr.optString("message").isEmpty())what.append("\n").append(Loc.t(pr.optString("message"))).append(" · ").append(Loc.f("next try %s",java.text.DateFormat.getTimeInstance(java.text.DateFormat.SHORT,Loc.locale()).format(new java.util.Date(pr.optLong("retryAt")))));
  StremioWriter writer=StremioWriter.forPhone(d);List<String> restorable=writer.originals();
  List<String> labels=new ArrayList<>(Arrays.asList("Manage"));List<Runnable> actions=new ArrayList<>(Arrays.asList(this::tools));
  if(reconnect){labels.add("Connect Stremio again");actions.add(this::tools);}
  if(on&&(WatchSync.stuck(d,p,now)||WatchSync.waiting(d,p,now))){labels.add("Retry");actions.add(()->{WatchSync.retry(d,p);WatchSyncSender.wake();a.render();});}
  if(!restorable.isEmpty()){labels.add("Restore a title");actions.add(()->restoreStremio(writer,restorable));}
  LinearLayout card=card("Stremio","S",Color.rgb(123,91,245),true,late?AMBER:GREEN,state,what.toString(),labels.toArray(new String[0]),actions.toArray(new Runnable[0]));
  int at=card.getChildCount()-1;
  /* S3: switching on sends only what is watched from now on. The second switch waits for the first. */
  Switch mark=new Switch(a);mark.setText(Loc.t("Mark watched in your Stremio account (also seen by Harbor)"));mark.setTextColor(MainActivity.WHITE);mark.setChecked(on);
  Switch add=new Switch(a);add.setText(Loc.t("Add watched titles to my Stremio library"));add.setTextColor(MainActivity.WHITE);add.setChecked(WatchSync.stremioAdd(d));add.setEnabled(on);add.setAlpha(on?1f:.45f);add.setPaddingRelative(a.dp(20),0,0,0);
  mark.setOnCheckedChangeListener((v,c)->{WatchSync.enable(d,p,c);WatchSyncSender.wake();a.render();});
  add.setOnCheckedChangeListener((v,c)->{d.phoneSetting("stremioAddTitles",c);a.render();});
  TextView note=a.text("New: try it on one title first, then check it in Stremio.",12,MainActivity.GOLD);note.setPadding(0,a.dp(2),0,a.dp(6));
  card.addView(mark,at);card.addView(add,at+1);card.addView(note,at+2);}
 /* Restore puts back the copy Noir saved before its first change to that title, unless it changed in Stremio since; the toast says how many were restored and left. */
 void restoreStremio(StremioWriter writer,List<String> ids){Map<String,String> names=new HashMap<>();for(JSONObject i:DataStore.objects(a.data.stateObject("library").optJSONArray("items")))if(!i.optString("imdb").isEmpty())names.put(i.optString("imdb"),i.optString("title"));
  String[] rows=new String[ids.size()];for(int i=0;i<rows.length;i++){String n=names.get(ids.get(i));rows[i]=n==null||n.isEmpty()?ids.get(i):n;}
  new android.app.AlertDialog.Builder(a).setTitle(Loc.t("Restore a title")).setItems(rows,(dialog,w)->new android.app.AlertDialog.Builder(a).setTitle(rows[w]).setMessage(Loc.t("Puts this title back in your Stremio account as it was before Noir first changed it. If it changed in Stremio since Noir's last change, it is left as it is.")).setNegativeButton(Loc.t("Cancel"),null)
   .setPositiveButton(Loc.t("Restore"),(x,y)->a.data.network.execute(()->{String m;try{m=writer.restore(Collections.singletonList(ids.get(w))).text();}catch(InterruptedException e){Thread.currentThread().interrupt();return;}a.runOnUiThread(()->{a.toast(m);a.render();});})).show()).show();}
 LinearLayout card(String name,String mark,int color,boolean on,String state,String what,String[] labels,Runnable[] actions){return card(name,mark,color,on,on?GREEN:0,state,what,labels,actions);}
 LinearLayout card(String name,String mark,int color,boolean on,int dotColor,String state,String what,String[] labels,Runnable[] actions){
  LinearLayout card=a.column();card.setPadding(a.dp(16),a.dp(14),a.dp(16),a.dp(12));GradientDrawable b=MainActivity.bg(Color.rgb(21,21,23),a.dp(16));b.setStroke(a.dp(1),on?Color.argb(70,228,192,132):Color.argb(26,255,255,255));card.setBackground(b);
  LinearLayout head=new LinearLayout(a);head.setGravity(Gravity.CENTER_VERTICAL);TextView m=a.text(mark,mark.length()>2?12:15,Color.WHITE);m.setTypeface(null,Typeface.BOLD);m.setGravity(Gravity.CENTER);m.setBackground(MainActivity.bg(color,a.dp(10)));head.addView(m,new LinearLayout.LayoutParams(a.dp(40),a.dp(40)));
  LinearLayout names=a.column();LinearLayout.LayoutParams np=new LinearLayout.LayoutParams(0,-2,1);np.setMarginStart(a.dp(12));np.setMarginEnd(a.dp(8));head.addView(names,np);
  TextView title=a.text(name,PhoneDensity.pick(15,16),MainActivity.WHITE);title.setTypeface(null,Typeface.BOLD);title.setTextAlignment(View.TEXT_ALIGNMENT_VIEW_START);names.addView(title);TextView st=a.text(state,12,on?MainActivity.GOLD:MainActivity.MUTED);st.setTextAlignment(View.TEXT_ALIGNMENT_VIEW_START);names.addView(st);
  if(dotColor!=0){View dot=new View(a);dot.setTag("dot");GradientDrawable d=new GradientDrawable();d.setShape(GradientDrawable.OVAL);d.setColor(dotColor);dot.setBackground(d);head.addView(dot,new LinearLayout.LayoutParams(a.dp(9),a.dp(9)));}
  card.addView(head);TextView w=a.text(what,PhoneDensity.pick(12.5f,13),MainActivity.MUTED);w.setTextAlignment(View.TEXT_ALIGNMENT_VIEW_START);w.setPadding(0,a.dp(8),0,a.dp(6));card.addView(w);
  if(labels.length>0){LinearLayout row=new LinearLayout(a);for(int i=0;i<labels.length;i++)row.addView(a.ui.small(labels[i],actions[i],i==0));card.addView(row);}
  LinearLayout.LayoutParams lp=new LinearLayout.LayoutParams(-1,-2);lp.setMargins(0,0,0,a.dp(12));a.body.addView(card,lp);return card;
 }

 // ---- History as one vertical list ----
 void section(LinearLayout host,String title,String subtitle,List<View> rows){
  if(rows.isEmpty())return;TextView t=a.text(title,PhoneDensity.railTitle(),MainActivity.WHITE);t.setTypeface(null,Typeface.BOLD);t.setPadding(0,a.dp(PhoneDensity.headingTop()),0,a.dp(2));host.addView(t);
  if(!subtitle.isEmpty()){TextView s=a.text(subtitle,PhoneDensity.railSubtitle(),MainActivity.MUTED);s.setPadding(0,0,0,a.dp(10));host.addView(s);}
  for(View r:rows){LinearLayout.LayoutParams p=new LinearLayout.LayoutParams(-1,-2);p.setMargins(0,0,0,a.dp(PhoneDensity.pick(8,10)));r.setLayoutParams(p);host.addView(r);}
 }
 // A compact title row for lists: poster, title, one line of detail.
 View titleRow(List<JSONObject> g,String detail){
  JSONObject show=g.get(0);LinearLayout row=new LinearLayout(a);row.setGravity(Gravity.CENTER_VERTICAL);row.setPadding(a.dp(10),a.dp(8),a.dp(10),a.dp(8));row.setBackground(MainActivity.bg(Color.rgb(20,20,22),a.dp(12)));
  row.addView(a.ui.art(show,true,a.dp(PhoneDensity.pick(40,44)),a.dp(PhoneDensity.pick(60,66))));LinearLayout info=a.column();LinearLayout.LayoutParams ip=new LinearLayout.LayoutParams(0,-2,1);ip.setMargins(a.dp(12),0,a.dp(8),0);row.addView(info,ip);
  TextView title=a.text(show.optString("title"),PhoneDensity.cardTitle(),MainActivity.WHITE);title.setTypeface(null,Typeface.BOLD);title.setSingleLine();title.setEllipsize(TextUtils.TruncateAt.END);info.addView(title);
  info.addView(a.text(detail,12,MainActivity.GOLD));row.setOnClickListener(v->a.open(show,row));Motion.card(row);return row;
 }
 // Tabs wrap onto rows instead of scrolling sideways.
 void tabs(LinearLayout host,String[] labels,String chosen,java.util.function.Consumer<String> pick){
  int perRow=a.getResources().getConfiguration().screenWidthDp>=600?6:3;LinearLayout line=null;
  for(int n=0;n<labels.length;n++){if(n%perRow==0){line=new LinearLayout(a);host.addView(line);}String label=labels[n];boolean on=label.equals(chosen);
   TextView t=a.text(label,PhoneDensity.chip(),on?MainActivity.BLACK:MainActivity.WHITE);t.setGravity(Gravity.CENTER);t.setTextAlignment(View.TEXT_ALIGNMENT_CENTER);t.setSingleLine();t.setTypeface(null,on?Typeface.BOLD:Typeface.NORMAL);t.setPadding(a.dp(6),a.dp(PhoneDensity.pick(9,10)),a.dp(6),a.dp(PhoneDensity.pick(9,10)));t.setBackground(MainActivity.bg(on?MainActivity.GOLD:Color.argb(50,255,255,255),a.dp(20)));t.setOnClickListener(v->{Motion.haptic(t);pick.accept(label);});Motion.card(t);
   LinearLayout.LayoutParams p=new LinearLayout.LayoutParams(0,-2,1);p.setMargins(0,0,a.dp(8),a.dp(8));line.addView(t,p);}
  if(line!=null)while(line.getChildCount()<perRow)line.addView(new View(a),new LinearLayout.LayoutParams(0,1,1));
 }
}
