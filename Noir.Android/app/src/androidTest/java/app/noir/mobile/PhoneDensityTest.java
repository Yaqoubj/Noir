package app.noir.mobile;
import android.util.TypedValue;
import android.view.*;
import android.widget.*;
import org.json.*;
import org.junit.*;
import static org.junit.Assert.*;
/** Compact is the default phone scale; Comfortable (the pre-0.17 sizes) is one Settings choice away and survives a restart. */
public final class PhoneDensityTest {
 AuditPhoneFixture f;String saved;
 @Before public void setup()throws Exception{f=new AuditPhoneFixture();saved=PhoneDensity.prefs(f.real).getString("density","");BackgroundSync.device(f.data.context).edit().putBoolean("askedBattery",true).apply();f.ui(()->{PhoneDensity.prefs(f.real).edit().remove("density").commit();PhoneDensity.load(f.real);});f.data.rememberPhoneItem(new JSONObject().put("id","dens-1").put("group","dens-1").put("title","Density show").put("movie",true).put("inLibrary",true).put("year",2020));}
 @After public void cleanup()throws Exception{f.ui(()->{PhoneDensity.prefs(f.real).edit().putString("density",saved).commit();PhoneDensity.load(f.real);});f.close();}
 float sp(MainActivity a,float value){return TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_SP,value,a.getResources().getDisplayMetrics());}
 TextView heading(MainActivity a,String text){return (TextView)AuditPhoneFixture.find(a.body,v->v instanceof TextView&&Loc.t(text).contentEquals(((TextView)v).getText()));}
 void assertScale(MainActivity a,float headingSp,int buttonDp,int sideDp){a.page="Library";a.detail="";a.render();TextView h=heading(a,"Your library");assertNotNull("Library heading",h);assertEquals(sp(a,headingSp),h.getTextSize(),.6f);Button b=a.button("Measure",()->{},true);assertEquals(a.dp(buttonDp),b.getLayoutParams().height);assertEquals(a.dp(sideDp),a.body.getPaddingLeft());assertEquals(a.dp(sideDp),a.body.getPaddingRight());}
 View sheetRoot(){if(android.os.Build.VERSION.SDK_INT<29)return null;for(View root:android.view.inspector.WindowInspector.getGlobalWindowViews()){View found=AuditPhoneFixture.find(root,v->"noir-sheet".equals(v.getTag()));if(found!=null)return found;}return null;}
 @Test public void compactIsTheDefaultAndComfortableIsOneSettingsChoiceAway()throws Exception{
  Assume.assumeTrue(android.os.Build.VERSION.SDK_INT>=29);
  MainActivity a=f.launch(MainActivity.class);
  f.ui(()->{assertTrue(PhoneDensity.compact);assertScale(a,19,42,16);a.page="Settings";a.detail="";a.render();View size=AuditPhoneFixture.find(a.body,v->"Size".equals(String.valueOf(v.getContentDescription())));assertNotNull("Settings has a Size row under Make it yours",size);assertNotNull(AuditPhoneFixture.find(size,v->v instanceof TextView&&"Compact".contentEquals(((TextView)v).getText())));assertTrue(size.performClick());View sheet=sheetRoot();assertNotNull("Size opens a sheet",sheet);View comfortable=AuditPhoneFixture.find(sheet,v->String.valueOf(v.getContentDescription()).startsWith("Comfortable"));assertNotNull(comfortable);assertNotNull(AuditPhoneFixture.find(sheet,v->String.valueOf(v.getContentDescription()).startsWith("Compact")&&String.valueOf(v.getContentDescription()).contains(Loc.t("Selected"))));assertTrue(comfortable.performClick());assertFalse(PhoneDensity.compact);});
  f.idle();
  f.ui(()->{assertScale(a,23,48,22);PhoneDensity.compact=true;PhoneDensity.load(f.real);assertFalse("The choice survives load()",PhoneDensity.compact);});
 }
 @Test public void arabicStaysRightToLeftInBothSizes()throws Exception{
  MainActivity a=f.launch(MainActivity.class);
  try{for(boolean compact:new boolean[]{true,false})f.ui(()->{PhoneDensity.set(f.real,compact);a.setLanguage("ar");assertTrue(Loc.arabic);assertEquals(View.LAYOUT_DIRECTION_RTL,a.getWindow().getDecorView().getLayoutDirection());a.page="Library";a.detail="";a.render();assertNotNull(heading(a,"Your library"));assertEquals(a.dp(PhoneDensity.pageSide()),a.body.getPaddingLeft());a.setLanguage("en");});}finally{f.ui(()->a.setLanguage("en"));}
 }
}
