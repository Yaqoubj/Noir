package app.noir.mobile;
import android.app.*;
import android.content.*;
import android.widget.*;
import androidx.test.platform.app.InstrumentationRegistry;
import org.junit.Test;
import static org.junit.Assert.*;
import org.json.*;
import java.io.*;

public final class PcDownloadTest {
    java.util.List<Button> chooseButtons(android.view.View root){java.util.List<Button> result=new java.util.ArrayList<>();if(root instanceof Button&&((Button)root).getText().toString().equals("Choose files"))result.add((Button)root);if(root instanceof android.view.ViewGroup){android.view.ViewGroup group=(android.view.ViewGroup)root;for(int i=0;i<group.getChildCount();i++)result.addAll(chooseButtons(group.getChildAt(i)));}return result;}
    Button qualityButton(android.view.View root){if(root instanceof Button&&((Button)root).getText().toString().startsWith("1080p · "))return (Button)root;if(root instanceof android.view.ViewGroup){android.view.ViewGroup g=(android.view.ViewGroup)root;for(int i=0;i<g.getChildCount();i++){Button found=qualityButton(g.getChildAt(i));if(found!=null)return found;}}return null;}
    Button findButton(android.view.View root,String label){if(root instanceof Button&&((Button)root).getText().toString().equals(label))return (Button)root;if(root instanceof android.view.ViewGroup){android.view.ViewGroup group=(android.view.ViewGroup)root;for(int i=0;i<group.getChildCount();i++){Button result=findButton(group.getChildAt(i),label);if(result!=null)return result;}}return null;}
    Instrumentation instrumentation(){return InstrumentationRegistry.getInstrumentation();}
    @Test public void testPcProgressSubtitlesAndRemoteDownloadUi()throws Exception {
        DataStore data=NoirApp.data;if(!data.paired())data.pair(TestRunner.arguments.getString("pairUri"));else data.sync();
        JSONObject pc=data.item("pc-download-fixture");assertNotNull(pc);assertEquals(421.625,pc.getJSONObject("progress").getDouble("position"),.0001);
        DataStore restored=new DataStore(instrumentation().getTargetContext());assertEquals(421.625,restored.item(pc.optString("id")).getJSONObject("progress").getDouble("position"),.0001);
        PlayerActivity[] session=new PlayerActivity[1];instrumentation().runOnMainSync(()->session[0]=new PlayerActivity());PlayerActivity stale=session[0];stale.data=data;stale.item=pc;stale.viewing=java.util.UUID.randomUUID().toString();stale.lastPosition=7;stale.duration=1800;stale.savedRevision=DataStore.date(pc.getJSONObject("progress").optString("updatedAt"))-1;int pending=data.pendingCount();stale.save();assertEquals(pending,data.pendingCount());assertEquals(421.625,data.item(pc.optString("id")).getJSONObject("progress").getDouble("position"),.0001);
        assertTrue(PlayerActivity.subtitleScore("English Full")>PlayerActivity.subtitleScore("English Signs & Songs"));assertTrue(PlayerActivity.subtitleScore("English")>PlayerActivity.subtitleScore("English Forced"));assertEquals(-1,PlayerActivity.subtitleScore("German"));assertTrue(PlayerActivity.subtitleScore("Arabic Full","Arabic")>PlayerActivity.subtitleScore("Arabic Forced","Arabic"));assertEquals(-1,PlayerActivity.subtitleScore("English","Arabic"));
        JSONObject subtitleItem=DataStore.objects(data.items()).stream().filter(i->i.optString("title").equals("Download fixture")).findFirst().get();data.cacheSubtitles(subtitleItem);JSONObject sub=subtitleItem.getJSONArray("subtitleFiles").getJSONObject(0);File cached=data.subtitle(sub);assertTrue(cached.exists());try(InputStream input=new FileInputStream(cached)){assertTrue(new String(DataStore.read(input,4096),java.nio.charset.StandardCharsets.UTF_8).contains("Noir subtitle fixture"));}
        JSONObject q=data.pcRequest("/v1/pc-downloads",null);assertTrue(q.getBoolean("configured"));for(JSONObject old:DataStore.objects(q.getJSONArray("jobs"))){JSONObject cancel=new JSONObject();DataStore.put(cancel,"id",old.optString("id"));DataStore.put(cancel,"action","cancel");data.pcRequest("/v1/pc-downloads/control",cancel);}
        PcDownloadsActivity activity=(PcDownloadsActivity)instrumentation().startActivitySync(new Intent(instrumentation().getTargetContext(),PcDownloadsActivity.class).putExtra("id",pc.optString("id")).putExtra("season",true).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
        for(int n=0;n<150&&(activity.busy||activity.body.getChildCount()<2);n++)Thread.sleep(100);
        assertFalse(activity.feedback.getText().toString(),activity.busy);Button[] foundRelease=new Button[1];instrumentation().runOnMainSync(()->{assertEquals(3,chooseButtons(activity.body).size());Button quality=qualityButton(activity.body);assertNotNull(quality);quality.performClick();assertEquals(23,chooseButtons(activity.body).size());quality.performClick();foundRelease[0]=findButton(activity.body,"Choose files");assertNotNull(findButton(activity.body,"Filter releases"));});assertNotNull(foundRelease[0]);try(java.io.FileOutputStream image=new java.io.FileOutputStream(new File(activity.getExternalFilesDir(null),"grouped-releases.png"))){instrumentation().getUiAutomation().takeScreenshot().compress(android.graphics.Bitmap.CompressFormat.PNG,100,image);}
        Button release=foundRelease[0];instrumentation().runOnMainSync(release::performClick);
        for(int n=0;n<150&&(activity.busy||activity.files==null);n++)Thread.sleep(100);assertNotNull(activity.feedback.getText().toString(),activity.files);
        Button[] submit=new Button[1];instrumentation().runOnMainSync(()->{for(int i=0;i<activity.body.getChildCount();i++)if(activity.body.getChildAt(i) instanceof Button){Button b=(Button)activity.body.getChildAt(i);if(b.getText().toString().equals("Download selected on PC"))submit[0]=b;}});assertNotNull(submit[0]);instrumentation().runOnMainSync(submit[0]::performClick);
        for(int n=0;n<150&&(activity.busy||!activity.itemId.isEmpty());n++)Thread.sleep(100);
        JSONObject queue=data.pcRequest("/v1/pc-downloads",null);assertEquals(1,DataStore.objects(queue.getJSONArray("jobs")).stream().filter(j->j.optString("status").equals("Queued")).count());assertFalse(queue.toString().contains("fixture-key"));assertFalse(queue.toString().contains("example.invalid"));assertFalse(queue.toString().contains("C:\\"));
        JSONObject job=DataStore.objects(queue.getJSONArray("jobs")).stream().filter(j->j.optString("status").equals("Queued")).findFirst().get();JSONObject command=new JSONObject();DataStore.put(command,"id",job.optString("id"));DataStore.put(command,"action","pause");data.pcRequest("/v1/pc-downloads/control",command);assertTrue(data.pcRequest("/v1/pc-downloads",null).toString().contains("Paused"));
        instrumentation().runOnMainSync(activity::finish);
    }
}
