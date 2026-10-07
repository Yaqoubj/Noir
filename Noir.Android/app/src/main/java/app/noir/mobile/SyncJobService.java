package app.noir.mobile;
import android.app.job.*;
public final class SyncJobService extends JobService {
    @Override public boolean onStartJob(JobParameters p){NoirApp.data.network.execute(()->{try{NoirApp.data.sync();}catch(Exception ignored){}jobFinished(p,false);});return true;}
    @Override public boolean onStopJob(JobParameters p){return true;}
}
