package app.noir.mobile;
import android.app.*;
import android.content.Intent;
import org.json.*;
import java.util.*;
import java.util.regex.Pattern;

// Streaming on the phone through the phone's own AIOStreams link (Download settings): the same
// stream list Stremio shows, played in Noir's player so resume, history and Up next keep working.
final class PhoneStream {
 interface StreamFetch {byte[] fetch(String url,int max)throws Exception;}
 static volatile StreamFetch fetchOverride;
 static final java.util.concurrent.ThreadPoolExecutor interactive=new java.util.concurrent.ThreadPoolExecutor(2,2,30,java.util.concurrent.TimeUnit.SECONDS,new java.util.concurrent.ArrayBlockingQueue<>(8));
 static final class Search {final DataStore data;final DataStore.WorkCancel cancel=new DataStore.WorkCancel();java.util.concurrent.Future<?> future;AlertDialog dialog;Search(DataStore data){this.data=data;}void stop(){cancel.cancel();if(future!=null)future.cancel(true);if(dialog!=null)dialog.dismiss();}}
 static final Map<Activity,Search> searches=new WeakHashMap<>();
 static void cancel(Activity a){requests.put(a,requests.getOrDefault(a,0)+1);Search search=searches.remove(a);if(search!=null){search.stop();if(a instanceof PlayerActivity){PlayerActivity player=(PlayerActivity)a;if(player.message!=null&&player.message.getText().toString().equals(Loc.t("Finding a stream…")))player.message.setText("");}}interactive.purge();if(search!=null&&a instanceof TvDetailActivity)((TvDetailActivity)a).streamSearchFinished();}
 static void finished(Activity a,Search search){if(searches.get(a)==search)searches.remove(a);search.stop();if(a instanceof TvDetailActivity)((TvDetailActivity)a).streamSearchFinished();}
 static {Loc.AR.put("Finding a stream…","جارٍ البحث عن بث…");Loc.AR.put("Stream search is busy. Please try again.","البحث عن البث مشغول. حاول مجددًا.");}
 static final Map<Activity,Integer> requests=new WeakHashMap<>();
 static String cacheProfile="";
 static final Map<Activity,android.os.Bundle> transitions=new WeakHashMap<>();
 // Shared with Home's services row: decrypted at most once a minute.
 static JSONObject settings(Activity a){if(!cacheProfile.equals(NoirApp.data.profileId)||PhoneHarbor.phoneCache==null||PhoneHarbor.phoneAt<System.currentTimeMillis()-60000){try{PhoneHarbor.phoneCache=new PhoneDownloadSettings(NoirApp.data.context).load();}catch(Exception e){PhoneHarbor.phoneCache=new JSONObject();}PhoneHarbor.phoneAt=System.currentTimeMillis();cacheProfile=NoirApp.data.profileId;}return PhoneHarbor.phoneCache;}
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
 static boolean shouldPick(boolean tv,boolean choose,List<JSONObject> options,TvStreamPick.Profile profile,double seconds){return choose||options.isEmpty()||!options.get(0).optBoolean("cached")||tv&&!TvStreamPick.pickerReason(options,profile,seconds).isEmpty();}
 static void start(Activity a,JSONObject item,boolean choose){int count=0;float speed=PlayerPrefs.defaultSpeed(NoirApp.data);if(a instanceof PlayerActivity){PlayerActivity p=(PlayerActivity)a;count=p.autoCount;speed=p.speed;}start(a,item,choose,count,speed);}
 static void start(Activity a,JSONObject item,boolean choose,int count,float speed){
  android.os.Bundle transition=new android.os.Bundle();transition.putInt("autoCount",count);transition.putFloat("speed",speed);transitions.put(a,transition);
  JSONObject s=settings(a);if(s.optString("manifest").isEmpty()){if(a instanceof TvDetailActivity)((TvDetailActivity)a).streamSearchFinished();if(TvUi.isTv(a))TvSetupActivity.stream(a,()->start(a,item,choose,count,speed));else{Toast(a,"Add your AIOStreams link in phone Download settings to stream.");a.startActivity(new Intent(a,PhoneDownloadSettingsActivity.class));}return;}
  if(!item.optString("imdb").matches("tt[0-9]+")){if(a instanceof TvDetailActivity)((TvDetailActivity)a).streamSearchFinished();Toast(a,"This title needs a verified match before it can stream.");return;}
  cancel(a);int request=requests.getOrDefault(a,0)+1;requests.put(a,request);
  Search search=new Search(NoirApp.data);searches.put(a,search);search.dialog=new AlertDialog.Builder(a).setTitle(Loc.t("Finding a stream…")).setMessage(item.optString("title")).setNegativeButton(Loc.t("Cancel"),(d,w)->cancel(a)).create();search.dialog.setOnCancelListener(d->cancel(a));search.dialog.show();
  try{search.future=interactive.submit(()->{DataStore.workCancel.set(search.cancel);try{
   List<JSONObject> found=new PhoneDownloadProvider(s,(url,key,form,torbox)->{StreamFetch fetch=fetchOverride;return new JSONObject(new String(fetch==null?DiscoveryClient.fetch(url,8*1024*1024):fetch.fetch(url,8*1024*1024),java.nio.charset.StandardCharsets.UTF_8));}).search(item);List<JSONObject> options=TvUi.isTv(a)?TvStreamPick.rank(found,tvProfile(a),tvRuntime(item)):rank(found,s.optString("preferredResolution","1080p"),item.optBoolean("movie"));
   DataStore.checkWork();SmartLists.rememberStream(search.data.profileId,item.optString("id"),options.stream().anyMatch(o->o.optBoolean("cached")));
   a.runOnUiThread(()->{if(a.isDestroyed()||a.isFinishing()||NoirApp.data!=search.data||requests.getOrDefault(a,0)!=request){if(!a.isDestroyed()&&searches.get(a)==search)finished(a,search);return;}finished(a,search);boolean tv=TvUi.isTv(a);if(options.isEmpty()&&!tv){Toast(a,"AIOStreams has no playable stream for this right now.");return;}
    if(shouldPick(tv,choose,options,tv?tvProfile(a):null,tv?tvRuntime(item):0))pick(a,item,options);else if(tv)playTv(a,item,options,0);else play(a,item,options.get(0));});
  }catch(Exception e){a.runOnUiThread(()->{if(!a.isDestroyed()&&!a.isFinishing()&&NoirApp.data==search.data&&requests.getOrDefault(a,0)==request){finished(a,search);Toast(a,e.getMessage()==null?"AIOStreams couldn't be reached.":PhoneDownloadSettings.safe(e.getMessage(),s));}else if(!a.isDestroyed()&&searches.get(a)==search)finished(a,search);});}finally{DataStore.workCancel.remove();}});}catch(java.util.concurrent.RejectedExecutionException e){finished(a,search);Toast(a,"Stream search is busy. Please try again.");}
 }
 static void pick(Activity a,JSONObject item,List<JSONObject> options){
  if(TvUi.isTv(a)){TvStreamPick.Profile profile=tvProfile(a);double seconds=tvRuntime(item);List<JSONObject> shown=options.subList(0,Math.min(15,options.size()));String[] labels=new String[shown.size()];for(int n=0;n<labels.length;n++)labels[n]=TvStreamPick.label(shown.get(n),profile,seconds);String reason=TvStreamPick.pickerReason(options,profile,seconds);new AlertDialog.Builder(a).setTitle(Loc.t("Choose a stream")+(reason.isEmpty()?"":" · "+Loc.t(reason))).setItems(labels,(d,n)->playTv(a,item,options,n)).setNegativeButton(Loc.t("Cancel"),null).show();return;}
  List<JSONObject> shown=options.subList(0,Math.min(15,options.size()));String[] labels=new String[shown.size()];
  for(int n=0;n<shown.size();n++){JSONObject o=shown.get(n);long size=o.optLong("size");labels[n]=(o.optBoolean("cached")?"⚡ ":"⏳ ")+(o.optString("resolution").isEmpty()?"":o.optString("resolution")+" · ")+(size>0?String.format(Locale.ROOT,"%.1f GB · ",size/1073741824d):"")+o.optString("name");}
  new AlertDialog.Builder(a).setTitle(Loc.t("Choose a stream")).setItems(labels,(d,n)->play(a,item,shown.get(n))).setNegativeButton(Loc.t("Cancel"),null).show();
 }
 static void play(Activity a,JSONObject item,JSONObject option){
  DataStore data=NoirApp.data;JSONObject saved=data.item(item.optString("id"));if(saved==null)saved=data.rememberPhoneItem(item);
  Intent i=streamIntent(a,saved.optString("id"),option);
  a.startActivity(i);if(a instanceof PlayerActivity)a.finish();
 }
 static Intent streamIntent(Activity a,String id,JSONObject option){
  JSONObject h=option.optJSONObject("headers");Intent i=new Intent(a,PlayerActivity.class).putExtra("id",id).putExtra("streamUrl",option.optString("url")).putExtra("streamRelease",option.optString("name"));
  if(h!=null)for(Iterator<String> it=h.keys();it.hasNext();){String k=it.next();if(k.equalsIgnoreCase("user-agent"))i.putExtra("streamAgent",h.optString(k));else if(k.equalsIgnoreCase("referer"))i.putExtra("streamReferrer",h.optString(k));}
  android.os.Bundle transition=transitions.get(a);if(transition!=null)i.putExtras(transition);return i;
 }
 static TvStreamPick.Profile tvProfile(Activity a){TvCaps caps=TvCaps.get(a);TvStreamPick.Profile p=new TvStreamPick.Profile();p.avc=caps.avc;p.hevc=caps.hevc;p.hevc10=caps.hevc10;p.av1=caps.av1;p.dolbyVision=caps.dolbyVision;p.dolbyVisionDecoder=caps.dolbyVisionDecoder;p.maxHeight=caps.maxHeight;p.audio=caps.passthrough;p.quality=TvPrefs.quality(a);p.mbps=TvPrefs.mbps(a);p.passthrough=TvPrefs.passthrough(a);return p;}
 static double tvRuntime(JSONObject item){return TvStreamPick.runtime(item,NoirApp.data.settings());}
 static JSONObject tvCandidate(JSONObject option,double seconds){JSONObject c=new JSONObject();for(String key:new String[]{"url","name","headers","size","resolution","cached","service","streamType","hash"})if(option.has(key))DataStore.put(c,key,option.opt(key));DataStore.put(c,"mbps",TvStreamPick.parse(option,seconds).mbps);return c;}
 static JSONArray tvFallbacks(List<JSONObject> options,int picked,double seconds,TvStreamPick.Profile profile){JSONArray result=new JSONArray();Set<String> urls=new HashSet<>();urls.add(options.get(picked).optString("url"));for(boolean cached:new boolean[]{true,false})for(int n=picked+1;n<options.size()&&result.length()<40;n++){JSONObject o=options.get(n);if(o.optBoolean("cached")==cached&&!TvStreamPick.limited(TvStreamPick.parse(o,seconds),profile)&&urls.add(o.optString("url")))result.put(tvCandidate(o,seconds));}return result;}
 static void playTv(Activity a,JSONObject item,List<JSONObject> options,int picked){
  DataStore data=NoirApp.data;JSONObject saved=data.item(item.optString("id"));if(saved==null)saved=data.rememberPhoneItem(item);double seconds=tvRuntime(item);JSONObject choice=tvCandidate(options.get(picked),seconds);JSONArray fallbacks=tvFallbacks(options,picked,seconds,tvProfile(a));
  Intent i=streamIntent(a,saved.optString("id"),choice).putExtra("streamMbps",choice.optDouble("mbps")).putExtra("streamFallbacks",fallbacks.toString()).putExtra("streamAttempt",0).putExtra("streamTotal",Math.min(5,fallbacks.length()+1));
  a.startActivity(i);if(a instanceof PlayerActivity)a.finish();
 }
 static void Toast(Activity a,String text){android.widget.Toast.makeText(a,Loc.t(text),android.widget.Toast.LENGTH_LONG).show();}
}
