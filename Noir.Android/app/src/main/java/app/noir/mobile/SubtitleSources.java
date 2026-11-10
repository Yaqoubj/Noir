package app.noir.mobile;

import org.json.*;
import java.io.*;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.util.zip.GZIPInputStream;

/** Optional subtitle sites (OpenSubtitles.com, SubDL, SubSource, Wyzie Subs, a Stremio addon). Mirrors the PC adapters in Noir/SubtitleProviders.cs. */
final class SubtitleSources {
    static final String OS_BASE="https://api.opensubtitles.com/api/v1/";
    static final String[] SITES={"opensubtitles","subdl","subsource","wyzie"};
    static {String[][] words={{"Free default","المصدر المجاني الافتراضي"},{"Stremio addon","إضافة Stremio"},{"Stremio addon link","رابط إضافة Stremio"},{"Subtitle source","مصدر الترجمة"},{"Subtitle site API key","مفتاح API لموقع الترجمة"},{"Username · optional","اسم المستخدم · اختياري"},{"Password · optional","كلمة المرور · اختيارية"},{"Authorization · optional","التفويض · اختياري"},{"Get a free key","احصل على مفتاح مجاني"},{"Your Wyzie key stays on this phone, encrypted.","يبقى مفتاح Wyzie على هذا الهاتف مشفرًا."},{"Check","تحقق"},{"Checking subtitle source…","جارٍ التحقق من مصدر الترجمة…"},{"works","يعمل"},{"key rejected","تم رفض المفتاح"},{"daily limit reached","تم بلوغ الحد اليومي"},{"temporarily unavailable","غير متاح مؤقتًا"},{"resets","يتجدد في"},{"no Arabic subtitles found","لم يتم العثور على ترجمة عربية"},{"no English subtitles found","لم يتم العثور على ترجمة إنجليزية"},{"Choose a subtitle source.","اختر مصدر ترجمة."},{"Paste a subtitle addon link first.","الصق رابط إضافة الترجمة أولًا."},{"Enter the subtitle site API key.","أدخل مفتاح API لموقع الترجمة."},{"Enter both subtitle username and password, or leave both empty.","أدخل اسم المستخدم وكلمة المرور للترجمة معًا، أو اتركهما فارغين."},{"Subtitle credentials contain invalid characters.","تحتوي بيانات دخول الترجمة على أحرف غير صالحة."},{"Subtitle source changed. Search again.","تغيّر مصدر الترجمة. ابحث مجددًا."},{"Couldn't check the subtitle source. Check the key and connection.","تعذر التحقق من مصدر الترجمة. تحقق من المفتاح والاتصال."},{"Subtitle source changed. Check again.","تغيّر مصدر الترجمة. تحقق مجددًا."},{"Subtitle source saved securely.","تم حفظ مصدر الترجمة بأمان."},{"Default subtitle source","مصدر الترجمة الافتراضي"},{"Use only this source","استخدم هذا المصدر فقط"},{"Save subtitle source","حفظ مصدر الترجمة"},{"Couldn't save subtitle source.","تعذر حفظ مصدر الترجمة."}};for(String[] w:words)Loc.AR.putIfAbsent(w[0],w[1]);}

    /** A validated subtitle choice. Equality includes every secret so an in-memory login is never reused for another account. */
    static final class Selection {
        final String source,apiKey,username,password,addonUrl,addonAuth;final boolean only;
        Selection(String source,String apiKey,String username,String password,String addonUrl,String addonAuth,boolean only){this.source=source==null?"":source;this.apiKey=apiKey==null?"":apiKey;this.username=username==null?"":username;this.password=password==null?"":password;this.addonUrl=addonUrl==null?"":addonUrl;this.addonAuth=addonAuth==null?"":addonAuth;this.only=only;}
        boolean chosen(){return !source.isEmpty();}
        boolean site(){return Arrays.asList(SITES).contains(source);}
        String name(){switch(source){case "opensubtitles":return "OpenSubtitles.com";case "subdl":return "SubDL";case "subsource":return "SubSource";case "wyzie":return "Wyzie Subs";case "addon":return Loc.t("Stremio addon");default:return Loc.t("Free default");}}
        /** An older saved addon link with no source means the addon. A site's key may sit in the per-site map. */
        static Selection from(JSONObject settings){
            String url=settings.optString("subtitleAddonUrl"),source=settings.optString("subtitleSource");if(source.isEmpty()&&!url.isEmpty())source="addon";
            String key=settings.optString("subtitleApiKey");JSONObject keys=settings.optJSONObject("subtitleSiteKeys");if(key.isEmpty()&&keys!=null)key=keys.optString(source);
            return new Selection(source,key,settings.optString("subtitleUsername"),settings.optString("subtitlePassword"),url,settings.optString("subtitleAddonAuth"),settings.optBoolean("subtitleAddonOnly",true));
        }
        Selection validate()throws IOException {
            if(!source.isEmpty()&&!site()&&!source.equals("addon"))throw new IOException("Choose a subtitle source.");
            if(!chosen())return new Selection("","","","","","",only);
            if(source.equals("addon")&&addonUrl.trim().isEmpty())throw new IOException("Paste a subtitle addon link first.");
            if(!source.equals("addon")&&apiKey.trim().isEmpty())throw new IOException("Enter the subtitle site API key.");
            if(source.equals("opensubtitles")&&username.trim().isEmpty()!=password.trim().isEmpty())throw new IOException("Enter both subtitle username and password, or leave both empty.");
            String[] secrets=source.equals("addon")?new String[]{addonAuth}:source.equals("opensubtitles")?new String[]{apiKey,username,password}:new String[]{apiKey};
            for(String secret:secrets)for(int i=0;i<secret.length();i++)if(Character.isISOControl(secret.charAt(i)))throw new IOException("Subtitle credentials contain invalid characters.");
            return new Selection(source,source.equals("addon")?"":apiKey.trim(),source.equals("opensubtitles")?username.trim():"",source.equals("opensubtitles")&&!password.trim().isEmpty()?password:"",source.equals("addon")?SubtitleClient.normalizeAddon(addonUrl):"",source.equals("addon")?addonAuth:"",only);
        }
        @Override public boolean equals(Object o){if(!(o instanceof Selection))return false;Selection s=(Selection)o;return source.equals(s.source)&&apiKey.equals(s.apiKey)&&username.equals(s.username)&&password.equals(s.password)&&addonUrl.equals(s.addonUrl)&&addonAuth.equals(s.addonAuth)&&only==s.only;}
        @Override public int hashCode(){return Objects.hash(source,apiKey,username,password,addonUrl,addonAuth,only);}
        @Override public String toString(){return name();}
    }
    /** Failures never carry a URL, key or provider text. */
    static final class SubtitleFailure extends IOException {
        enum Kind {BAD_KEY,LIMIT,UNAVAILABLE}
        final Kind kind;final String sourceName,reset;final int status;
        SubtitleFailure(Selection source,Kind kind,String reset,int status){super(source.name()+" · "+Loc.t(kind==Kind.BAD_KEY?"key rejected":kind==Kind.LIMIT?"daily limit reached":"temporarily unavailable")+(reset.isEmpty()?"":" · "+Loc.t("resets")+" "+reset));this.kind=kind;this.sourceName=source.name();this.reset=reset;this.status=status;}
    }

    final SubtitleTransport transport;final Object gate=new Object();
    Selection session;String token="",base=OS_BASE;
    SubtitleSources(SubtitleTransport transport){this.transport=transport;}
    static String keyPage(String source){switch(source){case "opensubtitles":return "https://www.opensubtitles.com/consumers";case "subdl":return "https://subdl.com/panel/api";case "subsource":return "https://subsource.net/api-docs";case "wyzie":return "https://docs.wyzie.io/subs/usage/api-keys";default:return "";}}

    static Object get(Object e,String key){Object v=e instanceof JSONObject?((JSONObject)e).opt(key):null;return v==JSONObject.NULL?null:v;}
    static String text(Object e,String key){Object v=get(e,key);if(v instanceof String)return (String)v;if(v instanceof Double||v instanceof Float){double d=((Number)v).doubleValue();return d==Math.rint(d)&&Math.abs(d)<1e15?Long.toString((long)d):v.toString();}return v instanceof Number?v.toString():"";}
    static JSONArray array(Object e){return e instanceof JSONArray?(JSONArray)e:null;}
    static String joined(Object e){if(e instanceof String)return (String)e;JSONArray a=array(e);if(a==null)return "";StringBuilder b=new StringBuilder();for(int i=0;i<a.length();i++)if(a.opt(i) instanceof String){if(b.length()>0)b.append(' ');b.append((String)a.opt(i));}return b.toString();}
    static boolean mismatch(Object e,String key,int expected){try{return Integer.parseInt(text(e,key))!=expected;}catch(NumberFormatException x){return false;}}
    static String basename(String path){return path.substring(Math.max(path.lastIndexOf('/'),path.lastIndexOf('\\'))+1);}
    static String query(Map<String,String> values){StringBuilder b=new StringBuilder();for(Map.Entry<String,String> e:values.entrySet()){if(b.length()>0)b.append('&');b.append(SubtitleClient.encode(e.getKey())).append('=').append(SubtitleClient.encode(e.getValue()));}return b.toString();}
    static Map<String,String> params(String... pairs){Map<String,String> m=new LinkedHashMap<>();for(int i=0;i+1<pairs.length;i+=2)m.put(pairs[i],pairs[i+1]);return m;}
    static SubtitleFailure unavailable(Selection s){return new SubtitleFailure(s,SubtitleFailure.Kind.UNAVAILABLE,"",0);}
    static boolean containsSecret(String url,Selection s){
        String decoded;try{decoded=URLDecoder.decode(url,"UTF-8");}catch(Exception e){decoded=url;}
        String auth=s.addonAuth,bare=auth.indexOf(' ')>=0?auth.substring(auth.indexOf(' ')+1):"";
        for(String secret:new String[]{s.apiKey,s.password,auth,bare}){if(secret.isEmpty())continue;if(decoded.contains(secret)||url.contains(secret)||url.contains(SubtitleClient.encode(secret)))return true;}return false;
    }
    Object json(Selection s,String url,String method,String body,boolean empty404,String access,long deadline)throws IOException {
        byte[] bytes=transport.send(s,url,true,method,body,SubtitleTransport.CAP,empty404,access,true,deadline);
        try{return new JSONTokener(new String(bytes,StandardCharsets.UTF_8)).nextValue();}catch(JSONException e){throw unavailable(s);}
    }
    void login(Selection s,long deadline)throws IOException {
        base=OS_BASE;token="";session=null;JSONObject credentials=new JSONObject();DataStore.put(credentials,"username",s.username);DataStore.put(credentials,"password",s.password);
        Object doc=json(s,OS_BASE+"login","POST",credentials.toString(),false,"",deadline);
        String access=text(doc,"token");if(access.isEmpty())throw unavailable(s);for(int i=0;i<access.length();i++)if(Character.isISOControl(access.charAt(i)))throw unavailable(s);
        String target=text(doc,"base_url");
        if(!target.isEmpty()){
            if(!target.contains("://"))target="https://"+target;
            try{URI u=new URI(target);String host=u.getHost()==null?"":u.getHost().toLowerCase(Locale.ROOT);if(!SubtitleTransport.validUrl(target)||!(host.equals("api.opensubtitles.com")||host.equals("vip-api.opensubtitles.com"))||u.getRawQuery()!=null)throw unavailable(s);base="https://"+host+"/api/v1/";}
            catch(URISyntaxException e){throw unavailable(s);}
        }
        token=access;session=s;
    }
    /** Optional login raises the quota. The token lives in memory only; one re-login on 401. */
    Object open(Selection s,String path,String body,long deadline)throws IOException {
        synchronized(gate){
            if(!s.equals(session)){token="";base=OS_BASE;if(!s.username.isEmpty()&&!s.password.isEmpty())login(s,deadline);else session=s;}
            try{return json(s,base+path,body==null?"GET":"POST",body,false,token,deadline);}
            catch(SubtitleFailure e){if(e.status==401&&!s.username.isEmpty()&&!s.password.isEmpty()){login(s,deadline);return json(s,base+path,body==null?"GET":"POST",body,false,token,deadline);}throw e;}
        }
    }
    /** One search for tt0111161 in English through the same adapter. Returns the addon's name for an addon, otherwise "". */
    String check(Selection selection)throws IOException {
        Selection s=selection.validate();if(!s.chosen())return "";long deadline=SubtitleTransport.deadline();String name="";
        if(s.source.equals("addon")){byte[] bytes=transport.send(s,s.addonUrl+"/manifest.json",true,"GET",null,SubtitleTransport.MANIFEST_CAP,false,"",true,deadline);try{name=SubtitleClient.manifestName(bytes);}catch(Exception e){throw unavailable(s);}}
        JSONObject item=new JSONObject();DataStore.put(item,"imdb","tt0111161");DataStore.put(item,"movie",true);
        find(s,item,"","en","",deadline);return name;
    }
    List<JSONObject> search(Selection selection,JSONObject item,String release,String lang,String hash)throws IOException {
        Selection s=selection.validate();if(!s.chosen())return new ArrayList<>();
        return find(s,item,release==null?"":release,lang,hash==null?"":hash,SubtitleTransport.deadline());
    }
    final class Found {
        final List<JSONObject> list=new ArrayList<>();final Selection s;final boolean movie;final int season,episode;final String release,lang;
        Found(Selection s,boolean movie,int season,int episode,String release,String lang){this.s=s;this.movie=movie;this.season=season;this.episode=episode;this.release=release;this.lang=lang;}
        void add(String id,String language,String name,String rel,String url,boolean exact){
            if(list.size()>=500||!lang.equals(language))return;
            if(!url.isEmpty()&&(!SubtitleTransport.validUrl(url)||containsSecret(url,s)))return;
            if(!movie&&SubtitleArchive.hasEpisodeName(name+" "+rel)&&!SubtitleArchive.episodeMatches(name+" "+rel,season,episode))return;
            JSONObject c=new JSONObject();DataStore.put(c,"id",id);DataStore.put(c,"language",language);DataStore.put(c,"subtitleFileName",name);DataStore.put(c,"movieReleaseName",rel);DataStore.put(c,"url",url);DataStore.put(c,"m",exact?"h":"");DataStore.put(c,"score",(exact?1000:0)+SubtitleClient.releaseScore(name+" "+rel,release));DataStore.put(c,"provider",s.source);DataStore.put(c,"ref",id);list.add(c);
        }
        List<JSONObject> result(){
            List<JSONObject> sorted=new ArrayList<>(list);sorted.sort(Comparator.comparingInt((JSONObject c)->c.optInt("score")).reversed());
            List<JSONObject> out=new ArrayList<>();Set<String> seen=new HashSet<>();
            for(JSONObject c:sorted){String key=c.optString("url").isEmpty()?c.optString("language")+":"+c.optString("id"):c.optString("url");if(seen.add(key)&&out.size()<500)out.add(c);}
            return out;
        }
    }
    List<JSONObject> find(Selection s,JSONObject item,String release,String lang,String hash,long deadline)throws IOException {
        String imdb=item.optString("imdb");if(!imdb.matches("tt\\d+"))return new ArrayList<>();if(!hash.matches("[0-9a-fA-F]{16}"))hash="";
        if(!lang.equals("en")&&!lang.equals("ar"))return new ArrayList<>();
        boolean movie=item.optBoolean("movie");int season=item.optInt("season"),episode=item.optInt("episode");Found found=new Found(s,movie,season,episode,release,lang);
        switch(s.source){
            case "opensubtitles":{
                String digits=imdb.substring(2).replaceFirst("^0+","");if(digits.isEmpty())digits="0";
                Map<String,String> p=params("languages",lang,movie?"imdb_id":"parent_imdb_id",digits);if(!movie){p.put("season_number",Integer.toString(season));p.put("episode_number",Integer.toString(episode));}if(!hash.isEmpty())p.put("moviehash",hash);
                Object doc=open(s,"subtitles?"+query(p),null,deadline);JSONArray data=array(get(doc,"data"));if(data==null)throw unavailable(s);
                for(int i=0;i<Math.min(500,data.length());i++){Object row=data.opt(i),a=get(row,"attributes"),details=get(a,"feature_details");if(!movie&&(mismatch(details,"season_number",season)||mismatch(details,"episode_number",episode)))continue;
                    JSONArray files=array(get(a,"files"));if(files!=null)for(int k=0;k<Math.min(100,files.length());k++){Object file=files.opt(k);String id=text(file,"file_id");long n;try{n=Long.parseLong(id);}catch(NumberFormatException x){continue;}if(n<=0)continue;found.add(id,text(a,"language"),text(file,"file_name"),text(a,"release"),"",Boolean.TRUE.equals(get(a,"moviehash_match")));}}
                break;}
            case "subdl":{
                Map<String,String> p=params("api_key",s.apiKey,"imdb_id",imdb,"type",movie?"movie":"tv","languages",lang.toUpperCase(Locale.ROOT),"subs_per_page","30","releases","1");if(!movie){p.put("season_number",Integer.toString(season));p.put("episode_number",Integer.toString(episode));}if(!release.isEmpty())p.put("file_name",basename(release));
                Object root=json(s,"https://api.subdl.com/api/v1/subtitles?"+query(p),"GET",null,true,"",deadline);if(root instanceof JSONArray&&((JSONArray)root).length()==0)break;
                if(Boolean.FALSE.equals(get(root,"status"))||Boolean.FALSE.equals(get(root,"success"))){String error=(text(root,"error")+" "+text(root,"message")).toLowerCase(Locale.ROOT);throw new SubtitleFailure(s,error.contains("key")||error.contains("forbidden")?SubtitleFailure.Kind.BAD_KEY:error.contains("limit")||error.contains("quota")?SubtitleFailure.Kind.LIMIT:SubtitleFailure.Kind.UNAVAILABLE,"",0);}
                JSONArray rows=array(get(root,"subtitles"));if(rows==null)throw unavailable(s);
                for(int i=0;i<Math.min(500,rows.length());i++){Object row=rows.opt(i);
                    if(!movie){if(mismatch(row,"season",season))continue;if(!Boolean.TRUE.equals(get(row,"full_season"))&&mismatch(row,"episode",episode))continue;try{if(Integer.parseInt(text(row,"episode_from"))>episode)continue;}catch(NumberFormatException x){}try{if(Integer.parseInt(text(row,"episode_end"))<episode)continue;}catch(NumberFormatException x){}}
                    String path=text(row,"url");if(!path.startsWith("/")||path.startsWith("//"))continue;String link="https://dl.subdl.com"+path;URI uri;try{uri=new URI(link);}catch(URISyntaxException x){continue;}if(!"dl.subdl.com".equalsIgnoreCase(uri.getHost()))continue;
                    String code=text(row,"lang");if(code.isEmpty())code=text(row,"language");code=code.toLowerCase(Locale.ROOT);found.add("",code.equals("en")||code.equals("english")?"en":code.equals("ar")||code.equals("arabic")?"ar":"",text(row,"name"),text(row,"release_name")+" "+joined(get(row,"releases")),uri.toASCIIString(),false);}
                break;}
            case "subsource":{
                Map<String,String> p=params("searchType","imdb","imdb",imdb);if(!movie)p.put("season",Integer.toString(season));
                Object lookup=json(s,"https://api.subsource.net/api/v1/movies/search?"+query(p),"GET",null,true,"",deadline);JSONArray data=lookup instanceof JSONArray?(JSONArray)lookup:array(get(lookup,"data"));if(data==null)throw unavailable(s);
                String movieId="";for(int i=0;i<data.length();i++){Object r=data.opt(i);if(text(r,"imdbId").equals(imdb)&&(movie||text(r,"season").equals(Integer.toString(season)))){movieId=text(r,"movieId");break;}}if(movieId.isEmpty())break;
                p=params("movieId",movieId,"language",lang.equals("en")?"english":"arabic","limit","100");if(!movie){p.put("seasonNumber",Integer.toString(season));p.put("episodeNumber",Integer.toString(episode));}
                Object doc=json(s,"https://api.subsource.net/api/v1/subtitles?"+query(p),"GET",null,true,"",deadline);data=doc instanceof JSONArray?(JSONArray)doc:array(get(doc,"data"));if(data==null)throw unavailable(s);
                for(int i=0;i<Math.min(500,data.length());i++){Object row=data.opt(i);String id=text(row,"subtitleId");String returned=text(row,"movieId");if(id.isEmpty()||!returned.isEmpty()&&!returned.equals(movieId))continue;String l=text(row,"language").toLowerCase(Locale.ROOT);found.add(id,l.equals("english")?"en":l.equals("arabic")?"ar":"","subtitle.srt",joined(get(row,"releaseInfo")),"",false);}
                break;}
            case "wyzie":{
                Map<String,String> p=params("id",imdb,"language",lang,"key",s.apiKey,"limit","200");if(!movie){p.put("season",Integer.toString(season));p.put("episode",Integer.toString(episode));}if(!hash.isEmpty())p.put("hash",hash);if(!release.isEmpty())p.put("video",basename(release));
                Object doc=json(s,"https://sub.wyzie.io/search?"+query(p),"GET",null,true,"",deadline);JSONArray rows=array(doc);if(rows==null)throw unavailable(s);
                for(int i=0;i<Math.min(500,rows.length());i++){Object row=rows.opt(i);String url=text(row,"url");URI uri;try{uri=new URI(url);}catch(URISyntaxException x){continue;}if(!SubtitleTransport.validUrl(url)||!"sub.wyzie.io".equalsIgnoreCase(uri.getHost())||uri.getPath()==null||!uri.getPath().startsWith("/c/"))continue;
                    String rel=text(row,"release")+" "+joined(get(row,"releases")),name=text(row,"fileName");if(!SubtitleArchive.textSubtitle(name))name="subtitle."+text(row,"format");
                    if(!movie&&java.util.regex.Pattern.compile("(?i)s\\d+[ ._-]*e\\d+|\\d+x\\d+").matcher(name+" "+rel).find()&&!SubtitleArchive.episodeMatches(name+" "+rel,season,episode))continue;
                    boolean exact=false;JSONArray reasons=array(get(get(row,"match"),"reasons"));if(reasons!=null)for(int k=0;k<reasons.length();k++)if("hash".equals(reasons.opt(k)))exact=true;
                    found.add(text(row,"id"),text(row,"language"),name,rel,url,exact);}
                break;}
            case "addon":{
                String id=movie?imdb:imdb+":"+season+":"+episode,extra="filename="+SubtitleClient.encode(basename(release));if(!hash.isEmpty())extra+="&videoHash="+hash;
                Object doc=json(s,s.addonUrl+"/subtitles/"+(movie?"movie/":"series/")+SubtitleClient.encode(id)+"/"+extra+".json","GET",null,false,"",deadline);JSONArray rows=array(get(doc,"subtitles"));if(rows==null)throw unavailable(s);
                for(int i=0;i<Math.min(500,rows.length());i++){Object row=rows.opt(i);if(!movie&&(mismatch(row,"season",season)||mismatch(row,"episode",episode)))continue;String url=text(row,"url");if(url.isEmpty())continue;found.add(text(row,"id"),SubtitleClient.language(text(row,"lang")),text(row,"subtitleFileName"),text(row,"movieReleaseName"),url,text(row,"m").equals("h"));}
                break;}
            default:break;
        }
        return found.result();
    }
    /** The bytes of one subtitle file for a candidate that came from {@link #search}. */
    SubtitleArchive.Picked download(Selection selection,JSONObject candidate,JSONObject item,String release)throws IOException {
        Selection s=selection.validate();long deadline=SubtitleTransport.deadline();
        try{
            byte[] bytes;String url=candidate.optString("url"),ref=candidate.optString("ref"),name=candidate.optString("subtitleFileName");
            if(!candidate.optString("provider").equals(s.source))throw new IOException();
            if(s.source.equals("opensubtitles")){
                long fileId;try{fileId=Long.parseLong(ref);}catch(NumberFormatException e){throw new IOException();}
                JSONObject request=new JSONObject();DataStore.put(request,"file_id",fileId);DataStore.put(request,"sub_format","srt");
                Object doc=open(s,"download",request.toString(),deadline);url=text(doc,"link");if(containsSecret(url,s))throw new IOException();
                bytes=transport.send(s,url,false,"GET",null,SubtitleTransport.CAP,false,"",false,deadline);
            }else if(s.source.equals("subsource")){
                bytes=transport.send(s,"https://api.subsource.net/api/v1/subtitles/"+SubtitleClient.encode(ref)+"/download",true,"GET",null,SubtitleTransport.CAP,false,"",false,deadline);
            }else{
                boolean auth=false;if(s.source.equals("addon"))try{auth=new URI(url).getHost().equalsIgnoreCase(new URI(s.addonUrl).getHost());}catch(Exception e){auth=false;}
                bytes=transport.send(s,url,auth,"GET",null,SubtitleTransport.CAP,false,"",false,deadline);
            }
            if(s.source.equals("subdl")||s.source.equals("subsource")){SubtitleArchive.Picked picked=SubtitleArchive.pick(bytes,item.optBoolean("movie"),item.optInt("season"),item.optInt("episode"),release);bytes=picked.bytes;name=picked.name;}
            else if(bytes.length>2&&(bytes[0]&255)==31&&(bytes[1]&255)==139)try(InputStream gzip=new GZIPInputStream(new ByteArrayInputStream(bytes))){bytes=DataStore.read(gzip,SubtitleClient.MAX);}
            String ext=SubtitleArchive.textSubtitle(name)?name.substring(name.lastIndexOf('.')).toLowerCase(Locale.ROOT):".srt";
            if(!SubtitleClient.validText(SubtitleClient.decode(bytes),ext))throw new IOException();
            return new SubtitleArchive.Picked(bytes,name);
        }catch(SubtitleFailure|InterruptedIOException e){throw e;}
        catch(Exception e){if(Thread.currentThread().isInterrupted())throw new InterruptedIOException("Subtitle request cancelled.");throw unavailable(s);}
    }
}
