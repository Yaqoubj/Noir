package app.noir.mobile;
import org.json.*;
import javax.net.ssl.HttpsURLConnection;
import java.io.*;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.time.*;
import java.util.*;
import java.util.concurrent.*;
import java.util.zip.*;

/** The phone's history tools: match imported rows on Cinemeta, apply them as undoable imports,
 *  sources, backups, Stremio and two-way Nuvio. Works without the PC; the PC learns on the next sync. */
final class PhoneHistoryIo {
    final DataStore data;final DiscoveryClient client;
    PhoneHistoryIo(DataStore data,DiscoveryClient client){this.data=data;this.client=client;}

    static final class View {int season,episode,rating;long at;PhoneHistory.Row row;View(int s,int e,long at,int rating,PhoneHistory.Row row){season=s;episode=e;this.at=at;this.rating=rating;this.row=row;}}
    static final class Title {JSONObject details;List<View> views=new ArrayList<>();boolean movie(){return details.optString("noirType").equals("movie");}}
    static final class Plan {String service;boolean lookupFailed;List<Title> titles=new ArrayList<>();List<String> unmatched=new ArrayList<>();
        int viewings(){int n=0;for(Title t:titles)n+=t.views.size();return n;}int undated(){int n=0;for(Title t:titles)for(View v:t.views)if(v.at==0)n++;return n;}}
    interface Progress{void report(int done,int total);}

    // ---- Matching (the PC's rules: IMDb ID, TVDB via TVmaze, then exact or unique near title) ----
    Plan resolve(String service,List<PhoneHistory.Row> rows,Progress progress)throws InterruptedException{
        Map<String,List<PhoneHistory.Row>> groups=new LinkedHashMap<>();for(PhoneHistory.Row r:rows)groups.computeIfAbsent(r.key(),k->new ArrayList<>()).add(r);
        Plan plan=new Plan();plan.service=service;ExecutorService pool=Executors.newFixedThreadPool(4);List<Future<Object[]>> futures=new ArrayList<>();
        int[] done={0};
        for(List<PhoneHistory.Row> group:groups.values())futures.add(pool.submit(()->{try{return match(group);}finally{synchronized(done){done[0]++;if(progress!=null)progress.report(done[0],groups.size());}}}));
        pool.shutdown();
        for(Future<Object[]> f:futures){try{Object[] r=f.get();if(r[0]!=null)plan.titles.add((Title)r[0]);@SuppressWarnings("unchecked")List<String> u=(List<String>)r[1];plan.unmatched.addAll(u);plan.lookupFailed|=(Boolean)r[2];}catch(ExecutionException e){plan.lookupFailed=true;plan.unmatched.add("A title couldn't be looked up.");}}
        return plan;
    }
    Object[] match(List<PhoneHistory.Row> group){
        PhoneHistory.Row row=group.get(0);List<String> unmatched=new ArrayList<>();String id=row.imdb.matches("tt\\d+")?row.imdb:"";boolean failed=false;
        if(id.isEmpty()&&!row.tvdb.isEmpty())try{id=PhoneHistory.s(new JSONObject(new String(DiscoveryClient.fetch("https://api.tvmaze.com/lookup/shows?thetvdb="+row.tvdb,1024*1024),StandardCharsets.UTF_8)).optJSONObject("externals"),"imdb");}catch(Exception ignored){failed=true;}
        JSONObject details=null;
        for(boolean movie:row.movie!=null?new boolean[]{row.movie}:new boolean[]{true,false}){
            if(Thread.currentThread().isInterrupted())break;JSONObject found=null;
            try{
                if(id.matches("tt\\d+")){found=new JSONObject();found.put("id",id);found.put("noirType",movie?"movie":"series");}
                else{String query=!movie&&row.movie==null?row.title.split(": ")[0]:row.title;found=pick(client.search(query,movie?"Movies":"TV shows"),query,row.year,movie);}
                if(found==null)continue;details=client.details(found);break;
            }catch(Exception ignored){failed=true;}
        }
        if(details==null){for(PhoneHistory.Row r:group)unmatched.add(label(r)+(failed?" — title lookup failed; try again later":" — no matching title was found"));return new Object[]{null,unmatched,failed};}
        Title title=new Title();title.details=details;List<JSONObject> episodes=DiscoveryClient.episodes(details);
        for(PhoneHistory.Row r:group){
            if(title.movie()){title.views.add(new View(0,0,r.at,r.rating,r));continue;}
            if(r.service.equals("Pasted list")){title.views.add(new View(r.season,r.episode,0,0,r));continue;}
            JSONObject e=episode(episodes,r);if(e==null){unmatched.add(label(r)+" — no matching episode");continue;}
            title.views.add(new View(e.optInt("season"),e.optInt("episode"),r.at,r.rating,r));
        }
        return new Object[]{title.views.isEmpty()?null:title,unmatched,false};
    }
    static String label(PhoneHistory.Row r){return r.title+(r.season>0?String.format(Locale.ROOT," · S%02dE%02d",r.season,r.episode):"")+(r.episodeTitle.isEmpty()?"":" · "+r.episodeTitle);}
    static int year(JSONObject t){return PhoneSeasonPlan.year(t.optString("releaseInfo"));}
    static JSONObject pick(JSONArray found,String query,int year,boolean movie){
        String wanted=PhoneHistory.normalize(query);List<JSONObject> kind=new ArrayList<>();for(JSONObject t:DataStore.objects(found))if(t.optString("noirType").equals(movie?"movie":"series")&&(year==0||year(t)==0||Math.abs(year(t)-year)<=1))kind.add(t);
        List<JSONObject> exact=new ArrayList<>();for(JSONObject t:kind)if(PhoneHistory.normalize(t.optString("name")).equals(wanted))exact.add(t);
        exact.sort(Comparator.comparingInt(t->year==0?0:Math.abs(year(t)-year)));
        Set<Integer> years=new HashSet<>();for(JSONObject t:exact)years.add(year(t));
        // Without a year, two remakes with the same title are a guess: leave it unmatched.
        if(!exact.isEmpty())return year==0&&years.size()>1?null:exact.get(0);
        if(wanted.length()>=4){List<JSONObject> near=new ArrayList<>();for(JSONObject t:kind){String n=PhoneHistory.normalize(t.optString("name"));if(!n.isEmpty()&&(n.startsWith(wanted)||wanted.startsWith(n))&&Math.min(n.length(),wanted.length())>=.6*Math.max(n.length(),wanted.length()))near.add(t);}if(near.size()==1)return near.get(0);}
        if(query.contains(": ")){String head=PhoneHistory.normalize(query.split(": ")[0]);List<JSONObject> main=new ArrayList<>();for(JSONObject t:kind)if(PhoneHistory.normalize(t.optString("name")).equals(head))main.add(t);if(head.length()>=4&&main.size()==1)return main.get(0);}
        return null;
    }
    static JSONObject episode(List<JSONObject> episodes,PhoneHistory.Row r){
        if(r.season>0&&r.episode>0){for(JSONObject e:episodes)if(e.optInt("season")==r.season&&e.optInt("episode")==r.episode)return e;return null;}
        List<JSONObject> candidates=new ArrayList<>();for(JSONObject e:episodes)if(r.season==0||e.optInt("season")==r.season)candidates.add(e);
        String name=PhoneHistory.normalize(r.episodeTitle);
        if(!name.isEmpty())for(List<JSONObject> pool:List.of(candidates,episodes))for(JSONObject e:pool)if(PhoneHistory.normalize(e.optString("name")).equals(name))return e;
        if(name.length()>=5)for(List<JSONObject> pool:List.of(candidates,episodes)){List<JSONObject> near=new ArrayList<>();for(JSONObject e:pool){String t=PhoneHistory.normalize(e.optString("name"));if(t.length()>=5&&(t.contains(name)||name.contains(t)))near.add(e);}if(near.size()==1)return near.get(0);}
        java.util.regex.Matcher m=java.util.regex.Pattern.compile("(?i)^(?:episode|chapter)\\s+(\\d+)\\s*$").matcher(r.episodeTitle);int n=r.episode>0?r.episode:m.find()?Integer.parseInt(m.group(1)):0;
        List<JSONObject> numbered=new ArrayList<>();for(JSONObject e:candidates)if(n>0&&e.optInt("episode")==n)numbered.add(e);return numbered.size()==1?numbered.get(0):null;
    }
    /** Pasted lines and quick picks: "S3" or "S3E5" means everything up to there. */
    Plan expandList(Plan plan){
        for(Title t:plan.titles){if(t.movie()||t.views.isEmpty())continue;PhoneHistory.Row r=t.views.get(0).row;if(r==null||!r.service.equals("Pasted list"))continue;
            t.views.clear();for(JSONObject e:DiscoveryClient.episodes(t.details)){if(!DiscoveryClient.released(e))continue;int s=e.optInt("season"),n=e.optInt("episode");if(r.season==0||s<r.season||s==r.season&&(r.episode==0||n<=r.episode))t.views.add(new View(s,n,0,0,r));}}
        plan.titles.removeIf(t->t.views.isEmpty());return plan;
    }

    // ---- Applying: one undoable batch, deduplicated against what's already here ----
    static final String[] WHEN={"When they aired","Around a year","Don't remember"};
    /** when: one of WHEN for undated views; year used by "Around a year". Returns viewings added. */
    int apply(Plan plan,String when,int aroundYear){
        String batch=UUID.randomUUID().toString();JSONArray added=new JSONArray(),rated=new JSONArray();String source="Imported from "+plan.service;
        for(Title t:plan.titles){
            List<JSONObject> items;try{items=PhoneSeasonPlan.discoverItems(t.details);}catch(Exception e){continue;}
            data.refreshPhoneTitle(items);
            for(View v:t.views){
                JSONObject template=null;for(JSONObject i:items)if(t.movie()||i.optInt("season")==v.season&&i.optInt("episode")==v.episode){template=i;break;}if(template==null)continue;
                JSONObject item=data.rememberPhoneItem(template);item=data.item(item.optString("id"));if(item==null)continue;
                long at=v.at;boolean unknown=false,approximate=v.row!=null&&v.row.approximate;
                if(at==0){
                    if(when.equals("When they aired")){long aired=LogActivity.airs(template);if(aired>0&&aired<=System.currentTimeMillis()){at=aired;approximate=true;}else unknown=true;}
                    else if(when.equals("Around a year")&&aroundYear>0){at=LocalDate.of(aroundYear,7,1).atTime(20,0).toInstant(ZoneOffset.UTC).toEpochMilli();approximate=true;}
                    else unknown=true;
                }
                String rowSource=v.row!=null&&!v.row.source.isEmpty()?v.row.source:source;
                if(v.row!=null&&(v.row.service.equals("Noir")||v.row.service.equals("Backup"))?alreadyExact(item,at,unknown,rowSource):already(item,at,unknown))continue;
                String viewing=data.markImported(item.optString("id"),at,unknown,approximate,rowSource,v.row==null?0:v.row.loggedAt);
                added.put(new JSONArray().put(item.optString("id")).put(viewing));
                JSONObject review=item.optJSONObject("review");
                if((v.rating>0||v.row!=null&&!v.row.note.isEmpty())&&(review==null||review.optInt("rating")==0&&review.optString("note").isEmpty())){data.review(item.optString("id"),v.rating,v.row==null?"":v.row.note);if(v.rating>0)rated.put(item.optString("id"));}
            }
        }
        if(added.length()>0||rated.length()>0){JSONArray batches=data.stateArray("importBatches");JSONObject b=new JSONObject();DataStore.put(b,"id",batch);DataStore.put(b,"service",plan.service);DataStore.put(b,"at",System.currentTimeMillis());DataStore.put(b,"viewings",added);DataStore.put(b,"rated",rated);batches.put(b);
            JSONArray kept=new JSONArray();for(int n=Math.max(0,batches.length()-30);n<batches.length();n++)kept.put(batches.opt(n));data.stateArray("importBatches",kept);lastBatch=batch;}
        data.syncSoon();return added.length();
    }
    String lastBatch="";
    // Re-importing never duplicates: a dated viewing within 12 hours, or any viewing for an undated one, counts as present.
    static boolean already(JSONObject item,long at,boolean unknown){List<JSONObject> views=DataStore.objects(item.optJSONArray("viewings"));if(unknown)return !views.isEmpty();for(JSONObject v:views)if(!v.optBoolean("dateUnknown")&&Math.abs(DataStore.date(v.optString("watchedAt"))-at)<12L*3600000)return true;return false;}
    static boolean alreadyExact(JSONObject item,long at,boolean unknown,String source){for(JSONObject v:DataStore.objects(item.optJSONArray("viewings")))if(v.optBoolean("dateUnknown")==unknown&&(unknown||DataStore.date(v.optString("watchedAt"))==at)&&v.optString("source").equals(source))return true;return false;}
    int undo(String batchId){
        JSONArray batches=data.stateArray("importBatches"),kept=new JSONArray();int n=0;
        for(JSONObject b:DataStore.objects(batches)){if(!b.optString("id").equals(batchId)){kept.put(b);continue;}
            for(int i=0;i<b.optJSONArray("viewings").length();i++){JSONArray pair=b.optJSONArray("viewings").optJSONArray(i);data.unview(pair.optString(0),pair.optString(1));n++;}
            JSONArray rated=b.optJSONArray("rated");if(rated!=null)for(int i=0;i<rated.length();i++)data.review(rated.optString(i),0,"");}
        data.stateArray("importBatches",kept);if(lastBatch.equals(batchId))lastBatch="";data.syncSoon();return n;
    }
    /** source → {viewings, titles}. */
    Map<String,int[]> sources(){Map<String,int[]> result=new TreeMap<>();Map<String,Set<String>> titles=new HashMap<>();
        for(JSONObject i:DataStore.objects(data.items()))for(JSONObject v:DataStore.objects(i.optJSONArray("viewings"))){String s=v.optString("source","Noir");result.computeIfAbsent(s,k->new int[2])[0]++;titles.computeIfAbsent(s,k->new HashSet<>()).add(i.optString("group")+i.optString("id"));}
        for(Map.Entry<String,int[]> e:result.entrySet())e.getValue()[1]=titles.get(e.getKey()).size();return result;}
    int removeSource(String source){int n=0;for(JSONObject i:DataStore.objects(data.items()))for(JSONObject v:DataStore.objects(i.optJSONArray("viewings")))if(v.optString("source","Noir").equals(source)){data.unview(i.optString("id"),v.optString("id"));n++;}data.syncSoon();return n;}

    // ---- Backups: a zip with a manifest, the lossless Noir JSON history and the phone's own state ----
    void writeBackup(OutputStream out)throws Exception{
        List<JSONObject> items=DataStore.objects(data.items());JSONObject ratings=data.settings().optJSONObject("showRatings");
        try(ZipOutputStream zip=new ZipOutputStream(out)){
            zip.putNextEntry(new ZipEntry("manifest.json"));zip.write(new JSONObject().put("format",1).put("app","Noir Android").put("created",Instant.now().toString()).toString().getBytes(StandardCharsets.UTF_8));zip.closeEntry();
            zip.putNextEntry(new ZipEntry("history.json"));zip.write(((String)PhoneHistory.export(items,ratings,"noir-json")[0]).getBytes(StandardCharsets.UTF_8));zip.closeEntry();
            zip.putNextEntry(new ZipEntry("phone.json"));zip.write(data.backupState().toString().getBytes(StandardCharsets.UTF_8));zip.closeEntry();
        }
    }
    static final class Backup {List<PhoneHistory.Row> rows=new ArrayList<>();JSONObject phone;String created="";int viewings(){return rows.size();}}
    /** Reads a phone backup or a PC .noirbackup (its library.json). */
    static Backup readBackup(byte[] bytes)throws Exception{
        Backup b=new Backup();
        try(ZipInputStream in=new ZipInputStream(new ByteArrayInputStream(bytes))){ZipEntry e;while((e=in.getNextEntry())!=null){String n=new File(e.getName()).getName();byte[] d=DataStore.read(in,128*1024*1024);String text=new String(d,StandardCharsets.UTF_8);
            if(n.equals("history.json"))b.rows.addAll(PhoneHistory.json(text).rows);
            else if(n.equals("phone.json"))b.phone=new JSONObject(text);
            else if(n.equals("manifest.json"))b.created=new JSONObject(text).optString("created");
            else if(n.equals("library.json"))b.rows.addAll(pcRows(new JSONObject(text)));}}
        if(b.rows.isEmpty()&&b.phone==null)throw new IOException("This isn't a Noir backup.");return b;
    }
    // A PC backup's catalog and viewings as lossless Noir rows (matched by IMDb ID, so no searching).
    static List<PhoneHistory.Row> pcRows(JSONObject state){
        List<PhoneHistory.Row> rows=new ArrayList<>();JSONObject catalog=state.optJSONObject("Catalog"),viewings=state.optJSONObject("Viewings"),reviews=state.optJSONObject("PersonalReviews"),showRatings=state.optJSONObject("ShowRatings");
        if(catalog==null||viewings==null)return rows;
        for(Iterator<String> it=viewings.keys();it.hasNext();){String id=it.next();JSONObject entry=catalog.optJSONObject(id);JSONObject item=entry==null?null:entry.optJSONObject("Item");if(item==null)continue;
            JSONObject review=reviews==null?null:reviews.optJSONObject(id);boolean movie=item.optBoolean("IsMovie");
            for(JSONObject v:DataStore.objects(viewings.optJSONArray(id))){PhoneHistory.Row r=new PhoneHistory.Row("Backup",item.optString("Title"),item.optInt("Year"),movie,item.optString("ImdbId"),"",movie?0:item.optInt("Season"),movie?0:item.optInt("Episode"),item.optString("EpisodeTitle"),v.optBoolean("DateUnknown")?0:PhoneHistory.date(v.optString("WatchedAt"),null),review==null?0:review.optInt("Rating"));
                r.source=v.optString("Source");r.loggedAt=PhoneHistory.date(v.optString("LoggedAt"),null);r.approximate=v.optBoolean("Approximate");r.note=review==null?"":review.optString("Note");rows.add(r);}}
        return rows;
    }

    // ---- Stremio: the library behind the sign-in made in the WebView ----
    List<PhoneHistory.Row> stremio(String authKey)throws Exception{
        JSONObject body=new JSONObject().put("authKey",authKey).put("collection","libraryItem").put("ids",new JSONArray()).put("all",true);
        JSONObject response=post("https://api.strem.io/api/datastoreGet",body.toString(),null);JSONArray result=response.optJSONArray("result");
        if(result==null)throw new IOException("Stremio did not return a library. Connect again.");
        List<PhoneHistory.Row> rows=new ArrayList<>();
        for(JSONObject item:DataStore.objects(result)){
            if(item.optBoolean("temp"))continue;String id=item.optString("_id");if(!id.matches("tt\\d+"))continue;
            JSONObject state=item.optJSONObject("state");if(state==null)state=new JSONObject();String type=item.optString("type"),title=item.optString("name");long at=PhoneHistory.date(state.optString("lastWatched"),null);
            boolean flagged=state.optInt("timesWatched")>0||state.optInt("flaggedWatched")>0||state.optBoolean("flaggedWatched");
            if(type.equals("movie")){if(flagged)rows.add(new PhoneHistory.Row("Stremio",title,0,true,id,"",0,0,"",at,0));continue;}
            if(!type.equals("series"))continue;
            String current=state.optString("video_id"),encoded=state.optString("watched");List<String> ids=new ArrayList<>();boolean failed=encoded.isEmpty();
            if(!failed)try{List<String> videos=new ArrayList<>();JSONObject seed=new JSONObject().put("id",id).put("noirType","series");for(JSONObject v:DataStore.objects(client.details(seed).optJSONArray("videos")))videos.add(v.optString("id"));ids=PhoneHistory.decodeWatched(encoded,videos);}catch(Exception e){failed=true;}
            // The last opened episode may be half-watched; only trust it when Stremio counted a finished watch.
            if(failed&&!current.isEmpty()&&flagged)ids.add(current);
            for(String video:new LinkedHashSet<>(ids)){java.util.regex.Matcher m=java.util.regex.Pattern.compile("^"+java.util.regex.Pattern.quote(id)+":(\\d+):(\\d+)$").matcher(video);if(m.find())rows.add(new PhoneHistory.Row("Stremio",title,0,false,id,"",Integer.parseInt(m.group(1)),Integer.parseInt(m.group(2)),"",video.equals(current)?at:0,0));}
        }
        return rows;
    }

    // ---- Nuvio: TV-style link code, then two-way watched sync ----
    static final String NUVIO="https://api.nuvio.tv",NUVIO_FALLBACK="https://api-two.nuvioapp.space",NUVIO_LINK="https://nuvio.tv/link";
    static final String NUVIO_KEY="eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJyb2xlIjoiYW5vbiIsImlzcyI6InN1cGFiYXNlIiwiaWF0IjoxNzgxNTIxMzQ2LCJleHAiOjE5MzkyMDEzNDZ9.tmQaj682pwzehpqlgCDMnySOqiUvpgRbrE43T4VJpDI";
    static final class NuvioExpired extends IOException{NuvioExpired(){super("Your Nuvio sign-in has expired. Connect again.");}}
    static Object post(String url,String body,String bearer,boolean any)throws Exception{
        HttpsURLConnection c=(HttpsURLConnection)new URL(url).openConnection();c.setConnectTimeout(8000);c.setReadTimeout(30000);c.setRequestMethod("POST");c.setDoOutput(true);c.setRequestProperty("Content-Type","application/json");c.setRequestProperty("User-Agent","Noir-Android");
        if(url.startsWith(NUVIO)||url.startsWith(NUVIO_FALLBACK)){c.setRequestProperty("apikey",NUVIO_KEY);c.setRequestProperty("Authorization","Bearer "+(bearer==null?NUVIO_KEY:bearer));}
        try{try(OutputStream o=c.getOutputStream()){o.write(body.getBytes(StandardCharsets.UTF_8));}int status=c.getResponseCode();
            if(status==401||status==403)throw new NuvioExpired();
            if(status<200||status>=300)throw new IOException("Service answered "+status+".");
            String text;try(InputStream in=c.getInputStream()){text=new String(DataStore.read(in,32*1024*1024),StandardCharsets.UTF_8).trim();}
            return text.isEmpty()?JSONObject.NULL:new JSONTokener(text).nextValue();}finally{c.disconnect();}
    }
    static JSONObject post(String url,String body,String bearer)throws Exception{Object o=post(url,body,bearer,true);return o instanceof JSONObject?(JSONObject)o:new JSONObject();}
    Object nuvio(String path,JSONObject body,boolean auth)throws Exception{
        JSONObject session=data.stateObject("nuvio");
        if(auth&&session.optLong("expiresAt")<System.currentTimeMillis()+60000)refreshNuvio();
        for(String root:new String[]{NUVIO,NUVIO_FALLBACK}){
            try{return post(root+path,body.toString(),auth?data.stateObject("nuvio").optString("access"):null,true);}
            catch(NuvioExpired e){if(!auth)throw e;refreshNuvio();return post(root+path,body.toString(),data.stateObject("nuvio").optString("access"),true);}
            catch(IOException e){if(root.equals(NUVIO_FALLBACK)||e.getMessage()!=null&&e.getMessage().startsWith("Service answered 4"))throw e;}
        }
        throw new IOException("Nuvio couldn't be reached.");
    }
    JSONObject first(Object o){return o instanceof JSONArray?((JSONArray)o).optJSONObject(0):o instanceof JSONObject?(JSONObject)o:new JSONObject();}
    void tokens(JSONObject j)throws IOException{JSONObject s=data.stateObject("nuvio");String access=j.optString("access_token");if(access.isEmpty())throw new IOException("Nuvio didn't return a sign-in.");
        DataStore.put(s,"access",access);if(!j.optString("refresh_token").isEmpty())DataStore.put(s,"refresh",j.optString("refresh_token"));DataStore.put(s,"expiresAt",System.currentTimeMillis()+Math.max(60,j.optLong("expires_in",3600))*1000);data.stateObject("nuvio",s);}
    void refreshNuvio()throws Exception{String refresh=data.stateObject("nuvio").optString("refresh");if(refresh.isEmpty())throw new NuvioExpired();
        try{tokens(post(NUVIO+"/auth/v1/token?grant_type=refresh_token",new JSONObject().put("refresh_token",refresh).toString(),null));}catch(IOException e){if(e.getMessage()!=null&&e.getMessage().contains(" 400"))throw new NuvioExpired();throw e;}}
    /** {deviceCode, userCode, url, interval, nonce} */
    JSONObject startNuvioLink()throws Exception{String nonce=UUID.randomUUID().toString();
        JSONObject j=first(nuvio("/rest/v1/rpc/start_device_login_session",new JSONObject().put("p_device_nonce",nonce).put("p_redirect_base_url",NUVIO_LINK).put("p_device_name","Noir on "+android.os.Build.MODEL).put("p_device_type","mobile"),false));
        if(j.optString("device_code").isEmpty())throw new IOException("Nuvio couldn't start a sign-in. Try again in a moment.");DataStore.put(j,"nonce",nonce);return j;}
    String pollNuvioLink(JSONObject link)throws Exception{return first(nuvio("/rest/v1/rpc/poll_tv_login_session",new JSONObject().put("p_code",link.optString("device_code")).put("p_device_nonce",link.optString("nonce")),false)).optString("status").trim().toLowerCase(Locale.ROOT);}
    void finishNuvioLink(JSONObject link)throws Exception{tokens(post(NUVIO+"/functions/v1/tv-logins-exchange",new JSONObject().put("code",link.optString("device_code")).put("device_nonce",link.optString("nonce")).toString(),null));}
    List<JSONObject> nuvioProfiles()throws Exception{Object o=nuvio("/rest/v1/rpc/sync_pull_profiles",new JSONObject(),true);List<JSONObject> list=o instanceof JSONArray?DataStore.objects((JSONArray)o):new ArrayList<>();list.sort(Comparator.comparingInt(p->p.optInt("profile_index",1)));return list;}
    void useNuvioProfile(int index,String name){JSONObject s=data.stateObject("nuvio");if(s.optInt("profile")!=index){DataStore.put(s,"cursor",0);DataStore.put(s,"known",new JSONObject());}DataStore.put(s,"profile",index);DataStore.put(s,"profileName",name);data.stateObject("nuvio",s);}
    boolean nuvioConnected(){JSONObject s=data.stateObject("nuvio");return s.optInt("profile")>0&&!s.optString("refresh").isEmpty();}
    static long normalize(long v){if(v<19000101000000L||v>29991231235959L)return v;try{return LocalDateTime.parse(String.valueOf(v),java.time.format.DateTimeFormatter.ofPattern("yyyyMMddHHmmss")).toInstant(ZoneOffset.UTC).toEpochMilli();}catch(Exception e){return v;}}
    static PhoneHistory.Row nuvioRow(JSONObject w){boolean episode=w.has("season")&&!w.isNull("season")&&w.has("episode")&&!w.isNull("episode");String type=w.optString("content_type");String id=w.optString("content_id");
        return new PhoneHistory.Row("Nuvio",w.optString("title"),0,episode?Boolean.FALSE:type.equals("movie")||type.equals("film")?Boolean.TRUE:null,id.matches("tt\\d+")?id:"","",episode?w.optInt("season"):0,episode?w.optInt("episode"):0,"",normalize(w.optLong("watched_at")),0);}
    static String nuvioKey(String id,int season,int episode,boolean movie){return movie?id+"||":id+"|"+season+"|"+episode;}
    /** Pull: everything the first time, then only changes. Returns {rows to import, removed count}. */
    Object[] pullNuvio(boolean full)throws Exception{
        JSONObject s=data.stateObject("nuvio");int profile=s.optInt("profile");long cursor=s.optLong("cursor");List<PhoneHistory.Row> rows=new ArrayList<>();int removed=0;
        if(full||cursor==0){
            Object c=nuvio("/rest/v1/rpc/sync_get_watched_items_delta_cursor",new JSONObject().put("p_profile_id",profile),true);long next=c instanceof Number?((Number)c).longValue():first(c).optLong("cursor");
            for(int page=1;page<=200;page++){Object o=nuvio("/rest/v1/rpc/sync_pull_watched_items",new JSONObject().put("p_profile_id",profile).put("p_page",page).put("p_page_size",500),true);List<JSONObject> items=o instanceof JSONArray?DataStore.objects((JSONArray)o):new ArrayList<>();for(JSONObject w:items)rows.add(nuvioRow(w));if(items.size()<500)break;}
            cursor=next;
        }else{
            Set<String> gone=new HashSet<>();Map<String,JSONObject> latest=new LinkedHashMap<>();
            for(int round=0;round<100;round++){Object o=nuvio("/rest/v1/rpc/sync_pull_watched_items_delta",new JSONObject().put("p_profile_id",profile).put("p_since_event_id",cursor).put("p_limit",500),true);List<JSONObject> events=o instanceof JSONArray?DataStore.objects((JSONArray)o):new ArrayList<>();
                for(JSONObject e:events){cursor=Math.max(cursor,e.optLong("event_id"));PhoneHistory.Row r=nuvioRow(e);String key=nuvioKey(e.optString("content_id"),r.season,r.episode,r.movie==Boolean.TRUE||r.season==0);JSONObject old=latest.get(key);if(old==null||old.optLong("event_id")<=e.optLong("event_id"))latest.put(key,e);}
                if(events.size()<500)break;}
            // Only each item's latest event counts: watched then un-checked stays un-checked.
            for(Map.Entry<String,JSONObject> e:latest.entrySet()){if(e.getValue().optString("operation").matches("(?i).*(del|remov).*"))gone.add(e.getKey());else rows.add(nuvioRow(e.getValue()));}
            // Nuvio removed something: drop only the viewings that came from Nuvio.
            for(JSONObject i:DataStore.objects(data.items())){if(!gone.contains(nuvioKey(i.optString("imdb"),i.optInt("season"),i.optInt("episode"),i.optBoolean("movie"))))continue;for(JSONObject v:DataStore.objects(i.optJSONArray("viewings")))if(v.optString("source").equals("Imported from Nuvio")){data.unview(i.optString("id"),v.optString("id"));removed++;}}
        }
        s=data.stateObject("nuvio");DataStore.put(s,"checkedAt",System.currentTimeMillis());data.stateObject("nuvio",s);
        return new Object[]{rows,removed,cursor};
    }
    // The cursor moves only once fetched changes are kept, so a failed lookup retries them next time.
    void commitNuvioCursor(long cursor){if(cursor<=0)return;JSONObject s=data.stateObject("nuvio");DataStore.put(s,"cursor",cursor);data.stateObject("nuvio",s);}
    static boolean lookupFailed(Plan p){return p.lookupFailed;
    }
    /** Push the phone's own watches (not imported ones) and un-checks of things Nuvio has. */
    int pushNuvio()throws Exception{
        JSONObject s=data.stateObject("nuvio");int profile=s.optInt("profile");JSONObject known=s.optJSONObject("known");if(known==null)known=new JSONObject();JSONArray items=new JSONArray();List<String> pushed=new ArrayList<>();Map<String,String> present=new HashMap<>();
        for(JSONObject i:DataStore.objects(data.items())){String id=i.optString("imdb");if(!id.matches("tt\\d+"))continue;boolean movie=i.optBoolean("movie");String key=nuvioKey(id,i.optInt("season"),i.optInt("episode"),movie);
            List<JSONObject> views=DataStore.objects(i.optJSONArray("viewings"));if(!views.isEmpty())present.put(key,"");
            for(JSONObject v:views){if(v.optString("source").equals("Imported from Nuvio")){known.put(key,true);continue;}if(v.optString("source").startsWith("Imported from"))continue;if(v.optBoolean("dateUnknown")||known.has(key))continue;
                JSONObject w=new JSONObject().put("content_id",id).put("content_type",movie?"movie":"series").put("title",i.optString("title")).put("watched_at",DataStore.date(v.optString("watchedAt")));if(!movie){w.put("season",i.optInt("season"));w.put("episode",i.optInt("episode"));}
                items.put(w);pushed.add(key);break;}}
        if(items.length()>0)nuvio("/rest/v1/rpc/sync_push_watched_items",new JSONObject().put("p_profile_id",profile).put("p_items",items),true);
        for(String k:pushed)known.put(k,true);
        JSONArray deletes=new JSONArray();List<String> forget=new ArrayList<>();
        for(Iterator<String> it=known.keys();it.hasNext();){String k=it.next();if(present.containsKey(k))continue;String[] p=k.split("\\|",-1);JSONObject d=new JSONObject().put("content_id",p[0]);if(!p[1].isEmpty()){d.put("season",Integer.parseInt(p[1]));d.put("episode",Integer.parseInt(p[2]));}else{d.put("season",JSONObject.NULL);d.put("episode",JSONObject.NULL);}deletes.put(d);forget.add(k);}
        if(deletes.length()>0)nuvio("/rest/v1/rpc/sync_delete_watched_items",new JSONObject().put("p_profile_id",profile).put("p_keys",deletes),true);
        for(String k:forget)known.remove(k);
        s=data.stateObject("nuvio");DataStore.put(s,"known",known);data.stateObject("nuvio",s);
        return items.length()+deletes.length();
    }
    /** Background check, at most every four hours while the app is open. */
    static void autoNuvio(DataStore data,android.content.Context context){
        JSONObject s=data.stateObject("nuvio");if(s.optInt("profile")<=0||s.optString("refresh").isEmpty()||!s.optBoolean("auto",true)||System.currentTimeMillis()-s.optLong("checkedAt")<4*3600000L)return;
        data.network.execute(()->{try{PhoneHistoryIo io=new PhoneHistoryIo(data,new DiscoveryClient(context));Object[] pulled=io.pullNuvio(false);@SuppressWarnings("unchecked")List<PhoneHistory.Row> rows=(List<PhoneHistory.Row>)pulled[0];
            Plan plan=io.resolve("Nuvio",rows,null);if(!rows.isEmpty())io.apply(plan,"Don't remember",0);if(!lookupFailed(plan))io.commitNuvioCursor((Long)pulled[2]);if(s.optBoolean("send",true))io.pushNuvio();}
            catch(NuvioExpired e){JSONObject x=data.stateObject("nuvio");DataStore.put(x,"refresh","");data.stateObject("nuvio",x);}catch(Exception ignored){}});
    }
    // ---- Media servers: directly over HTTPS (Plex sign-in, Jellyfin/Emby), or anything the paired PC knows ----
    static Object get(String url,Map<String,String> headers)throws Exception{
        if(!url.startsWith("https://"))throw new IOException("Use the server's https:// address, or import through your paired PC.");
        HttpsURLConnection c=(HttpsURLConnection)new URL(url).openConnection();c.setConnectTimeout(8000);c.setReadTimeout(60000);c.setRequestProperty("Accept","application/json");for(Map.Entry<String,String> h:headers.entrySet())c.setRequestProperty(h.getKey(),h.getValue());
        try{int status=c.getResponseCode();if(status==401||status==403)throw new IOException("The server didn't accept this sign-in.");if(status<200||status>=300)throw new IOException("The server answered "+status+".");try(InputStream in=c.getInputStream()){String t=new String(DataStore.read(in,64*1024*1024),StandardCharsets.UTF_8).trim();return t.isEmpty()?new JSONObject():new JSONTokener(t).nextValue();}}finally{c.disconnect();}
    }
    Map<String,String> plexHeaders(String token){Map<String,String> h=new HashMap<>();JSONObject s=data.stateObject("plex");String id=s.optString("client");if(id.isEmpty()){id=UUID.randomUUID().toString();DataStore.put(s,"client",id);data.stateObject("plex",s);}h.put("X-Plex-Client-Identifier",id);h.put("X-Plex-Product","Noir");if(token!=null)h.put("X-Plex-Token",token);return h;}
    /** {id, code}: approve at plexAuthUrl. */
    JSONObject plexPin()throws Exception{HttpsURLConnection c=(HttpsURLConnection)new URL("https://plex.tv/api/v2/pins?strong=true").openConnection();c.setRequestMethod("POST");c.setRequestProperty("Accept","application/json");for(Map.Entry<String,String> h:plexHeaders(null).entrySet())c.setRequestProperty(h.getKey(),h.getValue());
        try{if(c.getResponseCode()/100!=2)throw new IOException("Plex didn't return a sign-in code. Retry shortly.");try(InputStream in=c.getInputStream()){return new JSONObject(new String(DataStore.read(in,1024*1024),StandardCharsets.UTF_8));}}finally{c.disconnect();}}
    String plexAuthUrl(JSONObject pin){return "https://app.plex.tv/auth#?clientID="+plexHeaders(null).get("X-Plex-Client-Identifier")+"&code="+pin.optString("code")+"&context%5Bdevice%5D%5Bproduct%5D=Noir";}
    String plexPoll(JSONObject pin)throws Exception{Object o=get("https://plex.tv/api/v2/pins/"+pin.optLong("id"),plexHeaders(null));return o instanceof JSONObject?((JSONObject)o).optString("authToken"):"";}
    /** Plex servers reachable over HTTPS: {name, url, token}. */
    List<JSONObject> plexServers(String token)throws Exception{
        List<JSONObject> result=new ArrayList<>();Object o=get("https://plex.tv/api/v2/resources?includeHttps=1&includeRelay=1",plexHeaders(token));if(!(o instanceof JSONArray))return result;
        for(JSONObject r:DataStore.objects((JSONArray)o)){if(!Arrays.asList(r.optString("provides").split(",")).contains("server"))continue;String best="";int score=0;
            for(JSONObject c:DataStore.objects(r.optJSONArray("connections"))){String uri=c.optString("uri");if(!uri.startsWith("https://"))continue;int sc=c.optBoolean("local")?3:c.optBoolean("relay")?1:2;if(sc>score){score=sc;best=uri;}}
            if(!best.isEmpty())result.add(new JSONObject().put("name",r.optString("name")).put("url",best.replaceAll("/+$","")).put("token",r.optString("accessToken",token)));}
        return result;
    }
    static final java.util.regex.Pattern PLEX_IMDB=java.util.regex.Pattern.compile("(?:imdb://|imdb/)(tt\\d+)");
    List<PhoneHistory.Row> plexWatched(JSONObject server)throws Exception{
        List<PhoneHistory.Row> rows=new ArrayList<>();String url=server.optString("url");Map<String,String> h=plexHeaders(server.optString("token"));
        JSONObject sections=(JSONObject)get(url+"/library/sections",h);JSONObject container=sections.optJSONObject("MediaContainer");
        for(JSONObject d:DataStore.objects(container==null?null:container.optJSONArray("Directory"))){
            String type=d.optString("type");if(!type.equals("movie")&&!type.equals("show"))continue;boolean movie=type.equals("movie");
            JSONObject all=(JSONObject)get(url+"/library/sections/"+d.optString("key")+"/all?type="+(movie?"1":"4"),h);JSONObject mc=all.optJSONObject("MediaContainer");
            for(JSONObject i:DataStore.objects(mc==null?null:mc.optJSONArray("Metadata"))){if(i.optInt("viewCount")<=0)continue;long at=i.optLong("lastViewedAt")*1000;
                String imdb="";List<String> guids=new ArrayList<>();for(JSONObject g:DataStore.objects(i.optJSONArray("Guid")))guids.add(g.optString("id"));guids.add(i.optString("guid"));for(String g:guids){java.util.regex.Matcher m=PLEX_IMDB.matcher(g);if(m.find()){imdb=m.group(1);break;}}
                rows.add(movie?new PhoneHistory.Row("Plex",i.optString("title"),i.optInt("year"),true,imdb,"",0,0,"",at,0):new PhoneHistory.Row("Plex",i.optString("grandparentTitle"),0,false,"","",i.optInt("parentIndex"),i.optInt("index"),i.optString("title"),at,0));}
        }
        return rows;
    }
    List<PhoneHistory.Row> jellyfinWatched(String address,String user,String password,String kind)throws Exception{
        String base=address.trim().replaceAll("/+$","");if(!base.startsWith("https://"))throw new IOException("Use the server's https:// address, or import through your paired PC.");
        String device=plexHeaders(null).get("X-Plex-Client-Identifier");String auth="MediaBrowser Client=\"Noir\", Device=\"Android\", DeviceId=\""+device+"\", Version=\"1.0\"";
        HttpsURLConnection c=(HttpsURLConnection)new URL(base+"/Users/AuthenticateByName").openConnection();c.setRequestMethod("POST");c.setDoOutput(true);c.setRequestProperty("Content-Type","application/json");c.setRequestProperty("X-Emby-Authorization",auth);c.setRequestProperty("Authorization",auth);
        JSONObject login;try{try(OutputStream o=c.getOutputStream()){o.write(new JSONObject().put("Username",user).put("Pw",password).toString().getBytes(StandardCharsets.UTF_8));}if(c.getResponseCode()/100!=2)throw new IOException("The media server didn't confirm this account. Check your username and password.");try(InputStream in=c.getInputStream()){login=new JSONObject(new String(DataStore.read(in,1024*1024),StandardCharsets.UTF_8));}}finally{c.disconnect();}
        String token=login.optString("AccessToken"),userId=login.optJSONObject("User")==null?"":login.optJSONObject("User").optString("Id");
        Map<String,String> h=new HashMap<>();h.put("X-Emby-Token",token);
        JSONObject played=(JSONObject)get(base+"/Users/"+userId+"/Items?Recursive=true&IncludeItemTypes=Movie,Episode&IsPlayed=true&Fields=ProviderIds&EnableUserData=true",h);
        List<PhoneHistory.Row> rows=new ArrayList<>();
        for(JSONObject i:DataStore.objects(played.optJSONArray("Items"))){JSONObject ud=i.optJSONObject("UserData");long at=ud==null?0:PhoneHistory.date(ud.optString("LastPlayedDate"),null);if(at<86400000L*365)at=0;
            if(i.optString("Type").equals("Movie"))rows.add(new PhoneHistory.Row(kind,i.optString("Name"),i.optInt("ProductionYear"),true,i.optJSONObject("ProviderIds")==null?"":i.optJSONObject("ProviderIds").optString("Imdb"),"",0,0,"",at,0));
            else if(i.optString("Type").equals("Episode"))rows.add(new PhoneHistory.Row(kind,i.optString("SeriesName"),0,false,"","",i.optInt("ParentIndexNumber"),i.optInt("IndexNumber"),i.optString("Name"),at,0));}
        return rows;
    }
    List<JSONObject> pcServers()throws Exception{return DataStore.objects(data.pcRequest("/v1/history/servers",null).optJSONArray("servers"));}
    List<PhoneHistory.Row> pcServerWatched(String name)throws Exception{
        JSONObject reply=data.pcRequest("/v1/history/server-watched",new JSONObject().put("name",name));List<PhoneHistory.Row> rows=new ArrayList<>();
        for(JSONObject r:DataStore.objects(reply.optJSONArray("rows")))rows.add(new PhoneHistory.Row(name,r.optString("title"),r.optInt("year"),r.isNull("movie")?null:r.optBoolean("movie"),r.optString("imdb"),"",r.optInt("season"),r.optInt("episode"),r.optString("episodeTitle"),r.optLong("at"),0));
        return rows;
    }
}
