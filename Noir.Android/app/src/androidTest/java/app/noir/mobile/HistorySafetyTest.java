package app.noir.mobile;

import android.app.Instrumentation;
import android.content.*;
import androidx.test.platform.app.InstrumentationRegistry;
import org.json.*;
import org.junit.Test;
import java.io.*;
import java.nio.file.Files;
import java.util.*;
import java.util.concurrent.TimeUnit;
import java.util.zip.*;
import static org.junit.Assert.*;

public final class HistorySafetyTest {
    static final String BACKUP_FAILED="Couldn't save the safety backup. History was not changed. Check storage and try again.";
    final Instrumentation test=InstrumentationRegistry.getInstrumentation();
    final class Case implements AutoCloseable {
        final File root;final Context context;final DataStore data;final PhoneHistoryIo io;
        Case(){Context app=test.getTargetContext();root=new File(app.getFilesDir(),"work/sharpen-round/w1phone/"+UUID.randomUUID());assertTrue(root.mkdirs());context=new ContextWrapper(app){
            @Override public Context getApplicationContext(){return this;}
            @Override public File getFilesDir(){return root;}
            @Override public File getExternalFilesDir(String type){return new File(root,"external");}
            @Override public SharedPreferences getSharedPreferences(String name,int mode){return super.getSharedPreferences(root.getName()+name,mode);}
        };data=new DataStore(context);io=new PhoneHistoryIo(data,new DiscoveryClient(context));}
        public void close(){data.network.shutdown();data.images.shutdown();try{data.network.awaitTermination(10,TimeUnit.SECONDS);}catch(InterruptedException e){Thread.currentThread().interrupt();}DataStore.flushWrites();delete(root);}
    }
    static void delete(File f){File[] children=f.listFiles();if(children!=null)for(File child:children)delete(child);assertTrue("Delete test file "+f,!f.exists()||f.delete());}
    PhoneHistoryIo.Plan plan(PhoneHistory.Row row)throws Exception{PhoneHistoryIo.Plan p=new PhoneHistoryIo.Plan();p.service=row.service;PhoneHistoryIo.Title t=new PhoneHistoryIo.Title();t.details=new JSONObject().put("id","tt1234567").put("noirType","movie").put("name","Safety movie").put("releaseInfo","2020");t.views.add(new PhoneHistoryIo.View(0,0,row.at,row.rating,row));p.titles.add(t);return p;}
    PhoneHistory.Row row(int rating,String note){PhoneHistory.Row r=new PhoneHistory.Row("Noir","Safety movie",2020,true,"tt1234567","",0,0,"",1700000000000L,rating);r.note=note;return r;}
    JSONObject item(Case c){return c.data.items().optJSONObject(0);}
    // The same backup-then-mutation sequence as both destructive tools, using their existing backup API.
    void destructive(Case c,String kind)throws Exception{HistoryToolsActivity.saveAutoBackup(c.context,c.data,kind);if(kind.equals("before-restore"))c.data.replaceState(new JSONObject());else c.io.removeSource("Imported from Noir");}

    @Test public void undoKeepsLaterRatingAndNoteButClearsUntouchedImport()throws Exception{
        try(Case c=new Case()){
            c.io.apply(plan(row(4,"Imported note")),"Don't remember",0);String batch=c.io.lastBatch,id=item(c).optString("id");
            c.data.review10(id,7,"My later note");int pending=c.data.pendingCount("review");c.io.undo(batch);
            assertEquals(7,DataStore.rating10(c.data.item(id)));assertEquals("My later note",c.data.item(id).optJSONObject("review").optString("note"));assertEquals("Undo must not queue a review deletion",pending,c.data.pendingCount("review"));
            c.data.review10(id,0,"");c.io.apply(plan(row(4,"Imported note")),"Don't remember",0);c.io.undo(c.io.lastBatch);
            assertEquals(0,DataStore.rating10(c.data.item(id)));assertEquals("",c.data.item(id).optJSONObject("review").optString("note"));
            c.io.apply(plan(row(0,"Note only")),"Don't remember",0);c.io.undo(c.io.lastBatch);assertEquals("Note-only imports are undoable","",c.data.item(id).optJSONObject("review").optString("note"));
            c.io.apply(plan(row(0,"Note only")),"Don't remember",0);c.data.review10(id,0,"Later note only");c.io.undo(c.io.lastBatch);assertEquals("Later note only",c.data.item(id).optJSONObject("review").optString("note"));
        }
    }
    @Test public void noirJsonAndBackupKeepSevenOutOfTenAndAcceptLegacyRating()throws Exception{
        try(Case c=new Case()){
            c.io.apply(plan(row(4,"Review")),"Don't remember",0);c.data.review10(item(c).optString("id"),7,"Review");
            String json=(String)PhoneHistory.export(DataStore.objects(c.data.items()),null,"noir-json")[0];
            ByteArrayOutputStream out=new ByteArrayOutputStream();c.io.writeBackup(out);PhoneHistoryIo.Backup backup=PhoneHistoryIo.readBackup(out.toByteArray());
            c.data.replaceState(new JSONObject());c.io.apply(plan(PhoneHistory.json(json).rows.get(0)),"Don't remember",0);assertEquals("JSON round trip",7,DataStore.rating10(item(c)));
            c.data.replaceState(new JSONObject());c.io.apply(plan(backup.rows.get(0)),"Don't remember",0);assertEquals("Backup history round trip",7,DataStore.rating10(item(c)));
            JSONArray legacy=new JSONArray(json);legacy.getJSONObject(0).remove("rating10");c.data.replaceState(new JSONObject());c.io.apply(plan(PhoneHistory.json(legacy.toString()).rows.get(0)),"Don't remember",0);assertEquals("Legacy rating:4 remains 8/10",8,DataStore.rating10(item(c)));
        }
    }
    @Test public void destructiveToolsAbortAndShowBackupFailureThenProceedWithCompleteBackup()throws Exception{
        try(Case c=new Case()){
            c.io.apply(plan(row(4,"Keep")),"Don't remember",0);File external=c.context.getExternalFilesDir(null);assertTrue(external.isDirectory()||external.mkdirs());File blocked=new File(external,"backups");assertTrue(blocked.createNewFile());
            HistoryToolsActivity activity=(HistoryToolsActivity)test.startActivitySync(new Intent(test.getTargetContext(),HistoryToolsActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
            try{
                for(String kind:new String[]{"before-restore","before-remove"}){
                    String before=c.data.backupState().toString();test.runOnMainSync(()->activity.run("Working",()->destructive(c,kind)));
                    activity.work.submit(()->{}).get(10,TimeUnit.SECONDS);test.waitForIdleSync();assertEquals("Failed backup aborts "+kind,before,c.data.backupState().toString());
                    test.runOnMainSync(()->assertEquals(Loc.t(BACKUP_FAILED),activity.status.getText().toString()));
                }
                assertTrue(blocked.delete());
                for(String kind:new String[]{"before-remove","before-restore"}){
                    destructive(c,kind);
                    assertEquals(0,kind.equals("before-remove")?item(c).optJSONArray("viewings").length():c.data.items().length());
                }
                File[] files=HistoryToolsActivity.autoBackups(c.context);assertEquals(2,files.length);assertEquals("No temporary files remain",2,blocked.list().length);
                for(File f:files){Set<String> names=new HashSet<>();try(ZipFile zip=new ZipFile(f)){Enumeration<? extends ZipEntry> entries=zip.entries();while(entries.hasMoreElements()){ZipEntry e=entries.nextElement();names.add(e.getName());try(InputStream in=zip.getInputStream(e)){Object json=new JSONTokener(new String(DataStore.read(in,1024*1024),java.nio.charset.StandardCharsets.UTF_8)).nextValue();assertTrue(json instanceof JSONObject||json instanceof JSONArray);}}}assertEquals(new HashSet<>(Arrays.asList("manifest.json","history.json","phone.json")),names);PhoneHistoryIo.readBackup(Files.readAllBytes(f.toPath()));}
            }finally{test.runOnMainSync(activity::finish);}
        }
    }
}
