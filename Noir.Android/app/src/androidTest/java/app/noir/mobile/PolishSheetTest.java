package app.noir.mobile;
import android.app.*;
import android.view.*;
import android.widget.*;
import org.json.*;
import org.junit.*;
import java.util.*;
import java.util.concurrent.*;
import static org.junit.Assert.*;
/** The custom phone sheets share one family (NoirSheet); the dialogs that stay AlertDialogs share one dark theme. */
public final class PolishSheetTest {
 AuditPhoneFixture f;MainActivity a;JSONObject item;
 @Before public void setup()throws Exception{f=new AuditPhoneFixture();BackgroundSync.device(f.data.context).edit().putBoolean("askedBattery",true).apply();item=f.data.rememberPhoneItem(new JSONObject().put("id","sheet-1").put("group","sheet-1").put("title","Sheet show").put("imdb","tt987654321").put("season",1).put("episode",2).put("inLibrary",true).put("available",true));a=f.launch(MainActivity.class);}
 @After public void cleanup()throws Exception{f.ui(()->{a.titleActions.dismiss();PhoneStream.fetchOverride=null;});f.close();}
 View sheetRoot(){if(android.os.Build.VERSION.SDK_INT<29)return null;for(View root:android.view.inspector.WindowInspector.getGlobalWindowViews()){View found=AuditPhoneFixture.find(root,v->"noir-sheet".equals(v.getTag()));if(found!=null)return found;}return null;}
 // Dialog.onBackPressed is exactly what the Back key runs (cancel); synthetic key events depend on dispatcher tracking.
 void back(Dialog d){d.onBackPressed();}
 void assertFamily(NoirSheet s){assertNotNull(s);assertTrue(s.dialog.isShowing());assertEquals("noir-sheet",s.root.getTag());assertEquals(Loc.arabic?View.LAYOUT_DIRECTION_RTL:View.LAYOUT_DIRECTION_LTR,s.root.getLayoutDirection());}
 @Test public void titleActionsIsASheetThatBackAndCloseDismissInBothDirections()throws Exception{
  for(boolean arabic:new boolean[]{false,true}){
   f.ui(()->{Loc.arabic=arabic;a.options(item);assertFamily(a.titleActions.sheet);View close=AuditPhoneFixture.find(a.titleActions.sheet.root,v->Loc.t("Close episode actions").equals(String.valueOf(v.getContentDescription())));assertNotNull("The close control keeps its description",close);assertNotNull("The primary action is pinned (this fixture is unpaired with no local file and no stream source, so it offers Set up streaming)",AuditPhoneFixture.find(a.titleActions.sheet.root,v->Loc.t("Set up streaming").equals(String.valueOf(v.getContentDescription()))));assertTrue(close.performClick());});
   f.idle();f.ui(()->assertNull(a.titleActions.dialog));
   f.ui(()->{a.options(item);Dialog d=a.titleActions.dialog;assertNotNull(d);back(d);assertFalse(d.isShowing());});
  }
 }
 @Test public void discoverPreviewIsASheetWithOnePrimaryAction()throws Exception{
  for(boolean arabic:new boolean[]{false,true}){DiscoverSheet[] made={null};f.ui(()->{Loc.arabic=arabic;JSONObject seed=new JSONObject();DataStore.put(seed,"id","tt0000001");DataStore.put(seed,"name","Preview title");DataStore.put(seed,"noirType","movie");DiscoverSheet sheet=new DiscoverSheet(a,seed,null);assertFamily(sheet.sheet);assertNotNull(AuditPhoneFixture.find(sheet.sheet.root,v->Loc.t("Close preview").equals(String.valueOf(v.getContentDescription()))));assertNotNull("Full page link",AuditPhoneFixture.find(sheet.sheet.root,v->v instanceof TextView&&Loc.t("Full page ›").contentEquals(((TextView)v).getText())&&v.hasOnClickListeners()));assertNotNull("Stream is the pinned primary",AuditPhoneFixture.find(sheet.sheet.root,v->Loc.t("Stream").equals(String.valueOf(v.getContentDescription()))));made[0]=sheet;back(sheet.dialog);assertFalse(sheet.dialog.isShowing());});f.idle();f.ui(()->assertTrue("Dismiss listener cancelled the fetch",made[0].closed));}
 }
 @Test public void libraryStatusMenuIsASheet()throws Exception{
  Assume.assumeTrue(android.os.Build.VERSION.SDK_INT>=29);
  f.ui(()->{a.premium.status(item);View root=sheetRoot();assertNotNull("Viewing status opens as a sheet",root);assertNotNull(AuditPhoneFixture.find(root,v->v instanceof TextView&&Loc.t("Viewing status").contentEquals(((TextView)v).getText())));assertNotNull(AuditPhoneFixture.find(root,v->"Watching".equals(v.getTag())));View row=AuditPhoneFixture.find(root,v->"Dropped".equals(v.getTag()));assertNotNull(row);assertTrue(row.performClick());});
  f.idle();f.ui(()->assertNull("Choosing closes the sheet",sheetRoot()));
 }
 @Test public void streamSearchAndStreamChoiceAreSheets()throws Exception{
  PhoneHarbor.phoneCache=new JSONObject().put("manifest","https://noir-n3.invalid/manifest.json");PhoneHarbor.phoneAt=System.currentTimeMillis();PhoneStream.cacheProfile=f.data.profileId;CountDownLatch release=new CountDownLatch(1);PhoneStream.fetchOverride=(url,max)->{release.await(30,TimeUnit.SECONDS);throw new java.io.IOException("fixture");};
  try{f.ui(()->{PhoneStream.start(a,item,true);PhoneStream.Search search=PhoneStream.searches.get(a);assertNotNull(search);assertFamily(search.sheet);assertTrue(search.dialog.isShowing());back(search.dialog);});f.idle();f.ui(()->assertFalse(PhoneStream.searches.containsKey(a)));}finally{release.countDown();}
  List<JSONObject> options=new ArrayList<>();options.add(new JSONObject().put("name","Show.S01E02.1080p.WEB").put("url","https://noir-n3.invalid/a").put("resolution","1080p").put("size",2L*1073741824L).put("cached",true));options.add(new JSONObject().put("name","Show.S01E02.720p").put("url","https://noir-n3.invalid/b").put("resolution","720p").put("size",800L*1048576L).put("cached",false));
  Assume.assumeTrue(android.os.Build.VERSION.SDK_INT>=29);
  f.ui(()->{PhoneStream.pick(a,item,options);View root=sheetRoot();assertNotNull("Choose a stream opens as a sheet",root);assertNotNull(AuditPhoneFixture.find(root,v->v instanceof TextView&&Loc.t("Choose a stream").contentEquals(((TextView)v).getText())));List<View> rows=AuditPhoneFixture.all(root,v->v.getTag()!=null&&(String.valueOf(v.getTag()).contains("GB")||String.valueOf(v.getTag()).contains("720p")));assertEquals(2,rows.size());assertTrue(String.valueOf(rows.get(0).getContentDescription()).contains(Loc.t("Ready to play")));assertTrue(String.valueOf(rows.get(1).getContentDescription()).contains(Loc.t("Needs to be prepared first")));});
 }
 @Test public void remainingAlertDialogsUseTheNoirDialogTheme()throws Exception{
  f.ui(()->{int style=a.getResources().getIdentifier("NoirDialog","style",a.getPackageName());assertTrue(style!=0);android.util.TypedValue tv=new android.util.TypedValue();assertTrue(a.getTheme().resolveAttribute(android.R.attr.alertDialogTheme,tv,true));assertEquals(style,tv.resourceId);AlertDialog dialog=new AlertDialog.Builder(a).setTitle("Theme check").setMessage("x").setPositiveButton("OK",null).create();android.util.TypedValue accent=new android.util.TypedValue();assertTrue(dialog.getContext().getTheme().resolveAttribute(android.R.attr.colorAccent,accent,true));assertEquals(0xffe4c084,accent.data);});
 }
}
