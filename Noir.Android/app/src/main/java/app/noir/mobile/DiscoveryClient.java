package app.noir.mobile;
import android.content.Context;
import android.graphics.*;
import android.util.AtomicFile;
import android.util.LruCache;
import org.json.*;
import javax.net.ssl.HttpsURLConnection;
import java.io.*;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.*;

/** Public metadata only. PC commands continue to use the paired, pinned connection. */
final class DiscoveryClient {
    final File directory;
    static final LruCache<String,Bitmap> bitmaps=new LruCache<String,Bitmap>(12*1024*1024){protected int sizeOf(String key,Bitmap value){return value.getAllocationByteCount();}};
    boolean cached=false;
    DiscoveryClient(Context context){directory=new File(context.getFilesDir(),"discover");directory.mkdirs();}
    static String hash(String value)throws Exception{return DataStore.hex(MessageDigest.getInstance("SHA-256").digest(value.getBytes(StandardCharsets.UTF_8)));}
    JSONObject read(String path,long fresh)throws Exception{
        File file=new File(directory,hash(path)+".json");if(file.exists()&&file.lastModified()>System.currentTimeMillis()-fresh){cached=true;return new JSONObject(new String(new AtomicFile(file).readFully(),StandardCharsets.UTF_8));}
        try{JSONObject json=new JSONObject(new String(fetch("https://v3-cinemeta.strem.io/"+path,8*1024*1024),StandardCharsets.UTF_8));write(file,json.toString().getBytes(StandardCharsets.UTF_8));return json;}
        catch(Exception e){if(Thread.currentThread().isInterrupted())throw e;if(file.exists()){cached=true;return new JSONObject(new String(new AtomicFile(file).readFully(),StandardCharsets.UTF_8));}throw e;}
    }
    JSONArray search(String query,String filter)throws Exception{
        cached=false;List<JSONArray> sources=new ArrayList<>();Exception failure=null;
        for(String kind:filter.equals("Movies")?new String[]{"movie"}:filter.equals("TV shows")?new String[]{"series"}:new String[]{"movie","series"}){
            try{String extra=query.trim().isEmpty()?"skip=0":"search="+URLEncoder.encode(query.trim(),"UTF-8").replace("+","%20");JSONObject response=read("catalog/"+kind+"/top/"+extra+".json",6*3600000L);JSONArray titles=new JSONArray();for(JSONObject title:DataStore.objects(response.optJSONArray("metas"))){if(!title.optString("id").matches("tt[0-9]+"))continue;DataStore.put(title,"noirType",kind);titles.put(title);}sources.add(titles);}catch(Exception e){failure=e;}
        }
        if(sources.isEmpty()&&failure!=null)throw failure;JSONArray result=new JSONArray();Set<String> ids=new HashSet<>();int length=sources.stream().mapToInt(JSONArray::length).max().orElse(0);for(int i=0;i<length;i++)for(JSONArray source:sources){JSONObject title=source.optJSONObject(i);if(title!=null&&ids.add(title.optString("noirType")+title.optString("id")))result.put(title);}return result;
    }
    JSONObject details(JSONObject title)throws Exception{
        String type=title.optString("noirType"),id=title.optString("id");if(!id.matches("tt[0-9]+")||!Arrays.asList("movie","series").contains(type))throw new IOException("Choose a title from search.");
        JSONObject result=read("meta/"+type+"/"+id+".json",12*3600000L).getJSONObject("meta");DataStore.put(result,"noirType",type);return result;
    }
    static List<JSONObject> episodes(JSONObject title){List<JSONObject> episodes=new ArrayList<>();Set<String> seen=new HashSet<>();for(JSONObject episode:DataStore.objects(title.optJSONArray("videos"))){int season=episode.optInt("season"),number=episode.optInt("episode",episode.optInt("number"));if(season<=0||number<=0||!seen.add(season+":"+number))continue;DataStore.put(episode,"episode",number);episodes.add(episode);}episodes.sort(Comparator.comparingInt((JSONObject e)->e.optInt("season")).thenComparingInt(e->e.optInt("episode")));return episodes;}
    static boolean released(JSONObject episode){String date=episode.optString("released",episode.optString("firstAired"));return date.isEmpty()||DataStore.date(date)<=System.currentTimeMillis();}
    static final Object[] ART_LOCKS=new Object[32];static {for(int n=0;n<ART_LOCKS.length;n++)ART_LOCKS[n]=new Object();}
    static Object artLock(String key){return ART_LOCKS[(key.hashCode()&0x7fffffff)%ART_LOCKS.length];}
    static boolean valid(java.util.function.BooleanSupplier valid){return !Thread.currentThread().isInterrupted()&&valid.getAsBoolean();}
    Bitmap art(String url)throws Exception{return art(url,()->true);}
    Bitmap art(String url,java.util.function.BooleanSupplier valid)throws Exception{
        if(!valid(valid)||url.isEmpty()||!url.startsWith("https://"))return null;synchronized(artLock(url)){if(!valid(valid))return null;Bitmap bitmap=bitmaps.get(url);if(bitmap!=null)return bitmap;File file=new File(directory,hash(url)+".jpg");if(!file.exists()){byte[] bytes=fetch(url,12*1024*1024);if(!valid(valid))return null;write(file,bytes);}bitmap=decode(file,768,valid);if(bitmap!=null)bitmaps.put(url,bitmap);return bitmap;}
    }
    static String localArtKey(File file,int width){return file.getAbsolutePath()+":"+file.length()+":"+file.lastModified()+":"+width;}
    static Bitmap localArt(File file,int width,java.util.function.BooleanSupplier valid){if(!valid(valid))return null;String key=localArtKey(file,width);synchronized(artLock(key)){if(!valid(valid))return null;Bitmap bitmap=bitmaps.get(key);if(bitmap==null){bitmap=decode(file,width,valid);if(bitmap!=null)bitmaps.put(key,bitmap);}return bitmap;}}
    static Bitmap decode(File file,int width,java.util.function.BooleanSupplier valid){if(!valid(valid))return null;BitmapFactory.Options bounds=new BitmapFactory.Options();bounds.inJustDecodeBounds=true;BitmapFactory.decodeFile(file.getPath(),bounds);if(bounds.outWidth<=0||!valid(valid))return null;BitmapFactory.Options options=new BitmapFactory.Options();options.inSampleSize=1;while(bounds.outWidth/options.inSampleSize>Math.max(1,width)*2)options.inSampleSize*=2;return valid(valid)?BitmapFactory.decodeFile(file.getPath(),options):null;}
    static final class ArtRequest implements android.view.View.OnAttachStateChangeListener {final android.widget.ImageView image;final DataStore.WorkCancel work=new DataStore.WorkCancel();volatile java.util.concurrent.Future<?> future;volatile boolean cancelled;ArtRequest(android.widget.ImageView image){this.image=image;}void cancel(){cancelled=true;work.cancel();if(future!=null)future.cancel(true);image.removeOnAttachStateChangeListener(this);}public void onViewAttachedToWindow(android.view.View view){}public void onViewDetachedFromWindow(android.view.View view){cancel();}}
    static ArtRequest load(android.widget.ImageView image,java.util.concurrent.ExecutorService pool,java.util.function.BooleanSupplier valid,java.util.concurrent.Callable<Bitmap> loader,java.util.function.Consumer<Bitmap> ready){ArtRequest request=new ArtRequest(image);image.addOnAttachStateChangeListener(request);request.future=pool.submit(()->{if(request.cancelled||!valid(valid))return;DataStore.workCancel.set(request.work);try{Bitmap bitmap=loader.call();if(bitmap!=null&&!request.cancelled&&valid(valid))image.post(()->{if(!request.cancelled&&valid.getAsBoolean()&&image.isAttachedToWindow())ready.accept(bitmap);});}catch(Exception ignored){}finally{DataStore.workCancel.remove();}});return request;}
    static byte[] fetch(String url,int maximum)throws Exception{
        DataStore.checkWork();HttpsURLConnection connection=(HttpsURLConnection)new URL(url).openConnection();DataStore.WorkCancel work=DataStore.workCancel.get();if(work!=null){work.connections.add(connection);if(work.stopped){connection.disconnect();DataStore.checkWork();}}connection.setConnectTimeout(6000);connection.setReadTimeout(15000);connection.setRequestProperty("User-Agent","Noir-Android/0.4.1");try{if(connection.getResponseCode()!=200)throw new IOException("Discover is temporarily unavailable.");try(InputStream input=connection.getInputStream()){return DataStore.read(input,maximum);}}finally{connection.disconnect();if(work!=null)work.connections.remove(connection);}
    }
    static void write(File file,byte[] bytes)throws Exception{AtomicFile output=new AtomicFile(file);FileOutputStream stream=output.startWrite();try{stream.write(bytes);output.finishWrite(stream);}catch(Exception e){output.failWrite(stream);throw e;}}
}
