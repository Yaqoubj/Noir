package app.noir.mobile;
import android.content.*;
public final class SyncTriggerReceiver extends BroadcastReceiver {
 @Override public void onReceive(Context c,Intent intent){if(TvUi.isTv(c))return;if(Intent.ACTION_BOOT_COMPLETED.equals(intent.getAction()))BackgroundSync.register(c);BackgroundSync.network(c);}
}
