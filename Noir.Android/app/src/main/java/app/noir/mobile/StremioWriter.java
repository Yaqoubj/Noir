package app.noir.mobile;
import org.json.*;
import java.io.*;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.util.*;
import javax.net.ssl.HttpsURLConnection;

/** Marks titles watched in the phone's own Stremio account (the one Harbor reads), the PC's StremioLibraryWriter ported: plain HTTPS to api.strem.io, no WebView.
 Safety: read-modify-write of one library item (unknown fields and the remote removed/temp kept), the remote item saved once under files/stremio-originals before the first write,
 watched bits only ever set, a title the account lacks is skipped unless "add watched titles" is on, and both switches are off by default. */
final class StremioWriter {
 static final String API="https://api.strem.io/api/",ADDED="noirAdded";
 enum Status{MARKED,ALREADY_WATCHED,ADDED,RESTORED,CHANGED_SINCE,OFF,NOT_IN_LIBRARY,UNKNOWN_ID,UNKNOWN_EPISODE,OUT_OF_STEP,NO_KEY,AUTH,NETWORK,SERVER,BAD_RESPONSE,LOCAL_SAVE}
 static final class Result{final Status status;final String id,message;Result(Status s,String id){status=s;this.id=id;message=message(s);}
  /* Site: stop and back off the whole account. Title: skip this title only. */
  boolean site(){switch(status){case NO_KEY:case AUTH:case NETWORK:case SERVER:case BAD_RESPONSE:case LOCAL_SAVE:return true;default:return false;}}
  boolean title(){switch(status){case NOT_IN_LIBRARY:case CHANGED_SINCE:case UNKNOWN_ID:case UNKNOWN_EPISODE:case OUT_OF_STEP:return true;default:return false;}}
  boolean succeeded(){return status==Status.MARKED||status==Status.ALREADY_WATCHED||status==Status.ADDED;}}
 static String message(Status s){switch(s){
  case MARKED:return "Marked watched in your Stremio account";case ALREADY_WATCHED:return "Already watched in your Stremio account";case ADDED:return "Added to your Stremio library as watched";
  case RESTORED:return "Restored the Stremio item from Noir's saved copy";case CHANGED_SINCE:return "Left as it is because it changed in Stremio since";case OFF:return "Marking watched in Stremio is off";case NOT_IN_LIBRARY:return "Not in your Stremio library";
  case UNKNOWN_ID:return "This title has no IMDb identity";case UNKNOWN_EPISODE:return "Stremio does not list this episode";case OUT_OF_STEP:return "Stremio's episode list is out of step; nothing was changed";
  case NO_KEY:return "Connect Stremio first";case AUTH:return "Stremio sign-in expired. Connect Stremio again";case NETWORK:return "Stremio could not be reached";
  case SERVER:return "Stremio had a problem. Noir will try later";case BAD_RESPONSE:return "Stremio sent an unexpected reply";default:return "Noir could not save a copy of the Stremio item";}}
 static boolean reconnect(String message){return message(Status.NO_KEY).equals(message)||message(Status.AUTH).equals(message);}

 /* The HTTP layer: one POST of a JSON body; tests pass a fake (no live calls). */
 interface Transport{Response post(String url,String body)throws IOException;}
 static final class Response{final int status;final String body;Response(int s,String b){status=s;body=b;}}
 /* A show's Cinemeta videos ({id, season, episode|number, released}). */
 interface Videos{List<JSONObject> of(String imdb)throws Exception;}
 interface Clock{long now();}
 static final Transport HTTPS=(url,body)->{HttpsURLConnection c=(HttpsURLConnection)new URL(url).openConnection();c.setConnectTimeout(10000);c.setReadTimeout(30000);c.setRequestMethod("POST");c.setDoOutput(true);c.setInstanceFollowRedirects(false);c.setRequestProperty("Content-Type","application/json");c.setRequestProperty("User-Agent","Noir-Android");
  try{try(OutputStream o=c.getOutputStream()){o.write(body.getBytes(StandardCharsets.UTF_8));}int status=c.getResponseCode();if(status<200||status>=300)return new Response(status,"");try(InputStream in=c.getInputStream()){return new Response(status,new String(DataStore.read(in,16*1024*1024),StandardCharsets.UTF_8));}}finally{c.disconnect();}};
 /* Test seams for the phone's writer: the HTTP layer and the episode lists (null = the real services). */
 static volatile Transport testTransport;static volatile Videos testVideos;

 final File originals;final Transport http;final Videos videos;final java.util.function.Supplier<String> key;final Clock clock;
 volatile boolean enabled,addTitles;
 /* A fresh fetch for an episode the kept list does not have yet (null = none). */
 Videos refetch;
 StremioWriter(File originals,Transport http,Videos videos,java.util.function.Supplier<String> key,Clock clock){this.originals=originals;this.http=http;this.videos=videos;this.key=key;this.clock=clock;}

 /* The phone's writer: its own key (HistoryToolsActivity's sign-in), originals under the profile's files dir, episode lists from the 12-hour Cinemeta cache. */
 static StremioWriter forPhone(DataStore d){Transport t=testTransport;Videos v=testVideos;
  StremioWriter w=new StremioWriter(new File(d.context.getFilesDir(),"stremio-originals"),t!=null?t:HTTPS,v!=null?v:id->cinemeta(d,id),()->d.stateObject("stremio").optString("key"),()->WatchSync.clock.now());
  w.refetch=v!=null?null:id->cinemeta(d,id,0);w.enabled=WatchSync.enabled(d,WatchSync.STREMIO);w.addTitles=WatchSync.stremioAdd(d);return w;}
 static List<JSONObject> cinemeta(DataStore d,String id)throws Exception{return cinemeta(d,id,12*3600000L);}
 static List<JSONObject> cinemeta(DataStore d,String id,long fresh)throws Exception{JSONObject root=new DiscoveryClient(d.context).read("meta/series/"+id+".json",fresh);JSONObject meta=root.optJSONObject("meta");return DataStore.objects(meta==null?null:meta.optJSONArray("videos"));}

 static boolean validId(String id){return id!=null&&id.matches("^tt\\d+$");}
 static String videoId(JSONObject item){return item.optString("imdb")+":"+item.optInt("season")+":"+item.optInt("episode");}
 static boolean movieWatched(JSONObject state){return integer(state,"timesWatched")>0||integer(state,"flaggedWatched")>0;}
 static long integer(JSONObject o,String name){if(o==null)return 0;Object v=o.opt(name);if(v instanceof Number)return ((Number)v).longValue();if(v instanceof Boolean)return (Boolean)v?1:0;if(v instanceof String)try{return Long.parseLong((String)v);}catch(NumberFormatException ignored){}return 0;}
 static String stamp(long at){return java.time.format.DateTimeFormatter.ofPattern("yyyy-MM-dd'T'HH:mm:ss.SSS'Z'",Locale.ROOT).withZone(java.time.ZoneOffset.UTC).format(java.time.Instant.ofEpochMilli(at));}
 Result result(Status s,String id){return new Result(s,id);}

 Result mark(JSONObject item,long watchedAt)throws InterruptedException{String id=item.optString("imdb");boolean movie=item.optBoolean("movie");
  if(!enabled)return result(Status.OFF,id);
  if(!validId(id))return result(Status.UNKNOWN_ID,id);
  if(!movie&&(item.optInt("season")<0||item.optInt("episode")<=0))return result(Status.UNKNOWN_EPISODE,id);
  String k=key.get();if(k==null||k.isEmpty())return result(Status.NO_KEY,id);
  Object[] got=get(k,id);if(got[1]!=null)return (Result)got[1];JSONObject remote=(JSONObject)got[0];String stamp=stamp(clock.now());
  try{
   if(remote==null){if(!addTitles)return result(Status.NOT_IN_LIBRARY,id);Object[] made=newItem(item,watchedAt,stamp);if(made[1]!=null)return (Result)made[1];
    if(!saveOriginal(id,new JSONObject().put(ADDED,true).put("_id",id).toString())||!saveWritten(id,stamp))return result(Status.LOCAL_SAVE,id);Result put=put(k,(JSONObject)made[0]);return put!=null?put:result(Status.ADDED,id);}
   String original=remote.toString();JSONObject state=remote.optJSONObject("state");if(state==null){state=new JSONObject();remote.put("state",state);}
   if(movie){if(movieWatched(state))return result(Status.ALREADY_WATCHED,id);state.put("timesWatched",Math.max(1,integer(state,"timesWatched")));state.put("flaggedWatched",1);}
   else{Object[] f=field(item,state);if(f[1]!=null)return (Result)f[1];StremioWriterBits field=(StremioWriterBits)f[0];if(field.watched(videoId(item)))return result(Status.ALREADY_WATCHED,id);field.mark(videoId(item));state.put("watched",field.toString());}
   /* lastWatched only moves forward; video_id and timeOffset are left as they are */
   long last=state.optString("lastWatched").isEmpty()?0:WatchSync.parse(state.optString("lastWatched"));if(last==0||last<watchedAt)state.put("lastWatched",stamp(watchedAt));
   remote.put("_mtime",stamp);
   if(!saveOriginal(id,original)||!saveWritten(id,stamp))return result(Status.LOCAL_SAVE,id);
   Result put=put(k,remote);return put!=null?put:result(Status.MARKED,id);
  }catch(JSONException e){return result(Status.BAD_RESPONSE,id);}}

 boolean hasOriginal(String id){return validId(id)&&originalFile(id).isFile();}
 /* The titles with a saved original, newest change first. */
 List<String> originals(){List<String> ids=new ArrayList<>();File[] files=originals.listFiles();if(files==null)return ids;Arrays.sort(files,(a,b)->Long.compare(b.lastModified(),a.lastModified()));for(File f:files){String n=f.getName();if(n.endsWith(".json")&&validId(n.substring(0,n.length()-5)))ids.add(n.substring(0,n.length()-5));}return ids;}

 /* What one Restore press did: titles put back, titles left alone because they changed in Stremio after Noir's last write, and any other failures. */
 static final class Summary{int restored,kept;final List<Result> failed=new ArrayList<>();
  String text(){List<String> parts=new ArrayList<>();if(restored>0||kept==0&&failed.isEmpty())parts.add(Loc.plural(restored,"%d restored","%d restored"));if(kept>0)parts.add(Loc.plural(kept,"%d left as it is because it changed in Stremio since","%d left as they are because they changed in Stremio since"));
   String t=parts.size()==2?Loc.f("%1$s, %2$s",parts.get(0),parts.get(1)):parts.isEmpty()?"":parts.get(0);if(!failed.isEmpty())t=(t.isEmpty()?"":t+" · ")+Loc.t(failed.get(0).message);return t;}}
 /* Restores each title in turn and counts what was put back and what was left alone. */
 Summary restore(List<String> ids)throws InterruptedException{Summary s=new Summary();for(String id:ids){Result r=restore(id);if(r.status==Status.RESTORED)s.restored++;else if(r.status==Status.CHANGED_SINCE)s.kept++;else s.failed.add(r);}return s;}
 /* Puts the saved original back (its _mtime moved to now so it wins); a title Noir added is marked removed. The remote removed/temp are kept as they are now.
  A title changed in Stremio after Noir's last write to it (its _mtime is newer, or Noir has no record of that write) is left as it is. */
 Result restore(String id)throws InterruptedException{if(!hasOriginal(id))return result(Status.UNKNOWN_ID,id);String k=key.get();if(k==null||k.isEmpty())return result(Status.NO_KEY,id);
  JSONObject original;try{original=new JSONObject(new String(java.nio.file.Files.readAllBytes(originalFile(id).toPath()),StandardCharsets.UTF_8));}catch(Exception e){return result(Status.LOCAL_SAVE,id);}
  Object[] got=get(k,id);if(got[1]!=null)return (Result)got[1];JSONObject remote=(JSONObject)got[0];if(remote==null)return result(Status.NOT_IN_LIBRARY,id);
  long written=writtenAt(id),changed=WatchSync.parse(remote.optString("_mtime"));if(written==0||changed==0||changed>written)return result(Status.CHANGED_SINCE,id);
  try{JSONObject restored;if(original.optBoolean(ADDED)){restored=remote;restored.put("removed",true);}else{restored=original;for(String flag:new String[]{"removed","temp"})if(remote.has(flag))restored.put(flag,remote.opt(flag));}
   String stamp=stamp(clock.now());restored.put("_mtime",stamp);if(!saveWritten(id,stamp))return result(Status.LOCAL_SAVE,id);Result put=put(k,restored);return put!=null?put:result(Status.RESTORED,id);}catch(JSONException e){return result(Status.BAD_RESPONSE,id);}}

 Object[] field(JSONObject item,JSONObject state)throws InterruptedException{String id=item.optString("imdb");List<JSONObject> list;
  try{list=videos.of(id);if(refetch!=null&&!StremioWriterBits.order(list).contains(videoId(item)))list=refetch.of(id);}catch(InterruptedException e){throw e;}catch(JSONException e){return new Object[]{null,result(Status.BAD_RESPONSE,id)};}catch(Exception e){return new Object[]{null,result(Status.NETWORK,id)};}
  List<String> ids=StremioWriterBits.order(list);if(!ids.contains(videoId(item)))return new Object[]{null,result(Status.UNKNOWN_EPISODE,id)};
  String serialized=state==null?"":state.optString("watched");if(state!=null&&state.isNull("watched"))serialized="";StremioWriterBits field;
  try{field=serialized.isEmpty()?StremioWriterBits.empty(ids):StremioWriterBits.parse(serialized,ids);}catch(IOException e){return new Object[]{null,result(Status.OUT_OF_STEP,id)};}
  /* never write a field that would drop episodes the account already has as watched */
  if(field.lostRemoteBits())return new Object[]{null,result(Status.OUT_OF_STEP,id)};return new Object[]{field,null};}

 Object[] newItem(JSONObject item,long watchedAt,String stamp)throws JSONException,InterruptedException{boolean movie=item.optBoolean("movie");
  JSONObject state=new JSONObject().put("lastWatched",stamp(watchedAt)).put("timeWatched",0).put("timeOffset",0).put("overallTimeWatched",0).put("timesWatched",movie?1:0).put("flaggedWatched",movie?1:0).put("duration",0).put("video_id",JSONObject.NULL).put("watched",JSONObject.NULL).put("noNotif",false);
  if(!movie){Object[] f=field(item,null);if(f[1]!=null)return f;StremioWriterBits field=(StremioWriterBits)f[0];field.mark(videoId(item));state.put("watched",field.toString());}
  String poster=item.optString("poster");if(!poster.startsWith("https://"))poster=null;
  JSONObject hints=new JSONObject().put("defaultVideoId",JSONObject.NULL).put("featuredVideoId",JSONObject.NULL).put("hasScheduledVideos",false);
  JSONObject made=new JSONObject().put("_id",item.optString("imdb")).put("name",WatchSync.name(item)).put("type",movie?"movie":"series").put("poster",poster==null?JSONObject.NULL:poster).put("posterShape","poster").put("removed",false).put("temp",false).put("_ctime",stamp).put("_mtime",stamp).put("state",state).put("behaviorHints",hints);
  return new Object[]{made,null};}

 Object[] get(String k,String id)throws InterruptedException{Object[] r;try{r=call("datastoreGet",new JSONObject().put("authKey",k).put("collection","libraryItem").put("ids",new JSONArray().put(id)).put("all",false),id);}catch(JSONException e){return new Object[]{null,result(Status.BAD_RESPONSE,id)};}
  if(r[1]!=null)return r;JSONArray list=((JSONObject)r[0]).optJSONArray("result");if(list==null)return new Object[]{null,result(Status.BAD_RESPONSE,id)};
  for(int i=0;i<list.length();i++){JSONObject o=list.optJSONObject(i);if(o!=null&&id.equals(o.opt("_id")))return new Object[]{o,null};}return new Object[]{null,null};}
 Result put(String k,JSONObject item)throws InterruptedException{String id=item.optString("_id");try{return (Result)call("datastorePut",new JSONObject().put("authKey",k).put("collection","libraryItem").put("changes",new JSONArray().put(item)),id)[1];}catch(JSONException e){return result(Status.BAD_RESPONSE,id);}}
 Object[] call(String method,JSONObject body,String id)throws InterruptedException{Response r;
  try{r=http.post(API+method,body.toString());}catch(InterruptedIOException e){if(Thread.currentThread().isInterrupted())throw new InterruptedException();return new Object[]{null,result(Status.NETWORK,id)};}catch(IOException|RuntimeException e){return new Object[]{null,result(Status.NETWORK,id)};}
  if(r.status==401||r.status==403)return new Object[]{null,result(Status.AUTH,id)};if(r.status<200||r.status>=300)return new Object[]{null,result(Status.SERVER,id)};
  Object root;try{root=new JSONTokener(r.body==null?"":r.body).nextValue();}catch(JSONException e){return new Object[]{null,result(Status.BAD_RESPONSE,id)};}
  if(!(root instanceof JSONObject))return new Object[]{null,result(Status.BAD_RESPONSE,id)};JSONObject o=(JSONObject)root;
  if(o.has("error")&&!o.isNull("error")){Object error=o.opt("error");String message=error instanceof JSONObject?String.valueOf(((JSONObject)error).opt("message")):String.valueOf(error),code=error instanceof JSONObject&&((JSONObject)error).has("code")?String.valueOf(((JSONObject)error).opt("code")):"";
   boolean auth=code.equals("1")||java.util.regex.Pattern.compile("session|auth|token|login",java.util.regex.Pattern.CASE_INSENSITIVE).matcher(message).find();return new Object[]{null,result(auth?Status.AUTH:Status.SERVER,id)};}
  return new Object[]{o,null};}

 /* Saved once, before the first write to an item; an existing original is never replaced. */
 boolean saveOriginal(String id,String json){File f=originalFile(id);if(f.isFile())return true;
  try{originals.mkdirs();File temp=new File(originals,id+".json."+UUID.randomUUID().toString().replace("-","")+".tmp");try(FileOutputStream out=new FileOutputStream(temp)){out.write(json.getBytes(StandardCharsets.UTF_8));out.getFD().sync();}
   if(f.isFile()){temp.delete();return true;}if(!temp.renameTo(f)){temp.delete();return f.isFile();}return true;}catch(IOException e){return false;}}
 File originalFile(String id){return new File(originals,id+".json");}
 /* The _mtime of Noir's last write to an item, kept next to its original and replaced on every write. */
 File writtenFile(String id){return new File(originals,id+".written");}
 boolean saveWritten(String id,String stamp){File f=writtenFile(id);try{originals.mkdirs();File temp=new File(originals,id+".written."+UUID.randomUUID().toString().replace("-","")+".tmp");try(FileOutputStream out=new FileOutputStream(temp)){out.write(stamp.getBytes(StandardCharsets.UTF_8));out.getFD().sync();}
  if(!temp.renameTo(f)){temp.delete();return false;}return true;}catch(IOException e){return false;}}
 long writtenAt(String id){File f=writtenFile(id);if(!f.isFile())return 0;try{return WatchSync.parse(new String(java.nio.file.Files.readAllBytes(f.toPath()),StandardCharsets.UTF_8).trim());}catch(IOException e){return 0;}}

 static {String[][] words={
  {"Marked watched in your Stremio account","عُلِّم كمُشاهَد في حساب Stremio"},{"Already watched in your Stremio account","مُشاهَد مسبقًا في حساب Stremio"},
  {"Added to your Stremio library as watched","أُضيف إلى مكتبة Stremio كمُشاهَد"},{"Restored the Stremio item from Noir's saved copy","استُعيد عنصر Stremio من نسخة Noir المحفوظة"},{"Left as it is because it changed in Stremio since","تُرك كما هو لأنه تغيّر في Stremio منذ ذلك الحين"},{"%1$s, %2$s","%1$s، و%2$s"},
  {"Marking watched in Stremio is off","تعليم المُشاهَد في Stremio متوقف"},{"Not in your Stremio library","ليس في مكتبة Stremio"},{"This title has no IMDb identity","لا يوجد معرّف IMDb لهذا العنوان"},
  {"Stremio does not list this episode","لا يعرض Stremio هذه الحلقة"},{"Stremio's episode list is out of step; nothing was changed","قائمة حلقات Stremio غير متطابقة؛ لم يتغير شيء"},
  {"Connect Stremio first","اربط Stremio أولًا"},{"Stremio sign-in expired. Connect Stremio again","انتهت جلسة Stremio. اربط Stremio مجددًا"},{"Stremio could not be reached","تعذّر الوصول إلى Stremio"},
  {"Stremio had a problem. Noir will try later","واجه Stremio مشكلة. سيحاول Noir لاحقًا"},{"Stremio sent an unexpected reply","أرسل Stremio ردًا غير متوقع"},{"Noir could not save a copy of the Stremio item","تعذّر على Noir حفظ نسخة من عنصر Stremio"}};
  for(String[] w:words)Loc.AR.put(w[0],w[1]);
  Loc.plural("%d restored","استُعيد عنصر واحد","استُعيد عنصران","استُعيدت %d عناصر","استُعيد %d عنصرًا");
  Loc.plural("%d left as they are because they changed in Stremio since","تُرك عنصر واحد كما هو لأنه تغيّر في Stremio منذ ذلك الحين","تُرك عنصران كما هما لأنهما تغيّرا في Stremio منذ ذلك الحين","تُركت %d عناصر كما هي لأنها تغيّرت في Stremio منذ ذلك الحين","تُركت %d عنصرًا كما هي لأنها تغيّرت في Stremio منذ ذلك الحين");}
}
