package app.noir.mobile;
import android.app.*;
import android.content.*;
import android.view.*;
import android.widget.*;
import androidx.test.platform.app.InstrumentationRegistry;
import org.junit.Test;
import org.json.*;
import java.io.*;
import java.nio.charset.StandardCharsets;
import static org.junit.Assert.*;

public final class DiscoverTest {
    @Test public void liveSearchAndArtwork()throws Exception{
        DiscoveryClient client=new DiscoveryClient(instrumentation().getTargetContext());JSONArray movies=client.search("The Matrix","Movies");JSONObject matrix=DataStore.objects(movies).stream().filter(t->t.optString("id").equals("tt0133093")).findFirst().orElse(null);assertNotNull(matrix);JSONObject details=client.details(matrix);assertTrue(details.optString("description").length()>20);android.graphics.Bitmap poster=client.art(details.optString("poster"));assertNotNull(poster);assertTrue(poster.getWidth()<=768);
        DiscoverActivity activity=(DiscoverActivity)instrumentation().startActivitySync(new Intent(instrumentation().getTargetContext(),DiscoverActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));for(int i=0;i<120&&activity.loading;i++)Thread.sleep(100);instrumentation().runOnMainSync(()->activity.open(matrix));for(int i=0;i<120&&activity.loading;i++)Thread.sleep(100);Thread.sleep(1500);assertNotNull(find(activity.body,"Find movie downloads on PC"));try(FileOutputStream image=new FileOutputStream(new File(activity.getExternalFilesDir(null),"discover-movie.png"))){instrumentation().getUiAutomation().takeScreenshot().compress(android.graphics.Bitmap.CompressFormat.PNG,100,image);}instrumentation().runOnMainSync(activity::finish);
    }
    Instrumentation instrumentation(){return InstrumentationRegistry.getInstrumentation();}
    Button find(View root,String label){if(root instanceof Button&&((Button)root).getText().toString().equals(label))return (Button)root;if(root instanceof ViewGroup){ViewGroup group=(ViewGroup)root;for(int i=0;i<group.getChildCount();i++){Button found=find(group.getChildAt(i),label);if(found!=null)return found;}}return null;}
    void cached(DiscoveryClient client,String path,JSONObject json)throws Exception{DiscoveryClient.write(new File(client.directory,DiscoveryClient.hash(path)+".json"),json.toString().getBytes(StandardCharsets.UTF_8));}
    @Test public void discoveryCachesSchedulesAndSendsPcRequests()throws Exception{
        DataStore data=NoirApp.data;if(!data.paired())data.pair(TestRunner.arguments.getString("pairUri"));else data.sync();int pending=data.pendingCount();
        DiscoveryClient client=new DiscoveryClient(instrumentation().getTargetContext());JSONObject movie=new JSONObject("{\"id\":\"tt0111161\",\"name\":\"The Shawshank Redemption\",\"releaseInfo\":\"1994\",\"noirType\":\"movie\",\"description\":\"A cached discovery fixture\"}");JSONObject show=new JSONObject("{\"id\":\"tt11147852\",\"name\":\"Dorohedoro\",\"releaseInfo\":\"2020\",\"noirType\":\"series\",\"videos\":[{\"season\":1,\"episode\":1,\"name\":\"First\",\"released\":\"2020-01-01T00:00:00Z\"},{\"season\":2,\"episode\":1,\"name\":\"Future\",\"released\":\"2099-01-01T00:00:00Z\"}]}");
        cached(client,"catalog/movie/top/skip=0.json",new JSONObject().put("metas",new JSONArray().put(movie)));cached(client,"catalog/series/top/skip=0.json",new JSONObject().put("metas",new JSONArray().put(show)));cached(client,"meta/movie/tt0111161.json",new JSONObject().put("meta",movie));cached(client,"meta/series/tt11147852.json",new JSONObject().put("meta",show));
        assertEquals(2,client.search("","All").length());assertTrue(client.cached);assertEquals(2,DiscoveryClient.episodes(client.details(show)).size());assertFalse(DiscoveryClient.released(DiscoveryClient.episodes(show).get(1)));
        DiscoverActivity activity=(DiscoverActivity)instrumentation().startActivitySync(new Intent(instrumentation().getTargetContext(),DiscoverActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));for(int i=0;i<120&&activity.loading;i++)Thread.sleep(100);assertEquals(2,activity.titles.length());assertEquals(pending,data.pendingCount());
        instrumentation().runOnMainSync(()->activity.open(show));for(int i=0;i<120&&activity.loading;i++)Thread.sleep(100);instrumentation().runOnMainSync(()->{activity.season=2;activity.showDetails();assertFalse(find(activity.body,"Download episode to PC").isEnabled());assertFalse(find(activity.body,"Find season downloads on PC").isEnabled());});
        instrumentation().runOnMainSync(()->activity.open(movie));for(int i=0;i<120&&activity.loading;i++)Thread.sleep(100);assertNotNull(find(activity.body,"Find movie downloads on PC"));instrumentation().runOnMainSync(()->find(activity.body,"+ My List").performClick());for(int i=0;i<300&&activity.command;i++)Thread.sleep(100);assertTrue(activity.feedback.getText().toString(),activity.feedback.getText().toString().contains("Saved to My List"));assertTrue(DataStore.objects(data.items()).stream().anyMatch(i->i.optString("imdb").equals("tt0111161")&&i.optBoolean("listed")));assertEquals(pending,data.pendingCount());
        JSONObject request=new JSONObject().put("id","tt0111161").put("type","movie").put("watchlist",false);JSONObject registered=data.pcRequest("/v1/discover/save",request);String id=registered.getString("id");JSONObject release=data.pcRequest("/v1/pc-downloads/search/"+id+"?season=false",null);assertTrue(release.getJSONArray("choices").length()>0);assertFalse(release.toString().contains("fixture-key"));
        instrumentation().runOnMainSync(activity::finish);
        DownloadHubActivity hub=(DownloadHubActivity)instrumentation().startActivitySync(new Intent(instrumentation().getTargetContext(),DownloadHubActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));instrumentation().runOnMainSync(()->{assertNotNull(find(hub.getWindow().getDecorView(),"Downloads on PC"));assertNotNull(find(hub.getWindow().getDecorView(),"Offline on phone"));hub.finish();});
    }
}
