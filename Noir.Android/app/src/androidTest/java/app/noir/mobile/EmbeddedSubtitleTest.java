package app.noir.mobile;

import android.content.*;
import android.net.Uri;
import androidx.test.platform.app.InstrumentationRegistry;
import org.json.*;
import org.junit.Test;
import java.io.*;
import java.util.*;
import static org.junit.Assert.*;

public final class EmbeddedSubtitleTest {
    @Test public void languageAndPartialTracks(){
        assertEquals("en",EmbeddedSubtitles.language("eng","",false));
        assertEquals("ar",EmbeddedSubtitles.language(null,"Arabic",false));
        assertEquals("ar",EmbeddedSubtitles.language("und","Arabic",false));
        assertEquals("en",EmbeddedSubtitles.language("","[English] SDH",false));
        assertEquals("ar",EmbeddedSubtitles.language(" ARABIC ","",false));
        assertEquals("",EmbeddedSubtitles.language("fr","English",false));
        assertEquals("",EmbeddedSubtitles.language("eng","",true));
        for(String title:new String[]{"English [Forced]","Signs","Songs","Signs & Songs","Arabic Karaoke"})assertEquals(title,"",EmbeddedSubtitles.language("eng",title,false));
    }
    Context isolated(){Context real=InstrumentationRegistry.getInstrumentation().getTargetContext();File root=new File(real.getFilesDir(),"embedded-tests-"+UUID.randomUUID());root.mkdirs();return new ContextWrapper(real){@Override public Context getApplicationContext(){return this;}@Override public File getFilesDir(){return root;}@Override public File getExternalFilesDir(String type){return root;}};}
    File fixture(Context context,String asset)throws Exception{
        File file=new File(context.getFilesDir(),asset);try(InputStream input=InstrumentationRegistry.getInstrumentation().getContext().getAssets().open(asset);OutputStream output=new FileOutputStream(file)){input.transferTo(output);}return file;
    }
    @Test public void localVlcTracksAndAutomaticCopies()throws Exception{
        Context context=isolated();DataStore data=new DataStore(context);
        String id="embedded-fixture";JSONObject item=new JSONObject().put("id",id).put("title","Embedded fixture").put("subtitleFiles",new JSONArray().put(new JSONObject().put("name","en")).put(new JSONObject().put("name","ar")).put(new JSONObject().put("name","fr")));
        data.rememberPhoneItem(item);data.localFile(id,Uri.fromFile(fixture(context,"embedded-full-fixture.mkv")).toString());
        assertEquals(new HashSet<>(Arrays.asList("en","ar")),EmbeddedSubtitles.probe(data,id));
        JSONArray needed=EmbeddedSubtitles.neededFiles(item.optJSONArray("subtitleFiles"),new HashSet<>(Arrays.asList("en","ar")));assertEquals(1,needed.length());assertEquals("fr",needed.optJSONObject(0).optString("name"));
        DataStore before=NoirApp.data;try{NoirApp.data=data;DownloadStore downloads=new DownloadStore(context);downloads.enqueue(item);downloads.cacheSubtitles(id);String message=downloads.job(id).optString("subtitleMessage");assertTrue(message.contains("English built into the video"));assertTrue(message.contains("Arabic built into the video"));}finally{NoirApp.data=before;}
        data.localFile(id,Uri.fromFile(fixture(context,"embedded-partial-fixture.mkv")).toString());assertTrue(EmbeddedSubtitles.probe(data,id).isEmpty());
        data.localFile(id,Uri.fromFile(fixture(context,"phone-offline-fixture.mkv")).toString());assertTrue(EmbeddedSubtitles.probe(data,id).isEmpty());
        data.localFile(id,Uri.fromFile(new File(context.getFilesDir(),"missing.mkv")).toString());assertTrue(EmbeddedSubtitles.probe(data,id).isEmpty());
    }
}
