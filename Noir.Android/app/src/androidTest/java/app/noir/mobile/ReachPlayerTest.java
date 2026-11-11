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

    void tap(PlayerActivity p,float fx)throws Exception{gesture(p,fx,false);}
    // A busy emulator can deliver the second tap late; that pair is two single taps, not a skip, so try again (spaced out) up to three times.
    void doubleTap(PlayerActivity p,float fx)throws Exception{for(int attempt=0;attempt<3;attempt++){if(gesture(p,fx,true))return;Thread.sleep(800);}fail("Both taps occur within double-tap timeout (3 attempts)");}
    boolean gesture(PlayerActivity p,float fx,boolean twice)throws Exception{
        java.util.concurrent.CountDownLatch done=new java.util.concurrent.CountDownLatch(1);long[] first={0},second={0};
        test.runOnMainSync(()->{View touch=p.root.getChildAt(1);android.os.Handler handler=new android.os.Handler(android.os.Looper.getMainLooper());
            Runnable pair=new Runnable(){int count=0;public void run(){long downAt=SystemClock.uptimeMillis();if(count==0)first[0]=downAt;else second[0]=downAt;dispatch(touch,fx,MotionEvent.ACTION_DOWN,downAt);
                handler.postDelayed(()->{dispatch(touch,fx,MotionEvent.ACTION_UP,downAt);if(twice&&count++==0)handler.postDelayed(this,80);else done.countDown();},40);
            }};pair.run();
        });assertTrue("Touch sequence completes",done.await(2,java.util.concurrent.TimeUnit.SECONDS));return !twice||second[0]-first[0]<android.view.ViewConfiguration.getDoubleTapTimeout();
    }
    void dispatch(View touch,float fx,int action,long downAt){long now=SystemClock.uptimeMillis();MotionEvent event=MotionEvent.obtain(downAt,now,action,touch.getWidth()*fx,touch.getHeight()/2f,0);try{touch.dispatchTouchEvent(event);}finally{event.recycle();}}

    @Test public void doubleTapSkipsTenSeconds()throws Exception{
        DataStore data=NoirApp.data;if(!data.paired())data.pair(TestRunner.arguments.getString("pairUri"));else data.sync();
        JSONObject item=DataStore.objects(data.items()).stream().filter(i->i.optString("title").equals("Bright pattern fixture")).findFirst().orElseThrow(()->new AssertionError("Missing generated Bright pattern fixture"));
        data.pref("tipPlayerGestures",false);PlayerActivity p=(PlayerActivity)test.startActivitySync(new Intent(test.getTargetContext(),PlayerActivity.class).putExtra("id",item.optString("id")).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
        try{
            for(int n=0;n<360&&(p.player==null||!p.player.isPlaying()||!p.player.isSeekable()||p.duration<=0);n++)Thread.sleep(250);
            assertTrue("Player starts",p.player!=null&&p.player.isPlaying());assertTrue("The gesture tip is recorded as shown",data.prefs().optBoolean("tipPlayerGestures"));
            boolean[] tip={false};for(int n=0;n<60&&!tip[0];n++){test.runOnMainSync(()->tip[0]=p.flash.getVisibility()==View.VISIBLE&&p.flash.getText().toString().equals(Loc.t("Tip: double-tap left or right to skip 10 s")));Thread.sleep(100);}assertTrue("The first playback shows the gesture tip",tip[0]);
            boolean[] gone={false};for(int n=0;n<80&&!gone[0];n++){test.runOnMainSync(()->gone[0]=p.flash.getVisibility()==View.GONE);Thread.sleep(100);}assertTrue("The tip fades by itself",gone[0]);
            test.runOnMainSync(()->{p.player.pause();p.seek(120);p.manualHidden=true;p.controls.setVisibility(View.GONE);});PlayerResumeTest.waitPosition(p,120);
            double[] before={0};test.runOnMainSync(()->before[0]=p.lastPosition);
            doubleTap(p,.85f);test.waitForIdleSync();
            test.runOnMainSync(()->{assertEquals("Right double tap skips ahead 10 s",before[0]+10,p.lastPosition,1.5);assertEquals(View.VISIBLE,p.flash.getVisibility());assertTrue(p.flash.getText().toString(),p.flash.getText().toString().contains("10"));assertEquals("Hidden controls stay hidden",View.GONE,p.controls.getVisibility());before[0]=p.lastPosition;});
            Thread.sleep(1200);doubleTap(p,.15f);test.waitForIdleSync();
            test.runOnMainSync(()->{assertEquals("Left double tap skips back 10 s",before[0]-10,p.lastPosition,1.5);assertTrue(p.flash.getText().toString().startsWith("«"));});
            Thread.sleep(600);tap(p,.5f);Thread.sleep(700);test.waitForIdleSync();
            test.runOnMainSync(()->assertEquals("A single tap still shows the controls",View.VISIBLE,p.controls.getVisibility()));
        }finally{test.runOnMainSync(()->{if(!p.isFinishing())p.finish();});}
    }
}
