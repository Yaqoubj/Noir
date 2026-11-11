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
        assertNotNull(find(home.body,"Downloads"));assertNotNull(find(home.body,"Profile"));assertNotNull(find(home.body,"Playback"));capture("redesign-more.png");
        test.runOnMainSync(()->find(home.body,"Profile").performClick());assertEquals("Profile hook stays on More until wired","More",home.page);
        test.runOnMainSync(()->{home.page="Library";home.open(item);home.options(item);});test.waitForIdleSync();
        assertNotNull(home.titleActions.dialog);assertTrue(home.titleActions.dialog.isShowing());
        View sheet=home.titleActions.dialog.getWindow().getDecorView();
        assertNotNull(find(sheet,"Watch now or resume"));assertNotNull(find(sheet,"Download…"));
        assertTrue("Sheet stays within the visible screen",sheet.getHeight()<=home.getWindow().getDecorView().getHeight());
        capture("redesign-options.png");
        ScrollView scrolling=scroll(sheet);assertNotNull(scrolling);test.runOnMainSync(()->scrolling.scrollTo(0,scrolling.getChildAt(0).getHeight()));test.waitForIdleSync();
        assertTrue("Secondary actions remain reachable",scrolling.getScrollY()>0);assertTrue(find(sheet,"Correct match").isShown());capture("redesign-options-details.png");
        test.runOnMainSync(()->find(sheet,"Play queue").performClick());test.waitForIdleSync();
        assertNull("Action dismisses the sheet",home.titleActions.dialog);assertTrue(data.playQueue().toString().contains(item.optString("id")));data.consumeVideo(item.optString("id"));
        JSONObject missing=DataStore.copy(item);DataStore.put(missing,"id","ui-unavailable");DataStore.put(missing,"group","show:ui-unavailable");DataStore.put(missing,"imdb","tt993305");DataStore.put(missing,"available",false);DataStore.put(missing,"inLibrary",false);DataStore.put(missing,"libraryAt",0);data.rememberPhoneItem(missing);
        test.runOnMainSync(()->home.options(missing));test.waitForIdleSync();View unavailable=home.titleActions.dialog.getWindow().getDecorView();
        assertTrue("Unavailable PC media can still discover downloads",find(unavailable,"Download…").isEnabled());
        assertNull("Unavailable video has no Start over (U72: options that cannot work are hidden)",find(unavailable,"Start over"));
        Instrumentation.ActivityMonitor downloads=test.addMonitor(PhoneDownloadsActivity.class.getName(),null,false);
        test.runOnMainSync(()->find(unavailable,"Download…").performClick());
        PhoneDownloadsActivity discovery=(PhoneDownloadsActivity)test.waitForMonitorWithTimeout(downloads,5000);test.removeMonitor(downloads);assertNotNull("Download discovery opens for unavailable PC media",discovery);test.waitForIdleSync();
        test.runOnMainSync(()->{assertFalse(discovery.item.optBoolean("available"));assertNotNull(discovery.scopeSheet);assertTrue(discovery.scopeSheet.dialog.isShowing());discovery.finish();});assertNull(home.titleActions.dialog);
        // The PC fixture never acknowledges this fake title; a leaked op breaks later sync tests. The download screen's
        // background setup can queue it again after finish, so let that settle before dropping it.
        test.waitForIdleSync();for(int n=0;n<20;n++){data.dropPending("ui-unavailable");Thread.sleep(100);}data.dropPending("ui-unavailable");
        // Calendar layout gets a deterministic future episode even when the real library has no upcoming dates.
        JSONObject ep=new JSONObject();DataStore.put(ep,"season",2);DataStore.put(ep,"episode",3);DataStore.put(ep,"airsAt",java.time.Instant.now().plusSeconds(3*86400).toString());
        test.runOnMainSync(()->{home.page="More";home.detail="";home.render();home.body.addView(home.ui.calendarCard(item,ep),0);});capture("redesign-calendar.png");
        test.runOnMainSync(home::onBackPressed);assertEquals("Home",home.page);
        test.runOnMainSync(home::finish);
    }
}
