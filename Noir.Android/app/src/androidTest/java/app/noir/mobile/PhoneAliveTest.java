package app.noir.mobile;
import android.app.*;
import android.content.*;
import android.view.*;
import android.widget.*;
import androidx.test.platform.app.InstrumentationRegistry;
import org.json.*;
import org.junit.Test;
import java.io.File;
import java.util.*;
import static org.junit.Assert.*;

public final class PhoneAliveTest {
 final Instrumentation test=InstrumentationRegistry.getInstrumentation();
 TextView text(View v,String value){if(v instanceof TextView&&((TextView)v).getText().toString().equals(value))return (TextView)v;if(v instanceof ViewGroup)for(int n=0;n<((ViewGroup)v).getChildCount();n++){TextView found=text(((ViewGroup)v).getChildAt(n),value);if(found!=null)return found;}return null;}
 @Test public void trackerModePersistsAndCheckOffKeepsThePage()throws Exception{Context real=test.getTargetContext();File dir=new File(real.getFilesDir(),"phone-alive-"+UUID.randomUUID());assertTrue(dir.mkdirs());Context isolated=new ContextWrapper(real){@Override public Context getApplicationContext(){return this;}@Override public File getFilesDir(){return dir;}};DataStore previous=NoirApp.data,data=new DataStore(isolated);boolean arabic=Loc.arabic;Loc.arabic=false;MainActivity[] activity=new MainActivity[1];try{NoirApp.data=data;data.phoneSetting("historyOnly",true);data.pref("experienceChosen",true);for(int n=1;n<=4;n++)data.rememberPhoneItem(new JSONObject().put("id","alive-"+n).put("group","alive").put("title","A phone story").put("season",1).put("episode",n).put("airsAt","2020-01-01T00:00:00Z"));data.mark("alive-1",true);DataStore.flushWrites();DataStore reopened=new DataStore(isolated);try{assertTrue(PhoneExperience.trackerOnly(reopened));}finally{reopened.network.shutdownNow();reopened.images.shutdownNow();}
  activity[0]=(MainActivity)test.startActivitySync(new Intent(real,MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));MainActivity a=activity[0];View[] original=new View[2];test.runOnMainSync(()->{assertEquals("History",a.page);assertEquals(4,a.nav.getChildCount());assertNotNull(text(a.nav,"✓\nNext"));original[0]=a.body;original[1]=a.scroller;JSONObject next=data.item("alive-2");a.smart.checkOff(next,"Second episode");});test.waitForIdleSync();Thread.sleep(250);test.runOnMainSync(()->{assertSame(original[0],a.body);assertSame(original[1],a.scroller);assertTrue(data.item("alive-2").optJSONArray("viewings").length()>0);assertEquals(3,a.tracker.track(a.group("alive"),System.currentTimeMillis()).next.optInt("episode"));assertNotNull(text(a.body,"✓ Next episode"));assertNotNull(text(a.root,"Undo"));a.undoAction.run();a.undoUntil=0;a.renderKeep();assertFalse(DataStore.watched(data.item("alive-2")));a.detail="alive";a.render();TextView list=text(a.body,"+ My List");assertNotNull(list);View body=a.body;list.performClick();assertSame(body,a.body);assertEquals("✓ My List",list.getText().toString());assertTrue(data.item("alive-1").optBoolean("listed"));list.performClick();assertFalse(data.item("alive-1").optBoolean("listed"));});
 }finally{if(activity[0]!=null)test.runOnMainSync(activity[0]::finish);NoirApp.data=previous;Loc.arabic=arabic;data.network.shutdownNow();data.images.shutdownNow();}}
}
