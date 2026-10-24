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
    static DownloadStore downloads;
    static synchronized void switchProfile(android.content.Context c,String id,String pin){
        if(downloads!=null){for(org.json.JSONObject job:DataStore.objects(downloads.jobs()))if(!job.optString("status").equals("complete"))downloads.pause(job.optString("id"));if(!downloads.activeId.isEmpty())throw new IllegalStateException(Loc.t("Downloads are pausing. Try again in a moment."));}
        data.flush();new ProfileStore(c).select(id,pin);data=dataFor(c,id);downloads=new DownloadStore(data.context);Loc.use(data);
    }
    @Override public void onCreate(){super.onCreate();data=dataFor(this,new ProfileStore(this).current());downloads=new DownloadStore(data.context);Loc.use(data);
        // Arabic mirrors every screen; each activity gets the direction once its window is set up.
        registerActivityLifecycleCallbacks(new ActivityLifecycleCallbacks(){
            public void onActivityCreated(android.app.Activity a,android.os.Bundle b){}public void onActivityStarted(android.app.Activity a){Loc.apply(a);}public void onActivityResumed(android.app.Activity a){}
            public void onActivityPaused(android.app.Activity a){}public void onActivityStopped(android.app.Activity a){}public void onActivitySaveInstanceState(android.app.Activity a,android.os.Bundle b){}public void onActivityDestroyed(android.app.Activity a){}});JobScheduler jobs=(JobScheduler)getSystemService(JOB_SCHEDULER_SERVICE);
        jobs.schedule(new JobInfo.Builder(701,new ComponentName(this,SyncJobService.class)).setRequiredNetworkType(JobInfo.NETWORK_TYPE_ANY).setPeriodic(15*60*1000L).setPersisted(true).build());}
}
