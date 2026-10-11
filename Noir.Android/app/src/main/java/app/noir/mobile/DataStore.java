package app.noir.mobile;
import android.content.Context;
import android.net.Uri;
import android.util.AtomicFile;
import org.json.*;
import java.io.*;
import java.net.*;
import java.security.*;
import java.security.cert.*;
import java.time.Instant;
import java.util.*;
import java.util.concurrent.*;
import javax.net.ssl.*;

public final class DataStore {
    final Context context;
    public final ScheduledExecutorService network=Executors.newSingleThreadScheduledExecutor();
    final ExecutorService images=Executors.newFixedThreadPool(2);
    private JSONObject state;
    volatile String connection="Not paired";
    volatile boolean online=false;
    final java.util.concurrent.atomic.AtomicBoolean syncQueued=new java.util.concurrent.atomic.AtomicBoolean();
    final java.util.concurrent.atomic.AtomicBoolean artQueued=new java.util.concurrent.atomic.AtomicBoolean();
    DataStore(Context c){context=c.getApplicationContext();try{state=new JSONObject(new String(new AtomicFile(new File(c.getFilesDir(),"library.json")).readFully(),java.nio.charset.StandardCharsets.UTF_8));}catch(Exception e){state=new JSONObject();}
        connection=paired()?"Offline · saved library":"Not paired";}
    synchronized boolean paired(){return !state.optString("token").isEmpty();}
    synchronized String address(){return state.optString("address");}
    synchronized String instance(){return state.optJSONObject("library")==null?"":state.optJSONObject("library").optString("instance");}
    synchronized JSONArray items(){JSONObject l=state.optJSONObject("library");return copyArray(l==null?new JSONArray():l.optJSONArray("items"));}
    synchronized JSONObject item(String id){JSONObject library=state.optJSONObject("library");for(JSONObject i:objects(library==null?null:library.optJSONArray("items")))if(i.optString("id").equals(id))return copy(i);return null;}
    synchronized JSONObject settings(){JSONObject l=state.optJSONObject("library");return l==null?new JSONObject():copy(l);}
    synchronized boolean settingFlag(String key,boolean fallback){JSONObject library=state.optJSONObject("library");return library==null?fallback:library.optBoolean(key,fallback);}
    synchronized long lastSync(){return state.optLong("lastSync");}
    synchronized JSONArray playQueue(){return copyArray(state.optJSONArray("playQueue"));}
    synchronized void queueVideo(String id){JSONArray q=playQueue();for(int n=0;n<q.length();n++)if(q.optString(n).equals(id))return;q.put(id);put(state,"playQueue",q);save();}
    synchronized void moveVideo(String id,int step){JSONArray old=playQueue();List<String> values=new ArrayList<>();for(int n=0;n<old.length();n++)values.add(old.optString(n));int from=values.indexOf(id),to=from+Integer.signum(step);if(from<0||to<0||to>=values.size())return;values.remove(from);values.add(to,id);put(state,"playQueue",new JSONArray(values));save();}
    synchronized void consumeVideo(String id){JSONArray q=new JSONArray(),old=playQueue();for(int n=0;n<old.length();n++)if(!old.optString(n).equals(id))q.put(old.optString(n));put(state,"playQueue",q);save();}
    synchronized long now(){return System.currentTimeMillis()+state.optLong("clockOffset");}
    synchronized int pendingCount(){return pending().length();}
    synchronized String localFile(String id){return state.optJSONObject("localFiles")==null?"":state.optJSONObject("localFiles").optString(id);}
    synchronized void localFile(String id,String uri){JSONObject files=state.optJSONObject("localFiles");if(files==null)files=new JSONObject();put(files,id,uri);put(state,"localFiles",files);save();}
    synchronized JSONArray phoneSubtitles(String id){JSONObject files=state.optJSONObject("phoneSubtitles");return copyArray(files==null?null:files.optJSONArray(id));}
    synchronized void phoneSubtitle(String id,JSONObject sub){JSONObject files=state.optJSONObject("phoneSubtitles");if(files==null)files=new JSONObject();JSONArray saved=new JSONArray();for(JSONObject old:objects(phoneSubtitles(id)))if(!old.optString("sourceKey").equals(sub.optString("sourceKey")))saved.put(old);saved.put(copy(sub));put(files,id,saved);put(state,"phoneSubtitles",files);save();}
    synchronized String subtitleLanguage(String id){JSONObject library=state.optJSONObject("library"),prefs=state.optJSONObject("subtitlePreferences"),p=prefs==null?null:prefs.optJSONObject(id);String fallback=library==null?"English":library.optString("subtitles","English");return p==null?fallback:p.optString("language",fallback);}
    synchronized double subtitleDelay(String id){JSONObject prefs=state.optJSONObject("subtitlePreferences"),p=prefs==null?null:prefs.optJSONObject(id);return p==null?0:p.optDouble("delay",0);}
    synchronized String preferredSubtitle(String id){JSONObject prefs=state.optJSONObject("subtitlePreferences"),p=prefs==null?null:prefs.optJSONObject(id);return p==null?"":p.optString("file");}
    synchronized void subtitleFilePreference(String id,String file){JSONObject prefs=state.optJSONObject("subtitlePreferences");if(prefs==null)prefs=new JSONObject();JSONObject p=prefs.optJSONObject(id);if(p==null)p=new JSONObject();put(p,"file",file);put(prefs,id,p);put(state,"subtitlePreferences",prefs);save();}
    synchronized void subtitlePreference(String id,String language,double delay){JSONObject prefs=state.optJSONObject("subtitlePreferences");if(prefs==null)prefs=new JSONObject();JSONObject p=prefs.optJSONObject(id);if(p==null)p=new JSONObject();put(p,"language",language);put(p,"delay",Math.max(-120,Math.min(120,delay)));put(prefs,id,p);put(state,"subtitlePreferences",prefs);save();}
    synchronized void subtitleFiles(String id,JSONArray files){for(JSONObject item:objects(state.optJSONObject("library")==null?null:state.optJSONObject("library").optJSONArray("items")))if(item.optString("id").equals(id)){put(item,"subtitleFiles",copyArray(files));save();break;}}
    private JSONArray pending(){JSONArray p=state.optJSONArray("pending");return p==null?new JSONArray():p;}
    synchronized void queue(JSONObject op){JSONArray p=pending();
        // Coalesce only progress in the same viewing; explicit marks and finished earlier viewings stay queued.
        if(op.optString("kind").equals("progress")){JSONArray keep=new JSONArray();for(JSONObject old:objects(p)){
            if(!(old.optString("kind").equals("progress")&&old.optString("itemId").equals(op.optString("itemId"))&&old.optString("viewingId").equals(op.optString("viewingId"))))keep.put(old);}p=keep;}
        p.put(op);put(state,"pending",p);apply(state.optJSONObject("library"),op);save();}
    JSONObject operation(String id,String kind){JSONObject o=new JSONObject();put(o,"id",UUID.randomUUID().toString());put(o,"itemId",id);put(o,"kind",kind);put(o,"at",now());return o;}
    void progress(String id,String viewing,double position,double duration,boolean done){JSONObject o=operation(id,"progress");put(o,"viewingId",viewing);put(o,"position",Math.max(0,position));put(o,"duration",Math.max(0,duration));put(o,"completed",done);queue(o);}
    void mark(String id,boolean value){JSONObject i=item(id);JSONObject p=i==null?null:i.optJSONObject("progress");JSONObject o=operation(id,"mark");put(o,"viewingId",UUID.randomUUID().toString());put(o,"completed",value);double d=p==null?i.optDouble("duration",0):p.optDouble("duration",0);put(o,"duration",d);put(o,"position",value?d:0);queue(o);}
    static void apply(JSONObject library,JSONObject op){if(library==null)return;JSONArray items=library.optJSONArray("items");for(JSONObject i:objects(items)){
        if(!i.optString("id").equals(op.optString("itemId")))continue;
        String kind=op.optString("kind");long at=op.optLong("at");
        if(kind.equals("progress")||kind.equals("mark")){
            JSONObject prior=i.optJSONObject("progress");if(prior==null||at>=date(prior.optString("updatedAt"))){JSONObject p=new JSONObject();put(p,"position",op.optDouble("position",0));put(p,"duration",op.optDouble("duration",0));put(p,"completed",op.optBoolean("completed"));put(p,"viewingId",op.optString("viewingId"));put(p,"updatedAt",Instant.ofEpochMilli(at).toString());put(i,"progress",p);}
            if(op.optBoolean("completed")){JSONArray watches=i.optJSONArray("viewings");if(watches==null)watches=new JSONArray();boolean found=false;for(JSONObject v:objects(watches))if(v.optString("id").equals(op.optString("viewingId")))found=true;
                if(!found){JSONObject v=new JSONObject();put(v,"id",op.optString("viewingId"));put(v,"watchedAt",Instant.ofEpochMilli(at).toString());put(v,"source","Android");watches.put(v);put(i,"viewings",watches);}}
        }else if(kind.equals("review")&&at>=i.optLong("reviewAt")){JSONObject r=new JSONObject();put(r,"rating",op.optInt("rating"));put(r,"note",op.optString("note"));put(i,"review",r);put(i,"reviewAt",at);}
        else if(kind.equals("list")){String group=i.optString("group");for(JSONObject other:objects(items))if(other.optString("group").equals(group)&&at>=other.optLong("listAt")){put(other,"listed",op.optBoolean("value"));put(other,"listAt",at);}}
        else if(kind.equals("match")){String group=i.optString("group");int offset=op.optInt("season")-i.optInt("season");for(JSONObject other:objects(items))if(other.optString("id").equals(i.optString("id"))||op.optBoolean("value")&&other.optString("group").equals(group)){put(other,"title",op.optString("title"));put(other,"year",op.optInt("year"));put(other,"imdb",op.optString("imdb"));put(other,"season",op.optBoolean("value")?(other.optInt("season")==0?0:other.optInt("season")+offset):op.optInt("season"));if(!op.optBoolean("value"))put(other,"episode",op.optInt("episode"));put(other,"episodeImdb","");}}
        else if(kind.equals("status")){String group=i.optString("group");for(JSONObject other:objects(items))if(other.optString("group").equals(group)&&at>=other.optLong("statusAt")){put(other,"status",op.optString("status"));put(other,"statusAt",at);}}
        else if(kind.equals("collection")){String group=i.optString("group"),name=op.optString("name");JSONArray names=library.optJSONArray("collections");if(names==null)names=new JSONArray();boolean exists=false;for(int n=0;n<names.length();n++)if(names.optString(n).equals(name))exists=true;if(!exists)names.put(name);put(library,"collections",names);
            for(JSONObject other:objects(items))if(other.optString("group").equals(group)){JSONObject times=other.optJSONObject("collectionTimes");if(times==null)times=new JSONObject();if(at<times.optLong(name))continue;JSONArray members=new JSONArray();boolean present=false;JSONArray old=other.optJSONArray("collections");if(old!=null)for(int n=0;n<old.length();n++)if(old.optString(n).equals(name))present=true;else members.put(old.optString(n));if(op.optBoolean("value"))members.put(name);put(other,"collections",members);put(times,name,at);put(other,"collectionTimes",times);}}
        break;
    }}
    public void pair(String link)throws Exception{
        Uri uri=Uri.parse(link.trim());if(!"noir".equals(uri.getScheme())||!"pair".equals(uri.getHost()))throw new IOException("Paste the full pairing link shown in Noir on your PC.");
        String addr=uri.getQueryParameter("address"),code=uri.getQueryParameter("code"),pin=uri.getQueryParameter("pin");
        validateAddress(addr);if(pin==null||!pin.matches("[A-Fa-f0-9]{64}")||code==null||!code.matches("[0-9]{8}"))throw new IOException("The pairing link is incomplete.");
        JSONObject body=new JSONObject();put(body,"code",code);put(body,"name",android.os.Build.MANUFACTURER+" "+android.os.Build.MODEL);
        JSONObject result=jsonRequest(addr,pin,"","/v1/pair",body.toString());
        synchronized(this){if(!instance().isEmpty()&&!instance().equals(result.optString("instance")))throw new IOException("This phone has a library from another PC. Use your original PC to keep its history.");
            put(state,"address",addr);put(state,"pin",pin);put(state,"token",result.getString("token"));save();}
        sync();
    }
    static void validateAddress(String addr)throws IOException{if(addr==null||!addr.matches("(?:[0-9]{1,3}\\.){3}[0-9]{1,3}:[0-9]{2,5}"))throw new IOException("Use the PC address from its pairing link.");
        String[] parts=addr.split(":")[0].split("\\.");int a=Integer.parseInt(parts[0]),b=Integer.parseInt(parts[1]);for(String p:parts)if(Integer.parseInt(p)>255)throw new IOException("Invalid PC address");
        if(!(a==10||a==192&&b==168||a==172&&b>=16&&b<=31||a==127))throw new IOException("Pair using your home Wi-Fi address.");int port=Integer.parseInt(addr.split(":")[1]);if(port<1||port>65535)throw new IOException("Invalid port");}
    public void sync()throws Exception{
        String addr,pin,token;JSONArray ops=new JSONArray();synchronized(this){if(!paired())return;addr=state.optString("address");pin=state.optString("pin");token=state.optString("token");for(JSONObject op:objects(pending())){if(ops.length()==500)break;ops.put(copy(op));}}
        try{long started=System.currentTimeMillis();JSONObject response=jsonRequest(addr,pin,token,"/v1/sync",ops.toString());JSONObject library=response.getJSONObject("library");
            synchronized(this){if(!instance().isEmpty()&&!instance().equals(library.optString("instance")))throw new IOException("The PC library changed. Your offline history is kept.");
                Set<String> ack=new HashSet<>();JSONArray a=response.getJSONArray("acknowledged");for(int n=0;n<a.length();n++)ack.add(a.optString(n));JSONArray remaining=new JSONArray();for(JSONObject op:objects(pending()))if(!ack.contains(op.optString("id")))remaining.put(op);
                put(state,"pending",remaining);put(state,"library",library);put(state,"clockOffset",library.getLong("serverTime")-(started+System.currentTimeMillis())/2);put(state,"lastSync",System.currentTimeMillis());for(JSONObject op:objects(remaining))apply(library,op);save();}
            online=true;connection="Connected · synced";if(artQueued.compareAndSet(false,true))images.execute(()->{try{cacheArt(library);}finally{artQueued.set(false);}});
        }catch(Exception e){online=false;connection="Offline · changes saved";throw e;}
    }
    void syncSoon(){if(syncQueued.compareAndSet(false,true))network.execute(()->{try{sync();}catch(Exception ignored){}finally{syncQueued.set(false);}});}
    JSONObject pcRequest(String path,JSONObject body)throws Exception{String addr,pin,token;synchronized(this){if(!paired())throw new IOException("Pair with your PC first.");addr=state.optString("address");pin=state.optString("pin");token=state.optString("token");}return jsonRequest(addr,pin,token,path,body==null?null:body.toString());}
    HttpsURLConnection media(String id,String range)throws Exception{String addr,pin,token;synchronized(this){addr=state.optString("address");pin=state.optString("pin");token=state.optString("token");}HttpsURLConnection c=open(addr,pin,token,"/v1/media/"+id);if(range!=null)c.setRequestProperty("Range",range);return c;}
    static JSONObject jsonRequest(String addr,String pin,String token,String path,String body)throws Exception{HttpsURLConnection c=open(addr,pin,token,path);if((path.startsWith("/v1/pc-downloads")||path.startsWith("/v1/discover")))c.setReadTimeout(70000);if(path.startsWith("/v1/subtitle-tools"))c.setReadTimeout(path.endsWith("/sync")?300000:30000);try{if(body!=null){c.setRequestMethod("POST");c.setDoOutput(true);c.setRequestProperty("Content-Type","application/json");try(OutputStream o=c.getOutputStream()){o.write(body.getBytes(java.nio.charset.StandardCharsets.UTF_8));}}
        int status=c.getResponseCode();if(status!=200&&(path.startsWith("/v1/discover")||path.startsWith("/v1/subtitle-tools"))){try(InputStream error=c.getErrorStream()){if(error!=null){String message=new JSONObject(new String(read(error,16384),java.nio.charset.StandardCharsets.UTF_8)).optString("error");if(!message.isEmpty())throw new IOException(message);}}}if(status!=200)throw new IOException(status==401?"Pairing expired or this phone was disconnected. Generate a new pairing link on the PC.":"PC connection failed ("+status+").");try(InputStream in=c.getInputStream()){return new JSONObject(new String(read(in,8*1024*1024),java.nio.charset.StandardCharsets.UTF_8));}}finally{c.disconnect();}}
    static HttpsURLConnection open(String addr,String pin,String token,String path)throws Exception{
        validateAddress(addr);SSLContext tls=SSLContext.getInstance("TLS");tls.init(null,new TrustManager[]{new X509TrustManager(){public X509Certificate[] getAcceptedIssuers(){return new X509Certificate[0];}public void checkClientTrusted(X509Certificate[] certs,String type)throws CertificateException{throw new CertificateException();}public void checkServerTrusted(X509Certificate[] certs,String type)throws CertificateException{try{if(certs.length==0||!hex(MessageDigest.getInstance("SHA-256").digest(certs[0].getEncoded())).equalsIgnoreCase(pin))throw new CertificateException("PC certificate changed. Pair again using Noir's new link.");certs[0].checkValidity();}catch(GeneralSecurityException e){throw new CertificateException(e);}}}},new SecureRandom());
        HttpsURLConnection c=(HttpsURLConnection)new URL("https://"+addr+path).openConnection();c.setSSLSocketFactory(tls.getSocketFactory());c.setHostnameVerifier((host,session)->host.equals(addr.split(":")[0]));c.setInstanceFollowRedirects(false);c.setConnectTimeout(5000);c.setReadTimeout(20000);if(!token.isEmpty())c.setRequestProperty("Authorization","Bearer "+token);return c;}
    void cacheArt(JSONObject library){Set<String> names=new LinkedHashSet<>();for(JSONObject i:objects(library.optJSONArray("items")))for(String key:new String[]{"poster","backdrop","episodeImage"}){String n=i.optString(key);if(!n.isEmpty())names.add(n);}for(String name:names){File file=art(name);if(file.exists())continue;String addr,pin,token;synchronized(this){addr=state.optString("address");pin=state.optString("pin");token=state.optString("token");}try{HttpsURLConnection c=open(addr,pin,token,"/v1/art/"+name);try{if(c.getResponseCode()!=200)continue;byte[] bytes;try(InputStream in=c.getInputStream()){bytes=read(in,12*1024*1024);}AtomicFile output=new AtomicFile(file);FileOutputStream out=output.startWrite();try{out.write(bytes);output.finishWrite(out);}catch(Exception e){output.failWrite(out);throw e;}}finally{c.disconnect();}}catch(Exception ignored){}}
    }
    File art(String name){File dir=new File(context.getFilesDir(),"art");dir.mkdirs();return new File(dir,name.matches("[A-Za-z0-9._-]+")?name:"missing");}
    File subtitle(JSONObject sub){String id=sub.optString("id"),ext=sub.optString("extension");if(!id.matches("[A-Fa-f0-9]{24}")||!Arrays.asList(".srt",".ass",".ssa",".vtt").contains(ext))return null;File dir=new File(context.getFilesDir(),"subtitles");dir.mkdirs();return new File(dir,id+ext);}
    void cacheSubtitles(JSONObject item)throws Exception{for(JSONObject sub:objects(item.optJSONArray("subtitleFiles"))){File file=subtitle(sub);if(file==null||file.exists())continue;String addr,pin,token;synchronized(this){if(!paired())return;addr=state.optString("address");pin=state.optString("pin");token=state.optString("token");}HttpsURLConnection c=open(addr,pin,token,"/v1/subtitles/"+item.optString("id")+"/"+sub.optString("id"));c.setReadTimeout(5000);try{if(c.getResponseCode()!=200)continue;byte[] bytes;try(InputStream in=c.getInputStream()){bytes=read(in,4*1024*1024);}AtomicFile out=new AtomicFile(file);FileOutputStream f=out.startWrite();try{f.write(bytes);out.finishWrite(f);}catch(Exception e){out.failWrite(f);throw e;}}finally{c.disconnect();}}}
    private synchronized void save(){AtomicFile f=new AtomicFile(new File(context.getFilesDir(),"library.json"));FileOutputStream out=null;try{out=f.startWrite();out.write(state.toString().getBytes(java.nio.charset.StandardCharsets.UTF_8));f.finishWrite(out);}catch(Exception e){if(out!=null)f.failWrite(out);throw new IllegalStateException("Couldn't save Noir history",e);}}
    static byte[] read(InputStream in,int max)throws IOException{ByteArrayOutputStream out=new ByteArrayOutputStream();byte[] buf=new byte[32768];int n;while((n=in.read(buf))!=-1){if(out.size()+n>max)throw new IOException("Response is too large");out.write(buf,0,n);}return out.toByteArray();}
    static String hex(byte[] bytes){StringBuilder s=new StringBuilder();for(byte b:bytes)s.append(String.format(Locale.ROOT,"%02X",b));return s.toString();}
    static void put(JSONObject o,String key,Object value){try{o.put(key,value);}catch(JSONException e){throw new IllegalArgumentException(e);}}
    static JSONObject copy(JSONObject o){try{return new JSONObject(o.toString());}catch(Exception e){return new JSONObject();}}
    static JSONArray copyArray(JSONArray a){try{return a==null?new JSONArray():new JSONArray(a.toString());}catch(Exception e){return new JSONArray();}}
    static List<JSONObject> objects(JSONArray a){List<JSONObject> list=new ArrayList<>();if(a!=null)for(int n=0;n<a.length();n++){JSONObject o=a.optJSONObject(n);if(o!=null)list.add(o);}return list;}
    static long date(String value){try{return Instant.parse(value).toEpochMilli();}catch(Exception e){return 0;}}
    static boolean watched(JSONObject i){JSONArray a=i.optJSONArray("viewings");JSONObject p=i.optJSONObject("progress");return a!=null&&a.length()>0||p!=null&&p.optBoolean("completed");}
    static long recent(JSONObject i){long at=0;JSONObject p=i.optJSONObject("progress");if(p!=null)at=date(p.optString("updatedAt"));for(JSONObject v:objects(i.optJSONArray("viewings")))at=Math.max(at,date(v.optString("watchedAt")));return at;}
    static String time(double seconds){long s=(long)Math.max(0,seconds);return s>=3600?String.format(Locale.ROOT,"%d:%02d:%02d",s/3600,s/60%60,s%60):String.format(Locale.ROOT,"%d:%02d",s/60,s%60);}
}
