package app.noir.mobile;
import android.view.*;
import android.widget.*;
import org.json.*;
import org.junit.*;
import java.util.*;
import static org.junit.Assert.*;

/** The Downloads hub: sectioned job rows with state/speed/progress/actions, Pause all / Resume all, and the Keep ready sheet. Fixture jobs only, no network. */
public final class DownloadHubTest {
 static final long MB=1048576L;
 AuditPhoneFixture f;
 @Before public void setup()throws Exception{f=new AuditPhoneFixture();}
 @After public void cleanup()throws Exception{if(f!=null)f.close();}
 JSONObject job(String id,int episode,String state)throws Exception{JSONObject item=f.data.rememberPhoneItem(new JSONObject().put("id",id).put("group","grp-"+id).put("title","Hub "+id).put("movie",false).put("season",1).put("episode",episode).put("episodeTitle","Episode "+episode));NoirApp.downloads.enqueue(item);return item;}
 void state(String id,String state,long bytes,long total,String error){if(state.equals("complete"))try{java.nio.file.Files.write(NoirApp.downloads.file(id,false).toPath(),new byte[1]);}catch(Exception e){throw new AssertionError(e);}NoirApp.downloads.update(id,state,bytes,total,"\"tag\"",error);}
 /** Downloading with a measurable speed: two samples more than half a second apart. */
 void downloading(String id,int episode)throws Exception{job(id,episode,"queued");state(id,"downloading",10*MB,100*MB,"");Thread.sleep(700);state(id,"downloading",20*MB,100*MB,"");}
 View row(View root,String id){return AuditPhoneFixture.find(root,v->("job-row:"+id).equals(v.getTag()));}
 static View tagged(View root,Object tag){return AuditPhoneFixture.find(root,v->tag.equals(v.getTag()));}
 static String text(View v){return v instanceof TextView?((TextView)v).getText().toString():"";}
 List<String> sections(View root){List<String> tags=new ArrayList<>();for(View v:AuditPhoneFixture.all(root,x->x.getTag() instanceof String&&((String)x.getTag()).startsWith("section:")))tags.add((String)v.getTag());return tags;}
 DownloadHubActivity hub(){DownloadHubActivity a=f.launch(DownloadHubActivity.class);f.ui(a::update);return a;}

 @Test public void sectionsFollowTheAgreedOrderAndEmptyOnesAreHidden()throws Exception{
  DownloadHubActivity empty=hub();f.ui(()->{assertTrue("No sections without jobs",sections(empty.body).isEmpty());assertEquals("Friendly empty state",View.VISIBLE,tagged(empty.body,"downloads-empty").getVisibility());empty.finish();});
  downloading("dl",1);job("wait",2,"queued");job("fail",3,"queued");state("fail","failed",5*MB,100*MB,"Connection interrupted — check internet and Resume / retry.");job("done",4,"queued");state("done","complete",50*MB,50*MB,"");
  DownloadHubActivity a=hub();
  f.ui(()->{assertEquals(Arrays.asList("section:0","section:1","section:2","section:3"),sections(a.body));assertEquals(View.GONE,tagged(a.body,"downloads-empty").getVisibility());
   List<View> order=AuditPhoneFixture.all(a.body,v->v.getTag() instanceof String&&(((String)v.getTag()).startsWith("section:")||((String)v.getTag()).startsWith("job-row:")));List<String> tags=new ArrayList<>();for(View v:order)tags.add((String)v.getTag());
   assertEquals(Arrays.asList("section:0","job-row:dl","section:1","job-row:wait","section:2","job-row:fail","section:3","job-row:done"),tags);
   String summary=text(tagged(a.body,"downloads-summary"));assertTrue(summary,summary.contains("1 downloading")&&summary.contains("1 waiting")&&summary.contains("1 need attention"));});
 }
 @Test public void onlyFinishedJobsShowOnlyTheReadySection()throws Exception{
  job("only",1,"queued");state("only","complete",20*MB,20*MB,"");DownloadHubActivity a=hub();
  f.ui(()->assertEquals(Arrays.asList("section:3"),sections(a.body)));
 }
 @Test public void downloadingRowShowsSpeedTimeLeftAndADeterminateBar()throws Exception{
  downloading("dl",1);DownloadHubActivity a=hub();
  f.ui(()->{View r=row(a.body,"dl");assertNotNull(r);String line=text(tagged(r,"status"));assertTrue(line,line.startsWith("Downloading"));assertTrue("speed in "+line,line.contains("/s"));assertTrue("time left in "+line,line.contains("left"));
   ProgressBar bar=(ProgressBar)tagged(r,"job-bar");assertEquals(View.VISIBLE,((View)bar.getParent()).getVisibility());assertFalse("Known size is a determinate bar",bar.isIndeterminate());assertEquals(200,bar.getProgress());assertTrue(text(tagged(r,"job-pct")).contains("20"));
   assertNotNull("Pause is the action while downloading",tagged(r,"pause"));assertNull(tagged(r,"play"));assertEquals("S01 · E01",text(tagged(r,"episode")));assertNotNull(tagged(r,"job-more"));});
 }
 @Test public void unknownSizeShowsNoTimeLeft()throws Exception{
  job("blind",1,"queued");state("blind","downloading",0,0,"");Thread.sleep(700);state("blind","downloading",5*MB,0,"");DownloadHubActivity a=hub();
  f.ui(()->{View r=row(a.body,"blind");String line=text(tagged(r,"status"));assertFalse("No ETA without a size: "+line,line.contains("left"));assertTrue(((ProgressBar)tagged(r,"job-bar")).isIndeterminate());});
 }
 @Test public void failedRowShowsTheReasonAndRetry()throws Exception{
  job("fail",1,"queued");state("fail","failed",5*MB,100*MB,"Connection interrupted — check internet and Resume / retry.");DownloadHubActivity a=hub();
  f.ui(()->{View r=row(a.body,"fail");String line=text(tagged(r,"status"));assertTrue(line,line.startsWith("Failed — "));assertTrue(line.contains("Connection interrupted"));assertNotNull(tagged(r,"retry"));assertNull(tagged(r,"pause"));});
 }
 @Test public void finishedRowOffersPlayAndNeverPause()throws Exception{
  job("done",1,"queued");state("done","complete",50*MB,50*MB,"");DownloadHubActivity a=hub();
  f.ui(()->{View r=row(a.body,"done");String line=text(tagged(r,"status"));assertTrue(line,line.startsWith("Saved on this phone"));View play=tagged(r,"play");assertNotNull(play);assertTrue(text(play).contains("Play"));assertNull("No Pause on a finished video",tagged(r,"pause"));assertNull(tagged(r,"resume"));assertEquals(View.GONE,((View)tagged(r,"job-bar").getParent()).getVisibility());assertNotNull(tagged(r,"job-more"));});
 }
 @Test public void pauseAllThenResumeAllTogglesTheJobs()throws Exception{
  downloading("dl",1);job("wait",2,"queued");DownloadHubActivity a=hub();
  View all=tagged(a.body,"all-action");f.ui(()->{assertEquals(View.VISIBLE,all.getVisibility());assertEquals("Pause all",text(all));assertTrue(all.performClick());});
  f.until(()->NoirApp.downloads.job("dl").optString("status").equals("paused")&&NoirApp.downloads.job("wait").optString("status").equals("paused")&&text(all).equals("Resume all"));
  f.ui(()->assertTrue(all.performClick()));
  f.until(()->!NoirApp.downloads.job("dl").optString("status").equals("paused")&&!NoirApp.downloads.job("wait").optString("status").equals("paused"));
 }
 @Test public void keepReadyOpensTheSharedSheetAndItsPrimarySavesThePolicy()throws Exception{
  DownloadHubActivity a=hub();assertFalse(PhoneReadyJob.policy(f.data).enabled());
  f.ui(()->DownloadHubActivity.openKeepReady(a,"grp-sheet"));View dialog=null;long end=android.os.SystemClock.elapsedRealtime()+10000;View[] found={null};while(found[0]==null&&android.os.SystemClock.elapsedRealtime()<end){f.ui(()->{for(View root:android.view.inspector.WindowInspector.getGlobalWindowViews())if(root.isShown()&&tagged(root,"noir-sheet")!=null)found[0]=root;});Thread.sleep(40);}dialog=found[0];assertNotNull("Keep ready is a NoirSheet",dialog);View sheetView=dialog;
  f.ui(()->{Switch on=(Switch)AuditPhoneFixture.find(sheetView,v->v instanceof Switch&&text(v).equals("Keep this show ready"));assertNotNull(on);on.setChecked(true);AuditPhoneFixture.click(sheetView,"Save");});f.idle();
  PhoneReady p=PhoneReadyJob.policy(f.data);assertTrue(p.enabled());assertTrue(p.keepReadyJson().has("grp-sheet"));
 }
 @Test public void arabicHubMirrorsButKeepsTheEpisodeLabelLeftToRight()throws Exception{
  f.data.pref("language","ar");Loc.arabic=true;downloading("dl",5);
  DownloadHubActivity a=hub();
  f.ui(()->{assertEquals(View.LAYOUT_DIRECTION_RTL,a.getWindow().getDecorView().getLayoutDirection());View r=row(a.body,"dl");assertNotNull(r);TextView episode=(TextView)tagged(r,"episode");assertEquals("S01 · E05",episode.getText().toString());assertEquals(View.TEXT_DIRECTION_LTR,episode.getTextDirection());String line=text(tagged(r,"status"));assertTrue(line,line.startsWith(Loc.t("Downloading")));
   assertFalse("The status line is translated",line.startsWith("Downloading"));});
 }
}
