package app.noir.mobile;
import android.app.*;
import android.os.SystemClock;
import android.view.*;
import android.widget.*;
import org.json.*;
import org.junit.*;
import java.util.*;
import static org.junit.Assert.*;

/** Reachability: actions that used to be long-press only have a visible ⋯, tips appear once, Settings reach alerts, tips and Keep ready. */
public final class ReachPhoneTest {
 AuditPhoneFixture f;
 @Before public void setup()throws Exception{f=new AuditPhoneFixture();}
 @After public void cleanup()throws Exception{if(f!=null)f.close();}
 // A show with episode 1 watched just now and episode 2 out but unwatched.
 List<JSONObject> show(String id)throws Exception{String now=java.time.Instant.now().toString();List<JSONObject> eps=new ArrayList<>();for(int e=1;e<=2;e++){JSONObject i=new JSONObject().put("id",id+"-e"+e).put("group",id).put("title","Reach "+id).put("movie",false).put("imdb","tt9900"+Math.abs(id.hashCode()%1000)).put("season",1).put("episode",e).put("episodeTitle","Episode "+e).put("airsAt","2024-01-0"+e+"T00:00:00Z").put("inLibrary",true);if(e==1)i.put("viewings",new JSONArray().put(new JSONObject().put("id","reach-v-"+id).put("watchedAt",now).put("source","Android")));eps.add(i);}return f.data.rememberPhoneItems(eps);}
 static View options(View root,String title){return AuditPhoneFixture.find(root,v->v.getContentDescription()!=null&&v.getContentDescription().toString().startsWith(Loc.f("More options for %s",title))&&v.hasOnClickListeners());}
 static View described(View root,String text){return AuditPhoneFixture.find(root,v->v.getContentDescription()!=null&&Loc.t(text).contentEquals(v.getContentDescription())&&v.hasOnClickListeners());}
 static View tip(View root){return AuditPhoneFixture.find(root,v->v.getTag() instanceof String&&((String)v.getTag()).startsWith("tip:")&&v.getVisibility()==View.VISIBLE);}
 View dialogWith(String text){for(View root:android.view.inspector.WindowInspector.getGlobalWindowViews())if(root.isShown()&&AuditPhoneFixture.find(root,v->v instanceof TextView&&v.isShown()&&Loc.t(text).contentEquals(((TextView)v).getText()))!=null)return root;return null;}
 View waitDialog(String text)throws Exception{long end=SystemClock.elapsedRealtime()+10000;View[] found={null};while(SystemClock.elapsedRealtime()<end){f.ui(()->found[0]=dialogWith(text));if(found[0]!=null)return found[0];Thread.sleep(40);}fail("Dialog not shown: "+text);return null;}
 void streaming()throws Exception{PhoneHarbor.phoneCache=new JSONObject().put("manifest","https://noir-reach.invalid/manifest.json");PhoneHarbor.phoneAt=System.currentTimeMillis();PhoneStream.cacheProfile=f.data.profileId;}

 @Test public void continueWatchingCardHasVisibleOptions()throws Exception{
  show("home");MainActivity a=f.launch(MainActivity.class);
  f.ui(()->{a.page="Home";a.detail="";a.render();View more=options(a.body,"Reach home");assertNotNull("Continue Watching card shows ⋯",more);assertTrue(more.performClick());assertNotNull(a.titleActions.dialog);assertTrue("⋯ opens the episode actions",a.titleActions.dialog.isShowing());a.titleActions.dismiss();});
 }
 @Test public void upNextRowHasVisibleOptions()throws Exception{
  show("track");f.data.phoneSetting("historyOnly",true);MainActivity a=f.launch(MainActivity.class);
  f.ui(()->{a.page="History";a.premium.historyView="Up next";a.render();View more=options(a.body,"Reach track");assertNotNull("Up next row shows ⋯",more);assertTrue(more.performClick());assertTrue(a.titleActions.dialog!=null&&a.titleActions.dialog.isShowing());a.titleActions.dismiss();});
 }
 @Test public void homeTipAppearsOnceAndStaysDismissed()throws Exception{
  streaming();show("tip");MainActivity a=f.launch(MainActivity.class);
  f.ui(()->{a.page="Home";a.detail="";a.render();View card=tip(a.body);assertNotNull("Keep ready tip on Home",card);assertEquals("tip:keepReady",card.getTag());AuditPhoneFixture.click(card,"Not now");assertTrue(PhoneTips.seen(f.data,"keepReady"));a.render();assertNull("No second tip on the same day",tip(a.body));});
  f.data.pref("tipDay",java.time.LocalDate.now().minusDays(1).toEpochDay());
  f.ui(()->{a.render();View card=tip(a.body);assertTrue("A dismissed tip never returns",card==null||!"tip:keepReady".equals(card.getTag()));});
  f.ui(()->{f.data.pref("tipDay",-1L);PhoneReadyJob.policy(f.data).setting("enabled",true);assertNotEquals("No Keep ready tip once it is on","keepReady",PhoneTips.homeTip(a,a.ui.continuing()));});
 }
 @Test public void settingsReachAlertsAndTips()throws Exception{
  MainActivity a=f.launch(MainActivity.class);assertFalse(f.data.prefs().optBoolean("alerts",true));
  f.ui(()->{a.page="More";a.detail="";a.render();View alerts=described(a.body,"New-episode alerts");assertNotNull("Alerts row in Settings",alerts);assertTrue(alerts.performClick());});f.idle();
  assertTrue("Settings turns alerts on",f.data.prefs().optBoolean("alerts",false));
  f.ui(()->{View tips=described(a.body,"Tips & shortcuts");assertNotNull("Tips & shortcuts row",tips);assertTrue(tips.performClick());});
  View dialog=waitDialog("Double-tap the video");f.ui(()->{assertNotNull("Tips list names Keep ready",AuditPhoneFixture.find(dialog,v->v instanceof TextView&&Loc.t("Keep ready").contentEquals(((TextView)v).getText())));assertNotNull("Tips list rows lead somewhere",described(dialog,"Compare taste"));Button close=dialog.findViewById(android.R.id.button1);assertNotNull(close);close.performClick();});
 }
 @Test public void keepReadyFromATitleTurnsKeepReadyOn()throws Exception{
  show("ready");MainActivity a=f.launch(MainActivity.class);assertFalse(PhoneReadyJob.policy(f.data).enabled());
  f.ui(()->DownloadHubActivity.openKeepReady(a,"ready"));View dialog=waitDialog("Keep this show ready");
  f.ui(()->{Switch on=(Switch)AuditPhoneFixture.find(dialog,v->v instanceof Switch);assertNotNull(on);on.setChecked(true);assertNotNull("Keep ready opens the shared Noir sheet",AuditPhoneFixture.find(dialog,v->"noir-sheet".equals(v.getTag())));AuditPhoneFixture.click(dialog,"Save");});f.idle();
  PhoneReady p=PhoneReadyJob.policy(f.data);assertTrue("Saving a show turns Keep ready on",p.enabled());assertTrue(p.keepReadyJson().has("ready"));
 }
}
