package app.noir.mobile;

import android.app.Instrumentation;
import android.content.*;
import androidx.test.platform.app.InstrumentationRegistry;
import org.json.*;
import org.junit.Test;
import java.io.*;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicInteger;
import static org.junit.Assert.*;

// Sharpen round W3 (phone): A3-3 profile picker after rotation, A3-5 Ready budget, A3-7 import lookups, A3-8 taste favourites.
public final class PhoneBehaviourTest {
    final Instrumentation test=InstrumentationRegistry.getInstrumentation();
    final class Case implements AutoCloseable {
        final File root;final Context context;final DataStore data;final PhoneHistoryIo io;
        Case(){Context app=test.getTargetContext();root=new File(app.getFilesDir(),"work/sharpen-round/w3phone/"+UUID.randomUUID());assertTrue(root.mkdirs());context=new ContextWrapper(app){
            @Override public Context getApplicationContext(){return this;}
            @Override public File getFilesDir(){return root;}
            @Override public File getExternalFilesDir(String type){return new File(root,"external");}
            @Override public SharedPreferences getSharedPreferences(String name,int mode){return super.getSharedPreferences(root.getName()+name,mode);}
        };data=new DataStore(context);io=new PhoneHistoryIo(data,new DiscoveryClient(context));}
        public void close(){data.network.shutdown();data.images.shutdown();try{data.network.awaitTermination(10,TimeUnit.SECONDS);}catch(InterruptedException e){Thread.currentThread().interrupt();}DataStore.flushWrites();delete(root);}
    }
    static void delete(File f){File[] children=f.listFiles();if(children!=null)for(File child:children)delete(child);assertTrue("Delete test file "+f,!f.exists()||f.delete());}
    static PhoneHistory.Row row(String title,String imdb,int rating){return new PhoneHistory.Row("Noir",title,2020,true,imdb,"",0,0,"",1700000000000L,rating);}
    static PhoneHistoryIo.Plan plan(PhoneHistory.Row row)throws Exception{PhoneHistoryIo.Plan p=new PhoneHistoryIo.Plan();p.service=row.service;PhoneHistoryIo.Title t=new PhoneHistoryIo.Title();t.details=new JSONObject().put("id",row.imdb).put("noirType","movie").put("name",row.title).put("releaseInfo","2020");t.views.add(new PhoneHistoryIo.View(0,0,row.at,row.rating,row));p.titles.add(t);return p;}

    // A3-3: a network answer that arrives after the screen is gone must not touch it (it used to show a dialog on a destroyed window).
    @Test public void profilesDropsLateResultsAfterTheScreenIsGone()throws Exception{
        Intent intent=new Intent(test.getTargetContext(),ProfilesActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
        ProfilesActivity a=(ProfilesActivity)test.startActivitySync(intent);test.waitForIdleSync();
        CountDownLatch hold=new CountDownLatch(1);boolean[] ran={false};
        test.runOnMainSync(()->a.work(()->{hold.await(10,TimeUnit.SECONDS);a.post(()->ran[0]=true);throw new java.net.ConnectException("late");}));
        test.runOnMainSync(a::finish);for(int i=0;i<50&&!a.isDestroyed();i++){test.waitForIdleSync();Thread.sleep(100);}
        assertTrue("Activity destroyed",a.isDestroyed());hold.countDown();Thread.sleep(500);test.waitForIdleSync();
        assertFalse("A late result must not run on a destroyed profile screen",ran[0]);
        assertTrue("Destroying the screen forgets its jobs",a.jobs.isEmpty());
    }

    // A3-5: the Ready budget counts phone copies in every folder a profile can keep them in, not only the primary external one.
    @Test public void readyBudgetCountsEveryDownloadFolder()throws Exception{
        Context app=test.getTargetContext();ProfileStore profiles=new ProfileStore(app);String id=profiles.current();
        List<File> dirs=profiles.downloadDirectories(id);File external=DownloadStore.storageDirectories(ProfileStore.context(app,id)).get(0);
        File other=null;for(File d:dirs)if(!d.getCanonicalPath().equals(external.getCanonicalPath())){other=d;break;}
        assertNotNull("The profile has a second download folder (internal offline)",other);assertTrue(other.isDirectory()||other.mkdirs());
        DownloadStore store=new DownloadStore(app);long before=PhoneReadyJob.used(store);File video=new File(other,"sharpen-"+UUID.randomUUID()+".video");
        try{try(FileOutputStream out=new FileOutputStream(video)){out.write(new byte[1024*1024]);}
            assertEquals("A copy outside the primary folder counts toward the budget",before+1024*1024,PhoneReadyJob.used(store));
        }finally{assertTrue(!video.exists()||video.delete());}
    }

    // A3-7: leaving the import screen (interrupting its job) stops the title lookups instead of letting them run on.
    @Test public void importLookupsStopWhenTheJobIsCancelled()throws Exception{
        try(Case c=new Case()){
            AtomicInteger calls=new AtomicInteger();PhoneHistoryIo.matchOverride=group->{calls.incrementAndGet();try{Thread.sleep(200);}catch(InterruptedException e){Thread.currentThread().interrupt();}return new Object[]{null,new ArrayList<String>(),false};};
            try{
                List<PhoneHistory.Row> rows=new ArrayList<>();for(int n=0;n<60;n++)rows.add(row("Lookup "+n,String.format(Locale.ROOT,"tt%07d",2000000+n),0));
                Thread job=new Thread(()->{try{c.io.resolve("Noir",rows,null);}catch(InterruptedException ignored){}});job.start();
                Thread.sleep(500);job.interrupt();job.join(5000);assertFalse("Resolution returns after cancellation",job.isAlive());
                Thread.sleep(400);int settled=calls.get();Thread.sleep(1000);
                assertEquals("No lookup starts after cancellation",settled,calls.get());assertTrue("Most lookups never ran ("+settled+" of 60)",settled<30);
            }finally{PhoneHistoryIo.matchOverride=null;}
        }
    }

    // A3-8: the taste card uses the Top four this profile picked, which a standalone phone keeps itself (not the PC snapshot).
    @Test public void tasteCardUsesThisProfilesTopFour()throws Exception{
        try(Case c=new Case()){
            c.io.apply(plan(row("Taste one","tt1234567",8)),"Don't remember",0);c.io.apply(plan(row("Taste two","tt7654321",6)),"Don't remember",0);
            String group=null;for(JSONObject item:DataStore.objects(c.data.items()))if(item.optString("imdb").equals("tt7654321"))group=item.optString("group");
            assertNotNull(group);assertFalse("Standalone phone has no PC favourites",c.data.settings().has("favorites"));
            c.data.favorites(Collections.singletonList(group));
            TasteCode.Card card=TasteActivity.myCard(c.data,"Me",new TreeMap<>(),new HashSet<>());
            assertArrayEquals("The chosen favourite is on the card",new int[]{TasteCode.id("tt7654321")},card.favorites);
        }
    }
}
