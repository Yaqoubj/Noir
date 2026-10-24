package app.noir.mobile;

import android.app.Instrumentation;
import android.content.Intent;
import android.graphics.Bitmap;
import android.view.View;
import android.view.ViewGroup;
import android.widget.TextView;
import androidx.test.platform.app.InstrumentationRegistry;
import org.json.*;
import org.junit.Test;
import java.io.*;
import java.util.*;
import static org.junit.Assert.*;

/** Harbor parity on the phone: Arabic mirroring, Connections, a vertical History, the Nuvio link and the grouping cache. */
public final class HarborPhoneTest {
    final Instrumentation test=InstrumentationRegistry.getInstrumentation();
    boolean hasText(View view,String text){if(view instanceof TextView&&((TextView)view).getText().toString().contains(text))return true;if(view instanceof ViewGroup)for(int n=0;n<((ViewGroup)view).getChildCount();n++)if(hasText(((ViewGroup)view).getChildAt(n),text))return true;return false;}
    int horizontalRails(View view){int n=view instanceof android.widget.HorizontalScrollView?1:0;if(view instanceof ViewGroup)for(int i=0;i<((ViewGroup)view).getChildCount();i++)n+=horizontalRails(((ViewGroup)view).getChildAt(i));return n;}
    void capture(String name)throws Exception{
        test.waitForIdleSync();Thread.sleep(600);Bitmap bitmap=test.getUiAutomation().takeScreenshot();assertNotNull(bitmap);
        try(FileOutputStream out=new FileOutputStream(new File(test.getTargetContext().getFilesDir(),name))){bitmap.compress(Bitmap.CompressFormat.PNG,100,out);}bitmap.recycle();
    }
    @Test public void testStreamRanking()throws Exception{
        JSONObject response=new JSONObject("{\"streams\":["
            +"{\"name\":\"[TB] 2160p\",\"url\":\"https://aio.example/2\",\"behaviorHints\":{\"filename\":\"Show.S01E01.2160p.mkv\",\"videoSize\":9000000000},\"streamData\":{\"type\":\"debrid\",\"service\":{\"id\":\"torbox\",\"cached\":false}}},"
            +"{\"name\":\"⚡ Remux\",\"url\":\"https://aio.example/3\",\"behaviorHints\":{\"filename\":\"Show.S01E01.1080p.REMUX.mkv\",\"videoSize\":14000000000}},"
            +"{\"name\":\"[TB+] 1080p\",\"url\":\"https://aio.example/1\",\"behaviorHints\":{\"filename\":\"Show.S01E01.1080p.WEB.mkv\",\"videoSize\":2000000000},\"streamData\":{\"type\":\"debrid\",\"service\":{\"id\":\"torbox\",\"cached\":true}}},"
            +"{\"name\":\"Auth\",\"url\":\"https://proxy.example/x\",\"behaviorHints\":{\"proxyHeaders\":{\"request\":{\"Authorization\":\"Bearer x\"}}}}]}");
        JSONObject item=new JSONObject();DataStore.put(item,"movie",false);
        List<JSONObject> ranked=PhoneStream.rank(PhoneDownloadProvider.parse(response,item,new JSONObject()),"1080p",false);
        assertEquals("Streams needing other headers are dropped",3,ranked.size());
        assertEquals("A ready, lighter 1080p stream plays first","https://aio.example/1",ranked.get(0).optString("url"));
        assertEquals("A ready remux beats one that isn't ready","https://aio.example/3",ranked.get(1).optString("url"));
    }
    @Test public void testHarborParity()throws Exception{
        DataStore data=NoirApp.data;if(!data.paired())data.pair(TestRunner.arguments.getString("pairUri"));data.sync();
        MainActivity home=(MainActivity)test.startActivitySync(new Intent(test.getTargetContext(),MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
        try{
            // Grouping is reused until the library changes.
            List<List<JSONObject>> first=home.groups();long version=home.groupVersion;List<List<JSONObject>> again=home.groups();
            assertEquals("Grouping is cached between calls",version,home.groupVersion);assertSame("The same groups are reused",first.get(0),again.get(0));
            assertNotNull("Group lookup uses the index",home.group(first.get(0).get(0).optString("group")));
            test.runOnMainSync(()->{home.page="Home";home.detail="";home.render();});capture("harbor-home.png");
            assertTrue("Home shows your services",hasText(home.body,"Your services"));
            test.runOnMainSync(()->{home.page="Connections";home.render();});
            assertTrue("Connections lists the PC, Nuvio and Stremio",hasText(home.body,"Optional PC sync")&&hasText(home.body,"Nuvio")&&hasText(home.body,"Stremio"));capture("harbor-connections.png");
            test.runOnMainSync(()->{home.page="History";home.premium.historyView="Up next";home.render();});
            assertEquals("History's Up next has no sideways rails apart from the action chips",1,horizontalRails(home.body));
            assertTrue("History offers quick add and paste a list",hasText(home.body,"Quick add")&&hasText(home.body,"Paste a list"));capture("harbor-history.png");
            test.runOnMainSync(()->home.setLanguage("ar"));
            assertTrue("Arabic is on",Loc.arabic);assertEquals("Arabic mirrors the screen",View.LAYOUT_DIRECTION_RTL,home.getWindow().getDecorView().getLayoutDirection());
            assertTrue("History is translated",hasText(home.body,"التالي"));capture("harbor-history-ar.png");
            test.runOnMainSync(()->{home.page="Home";home.render();});capture("harbor-home-ar.png");
            test.runOnMainSync(()->{home.page="Connections";home.render();});assertTrue("Connections is translated",hasText(home.body,"الاتصالات"));capture("harbor-connections-ar.png");
        }finally{test.runOnMainSync(()->home.setLanguage("en"));}
        assertFalse("English again",Loc.arabic);
    }
}
