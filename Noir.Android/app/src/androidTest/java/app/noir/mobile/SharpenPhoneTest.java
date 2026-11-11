package app.noir.mobile;

import android.content.*;
import androidx.test.platform.app.InstrumentationRegistry;
import org.json.*;
import org.junit.Test;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.util.*;
import static org.junit.Assert.*;

/** Sharpen round, phone side: torrent trackers and stall, interrupted downloads, subtitle source line, replace and preferred uploader. No network, no PC. */
public final class SharpenPhoneTest {
    static final String HASH="1234567890123456789012345678901234567890";
    static File root;
    static Context context()throws Exception{
        final Context base=InstrumentationRegistry.getInstrumentation().getTargetContext();final File dir=new File(base.getFilesDir(),"sharpen-"+UUID.randomUUID());dir.mkdirs();root=dir;
        return new ContextWrapper(base){@Override public Context getApplicationContext(){return this;}@Override public File getFilesDir(){return dir;}@Override public File getCacheDir(){File f=new File(dir,"cache");f.mkdirs();return f;}@Override public File getExternalFilesDir(String type){File f=new File(dir,"external");f.mkdirs();return f;}@Override public File[] getExternalFilesDirs(String type){return new File[]{getExternalFilesDir(type),new File(dir,"sd")};}@Override public SharedPreferences getSharedPreferences(String name,int mode){return base.getSharedPreferences(dir.getName()+"-"+name,mode);}};
    }

    /* F1: trackers stay on the job (sealed choice) and a default list covers a source with none. */
    @Test public void trackersComeFromTheJobSurviveRestartAndDefaultsCoverNone()throws Exception{
        boolean before=PhoneTorrent.loopbackTests;PhoneTorrent.loopbackTests=false;
        try{
            JSONObject choice=new JSONObject().put("hash",HASH).put("trackers",new JSONArray().put("udp://tracker.test:1337/announce"));
            Context c=context();PhoneDownloadSettings settings=new PhoneDownloadSettings(c);
            JSONObject restored=new JSONObject(settings.unseal(settings.seal(choice.toString())));
            assertEquals(Collections.singletonList("udp://tracker.test:1337/announce"),PhoneTorrent.trackers(restored));
            assertTrue(PhoneTorrent.magnet(restored).contains("&tr="+PhoneDownloadProvider.encode("udp://tracker.test:1337/announce")));
            List<String> fallback=PhoneTorrent.trackers(new JSONObject().put("hash",HASH));assertEquals(Arrays.asList(PhoneTorrent.DEFAULT_TRACKERS),fallback);
            assertTrue("magnet without source trackers carries the built-in list",PhoneTorrent.magnet(new JSONObject().put("hash",HASH)).contains("&tr="));
            PhoneTorrent.loopbackTests=true;assertTrue("loopback tests never reach public trackers",PhoneTorrent.trackers(new JSONObject().put("hash",HASH)).isEmpty());
        }finally{PhoneTorrent.loopbackTests=before;}
    }

    /* F2: connected seeds that send nothing are not progress. */
    @Test public void stallMeansNoByteProgressWhateverTheSeedCount()throws Exception{
        assertFalse(PhoneTorrent.progressed(8,0,1000,1000));
        assertTrue(PhoneTorrent.progressed(0,0,1001,1000));
        assertTrue(PhoneTorrent.progressed(0,4096,1000,1000));
        assertTrue(PhoneTorrent.progressed(0,0,10,-1));
    }

    /* F3: jobs the app was cut off in come back queued; user pauses stay paused. */
    @Test public void interruptedDownloadsContinueAndUserPausesStay()throws Exception{
        Context c=context();
        JSONArray jobs=new JSONArray();
        jobs.put(new JSONObject().put("id","cut").put("status","downloading"));
        jobs.put(new JSONObject().put("id","user").put("status","paused"));
        jobs.put(new JSONObject().put("id","flagged").put("status","paused").put("interrupted",true));
        jobs.put(new JSONObject().put("id","ready").put("status","downloading").put("readyManaged",true));
        jobs.put(new JSONObject().put("id","done").put("status","complete"));
        Files.write(new File(c.getFilesDir(),"downloads.json").toPath(),jobs.toString().getBytes(StandardCharsets.UTF_8));
        DownloadStore store=new DownloadStore(c);
        assertEquals("queued",store.job("cut").optString("status"));
        assertTrue("this start put interrupted downloads back in the queue",store.resumedOnStart);
        assertEquals("paused",store.job("user").optString("status"));
        assertEquals("queued",store.job("flagged").optString("status"));
        assertFalse(store.job("flagged").has("interrupted"));
        assertEquals("paused",store.job("ready").optString("status"));
        assertEquals("complete",store.job("done").optString("status"));
        /* a user pause clears any interrupted mark, so it survives the next start */
        store.pause("cut");assertEquals("paused",store.job("cut").optString("status"));
        store.pauseInterrupted("flagged");assertTrue(store.job("flagged").optBoolean("interrupted"));
        store.pauseInterrupted("cut");assertFalse(store.job("cut").optBoolean("interrupted"));
        DownloadStore again=new DownloadStore(c);
        assertTrue("the job cut off by the service comes back queued again",again.resumedOnStart);
        assertEquals("paused",again.job("cut").optString("status"));
        assertEquals("queued",again.job("flagged").optString("status"));
    }

    /* F8: uploader read from each site's field, ordering only. */
    @Test public void uploadersAreReadFromEachSiteAndOnlyOrderTheList()throws Exception{
        SubtitleSourcesTest.Net os=new SubtitleSourcesTest.Net(r->SubtitleSourcesTest.ok("{\"data\":[{\"attributes\":{\"language\":\"en\",\"release\":\"R\",\"uploader\":{\"name\":\"Bob\"},\"files\":[{\"file_id\":7,\"file_name\":\"a.srt\"}]}}]}"));
        List<JSONObject> found=SubtitleSourcesTest.sources(os).search(SubtitleSourcesTest.sel("opensubtitles"),SubtitleSourcesTest.item("tt0111161",true,0,0),"","en","");
        assertEquals("Bob",found.get(0).optString("uploader"));
        SubtitleSourcesTest.Net dl=new SubtitleSourcesTest.Net(r->SubtitleSourcesTest.ok("{\"status\":true,\"subtitles\":[{\"name\":\"x\",\"url\":\"/subtitle/1.zip\",\"lang\":\"EN\",\"author\":\"Ann\",\"release_name\":\"r\"}]}"));
        found=SubtitleSourcesTest.sources(dl).search(SubtitleSourcesTest.sel("subdl"),SubtitleSourcesTest.item("tt0111161",true,0,0),"","en","");
        assertEquals("Ann",found.get(0).optString("uploader"));
        SubtitleSourcesTest.Net ss=new SubtitleSourcesTest.Net(r->r.url.contains("movies/search")?SubtitleSourcesTest.ok("{\"data\":[{\"imdbId\":\"tt0111161\",\"movieId\":\"5\"}]}"):SubtitleSourcesTest.ok("{\"data\":[{\"subtitleId\":9,\"movieId\":\"5\",\"language\":\"english\",\"releaseInfo\":[\"r\"],\"contributors\":[{\"displayname\":\"Zed\"}]}]}"));
        found=SubtitleSourcesTest.sources(ss).search(SubtitleSourcesTest.sel("subsource"),SubtitleSourcesTest.item("tt0111161",true,0,0),"","en","");
        assertEquals("Zed",found.get(0).optString("uploader"));
        assertEquals("A",SubtitleSources.subSourceUploader(new JSONObject().put("uploader","A")));
        assertEquals("B",SubtitleSources.subSourceUploader(new JSONObject().put("uploaderName","B")));
        assertEquals("C",SubtitleSources.subSourceUploader(new JSONObject().put("uploadedBy","C")));
        assertEquals("D",SubtitleSources.subSourceUploader(new JSONObject().put("contributors",new JSONArray().put(new JSONObject().put("name","D")))));
        assertEquals("",SubtitleSources.subSourceUploader(new JSONObject()));
        List<JSONObject> list=new ArrayList<>();for(String name:new String[]{"x","Fav","","y","fav"})list.add(new JSONObject().put("uploader",name).put("id",name));
        List<JSONObject> ordered=SubtitleClient.preferUploader(list,"FAV");
        assertEquals(5,ordered.size());assertEquals("Fav",ordered.get(0).optString("id"));assertEquals("fav",ordered.get(1).optString("id"));assertEquals("x",ordered.get(2).optString("id"));
        assertEquals(list,SubtitleClient.preferUploader(list,""));
    }

    /* F5: the result line names where the subtitle came from. */
    @Test public void resultLineNamesTheSourceAndTheUploader()throws Exception{
        boolean arabic=Loc.arabic;Loc.arabic=false;
        try{
            JSONObject site=new JSONObject().put("language","en").put("origin","site").put("sourceName","SubDL").put("uploader","Ann");
            assertEquals("English saved from SubDL · by Ann · timing checked",SubtitleClient.resultLine(site,"timing checked"));
            JSONObject free=new JSONObject().put("language","en").put("origin","free");
            assertEquals("English saved from the free default · timing unchanged",SubtitleClient.resultLine(free,"timing unchanged"));
            JSONObject fell=new JSONObject().put("language","ar").put("origin","free").put("fellBackFrom","SubDL");
            assertEquals("SubDL had no Arabic · saved from the free default",SubtitleClient.originLine(fell));
            assertEquals("English saved from the release",SubtitleClient.originLine(new JSONObject().put("language","en").put("origin","release")));
            Loc.arabic=true;assertNotEquals("English saved from SubDL",SubtitleClient.originLine(site.put("uploader","")));
        }finally{Loc.arabic=arabic;}
    }

    /* F7: Replace moves the saved file to subtitle-originals first and drops it from the list. */
    @Test public void replaceMovesTheSavedSubtitleToTheOriginalsFolder()throws Exception{
        Context c=context();DataStore data=new DataStore(c);
        try{
            JSONObject item=new JSONObject().put("id","m1").put("title","Movie").put("movie",true).put("imdb","tt0111161");
            SubtitleClient client=new SubtitleClient(data,item,(url,max)->new byte[0]);
            JSONObject sub=new JSONObject().put("sourceKey","k1").put("language","en").put("name","English · a").put("extension",".srt");
            client.save(sub,SubtitleSourcesTest.srt("old"));File old=data.subtitle(sub);assertTrue(old.isFile());
            JSONObject other=new JSONObject().put("sourceKey","k2").put("language","ar").put("name","Arabic · a").put("extension",".srt");client.save(other,SubtitleSourcesTest.srt("arabic"));
            assertEquals(2,data.phoneSubtitles("m1").length());
            assertEquals(1,client.replaceSaved("en"));
            assertFalse("the old sidecar left the subtitle folder",old.exists());
            File[] backups=new File(data.context.getFilesDir(),"subtitle-originals").listFiles();assertNotNull(backups);assertEquals(1,backups.length);
            assertEquals(SubtitleSourcesTest.srt("old"),new String(Files.readAllBytes(backups[0].toPath()),StandardCharsets.UTF_8));
            assertEquals(1,data.phoneSubtitles("m1").length());assertEquals("ar",data.phoneSubtitles("m1").getJSONObject(0).optString("language"));
        }finally{DataStore.flushWrites();data.network.shutdownNow();data.images.shutdownNow();PhoneTorrent.erase(root);}
    }

    /* F4: six tries with growing waits; Last-Modified stands in when there is no ETag. */
    @Test public void directDownloadsRetrySixTimesAndFallBackToLastModified()throws Exception{
        assertArrayEquals(new long[]{5000,15000,30000,60000,120000,120000},DownloadStore.retryWaitsMs);
        assertEquals("\"abc\"",DownloadStore.validator("\"abc\"","Tue, 10 Nov 2026 10:00:00 GMT"));
        assertEquals("Tue, 10 Nov 2026 10:00:00 GMT",DownloadStore.validator(null,"Tue, 10 Nov 2026 10:00:00 GMT"));
        assertEquals("Tue, 10 Nov 2026 10:00:00 GMT",DownloadStore.validator("W/\"weak\"","Tue, 10 Nov 2026 10:00:00 GMT"));
        assertEquals("",DownloadStore.validator(null,null));
        Context c=context();DownloadStore store=new DownloadStore(c);long before=System.currentTimeMillis();
        store.waitOut("missing-job",60000);/* a job that no longer exists counts as paused: the wait ends at once */
        assertTrue(System.currentTimeMillis()-before<2000);
    }

    /* F6: a far-off refusal tries one more candidate; close or accepted keeps; both far keeps the first. */
    @Test public void farOffTimingTriesOneMoreCandidateAndWarnsWhenBothAreOff()throws Exception{
        assertTrue(SubtitleClient.farOff(28.9,1.04));assertTrue(SubtitleClient.farOff(2,1.01));assertFalse(SubtitleClient.farOff(3.2,1.0));
        final List<String> discarded=new ArrayList<>();final List<String> downloaded=new ArrayList<>();
        final Map<String,SubtitleClient.Refused> verdict=new HashMap<>();
        SubtitleClient.Steps steps=new SubtitleClient.Steps(){
            public JSONObject download(JSONObject c)throws Exception{downloaded.add(c.optString("ref"));return new JSONObject().put("id",c.optString("ref")).put("language","en");}
            public JSONObject sync(JSONObject sub)throws Exception{SubtitleClient.Refused r=verdict.get(sub.optString("id"));if(r!=null)throw r;return new JSONObject().put("id",sub.optString("id")+"-fixed").put("aligned",true);}
            public void discard(JSONObject sub){discarded.add(sub.optString("id"));}
            public boolean checkable(){return true;}
        };
        JSONObject a=new JSONObject().put("ref","a").put("language","en").put("provider","x"),b=new JSONObject().put("ref","b").put("language","en").put("provider","x"),ar=new JSONObject().put("ref","c").put("language","ar").put("provider","x");
        List<JSONObject> all=Arrays.asList(a,ar,b);
        verdict.put("a",new SubtitleClient.Refused("no",true,28.9,1.04));verdict.put("b",new SubtitleClient.Refused("no",true,30,1.0));
        SubtitleClient.Checked both=SubtitleClient.decide(steps,a,all);
        assertEquals(Arrays.asList("a","b"),downloaded);assertEquals("a",both.sub.optString("id"));assertEquals(SubtitleClient.MISMATCH,both.tail);assertEquals(Collections.singletonList("b"),discarded);
        downloaded.clear();discarded.clear();verdict.remove("b");
        SubtitleClient.Checked second=SubtitleClient.decide(steps,a,all);
        assertEquals("b-fixed",second.sub.optString("id"));assertEquals(Collections.singletonList("a"),discarded);assertEquals(2,downloaded.size());
        downloaded.clear();discarded.clear();verdict.put("a",new SubtitleClient.Refused("unsure",true,4,1.0));
        SubtitleClient.Checked close=SubtitleClient.decide(steps,a,all);
        assertEquals(1,downloaded.size());assertEquals("a",close.sub.optString("id"));assertEquals("unsure",close.tail);
        downloaded.clear();verdict.put("a",new SubtitleClient.Refused("no raw numbers",false,0,0));
        assertEquals(1,SubtitleClient.decide(steps,a,all).sub==null?0:1);assertEquals(1,downloaded.size());
    }
    @Test public void letterboxdExportWritesHalfStarsAndTheLocalDate(){
        assertEquals("3.5",PhoneHistory.letterboxdRating(7));assertEquals("5.0",PhoneHistory.letterboxdRating(10));assertEquals("",PhoneHistory.letterboxdRating(0));
        long lateUtc=java.time.Instant.parse("2024-03-04T20:00:00Z").toEpochMilli();
        assertEquals("2024-03-05",PhoneHistory.letterboxdDate(lateUtc,java.time.ZoneOffset.ofHours(9)));assertEquals("2024-03-04",PhoneHistory.letterboxdDate(lateUtc,java.time.ZoneOffset.UTC));
    }
}
