package app.noir.mobile;

import android.app.Instrumentation;
import android.content.Intent;
import android.graphics.Bitmap;
import android.view.View;
import android.view.ViewGroup;
import android.widget.ScrollView;
import androidx.test.platform.app.InstrumentationRegistry;
import org.json.JSONObject;
import org.junit.Test;
import java.io.*;
import static org.junit.Assert.*;

/** Exercises the real activity and options against the disposable PC fixture. */
public final class PremiumUiTest {
    final Instrumentation test=InstrumentationRegistry.getInstrumentation();
    View find(View view,String description){
        if(description.contentEquals(view.getContentDescription()==null?"":view.getContentDescription()))return view;
        if(view instanceof ViewGroup)for(int n=0;n<((ViewGroup)view).getChildCount();n++){View found=find(((ViewGroup)view).getChildAt(n),description);if(found!=null)return found;}
        return null;
    }
    ScrollView scroll(View view){if(view instanceof ScrollView)return (ScrollView)view;if(view instanceof ViewGroup)for(int n=0;n<((ViewGroup)view).getChildCount();n++){ScrollView found=scroll(((ViewGroup)view).getChildAt(n));if(found!=null)return found;}return null;}
    void capture(String name)throws Exception{
        test.waitForIdleSync();Thread.sleep(400);Bitmap bitmap=test.getUiAutomation().takeScreenshot();assertNotNull(bitmap);
        try(FileOutputStream out=new FileOutputStream(new File(test.getTargetContext().getFilesDir(),name))){bitmap.compress(Bitmap.CompressFormat.PNG,100,out);}bitmap.recycle();
    }
    @Test public void testOptionsAndMore()throws Exception{
        DataStore data=NoirApp.data;if(!data.paired())data.pair(TestRunner.arguments.getString("pairUri"));else data.sync();
        JSONObject item=DataStore.objects(data.items()).stream().filter(i->i.optBoolean("available")&&!i.optBoolean("movie")).findFirst().get();
        MainActivity home=(MainActivity)test.startActivitySync(new Intent(test.getTargetContext(),MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
        test.waitForIdleSync();capture("redesign-home.png");
        test.runOnMainSync(()->home.nav.getChildAt(4).performClick());test.waitForIdleSync();
        assertEquals("More is a destination, not a popup","More",home.page);
        assertNotNull(find(home.body,"Downloads"));assertNotNull(find(home.body,"Settings"));capture("redesign-more.png");
        test.runOnMainSync(()->find(home.body,"Settings").performClick());assertEquals("Settings",home.page);
        test.runOnMainSync(()->{home.page="Library";home.open(item);home.options(item);});test.waitForIdleSync();
        assertNotNull(home.titleActions.dialog);assertTrue(home.titleActions.dialog.isShowing());
        View sheet=home.titleActions.dialog.getWindow().getDecorView();
        assertNotNull(find(sheet,"Watch now or resume"));assertNotNull(find(sheet,"Season to PC"));assertNotNull(find(sheet,"Save on phone"));
        assertTrue("Sheet stays within the visible screen",sheet.getHeight()<=home.getWindow().getDecorView().getHeight());
        capture("redesign-options.png");
        ScrollView scrolling=scroll(sheet);assertNotNull(scrolling);test.runOnMainSync(()->scrolling.scrollTo(0,scrolling.getChildAt(0).getHeight()));test.waitForIdleSync();
        assertTrue("Secondary actions remain reachable",scrolling.getScrollY()>0);assertTrue(find(sheet,"Correct match").isShown());capture("redesign-options-details.png");
        test.runOnMainSync(()->find(sheet,"Play queue").performClick());test.waitForIdleSync();
        assertNull("Action dismisses the sheet",home.titleActions.dialog);assertTrue(data.playQueue().toString().contains(item.optString("id")));data.consumeVideo(item.optString("id"));
        JSONObject missing=DataStore.copy(item);DataStore.put(missing,"id","ui-unavailable");DataStore.put(missing,"available",false);
        test.runOnMainSync(()->home.options(missing));test.waitForIdleSync();View unavailable=home.titleActions.dialog.getWindow().getDecorView();
        assertFalse("Unavailable PC video cannot start a phone save",find(unavailable,"Save on phone").isEnabled());
        assertFalse("Unavailable video cannot restart",find(unavailable,"Start over").isEnabled());
        test.runOnMainSync(()->find(unavailable,"Close episode actions").performClick());assertNull(home.titleActions.dialog);
        // Calendar layout gets a deterministic future episode even when the real library has no upcoming dates.
        JSONObject ep=new JSONObject();DataStore.put(ep,"season",2);DataStore.put(ep,"episode",3);DataStore.put(ep,"airsAt",java.time.Instant.now().plusSeconds(3*86400).toString());
        test.runOnMainSync(()->{home.page="More";home.detail="";home.render();home.body.addView(home.ui.calendarCard(item,ep),0);});capture("redesign-calendar.png");
        test.runOnMainSync(home::onBackPressed);assertEquals("Home",home.page);
        test.runOnMainSync(home::finish);
    }
}
