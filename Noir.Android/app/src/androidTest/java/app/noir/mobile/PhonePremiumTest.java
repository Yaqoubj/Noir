package app.noir.mobile;
import android.app.*;
import android.view.*;
import android.widget.*;
import org.json.*;
import org.junit.*;
import java.util.*;
import static org.junit.Assert.*;

/** Premium round on the phone, U147 to U160: Home status strip and tile size, Downloads tabs, History line, Discover title page, download sheet. */
public final class PhonePremiumTest {
 AuditPhoneFixture f;
 @Before public void setup()throws Exception{f=new AuditPhoneFixture();}
 @After public void cleanup()throws Exception{PhoneDensity.cardScale=1;if(f!=null)f.close();}
 static View tagged(View root,Object tag){return AuditPhoneFixture.find(root,v->tag.equals(v.getTag()));}
 static String text(View v){return v instanceof TextView?((TextView)v).getText().toString():"";}
 /** A two-episode show; episode 1 is watched, so Home has a Continue watching row and History has a viewing. */
 List<JSONObject> show(String id)throws Exception{String now=java.time.Instant.now().toString();List<JSONObject> eps=new ArrayList<>();for(int e=1;e<=2;e++){JSONObject i=new JSONObject().put("id",id+"-e"+e).put("group",id).put("title","Premium "+id).put("movie",false).put("imdb","tt9700"+Math.abs(id.hashCode()%1000)).put("season",1).put("episode",e).put("episodeTitle","Episode "+e).put("airsAt","2024-01-0"+e+"T00:00:00Z").put("inLibrary",true);if(e==1)i.put("viewings",new JSONArray().put(new JSONObject().put("id","prem-v-"+id).put("watchedAt",now).put("source","Android")));eps.add(i);}return f.data.rememberPhoneItems(eps);}
 void job(JSONObject item,String status,long bytes,long total)throws Exception{DownloadStore store=NoirApp.downloads;store.enqueue(item);if(status.equals("complete"))java.nio.file.Files.write(store.file(item.optString("id"),false).toPath(),new byte[(int)total]);store.update(item.optString("id"),status,bytes,total,"","");}
 String value(View strip,String key){return text(tagged(tagged(strip,"home-status:"+key),"home-status-value"));}

 @Test public void u147HomeStatusStripSitsUnderTheHeroAndFollowsTheQueue()throws Exception{
  List<JSONObject> eps=show("strip");job(eps.get(0),"complete",4096,4096);job(eps.get(1),"complete",4096,4096);
  MainActivity a=f.launch(MainActivity.class);View[] strip={null};
  f.ui(()->{a.page="Home";a.detail="";a.render();strip[0]=tagged(a.body,"home-status");assertNotNull("Home has the status strip",strip[0]);
   int at=a.body.indexOfChild(strip[0]);assertTrue("The strip is right under the hero, before the rows: child "+at,at>=1&&at<=2);
   assertEquals(Loc.plural(2,"%d saved on this phone","%d saved on this phone"),value(strip[0],"downloads"));
   assertNotNull("Free space tile",tagged(strip[0],"home-status:space"));assertNotNull("Edit Home is on the strip",tagged(strip[0],"home-status:edit"));
   // U148: streaming setup is a tile of the strip while no source is set, and nothing sits above the hero.
   View setup=tagged(a.body,"stream-setup");assertNotNull(setup);assertSame("The setup call is a tile of the strip",((ViewGroup)strip[0]).getChildAt(0),setup.getParent());
   assertEquals("One Edit Home control",1,AuditPhoneFixture.all(a.body,v->v instanceof TextView&&((TextView)v).getText().toString().equals(Loc.t("Edit Home"))).size());});
  // The download tile is live: a new waiting job shows without leaving Home.
  JSONObject extra=f.data.rememberPhoneItem(new JSONObject().put("id","strip-e3").put("group","strip").put("title","Premium strip").put("movie",false).put("imdb","tt9700001").put("season",1).put("episode",3).put("episodeTitle","Episode 3"));job(extra,"paused",10,100);
  f.until(()->Loc.f("%d waiting",1).equals(value(strip[0],"downloads")));
  Instrumentation.ActivityMonitor monitor=f.test.addMonitor(DownloadHubActivity.class.getName(),null,false);
  try{f.ui(()->assertTrue(tagged(strip[0],"home-status:downloads").performClick()));Activity hub=monitor.waitForActivityWithTimeout(8000);assertNotNull("The Downloads tile opens Downloads",hub);f.activities.add(hub);}finally{f.test.removeMonitor(monitor);}
 }

 @Test public void u149U152OwnRowsComeFirstAndTileSizeScalesHomeOnly()throws Exception{
  List<String> order=new ArrayList<>();for(HomeLayout.Row r:HomeLayout.resolve(null,List.of()))order.add(r.id);
  assertTrue("My List and Keep ready come before Top 10 and the suggestions",order.indexOf("list")<order.indexOf("top10")&&order.indexOf("ready")<order.indexOf("because")&&order.indexOf("recently-added")<order.indexOf("picked"));
  show("size");float medium=PhoneDensity.pick(112,128);
  f.data.pref("homeCardSize","Large");MainActivity a=f.launch(MainActivity.class);
  f.ui(()->{a.page="Home";a.detail="";a.render();assertEquals(1.18f,PhoneDensity.cardScale,.001f);assertEquals(medium*1.18f,PhoneDensity.posterWidth(),.01f);
   a.page="Library";a.render();assertEquals("Only Home is scaled",1f,PhoneDensity.cardScale,.001f);assertEquals(medium,PhoneDensity.posterWidth(),.01f);
   a.data.pref("homeCardSize","Small");a.page="Home";a.render();assertEquals(.86f,PhoneDensity.cardScale,.001f);
   a.data.pref("homeCardSize","nonsense");assertEquals("Medium",a.homeLayout.cardSize());assertEquals(1f,a.homeLayout.scale(),.001f);});
 }

 @Test public void u153DownloadsTabsShowOnePartAndOpenWhereTheWorkIs()throws Exception{
  // Nothing at all: the queue, with a way out of the empty page.
  DownloadHubActivity none=f.launch(DownloadHubActivity.class);
  f.ui(()->{none.update();assertEquals("queue",none.tab);assertEquals(View.VISIBLE,tagged(none.body,"downloads-empty").getVisibility());assertNotNull("The empty queue offers Discover",tagged(none.body,"downloads-find"));assertEquals(View.GONE,tagged(none.body,"downloads-none-saved").getVisibility());
   assertNull("No PC link without a paired PC",tagged(none.body,"downloads-link:pc"));assertNotNull(tagged(none.body,"downloads-link:settings"));
   assertNull("Offline videos is the On this phone tab, not a button",AuditPhoneFixture.find(none.body,v->v instanceof TextView&&((TextView)v).getText().toString().equals(Loc.t("Offline on phone"))));none.finish();});
  // Saved files only: it opens on what is on the phone.
  List<JSONObject> eps=show("tabs");job(eps.get(0),"complete",4096,4096);
  DownloadHubActivity saved=f.launch(DownloadHubActivity.class);
  f.ui(()->{saved.update();assertEquals("phone",saved.tab);assertEquals(View.VISIBLE,saved.list.savedPart.getVisibility());assertEquals(View.GONE,saved.list.queuePart.getVisibility());assertEquals(View.GONE,saved.readyBody.getVisibility());assertEquals(View.GONE,tagged(saved.body,"downloads-empty").getVisibility());
   assertEquals(Loc.f("%d saved",1),text(tagged(tagged(saved.body,"downloads-tab:phone"),"tab-note")));assertTrue(tagged(saved.body,"downloads-tab:phone").isSelected());assertNotNull("A saved show carries its poster on its row (U161)",AuditPhoneFixture.find(saved.list.savedPart,v->String.valueOf(v.getTag()).startsWith("saved-group-art:")));assertFalse(tagged(saved.body,"downloads-tab:queue").isSelected());
   assertTrue(tagged(saved.body,"downloads-tab:ready").performClick());assertEquals("ready",saved.tab);assertEquals(View.VISIBLE,saved.readyBody.getVisibility());assertEquals(View.GONE,saved.list.savedPart.getVisibility());assertNotNull(tagged(saved.readyBody,"keep-ready-card"));assertNotNull(tagged(saved.readyBody,"auto-rules-card"));
   assertEquals(Loc.t("Off"),text(tagged(tagged(saved.body,"downloads-tab:ready"),"tab-note")));saved.finish();});
  // Something waiting: it opens on the queue, and Pause all / Resume all show there only.
  job(eps.get(1),"paused",10,100);
  DownloadHubActivity active=f.launch(DownloadHubActivity.class);
  f.ui(()->{active.update();assertEquals("queue",active.tab);assertEquals(View.VISIBLE,active.list.queuePart.getVisibility());assertEquals(View.GONE,active.list.savedPart.getVisibility());View all=tagged(active.body,"all-action");assertEquals(View.VISIBLE,all.getVisibility());
   assertEquals(Loc.f("%d waiting",1),text(tagged(tagged(active.body,"downloads-tab:queue"),"tab-note")));
   active.select("phone");assertEquals("Resume all belongs to the queue",View.GONE,all.getVisibility());active.select("queue");assertEquals(View.VISIBLE,all.getVisibility());active.finish();});
  // A caller can ask for a tab (Home's free space tile, the Keep ready row).
  DownloadHubActivity asked=f.launch(new android.content.Intent(f.real,DownloadHubActivity.class).putExtra("tab","ready"));
  f.ui(()->{asked.update();assertEquals("ready",asked.tab);assertEquals(View.VISIBLE,asked.readyBody.getVisibility());});
 }

 @Test public void u155U156HistoryCarriesTheViewingLifeAndOneImportChip()throws Exception{
  show("life");MainActivity a=f.launch(MainActivity.class);
  f.ui(()->{a.page="History";a.detail="";a.premium.historyView="Up next";a.render();View life=tagged(a.body,"history-life");assertNotNull("The viewing life line is under the heading",life);
   assertTrue(text(life),text(life).startsWith(Loc.plural(1,"%d episode","%d episodes")));
   assertEquals("One chip opens import, export and backup",1,AuditPhoneFixture.all(a.body,v->v instanceof TextView&&((TextView)v).getText().toString().equals(Loc.t("Import & export"))&&v.hasOnClickListeners()).size());
   assertTrue(life.performClick());assertEquals("The line opens Stats","Stats",a.premium.historyView);});
 }

 @Test public void u158U159DiscoverTitlePageHasOneGoldActionAndCompactEpisodeRows()throws Exception{
  DiscoverActivity d=f.launch(DiscoverActivity.class);
  JSONObject title=new JSONObject().put("id","tt9700555").put("noirType","series").put("name","Premium discover").put("description","A show.").put("videos",new JSONArray().put(new JSONObject().put("season",1).put("episode",1).put("name","One").put("released","2020-01-01T00:00:00Z")).put(new JSONObject().put("season",1).put("episode",2).put("name","Two").put("released","2020-01-02T00:00:00Z")).put(new JSONObject().put("season",2).put("episode",1).put("name","Later").put("released","2099-01-01T00:00:00Z")));
  f.ui(()->{if(d.current!=null)d.current.cancel(true);d.title=title;d.season=1;d.showDetails();
   List<View> gold=AuditPhoneFixture.all(d.body,v->v instanceof Button&&((Button)v).getCurrentTextColor()==MainActivity.BLACK);assertEquals("One gold button on the page",1,gold.size());
   assertTrue(text(gold.get(0)),text(gold.get(0)).contains("S01 · E01"));assertSame(gold.get(0),tagged(d.body,"discover-stream"));
   View row=tagged(d.body,"discover-episode:1");assertNotNull(row);assertEquals("An episode row holds its text and two icon buttons",3,((ViewGroup)row).getChildCount());assertTrue("Tapping a released episode streams it",row.hasOnClickListeners());
   assertEquals("No full-width buttons inside episode rows",0,AuditPhoneFixture.all(row,v->v instanceof Button).size());
   assertEquals("Seasons are chips",2,((ViewGroup)tagged(d.body,"discover-seasons")).getChildCount());
   // An unreleased season: nothing to stream or download from its rows, and the season buttons are off.
   d.season=2;d.showDetails();View later=tagged(d.body,"discover-episode:1");assertFalse(later.hasOnClickListeners());assertFalse(DiscoverTest.described(later,Loc.t("Stream episode")).isEnabled());assertFalse(DiscoverTest.described(later,Loc.t("Download episode")).isEnabled());
   assertTrue("Stream still offers the next released episode",tagged(d.body,"discover-stream").isEnabled());});
 }

 @Test public void u160DownloadSheetNamesKeepReadyOnceAndShowsTheStepperForNextEpisodesOnly()throws Exception{
  List<JSONObject> eps=show("sheet");PhoneDownloadsActivity a=f.launch(new android.content.Intent(f.real,PhoneDownloadsActivity.class).putExtra("id",eps.get(0).optString("id")).putExtra("season",true));
  f.ui(()->{View root=a.scopeSheet.root;assertNotNull(AuditPhoneFixture.find(root,v->v instanceof CheckBox&&text(v).equals(Loc.t("Keep this show ready"))));
   assertNull("One name for Keep ready",AuditPhoneFixture.find(root,v->v instanceof TextView&&text(v).contains("Keep new episodes coming")));
   assertNotNull(AuditPhoneFixture.find(root,v->v instanceof TextView&&text(v).startsWith(Loc.t("Quality and limits")+": ")));
   assertNull("The stepper is for Next N episodes only",tagged(root,"scope-next-count"));
   a.options.scope="Next N episodes";a.showForm();assertNotNull(tagged(a.scopeSheet.root,"scope-next-count"));});
 }
}
