package app.noir.mobile;

import android.net.Uri;
import android.os.ParcelFileDescriptor;
import android.system.Os;
import android.system.OsConstants;
import org.json.*;
import javax.net.ssl.HttpsURLConnection;
import java.io.*;
import java.net.*;
import java.nio.*;
import java.nio.charset.*;
import java.security.MessageDigest;
import java.util.*;
import java.util.zip.GZIPInputStream;

/** Subtitle files are private phone files. Internet requests never carry the PC token. */
final class SubtitleClient {
    static final int MAX=4*1024*1024;
    interface Transport{byte[] read(String url,int maximum)throws Exception;}
    final DataStore data;final JSONObject item;final String id;final Transport internet;
    String videoHash="",release="",streamRelease=null;boolean identityRead=false;
    void streaming(String name){streamRelease=name==null?"":name;if(!streamRelease.isEmpty())release=streamRelease;videoHash="";identityRead=true;}
    final SubtitleSources sources;
    /** "Replace saved subtitles": when on, saved phone subtitles for the language are moved to subtitle-originals before the new one is kept. */
    volatile boolean replace=false;
    static {String[][] words={{"%s saved from %s","تم حفظ %s من %s"},{"%s had no %s · saved from %s","لم يجد %s ترجمة %s · تم الحفظ من %s"},{"the free default","المصدر المجاني الافتراضي"},{"the release","الإصدار"},{"by %s","بواسطة %s"},{"Replace saved subtitles","استبدال الترجمات المحفوظة"},{"exact video match","تطابق تام مع الفيديو"},{"timing unchanged","التوقيت دون تغيير"},{"timing checked","تم فحص التوقيت"},{"timing unavailable","فحص التوقيت غير متاح"}};for(String[] w:words)Loc.AR.put(w[0],w[1]);}
    /** Matching uploaders first, in their original order; everyone else follows. A preference, never a filter. */
    static List<JSONObject> preferUploader(List<JSONObject> list,String preferred){
        String want=preferred==null?"":preferred.trim();if(want.isEmpty())return list;List<JSONObject> first=new ArrayList<>(),rest=new ArrayList<>();
        for(JSONObject c:list)if(c.optString("uploader").trim().equalsIgnoreCase(want))first.add(c);else rest.add(c);first.addAll(rest);return first;
    }
    static void markFallback(List<JSONObject> list,String chosenName){for(JSONObject c:list)DataStore.put(c,"fellBackFrom",chosenName);}
    /** "English saved from SubDL", "the free default" for the fallback, "SubDL had no Arabic · saved from the free default" when the chosen site had nothing. */
    static String originLine(JSONObject sub){
        String language=Loc.t(label(sub.optString("language"))),from=sub.optString("fellBackFrom"),name=sub.optString("sourceName"),origin=sub.optString("origin");
        if(!from.isEmpty())return Loc.f("%s had no %s · saved from %s",from,language,Loc.t("the free default"));
        return Loc.f("%s saved from %s",language,origin.equals("release")?Loc.t("the release"):origin.equals("site")&&!name.isEmpty()?name:Loc.t("the free default"));
    }
    /** The full result line: where it came from, the uploader when the site gave one, then the timing/match note. */
    static String resultLine(JSONObject sub,String tail){
        StringBuilder line=new StringBuilder(originLine(sub));String by=sub.optString("uploader");
        if(!by.isEmpty())line.append(" · ").append(Loc.f("by %s",by));if(tail!=null&&!tail.isEmpty())line.append(" · ").append(Loc.t(tail));return line.toString();
    }
    /** Moves this playback's saved phone subtitles for the language into subtitle-originals and drops them from the list. Returns how many moved. */
    int replaceSaved(String lang)throws IOException{return moveOut(sub->matchesPlayback(sub)&&language(sub.optString("language")).equals(lang));}
    int moveOut(java.util.function.Predicate<JSONObject> which)throws IOException{
        JSONArray keep=new JSONArray();int moved=0;File backup=new File(data.context.getFilesDir(),"subtitle-originals");
        for(JSONObject sub:DataStore.objects(data.phoneSubtitles(id))){
            if(!which.test(sub)){keep.put(sub);continue;}
            File file=data.subtitle(sub);
            if(file!=null&&file.isFile()){if(!backup.isDirectory()&&!backup.mkdirs())throw new IOException("Couldn't back up the saved subtitle.");try{java.nio.file.Files.move(file.toPath(),new File(backup,id.replaceAll("[^A-Za-z0-9._-]","_")+"-"+System.currentTimeMillis()+"-"+file.getName()).toPath(),java.nio.file.StandardCopyOption.REPLACE_EXISTING);}catch(IOException e){throw new IOException("Couldn't back up the saved subtitle.");}}
            moved++;
        }
        if(moved>0){JSONObject all=data.stateObject("phoneSubtitles");DataStore.put(all,id,keep);data.stateObject("phoneSubtitles",all);}
        return moved;
    }
    /** The timing check refused. When the PC reports its raw offset and scale they are kept here. */
    static final class Refused extends IOException {
        final boolean known;final double offset,scale;
        Refused(String message,boolean known,double offset,double scale){super(message);this.known=known;this.offset=offset;this.scale=scale;}
        boolean far(){return known&&farOff(offset,scale);}
    }
    static boolean same(JSONObject a,JSONObject b){String ua=a.optString("url"),ra=a.optString("ref");return !ua.isEmpty()&&ua.equals(b.optString("url"))||!ra.isEmpty()&&ra.equals(b.optString("ref"))&&a.optString("provider").equals(b.optString("provider"));}
    static boolean farOff(double offset,double scale){return Math.abs(offset)>10||Math.abs(scale-1)>0.003;}
    static final String MISMATCH="may not match this video · try Replace";
    static {Loc.AR.put(MISMATCH,"قد لا تطابق هذا الفيديو · جرّب الاستبدال");}
    static final class Checked {final JSONObject sub;final String tail;Checked(JSONObject sub,String tail){this.sub=sub;this.tail=tail;}}
    interface Steps {JSONObject download(JSONObject choice)throws Exception;JSONObject sync(JSONObject sub)throws Exception;void discard(JSONObject sub)throws Exception;boolean checkable();}
    /** Local files with a paired PC only: if the check refuses far off, one more candidate for the language is tried (two downloads at most). Never for streams. */
    static Checked decide(Steps steps,JSONObject choice,List<JSONObject> candidates)throws Exception{
        JSONObject first=steps.download(choice);
        if(first.optBoolean("hashMatch"))return new Checked(first,"exact video match");
        if(!steps.checkable())return new Checked(first,"timing unchanged");
        try{return new Checked(steps.sync(first),"timing checked");}
        catch(Refused refused){
            if(!refused.far())return new Checked(first,refused.getMessage()==null?"timing unavailable":refused.getMessage());
            JSONObject next=null;
            if(candidates!=null)for(JSONObject c:candidates)if(c!=choice&&c.optString("language").equals(choice.optString("language"))&&!same(c,choice)){next=c;break;}
            if(next==null)return new Checked(first,MISMATCH);
            JSONObject second;try{second=steps.download(next);}catch(Exception e){return new Checked(first,MISMATCH);}
            try{JSONObject fixed=steps.sync(second);steps.discard(first);return new Checked(fixed,"timing checked");}
            catch(Refused again){if(!again.far()){steps.discard(first);return new Checked(second,again.getMessage()==null?"timing unavailable":again.getMessage());}steps.discard(second);return new Checked(first,MISMATCH);}
            catch(Exception e){steps.discard(second);return new Checked(first,MISMATCH);}
        }
        catch(Exception e){return new Checked(first,e.getMessage()==null?"timing unavailable":e.getMessage());}
    }
    Checked downloadAndCheck(JSONObject choice,List<JSONObject> candidates,java.util.function.Consumer<JSONObject> saved)throws Exception{
        final boolean wanted=replace;int[] downloads={0};
        return decide(new Steps(){
            public JSONObject download(JSONObject c)throws Exception{replace=wanted&&downloads[0]==0;try{JSONObject sub=SubtitleClient.this.download(c);downloads[0]++;if(saved!=null&&downloads[0]==1)saved.accept(sub);return sub;}finally{replace=wanted;}}
            public JSONObject sync(JSONObject sub)throws Exception{return SubtitleClient.this.sync(sub);}
            public void discard(JSONObject sub)throws Exception{String gone=sub.optString("id");moveOut(x->x.optString("id").equals(gone));}
            public boolean checkable(){return data.paired()&&streamRelease==null;}
        },choice,candidates);
    }
    SubtitleClient(DataStore data,JSONObject item){this(data,item,SubtitleClient::fetch,new SubtitleSources(new SubtitleTransport()));}
    /** A caller-supplied transport stands in for the network entirely, so its hosts need no DNS and every request is a plain GET. */
    SubtitleClient(DataStore data,JSONObject item,Transport internet){this(data,item,internet,new SubtitleSources(new SubtitleTransport((request,cap,deadline)->new SubtitleTransport.Response(200,null,legacy(internet,request.url,cap)),host->new java.net.InetAddress[]{PUBLIC})));}
    static final java.net.InetAddress PUBLIC=publicAddress();
    static java.net.InetAddress publicAddress(){try{return java.net.InetAddress.getByAddress(new byte[]{(byte)93,(byte)184,(byte)216,34});}catch(java.net.UnknownHostException e){throw new IllegalStateException(e);}}
    static byte[] legacy(Transport internet,String url,int cap)throws IOException{try{return internet.read(url,cap);}catch(IOException e){throw e;}catch(Exception e){throw new IOException(e);}}
    SubtitleClient(DataStore data,JSONObject item,Transport internet,SubtitleSources sources){this.data=data;this.item=item;this.internet=internet;this.sources=sources;id=item.optString("id");release=item.optString("title")+(item.optBoolean("movie")?"":" S"+String.format(Locale.ROOT,"%02dE%02d",item.optInt("season"),item.optInt("episode")));}
    static String language(String value){String s=value.trim().toLowerCase(Locale.ROOT);return Arrays.asList("en","eng","english").contains(s)?"en":Arrays.asList("ar","ara","arabic").contains(s)?"ar":"";}
    static String label(String language){return language.equals("ar")?"Arabic":"English";}
    static String hashBytes(long size,byte[] first,byte[] last){if(size<131072||first.length!=65536||last.length!=65536)return "";long hash=size;for(byte[] chunk:new byte[][]{first,last}){ByteBuffer bytes=ByteBuffer.wrap(chunk).order(ByteOrder.LITTLE_ENDIAN);while(bytes.hasRemaining())hash+=bytes.getLong();}return String.format(Locale.ROOT,"%016x",hash);}
    void identity()throws Exception{
        if(identityRead)return;identityRead=true;try{String local=data.localFile(id);
        if(!local.isEmpty()){
            Uri uri=Uri.parse(local);
            try(ParcelFileDescriptor fd=local.startsWith("file:")?ParcelFileDescriptor.open(new File(uri.getPath()),ParcelFileDescriptor.MODE_READ_ONLY):data.context.getContentResolver().openFileDescriptor(uri,"r")){
                if(fd==null)throw new IOException("Phone video is unavailable.");long size=fd.getStatSize();if(size<131072)return;
                byte[] first=new byte[65536],last=new byte[65536];readAt(fd,0,first);readAt(fd,size-65536,last);videoHash=hashBytes(size,first,last);
            }
            // Only reuse a PC release name when the saved phone copy is the exact same file.
            try{JSONObject remote=data.pcRequest("/v1/subtitle-tools/"+id,null);if(videoHash.equals(remote.optString("hash")))release=remote.optString("release",release);}catch(Exception ignored){}
        }else{
            try{JSONObject remote=data.pcRequest("/v1/subtitle-tools/"+id,null);videoHash=remote.optString("hash");release=remote.optString("release",release);return;}catch(Exception ignored){}
            // Older PC versions still support file-matched subtitles through their byte-range endpoint.
            HttpsURLConnection head=data.media(id,null);long size;try{head.setRequestMethod("HEAD");if(head.getResponseCode()!=200)throw new IOException("PC video is unavailable.");size=head.getContentLengthLong();}finally{head.disconnect();}
            if(size>=131072)videoHash=hashBytes(size,range(0,65535),range(size-65536,size-1));
        }}catch(Exception error){identityRead=false;throw error;}
    }
    static void readAt(ParcelFileDescriptor fd,long offset,byte[] bytes)throws Exception{Os.lseek(fd.getFileDescriptor(),offset,OsConstants.SEEK_SET);int count=0;while(count<bytes.length){int n=Os.read(fd.getFileDescriptor(),bytes,count,bytes.length-count);if(n<=0)throw new IOException("Video fingerprint is incomplete.");count+=n;}}
    byte[] range(long start,long end)throws Exception{HttpsURLConnection c=data.media(id,"bytes="+start+"-"+end);try{if(c.getResponseCode()!=206||!c.getHeaderField("Content-Range").startsWith("bytes "+start+"-"+end+"/"))throw new IOException("PC video fingerprint changed. Try again.");try(InputStream in=c.getInputStream()){return DataStore.read(in,65536);}}finally{c.disconnect();}}
    List<JSONObject> search(String lang)throws Exception{
        String imdb=item.optString("imdb");if(!imdb.matches("tt\\d+"))throw new IOException("Correct this title's match in Noir before searching for subtitles.");
        try{identity();}catch(Exception ignored){}
        JSONObject settings=new PhoneDownloadSettings(data.context).load();String uploader=settings.optString("subtitleUploader");
        SubtitleSources.Selection chosen=SubtitleSources.Selection.from(settings).validate();
        if(!chosen.chosen())return defaultSearch(lang,imdb);
        // A chosen source is asked first. With "only" on, no other site is contacted, whatever happens.
        List<JSONObject> own=new ArrayList<>();SubtitleSources.SubtitleFailure failure=null;
        try{own=sources.search(chosen,item,release,lang,videoHash);}catch(SubtitleSources.SubtitleFailure e){failure=e;}
        if(!own.isEmpty())return preferUploader(own,uploader);
        if(!chosen.only)try{List<JSONObject> fallback=defaultSearch(lang,imdb);if(!fallback.isEmpty()){markFallback(fallback,chosen.name());return fallback;}}catch(IOException e){if(failure==null)throw e;}
        if(failure!=null)throw failure;
        throw new IOException(chosen.name()+" · "+Loc.t("no "+label(lang)+" subtitles found"));
    }
    List<JSONObject> defaultSearch(String lang,String imdb)throws Exception{
        List<String> urls=searchUrls("",true,imdb,item.optBoolean("movie"),item.optInt("season"),item.optInt("episode"),release,videoHash);
        List<JSONObject> found=new ArrayList<>();Set<String> seen=new HashSet<>();boolean succeeded=false;
        for(String url:urls)try{
            JSONObject response=new JSONObject(new String(internet.read(url,MAX),StandardCharsets.UTF_8));succeeded=true;JSONArray all=response.optJSONArray("subtitles");if(all==null)continue;
            for(int n=0;n<Math.min(500,all.length());n++){JSONObject candidate=all.optJSONObject(n);if(candidate==null||!language(candidate.optString("lang")).equals(lang))continue;
                if(!item.optBoolean("movie")&&(candidate.opt("season") instanceof Number&&candidate.optInt("season")!=item.optInt("season")||candidate.opt("episode") instanceof Number&&candidate.optInt("episode")!=item.optInt("episode")))continue;
                String link=candidate.optString("url");if(!validUrl(link)||!seen.add(link))continue;DataStore.put(candidate,"language",lang);DataStore.put(candidate,"score",(candidate.optString("m").equals("h")?1000:0)+releaseScore(candidate.optString("movieReleaseName")+" "+candidate.optString("subtitleFileName"),release));found.add(candidate);
            }
        }catch(Exception e){if(Thread.currentThread().isInterrupted())throw new IOException("Subtitle lookup cancelled.");if(urls.size()==1)throw new IOException("Subtitle lookup unavailable.");}
        if(!succeeded)throw new IOException("Subtitle lookup unavailable.");found.sort(Comparator.comparingInt((JSONObject c)->c.optInt("score")).reversed());return new ArrayList<>(found.subList(0,Math.min(500,found.size())));
    }
    static final String DEFAULT_SOURCE="https://opensubtitles-v3.strem.io";
    static String normalizeAddon(String value)throws IOException{
        String text=value.trim();if(text.isEmpty())return "";if(text.regionMatches(true,0,"stremio://",0,10))text="https://"+text.substring(10);
        try{URI u=new URI(text);String host=u.getHost();if(!"https".equalsIgnoreCase(u.getScheme())||host==null||u.getRawUserInfo()!=null||(u.getPort()!=-1&&u.getPort()!=443)||u.getRawQuery()!=null||u.getRawFragment()!=null||host.equalsIgnoreCase("localhost")||host.equals("127.0.0.1")||host.equals("[::1]"))throw new IOException();
            String path=u.getRawPath();while(path.endsWith("/"))path=path.substring(0,path.length()-1);if(path.toLowerCase(Locale.ROOT).endsWith("/manifest.json"))path=path.substring(0,path.length()-14);
            return "https://"+host.toLowerCase(Locale.ROOT)+path;
        }catch(Exception e){throw new IOException("Paste an HTTPS Stremio subtitle addon link without credentials or a custom port.");}
    }
    static List<String> searchUrls(String link,boolean only,String imdb,boolean movie,int season,int episode,String release,String hash)throws IOException{
        String base=normalizeAddon(link);List<String> sources=new ArrayList<>();if(!base.isEmpty())sources.add(base);if(base.isEmpty()||!only&&!base.equals(DEFAULT_SOURCE))sources.add(DEFAULT_SOURCE);
        String title=movie?imdb:imdb+":"+season+":"+episode,extra="filename="+encode(release);if(!hash.isEmpty())extra+="&videoHash="+encode(hash);
        List<String> urls=new ArrayList<>();for(String source:sources)urls.add(source+"/subtitles/"+(movie?"movie/":"series/")+encode(title)+"/"+extra+".json");return urls;
    }
    static String encode(String value){try{return URLEncoder.encode(value,"UTF-8").replace("+","%20").replace("%7E","~").replace("*","%2A");}catch(UnsupportedEncodingException e){throw new IllegalStateException(e);}}
    static String manifestName(byte[] bytes)throws Exception{
        JSONObject manifest=new JSONObject(new String(bytes,StandardCharsets.UTF_8));JSONArray resources=manifest.optJSONArray("resources");boolean subtitles=false;
        if(resources!=null)for(int i=0;i<resources.length();i++){Object r=resources.opt(i);if("subtitles".equals(r)||r instanceof JSONObject&&"subtitles".equals(((JSONObject)r).optString("name")))subtitles=true;}
        if(!subtitles)throw new IOException("This addon does not provide subtitles.");String name=manifest.optString("name","Subtitle addon").replace('\r',' ').replace('\n',' ');return name.isEmpty()?"Subtitle addon":name.substring(0,Math.min(120,name.length()));
    }
    static String checkAddon(String link)throws Exception{
        String base=normalizeAddon(link);if(base.isEmpty())throw new IOException("Paste a subtitle addon link first.");
        HttpsURLConnection c=(HttpsURLConnection)new URL(base+"/manifest.json").openConnection();c.setConnectTimeout(7000);c.setReadTimeout(8000);c.setInstanceFollowRedirects(false);
        try{if(c.getResponseCode()!=200)throw new IOException();try(InputStream input=c.getInputStream()){return manifestName(DataStore.read(input,256*1024));}}
        catch(Exception e){if(e instanceof IOException&&"This addon does not provide subtitles.".equals(e.getMessage()))throw e;throw new IOException("Couldn't check the subtitle addon. Check the link and connection.");}finally{c.disconnect();}
    }
    static int releaseScore(String subtitle,String release){Set<String> a=new HashSet<>(Arrays.asList(subtitle.toLowerCase(Locale.ROOT).split("[^a-z0-9]+"))),b=new HashSet<>(Arrays.asList(release.toLowerCase(Locale.ROOT).split("[^a-z0-9]+")));b.removeAll(Arrays.asList("","mkv","mp4","srt","eng","ara","en","ar"));a.retainAll(b);return b.isEmpty()?0:100*a.size()/b.size();}
    /** The saved identity of a subtitle is a hash: signed links and provider references never reach library.json. */
    static String keyOf(String seed){try{return "sha256:"+DataStore.hex(MessageDigest.getInstance("SHA-256").digest(seed.getBytes(StandardCharsets.UTF_8))).toLowerCase(Locale.ROOT);}catch(java.security.NoSuchAlgorithmException e){throw new IllegalStateException(e);}}
    byte[] fetchDefault(String url)throws Exception{
        byte[] bytes=internet.read(url,MAX);
        if(bytes.length>2&&(bytes[0]&255)==31&&(bytes[1]&255)==139)try(InputStream gzip=new GZIPInputStream(new ByteArrayInputStream(bytes))){bytes=DataStore.read(gzip,MAX);}
        return bytes;
    }
    JSONObject download(JSONObject candidate)throws Exception{
        String lang=candidate.optString("language"),provider=candidate.optString("provider"),extension=extension(candidate.optString("subtitleFileName")),seed=candidate.optString("url"),sourceName="",fellBackFrom=candidate.optString("fellBackFrom");byte[] bytes;
        if(provider.isEmpty())bytes=fetchDefault(candidate.getString("url"));
        else{
            SubtitleSources.Selection chosen=SubtitleSources.Selection.from(new PhoneDownloadSettings(data.context).load()).validate();
            if(!chosen.source.equals(provider))throw new IOException(Loc.t("Subtitle source changed. Search again."));
            seed=provider+":"+(seed.isEmpty()?lang+":"+candidate.optString("ref"):seed);sourceName=chosen.name();
            try{SubtitleArchive.Picked picked=sources.download(chosen,candidate,item,release);bytes=picked.bytes;extension=extension(picked.name);}
            catch(SubtitleSources.SubtitleFailure failure){
                if(chosen.only)throw failure;
                List<JSONObject> other=defaultSearch(lang,item.optString("imdb"));if(other.isEmpty())throw failure;
                candidate=other.get(0);sourceName="";fellBackFrom=chosen.name();seed=candidate.getString("url");extension=extension(candidate.optString("subtitleFileName"));bytes=fetchDefault(seed);
            }
        }
        String content=decode(bytes);if(!validText(content,extension))throw new IOException("This provider returned an invalid subtitle. Try another release.");
        String rel=candidate.optString("movieReleaseName");if(rel.isEmpty())rel=candidate.optString("subtitleFileName");if(rel.isEmpty())rel=release;
        if(replace)replaceSaved(lang);
        JSONObject sub=new JSONObject();DataStore.put(sub,"origin",sourceName.isEmpty()?"free":"site");if(!sourceName.isEmpty())DataStore.put(sub,"sourceName",sourceName);if(!fellBackFrom.isEmpty()&&sourceName.isEmpty())DataStore.put(sub,"fellBackFrom",fellBackFrom);if(!candidate.optString("uploader").isEmpty())DataStore.put(sub,"uploader",candidate.optString("uploader"));DataStore.put(sub,"sourceKey",keyOf(seed));DataStore.put(sub,"language",lang);DataStore.put(sub,"name",label(lang)+" · "+rel);DataStore.put(sub,"extension",extension);DataStore.put(sub,"hashMatch",candidate.optString("m").equals("h"));if(streamRelease!=null)DataStore.put(sub,"streamRelease",streamRelease);return save(sub,content);
    }
    JSONObject save(JSONObject sub,String content)throws Exception{
        String originalId=sub.optString("id");DataStore.put(sub,"id",DataStore.hex(MessageDigest.getInstance("SHA-256").digest((id+":"+sub.optString("sourceKey")+":"+sub.optString("streamRelease")+":"+content).getBytes(StandardCharsets.UTF_8))).substring(0,24));File target=data.subtitle(sub);if(target==null)throw new IOException("Unsupported subtitle file.");
        DiscoveryClient.write(target,content.getBytes(StandardCharsets.UTF_8));if(!originalId.isEmpty()&&!originalId.equals(sub.optString("id")))DataStore.put(sub,"originalId",sub.optString("originalId",originalId));data.phoneSubtitle(id,sub);return sub;
    }
    JSONObject sync(JSONObject sub)throws Exception{
        if(streamRelease!=null)throw new IOException("Use Adjust timing while streaming. Automatic timing needs a verified local video.");
        if(!data.paired())throw new IOException("Pair with your PC for automatic timing. Manual timing also works offline.");identity();if(videoHash.isEmpty())throw new IOException("Couldn't verify this video's identity. Use manual timing.");File file=data.subtitle(sub);if(file==null||!file.exists())throw new IOException("Save a subtitle first.");String text=decode(read(file));
        JSONObject request=new JSONObject();DataStore.put(request,"hash",videoHash);DataStore.put(request,"text",text);DataStore.put(request,"extension",sub.optString("extension"));JSONObject response=data.pcRequest("/v1/subtitle-tools/"+id+"/sync",request);
        if(!response.optBoolean("accepted"))throw new Refused(response.optString("message","Timing was uncertain. The original subtitle is kept."),response.has("offset")&&response.has("scale"),response.optDouble("offset"),response.optDouble("scale"));String corrected=response.getString("text");if(!validText(corrected,sub.optString("extension")))throw new IOException("Timing result was invalid. The original is kept.");JSONObject next=DataStore.copy(sub);if(next.optString("sourceKey").isEmpty())DataStore.put(next,"sourceKey","pc:"+sub.optString("id"));DataStore.put(next,"aligned",true);DataStore.put(next,"originalId",sub.optString("originalId",sub.optString("id")));return save(next,corrected);
    }
    boolean matchesPlayback(JSONObject sub){return streamRelease==null?!sub.has("streamRelease"):sub.has("streamRelease")&&streamRelease.equals(sub.optString("streamRelease"));}
    List<JSONObject> files(){List<JSONObject> all=new ArrayList<>();JSONObject latest=data.item(id);for(JSONObject sub:DataStore.objects(latest==null?null:latest.optJSONArray("subtitleFiles")))if(matchesPlayback(sub)&&data.subtitle(sub)!=null&&data.subtitle(sub).exists())all.add(sub);for(JSONObject sub:DataStore.objects(data.phoneSubtitles(id)))if(matchesPlayback(sub)&&data.subtitle(sub)!=null&&data.subtitle(sub).exists())all.add(sub);return all;}
    void refreshFiles(){if(streamRelease!=null)return;try{JSONObject response=data.pcRequest("/v1/subtitle-tools/"+id,null);data.subtitleFiles(id,response.getJSONArray("files"));}catch(Exception ignored){}JSONObject latest=data.item(id);try{if(latest!=null)data.cacheSubtitles(latest);}catch(Exception ignored){}}
    static String extension(String name){int dot=name.lastIndexOf('.');String ext=dot<0?".srt":name.substring(dot).toLowerCase(Locale.ROOT);return Arrays.asList(".srt",".ass",".ssa",".vtt").contains(ext)?ext:".srt";}
    static boolean validText(String text,String ext){String value=text.trim();return value.length()>10&&value.length()<MAX&&!value.toLowerCase(Locale.ROOT).startsWith("<!doctype")&&!value.toLowerCase(Locale.ROOT).startsWith("<html")&&(ext.equals(".ass")||ext.equals(".ssa")?value.contains("[Events]"):java.util.regex.Pattern.compile("\\d{1,2}:\\d{2}(?::\\d{2})?[,.]\\d{3}\\s+-->\\s+\\d{1,2}:\\d{2}").matcher(value).find());}
    static String decode(byte[] bytes)throws Exception{if(bytes.length>=2&&(bytes[0]&255)==255&&(bytes[1]&255)==254)return new String(bytes,2,bytes.length-2,StandardCharsets.UTF_16LE);if(bytes.length>=2&&(bytes[0]&255)==254&&(bytes[1]&255)==255)return new String(bytes,2,bytes.length-2,StandardCharsets.UTF_16BE);try{return StandardCharsets.UTF_8.newDecoder().onMalformedInput(CodingErrorAction.REPORT).decode(ByteBuffer.wrap(bytes)).toString().replaceFirst("^\\uFEFF","");}catch(CharacterCodingException e){return new String(bytes,Charset.forName("windows-1256"));}}
    static byte[] read(File file)throws IOException{try(InputStream input=new FileInputStream(file)){return DataStore.read(input,MAX);}}
    static boolean validUrl(String link){try{URI url=new URI(link);String host=url.getHost();return url.getScheme().equals("https")&&url.getUserInfo()==null&&host!=null&&!host.equals("localhost")&&!host.matches("[0-9.]+")&&!host.contains(":");}catch(Exception e){return false;}}
    static byte[] fetch(String url,int maximum)throws Exception{if(!validUrl(url))throw new IOException("Invalid subtitle link.");HttpsURLConnection c=(HttpsURLConnection)new URL(url).openConnection();c.setConnectTimeout(7000);c.setReadTimeout(25000);c.setInstanceFollowRedirects(false);c.setRequestProperty("User-Agent","Noir-Android/0.4.1");try{int status=c.getResponseCode();if(status>=300&&status<400){String next=c.getHeaderField("Location");if(next!=null)next=new URL(new URL(url),next).toString();return fetchRedirect(next,maximum);}if(status!=200)throw new IOException(status==429?"Subtitle provider is busy. Try again shortly.":"Subtitle provider is unavailable ("+status+").");try(InputStream input=c.getInputStream()){return DataStore.read(input,maximum);}}finally{c.disconnect();}}
    static byte[] fetchRedirect(String url,int maximum)throws Exception{if(!validUrl(url))throw new IOException("Invalid subtitle redirect.");HttpsURLConnection c=(HttpsURLConnection)new URL(url).openConnection();c.setConnectTimeout(7000);c.setReadTimeout(25000);c.setInstanceFollowRedirects(false);try{if(c.getResponseCode()!=200)throw new IOException("Subtitle download is unavailable.");try(InputStream input=c.getInputStream()){return DataStore.read(input,maximum);}}finally{c.disconnect();}}
}
