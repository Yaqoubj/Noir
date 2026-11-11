package app.noir.mobile;
import android.app.*;
import android.content.*;
import androidx.test.platform.app.InstrumentationRegistry;
import org.json.*;
import org.junit.*;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.util.UUID;
import java.util.concurrent.*;
import static org.junit.Assert.*;

public final class TvProfileLockTest {
 final Instrumentation test=InstrumentationRegistry.getInstrumentation();
 DataStore previous,data;Context context,previousRoot;DownloadStore previousDownloads;File root;boolean chosen;TvActivity home;TvProfilesActivity picker;TvKeypad pad;
 @Before public void setup()throws Exception{org.junit.Assume.assumeTrue("TV-only tests: run in work/test-tv.ps1",TvUi.isTv(test.getTargetContext()));Context real=test.getTargetContext();root=new File(real.getFilesDir(),"work/sharpen-round/w3tv/"+UUID.randomUUID());assertTrue(root.mkdirs());context=new ContextWrapper(real){@Override public Context getApplicationContext(){return this;}@Override public File getFilesDir(){return root;}@Override public File getCacheDir(){return new File(root,"cache");}@Override public File getExternalFilesDir(String type){return new File(root,"external");}@Override public SharedPreferences getSharedPreferences(String name,int mode){return super.getSharedPreferences("w3tv-"+root.getName()+name,mode);}};previous=NoirApp.data;previousDownloads=NoirApp.downloads;NoirApp app=(NoirApp)real.getApplicationContext();previousRoot=app.rootContext;data=new DataStore(context);app.rootContext=context;NoirApp.data=data;NoirApp.downloads=null;chosen=TvProfilesActivity.chosen;TvProfilesActivity.chosen=false;data.pref("tvSetupSkipped",true);for(String kind:new String[]{"movie","series"})DiscoveryClient.write(new File(new DiscoveryClient(data.context).directory,DiscoveryClient.hash("catalog/"+kind+"/top/skip=0.json")+".json"),"{\"metas\":[]}".getBytes(StandardCharsets.UTF_8));}
 @After public void cleanup()throws Exception{if(data==null)return;test.runOnMainSync(()->{if(pad!=null)pad.dialog.dismiss();if(picker!=null)picker.finish();if(home!=null)home.finish();});test.waitForIdleSync();data.network.submit(()->{}).get(10,TimeUnit.SECONDS);((NoirApp)test.getTargetContext().getApplicationContext()).rootContext=previousRoot;NoirApp.data=previous;NoirApp.downloads=previousDownloads;Loc.use(previous);TvProfilesActivity.chosen=chosen;data.flush();data.network.shutdownNow();data.images.shutdownNow();context.getSharedPreferences("tv-profile-pin",0).edit().clear().commit();erase(root);}
 static void erase(File f){File[] children=f.listFiles();if(children!=null)for(File child:children)erase(child);assertTrue(!f.exists()||f.delete());}
 TvActivity home(){return (TvActivity)test.startActivitySync(new Intent(test.getTargetContext(),TvActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));}
 @Test public void pickerBackFinishesHomeWithoutOpeningSavedProfile()throws Exception{
  data.profiles.pin(data.profileId,"1234");data.profiles.create("Other","#7AA7FF");String current=data.profiles.current();
  // Drive the Home gate result path directly (chosen=true keeps Home from opening its own picker first).
  TvProfilesActivity.chosen=true;home=home();Instrumentation.ActivityMonitor monitor=test.addMonitor(TvProfilesActivity.class.getName(),null,false);
  try{test.runOnMainSync(()->{home.gated=true;home.startActivityForResult(new Intent(home,TvProfilesActivity.class),41);});picker=(TvProfilesActivity)monitor.waitForActivityWithTimeout(5000);assertNotNull(picker);test.runOnMainSync(picker::onBackPressed);test.waitForIdleSync();test.runOnMainSync(()->{assertTrue("Cancel must finish Home",home.isFinishing()||home.isDestroyed());assertTrue("Cancel must never open Home",home.gated);});assertSame(data,NoirApp.data);assertEquals(current,data.profiles.current());}finally{test.removeMonitor(monitor);}
 }
 @Test public void startupUsesSavedProfileWithoutPinJustLikePhone()throws Exception{
  data.profiles.pin(data.profileId,"1234");assertFalse("Phone MainActivity has no startup PIN/picker",TvProfilesActivity.needed(data));
  home=home();test.runOnMainSync(()->{assertFalse(home.gated);assertSame(data,home.data);assertEquals(data.profileId,data.profiles.current());});
  data.profiles.create("Other","#7AA7FF");TvProfilesActivity.chosen=false;assertTrue("Two or more profiles still ask who is watching",TvProfilesActivity.needed(data));
 }
 @Test public void cancellingSubmittedPinBeforeWorkerRunsCannotSwitch()throws Exception{
  JSONObject other=data.profiles.create("Protected","#7AA7FF");String id=other.optString("id"),current=data.profiles.current();data.profiles.pin(id,"1234");CountDownLatch entered=new CountDownLatch(1),release=new CountDownLatch(1);
  data.network.execute(()->{entered.countDown();try{release.await();}catch(InterruptedException e){Thread.currentThread().interrupt();}});assertTrue(entered.await(5,TimeUnit.SECONDS));
  try{picker=(TvProfilesActivity)test.startActivitySync(new Intent(test.getTargetContext(),TvProfilesActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));test.runOnMainSync(()->pad=new TvKeypad(picker,"Protected",(pin,keypad)->picker.switchTo(id,pin,keypad)));test.waitForIdleSync(); /* the keypad wires its Open button in the dialog's show listener, which runs after show() returns */
   test.runOnMainSync(()->{pad.pin.append("1234");pad.dialog.getButton(-1).performClick();assertTrue(picker.switching);pad.dialog.cancel();});release.countDown();data.network.submit(()->{}).get(10,TimeUnit.SECONDS);test.waitForIdleSync();assertSame("Cancelled work must not replace the active store",data,NoirApp.data);assertEquals(current,data.profiles.current());assertFalse(TvProfilesActivity.chosen);}finally{release.countDown();}
 }
}
