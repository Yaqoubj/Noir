package app.noir.mobile;

import android.app.Instrumentation;
import android.content.Intent;
import android.graphics.Bitmap;
import android.os.Handler;
import android.os.Looper;
import android.view.PixelCopy;
import android.view.SurfaceView;
import androidx.test.platform.app.InstrumentationRegistry;
import org.json.JSONObject;
import org.junit.Test;
import java.util.HashSet;
import java.util.Set;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.TimeUnit;
import static org.junit.Assert.*;

/** Leaving the player for another app destroys its video surface; coming back must show the picture again, paused and playing. */
public final class PlayerResumeTest {
    final Instrumentation test=InstrumentationRegistry.getInstrumentation();

    @Test public void videoReturnsAfterSwitchingApps()throws Exception{
        DataStore data=NoirApp.data;if(!data.paired())data.pair(TestRunner.arguments.getString("pairUri"));else data.sync();
        JSONObject item=DataStore.objects(data.items()).stream().filter(i->i.optString("title").equals("Bright pattern fixture")).findFirst().orElseThrow(()->new AssertionError("Missing generated Bright pattern fixture"));
        PlayerActivity p=(PlayerActivity)test.startActivitySync(new Intent(test.getTargetContext(),PlayerActivity.class).putExtra("id",item.optString("id")).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
        try{
            for(int n=0;n<360&&(p.player==null||!p.player.isPlaying()||!p.player.isSeekable());n++)Thread.sleep(250);
            assertTrue("Player starts",p.player!=null&&p.player.isPlaying());test.runOnMainSync(()->p.seek(120));waitPosition(p,120);
            int initial=pictureColors(p,6000);assertTrue("Picture before leaving ("+initial+" colours)",initial>20);
            for(int round=1;round<=3;round++){
                test.getUiAutomation().performGlobalAction(android.accessibilityservice.AccessibilityService.GLOBAL_ACTION_HOME);
                waitUi(()->!p.active,4000,"Player leaves foreground");
                for(int n=0;n<40&&p.player.isPlaying();n++)Thread.sleep(100);assertFalse("Background player is paused",p.player.isPlaying());
                test.getTargetContext().startActivity(new Intent(test.getTargetContext(),PlayerActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK|Intent.FLAG_ACTIVITY_REORDER_TO_FRONT));
                waitUi(()->p.active,5000,"Player returns to foreground");test.waitForIdleSync();assertFalse("Returning preserves pause",p.player.isPlaying());int c=pictureColors(p,6000);
                assertTrue("Round "+round+": paused picture within 6 s of returning ("+c+" colours)",c>20);
                long before=p.player.getTime();test.runOnMainSync(()->p.player.play());
                long end=android.os.SystemClock.elapsedRealtime()+6000;while(android.os.SystemClock.elapsedRealtime()<end&&(!p.player.isPlaying()||p.player.getTime()<=before))Thread.sleep(50);assertTrue("Playback time advances after returning",p.player.getTime()>before);
                assertTrue("Round "+round+": still playing",p.player.isPlaying());
                int moving=pictureColors(p,6000);assertTrue("Round "+round+": moving picture after returning ("+moving+" colours)",moving>20);
            }
        }finally{test.runOnMainSync(()->{if(!p.isFinishing())p.finish();});}
    }

    static void waitUi(java.util.function.BooleanSupplier condition,long timeout,String label)throws Exception{
        Instrumentation test=InstrumentationRegistry.getInstrumentation();long end=android.os.SystemClock.elapsedRealtime()+timeout;boolean[] ready={false};
        while(android.os.SystemClock.elapsedRealtime()<end){test.runOnMainSync(()->ready[0]=condition.getAsBoolean());if(ready[0])return;Thread.sleep(50);}fail(label);
    }
    static void waitPosition(PlayerActivity p,double target)throws Exception{
        long end=android.os.SystemClock.elapsedRealtime()+10000;double actual=-1;
        while(android.os.SystemClock.elapsedRealtime()<end){actual=p.player.getTime()/1000d;if(Math.abs(actual-target)<1.5)return;Thread.sleep(50);}
        fail("Seek reaches "+target+" s; actual="+actual);
    }
    static int distinct(Bitmap b){if(b==null)return 0;Set<Integer> colors=new HashSet<>();for(int y=0;y<180;y+=3)for(int x=0;x<320;x+=3)colors.add(b.getPixel(x,y));return colors.size();}
    static int pictureColors(PlayerActivity p,long timeout)throws Exception{Bitmap b=picture(p,timeout);int c=distinct(b);if(b!=null)b.recycle();return c;}
    /** One deadline includes PixelCopy callbacks and retries; persistent black/lost surfaces still fail. */
    static Bitmap picture(PlayerActivity p,long timeout)throws Exception{
        Instrumentation test=InstrumentationRegistry.getInstrumentation();long end=android.os.SystemClock.elapsedRealtime()+timeout;Bitmap last=null;
        for(int attempt=0;attempt<30&&android.os.SystemClock.elapsedRealtime()<end;attempt++){
            Bitmap[] frame={null};CountDownLatch copied=new CountDownLatch(1);Object gate=new Object();boolean[] expired={false};
            test.runOnMainSync(()->{SurfaceView surface=p.video.findViewById(org.videolan.R.id.surface_video);if(surface==null||!surface.getHolder().getSurface().isValid()){copied.countDown();return;}Bitmap b=Bitmap.createBitmap(320,180,Bitmap.Config.ARGB_8888);try{PixelCopy.request(surface,b,r->{synchronized(gate){if(r==PixelCopy.SUCCESS&&!expired[0])frame[0]=b;else b.recycle();copied.countDown();}},new Handler(Looper.getMainLooper()));}catch(IllegalArgumentException e){b.recycle();copied.countDown();}});
            copied.await(Math.max(1,Math.min(500,end-android.os.SystemClock.elapsedRealtime())),TimeUnit.MILLISECONDS);
            synchronized(gate){expired[0]=true;if(frame[0]!=null){if(last!=null)last.recycle();last=frame[0];}}
            if(distinct(last)>20)return last;
            long remaining=end-android.os.SystemClock.elapsedRealtime();if(remaining>0)Thread.sleep(Math.min(200,remaining));
        }
        return last;
    }
}
