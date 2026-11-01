package app.noir.mobile;
import android.app.*;
import android.content.*;
import android.os.Build;
import org.json.*;
import java.util.*;

// New-episode notifications for shows you follow, checked by the background sync job. Shows logged
// only on the phone get their air dates refreshed from Cinemeta twice a day.
final class EpisodeAlerts {
    static final String CHANNEL="new_episodes";
    static void askPermission(Activity activity){if(Build.VERSION.SDK_INT>=33&&activity.checkSelfPermission(android.Manifest.permission.POST_NOTIFICATIONS)!=android.content.pm.PackageManager.PERMISSION_GRANTED&&!NoirApp.data.prefs().optBoolean("askedNotifications"))
        {NoirApp.data.pref("askedNotifications",true);activity.requestPermissions(new String[]{android.Manifest.permission.POST_NOTIFICATIONS},13);}}
    static void refreshPhoneSchedules(Context context){
        DataStore data=NoirApp.data;if(DataStore.workStopped()||!data.prefs().optBoolean("alerts",true))return;long now=System.currentTimeMillis();if(now-data.prefs().optLong("schedulesAt")<12*3600000L)return;
        Set<String> pc=data.pcImdbs();
        Set<String> phoneShows=new LinkedHashSet<>();for(JSONObject i:DataStore.objects(data.items()))if(!i.optBoolean("movie")&&i.optString("group").startsWith("show:")&&!pc.contains(i.optString("imdb"))&&!i.optString("imdb").isEmpty())phoneShows.add(i.optString("imdb"));
        DiscoveryClient client=new DiscoveryClient(context);
        for(String imdb:phoneShows){if(DataStore.workStopped()||data!=NoirApp.data)return;try{JSONObject seed=new JSONObject();DataStore.put(seed,"id",imdb);DataStore.put(seed,"noirType","series");List<JSONObject> items=PhoneSeasonPlan.discoverItems(client.details(seed));if(DataStore.workStopped()||data!=NoirApp.data)return;data.refreshPhoneTitle(items);}catch(Exception ignored){}}
        if(!DataStore.workStopped()&&data==NoirApp.data)data.pref("schedulesAt",now);
    }
    static void check(Context context){
        DataStore data=NoirApp.data;if(DataStore.workStopped()||!data.prefs().optBoolean("alerts",true))return;
        NotificationManager manager=context.getSystemService(NotificationManager.class);if(manager==null||!manager.areNotificationsEnabled())return;
        if(Build.VERSION.SDK_INT>=33&&context.checkSelfPermission(android.Manifest.permission.POST_NOTIFICATIONS)!=android.content.pm.PackageManager.PERMISSION_GRANTED)return;
        manager.createNotificationChannel(new NotificationChannel(CHANNEL,"New episodes",NotificationManager.IMPORTANCE_DEFAULT));if(manager.getNotificationChannel(CHANNEL).getImportance()==NotificationManager.IMPORTANCE_NONE)return;
        long now=System.currentTimeMillis();Map<String,Long> air=new HashMap<>();JSONObject schedules=data.settings().optJSONObject("schedules");
        if(schedules!=null)for(Iterator<String> k=schedules.keys();k.hasNext();){String g=k.next();for(JSONObject e:DataStore.objects(schedules.optJSONArray(g)))air.put(g+":"+e.optInt("season")+":"+e.optInt("episode"),DataStore.date(e.optString("airsAt")));}
        Map<String,List<JSONObject>> groups=new LinkedHashMap<>();for(JSONObject i:DataStore.objects(data.items()))if(!i.optBoolean("movie"))groups.computeIfAbsent(i.optString("group"),x->new ArrayList<>()).add(i);
        JSONObject sent=data.prefs().optJSONObject("alerted");if(sent==null)sent=new JSONObject();List<JSONObject> fresh=new ArrayList<>();
        for(List<JSONObject> g:groups.values()){if(g.get(0).optString("status").equals("Dropped")||g.stream().noneMatch(DataStore::watched))continue;
            // A show abandoned long ago (only old history, episodes still unwatched) doesn't ping.
            long last=0;for(JSONObject i:g)last=Math.max(last,DataStore.recent(i));final long lastActive=last;
            for(JSONObject i:g){if(DataStore.watched(i)||i.optInt("season")<=0)continue;
                if(lastActive<now-180L*86400000&&g.stream().anyMatch(x->x.optInt("season")>0&&!DataStore.watched(x)&&(x.optInt("season")<i.optInt("season")||x.optInt("season")==i.optInt("season")&&x.optInt("episode")<i.optInt("episode"))))continue;long at=LogActivity.airs(i);if(at==0)at=air.getOrDefault(i.optString("group")+":"+i.optInt("season")+":"+i.optInt("episode"),0L);
                String key=i.optString("group")+":"+i.optInt("season")+":"+i.optInt("episode");if(at>0&&at<=now&&at>now-3L*86400000&&!sent.has(key)){fresh.add(i);DataStore.put(sent,key,now);}}}
        // Forget alerts older than a month so the record stays small.
        JSONObject keep=new JSONObject();for(Iterator<String> k=sent.keys();k.hasNext();){String key=k.next();if(sent.optLong(key)>now-31L*86400000)DataStore.put(keep,key,sent.optLong(key));}
        if(fresh.isEmpty())return;
        JSONObject first=fresh.get(0);String text=String.format(Locale.ROOT,"%s S%02dE%02d",first.optString("title"),first.optInt("season"),first.optInt("episode"))+(fresh.size()>1?" and "+(fresh.size()-1)+" more are out.":" is out.");
        Intent open=new Intent(context,MainActivity.class).putExtra("page","History").putExtra("tab","Upcoming").addFlags(Intent.FLAG_ACTIVITY_NEW_TASK|Intent.FLAG_ACTIVITY_CLEAR_TOP);
        PendingIntent tap=PendingIntent.getActivity(context,31,open,PendingIntent.FLAG_UPDATE_CURRENT|PendingIntent.FLAG_IMMUTABLE);
        if(DataStore.workStopped()||data!=NoirApp.data)return;try{manager.notify(31,new Notification.Builder(context,CHANNEL).setSmallIcon(android.R.drawable.ic_popup_reminder).setContentTitle("New episode").setContentText(text).setContentIntent(tap).setAutoCancel(true).build());}catch(SecurityException e){return;}data.pref("alerted",keep);
    }
}
