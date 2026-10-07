package app.noir.mobile;
import android.app.Application;
import android.app.job.*;
import android.content.ComponentName;
public final class NoirApp extends Application {
    public static DataStore data;
    static DownloadStore downloads;
    @Override public void onCreate(){super.onCreate();data=new DataStore(this);downloads=new DownloadStore(this);JobScheduler jobs=(JobScheduler)getSystemService(JOB_SCHEDULER_SERVICE);
        jobs.schedule(new JobInfo.Builder(701,new ComponentName(this,SyncJobService.class)).setRequiredNetworkType(JobInfo.NETWORK_TYPE_ANY).setPeriodic(15*60*1000L).setPersisted(true).build());}
}
