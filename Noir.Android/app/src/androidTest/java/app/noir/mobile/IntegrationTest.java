package app.noir.mobile;
import android.app.Instrumentation;
import androidx.test.platform.app.InstrumentationRegistry;
import org.junit.Test;
import static org.junit.Assert.*;
import android.content.*;
import android.os.*;
import org.json.*;
import org.videolan.libvlc.*;
import java.io.*;
import java.util.*;

public final class IntegrationTest {
    Instrumentation getInstrumentation(){return InstrumentationRegistry.getInstrumentation();}
    @Test
    public void testEndToEnd()throws Exception{
        DataStore data=NoirApp.data;if(!data.paired())data.pair(TestRunner.arguments.getString("pairUri"));else data.sync();
        assertTrue("Library received",data.items().length()>20);assertTrue("Paired PC online",data.online);
        JSONObject deleted=null,video=null;for(JSONObject i:DataStore.objects(data.items())){if(!i.optBoolean("available")&&DataStore.watched(i)&&i.optBoolean("movie"))deleted=i;if(video==null&&i.optBoolean("available"))video=i;}
        assertNotNull("Deleted watched movie retained",deleted);assertTrue("Deleted movie poster cached offline",data.art(deleted.optString("poster")).exists());assertNotNull(video);
        String id=video.optString("id");String viewing=UUID.randomUUID().toString();data.progress(id,viewing,37.125,1800,false);data.sync();assertEquals(37.125,data.item(id).getJSONObject("progress").getDouble("position"),.0001);assertEquals(0,data.pendingCount());
        DataStore reloaded=new DataStore(getInstrumentation().getTargetContext());assertEquals(37.125,reloaded.item(id).getJSONObject("progress").getDouble("position"),.0001);assertTrue("Pair persists after relaunch",reloaded.paired());
        // Offline queued edits stay durable; newer PC results overlay only after receipt, never before.
        JSONObject op=data.operation(id,"review");DataStore.put(op,"rating",4);DataStore.put(op,"note","Android fixture note");data.queue(op);DataStore offline=new DataStore(getInstrumentation().getTargetContext());assertEquals(1,offline.pendingCount());assertEquals(4,offline.item(id).getJSONObject("review").getInt("rating"));data.sync();assertEquals(4,data.item(id).getJSONObject("review").getInt("rating"));assertEquals(0,data.pendingCount());
        // Exercise the same pinning/range bridge used by the real Android VLC player.
        LibVLC[] lib=new LibVLC[1];org.videolan.libvlc.MediaPlayer[] player=new org.videolan.libvlc.MediaPlayer[1];MediaProxy bridge=new MediaProxy(data,id);
        getInstrumentation().runOnMainSync(()->{lib[0]=new LibVLC(getInstrumentation().getTargetContext(),new ArrayList<>(Arrays.asList("--vout=dummy","--aout=dummy","--avcodec-hw=none","--quiet")));player[0]=new org.videolan.libvlc.MediaPlayer(lib[0]);Media m=new Media(lib[0],android.net.Uri.parse(bridge.url()));player[0].setMedia(m);m.release();player[0].play();});
        try{for(int n=0;n<120&&(!player[0].isPlaying()||player[0].getLength()<60000);n++)Thread.sleep(250);assertTrue("VLC decodes real PC episode through pinned HTTPS",player[0].isPlaying());assertTrue(player[0].getLength()>60000);
            getInstrumentation().runOnMainSync(()->player[0].setTime(83125,false));for(int n=0;n<80&&Math.abs(player[0].getTime()-83125)>3000;n++)Thread.sleep(250);assertTrue("Android stream seeks",Math.abs(player[0].getTime()-83125)<3000);
            assertTrue("Embedded audio/subtitle tracks available",player[0].getAudioTracksCount()>0&&player[0].getSpuTracksCount()>0);
        }finally{getInstrumentation().runOnMainSync(()->{player[0].stop();player[0].release();lib[0].release();});bridge.close();}
        JSONObject wrongPin=new JSONObject();DataStore.put(wrongPin,"code","00000000");try{DataStore.jsonRequest(data.address(),"0".repeat(64),"","/v1/pair",wrongPin.toString());fail("Wrong certificate must be rejected");}catch(javax.net.ssl.SSLException expected){}
        Intent home=new Intent(getInstrumentation().getTargetContext(),MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);getInstrumentation().startActivitySync(home);getInstrumentation().waitForIdleSync();
        File result=new File(getInstrumentation().getTargetContext().getFilesDir(),"android-checks.txt");try(FileWriter out=new FileWriter(result)){out.write("PASS: pairing persistence, full catalog, deleted movie poster, fractional two-way resume, offline durable queue, rating sync, real VLC playback, seeking, audio/subtitles, certificate pin rejection\n");}
    }
    @Test public void testVisiblePlayerAndHistory()throws Exception {
        DataStore data=NoirApp.data;if(!data.paired())data.pair(TestRunner.arguments.getString("pairUri"));else data.sync();
        MainActivity home=(MainActivity)getInstrumentation().startActivitySync(new Intent(getInstrumentation().getTargetContext(),MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
        getInstrumentation().waitForIdleSync();Thread.sleep(600);screenshot("home.png");
        getInstrumentation().runOnMainSync(()->home.nav.getChildAt(2).performClick());getInstrumentation().waitForIdleSync();Thread.sleep(500);screenshot("history.png");assertEquals("History",home.page);getInstrumentation().runOnMainSync(()->{home.premium.historyView="Diary";home.render();});getInstrumentation().waitForIdleSync();screenshot("premium-diary.png");getInstrumentation().runOnMainSync(()->{home.premium.historyView="Collection";home.render();});getInstrumentation().waitForIdleSync();screenshot("premium-history.png");
        JSONObject item=DataStore.objects(data.items()).stream().filter(i->i.optBoolean("available")).findFirst().get();String id=item.optString("id");
        getInstrumentation().runOnMainSync(()->{home.page="Library";home.detail=item.optString("group");home.render();});getInstrumentation().waitForIdleSync();Thread.sleep(500);screenshot("show.png");
        PlayerActivity p=(PlayerActivity)getInstrumentation().startActivitySync(new Intent(getInstrumentation().getTargetContext(),PlayerActivity.class).putExtra("id",id).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
        for(int n=0;n<100&&(p.player==null||!p.player.isPlaying()||!p.player.isSeekable());n++)Thread.sleep(250);
        assertTrue("Visible player is playing",p.player!=null&&p.player.isPlaying());getInstrumentation().runOnMainSync(()->p.seek(360.125));Thread.sleep(3000);getInstrumentation().runOnMainSync(()->p.show());screenshot("player-controls.png");
        android.graphics.Bitmap[] frame=new android.graphics.Bitmap[1];getInstrumentation().runOnMainSync(()->{android.view.TextureView texture=p.video.findViewById(org.videolan.R.id.texture_video);if(texture!=null)frame[0]=texture.getBitmap(320,180);});
        assertNotNull("Video texture produces a frame",frame[0]);Set<Integer> colors=new HashSet<>();for(int y=0;y<180;y+=3)for(int x=0;x<320;x+=3)colors.add(frame[0].getPixel(x,y));try(FileOutputStream out=new FileOutputStream(new File(getInstrumentation().getTargetContext().getFilesDir(),"decoded-frame.png"))){frame[0].compress(android.graphics.Bitmap.CompressFormat.PNG,100,out);}frame[0].recycle();assertTrue("Decoded picture contains real video pixels, not a black screen ("+colors.size()+")",colors.size()>20);
        getInstrumentation().runOnMainSync(()->p.root.getChildAt(1).performClick());getInstrumentation().waitForIdleSync();assertEquals(android.view.View.GONE,p.controls.getVisibility());screenshot("player-hidden.png");
        getInstrumentation().runOnMainSync(()->{p.root.getChildAt(1).performClick();p.pause.performClick();});for(int n=0;n<20&&p.player.isPlaying();n++)Thread.sleep(100);assertFalse("Pause works",p.player.isPlaying());assertTrue("Pause saves actual timestamp",data.item(id).getJSONObject("progress").getDouble("position")>=359);
        getInstrumentation().runOnMainSync(p::finish);for(int n=0;n<50&&!p.destroyed;n++)Thread.sleep(100);getInstrumentation().waitForIdleSync();assertTrue("Player released when returning to library",p.destroyed);screenshot("returned-library.png");
    }
    void screenshot(String name)throws Exception {Thread.sleep(350);android.graphics.Bitmap b=getInstrumentation().getUiAutomation().takeScreenshot();try(FileOutputStream out=new FileOutputStream(new File(getInstrumentation().getTargetContext().getFilesDir(),name))){b.compress(android.graphics.Bitmap.CompressFormat.PNG,100,out);}b.recycle();}
}
