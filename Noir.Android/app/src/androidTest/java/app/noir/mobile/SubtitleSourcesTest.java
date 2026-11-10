package app.noir.mobile;

import android.content.Context;
import android.content.ContextWrapper;
import android.content.Intent;
import androidx.test.platform.app.InstrumentationRegistry;
import org.json.*;
import org.junit.Test;
import java.io.*;
import java.net.InetAddress;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.util.function.Function;
import java.util.zip.*;
import static org.junit.Assert.*;

/** No network: every request goes to a fake transport and every name to a fake resolver. All keys are obvious fakes. */
public final class SubtitleSourcesTest {
    static final String KEY="FAKE-KEY-123",ADDON="https://addon.example/private-token",SRT="1\n00:00:01,000 --> 00:00:04,000\nHello world\n";
    static final SubtitleTransport.Resolver PUBLIC=host->new InetAddress[]{SubtitleClient.PUBLIC};
    static final class Net implements SubtitleTransport.Http {
        final List<SubtitleTransport.Request> requests=new ArrayList<>();final Function<SubtitleTransport.Request,SubtitleTransport.Response> reply;
        Net(Function<SubtitleTransport.Request,SubtitleTransport.Response> reply){this.reply=reply;}
        public SubtitleTransport.Response send(SubtitleTransport.Request request,int cap,long deadline){requests.add(request);return reply.apply(request);}
        SubtitleTransport.Request last(){return requests.get(requests.size()-1);}
        Set<String> hosts(){Set<String> hosts=new TreeSet<>();for(SubtitleTransport.Request r:requests)hosts.add(java.net.URI.create(r.url).getHost());return hosts;}
    }
    static SubtitleTransport.Response ok(String body){return new SubtitleTransport.Response(200,null,body.getBytes(StandardCharsets.UTF_8));}
    static SubtitleTransport.Response bytes(byte[] body){return new SubtitleTransport.Response(200,null,body);}
    static SubtitleTransport.Response status(int code,String body,String... headers){Map<String,String> h=new HashMap<>();for(int i=0;i+1<headers.length;i+=2)h.put(headers[i],headers[i+1]);return new SubtitleTransport.Response(code,h,body.getBytes(StandardCharsets.UTF_8));}
    static SubtitleSources sources(Net net){return new SubtitleSources(new SubtitleTransport(net,PUBLIC));}
    static SubtitleSources.Selection sel(String source){try{return new SubtitleSources.Selection(source,source.equals("addon")?"":KEY,"","",source.equals("addon")?ADDON:"",source.equals("addon")?"Bearer ADDON-FAKE":"",true).validate();}catch(IOException e){throw new IllegalStateException(e);}}
    static JSONObject item(String imdb,boolean movie,int season,int episode)throws Exception{return new JSONObject().put("imdb",imdb).put("movie",movie).put("season",season).put("episode",episode);}
    static byte[] zip(String... namesAndText)throws Exception{ByteArrayOutputStream out=new ByteArrayOutputStream();try(ZipOutputStream zip=new ZipOutputStream(out)){for(int i=0;i+1<namesAndText.length;i+=2){zip.putNextEntry(new ZipEntry(namesAndText[i]));zip.write(namesAndText[i+1].getBytes(StandardCharsets.UTF_8));zip.closeEntry();}}return out.toByteArray();}
    static String srt(String word){return "1\n00:00:01,000 --> 00:00:04,000\n"+word+"\n";}
    static void kind(IOException e,SubtitleSources.SubtitleFailure.Kind expected){assertTrue(String.valueOf(e),e instanceof SubtitleSources.SubtitleFailure);assertEquals(expected,((SubtitleSources.SubtitleFailure)e).kind);}
    interface Run {void run()throws Exception;}
    static SubtitleSources.SubtitleFailure fails(Run run)throws Exception{try{run.run();}catch(SubtitleSources.SubtitleFailure e){return e;}fail("Expected a subtitle failure");return null;}

    // ---- adapters: exact requests ----
    @Test public void openSubtitlesRequestsAndDownload()throws Exception{
        String body="{\"data\":[{\"id\":\"1\",\"attributes\":{\"language\":\"en\",\"release\":\"Show 2020 WEB\",\"moviehash_match\":true,\"feature_details\":{\"season_number\":1,\"episode_number\":2},\"files\":[{\"file_id\":42,\"file_name\":\"show.srt\"}]}},{\"id\":\"2\",\"attributes\":{\"language\":\"en\",\"release\":\"Wrong\",\"feature_details\":{\"season_number\":1,\"episode_number\":5},\"files\":[{\"file_id\":43,\"file_name\":\"wrong.srt\"}]}}]}";
        Net net=new Net(r->r.method.equals("POST")?ok("{\"link\":\"https://dl.example.org/f/1.srt\"}"):r.url.startsWith("https://dl.example.org/")?ok(SRT):ok(body));SubtitleSources s=sources(net);
        List<JSONObject> movie=s.search(sel("opensubtitles"),item("tt0111161",true,0,0),"",  "en","");
        assertEquals("https://api.opensubtitles.com/api/v1/subtitles?languages=en&imdb_id=111161",net.last().url);assertEquals("GET",net.last().method);assertEquals(KEY,net.last().headers.get("Api-Key"));assertEquals("application/json",net.last().headers.get("Accept"));assertFalse(net.last().headers.containsKey("Authorization"));assertTrue(net.last().headers.get("User-Agent").startsWith("Noir"));
        assertEquals(2,movie.size());
        List<JSONObject> found=s.search(sel("opensubtitles"),item("tt0903747",false,1,2),"Show.S01E02.WEB.mkv","ar","0123456789abcdef");
        assertEquals("https://api.opensubtitles.com/api/v1/subtitles?languages=ar&parent_imdb_id=903747&season_number=1&episode_number=2&moviehash=0123456789abcdef",net.last().url);assertTrue("language filter",found.isEmpty());
        found=s.search(sel("opensubtitles"),item("tt0903747",false,1,2),"Show.S01E02.WEB.mkv","en","not-a-hash");assertFalse(net.last().url.contains("moviehash"));
        assertEquals(1,found.size());JSONObject c=found.get(0);assertEquals("42",c.getString("ref"));assertEquals("",c.getString("url"));assertEquals("opensubtitles",c.getString("provider"));assertEquals("h",c.getString("m"));assertTrue(c.getInt("score")>=1000);
        net.requests.clear();SubtitleArchive.Picked file=s.download(sel("opensubtitles"),c,item("tt0903747",false,1,2),"Show.S01E02.WEB.mkv");
        assertEquals(2,net.requests.size());SubtitleTransport.Request post=net.requests.get(0);assertEquals("POST",post.method);assertEquals("https://api.opensubtitles.com/api/v1/download",post.url);JSONObject sent=new JSONObject(new String(post.body,StandardCharsets.UTF_8));assertEquals(42L,sent.getLong("file_id"));assertEquals("srt",sent.getString("sub_format"));assertEquals(KEY,post.headers.get("Api-Key"));
        SubtitleTransport.Request fetch=net.requests.get(1);assertEquals("https://dl.example.org/f/1.srt",fetch.url);assertFalse(fetch.headers.containsKey("Api-Key"));assertFalse(fetch.headers.containsKey("Authorization"));assertTrue(new String(file.bytes,StandardCharsets.UTF_8).contains("Hello"));
    }
    @Test public void openSubtitlesLoginReloginAndBaseUrl()throws Exception{
        int[] searches={0};
        Net net=new Net(r->{
            if(r.url.endsWith("/login"))return ok("{\"token\":\"TOKEN-FAKE\",\"base_url\":\"vip-api.opensubtitles.com\"}");
            if(r.url.contains("/subtitles?")){searches[0]++;if(searches[0]==2)return status(401,"{}");return ok("{\"data\":[]}");}
            return status(500,"");
        });
        SubtitleSources s=sources(net);SubtitleSources.Selection login=new SubtitleSources.Selection("opensubtitles",KEY,"fake-user","fake-pass","","",true).validate();
        s.search(login,item("tt0111161",true,0,0),"","en","");
        assertEquals("https://api.opensubtitles.com/api/v1/login",net.requests.get(0).url);assertEquals("POST",net.requests.get(0).method);JSONObject credentials=new JSONObject(new String(net.requests.get(0).body,StandardCharsets.UTF_8));assertEquals("fake-user",credentials.getString("username"));assertEquals("fake-pass",credentials.getString("password"));assertEquals(KEY,net.requests.get(0).headers.get("Api-Key"));assertFalse(net.requests.get(0).headers.containsKey("Authorization"));
        assertTrue(net.requests.get(1).url.startsWith("https://vip-api.opensubtitles.com/api/v1/subtitles?"));assertEquals("Bearer TOKEN-FAKE",net.requests.get(1).headers.get("Authorization"));
        s.search(login,item("tt0111161",true,0,0),"","en","");
        List<String> urls=new ArrayList<>();for(SubtitleTransport.Request r:net.requests)urls.add(r.url.substring(r.url.indexOf("/api/v1/")+8).replaceAll("\\?.*",""));
        assertEquals(Arrays.asList("login","subtitles","subtitles","login","subtitles"),urls);
        // A base_url on any other host is refused.
        Net evil=new Net(r->r.url.endsWith("/login")?ok("{\"token\":\"T\",\"base_url\":\"evil.example\"}"):ok("{\"data\":[]}"));
        kind(fails(()->sources(evil).search(login,item("tt0111161",true,0,0),"","en","")),SubtitleSources.SubtitleFailure.Kind.UNAVAILABLE);assertEquals(1,evil.requests.size());
    }
    @Test public void subDlRequestsAndZipDownload()throws Exception{
        String rows="{\"status\":true,\"subtitles\":[{\"release_name\":\"Show.S01E02.WEB\",\"name\":\"a\",\"lang\":\"EN\",\"url\":\"/subtitle/123-456.zip\",\"season\":1,\"episode\":2},{\"release_name\":\"Show.S01E03\",\"lang\":\"EN\",\"url\":\"/subtitle/9-9.zip\",\"season\":1,\"episode\":3},{\"release_name\":\"Pack\",\"lang\":\"AR\",\"url\":\"/subtitle/7-7.zip\",\"season\":1,\"full_season\":true,\"episode\":0},{\"release_name\":\"Evil\",\"lang\":\"EN\",\"url\":\"//evil.example/x.zip\",\"season\":1,\"episode\":2}]}";
        byte[] archive=zip("Show.S01E01.srt",srt("one"),"folder/Show.S01E02.WEB.srt",srt("two"),"Show.S01E03.srt",srt("three"));
        Net net=new Net(r->r.url.startsWith("https://dl.subdl.com/")?bytes(archive):ok(rows));SubtitleSources s=sources(net);
        s.search(sel("subdl"),item("tt0111161",true,0,0),"","en","");
        assertEquals("https://api.subdl.com/api/v1/subtitles?api_key="+KEY+"&imdb_id=tt0111161&type=movie&languages=EN&subs_per_page=30&releases=1",net.last().url);assertFalse(net.last().headers.containsKey("Authorization"));
        List<JSONObject> found=s.search(sel("subdl"),item("tt0903747",false,1,2),"Show.S01E02.WEB.mkv","en","");
        assertEquals("https://api.subdl.com/api/v1/subtitles?api_key="+KEY+"&imdb_id=tt0903747&type=tv&languages=EN&subs_per_page=30&releases=1&season_number=1&episode_number=2&file_name=Show.S01E02.WEB.mkv",net.last().url);
        assertEquals(1,found.size());assertEquals("https://dl.subdl.com/subtitle/123-456.zip",found.get(0).getString("url"));assertFalse(found.get(0).toString().contains(KEY));
        assertEquals("ar",s.search(sel("subdl"),item("tt0903747",false,1,2),"","ar","").get(0).getString("language"));assertTrue(net.last().url.contains("languages=AR"));
        net.requests.clear();SubtitleArchive.Picked file=s.download(sel("subdl"),found.get(0),item("tt0903747",false,1,2),"Show.S01E02.WEB.mkv");
        assertEquals(1,net.requests.size());assertEquals("https://dl.subdl.com/subtitle/123-456.zip",net.last().url);
        for(String header:new String[]{"Api-Key","X-API-Key","Authorization"})assertFalse(net.last().headers.containsKey(header));assertEquals("Show.S01E02.WEB.srt",file.name);assertTrue(new String(file.bytes,StandardCharsets.UTF_8).contains("two"));
    }
    @Test public void subSourceRequestsAndZipDownload()throws Exception{
        byte[] archive=zipOf();
        Net net=new Net(r->{
            if(r.url.contains("/movies/search"))return ok("{\"data\":[{\"movieId\":7,\"imdbId\":\"tt0903747\",\"season\":2},{\"movieId\":8,\"imdbId\":\"tt0903747\",\"season\":1}]}");
            if(r.url.contains("/download"))return bytes(archive);
            return ok("{\"data\":[{\"subtitleId\":555,\"movieId\":8,\"language\":\"Arabic\",\"releaseInfo\":[\"Show.S01E02.WEB\"]},{\"subtitleId\":556,\"movieId\":99,\"language\":\"arabic\"}]}");
        });
        SubtitleSources s=sources(net);List<JSONObject> found=s.search(sel("subsource"),item("tt0903747",false,1,2),"Show.S01E02.WEB.mkv","ar","");
        assertEquals("https://api.subsource.net/api/v1/movies/search?searchType=imdb&imdb=tt0903747&season=1",net.requests.get(0).url);assertEquals("https://api.subsource.net/api/v1/subtitles?movieId=8&language=arabic&limit=100&seasonNumber=1&episodeNumber=2",net.requests.get(1).url);
        for(SubtitleTransport.Request r:net.requests){assertEquals(KEY,r.headers.get("X-API-Key"));assertFalse(r.url.contains(KEY));}
        assertEquals(1,found.size());assertEquals("555",found.get(0).getString("ref"));assertEquals("",found.get(0).getString("url"));
        net.requests.clear();SubtitleArchive.Picked file=s.download(sel("subsource"),found.get(0),item("tt0903747",false,1,2),"Show.S01E02.WEB.mkv");
        assertEquals("https://api.subsource.net/api/v1/subtitles/555/download",net.last().url);assertEquals(KEY,net.last().headers.get("X-API-Key"));assertEquals("Show.S01E02.srt",file.name);
    }
    static byte[] zipOf()throws Exception{return zip("Show.S01E01.srt",srt("one"),"Show.S01E02.srt",srt("two"));}
    @Test public void wyzieRequestsAndSignedFileDownload()throws Exception{
        String rows="[{\"id\":\"w1\",\"url\":\"https://sub.wyzie.io/c/abc.srt?tok=SIGNED-FAKE\",\"format\":\"srt\",\"language\":\"en\",\"release\":\"Movie 2020\",\"fileName\":\"movie.srt\",\"match\":{\"reasons\":[\"hash\"]}},{\"id\":\"w2\",\"url\":\"https://sub.wyzie.io/c/leak.srt?key="+KEY+"\",\"language\":\"en\"},{\"id\":\"w3\",\"url\":\"https://elsewhere.example/c/x.srt\",\"language\":\"en\"},{\"id\":\"w4\",\"url\":\"https://sub.wyzie.io/other/x.srt\",\"language\":\"en\"}]";
        Net net=new Net(r->r.url.contains("/c/")?ok(SRT):ok(rows));SubtitleSources s=sources(net);
        List<JSONObject> found=s.search(sel("wyzie"),item("tt0111161",true,0,0),"","en","");
        assertEquals("https://sub.wyzie.io/search?id=tt0111161&language=en&key="+KEY+"&limit=200",net.last().url);assertEquals(1,found.size());
        found=s.search(sel("wyzie"),item("tt0903747",false,1,2),"Dir/Show.S01E02.mkv","en","0123456789abcdef");
        assertEquals("https://sub.wyzie.io/search?id=tt0903747&language=en&key="+KEY+"&limit=200&season=1&episode=2&hash=0123456789abcdef&video=Show.S01E02.mkv",net.last().url);
        JSONObject c=found.get(0);assertEquals("https://sub.wyzie.io/c/abc.srt?tok=SIGNED-FAKE",c.getString("url"));assertEquals("h",c.getString("m"));assertFalse(c.toString().contains(KEY));
        net.requests.clear();s.download(sel("wyzie"),c,item("tt0111161",true,0,0),"");
        assertEquals(c.getString("url"),net.last().url);assertFalse(net.last().url.contains(KEY));assertEquals(0,net.requests.get(0).headers.keySet().stream().filter(h->h.toLowerCase(Locale.ROOT).contains("key")||h.equals("Authorization")).count());
    }
    @Test public void addonRequestsAndAuthorizationScope()throws Exception{
        String rows="{\"subtitles\":[{\"id\":\"a\",\"lang\":\"eng\",\"url\":\""+ADDON+"/file/1.srt\",\"subtitleFileName\":\"one.srt\",\"movieReleaseName\":\"Movie\",\"m\":\"h\"},{\"id\":\"b\",\"lang\":\"English\",\"url\":\"https://files.example/2.srt\"}]}";
        Net net=new Net(r->r.url.endsWith(".json")?ok(rows):ok(SRT));SubtitleSources s=sources(net);
        List<JSONObject> found=s.search(sel("addon"),item("tt0903747",false,1,2),"Show.S01E02.mkv","en","0123456789abcdef");
        assertEquals(ADDON+"/subtitles/series/tt0903747%3A1%3A2/filename=Show.S01E02.mkv&videoHash=0123456789abcdef.json",net.last().url);assertEquals("Bearer ADDON-FAKE",net.last().headers.get("Authorization"));assertEquals(2,found.size());
        net.requests.clear();for(JSONObject c:found)s.download(sel("addon"),c,item("tt0903747",false,1,2),"");
        for(SubtitleTransport.Request r:net.requests)assertEquals(r.url,r.url.startsWith(ADDON)?"Bearer ADDON-FAKE":null,r.headers.get("Authorization"));assertEquals(2,net.requests.size());
        // Check: manifest first, then one English search for tt0111161.
        Net checkNet=new Net(r->r.url.endsWith("/manifest.json")?ok("{\"name\":\"Fake addon\",\"resources\":[\"subtitles\"]}"):ok("{\"subtitles\":[]}"));
        assertEquals("Fake addon",sources(checkNet).check(sel("addon")));assertEquals(ADDON+"/manifest.json",checkNet.requests.get(0).url);assertTrue(checkNet.requests.get(1).url.startsWith(ADDON+"/subtitles/movie/tt0111161/"));assertEquals(2,checkNet.requests.size());
    }
    @Test public void checkRunsOneEnglishSearchOnTheSameAdapter()throws Exception{
        for(String source:new String[]{"opensubtitles","subdl","subsource","wyzie"}){
            Net net=new Net(r->r.url.contains("movies/search")?ok("{\"data\":[]}"):ok(source.equals("wyzie")?"[]":source.equals("subdl")?"{\"status\":true,\"subtitles\":[]}":"{\"data\":[]}"));
            assertEquals("",sources(net).check(sel(source)));assertTrue(source,net.requests.get(0).url.contains("tt0111161")||net.requests.get(0).url.contains("111161"));assertTrue(net.requests.size()<=2);
            Net bad=new Net(r->status(401,"{}"));SubtitleSources.SubtitleFailure e=fails(()->sources(bad).check(sel(source)));assertEquals(SubtitleSources.SubtitleFailure.Kind.BAD_KEY,e.kind);
        }
    }

    // ---- failures ----
    static final String[] SOURCES={"opensubtitles","subdl","subsource","wyzie","addon"};
    @Test public void everyFailureStatusMapsAndNeverLeaksASecret()throws Exception{
        int[][] table={{400,2},{401,0},{402,1},{403,0},{406,1},{410,2},{429,1},{500,2},{502,2},{503,2}};
        SubtitleSources.SubtitleFailure.Kind[] kinds=SubtitleSources.SubtitleFailure.Kind.values();
        String[] words={"key rejected","daily limit reached","temporarily unavailable"};
        for(String source:SOURCES)for(int[] row:table){
            Net net=new Net(r->status(row[0],"{\"message\":\"secret "+KEY+" https://leak.example\"}"));SubtitleSources.SubtitleFailure e=fails(()->sources(net).search(sel(source),item("tt0111161",true,0,0),"","en",""));
            assertEquals(source+" "+row[0],kinds[row[1]],e.kind);assertEquals(sel(source).name()+" · "+words[row[1]],e.getMessage());assertEquals(row[0],e.status);
            assertFalse(e.getMessage().contains(KEY));assertFalse(e.getMessage().contains("http"));assertNull(e.getCause());
        }
        for(String source:new String[]{"subdl","subsource","wyzie"}){Net net=new Net(r->status(404,"{}"));assertTrue(source,sources(net).search(sel(source),item("tt0111161",true,0,0),"","en","").isEmpty());}
        for(String source:new String[]{"opensubtitles","addon"}){Net net=new Net(r->status(404,"{}"));kind(fails(()->sources(net).search(sel(source),item("tt0111161",true,0,0),"","en","")),SubtitleSources.SubtitleFailure.Kind.UNAVAILABLE);}
    }
    @Test public void subDlPayloadErrorsAndLimitReset()throws Exception{
        String[][] cases={{"{\"status\":false,\"error\":\"Invalid api key\"}","0"},{"{\"success\":false,\"message\":\"Forbidden\"}","0"},{"{\"status\":false,\"error\":\"Daily limit exceeded\"}","1"},{"{\"status\":false,\"message\":\"quota\"}","1"},{"{\"status\":false,\"message\":\"boom\"}","2"}};
        for(String[] c:cases){Net net=new Net(r->ok(c[0]));SubtitleSources.SubtitleFailure e=fails(()->sources(net).search(sel("subdl"),item("tt0111161",true,0,0),"","en",""));assertEquals(c[1],Integer.toString(e.kind.ordinal()));}
        Net limited=new Net(r->status(429,"{\"reset_at\":\"2030-01-02T03:04:05Z\"}"));assertEquals("Wyzie Subs · daily limit reached · resets 2030-01-02 03:04:05Z",fails(()->sources(limited).search(sel("wyzie"),item("tt0111161",true,0,0),"","en","")).getMessage());
        Net header=new Net(r->status(429,"",  "Retry-After","3600"));String message=fails(()->sources(header).search(sel("subdl"),item("tt0111161",true,0,0),"","en","")).getMessage();assertTrue(message,message.matches("SubDL · daily limit reached · resets \\d{4}-\\d\\d-\\d\\d \\d\\d:\\d\\d:\\d\\dZ"));
        Net junk=new Net(r->status(429,"",  "Retry-After","soon"));assertEquals("SubDL · daily limit reached",fails(()->sources(junk).search(sel("subdl"),item("tt0111161",true,0,0),"","en","")).getMessage());
    }
    @Test public void arabicMessagesAreLocalized()throws Exception{
        boolean was=Loc.arabic;Loc.arabic=true;
        try{
            Net net=new Net(r->status(401,""));assertEquals("SubDL · "+Loc.t("key rejected"),fails(()->sources(net).search(sel("subdl"),item("tt0111161",true,0,0),"","en","")).getMessage());assertNotEquals("key rejected",Loc.t("key rejected"));
            Net limit=new Net(r->status(429,""));assertEquals("SubDL · "+Loc.t("daily limit reached"),fails(()->sources(limit).search(sel("subdl"),item("tt0111161",true,0,0),"","en","")).getMessage());assertNotEquals("daily limit reached",Loc.t("daily limit reached"));
            assertNotEquals("no Arabic subtitles found",Loc.t("no Arabic subtitles found"));assertNotEquals("Free default",Loc.t("Free default"));assertNotEquals("Use only this source",Loc.t("Use only this source"));assertNotEquals("Get a free key",Loc.t("Get a free key"));
            Net down=new Net(r->status(500,""));assertTrue(fails(()->sources(down).search(sel("addon"),item("tt0111161",true,0,0),"","en","")).getMessage().startsWith(Loc.t("Stremio addon")+" · "));
        }finally{Loc.arabic=was;}
    }
    @Test public void malformedAndOversizedResponsesAreUnavailable()throws Exception{
        String[] bodies={"not json","\"text\"","{\"data\":5}","<html>"};
        for(String source:SOURCES)for(String body:bodies){Net net=new Net(r->ok(body));kind(fails(()->sources(net).search(sel(source),item("tt0111161",true,0,0),"","en","")),SubtitleSources.SubtitleFailure.Kind.UNAVAILABLE);}
        byte[] huge=new byte[SubtitleTransport.CAP+1];Arrays.fill(huge,(byte)' ');
        for(String source:SOURCES){Net net=new Net(r->bytes(huge));kind(fails(()->sources(net).search(sel(source),item("tt0111161",true,0,0),"","en","")),SubtitleSources.SubtitleFailure.Kind.UNAVAILABLE);}
        Net manifest=new Net(r->bytes(new byte[SubtitleTransport.MANIFEST_CAP+1]));kind(fails(()->sources(manifest).check(sel("addon"))),SubtitleSources.SubtitleFailure.Kind.UNAVAILABLE);
        Net notSubtitles=new Net(r->ok("{\"name\":\"x\",\"resources\":[\"stream\"]}"));kind(fails(()->sources(notSubtitles).check(sel("addon"))),SubtitleSources.SubtitleFailure.Kind.UNAVAILABLE);
        // A download that is not a subtitle is refused.
        Net page=new Net(r->r.url.contains("/c/")?ok("<!DOCTYPE html><html>not a subtitle at all</html>"):ok("[{\"id\":\"1\",\"url\":\"https://sub.wyzie.io/c/a.srt\",\"language\":\"en\"}]"));SubtitleSources s=sources(page);JSONObject c=s.search(sel("wyzie"),item("tt0111161",true,0,0),"","en","").get(0);
        kind(fails(()->s.download(sel("wyzie"),c,item("tt0111161",true,0,0),"")),SubtitleSources.SubtitleFailure.Kind.UNAVAILABLE);
    }
    @Test public void noLoginDownloadAndStatusFailuresFromFileHostsAreMapped()throws Exception{
        Net net=new Net(r->r.url.contains("/c/")?status(429,"",  "Retry-After","60"):ok("[{\"id\":\"1\",\"url\":\"https://sub.wyzie.io/c/a.srt\",\"language\":\"en\"}]"));SubtitleSources s=sources(net);JSONObject c=s.search(sel("wyzie"),item("tt0111161",true,0,0),"","en","").get(0);
        kind(fails(()->s.download(sel("wyzie"),c,item("tt0111161",true,0,0),"")),SubtitleSources.SubtitleFailure.Kind.LIMIT);
    }

    // ---- transport policy ----
    @Test public void redirectsAreManualBoundedAndKeepCredentialsOnTheirHost()throws Exception{
        SubtitleSources.Selection subsource=sel("subsource");
        // keyed request, same host: followed, key kept
        Net same=new Net(r->r.url.endsWith("/a")?status(302,"","location","/b"):ok("[]"));new SubtitleTransport(same,PUBLIC).send(subsource,"https://api.subsource.net/a",true,"GET",null,100,false,"",true,SubtitleTransport.deadline());
        assertEquals(2,same.requests.size());assertEquals("https://api.subsource.net/b",same.last().url);assertEquals(KEY,same.last().headers.get("X-API-Key"));
        // keyed request, other host: stopped as Unavailable after one request
        for(String target:new String[]{"https://evil.example/x","https://api.subsource.net.evil.example/x"}){Net other=new Net(r->status(302,"","location",target));
            kind(fails(()->new SubtitleTransport(other,PUBLIC).send(subsource,"https://api.subsource.net/a",true,"GET",null,100,false,"",true,SubtitleTransport.deadline())),SubtitleSources.SubtitleFailure.Kind.UNAVAILABLE);assertEquals(1,other.requests.size());}
        // keyed request to a host that is not the provider's: nothing is sent
        Net none=new Net(r->ok("[]"));kind(fails(()->new SubtitleTransport(none,PUBLIC).send(subsource,"https://other.example/a",true,"GET",null,100,false,"",true,SubtitleTransport.deadline())),SubtitleSources.SubtitleFailure.Kind.UNAVAILABLE);assertEquals(0,none.requests.size());
        // unkeyed files may follow validated public redirects
        Net open=new Net(r->r.url.startsWith("https://cdn.example/")?ok("x"):status(301,"","location","https://cdn.example/f"));
        assertArrayEquals("x".getBytes(),new SubtitleTransport(open,PUBLIC).send(subsource,"https://files.example/f",false,"GET",null,100,false,"",false,SubtitleTransport.deadline()));assertEquals(2,open.requests.size());assertFalse(open.last().headers.containsKey("X-API-Key"));
        // ...but not to private, plain, odd-port or userinfo destinations
        for(String bad:new String[]{"https://10.0.0.5/x","https://127.0.0.1/x","https://[::1]/x","http://cdn.example/x","https://cdn.example:8443/x","https://user:pw@cdn.example/x","https://localhost/x"}){Net n=new Net(r->status(302,"","location",bad));
            kind(fails(()->new SubtitleTransport(n,PUBLIC).send(subsource,"https://files.example/f",false,"GET",null,100,false,"",false,SubtitleTransport.deadline())),SubtitleSources.SubtitleFailure.Kind.UNAVAILABLE);assertEquals(bad,1,n.requests.size());}
        // at most 3 redirects: the fourth response is refused
        Net loop=new Net(r->status(302,"","location",r.url.endsWith("/1")?"/2":r.url.endsWith("/2")?"/3":r.url.endsWith("/3")?"/4":"/1"));
        kind(fails(()->new SubtitleTransport(loop,PUBLIC).send(subsource,"https://files.example/1",false,"GET",null,100,false,"",false,SubtitleTransport.deadline())),SubtitleSources.SubtitleFailure.Kind.UNAVAILABLE);assertEquals(4,loop.requests.size());
        Net three=new Net(r->r.url.endsWith("/4")?ok("done"):status(302,"","location",r.url.endsWith("/1")?"/2":r.url.endsWith("/2")?"/3":"/4"));
        assertArrayEquals("done".getBytes(),new SubtitleTransport(three,PUBLIC).send(subsource,"https://files.example/1",false,"GET",null,100,false,"",false,SubtitleTransport.deadline()));
    }
    @Test public void addressesAreCheckedRightBeforeEachRequest()throws Exception{
        for(String literal:new String[]{"127.0.0.1","10.1.1.1","192.168.0.1","172.16.0.1","172.31.255.255","169.254.1.1","100.64.0.1","100.127.0.1","0.0.0.0","224.0.0.1","255.255.255.255","::1","::","fe80::1","fc00::1","ff02::1","::ffff:10.0.0.1","::ffff:127.0.0.1","::ffff:192.168.1.1"})assertFalse(literal,SubtitleTransport.publicAddress(InetAddress.getByName(literal)));
        for(String literal:new String[]{"93.184.216.34","8.8.8.8","100.63.0.1","172.32.0.1","2606:4700:4700::1111","::ffff:8.8.8.8"})assertTrue(literal,SubtitleTransport.publicAddress(InetAddress.getByName(literal)));
        for(String url:new String[]{"https://127.0.0.1/x","https://[::1]/x","https://localhost/x","https://a.localhost/x","https://user@example.com/","https://example.com:8443/","http://example.com/","https://example.com/#f","https://2130706433/","https://10.0.0.1/","ftp://example.com/","https://"})assertFalse(url,SubtitleTransport.validUrl(url));
        assertTrue(SubtitleTransport.validUrl("https://example.com/x?y=1"));assertTrue(SubtitleTransport.validUrl("https://example.com:443/x"));
        // A name that resolves to anything private (even among public answers) is refused before a request is made.
        for(String bad:new String[]{"127.0.0.1","10.0.0.9","169.254.169.254","::1","::ffff:192.168.0.2"}){
            Net net=new Net(r->ok("[]"));SubtitleTransport t=new SubtitleTransport(net,host->new InetAddress[]{SubtitleClient.PUBLIC,InetAddress.getByName(bad)});
            kind(fails(()->t.send(sel("wyzie"),"https://sub.wyzie.io/search",true,"GET",null,100,false,"",true,SubtitleTransport.deadline())),SubtitleSources.SubtitleFailure.Kind.UNAVAILABLE);assertEquals(0,net.requests.size());}
        // Rebinding: public on the first lookup, private afterwards. The second request never goes out; so does a redirect target.
        int[] lookups={0};Net net=new Net(r->ok("[]"));SubtitleTransport t=new SubtitleTransport(net,host->new InetAddress[]{lookups[0]++==0?SubtitleClient.PUBLIC:InetAddress.getByName("10.0.0.1")});
        t.send(sel("wyzie"),"https://sub.wyzie.io/search",true,"GET",null,100,false,"",true,SubtitleTransport.deadline());assertEquals(1,net.requests.size());
        kind(fails(()->t.send(sel("wyzie"),"https://sub.wyzie.io/search",true,"GET",null,100,false,"",true,SubtitleTransport.deadline())),SubtitleSources.SubtitleFailure.Kind.UNAVAILABLE);assertEquals(1,net.requests.size());
        Net hop=new Net(r->r.url.startsWith("https://cdn.example/")?ok("x"):status(302,"","location","https://cdn.example/f"));
        SubtitleTransport rebinding=new SubtitleTransport(hop,host->new InetAddress[]{host.equals("cdn.example")?InetAddress.getByName("192.168.1.9"):SubtitleClient.PUBLIC});
        kind(fails(()->rebinding.send(sel("wyzie"),"https://files.example/f",false,"GET",null,100,false,"",false,SubtitleTransport.deadline())),SubtitleSources.SubtitleFailure.Kind.UNAVAILABLE);assertEquals(1,hop.requests.size());
        // The resolver failing is Unavailable too.
        SubtitleTransport down=new SubtitleTransport(new Net(r->ok("[]")),host->{throw new java.net.UnknownHostException(host);});
        kind(fails(()->down.send(sel("wyzie"),"https://sub.wyzie.io/search",true,"GET",null,100,false,"",true,SubtitleTransport.deadline())),SubtitleSources.SubtitleFailure.Kind.UNAVAILABLE);
    }
    @Test public void bodiesAboveTheCapAreRefused()throws Exception{
        Net net=new Net(r->bytes(new byte[101]));kind(fails(()->new SubtitleTransport(net,PUBLIC).send(sel("wyzie"),"https://sub.wyzie.io/search",true,"GET",null,100,false,"",true,SubtitleTransport.deadline())),SubtitleSources.SubtitleFailure.Kind.UNAVAILABLE);
        assertEquals(100,new SubtitleTransport(new Net(r->bytes(new byte[100])),PUBLIC).send(sel("wyzie"),"https://sub.wyzie.io/search",true,"GET",null,100,false,"",true,SubtitleTransport.deadline()).length);
        assertEquals(4*1024*1024,SubtitleTransport.CAP);assertEquals(256*1024,SubtitleTransport.MANIFEST_CAP);assertEquals(35_000_000_000L,SubtitleTransport.BUDGET_NANOS);
        try{SubtitleTransport.readCapped(new ByteArrayInputStream(new byte[50]),49,System.nanoTime()+10_000_000_000L);fail();}catch(IOException expected){}
        try{SubtitleTransport.readCapped(new ByteArrayInputStream(new byte[50]),100,System.nanoTime()-1);fail();}catch(IOException expected){}
    }

    // ---- zip ----
    @Test public void zipSafetyAndEpisodeSelection()throws Exception{
        String[] many=new String[402];for(int i=0;i<201;i++){many[2*i]="f"+i+".srt";many[2*i+1]=srt("x");}
        try{SubtitleArchive.pick(zip(many),true,0,0,"");fail("201 entries");}catch(IOException expected){}
        String[] enough=new String[400];for(int i=0;i<200;i++){enough[2*i]="f"+i+".srt";enough[2*i+1]=srt("x");}assertNotNull(SubtitleArchive.pick(zip(enough),true,0,0,""));
        char[] big=new char[4*1024*1024+1];Arrays.fill(big,'a');try{SubtitleArchive.pick(zip("big.srt",new String(big)),true,0,0,"");fail("one entry over 4 MiB");}catch(IOException expected){}
        char[] exact=new char[4*1024*1024];Arrays.fill(exact,'a');String block=new String(exact);try{SubtitleArchive.pick(zip("a.srt",block,"b.srt",block,"c.srt",block,"d.srt",block,"e.srt",block),true,0,0,"");fail("over 16 MiB in total");}catch(IOException expected){}
        // Inflated bytes are counted, whatever a header claims: a tiny deflated entry that inflates past the cap.
        try{SubtitleArchive.pick(zip("bomb.srt",new String(big)),true,0,0,"");fail();}catch(IOException expected){}
        try{SubtitleArchive.pick(new byte[]{1,2,3,4,5,6,7,8},true,0,0,"");fail("not a zip");}catch(IOException expected){}
        try{SubtitleArchive.pick(zip("setup.exe","x","notes.txt","y","movie.nfo","z"),true,0,0,"");fail("no subtitle files");}catch(IOException expected){}
        // Directory parts are ignored; only subtitle extensions count.
        // Android 14+ refuses "../" names itself; older versions reach the base-name rule. Either way nothing escapes.
        byte[] traversal=zip("../../evil/Show.S01E02.srt",srt("two"),"x/Show.S01E01.srt",srt("one"),"Show.S01E02.txt","no");if(android.os.Build.VERSION.SDK_INT>=34){try{SubtitleArchive.pick(traversal,false,1,2,"");fail("traversal must be refused");}catch(IOException expected){}}else assertEquals("Show.S01E02.srt",SubtitleArchive.pick(traversal,false,1,2,"").name);
        SubtitleArchive.Picked p=SubtitleArchive.pick(zip("x/Show.S01E02.srt",srt("two"),"x/Show.S01E01.srt",srt("one"),"Show.S01E02.txt","no"),false,1,2,"");assertEquals("Show.S01E02.srt",p.name);
        assertEquals("Show.S01E02.vtt",SubtitleArchive.pick(zip("a\\b\\Show.S01E02.vtt",srt("v")),false,1,2,"").name);
        // Episode rules
        assertEquals("Show.S01E02.WEB.srt",SubtitleArchive.pick(zip("Show.S01E02.BluRay.srt",srt("a"),"Show.S01E02.WEB.srt",srt("b"),"Show.S01E03.WEB.srt",srt("c"),"Show.S02E02.WEB.srt",srt("d"),"Show.S01E02-E03.WEB.srt",srt("e")),false,1,2,"Show S01E02 WEB").name);
        assertEquals("E02.srt",SubtitleArchive.pick(zip("E02.srt",srt("bare"),"E03.srt",srt("other")),false,1,2,"").name);
        try{SubtitleArchive.pick(zip("Show.S01E01.srt",srt("a"),"Show.S01E03.srt",srt("c"),"Show.srt",srt("none")),false,1,2,"");fail("no entry matches the episode");}catch(IOException expected){}
        assertEquals("Movie.2020.WEB.srt",SubtitleArchive.pick(zip("Movie.2020.BluRay.srt",srt("a"),"Movie.2020.WEB.srt",srt("b")),true,0,0,"Movie 2020 WEB").name);
        assertTrue(SubtitleArchive.episodeMatches("Show.S01E02.srt",1,2));assertTrue(SubtitleArchive.episodeMatches("Show.1x02.srt",1,2));assertTrue(SubtitleArchive.episodeMatches("Show Episode 02.srt",1,2));assertFalse(SubtitleArchive.episodeMatches("Show.S01E03.srt",1,2));assertFalse(SubtitleArchive.episodeMatches("Show Season 2 E02.srt",1,2));assertFalse(SubtitleArchive.episodeMatches("Show.srt",1,2));assertFalse(SubtitleArchive.episodeMatches("Show.S01E02-E04.srt",1,2));
        assertTrue(SubtitleArchive.hasEpisodeName("Show.S01E02.srt"));assertFalse(SubtitleArchive.hasEpisodeName("Movie.2020.srt"));
    }

    // ---- settings ----
    static final class Env {
        final Context context;final DataStore data;final PhoneDownloadSettings storage;
        Env()throws Exception{
            Context real=InstrumentationRegistry.getInstrumentation().getTargetContext();File root=new File(real.getFilesDir(),"subtitle-sources-test-"+UUID.randomUUID());assertTrue(root.mkdirs());
            context=new ContextWrapper(real){@Override public Context getApplicationContext(){return this;}@Override public File getFilesDir(){return root;}@Override public File getExternalFilesDir(String type){return root;}};
            data=new DataStore(context);storage=new PhoneDownloadSettings(context);
        }
        void use(String source,String key,boolean only)throws Exception{JSONObject config=new JSONObject();PhoneDownloadSettings.applySubtitle(config,new SubtitleSources.Selection(source,key,"","",source.equals("addon")?ADDON:"","",only),new JSONObject());storage.save(config);}
    }
    @Test public void settingsMigrationOldPayloadsKeysPerSiteAndBlankRefused()throws Exception{
        Env env=new Env();
        // Older settings (no subtitleSource) still load; a saved addon link means "addon".
        env.storage.save(new JSONObject().put("subtitleAddonUrl",ADDON+"/manifest.json").put("subtitleAddonOnly",false));
        SubtitleSources.Selection old=SubtitleSources.Selection.from(env.storage.load());assertEquals("addon",old.source);assertEquals(ADDON,old.addonUrl);assertFalse(old.only);
        env.storage.save(new JSONObject().put("manifest","https://aio.invalid/private/manifest.json"));SubtitleSources.Selection none=SubtitleSources.Selection.from(env.storage.load());assertFalse(none.chosen());assertTrue(none.only);
        // Per-site keys survive switching, encrypted at rest.
        JSONObject value=new JSONObject(),keys=new JSONObject();
        PhoneDownloadSettings.applySubtitle(value,new SubtitleSources.Selection("subdl","SUBDL-FAKE-1","","","","",true),keys);keys=value.getJSONObject("subtitleSiteKeys");
        PhoneDownloadSettings.applySubtitle(value,new SubtitleSources.Selection("wyzie","WYZIE-FAKE-2","","","","",false),keys);keys=value.getJSONObject("subtitleSiteKeys");
        PhoneDownloadSettings.applySubtitle(value,new SubtitleSources.Selection("addon","","","",ADDON,"Bearer AUTH-FAKE-3",true),keys);
        env.storage.save(value);JSONObject loaded=env.storage.load();assertEquals("addon",loaded.getString("subtitleSource"));assertEquals("SUBDL-FAKE-1",loaded.getJSONObject("subtitleSiteKeys").getString("subdl"));assertEquals("WYZIE-FAKE-2",loaded.getJSONObject("subtitleSiteKeys").getString("wyzie"));assertEquals("",loaded.getString("subtitleApiKey"));
        assertEquals("SUBDL-FAKE-1",SubtitleSources.Selection.from(new JSONObject(loaded.toString()).put("subtitleSource","subdl")).apiKey);
        String plain=new String(java.nio.file.Files.readAllBytes(new File(env.context.getFilesDir(),"phone-download-settings.enc").toPath()),StandardCharsets.UTF_8);for(String secret:new String[]{"SUBDL-FAKE-1","WYZIE-FAKE-2","AUTH-FAKE-3","private-token"})assertFalse(secret,plain.contains(secret));
        // Free default clears the addon link, so it is not read back as an addon.
        PhoneDownloadSettings.applySubtitle(value,new SubtitleSources.Selection("","","","","","",true).validate(),keys);assertFalse(SubtitleSources.Selection.from(value).chosen());
        // Unknown sources are refused; blank keys and links get plain messages.
        try{env.storage.save(new JSONObject().put("subtitleSource","bogus"));fail();}catch(IOException expected){}
        for(String[] bad:new String[][]{{"subdl","  ","Enter the subtitle site API key."},{"wyzie","","Enter the subtitle site API key."},{"addon","","Paste a subtitle addon link first."},{"nonsense","k","Choose a subtitle source."}})try{new SubtitleSources.Selection(bad[0],bad[0].equals("addon")?"":bad[1],"","",bad[0].equals("addon")?bad[1]:"","",true).validate();fail(bad[0]);}catch(IOException expected){assertEquals(bad[2],expected.getMessage());}
        try{new SubtitleSources.Selection("opensubtitles",KEY,"user","","","",true).validate();fail();}catch(IOException expected){assertEquals("Enter both subtitle username and password, or leave both empty.",expected.getMessage());}
        try{new SubtitleSources.Selection("subdl","bad\nkey","","","","",true).validate();fail();}catch(IOException expected){assertEquals("Subtitle credentials contain invalid characters.",expected.getMessage());}
        SubtitleSources.Selection trimmed=new SubtitleSources.Selection("subdl","  "+KEY+"  ","ignored","ignored",ADDON,"ignored",true).validate();assertEquals(KEY,trimmed.apiKey);assertEquals("",trimmed.username);assertEquals("",trimmed.addonUrl);assertEquals("",trimmed.addonAuth);
        // Redaction covers the new secrets, including keys kept for other sites.
        JSONObject secrets=new JSONObject().put("subtitleApiKey","KEY-A").put("subtitlePassword","PASS-B").put("subtitleAddonAuth","AUTH-C").put("subtitleSiteKeys",new JSONObject().put("wyzie","KEY-D"));
        String safe=PhoneDownloadSettings.safe("failed KEY-A PASS-B AUTH-C KEY-D https://x.example/p?key=1",secrets);for(String secret:new String[]{"KEY-A","PASS-B","AUTH-C","KEY-D","x.example"})assertFalse(secret,safe.contains(secret));
    }
    @Test public void importingFromThePcKeepsOldAndNewPayloads()throws Exception{
        Env env=new Env();
        env.storage.save(new JSONObject().put("subtitleSiteKeys",new JSONObject().put("wyzie","WYZIE-LOCAL-FAKE")));
        // An older PC sends only the addon fields.
        JSONObject older=env.storage.imported(new JSONObject().put("subtitleAddonUrl",ADDON).put("subtitleAddonOnly",false).put("manifest","https://aio.invalid/private/manifest.json"));
        env.storage.save(older);SubtitleSources.Selection a=SubtitleSources.Selection.from(env.storage.load());assertEquals("addon",a.source);assertFalse(a.only);assertEquals("WYZIE-LOCAL-FAKE",env.storage.load().getJSONObject("subtitleSiteKeys").getString("wyzie"));
        // A current PC sends the whole choice (camelCase names as in MobileSyncServer).
        JSONObject current=env.storage.imported(new JSONObject().put("subtitleSource","opensubtitles").put("subtitleApiKey","OS-KEY-FAKE").put("subtitleUsername","fake-user").put("subtitlePassword","fake-pass").put("subtitleAddonUrl","").put("subtitleAddonAuth","").put("subtitleAddonOnly",true).put("manifest","https://aio.invalid/private/manifest.json"));
        env.storage.save(current);SubtitleSources.Selection o=SubtitleSources.Selection.from(env.storage.load());assertEquals("opensubtitles",o.source);assertEquals("OS-KEY-FAKE",o.apiKey);assertEquals("fake-user",o.username);assertEquals("fake-pass",o.password);assertTrue(o.only);assertEquals("OS-KEY-FAKE",env.storage.load().getJSONObject("subtitleSiteKeys").getString("opensubtitles"));assertEquals("WYZIE-LOCAL-FAKE",env.storage.load().getJSONObject("subtitleSiteKeys").getString("wyzie"));
        // A payload with nothing chosen, or an unknown source, means the free default.
        assertEquals("",env.storage.imported(new JSONObject().put("subtitleSource","bogus")).getString("subtitleSource"));assertEquals("",env.storage.imported(new JSONObject().put("manifest","https://aio.invalid/x/manifest.json")).getString("subtitleSource"));
    }

    // ---- client: exclusive mode, fallback, identity ----
    static JSONObject movieItem()throws Exception{return new JSONObject().put("id","src-"+System.nanoTime()).put("imdb","tt0111161").put("title","Movie 2020").put("movie",true);}
    static final String DEFAULT_ROWS="{\"subtitles\":[{\"id\":\"d1\",\"lang\":\"eng\",\"url\":\"https://subs.example/default-en\",\"subtitleFileName\":\"d.srt\",\"movieReleaseName\":\"Movie 2020\"},{\"id\":\"d2\",\"lang\":\"ara\",\"url\":\"https://subs.example/default-ar\",\"subtitleFileName\":\"d.srt\",\"movieReleaseName\":\"Movie 2020\"}]}";
    static class Internet implements SubtitleClient.Transport {
        final List<String> calls=new ArrayList<>();
        public byte[] read(String url,int max){calls.add(url);return (url.contains("/subtitles/")?DEFAULT_ROWS:SRT).getBytes(StandardCharsets.UTF_8);}
    }
    static String emptyFor(String source){return source.equals("wyzie")?"[]":source.equals("subdl")?"{\"status\":true,\"subtitles\":[]}":source.equals("addon")?"{\"subtitles\":[]}":"{\"data\":[]}";}
    static Set<String> apiHosts(String source){switch(source){case "opensubtitles":return new TreeSet<>(Arrays.asList("api.opensubtitles.com"));case "subdl":return new TreeSet<>(Arrays.asList("api.subdl.com"));case "subsource":return new TreeSet<>(Arrays.asList("api.subsource.net"));case "wyzie":return new TreeSet<>(Arrays.asList("sub.wyzie.io"));default:return new TreeSet<>(Arrays.asList("addon.example"));}}
    @Test public void onlyThisSourceContactsNoOtherSiteWhateverHappens()throws Exception{
        Env env=new Env();
        for(String source:SOURCES){
            env.use(source,source.equals("addon")?"":KEY,true);
            List<Function<SubtitleTransport.Request,SubtitleTransport.Response>> outcomes=new ArrayList<>();
            for(int code:new int[]{401,403,402,429,500,503})outcomes.add(r->status(code,"{}"));outcomes.add(r->ok(emptyFor(source)));outcomes.add(r->ok("garbage"));outcomes.add(r->status(404,"{}"));
            for(Function<SubtitleTransport.Request,SubtitleTransport.Response> outcome:outcomes)for(String lang:new String[]{"en","ar"}){
                Net net=new Net(r->r.url.contains("movies/search")&&outcome.apply(r).status==200?ok("{\"data\":[]}"):outcome.apply(r));Internet internet=new Internet();SubtitleClient client=new SubtitleClient(env.data,movieItem(),internet,sources(net));client.identityRead=true;
                try{client.search(lang);fail("A chosen source with nothing to show must say so, not fall through: "+source);}
                catch(IOException expected){String m=expected.getMessage();assertTrue(m,m.startsWith(sel(source).name()+" · "));assertFalse(m.contains(KEY));}
                assertEquals(source+" must not contact the free default",0,internet.calls.size());assertTrue(source+" "+net.hosts(),apiHosts(source).containsAll(net.hosts()));
            }
        }
        // The exact "nothing found" wording.
        env.use("subdl",KEY,true);Net empty=new Net(r->ok(emptyFor("subdl")));SubtitleClient client=new SubtitleClient(env.data,movieItem(),new Internet(),sources(empty));client.identityRead=true;
        try{client.search("ar");fail();}catch(IOException expected){assertEquals("SubDL · no Arabic subtitles found",expected.getMessage());}
        try{client.search("en");fail();}catch(IOException expected){assertEquals("SubDL · no English subtitles found",expected.getMessage());}
        // Downloads stay on the chosen source too: a failing file never reaches the free default.
        Net rows=new Net(r->r.url.startsWith("https://dl.subdl.com/")?status(500,""):ok("{\"status\":true,\"subtitles\":[{\"lang\":\"EN\",\"url\":\"/subtitle/1-1.zip\",\"release_name\":\"Movie 2020\"}]}"));Internet internet=new Internet();
        SubtitleClient exclusive=new SubtitleClient(env.data,movieItem(),internet,sources(rows));exclusive.identityRead=true;JSONObject candidate=exclusive.search("en").get(0);
        try{exclusive.download(candidate);fail();}catch(SubtitleSources.SubtitleFailure expected){assertEquals(SubtitleSources.SubtitleFailure.Kind.UNAVAILABLE,expected.kind);}assertEquals(0,internet.calls.size());assertEquals(new TreeSet<>(Arrays.asList("api.subdl.com","dl.subdl.com")),rows.hosts());
        // A source that changed since the search is refused.
        env.use("wyzie",KEY,true);try{exclusive.download(candidate);fail();}catch(IOException expected){assertEquals("Subtitle source changed. Search again.",expected.getMessage());}assertEquals(0,internet.calls.size());
    }
    @Test public void withOnlyOffTheFreeDefaultFillsInWhatIsMissing()throws Exception{
        Env env=new Env();env.use("subdl",KEY,false);
        // Chosen source has the language: the default is not contacted.
        Net good=new Net(r->ok("{\"status\":true,\"subtitles\":[{\"lang\":\"EN\",\"url\":\"/subtitle/1-1.zip\",\"release_name\":\"Movie 2020\"}]}"));Internet internet=new Internet();SubtitleClient client=new SubtitleClient(env.data,movieItem(),internet,sources(good));client.identityRead=true;
        assertEquals("subdl",client.search("en").get(0).getString("provider"));assertEquals(0,internet.calls.size());
        // Missing language, empty answer, or a failure: the default fills in, after the chosen source.
        for(Function<SubtitleTransport.Request,SubtitleTransport.Response> outcome:Arrays.<Function<SubtitleTransport.Request,SubtitleTransport.Response>>asList(r->ok(emptyFor("subdl")),r->status(429,"{}"),r->status(401,"{}"),r->status(500,"{}"))){
            Net net=new Net(outcome);Internet fallback=new Internet();SubtitleClient c=new SubtitleClient(env.data,movieItem(),fallback,sources(net));c.identityRead=true;
            List<JSONObject> found=c.search("ar");assertEquals(1,net.requests.size());assertEquals(1,fallback.calls.size());assertTrue(fallback.calls.get(0).startsWith(SubtitleClient.DEFAULT_SOURCE+"/"));assertEquals("https://subs.example/default-ar",found.get(0).getString("url"));assertEquals("",found.get(0).optString("provider"));
        }
        // A download from the chosen source that fails falls back to the default for the same language.
        Net rows=new Net(r->r.url.startsWith("https://dl.subdl.com/")?status(500,""):ok("{\"status\":true,\"subtitles\":[{\"lang\":\"EN\",\"url\":\"/subtitle/1-1.zip\",\"release_name\":\"Movie 2020\"}]}"));Internet downloads=new Internet();
        SubtitleClient c=new SubtitleClient(env.data,movieItem(),downloads,sources(rows));c.identityRead=true;JSONObject sub=c.download(c.search("en").get(0));assertEquals("en",sub.getString("language"));assertTrue(sub.getString("sourceKey").startsWith("sha256:"));assertTrue(downloads.calls.size()>=2);
        // Nothing chosen: exactly today's behaviour, one request to the default and nothing else.
        env.storage.save(new JSONObject());Net untouched=new Net(r->{throw new AssertionError("no optional site may be contacted");});Internet only=new Internet();SubtitleClient plain=new SubtitleClient(env.data,movieItem(),only,sources(untouched));plain.identityRead=true;
        assertEquals(1,plain.search("en").size());assertEquals(1,only.calls.size());assertTrue(only.calls.get(0).startsWith(SubtitleClient.DEFAULT_SOURCE+"/"));assertEquals(0,untouched.requests.size());
    }
    @Test public void savedSubtitlesKeepOnlyAHashOfTheirSource()throws Exception{
        Env env=new Env();String signed="https://sub.wyzie.io/c/abc.srt?tok=SIGNED-FAKE",leak="https://subs.example/ar?token=SECRET-LINK-FAKE";
        env.use("wyzie",KEY,true);Net net=new Net(r->r.url.contains("/search")?ok("[{\"id\":\"w1\",\"url\":\""+signed+"\",\"language\":\"en\",\"fileName\":\"m.srt\",\"release\":\"Movie 2020\"}]"):ok(SRT));Internet internet=new Internet();
        SubtitleClient client=new SubtitleClient(env.data,movieItem(),internet,sources(net));client.identityRead=true;
        JSONObject first=client.download(client.search("en").get(0));JSONObject second=client.download(client.search("en").get(0));
        assertTrue(first.getString("sourceKey").startsWith("sha256:"));assertEquals("stable identity",first.getString("sourceKey"),second.getString("sourceKey"));assertEquals(first.getString("id"),second.getString("id"));
        // The free default path: the returned link itself used to be the sourceKey.
        env.storage.save(new JSONObject());Internet def=new Internet(){@Override public byte[] read(String url,int max){calls.add(url);return (url.contains("/subtitles/")?"{\"subtitles\":[{\"id\":\"d\",\"lang\":\"ara\",\"url\":\""+leak+"\",\"subtitleFileName\":\"d.srt\"}]}":srt("مرحبا")).getBytes(StandardCharsets.UTF_8);}};
        SubtitleClient plain=new SubtitleClient(env.data,movieItem(),def,sources(net));plain.identityRead=true;JSONObject legacy=plain.download(plain.search("ar").get(0));assertTrue(legacy.getString("sourceKey").startsWith("sha256:"));
        env.data.flush();String library=new String(java.nio.file.Files.readAllBytes(new File(env.data.context.getFilesDir(),"library.json").toPath()),StandardCharsets.UTF_8);
        for(String secret:new String[]{KEY,"SIGNED-FAKE","SECRET-LINK-FAKE","abc.srt","default-ar"})assertFalse(secret,library.contains(secret));assertTrue(library.contains("sha256:"));
        for(JSONObject candidate:client.search("en"))assertFalse(candidate.toString().contains(KEY));
        // Older saved values are read unchanged.
        JSONObject old=new JSONObject().put("sourceKey","https://old.example/raw-link").put("extension",".srt").put("language","en");JSONObject saved=client.save(old,SRT);assertEquals("https://old.example/raw-link",saved.getString("sourceKey"));
    }

    // ---- UI ----
    @Test public void pickerShowsOnlyTheFieldsItNeedsAndChecksTheKey()throws Exception{
        android.app.Instrumentation test=InstrumentationRegistry.getInstrumentation();
        Net net=new Net(r->status(401,"{}"));PhoneDownloadSettingsActivity.sourcesFactory=()->sources(net);
        PhoneDownloadSettingsActivity a=(PhoneDownloadSettingsActivity)test.startActivitySync(new Intent(test.getTargetContext(),PhoneDownloadSettingsActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
        try{
            test.runOnMainSync(()->{a.selectSubtitle("");});
            test.runOnMainSync(()->{assertEquals(android.view.View.GONE,a.subtitleKey.getVisibility());assertEquals(android.view.View.GONE,a.subtitleUser.getVisibility());assertEquals(android.view.View.GONE,a.subtitleLink.getVisibility());assertEquals(android.view.View.GONE,a.subtitleGetKey.getVisibility());assertEquals(android.view.View.GONE,a.subtitleNote.getVisibility());assertFalse("Only needs a source",a.subtitleOnly.isEnabled());});
            String[][] expect={{"opensubtitles","key,user,pass,get"},{"subdl","key,get"},{"subsource","key,get"},{"wyzie","key,get,note"},{"addon","link,auth"}};
            for(String[] row:expect){test.runOnMainSync(()->{a.selectSubtitle(row[0]);List<String> shown=Arrays.asList(row[1].split(","));
                assertEquals(row[0],shown.contains("key")?android.view.View.VISIBLE:android.view.View.GONE,a.subtitleKey.getVisibility());assertEquals(row[0],shown.contains("user")?android.view.View.VISIBLE:android.view.View.GONE,a.subtitleUser.getVisibility());assertEquals(row[0],shown.contains("pass")?android.view.View.VISIBLE:android.view.View.GONE,a.subtitlePass.getVisibility());
                assertEquals(row[0],shown.contains("link")?android.view.View.VISIBLE:android.view.View.GONE,a.subtitleLink.getVisibility());assertEquals(row[0],shown.contains("auth")?android.view.View.VISIBLE:android.view.View.GONE,a.subtitleAuth.getVisibility());assertEquals(row[0],shown.contains("get")?android.view.View.VISIBLE:android.view.View.GONE,a.subtitleGetKey.getVisibility());assertEquals(row[0],shown.contains("note")?android.view.View.VISIBLE:android.view.View.GONE,a.subtitleNote.getVisibility());
                assertNotNull(a.subtitleKey.getContentDescription());assertTrue("masked",(a.subtitleKey.getInputType()&android.text.InputType.TYPE_TEXT_VARIATION_PASSWORD)!=0);assertTrue((a.subtitlePass.getInputType()&android.text.InputType.TYPE_TEXT_VARIATION_PASSWORD)!=0);});}
            // A key for one site is kept while looking at another.
            test.runOnMainSync(()->{a.selectSubtitle("subdl");a.subtitleKey.setText("SUBDL-UI-FAKE");a.selectSubtitle("wyzie");assertEquals("",a.subtitleKey.getText().toString());a.subtitleKey.setText("WYZIE-UI-FAKE");a.selectSubtitle("subdl");assertEquals("SUBDL-UI-FAKE",a.subtitleKey.getText().toString());assertTrue(a.subtitleOnly.isEnabled());});
            // Blank key: a plain message, nothing sent.
            test.runOnMainSync(()->{a.subtitleKey.setText("");a.checkSubtitle(new JSONObject(),false);assertEquals("Enter the subtitle site API key.",a.subtitleStatus.getText().toString());});assertEquals(0,net.requests.size());
            // Check with a rejected key says so, and nothing is saved by it.
            test.runOnMainSync(()->{a.subtitleKey.setText("SUBDL-UI-FAKE");a.checkSubtitle(new JSONObject(),false);});
            for(int n=0;n<100&&!a.subtitleStatus.getText().toString().contains("key rejected");n++)Thread.sleep(50);
            assertEquals("SubDL · key rejected",a.subtitleStatus.getText().toString());assertEquals(1,net.requests.size());assertTrue(net.last().url.startsWith("https://api.subdl.com/"));
        }finally{PhoneDownloadSettingsActivity.sourcesFactory=()->new SubtitleSources(new SubtitleTransport());test.runOnMainSync(a::finish);}
    }
}
