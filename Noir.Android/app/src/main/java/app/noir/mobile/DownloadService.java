package app.noir.mobile;
import android.app.*;
import android.content.*;
import android.os.*;
import org.json.JSONObject;
import java.util.concurrent.*;

public final class DownloadService extends Service {
 final ExecutorService worker=Executors.newSingleThreadExecutor();volatile boolean running,stopped;
 @Override public void onCreate(){super.onCreate();NotificationManager manager=getSystemService(NotificationManager.class);manager.createNotificationChannel(new NotificationChannel("downloads","Offline videos",NotificationManager.IMPORTANCE_LOW));}
 Notification notice(JSONObject j){String title=j==null?"Preparing your offline videos":j.optString("title"),text=j==null?"Starting download":j.optString("status");long total=j==null?0:j.optLong("total"),bytes=j==null?0:j.optLong("bytes");PendingIntent open=PendingIntent.getActivity(this,704,new Intent(this,DownloadsActivity.class),PendingIntent.FLAG_UPDATE_CURRENT|PendingIntent.FLAG_IMMUTABLE);return new Notification.Builder(this,"downloads").setSmallIcon(android.R.drawable.stat_sys_download).setContentTitle(title).setContentText(text).setContentIntent(open).setOngoing(true).setOnlyAlertOnce(true).setProgress(100,total>0?(int)(100d*bytes/total):0,total<=0).build();}
 @Override public synchronized int onStartCommand(Intent intent,int flags,int id){startForeground(704,notice(null));if(!running){running=true;worker.execute(()->{try{String next;while(!stopped&&(next=NoirApp.downloads.next())!=null)NoirApp.downloads.transfer(next,j->{if(!stopped)getSystemService(NotificationManager.class).notify(704,notice(j));});}finally{running=false;stopForeground(STOP_FOREGROUND_REMOVE);stopSelf();}});}return START_NOT_STICKY;}
 @Override public void onTimeout(int id,int type){stopped=true;if(!NoirApp.downloads.activeId.isEmpty())NoirApp.downloads.pause(NoirApp.downloads.activeId);stopForeground(STOP_FOREGROUND_REMOVE);stopSelf();}
 @Override public void onDestroy(){stopped=true;if(!NoirApp.downloads.activeId.isEmpty())NoirApp.downloads.pause(NoirApp.downloads.activeId);worker.shutdown();super.onDestroy();}
 @Override public IBinder onBind(Intent i){return null;}
}
