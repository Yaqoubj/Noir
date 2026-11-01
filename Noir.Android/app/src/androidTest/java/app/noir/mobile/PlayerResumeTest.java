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
        JSONObject item=DataStore.objects(data.items()).stream().filter(i->i.optBoolean("available")).findFirst().get();
        PlayerActivity p=(PlayerActivity)test.startActivitySync(new Intent(test.getTargetContext(),PlayerActivity.class).putExtra("id",item.optString("id")).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
        try{
            for(int n=0;n<360&&(p.player==null||!p.player.isPlaying()||!p.player.isSeekable());n++)Thread.sleep(250);
            assertTrue("Player starts",p.player!=null&&p.player.isPlaying());test.runOnMainSync(()->p.seek(120));Thread.sleep(3000);// a fixed bright scene, not wherever an earlier test left the resume point
            assertTrue("Picture before leaving ("+colors(p)+" colours)",colors(p)>20);
            for(int round=1;round<=3;round++){
                test.getUiAutomation().performGlobalAction(android.accessibilityservice.AccessibilityService.GLOBAL_ACTION_HOME);
                for(int n=0;n<40&&p.active;n++)Thread.sleep(100);Thread.sleep(1500);
                test.getTargetContext().startActivity(new Intent(test.getTargetContext(),PlayerActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK|Intent.FLAG_ACTIVITY_REORDER_TO_FRONT));
                for(int n=0;n<50&&!p.active;n++)Thread.sleep(100);test.waitForIdleSync();int c=0;for(int n=0;n<12&&(c=colors(p))<=20;n++)Thread.sleep(500);
                assertTrue("Round "+round+": paused picture within 6 s of returning ("+c+" colours)",c>20);
                test.runOnMainSync(()->p.player.play());Thread.sleep(2500);
                assertTrue("Round "+round+": still playing",p.player.isPlaying());
                assertTrue("Round "+round+": moving picture after returning ("+colors(p)+" colours)",colors(p)>20);
            }
        }finally{test.runOnMainSync(()->{if(!p.isFinishing())p.finish();});}
    }

    /** Distinct colours in a small copy of the video surface; a black or missing picture has almost none. */
    int colors(PlayerActivity p)throws Exception{
        Bitmap[] frame=new Bitmap[1];CountDownLatch copied=new CountDownLatch(1);
        test.runOnMainSync(()->{SurfaceView surface=p.video.findViewById(org.videolan.R.id.surface_video);if(surface==null||!surface.getHolder().getSurface().isValid()){copied.countDown();return;}Bitmap b=Bitmap.createBitmap(320,180,Bitmap.Config.ARGB_8888);PixelCopy.request(surface,b,r->{if(r==PixelCopy.SUCCESS)frame[0]=b;copied.countDown();},new Handler(Looper.getMainLooper()));});
        copied.await(5,TimeUnit.SECONDS);if(frame[0]==null)return 0;
        Set<Integer> colors=new HashSet<>();for(int y=0;y<180;y+=3)for(int x=0;x<320;x+=3)colors.add(frame[0].getPixel(x,y));frame[0].recycle();return colors.size();
    }
}
