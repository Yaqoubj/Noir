package app.noir.mobile;

import android.content.Context;
import androidx.test.platform.app.InstrumentationRegistry;
import org.json.*;
import org.junit.After;
import org.junit.Before;
import org.junit.Test;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import static org.junit.Assert.*;

/** The phone's Stremio account writer and its watched bitfield. The bitfield reproduces stremio-core's own test vectors (the same ones Noir.Checks/StremioLibraryChecks.cs uses);
 every writer call goes to a fake HTTP layer: nothing here reaches api.strem.io or Cinemeta. */
public final class StremioWriterTest {
 /* A fake api.strem.io datastore: items by _id, every call recorded. */
 static final class Fake implements StremioWriter.Transport {
  interface Respond{StremioWriter.Response on(String method)throws IOException;}
  final Map<String,String> items=new LinkedHashMap<>();final List<String[]> calls=Collections.synchronizedList(new ArrayList<>());volatile Respond override;
  public StremioWriter.Response post(String url,String body)throws IOException{assertTrue(url,url.startsWith("https://api.strem.io/api/"));String method=url.substring(url.lastIndexOf('/')+1);calls.add(new String[]{method,body});
   Respond o=override;if(o!=null){StremioWriter.Response r=o.on(method);if(r!=null)return r;}
   try{JSONObject b=new JSONObject(body);
    if(method.equals("datastoreGet")){JSONArray ids=b.getJSONArray("ids"),found=new JSONArray();for(int i=0;i<ids.length();i++){String v=items.get(ids.getString(i));if(v!=null)found.put(new JSONObject(v));}return ok(new JSONObject().put("result",found).toString());}
    if(method.equals("datastorePut")){JSONArray changes=b.getJSONArray("changes");for(int i=0;i<changes.length();i++){JSONObject c=changes.getJSONObject(i);items.put(c.getString("_id"),c.toString());}return ok("{\"result\":{\"success\":true}}");}
   }catch(JSONException e){throw new IOException(e);}
   return new StremioWriter.Response(404,"");}
  int puts(){int n=0;synchronized(calls){for(String[] c:calls)if(c[0].equals("datastorePut"))n++;}return n;}
  JSONObject lastPut()throws JSONException{String body=null;synchronized(calls){for(String[] c:calls)if(c[0].equals("datastorePut"))body=c[1];}return new JSONObject(body).getJSONArray("changes").getJSONObject(0);}
  List<String> methods(){List<String> m=new ArrayList<>();synchronized(calls){for(String[] c:calls)m.add(c[0]);}return m;}
 }
 static StremioWriter.Response ok(String body){return new StremioWriter.Response(200,body);}
 static final String SHOW="tt2934286";
 /* Cinemeta-style list, deliberately out of order (Stremio sorts by season, episode, release). */
 static List<JSONObject> videos(String id,int from)throws JSONException{List<JSONObject> list=new ArrayList<>();for(int e=9;e>=from;e--)list.add(new JSONObject().put("id",id+":1:"+e).put("season",1).put("episode",e).put("released",String.format(Locale.ROOT,"2020-01-%02dT00:00:00.000Z",e)));return list;}
 static List<String> range(String prefix,int from,int to){List<String> l=new ArrayList<>();for(int i=from;i<=to;i++)l.add(prefix+i);return l;}

 File root;long now=1_794_391_200_000L;
 @Before public void setUp(){Context c=InstrumentationRegistry.getInstrumentation().getTargetContext();root=new File(c.getFilesDir(),"stremio-writer-test/"+UUID.randomUUID());assertTrue(root.mkdirs());}
 @After public void tearDown(){WatchSyncTest.delete(root);}
 StremioWriter writer(Fake fake,int from,String key){StremioWriter w=new StremioWriter(new File(root,"stremio-originals"),fake,id->videos(id,from),()->key,()->now);w.enabled=true;return w;}

 // ---- Bitfield: stremio-core stremio-watched-bitfield/src/watched_bitfield.rs and bitfield8.rs test vectors ----
 @Test public void bitfieldReproducesStremioCoreVectors()throws Exception{
  List<String> nine=range("tt2934286:1:",1,9);String parseAndModify="tt2934286:1:5:5:eJyTZwAAAEAAIA==";
  StremioWriterBits wb=StremioWriterBits.parse(parseAndModify,nine);String asText=wb.toString();wb.mark("tt2934286:1:6");
  assertTrue(StremioWriterBits.parse(parseAndModify,nine).watched("tt2934286:1:5"));assertFalse(StremioWriterBits.parse(parseAndModify,nine).watched("tt2934286:1:6"));
  assertEquals("parse_and_modify re-encodes byte for byte",parseAndModify,asText);assertTrue(wb.watched("tt2934286:1:6"));
  List<String> ids499=range("tt2934286:1:",1,499);StremioWriterBits blank=StremioWriterBits.fromArray(new boolean[500],ids499);
  boolean[] alt=new boolean[499];for(int i=0;i<alt.length;i++)alt[i]=i%2==0;StremioWriterBits back=StremioWriterBits.parse(StremioWriterBits.fromArray(alt,ids499).toString(),ids499);
  for(int i=0;i<499;i++){assertFalse(blank.get(i));assertEquals("construct_from_array "+i,i%2==0,back.get(i));assertEquals(i%2==0,back.watched(ids499.get(i)));}
  assertEquals("to_string_empty","undefined:1:eJwDAAAAAAE=",StremioWriterBits.empty(new ArrayList<>()).toString());
  StremioWriterBits empty=StremioWriterBits.parse("undefined:1:eJwDAAAAAAE=",new ArrayList<>());assertEquals("deserialize_empty",0,empty.length());assertEquals(0,empty.setBits());
  String deSerialize="tt7767422:3:8:24:eJz7//8/AAX9Av4=";List<String> show24=new ArrayList<>();for(int i=0;i<24;i++)show24.add(i==23?"tt7767422:3:8":"tt7767422:x:"+i);
  StremioWriterBits field24=StremioWriterBits.parse(deSerialize,show24);
  assertEquals("eJz7//8/AAX9Av4=",StremioWriterBits.compress(StremioWriterBits.decompress("eJz7//8/AAX9Av4=")));assertTrue(field24.watched("tt7767422:3:8"));assertEquals("test_watched_field_de_serialize",deSerialize,field24.toString());
  StremioWriterBits anchored=StremioWriterBits.empty(range("tt0:1:",1,20));anchored.mark("tt0:1:16");assertTrue("anchor points to the last watched video",anchored.toString().startsWith("tt0:1:16:16:"));
  assertArrayEquals("bitfield8 vector",new byte[]{31,0},StremioWriterBits.decompress("eJyTZwAAAEAAIA=="));assertEquals("eJyTZwAAAEAAIA==",StremioWriterBits.compress(new byte[]{31,0}));
 }
 @Test public void bitfieldRealignsAndReportsBitsItWouldLose()throws Exception{
  List<String> nine=range("tt2934286:1:",1,9);String v="tt2934286:1:5:5:eJyTZwAAAEAAIA==";List<String> specials=new ArrayList<>(Arrays.asList("tt2934286:0:1","tt2934286:0:2","tt2934286:0:3"));specials.addAll(nine);
  StremioWriterBits moved=StremioWriterBits.parse(v,specials);for(int i=1;i<=5;i++)assertTrue(moved.watched("tt2934286:1:"+i));assertFalse(moved.watched("tt2934286:0:3"));assertFalse(moved.lostRemoteBits());
  assertTrue(StremioWriterBits.parse(v,nine.subList(2,9)).lostRemoteBits());assertTrue(StremioWriterBits.parse("tt9:1:1:1:eJyTZwAAAEAAIA==",nine).lostRemoteBits());
  assertEquals("the phone's import decoder reads the writer's encoding the same way",nine.subList(0,5),PhoneHistory.decodeWatched(moved.toString(),specials));
  List<JSONObject> list=Arrays.asList(new JSONObject().put("id","s:2:1").put("season",2).put("episode",1),new JSONObject().put("id","s:1:2").put("season",1).put("episode",2),new JSONObject().put("id","s:0:1").put("season",0).put("episode",1),
   new JSONObject().put("id","s:1:1b").put("season",1).put("episode",1).put("released","2021-01-01T00:00:00.000Z"),new JSONObject().put("id","s:1:1a").put("season",1).put("number",1).put("released","2020-01-01T00:00:00.000Z"),new JSONObject().put("id","x"));
  assertEquals("ordered like stremio-core",Arrays.asList("x","s:0:1","s:1:1a","s:1:1b","s:1:2","s:2:1"),StremioWriterBits.order(list));
  try{StremioWriterBits.parse("nonsense",nine);fail();}catch(IOException expected){}
 }

 // ---- Writer ----
 static final String MOVIE_REMOTE="{\"_id\":\"tt0111161\",\"name\":\"Fixture Movie\",\"type\":\"movie\",\"removed\":true,\"temp\":true,\"_ctime\":\"2020-01-01T00:00:00.000Z\",\"_mtime\":\"2020-01-02T00:00:00.000Z\",\"futureField\":{\"nested\":[1,2.5,\"x\"]},\"state\":{\"timesWatched\":0,\"flaggedWatched\":0,\"lastWatched\":\"2019-05-05T00:00:00.000Z\",\"timeOffset\":1234,\"stateExtra\":true},\"behaviorHints\":{\"defaultVideoId\":\"tt0111161\"}}";
 static JSONObject movie(String imdb)throws JSONException{return new JSONObject().put("id","m-"+imdb).put("imdb",imdb).put("movie",true).put("title","Fixture Movie");}
 static JSONObject episode(int e)throws JSONException{return new JSONObject().put("id","e"+e).put("imdb",SHOW).put("movie",false).put("season",1).put("episode",e).put("title","Fixture Show");}
 static final long WATCHED=java.time.Instant.parse("2026-11-10T21:30:00Z").toEpochMilli();

 @Test public void offByDefaultMakesNoCalls()throws Exception{
  Fake fake=new Fake();StremioWriter w=new StremioWriter(new File(root,"o"),fake,id->videos(id,1),()->"key-12345678",()->now);
  assertFalse(w.enabled);assertFalse(w.addTitles);assertEquals(StremioWriter.Status.OFF,w.mark(movie("tt0111161"),WATCHED).status);assertTrue(fake.calls.isEmpty());
 }
 @Test public void movieReadModifyWriteKeepsUnknownFieldsSavesTheOriginalAndRestores()throws Exception{
  Fake fake=new Fake();fake.items.put("tt0111161",MOVIE_REMOTE);StremioWriter w=writer(fake,1,"key-12345678");
  StremioWriter.Result r=w.mark(movie("tt0111161"),WATCHED);assertEquals(StremioWriter.Status.MARKED,r.status);assertTrue(r.succeeded());
  assertEquals(Arrays.asList("datastoreGet","datastorePut"),fake.methods());JSONObject get=new JSONObject(fake.calls.get(0)[1]);assertEquals("tt0111161",get.getJSONArray("ids").getString(0));assertEquals("key-12345678",get.getString("authKey"));assertFalse(get.getBoolean("all"));
  JSONObject put=fake.lastPut(),state=put.getJSONObject("state");
  assertEquals("{\"nested\":[1,2.5,\"x\"]}",put.getJSONObject("futureField").toString());assertTrue(state.getBoolean("stateExtra"));assertEquals(1234,state.getInt("timeOffset"));
  assertTrue("remote removed kept",put.getBoolean("removed"));assertTrue("remote temp kept",put.getBoolean("temp"));assertEquals("2020-01-01T00:00:00.000Z",put.getString("_ctime"));
  assertEquals(StremioWriter.stamp(now),put.getString("_mtime"));assertEquals(1,state.getInt("timesWatched"));assertEquals(1,state.getInt("flaggedWatched"));assertEquals("2026-11-10T21:30:00.000Z",state.getString("lastWatched"));
  File original=new File(root,"stremio-originals/tt0111161.json");assertTrue(original.isFile());assertEquals(new JSONObject(MOVIE_REMOTE).toString(),read(original));assertTrue(w.hasOriginal("tt0111161"));
  fake.calls.clear();assertEquals(StremioWriter.Status.ALREADY_WATCHED,w.mark(movie("tt0111161"),WATCHED).status);assertEquals("an already watched movie is never rewritten",0,fake.puts());
  fake.calls.clear();fake.items.put("tt0111161",fake.items.get("tt0111161").replace("\"removed\":true","\"removed\":false"));now+=60000;
  assertEquals(StremioWriter.Status.RESTORED,w.restore("tt0111161").status);JSONObject back=fake.lastPut();
  assertEquals(0,back.getJSONObject("state").getInt("timesWatched"));assertEquals("{\"nested\":[1,2.5,\"x\"]}",back.getJSONObject("futureField").toString());assertFalse("today's removed",back.getBoolean("removed"));assertTrue("today's temp",back.getBoolean("temp"));
  assertEquals(StremioWriter.stamp(now),back.getString("_mtime"));assertEquals("the original is kept",new JSONObject(MOVIE_REMOTE).toString(),read(original));
  assertEquals(Collections.singletonList("tt0111161"),w.originals());
 }
 @Test public void aTitleTheAccountLacksIsSkippedUnlessAddIsOn()throws Exception{
  Fake fake=new Fake();StremioWriter w=writer(fake,1,"key-12345678");
  StremioWriter.Result skipped=w.mark(movie("tt0000001"),WATCHED);assertEquals(StremioWriter.Status.NOT_IN_LIBRARY,skipped.status);assertTrue(skipped.title());assertFalse(skipped.site());assertEquals("Not in your Stremio library",skipped.message);
  assertEquals(0,fake.puts());assertFalse(fake.items.containsKey("tt0000001"));assertFalse(w.hasOriginal("tt0000001"));
  w.addTitles=true;assertEquals(StremioWriter.Status.ADDED,w.mark(movie("tt0000001"),WATCHED).status);JSONObject added=fake.lastPut();
  assertEquals("tt0000001",added.getString("_id"));assertEquals("movie",added.getString("type"));assertFalse(added.getBoolean("removed"));assertEquals(1,added.getJSONObject("state").getInt("timesWatched"));assertTrue(w.hasOriginal("tt0000001"));
  assertEquals(StremioWriter.Status.RESTORED,w.restore("tt0000001").status);assertTrue("a title Noir added is marked removed",fake.lastPut().getBoolean("removed"));
 }
 /* Restore leaves a title alone when Stremio changed it after Noir's last write (its _mtime is newer); untouched ones are put back and the summary counts both. */
 @Test public void restoreSkipsTitlesChangedInStremioSinceNoirsWrite()throws Exception{
  Fake fake=new Fake();StremioWriter w=writer(fake,1,"key-12345678");String[] ids={"tt0000201","tt0000202","tt0000203"};
  for(String id:ids){fake.items.put(id,MOVIE_REMOTE.replace("tt0111161",id));assertEquals(StremioWriter.Status.MARKED,w.mark(movie(id),WATCHED).status);}
  String later=StremioWriter.stamp(now+3600000L);for(String id:new String[]{"tt0000202","tt0000203"})fake.items.put(id,new JSONObject(fake.items.get(id)).put("_mtime",later).toString());
  String touched=fake.items.get("tt0000202");now+=2*3600000L;fake.calls.clear();
  StremioWriter.Result single=w.restore("tt0000202");assertEquals("changed in Stremio after Noir's write: left alone",StremioWriter.Status.CHANGED_SINCE,single.status);assertEquals(0,fake.puts());assertEquals(touched,fake.items.get("tt0000202"));assertTrue(w.hasOriginal("tt0000202"));
  fake.calls.clear();boolean was=Loc.arabic;StremioWriter.Summary mixed;String en,ar;
  try{Loc.arabic=false;mixed=w.restore(Arrays.asList(ids));en=mixed.text();Loc.arabic=true;ar=mixed.text();}finally{Loc.arabic=was;}
  assertEquals(1,mixed.restored);assertEquals(2,mixed.kept);assertTrue(mixed.failed.isEmpty());assertEquals("only the untouched title is written",1,fake.puts());assertEquals("tt0000201",fake.lastPut().getString("_id"));assertEquals(0,fake.lastPut().getJSONObject("state").getInt("timesWatched"));
  assertEquals(touched,fake.items.get("tt0000202"));
  assertEquals("1 restored, 2 left as they are because they changed in Stremio since",en);
  assertEquals("\u0627\u0633\u062a\u064f\u0639\u064a\u062f \u0639\u0646\u0635\u0631 \u0648\u0627\u062d\u062f\u060c \u0648\u062a\u064f\u0631\u0643 \u0639\u0646\u0635\u0631\u0627\u0646 \u0643\u0645\u0627 \u0647\u0645\u0627 \u0644\u0623\u0646\u0647\u0645\u0627 \u062a\u063a\u064a\u0651\u0631\u0627 \u0641\u064a Stremio \u0645\u0646\u0630 \u0630\u0644\u0643 \u0627\u0644\u062d\u064a\u0646",ar);
  fake.calls.clear();StremioWriter.Summary again=w.restore(Collections.singletonList("tt0000201"));assertEquals("an untouched title (including one Noir restored itself) is restored",1,again.restored);assertEquals(0,again.kept);assertEquals(1,fake.puts());
 }
 @Test public void seriesSetsTheBitKeepsEveryBitAndNeverClearsOne()throws Exception{
  String seriesRemote="{\"_id\":\"tt2934286\",\"name\":\"Fixture Show\",\"type\":\"series\",\"removed\":false,\"temp\":false,\"_mtime\":\"2020-01-02T00:00:00.000Z\",\"extra\":[{}],\"state\":{\"watched\":\"tt2934286:1:5:5:eJyTZwAAAEAAIA==\",\"video_id\":\"tt2934286:1:5\",\"timesWatched\":0,\"lastWatched\":\"2027-01-01T00:00:00.000Z\"}}";
  Fake fake=new Fake();fake.items.put(SHOW,seriesRemote);StremioWriter w=writer(fake,1,"key-12345678");
  assertEquals(StremioWriter.Status.MARKED,w.mark(episode(7),WATCHED).status);JSONObject put=fake.lastPut(),state=put.getJSONObject("state");
  StremioWriterBits written=StremioWriterBits.parse(state.getString("watched"),range(SHOW+":1:",1,9));
  assertTrue(state.getString("watched").startsWith(SHOW+":1:7:7:"));for(int e:new int[]{1,2,3,4,5,7})assertTrue("episode "+e,written.watched(SHOW+":1:"+e));assertFalse(written.watched(SHOW+":1:6"));
  assertEquals(SHOW+":1:5",state.getString("video_id"));assertEquals("a newer lastWatched is kept","2027-01-01T00:00:00.000Z",state.getString("lastWatched"));assertEquals("[{}]",put.getJSONArray("extra").toString());
  fake.calls.clear();assertEquals(StremioWriter.Status.ALREADY_WATCHED,w.mark(episode(2),WATCHED).status);assertEquals(0,fake.puts());
  w.mark(episode(8),WATCHED);assertEquals(1,fake.puts());assertEquals("the original is saved only once",new JSONObject(seriesRemote).toString(),read(new File(root,"stremio-originals/"+SHOW+".json")));
  fake.calls.clear();StremioWriter shorter=writer(fake,3,"key-12345678");
  assertEquals("a list that would drop watched episodes writes nothing",StremioWriter.Status.OUT_OF_STEP,shorter.mark(episode(9),WATCHED).status);
  JSONObject season4=episode(1).put("season",4);assertEquals(StremioWriter.Status.UNKNOWN_EPISODE,w.mark(season4,WATCHED).status);assertEquals(0,fake.puts());
  int[] fetched={0};StremioWriter fresh=writer(fake,1,"key-12345678");fresh.refetch=id->{fetched[0]++;List<JSONObject> l=videos(id,1);l.add(new JSONObject().put("id",id+":1:10").put("season",1).put("episode",10));return l;};
  assertEquals("an episode newer than the kept list is fetched again once",StremioWriter.Status.MARKED,fresh.mark(episode(10),WATCHED).status);assertEquals(1,fetched[0]);
 }
 @Test public void failuresAreSiteLevelOrPerTitle()throws Exception{
  Fake fake=new Fake();fake.items.put("tt0111161",MOVIE_REMOTE);
  StremioWriter.Result server=with(fake,m->new StremioWriter.Response(502,""),"key-12345678"),unauthorized=with(fake,m->new StremioWriter.Response(401,""),"key-12345678"),
   session=with(fake,m->ok("{\"error\":{\"message\":\"Session does not exist\",\"code\":1}}"),"key-12345678"),network=with(fake,m->{throw new IOException("offline");},"key-12345678"),
   garbage=with(fake,m->ok("<html>"),"key-12345678"),noKey=with(fake,m->null,"");
  StremioWriter.Result badId=writer(fake,1,"key-12345678").mark(movie("local-1"),WATCHED);
  assertEquals(StremioWriter.Status.SERVER,server.status);assertEquals(StremioWriter.Status.AUTH,unauthorized.status);assertEquals(StremioWriter.Status.AUTH,session.status);assertEquals(StremioWriter.Status.NETWORK,network.status);
  assertEquals(StremioWriter.Status.BAD_RESPONSE,garbage.status);assertEquals(StremioWriter.Status.NO_KEY,noKey.status);assertEquals(StremioWriter.Status.UNKNOWN_ID,badId.status);
  for(StremioWriter.Result r:new StremioWriter.Result[]{server,unauthorized,session,network,garbage,noKey}){assertTrue(r.status+" is site-level",r.site());assertFalse(r.title());}
  assertTrue(badId.title());assertTrue(StremioWriter.reconnect(session.message));assertTrue(StremioWriter.reconnect(noKey.message));assertFalse(StremioWriter.reconnect(server.message));
  assertEquals("no write on any failure",0,fake.puts());
 }
 StremioWriter.Result with(Fake fake,Fake.Respond respond,String key)throws Exception{fake.override=respond;try{return writer(fake,1,key).mark(movie("tt0111161"),WATCHED);}finally{fake.override=null;}}

 @Test public void everyMessageHasArabic()throws Exception{Class.forName("app.noir.mobile.StremioWriter");Class.forName("app.noir.mobile.PhoneHarbor");
  List<String> all=new ArrayList<>();for(StremioWriter.Status s:StremioWriter.Status.values())all.add(StremioWriter.message(s));
  all.addAll(Arrays.asList("Stremio marks go through your PC","Stremio marks","Mark watched in your Stremio account (also seen by Harbor)","Add watched titles to my Stremio library","New: try it on one title first, then check it in Stremio.","Bring in Stremio's library and watched episodes.","Connect Stremio again","Restore a title","Restore",
   "Marks what you finish on this phone as watched in your Stremio account, using your Stremio sign-in here.","Puts this title back in your Stremio account as it was before Noir first changed it. If it changed in Stremio since Noir's last change, it is left as it is."));
  for(String s:all){assertTrue(s,Loc.AR.containsKey(s));assertTrue(s,Loc.AR.get(s).matches("(?s).*[\\u0600-\\u06FF].*"));}
 }
 static String read(File f)throws IOException{return new String(java.nio.file.Files.readAllBytes(f.toPath()),StandardCharsets.UTF_8);}
}
