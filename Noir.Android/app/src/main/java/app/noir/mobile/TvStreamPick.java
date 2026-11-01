package app.noir.mobile;
import org.json.*;
import java.util.*;
import java.util.regex.*;

/** TV stream policy. Profiles and timestamps are supplied so tests need no TV hardware. */
final class TvStreamPick {
 static final class Profile {
  boolean avc=true,hevc,hevc10,av1,dolbyVision,dolbyVisionDecoder,passthrough;
  int maxHeight=1080;String quality="auto";double mbps;
  Set<String> audio=Collections.emptySet();
 }
 static final Pattern AV1=tag("av1"),HEVC=tag("x265|h[.]?265|hevc"),AVC=tag("x264|h[.]?264|avc"),DV=tag("dv|dovi|dolby.?vision"),HDR=tag("hdr(?:10\\+?)?"),HDRPLUS=tag("hdr10\\+"),HLG=tag("hlg"),TEN=tag("10-?bit|hi10p?");
 static final Pattern RES=tag("2160p|1080p|720p|576p|480p|4k"),REMUX=tag("remux"),TRUEHD=audioTag("true[ .-]?hd"),ATMOS=audioTag("atmos"),DTSHD=audioTag("dts[ .:-]?(?:hd|ma|x)(?:[ .-]?ma)?"),DTS=audioTag("dts"),EAC3=audioTag("e[ .-]?ac[ .-]?3|ddp|dd\\+"),AC3=audioTag("ac[ .-]?3|dd"),WEB=tag("web[ .-]?dl"),RIP=tag("web[ .-]?rip"),BLURAY=tag("blu[ .-]?ray|bdrip|brrip");
 static Pattern tag(String p){return Pattern.compile("(?i)(?<![a-z0-9])(?:"+p+")(?![a-z0-9])");}
 static Pattern audioTag(String p){return Pattern.compile("(?i)(?<![a-z0-9])(?:"+p+")(?![a-z])");}
 static boolean has(Pattern p,String text){return p.matcher(text).find();}
 static final class Release {
  final JSONObject option;final String codec,hdr,audio,source;final boolean tenBit,remux,dv,baseHdr,cached,atmos;final int height;final long size;final double mbps;
  Release(JSONObject o,double seconds){option=o;String n=o.optString("name");codec=has(AV1,n)?"av1":has(HEVC,n)?"hevc":has(AVC,n)?"avc":"unknown";
   dv=has(DV,n);baseHdr=has(HDR,n)||has(HLG,n);hdr=dv?"DV":has(HDRPLUS,n)?"HDR10+":has(HDR,n)?"HDR10":has(HLG,n)?"HLG":"";
   tenBit=has(TEN,n)||dv||baseHdr;remux=has(REMUX,n);atmos=has(ATMOS,n);audio=has(TRUEHD,n)?"truehd":has(DTSHD,n)?"dtshd":has(DTS,n)?"dts":has(EAC3,n)?"eac3":has(AC3,n)?"ac3":atmos?"atmos":"";
   source=remux?"remux":has(WEB,n)?"web-dl":has(RIP,n)?"webrip":has(BLURAY,n)?"bluray":"";
   Matcher m=RES.matcher(o.optString("resolution")+" "+n);String r=m.find()?m.group().toLowerCase(Locale.ROOT):"";height=r.equals("4k")?2160:r.isEmpty()?0:Integer.parseInt(r.substring(0,r.length()-1));
   cached=o.optBoolean("cached");size=Math.max(0,o.optLong("size"));mbps=seconds>0?size*8d/seconds/1000000d:0;
  }
 }
 // Library duration/progress.duration are seconds; discovery runtime and showRuntimes are minutes.
 static double runtime(JSONObject item,JSONObject settings){
  JSONObject p=item.optJSONObject("progress");double seconds=p==null?0:p.optDouble("duration");if(!(seconds>0))seconds=item.optDouble("duration");
  if(!(seconds>0)){Matcher m=Pattern.compile("[0-9]+(?:[.][0-9]+)?").matcher(item.optString("runtime"));if(m.find())seconds=Double.parseDouble(m.group())*60;}
  if(!(seconds>0)&&!item.optBoolean("movie")&&settings!=null){JSONObject shows=settings.optJSONObject("showRuntimes");if(shows!=null)seconds=shows.optDouble(item.optString("group"))*60;}
  return seconds>0&&Double.isFinite(seconds)?seconds:(item.optBoolean("movie")?115:45)*60;
 }
 static Release parse(JSONObject option,double seconds){return new Release(option,seconds);}
 static int decode(Release r,Profile p){
  if(r.codec.equals("unknown"))return 1;
  if(r.codec.equals("av1")&&!p.av1)return 3;
  if(r.codec.equals("hevc")&&(!p.hevc||r.tenBit&&!p.hevc10)||r.codec.equals("avc")&&!p.avc)return 2;
  if(r.dv&&!r.baseHdr&&!(p.dolbyVision&&p.dolbyVisionDecoder))return 1;
  return 0;
 }
 static boolean limited(Release r,Profile p){switch(p.quality){case "1080p":return r.height>1080;case "2160p":return r.remux;case "best":return false;default:return r.height>p.maxHeight||r.remux&&!(p.mbps>0&&p.mbps>=1.5*r.mbps&&r.mbps>0&&decode(r,p)==0);}}
 static String pickerReason(List<JSONObject> options,Profile p,double seconds){if(options.isEmpty())return "No playable streams found";Release best=parse(options.get(0),seconds);return limited(best,p)?"Above your quality limit":decode(best,p)>0?best.codec.equals("unknown")?"Compatibility unknown":"This TV can't decode it smoothly":"";}
 static double excess(Release r,Profile p){return Math.max(0,r.mbps-.6*(p.mbps>0?p.mbps:25));}
 static int distance(Release r,Profile p){int target=p.maxHeight>=2000&&p.hevc10&&!p.quality.equals("1080p")?2160:1080;return r.height==0?target:Math.abs(r.height-target);}
 static boolean audioPenalty(Release r,Profile p){return (r.audio.equals("truehd")||r.audio.equals("dtshd"))&&(!p.passthrough||!p.audio.contains(r.audio));}
 static List<JSONObject> rank(List<JSONObject> options,Profile profile,double seconds){
  List<Release> list=new ArrayList<>();for(JSONObject o:options)if(o.optString("service").equals("direct")&&PhoneStream.usableHeaders(o.optJSONObject("headers"))&&!Arrays.asList("p2p","live","error").contains(o.optString("streamType")))list.add(parse(o,seconds));
  list.sort(Comparator.comparing((Release r)->limited(r,profile)).thenComparingInt(r->decode(r,profile)).thenComparing(r->r.mbps<=0).thenComparing(r->!r.cached).thenComparingDouble(r->excess(r,profile)).thenComparingInt(r->distance(r,profile)).thenComparing(r->audioPenalty(r,profile)));
  List<JSONObject> result=new ArrayList<>();for(Release r:list)result.add(r.option);return result;
 }
 static {String[][] words={{"No playable streams found","لم يتم العثور على بث قابل للتشغيل"},{"Compatibility unknown","التوافق غير معروف"},{"Good for this TV","مناسب لهذا التلفزيون"},{"May stutter on this connection","قد يتقطع مع هذا الاتصال"},{"This TV can't decode it smoothly","لا يستطيع هذا التلفزيون فك ترميزه بسلاسة"},{"Above your quality limit","أعلى من حد الجودة المحدد"},{"Bitrate unknown","معدل البت غير معروف"},{"Unknown resolution","دقة غير معروفة"},{"%.1f GB","%.1f غيغابايت"},{"~%.0f Mbit/s","~%.0f ميغابت/ث"}};for(String[] w:words)Loc.AR.put(w[0],w[1]);}
 static String label(JSONObject o,Profile p,double seconds){Release r=parse(o,seconds);List<String> parts=new ArrayList<>();parts.add(r.height>0?r.height+"p":Loc.t("Unknown resolution"));if(!r.hdr.isEmpty())parts.add(r.hdr);parts.add(r.codec.equals("unknown")?Loc.t("Compatibility unknown"):r.codec.toUpperCase(Locale.ROOT));if(!r.source.isEmpty())parts.add(r.source.toUpperCase(Locale.ROOT));if(r.size>0)parts.add(Loc.f("%.1f GB",r.size/1073741824d));parts.add(r.mbps>0?Loc.f("~%.0f Mbit/s",r.mbps):Loc.t("Bitrate unknown"));String verdict=!r.codec.equals("unknown")&&decode(r,p)>0?"This TV can't decode it smoothly":excess(r,p)>0?"May stutter on this connection":limited(r,p)?"Above your quality limit":!r.codec.equals("unknown")&&r.mbps>0&&r.height>0?"Good for this TV":"";if(!verdict.isEmpty())parts.add(Loc.t(verdict));return (r.cached?"⚡ ":"⏳ ")+String.join(" · ",parts);}

 /** Counts each continuous buffering spell once, including a third spell that never recovers. */
 static final class Stalls {
  long since=-1;boolean counted;final ArrayDeque<Long> recent=new ArrayDeque<>();
  void buffering(boolean buffering,long now){if(buffering){if(since<0){since=now;counted=false;}}else {since=-1;counted=false;}}
  boolean check(long now){while(!recent.isEmpty()&&now-recent.peekFirst()>60000)recent.removeFirst();if(since>=0&&!counted&&now-since>2000){counted=true;recent.addLast(now);}return recent.size()>=3;}
  void reset(){since=-1;counted=false;recent.clear();}
 }
}
