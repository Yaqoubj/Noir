package app.noir.mobile;

import android.content.Context;
import android.net.*;
import org.json.*;
import org.libtorrent4j.*;
import org.libtorrent4j.alerts.*;
import org.libtorrent4j.swig.*;
import java.io.*;
import java.nio.file.Files;
import java.util.*;

/** The process owns one lazy session. A pack has one handle, with priorities shared by its jobs. */
final class PhoneTorrent {
 static final String EMPTY="No one is sharing this right now. Pick another release.",SWITCHED="Switched to another release — the first one had no one sharing";
 static long metadataTimeoutMs=180000,stallTimeoutMs=600000;
 static volatile boolean loopbackTests;
 static volatile java.util.function.Function<Context,Boolean> unmeteredOverride;
 private static SessionManager session;
 private static final Map<String,Group> groups=new HashMap<>();
 private static final Object ENGINE=new Object();
 final DownloadStore store;final Map<String,Task> tasks=new java.util.concurrent.ConcurrentHashMap<>();
 PhoneTorrent(DownloadStore store){this.store=store;}
 static long now(){return android.os.SystemClock.elapsedRealtime();}
 static SettingsPack settings(boolean local){SettingsPack p=new SettingsPack().listenInterfaces(local?"127.0.0.1:0":"0.0.0.0:0,[::]:0").activeDownloads(2).activeSeeds(20).activeLimit(24).uploadRateLimit(512*1024);p.setEnableDht(!local);p.setEnableLsd(!local);p.setBoolean(settings_pack.bool_types.enable_upnp.swigValue(),!local);p.setBoolean(settings_pack.bool_types.enable_natpmp.swigValue(),!local);if(local){p.setDhtBootstrapNodes("");p.setBoolean(settings_pack.bool_types.allow_multiple_connections_per_ip.swigValue(),true);}return p;}
 static SessionManager session(){synchronized(ENGINE){if(session==null){session=new SessionManager();session.addListener(new AlertListener(){public int[] types(){return null;}public void alert(Alert<?> alert){if(alert instanceof FileRenamedAlert){FileRenamedAlert a=(FileRenamedAlert)alert;synchronized(ENGINE){Group g=groups.get(a.handle().infoHash().toHex());if(g!=null)g.renamed.add(a.index());}}}});session.start(new SessionParams(settings(loopbackTests)));}return session;}}
 static boolean unmetered(Context c){java.util.function.Function<Context,Boolean> override=unmeteredOverride;if(override!=null)return override.apply(c);ConnectivityManager cm=c.getSystemService(ConnectivityManager.class);if(cm==null)return false;Network n=cm.getActiveNetwork();NetworkCapabilities cap=n==null?null:cm.getNetworkCapabilities(n);return cap!=null&&cap.hasCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET)&&!cm.isActiveNetworkMetered();}
 static boolean connected(Context c){if(unmeteredOverride!=null)return true;ConnectivityManager cm=c.getSystemService(ConnectivityManager.class);Network n=cm==null?null:cm.getActiveNetwork();NetworkCapabilities cap=n==null?null:cm.getNetworkCapabilities(n);return cap!=null&&cap.hasCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET);}
 static String magnet(JSONObject c)throws Exception{String hash=c.optString("hash");if(!hash.matches("[a-f0-9]{40}"))throw new IOException(EMPTY);StringBuilder s=new StringBuilder("magnet:?xt=urn:btih:").append(hash);for(String tracker:trackers(c))s.append("&tr=").append(PhoneDownloadProvider.encode(tracker));return s.toString();}
 /** Well-known public trackers, used only when the source gave none and the network is real. */
 static final String[] DEFAULT_TRACKERS={"udp://tracker.opentrackr.org:1337/announce","udp://open.stealth.si:80/announce","udp://tracker.torrent.eu.org:451/announce","udp://exodus.desync.com:6969/announce","udp://open.demonii.com:1337/announce"};
 /** The job's own trackers (kept in its sealed choice, so they survive a restart), else the built-in list. Loopback tests never get the public list. */
 static List<String> trackers(JSONObject c){List<String> list=new ArrayList<>();JSONArray given=c==null?null:c.optJSONArray("trackers");if(given!=null)for(int n=0;n<given.length();n++){String t=given.optString(n).trim();if(!t.isEmpty()&&!list.contains(t))list.add(t);}if(list.isEmpty()&&!loopbackTests)list.addAll(Arrays.asList(DEFAULT_TRACKERS));return list;}
 /** Progress means new bytes or live payload; connected seeds that send nothing do not count. */
 static boolean progressed(int seeds,double payloadRate,long bytes,long lastBytes){return payloadRate>0||bytes>lastBytes;}
 static File meta(Context c,String hash)throws IOException{if(!hash.matches("[a-f0-9]{40}"))throw new IOException(EMPTY);File dir=new File(c.getFilesDir(),"torrent-meta");dir.mkdirs();return new File(dir,hash+".torrent");}
 static List<JSONObject> files(Context context,JSONObject release,JSONObject item)throws Exception{
  File cache=meta(context,release.optString("hash"));byte[] bytes=null;if(cache.isFile())try{bytes=Files.readAllBytes(cache.toPath());new TorrentInfo(bytes);}catch(Exception ignored){bytes=null;}
  if(bytes==null){JSONObject settings=new PhoneDownloadSettings(context).load();if(!connected(context)||!unmetered(context)&&!settings.optBoolean("torrentMobile"))throw new IOException("Waiting for Wi-Fi");synchronized(ENGINE){Group g=groups.get(release.optString("hash"));if(g!=null&&g.handle.status().hasMetadata()){AddTorrentParams p=new AddTorrentParams(g.handle.swig().get_resume_data(TorrentHandle.SAVE_INFO_DICT));bytes=Vectors.byte_vector2bytes(libtorrent.write_torrent_file_buf_ex(p.swig()));}}
   if(bytes==null)bytes=session().fetchMagnet(magnet(release),90,new File(context.getCacheDir(),"torrent-metadata"));if(bytes==null)throw new IOException(EMPTY);TorrentInfo info=new TorrentInfo(bytes);if(!info.infoHash().toHex().equals(release.optString("hash")))throw new IOException(EMPTY);DiscoveryClient.write(cache,bytes);
  }
  TorrentInfo info=new TorrentInfo(bytes);List<JSONObject> result=new ArrayList<>();for(int n=0;n<info.numFiles();n++){String name=info.files().filePath(n);if(!PhoneDownloadProvider.video(name)||name.matches("(?i).*S\\d{1,2}E\\d{1,3}(?:E|-E|-)\\d{1,3}(?![\\dp]).*"))continue;int[] ep=PhoneDownloadProvider.episode(name,item.optInt("season"));JSONObject c=DataStore.copy(release);DataStore.put(c,"pack",false);DataStore.put(c,"fileIdx",n);DataStore.put(c,"fileName",PhoneDownloadSettings.safe(name,new JSONObject()));DataStore.put(c,"size",info.files().fileSize(n));DataStore.put(c,"season",ep[0]);DataStore.put(c,"episode",ep[1]);result.add(c);}if(result.size()==1&&result.get(0).optInt("episode")==0){DataStore.put(result.get(0),"season",item.optInt("season"));DataStore.put(result.get(0),"episode",item.optInt("episode"));}return result;
 }
 static final class Group {final PhoneTorrent owner;final String hash;final File dir,resume;final TorrentHandle handle;final Set<Integer> renamed=new HashSet<>();Group(PhoneTorrent o,String hash,File dir,File resume,TorrentHandle h){owner=o;this.hash=hash;this.dir=dir;this.resume=resume;handle=h;}}
 static final class Task {final String id;final JSONObject choice;final Group group;final int index;long start=now(),idle=now(),checkpoint=now(),completedAt,lastBytes=-1;boolean selected,moving,complete;Task(String id,JSONObject c,Group g){this.id=id;choice=c;group=g;index=c.optInt("fileIdx",-1);}}
 boolean isTorrent(JSONObject j){return j!=null&&j.optString("kind").equals("torrent");}
 int count(){return tasks.size();}
 /** Finished tasks that are only sharing back don't take a download slot. */
 int downloading(){int n=0;for(Task t:tasks.values())if(!t.complete)n++;return n;}
 /** A paused or queued job of the same torrent still needs its partial data and resume file. */
 private boolean waitingOn(String id,String hash){for(JSONObject j:DataStore.objects(store.jobs())){if(j.optString("id").equals(id)||!isTorrent(j)||!j.optString("status").matches("paused|queued|downloading"))continue;try{if(store.choice(j).optString("hash").equals(hash))return true;}catch(Exception ignored){}}return false;}
 synchronized boolean busy(){if(!tasks.isEmpty())return true;for(JSONObject j:DataStore.objects(store.jobs()))if(isTorrent(j)&&j.optString("status").equals("queued"))return true;return false;}
 boolean contains(String id){return tasks.containsKey(id);}
 void requestPause(String id){Task t=tasks.get(id);if(t!=null){t.group.handle.pause();checkpoint(t.group);}}
 synchronized TorrentHandle handle(String id){Task t=tasks.get(id);return t==null?null:t.group.handle;}
 private File resume(String hash){File dir=new File(store.context.getFilesDir(),"torrent-engine");dir.mkdirs();return new File(dir,hash+".resume");}
 void checkpoint(Group g){try{AddTorrentParams p=new AddTorrentParams(g.handle.swig().get_resume_data(TorrentHandle.SAVE_INFO_DICT));DiscoveryClient.write(g.resume,AddTorrentParams.writeResumeDataBuf(p));}catch(Exception ignored){}}
 private void add(JSONObject job)throws Exception{
  String id=job.optString("id");JSONObject c=store.choice(job);String hash=c.optString("hash");if(!hash.matches("[a-f0-9]{40}"))throw new IOException(EMPTY);Group g;
  synchronized(ENGINE){g=groups.get(hash);if(g!=null&&(g.owner!=this||!g.dir.getCanonicalFile().equals(store.torrentDirectory(id,hash).getCanonicalFile()))){store.torrentMessage(id,"Waiting for another download");return;}
   if(g==null){SessionManager manager=session();File dir=store.torrentDirectory(id,hash);if(!dir.isDirectory()&&!dir.mkdirs())throw new IOException("Couldn't save the download");File resume=resume(hash);AddTorrentParams p=null;
    if(resume.isFile())try{error_code error=new error_code();p=new AddTorrentParams(libtorrent.read_resume_data_ex(Vectors.bytes2byte_vector(Files.readAllBytes(resume.toPath())),error));if(error.value()!=0||!p.getInfoHashes().getV1().toHex().equals(hash))p=null;}catch(Exception ignored){}
    if(p==null)p=AddTorrentParams.parseMagnetUri(magnet(c));File cache=meta(store.context,hash);if(cache.isFile())p.setTorrentInfo(new TorrentInfo(cache));if(!loopbackTests&&p.getTrackers().isEmpty())p.setTrackers(Arrays.asList(DEFAULT_TRACKERS));p.setSavePath(dir.getAbsolutePath());p.setFlags(p.getFlags().and_(TorrentFlags.AUTO_MANAGED.inv()).and_(TorrentFlags.PAUSED.inv()).or_(TorrentFlags.DEFAULT_DONT_DOWNLOAD));
    TorrentInfo info=p.getTorrentInfo();if(info!=null&&info.isValid()){Priority[] priority=Priority.array(Priority.IGNORE,info.numFiles());int index=c.optInt("fileIdx",-1);if(index>=0&&index<priority.length)priority[index]=Priority.DEFAULT;p.filePriorities(priority);}
    error_code error=new error_code();TorrentHandle h=new TorrentHandle(manager.swig().add_torrent(p.swig(),error));if(error.value()!=0||!h.isValid())throw new IOException(EMPTY);g=new Group(this,hash,dir,resume,h);groups.put(hash,g);
   }
  }
  Task t=new Task(id,c,g);tasks.put(id,t);store.update(id,"downloading",job.optLong("bytes"),job.optLong("total"),"","");store.torrentMessage(id,"Finding people to download from…");if(loopbackTests&&c.optInt("peerPort")>0)g.handle.swig().connect_peer(new TcpEndpoint("127.0.0.1",c.optInt("peerPort")).swig());
 }
 private void priorities(Group g){TorrentInfo info=g.handle.torrentFile();if(info==null||!info.isValid())return;Priority[] p=Priority.array(Priority.IGNORE,info.numFiles());for(Task t:tasks.values())if(t.group==g&&t.index>=0&&t.index<p.length)p[t.index]=Priority.DEFAULT;g.handle.prioritizeFiles(p);}
 synchronized void tick(){try{JSONObject config=new PhoneDownloadSettings(store.context).load();boolean wifi=unmetered(store.context),allowed=connected(store.context)&&(wifi||config.optBoolean("torrentMobile"));
  for(JSONObject j:DataStore.objects(store.jobs()))if(downloading()<2&&isTorrent(j)&&j.optString("status").equals("queued")&&!tasks.containsKey(j.optString("id"))){if(!config.optBoolean("torrents")||TvUi.isTv(store.context)){store.pause(j.optString("id"));continue;}if(!allowed){store.torrentMessage(j.optString("id"),"Waiting for Wi-Fi");continue;}try{add(j);}catch(Exception e){store.update(j.optString("id"),"failed",j.optLong("bytes"),j.optLong("total"),"",DownloadStore.failureText(e));}}
  for(Task t:new ArrayList<>(tasks.values())){JSONObject j=store.job(t.id);if(j==null||j.optString("status").equals("paused")){stop(t.id,false);continue;}try{poll(t,j,config,allowed,wifi);}catch(Exception e){stop(t.id,false);store.update(t.id,"failed",j.optLong("bytes"),j.optLong("total"),"",DownloadStore.failureText(e));}}
 }catch(Exception ignored){}}
 private void poll(Task t,JSONObject job,JSONObject config,boolean allowed,boolean wifi)throws Exception{
  Group g=t.group;TorrentHandle h=g.handle;if(!config.optBoolean("torrents")){if(t.complete){stop(t.id,false);store.torrentMessage(t.id,"");}else store.pause(t.id);return;}if(!allowed||t.complete&&!wifi){h.pause();t.start=now();t.idle=now();store.torrentMessage(t.id,"Waiting for Wi-Fi");return;}
  h.resume();h.setUploadLimit(Math.max(1,config.optInt("torrentUploadKB",512))*1024);TorrentStatus s=h.status();if(s.errorCode().getValue()!=0)throw new IOException("Couldn't save the download");
  if(!s.hasMetadata()){store.torrentMessage(t.id,s.numPeers()>0?"Getting the file list…":"Finding people to download from…");if(now()-t.start>=metadataTimeoutMs)stalled(t,config);return;}
  if(!t.selected){TorrentInfo info=h.torrentFile();if(t.index<0||t.index>=info.numFiles()||!PhoneDownloadProvider.video(info.files().filePath(t.index)))throw new IOException("Source selection changed");long size=info.files().fileSize(t.index);if(!PhoneDownloadProvider.eligible(withSize(t.choice,size),config))throw new IOException("Source selection changed");if(new android.os.StatFs(store.targetDirectory(t.id).getAbsolutePath()).getAvailableBytes()<Math.max(0,size-job.optLong("bytes"))+32L*1024*1024)throw new IOException("Phone storage is full");priorities(g);t.selected=true;store.torrentDetails(t.id,s.numPeers(),s.numSeeds(),s.allTimeUpload(),0);}
  long total=h.torrentFile().files().fileSize(t.index),bytes=h.fileProgress()[t.index];double ratio=total<=0?0:s.allTimeUpload()/(double)total;store.torrentDetails(t.id,s.numPeers(),s.numSeeds(),s.allTimeUpload(),ratio);
  if(t.complete){if(!store.file(t.id,false).isFile()||config.optString("torrentSharing","ratio").equals("off")||config.optString("torrentSharing","ratio").equals("ratio")&&(ratio>=1||now()-t.completedAt>=2*60*60*1000L)){stop(t.id,false);store.torrentMessage(t.id,"");return;}store.torrentMessage(t.id,"Sharing back");return;}
  if(s.state()==TorrentStatus.State.CHECKING_FILES||s.state()==TorrentStatus.State.CHECKING_RESUME_DATA){store.torrentMessage(t.id,"Checking the file…");t.idle=now();return;}
  if(progressed(s.numSeeds(),s.downloadPayloadRate(),bytes,t.lastBytes)){t.idle=now();t.lastBytes=Math.max(t.lastBytes,bytes);}if(now()-t.idle>=stallTimeoutMs){stalled(t,config);return;}
  store.update(t.id,"downloading",bytes,total,"","");store.torrentMessage(t.id,"people");
  if(now()-t.checkpoint>=10000){checkpoint(g);t.checkpoint=now();}
  if(total>0&&bytes==total){if(!t.moving){store.torrentMessage(t.id,"Checking the file…");synchronized(ENGINE){g.renamed.remove(t.index);}h.renameFile(t.index,store.file(t.id,false).getAbsolutePath());t.moving=true;return;}boolean renamed;synchronized(ENGINE){renamed=g.renamed.contains(t.index);}if(!renamed)return;File ready=store.file(t.id,false);if(!ready.isFile()||ready.length()!=total)throw new IOException("Couldn't finalize video");store.registerTorrent(t.id,total);t.complete=true;t.completedAt=now();checkpoint(g);if(config.optString("torrentSharing","ratio").equals("off")){stop(t.id,false);store.torrentMessage(t.id,"");}else store.torrentMessage(t.id,"Sharing back");}
 }
 private static JSONObject withSize(JSONObject c,long size){JSONObject copy=DataStore.copy(c);DataStore.put(copy,"size",size);return copy;}
 private void stalled(Task t,JSONObject config)throws Exception{stop(t.id,true);if(config.optBoolean("torrentAutoSwitch",true)&&store.switchTorrent(t.id))return;JSONObject j=store.job(t.id);if(j!=null)store.update(t.id,"failed",0,j.optLong("total"),"",EMPTY);store.torrentMessage(t.id,"");}
 synchronized void stop(String id,boolean delete){Task t=tasks.remove(id);if(t==null){if(delete){JSONObject job=store.job(id);if(isTorrent(job))try{String hash=store.choice(job).optString("hash");if(hash.matches("[a-f0-9]{40}")){boolean used=false;for(Task other:tasks.values())if(other.group.hash.equals(hash))used=true;if(!used&&!waitingOn(id,hash)){erase(store.torrentDirectory(id,hash));resume(hash).delete();}}}catch(Exception ignored){}}return;}Group g=t.group;boolean used=false;for(Task other:tasks.values())if(other.group==g)used=true;if(used){priorities(g);return;}boolean keep=waitingOn(id,g.hash);g.handle.pause();if(keep||!delete&&!t.complete)checkpoint(g);synchronized(ENGINE){if(session!=null)session.remove(g.handle);groups.remove(g.hash);}if((delete||t.complete)&&!keep){g.resume.delete();erase(g.dir);}store.torrentMessage(id,"");}
 synchronized void stopAll(){stopAll(false);}
 /** interrupted: the service was cut off, so these continue by themselves next time; otherwise they stay paused. */
 synchronized void stopAll(boolean interrupted){for(String id:new ArrayList<>(tasks.keySet())){JSONObject j=store.job(id);stop(id,false);if(j!=null&&!j.optString("status").equals("complete")){store.update(id,"paused",j.optLong("bytes"),j.optLong("total"),"","");if(interrupted)store.markInterrupted(id);}}}
 static void erase(File f){if(!Files.isSymbolicLink(f.toPath())&&f.isDirectory()){File[] files=f.listFiles();if(files!=null)for(File child:files)erase(child);}f.delete();}
 static void shutdownTests(){synchronized(ENGINE){if(session!=null){session.stop();session=null;}groups.clear();}loopbackTests=false;unmeteredOverride=null;metadataTimeoutMs=180000;stallTimeoutMs=600000;}
 static String status(JSONObject job){String message=job.optString("torrentMessage");if(message.equals("people"))message=Loc.f("%d people · %d sharing",job.optInt("peers"),job.optInt("seeds"));else if(message.equals("Sharing back"))message=Loc.f("Sharing back · ratio %.1f",job.optDouble("ratio"));else message=Loc.t(message);if(!job.optString("switchMessage").isEmpty())message+=(message.isEmpty()?"":" · ")+Loc.t(job.optString("switchMessage"));return message;}
 static {String[][] words={{EMPTY,"لا يشارك أحد هذا الملف حاليًا. اختر إصدارًا آخر."},{SWITCHED,"تم التبديل إلى إصدار آخر — لم يكن أحد يشارك الإصدار الأول"},{"Finding people to download from…","جارٍ البحث عن أشخاص للتنزيل منهم…"},{"Getting the file list…","جارٍ جلب قائمة الملفات…"},{"Checking the file…","جارٍ التحقق من الملف…"},{"Waiting for Wi-Fi","بانتظار واي فاي"},{"Waiting for another download","بانتظار تنزيل آخر"},{"%d people · %d sharing","%d أشخاص · %d يشاركون"},{"Sharing back · ratio %.1f","مشاركة مع الآخرين · النسبة %.1f"},{"%d torrents · %s","%d تنزيلات تورنت · %s"},{"Good","جيد"},{"Low","ضعيف"},{"Unknown","غير معروف"},{"Dead","غير متاح"}};for(String[] w:words)Loc.AR.put(w[0],w[1]);}
}
