package app.noir.mobile;
import android.view.*;
import android.widget.*;
import org.json.*;
import org.junit.*;
import java.util.*;
import static org.junit.Assert.*;

/** Sharpen round U17, U19, U20, U21, U22: remove / merge / rename decisions on the phone. */
public final class PhoneUiDecisionsTest {
 AuditPhoneFixture f;
 @Before public void setup()throws Exception{f=new AuditPhoneFixture();}
 @After public void cleanup()throws Exception{if(f!=null)f.close();}
 List<JSONObject> show(String id)throws Exception{String now=java.time.Instant.now().toString();List<JSONObject> eps=new ArrayList<>();for(int e=1;e<=2;e++){JSONObject i=new JSONObject().put("id",id+"-e"+e).put("group",id).put("title","Decide "+id).put("movie",false).put("imdb","tt9800"+Math.abs(id.hashCode()%1000)).put("season",1).put("episode",e).put("episodeTitle","Episode "+e).put("airsAt","2024-01-0"+e+"T00:00:00Z").put("inLibrary",true);if(e==1)i.put("viewings",new JSONArray().put(new JSONObject().put("id","dec-v-"+id).put("watchedAt",now).put("source","Android")));eps.add(i);}return f.data.rememberPhoneItems(eps);}
 static List<View> texts(View root,String exact){return AuditPhoneFixture.all(root,v->v instanceof TextView&&((TextView)v).getText().toString().equals(Loc.t(exact)));}
 static List<View> containing(View root,String part){return AuditPhoneFixture.all(root,v->v instanceof TextView&&((TextView)v).getText().toString().contains(part));}

 @Test public void u17SyncProblemShownOnceAsTheHeaderPill()throws Exception{
  // Set the failure after launch: the activity's own first save would otherwise succeed and clear it.
  MainActivity a=f.launch(MainActivity.class);String[] lines=new String[1];
  f.ui(()->{a.data.writeFailure=new RuntimeException("disk full");lines[0]=a.data.problemLine();assertFalse(lines[0].isEmpty());String line=lines[0];a.page="Home";a.render();List<View> shown=AuditPhoneFixture.all(a.root,v->v instanceof TextView&&((TextView)v).getText().toString().equals(a.syncStatus())&&v.isShown());
   assertEquals("The problem is drawn once, as the pill",1,shown.size());assertSame(a.status,shown.get(0));assertTrue("The pill acts on the problem",a.status.hasOnClickListeners());
   assertEquals(0,AuditPhoneFixture.all(a.root,v->v instanceof TextView&&v!=a.status&&((TextView)v).getText().toString().equals(line)).size());
   assertTrue("The pill keeps meaningful accessibility text: ["+a.status.getContentDescription()+"] vs ["+line+"]",String.valueOf(a.status.getContentDescription()).contains(line));
   });
  f.data.writeFailure=null;
 }

 @Test public void u19BackgroundSyncSheetOnlyAfterAFirstSuccessfulSync()throws Exception{
  assertFalse("paired but never synced",BackgroundSync.shouldAsk(true,0,false,true));
  assertTrue("paired and synced",BackgroundSync.shouldAsk(true,1,false,true));
  assertFalse("not paired",BackgroundSync.shouldAsk(false,5,false,true));
  assertFalse("already asked",BackgroundSync.shouldAsk(true,5,false,false));
  assertFalse("TV never asks",BackgroundSync.shouldAsk(true,5,true,true));
 }

 @Test public void u20PickupCardLeavesMoreAndKeepReadyDropsRepeatedLines()throws Exception{
  MainActivity a=f.launch(MainActivity.class);
  f.ui(()->{a.page="Settings";a.detail="";a.render();assertEquals("No loose pickup card on More",0,containing(a.body,Loc.t("Bring unfinished PC episodes")).size());});
  PhoneReadyJob.policy(f.data).setting("enabled",false);PhoneReadyJob.policy(f.data).setting("pickup",false);
  DownloadHubActivity d=f.launch(DownloadHubActivity.class);
  f.ui(()->{d.renderReady();View root=d.getWindow().getDecorView();
   assertEquals("No 'Keep ready is off' line next to the off switch",0,texts(root,"Keep ready is off").size());
   assertEquals("No 'pickup is off' line next to the off switch",0,texts(root,"Unfinished PC pickup is off").size());
   assertEquals("No Pause switch while Keep ready is off",0,AuditPhoneFixture.all(root,v->v instanceof Switch&&((Switch)v).getText().toString().equals(Loc.t("Pause Keep ready"))).size());
   assertEquals("Pickup still lives inside Downloads",1,AuditPhoneFixture.all(root,v->v instanceof Switch&&((Switch)v).getText().toString().equals(Loc.t("Bring unfinished PC episodes"))).size());});
  PhoneReadyJob.policy(f.data).setting("enabled",true);
  f.ui(()->{d.renderReady();View root=d.getWindow().getDecorView();assertEquals("Pause switch appears once Keep ready is on",1,AuditPhoneFixture.all(root,v->v instanceof Switch&&((Switch)v).getText().toString().equals(Loc.t("Pause Keep ready"))).size());});
  PhoneReadyJob.policy(f.data).setting("paused",true);
  f.ui(()->{d.renderReady();assertTrue("A real reason (paused) stays visible",texts(d.getWindow().getDecorView(),"Keep ready paused").size()>0);});
 }

 @Test public void u21OneSetupCardOnHomeAndNoSetupBadgeOnCards()throws Exception{
  show("setup");MainActivity a=f.launch(MainActivity.class);
  f.ui(()->{Loc.arabic=false;a.page="Home";a.detail="";a.render();
   assertEquals("Exactly one Set up streaming element on Home",1,texts(a.body,"Set up streaming").size());
   assertEquals(0,containing(a.body,"SET UP STREAMING").size());assertEquals(1,containing(a.body,"Set up streaming").size());
   assertNotNull(AuditPhoneFixture.find(a.body,v->"stream-setup".equals(v.getTag())));});
  f.ui(()->{try{PhoneHarbor.phoneCache=new JSONObject().put("manifest","https://noir-decide.invalid/manifest.json");}catch(JSONException e){throw new RuntimeException(e);}PhoneHarbor.phoneAt=System.currentTimeMillis();PhoneStream.cacheProfile=f.data.profileId;a.render();
   assertEquals("Streaming set up: Home is unchanged",0,containing(a.body,"Set up streaming").size());});
 }

 @Test public void u22Vocabulary()throws Exception{
  show("vocab");MainActivity a=f.launch(MainActivity.class);
  f.ui(()->{Loc.arabic=false;a.page="Home";a.detail="";a.render();assertEquals(0,containing(a.body,"Continue Watching").size());assertEquals(0,containing(a.body,"NOT ON THIS PC YET").size());
   a.page="History";a.premium.historyView="Up next";a.render();assertEquals(0,containing(a.body,"Watch next").size());});
  f.ui(()->{Loc.arabic=true;try{assertEquals("متابعة المشاهدة",Loc.t("Continue watching"));assertEquals("التالي",Loc.t("Up next"));assertEquals("ليس على هذا الهاتف",Loc.t("Not on this phone"));assertEquals("التالي · ليس على هذا الحاسوب",Loc.t("UP NEXT · NOT ON THIS PC"));}finally{Loc.arabic=false;}});
 }
 /* Round 2 (U101-U105): Connections cards on the phone. */
 static View cardOf(MainActivity a,String title){View t=AuditPhoneFixture.find(a.body,v->v instanceof TextView&&((TextView)v).getText().toString().equals(title));assertNotNull("card title "+title,t);View c=t;while(c.getParent()!=a.body)c=(View)c.getParent();return c;}
 static int dotColor(View card){View dot=AuditPhoneFixture.find(card,v->"dot".equals(v.getTag()));if(dot==null)return 0;return ((android.graphics.drawable.GradientDrawable)dot.getBackground()).getColor().getDefaultColor();}
 @Test public void u101to104BlockedAndFailingSitesOneStremioCardNoLooseNotes()throws Exception{
  MainActivity a=f.launch(MainActivity.class);long now=System.currentTimeMillis();DataStore d=f.data;
  d.stateObject("stremio",new JSONObject().put("key","round2-fake-key"));for(String p:WatchSync.PROVIDERS)WatchSync.enable(d,p,true);
  WatchSync.editProvider(d,"Trakt",pr->{DataStore.put(pr,"failures",3);DataStore.put(pr,"failingSince",now-30*3600000L);DataStore.put(pr,"lastSuccess",now-31*3600000L);});
  WatchSync.editProvider(d,"Serializd",pr->{DataStore.put(pr,"failures",WatchSync.BACKOFF.length);DataStore.put(pr,"failingSince",now-3*86400000L);});
  f.ui(()->{a.page="Connections";a.render();
   View sd=cardOf(a,"Serializd"),tr=cardOf(a,"Trakt");
   assertEquals("Serializd status line",1,texts(sd,"Syncs through your PC").size());assertEquals("blocked dot is grey",PhoneHarbor.GREY,dotColor(sd));assertEquals("no Sign in on a blocked site",0,texts(sd,"Sign in").size());assertEquals("no Retry on a blocked site",0,texts(sd,"Retry").size());assertEquals("Turn off stays",1,texts(sd,"Turn off").size());
   assertEquals("one-day notice is amber",PhoneHarbor.AMBER,dotColor(tr));assertEquals(1,containing(tr,"hasn't accepted marks for a day").size());assertEquals("Trakt keeps Sign in",1,texts(tr,"Sign in").size());
   View st=cardOf(a,"Stremio");assertEquals("one Stremio card",1,texts(a.body,"Stremio").size());assertEquals("no separate Stremio marks card",0,texts(a.body,"Stremio marks").size());
   assertEquals("both switches live in the Stremio card",2,AuditPhoneFixture.all(st,v->v instanceof Switch).size());assertEquals(1,texts(st,"New: try it on one title first, then check it in Stremio.").size());assertEquals("old note is gone",0,containing(a.body,"new · check one title first").size());
   assertEquals("green when healthy",PhoneHarbor.GREEN,dotColor(st));assertTrue("counts line in the Stremio card",containing(st,"synced").size()>0);
   assertEquals("no loose Nuvio note",0,containing(a.body,"Nuvio syncs through your PC").size());assertEquals("no loose Google note",0,containing(a.body,"Google sign-in").size());
   });
  d.stateObject("stremio",new JSONObject());f.ui(()->{a.render();assertEquals("Not connected shows a plain Stremio card and the PC note",1,containing(a.body,"Stremio marks go through your PC").size());assertEquals(0,AuditPhoneFixture.all(a.body,v->v instanceof Switch).size());});
 }
 @Test public void u104HeaderTextNeverUnderTheIconInArabicAndSecondSwitchIndentsOnTheStartSide()throws Exception{
  MainActivity a=f.launch(MainActivity.class);DataStore d=f.data;d.stateObject("stremio",new JSONObject().put("key","round2-fake-key"));WatchSync.enable(d,WatchSync.STREMIO,true);
  f.ui(()->{Loc.arabic=true;try{a.page="Connections";a.render();View st=cardOf(a,"Stremio");View title=AuditPhoneFixture.find(st,v->v instanceof TextView&&((TextView)v).getText().toString().equals("Stremio"));LinearLayout names=(LinearLayout)title.getParent();LinearLayout.LayoutParams lp=(LinearLayout.LayoutParams)names.getLayoutParams();assertTrue("start-side margin keeps the text off the icon",lp.getMarginStart()>0&&lp.getMarginEnd()>0);
   Switch add=(Switch)AuditPhoneFixture.find(st,v->v instanceof Switch&&((Switch)v).getText().toString().equals(Loc.t("Add watched titles to my Stremio library")));assertTrue("second switch indented on the start side",add.getPaddingStart()>0&&add.getPaddingEnd()==0);
  }finally{Loc.arabic=false;}});
 }
 @Test public void u105SharedChipIsCentredWithSidePadding()throws Exception{
  MainActivity a=f.launch(MainActivity.class);f.ui(()->{TextView chip=a.ui.chip("مشاهد",()->{},false);assertTrue(chip.getPaddingLeft()>0&&chip.getPaddingRight()>0);assertEquals(View.TEXT_ALIGNMENT_CENTER,chip.getTextAlignment());assertEquals(Gravity.CENTER,chip.getGravity());});
 }
}
