package app.noir.mobile;

import android.app.Activity;
import android.app.Instrumentation;
import android.content.*;
import android.net.Uri;
import android.webkit.*;
import androidx.test.platform.app.InstrumentationRegistry;
import org.json.*;
import org.junit.After;
import org.junit.Test;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.util.concurrent.*;
import static org.junit.Assert.*;

/** P1-P5: the phone's own Trakt / Serializd sender against fake site pages served inside the WebView (no network, never the live sites). */
public final class WatchSyncTest {
 final Instrumentation test=InstrumentationRegistry.getInstrumentation();

 static final class TestClock implements WatchSync.Clock {volatile long now=1_800_000_000_000L;public long now(){return now;}public void sleep(long ms)throws InterruptedException{Thread.sleep(Math.min(ms,20));}}

 /* Fake app.trakt.tv and www.serializd.com: state lives here; pages read it, a click records a mark with a synchronous request. */
 static final class Sites {
  final Set<String> watched=ConcurrentHashMap.newKeySet(),down=ConcurrentHashMap.newKeySet(),signedOut=ConcurrentHashMap.newKeySet();
  final Map<String,Integer> loads=new ConcurrentHashMap<>();final List<String> requested=Collections.synchronizedList(new ArrayList<>());final AtomicCounter clicks=new AtomicCounter();
  final List<String[]> shows=Collections.synchronizedList(new ArrayList<>());
  Sites(){shows.add(new String[]{"Fake Show","fake-show-777","777","555","2020-01-01"});shows.add(new String[]{"Other Show","other-show-778","778","556","2019-05-01"});}
  int loads(String host){Integer n=loads.get(host);return n==null?0:n;}
  WebResourceResponse page(Uri u){String host=u.getHost(),path=u.getPath()==null?"":u.getPath();requested.add(u.toString());
   if(path.equals("/fake-mark")){watched.add(u.getQueryParameter("key"));clicks.n++;return html("ok");}
   if(path.equals("/favicon.ico"))return null;
   /* the sign-in pages are opened by the visible sign-in view (also the tests' host screen), never by the sender: not counted as sender page loads */
   if(!u.toString().equals(WatchSync.TRAKT_SIGN_IN)&&!u.toString().equals(WatchSync.SERIALIZD_SIGN_IN))loads.merge(host,1,Integer::sum);
   if(down.contains(host))return new WebResourceResponse("text/html","utf-8",503,"Service Unavailable",new HashMap<>(),new ByteArrayInputStream("down".getBytes(StandardCharsets.UTF_8)));
   if(host.equals("app.trakt.tv")||host.equals("trakt.tv"))return trakt(path);
   if(host.equals("www.serializd.com"))return serializd(u,path);
   return null;}
  WebResourceResponse trakt(String path){String[] p=path.split("/");boolean out=signedOut.contains("app.trakt.tv");
   if(path.startsWith("/auth/"))return html("<form><input name=user><input type=password name=pass></form>");
   if(p.length==7&&p[1].equals("shows")&&p[3].equals("seasons")&&p[5].equals("episodes")){String id=p[2],s=p[4],e=p[6],key=id+":"+s+":"+e;boolean w=watched.contains(key);
    String button=out?"<a href='/auth/signin'>Sign in</a>":w?"<button aria-label='Remove \"Episode "+e+"\" from your History'>done</button>":"<button aria-label='Mark \"Episode "+e+"\" as watched' onclick=\"document.getElementById('dd').style.display='block'\">mark</button>";
    return html("<script>history.replaceState(null,'','/shows/"+id+"?view=episode&season="+s+"&episode="+e+"');</script><div class='trakt-drawer'><h2>S"+s+" • E"+e+" Episode "+e+"</h2>"+button+dropdown(key)+"</div>");}
   if(p.length==3&&p[1].equals("movies")){String id=p[2];boolean w=watched.contains(id);String title="Fake Film";
    String button=out?"<a href='/auth/signin'>Sign in</a>":w?"<button aria-label='Remove \""+title+"\" from your History'>done</button>":"<button aria-label='Mark \""+title+"\" as watched' onclick=\"document.getElementById('dd').style.display='block'\">mark</button>";
    return html("<h1>"+title+"</h1>"+button+"<div class='trakt-drawer'>"+dropdown(id)+"</div>");}
   return null;}
  static String dropdown(String key){return "<div class='trakt-dropdown-group' id='dd' style='display:none'><button onclick=\"const x=new XMLHttpRequest();x.open('GET','/fake-mark?key="+key+"',false);x.send();\">Just now</button></div>";}
  WebResourceResponse serializd(Uri u,String path){boolean out=signedOut.contains("www.serializd.com");
   if(path.equals("/login"))return html("<form><input name=email><input type=password name=password></form>");
   if(path.equals("/search")){StringBuilder b=new StringBuilder();for(String[] s:shows)b.append("<a href='/show/").append(s[1]).append("'><img alt='Poster for ").append(s[0]).append("'></a>");return html(b.toString());}
   for(String[] s:shows){if(path.equals("/show/"+s[1]))return html(next("{\"data\":{\"showDetails\":{\"name\":\""+s[0]+"\",\"premiereDate\":\""+s[4]+"\",\"seasons\":[{\"seasonId\":"+s[3]+",\"seasonNumber\":1}]}}}"));
    String prefix="/show/"+s[1]+"/season/"+s[3]+"/";if(path.startsWith(prefix)){String[] rest=path.substring(prefix.length()).split("/");if(rest.length!=3||!rest[1].equals("episode"))return null;String key=s[1]+":"+rest[0]+":"+rest[2];boolean w=watched.contains(key);
     String control=out?"<a href='/login'>Log in</a>":"<div class='common-show-interact-button' onclick=\"const x=new XMLHttpRequest();x.open('GET','/fake-mark?key="+key+"',false);x.send();this.querySelector('span').textContent='Watched';\"><span>"+(w?"Watched":"Mark watched")+"</span></div>";
     return html(next("{\"showId\":"+s[2]+",\"seasonNumber\":\""+rest[0]+"\",\"episodeNumber\":\""+rest[2]+"\",\"data\":{\"runtime\":30,\"showDetails\":{\"name\":\""+s[0]+"\"}}}")+control);}}
   return null;}
  static String next(String pageProps){return "<script id='__NEXT_DATA__' type='application/json'>{\"props\":{\"pageProps\":"+pageProps+"}}</script>";}
  static WebResourceResponse html(String body){return new WebResourceResponse("text/html","utf-8",200,"OK",new HashMap<>(),new ByteArrayInputStream(("<!doctype html><html><head><meta name='viewport' content='width=device-width'><title>Fake</title></head><body>"+body+"</body></html>").getBytes(StandardCharsets.UTF_8)));}
 }
 static final class AtomicCounter{volatile int n;}

 final class Case implements AutoCloseable {
  final File root;final Context context;final DataStore data;final Sites sites=new Sites();final TestClock clock=new TestClock();Activity host;
  Case()throws InterruptedException{WatchSyncSender.holdForTest(true);Context app=test.getTargetContext();root=new File(app.getFilesDir(),"watch-sync-test/"+UUID.randomUUID());assertTrue(root.mkdirs());context=new ContextWrapper(app){
    @Override public Context getApplicationContext(){return this;}@Override public File getFilesDir(){return root;}@Override public File getExternalFilesDir(String type){return new File(root,"external");}
    @Override public SharedPreferences getSharedPreferences(String name,int mode){return super.getSharedPreferences(root.getName()+name,mode);}};
   data=new DataStore(context);WatchSync.clock=clock;WatchSyncSender.testPages=sites::page;WatchSync.tv=c->false;}
  Activity host(){if(host==null)host=test.startActivitySync(WatchSignInActivity.intent(test.getTargetContext(),"Trakt").addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));return host;}
  void on(String p){data.phoneSetting("sync"+p,true);}
  JSONObject episode(String id,String imdb,String title,int season,int episode)throws Exception{return data.rememberPhoneItem(new JSONObject().put("id",id).put("imdb",imdb).put("movie",false).put("season",season).put("episode",episode).put("title",title).put("group","group-"+imdb).put("year",2020).put("duration",1800));}
  JSONObject movie(String id,String imdb,String title)throws Exception{return data.rememberPhoneItem(new JSONObject().put("id",id).put("imdb",imdb).put("movie",true).put("title",title).put("group","group-"+imdb).put("year",2021).put("duration",6000));}
  String watch(String id){return data.mark(id,true,0,false);}
  void pass(){WatchSyncSender.runForTest(data,host(),context);}
  JSONObject job(String p,String id){return WatchSync.job(data,p+":"+id);}
  public void close()throws InterruptedException{try{if(host!=null){Activity a=host;test.runOnMainSync(a::finish);}data.network.shutdown();data.images.shutdown();DataStore.flushWrites();delete(root);}finally{WatchSyncSender.holdForTest(false);}}
 }
 static void delete(File f){File[] children=f.listFiles();if(children!=null)for(File child:children)delete(child);f.delete();}
 @After public void reset(){WatchSyncSender.testStep=null;WatchSync.clock=WatchSync.SYSTEM;WatchSync.tv=TvUi::isTv;WatchSyncSender.testPages=null;}

 static JSONObject goldenItem(JSONObject i)throws JSONException{return new JSONObject().put("imdb",i.getString("imdb")).put("movie",i.getBoolean("movie")).put("season",i.getInt("season")).put("episode",i.getInt("episode")).put("title",i.getString("title")).put("reviewTitle",i.getString("reviewTitle"));}

 @Test public void javaScriptsAreByteEqualToTheSharedGoldenFile()throws Exception{
  JSONObject golden;try(InputStream in=test.getContext().getAssets().open("watch-controls.json")){golden=new JSONObject(new String(DataStore.read(in,4*1024*1024),StandardCharsets.UTF_8));}
  Map<String,JSONObject> items=new HashMap<>();for(JSONObject i:DataStore.objects(golden.getJSONArray("items")))items.put(i.getString("name"),goldenItem(i));
  int checked=0;Set<String> kinds=new HashSet<>();
  for(JSONObject s:DataStore.objects(golden.getJSONArray("scripts"))){JSONObject item=items.get(s.getString("item"));assertNotNull(s.getString("item"),item);String kind=s.getString("kind");boolean click=s.getBoolean("click");kinds.add(kind);
   String mine=kind.equals("trakt")?WatchSync.traktControl(item,click):kind.equals("serializd")?WatchSync.serializdControl(click):WatchSync.TRAKT_DATE_CONTROL;
   assertEquals(s.getString("item")+" "+kind+" click="+click,s.getString("script"),mine);checked++;}
  assertEquals(16,checked);assertEquals(new HashSet<>(Arrays.asList("trakt","serializd","traktDate")),kinds);
 }

 @Test public void traktEpisodeAndMovieGoReadyClickedConfirmedAndTellThePc()throws Exception{
  try(Case c=new Case()){c.on("Trakt");c.episode("e1","tt7000001","Fake Show",1,2);c.movie("m1","tt7100001","Fake Film");String viewing=c.watch("e1");c.watch("m1");
   assertNotNull("finished viewing queues a Trakt job",c.job("Trakt","e1"));assertNull("Serializd is off",c.job("Serializd","e1"));
   c.pass();
   assertTrue(c.job("Trakt","e1").has("syncedAt"));assertTrue(c.job("Trakt","m1").has("syncedAt"));assertEquals("Watched ✓",c.job("Trakt","e1").optString("message"));
   assertTrue(c.sites.watched.contains("tt7000001:1:2"));assertTrue(c.sites.watched.contains("tt7100001"));assertEquals("one click (Just now) per title",2,c.sites.clicks.n);
   assertTrue("open, then reload to confirm",c.sites.loads("app.trakt.tv")>=4);assertTrue(WatchSync.provider(c.data,"Trakt").optLong("lastSuccess")>0);
   assertEquals(2,c.data.pendingCount("synced"));boolean carried=false;
   for(JSONObject op:DataStore.objects(c.data.backupState().optJSONArray("pending"))){if(op.optString("kind").equals("synced")&&op.optString("viewingId").equals(viewing))assertFalse(op.getJSONObject("synced").optString("Trakt").isEmpty());
    if(op.optString("kind").equals("mark")&&op.optString("viewingId").equals(viewing)){assertFalse("the waiting mark carries synced.Trakt",op.getJSONObject("synced").optString("Trakt").isEmpty());carried=true;}}
   assertTrue(carried);
  }
 }

 @Test public void serializdGoesSearchShowEpisodeReadyClickedConfirmed()throws Exception{
  try(Case c=new Case()){c.on("Serializd");c.episode("e2","tt7000002","Fake Show",1,3);c.movie("m2","tt7100002","Fake Film");c.watch("e2");c.watch("m2");
   assertNull("movies never go to Serializd",c.job("Serializd","m2"));
   c.pass();
   assertTrue(c.job("Serializd","e2").optString("message"),c.job("Serializd","e2").has("syncedAt"));assertTrue(c.sites.watched.contains("fake-show-777:1:3"));assertEquals(1,c.sites.clicks.n);
   assertTrue("season link remembered",WatchSync.serializdEpisode(c.data,c.data.item("e2")).endsWith("/show/fake-show-777/season/555/1/episode/3"));
  }
 }

 @Test public void alreadyWatchedOnTheSiteIsDoneWithoutAClick()throws Exception{
  try(Case c=new Case()){c.on("Trakt");c.on("Serializd");c.episode("e3","tt7000003","Fake Show",1,4);c.sites.watched.add("tt7000003:1:4");c.sites.watched.add("fake-show-777:1:4");c.watch("e3");
   c.pass();
   assertTrue(c.job("Trakt","e3").has("syncedAt"));assertTrue(c.job("Serializd","e3").has("syncedAt"));assertEquals("no click when the site already says watched",0,c.sites.clicks.n);
  }
 }

 @Test public void signedOutIsASiteFailureWithBackoff()throws Exception{
  try(Case c=new Case()){c.on("Trakt");c.on("Serializd");c.episode("e4","tt7000004","Fake Show",1,5);c.episode("e5","tt7000005","Fake Show",1,6);c.sites.signedOut.add("app.trakt.tv");c.sites.signedOut.add("www.serializd.com");c.watch("e4");c.watch("e5");
   c.pass();
   JSONObject sd=WatchSync.provider(c.data,"Serializd"),tr=WatchSync.provider(c.data,"Trakt");
   assertEquals(1,sd.optInt("failures"));assertEquals(c.clock.now+5*60000L,sd.optLong("retryAt"));assertTrue(c.job("Serializd","e5").optString("message").startsWith("Your website session has expired"));
   assertEquals(1,tr.optInt("failures"));assertTrue(c.job("Trakt","e5").optString("message").startsWith("The website's watched controls are unavailable"));
   assertFalse(c.job("Trakt","e4").has("syncedAt"));assertEquals("newest first, and the provider's pass stopped at the first site failure","Waiting to sync",c.job("Trakt","e4").optString("message"));assertEquals("Waiting to sync",c.job("Serializd","e4").optString("message"));
   assertEquals(0,c.sites.clicks.n);
  }
 }

 @Test public void siteDownBacksOffFiveThirtyTwoTwelveThenDailyAndSerializdStopsAtDaily()throws Exception{
  try(Case c=new Case()){c.on("Trakt");c.on("Serializd");c.episode("e6","tt7000006","Fake Show",1,7);c.episode("e7","tt7000007","Fake Show",1,8);c.sites.down.add("app.trakt.tv");c.sites.down.add("www.serializd.com");c.watch("e6");c.watch("e7");
   long[] waits={5*60000L,30*60000L,2*3600000L,12*3600000L,24*3600000L,24*3600000L};
   for(int n=0;n<waits.length;n++){int before=c.sites.loads("app.trakt.tv");c.pass();
    assertEquals("one page load per pass, not one per title (pass "+n+")",before+1,c.sites.loads("app.trakt.tv"));
    JSONObject tr=WatchSync.provider(c.data,"Trakt");assertEquals(n+1,tr.optInt("failures"));assertEquals("wait "+n,c.clock.now+waits[n],tr.optLong("retryAt"));
    c.clock.now+=waits[n]-60000;c.pass();assertEquals("still waiting",before+1,c.sites.loads("app.trakt.tv"));c.clock.now+=60000;}
   assertTrue("Serializd stops once its backoff reached daily",WatchSync.refused(c.data,"Serializd"));int sd=c.sites.loads("www.serializd.com");c.clock.now+=3*24*3600000L;c.pass();assertEquals(sd,c.sites.loads("www.serializd.com"));
   assertTrue(WatchSync.failingForADay(c.data,"Trakt",c.clock.now));
   c.sites.down.clear();WatchSync.retry(c.data,null);assertFalse(WatchSync.refused(c.data,"Serializd"));c.pass();
   assertTrue(c.job("Trakt","e6").has("syncedAt"));assertTrue(c.job("Trakt","e7").has("syncedAt"));assertTrue(c.job("Serializd","e7").has("syncedAt"));
   assertEquals("success resets the backoff",0,WatchSync.provider(c.data,"Trakt").optInt("failures"));assertFalse(WatchSync.failingForADay(c.data,"Trakt",c.clock.now));
  }
 }

 @Test public void perTitleFailureWaitsForRetryAndDoesNotBlockOthers()throws Exception{
  try(Case c=new Case()){c.on("Trakt");c.on("Serializd");c.episode("a","","Missing Show",1,1);c.episode("b","tt7000009","Fake Show",1,9);c.watch("a");c.watch("b");
   c.pass();
   assertTrue(c.job("Trakt","a").optBoolean("held"));assertTrue(c.job("Trakt","a").optString("message").startsWith("Waiting for a verified title ID"));
   assertTrue(c.job("Serializd","a").optBoolean("held"));assertTrue(c.job("Serializd","a").optString("message").startsWith("No exact Serializd title match"));
   assertTrue("the next title still runs",c.job("Trakt","b").has("syncedAt"));assertTrue(c.job("Serializd","b").has("syncedAt"));
   assertEquals("no provider backoff for a title problem",0,WatchSync.provider(c.data,"Trakt").optInt("failures"));assertEquals(0,WatchSync.provider(c.data,"Serializd").optInt("failures"));
   c.clock.now+=7*24*3600000L;assertTrue("a held title is not retried by time alone",WatchSync.due(c.data,c.clock.now).isEmpty());
   WatchSync.retry(c.data,"Trakt");assertEquals(1,WatchSync.due(c.data,c.clock.now).size());
  }
 }

 @Test public void aViewingTheOtherDeviceSyncedIsNotQueuedOrIsDoneWithoutAPage()throws Exception{
  try(Case c=new Case()){c.on("Trakt");c.on("Serializd");
   JSONObject pc=new JSONObject().put("id","s1").put("imdb","tt7000010").put("movie",false).put("season",1).put("episode",1).put("title","Fake Show").put("group","g10").put("year",2020)
    .put("viewings",new JSONArray().put(new JSONObject().put("id",UUID.randomUUID().toString()).put("watchedAt","2026-11-10T20:00:00Z").put("synced",new JSONObject().put("Trakt","2026-11-10T20:05:00.0000000Z"))));
   c.data.stateObject("library",new JSONObject().put("items",new JSONArray().put(pc)));c.watch("s1");
   assertNull("synced.Trakt from the PC: no Trakt job",c.job("Trakt","s1"));assertNotNull("Serializd still queued",c.job("Serializd","s1"));
   c.episode("s2","tt7000011","Fake Show",1,2);c.watch("s2");assertNotNull(c.job("Trakt","s2"));
   JSONObject later=new JSONObject(pc.toString()).put("id","s2").put("imdb","tt7000011").put("episode",2);later.getJSONArray("viewings").getJSONObject(0).put("synced",new JSONObject().put("Trakt","2026-11-11T09:00:00Z").put("Serializd","2026-11-11T09:00:00Z"));
   c.data.stateObject("library",new JSONObject().put("items",new JSONArray().put(pc).put(later)));c.sites.signedOut.add("www.serializd.com");
   c.pass();
   assertTrue(c.job("Trakt","s2").has("syncedAt"));assertEquals("PC",c.job("Trakt","s2").optString("by"));assertTrue(c.job("Serializd","s2").has("syncedAt"));
   assertEquals("no Trakt page for a PC-synced viewing",0,c.sites.loads("app.trakt.tv"));
  }
 }

 @Test public void neverRunsOrQueuesOnTv()throws Exception{
  try(Case c=new Case()){c.on("Trakt");c.episode("t1","tt7000012","Fake Show",1,1);c.episode("t2","tt7000013","Fake Show",1,2);c.watch("t1");assertNotNull(c.job("Trakt","t1"));
   WatchSync.tv=x->true;c.watch("t2");assertNull("TV viewings are not queued",c.job("Trakt","t2"));
   int passes=WatchSyncSender.passes;WatchSyncSender.wake();test.waitForIdleSync();Thread.sleep(500);test.waitForIdleSync();assertEquals("no pass on TV",passes,WatchSyncSender.passes);
   c.pass();assertFalse("a TV never sends",c.job("Trakt","t1").has("syncedAt"));assertEquals(0,c.sites.loads("app.trakt.tv"));
  }
 }

 @Test public void detachedWebViewWithExplicitLayoutAlsoWorks()throws Exception{
  try(Case c=new Case()){c.on("Trakt");c.on("Serializd");c.episode("d1","tt7000014","Fake Show",1,11);c.watch("d1");
   WatchSyncSender.runForTest(c.data,null,c.context);
   assertTrue(c.job("Trakt","d1").optString("message"),c.job("Trakt","d1").has("syncedAt"));assertTrue(c.job("Serializd","d1").optString("message"),c.job("Serializd","d1").has("syncedAt"));assertEquals(2,c.sites.clicks.n);
  }
 }

 /* The real app's guard: screen resumes, wakes and the tick all go through startWith on the main thread, and only one pass runs at a time in the process. */
 @Test public void twoStartsAtOnceRunOnePassAndClickOnce()throws Exception{
  try(Case c=new Case()){c.on("Trakt");c.episode("g1","tt7000021","Fake Show",1,21);c.watch("g1");Activity host=c.host();
   test.runOnMainSync(()->{WatchSyncSender.startWith(c.data,host,c.context);WatchSyncSender.startWith(c.data,host,c.context);WatchSyncSender.startWith(c.data,host,c.context);});
   boolean[] idle={false};for(int n=0;n<600&&!idle[0];n++){Thread.sleep(100);test.runOnMainSync(()->idle[0]=!WatchSyncSender.busy);}
   assertTrue("the pass finished",idle[0]);
   assertEquals("one click for one job, however many starts",1,c.sites.clicks.n);assertTrue(c.job("Trakt","g1").optString("message"),c.job("Trakt","g1").has("syncedAt"));
  }
 }

 @Test public void signInRowsOpenEachSitesOwnSignInPage()throws Exception{
  Sites sites=new Sites();WatchSyncSender.testPages=sites::page;
  for(String p:WatchSync.PROVIDERS){WatchSignInActivity a=(WatchSignInActivity)test.startActivitySync(WatchSignInActivity.intent(test.getTargetContext(),p).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
   try{Uri u=Uri.parse(a.opened);if(p.equals("Trakt")){assertEquals("app.trakt.tv",u.getHost());assertEquals("/",u.getPath());}else{assertEquals("www.serializd.com",u.getHost());assertEquals("/login",u.getPath());}
    long end=System.currentTimeMillis()+10000;while(System.currentTimeMillis()<end&&!sites.requested.contains(a.opened))Thread.sleep(100);assertTrue("the visible view loaded "+a.opened,sites.requested.contains(a.opened));
    String[] ua={null,null};test.runOnMainSync(()->{ua[0]=a.web.getSettings().getUserAgentString();ua[1]=WebSettings.getDefaultUserAgent(a);});assertEquals("the WebView's own user agent",ua[1],ua[0]);
   }finally{test.runOnMainSync(a::finish);}}
 }

 @Test public void disconnectingStremioKeepsTraktAndSerializdCookies()throws Exception{
  CookieManager cookies=CookieManager.getInstance();
  test.runOnMainSync(()->{cookies.setCookie("https://web.stremio.com","stremio_session=abc; Path=/");cookies.setCookie("https://www.stremio.com","stremio_domain=abc; Domain=.stremio.com; Path=/");cookies.setCookie("https://app.trakt.tv","trakt_session=keep; Path=/");cookies.setCookie("https://www.serializd.com","sd_session=keep; Path=/");cookies.flush();});
  test.runOnMainSync(HistoryToolsActivity::forgetStremio);
  long end=System.currentTimeMillis()+5000;String stremio="x";while(System.currentTimeMillis()<end){String[] v={null};test.runOnMainSync(()->v[0]=cookies.getCookie("https://web.stremio.com"));stremio=v[0];if(stremio==null||stremio.isEmpty())break;Thread.sleep(100);}
  assertTrue("Stremio cookies gone: "+stremio,stremio==null||stremio.isEmpty());
  String[] kept={null,null};test.runOnMainSync(()->{kept[0]=cookies.getCookie("https://app.trakt.tv");kept[1]=cookies.getCookie("https://www.serializd.com");});
  assertTrue(String.valueOf(kept[0]),kept[0]!=null&&kept[0].contains("trakt_session=keep"));assertTrue(String.valueOf(kept[1]),kept[1]!=null&&kept[1].contains("sd_session=keep"));
  test.runOnMainSync(()->{cookies.setCookie("https://app.trakt.tv","trakt_session=; Max-Age=0; Path=/");cookies.setCookie("https://www.serializd.com","sd_session=; Max-Age=0; Path=/");});
 }

 /* S1 + S2: 5,000 queued jobs for PC-library titles, the page step faked: newest first, 30 an hour, 200 a day, counters survive a restart, nothing dropped. */
 @Test public void bigQueueGoesNewestFirstAtThirtyAnHourAndTwoHundredADay()throws Exception{
  try(Case c=new Case()){c.on("Trakt");List<JSONObject> items=new ArrayList<>();JSONObject jobs=new JSONObject();long base=1_700_000_000_000L;List<JSONObject> order=new ArrayList<>();
   for(int n=0;n<5000;n++){String id="big"+n;items.add(new JSONObject().put("id",id).put("imdb","tt8"+String.format(Locale.ROOT,"%06d",n)).put("movie",true).put("title","Big "+n).put("group","big-"+n).put("viewings",new JSONArray().put(new JSONObject().put("id",UUID.randomUUID().toString()).put("watchedAt","2023-01-01T00:00:00Z"))));
    JSONObject j=new JSONObject().put("provider","Trakt").put("itemId",id).put("viewingId",UUID.randomUUID().toString()).put("watchedAt",base+(n*7919L%5000)*60000L).put("retryAt",0).put("attempts",0).put("message","Waiting to sync");jobs.put("Trakt:"+id,j);order.add(j);}
   JSONArray library=new JSONArray();for(JSONObject i:items)library.put(i);c.data.stateObject("library",new JSONObject().put("items",library));c.data.stateObject("watchSync",new JSONObject().put("jobs",jobs));
   Collections.sort(order,(a,b)->Long.compare(b.optLong("watchedAt"),a.optLong("watchedAt")));List<String> expected=new ArrayList<>();for(JSONObject j:order)expected.add(j.optString("itemId"));
   List<String> sent=Collections.synchronizedList(new ArrayList<>());WatchSyncSender.testStep=(job,item)->sent.add(job.optString("itemId"));
   long t0=c.clock.now;WatchSyncSender.runForTest(c.data,null,c.context);assertEquals("30 in the first hour",30,sent.size());assertEquals("newest first",expected.subList(0,30),new ArrayList<>(sent));
   WatchSyncSender.runForTest(c.data,null,c.context);assertEquals("the hour cap holds",30,sent.size());
   int[] totals={60,90,120,150,180,200,200};for(int h=1;h<=7;h++){c.clock.now=t0+h*3600000L;WatchSyncSender.runForTest(c.data,null,c.context);assertEquals("after hour "+h,totals[h-1],sent.size());}
   DataStore.flushWrites();DataStore restarted=new DataStore(c.context);assertTrue("the day count survives a restart",WatchSync.paced(restarted,"Trakt",c.clock.now));restarted.network.shutdown();restarted.images.shutdown();
   c.clock.now=t0+WatchSync.DAY+1;WatchSyncSender.runForTest(c.data,null,c.context);assertEquals("a rolling day frees the first hour's 30",230,sent.size());
   assertEquals(expected.subList(0,230),new ArrayList<>(sent));assertEquals(230,WatchSync.count(c.data,"Trakt",true));assertEquals("nothing dropped",4770,WatchSync.count(c.data,"Trakt",false));
  }
 }

 @Test public void newLabelsHaveArabic()throws Exception{Class.forName("app.noir.mobile.PhoneHarbor");Class.forName("app.noir.mobile.WatchSync");
  for(String s:new String[]{"Waiting to sync","Syncing…","Watched ✓",WatchSync.GOOGLE,"Serializd does not allow this from the phone · it syncs through your PC","Nuvio syncs through your PC.","%d synced · %d waiting","last success %s","%s hasn't accepted marks for a day. Sign in again or tap Retry.","Sign in to %s","‹ Connections","If the site shows a security check, complete it yourself here.","Your website session has expired. Sign in once under More → Connections; Noir resumes syncing automatically.","The website could not load. Noir will retry later.","Turn on","Turn off","Retry","next try %s","Marks what you finish on this phone as watched on %s, using your sign-in here."}){
   assertTrue(s,Loc.AR.containsKey(s));assertTrue(s,Loc.AR.get(s).matches("(?s).*[\\u0600-\\u06FF].*"));}
 }
}
