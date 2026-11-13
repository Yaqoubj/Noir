package app.noir.mobile;
import android.app.*;
import android.content.*;
import android.graphics.Bitmap;
import android.graphics.Canvas;
import android.os.SystemClock;
import android.view.*;
import android.widget.*;
import androidx.test.platform.app.InstrumentationRegistry;
import androidx.test.runner.lifecycle.ActivityLifecycleMonitorRegistry;
import androidx.test.runner.lifecycle.Stage;
import org.json.*;
import org.junit.*;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import static org.junit.Assert.*;

/**
 * Captures every phone screen and every TV screen, in English and Arabic, as PNG files under
 * <external files dir>/ui-shots/<phone|tv>/ so a designer can review them. Changes no app behaviour.
 * Names: <nn>-<screen>-<lang>.png (nn is the same for en and ar). A screen behind FLAG_SECURE also gets <name>-render.png (a View.draw of the decor view).
 * A screen that cannot be reached is written to missing.txt and the run goes on. The run only fails if fewer than 80% of the planned captures exist.
 * Pull with: adb pull /sdcard/Android/data/app.noir.mobile/files/ui-shots
 */
public final class ScreenShotsTest {
 interface Act{void run()throws Exception;}
 final Instrumentation test=InstrumentationRegistry.getInstrumentation();final Context ctx=test.getTargetContext();
 File dir;String platform="",lang="en",current="";int seq,captured;
 final List<String> expected=new ArrayList<>();final List<File> madeCaches=new ArrayList<>();final List<Activity> opened=new ArrayList<>();
 AuditPhoneFixture fx;MainActivity a,ob;DiscoverActivity d;PlayerActivity p;TvActivity tv;
 JSONObject movie,show,bright;

 // ---------------------------------------------------------------- harness
 void init(String kind)throws Exception{platform=kind;dir=new File(ctx.getExternalFilesDir(null),"ui-shots/"+kind);dir.mkdirs();File[] old=dir.listFiles();if(old!=null)for(File f:old)if(f.getName().endsWith(".png")||f.getName().equals("missing.txt")||f.getName().equals("summary.txt"))f.delete();}
 void ui(Act r)throws Exception{Throwable[] err={null};test.runOnMainSync(()->{try{r.run();}catch(Throwable t){err[0]=t;}});if(err[0]!=null){if(err[0] instanceof Exception)throw (Exception)err[0];if(err[0] instanceof Error)throw (Error)err[0];throw new RuntimeException(err[0]);}}
 void settle(long ms)throws Exception{test.waitForIdleSync();Thread.sleep(ms);test.waitForIdleSync();}
 void line(String text){android.util.Log.w("ScreenShots",text);try(FileWriter w=new FileWriter(new File(dir,"missing.txt"),true)){w.write(text+"\n");}catch(IOException ignored){}}
 void note(String text){line("NOTE "+current+": "+text);}
 void missing(String what,Throwable t){line(what+": "+t.getClass().getSimpleName()+": "+String.valueOf(t.getMessage()).replace('\n',' '));}
 void step(String name,Act body){seq++;current=String.format(Locale.ROOT,"%02d-%s-%s",seq,name,lang);expected.add(current);try{body.run();}catch(Throwable t){missing(current,t);}finally{try{Activity top=resumed();if(top!=null)closeDialogs(top);}catch(Throwable ignored){}}}
 Activity resumed()throws Exception{Activity[] r={null};test.runOnMainSync(()->{for(Activity x:ActivityLifecycleMonitorRegistry.getInstance().getActivitiesInStage(Stage.RESUMED))r[0]=x;});return r[0];}
 <T extends Activity> T waitFor(Class<T> c,long ms)throws Exception{long end=SystemClock.elapsedRealtime()+ms;while(SystemClock.elapsedRealtime()<end){Activity x=resumed();if(c.isInstance(x))return c.cast(x);Thread.sleep(60);}throw new AssertionError("Timed out waiting for "+c.getSimpleName());}
 Intent in(Class<?> c){return new Intent(ctx,c).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);}
 @SuppressWarnings("unchecked") <T extends Activity> T start(Intent i)throws Exception{Activity x=test.startActivitySync(i);if(x==null)throw new AssertionError("activity did not start");opened.add(x);test.waitForIdleSync();return (T)x;}
 void end(Activity x){if(x==null)return;try{ui(()->{if(!x.isFinishing())x.finish();});settle(350);}catch(Throwable ignored){}}
 static Activity owner(Context c){while(c!=null){if(c instanceof Activity)return (Activity)c;if(!(c instanceof ContextWrapper))return null;c=((ContextWrapper)c).getBaseContext();}return null;}
 int dialogs(Activity act)throws Exception{int[] n={0};ui(()->{for(View r:android.view.inspector.WindowInspector.getGlobalWindowViews()){if(r==act.getWindow().getDecorView()||!r.isShown())continue;ViewGroup.LayoutParams lp=r.getLayoutParams();if(lp instanceof WindowManager.LayoutParams&&((WindowManager.LayoutParams)lp).type==WindowManager.LayoutParams.TYPE_APPLICATION&&owner(r.getContext())==act)n[0]++;}});return n[0];}
 void closeDialogs(Activity act)throws Exception{for(int k=0;k<6&&dialogs(act)>0;k++){test.sendKeyDownUpSync(KeyEvent.KEYCODE_BACK);settle(300);}}
 void key(int code)throws Exception{test.sendKeyDownUpSync(code);test.waitForIdleSync();Thread.sleep(250);}
 // -e shotsLang en|ar runs one language per instrumentation run (the app process stays smaller); default both.
 static String[] langs(){String one=TestRunner.arguments.getString("shotsLang");return one==null||one.isEmpty()?new String[]{"en","ar"}:new String[]{one};}
 void closeActivities()throws Exception{for(int n=0;n<3;n++){test.runOnMainSync(()->{for(Stage st:new Stage[]{Stage.RESUMED,Stage.PAUSED,Stage.STOPPED,Stage.CREATED,Stage.STARTED})for(Activity a:new java.util.ArrayList<>(ActivityLifecycleMonitorRegistry.getInstance().getActivitiesInStage(st)))a.finish();});test.waitForIdleSync();Thread.sleep(300);}Runtime.getRuntime().gc();}
 void save(Bitmap b,String name)throws IOException{try(FileOutputStream o=new FileOutputStream(new File(dir,name))){b.compress(Bitmap.CompressFormat.PNG,100,o);}}
 void renderOf(Activity act,String name)throws Exception{Bitmap[] out={null};ui(()->{View v=act.getWindow().getDecorView();if(v.getWidth()<=0||v.getHeight()<=0)return;Bitmap bm=Bitmap.createBitmap(v.getWidth(),v.getHeight(),Bitmap.Config.ARGB_8888);v.draw(new Canvas(bm));out[0]=bm;});if(out[0]==null)throw new AssertionError("no decor view to render");save(out[0],name);out[0].recycle();}
 /** Waits for the UI to settle, then saves the screen (plus a View render when the window is FLAG_SECURE, whose screenshot is black). */
 void shot()throws Exception{shot(900);}
 void shot(long settleMs)throws Exception{settle(settleMs);Activity top=resumed();boolean[] secure={false};if(top!=null)ui(()->secure[0]=(top.getWindow().getAttributes().flags&WindowManager.LayoutParams.FLAG_SECURE)!=0);Bitmap b=test.getUiAutomation().takeScreenshot();boolean ok=false;if(b!=null){save(b,current+".png");b.recycle();ok=true;}if(secure[0]&&top!=null){renderOf(top,current+"-render.png");ok=true;}if(!ok)throw new AssertionError("screenshot returned null");captured++;}
 View byDesc(View root,String english){String want=Loc.t(english);return AuditPhoneFixture.find(root,v->v.getContentDescription()!=null&&want.contentEquals(v.getContentDescription())&&v.hasOnClickListeners());}
 View clickable(View v){while(v!=null&&!v.hasOnClickListeners()){ViewParent p=v.getParent();v=p instanceof View?(View)p:null;}return v;}
 void clickPrefix(View root,String prefix)throws Exception{View t=AuditPhoneFixture.find(root,v->v instanceof TextView&&((TextView)v).getText().toString().startsWith(prefix)&&clickable(v)!=null);assertNotNull("control not found: "+prefix,t);assertTrue(clickable(t).performClick());}
 int dp(float n){return Math.round(n*ctx.getResources().getDisplayMetrics().density);}
 void setLang()throws Exception{NoirApp.data.pref("language",lang);Loc.use(NoirApp.data);}
 File cacheFile(DiscoveryClient c,String path,JSONObject value)throws Exception{File f=new File(c.directory,DiscoveryClient.hash(path)+".json");if(f.exists())return f;DiscoveryClient.write(f,value.toString().getBytes(StandardCharsets.UTF_8));madeCaches.add(f);return f;}
 void dropCaches(){for(File f:madeCaches){f.delete();new File(f+".bak").delete();new File(f+".new").delete();}madeCaches.clear();}
 void finishRun()throws Exception{
  for(Activity x:new ArrayList<>(opened))end(x);opened.clear();dropCaches();
  int have=0;for(String n:expected)if(new File(dir,n+".png").exists())have++;
  String summary=platform+": "+have+" of "+expected.size()+" planned captures saved ("+captured+" shot calls succeeded)";android.util.Log.i("ScreenShots",summary);
  try(FileWriter w=new FileWriter(new File(dir,"summary.txt"))){w.write(summary+"\n");}catch(IOException ignored){}
  assertTrue(summary+"; see missing.txt",expected.size()>0&&have*5>=expected.size()*4);
 }

 // ---------------------------------------------------------------- fixture data
 JSONArray fakeTitles(String prefix,String kind,int base,int n)throws Exception{JSONArray metas=new JSONArray();for(int i=0;i<n;i++)metas.put(new JSONObject().put("id","tt"+(base+i)).put("name",prefix+(kind.equals("movie")?" movie ":" show ")+(i+1)).put("releaseInfo",String.valueOf(2015+i)).put("description","A fixture synopsis used only for interface screenshots. It runs long enough to wrap onto a few lines so the layout can be judged.").put("genres",new JSONArray().put("Drama")).put("runtime","45 min").put("imdbRating","7."+i));return metas;}
 JSONObject withVideos(JSONObject meta)throws Exception{JSONArray v=new JSONArray();for(int s=1;s<=2;s++)for(int e=1;e<=4;e++)v.put(new JSONObject().put("season",s).put("episode",e).put("name","Episode "+e).put("released","2020-0"+s+"-0"+e+"T00:00:00Z"));return new JSONObject(meta.toString()).put("videos",v);}
 /** Fake Discover caches (no network). Existing cache files are never overwritten and never deleted. */
 void prepareDiscover()throws Exception{
  DiscoveryClient c=new DiscoveryClient(ctx);
  JSONArray bm=fakeTitles("Browse","movie",9100001,8),bs=fakeTitles("Browse","series",9100101,8),mm=fakeTitles("Match","movie",9100201,8),ms=fakeTitles("Match","series",9100301,8);
  cacheFile(c,"catalog/movie/top/skip=0.json",new JSONObject().put("metas",bm));cacheFile(c,"catalog/series/top/skip=0.json",new JSONObject().put("metas",bs));
  cacheFile(c,"catalog/movie/top/search=Fixture.json",new JSONObject().put("metas",mm));cacheFile(c,"catalog/series/top/search=Fixture.json",new JSONObject().put("metas",ms));
  for(JSONArray list:new JSONArray[]{bm,mm})for(JSONObject m:DataStore.objects(list))cacheFile(c,"meta/movie/"+m.optString("id")+".json",new JSONObject().put("meta",m));
  for(JSONArray list:new JSONArray[]{bs,ms})for(JSONObject m:DataStore.objects(list))cacheFile(c,"meta/series/"+m.optString("id")+".json",new JSONObject().put("meta",withVideos(m)));
 }
 void pickItems()throws Exception{
  movie=null;show=null;bright=null;Map<String,Integer> count=new HashMap<>();List<JSONObject> all=DataStore.objects(NoirApp.data.itemsShared());
  for(JSONObject i:all){if(i.optString("title").equals("Bright pattern fixture"))bright=i;if(!i.optBoolean("movie"))count.merge(i.optString("group"),1,Integer::sum);}
  for(JSONObject i:all){if(i.optString("title").isEmpty()||i.optString("group").isEmpty()||i.optString("title").endsWith("fixture"))continue;if(i.optBoolean("movie")){if(movie==null)movie=i;}else if(show==null||count.getOrDefault(i.optString("group"),0)>count.getOrDefault(show.optString("group"),0))show=i;}
  if(movie==null)for(JSONObject i:all)if(i.optBoolean("movie")&&!i.optString("group").isEmpty()){movie=i;break;}
  if(show==null)for(JSONObject i:all)if(!i.optBoolean("movie")&&!i.optString("group").isEmpty()){show=i;break;}
  if(bright==null)bright=movie;
 }
 void pair()throws Exception{DataStore data=NoirApp.data;if(!data.paired()){String link=TestRunner.arguments==null?null:TestRunner.arguments.getString("pairUri");if(link==null)throw new AssertionError("not paired and no pairUri runner argument");data.pair(link);}else data.sync();assertTrue("paired with the fixture PC",data.paired());assertTrue("fixture library received",data.itemsShared().length()>0);}

 // ---------------------------------------------------------------- phone
 void go(String page,String tab)throws Exception{main();ui(()->{a.detail="";a.search="";a.page=page;if(tab!=null)a.premium.historyView=tab;a.render();});}
 void main()throws Exception{
  if(a==null||a.isDestroyed()||a.isFinishing())a=start(in(MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_CLEAR_TASK));
  else if(!(resumed() instanceof MainActivity))ctx.startActivity(in(MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_REORDER_TO_FRONT));
  a=waitFor(MainActivity.class,10000);
 }
 void onboarding()throws Exception{
  try{fx=new AuditPhoneFixture();fx.data.pref("language",lang);Loc.use(fx.data);fx.data.pref("experienceChosen",false);}catch(Throwable t){missing("onboarding fixture",t);fx=null;}
  try{
   step("onboarding-welcome",()->{ob=fx.launch(MainActivity.class);shot();});
   step("onboarding-pairing-dialog",()->{ui(()->ob.pairInput());shot();});
   step("history-empty",()->{fx.data.pref("experienceChosen",true);fx.data.phoneSetting("historyOnly",true);ui(()->{ob.page="History";ob.detail="";ob.premium.historyView="Up next";ob.render();});shot();});
  }finally{try{if(fx!=null)fx.close();}catch(Throwable t){missing("onboarding fixture close",t);}fx=null;ob=null;}
 }
 void moreDialog(String name,String title){step(name,()->{go("More",null);settle(500);ui(()->{View r=byDesc(a.body,title);assertNotNull("More row not found: "+title,r);assertTrue(r.performClick());});shot(1200);});}
 void moreActivity(String name,String title,Class<? extends Activity> cls){step(name,()->{go("More",null);settle(500);ui(()->{View r=byDesc(a.body,title);assertNotNull("More row not found: "+title,r);assertTrue(r.performClick());});Activity x=waitFor(cls,10000);opened.add(x);try{shot(1500);}finally{end(x);}});}
 void screen(String name,Class<? extends Activity> cls,long settleMs){step(name,()->{Activity x=start(in(cls));x=waitFor(cls,10000);try{shot(settleMs);}finally{end(x);}});}
 void phoneLang()throws Exception{
  seq=0;onboarding();setLang();a=null;
  step("home-top",()->{go("Home",null);shot(1500);});
  step("home-scrolled",()->{go("Home",null);settle(1200);ui(()->{ScrollView s=a.scroller;int max=Math.max(0,s.getChildAt(0).getHeight()-s.getHeight());s.scrollTo(0,Math.min(max,Math.round(s.getHeight()*1.1f)));});shot(600);});
  step("library",()->{go("Library",null);shot();});
  step("library-view-sheet",()->{go("Library",null);settle(500);ui(()->clickPrefix(a.body,Loc.t("View")+" · "));shot(1000);});
  step("library-card-size-sheet",()->{go("Library",null);settle(500);ui(()->clickPrefix(a.body,Loc.t("Card size")+" · "));shot(1000);});
  step("library-shows",()->{go("Library",null);ui(()->{a.homeLayout.libraryType="Shows";a.render();});try{shot();}finally{ui(()->a.homeLayout.libraryType="All");}});
  step("library-my-list",()->{go("Library",null);String[] before={""};ui(()->{before[0]=a.premium.collection;clickPrefix(a.body,Loc.t("My List"));});try{shot();}finally{ui(()->{a.premium.collection=before[0].isEmpty()?"All":before[0];});}});
  String[][] tabs={{"Up next","upnext"},{"Shows","shows"},{"Movies","movies"},{"Diary","diary"},{"Stats","stats"},{"Upcoming","upcoming"}};
  for(String[] t:tabs)step("history-"+t[1],()->{go("History",t[0]);shot(1200);});
  step("history-reset",()->{ui(()->a.premium.historyView="Up next");expected.remove(current);});
  discover();
  step("more-top",()->{go("More",null);shot();});
  step("more-scrolled",()->{go("More",null);settle(600);ui(()->{ScrollView s=a.scroller;s.scrollTo(0,Math.max(0,s.getChildAt(0).getHeight()-s.getHeight()));});shot(500);});
  moreDialog("more-experience","Mode");moreDialog("more-playback","Playback");moreDialog("more-previews","Previews");moreDialog("more-subtitles","Subtitles");
  moreActivity("more-downloads-hub","Downloads",DownloadHubActivity.class);
  moreActivity("more-streaming","Streaming",PhoneDownloadSettingsActivity.class);
  moreActivity("more-history-tools","History tools",HistoryToolsActivity.class);
  moreActivity("more-taste","Compare taste",TasteActivity.class);
  moreDialog("more-catch-up","Catch me up");
  step("more-connections",()->{go("More",null);settle(500);ui(()->{View r=byDesc(a.body,"Connections");assertNotNull("More row not found: Connections",r);assertTrue(r.performClick());});shot(1200);});
  moreDialog("more-your-pc","Your PC");moreDialog("more-background-sync","Background sync");moreDialog("more-language","Language");moreDialog("more-size","Size");moreDialog("more-tips","Tips & shortcuts");moreDialog("more-about","About");
  step("title-sheet-show",()->{go("Home",null);settle(800);ui(()->a.options(show));shot(1200);});
  step("title-sheet-movie",()->{go("Home",null);settle(800);ui(()->a.options(movie));shot(1200);});
  step("show-detail",()->{go("Home",null);ui(()->a.open(show));shot(1500);});
  step("show-detail-scrolled",()->{go("Home",null);ui(()->a.open(show));settle(1200);ui(()->{ScrollView s=a.scroller;int max=Math.max(0,s.getChildAt(0).getHeight()-s.getHeight());s.scrollTo(0,Math.min(max,Math.round(s.getHeight()*.9f)));});shot(600);});
  step("movie-detail",()->{go("Home",null);ui(()->a.open(movie));shot(1500);});
  watchChoice("watch-choice-show",()->show);watchChoice("watch-choice-movie",()->movie);
  step("detail-reset",()->{go("Home",null);expected.remove(current);});
  screen("downloads-hub",DownloadHubActivity.class,1500);
  screen("offline-videos",DownloadsActivity.class,1500);
  screen("pc-downloads",PcDownloadsActivity.class,1500);
  screen("profiles",ProfilesActivity.class,1000);
  step("share-to-log",()->{Intent i=new Intent(Intent.ACTION_SEND).setClass(ctx,ShareToLogActivity.class).setType("text/plain").putExtra(Intent.EXTRA_TEXT,"https://www.imdb.com/title/tt9100001/").putExtra(Intent.EXTRA_SUBJECT,"Browse movie 1").addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);ctx.startActivity(i);LogActivity x=waitFor(LogActivity.class,10000);opened.add(x);try{shot(1800);}finally{end(x);}});
  step("widget",()->widget());
  player();
  for(Activity x:new ArrayList<>(opened))if(x instanceof PlayerActivity||x instanceof DiscoverActivity)end(x);
  end(a);a=null;
 }
 interface Pick{JSONObject get();}
 void watchChoice(String name,Pick pick){step(name,()->{go("Home",null);settle(600);JSONObject cache=PhoneHarbor.phoneCache;long at=PhoneHarbor.phoneAt;String profile=PhoneStream.cacheProfile;
  try{PhoneHarbor.phoneCache=new JSONObject().put("manifest","https://noir-shots.invalid/manifest.json");PhoneHarbor.phoneAt=System.currentTimeMillis();PhoneStream.cacheProfile=NoirApp.data.profileId;
   JSONObject item=pick.get();assertNotNull("no library item for the watch choice",item);ui(()->WatchChoice.sheet(a,item,false).show());shot(1200);
  }finally{PhoneHarbor.phoneCache=cache;PhoneHarbor.phoneAt=at;PhoneStream.cacheProfile=profile;}});}
 void discover(){
  step("discover-browse",()->{d=start(in(DiscoverActivity.class).addFlags(Intent.FLAG_ACTIVITY_CLEAR_TASK));long end=SystemClock.elapsedRealtime()+10000;boolean[] ready={false};while(SystemClock.elapsedRealtime()<end&&!ready[0]){ui(()->ready[0]=!d.loading&&d.titles.length()>0);Thread.sleep(80);}if(!ready[0])note("browse list did not load");shot(1200);});
  step("discover-search-results",()->{assertNotNull("Discover not open",d);ui(()->{d.query="Fixture";d.search.setText("Fixture");d.find();});long end=SystemClock.elapsedRealtime()+10000;boolean[] ready={false};while(SystemClock.elapsedRealtime()<end&&!ready[0]){ui(()->ready[0]=!d.loading&&d.shown>0&&d.titles.optJSONObject(0)!=null&&d.titles.optJSONObject(0).optString("name").startsWith("Match"));Thread.sleep(80);}if(!ready[0])note("search results did not load");shot(1200);});
  step("discover-sheet",()->{assertNotNull("Discover not open",d);ui(()->{View card=AuditPhoneFixture.find(d.results,v->v.getContentDescription()!=null&&v.getContentDescription().toString().startsWith("Explore ")&&v.hasOnClickListeners());assertNotNull("no result card",card);assertTrue(card.performClick());});shot(2000);});
  step("discover-title-page",()->{assertNotNull("Discover not open",d);JSONObject[] seed={null};ui(()->seed[0]=d.titles.optJSONObject(1));assertNotNull(seed[0]);ui(()->d.open(seed[0]));long end=SystemClock.elapsedRealtime()+10000;boolean[] ready={false};while(SystemClock.elapsedRealtime()<end&&!ready[0]){ui(()->ready[0]=!d.loading&&d.title!=null&&d.title.has("videos"));Thread.sleep(80);}if(!ready[0])note("title details did not load");shot(1000);});
  step("discover-close",()->{end(d);d=null;expected.remove(current);});
 }
 void widget()throws Exception{
  DataStore data=NoirApp.data;JSONObject next=NextUpWidget.next(data);int layout=ctx.getResources().getIdentifier("next_up_widget","layout",ctx.getPackageName());assertTrue("widget layout",layout!=0);
  RemoteViews v=new RemoteViews(ctx.getPackageName(),layout);
  v.setTextViewText(id("next_up_brand"),Loc.t("NOIR · UP NEXT"));v.setTextViewText(id("next_up_streak"),Loc.f("Day %d streak",PhoneRewardCard.stats(data).streak));v.setImageViewResource(id("next_up_poster"),android.R.drawable.ic_media_play);v.setTextViewText(id("next_up_check"),"✓");v.setContentDescription(id("next_up_check"),Loc.t("Mark watched"));v.setViewVisibility(id("next_up_check"),next==null?View.GONE:View.VISIBLE);
  if(next==null){v.setTextViewText(id("next_up_title"),Loc.t("All caught up"));v.setTextViewText(id("next_up_episode"),Loc.t("Open Noir to find your next story"));}
  else{v.setTextViewText(id("next_up_title"),next.optString("title"));v.setTextViewText(id("next_up_episode"),PhoneSmart.label(next)+(next.optString("episodeTitle").isEmpty()?"":" · "+next.optString("episodeTitle")));}
  Bitmap[] out={null};ui(()->{View view=v.apply(ctx,new FrameLayout(ctx));int w=dp(320),h=dp(110);view.setLayoutDirection(Loc.arabic?View.LAYOUT_DIRECTION_RTL:View.LAYOUT_DIRECTION_LTR);view.measure(View.MeasureSpec.makeMeasureSpec(w,View.MeasureSpec.EXACTLY),View.MeasureSpec.makeMeasureSpec(h,View.MeasureSpec.EXACTLY));view.layout(0,0,w,h);Bitmap bm=Bitmap.createBitmap(w,h,Bitmap.Config.ARGB_8888);view.draw(new Canvas(bm));out[0]=bm;});
  save(out[0],current+".png");out[0].recycle();captured++;
 }
 int id(String name){return ctx.getResources().getIdentifier(name,"id",ctx.getPackageName());}
 void player(){
  step("player-controls",()->{assertNotNull("Bright pattern fixture missing",bright);p=start(in(PlayerActivity.class).putExtra("id",bright.optString("id")).putExtra("restart",true));p=waitFor(PlayerActivity.class,15000);long end=SystemClock.elapsedRealtime()+45000;boolean[] up={false};while(SystemClock.elapsedRealtime()<end&&!up[0]){ui(()->up[0]=p.firstVideo&&p.player!=null&&p.player.isPlaying());Thread.sleep(250);}if(!up[0])note("video did not start in 45 s");Thread.sleep(1500);ui(()->{p.manualHidden=false;p.show();p.handler.removeCallbacks(p.hide);if(p.player!=null&&p.player.isPlaying())p.player.pause();});shot(700);});
  step("player-audio-menu",()->{assertNotNull("Player not open",p);ui(()->{p.manualHidden=false;p.tracks(false);});shot(800);});
  step("player-subtitle-menu",()->{assertNotNull("Player not open",p);ui(()->{p.manualHidden=false;p.tracks(true);});shot(800);});
  step("player-speed-menu",()->{assertNotNull("Player not open",p);ui(()->{p.manualHidden=false;p.chooseSpeed();});shot(800);});
  step("player-close",()->{end(p);p=null;expected.remove(current);});
 }

 // Opt-in (slow, for the design review): am instrument ... -e shots true -e class app.noir.mobile.ScreenShotsTest
 @Test public void phoneScreens()throws Exception{org.junit.Assume.assumeTrue("Opt-in: -e shots true","true".equals(TestRunner.arguments.getString("shots")));
  Assume.assumeFalse("phone screens run on a phone, not a TV",TvUi.isTv(ctx));
  init("phone");
  DataStore data=NoirApp.data;boolean oldHistoryOnly=data.playbackFlag("historyOnly",false),oldGesture=data.prefs().optBoolean("tipPlayerGestures",false);String oldPreview=data.prefs().optString(PhonePreview.PREF,"");
  DiscoveryClient.GenreCatalog oldGenre=DiscoveryClient.genreOverride;
  try{
   try{pair();}catch(Throwable t){missing("pairing with the fixture PC",t);}
   data=NoirApp.data;data.phoneSetting("historyOnly",false);data.pref("experienceChosen",true);data.pref("tipPlayerGestures",true);data.pref(PhonePreview.PREF,"off");
   try{pickItems();}catch(Throwable t){missing("choosing fixture items",t);}
   try{prepareDiscover();}catch(Throwable t){missing("preparing fake Discover caches",t);}
   JSONArray picks=fakeTitles("Fixture pick","movie",9100401,24);DiscoveryClient.genreOverride=(genre,top)->picks;
   for(String l:langs()){lang=l;try{if(b2only())phoneBatch2();else if(premiumOnly())phonePremium();else phoneLang();}catch(Throwable t){missing("phone "+l+" run",t);}closeActivities();}
  }finally{
   DiscoveryClient.genreOverride=oldGenre;
   try{lang="en";DataStore now=NoirApp.data;now.pref("language","en");Loc.use(now);now.phoneSetting("historyOnly",oldHistoryOnly);now.pref("tipPlayerGestures",oldGesture);if(!oldPreview.isEmpty())now.pref(PhonePreview.PREF,oldPreview);}catch(Throwable t){missing("restoring English and settings",t);}
   finishRun();
  }
 }

 // ---------------------------------------------------------------- batch 2 (opt-in: -e shotsOnly b2 runs only these)
 // Seeds sync state for the Connections page, in the emulator's app data only (the run installs a fresh app, so nothing outlives it).
 void seedB2()throws Exception{
  DataStore d=NoirApp.data;long now=System.currentTimeMillis();
  d.stateObject("stremio",new JSONObject().put("key","shots-fake-stremio-key"));
  for(String p:WatchSync.PROVIDERS)WatchSync.enable(d,p,true);
  synchronized(d){
   JSONObject jobs=WatchSync.section(WatchSync.state(d),"jobs");
   String[] who={"Trakt","Serializd",WatchSync.STREMIO};int[] done={12,6,5},wait={4,9,1};
   for(int k=0;k<3;k++){
    for(int n=0;n<done[k];n++)DataStore.put(jobs,who[k]+":shots-s"+n,new JSONObject().put("provider",who[k]).put("itemId","shots-s"+n).put("viewingId","v"+n).put("watchedAt",now-(n+2)*DAYMS).put("syncedAt",now-(n+1)*3600000L*5).put("message","Watched ✓"));
    for(int n=0;n<wait[k];n++)DataStore.put(jobs,who[k]+":shots-w"+n,new JSONObject().put("provider",who[k]).put("itemId","shots-w"+n).put("viewingId","w"+n).put("watchedAt",now-n*3600000L).put("retryAt",now+3*3600000L).put("message","Waiting to sync"));
   }
  }
  WatchSync.editProvider(d,"Trakt",pr->{DataStore.put(pr,"failures",3);DataStore.put(pr,"failingSince",now-30*3600000L);DataStore.put(pr,"retryAt",now+3*3600000L);DataStore.put(pr,"message","The website could not load.");DataStore.put(pr,"lastSuccess",now-31*3600000L);});
  WatchSync.editProvider(d,"Serializd",pr->{DataStore.put(pr,"failures",WatchSync.BACKOFF.length);DataStore.put(pr,"failingSince",now-3*DAYMS);DataStore.put(pr,"retryAt",now+24*3600000L);DataStore.put(pr,"message","The website could not load.");DataStore.put(pr,"lastSuccess",now-4*DAYMS);});
  WatchSync.editProvider(d,WatchSync.STREMIO,pr->{DataStore.put(pr,"lastSuccess",now-20*60000L);DataStore.put(pr,"retryAt",0);});
  d.phoneSetting("stremioAddTitles",true);
 }
 static final long DAYMS=24*3600000L;
 void scrollToView(ScrollView s,View v,int offsetDp)throws Exception{ui(()->{android.graphics.Rect r=new android.graphics.Rect();v.getDrawingRect(r);s.offsetDescendantRectToMyCoords(v,r);s.scrollTo(0,Math.max(0,r.top-dp(offsetDp)));});settle(500);}
 View textView(View root,String english){String want=Loc.t(english);return AuditPhoneFixture.find(root,v->v instanceof TextView&&want.contentEquals(((TextView)v).getText()));}
 void connectionsAt(String name,String english,int offsetDp){step(name,()->{go("Connections",null);settle(900);View[] t={null};ui(()->t[0]=textView(a.body,english));assertNotNull("text not found: "+english,t[0]);scrollToView(a.scroller,t[0],offsetDp);shot(600);});}
 JSONObject savedDownload(String id,String group,String title,int episode,long watchedAt)throws Exception{DataStore data=NoirApp.data;JSONObject item=data.rememberPhoneItem(new JSONObject().put("id",id).put("group",group).put("imdb",group.equals("b2-night")?"tt9300001":"tt9300002").put("title",title).put("movie",false).put("season",1).put("episode",episode).put("episodeTitle","Episode "+episode).put("progress",watchedAt>0?new JSONObject().put("completed",true).put("updatedAt",java.time.Instant.ofEpochMilli(watchedAt).toString()):new JSONObject()));DownloadStore store=NoirApp.downloads;store.enqueue(item);java.nio.file.Files.write(store.file(id,false).toPath(),new byte[4096]);store.update(id,"complete",4096,4096,"","");return item;}
 void hubStep(String name,Act body){step(name,()->{Activity x=start(in(DownloadHubActivity.class));DownloadHubActivity h=(DownloadHubActivity)waitFor(DownloadHubActivity.class,10000);opened.add(h);try{ui(h::update);settle(900);hub=h;body.run();}finally{hub=null;end(h);}});}
 DownloadHubActivity hub;
 ScrollView scrollOf(Activity act)throws Exception{View[] s={null};ui(()->s[0]=AuditPhoneFixture.find(act.getWindow().getDecorView(),v->v instanceof ScrollView));assertNotNull("no ScrollView",s[0]);return (ScrollView)s[0];}
 void batch2()throws Exception{
  step("b2-seed",()->{seedB2();expected.remove(current);});
  step("b2-connections-pc-sync",()->{go("Connections",null);settle(900);ui(()->a.scroller.scrollTo(0,0));shot(600);});
  step("b2-connections-too-large",()->{String before=NoirApp.data.connection;NoirApp.data.connection=Loc.t(DataStore.LIBRARY_TOO_LARGE);try{go("Connections",null);settle(900);shot(600);}finally{NoirApp.data.connection=before;}});
  connectionsAt("b2-connections-trakt-serializd","Trakt",24);
  connectionsAt("b2-connections-serializd","Serializd",24);
  connectionsAt("b2-connections-stremio-marks","Stremio",24);
  step("b2-connections-bottom",()->{go("Connections",null);settle(900);ui(()->{ScrollView s=a.scroller;s.scrollTo(0,Math.max(0,s.getChildAt(0).getHeight()-s.getHeight()));});shot(600);});
  step("b2-downloads-seed",()->{long now=System.currentTimeMillis();savedDownload("b2-ns1","b2-night","Night Signal",1,now-2*DAYMS);savedDownload("b2-ns2","b2-night","Night Signal",2,now-DAYMS);savedDownload("b2-ns3","b2-night","Night Signal",3,0);savedDownload("b2-pl1","b2-paper","Paper Lanterns",1,0);savedDownload("b2-pl2","b2-paper","Paper Lanterns",2,0);NoirApp.data.pref("downloadsFilter","All");expected.remove(current);});
  hubStep("b2-downloads-filter-all",()->{View[] f={null};ui(()->f[0]=DownloadHubTest.tagged(hub.body,"saved-filter"));assertNotNull("filter row",f[0]);scrollToView(scrollOf(hub),f[0],160);shot(600);});
  hubStep("b2-downloads-filter-unwatched",()->{ui(()->{View p=DownloadHubTest.tagged(hub.body,"saved-filter:2");assertNotNull("Unwatched pill",p);assertTrue(p.performClick());});settle(900);View[] f={null};ui(()->f[0]=DownloadHubTest.tagged(hub.body,"saved-filter"));scrollToView(scrollOf(hub),f[0],160);shot(600);ui(()->{View p=DownloadHubTest.tagged(hub.body,"saved-filter:0");if(p!=null)p.performClick();});});
  hubStep("b2-downloads-auto-rules",()->{View[] c={null};ui(()->c[0]=DownloadHubTest.tagged(hub.body,"auto-rules-card"));assertNotNull("automatic rules card",c[0]);scrollToView(scrollOf(hub),c[0],30);shot(600);});
  hubStep("b2-downloads-auto-rules-end",()->{ScrollView s=scrollOf(hub);ui(()->s.scrollTo(0,Math.max(0,s.getChildAt(0).getHeight()-s.getHeight())));shot(600);});
  hubStep("b2-keep-ready-show-sheet",()->{ui(()->DownloadHubActivity.openKeepReady(hub,"b2-night"));shot(1800);});
  step("b2-restore",()->{for(String p:WatchSync.PROVIDERS)try{WatchSync.enable(NoirApp.data,p,false);}catch(Throwable ignored){}expected.remove(current);});
 }
 static boolean b2only(){return "b2".equals(TestRunner.arguments.getString("shotsOnly"));}
 void phoneBatch2()throws Exception{seq=0;setLang();a=null;batch2();end(a);a=null;}

 // ---------------------------------------------------------------- premium pass (opt-in: -e shotsOnly p runs only these)
 static boolean premiumOnly(){return "p".equals(TestRunner.arguments.getString("shotsOnly"));}
 void hubTab(String name,String tab){step(name,()->{Activity x=start(in(DownloadHubActivity.class).putExtra("tab",tab));DownloadHubActivity h=(DownloadHubActivity)waitFor(DownloadHubActivity.class,10000);opened.add(h);try{ui(h::update);shot(1200);}finally{end(h);}});}
 void homeAtStrip(String name,String size){step(name,()->{NoirApp.data.pref("homeCardSize",size);try{go("Home",null);settle(1200);View[] strip={null};ui(()->strip[0]=a.body.findViewWithTag("home-status"));assertNotNull("status strip",strip[0]);scrollToView(a.scroller,strip[0],10);shot(700);}finally{NoirApp.data.pref("homeCardSize","Medium");}});}
 void waitDiscover()throws Exception{long end=SystemClock.elapsedRealtime()+10000;boolean[] ready={false};while(SystemClock.elapsedRealtime()<end&&!ready[0]){ui(()->ready[0]=!d.loading&&d.titles.length()>0);Thread.sleep(80);}if(!ready[0])note("browse list did not load");}
 void openTitle(int index)throws Exception{JSONObject[] seed={null};ui(()->seed[0]=d.titles.optJSONObject(index));assertNotNull(seed[0]);ui(()->d.open(seed[0]));long end=SystemClock.elapsedRealtime()+10000;boolean[] ready={false};while(SystemClock.elapsedRealtime()<end&&!ready[0]){ui(()->ready[0]=!d.loading&&d.title!=null);Thread.sleep(80);}if(!ready[0])note("title details did not load");}
 JSONObject readyItem(String id,int episode)throws Exception{return new JSONObject().put("id",id).put("group","b2-night").put("imdb","tt9300001").put("title","Night Signal").put("movie",false).put("season",1).put("episode",episode).put("episodeTitle","Episode "+episode);}
 void phonePremium()throws Exception{
  seq=0;setLang();a=null;
  step("p-seed",()->{long now=System.currentTimeMillis();DownloadStore store=NoirApp.downloads;
   savedDownload("b2-ns1","b2-night","Night Signal",1,now-2*DAYMS);savedDownload("b2-ns2","b2-night","Night Signal",2,now-DAYMS);savedDownload("b2-ns3","b2-night","Night Signal",3,0);savedDownload("b2-pl1","b2-paper","Paper Lanterns",1,0);
   JSONObject waiting=NoirApp.data.rememberPhoneItem(new JSONObject().put("id","p-w1").put("group","b2-paper").put("imdb","tt9300002").put("title","Paper Lanterns").put("movie",false).put("season",1).put("episode",3).put("episodeTitle","Episode 3"));store.enqueue(waiting);store.update("p-w1","paused",1500,4096,"","");
   store.enqueueReady(readyItem("p-r1",4),null,4096);java.nio.file.Files.write(store.file("p-r1",false).toPath(),new byte[4096]);store.update("p-r1","complete",4096,4096,"","");store.enqueueReady(readyItem("p-r2",5),null,4096);
   NoirApp.data.pref("downloadsFilter","All");NoirApp.data.pref("homeCardSize","Medium");expected.remove(current);});
  step("p-home-top",()->{go("Home",null);shot(1500);});
  homeAtStrip("p-home-strip","Medium");
  step("p-home-rows",()->{go("Home",null);settle(1200);ui(()->{ScrollView s=a.scroller;int max=Math.max(0,s.getChildAt(0).getHeight()-s.getHeight());s.scrollTo(0,Math.min(max,Math.round(s.getHeight()*1.45f)));});shot(700);});
  step("p-home-rows2",()->{go("Home",null);settle(1200);ui(()->{ScrollView s=a.scroller;int max=Math.max(0,s.getChildAt(0).getHeight()-s.getHeight());s.scrollTo(0,Math.min(max,Math.round(s.getHeight()*2.3f)));});shot(700);});
  step("p-home-edit",()->{go("Home",null);settle(800);ui(()->a.homeLayout.edit());shot(1200);});
  homeAtStrip("p-home-small","Small");homeAtStrip("p-home-large","Large");
  hubTab("p-downloads-queue","queue");hubTab("p-downloads-phone","phone");hubTab("p-downloads-ready","ready");
  step("p-history",()->{go("History","Up next");shot(1200);});
  step("p-discover-sheet",()->{d=start(in(DiscoverActivity.class).addFlags(Intent.FLAG_ACTIVITY_CLEAR_TASK));waitDiscover();ui(()->{View card=AuditPhoneFixture.find(d.results,v->v.getContentDescription()!=null&&v.getContentDescription().toString().startsWith("Explore ")&&v.hasOnClickListeners());assertNotNull("no result card",card);assertTrue(card.performClick());});shot(2000);});
  step("p-discover-show",()->{assertNotNull("Discover not open",d);openTitle(1);shot(1000);});
  step("p-discover-show-episodes",()->{assertNotNull("Discover not open",d);ui(()->{View chips=d.body.findViewWithTag("discover-seasons");if(chips!=null){android.graphics.Rect r=new android.graphics.Rect();chips.getDrawingRect(r);d.scroll.offsetDescendantRectToMyCoords(chips,r);d.scroll.scrollTo(0,Math.max(0,r.top-dp(12)));}});shot(700);});
  step("p-discover-movie",()->{assertNotNull("Discover not open",d);ui(()->d.back());waitDiscover();openTitle(0);shot(1000);});
  step("p-download-sheet",()->{end(d);d=null;assertNotNull("no fixture show",show);Activity x=start(in(PhoneDownloadsActivity.class).putExtra("id",show.optString("id")).putExtra("season",true));opened.add(x);try{shot(1800);}finally{end(x);}});
  step("p-restore",()->{DownloadStore store=NoirApp.downloads;for(String id:new String[]{"p-w1","p-r1","p-r2","b2-ns1","b2-ns2","b2-ns3","b2-pl1"})try{store.remove(id);}catch(Throwable ignored){}expected.remove(current);});
  end(a);a=null;
 }

 // ---------------------------------------------------------------- TV
 void key2(int code,int times)throws Exception{for(int n=0;n<times;n++)key(code);}
 void tvHome()throws Exception{
  if(tv==null||tv.isDestroyed()||tv.isFinishing()){tv=start(in(TvActivity.class).addFlags(Intent.FLAG_ACTIVITY_CLEAR_TASK));}
  tv=waitFor(TvActivity.class,10000);long end=SystemClock.elapsedRealtime()+15000;boolean[] ready={false};while(SystemClock.elapsedRealtime()<end&&!ready[0]){ui(()->ready[0]=tv.model!=null&&!tv.visibleRows.isEmpty());Thread.sleep(80);}if(!ready[0])note("Home rows did not arrive");
 }
 void tvPage(String name,String page,String query){step(name,()->{tvHome();ui(()->{tv.query=query;tv.showPage(page,true);});shot(1500);});}
 void tvLang()throws Exception{
  seq=0;setLang();tv=null;
  step("home-top",()->{if(tv!=null)end(tv);tv=null;tvHome();shot(2000);});
  step("home-down-two-rows",()->{tvHome();ui(()->tv.showPage("Home",true));settle(1000);key2(KeyEvent.KEYCODE_DPAD_DOWN,2);shot(800);});
  tvPage("search-empty","Search","");
  String word=(bright==null?"Bright":bright.optString("title","Bright").split(" ")[0]);
  step("search-results",()->{tvHome();ui(()->{tv.query=word;tv.showPage("Search",true);});shot(2500);});
  tvPage("my-list","My List","");tvPage("ready","Ready","");
  step("home-close",()->{end(tv);tv=null;expected.remove(current);});
  tvDetail("title-show",()->show);tvDetail("title-movie",()->movie);
  screen("settings",TvSettingsActivity.class,1500);
  screen("setup",TvSetupActivity.class,1500);
  step("profile-picker",()->{DataStore data=NoirApp.data;String id=null;try{for(JSONObject pr:DataStore.objects(data.profiles.list()))if("Shots guest".equals(pr.optString("name")))data.profiles.delete(pr.optString("id"));id=data.profiles.create("Shots guest","#7AA7FF").optString("id");Activity x=start(in(TvProfilesActivity.class));x=waitFor(TvProfilesActivity.class,10000);try{shot(1500);}finally{end(x);}}finally{if(id!=null)try{data.profiles.delete(id);}catch(Throwable t){missing("deleting the fake profile",t);}}});
  tvPlayer();
  tvExtras();
 }
 /** U23 screens the first runner missed. They come after the player so the earlier files keep their numbers. */
 View dialogOf(Activity act)throws Exception{View[] out={null};ui(()->{for(View r:android.view.inspector.WindowInspector.getGlobalWindowViews()){if(r==act.getWindow().getDecorView()||!r.isShown())continue;if(owner(r.getContext())==act)out[0]=r;}});return out[0];}
 void tvExtras(){
  step("home-rail-open",()->{tvHome();ui(()->tv.showPage("Home",true));settle(800);ui(()->tv.focusRail());shot(900);});
  step("home-tile-menu",()->{tvHome();ui(()->tv.showPage("Home",true));settle(800);ui(()->{assertFalse("Home has no rows",tv.visibleRows.isEmpty());tv.tileMenu(tv.visibleRows.get(0).values.get(0));});shot(1000);});
  step("home-see-all",()->{tvHome();ui(()->{TvRows.Row pick=null;for(TvRows.Row r:tv.model.rows)if(!r.allTiles.isEmpty()&&(pick==null||r.allTiles.size()>pick.allTiles.size()))pick=r;assertNotNull("Home has no library row",pick);tv.showShelf(pick);});shot(1500);});
  step("home-close-2",()->{end(tv);tv=null;expected.remove(current);});
  screen("tv-check",TvCheckActivity.class,3000);
  step("setup-pair-dialog",()->{Activity x=start(in(TvSetupActivity.class));x=waitFor(TvSetupActivity.class,10000);Activity host=x;try{ui(()->TvSetupActivity.pair(host,()->{}));shot(1200);}finally{closeDialogs(x);end(x);}});
  step("setup-pair-check-code",()->{Activity x=start(in(TvSetupActivity.class));x=waitFor(TvSetupActivity.class,10000);Activity host=x;try{ui(()->TvSetupActivity.pair(host,()->{}));settle(700);View d=dialogOf(x);assertNotNull("pairing dialog not found",d);
    /* The fixture PC answers the certificate check; no pairing code is used up because "It matches" is never pressed. */
    ui(()->{List<EditText> fields=new ArrayList<>();AuditPhoneFixture.find(d,v->{if(v instanceof EditText)fields.add((EditText)v);return false;});assertEquals("address and code fields",2,fields.size());fields.get(0).setText("10.0.2.2:49272");fields.get(1).setText("12345678");View connect=AuditPhoneFixture.find(d,v->v instanceof Button&&Loc.t("Connect").contentEquals(((Button)v).getText()));assertNotNull("Connect button",connect);assertTrue(connect.performClick());});
    long end=SystemClock.elapsedRealtime()+12000;boolean[] shown={false};while(SystemClock.elapsedRealtime()<end&&!shown[0]){ui(()->shown[0]=AuditPhoneFixture.find(d,v->v instanceof Button&&Loc.t("It matches").contentEquals(((Button)v).getText()))!=null);Thread.sleep(150);}if(!shown[0])note("the check code did not appear; the capture shows the state reached");shot(700);}finally{closeDialogs(x);end(x);}});
  step("setup-stream-dialog",()->{Activity x=start(in(TvSetupActivity.class));x=waitFor(TvSetupActivity.class,10000);Activity host=x;try{ui(()->TvSetupActivity.stream(host,()->{}));shot(1800);}finally{closeDialogs(x);end(x);}});
  step("pin-keypad",()->{DataStore data=NoirApp.data;String id=null;try{for(JSONObject pr:DataStore.objects(data.profiles.list()))if("Shots guest".equals(pr.optString("name")))data.profiles.delete(pr.optString("id"));id=data.profiles.create("Shots guest","#7AA7FF").optString("id");data.profiles.pin(id,"1234");Activity x=start(in(TvProfilesActivity.class));x=waitFor(TvProfilesActivity.class,10000);TvProfilesActivity host=(TvProfilesActivity)x;TvKeypad[] pad={null};try{settle(600);ui(()->{pad[0]=new TvKeypad(host,"Shots guest",(pin,keypad)->{});pad[0].pin.append("12");pad[0].dots.setText("••");});shot(1000);}finally{ui(()->{if(pad[0]!=null)pad[0].dialog.dismiss();});end(x);}}finally{if(id!=null)try{data.profiles.delete(id);}catch(Throwable t){missing("deleting the fake PIN profile",t);}}});
 }
 void tvDetail(String name,Pick pick){step(name,()->{JSONObject item=pick.get();assertNotNull("no library item",item);Activity x=start(in(TvDetailActivity.class).putExtra("group",item.optString("group")));x=waitFor(TvDetailActivity.class,10000);try{shot(2000);}finally{end(x);}});}
 void tvPlayer(){
  step("player-overlay",()->{assertNotNull("Bright pattern fixture missing",bright);p=start(in(PlayerActivity.class).putExtra("id",bright.optString("id")).putExtra("restart",true));p=waitFor(PlayerActivity.class,15000);long end=SystemClock.elapsedRealtime()+45000;boolean[] up={false};while(SystemClock.elapsedRealtime()<end&&!up[0]){ui(()->up[0]=p.firstVideo&&p.player!=null&&p.player.isPlaying());Thread.sleep(250);}if(!up[0])note("video did not start in 45 s");Thread.sleep(1500);ui(()->{p.manualHidden=false;p.show();p.handler.removeCallbacks(p.hide);});shot(600);});
  step("player-tracks-panel",()->{assertNotNull("Player not open",p);ui(()->{p.manualHidden=false;p.tvTracks();});shot(800);});
  step("player-audio-panel",()->{assertNotNull("Player not open",p);ui(()->{p.manualHidden=false;p.tracks(false);});shot(800);});
  step("player-subtitles-panel",()->{assertNotNull("Player not open",p);ui(()->{p.manualHidden=false;p.tracks(true);});shot(800);});
  step("player-close",()->{end(p);p=null;expected.remove(current);});
 }

 @Test public void tvScreens()throws Exception{org.junit.Assume.assumeTrue("Opt-in: -e shots true","true".equals(TestRunner.arguments.getString("shots")));
  Assume.assumeTrue("TV screens run on a TV device",TvUi.isTv(ctx));
  init("tv");
  DataStore data=NoirApp.data;boolean oldChosen=TvProfilesActivity.chosen;
  try{
   try{pair();}catch(Throwable t){missing("pairing with the fixture PC",t);}
   data=NoirApp.data;TvProfilesActivity.chosen=true;
   try{pickItems();}catch(Throwable t){missing("choosing fixture items",t);}
   try{String q=(bright==null?"Bright":bright.optString("title","Bright").split(" ")[0]);DiscoveryClient c=new DiscoveryClient(NoirApp.data.context);String enc=java.net.URLEncoder.encode(q.trim(),"UTF-8").replace("+","%20");cacheFile(c,"catalog/movie/top/search="+enc+".json",new JSONObject().put("metas",new JSONArray()));cacheFile(c,"catalog/series/top/search="+enc+".json",new JSONObject().put("metas",new JSONArray()));}catch(Throwable t){missing("preparing fake Discover caches",t);}
   for(String l:langs()){lang=l;try{tvLang();}catch(Throwable t){missing("tv "+l+" run",t);}closeActivities();}
  }finally{
   try{lang="en";DataStore now=NoirApp.data;now.pref("language","en");Loc.use(now);}catch(Throwable t){missing("restoring English",t);}
   TvProfilesActivity.chosen=oldChosen;
   finishRun();
  }
 }
}
