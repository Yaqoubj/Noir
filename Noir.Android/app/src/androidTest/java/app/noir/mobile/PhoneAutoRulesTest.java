package app.noir.mobile;
import android.app.job.*;
import android.content.*;
import android.view.*;
import android.widget.*;
import androidx.test.platform.app.InstrumentationRegistry;
import org.json.*;
import org.junit.*;
import java.io.*;
import java.nio.file.Files;
import java.util.*;
import static org.junit.Assert.*;

/** Automatic removal and downloading on the phone (A1-A8) and the saved-list filter (O6). Temp folders and fixture jobs only. */
public final class PhoneAutoRulesTest {
 static final long DAY=86400000L,HOUR=3600000L;
 AuditPhoneFixture f;
 @After public void cleanup()throws Exception{PhoneReadyJob.clock=Calendar::getInstance;if(f!=null)f.close();}
 JSONObject json(String text)throws Exception{return new JSONObject(text);}
 Context isolated(){Context real=InstrumentationRegistry.getInstrumentation().getTargetContext();File root=new File(real.getFilesDir(),"auto-tests-"+UUID.randomUUID());root.mkdirs();return new ContextWrapper(real){@Override public Context getApplicationContext(){return this;}@Override public File getFilesDir(){return root;}@Override public File getExternalFilesDir(String type){File external=new File(root,"external");external.mkdirs();return external;}};}
 static String iso(long at){return java.time.Instant.ofEpochMilli(at).toString();}
 /** A completed, Keep-ready-owned (ready) or manual download that has been watched at the given time. */
 JSONObject completed(String id,String group,int episode,boolean ready,boolean movie,long watchedAt,long size)throws Exception{DataStore data=f.data;JSONObject item=new JSONObject().put("id",id).put("group",group).put("title","T "+id).put("movie",movie).put("season",movie?0:1).put("episode",movie?0:episode).put("progress",watchedAt>0?new JSONObject().put("completed",true).put("updatedAt",iso(watchedAt)):new JSONObject());item=data.rememberPhoneItem(item);DownloadStore store=NoirApp.downloads;if(ready)assertTrue(store.enqueueReady(item,null,size));else store.enqueue(item);Files.write(store.file(id,false).toPath(),new byte[(int)size]);store.update(id,"complete",size,size,"","");return item;}
 void fixture()throws Exception{f=new AuditPhoneFixture();}

 /* Migration: a phone-ready.json from before the rules read exactly as today. */
 @Test public void migrationReadsAnOldFileAsToday()throws Exception{
  Context c=isolated();File dir=c.getFilesDir();Files.write(new File(dir,"phone-ready.json").toPath(),"{\"cleanup\":true,\"enabled\":true,\"shows\":{\"s\":2,\"t\":-1},\"removals\":[{\"id\":\"a\",\"at\":123}]}".getBytes("UTF-8"));
  PhoneReady p=new PhoneReady(dir);assertEquals("d7",p.removeCode());assertTrue(p.cleanup());assertEquals("ready",p.removeScope());assertFalse(p.removeMovies());assertEquals(0,p.keepWatched());assertEquals(2,p.ahead());assertEquals("mine",p.showsMode());assertEquals(0,p.winStart());assertEquals(6,p.winEnd());assertTrue(p.wifiOnly());assertTrue(p.needCharging());
  JobInfo job=PhoneReadyJob.jobInfo(c,p);assertEquals(JobInfo.NETWORK_TYPE_UNMETERED,job.getNetworkType());assertTrue(job.isRequireCharging());
  long now=System.currentTimeMillis();p.scheduleRemoval("a","s",now-30*DAY,now);assertEquals(123,p.removals().optJSONObject(0).optLong("at"));assertFalse("A row without a marker stays untouched",p.removals().optJSONObject(0).has("rule"));
  File off=new File(isolated().getFilesDir(),"x");off.mkdirs();Files.write(new File(off,"phone-ready.json").toPath(),"{\"cleanup\":false}".getBytes("UTF-8"));PhoneReady never=new PhoneReady(off);assertEquals("never",never.removeCode());assertFalse(never.cleanup());
  PhoneReady blank=new PhoneReady(isolated().getFilesDir());assertEquals("never",blank.removeCode());
  JSONObject movie=json("{id:'m',movie:true,progress:{completed:true}}"),job2=json("{id:'m',readyManaged:true,readyPickup:true,status:'complete'}");
  assertTrue("A pickup movie Keep ready removes today, so it still does by default",PhoneReady.cleanupEligible(job2,movie,true,false,false));assertTrue(PhoneReady.cleanupEligible(job2,movie,true,false,false,false,false));
  assertFalse("Manual movies are not in the default scope",PhoneReady.cleanupEligible(json("{id:'m',noirManual:true,status:'complete'}"),movie,true,false,false));
 }

 /* A1 */
 @Test public void removalWaitsMatchTheRuleAndRuleChangesNeverMoveSoonerThanADay()throws Exception{
  Context c=isolated();PhoneReady p=new PhoneReady(c.getFilesDir());long now=1_800_000_000_000L;
  p.setting("removeWhen","right");assertEquals("right",p.removeCode());p.scheduleRemoval("r","g",now-DAY,now);assertEquals(now+HOUR,p.removals().optJSONObject(0).optLong("at"));assertEquals("right",p.removals().optJSONObject(0).optString("rule"));p.scheduleRemoval("r2","g",now-10,now);assertEquals(now+HOUR,p.removals().optJSONObject(1).optLong("at"));
  p.protect("r",false);p.protect("r2",false);
  p.setting("removeWhen","days");p.setting("removeDays",1);assertEquals("d1",p.removeCode());p.scheduleRemoval("d",  "g",now,now);assertEquals(now+DAY,p.removals().optJSONObject(0).optLong("at"));p.protect("d",false);
  p.setting("removeDays",30);assertEquals("d30",p.removeCode());p.scheduleRemoval("m","g",now,now);assertEquals(now+30*DAY,p.removals().optJSONObject(0).optLong("at"));
  p.setting("removeDays",7);p.scheduleRemoval("m","g",now,now);assertEquals("Earlier date moves to the new rule, not sooner than a day",now+7*DAY,p.removals().optJSONObject(0).optLong("at"));assertEquals("d7",p.removals().optJSONObject(0).optString("rule"));
  p.setting("removeDays",30);p.scheduleRemoval("m","g",now,now);assertEquals("A later date applies at once",now+30*DAY,p.removals().optJSONObject(0).optLong("at"));
  p.protect("m",false);p.setting("removeDays",30);p.scheduleRemoval("w","g",now-20*DAY,now);long first=p.removals().optJSONObject(0).optLong("at");p.setting("removeDays",1);p.scheduleRemoval("w","g",now-20*DAY,now);assertEquals("min(old, max(new, now + 1 day))",now+DAY,p.removals().optJSONObject(0).optLong("at"));assertTrue(first>now+DAY);
  p.protect("w",false);p.setting("removeWhen","never");p.scheduleRemoval("n","g",now-DAY,now);assertEquals(0,p.removals().length());
  p.showRemoval("g","d30");assertEquals("d30",p.ruleFor("g"));assertEquals("never",p.ruleFor("other"));p.scheduleRemoval("n","g",now,now);assertEquals(now+30*DAY,p.removals().optJSONObject(0).optLong("at"));p.showRemoval("g","never");p.scheduleRemoval("n","g",now,now);assertEquals(0,p.removals().length());p.showRemoval("g","");assertEquals("never",p.ruleFor("g"));
  assertEquals(HOUR,PhoneReady.delayOf("right"));assertEquals(30*DAY,PhoneReady.delayOf("d30"));assertEquals(-1,PhoneReady.delayOf("never"));
 }
 @Test public void aWatchedKeepReadyCopyIsDeletedOnlyWhenItsRuleSaysSo()throws Exception{
  fixture();long now=System.currentTimeMillis();PhoneReady p=PhoneReadyJob.policy(f.data);DownloadStore store=NoirApp.downloads;
  completed("old","ga",1,true,false,now-40*DAY,4);completed("fresh","gb",1,true,false,now-2*DAY,4);
  p.setting("removeWhen","days");p.setting("removeDays",30);PhoneReadyJob.cleanup(f.data,store,p,()->true);
  assertTrue("Watched 40 days ago is scheduled but a first schedule waits a day",store.file("old",false).isFile());assertEquals(2,p.removals().length());
  /* Fast-forward: the same rows scheduled in the past are due now. */
  p.forgetRemoval("old");p.forgetRemoval("fresh");p.scheduleRemoval("old","ga",now-40*DAY,now-35*DAY);p.scheduleRemoval("fresh","gb",now-2*DAY,now-DAY);
  PhoneReadyJob.cleanup(f.data,store,p,()->true);assertFalse("Rule d30 and watched 40 days ago: removed",store.file("old",false).isFile());assertTrue("Watched 2 days ago under d30: kept",store.file("fresh",false).isFile());
  p.setting("removeWhen","right");PhoneReadyJob.retarget(f.data,store);long at=0;for(JSONObject r:DataStore.objects(p.removals()))if(r.optString("id").equals("fresh")){at=r.optLong("at");assertEquals("right",r.optString("rule"));}assertTrue("An earlier rule never lands sooner than about a day",at>=now+DAY-1000&&at<now+30*DAY);
 }

 /* A2 */
 @Test public void everythingNoirDownloadedNeedsTheFlagTheScopeAndRespectsMoviesAndPins()throws Exception{
  JSONObject episode=json("{id:'e',season:1,episode:1,progress:{completed:true}}"),movie=json("{id:'m',movie:true,progress:{completed:true}}"),manual=json("{id:'e',noirManual:true,status:'complete'}"),movieJob=json("{id:'m',noirManual:true,status:'complete'}");
  assertFalse("Default scope leaves manual downloads alone",PhoneReady.cleanupEligible(manual,episode,true,false,false,false,false));assertTrue(PhoneReady.cleanupEligible(manual,episode,true,false,false,true,false));
  assertFalse("Movies are off by default",PhoneReady.cleanupEligible(movieJob,movie,true,false,false,true,false));assertTrue(PhoneReady.cleanupEligible(movieJob,movie,true,false,false,true,true));
  DataStore.put(manual,"pinned",true);assertFalse("Pinned files never",PhoneReady.cleanupEligible(manual,episode,true,false,false,true,true));DataStore.put(manual,"pinned",false);assertFalse("Protected files never",PhoneReady.cleanupEligible(manual,episode,true,false,true,true,true));
  assertFalse("A file the user added themselves (no flag) never",PhoneReady.cleanupEligible(json("{id:'e',status:'complete'}"),episode,true,false,false,true,true));
  fixture();long now=System.currentTimeMillis();DownloadStore store=NoirApp.downloads;PhoneReady p=PhoneReadyJob.policy(f.data);
  completed("man",  "gm",1,false,false,now-40*DAY,4);assertTrue("Manual downloads are flagged",store.job("man").optBoolean("noirManual"));assertTrue(store.job("man").optLong("readyFileBytes")>0);assertTrue(store.ownsReadyFile(store.job("man")));
  completed("rdy","gr",1,true,false,now-40*DAY,4);assertFalse(store.job("rdy").optBoolean("noirManual"));
  p.setting("removeWhen","days");p.setting("removeDays",1);
  p.setting("removeScope","ready");p.scheduleRemoval("rdy","gr",now-40*DAY,now-35*DAY);PhoneReadyJob.cleanup(f.data,store,p,()->true);assertFalse("Keep ready copy is removed",store.file("rdy",false).isFile());assertTrue("Manual copy stays in the default scope",store.file("man",false).isFile());
  p.setting("removeScope","all");p.scheduleRemoval("man","gm",now-40*DAY,now-35*DAY);PhoneReadyJob.cleanup(f.data,store,p,()->true);assertFalse("Everything Noir downloaded: removed",store.file("man",false).isFile());
  completed("mov","gv",0,false,true,now-40*DAY,4);p.scheduleRemoval("mov","",now-40*DAY,now-35*DAY);PhoneReadyJob.cleanup(f.data,store,p,()->true);assertTrue("Movies off by default",store.file("mov",false).isFile());
  p.setting("removeMovies",true);p.scheduleRemoval("mov","",now-40*DAY,now-35*DAY);PhoneReadyJob.cleanup(f.data,store,p,()->true);assertFalse(store.file("mov",false).isFile());
 }

 /* A3 */
 @Test public void theLastWatchedEpisodesOfEachShowAreShielded()throws Exception{
  long now=System.currentTimeMillis();List<JSONObject> items=new ArrayList<>();for(int e=1;e<=4;e++)items.add(json("{id:'a"+e+"',group:'a',season:1,episode:"+e+",progress:{completed:true,updatedAt:'"+iso(now-(10-e)*DAY)+"'}}"));items.add(json("{id:'b1',group:'b',season:1,episode:1,progress:{completed:true,updatedAt:'"+iso(now-DAY)+"'}}"));items.add(json("{id:'a5',group:'a',season:1,episode:5}"));
  assertTrue(PhoneReady.shielded(items,0).isEmpty());assertEquals(new HashSet<>(Arrays.asList("a4","b1")),PhoneReady.shielded(items,1));assertEquals(new HashSet<>(Arrays.asList("a3","a4","b1")),PhoneReady.shielded(items,2));assertEquals(4,PhoneReady.shielded(items,3).size());
  fixture();PhoneReady p=PhoneReadyJob.policy(f.data);DownloadStore store=NoirApp.downloads;long t=System.currentTimeMillis();completed("s1","gs",1,true,false,t-50*DAY,4);completed("s2","gs",2,true,false,t-40*DAY,4);
  p.setting("removeWhen","days");p.setting("removeDays",1);p.setting("keepWatched",1);p.scheduleRemoval("s1","gs",t-50*DAY,t-45*DAY);p.scheduleRemoval("s2","gs",t-40*DAY,t-35*DAY);PhoneReadyJob.cleanup(f.data,store,p,()->true);
  assertFalse("Older watched episode goes",store.file("s1",false).isFile());assertTrue("The newest watched episode stays",store.file("s2",false).isFile());assertEquals("No removal is scheduled for a shielded episode",0,p.removals().length());
 }

 /* A4: no option on the phone; the scheduled-removal rows carry the rule text. */
 @Test public void scheduledRemovalsNameTheirRule()throws Exception{
  assertEquals("After 7 days",PhoneReady.ruleName("d7"));assertEquals("Right after watching",PhoneReady.ruleName("right"));assertEquals("Never",PhoneReady.ruleName("never"));
  Context c=isolated();PhoneReady p=new PhoneReady(c.getFilesDir());p.setting("removeWhen","right");long now=System.currentTimeMillis();p.scheduleRemoval("x","g",now,now);assertEquals("Watched copy scheduled for removal (right after watching)",p.lastReason("x"));
  Loc.arabic=true;try{DownloadHubActivity.words();assertNotEquals(p.lastReason("x"),Loc.t(p.lastReason("x")));assertNotEquals("After 30 days",Loc.t("After 30 days"));}finally{Loc.arabic=false;}
 }

 /* A5 */
 @Test public void howFarAheadHasFiveCountsAGlobalDefaultAndASameAsDefault()throws Exception{
  List<JSONObject> items=new ArrayList<>();for(int e=1;e<=7;e++)items.add(json("{id:'x"+e+"',group:'x',season:1,episode:"+e+",airsAt:'2020-01-01'}"));for(int e=1;e<=3;e++)items.add(json("{id:'y"+e+"',group:'y',season:1,episode:"+e+",airsAt:'2020-01-01'}"));items.add(json("{id:'x8',group:'x',season:2,episode:1,airsAt:'2020-01-01'}"));long now=System.currentTimeMillis();
  assertEquals("Five",5,PhoneReady.select(items,json("{x:5}"),now,2).size());assertEquals("Rest of the season stops at the season",7,PhoneReady.select(items,json("{x:-1}"),now,2).size());
  assertEquals("Same as default uses the default",3,PhoneReady.select(items,json("{x:-2}"),now,3).size());assertEquals(2,PhoneReady.select(items,json("{x:-2}"),now).size());assertEquals("An unknown count falls back to the default",3,PhoneReady.select(items,json("{x:4}"),now,3).size());assertEquals(1,PhoneReady.select(items,json("{x:1}"),now,5).size());
  Context c=isolated();PhoneReady p=new PhoneReady(c.getFilesDir());assertEquals(2,p.ahead());for(int n:new int[]{1,3,5,-1}){p.setting("ahead",n);assertEquals(n,p.ahead());}p.setting("ahead",4);assertEquals("Invalid values read as the default 2",2,p.ahead());
  p.keep("g",5);assertEquals(5,p.keepReadyJson().optInt("g"));p.keep("g",-2);assertEquals(-2,p.keepReadyJson().optInt("g"));assertTrue(p.keepReadyJson().has("g"));p.keep("g",4);assertEquals(3,p.keepReadyJson().optInt("g"));p.keep("g",1);assertEquals("Old values read as before",1,p.keepReadyJson().optInt("g"));
 }

 /* A6 */
 @Test public void whichShowsDownloadAutomaticallyFollowsTheChoiceAndPerShowSwitches()throws Exception{
  Context c=isolated();PhoneReady p=new PhoneReady(c.getFilesDir());
  List<JSONObject> items=Arrays.asList(json("{id:'a1',group:'a',season:1,episode:1,progress:{completed:true}}"),json("{id:'a2',group:'a',season:1,episode:2}"),json("{id:'b1',group:'b',season:1,episode:1,listed:true}"),json("{id:'c1',group:'c',season:1,episode:1,progress:{position:30},status:'Dropped'}"),json("{id:'d1',group:'d',season:1,episode:1,progress:{position:30},hidden:true}"),json("{id:'m',group:'m',movie:true,progress:{completed:true}}"),json("{id:'e1',group:'e',season:1,episode:1,progress:{position:10}}"));
  p.keep("only",2);assertEquals("Default is only the switched-on shows",Collections.singleton("only"),keys(p.effectiveShows(items)));
  p.setting("showsMode","watching");assertEquals(new HashSet<>(Arrays.asList("only","a","e")),keys(p.effectiveShows(items)));assertEquals(-2,p.effectiveShows(items).optInt("a"));assertEquals(2,p.effectiveShows(items).optInt("only"));
  p.setting("showsMode","watchlist");assertEquals(new HashSet<>(Arrays.asList("only","a","b","e")),keys(p.effectiveShows(items)));
  p.keep("a",0);assertEquals("A show switched off stays off against the mode",new HashSet<>(Arrays.asList("only","b","e")),keys(p.effectiveShows(items)));assertFalse(p.wanted("a"));assertTrue(p.wanted("b"));
  p.keep("a",3);assertEquals("Switching it on again overrides",3,p.effectiveShows(items).optInt("a"));assertTrue(p.wanted("a"));
  p.setting("showsMode","mine");assertEquals(new HashSet<>(Arrays.asList("only","a")),keys(p.effectiveShows(items)));
  List<JSONObject> dated=new ArrayList<>(items);dated.add(json("{id:'a3',group:'a',season:1,episode:3,airsAt:'2020-01-01'}"));dated.add(json("{id:'e2',group:'e',season:1,episode:2,airsAt:'2020-01-01'}"));p.setting("showsMode","watching");JSONObject shows=p.effectiveShows(dated);List<JSONObject> plan=PhoneReady.select(dated,shows,System.currentTimeMillis(),p.ahead());Set<String> ids=new HashSet<>();for(JSONObject i:plan)ids.add(i.optString("id"));assertTrue(ids.contains("e2"));assertTrue(ids.contains("a3"));
 }
 static Set<String> keys(JSONObject o){Set<String> k=new HashSet<>();for(Iterator<String> it=o.keys();it.hasNext();)k.add(it.next());return k;}

 /* A8 */
 @Test public void theDownloadWindowCrossesMidnightAndDefaultsToTodays()throws Exception{
  assertTrue(PhoneReady.inWindow(0,0,6));assertTrue(PhoneReady.inWindow(5,0,6));assertFalse(PhoneReady.inWindow(6,0,6));assertFalse(PhoneReady.inWindow(23,0,6));
  assertTrue(PhoneReady.inWindow(23,22,4));assertTrue(PhoneReady.inWindow(2,22,4));assertFalse(PhoneReady.inWindow(4,22,4));assertFalse(PhoneReady.inWindow(12,22,4));assertTrue(PhoneReady.inWindow(22,22,4));
  assertTrue("Start equal to end means any time",PhoneReady.inWindow(13,0,0)&&PhoneReady.inWindow(0,9,9));assertTrue(PhoneReady.overnight(3));assertFalse(PhoneReady.overnight(6));
  Calendar noon=new GregorianCalendar(2026,10,11,12,30,0);assertEquals(11*HOUR+30*60000L,PhoneReadyJob.windowDelay(noon,0,6));assertEquals(9*HOUR+HOUR/2,PhoneReadyJob.windowDelay(noon,22,4));assertEquals(0,PhoneReadyJob.windowDelay(noon,10,14));assertEquals(0,PhoneReadyJob.windowDelay(noon,5,5));
  Context c=isolated();PhoneReady p=new PhoneReady(c.getFilesDir());
  PhoneReadyJob.clock=()->new GregorianCalendar(2026,10,11,12,0,0);assertEquals("Waiting for overnight hours (00:00–06:00)",PhoneReadyJob.conditions(c,p));
  p.setting("winStart",9);p.setting("winEnd",17);p.setting("needCharging",false);p.setting("wifiOnly",false);assertEquals("The window is read from the clock",true,PhoneReadyJob.conditions(c,p).length()==0||!PhoneReadyJob.conditions(c,p).contains("download window")&&!PhoneReadyJob.conditions(c,p).contains("overnight"));
  PhoneReadyJob.clock=()->new GregorianCalendar(2026,10,11,20,0,0);assertEquals("Waiting for your download window (09:00–17:00)",PhoneReadyJob.conditions(c,p));
  p.setting("winStart",5);p.setting("winEnd",5);PhoneReadyJob.clock=()->new GregorianCalendar(2026,10,11,20,0,0);assertFalse("Any time",PhoneReadyJob.conditions(c,p).contains("window"));
  assertEquals(0,new PhoneReady(isolated().getFilesDir()).winStart());
 }
 @Test public void jobConstraintsFollowTheSettingsAndTheJobIsRescheduledWhenTheyChange()throws Exception{
  fixture();Context c=f.context;PhoneReady p=PhoneReadyJob.policy(f.data);JobScheduler jobs=c.getSystemService(JobScheduler.class);
  try{
   JobInfo today=PhoneReadyJob.jobInfo(c,p);assertEquals(JobInfo.NETWORK_TYPE_UNMETERED,today.getNetworkType());assertTrue(today.isRequireCharging());
   p.setting("paused",true);p.setting("enabled",true);PhoneReadyJob.schedule(c);JobInfo pending=jobs.getPendingJob(PhoneReadyJob.JOB_ID);assertNotNull(pending);assertEquals(JobInfo.NETWORK_TYPE_UNMETERED,pending.getNetworkType());assertTrue(pending.isRequireCharging());
   p.setting("needCharging",false);p.setting("wifiOnly",false);PhoneReadyJob.schedule(c);pending=jobs.getPendingJob(PhoneReadyJob.JOB_ID);assertFalse("Charging is a choice now",pending.isRequireCharging());assertEquals(JobInfo.NETWORK_TYPE_ANY,pending.getNetworkType());
   p.setting("needCharging",true);p.setting("wifiOnly",true);PhoneReadyJob.schedule(c);assertTrue(jobs.getPendingJob(PhoneReadyJob.JOB_ID).isRequireCharging());
   PhoneReadyJob.clock=()->new GregorianCalendar(2026,10,11,12,0,0);p.setting("winStart",0);p.setting("winEnd",6);PhoneReadyJob.reschedule(c);JobInfo refill=jobs.getPendingJob(PhoneReadyJob.REFILL_ID);assertNotNull(refill);assertTrue("Delay is until the window opens (12 h)",Math.abs(refill.getMinLatencyMillis()-12*HOUR)<2000);
   p.setting("winStart",18);p.setting("winEnd",22);PhoneReadyJob.reschedule(c);refill=jobs.getPendingJob(PhoneReadyJob.REFILL_ID);assertTrue("Rescheduled with the new window (6 h)",Math.abs(refill.getMinLatencyMillis()-6*HOUR)<2000);
   p.setting("needCharging",false);PhoneReadyJob.reschedule(c);assertFalse(jobs.getPendingJob(PhoneReadyJob.REFILL_ID).isRequireCharging());
   assertEquals("Pickup stays unmetered and charging-free",JobInfo.NETWORK_TYPE_UNMETERED,PhonePickupJob.jobInfo(c).getNetworkType());assertFalse(PhonePickupJob.jobInfo(c).isRequireCharging());
  }finally{jobs.cancel(PhoneReadyJob.JOB_ID);jobs.cancel(PhoneReadyJob.REFILL_ID);}
 }

 /* O6 */
 @Test public void savedListFilterAllWatchedUnwatched()throws Exception{
  fixture();long now=System.currentTimeMillis();completed("w1","gw",1,false,false,now-DAY,4);completed("u1","gu",1,false,false,0,4);completed("w2","gx",1,false,false,now-DAY,4);
  PhoneDownloadsManage m=new PhoneDownloadsManage(f.data,NoirApp.downloads);assertEquals(3,m.groups("Name").size());assertEquals(3,m.groups("Name","All").size());
  List<PhoneDownloadsManage.Group> watched=m.groups("Name","Watched"),unwatched=m.groups("Name","Unwatched");assertEquals(2,watched.size());assertEquals(1,unwatched.size());assertEquals("u1",unwatched.get(0).files.get(0).id());for(PhoneDownloadsManage.Group g:watched)for(PhoneDownloadsManage.Saved s:g.files)assertTrue(s.watched());
  f.data.pref("downloadsFilter","All");DownloadHubActivity a=f.launch(DownloadHubActivity.class);f.ui(a::update);
  f.ui(()->{assertNotNull(DownloadHubTest.tagged(a.body,"saved-filter"));assertNotNull(DownloadHubTest.tagged(a.body,"saved-sort"));assertNotNull(DownloadHubTest.tagged(a.body,"saved-group:gw"));assertNotNull(DownloadHubTest.tagged(a.body,"saved-group:gu"));View pill=DownloadHubTest.tagged(a.body,"saved-filter:2");assertNotNull(pill);assertTrue(pill.performClick());});f.idle();
  f.ui(()->{assertEquals("Unwatched",f.data.prefs().optString("downloadsFilter"));assertNotNull(DownloadHubTest.tagged(a.body,"saved-group:gu"));assertNull(DownloadHubTest.tagged(a.body,"saved-group:gw"));assertNull(DownloadHubTest.tagged(a.body,"saved-filter-empty"));assertNotNull("Free up space still counts the whole list",AuditPhoneFixture.find(a.body,v->v instanceof TextView&&((TextView)v).getText().toString().equals(Loc.t("Free up space"))));});
  f.ui(()->assertTrue(DownloadHubTest.tagged(a.body,"saved-filter:1").performClick()));f.idle();f.ui(()->{assertNull(DownloadHubTest.tagged(a.body,"saved-group:gu"));assertNotNull(DownloadHubTest.tagged(a.body,"saved-group:gw"));assertNotNull(DownloadHubTest.tagged(a.body,"saved-group:gx"));});
  f.ui(()->assertTrue(DownloadHubTest.tagged(a.body,"saved-filter:0").performClick()));f.idle();f.ui(()->assertNotNull(DownloadHubTest.tagged(a.body,"saved-group:gu")));
 }
 @Test public void anEmptyFilterShowsANoteAndKeepsTheSection()throws Exception{
  fixture();completed("u1","gu",1,false,false,0,4);f.data.pref("downloadsFilter","Watched");
  DownloadHubActivity a=f.launch(DownloadHubActivity.class);f.ui(a::update);f.ui(()->{assertNotNull("Section stays",DownloadHubTest.tagged(a.body,"section:3"));assertNotNull(DownloadHubTest.tagged(a.body,"saved-filter-empty"));assertNull(DownloadHubTest.tagged(a.body,"saved-group:gu"));assertNotNull(AuditPhoneFixture.find(a.body,v->v instanceof TextView&&((TextView)v).getText().toString().equals(Loc.t("Free up space"))));});
 }

 /* Settings card */
 @Test public void theSettingsCardShowsEveryChoiceAndChangesThePolicy()throws Exception{
  fixture();DownloadHubActivity a=f.launch(DownloadHubActivity.class);f.ui(a::renderReady);PhoneReady p=PhoneReadyJob.policy(f.data);
  for(String tag:new String[]{"rule-remove","rule-scope","rule-keep-watched","rule-ahead","rule-shows","rule-window"}){View[] v={null};f.ui(()->v[0]=DownloadHubTest.tagged(a.getWindow().getDecorView(),tag));assertNotNull("Row "+tag,v[0]);}
  f.ui(()->assertTrue(DownloadHubTest.tagged(a.getWindow().getDecorView(),"rule-remove").performClick()));View sheet=waitDialog("After 30 days");f.ui(()->DownloadHubTest.tagged(sheet,"After 30 days").performClick());f.idle();assertEquals("d30",p.removeCode());
  f.ui(()->assertTrue(DownloadHubTest.tagged(a.getWindow().getDecorView(),"rule-ahead").performClick()));View ahead=waitDialog("Rest of the season");f.ui(()->DownloadHubTest.tagged(ahead,"5 episodes").performClick());f.idle();assertEquals(5,p.ahead());
  f.ui(()->assertTrue(DownloadHubTest.tagged(a.getWindow().getDecorView(),"rule-shows").performClick()));View shows=waitDialog("Also my watchlist");f.ui(()->DownloadHubTest.tagged(shows,"Shows I am watching").performClick());f.idle();assertEquals("watching",p.showsMode());
  f.ui(()->assertTrue(DownloadHubTest.tagged(a.getWindow().getDecorView(),"rule-scope").performClick()));View scope=waitDialog("Everything Noir downloaded");f.ui(()->DownloadHubTest.tagged(scope,"Everything Noir downloaded").performClick());f.idle();assertEquals("all",p.removeScope());f.ui(()->assertNotNull("Remove movies too appears for that scope",AuditPhoneFixture.find(a.getWindow().getDecorView(),v->v instanceof Switch&&((Switch)v).getText().toString().equals(Loc.t("Remove movies too")))));
  f.ui(()->{DownloadHubActivity.openKeepReady(a,"sheetgrp");});View keep=waitDialog("How far ahead");f.ui(()->{assertNotNull(DownloadHubTest.tagged(keep,"keep-count:0"));assertNotNull(DownloadHubTest.tagged(keep,"keep-remove:0"));assertEquals(Loc.t("Same as default"),((TextView)DownloadHubTest.tagged(keep,"keep-count:0")).getText().toString());DownloadHubTest.tagged(keep,"keep-count:4").performClick();DownloadHubTest.tagged(keep,"keep-remove:5").performClick();((Switch)AuditPhoneFixture.find(keep,v->v instanceof Switch&&DownloadHubTest.text(v).equals("Keep this show ready"))).setChecked(true);AuditPhoneFixture.click(keep,"Save");});f.idle();
  assertEquals(5,p.keepReadyJson().optInt("sheetgrp"));assertEquals("d30",p.showRemoval("sheetgrp"));
 }
 /* U106 / U107 / U105 / U108 / U97 */
 @Test public void conditionsLineFollowsTheRealSettingsAndBudgetHasNoDecimalsWhenWhole()throws Exception{
  fixture();DownloadHubActivity a=f.launch(DownloadHubActivity.class);PhoneReady p=PhoneReadyJob.policy(f.data);
  assertEquals("Overnight · unmetered Wi-Fi · charging · 2 GB kept free",a.readyHint(p));assertEquals("overnight, Wi-Fi, charging",DownloadHubActivity.conditionsWords(p));
  p.setting("wifiOnly",false);p.setting("needCharging",false);p.setting("winStart",8);p.setting("winEnd",20);
  String line=a.readyHint(p),words=DownloadHubActivity.conditionsWords(p);assertTrue(line,line.contains("08:00–20:00")&&line.contains("any network")&&!line.contains("charging")&&!line.contains("Overnight")&&!line.contains("Wi-Fi"));assertTrue(words,words.contains("20:00")&&words.contains("any network")&&!words.contains("charging")&&!words.contains("overnight")&&!words.contains("Wi-Fi"));
  p.setting("winStart",0);p.setting("winEnd",0);p.setting("wifiOnly",true);assertTrue(a.readyHint(p).startsWith("Any time · unmetered Wi-Fi"));assertEquals("any time, Wi-Fi",DownloadHubActivity.conditionsWords(p));
  p.setting("enabled",false);p.setting("winStart",0);p.setting("winEnd",6);p.setting("wifiOnly",false);p.setting("needCharging",true);f.ui(()->DownloadHubActivity.openKeepReady(a,"cond"));View sheet=waitDialog("Keep this show ready");f.ui(()->assertNotNull("The sheet note uses the real settings",AuditPhoneFixture.find(sheet,v->v instanceof TextView&&DownloadHubTest.text(v).contains("(overnight, any network, charging)"))));
  assertEquals("5 GB",DownloadHubActivity.size(5*PhoneReady.GB));assertEquals("10 GB",DownloadHubActivity.size(10*PhoneReady.GB));assertEquals("1.5 GB",DownloadHubActivity.size(PhoneReady.GB*3/2));assertEquals("0 GB",DownloadHubActivity.size(0));
  f.ui(()->assertEquals("No known budget impact line when there is nothing known to add",0,AuditPhoneFixture.all(sheet,v->v instanceof TextView&&DownloadHubTest.text(v).contains("Known budget impact")).size()));
 }
 @Test public void chipsAreCentredWithSidePaddingAndSortSitsOnTheReadyHeader()throws Exception{
  fixture();long now=System.currentTimeMillis();completed("c1","gc",1,false,false,now-DAY,4);DownloadHubActivity a=f.launch(DownloadHubActivity.class);f.ui(a::update);
  f.ui(()->{View pills=DownloadHubTest.tagged(a.body,"saved-filter");assertNotNull(pills);for(View line:AuditPhoneFixture.all(pills,v->v instanceof TextView&&v.getTag()!=null)){TextView t=(TextView)line;assertTrue("side padding "+t.getText(),t.getPaddingLeft()>0&&t.getPaddingRight()>0);assertEquals(View.TEXT_ALIGNMENT_CENTER,t.getTextAlignment());}
   View sort=DownloadHubTest.tagged(a.body,"saved-sort"),head=DownloadHubTest.tagged(a.body,"section:3");assertNotNull(sort);assertNotNull(head);assertSame("Sort is on the Ready to watch header line",head,sort.getParent());assertTrue(DownloadHubTest.text(sort).startsWith(Loc.t("Sort")));});
 }
 @Test public void phoneStringsUseLatinDigitsInArabic()throws Exception{
  Class.forName("app.noir.mobile.DownloadHubActivity");Class.forName("app.noir.mobile.PhoneDownloadsManage");Class.forName("app.noir.mobile.PlayerPrefs");Class.forName("app.noir.mobile.PhoneHarbor");
  for(Map.Entry<String,String> e:Loc.AR.entrySet())assertFalse("Arabic-Indic digit in: "+e.getKey(),e.getValue().matches("(?s).*[\u0660-\u0669\u066b].*"));
  assertEquals("3 حلقات",Loc.AR.get("3 episodes"));assertTrue(Loc.AR.get("Overnight · unmetered Wi-Fi · charging · 2 GB kept free").contains("2"));
 }
 View waitDialog(String text)throws Exception{long end=android.os.SystemClock.elapsedRealtime()+10000;View[] found={null};while(android.os.SystemClock.elapsedRealtime()<end){f.ui(()->{for(View root:android.view.inspector.WindowInspector.getGlobalWindowViews())if(root.isShown()&&AuditPhoneFixture.find(root,v->v instanceof TextView&&v.isShown()&&Loc.t(text).contentEquals(((TextView)v).getText()))!=null)found[0]=root;});if(found[0]!=null)return found[0];Thread.sleep(40);}fail("Dialog not shown: "+text);return null;}
}
