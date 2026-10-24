package app.noir.mobile;
import android.app.job.*;
public final class SyncJobService extends JobService {
    @Override public boolean onStartJob(JobParameters p){final DataStore captured=NoirApp.data;captured.network.execute(()->{try{captured.sync();}catch(Exception ignored){}
        try{if(NoirApp.data==captured){EpisodeAlerts.refreshPhoneSchedules(this);EpisodeAlerts.check(this);}}catch(Exception ignored){}jobFinished(p,false);});return true;}
    @Override public boolean onStopJob(JobParameters p){return true;}
}
