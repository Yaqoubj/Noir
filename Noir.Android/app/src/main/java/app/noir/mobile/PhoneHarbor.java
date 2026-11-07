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
  card("Stremio","S",Color.rgb(123,91,245),stremioOn,stremioOn?Loc.t("Connected"):Loc.t("Not connected"),"Bring in Stremio's library and watched episodes.",new String[]{stremioOn?"Manage":"Connect Stremio"},new Runnable[]{this::tools});
  card("Plex · Jellyfin · Emby","▤",Color.rgb(229,160,13),false,"Connect on this phone","Import watched history directly from Plex or HTTPS Jellyfin / Emby. Kodi and PC-configured servers are also available when paired.",new String[]{"Open"},new Runnable[]{this::tools});
  card("Trakt · Serializd","T",Color.rgb(237,34,36),a.data.paired(),"Managed on your PC","Watches you finish on this phone reach your PC, which marks them on Trakt and Serializd. Manage them in the PC's Settings → Connections.",new String[0],new Runnable[0]);
  JSONObject phone=phoneSettings();boolean downloads=!phone.optString("manifest").isEmpty()||!phone.optString("torBoxKey").isEmpty()||!phone.optString("realDebridKey").isEmpty();
  List<String> names=new ArrayList<>();if(!phone.optString("torBoxKey").isEmpty())names.add("TorBox");if(!phone.optString("realDebridKey").isEmpty())names.add("Real-Debrid");if(!phone.optString("manifest").isEmpty())names.add("AIOStreams");
  if(!PhoneExperience.trackerOnly(a.data))card("Streaming & phone downloads","↓",Color.rgb(5,165,122),downloads,downloads?String.join(" · ",names):Loc.t("Not connected"),"Download straight to this phone with your own TorBox, Real-Debrid or AIOStreams.",new String[]{downloads?"Manage":"Set up"},new Runnable[]{()->a.startActivity(new Intent(a,PhoneDownloadSettingsActivity.class))});
  card("Netflix · Letterboxd · IMDb · TV Time","⇅",Color.rgb(58,56,51),false,"Import files","Import an export file, or paste a list of titles. You review everything before it's added.",new String[]{"Open"},new Runnable[]{this::tools});
 }
 void tools(){a.startActivity(new Intent(a,HistoryToolsActivity.class));}
 void card(String name,String mark,int color,boolean on,String state,String what,String[] labels,Runnable[] actions){
  LinearLayout card=a.column();card.setPadding(a.dp(16),a.dp(14),a.dp(16),a.dp(12));GradientDrawable b=MainActivity.bg(Color.rgb(21,21,23),a.dp(16));b.setStroke(a.dp(1),on?Color.argb(70,228,192,132):Color.argb(26,255,255,255));card.setBackground(b);
  LinearLayout head=new LinearLayout(a);head.setGravity(Gravity.CENTER_VERTICAL);TextView m=a.text(mark,mark.length()>2?12:15,Color.WHITE);m.setTypeface(null,Typeface.BOLD);m.setGravity(Gravity.CENTER);m.setBackground(MainActivity.bg(color,a.dp(10)));head.addView(m,new LinearLayout.LayoutParams(a.dp(40),a.dp(40)));
  LinearLayout names=a.column();LinearLayout.LayoutParams np=new LinearLayout.LayoutParams(0,-2,1);np.setMargins(a.dp(12),0,0,0);head.addView(names,np);
  TextView title=a.text(name,PhoneDensity.pick(15,16),MainActivity.WHITE);title.setTypeface(null,Typeface.BOLD);names.addView(title);TextView st=a.text(state,12,on?MainActivity.GOLD:MainActivity.MUTED);st.setSingleLine();st.setEllipsize(TextUtils.TruncateAt.END);names.addView(st);
  if(on){View dot=new View(a);GradientDrawable d=new GradientDrawable();d.setShape(GradientDrawable.OVAL);d.setColor(Color.rgb(123,216,143));dot.setBackground(d);head.addView(dot,new LinearLayout.LayoutParams(a.dp(9),a.dp(9)));}
  card.addView(head);TextView w=a.text(what,PhoneDensity.pick(12.5f,13),MainActivity.MUTED);w.setPadding(0,a.dp(8),0,a.dp(6));card.addView(w);
  if(labels.length>0){LinearLayout row=new LinearLayout(a);for(int i=0;i<labels.length;i++)row.addView(a.ui.small(labels[i],actions[i],i==0));card.addView(row);}
  LinearLayout.LayoutParams lp=new LinearLayout.LayoutParams(-1,-2);lp.setMargins(0,0,0,a.dp(12));a.body.addView(card,lp);
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
   TextView t=a.text(label,PhoneDensity.chip(),on?MainActivity.BLACK:MainActivity.WHITE);t.setGravity(Gravity.CENTER);t.setSingleLine();t.setTypeface(null,on?Typeface.BOLD:Typeface.NORMAL);t.setPadding(a.dp(6),a.dp(PhoneDensity.pick(9,10)),a.dp(6),a.dp(PhoneDensity.pick(9,10)));t.setBackground(MainActivity.bg(on?MainActivity.GOLD:Color.argb(50,255,255,255),a.dp(20)));t.setOnClickListener(v->{Motion.haptic(t);pick.accept(label);});Motion.card(t);
   LinearLayout.LayoutParams p=new LinearLayout.LayoutParams(0,-2,1);p.setMargins(0,0,a.dp(8),a.dp(8));line.addView(t,p);}
  if(line!=null)while(line.getChildCount()<perRow)line.addView(new View(a),new LinearLayout.LayoutParams(0,1,1));
 }
}
