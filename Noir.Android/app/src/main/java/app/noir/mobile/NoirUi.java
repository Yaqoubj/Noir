package app.noir.mobile;
import android.app.AlertDialog;
import android.graphics.*;
import android.graphics.drawable.GradientDrawable;
import android.text.TextUtils;
import android.view.*;
import android.widget.*;
import org.json.*;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.time.*;
import java.util.*;

// Browsing design shared by Home, History and title pages: a cinematic hero, horizontal rails,
// landscape "continue" cards that know the next step, poster cards and the Stats view.
final class NoirUi {
 final MainActivity a;
 NoirUi(MainActivity activity){a=activity;}
 static final String RESUME="resume",READY="ready",MISSING="missing",UPCOMING="upcoming";
 static final class Next{JSONObject item;String kind;long activity;long airsAt;Next(JSONObject i,String k,long at,long airs){item=i;kind=k;activity=at;airsAt=airs;}}

 // Mirrors the PC's up-next rules: resume, the next local episode, a released episode that is
 // not on the PC yet (requestable as a PC download), or the next announced air date.
 Next next(List<JSONObject> g){
  JSONObject last=null;long at=0;
  for(JSONObject i:g){if(!(MainActivity.position(i)>0||MainActivity.completed(i)||DataStore.watched(i)))continue;long r=DataStore.recent(i);if(last==null||r>at){last=i;at=r;}}
  if(last==null)return null;
  boolean finished=MainActivity.completed(last)||DataStore.watched(last)&&MainActivity.position(last)<=0;
  if(!finished)return new Next(last,a.playable(last)?RESUME:MISSING,at,0);
  if(last.optBoolean("movie"))return null;
  JSONObject local=a.continuation(g);if(local!=null)return new Next(local,READY,at,0);
  int s=last.optInt("season"),e=last.optInt("episode");String group=last.optString("group");
  JSONObject best=null;
  JSONObject schedules=a.browsingSettings().optJSONObject("schedules");
  for(JSONObject ep:DataStore.objects(schedules==null?null:schedules.optJSONArray(group))){
   int es=ep.optInt("season"),ee=ep.optInt("episode");if(es<=0||ee<=0||(s==0)!=(es==0))continue;
   if(!(es>s||es==s&&ee>e))continue;
   JSONObject known=g.stream().filter(i->i.optInt("season")==es&&i.optInt("episode")==ee).findFirst().orElse(null);
   if(known!=null&&DataStore.watched(known))continue;
   if(best==null||es<best.optInt("season")||es==best.optInt("season")&&ee<best.optInt("episode"))best=ep;
  }
  if(best==null)return null;
  final JSONObject chosen=best;
  JSONObject known=g.stream().filter(i->i.optInt("season")==chosen.optInt("season")&&i.optInt("episode")==chosen.optInt("episode")).findFirst().orElse(null);
  JSONObject item=known!=null?known:placeholder(last,chosen);
  long airs=DataStore.date(chosen.optString("airsAt"));
  if(airs>System.currentTimeMillis())return new Next(item,UPCOMING,at,airs);
  return new Next(item,a.playable(item)?READY:MISSING,at,airs);
 }
 // An episode known only from the schedule, using the PC's identifier for such episodes.
 static JSONObject placeholder(JSONObject show,JSONObject ep){
  JSONObject i=new JSONObject();String group=show.optString("group");int s=ep.optInt("season"),e=ep.optInt("episode");
  DataStore.put(i,"id","episode-"+sha(group+":"+s+":"+e));DataStore.put(i,"group",group);DataStore.put(i,"title",show.optString("title"));
  DataStore.put(i,"season",s);DataStore.put(i,"episode",e);DataStore.put(i,"episodeTitle",ep.optString("title"));DataStore.put(i,"poster",show.optString("poster"));
  DataStore.put(i,"backdrop",show.optString("backdrop"));DataStore.put(i,"available",false);DataStore.put(i,"placeholder",true);return i;
 }
 static String sha(String text){try{byte[] d=MessageDigest.getInstance("SHA-256").digest(text.getBytes(StandardCharsets.UTF_8));StringBuilder b=new StringBuilder();for(byte x:d)b.append(String.format(Locale.ROOT,"%02X",x));return b.toString();}catch(Exception e){return "";}}
 boolean isNew(JSONObject i){if(i.optBoolean("movie")||DataStore.watched(i))return false;JSONObject schedules=a.browsingSettings().optJSONObject("schedules");for(JSONObject ep:DataStore.objects(schedules==null?null:schedules.optJSONArray(i.optString("group"))))if(ep.optInt("season")==i.optInt("season")&&ep.optInt("episode")==i.optInt("episode")){long at=DataStore.date(ep.optString("airsAt"));return at>0&&at<=System.currentTimeMillis()&&at>System.currentTimeMillis()-14L*86400000;}return false;}
 List<Next> continuing(){List<Next> list=new ArrayList<>();long recent=System.currentTimeMillis()-120L*86400000;for(List<JSONObject> g:a.groups()){if(g.get(0).optString("status").equals("Dropped"))continue;Next n=next(g);if(n==null)continue;if((n.kind.equals(MISSING)||n.kind.equals(UPCOMING))&&n.activity<recent)continue;list.add(n);}list.sort((x,y)->{boolean wx=x.kind.equals(UPCOMING),wy=y.kind.equals(UPCOMING);if(wx!=wy)return wx?1:-1;return Long.compare(y.activity,x.activity);});return list;}
 void act(Next n){switch(n.kind){case UPCOMING:a.open(n.item);break;case MISSING:a.pcDownload(n.item,false);break;default:a.play(n.item,false);}}
 String action(Next n){switch(n.kind){case RESUME:return MainActivity.position(n.item)>=1?"Resume "+DataStore.time(MainActivity.position(n.item)):"Play "+MainActivity.episode(n.item);case MISSING:return "Get it on your PC";case UPCOMING:return "Airs "+date(n.airsAt);default:return "Play "+MainActivity.episode(n.item);}}
 static String date(long at){return Instant.ofEpochMilli(at).atZone(ZoneId.systemDefault()).toLocalDate().format(java.time.format.DateTimeFormatter.ofPattern("EEE d MMM"));}
 static String ago(long at){long m=(System.currentTimeMillis()-at)/60000;if(m<2)return "just now";if(m<60)return m+" min ago";if(m<1440)return (m/60)+" h ago";if(m<2880)return "yesterday";if(m<10080)return Instant.ofEpochMilli(at).atZone(ZoneId.systemDefault()).getDayOfWeek().getDisplayName(java.time.format.TextStyle.FULL,Locale.getDefault());return date(at);}

 // ---- building blocks ----
 GradientDrawable fade(int top,int bottom){return new GradientDrawable(GradientDrawable.Orientation.TOP_BOTTOM,new int[]{top,bottom});}
 TextView pill(String s,int fg,int bgColor){TextView t=a.text(s,11,fg);t.setTypeface(null,Typeface.BOLD);t.setPadding(a.dp(9),a.dp(3),a.dp(9),a.dp(3));t.setBackground(MainActivity.bg(bgColor,a.dp(8)));return t;}
 TextView chip(String s,Runnable action,boolean primary){TextView t=a.text(s,14,primary?MainActivity.BLACK:MainActivity.WHITE);t.setTypeface(null,primary?Typeface.BOLD:Typeface.NORMAL);t.setGravity(Gravity.CENTER);t.setSingleLine();t.setPadding(a.dp(18),a.dp(11),a.dp(18),a.dp(11));t.setBackground(MainActivity.bg(primary?MainActivity.GOLD:Color.argb(70,255,255,255),a.dp(24)));t.setOnClickListener(v->action.run());LinearLayout.LayoutParams p=new LinearLayout.LayoutParams(-2,-2);p.setMargins(0,0,a.dp(8),a.dp(8));t.setLayoutParams(p);return t;}
 HorizontalScrollView row(LinearLayout content){HorizontalScrollView scroll=new HorizontalScrollView(a);scroll.setHorizontalScrollBarEnabled(false);scroll.setClipToPadding(false);content.setPadding(0,0,a.dp(22),0);scroll.addView(content);return scroll;}
 void rail(LinearLayout host,String title,String subtitle,List<View> cards,Runnable seeAll){
  if(cards.isEmpty())return;
  LinearLayout header=new LinearLayout(a);header.setGravity(Gravity.BOTTOM);header.setPadding(0,a.dp(26),0,a.dp(12));
  LinearLayout titles=a.column();TextView t=a.text(title,20,MainActivity.WHITE);t.setTypeface(null,Typeface.BOLD);titles.addView(t);if(!subtitle.isEmpty())titles.addView(a.text(subtitle,12,MainActivity.MUTED));header.addView(titles,new LinearLayout.LayoutParams(0,-2,1));
  if(seeAll!=null){TextView all=a.text("See all ›",13,MainActivity.GOLD);all.setPadding(a.dp(10),a.dp(6),0,a.dp(6));all.setOnClickListener(v->seeAll.run());header.addView(all);}
  host.addView(header);LinearLayout strip=new LinearLayout(a);for(View c:cards)strip.addView(c);host.addView(row(strip));
 }
 FrameLayout art(JSONObject i,boolean portrait,int w,int h){FrameLayout f=new FrameLayout(a);f.setClipToOutline(true);f.setBackground(MainActivity.bg(MainActivity.PANEL,a.dp(14)));f.addView(a.poster(i,portrait),new FrameLayout.LayoutParams(-1,-1));f.setLayoutParams(new LinearLayout.LayoutParams(w,h));return f;}
 View continueCard(Next n){
  JSONObject i=n.item;LinearLayout card=a.column();LinearLayout.LayoutParams cp=new LinearLayout.LayoutParams(a.dp(264),-2);cp.setMargins(0,0,a.dp(14),0);card.setLayoutParams(cp);
  FrameLayout art=art(i,false,-1,a.dp(148));
  View shade=new View(a);shade.setBackground(fade(Color.TRANSPARENT,Color.argb(n.kind.equals(MISSING)||n.kind.equals(UPCOMING)?200:150,0,0,0)));art.addView(shade,new FrameLayout.LayoutParams(-1,-1));
  String center=n.kind.equals(MISSING)?"↓  GET IT ON PC":n.kind.equals(UPCOMING)?"NEW EPISODE SOON":"▶";
  TextView mark=a.text(center,center.equals("▶")?24:12,MainActivity.GOLD);mark.setTypeface(null,Typeface.BOLD);mark.setGravity(Gravity.CENTER);mark.setBackground(MainActivity.bg(Color.argb(200,12,12,14),a.dp(center.equals("▶")?28:16)));mark.setPadding(a.dp(14),a.dp(6),a.dp(14),a.dp(6));
  art.addView(mark,center.equals("▶")?new FrameLayout.LayoutParams(a.dp(56),a.dp(56),Gravity.CENTER):new FrameLayout.LayoutParams(-2,-2,Gravity.CENTER));
  if(isNew(i)&&!n.kind.equals(UPCOMING)){TextView badge=pill("NEW EPISODE",MainActivity.BLACK,MainActivity.GOLD);FrameLayout.LayoutParams bp=new FrameLayout.LayoutParams(-2,-2,Gravity.TOP|Gravity.START);bp.setMargins(a.dp(10),a.dp(10),0,0);art.addView(badge,bp);}
  JSONObject p=i.optJSONObject("progress");double d=p==null?i.optDouble("duration",0):p.optDouble("duration",i.optDouble("duration",0));
  if(n.kind.equals(RESUME)&&d>0){ProgressBar bar=new ProgressBar(a,null,android.R.attr.progressBarStyleHorizontal);bar.setMax(1000);bar.setProgress((int)(1000*MainActivity.position(i)/d));bar.setProgressTintList(android.content.res.ColorStateList.valueOf(MainActivity.GOLD));art.addView(bar,new FrameLayout.LayoutParams(-1,a.dp(6),Gravity.BOTTOM));}
  art.setContentDescription(action(n)+" · "+i.optString("title")+" "+MainActivity.episode(i));art.setOnClickListener(v->act(n));card.addView(art);
  TextView title=a.text(i.optString("title"),15,MainActivity.WHITE);title.setTypeface(null,Typeface.BOLD);title.setSingleLine();title.setEllipsize(TextUtils.TruncateAt.END);title.setPadding(0,a.dp(9),0,a.dp(2));title.setOnClickListener(v->a.open(i));card.addView(title);
  TextView sub=a.text(MainActivity.episode(i)+" · "+action(n),12,n.kind.equals(MISSING)?MainActivity.GOLD:MainActivity.MUTED);sub.setSingleLine();sub.setEllipsize(TextUtils.TruncateAt.END);card.addView(sub);
  card.setOnLongClickListener(v->{if(!i.optBoolean("placeholder"))a.options(i);return true;});return card;
 }
 View posterCard(List<JSONObject> g){
  JSONObject i=g.get(0);LinearLayout card=a.column();LinearLayout.LayoutParams cp=new LinearLayout.LayoutParams(a.dp(128),-2);cp.setMargins(0,0,a.dp(12),0);card.setLayoutParams(cp);
  FrameLayout art=art(i,true,-1,a.dp(190));long watched=g.stream().filter(DataStore::watched).count();
  if(watched>0&&!i.optBoolean("movie")){ProgressBar bar=new ProgressBar(a,null,android.R.attr.progressBarStyleHorizontal);bar.setMax(g.size());bar.setProgress((int)watched);bar.setProgressTintList(android.content.res.ColorStateList.valueOf(MainActivity.GOLD));art.addView(bar,new FrameLayout.LayoutParams(-1,a.dp(6),Gravity.BOTTOM));}
  if(i.optBoolean("movie")&&watched>0){TextView w=pill("✓ Watched",MainActivity.GOLD,Color.argb(220,12,12,14));FrameLayout.LayoutParams wp=new FrameLayout.LayoutParams(-2,-2,Gravity.BOTTOM|Gravity.START);wp.setMargins(a.dp(8),0,0,a.dp(8));art.addView(w,wp);}
  card.addView(art);TextView title=a.text(i.optString("title"),13,MainActivity.WHITE);title.setTypeface(null,Typeface.BOLD);title.setSingleLine();title.setEllipsize(TextUtils.TruncateAt.END);title.setPadding(0,a.dp(8),0,0);card.addView(title);
  TextView meta=a.text(i.optBoolean("movie")?(i.optInt("year")>0?""+i.optInt("year"):"Movie"):watched+" / "+g.size()+" watched",11,MainActivity.MUTED);meta.setSingleLine();card.addView(meta);
  card.setOnClickListener(v->a.open(i));return card;
 }
 // Full-width artwork with the title, details and the main action over a dark fade.
 void hero(LinearLayout host,JSONObject i,List<JSONObject> g,String eyebrow,String primary,Runnable act,boolean onDetail){
  FrameLayout hero=new FrameLayout(a);hero.setClipToOutline(true);hero.setBackground(MainActivity.bg(MainActivity.PANEL,a.dp(20)));
  hero.addView(a.poster(i,false),new FrameLayout.LayoutParams(-1,-1));View shade=new View(a);shade.setBackground(fade(Color.argb(30,12,12,14),Color.argb(250,12,12,14)));hero.addView(shade,new FrameLayout.LayoutParams(-1,-1));
  LinearLayout info=a.column();info.setPadding(a.dp(18),0,a.dp(18),a.dp(16));FrameLayout.LayoutParams ip=new FrameLayout.LayoutParams(-1,-2,Gravity.BOTTOM);
  if(!eyebrow.isEmpty()){TextView e=a.text(eyebrow,11,MainActivity.GOLD);e.setTypeface(null,Typeface.BOLD);e.setLetterSpacing(.08f);info.addView(e);}
  TextView name=a.text(i.optString("title"),onDetail?28:30,MainActivity.WHITE);name.setTypeface(null,Typeface.BOLD);name.setPadding(0,a.dp(4),0,a.dp(6));info.addView(name);
  String genres=i.optString("genres");if(!genres.isEmpty()){LinearLayout pills=new LinearLayout(a);int n=0;for(String gn:genres.split("·")){if(n++>=3)break;TextView pl=pill(gn.trim(),MainActivity.WHITE,Color.argb(60,255,255,255));pl.setTypeface(null,Typeface.NORMAL);LinearLayout.LayoutParams pp=new LinearLayout.LayoutParams(-2,-2);pp.setMargins(0,0,a.dp(6),a.dp(8));pills.addView(pl,pp);}info.addView(pills);}
  info.addView(a.text(a.count(g),12,Color.rgb(205,200,192)));
  LinearLayout actions=new LinearLayout(a);actions.setPadding(0,a.dp(12),0,0);actions.addView(chip(primary,act,true));
  if(!onDetail)actions.addView(chip(i.optBoolean("movie")?"Details":"Episodes",()->a.open(i),false));
  boolean listed=i.optBoolean("listed");actions.addView(chip(listed?"✓ My List":"+ My List",()->{JSONObject op=a.data.operation(i.optString("id"),"list");DataStore.put(op,"value",!listed);a.data.queue(op);a.data.syncSoon();a.render();},false));
  HorizontalScrollView actionRow=row(actions);info.addView(actionRow);hero.addView(info,ip);
  LinearLayout.LayoutParams hp=new LinearLayout.LayoutParams(-1,a.dp(onDetail?360:420));hp.setMargins(0,a.dp(4),0,a.dp(4));host.addView(hero,hp);
 }

 // ---- Calendar cards for announced episodes ----
 View calendarCard(JSONObject show,JSONObject ep){
  long at=DataStore.date(ep.optString("airsAt"));ZonedDateTime when=Instant.ofEpochMilli(at).atZone(ZoneId.systemDefault());
  LinearLayout card=a.column();LinearLayout.LayoutParams cp=new LinearLayout.LayoutParams(a.dp(264),-2);cp.setMargins(0,0,a.dp(14),0);card.setLayoutParams(cp);
  FrameLayout art=art(show,false,-1,a.dp(148));View shade=new View(a);shade.setBackground(fade(Color.argb(40,0,0,0),Color.argb(235,10,10,12)));art.addView(shade,new FrameLayout.LayoutParams(-1,-1));
  LinearLayout date=a.column();date.setGravity(Gravity.CENTER);date.setPadding(a.dp(12),a.dp(6),a.dp(12),a.dp(6));GradientDrawable db=MainActivity.bg(Color.argb(230,16,16,18),a.dp(12));db.setStroke(a.dp(1),Color.argb(90,228,192,132));date.setBackground(db);
  TextView mo=a.text(when.getMonth().getDisplayName(java.time.format.TextStyle.SHORT,Locale.getDefault()).toUpperCase(Locale.ROOT),10,MainActivity.GOLD);mo.setTypeface(null,Typeface.BOLD);date.addView(mo);TextView day=a.text(""+when.getDayOfMonth(),24,MainActivity.WHITE);day.setTypeface(null,Typeface.BOLD);day.setLineSpacing(0,1);date.addView(day);
  date.addView(a.text(when.getDayOfWeek().getDisplayName(java.time.format.TextStyle.SHORT,Locale.getDefault()).toUpperCase(Locale.ROOT),10,MainActivity.MUTED));
  FrameLayout.LayoutParams dp=new FrameLayout.LayoutParams(-2,-2,Gravity.TOP|Gravity.START);dp.setMargins(a.dp(10),a.dp(10),0,0);art.addView(date,dp);
  long days=java.time.temporal.ChronoUnit.DAYS.between(LocalDate.now(),when.toLocalDate());TextView in=pill(days<=0?"Tonight":days==1?"Tomorrow":days<7?"In "+days+" days":date(at),MainActivity.BLACK,MainActivity.GOLD);FrameLayout.LayoutParams ip=new FrameLayout.LayoutParams(-2,-2,Gravity.TOP|Gravity.END);ip.setMargins(0,a.dp(10),a.dp(10),0);art.addView(in,ip);
  LinearLayout caption=a.column();caption.setPadding(a.dp(12),0,a.dp(12),a.dp(10));TextView t=a.text(show.optString("title"),15,MainActivity.WHITE);t.setTypeface(null,Typeface.BOLD);t.setSingleLine();caption.addView(t);caption.addView(a.text(String.format(Locale.ROOT,"S%02d · E%02d · %02d:%02d",ep.optInt("season"),ep.optInt("episode"),when.getHour(),when.getMinute()),11,Color.rgb(217,212,204)));art.addView(caption,new FrameLayout.LayoutParams(-1,-2,Gravity.BOTTOM));
  art.setOnClickListener(v->a.open(show));card.addView(art);return card;
 }
 void comingUp(LinearLayout host){
  JSONObject schedules=a.browsingSettings().optJSONObject("schedules");if(schedules==null)return;List<View> cards=new ArrayList<>();List<Object[]> found=new ArrayList<>();
  for(List<JSONObject> g:a.groups()){JSONObject best=null;for(JSONObject ep:DataStore.objects(schedules.optJSONArray(g.get(0).optString("group")))){long at=DataStore.date(ep.optString("airsAt"));if(ep.optInt("season")>0&&at>System.currentTimeMillis()&&at<System.currentTimeMillis()+45L*86400000&&(best==null||at<DataStore.date(best.optString("airsAt"))))best=ep;}if(best!=null)found.add(new Object[]{g.get(0),best});}
  found.sort(Comparator.comparingLong(x->DataStore.date(((JSONObject)x[1]).optString("airsAt"))));for(Object[] x:found)cards.add(calendarCard((JSONObject)x[0],(JSONObject)x[1]));
  rail(host,"Coming up","The next announced episode of each show you have.",cards,null);
 }
 // ---- History ----
 void latest(LinearLayout host){
  List<List<JSONObject>> groups=a.groups();groups.removeIf(g->g.stream().noneMatch(i->DataStore.watched(i)||MainActivity.position(i)>0));groups.sort((x,y)->Long.compare(MainActivity.recent(y),MainActivity.recent(x)));
  List<View> cards=new ArrayList<>();for(List<JSONObject> g:groups.subList(0,Math.min(14,groups.size())))cards.add(latestCard(g));
  rail(host,"Latest","Your most recent viewing, with the next step on each card.",cards,null);
 }
 View latestCard(List<JSONObject> g){
  JSONObject last=g.stream().max(Comparator.comparingLong(DataStore::recent)).orElse(g.get(0));
  LinearLayout card=a.column();LinearLayout.LayoutParams cp=new LinearLayout.LayoutParams(a.dp(270),-2);cp.setMargins(0,0,a.dp(14),0);card.setLayoutParams(cp);card.setPadding(0,0,0,a.dp(4));
  FrameLayout art=art(last,false,-1,a.dp(150));View shade=new View(a);shade.setBackground(fade(Color.TRANSPARENT,Color.argb(235,10,10,12)));art.addView(shade,new FrameLayout.LayoutParams(-1,-1));
  boolean finished=MainActivity.completed(last)||DataStore.watched(last)&&MainActivity.position(last)<=0;
  TextView when=pill((finished?"✓ Watched · ":MainActivity.position(last)>=1?"Stopped at "+DataStore.time(MainActivity.position(last))+" · ":"Started · ")+ago(DataStore.recent(last)),MainActivity.GOLD,Color.argb(220,12,12,14));
  FrameLayout.LayoutParams wp=new FrameLayout.LayoutParams(-2,-2,Gravity.TOP|Gravity.START);wp.setMargins(a.dp(10),a.dp(10),0,0);art.addView(when,wp);
  LinearLayout caption=a.column();caption.setPadding(a.dp(12),0,a.dp(12),a.dp(10));TextView t=a.text(last.optString("title"),16,MainActivity.WHITE);t.setTypeface(null,Typeface.BOLD);t.setSingleLine();t.setEllipsize(TextUtils.TruncateAt.END);caption.addView(t);
  boolean spoilerFree=!a.browsingSettings().optBoolean("hideSpoilers",true)||DataStore.watched(last);
  TextView s=a.text(MainActivity.episode(last)+(spoilerFree&&!last.optString("episodeTitle").isEmpty()?" · "+last.optString("episodeTitle"):""),11,Color.rgb(217,212,204));s.setSingleLine();s.setEllipsize(TextUtils.TruncateAt.END);caption.addView(s);art.addView(caption,new FrameLayout.LayoutParams(-1,-2,Gravity.BOTTOM));
  art.setOnClickListener(v->a.open(last));card.addView(art);
  LinearLayout actions=new LinearLayout(a);actions.setPadding(0,a.dp(10),0,0);
  if(!finished){if(a.playable(last))actions.addView(small("▶ Resume",()->a.play(last,false),true));}
  else{Next n=last.optBoolean("movie")?null:next(g);if(n!=null)actions.addView(small(n.kind.equals(MISSING)?"↓ Get "+compact(n.item):n.kind.equals(UPCOMING)?"Airs "+date(n.airsAt):"▶ "+compact(n.item),()->act(n),!n.kind.equals(UPCOMING)));else if(a.playable(last))actions.addView(small("↻ Again",()->a.play(last,true),false));}
  JSONObject review=last.optJSONObject("review");int stars=review==null?0:review.optInt("rating");
  actions.addView(small(stars>0?String.join("",Collections.nCopies(stars,"★")):"☆ Rate",()->a.rating(last),false));
  actions.addView(small("💬",()->a.reviews(last),false));
  card.addView(actions);return card;
 }
 static String compact(JSONObject i){return String.format(Locale.ROOT,"S%02dE%02d",i.optInt("season"),i.optInt("episode"));}
 TextView small(String s,Runnable action,boolean primary){TextView t=a.text(s,12,primary?MainActivity.BLACK:MainActivity.WHITE);t.setTypeface(null,primary?Typeface.BOLD:Typeface.NORMAL);t.setSingleLine();t.setPadding(a.dp(12),a.dp(7),a.dp(12),a.dp(7));t.setBackground(MainActivity.bg(primary?MainActivity.GOLD:MainActivity.PANEL,a.dp(16)));t.setOnClickListener(v->action.run());LinearLayout.LayoutParams p=new LinearLayout.LayoutParams(-2,-2);p.setMargins(0,0,a.dp(6),0);t.setLayoutParams(p);return t;}

 double runtime(JSONObject i){JSONObject p=i.optJSONObject("progress");double d=p==null?0:p.optDouble("duration",0);if(d<60)d=i.optDouble("duration",0);return d>=60?d:i.optBoolean("movie")?6600:2580;}
 static String span(double seconds){long m=(long)(seconds/60),d=m/1440,h=(m%1440)/60,min=m%60;StringBuilder b=new StringBuilder();if(d>0)b.append(d).append(d==1?" day ":" days ");if(h>0)b.append(h).append(h==1?" hour ":" hours ");if(d==0&&min>0)b.append(min).append(" min");String r=b.toString().trim();return r.isEmpty()?"0 min":r;}
 View stat(String value,String label){LinearLayout c=a.column();c.setPadding(a.dp(16),a.dp(14),a.dp(16),a.dp(14));c.setBackground(MainActivity.bg(MainActivity.PANEL,a.dp(16)));TextView v=a.text(value,24,MainActivity.GOLD);v.setTypeface(null,Typeface.BOLD);c.addView(v);c.addView(a.text(label,11,MainActivity.MUTED));LinearLayout.LayoutParams p=new LinearLayout.LayoutParams(-2,-2);p.setMargins(0,0,a.dp(10),0);c.setLayoutParams(p);return c;}
 View meter(String label,double fraction,String value){LinearLayout row=new LinearLayout(a);row.setGravity(Gravity.CENTER_VERTICAL);row.setPadding(0,a.dp(6),0,a.dp(6));TextView l=a.text(label,13,Color.rgb(199,194,188));row.addView(l,new LinearLayout.LayoutParams(a.dp(96),-2));
  ProgressBar bar=new ProgressBar(a,null,android.R.attr.progressBarStyleHorizontal);bar.setMax(1000);bar.setProgress((int)(1000*Math.max(0,Math.min(1,fraction))));bar.setProgressTintList(android.content.res.ColorStateList.valueOf(MainActivity.GOLD));bar.setProgressBackgroundTintList(android.content.res.ColorStateList.valueOf(Color.rgb(50,48,52)));row.addView(bar,new LinearLayout.LayoutParams(0,a.dp(8),1));
  TextView v=a.text(value,12,MainActivity.MUTED);v.setGravity(Gravity.END);row.addView(v,new LinearLayout.LayoutParams(a.dp(64),-2));return row;}
 void stats(LinearLayout host){
  List<JSONObject[]> views=new ArrayList<>();for(JSONObject i:DataStore.objects(a.data.items()))for(JSONObject v:DataStore.objects(i.optJSONArray("viewings")))views.add(new JSONObject[]{i,v});
  if(views.isEmpty()){a.paragraph("Watch time, streaks, favourite shows and milestones appear here after your first finished episode.");return;}
  double total=0,month=0;int episodes=0,movies=0;Set<String> shows=new HashSet<>();Map<String,Double> byShow=new HashMap<>(),byGenre=new HashMap<>();Map<String,Integer> showCount=new HashMap<>();TreeSet<LocalDate> days=new TreeSet<>();int phone=0;int[] parts=new int[4];
  LocalDate first=LocalDate.now().withDayOfMonth(1);
  for(JSONObject[] x:views){JSONObject i=x[0];double r=runtime(i);total+=r;ZonedDateTime at=Instant.ofEpochMilli(DataStore.date(x[1].optString("watchedAt"))).atZone(ZoneId.systemDefault());days.add(at.toLocalDate());if(!at.toLocalDate().isBefore(first))month+=r;
   if(i.optBoolean("movie"))movies++;else{episodes++;shows.add(i.optString("group"));byShow.merge(i.optString("group"),r,Double::sum);showCount.merge(i.optString("group"),1,Integer::sum);}
   for(String gn:i.optString("genres").split("·")){String k=gn.trim();if(!k.isEmpty())byGenre.merge(k,r,Double::sum);}
   if(x[1].optString("source").contains("Android"))phone++;int h=at.getHour();parts[h>=5&&h<12?0:h>=12&&h<17?1:h>=17&&h<22?2:3]++;}
  int longest=0,run=0;LocalDate prev=null;for(LocalDate d:days){run=prev!=null&&prev.plusDays(1).equals(d)?run+1:1;longest=Math.max(longest,run);prev=d;}
  int current=0;LocalDate c=days.contains(LocalDate.now())?LocalDate.now():LocalDate.now().minusDays(1);while(days.contains(c)){current++;c=c.minusDays(1);}
  LinearLayout headline=a.column();headline.setPadding(a.dp(20),a.dp(18),a.dp(20),a.dp(18));headline.setBackground(new GradientDrawable(GradientDrawable.Orientation.TL_BR,new int[]{Color.rgb(42,36,28),Color.rgb(20,20,22)}){{setCornerRadius(a.dp(20));}});
  TextView eb=a.text("TIME WATCHED",11,MainActivity.GOLD);eb.setTypeface(null,Typeface.BOLD);headline.addView(eb);TextView big=a.text(span(total),30,MainActivity.WHITE);big.setTypeface(null,Typeface.BOLD);big.setPadding(0,a.dp(4),0,a.dp(4));headline.addView(big);
  headline.addView(a.text(String.format(Locale.ROOT,"%.1f h this month · %d viewings",month/3600,views.size()),12,Color.rgb(199,194,188)));
  LinearLayout.LayoutParams hp=new LinearLayout.LayoutParams(-1,-2);hp.setMargins(0,a.dp(12),0,a.dp(12));host.addView(headline,hp);
  LinearLayout cards=new LinearLayout(a);cards.addView(stat(""+episodes,"episodes"));cards.addView(stat(""+shows.size(),"shows"));cards.addView(stat(""+movies,"movies"));cards.addView(stat(""+(current>0?current:longest),current>0?"day streak":"best streak"));cards.addView(stat(phone*100/views.size()+"%","on this phone"));host.addView(row(cards));
  a.heading("When you watch");String[] names={"Morning","Afternoon","Evening","Late night"};for(int n=0;n<4;n++)host.addView(meter(names[n],parts[n]/(double)views.size(),parts[n]*100/views.size()+"%"));
  List<Map.Entry<String,Double>> top=new ArrayList<>(byShow.entrySet());top.sort((x,y)->Double.compare(y.getValue(),x.getValue()));
  if(!top.isEmpty()){List<View> cardsTop=new ArrayList<>();for(Map.Entry<String,Double> e:top.subList(0,Math.min(6,top.size()))){List<JSONObject> g=null;for(List<JSONObject> gg:a.groups())if(gg.get(0).optString("group").equals(e.getKey()))g=gg;if(g==null)continue;LinearLayout card=(LinearLayout)posterCard(g);card.addView(a.text(String.format(Locale.ROOT,"%.1f h",e.getValue()/3600),11,MainActivity.GOLD));cardsTop.add(card);}rail(host,"Your top shows","By time watched.",cardsTop,null);}
  List<Map.Entry<String,Double>> genres=new ArrayList<>(byGenre.entrySet());genres.sort((x,y)->Double.compare(y.getValue(),x.getValue()));
  if(!genres.isEmpty()){a.heading("Your genres");double max=genres.get(0).getValue();for(Map.Entry<String,Double> e:genres.subList(0,Math.min(5,genres.size())))host.addView(meter(e.getKey(),e.getValue()/max,String.format(Locale.ROOT,"%.1f h",e.getValue()/3600)));}
  a.heading("Milestones");LinearLayout badges=new LinearLayout(a);
  for(int step:new int[]{10,50,100,250,500,1000}){if(step<=episodes)badges.addView(milestone("★ "+step+" episodes","",true));else{badges.addView(milestone("☆ "+step+" episodes",(step-episodes)+" to go",false));break;}}
  for(int step:new int[]{24,100,250,500}){int hours=(int)(total/3600);if(step<=hours)badges.addView(milestone("★ "+step+" hours","",true));else{badges.addView(milestone("☆ "+step+" hours",(step-hours)+" to go",false));break;}}
  if(longest>=7)badges.addView(milestone("★ Week-long streak",longest+" days",true));host.addView(row(badges));
 }
 View milestone(String title,String detail,boolean on){LinearLayout c=a.column();c.setPadding(a.dp(14),a.dp(12),a.dp(14),a.dp(12));GradientDrawable b=MainActivity.bg(on?Color.rgb(34,30,24):Color.rgb(21,21,23),a.dp(14));b.setStroke(a.dp(1),on?Color.rgb(94,78,54):Color.rgb(41,41,45));c.setBackground(b);TextView t=a.text(title,13,on?MainActivity.GOLD:MainActivity.MUTED);t.setTypeface(null,Typeface.BOLD);c.addView(t);if(!detail.isEmpty())c.addView(a.text(detail,11,MainActivity.MUTED));LinearLayout.LayoutParams p=new LinearLayout.LayoutParams(-2,-2);p.setMargins(0,0,a.dp(10),0);c.setLayoutParams(p);return c;}
}
