package app.noir.mobile;
import android.app.Instrumentation;
import android.content.*;
import android.graphics.Bitmap;
import android.view.View;
import androidx.test.platform.app.InstrumentationRegistry;
import androidx.test.runner.lifecycle.ActivityLifecycleMonitorRegistry;
import androidx.test.runner.lifecycle.Stage;
import org.json.*;
import org.junit.Test;
import org.videolan.libvlc.MediaPlayer;
import java.io.*;
import java.nio.charset.*;
import static org.junit.Assert.*;

public final class SubtitleTest {
    Instrumentation test(){return InstrumentationRegistry.getInstrumentation();}
    @Test public void streamedSubtitlePixelsAndTiming()throws Exception{
        DataStore data=NoirApp.data;if(!data.paired())data.pair(TestRunner.arguments.getString("pairUri"));else data.sync();JSONObject item=DataStore.objects(data.items()).stream().filter(i->i.optString("title").equals("Subtitle surface fixture")).findFirst().get();String id=item.optString("id");data.subtitlePreference(id,"English",0);
        test().runOnMainSync(()->test().getTargetContext().startActivity(new Intent(test().getTargetContext(),PlayerActivity.class).putExtra("id",id).putExtra("restart",true).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)));
        PlayerActivity[] resumed=new PlayerActivity[1];for(int n=0;n<200&&resumed[0]==null;n++){test().runOnMainSync(()->{for(android.app.Activity active:ActivityLifecycleMonitorRegistry.getInstance().getActivitiesInStage(Stage.RESUMED))if(active instanceof PlayerActivity)resumed[0]=(PlayerActivity)active;});Thread.sleep(100);}assertNotNull("Player reaches the foreground",resumed[0]);PlayerActivity p=resumed[0];
        try{
            for(int n=0;n<600&&(p.player==null||!p.player.isPlaying()||p.player.getSpuTracksCount()<2);n++)Thread.sleep(100);
            assertTrue("Streaming video plays: "+p.message.getText()+" / "+p.player.getPlayerState(),p.player!=null&&p.player.isPlaying());assertNotNull("VLC owns a separate subtitle surface",p.video.findViewById(org.videolan.R.id.surface_subtitles));
            int english=-1,arabic=-1;for(MediaPlayer.TrackDescription track:p.player.getSpuTracks()){if(PlayerActivity.subtitleScore(track.name,"English")>=0)english=track.id;if(PlayerActivity.subtitleScore(track.name,"Arabic")>=0)arabic=track.id;}assertTrue(english>=0&&arabic>=0);final int en=english,ar=arabic;
            test().runOnMainSync(()->{p.subtitles.manual=true;p.player.setSpuTrack(en);p.seek(4);p.manualHidden=true;p.controls.setVisibility(View.GONE);});Thread.sleep(500);test().runOnMainSync(()->p.player.pause());Thread.sleep(800);Bitmap shown=test().getUiAutomation().takeScreenshot();save(shown,"subtitle-english.png");
            test().runOnMainSync(()->p.player.setSpuTrack(-1));Thread.sleep(600);Bitmap hidden=test().getUiAutomation().takeScreenshot();save(hidden,"subtitle-off.png");int changed=0;for(int y=shown.getHeight()/2;y<shown.getHeight();y++)for(int x=0;x<shown.getWidth();x++)if(shown.getPixel(x,y)!=hidden.getPixel(x,y))changed++;assertTrue("English subtitle changes actual screen pixels: "+changed,changed>100);shown.recycle();hidden.recycle();
            test().runOnMainSync(()->{p.player.setSpuTrack(ar);p.player.play();p.seek(4);});Thread.sleep(500);test().runOnMainSync(()->p.player.pause());Thread.sleep(800);Bitmap arabicFrame=test().getUiAutomation().takeScreenshot();save(arabicFrame,"subtitle-arabic.png");arabicFrame.recycle();assertEquals(ar,p.player.getSpuTrack());
            test().runOnMainSync(()->{data.subtitlePreference(id,"Arabic",1.5);p.player.setSpuDelay(1500000);});assertEquals(1500000,p.player.getSpuDelay());DataStore reloaded=new DataStore(test().getTargetContext());assertEquals(1.5,reloaded.subtitleDelay(id),.0001);assertEquals("Arabic",reloaded.subtitleLanguage(id));
            SubtitleClient client=new SubtitleClient(data,item);client.identity();assertTrue(client.videoHash.matches("[a-f0-9]{16}"));assertEquals(client.videoHash,data.pcRequest("/v1/subtitle-tools/"+id,null).getString("hash"));
            StringBuilder cues=new StringBuilder();for(int n=0;n<18;n++){int start=7+n*6,end=10+n*6;cues.append(n+1).append('\n').append(String.format(java.util.Locale.ROOT,"00:%02d:%02d,000 --> 00:%02d:%02d,000\nNOIR SUBTITLE CHECK\n\n",start/60,start%60,end/60,end%60));}
            JSONObject sub=new JSONObject().put("sourceKey","test-english").put("language","en").put("name","English test").put("extension",".srt");sub=client.save(sub,cues.toString());String original=sub.getString("id");JSONObject synced=client.sync(sub);assertTrue("PC check accepts a clear embedded reference match",synced.optBoolean("aligned"));assertFalse(original.equals(synced.getString("id")));assertTrue("Original subtitle survives alignment",data.subtitle(new JSONObject().put("id",original).put("extension",".srt")).exists());assertTrue("Synced phone subtitle survives reopening",new DataStore(test().getTargetContext()).phoneSubtitles(id).length()>0);
            final JSONObject selected=synced;test().runOnMainSync(()->{data.subtitlePreference(id,"English",0);p.subtitles.selectFile(selected);p.subtitles.show();});Thread.sleep(400);Bitmap menu=test().getUiAutomation().takeScreenshot();save(menu,"subtitle-menu.png");menu.recycle();
        }finally{test().runOnMainSync(p::finish);}
    }
    @Test public void languageAndTextGuards()throws Exception{
        assertEquals("ar",SubtitleClient.language("ara"));assertEquals("en",SubtitleClient.language("eng"));assertEquals("مرحبا",SubtitleClient.decode("مرحبا".getBytes(Charset.forName("windows-1256"))));assertFalse(SubtitleClient.validText("<html>Login required</html>",".srt"));assertFalse(SubtitleClient.validUrl("http://example.com/sub.srt"));assertFalse(SubtitleClient.validUrl("https://localhost/sub.srt"));assertTrue(SubtitleClient.validText("1\n00:00:01,000 --> 00:00:03,000\nHello", ".srt"));assertTrue(PlayerActivity.subtitleScore("English full dialogue")>PlayerActivity.subtitleScore("English signs and songs"));
        byte[] first=new byte[65536],last=new byte[65536];first[0]=1;last[0]=2;assertEquals("0000000000020003",SubtitleClient.hashBytes(131072,first,last));
    }
    @Test public void downloadsKeepArabicTextAndUseFileMatches()throws Exception{
        JSONObject item=new JSONObject().put("id","phone-subtitle-download-fixture").put("imdb","tt123").put("title","Fixture").put("season",1).put("episode",2);
        JSONArray choices=new JSONArray();for(String lang:new String[]{"eng","ara"})choices.put(new JSONObject().put("id",lang).put("lang",lang).put("url","https://subtitle.invalid/"+lang).put("subtitleFileName",lang+".srt").put("movieReleaseName","Fixture WEB").put("m",lang.equals("eng")?"h":"i").put("season",1).put("episode",2));
        SubtitleClient client=new SubtitleClient(NoirApp.data,item,(url,limit)->{if(url.contains("/subtitles/series/")){assertTrue(url.contains("videoHash="));return new JSONObject().put("subtitles",choices).toString().getBytes(StandardCharsets.UTF_8);}String text="1\n00:00:01,000 --> 00:00:03,000\n"+(url.endsWith("ara")?"مرحبا":"Hello");if(url.endsWith("ara")){ByteArrayOutputStream bytes=new ByteArrayOutputStream();try(java.util.zip.GZIPOutputStream gzip=new java.util.zip.GZIPOutputStream(bytes)){gzip.write(text.getBytes(Charset.forName("windows-1256")));}return bytes.toByteArray();}return text.getBytes(StandardCharsets.UTF_8);});client.identityRead=true;client.videoHash="0123456789abcdef";
        JSONObject english=client.download(client.search("en").get(0)),arabic=client.download(client.search("ar").get(0));assertTrue(english.optBoolean("hashMatch"));assertTrue(new String(SubtitleClient.read(NoirApp.data.subtitle(arabic)),StandardCharsets.UTF_8).contains("مرحبا"));assertEquals(2,new DataStore(test().getTargetContext()).phoneSubtitles(item.optString("id")).length());NoirApp.data.subtitleFilePreference(item.optString("id"),arabic.getString("id"));NoirApp.data.subtitlePreference(item.optString("id"),"Arabic",.5);DataStore reopened=new DataStore(test().getTargetContext());assertEquals(arabic.getString("id"),reopened.preferredSubtitle(item.optString("id")));assertEquals(.5,reopened.subtitleDelay(item.optString("id")),.001);
    }
    void save(Bitmap bitmap,String name)throws Exception{try(FileOutputStream out=new FileOutputStream(new File(test().getTargetContext().getExternalFilesDir(null),name))){bitmap.compress(Bitmap.CompressFormat.PNG,100,out);}}
}
