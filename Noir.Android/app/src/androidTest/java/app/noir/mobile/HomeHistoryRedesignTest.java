package app.noir.mobile;
import android.app.Instrumentation;
import android.content.*;
import android.graphics.Bitmap;
import android.view.*;
import android.widget.*;
import androidx.test.platform.app.InstrumentationRegistry;
import org.json.*;
import org.junit.Test;
import java.io.*;
import java.util.*;
import static org.junit.Assert.*;

public final class HomeHistoryRedesignTest {
 final Instrumentation test=InstrumentationRegistry.getInstrumentation();
 Context isolated(){Context base=test.getTargetContext();File dir=new File(base.getFilesDir(),"hh-d-"+UUID.randomUUID());dir.mkdirs();return new ContextWrapper(base){@Override public Context getApplicationContext(){return this;}@Override public File getFilesDir(){return dir;}};}
 JSONObject item(String id,int episode)throws Exception{return new JSONObject().put("id",id).put("imdb","tt10001").put("group","show").put("title","Fixture show").put("season",1).put("episode",episode).put("genres",new JSONArray().put("Drama"));}
 void close(DataStore data){data.flush();data.network.shutdownNow();data.images.shutdownNow();}
 @Test public void defaultOrderAndOneTimeMigrationKeepPersonalChoices()throws Exception{
  List<String> order=new ArrayList<>();for(HomeLayout.Row r:HomeLayout.resolve(null,List.of()))order.add(r.id);
  assertEquals(Arrays.asList("continue","top10","because","because-2","picked","fav-like","genre-popular","genre-top","unfinished","comfort","directors","short-tonight","list","ready"),order.subList(0,14));
  JSONArray old=new JSONArray("[{\"id\":\"continue\"},{\"id\":\"ready\",\"visible\":false,\"pinned\":true},{\"id\":\"genre:Drama\",\"pinned\":true},{\"id\":\"because\",\"visible\":false},{\"id\":\"list\"}]");
  JSONArray migrated=HomeLayout.migrate(old);List<HomeLayout.Row> rows=HomeLayout.resolve(migrated,List.of("genre:Drama"));List<String> ids=new ArrayList<>();for(HomeLayout.Row r:rows)ids.add(r.id);
  assertEquals(ids.indexOf("list")+1,ids.indexOf("ready"));assertEquals(ids.indexOf("because")+1,ids.indexOf("because-2"));assertTrue(rows.get(ids.indexOf("ready")).pinned);assertFalse(rows.get(ids.indexOf("ready")).visible);assertFalse(rows.get(ids.indexOf("because")).visible);assertTrue(rows.get(ids.indexOf("genre:Drama")).pinned);
  assertEquals(migrated.toString(),HomeLayout.migrate(migrated).toString());assertEquals(5,old.length());
 }
 @Test public void becauseFillsFromFakeCatalogExcludesKnownTitlesAndKeepsLibraryFirst()throws Exception{
  JSONArray catalog=new JSONArray();for(int n=0;n<30;n++)catalog.put(new JSONObject().put("id","tt"+(10000+n)).put("name","Catalog "+n).put("noirType","series"));
  DiscoveryClient.GenreCatalog before=DiscoveryClient.genreOverride;DiscoveryClient.genreOverride=(genre,top)->catalog;
  try{JSONObject library=item("local",1).put("imdb","tt90000"),dropped=item("drop",1).put("imdb","tt10002").put("status","Dropped"),hidden=item("hide",1).put("imdb","tt10003").put("hidden",true),watched=item("seen",1).put("viewings",new JSONArray().put(new JSONObject().put("id","v")));
   JSONArray fake=new DiscoveryClient(test.getTargetContext()).genreCatalog("Drama",false);List<JSONObject> picks=HomeLayout.fillRecommendations(List.of(List.of(library),List.of(dropped),List.of(hidden),List.of(watched)),Set.of("Drama"),fake,20);
   assertEquals(20,picks.size());assertEquals("local",picks.get(0).getString("id"));for(JSONObject i:picks)assertFalse(Set.of("tt10001","tt10002","tt10003").contains(HomeLayout.identity(i)));assertEquals(1,HomeLayout.fillRecommendations(List.of(List.of(library)),Set.of("Drama"),null,20).size());JSONObject noImdb=item("local-two",1).put("imdb","");assertEquals(2,HomeLayout.fillRecommendations(List.of(List.of(library),List.of(noImdb)),Set.of("Drama"),null,20).size());assertTrue(HomeLayout.fillRecommendations(List.of(List.of(library)),Set.of("Drama"),null,20,false).isEmpty());
  }finally{DiscoveryClient.genreOverride=before;}
 }
 @Test public void promptRequiresTheLastPlaybackToBeFinishedAndHonorsSettings()throws Exception{
  DataStore data=new DataStore(isolated());try{long now=System.currentTimeMillis();JSONObject done=item("one",1).put("progress",new JSONObject().put("completed",true).put("updatedAt",java.time.Instant.ofEpochMilli(now-500).toString()));JSONObject half=item("two",2).put("progress",new JSONObject().put("position",900).put("completed",false).put("updatedAt",java.time.Instant.ofEpochMilli(now).toString()));
   assertEquals("one",PhoneJustWatched.finished(List.of(done),now-1000,now).getString("id"));assertNull(PhoneJustWatched.finished(List.of(done,half),now-1000,now));assertNull(PhoneJustWatched.finished(List.of(done),now+1,now));assertNull(PhoneJustWatched.finished(List.of(done),now-1000,now+6*60000));assertTrue(PhoneJustWatched.allowed(data,false));
   for(int n=0;n<4;n++)assertFalse(PhoneJustWatched.ignored(data));assertTrue(PhoneJustWatched.ignored(data));assertFalse(PhoneJustWatched.allowed(data,true));assertFalse(PhoneJustWatched.ignored(data));PhoneJustWatched.keepAsking(data);assertTrue(PhoneJustWatched.allowed(data,false));PhoneJustWatched.ignored(data);PhoneJustWatched.answered(data);assertEquals(0,data.prefs().getInt(PhoneJustWatched.IGNORES));
   data.pref(PhoneJustWatched.MODE,"Finales and movies only");assertFalse(PhoneJustWatched.allowed(data,false));assertTrue(PhoneJustWatched.allowed(data,true));data.pref(PhoneJustWatched.MODE,"Never");assertFalse(PhoneJustWatched.allowed(data,true));assertFalse(PhoneJustWatched.finale(done,List.of(done,half)));assertFalse("the last known episode alone is not proof of a finale",PhoneJustWatched.finale(half,List.of(done,half)));assertTrue(PhoneJustWatched.finale(half,List.of(done,half,item("next",1).put("season",2))));
  }finally{close(data);}
 }
 View exact(View v,String value){if(v instanceof TextView&&!(v instanceof EditText)&&((TextView)v).getText().toString().equals(value))return v;if(v instanceof ViewGroup)for(int n=0;n<((ViewGroup)v).getChildCount();n++){View match=exact(((ViewGroup)v).getChildAt(n),value);if(match!=null)return match;}return null;}
 View description(View v,String value){if(value.equals(String.valueOf(v.getContentDescription())))return v;if(v instanceof ViewGroup)for(int n=0;n<((ViewGroup)v).getChildCount();n++){View match=description(((ViewGroup)v).getChildAt(n),value);if(match!=null)return match;}return null;}
 View kind(View v,Class<?> type){if(type.isInstance(v))return v;if(v instanceof ViewGroup)for(int n=0;n<((ViewGroup)v).getChildCount();n++){View match=kind(((ViewGroup)v).getChildAt(n),type);if(match!=null)return match;}return null;}
 View tag(View v,String tag){if(tag.equals(v.getTag()))return v;if(v instanceof ViewGroup)for(int n=0;n<((ViewGroup)v).getChildCount();n++){View match=tag(((ViewGroup)v).getChildAt(n),tag);if(match!=null)return match;}return null;}
 boolean text(View v,String needle){if(v instanceof TextView&&((TextView)v).getText().toString().contains(needle))return true;if(v instanceof ViewGroup)for(int n=0;n<((ViewGroup)v).getChildCount();n++)if(text(((ViewGroup)v).getChildAt(n),needle))return true;return false;}
 int index(MainActivity a,String needle){for(int n=0;n<a.body.getChildCount();n++)if(text(a.body.getChildAt(n),needle))return n;return -1;}
 void capture(String name)throws Exception{test.waitForIdleSync();Thread.sleep(600);Bitmap bitmap=test.getUiAutomation().takeScreenshot();assertNotNull(bitmap);try(FileOutputStream out=new FileOutputStream(new File(test.getTargetContext().getFilesDir(),name))){bitmap.compress(Bitmap.CompressFormat.PNG,100,out);}bitmap.recycle();}
 @Test public void historyOrderDropUndoHeartAndFinishedPlayerCard()throws Exception{
  DataStore before=NoirApp.data,data=new DataStore(isolated());MainActivity[] activity={null};boolean arabic=Loc.arabic;DiscoveryClient.GenreCatalog catalog=DiscoveryClient.genreOverride;
  try{data.phoneSetting("historyOnly",true);data.pref("experienceChosen",true);data.rememberPhoneItem(item("one",1));data.rememberPhoneItem(item("two",2));long watched=System.currentTimeMillis();data.progress("one","view-one",2500,2500,true);NoirApp.data=data;DiscoveryClient.genreOverride=(genre,top)->new JSONArray();
   activity[0]=(MainActivity)test.startActivitySync(new Intent(test.getTargetContext(),MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK|Intent.FLAG_ACTIVITY_CLEAR_TASK));MainActivity a=activity[0];test.runOnMainSync(()->{Loc.arabic=false;Loc.apply(a);a.page="History";a.detail="";a.premium.historyView="Up next";a.render();assertTrue(index(a,"tap the circle the moment you finish")>=0);assertTrue(index(a,"Recently watched")>index(a,"tap the circle the moment you finish"));View rewards=tag(a.body,"history-rewards");assertNotNull(rewards);assertTrue(a.body.indexOfChild(rewards)>index(a,"Recently watched"));assertTrue(a.body.indexOfChild(rewards)>index(a,"Top four"));assertNotNull(tag(a.body,"just-watched-controls"));
    JSONObject first=data.item("one");a.tracker.stopShowing(first);assertEquals("Dropped",data.item("one").optString("status"));assertEquals(-1,index(a,"tap the circle the moment you finish"));assertNotNull(a.undoAction);a.undoAction.run();assertFalse(data.item("one").optString("status").equals("Dropped"));assertTrue(index(a,"tap the circle the moment you finish")>=0);a.tracker.stopShowing(data.item("one"));a.tracker.bringBack(data.item("one"));assertEquals("Automatic",data.item("one").optString("status"));TextView heart=a.titleActions.favouriteChip(first);heart.performClick();assertTrue(data.liked("show"));a.titleActions.favouriteChip(first).performClick();assertFalse(data.liked("show"));
    a.justWatched.leavingForPlayer();data.progress("two","view-two",2500,2500,true);a.justWatched.returned();assertNotNull(a.justWatched.popup);assertTrue(a.justWatched.popup.isShowing());View controls=tag(a.justWatched.popup.getContentView(),"just-watched-controls");assertNotNull(controls);View loved=description(controls,Loc.t("loved"));assertNotNull(loved);loved.performClick();assertEquals("loved",data.reaction("two"));RatingBar stars=(RatingBar)kind(controls,RatingBar.class);stars.getOnRatingBarChangeListener().onRatingChanged(stars,3.5f,true);assertEquals(7,DataStore.rating10(data.item("two")));exact(controls,Loc.t("Add a note…")).performClick();EditText note=(EditText)kind(controls,EditText.class);note.setText("A favourite scene");exact(controls,Loc.t("Done")).performClick();assertEquals("A favourite scene",data.item("two").optJSONObject("review").optString("note"));assertEquals(7,DataStore.rating10(data.item("two")));a.justWatched.close(false);
    data.pref(PhoneJustWatched.MODE,"Never");a.justWatched.leavingForPlayer();data.progress("two","view-three",2500,2500,true);a.justWatched.returned();assertNull(a.justWatched.popup);
   });capture("hh-d-history-en.png");test.runOnMainSync(()->{a.setLanguage("ar");assertTrue("History is really in Arabic",Loc.arabic&&a.getWindow().getDecorView().getLayoutDirection()==View.LAYOUT_DIRECTION_RTL);assertNotNull(tag(a.body,"just-watched-controls"));assertTrue(text(a.body,"شوهد مؤخرًا"));});test.waitForIdleSync();capture("hh-d-history-ar.png");
  }finally{test.runOnMainSync(()->{if(activity[0]!=null){activity[0].justWatched.close(false);activity[0].finish();}Loc.arabic=arabic;});test.waitForIdleSync();NoirApp.data=before;DiscoveryClient.genreOverride=catalog;close(data);}
 }
}
