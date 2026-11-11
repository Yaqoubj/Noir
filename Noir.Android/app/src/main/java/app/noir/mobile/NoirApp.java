package app.noir.mobile;
import android.app.Application;
import android.app.job.*;
import android.content.ComponentName;
public final class NoirApp extends Application {
    android.content.Context rootContext;
    @Override protected void attachBaseContext(android.content.Context base){super.attachBaseContext(base);rootContext=new android.content.ContextWrapper(base){@Override public android.content.Context getApplicationContext(){return this;}};}
    @Override public java.io.File getFilesDir(){return data==null?super.getFilesDir():data.context.getFilesDir();}
    @Override public java.io.File getCacheDir(){return data==null?super.getCacheDir():data.context.getCacheDir();}
    @Override public java.io.File getExternalFilesDir(String type){return data==null?super.getExternalFilesDir(type):data.context.getExternalFilesDir(type);}
    @Override public android.content.SharedPreferences getSharedPreferences(String name,int mode){return data==null?super.getSharedPreferences(name,mode):data.context.getSharedPreferences(name,mode);}
    public static volatile DataStore data;
    static final java.util.Map<String,java.lang.ref.WeakReference<DataStore>> stores=new java.util.HashMap<>();
    static synchronized DataStore dataFor(android.content.Context c,String id){java.lang.ref.WeakReference<DataStore> ref=stores.get(id);DataStore saved=ref==null?null:ref.get();if(saved==null){saved=new DataStore(c,id);stores.put(id,new java.lang.ref.WeakReference<>(saved));}return saved;}
    static final java.util.Map<String,java.lang.ref.WeakReference<DownloadStore>> downloadStores=new java.util.HashMap<>();
    static synchronized void useDownloads(DownloadStore store){downloads=store;downloadStores.put(data.profileId,new java.lang.ref.WeakReference<>(store));}
    static void stopProfileDownloads(String id){DownloadStore store;synchronized(NoirApp.class){java.lang.ref.WeakReference<DownloadStore> ref=downloadStores.get(id);store=ref==null?null:ref.get();}if(store==null)return;store.torrents.stopAll();for(org.json.JSONObject job:DataStore.objects(store.jobs()))if(!job.optString("status").equals("complete"))store.pause(job.optString("id"));long end=android.os.SystemClock.elapsedRealtime()+5000;while(!store.activeId.isEmpty()&&android.os.SystemClock.elapsedRealtime()<end)try{Thread.sleep(50);}catch(InterruptedException e){Thread.currentThread().interrupt();break;}if(!store.activeId.isEmpty())throw new IllegalStateException(Loc.t("Downloads are pausing. Try again in a moment."));}
    static synchronized void forgetProfile(String id){java.lang.ref.WeakReference<DataStore> ref=stores.remove(id);DataStore store=ref==null?null:ref.get();if(store!=null){store.network.shutdownNow();store.images.shutdownNow();}downloadStores.remove(id);}
    static DownloadStore downloads;
    static synchronized void switchProfile(android.content.Context c,String id,String pin){
        if(downloads!=null){for(org.json.JSONObject job:DataStore.objects(downloads.jobs()))if(!job.optString("status").equals("complete"))downloads.pause(job.optString("id"));if(!downloads.activeId.isEmpty())throw new IllegalStateException(Loc.t("Downloads are pausing. Try again in a moment."));}
        data.flush();new ProfileStore(c).select(id,pin);data=dataFor(c,id);useDownloads(new DownloadStore(data.context));Loc.use(data);scheduleSync(c);
    }
    @Override public void onCreate(){super.onCreate();PhoneDensity.load(this);data=dataFor(this,new ProfileStore(this).current());useDownloads(new DownloadStore(data.context));Loc.use(data);WatchSyncSender.install(this);
        // Arabic mirrors every screen; each activity gets the direction once its window is set up.
        registerActivityLifecycleCallbacks(new ActivityLifecycleCallbacks(){
            public void onActivityCreated(android.app.Activity a,android.os.Bundle b){}public void onActivityStarted(android.app.Activity a){Loc.apply(a);}public void onActivityResumed(android.app.Activity a){if(a instanceof MainActivity)a.getWindow().getDecorView().post(()->{if(!a.isFinishing())BackgroundSync.maybeAsk((MainActivity)a);});}
            public void onActivityPaused(android.app.Activity a){PhoneStream.cancel(a);}public void onActivityStopped(android.app.Activity a){}public void onActivitySaveInstanceState(android.app.Activity a,android.os.Bundle b){}public void onActivityDestroyed(android.app.Activity a){PhoneStream.cancel(a);PhoneStream.requests.remove(a);PhoneStream.transitions.remove(a);}});data.network.execute(()->{scheduleSync(this);BackgroundSync.register(this);if(data.hasOutbox())BackgroundSync.schedule(data);});}
    static void scheduleSync(android.content.Context c){DataStore current=data;if(current==null)return;JobScheduler jobs=c.getSystemService(JobScheduler.class);if(jobs==null)return;if(!needsSync(current.paired(),current.prefs().optBoolean("alerts",true))){jobs.cancel(701);return;}JobInfo old=jobs.getPendingJob(701);if(old!=null&&old.getNetworkType()==(TvUi.isTv(c)?JobInfo.NETWORK_TYPE_UNMETERED:JobInfo.NETWORK_TYPE_ANY))return;jobs.schedule(new JobInfo.Builder(701,new ComponentName(c,SyncJobService.class)).setRequiredNetworkType(TvUi.isTv(c)?JobInfo.NETWORK_TYPE_UNMETERED:JobInfo.NETWORK_TYPE_ANY).setPeriodic(15*60*1000L).setPersisted(true).build());}
    static boolean needsSync(boolean paired,boolean alerts){return paired||alerts;}
}
