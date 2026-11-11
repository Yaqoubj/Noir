package app.noir.mobile;

import androidx.test.platform.app.InstrumentationRegistry;
import org.json.*;
import org.junit.Test;
import java.nio.charset.StandardCharsets;
import static org.junit.Assert.*;

public final class StreamingSubtitleTest {
    @Test public void discoverOffersStreamingAndDisablesFutureEpisodes()throws Exception{
        android.app.Instrumentation test=InstrumentationRegistry.getInstrumentation();
        DiscoverActivity a=(DiscoverActivity)test.startActivitySync(new android.content.Intent(test.getTargetContext(),DiscoverActivity.class).addFlags(android.content.Intent.FLAG_ACTIVITY_NEW_TASK));
        try{
            JSONObject movie=new JSONObject().put("id","tt123").put("noirType","movie").put("name","Stream movie");
            JSONObject show=new JSONObject().put("id","tt124").put("noirType","series").put("name","Stream show").put("videos",new JSONArray().put(new JSONObject().put("season",1).put("episode",1).put("released","2099-01-01T00:00:00Z")));
            test.runOnMainSync(()->{if(a.current!=null)a.current.cancel(true);a.title=movie;a.showDetails();assertNotNull(new DiscoverTest().find(a.body,"Stream"));a.title=show;a.season=1;a.showDetails();android.widget.Button stream=new DiscoverTest().find(a.body,"Stream episode");assertNotNull(stream);assertFalse(stream.isEnabled());});
        }finally{test.runOnMainSync(a::finish);}
    }
    @Test public void playerLoadsStreamSubtitlesAndRemembersOff()throws Exception{
        android.app.Instrumentation test=InstrumentationRegistry.getInstrumentation();DataStore data=NoirApp.data;
        JSONObject item=new JSONObject().put("id","player-stream-subs-"+System.nanoTime()).put("imdb","tt123").put("title","Stream subtitle player").put("movie",true);
        item=data.rememberPhoneItem(item);String id=item.optString("id");assertNotNull(data.item(id));java.io.File video=new java.io.File(test.getTargetContext().getCacheDir(),"stream-subtitle-fixture.mkv");
        try(java.io.InputStream in=test.getContext().getAssets().open("phone-offline-fixture.mkv");java.io.OutputStream out=new java.io.FileOutputStream(video)){in.transferTo(out);}
        SubtitleClient client=new SubtitleClient(data,item);client.save(new JSONObject().put("sourceKey","fixture").put("name","English stream").put("language","en").put("extension",".srt").put("streamRelease","Fixture.WEB.mkv"),"1\n00:00:00,000 --> 00:01:00,000\nSTREAM SUBTITLE CHECK");
        data.subtitlePreference(id,"English",.5);PlayerActivity[] active={null};
        android.content.Intent intent=new android.content.Intent(test.getTargetContext(),PlayerActivity.class).putExtra("id",id).putExtra("restart",true).putExtra("streamUrl",android.net.Uri.fromFile(video).toString()).putExtra("streamRelease","Fixture.WEB.mkv").addFlags(android.content.Intent.FLAG_ACTIVITY_NEW_TASK);
        try{
            test.runOnMainSync(()->test.getTargetContext().startActivity(intent));
            for(int n=0;n<150&&active[0]==null;n++){test.runOnMainSync(()->{for(android.app.Activity a:androidx.test.runner.lifecycle.ActivityLifecycleMonitorRegistry.getInstance().getActivitiesInStage(androidx.test.runner.lifecycle.Stage.RESUMED))if(a instanceof PlayerActivity)active[0]=(PlayerActivity)a;});Thread.sleep(100);}
            assertNotNull(active[0]);PlayerActivity p=active[0];
            for(int n=0;n<200&&(p.player==null||!p.player.isPlaying()||p.player.getSpuTrack()<0);n++)Thread.sleep(100);
            assertTrue("Cached external track loads in the stream player",p.player.getSpuTrack()>=0);assertEquals(500000,p.player.getSpuDelay());assertEquals("Fixture.WEB.mkv",p.subtitles.client.streamRelease);assertTrue(data.localFile(id).isEmpty());
            test.runOnMainSync(()->{p.subtitles.manual=true;p.player.setSpuTrack(-1);data.subtitlePreference(id,"Off",.5);p.finish();});Thread.sleep(600);active[0]=null;
            test.runOnMainSync(()->test.getTargetContext().startActivity(intent));
            for(int n=0;n<150&&active[0]==null;n++){test.runOnMainSync(()->{for(android.app.Activity a:androidx.test.runner.lifecycle.ActivityLifecycleMonitorRegistry.getInstance().getActivitiesInStage(androidx.test.runner.lifecycle.Stage.RESUMED))if(a instanceof PlayerActivity)active[0]=(PlayerActivity)a;});Thread.sleep(100);}
            assertNotNull(active[0]);PlayerActivity reopened=active[0];for(int n=0;n<200&&(reopened.player==null||!reopened.player.isPlaying());n++)Thread.sleep(100);
            assertTrue(reopened.player.isPlaying());Thread.sleep(3000);assertEquals(-1,reopened.player.getSpuTrack());assertEquals(500000,reopened.player.getSpuDelay());
        }finally{if(active[0]!=null)test.runOnMainSync(active[0]::finish);for(int n=0;n<25;n++){Thread.sleep(200);data.dropPending(id);}}// the player saves progress as it closes, sometimes after finish returns; keep it out of later tests' outbox
    }
    @Test public void streamingWithoutPcKeepsReleaseAndLanguage()throws Exception{
        DataStore data=NoirApp.data;String id="stream-subs-"+System.nanoTime();
        JSONObject item=new JSONObject().put("id",id).put("imdb","tt123").put("title","Stream fixture").put("season",1).put("episode",2);
        SubtitleClient client=new SubtitleClient(data,item,(url,limit)->{
            if(url.contains("/subtitles/series/")){
                assertTrue(url.contains("Fixture.WEB.mkv"));assertFalse(url.contains("videoHash"));
                return new JSONObject().put("subtitles",new JSONArray().put(new JSONObject().put("lang","ara").put("url","https://subs.example/ar").put("subtitleFileName","Arabic.srt").put("movieReleaseName","Fixture WEB").put("season",1).put("episode",2))).toString().getBytes(StandardCharsets.UTF_8);
            }
            return "1\n00:00:01,000 --> 00:00:04,000\nمرحبا".getBytes(StandardCharsets.UTF_8);
        });
        client.streaming("Fixture.WEB.mkv");client.identity();assertEquals("",client.videoHash);
        JSONObject sub=client.download(client.search("ar").get(0));assertEquals(1,client.files().size());
        assertTrue(new String(SubtitleClient.read(data.subtitle(sub)),StandardCharsets.UTF_8).contains("مرحبا"));
        try{client.sync(sub);fail("A stream cannot be synced against the PC's different video");}catch(java.io.IOException expected){assertTrue(expected.getMessage().contains("Adjust timing"));}
        SubtitleClient reopened=new SubtitleClient(new DataStore(InstrumentationRegistry.getInstrumentation().getTargetContext()),item);
        reopened.streaming("Fixture.WEB.mkv");assertEquals(1,reopened.files().size());
        reopened.streaming("Fixture.BluRay.mkv");assertTrue(reopened.files().isEmpty());
        assertTrue(new SubtitleClient(data,item).files().isEmpty());
    }
    @Test public void savedTracksStaySeparateForEachStreamRelease()throws Exception{
        JSONObject item=new JSONObject().put("id","release-subs-"+System.nanoTime());
        SubtitleClient client=new SubtitleClient(NoirApp.data,item);
        String text="1\n00:00:01,000 --> 00:00:04,000\nHello";
        JSONObject a=client.save(new JSONObject().put("sourceKey","same").put("streamRelease","WEB").put("extension",".srt"),text);
        JSONObject b=client.save(new JSONObject().put("sourceKey","same").put("streamRelease","BluRay").put("extension",".srt"),text);
        assertNotEquals(a.getString("id"),b.getString("id"));client.streaming("WEB");assertEquals(1,client.files().size());client.streaming("BluRay");assertEquals(1,client.files().size());
    }
}
