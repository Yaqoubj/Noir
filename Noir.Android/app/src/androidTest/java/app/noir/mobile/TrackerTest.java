package app.noir.mobile;

import android.app.Instrumentation;
import android.content.Intent;
import android.graphics.Bitmap;
import android.view.View;
import android.view.ViewGroup;
import android.widget.TextView;
import androidx.test.platform.app.InstrumentationRegistry;
import org.json.*;
import org.junit.Test;
import java.io.*;
import java.util.*;
import static org.junit.Assert.*;

/** The phone tracker: check-off with undo, dated logging, sync to the PC fixture and the Log screen. */
public final class TrackerTest {
    // Diagnostic: what is still waiting for the PC when an outbox assertion fails.
    static String outbox(DataStore d){try{java.lang.reflect.Method m=DataStore.class.getDeclaredMethod("pending");m.setAccessible(true);return String.valueOf(m.invoke(d));}catch(Exception e){return "?";}}
    final Instrumentation test=InstrumentationRegistry.getInstrumentation();
    View find(View view,String prefix){
        CharSequence d=view.getContentDescription();if(d!=null&&d.toString().startsWith(prefix))return view;
        if(view instanceof ViewGroup)for(int n=0;n<((ViewGroup)view).getChildCount();n++){View found=find(((ViewGroup)view).getChildAt(n),prefix);if(found!=null)return found;}
        return null;
    }
    boolean hasText(View view,String text){if(view instanceof TextView&&((TextView)view).getText().toString().contains(text))return true;if(view instanceof ViewGroup)for(int n=0;n<((ViewGroup)view).getChildCount();n++)if(hasText(((ViewGroup)view).getChildAt(n),text))return true;return false;}
    void capture(String name)throws Exception{
        test.waitForIdleSync();Thread.sleep(500);Bitmap bitmap=test.getUiAutomation().takeScreenshot();assertNotNull(bitmap);
        try(FileOutputStream out=new FileOutputStream(new File(test.getTargetContext().getFilesDir(),name))){bitmap.compress(Bitmap.CompressFormat.PNG,100,out);}bitmap.recycle();
    }
    @Test public void testDatedMarkAndUnviewApplyLocally(){
        JSONObject library=new JSONObject(),item=new JSONObject();JSONArray items=new JSONArray();DataStore.put(item,"id","x");items.put(item);DataStore.put(library,"items",items);
        JSONObject mark=new JSONObject();DataStore.put(mark,"kind","mark");DataStore.put(mark,"itemId","x");DataStore.put(mark,"at",2_000_000_000_000L);DataStore.put(mark,"viewingId","v1");DataStore.put(mark,"completed",true);DataStore.put(mark,"watchedAt",1_500_000_000_000L);DataStore.put(mark,"dateUnknown",true);
        DataStore.apply(library,mark);assertNull("Shared items are copied, never changed in place",item.optJSONArray("viewings"));JSONObject v=items.optJSONObject(0).optJSONArray("viewings").optJSONObject(0);
        assertEquals("A logged date is kept",1_500_000_000_000L,DataStore.date(v.optString("watchedAt")));assertTrue("Don't remember is kept",v.optBoolean("dateUnknown"));
        JSONObject unview=new JSONObject();DataStore.put(unview,"kind","unview");DataStore.put(unview,"itemId","x");DataStore.put(unview,"at",2_000_000_000_100L);DataStore.put(unview,"viewingId","");
        DataStore.apply(library,unview);assertEquals("Unchecking forgets every viewing",0,items.optJSONObject(0).optJSONArray("viewings").length());assertFalse(DataStore.watched(items.optJSONObject(0)));
    }
    @Test public void testTrackerCheckOffUndoAndSync()throws Exception{
        DataStore data=NoirApp.data;if(!data.paired())data.pair(TestRunner.arguments.getString("pairUri"));data.sync();
        MainActivity home=(MainActivity)test.startActivitySync(new Intent(test.getTargetContext(),MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
        List<List<JSONObject>> groups=home.groups();List<JSONObject> show=null;for(List<JSONObject> g:groups)if(!g.get(0).optBoolean("movie")&&g.stream().filter(i->i.optInt("season")>0).count()>=2)show=g;
        assertNotNull("The fixture has a show with episodes",show);
        JSONObject first=show.stream().filter(i->i.optInt("season")>0&&!DataStore.watched(i)).findFirst().orElse(null);
        if(first==null){for(JSONObject i:show)data.unview(i.optString("id"),"");data.sync();first=show.stream().filter(i->i.optInt("season")>0).findFirst().get();}
        final String id=first.optString("id");String viewing=data.mark(id,true,0,false);data.sync();
        assertEquals("The PC accepts the check-off "+outbox(data),0,data.pendingCount());assertTrue(DataStore.watched(data.item(id)));
        test.runOnMainSync(()->{home.page="History";home.detail="";home.premium.historyView="Up next";home.render();});test.waitForIdleSync();
        assertTrue("Up next lists the show in progress",hasText(home.body,"tap the circle the moment you finish"));capture("tracker-up-next.png");
        assertNotNull("Up next offers a one-tap check-off",find(home.body,"Mark "));int before=data.pendingCount();
        // An episode with nothing unwatched before it checks off at once; others ask about the earlier ones first.
        final List<JSONObject> chosen=show;test.runOnMainSync(()->{List<JSONObject> eps=home.smart.released(chosen.get(0));for(JSONObject e:eps){JSONObject fresh=data.item(e.optString("id"));android.util.Log.i("NoirTrackerTest","candidate "+e.optString("id")+" "+(fresh!=null&&DataStore.watched(fresh)));if(fresh!=null&&!DataStore.watched(fresh)){android.util.Log.i("NoirTrackerTest","earlier="+home.smart.earlier(fresh).size());home.tracker.checkOff(fresh,"test");break;}}android.util.Log.i("NoirTrackerTest","released="+eps.size()+" undo="+home.undoUntil);});long wait=System.currentTimeMillis()+8000;while(home.undoUntil<=System.currentTimeMillis()&&System.currentTimeMillis()<wait)Thread.sleep(100);boolean undoShown=home.undoUntil>0;
        assertTrue("The check-off is queued",data.pendingCount()>before||data.pendingCount()==0);
        assertTrue("An undo bar appears",undoShown);capture("tracker-checked.png");
        test.runOnMainSync(()->{Runnable undo=home.undoAction;home.undoUntil=0;if(undo!=null)undo.run();home.renderKeep();});data.sync();assertEquals("Undo reaches the PC "+outbox(data),0,data.pendingCount());
        data.unview(id,viewing);data.sync();assertEquals("pending "+outbox(data),0,data.pendingCount());assertFalse("Unchecking reaches the PC and back",DataStore.watched(data.item(id)));
        for(String tab:new String[]{"Upcoming","Shows","Movies","Diary","Stats"}){test.runOnMainSync(()->{home.premium.historyView=tab;home.render();});capture("tracker-"+tab.toLowerCase(Locale.ROOT)+".png");}
    }
    @Test public void testLogAnythingWithoutPc()throws Exception{
        DataStore data=NoirApp.data;
        LogActivity log=(LogActivity)test.startActivitySync(new Intent(test.getTargetContext(),LogActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
        long deadline=System.currentTimeMillis()+30000;while(log.titles.length()==0&&System.currentTimeMillis()<deadline)Thread.sleep(300);
        assertTrue("Popular titles load from Cinemeta",log.titles.length()>0);capture("tracker-log-search.png");
        JSONObject series=null;for(int n=0;n<log.titles.length();n++)if(log.titles.optJSONObject(n).optString("noirType").equals("series")){series=log.titles.optJSONObject(n);break;}
        assertNotNull(series);final JSONObject pick=series;android.util.Log.i("NoirTrackerTest","Logging "+series.optString("name")+" "+series.optString("id"));test.runOnMainSync(()->log.open(pick));
        deadline=System.currentTimeMillis()+30000;while(log.saved.isEmpty()&&System.currentTimeMillis()<deadline)Thread.sleep(300);test.waitForIdleSync();
        assertFalse("The show's episodes load",log.saved.isEmpty());capture("tracker-log-title.png");
        List<JSONObject> aired=new ArrayList<>();for(JSONObject e:log.saved)if(e.optInt("season")==1&&LogActivity.released(e)&&aired.size()<2)aired.add(e);
        test.runOnMainSync(()->{log.when="Don't remember";log.log(aired,"test");});test.waitForIdleSync();
        for(JSONObject e:aired)assertTrue("Logged without a file",log.watched(e));
        JSONObject logged=null;for(JSONObject i:DataStore.objects(data.items()))if(i.optString("imdb").equals(pick.optString("id"))&&i.optInt("season")==1&&i.optInt("episode")==aired.get(0).optInt("episode"))logged=i;
        assertNotNull(logged);final JSONObject record=logged;assertTrue("Don't remember is recorded: "+record,DataStore.objects(record.optJSONArray("viewings")).stream().anyMatch(v->v.optBoolean("dateUnknown")&&v.optString("source").contains("Android")));capture("tracker-log-checked.png");
        if(data.paired()){data.sync();assertEquals("The PC imports titles logged on the phone "+outbox(data),0,data.pendingCount());}
    }
    @Test public void testSmartHistoryOnPhone()throws Exception{
        JSONObject undated=new JSONObject();JSONArray v=new JSONArray();JSONObject one=new JSONObject();DataStore.put(one,"watchedAt","2026-10-17T10:00:00Z");DataStore.put(one,"dateUnknown",true);v.put(one);DataStore.put(undated,"viewings",v);
        JSONObject done=new JSONObject();DataStore.put(done,"completed",true);DataStore.put(done,"updatedAt","2026-10-17T10:00:00Z");DataStore.put(undated,"progress",done);
        assertEquals("Undated logs and completion bookmarks are never recent",0,DataStore.recent(undated));
        JSONObject old=new JSONObject();JSONArray ov=new JSONArray();JSONObject o1=new JSONObject();DataStore.put(o1,"watchedAt","2019-03-01T20:00:00Z");DataStore.put(o1,"loggedAt","2026-10-18T09:00:00Z");ov.put(o1);DataStore.put(old,"viewings",ov);
        assertEquals("History logged years later is never recent",0,DataStore.recent(old));
        JSONObject weekend=new JSONObject();JSONArray wv=new JSONArray();JSONObject w1=new JSONObject();DataStore.put(w1,"watchedAt","2026-10-15T20:00:00Z");DataStore.put(w1,"loggedAt","2026-10-18T09:00:00Z");wv.put(w1);DataStore.put(weekend,"viewings",wv);
        assertTrue("Something logged a few days later still counts",DataStore.recent(weekend)>0);
        DataStore data=NoirApp.data;if(!data.paired())data.pair(TestRunner.arguments.getString("pairUri"));data.sync();
        MainActivity home=(MainActivity)test.startActivitySync(new Intent(test.getTargetContext(),MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
        final List<JSONObject>[] show=new List[1];test.runOnMainSync(()->{for(List<JSONObject> g:home.groups())if(!g.get(0).optBoolean("movie")&&home.smart.released(g.get(0)).size()>=4)show[0]=g;});
        assertNotNull("The fixture has a show with 4+ released episodes",show[0]);
        final List<JSONObject>[] released=new List[1];test.runOnMainSync(()->released[0]=home.smart.released(show[0].get(0)));
        for(JSONObject i:released[0])data.unview(i.optString("id"),"");data.sync();
        JSONObject target=data.item(released[0].get(3).optString("id"));
        final List<JSONObject>[] earlier=new List[1];test.runOnMainSync(()->earlier[0]=home.smart.earlier(target));
        assertEquals("Checking off the 4th episode finds the 3 before it",3,earlier[0].size());
        final List<String[]>[] logged=new List[1];test.runOnMainSync(()->logged[0]=home.smart.log(earlier[0],"Don't remember",0));
        for(JSONObject i:earlier[0]){JSONObject fresh=data.item(i.optString("id"));assertTrue("Earlier episodes are checked off",DataStore.watched(fresh));assertEquals("Undated catch-ups are never recent",0,DataStore.recent(fresh));}
        test.runOnMainSync(()->home.smart.log(java.util.List.of(target),"Just now",0));
        final List<JSONObject>[] after=new List[1];test.runOnMainSync(()->after[0]=home.smart.later(data.item(earlier[0].get(0).optString("id"))));
        assertEquals("Un-checking the first knows the 3 later ones are checked",3,after[0].size());
        test.runOnMainSync(()->home.smart.rate(target,target.optString("group"),"S"+target.optInt("season"),4));data.sync();
        assertEquals("Ratings and check-offs reach the PC "+outbox(data),0,data.pendingCount());
        assertEquals("The PC sends the season rating back",4,data.settings().optJSONObject("showRatings").optInt(target.optString("group")+":S"+target.optInt("season")));
        for(JSONObject i:released[0])data.unview(i.optString("id"),"");data.sync();
    }
    @Test public void testLibraryAddAndRemove()throws Exception{
        DataStore data=NoirApp.data;if(!data.paired())data.pair(TestRunner.arguments.getString("pairUri"));data.sync();
        MainActivity home=(MainActivity)test.startActivitySync(new Intent(test.getTargetContext(),MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
        final List<JSONObject>[] g=new List[1];test.runOnMainSync(()->{for(List<JSONObject> x:home.groups())if(home.inLibrary(x)&&NoirApp.downloads.job(x.get(0).optString("id"))==null&&data.localFile(x.get(0).optString("id")).isEmpty())g[0]=x;});
        assertNotNull("The fixture has a library title",g[0]);String id=g[0].get(0).optString("id"),group=g[0].get(0).optString("group");
        data.library(id,false);data.sync();assertEquals("pending "+outbox(data),0,data.pendingCount());
        final boolean[] gone=new boolean[1];test.runOnMainSync(()->gone[0]=!home.inLibrary(home.group(group)));assertTrue("Removing hides it from the phone Library after syncing with the PC",gone[0]);
        test.runOnMainSync(()->{home.page="Library";home.detail="";home.render();});capture("library-after-remove.png");
        assertFalse("It's gone from the Library grid",hasText(home.body,g[0].get(0).optString("title"))&&home.premium.collection.equals("All"));
        data.library(id,true);data.sync();final boolean[] back=new boolean[1];test.runOnMainSync(()->back[0]=home.inLibrary(home.group(group)));assertTrue("“+ Library” brings it back",back[0]);
    }
}
