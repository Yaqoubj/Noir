package app.noir.mobile;

import android.app.Instrumentation;
import android.content.Intent;
import android.util.Log;
import androidx.test.platform.app.InstrumentationRegistry;
import org.json.*;
import org.junit.Test;
import java.io.*;
import java.lang.reflect.*;
import java.util.*;
import static org.junit.Assert.*;

/** Render and check-off timing with a large library (~300 shows, ~2000 movies); writes perf-phone.json. */
public final class PerfPhoneTest {
    final Instrumentation test=InstrumentationRegistry.getInstrumentation();
    // Poor man's profiler: samples the main thread's stack while r runs and ranks app frames by inclusive hits.
    String profile(Runnable r)throws Exception{Thread main=android.os.Looper.getMainLooper().getThread();Map<String,Integer> hits=new HashMap<>();boolean[] done={false};int[] total={0};
        Thread sampler=new Thread(()->{while(!done[0]){StackTraceElement[] st=main.getStackTrace();Set<String> seen=new HashSet<>();for(StackTraceElement e:st){String k=e.getClassName()+"."+e.getMethodName()+(e.getClassName().startsWith("app.noir")?"":"");if(k.startsWith("app.noir")||seen.size()<1){if(seen.add(k))hits.merge(k,1,Integer::sum);}}total[0]++;try{Thread.sleep(3);}catch(InterruptedException ignored){}}});
        sampler.start();for(int n=0;n<3;n++)test.runOnMainSync(r);done[0]=true;sampler.join();
        StringBuilder out=new StringBuilder("samples "+total[0]+"\n");hits.entrySet().stream().sorted((x,y)->y.getValue()-x.getValue()).limit(40).forEach(e->out.append(e.getValue()).append("  ").append(e.getKey()).append("\n"));return out.toString();}
    long time(Runnable r){long[] ms=new long[3];for(int n=0;n<3;n++){long t=System.nanoTime();test.runOnMainSync(r);ms[n]=(System.nanoTime()-t)/1000000;}Arrays.sort(ms);return ms[1];}
    long screen(Class<?> c){long t=System.nanoTime();android.app.Activity a=test.startActivitySync(new Intent(test.getTargetContext(),c).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));test.waitForIdleSync();long ms=(System.nanoTime()-t)/1000000;test.runOnMainSync(a::finish);test.waitForIdleSync();return ms;}
    /** S5: the PC fixture serves the synthetic big library (Noir.MobileChecks --offline --scale-library); times snapshot apply, History, Downloads and a 4,000-job phone sync queue. Run with -e scale true; writes perf-scale.json. */
    @Test public void testScaleLibrary()throws Exception{
        org.junit.Assume.assumeTrue("Run with -e scale true",Boolean.parseBoolean(TestRunner.arguments.getString("scale","false")));
        DataStore data=NoirApp.data;JSONObject report=new JSONObject();int cap=DataStore.maxLibraryBytes;DataStore.maxLibraryBytes=256*1024*1024;report.put("app-cap-mb",cap/1048576);long t=System.nanoTime();
        if(!data.paired())data.pair(TestRunner.arguments.getString("pairUri"));report.put("pair-ms",(System.nanoTime()-t)/1000000);
        t=System.nanoTime();data.sync();report.put("sync-first-ms",(System.nanoTime()-t)/1000000);
        t=System.nanoTime();data.sync();report.put("sync-again-ms",(System.nanoTime()-t)/1000000);
        Field stateField=DataStore.class.getDeclaredField("state");stateField.setAccessible(true);Method write=DataStore.class.getDeclaredMethod("write");write.setAccessible(true);
        File file=new File(test.getTargetContext().getFilesDir(),"library.json");
        t=System.nanoTime();write.invoke(data);report.put("library-write-ms",(System.nanoTime()-t)/1000000);report.put("library-json-kb",file.length()/1024);
        report.put("items",data.itemsShared().length());
        String original;synchronized(data){original=stateField.get(data).toString();}
        try{
        MainActivity home=(MainActivity)test.startActivitySync(new Intent(test.getTargetContext(),MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
        try{
            test.waitForIdleSync();
            report.put("groups-first-ms",time(()->{home.groupCache=null;home.groups();}));
            report.put("home-ms",time(()->{home.page="Home";home.detail="";home.render();}));
            report.put("library-ms",time(()->{home.page="Library";home.render();}));
            for(String tab:PhoneTracker.TABS)report.put("history-"+tab+"-ms",time(()->{home.page="History";home.premium.historyView=tab;home.render();}));
        }finally{test.runOnMainSync(home::finish);test.waitForIdleSync();}
        report.put("screen-download-hub-ms",screen(DownloadHubActivity.class));
        report.put("screen-phone-downloads-ms",screen(PhoneDownloadsActivity.class));
        report.put("screen-pc-downloads-ms",screen(PcDownloadsActivity.class));
        /* The phone's own sync queue: up to 4,000 waiting Trakt and Serializd jobs for watched items (the switch stays off, nothing is sent). */
        JSONArray items=data.itemsShared();int added=0;
        synchronized(data){JSONObject jobs=WatchSync.section(WatchSync.state(data),"jobs");
            for(String p:WatchSync.PROVIDERS)for(int n=0;n<items.length()&&added<4000;n++){JSONObject i=items.optJSONObject(n);if(i==null||!DataStore.watched(i)||p.equals("Serializd")&&i.optBoolean("movie"))continue;JSONObject j=new JSONObject();j.put("provider",p).put("itemId",i.optString("id")).put("viewingId","scale-"+n).put("watchedAt",1700000000000L+n*60000L).put("retryAt",0).put("attempts",0).put("message","Waiting to sync");jobs.put(p+":"+i.optString("id"),j);added++;}}
        report.put("queue-jobs",added);
        report.put("queue-copy-ms",time(()->WatchSync.jobs(data)));
        report.put("queue-count-ms",time(()->WatchSync.count(data,"Trakt",false)));
        report.put("queue-item-lookups-ms",time(()->{synchronized(data){for(JSONObject j:WatchSync.live(data))data.item(j.optString("itemId"));}}));
        t=System.nanoTime();write.invoke(data);report.put("library-write-with-queue-ms",(System.nanoTime()-t)/1000000);report.put("library-json-with-queue-kb",file.length()/1024);
        List<JSONObject> some=WatchSync.jobs(data);t=System.nanoTime();for(int n=0;n<10;n++)WatchSync.editJob(data,WatchSync.key(some.get(n)),j->DataStore.put(j,"message","Syncing…"));report.put("queue-10-edits-caller-ms",(System.nanoTime()-t)/1000000);
        }finally{synchronized(data){stateField.set(data,new JSONObject(original));write.invoke(data);}}
        Log.i("NoirPerf",report.toString(2));
        DataStore.maxLibraryBytes=cap;
        try(FileWriter out=new FileWriter(new File(test.getTargetContext().getFilesDir(),"perf-scale.json"))){out.write(report.toString(2));}
    }
    // A benchmark, not a regression test: run with work\phone-perf.ps1 (instrumentation argument perf=true).
    @Test public void testLargeLibrary()throws Exception{
        org.junit.Assume.assumeTrue("Run with -e perf true",Boolean.parseBoolean(TestRunner.arguments.getString("perf","false")));
        DataStore data=NoirApp.data;if(!data.paired())data.pair(TestRunner.arguments.getString("pairUri"));data.sync();
        Field stateField=DataStore.class.getDeclaredField("state");stateField.setAccessible(true);Method save=DataStore.class.getDeclaredMethod("save");save.setAccessible(true);
        String original;synchronized(data){original=stateField.get(data).toString();}
        try{
        synchronized(data){
            JSONArray items=((JSONObject)stateField.get(data)).getJSONObject("library").getJSONArray("items");Random random=new Random(7);
            for(int s=0;s<300;s++){boolean started=random.nextInt(3)==0;int upTo=1+random.nextInt(30),n=0;for(int season=1;season<=3;season++)for(int e=1;e<=8;e++,n++){JSONObject i=new JSONObject();
                i.put("id","perf-s"+s+"-"+season+"-"+e).put("group","show:PERF SHOW "+s).put("title","Perf Show "+s).put("year",2000+s%25).put("movie",false).put("season",season).put("episode",e).put("imdb",String.format(Locale.ROOT,"tt9%06d",s)).put("duration",2600).put("inLibrary",true).put("status","Watching").put("summary","A synthetic show used to time rendering with a large library.");
                if(started&&n<upTo)i.put("viewings",new JSONArray().put(new JSONObject().put("id",UUID.randomUUID().toString()).put("watchedAt","2025-0"+(1+n%9)+"-1"+(n%9)+"T20:00:00+00:00").put("source","Perf")));items.put(i);}}
            for(int m=0;m<2000;m++){JSONObject i=new JSONObject();i.put("id","perf-m"+m).put("group","movie:perf-m"+m).put("title","Perf Movie "+m).put("year",1980+m%45).put("movie",true).put("imdb",String.format(Locale.ROOT,"tt8%06d",m)).put("duration",6500).put("inLibrary",true).put("status","Planned");
                if(random.nextInt(3)==0)i.put("viewings",new JSONArray().put(new JSONObject().put("id",UUID.randomUUID().toString()).put("watchedAt","2024-0"+(1+m%9)+"-0"+(1+m%9)+"T20:00:00+00:00").put("source","Perf")));items.put(i);}
            save.invoke(data);
        }
        JSONObject report=new JSONObject();report.put("items",data.itemsShared().length());
        long t=System.nanoTime();data.itemsShared().toString().hashCode();report.put("sync-hash-ms",(System.nanoTime()-t)/1000000);
        MainActivity home=(MainActivity)test.startActivitySync(new Intent(test.getTargetContext(),MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
        try{
            report.put("groups-first-ms",time(()->{home.groupCache=null;home.groups();}));
            report.put("home-ms",time(()->{home.page="Home";home.detail="";home.render();}));
            report.put("library-ms",time(()->{home.page="Library";home.render();}));
            for(String tab:PhoneTracker.TABS)report.put("history-"+tab+"-ms",time(()->{home.page="History";home.premium.historyView=tab;home.render();}));
            test.runOnMainSync(()->{home.page="History";home.premium.historyView="Up next";home.render();});
            boolean[] flip={true};report.put("check-off-ms",time(()->{data.mark("perf-s1-3-8",flip[0]);flip[0]=!flip[0];home.render();}));
            StringBuilder prof=new StringBuilder();
            prof.append("== Home\n").append(profile(()->{home.page="Home";home.render();}));
            prof.append("== Library\n").append(profile(()->{home.page="Library";home.render();}));
            prof.append("== Diary\n").append(profile(()->{home.page="History";home.premium.historyView="Diary";home.render();}));
            test.runOnMainSync(()->{home.page="History";home.premium.historyView="Up next";home.render();});
            boolean[] f2={true};prof.append("== Check-off\n").append(profile(()->{data.mark("perf-s1-3-8",f2[0]);f2[0]=!f2[0];home.render();}));
            try(FileWriter out=new FileWriter(new File(test.getTargetContext().getFilesDir(),"perf-profile.txt"))){out.write(prof.toString());}
        }finally{test.runOnMainSync(home::finish);}
        Log.i("NoirPerf",report.toString(2));
        try(FileWriter out=new FileWriter(new File(test.getTargetContext().getFilesDir(),"perf-phone.json"))){out.write(report.toString(2));}
        assertTrue("A check-off re-renders in well under a second ("+report.getLong("check-off-ms")+" ms)",report.getLong("check-off-ms")<1000);
        // The synthetic titles and their check-offs must never reach the PC fixture or later tests.
        }finally{synchronized(data){stateField.set(data,new JSONObject(original));save.invoke(data);}}
    }
}
