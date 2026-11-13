package app.noir.mobile;

import android.app.*;
import android.content.*;
import android.view.*;
import android.widget.*;
import androidx.test.platform.app.InstrumentationRegistry;
import org.json.*;
import org.junit.*;
import java.io.File;
import java.util.*;
import static org.junit.Assert.*;

public final class StandalonePhoneTest {
    final Instrumentation test=InstrumentationRegistry.getInstrumentation();DataStore previous,data;Context context;
    @Before public void setup(){Context real=test.getTargetContext();File root=new File(real.getFilesDir(),"standalone-"+UUID.randomUUID());root.mkdirs();context=new ContextWrapper(real){@Override public Context getApplicationContext(){return this;}@Override public File getFilesDir(){return root;}@Override public File getExternalFilesDir(String type){return root;}};previous=NoirApp.data;data=new DataStore(context);NoirApp.data=data;assertFalse(data.paired());}
    @After public void cleanup(){NoirApp.data=previous;data.network.shutdownNow();data.images.shutdownNow();}
    boolean has(View v,String text){if(v instanceof TextView&&((TextView)v).getText().toString().contains(text))return true;if(v instanceof ViewGroup)for(int n=0;n<((ViewGroup)v).getChildCount();n++)if(has(((ViewGroup)v).getChildAt(n),text))return true;return false;}
    JSONObject episode(int n,String airs)throws Exception{return new JSONObject().put("id","standalone-e"+n).put("group","show:tt123").put("title","Standalone show").put("imdb","tt123").put("season",1).put("episode",n).put("airsAt",airs).put("available",false);}
    @Test public void welcomeAndSettingsWorkWithoutPairing()throws Exception{
        MainActivity a=(MainActivity)test.startActivitySync(new Intent(test.getTargetContext(),MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
        try{test.runOnMainSync(()->{assertTrue(has(a.body,"Just track what I watch"));assertTrue(has(a.body,"Watch & track"));assertFalse(has(a.body,"Pair with my PC"));a.page="Settings";a.render();assertTrue(has(a.body,"Only on this phone"));assertTrue(has(a.body,"Playback"));assertTrue(has(a.body,"Optional sync with Windows"));assertFalse(has(a.body,"changes waiting to sync"));a.page="More";a.render();assertTrue(has(a.body,"Your space"));});}finally{test.runOnMainSync(a::finish);}
    }
    @Test public void discoveryListAndLibraryPersistWithoutPc()throws Exception{
        DiscoverActivity a=(DiscoverActivity)test.startActivitySync(new Intent(test.getTargetContext(),DiscoverActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
        try{JSONObject title=new JSONObject().put("id","tt123").put("noirType","series").put("name","Standalone show").put("videos",new JSONArray().put(new JSONObject().put("season",1).put("episode",1).put("released","2020-01-01T00:00:00Z")).put(new JSONObject().put("season",1).put("episode",2).put("released","2020-01-02T00:00:00Z")));
            test.runOnMainSync(()->{if(a.current!=null)a.current.cancel(true);a.title=title;a.season=1;a.showDetails();assertFalse("No PC download without a paired PC",has(a.body,"to PC"));assertTrue(has(a.body,"↓ Season to phone"));assertNotNull(DiscoverTest.described(a.body,"Stream episode"));assertNotNull(DiscoverTest.described(a.body,"Download episode"));a.saveList();assertTrue(a.feedback.getText().toString().contains("Saved to My List"));});
            List<JSONObject> items=DataStore.objects(data.items());assertEquals(2,items.size());assertTrue(items.stream().allMatch(i->i.optBoolean("listed")));assertTrue(items.stream().noneMatch(DataStore::watched));data.library(items.get(0).optString("id"),true);
            DataStore reopened=new DataStore(context);assertFalse(reopened.paired());assertEquals(2,reopened.items().length());assertTrue(DataStore.objects(reopened.items()).stream().allMatch(i->i.optBoolean("listed")&&i.optBoolean("inLibrary")));reopened.network.shutdownNow();reopened.images.shutdownNow();
        }finally{test.runOnMainSync(a::finish);}
    }
    @Test public void continueWatchingUsesPhoneSchedule()throws Exception{
        JSONObject one=episode(1,"2020-01-01T00:00:00Z"),two=episode(2,"2020-01-02T00:00:00Z"),future=episode(3,"2099-01-01T00:00:00Z");for(JSONObject i:List.of(one,two,future))data.rememberPhoneItem(i);data.mark(one.optString("id"),true);
        MainActivity a=(MainActivity)test.startActivitySync(new Intent(test.getTargetContext(),MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
        try{test.runOnMainSync(()->{NoirUi.Next next=a.ui.next(a.group("show:tt123"));assertNotNull(next);assertEquals(2,next.item.optInt("episode"));assertEquals(NoirUi.MISSING,next.kind);assertFalse(a.ui.action(next).contains("PC"));});assertEquals(2,PhoneSeasonPlan.nextStreamable(one,DataStore.objects(data.items()),System.currentTimeMillis()).optInt("episode"));data.mark(two.optString("id"),true);assertNull(PhoneSeasonPlan.nextStreamable(two,DataStore.objects(data.items()),System.currentTimeMillis()));test.runOnMainSync(()->{NoirUi.Next next=a.ui.next(a.group("show:tt123"));assertEquals(NoirUi.UPCOMING,next.kind);});}finally{test.runOnMainSync(a::finish);}
    }
    @Test public void phonePlaybackPreferencesSurviveRestartAndBackup(){data.phoneSetting("subtitles","Arabic");data.phoneSetting("autoNext",false);data.phoneSetting("autoSkipIntro",false);DataStore reopened=new DataStore(context);try{assertEquals("Arabic",reopened.subtitleLanguage("new-video"));assertFalse(reopened.playbackFlag("autoNext",true));assertFalse(reopened.playbackFlag("autoSkipIntro",true));reopened.subtitlePreference("chosen-video","Off",1.5);assertEquals("Off",reopened.subtitleLanguage("chosen-video"));assertTrue(reopened.backupState().has("phoneSettings"));}finally{reopened.network.shutdownNow();reopened.images.shutdownNow();}}
    @Test public void savingStreamSettingsInvalidatesOldConnection()throws Exception{PhoneHarbor.phoneCache=new JSONObject().put("manifest","old");PhoneHarbor.phoneAt=System.currentTimeMillis();new PhoneDownloadSettings(context).save(new JSONObject().put("manifest","https://example.com/new/manifest.json"));assertNull(PhoneHarbor.phoneCache);assertEquals(0,PhoneHarbor.phoneAt);assertEquals("https://example.com/new/manifest.json",new PhoneDownloadSettings(context).load().optString("manifest"));}
    @Test public void collectionsRemainAvailableWithoutPc()throws Exception{JSONObject item=data.rememberPhoneItem(episode(1,"2020-01-01T00:00:00Z"));JSONObject op=data.operation(item.optString("id"),"collection");op.put("name","Weekend").put("value",true);data.queue(op);DataStore reopened=new DataStore(context);try{assertEquals("Weekend",reopened.settings().getJSONArray("collections").getString(0));assertEquals("Weekend",reopened.item(item.optString("id")).getJSONArray("collections").getString(0));}finally{reopened.network.shutdownNow();reopened.images.shutdownNow();}}
}
