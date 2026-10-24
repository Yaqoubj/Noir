package app.noir.mobile;

import android.app.Instrumentation;
import android.content.Intent;
import android.view.View;
import android.view.ViewGroup;
import android.widget.TextView;
import androidx.test.platform.app.InstrumentationRegistry;
import org.json.*;
import org.junit.Test;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.util.zip.*;
import static org.junit.Assert.*;

/** The phone's history tools: the PC's file formats, undoable imports, sources, backups, Stremio and Nuvio rows. */
public final class HistoryIoTest {
    final Instrumentation test=InstrumentationRegistry.getInstrumentation();
    static byte[] zip(String[][] entries)throws Exception{ByteArrayOutputStream out=new ByteArrayOutputStream();try(ZipOutputStream z=new ZipOutputStream(out)){for(String[] e:entries){z.putNextEntry(new ZipEntry(e[0]));z.write(e[1].getBytes(StandardCharsets.UTF_8));z.closeEntry();}}return out.toByteArray();}
    boolean hasText(View view,String text){if(view instanceof TextView&&((TextView)view).getText().toString().contains(text))return true;if(view instanceof ViewGroup)for(int n=0;n<((ViewGroup)view).getChildCount();n++)if(hasText(((ViewGroup)view).getChildAt(n),text))return true;return false;}

    @Test public void testFormatsMatchThePc()throws Exception{
        PhoneHistory.Parsed netflix=PhoneHistory.read("NetflixViewingHistory.csv","Title,Date\n\"Stranger Things: Stranger Things 2: Chapter One: MADMAX\",13/10/2024\nArrival,03/10/2024\n".getBytes(StandardCharsets.UTF_8));
        assertEquals("Netflix",netflix.service);PhoneHistory.Row st=netflix.rows.get(0);
        assertEquals("Stranger Things",st.title);assertEquals(2,st.season);assertEquals("Chapter One: MADMAX",st.episodeTitle);assertEquals(Boolean.FALSE,st.movie);
        assertEquals("Day-first dates across the file",java.time.LocalDate.of(2024,10,3).atStartOfDay(java.time.ZoneOffset.UTC).toInstant().toEpochMilli(),netflix.rows.get(1).at);
        PhoneHistory.Parsed lb=PhoneHistory.read("letterboxd.zip",zip(new String[][]{{"diary.csv","Date,Name,Year,Letterboxd URI,Rating,Rewatch,Watched Date\n2024-01-02,Heat,1995,x,,No,2024-01-01\n"},{"ratings.csv","Date,Name,Year,Letterboxd URI,Rating\n2024-01-02,Heat,1995,x,4.5\n2024-01-02,Alien,1979,y,5\n"}}));
        assertEquals("Letterboxd",lb.service);assertEquals("Diary and rated films merge",2,lb.rows.size());assertEquals("Diary picks up its rating",5,lb.rows.get(0).rating);
        PhoneHistory.Parsed trakt=PhoneHistory.read("history.json","[{\"watched_at\":\"2024-02-01T20:00:00.000Z\",\"episode\":{\"season\":1,\"number\":2,\"title\":\"Two\"},\"show\":{\"title\":\"Lost\",\"year\":2004,\"ids\":{\"imdb\":\"tt0411008\"}}}]".getBytes(StandardCharsets.UTF_8));
        assertEquals("Trakt",trakt.service);assertEquals("tt0411008",trakt.rows.get(0).imdb);assertEquals(2,trakt.rows.get(0).episode);
        PhoneHistory.Parsed noir=PhoneHistory.read("noir.json","[{\"type\":\"movie\",\"title\":\"Heat\",\"year\":1995,\"imdb_id\":\"tt0113277\",\"watched_at\":\"2024-03-01T20:00:00Z\",\"date_unknown\":false,\"approximate\":true,\"source\":\"Playback\",\"rating\":4,\"logged_at\":\"2024-03-01T21:00:00Z\",\"note\":\"Diner\",\"show_rating\":0}]".getBytes(StandardCharsets.UTF_8));
        PhoneHistory.Row heat=noir.rows.get(0);assertEquals("Noir",noir.service);assertEquals("Playback",heat.source);assertEquals("Diner",heat.note);assertTrue(heat.approximate);assertTrue(heat.loggedAt>0);
        List<PhoneHistory.Row> list=PhoneHistory.list("- Lost S2E10\nInception (2010)\nmovie: Dune\nThe Office S4");
        assertEquals(4,list.size());assertEquals(2,list.get(0).season);assertEquals(10,list.get(0).episode);assertEquals(2010,list.get(1).year);assertEquals(Boolean.TRUE,list.get(2).movie);assertEquals(4,list.get(3).season);
        try{PhoneHistory.read("bad.csv","nothing here".getBytes(StandardCharsets.UTF_8));fail("Unknown files are refused");}catch(IOException expected){}
        assertNotNull("A PC backup is recognised",PhoneHistory.read("Noir.noirbackup",zip(new String[][]{{"library.json","{}"}})).backup);
    }
    @Test public void testStremioBitfieldAndNuvioRows()throws Exception{
        List<String> videos=Arrays.asList("tt1:1:1","tt1:1:2","tt1:1:3");byte[] bits={0b101};Deflater d=new Deflater();d.setInput(bits);d.finish();byte[] buf=new byte[64];int n=d.deflate(buf);
        String encoded="tt1:1:3:3:"+android.util.Base64.encodeToString(Arrays.copyOf(buf,n),android.util.Base64.NO_WRAP);
        assertEquals("Stremio watched episodes decode",Arrays.asList("tt1:1:1","tt1:1:3"),PhoneHistory.decodeWatched(encoded,videos));
        assertEquals("Compact Nuvio timestamps normalise",java.time.Instant.parse("2024-01-31T23:59:59Z").toEpochMilli(),PhoneHistoryIo.normalize(20240131235959L));
        JSONObject w=new JSONObject().put("content_id","tt0000500").put("content_type","series").put("title","Show").put("season",1).put("episode",3).put("watched_at",1714600000000L);
        PhoneHistory.Row r=PhoneHistoryIo.nuvioRow(w);assertEquals(Boolean.FALSE,r.movie);assertEquals(3,r.episode);assertEquals("tt0000500",r.imdb);
    }
    @Test public void testNoirRewatchesAndLookupFailureFlag()throws Exception{
        long at=java.time.Instant.parse("2024-01-01T20:00:00Z").toEpochMilli();JSONObject item=new JSONObject().put("viewings",new JSONArray().put(new JSONObject().put("watchedAt",PhoneHistory.iso(at)).put("source","Playback")));
        assertTrue(PhoneHistoryIo.already(item,at+3600000,false));
        assertFalse("Noir keeps a second viewing an hour later",PhoneHistoryIo.alreadyExact(item,at+3600000,false,"Playback"));
        assertTrue("The same Noir viewing is idempotent",PhoneHistoryIo.alreadyExact(item,at,false,"Playback"));
        assertFalse("Exact imports also compare sources",PhoneHistoryIo.alreadyExact(item,at,false,"Imported from Nuvio"));
        PhoneHistoryIo.Plan plan=new PhoneHistoryIo.Plan();plan.unmatched.add("An ordinary unmatched title");assertFalse(PhoneHistoryIo.lookupFailed(plan));plan.lookupFailed=true;assertTrue(PhoneHistoryIo.lookupFailed(plan));
    }
    @Test public void testImportUndoSourcesAndBackup()throws Exception{
        DataStore data=NoirApp.data;PhoneHistoryIo io=new PhoneHistoryIo(data,new DiscoveryClient(test.getTargetContext()));String tag=String.valueOf(System.nanoTime()%1000000);
        JSONObject show=new JSONObject().put("id","tt99"+tag).put("noirType","series").put("name","Phone Show "+tag).put("releaseInfo","2020")
            .put("videos",new JSONArray().put(new JSONObject().put("season",1).put("episode",1).put("name","Pilot").put("released","2020-01-01T00:00:00Z")).put(new JSONObject().put("season",1).put("episode",2).put("name","Two").put("released","2020-01-08T00:00:00Z")));
        PhoneHistoryIo.Plan plan=new PhoneHistoryIo.Plan();plan.service="Test "+tag;PhoneHistoryIo.Title t=new PhoneHistoryIo.Title();t.details=show;
        PhoneHistory.Row row=new PhoneHistory.Row(plan.service,"Phone Show "+tag,2020,false,"","",1,1,"",1700000000000L,4);
        t.views.add(new PhoneHistoryIo.View(1,1,1700000000000L,4,row));t.views.add(new PhoneHistoryIo.View(1,2,0,0,null));plan.titles.add(t);
        assertEquals("Both episodes are added",2,io.apply(plan,"When they aired",0));String batch=io.lastBatch;
        assertEquals("Importing again adds nothing",0,io.apply(plan,"When they aired",0));
        List<JSONObject> mine=new ArrayList<>();for(JSONObject i:DataStore.objects(data.items()))if(i.optString("imdb").equals("tt99"+tag))mine.add(i);
        int watched=0;for(JSONObject i:mine)for(JSONObject v:DataStore.objects(i.optJSONArray("viewings"))){assertEquals("Imported from Test "+tag,v.optString("source"));watched++;}
        assertEquals(2,watched);
        int[] counts=io.sources().get("Imported from Test "+tag);assertNotNull(counts);assertEquals(2,counts[0]);
        ByteArrayOutputStream backup=new ByteArrayOutputStream();io.writeBackup(backup);PhoneHistoryIo.Backup b=PhoneHistoryIo.readBackup(backup.toByteArray());
        assertNotNull("Phone backups carry the phone state",b.phone);assertFalse("…but never the pairing",b.phone.has("token"));
        assertTrue("Backups carry the imported history with its source",b.rows.stream().anyMatch(x->x.imdb.equals("tt99"+tag)&&x.source.equals("Imported from Test "+tag)));
        assertEquals("Undo removes what that import added",2,io.undo(batch));
        for(JSONObject i:DataStore.objects(data.items()))if(i.optString("imdb").equals("tt99"+tag))assertEquals(0,DataStore.objects(i.optJSONArray("viewings")).size());
        assertNull(io.sources().get("Imported from Test "+tag));
        io.apply(plan,"Don't remember",0);assertEquals("Remove source clears it",2,io.removeSource("Imported from Test "+tag));assertNull(io.sources().get("Imported from Test "+tag));
    }
    @Test public void testToolsScreenOpens()throws Exception{
        HistoryToolsActivity tools=(HistoryToolsActivity)test.startActivitySync(new Intent(test.getTargetContext(),HistoryToolsActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
        test.waitForIdleSync();View root=tools.getWindow().getDecorView();
        for(String s:new String[]{"Import from another app","Paste a list","Nuvio","Stremio","Export","Back up & restore"})assertTrue(s+" is offered",hasText(root,s));
        Thread.sleep(400);android.graphics.Bitmap shot=test.getUiAutomation().takeScreenshot();try(FileOutputStream out=new FileOutputStream(new File(test.getTargetContext().getFilesDir(),"history-tools.png"))){shot.compress(android.graphics.Bitmap.CompressFormat.PNG,100,out);}
        tools.finish();
    }
}
