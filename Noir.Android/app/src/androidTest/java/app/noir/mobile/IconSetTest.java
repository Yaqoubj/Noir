package app.noir.mobile;

import android.app.*;
import android.content.*;
import android.view.*;
import android.widget.*;
import androidx.test.platform.app.InstrumentationRegistry;
import org.junit.*;
import java.util.*;
import static org.junit.Assert.*;

/** U18 / P5: the phone bottom bar, the More rows and the TV rail draw vector icons, never text characters. */
public final class IconSetTest {
 static final String[] OLD=("⌂ ▦ ◷ ⌕ ⋯ ✓ ◉ ▶ ≡ ◫ ⌁ ↓ ♡ ⇄ ع ▣ ▤ ✦ ⓘ 🔔 ＋ ⚙").split(" ");
 AuditPhoneFixture f;
 @After public void cleanup()throws Exception{if(f!=null)f.close();}
 static void texts(View v,List<String> out){if(v instanceof TextView)out.add(((TextView)v).getText().toString());if(v instanceof ViewGroup)for(int n=0;n<((ViewGroup)v).getChildCount();n++)texts(((ViewGroup)v).getChildAt(n),out);}
 static void images(View v,List<ImageView> out){if(v instanceof ImageView)out.add((ImageView)v);if(v instanceof ViewGroup)for(int n=0;n<((ViewGroup)v).getChildCount();n++)images(((ViewGroup)v).getChildAt(n),out);}
 static void noOldGlyph(String where,View root){List<String> all=new ArrayList<>();texts(root,all);for(String text:all)for(String glyph:OLD)assertFalse(where+" still has the text icon "+glyph+" in \""+text+"\"",text.trim().equals(glyph)||text.startsWith(glyph+"\n")||text.startsWith(glyph+" "));}
 static void itemHasIcon(String where,View item){List<ImageView> pics=new ArrayList<>();images(item,pics);assertFalse(where+" item has an ImageView",pics.isEmpty());assertNotNull(where+" icon has a drawable",pics.get(0).getDrawable());assertTrue(where+" item has a contentDescription",item.getContentDescription()!=null&&item.getContentDescription().length()>0);}
 void bar(MainActivity a,int count,String where){f.ui(()->{assertEquals(count,a.nav.getChildCount());noOldGlyph(where,a.nav);for(int n=0;n<a.nav.getChildCount();n++)itemHasIcon(where+" #"+n,a.nav.getChildAt(n));});}
 @Test public void phoneBottomBarUsesDrawnIcons()throws Exception{f=new AuditPhoneFixture();MainActivity a=f.launch(MainActivity.class);bar(a,5,"bottom bar");f.data.phoneSetting("historyOnly",true);f.ui(a::render);bar(a,4,"tracker bottom bar");}
 @Test public void moreRowsUseDrawnIcons()throws Exception{
  f=new AuditPhoneFixture();MainActivity a=f.launch(MainActivity.class);
  f.ui(()->{a.page="Settings";a.detail="";a.render();noOldGlyph("More page",a.body);int rows=0;for(int n=0;n<a.body.getChildCount();n++){View row=a.body.getChildAt(n);if(!(row instanceof LinearLayout)||!row.hasOnClickListeners()||row.getContentDescription()==null)continue;List<ImageView> pics=new ArrayList<>();images(row,pics);if(pics.isEmpty())continue;rows++;itemHasIcon("More row "+row.getContentDescription(),row);}assertTrue("More page shows icon rows, found "+rows,rows>=8);});
 }
 @Test public void tvRailUsesDrawnIcons()throws Exception{
  android.app.Instrumentation test=InstrumentationRegistry.getInstrumentation();Assume.assumeTrue("TV-only test",TvUi.isTv(test.getTargetContext()));
  TvActivity a=(TvActivity)test.startActivitySync(new Intent(test.getTargetContext(),TvActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK|Intent.FLAG_ACTIVITY_CLEAR_TASK));
  try{test.runOnMainSync(()->{assertEquals(5,a.nav.size());noOldGlyph("TV rail",a.rail);for(View item:a.nav){itemHasIcon("TV rail item",item);assertTrue("rail item stays a D-pad target",item.isFocusable());}});}finally{test.runOnMainSync(a::finish);}
 }
}
