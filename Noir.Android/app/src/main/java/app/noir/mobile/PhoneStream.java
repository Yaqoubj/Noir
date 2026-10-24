package app.noir.mobile;
import android.app.*;
import android.content.Intent;
import org.json.*;
import java.util.*;
import java.util.regex.Pattern;

// Streaming on the phone through the phone's own AIOStreams link (Download settings): the same
// stream list Stremio shows, played in Noir's player so resume, history and Up next keep working.
final class PhoneStream {
 static final Map<Activity,Integer> requests=new WeakHashMap<>();
 // Shared with Home's services row: decrypted at most once a minute.
 static JSONObject settings(Activity a){if(PhoneHarbor.phoneCache==null||PhoneHarbor.phoneAt<System.currentTimeMillis()-60000){try{PhoneHarbor.phoneCache=new PhoneDownloadSettings(a).load();}catch(Exception e){PhoneHarbor.phoneCache=new JSONObject();}PhoneHarbor.phoneAt=System.currentTimeMillis();}return PhoneHarbor.phoneCache;}
 static boolean available(Activity a){return !settings(a).optString("manifest").isEmpty();}
 // Ready first, then a size that streams comfortably on mobile data or Wi-Fi, then the preferred quality.
 static List<JSONObject> rank(List<JSONObject> all,String preferred,boolean movie){
  List<JSONObject> list=new ArrayList<>();for(JSONObject c:all)if(c.optString("service").equals("direct")&&usableHeaders(c.optJSONObject("headers"))&&!Arrays.asList("p2p","live","error").contains(c.optString("streamType")))list.add(c);
  long comfortable=(movie?25L:6L)*1024*1024*1024;Pattern remux=Pattern.compile("(?i)\\bremux\\b");
  list.sort(Comparator.comparing((JSONObject c)->!c.optBoolean("cached")).thenComparing(c->c.optLong("size")>comfortable||remux.matcher(c.optString("name")).find()).thenComparingInt(c->quality(c.optString("resolution"),preferred)));
  return list;
 }
 static int quality(String r,String preferred){if(preferred.isEmpty()||preferred.equals("Any")||r.equals(preferred))return 0;if(r.isEmpty())return 3;return Math.abs(order(r)-order(preferred));}
 static int order(String r){switch(r){case "2160p":return 4;case "720p":return 2;case "480p":return 1;default:return 3;}}
 // VLC can send only a user agent and referrer.
 static boolean usableHeaders(JSONObject h){if(h==null)return true;for(Iterator<String> it=h.keys();it.hasNext();){String k=it.next().toLowerCase(Locale.ROOT);if(!k.equals("user-agent")&&!k.equals("referer"))return false;}return true;}
 static void start(Activity a,JSONObject item,boolean choose){
  JSONObject s=settings(a);if(s.optString("manifest").isEmpty()){Toast(a,"Add your AIOStreams link in phone Download settings to stream.");a.startActivity(new Intent(a,PhoneDownloadSettingsActivity.class));return;}
  if(!item.optString("imdb").matches("tt[0-9]+")){Toast(a,"This title needs a verified match before it can stream.");return;}
  int request=requests.getOrDefault(a,0)+1;requests.put(a,request);
  Toast(a,"Finding a stream…");
  NoirApp.data.network.execute(()->{try{
   List<JSONObject> options=rank(new PhoneDownloadProvider(s).search(item),s.optString("preferredResolution","1080p"),item.optBoolean("movie"));
   a.runOnUiThread(()->{if(a.isDestroyed()||a.isFinishing()||requests.getOrDefault(a,0)!=request)return;if(options.isEmpty()){Toast(a,"AIOStreams has no playable stream for this right now.");return;}
    if(choose||!options.get(0).optBoolean("cached"))pick(a,item,options);else play(a,item,options.get(0));});
  }catch(Exception e){a.runOnUiThread(()->{if(!a.isDestroyed()&&!a.isFinishing()&&requests.getOrDefault(a,0)==request)Toast(a,e.getMessage()==null?"AIOStreams couldn't be reached.":PhoneDownloadSettings.safe(e.getMessage(),s));});}});
 }
 static void pick(Activity a,JSONObject item,List<JSONObject> options){
  List<JSONObject> shown=options.subList(0,Math.min(15,options.size()));String[] labels=new String[shown.size()];
  for(int n=0;n<shown.size();n++){JSONObject o=shown.get(n);long size=o.optLong("size");labels[n]=(o.optBoolean("cached")?"⚡ ":"⏳ ")+(o.optString("resolution").isEmpty()?"":o.optString("resolution")+" · ")+(size>0?String.format(Locale.ROOT,"%.1f GB · ",size/1073741824d):"")+o.optString("name");}
  new AlertDialog.Builder(a).setTitle(Loc.t("Choose a stream")).setItems(labels,(d,n)->play(a,item,shown.get(n))).setNegativeButton(Loc.t("Cancel"),null).show();
 }
 static void play(Activity a,JSONObject item,JSONObject option){
  DataStore data=NoirApp.data;JSONObject saved=data.item(item.optString("id"));if(saved==null)saved=data.rememberPhoneItem(item);
  JSONObject h=option.optJSONObject("headers");Intent i=new Intent(a,PlayerActivity.class).putExtra("id",saved.optString("id")).putExtra("streamUrl",option.optString("url")).putExtra("streamRelease",option.optString("name"));
  if(h!=null)for(Iterator<String> it=h.keys();it.hasNext();){String k=it.next();if(k.equalsIgnoreCase("user-agent"))i.putExtra("streamAgent",h.optString(k));else if(k.equalsIgnoreCase("referer"))i.putExtra("streamReferrer",h.optString(k));}
  a.startActivity(i);if(a instanceof PlayerActivity)a.finish();
 }
 static void Toast(Activity a,String text){android.widget.Toast.makeText(a,Loc.t(text),android.widget.Toast.LENGTH_LONG).show();}
}
