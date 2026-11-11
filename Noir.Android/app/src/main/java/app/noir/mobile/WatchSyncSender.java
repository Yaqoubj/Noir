package app.noir.mobile;
import android.app.Activity;
import android.app.Application;
import android.content.Context;
import android.net.Uri;
import android.os.*;
import android.view.View;
import android.view.ViewGroup;
import android.view.ViewParent;
import android.webkit.*;
import org.json.*;
import java.io.IOException;
import java.util.*;
import java.util.concurrent.*;

/** P2: marks the phone's own viewings on Trakt and Serializd with an off-screen WebView on the app's signed-in session, in the PC's order:
 open the page, read the control, already watched = done without a click, else click (Trakt: pick "Just now"), reload, confirm.
 Runs only while the app process is alive: after a viewing is recorded, on app start and every 15 minutes. Never on TV. */
final class WatchSyncSender {
 static final Handler MAIN=new Handler(Looper.getMainLooper());
 static final ExecutorService WORK=Executors.newSingleThreadExecutor();
 static final long EVERY=15*60000L;
 static final int WIDTH_CSS=1280,HEIGHT_CSS=820;
 static final Set<String> HOSTS=new HashSet<>(Arrays.asList("app.trakt.tv","trakt.tv","www.serializd.com","serializd.com"));
 /* Test seam: serves every request of the sender's WebView (null = the real sites). Anything it does not serve is a 404, so tests never reach the network. */
 static volatile java.util.function.Function<Uri,WebResourceResponse> testPages;
 static volatile long loadTimeoutMs=40000;
 /* Test seam: replaces the page work of one job (no WebView), for queue-size tests. */
 interface Step{void send(JSONObject job,JSONObject item)throws Exception;}
 static volatile Step testStep;
 static Context app;static Activity foreground;static boolean busy,again,ticking;static Pass running;static volatile int passes;

 /* The one settings helper shared by the sender and the visible sign-in view. The WebView's own user agent and headers are kept as they are. */
 @android.annotation.SuppressLint("SetJavaScriptEnabled")
 static void configure(WebView w){WebSettings s=w.getSettings();s.setJavaScriptEnabled(true);s.setDomStorageEnabled(true);s.setAllowFileAccess(false);s.setAllowContentAccess(false);s.setMixedContentMode(WebSettings.MIXED_CONTENT_NEVER_ALLOW);s.setMediaPlaybackRequiresUserGesture(true);CookieManager.getInstance().setAcceptCookie(true);CookieManager.getInstance().setAcceptThirdPartyCookies(w,true);}

 static void install(Application a){app=a;if(WatchSync.tv.test(a))return;
  a.registerActivityLifecycleCallbacks(new Application.ActivityLifecycleCallbacks(){
   public void onActivityCreated(Activity x,Bundle b){}public void onActivityStarted(Activity x){}public void onActivityStopped(Activity x){}public void onActivitySaveInstanceState(Activity x,Bundle b){}public void onActivityDestroyed(Activity x){if(foreground==x)foreground=null;}
   public void onActivityResumed(Activity x){if(x instanceof PlayerActivity||x instanceof WatchSignInActivity)return;/* never host a pass on the player or while the user signs in */foreground=x;tick();MAIN.postDelayed(WatchSyncSender::start,1500);}
   public void onActivityPaused(Activity x){if(foreground==x)foreground=null;Pass p=running;if(p!=null&&p.host==x)p.cancelled=true;}});}
 static void tick(){if(ticking)return;ticking=true;MAIN.postDelayed(new Runnable(){public void run(){start();MAIN.postDelayed(this,EVERY);}},EVERY);}
 static void wake(){MAIN.post(WatchSyncSender::start);}
 /* With no visible activity (process alive for a download or sync service), a detached WebView laid out at 1280 CSS px does the same work. */
 static boolean processBusy(Context c){android.app.ActivityManager m=(android.app.ActivityManager)c.getSystemService(Context.ACTIVITY_SERVICE);if(m==null)return false;try{for(android.app.ActivityManager.RunningServiceInfo s:m.getRunningServices(50))if(s.started&&s.service.getPackageName().equals(c.getPackageName()))return true;}catch(Exception ignored){}return false;}
 /* Tests that drive their own pass hold the automatic one (false outside tests). */
 static volatile boolean hold;
 static void start(){if(hold)return;startWith(NoirApp.data,foreground,app);}
 /* One pass at a time in the whole process: every entry (screen resume, wake, the 15-minute tick, the rerun below) runs here on the main thread, and busy is set and cleared only on the main thread. */
 static void startWith(DataStore data,Activity host,Context c){if(Looper.myLooper()!=Looper.getMainLooper())throw new IllegalStateException("start the sender on the main thread");
  if(c==null||data==null||WatchSync.tv.test(c))return;if(busy){again=true;return;}
  if(!WatchSync.hasDue(data,WatchSync.clock.now()))return;
  if(host!=null&&(host.isFinishing()||host.isDestroyed()))host=null;if(host==null&&!processBusy(c))return;
  busy=true;Pass pass=new Pass(data,host,c);running=pass;
  WORK.execute(()->{try{pass.run();}finally{MAIN.post(()->{if(running==pass)running=null;busy=false;if(again){again=false;start();}});}});}
 /* Tests: one blocking pass on the calling (non-main) thread; host null = detached WebView. */
 static void runForTest(DataStore data,Activity host,Context c){new Pass(data,host,c).run();}
 static void holdForTest(boolean on)throws InterruptedException{hold=on;if(!on)return;for(int n=0;n<600&&busy;n++)Thread.sleep(100);if(busy)throw new IllegalStateException("an automatic pass is still running");}

 static final class Problem extends IOException{final boolean site;Problem(String m,boolean site){super(m);this.site=site;}}
 static final class Cancelled extends RuntimeException{}
 static final class Skip extends RuntimeException{}
 static Problem site(String m){return new Problem(m,true);}
 static Problem title(String m){return new Problem(m,false);}

 static final class Pass {
  final DataStore data;final Activity host;final Context context;WebView web;volatile boolean cancelled,loadFailed;volatile CountDownLatch load;
  Pass(DataStore d,Activity h,Context c){data=d;host=h;context=c;}
  void run(){passes++;WatchSync.absorb(data);Set<String> stopped=new HashSet<>();boolean sentOne=false;
   try{for(JSONObject job:WatchSync.due(data,WatchSync.clock.now())){String p=job.optString("provider"),key=WatchSync.key(job);if(cancelled)break;if(stopped.contains(p)||WatchSync.waiting(data,p,WatchSync.clock.now())||!WatchSync.current(data,job))continue;
     JSONObject item=data.item(job.optString("itemId"));if(!WatchSync.syncedAt(item,p).isEmpty()){WatchSync.absorb(data);continue;}
     if(WatchSync.paced(data,p,WatchSync.clock.now())){stopped.add(p);continue;}
     if(sentOne)try{sleep(WatchSync.SPACING);}catch(Cancelled e){break;}catch(InterruptedException e){Thread.currentThread().interrupt();break;}
     WatchSync.started(data,key);WatchSync.counted(data,p);sentOne=true;
     try{Step step=testStep;if(step!=null)step.send(job,item);else send(job,item);if(WatchSync.current(data,job)){WatchSync.done(data,key);WatchSync.siteWorked(data,p);}else WatchSync.interrupted(data,key);}
     catch(Problem e){if(e.site){WatchSync.siteFailed(data,p,key,e.getMessage());stopped.add(p);}else WatchSync.titleFailed(data,key,item,e.getMessage());}
     catch(Skip e){WatchSync.interrupted(data,key);}
     catch(Cancelled e){WatchSync.interrupted(data,key);break;}
     catch(InterruptedException e){WatchSync.interrupted(data,key);Thread.currentThread().interrupt();break;}
     catch(Exception e){WatchSync.siteFailed(data,p,key,"Website unavailable. Noir will retry later.");stopped.add(p);}
     changed();}
   }finally{release();}}

  void send(JSONObject job,JSONObject item)throws Exception{String p=job.optString("provider");boolean trakt=p.equals("Trakt"),movie=item.optBoolean("movie");
   if(trakt&&item.optInt("season")==0&&!movie)throw title("Specials need a confirmed Trakt match. Mark the correct special on its website; your Noir history remains saved.");
   if(trakt&&!item.optString("imdb").matches("^tt\\d+$"))throw title("Waiting for a verified title ID. Refresh information on your PC.");
   browser();String url=trakt?WatchSync.traktUrl(item):resolveSerializd(item);
   if(trakt)navigate(url);
   if(!trakt&&!verifySerializd(item))throw title("Episode match needs checking in Reviews & comments.");
   String control=waitControl(item,p);
   if(control.equals("watched"))return;
   if(control.equals("signed-out"))throw site("Your website session has expired. Sign in once under More → Connections; Noir resumes syncing automatically.");
   if(!control.equals("ready"))throw site("The website's watched controls are unavailable. Noir will retry automatically.");
   if(!WatchSync.current(data,job))throw new Skip();
   if(!"clicked".equals(control(item,p,true)))throw site("The watched control changed. Noir will retry later.");
   if(trakt){boolean clicked=false;for(int i=0;i<30&&!clicked;i++){check();if(!WatchSync.current(data,job))break;clicked="true".equals(eval(WatchSync.TRAKT_DATE_CONTROL));if(!clicked)sleep(500);}
    if(!clicked)throw site("Trakt's watched-date control is unavailable. Noir will retry automatically.");}
   /* Both sites update their controls optimistically. Reload before calling the job successful. */
   sleep(4000);navigate(url);
   if(!"watched".equals(waitControl(item,p)))throw site("The website hasn't confirmed the watched mark. Noir will recheck later.");}

  String control(JSONObject item,String p,boolean click)throws Exception{Object v=read(p.equals("Serializd")?WatchSync.serializdControl(click):WatchSync.traktControl(item,click));return v instanceof String?(String)v:"waiting";}
  String waitControl(JSONObject item,String p)throws Exception{for(int i=0;i<50;i++){check();String r=control(item,p,false);if(r.equals("watched")||r.equals("ready"))return r;if(r.equals("signed-out")&&i>=10)return r;sleep(500);}return "waiting";}

  /* CommunityPages.ResolveSerializd: search, exact name, premiere year from #__NEXT_DATA__, season id, then the verified episode page. */
  String resolveSerializd(JSONObject item)throws Exception{String cached=WatchSync.serializdEpisode(data,item);if(!cached.isEmpty()){navigate(cached);return cached;}
   if(item.optBoolean("movie")||item.optInt("episode")<1)throw title("Serializd tracks TV episodes only.");
   navigate("https://www.serializd.com/search?searchQuery="+Uri.encode(WatchSync.name(item)));
   String name=WatchSync.normalize(WatchSync.name(item));List<String> candidates=new ArrayList<>();
   for(int attempt=0;attempt<40;attempt++){check();candidates=matches(read(WatchSync.SERIALIZD_RESULTS),name);if(!candidates.isEmpty()){sleep(700);break;}sleep(500);}
   if(candidates.isEmpty())throw title("No exact Serializd title match yet. Choose the correct episode in its review tab.");
   for(String u:matches(read(WatchSync.SERIALIZD_RESULTS),name))if(!candidates.contains(u))candidates.add(u);
   int year=item.optInt("year");if(candidates.size()>1&&year<=0)throw title("More than one show matches. Open Serializd and select the correct episode first.");
   String show=null;JSONObject details=null;
   for(String candidate:candidates.subList(0,Math.min(6,candidates.size()))){navigate(candidate);Object found=read(WatchSync.SERIALIZD_SHOW);if(!(found instanceof JSONObject))continue;JSONObject f=(JSONObject)found;
    if(!WatchSync.normalize(f.optString("name")).equals(name)||year>0&&!f.optString("date").startsWith(String.valueOf(year)))continue;show=candidate;details=f;break;}
   if(show==null)throw title("Serializd's title/year could not be verified. Select its exact episode in Reviews & comments.");
   JSONObject season=null;int count=0;for(JSONObject s:DataStore.objects(details.optJSONArray("seasons")))if(s.has("number")&&s.optInt("number",-1)==item.optInt("season")){season=s;count++;}
   if(count!=1)throw title("Serializd does not have this season yet.");
   String seasonUrl=show+"/season/"+season.opt("id")+"/"+item.optInt("season"),url=seasonUrl+"/episode/"+item.optInt("episode");
   navigate(url);if(!verifySerializd(item))throw title("Serializd's episode could not be verified.");
   WatchSync.serializdSeason(data,item,seasonUrl);return url;}
  static List<String> matches(Object results,String name){List<String> list=new ArrayList<>();if(results instanceof JSONArray)for(JSONObject a:DataStore.objects((JSONArray)results))if(WatchSync.normalize(a.optString("name")).equals(name)&&!list.contains(a.optString("url")))list.add(a.optString("url"));return list;}
  boolean verifySerializd(JSONObject item)throws Exception{Object found=read(WatchSync.SERIALIZD_EPISODE);if(!(found instanceof JSONObject))return false;JSONObject d=(JSONObject)found;int season=item.optInt("season"),episode=item.optInt("episode");
   if(d.optInt("season",-1)!=season||d.optInt("episode",-1)!=episode)return false;
   Uri u=Uri.parse(url());String path=u.getEncodedPath()==null?"":u.getEncodedPath();
   if(!"www.serializd.com".equals(u.getHost())||!path.endsWith("/"+season+"/episode/"+episode)||!path.contains("-"+(d.isNull("show")?"":String.valueOf(d.opt("show")))+"/season/"))return false;
   if(!WatchSync.normalize(d.optString("name")).equals(WatchSync.normalize(WatchSync.name(item))))return false;
   /* Specials can be numbered differently, or combine several shorts into one file. */
   double minutes=d.optDouble("runtime",0),duration=item.optDouble("duration",0);
   return !(season==0&&duration>0&&minutes>0&&Math.abs(duration/60-minutes)>Math.max(10,minutes*.5));}

  // ---- The off-screen browser ----
  void browser()throws Exception{if(web!=null)return;onMain(()->{if(host!=null&&(host.isFinishing()||host.isDestroyed()))throw new Cancelled();
   WebView w=new WebView(host!=null?host:context);configure(w);w.getSettings().setUseWideViewPort(true);w.getSettings().setLoadWithOverviewMode(true);w.setFocusable(false);w.setImportantForAccessibility(View.IMPORTANT_FOR_ACCESSIBILITY_NO_HIDE_DESCENDANTS);
   float density=w.getResources().getDisplayMetrics().density;int width=Math.round(WIDTH_CSS*density),height=Math.round(HEIGHT_CSS*density);
   w.setWebViewClient(new WebViewClient(){
    @Override public WebResourceResponse shouldInterceptRequest(WebView v,WebResourceRequest r){java.util.function.Function<Uri,WebResourceResponse> pages=testPages;if(pages==null)return null;WebResourceResponse page=pages.apply(r.getUrl());return page!=null?page:new WebResourceResponse("text/plain","utf-8",404,"Not Found",new HashMap<>(),new java.io.ByteArrayInputStream(new byte[0]));}
    @Override public boolean shouldOverrideUrlLoading(WebView v,WebResourceRequest r){return !"https".equals(r.getUrl().getScheme())||!HOSTS.contains(r.getUrl().getHost());}
    @Override public void onReceivedError(WebView v,WebResourceRequest r,WebResourceError e){if(r.isForMainFrame())loadFailed=true;}
    @Override public void onReceivedHttpError(WebView v,WebResourceRequest r,WebResourceResponse e){if(r.isForMainFrame()&&e.getStatusCode()>=400)loadFailed=true;}
    @Override public void onPageFinished(WebView v,String url){CountDownLatch l=load;if(l!=null)l.countDown();}});
   w.setWebChromeClient(new WebChromeClient());
   if(host!=null){/* Attached to the visible activity's window far off-screen, so it lays out and runs like a visible page without being seen. */
    ViewGroup decor=(ViewGroup)host.getWindow().getDecorView();android.widget.FrameLayout.LayoutParams lp=new android.widget.FrameLayout.LayoutParams(width,height);w.setTranslationX(-(width+20000));w.setTranslationY(-(height+20000));decor.addView(w,lp);}
   else{w.measure(View.MeasureSpec.makeMeasureSpec(width,View.MeasureSpec.EXACTLY),View.MeasureSpec.makeMeasureSpec(height,View.MeasureSpec.EXACTLY));w.layout(0,0,width,height);}
   w.onResume();w.resumeTimers();web=w;return null;});}
  void release(){WebView w=web;web=null;if(w==null)return;try{onMain(()->{w.stopLoading();ViewParent parent=w.getParent();if(parent instanceof ViewGroup)((ViewGroup)parent).removeView(w);w.destroy();CookieManager.getInstance().flush();return null;});}catch(Exception ignored){}}
  void navigate(String url)throws Exception{check();CountDownLatch l=new CountDownLatch(1);loadFailed=false;load=l;onMain(()->{if(web!=null){web.stopLoading();web.loadUrl(url);}return null;});
   long end=SystemClock.elapsedRealtime()+loadTimeoutMs;while(!l.await(100,TimeUnit.MILLISECONDS)){check();if(SystemClock.elapsedRealtime()>end)throw site("The website could not load. Noir will retry later.");}
   load=null;if(loadFailed)throw site("The website could not load. Noir will retry later.");
   if(Boolean.TRUE.equals(read(WatchSync.CHALLENGE)))throw site("The website's security check wasn't completed. Sign in under More → Connections.");}
  String eval(String script)throws Exception{check();CountDownLatch l=new CountDownLatch(1);String[] out={null};MAIN.post(()->{if(web==null){l.countDown();return;}web.evaluateJavascript(script,v->{out[0]=v;l.countDown();});});
   long end=SystemClock.elapsedRealtime()+15000;while(!l.await(100,TimeUnit.MILLISECONDS)){check();if(SystemClock.elapsedRealtime()>end)throw site("The website's watched controls are unavailable. Noir will retry automatically.");}return out[0]==null?"null":out[0];}
  Object read(String script)throws Exception{Object v=new JSONTokener(eval(script)).nextValue();return v==JSONObject.NULL?null:v;}
  String url()throws Exception{return onMain(()->web==null||web.getUrl()==null?"":web.getUrl());}
  void check(){if(cancelled||host!=null&&(host.isFinishing()||host.isDestroyed()))throw new Cancelled();}
  void sleep(long ms)throws InterruptedException{check();WatchSync.clock.sleep(ms);check();}
 }
 interface MainCall<T>{T call()throws Exception;}
 static <T> T onMain(MainCall<T> call)throws Exception{if(Looper.myLooper()==Looper.getMainLooper())return call.call();Object[] out=new Object[2];CountDownLatch l=new CountDownLatch(1);MAIN.post(()->{try{out[0]=call.call();}catch(Throwable t){out[1]=t;}l.countDown();});
  if(!l.await(30,TimeUnit.SECONDS))throw new IOException("The website could not load. Noir will retry later.");if(out[1] instanceof RuntimeException)throw (RuntimeException)out[1];if(out[1] instanceof Exception)throw (Exception)out[1];if(out[1] instanceof Error)throw (Error)out[1];@SuppressWarnings("unchecked") T t=(T)out[0];return t;}
 /* Refresh the Connections page when a job changes. */
 static void changed(){MAIN.post(()->{Activity f=foreground;if(f instanceof MainActivity&&"Connections".equals(((MainActivity)f).page)&&!f.isFinishing())((MainActivity)f).render();});}
}
