package app.noir.mobile;
import org.json.*;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.text.Normalizer;
import java.time.*;
import java.util.*;
import java.util.regex.*;
import java.util.zip.*;

/** History files on the phone: the same formats the PC reads and writes. No network, no UI. */
final class PhoneHistory {
    /** One watched movie or episode read from another app, before it is matched to a title. */
    static final class Row {
        String service="",title="",imdb="",tvdb="",episodeTitle="",source="",note="";int year,season,episode,rating,showRating;Boolean movie;long at,loggedAt;boolean approximate;
        Row(String service,String title,int year,Boolean movie,String imdb,String tvdb,int season,int episode,String episodeTitle,long at,int rating){this.service=service;this.title=title;this.year=year;this.movie=movie;this.imdb=imdb;this.tvdb=tvdb;this.season=season;this.episode=episode;this.episodeTitle=episodeTitle;this.at=at;this.rating=rating;}
        String key(){return (imdb.matches("tt\\d+")?imdb:!tvdb.isEmpty()?"tvdb:"+tvdb:normalize(title)+":"+year)+":"+movie;}
    }
    static final class Parsed {String service="";List<Row> rows=new ArrayList<>();byte[] backup;}

    static Parsed read(String name,byte[] bytes)throws IOException{
        Parsed result=new Parsed();List<String[]> files=new ArrayList<>();boolean zip=bytes.length>4&&bytes[0]=='P'&&bytes[1]=='K';
        if(zip){try(ZipInputStream in=new ZipInputStream(new ByteArrayInputStream(bytes))){ZipEntry e;while((e=in.getNextEntry())!=null){String n=new File(e.getName()).getName();byte[] data=DataStore.read(in,64*1024*1024);
            if(n.equals("library.json")||n.equals("phone.json")){result.backup=bytes;}
            if(n.toLowerCase(Locale.ROOT).matches(".*\\.(csv|json)")&&!n.equals("manifest.json"))files.add(new String[]{n,new String(data,StandardCharsets.UTF_8)});}}
            if(result.backup!=null){result.service="Backup";return result;}}
        else files.add(new String[]{name,new String(bytes,StandardCharsets.UTF_8)});
        List<Object[]> parsed=new ArrayList<>();
        for(String[] f:files){try{boolean[] diary={false};Parsed p=f[0].toLowerCase(Locale.ROOT).endsWith(".json")||f[1].trim().startsWith("[")||f[1].trim().startsWith("{")?json(f[1]):csv(f[0],f[1],diary);if(!p.service.isEmpty())parsed.add(new Object[]{p,diary[0]});}catch(Exception e){if(!zip)throw new IOException("This history export could not be read. Check that it is a complete CSV, JSON or ZIP export.");}}
        if(parsed.isEmpty())throw new IOException("No recognizable watch history was found. Choose a viewing-history CSV, JSON or export ZIP.");
        // Letterboxd: the diary has dates; watched.csv and ratings.csv fill in films and ratings it lacks.
        Map<String,Integer> ratings=new HashMap<>();Set<String> present=new HashSet<>();List<Row> other=new ArrayList<>();Set<String> services=new LinkedHashSet<>();Set<String> tvtime=new HashSet<>();
        for(Object[] o:parsed){Parsed p=(Parsed)o[0];services.add(p.service);if(!p.service.equals("Letterboxd")){for(Row r:p.rows)if(!r.service.equals("TV Time")||tvtime.add(r.key()+r.season+":"+r.episode+":"+r.at))result.rows.add(r);continue;}
            for(Row r:p.rows){if((Boolean)o[1]){present.add(movieKey(r));result.rows.add(r);}else{other.add(r);if(r.rating>0)ratings.putIfAbsent(movieKey(r),r.rating);}}}
        for(Row r:result.rows)if(r.service.equals("Letterboxd")&&r.rating==0)r.rating=ratings.getOrDefault(movieKey(r),0);
        for(Row r:other)if(present.add(movieKey(r))){if(r.rating==0)r.rating=ratings.getOrDefault(movieKey(r),0);result.rows.add(r);}
        result.service=services.size()==1?services.iterator().next():"Mixed history";return result;
    }
    static String movieKey(Row r){return normalize(r.title)+":"+r.year;}
    static String key(String s){return s.replace("\uFEFF","").toLowerCase(Locale.ROOT).replaceAll("[^a-z0-9]","");}
    static String normalize(String s){s=Normalizer.normalize(s.toLowerCase(Locale.ROOT).replace("&"," and "),Normalizer.Form.NFD).replaceAll("\\p{Mn}","");s=s.replaceAll("[^\\p{L}\\p{N}]+"," ").trim().replaceAll("^the\\s+","");return s.replace(" ","");}
    static int integer(String s){try{return Integer.parseInt(s.trim());}catch(Exception e){return 0;}}
    static int rating(String s,int divisor){try{double n=Double.parseDouble(s.trim());if(!(n>0)||Double.isInfinite(n))return 0;return (int)Math.max(1,Math.min(5,Math.round(n/divisor)));}catch(Exception e){return 0;}}
    static boolean dayFirst(List<String> dates){Pattern p=Pattern.compile("^(\\d{1,2})[/.\\-](\\d{1,2})[/.\\-]\\d{2,4}$");for(String s:dates){Matcher m=p.matcher(s);if(!m.find())continue;if(integer(m.group(1))>12)return true;if(integer(m.group(2))>12)return false;}return !Locale.getDefault().getCountry().equals("US");}
    static long date(String s,Boolean dayFirst){
        if(s==null||s.trim().isEmpty())return 0;s=s.trim();
        if(s.matches("\\d{9,}")){long n=Long.parseLong(s);return n>100000000000L?n:n*1000;}
        Matcher m=Pattern.compile("^(\\d{1,2})[/.\\-](\\d{1,2})[/.\\-](\\d{2}|\\d{4})$").matcher(s);
        if(m.find()){int a=integer(m.group(1)),b=integer(m.group(2)),y=integer(m.group(3));if(y<100)y+=y<50?2000:1900;boolean d=dayFirst!=null?dayFirst:dayFirst(List.of(s));try{return LocalDate.of(y,d?b:a,d?a:b).atStartOfDay(ZoneOffset.UTC).toInstant().toEpochMilli();}catch(Exception e){return 0;}}
        try{return OffsetDateTime.parse(s).toInstant().toEpochMilli();}catch(Exception ignored){}
        try{return Instant.parse(s).toEpochMilli();}catch(Exception ignored){}
        try{return LocalDateTime.parse(s.replace(' ','T')).toInstant(ZoneOffset.UTC).toEpochMilli();}catch(Exception ignored){}
        try{return LocalDate.parse(s.length()>=10?s.substring(0,10):s).atStartOfDay(ZoneOffset.UTC).toInstant().toEpochMilli();}catch(Exception ignored){}
        return 0;
    }
    static List<List<String>> table(String text)throws IOException{
        List<List<String>> rows=new ArrayList<>();List<String> row=new ArrayList<>();StringBuilder field=new StringBuilder();boolean quoted=false;
        for(int i=0;i<text.length();i++){char c=text.charAt(i);if(i==0&&c=='\uFEFF')continue;
            if(c=='"'){if(quoted&&i+1<text.length()&&text.charAt(i+1)=='"'){field.append('"');i++;}else if(quoted||field.length()==0)quoted=!quoted;else field.append(c);}
            else if(!quoted&&(c==','||c=='\r'||c=='\n')){row.add(field.toString());field.setLength(0);if(c!=','){rows.add(row);row=new ArrayList<>();if(c=='\r'&&i+1<text.length()&&text.charAt(i+1)=='\n')i++;}}
            else field.append(c);}
        if(quoted)throw new IOException("An exported CSV field has no closing quote.");
        if(field.length()>0||!row.isEmpty()){row.add(field.toString());rows.add(row);}return rows;
    }
    static Parsed csv(String name,String text,boolean[] diary)throws IOException{
        Parsed result=new Parsed();List<List<String>> table=table(text);if(table.isEmpty())return result;
        List<String> headers=new ArrayList<>();for(String h:table.get(0))headers.add(key(h));String file=new File(name).getName();
        diary[0]=file.equalsIgnoreCase("diary.csv")||headers.contains("watcheddate");
        java.util.function.Predicate<String[]> has=keys->{for(String k:keys)if(headers.contains(key(k)))return true;return false;};
        boolean netflix=has.test(new String[]{"title"})&&has.test(new String[]{"date"})&&(headers.size()==2||file.matches("(?i).*(NetflixViewingHistory|ViewingActivity).*"));
        boolean letterboxd=has.test(new String[]{"name","title"})&&(has.test(new String[]{"Letterboxd URI"})||has.test(new String[]{"imdbID"})&&has.test(new String[]{"Rewatch"})||file.matches("(?i)^(diary|watched|ratings)\\.csv$"));
        boolean imdb=has.test(new String[]{"Const"})&&has.test(new String[]{"Your Rating"});
        boolean noir=has.test(new String[]{"date_unknown"})&&has.test(new String[]{"source"})&&has.test(new String[]{"imdb_id"});
        boolean tvtime=has.test(new String[]{"tv_show_name","series_name","show_name","movie_name"})&&has.test(new String[]{"episode_number","number","episode_title","episode_season_number","season_number","season","watched_at","created_at","updated_at"});
        if(!netflix&&!letterboxd&&!imdb&&!noir&&!tvtime&&!(has.test(new String[]{"title","name"})&&has.test(new String[]{"imdb","imdb_id","const","date","watched_at","watched date","season","episode","episode_title","type","year","rating"})))return result;
        result.service=netflix?"Netflix":letterboxd?"Letterboxd":imdb?"IMDb":noir?"Noir":tvtime?"TV Time":"CSV";
        List<List<String>> data=new ArrayList<>();for(List<String> r:table.subList(1,table.size()))for(String v:r)if(!v.isEmpty()){data.add(r);break;}
        java.util.function.BiFunction<List<String>,String[],String> get=(row,keys)->{for(String k:keys){int i=headers.indexOf(key(k));if(i>=0&&i<row.size()&&!row.get(i).isEmpty())return row.get(i).trim();}return "";};
        List<String> dates=new ArrayList<>();for(List<String> r:data)dates.add(get.apply(r,new String[]{"Date","Watched Date","watched_at","created_at","updated_at","Date Rated"}));boolean dayFirst=dayFirst(dates);
        for(List<String> row:data){
            String title=get.apply(row,new String[]{"tv_show_name","series_name","show_name","movie_name","title","name"}),id=get.apply(row,new String[]{"imdb_id","imdb","imdbID","const"});
            if(title.isEmpty()&&id.isEmpty())continue;
            int year=integer(get.apply(row,new String[]{"year"})),season=integer(get.apply(row,new String[]{"episode_season_number","season_number","season"})),episode=integer(get.apply(row,new String[]{"episode_number","episode","number"}));String epTitle=get.apply(row,new String[]{"episode_title","episode_name"});
            String type=get.apply(row,new String[]{"entity_type","type","title type"}).toLowerCase(Locale.ROOT);
            Boolean movie=type.contains("movie")||type.equals("short")||type.equals("video")||type.equals("tvspecial")?Boolean.TRUE:type.contains("episode")||type.contains("series")||type.equals("show")||type.equals("anime")?Boolean.FALSE:null;
            if(movie==null&&!get.apply(row,new String[]{"movie_name"}).isEmpty())movie=true;
            if(movie==null&&(season>0||episode>0||!get.apply(row,new String[]{"tv_show_name","series_name","show_name"}).isEmpty()))movie=false;
            long at=date(get.apply(row,new String[]{"watched_at","Watched Date","WatchedDate","Date Rated","created_at","updated_at","date"}),dayFirst);
            int rating=rating(get.apply(row,new String[]{imdb?"Your Rating":"Rating"}),imdb?2:1);
            if(netflix){Object[] t=netflixTitle(title);title=(String)t[0];movie=(Boolean)t[1];season=(Integer)t[2];epTitle=(String)t[3];episode=0;}
            if(letterboxd){movie=true;if(file.equalsIgnoreCase("watched.csv"))at=0;}
            if(noir&&get.apply(row,new String[]{"date_unknown"}).equalsIgnoreCase("true"))at=0;
            Row r=new Row(result.service,title,year,movie,id,get.apply(row,new String[]{"tvdb_id","tv_show_id","series_id","tvdb"}),season,episode,epTitle,at,rating);
            if(noir){r.approximate=get.apply(row,new String[]{"approximate"}).equalsIgnoreCase("true");r.source=get.apply(row,new String[]{"source"});}
            result.rows.add(r);
        }
        return result;
    }
    static final String[] WORDS={"one","two","three","four","five","six","seven","eight","nine","ten"};
    static Object[] netflixTitle(String value){
        String[] parts=value.split(": ");
        for(int i=1;i<parts.length;i++){
            Matcher sequel=Pattern.compile("^(.+?)\\s+(\\d{1,2})$").matcher(parts[i]);
            if(sequel.find()&&normalize(sequel.group(1)).equals(normalize(parts[0])))return new Object[]{parts[0],false,Integer.parseInt(sequel.group(2)),join(parts,i+1)};
            Matcher m=Pattern.compile("(?i)^(?:(?:season|series|part|volume|vol\\.?|book|chapter|collection)\\s+(\\d+|one|two|three|four|five|six|seven|eight|nine|ten)|limited series|miniseries)$").matcher(parts[i]);
            if(!m.find())continue;String n=m.group(1)==null?"":m.group(1).toLowerCase(Locale.ROOT);
            int season=n.matches("\\d+")?Integer.parseInt(n):n.isEmpty()?1:Arrays.asList(WORDS).indexOf(n)+1;
            return new Object[]{String.join(": ",Arrays.copyOfRange(parts,0,i)),false,season,join(parts,i+1)};
        }
        return new Object[]{value,parts.length==1?Boolean.TRUE:null,0,parts.length>1?parts[parts.length-1]:""};
    }
    static String join(String[] parts,int from){return from>=parts.length?"":String.join(": ",Arrays.copyOfRange(parts,from,parts.length));}
    static String s(JSONObject j,String... keys){if(j==null)return "";for(String k:keys){Object v=j.opt(k);if(v!=null&&v!=JSONObject.NULL){String s=v.toString();if(!s.isEmpty())return s;}}return "";}
    static Parsed json(String text)throws JSONException{
        Parsed result=new Parsed();text=text.replace("\uFEFF","").trim();Object root=new JSONTokener(text).nextValue();
        JSONArray array=root instanceof JSONArray?(JSONArray)root:root instanceof JSONObject?((JSONObject)root).optJSONArray("history"):null;
        if(array!=null){List<JSONObject> values=DataStore.objects(array);
            if(values.isEmpty()){result.service=root instanceof JSONObject?"Trakt":"Noir";return result;}
            boolean noir=false,watched=false,trakt=false,rated=false;for(JSONObject v:values){noir|=v.has("imdb_id")||v.has("date_unknown");watched|=v.has("watched_at")||v.has("last_watched_at");trakt|=v.has("movie")||v.has("show")||v.has("episode");rated|=v.has("rated_at");}
            if(noir){result.service="Noir";for(JSONObject v:values){Row r=new Row("Noir",s(v,"title"),integer(s(v,"year")),s(v,"type").equals("movie"),s(v,"imdb_id"),"",integer(s(v,"season")),integer(s(v,"episode")),s(v,"episode_title"),s(v,"date_unknown").equals("true")?0:date(s(v,"watched_at"),null),rating(s(v,"rating"),1));
                r.approximate=s(v,"approximate").equals("true");r.source=s(v,"source");r.loggedAt=date(s(v,"logged_at"),null);r.note=s(v,"note");r.showRating=rating(s(v,"show_rating"),1);result.rows.add(r);}return result;}
            if(rated&&!watched)return result;
            if(trakt){result.service="Trakt";for(JSONObject v:values)traktItem(v,result.rows);return result;}
        }
        if(root instanceof JSONObject){JSONObject o=(JSONObject)root;if(o.optJSONArray("movies")!=null||o.optJSONArray("shows")!=null||o.optJSONArray("anime")!=null){
            boolean simkl=o.has("anime");for(String k:new String[]{"movies","shows"})for(JSONObject v:DataStore.objects(o.optJSONArray(k)))simkl|=v.has("status")||v.has("movie")||v.has("show");
            String service=simkl?"Simkl":"Trakt";result.service=service;
            for(JSONObject m:DataStore.objects(o.optJSONArray("movies"))){if(simkl&&!completed(m))continue;JSONObject meta=m.has("movie")?m.optJSONObject("movie"):m;result.rows.add(meta(service,meta,true,0,0,"",date(s(m,"watched_at","last_watched_at"),null)));}
            for(String k:new String[]{"shows","anime"})for(JSONObject show:DataStore.objects(o.optJSONArray(k))){boolean any=false;for(JSONObject season:DataStore.objects(show.optJSONArray("seasons")))if(season.optJSONArray("episodes")!=null&&season.optJSONArray("episodes").length()>0)any=true;if(simkl&&!completed(show)&&!any)continue;show(show,result.rows,service);}
        }}
        return result;
    }
    static boolean completed(JSONObject v){String s=s(v,"status").toLowerCase(Locale.ROOT);return s.equals("completed")||s.equals("watched");}
    static Row meta(String service,JSONObject meta,boolean movie,int season,int episode,String episodeTitle,long at){JSONObject ids=meta==null?null:meta.optJSONObject("ids");return new Row(service,s(meta,"title","name"),integer(s(meta,"year")),movie,s(ids,"imdb"),s(ids,"tvdb"),season,episode,episodeTitle,at,0);}
    static void traktItem(JSONObject v,List<Row> rows){long at=date(s(v,"watched_at","last_watched_at"),null);
        if(v.has("movie"))rows.add(meta("Trakt",v.optJSONObject("movie"),true,0,0,"",at));
        else if(v.has("episode")){JSONObject ep=v.optJSONObject("episode");rows.add(meta("Trakt",v.optJSONObject("show"),false,integer(s(ep,"season")),integer(s(ep,"number")),s(ep,"title"),at));}
        else if(v.has("show"))show(v,rows,"Trakt");}
    static void show(JSONObject v,List<Row> rows,String service){JSONObject meta=v.has("show")?v.optJSONObject("show"):v;long fallback=date(s(v,"watched_at","last_watched_at"),null);
        for(JSONObject season:DataStore.objects(v.optJSONArray("seasons")))for(JSONObject ep:DataStore.objects(season.optJSONArray("episodes"))){long at=date(s(ep,"watched_at","last_watched_at"),null);rows.add(meta(service,meta,false,integer(s(season,"number")),integer(s(ep,"number")),s(ep,"title"),at>0?at:fallback));}}

    /** "Lost S2E10", "Inception (2010)", "show: The Office S4". */
    static List<Row> list(String text){
        List<Row> rows=new ArrayList<>();Pattern kind=Pattern.compile("(?i)^(movie|film|show|series|tv)\\s*:\\s*"),ep=Pattern.compile("(?i)[\\s,;-]*(?:up\\s+to\\s+)?(?:S(?:eason)?\\s*(\\d{1,3})(?:\\s*E(?:pisode)?\\s*(\\d{1,4}))?)\\s*$"),yr=Pattern.compile("[\\s,;-]*\\(?((?:19|20)\\d{2})\\)?\\s*$");
        for(String raw:text.split("\n")){String line=raw.trim().replaceAll("^[-*•\\s]+","").trim();if(line.isEmpty())continue;if(rows.size()>=500)break;Boolean movie=null;
            Matcher k=kind.matcher(line);if(k.find()){String t=k.group(1).toLowerCase(Locale.ROOT);movie=t.equals("movie")||t.equals("film");line=line.substring(k.end());}
            int season=0,episode=0;Matcher e=ep.matcher(line);if(e.find()){season=Integer.parseInt(e.group(1));episode=e.group(2)!=null?Integer.parseInt(e.group(2)):0;line=line.substring(0,e.start());if(movie==null)movie=false;}
            int year=0;Matcher y=yr.matcher(line);if(y.find()&&y.start()>0){year=Integer.parseInt(y.group(1));line=line.substring(0,y.start());}
            line=line.trim().replaceAll("[,;\\-]+$","").trim();if(line.isEmpty())continue;
            Row r=new Row("Pasted list",line,year,movie,"","",season,episode,"",0,0);r.approximate=false;rows.add(r);}
        return rows;
    }

    // ---- Export: the PC's formats, from what the phone knows ----
    static String field(String s){return s.indexOf(',')>=0||s.indexOf('"')>=0||s.indexOf('\r')>=0||s.indexOf('\n')>=0?"\""+s.replace("\"","\"\"")+"\"":s;}
    static String iso(long ms){return Instant.ofEpochMilli(ms).toString();}
    static final class View {JSONObject item,viewing;int rating;}
    static List<View> views(List<JSONObject> items){
        List<View> list=new ArrayList<>();
        for(JSONObject i:items)for(JSONObject v:DataStore.objects(i.optJSONArray("viewings"))){View x=new View();x.item=i;x.viewing=v;JSONObject review=i.optJSONObject("review");x.rating=review==null?0:review.optInt("rating");list.add(x);}
        list.sort(Comparator.comparing((View x)->x.item.optString("title")).thenComparingInt(x->x.item.optInt("season")).thenComparingInt(x->x.item.optInt("episode")).thenComparingLong(x->DataStore.date(x.viewing.optString("watchedAt"))));
        return list;
    }
    /** Returns {text, exported, skipped}. Formats: noir-csv, noir-json, letterboxd, trakt. */
    static Object[] export(List<JSONObject> items,JSONObject showRatings,String format)throws JSONException{
        List<View> all=views(items),views=new ArrayList<>();
        for(View v:all){if(format.equals("letterboxd")&&!v.item.optBoolean("movie"))continue;if(format.equals("trakt")&&!v.item.optString("imdb").matches("tt\\d+"))continue;views.add(v);}
        StringBuilder out=new StringBuilder();
        if(format.equals("noir-csv")||format.equals("letterboxd")){
            out.append(format.equals("noir-csv")?"type,title,year,imdb_id,season,episode,episode_title,watched_at,date_unknown,source,rating,approximate\r\n":"Title,Year,imdbID,WatchedDate,Rating,Rewatch\r\n");Set<String> seen=new HashSet<>();
            for(View x:views){JSONObject i=x.item,v=x.viewing;boolean unknown=v.optBoolean("dateUnknown"),movie=i.optBoolean("movie");long at=DataStore.date(v.optString("watchedAt"));String[] f=format.equals("noir-csv")
                ?new String[]{movie?"movie":"episode",i.optString("title"),String.valueOf(i.optInt("year")),i.optString("imdb"),String.valueOf(movie?0:i.optInt("season")),String.valueOf(movie?0:i.optInt("episode")),i.optString("episodeTitle"),unknown?"":iso(at),unknown?"true":"false",v.optString("source"),String.valueOf(x.rating),v.optBoolean("approximate")?"true":"false"}
                :new String[]{i.optString("title"),i.optInt("year")==0?"":String.valueOf(i.optInt("year")),i.optString("imdb"),unknown?"":iso(at).substring(0,10),x.rating==0?"":String.valueOf(x.rating),seen.add(i.optString("id"))?"false":"true"};
                for(int n=0;n<f.length;n++){if(n>0)out.append(',');out.append(field(f[n]));}out.append("\r\n");}
        }else if(format.equals("noir-json")){
            JSONArray a=new JSONArray();for(View x:views){JSONObject i=x.item,v=x.viewing,o=new JSONObject();boolean movie=i.optBoolean("movie");JSONObject review=i.optJSONObject("review");
                o.put("type",movie?"movie":"episode");o.put("title",i.optString("title"));o.put("year",i.optInt("year"));o.put("imdb_id",i.optString("imdb"));o.put("season",movie?0:i.optInt("season"));o.put("episode",movie?0:i.optInt("episode"));o.put("episode_title",i.optString("episodeTitle"));
                o.put("watched_at",v.optBoolean("dateUnknown")?"":iso(DataStore.date(v.optString("watchedAt"))));o.put("date_unknown",v.optBoolean("dateUnknown"));o.put("approximate",v.optBoolean("approximate"));o.put("source",v.optString("source"));o.put("rating",x.rating);o.put("id",v.optString("id"));
                o.put("logged_at",v.optString("loggedAt"));o.put("note",review==null?"":review.optString("note"));o.put("show_rating",movie||showRatings==null?0:showRatings.optInt(i.optString("group")));a.put(o);}
            out.append(a.toString(2));
        }else{
            JSONArray movies=new JSONArray();Map<String,JSONObject> shows=new LinkedHashMap<>();
            for(View x:views){JSONObject i=x.item,v=x.viewing;String at=v.optBoolean("dateUnknown")?"":iso(DataStore.date(v.optString("watchedAt")));
                if(i.optBoolean("movie")){JSONObject m=new JSONObject();m.put("title",i.optString("title"));m.put("year",i.optInt("year"));m.put("ids",new JSONObject().put("imdb",i.optString("imdb")));if(!at.isEmpty())m.put("watched_at",at);movies.put(m);continue;}
                JSONObject show=shows.get(i.optString("imdb"));if(show==null){show=new JSONObject();show.put("title",i.optString("title"));show.put("year",i.optInt("year"));show.put("ids",new JSONObject().put("imdb",i.optString("imdb")));show.put("seasons",new JSONArray());shows.put(i.optString("imdb"),show);}
                JSONObject season=null;for(JSONObject s:DataStore.objects(show.getJSONArray("seasons")))if(s.optInt("number")==i.optInt("season"))season=s;if(season==null){season=new JSONObject().put("number",i.optInt("season")).put("episodes",new JSONArray());show.getJSONArray("seasons").put(season);}
                JSONObject ep=new JSONObject().put("number",i.optInt("episode"));if(!at.isEmpty())ep.put("watched_at",at);season.getJSONArray("episodes").put(ep);}
            out.append(new JSONObject().put("movies",movies).put("shows",new JSONArray(shows.values())).toString(2));
        }
        return new Object[]{out.toString(),views.size(),all.size()-views.size()};
    }

    /** Stremio's per-series watched bitfield: "anchorVideoId:length:base64(zlib(bits))". */
    static List<String> decodeWatched(String watched,List<String> videoIds)throws IOException{
        int last=watched.lastIndexOf(':'),previous=last>0?watched.lastIndexOf(':',last-1):-1;int length;
        try{length=Integer.parseInt(watched.substring(previous+1,last));}catch(Exception e){throw new IOException("Invalid Stremio watched bitfield.");}
        if(previous<0||length<=0)throw new IOException("Invalid Stremio watched bitfield.");
        int anchor=videoIds.indexOf(watched.substring(0,previous));if(anchor<0)throw new IOException("The watched anchor is absent from the video list.");
        byte[] bytes;try{Inflater inflater=new Inflater();inflater.setInput(android.util.Base64.decode(watched.substring(last+1),android.util.Base64.DEFAULT));ByteArrayOutputStream out=new ByteArrayOutputStream();byte[] buf=new byte[4096];while(!inflater.finished()){int n=inflater.inflate(buf);if(n==0&&(inflater.needsInput()||inflater.needsDictionary()))break;out.write(buf,0,n);}inflater.end();bytes=out.toByteArray();}catch(DataFormatException e){throw new IOException("Invalid Stremio watched bitfield.");}
        long offset=(long)length-anchor-1;List<String> result=new ArrayList<>();
        for(int i=0;i<videoIds.size();i++){long bit=i+offset;if(bit>=0&&bit/8<bytes.length&&((bytes[(int)(bit/8)]>>(int)(bit%8))&1)==1)result.add(videoIds.get(i));}
        return result;
    }
}
