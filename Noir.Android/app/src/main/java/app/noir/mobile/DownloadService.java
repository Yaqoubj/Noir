package app.noir.mobile;
import android.app.*;
import android.content.*;
import android.os.*;
import org.json.JSONObject;
import java.util.concurrent.*;

public final class DownloadService extends Service {
 final ExecutorService worker=Executors.newSingleThreadExecutor();volatile boolean running,stopped;volatile String serviceActive="";int lastStart;PowerManager.WakeLock wake;final Handler main=new Handler(Looper.getMainLooper());
 @Override public void onCreate(){super.onCreate();DownloadHubActivity.words();NotificationManager manager=getSystemService(NotificationManager.class);manager.createNotificationChannel(new NotificationChannel("downloads",Loc.t("Offline videos"),NotificationManager.IMPORTANCE_LOW));}
 Notification notice(JSONObject j){String title=j==null?Loc.t("Preparing your offline videos"):j.optString("title"),text=j==null?Loc.t("Starting download"):Loc.t(j.optString("status"));long total=j==null?0:j.optLong("total"),bytes=j==null?0:j.optLong("bytes");PendingIntent open=PendingIntent.getActivity(this,704,new Intent(this,DownloadsActivity.class),PendingIntent.FLAG_UPDATE_CURRENT|PendingIntent.FLAG_IMMUTABLE);return new Notification.Builder(this,"downloads").setSmallIcon(android.R.drawable.stat_sys_download).setContentTitle(title).setContentText(text).setContentIntent(open).setOngoing(true).setOnlyAlertOnce(true).setProgress(100,total>0?(int)(100d*bytes/total):0,total<=0).build();}
 @Override public synchronized int onStartCommand(Intent intent,int flags,int id){lastStart=id;startForeground(704,notice(null));if(wake==null){wake=getSystemService(PowerManager.class).newWakeLock(PowerManager.PARTIAL_WAKE_LOCK,"Noir:phone-downloads");wake.setReferenceCounted(false);}if(!wake.isHeld())wake.acquire(6*60*60*1000L);if(!running)begin();return START_NOT_STICKY;}
 void begin(){running=true;worker.execute(()->{try{String next;while(!stopped&&(next=NoirApp.downloads.next())!=null){if(!NoirApp.downloads.activeId.isEmpty()){try{Thread.sleep(250);}catch(InterruptedException e){Thread.currentThread().interrupt();break;}continue;}serviceActive=next;try{NoirApp.downloads.transfer(next,j->{if(!stopped)getSystemService(NotificationManager.class).notify(704,notice(j));});}finally{serviceActive="";}}}finally{main.post(()->{synchronized(DownloadService.this){running=false;if(!stopped&&NoirApp.downloads.next()!=null){begin();return;}releaseWake();stopForeground(STOP_FOREGROUND_REMOVE);stopSelfResult(lastStart);}});}});}
 void releaseWake(){if(wake!=null&&wake.isHeld())wake.release();}
 @Override public void onTimeout(int id,int type){stopped=true;if(!serviceActive.isEmpty())NoirApp.downloads.pause(serviceActive);stopForeground(STOP_FOREGROUND_REMOVE);stopSelf();}
 @Override public void onDestroy(){stopped=true;if(!serviceActive.isEmpty())NoirApp.downloads.pause(serviceActive);releaseWake();worker.shutdownNow();super.onDestroy();}
 @Override public IBinder onBind(Intent i){return null;}
}
