package app.noir.mobile;

import android.net.Uri;
import android.os.Looper;
import android.os.ParcelFileDescriptor;
import org.json.*;
import org.videolan.libvlc.LibVLC;
import org.videolan.libvlc.Media;
import org.videolan.libvlc.interfaces.IMedia;
import java.io.File;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicInteger;
import java.util.regex.*;

final class EmbeddedSubtitles {
    static final Pattern PARTIAL=Pattern.compile("(?:^|[^a-z])(forced|signs?|songs?|karaoke)(?:[^a-z]|$)",Pattern.CASE_INSENSITIVE);
    static final Pattern LANGUAGE=Pattern.compile("(?:^|[^a-z])(eng|en|english|ara|ar|arabic)(?:[^a-z]|$)",Pattern.CASE_INSENSITIVE);
    static String titleLanguage(String title){Matcher match=LANGUAGE.matcher(title==null?"":title);return match.find()?SubtitleClient.language(match.group(1)):"";}
    static String language(String language,String description,boolean forced){
        String title=description==null?"":description;if(forced||PARTIAL.matcher(title).find())return "";
        String tag=language==null?"":language.trim();return tag.isEmpty()||tag.equalsIgnoreCase("und")?titleLanguage(title):SubtitleClient.language(tag);
    }
    static JSONArray neededFiles(JSONArray files,Set<String> embedded){
        JSONArray result=new JSONArray();for(JSONObject sub:DataStore.objects(files)){String lang=SubtitleClient.language(sub.optString("language"));if(lang.isEmpty())lang=titleLanguage(sub.optString("name"));if(!embedded.contains(lang))result.put(sub);}return result;
    }
    static Set<String> probe(DataStore data,String id){
        Set<String> result=new HashSet<>();if(Looper.myLooper()==Looper.getMainLooper())return result;
        String local=data.localFile(id);if(local.isEmpty())return result;Uri uri=Uri.parse(local);
        try(ParcelFileDescriptor file=local.startsWith("file:")?ParcelFileDescriptor.open(new File(uri.getPath()),ParcelFileDescriptor.MODE_READ_ONLY):data.context.getContentResolver().openFileDescriptor(uri,"r")){
            if(file==null)return result;LibVLC vlc=new LibVLC(data.context,new ArrayList<>(Arrays.asList("--quiet","--no-sub-autodetect-file")));
            try{
                boolean descriptor=!local.startsWith("file:");Media media=descriptor?new Media(vlc,file.getFileDescriptor()):new Media(vlc,uri);
                try{
                    CountDownLatch parsed=new CountDownLatch(1);AtomicInteger status=new AtomicInteger(IMedia.ParsedStatus.Failed);
                    media.setEventListener(event->{if(event.type==IMedia.Event.ParsedChanged){status.set(event.getParsedStatus());parsed.countDown();}});
                    media.addOption(":sub-autodetect-file=0");
                    // VLC treats fd:// inputs as nonlocal when deciding whether to preparse.
                    if(!media.parseAsync(descriptor?IMedia.Parse.ParseNetwork:IMedia.Parse.ParseLocal,30000)||!parsed.await(30,TimeUnit.SECONDS)||status.get()!=IMedia.ParsedStatus.Done)return result;
                    for(int n=0;n<media.getTrackCount();n++){
                        IMedia.Track track=media.getTrack(n);if(track==null||track.type!=IMedia.Track.Type.Text)continue;
                        // LibVLC exposes track language/description, but no separate forced disposition.
                        String lang=language(track.language,track.description,false);if(!lang.isEmpty())result.add(lang);
                    }
                }finally{media.setEventListener(null);media.release();}
            }finally{vlc.release();}
        }catch(InterruptedException interrupted){Thread.currentThread().interrupt();return new HashSet<>();}
        catch(Exception|LinkageError ignored){return new HashSet<>();}
        return result;
    }
}
