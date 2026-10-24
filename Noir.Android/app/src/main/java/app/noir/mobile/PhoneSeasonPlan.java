package app.noir.mobile;

import org.json.*;
import java.util.*;

/** Selection is independent of pairing, views and network calls. */
final class PhoneSeasonPlan {
    static JSONObject nextStreamable(JSONObject current,List<JSONObject> items,long now){
        if(current.optBoolean("movie"))return null;JSONObject next=null;int season=current.optInt("season"),episode=current.optInt("episode");
        for(JSONObject item:items){int s=item.optInt("season"),e=item.optInt("episode");if(!item.optString("group").equals(current.optString("group"))||s<=0||!(s>season||s==season&&e>episode)||DataStore.watched(item))continue;if(next==null||s<next.optInt("season")||s==next.optInt("season")&&e<next.optInt("episode"))next=item;}
        if(next==null||!released(next,now))return null;JSONObject result=DataStore.copy(next);if(result.optString("imdb").isEmpty())DataStore.put(result,"imdb",current.optString("imdb"));return result;
    }
    static final String[] SCOPES={"Missing on phone","Unwatched","From episode N","Next N","Specific episodes","Several seasons"};
    static Set<Integer> numbers(String value){Set<Integer> result=new LinkedHashSet<>();for(String token:value.trim().split("[, ]+")){if(token.isEmpty())continue;String[] range=token.split("-");try{int start=Integer.parseInt(range[0]),end=range.length==2?Integer.parseInt(range[1]):start;if(start<0||end<start||end-start>1000)continue;for(int n=start;n<=end;n++)result.add(n);}catch(NumberFormatException ignored){}}return result;}
    static boolean released(JSONObject item,long now){String at=item.optString("airsAt",item.optString("released"));if(at.isEmpty()||at.equals("null"))return true;long date=DataStore.date(at);if(date==0)try{date=java.time.LocalDate.parse(at.substring(0,10)).atStartOfDay(java.time.ZoneOffset.UTC).toInstant().toEpochMilli();}catch(Exception ignored){return false;}return date<=now;}
    static List<JSONObject> select(List<JSONObject> episodes,int season,String scope,String input,Set<String> saved,long now){
        Set<Integer> values=numbers(input);int number=values.isEmpty()?1:values.iterator().next();List<JSONObject> sorted=new ArrayList<>(episodes),result=new ArrayList<>();int furthest=0;for(JSONObject e:episodes)if(e.optInt("season")>0&&DataStore.watched(e))furthest=Math.max(furthest,e.optInt("season")*10000+e.optInt("episode"));final int lastWatched=furthest;sorted.sort(Comparator.comparingInt((JSONObject e)->e.optInt("season")).thenComparingInt(e->e.optInt("episode")));
        for(JSONObject e:sorted){if(e.optInt("episode")<=0||e.optInt("season")<=0||!released(e,now))continue;if(scope.equals("Several seasons")?!values.contains(e.optInt("season")):scope.equals("Next N")?e.optInt("season")<season:e.optInt("season")!=season)continue;if(scope.equals("Next N")&&e.optInt("season")*10000+e.optInt("episode")<=lastWatched)continue;
            if(saved.contains(e.optString("id")))continue;
            if((scope.equals("Unwatched")||scope.equals("Next N"))&&DataStore.watched(e))continue;
            if(scope.equals("From episode N")&&e.optInt("episode")<number)continue;
            if(scope.equals("Specific episodes")&&!values.contains(e.optInt("episode")))continue;
            result.add(e);if(scope.equals("Next N")&&result.size()>=number)break;
        }return result;
    }
    static String episodeId(String group,int season,int episode)throws Exception{return "episode-"+DiscoveryClient.hash(group+":"+season+":"+episode);}
    static List<JSONObject> discoverItems(JSONObject title)throws Exception{
        String imdb=title.optString("id"),type=title.optString("noirType"),group=(type.equals("movie")?"movie:":"show:")+imdb;if(!imdb.matches("tt[0-9]+"))throw new java.io.IOException("Title needs an IMDb identity.");List<JSONObject> result=new ArrayList<>();boolean movie=type.equals("movie");List<JSONObject> videos=movie?List.of(new JSONObject()):DiscoveryClient.episodes(title);
        int index=0;for(JSONObject video:videos){JSONObject item=new JSONObject();int s=movie?0:video.optInt("season"),e=movie?0:video.optInt("episode");DataStore.put(item,"id",index++==0?"discover-"+type+"-"+imdb:episodeId(group,s,e));DataStore.put(item,"group",group);DataStore.put(item,"title",title.optString("name"));DataStore.put(item,"year",year(title.optString("releaseInfo")));DataStore.put(item,"imdb",imdb);DataStore.put(item,"movie",movie);DataStore.put(item,"season",s);DataStore.put(item,"episode",e);DataStore.put(item,"episodeTitle",video.optString("name"));DataStore.put(item,"summary",title.optString("description"));DataStore.put(item,"available",false);DataStore.put(item,"airsAt",video.optString("released",video.optString("firstAired")));DataStore.put(item,"posterUrl",title.optString("poster"));DataStore.put(item,"backdropUrl",title.optString("background"));JSONArray genres=title.optJSONArray("genres");if(genres!=null){List<String> names=new ArrayList<>();for(int g=0;g<genres.length()&&g<4;g++)names.add(genres.optString(g));DataStore.put(item,"genres",String.join(" · ",names));}result.add(item);}return result;
    }
    static int year(String text){try{return Integer.parseInt(text.substring(0,4));}catch(Exception e){return 0;}}
}
