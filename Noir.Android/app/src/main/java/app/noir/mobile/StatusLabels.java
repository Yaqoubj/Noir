package app.noir.mobile;
import org.json.*;
import java.util.*;
final class StatusLabels {
 static {String[][] words={{"New season","موسم جديد"},{"New episode","حلقة جديدة"},{"Finale","ختام الموسم"},{"Still planned","ما زال ضمن الخطط"},{"#%d this week","#%d هذا الأسبوع"}};for(String[] p:words)Loc.AR.put(p[0],p[1]);}
 static final class Episode {final int season,episode;final long aired;Episode(int s,int e,long at){season=s;episode=e;aired=at;}}
 static String label(List<Episode> episodes,long now,int rank,boolean planned,boolean myListFilter){
  if(rank>=1&&rank<=10)return "#"+rank+" this week";Episode recent=null;long edge=now-14L*86400000;
  for(Episode e:episodes)if(e.season>0&&e.episode>0&&e.aired>0&&e.aired>=edge&&e.aired<=now&&(recent==null||e.aired>recent.aired||e.aired==recent.aired&&(e.season>recent.season||e.season==recent.season&&e.episode>recent.episode)))recent=e;
  if(recent!=null){int max=0;boolean newSeason=false;for(Episode e:episodes)if(e.season==recent.season){max=Math.max(max,e.episode);if(e.episode==1&&e.aired>0&&e.aired>=edge&&e.aired<=now)newSeason=true;}if(recent.episode>1&&recent.episode==max)return "Finale";if(newSeason)return "New season";return "New episode";}return planned&&myListFilter?"Still planned":"";
 }

 static String discover(JSONObject title){List<Episode> episodes=new ArrayList<>();for(JSONObject e:DataStore.objects(title.optJSONArray("videos")))episodes.add(new Episode(e.optInt("season"),e.optInt("episode"),DataStore.date(e.optString("released",e.optString("firstAired")))));int rank=0;if(PhoneHarbor.topTen!=null)for(int n=0;n<PhoneHarbor.topTen.length();n++)if(title.optString("id").equals(PhoneHarbor.topTen.optJSONObject(n).optString("id"))){rank=n+1;break;}return label(episodes,System.currentTimeMillis(),rank,false,false);}
 static String text(String value){if(value.startsWith("#"))try{return Loc.f("#%d this week",Integer.parseInt(value.split(" ")[0].substring(1)));}catch(Exception ignored){}return Loc.t(value);}
 static String tile(MainActivity a,JSONObject item){List<Episode> episodes=new ArrayList<>();JSONObject schedules=a.browsingSettings().optJSONObject("schedules");if(schedules!=null)for(JSONObject e:DataStore.objects(schedules.optJSONArray(item.optString("group"))))episodes.add(new Episode(e.optInt("season"),e.optInt("episode"),DataStore.date(e.optString("airsAt"))));int rank=0;JSONArray ten=PhoneHarbor.topTen;if(ten!=null&&!item.optString("imdb").isEmpty())for(int i=0;i<ten.length();i++)if(item.optString("imdb").equals(ten.optJSONObject(i).optString("id"))){rank=i+1;break;}return label(episodes,System.currentTimeMillis(),rank,false,false);}
 static void pill(MainActivity a,android.widget.FrameLayout art,JSONObject item){String value=tile(a,item);if(value.isEmpty())return;android.widget.TextView badge=a.ui.pill(text(value),MainActivity.BLACK,MainActivity.GOLD);android.widget.FrameLayout.LayoutParams p=new android.widget.FrameLayout.LayoutParams(-2,-2,android.view.Gravity.TOP|android.view.Gravity.START);p.setMargins(a.dp(7),a.dp(7),a.dp(7),0);art.addView(badge,p);}
}
