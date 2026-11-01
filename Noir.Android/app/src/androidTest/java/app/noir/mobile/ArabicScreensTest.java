package app.noir.mobile;
import android.app.*;
import android.content.Intent;
import android.graphics.Bitmap;
import android.os.SystemClock;
import android.view.*;
import android.widget.*;
import androidx.test.platform.app.InstrumentationRegistry;
import org.json.*;
import org.junit.*;
import java.io.*;
import java.util.*;
import static org.junit.Assert.*;

/** Arabic (RTL) look of this round's new phone UI: tip card, ⋯, Settings rows, Tips & shortcuts, Keep ready and the player's double-tap label. Screens are saved to files/arabic-shots for a person to look at. */
public final class ArabicScreensTest {
 AuditPhoneFixture f;
 @Before public void setup()throws Exception{f=new AuditPhoneFixture();f.data.pref("language","ar");Loc.arabic=true;}
 @After public void cleanup()throws Exception{if(f!=null)f.close();}
 final Instrumentation test=InstrumentationRegistry.getInstrumentation();
 void shot(String name)throws Exception{test.waitForIdleSync();Thread.sleep(700);Bitmap b=test.getUiAutomation().takeScreenshot();assertNotNull(b);File dir=new File(test.getTargetContext().getExternalFilesDir(null),"arabic-shots");dir.mkdirs();try(FileOutputStream out=new FileOutputStream(new File(dir,name+".png"))){b.compress(Bitmap.CompressFormat.PNG,100,out);}}
 List<JSONObject> show(String id)throws Exception{String now=java.time.Instant.now().toString();List<JSONObject> eps=new ArrayList<>();for(int e=1;e<=2;e++){JSONObject i=new JSONObject().put("id",id+"-e"+e).put("group",id).put("title","Reach "+id).put("movie",false).put("imdb","tt9900"+Math.abs(id.hashCode()%1000)).put("season",1).put("episode",e).put("episodeTitle","Episode "+e).put("airsAt","2024-01-0"+e+"T00:00:00Z").put("inLibrary",true);if(e==1)i.put("viewings",new JSONArray().put(new JSONObject().put("id","ar-v-"+id).put("watchedAt",now).put("source","Android")));eps.add(i);}return f.data.rememberPhoneItems(eps);}
 View dialogWith(String text){for(View root:android.view.inspector.WindowInspector.getGlobalWindowViews())if(root.isShown()&&AuditPhoneFixture.find(root,v->v instanceof TextView&&v.isShown()&&Loc.t(text).contentEquals(((TextView)v).getText()))!=null)return root;return null;}
 View waitDialog(String text)throws Exception{long end=SystemClock.elapsedRealtime()+10000;View[] found={null};while(SystemClock.elapsedRealtime()<end){f.ui(()->found[0]=dialogWith(text));if(found[0]!=null)return found[0];Thread.sleep(40);}fail("Dialog not shown: "+text);return null;}

 @Test public void newPhoneScreensReadRightToLeft()throws Exception{
  PhoneHarbor.phoneCache=new JSONObject().put("manifest","https://noir-arabic.invalid/manifest.json");PhoneHarbor.phoneAt=System.currentTimeMillis();PhoneStream.cacheProfile=f.data.profileId;
  show("ar");MainActivity a=f.launch(MainActivity.class);
  f.ui(()->{a.page="Home";a.detail="";a.render();View card=AuditPhoneFixture.find(a.body,v->"tip:keepReady".equals(v.getTag()));assertNotNull("Keep ready tip",card);
   assertNotNull("Tip title is Arabic",AuditPhoneFixture.find(card,v->v instanceof TextView&&((TextView)v).getText().toString().equals("شاهد دون اتصال دون أن تفكر في ذلك")));});
  shot("phone-home-ar");f.ui(()->{View card=AuditPhoneFixture.find(a.body,v->"tip:keepReady".equals(v.getTag()));card.requestRectangleOnScreen(new android.graphics.Rect(0,0,card.getWidth(),card.getHeight()),true);});shot("phone-home-tip-ar");f.ui(()->assertEquals("Home is mirrored",View.LAYOUT_DIRECTION_RTL,a.getWindow().getDecorView().getLayoutDirection()));
  f.ui(()->{a.page="More";a.detail="";a.render();});shot("phone-more-ar");
  f.ui(()->{View alerts=AuditPhoneFixture.find(a.body,v->v.getContentDescription()!=null&&Loc.t("New-episode alerts").contentEquals(v.getContentDescription())&&v.hasOnClickListeners());assertNotNull(alerts);alerts.requestRectangleOnScreen(new android.graphics.Rect(0,0,alerts.getWidth(),alerts.getHeight()*4),true);});shot("phone-more-rows-ar");
  f.ui(()->{View tips=AuditPhoneFixture.find(a.body,v->v.getContentDescription()!=null&&Loc.t("Tips & shortcuts").contentEquals(v.getContentDescription())&&v.hasOnClickListeners());assertNotNull(tips);assertTrue(tips.performClick());});
  View list=waitDialog("Double-tap the video");f.ui(()->assertNotNull("Tips list is Arabic",AuditPhoneFixture.find(list,v->v instanceof TextView&&((TextView)v).getText().toString().equals("اضغط مرتين على الفيديو"))));shot("phone-tips-ar");
  f.ui(()->{Button close=list.findViewById(android.R.id.button1);close.performClick();DownloadHubActivity.openKeepReady(a,"ar");});waitDialog("Keep this show ready");shot("phone-keep-ready-ar");
 }
 /** Needs the paired fixture (run FixturePairTest first); the "« −10 ث" label keeps its arrow on the tapped side. */
 @Test public void doubleTapLabelKeepsItsDirection()throws Exception{
  f.close();f=null;boolean was=Loc.arabic;
  DataStore data=NoirApp.data;Assume.assumeTrue("needs the paired fixture",data!=null&&data.paired());Optional<JSONObject> item=DataStore.objects(data.items()).stream().filter(i->i.optBoolean("available")).findFirst();Assume.assumeTrue(item.isPresent());
  PhoneTips.init();Loc.arabic=true;data.pref("tipPlayerGestures",true);PlayerActivity p=(PlayerActivity)test.startActivitySync(new Intent(test.getTargetContext(),PlayerActivity.class).putExtra("id",item.get().optString("id")).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
  try{for(int n=0;n<240&&(p.player==null||!p.player.isPlaying()||!p.player.isSeekable()||p.duration<=0);n++)Thread.sleep(250);assertTrue("Player starts",p.player!=null&&p.player.isPlaying());
   test.runOnMainSync(()->{p.player.pause();p.seek(120);p.manualHidden=true;p.controls.setVisibility(View.GONE);p.tapSeek(-10);assertEquals(View.TEXT_DIRECTION_LTR,p.flash.getTextDirection());assertTrue(p.flash.getText().toString().startsWith("«"));p.flash(p.flash.getText().toString(),Gravity.LEFT,4000);});shot("phone-player-back-ar");
   Thread.sleep(1000);test.runOnMainSync(()->p.flash(Loc.t("Tip: double-tap left or right to skip 10 s"),Gravity.CENTER_HORIZONTAL,4000));shot("phone-player-tip-ar");
  }finally{test.runOnMainSync(()->{if(!p.isFinishing())p.finish();});Loc.arabic=was;}
 }
}
