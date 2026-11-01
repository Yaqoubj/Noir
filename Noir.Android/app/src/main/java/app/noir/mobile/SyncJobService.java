package app.noir.mobile;
import android.app.job.*;
public final class SyncJobService extends JobService {
    DataStore.WorkCancel cancellation;java.util.concurrent.Future<?> task;
    @Override public boolean onStartJob(JobParameters p){final DataStore captured=NoirApp.data;final DataStore.WorkCancel work=new DataStore.WorkCancel();cancellation=work;task=captured.network.submit(()->{DataStore.workCancel.set(work);try{if(work.stopped||captured!=NoirApp.data)return;if(captured.paired()){try{captured.sync();if(!work.stopped&&NoirApp.data==captured)PhonePickupJob.kick(this);}catch(Exception ignored){}}if(!work.stopped&&NoirApp.data==captured&&captured.prefs().optBoolean("alerts",true)){EpisodeAlerts.refreshPhoneSchedules(this);if(!work.stopped)EpisodeAlerts.check(this);}if(!work.stopped&&NoirApp.data==captured)PhoneReadyJob.refill(this);}finally{DataStore.workCancel.remove();Thread.interrupted();work.connections.clear();if(!work.stopped)jobFinished(p,false);}});return true;}
    @Override public boolean onStopJob(JobParameters p){if(cancellation!=null)cancellation.cancel();if(task!=null)task.cancel(true);return true;}
}
