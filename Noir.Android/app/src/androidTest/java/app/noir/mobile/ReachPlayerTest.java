package app.noir.mobile;

import android.app.Instrumentation;
import android.content.Intent;
import android.os.SystemClock;
import android.view.MotionEvent;
import android.view.View;
import androidx.test.platform.app.InstrumentationRegistry;
import org.json.JSONObject;
import org.junit.Test;
import static org.junit.Assert.*;

/** Phone player: a double tap on the right skips ahead 10 s, on the left back 10 s, with a short label and no change to the controls; the gesture tip is shown once. */
public final class ReachPlayerTest {
    final Instrumentation test=InstrumentationRegistry.getInstrumentation();

    void tap(PlayerActivity p,float fx,long at){View touch=p.root.getChildAt(1);int[] o=new int[2];float[] xy=new float[2];test.runOnMainSync(()->{touch.getLocationOnScreen(o);xy[0]=o[0]+touch.getWidth()*fx;xy[1]=o[1]+touch.getHeight()/2f;});
        MotionEvent down=MotionEvent.obtain(at,at,MotionEvent.ACTION_DOWN,xy[0],xy[1],0),up=MotionEvent.obtain(at,at+40,MotionEvent.ACTION_UP,xy[0],xy[1],0);test.sendPointerSync(down);test.sendPointerSync(up);down.recycle();up.recycle();}
    void doubleTap(PlayerActivity p,float fx){long t=SystemClock.uptimeMillis();tap(p,fx,t);tap(p,fx,t+120);}

    @Test public void doubleTapSkipsTenSeconds()throws Exception{
        DataStore data=NoirApp.data;if(!data.paired())data.pair(TestRunner.arguments.getString("pairUri"));else data.sync();
        JSONObject item=DataStore.objects(data.items()).stream().filter(i->i.optBoolean("available")).findFirst().get();
        data.pref("tipPlayerGestures",false);PlayerActivity p=(PlayerActivity)test.startActivitySync(new Intent(test.getTargetContext(),PlayerActivity.class).putExtra("id",item.optString("id")).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
        try{
            for(int n=0;n<360&&(p.player==null||!p.player.isPlaying()||!p.player.isSeekable()||p.duration<=0);n++)Thread.sleep(250);
            assertTrue("Player starts",p.player!=null&&p.player.isPlaying());assertTrue("The gesture tip is recorded as shown",data.prefs().optBoolean("tipPlayerGestures"));
            boolean[] tip={false};for(int n=0;n<60&&!tip[0];n++){test.runOnMainSync(()->tip[0]=p.flash.getVisibility()==View.VISIBLE&&p.flash.getText().toString().equals(Loc.t("Tip: double-tap left or right to skip 10 s")));Thread.sleep(100);}assertTrue("The first playback shows the gesture tip",tip[0]);
            boolean[] gone={false};for(int n=0;n<80&&!gone[0];n++){test.runOnMainSync(()->gone[0]=p.flash.getVisibility()==View.GONE);Thread.sleep(100);}assertTrue("The tip fades by itself",gone[0]);
            test.runOnMainSync(()->{p.player.pause();p.seek(120);p.manualHidden=true;p.controls.setVisibility(View.GONE);});Thread.sleep(800);
            double[] before={0};test.runOnMainSync(()->before[0]=p.lastPosition);
            doubleTap(p,.85f);test.waitForIdleSync();
            test.runOnMainSync(()->{assertEquals("Right double tap skips ahead 10 s",before[0]+10,p.lastPosition,1.5);assertEquals(View.VISIBLE,p.flash.getVisibility());assertTrue(p.flash.getText().toString(),p.flash.getText().toString().contains("10"));assertEquals("Hidden controls stay hidden",View.GONE,p.controls.getVisibility());before[0]=p.lastPosition;});
            Thread.sleep(1200);doubleTap(p,.15f);test.waitForIdleSync();
            test.runOnMainSync(()->{assertEquals("Left double tap skips back 10 s",before[0]-10,p.lastPosition,1.5);assertTrue(p.flash.getText().toString().startsWith("«"));});
            Thread.sleep(600);long t=SystemClock.uptimeMillis();tap(p,.5f,t);Thread.sleep(700);test.waitForIdleSync();
            test.runOnMainSync(()->assertEquals("A single tap still shows the controls",View.VISIBLE,p.controls.getVisibility()));
        }finally{test.runOnMainSync(()->{if(!p.isFinishing())p.finish();});}
    }
}
