package app.noir.mobile;

import android.app.job.*;
import android.content.*;
import android.net.*;
import org.json.*;
import java.io.*;
import java.util.*;

/** PC files only; shares the Ready transfer lock, storage accounting and cleanup. */
public final class PhonePickupJob extends JobService {
 public static final int JOB_ID=710,KICK_ID=711;
 public static final long PERIOD=15*60000L;
 volatile boolean stopped,ownsRun;Thread worker;DownloadStore captured;
 public static JobInfo jobInfo(Context c){return new JobInfo.Builder(JOB_ID,new ComponentName(c,PhonePickupJob.class)).setRequiredNetworkType(JobInfo.NETWORK_TYPE_UNMETERED).setPeriodic(PERIOD).setPersisted(true).build();}
 public static void schedule(Context c){DataStore data=NoirApp.data;if(data==null)return;try{JobScheduler jobs=c.getSystemService(JobScheduler.class);if(PhoneReadyJob.policy(data).pickupEnabled()&&data.paired()){if(jobs.getPendingJob(JOB_ID)==null)jobs.schedule(jobInfo(c));}else{jobs.cancel(JOB_ID);jobs.cancel(KICK_ID);}}catch(RuntimeException ignored){}}
 public static void kick(Context c){schedule(c);DataStore data=NoirApp.data;if(data==null||!data.paired()||!PhoneReadyJob.policy(data).pickupEnabled())return;try{JobScheduler jobs=c.getSystemService(JobScheduler.class);if(jobs.getPendingJob(KICK_ID)==null)jobs.schedule(new JobInfo.Builder(KICK_ID,new ComponentName(c,PhonePickupJob.class)).setRequiredNetworkType(JobInfo.NETWORK_TYPE_UNMETERED).setMinimumLatency(0).build());}catch(RuntimeException ignored){}}
 static String wifi(Context c){ConnectivityManager cm=c.getSystemService(ConnectivityManager.class);NetworkCapabilities nc=cm==null?null:cm.getNetworkCapabilities(cm.getActiveNetwork());return nc!=null&&nc.hasTransport(NetworkCapabilities.TRANSPORT_WIFI)&&nc.hasCapability(NetworkCapabilities.NET_CAPABILITY_NOT_METERED)?"":"Waiting for unmetered Wi-Fi";}
 static String allowed(DataStore data,DownloadStore store){if(data==null||store==null||data!=NoirApp.data||store!=NoirApp.downloads)return "Phone profile changed";String reason=PhoneReadyJob.policy(data).pickupPolicy(data.paired());return reason.isEmpty()?wifi(data.context):reason;}
 @Override public boolean onStartJob(JobParameters parameters){if(worker!=null&&worker.isAlive())return false;stopped=false;DataStore data=NoirApp.data;captured=NoirApp.downloads;worker=new Thread(()->{try{if(PhoneReadyJob.RUN.tryAcquire()){ownsRun=true;try{run(data,captured);}finally{ownsRun=false;PhoneReadyJob.RUN.release();}}}catch(Exception e){if(data!=null)PhoneReadyJob.policy(data).pickupBlocked("PC not reachable");}finally{jobFinished(parameters,false);}},"Noir-PC-pickup");worker.start();return true;}
 @Override public boolean onStopJob(JobParameters parameters){stopped=true;if(ownsRun&&captured!=null){JSONObject j=captured.job(captured.activeId);if(j!=null&&j.optBoolean("readyManaged")&&j.optBoolean("readyPickup"))captured.pause(j.optString("id"));}if(worker!=null)worker.interrupt();return true;}
 void run(DataStore data,DownloadStore store)throws Exception{if(data==null||store==null)return;PhoneReady p=PhoneReadyJob.policy(data);String reason=allowed(data,store);if(!reason.isEmpty()){p.pickupBlocked(reason);return;}if(!store.activeId.isEmpty()){p.pickupBlocked("Waiting for phone downloads to finish");return;}
  PhoneReadyJob.cleanup(data,store,p,()->!stopped&&allowed(data,store).isEmpty()&&store.activeId.isEmpty());
  List<JSONObject> candidates=PhoneReady.pickupCandidates(DataStore.objects(data.itemsShared()),id->!data.localFile(id).isEmpty(),DataStore::recent);
  if(candidates.isEmpty()){p.pickupPlanned(Collections.emptyList());store.pausePickupExcept(Collections.emptySet());p.pickupBlocked("No unfinished PC videos to copy");return;}
  String address=data.address();if(!PhoneReadyJob.lan(address)){p.pickupBlocked("PC not reachable");return;}
  Map<String,Long> sizes=new HashMap<>();List<JSONObject> selected=new ArrayList<>();int kept=0;
  for(JSONObject item:candidates){if(stopped||!allowed(data,store).isEmpty()||!address.equals(data.address()))return;String id=item.optString("id");if(!data.localFile(id).isEmpty()){if(++kept+selected.size()>=PhoneReady.PICKUP_LIMIT)break;continue;}JSONObject old=store.job(id);if(!item.optBoolean("available")||old!=null&&(!old.optBoolean("readyManaged")||old.optString("status").equals("complete")))continue;
   try{sizes.put(id,PhoneReadyJob.pcSize(data,id));selected.add(item);}catch(FileNotFoundException missing){p.log(id,"Unfinished video is no longer on your PC");}catch(Exception offline){p.pickupBlocked("PC not reachable");return;}if(selected.size()+kept>=PhoneReady.PICKUP_LIMIT)break;
  }
  p.pickupPlanned(selected);Set<String> ids=new HashSet<>();for(JSONObject i:selected)ids.add(i.optString("id"));store.pausePickupExcept(ids);p.pickupBlocked(selected.isEmpty()?"No unfinished PC videos to copy":"");
  for(JSONObject item:selected){if(stopped)return;reason=allowed(data,store);if(!reason.isEmpty()){p.pickupBlocked(reason);return;}if(!store.activeId.isEmpty()){p.pickupBlocked("Waiting for phone downloads to finish");return;}String id=item.optString("id");JSONObject fresh=data.item(id);if(!PhoneReady.unfinished(fresh)||!data.localFile(id).isEmpty())continue;long size=sizes.get(id);reason=PhoneReadyJob.storage(store,p,id,size);if(!reason.isEmpty()){p.pickupBlocked(reason);return;}
   if(store.enqueuePickup(fresh,size)){if(!p.pickupEnabled()){store.pausePickupExcept(Collections.emptySet());return;}p.log(id,item.optBoolean("movie")?"Copying your unfinished movie from your PC":"Copying your unfinished episode from your PC");store.transferReady(id,j->{},()->!stopped&&allowed(data,store).isEmpty()&&address.equals(data.address()));JSONObject result=store.job(id);if(result!=null&&result.optString("status").equals("complete"))p.log(id,"Ready to watch offline");else{reason=allowed(data,store);if(reason.isEmpty())reason=PhoneReadyJob.storage(store,p,id,result==null?size:result.optLong("total"));p.pickupBlocked(reason.isEmpty()?"PC copy interrupted; will resume later":reason);p.log(id,"PC copy interrupted; will resume later");return;}}
  }
 }
}
