package app.noir.mobile;
import android.content.Context;
import android.util.AtomicFile;
import android.os.StatFs;
import org.json.*;
import java.net.*;
import java.io.*;
import java.util.*;
import java.util.regex.*;
import java.security.MessageDigest;

final class DownloadStore {
 final Context context;final File directory;private JSONArray jobs;final java.util.function.Function<JSONObject,PhoneDownloadProvider> providers;
 volatile HttpURLConnection activeConnection;volatile String activeId="";
 DownloadStore(Context c){this(c,PhoneDownloadProvider::new);}
 DownloadStore(Context c,java.util.function.Function<JSONObject,PhoneDownloadProvider> providers){this.providers=providers;context=c.getApplicationContext();File base=c.getExternalFilesDir(null);directory=new File(base==null?c.getFilesDir():base,"offline");directory.mkdirs();try{jobs=new JSONArray(new String(new AtomicFile(new File(c.getFilesDir(),"downloads.json")).readFully(),java.nio.charset.StandardCharsets.UTF_8));}catch(Exception e){jobs=new JSONArray();}for(JSONObject job:DataStore.objects(jobs))if(job.optString("status").equals("downloading"))DataStore.put(job,"status","paused");save();}
 synchronized JSONArray jobs(){return DataStore.copyArray(jobs);}
 synchronized JSONObject job(String id){for(JSONObject j:DataStore.objects(jobs))if(j.optString("id").equals(id))return DataStore.copy(j);return null;}
 synchronized void update(String id,String state,long bytes,long total,String etag,String error){for(JSONObject j:DataStore.objects(jobs))if(j.optString("id").equals(id)){if(state.equals("downloading")&&j.optString("status").equals("paused"))return;DataStore.put(j,"status",state);DataStore.put(j,"bytes",bytes);DataStore.put(j,"total",total);DataStore.put(j,"etag",etag);DataStore.put(j,"error",error);save();return;}}
 synchronized void enqueue(JSONObject item)throws Exception{NoirApp.data.library(item.optString("id"),true);String id=item.optString("id");JSONObject old=job(id);if(old==null){if(jobs.length()>=1000)throw new IOException("Remove a saved download before adding another.");old=new JSONObject();DataStore.put(old,"id",id);DataStore.put(old,"title",item.optString("title")+" · "+MainActivity.episode(item));DataStore.put(old,"status","queued");jobs.put(old);save();}else if(!old.optString("status").equals("complete"))update(id,"queued",old.optLong("bytes"),old.optLong("total"),old.optString("etag"),"");}
 synchronized void enqueuePhone(JSONObject item,JSONObject choice)throws Exception{NoirApp.data.library(item.optString("id"),true);
  JSONObject remembered=NoirApp.data.rememberPhoneItem(item);String id=remembered.optString("id");JSONObject old=job(id);if(old!=null&&old.optString("status").equals("complete"))return;if(activeId.equals(id))throw new IOException("Pause this download before changing its release.");
  String sealed=new PhoneDownloadSettings(context).seal(choice.toString());boolean replacing=old!=null&&!old.optString("phoneSource").isEmpty();
  if(replacing){file(id,true).delete();file(id,false).delete();}
  enqueue(remembered);for(JSONObject j:DataStore.objects(jobs))if(j.optString("id").equals(id)){DataStore.put(j,"phoneSource",sealed);DataStore.put(j,"bytes",0);DataStore.put(j,"total",choice.optLong("size"));DataStore.put(j,"etag","");save();break;}
 }
 synchronized void source(String id,JSONObject source)throws Exception{String sealed=new PhoneDownloadSettings(context).seal(source.toString());for(JSONObject j:DataStore.objects(jobs))if(j.optString("id").equals(id)){DataStore.put(j,"phoneSource",sealed);save();return;}}
 long reservedBytes(){long bytes=0;for(JSONObject j:DataStore.objects(jobs()))if(!j.optString("status").equals("complete"))bytes+=Math.max(0,j.optLong("total")-j.optLong("bytes"));return bytes;}
 synchronized void pause(String id){JSONObject j=job(id);if(j==null||j.optString("status").equals("complete"))return;update(id,"paused",j.optLong("bytes"),j.optLong("total"),j.optString("etag"),"");if(activeId.equals(id)&&activeConnection!=null)activeConnection.disconnect();}
 synchronized void remove(String id)throws Exception{pause(id);if(activeId.equals(id))throw new IOException("Transfer is stopping. Try removing it in a moment.");file(id,true).delete();file(id,false).delete();NoirApp.data.localFile(id,"");JSONArray keep=new JSONArray();for(JSONObject j:DataStore.objects(jobs))if(!j.optString("id").equals(id))keep.put(j);jobs=keep;save();}
 File file(String id,boolean partial)throws Exception{return new File(directory,DataStore.hex(MessageDigest.getInstance("SHA-256").digest(id.getBytes(java.nio.charset.StandardCharsets.UTF_8)))+(partial?".part":".video"));}
 synchronized String next(){for(JSONObject j:DataStore.objects(jobs))if(j.optString("status").equals("queued"))return j.optString("id");return null;}
 boolean paused(String id){JSONObject j=job(id);return j==null||j.optString("status").equals("paused");}
 static long[] range(String value)throws IOException{Matcher m=Pattern.compile("bytes (\\d+)-(\\d+)/(\\d+)").matcher(value==null?"":value);if(!m.matches())throw new IOException("PC returned an invalid resume response.");long start=Long.parseLong(m.group(1)),end=Long.parseLong(m.group(2)),total=Long.parseLong(m.group(3));if(start>end||end>=total)throw new IOException("PC returned invalid video sizes.");return new long[]{start,end,total};}
 void transfer(String id,java.util.function.Consumer<JSONObject> progress){
  activeId=id;try{JSONObject job=job(id);if(job==null||paused(id))return;boolean phone=!job.optString("phoneSource").isEmpty();JSONObject selected=phone?new JSONObject(new PhoneDownloadSettings(context).unseal(job.optString("phoneSource"))):null;
   PhoneDownloadProvider provider=phone?providers.apply(new PhoneDownloadSettings(context).load()):null;
   for(int attempt=0;attempt<4&&!paused(id);attempt++){
    try{JSONObject resolved=null;if(phone){update(id,"downloading",file(id,true).length(),job.optLong("total"),job.optString("etag"),"Preparing cloud file");progress.accept(job(id));
      boolean changed=false;if(selected.optString("service").equals("pc")){JSONObject payload=new JSONObject();DataStore.put(payload,"itemId",selected.optString("itemId",id));DataStore.put(payload,"releaseId",selected.optString("releaseId"));DataStore.put(payload,"fileId",selected.optString("fileId"));DataStore.put(payload,"name",selected.optString("name"));try{resolved=NoirApp.data.pcRequest("/v1/phone-downloads/prepare",payload);}catch(Exception expired){JSONObject refreshed=NoirApp.data.pcRequest("/v1/phone-downloads/search/"+payload.optString("itemId"),null);List<JSONObject> options=DataStore.objects(refreshed.optJSONArray("choices"));options.sort(java.util.Comparator.comparingInt(c1->c1.optString("name").equals(payload.optString("name"))?0:1));if(options.isEmpty())throw new IOException("PC source unavailable");selected=options.get(0);source(id,selected);changed=true;DataStore.put(payload,"releaseId",selected.optString("releaseId"));DataStore.put(payload,"fileId",selected.optString("fileId"));resolved=NoirApp.data.pcRequest("/v1/phone-downloads/prepare",payload);}DataStore.put(resolved,"service","pc");}
      else resolved=attempt==0?provider.resolve(selected,NoirApp.data.item(id),()->paused(id)):provider.refresh(NoirApp.data.item(id),selected,()->paused(id));
      // A PC descriptor carries fileName, not the release name: only a re-searched source counts as a change.
      if(selected.optString("service").equals("pc")?changed:!selected.optString("hash").equals(resolved.optString("hash"))||!selected.optString("name").equals(resolved.optString("name"))){file(id,true).delete();update(id,"downloading",0,0,"","");}
      // Keep the recipe for refreshing PC descriptors; signed URLs and headers stay encrypted.
      if(!selected.optString("service").equals("pc")){selected=resolved;source(id,selected);}
    }
    transferOnce(id,resolved,progress);return;
    }catch(Exception e){if(paused(id))return;if(attempt==3)throw e;update(id,"downloading",file(id,true).length(),job(id).optLong("total"),job(id).optString("etag"),"Connection interrupted · resolving a fresh link");progress.accept(job(id));Thread.sleep(1500L*(attempt+1));}
   }
  }catch(Exception e){JSONObject job=job(id);if(job!=null)update(id,paused(id)?"paused":"failed",safeLength(id),job.optLong("total"),job.optString("etag"),"Download interrupted. Check storage, internet and phone download settings, then Resume / retry.");}
  finally{activeConnection=null;activeId="";JSONObject job=job(id);if(job!=null)progress.accept(job);}
 }
 long safeLength(String id){try{return file(id,true).length();}catch(Exception e){return 0;}}
 HttpURLConnection connect(JSONObject source,String id,long start,String etag)throws Exception{
  if(source==null){HttpURLConnection c=NoirApp.data.media(id,start>0?"bytes="+start+"-":null);if(start>0)c.setRequestProperty("If-Range",etag);return c;}
  String url=source.optString("url");JSONObject headers=source.optJSONObject("headers");for(int redirects=0;redirects<6;redirects++){
   HttpURLConnection c=source.optString("service").equals("pc")?NoirApp.data.phoneSource(url):(HttpURLConnection)new URL(url).openConnection();if(!source.optString("service").equals("pc")&&!PhoneDownloadProvider.validUrl(url))throw new IOException("Invalid download URL");c.setConnectTimeout(15000);c.setReadTimeout(30000);c.setInstanceFollowRedirects(false);c.setRequestProperty("Accept-Encoding","identity");c.setRequestProperty("User-Agent","AIO-Noir/0.11.0");if(headers!=null)for(Iterator<String> it=headers.keys();it.hasNext();){String name=it.next();c.setRequestProperty(name,headers.optString(name));}if(start>0){c.setRequestProperty("Range","bytes="+start+"-");c.setRequestProperty("If-Range",etag);}activeConnection=c;int status=c.getResponseCode();if(status!=301&&status!=302&&status!=303&&status!=307&&status!=308)return c;String location=c.getHeaderField("Location");c.disconnect();if(location==null||source.optString("service").equals("pc"))throw new IOException("Invalid redirect");URL old=new URL(url),next=new URL(old,location);if(!PhoneDownloadProvider.validUrl(next.toString())||old.getProtocol().equals("https")&&!next.getProtocol().equals("https"))throw new IOException("Invalid redirect");if(!old.getAuthority().equalsIgnoreCase(next.getAuthority()))headers=null;url=next.toString();
  }throw new IOException("Too many redirects");
 }
 void transferOnce(String id,JSONObject source,java.util.function.Consumer<JSONObject> progress)throws Exception{
  JSONObject job=job(id);File part=file(id,true),ready=file(id,false);long bytes=part.length(),total=job.optLong("total");String etag=job.optString("etag");
  if(ready.exists()&&total>0&&ready.length()==total){NoirApp.data.localFile(id,android.net.Uri.fromFile(ready).toString());update(id,"complete",total,total,etag,"");cacheSubtitles(id);return;}
  if(bytes>0&&(etag.isEmpty()||etag.startsWith("W/"))){try(RandomAccessFile out=new RandomAccessFile(part,"rw")){out.setLength(0);}bytes=0;}
  HttpURLConnection c=connect(source,id,bytes,etag);activeConnection=c;
  try{int response=c.getResponseCode();String tag=c.getHeaderField("ETag");if(tag==null||tag.startsWith("W/"))tag="";
   if(response==206){long[] range=range(c.getHeaderField("Content-Range"));if(range[0]!=bytes||range[1]!=range[2]-1||etag.isEmpty()||!etag.equals(tag)){try(RandomAccessFile out=new RandomAccessFile(part,"rw")){out.setLength(0);}update(id,"downloading",0,0,"","");throw new IOException("Source changed during resume");}total=range[2];}
   else if(response==200){bytes=0;total=c.getContentLengthLong();}else if(response==416&&bytes>0){try(RandomAccessFile out=new RandomAccessFile(part,"rw")){out.setLength(0);}update(id,"downloading",0,0,"","");throw new IOException("Restart required");}else throw new IOException("Source unavailable");
   etag=tag;long available=new StatFs(directory.getAbsolutePath()).getAvailableBytes();if(available<Math.max(0,total-bytes)+32L*1024*1024)throw new IOException("Not enough phone storage");if(paused(id))return;update(id,"downloading",bytes,total,etag,"");progress.accept(job(id));long checkpoint=System.currentTimeMillis();
   try(InputStream in=c.getInputStream();RandomAccessFile out=new RandomAccessFile(part,"rw")){if(bytes==0)out.setLength(0);out.seek(bytes);byte[] buffer=new byte[128*1024];int n;while((n=in.read(buffer))!=-1){if(paused(id))return;if(total>0&&bytes+n>total)throw new IOException("Unexpected video size");out.write(buffer,0,n);bytes+=n;if(System.currentTimeMillis()-checkpoint>=1000){if(new StatFs(directory.getAbsolutePath()).getAvailableBytes()<32L*1024*1024)throw new IOException("Phone storage is full");out.getFD().sync();update(id,"downloading",bytes,total,etag,"");progress.accept(job(id));checkpoint=System.currentTimeMillis();}}out.getFD().sync();}
   if(paused(id))return;if(total>0&&bytes!=total||bytes==0)throw new IOException("Incomplete transfer");total=bytes;if(!part.renameTo(ready))throw new IOException("Couldn't finalize video");NoirApp.data.localFile(id,android.net.Uri.fromFile(ready).toString());update(id,"complete",bytes,total,etag,"");cacheSubtitles(id);
  }finally{c.disconnect();activeConnection=null;}
 }
 void cacheSubtitles(String id){try{
  JSONObject item=NoirApp.data.item(id);if(item==null)return;Set<String> embedded=EmbeddedSubtitles.probe(NoirApp.data,id);JSONArray files=EmbeddedSubtitles.neededFiles(item.optJSONArray("subtitleFiles"),embedded);
  List<String> messages=new ArrayList<>();for(String lang:new String[]{"en","ar"})if(embedded.contains(lang))messages.add(SubtitleClient.label(lang)+" built into the video");
  synchronized(this){for(JSONObject job:DataStore.objects(jobs))if(job.optString("id").equals(id)){DataStore.put(job,"subtitleMessage",String.join(" · ",messages));save();break;}}
  JSONObject filtered=DataStore.copy(item);DataStore.put(filtered,"subtitleFiles",files);NoirApp.data.cacheSubtitles(filtered);
 }catch(Exception ignored){}}
 private synchronized void save(){AtomicFile f=new AtomicFile(new File(context.getFilesDir(),"downloads.json"));FileOutputStream out=null;try{out=f.startWrite();out.write(jobs.toString().getBytes(java.nio.charset.StandardCharsets.UTF_8));f.finishWrite(out);}catch(Exception e){if(out!=null)f.failWrite(out);throw new IllegalStateException("Couldn't save the download queue",e);}}
}
