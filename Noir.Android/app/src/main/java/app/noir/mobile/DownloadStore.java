package app.noir.mobile;
import android.content.Context;
import android.util.AtomicFile;
import android.os.StatFs;
import org.json.*;
import javax.net.ssl.HttpsURLConnection;
import java.io.*;
import java.util.*;
import java.util.regex.*;
import java.security.MessageDigest;

final class DownloadStore {
 final Context context;final File directory;private JSONArray jobs;
 volatile HttpsURLConnection activeConnection;volatile String activeId="";
 DownloadStore(Context c){context=c.getApplicationContext();File base=c.getExternalFilesDir(null);directory=new File(base==null?c.getFilesDir():base,"offline");directory.mkdirs();try{jobs=new JSONArray(new String(new AtomicFile(new File(c.getFilesDir(),"downloads.json")).readFully(),java.nio.charset.StandardCharsets.UTF_8));}catch(Exception e){jobs=new JSONArray();}for(JSONObject job:DataStore.objects(jobs))if(job.optString("status").equals("downloading"))DataStore.put(job,"status","paused");save();}
 synchronized JSONArray jobs(){return DataStore.copyArray(jobs);}
 synchronized JSONObject job(String id){for(JSONObject j:DataStore.objects(jobs))if(j.optString("id").equals(id))return DataStore.copy(j);return null;}
 synchronized void update(String id,String state,long bytes,long total,String etag,String error){for(JSONObject j:DataStore.objects(jobs))if(j.optString("id").equals(id)){DataStore.put(j,"status",state);DataStore.put(j,"bytes",bytes);DataStore.put(j,"total",total);DataStore.put(j,"etag",etag);DataStore.put(j,"error",error);save();return;}}
 synchronized void enqueue(JSONObject item)throws Exception{String id=item.optString("id");JSONObject old=job(id);if(old==null){if(jobs.length()>=100)throw new IOException("Remove a saved download before adding another.");old=new JSONObject();DataStore.put(old,"id",id);DataStore.put(old,"title",item.optString("title")+" · "+MainActivity.episode(item));DataStore.put(old,"status","queued");jobs.put(old);save();}else if(!old.optString("status").equals("complete"))update(id,"queued",old.optLong("bytes"),old.optLong("total"),old.optString("etag"),"");}
 synchronized void pause(String id){JSONObject j=job(id);if(j==null||j.optString("status").equals("complete"))return;update(id,"paused",j.optLong("bytes"),j.optLong("total"),j.optString("etag"),"");if(activeId.equals(id)&&activeConnection!=null)activeConnection.disconnect();}
 synchronized void remove(String id)throws Exception{pause(id);if(activeId.equals(id))throw new IOException("Transfer is stopping. Try removing it in a moment.");file(id,true).delete();file(id,false).delete();NoirApp.data.localFile(id,"");JSONArray keep=new JSONArray();for(JSONObject j:DataStore.objects(jobs))if(!j.optString("id").equals(id))keep.put(j);jobs=keep;save();}
 File file(String id,boolean partial)throws Exception{return new File(directory,DataStore.hex(MessageDigest.getInstance("SHA-256").digest(id.getBytes(java.nio.charset.StandardCharsets.UTF_8)))+(partial?".part":".video"));}
 synchronized String next(){for(JSONObject j:DataStore.objects(jobs))if(j.optString("status").equals("queued"))return j.optString("id");return null;}
 boolean paused(String id){JSONObject j=job(id);return j==null||j.optString("status").equals("paused");}
 static long[] range(String value)throws IOException{Matcher m=Pattern.compile("bytes (\\d+)-(\\d+)/(\\d+)").matcher(value==null?"":value);if(!m.matches())throw new IOException("PC returned an invalid resume response.");long start=Long.parseLong(m.group(1)),end=Long.parseLong(m.group(2)),total=Long.parseLong(m.group(3));if(start>end||end>=total)throw new IOException("PC returned invalid video sizes.");return new long[]{start,end,total};}
 void transfer(String id,java.util.function.Consumer<JSONObject> progress){long bytes=0,total=0;String etag="";HttpsURLConnection c=null;try{JSONObject j=job(id);if(j==null||paused(id))return;File part=file(id,true),ready=file(id,false);bytes=part.length();total=j.optLong("total");etag=j.optString("etag");
   if(ready.exists()&&total>0&&ready.length()==total){NoirApp.data.localFile(id,android.net.Uri.fromFile(ready).toString());update(id,"complete",total,total,etag,"");cacheSubtitles(id);return;}
   if(bytes>0&&etag.isEmpty()){try(RandomAccessFile f=new RandomAccessFile(part,"rw")){f.setLength(0);}bytes=0;}
   activeId=id;c=NoirApp.data.media(id,bytes>0?"bytes="+bytes+"-":null);activeConnection=c;if(bytes>0)c.setRequestProperty("If-Range",etag);int response=c.getResponseCode();
   if(response==206){long[] r=range(c.getHeaderField("Content-Range"));if(r[0]!=bytes||r[1]!=r[2]-1)throw new IOException("PC resume range did not match the saved file.");total=r[2];String tag=c.getHeaderField("ETag");if(!etag.isEmpty()&&!etag.equals(tag))throw new IOException("Video changed on the PC. Restart its download.");}
   else if(response==200){bytes=0;total=c.getContentLengthLong();}else throw new IOException(response==404?"Video is unavailable on your PC.":"Download connection failed ("+response+").");
   etag=c.getHeaderField("ETag");if(etag==null)etag="";if(total<=0)throw new IOException("PC did not provide the video's size.");
   if(new StatFs(directory.getAbsolutePath()).getAvailableBytes()<total-bytes+32L*1024*1024)throw new IOException("Not enough phone storage for this video.");
   if(paused(id))return;update(id,"downloading",bytes,total,etag,"");progress.accept(job(id));long checkpoint=System.currentTimeMillis();
   try(InputStream in=c.getInputStream();RandomAccessFile out=new RandomAccessFile(part,"rw")){if(bytes==0)out.setLength(0);out.seek(bytes);byte[] buffer=new byte[128*1024];int n;while((n=in.read(buffer))!=-1){if(paused(id))return;if(bytes+n>total)throw new IOException("PC sent more data than the expected video size.");out.write(buffer,0,n);bytes+=n;if(System.currentTimeMillis()-checkpoint>1000){out.getFD().sync();update(id,"downloading",bytes,total,etag,"");progress.accept(job(id));checkpoint=System.currentTimeMillis();}}out.getFD().sync();}
   if(paused(id))return;if(bytes!=total||part.length()!=total)throw new IOException("Transfer was interrupted. Resume to finish saving the video.");if(!part.renameTo(ready))throw new IOException("Couldn't finish saving the downloaded video.");NoirApp.data.localFile(id,android.net.Uri.fromFile(ready).toString());update(id,"complete",bytes,total,etag,"");cacheSubtitles(id);
  }catch(Exception e){update(id,paused(id)?"paused":"failed",bytes,total,etag,e.getMessage()==null?"Connection interrupted":e.getMessage());}
  finally{if(c!=null)c.disconnect();activeConnection=null;activeId="";JSONObject j=job(id);if(j!=null)progress.accept(j);}
 }
 void cacheSubtitles(String id){try{JSONObject item=NoirApp.data.item(id);if(item!=null)NoirApp.data.cacheSubtitles(item);}catch(Exception ignored){}}
 private synchronized void save(){AtomicFile f=new AtomicFile(new File(context.getFilesDir(),"downloads.json"));FileOutputStream out=null;try{out=f.startWrite();out.write(jobs.toString().getBytes(java.nio.charset.StandardCharsets.UTF_8));f.finishWrite(out);}catch(Exception e){if(out!=null)f.failWrite(out);throw new IllegalStateException("Couldn't save the download queue",e);}}
}
