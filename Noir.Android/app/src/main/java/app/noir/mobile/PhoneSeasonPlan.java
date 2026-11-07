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
    static final class Options {
        String scope="This episode",input="1",quality="1080p",audio="Any";int next=3;boolean onlyMissing=true,skipWatched,specials;long maxEach,maxTotal;
        JSONObject payload(int season){JSONObject r=new JSONObject(),p=new JSONObject();DataStore.put(r,"scope",scope);DataStore.put(r,"onlyMissing",onlyMissing);DataStore.put(r,"skipWatched",skipWatched);DataStore.put(r,"includeSpecials",specials);DataStore.put(r,"maxTotalBytes",maxTotal);DataStore.put(r,"nextCount",next);Set<Integer> ns=numbers(input);DataStore.put(r,"fromEpisode",ns.isEmpty()?1:ns.iterator().next());JSONArray seasons=new JSONArray(),specific=new JSONArray();if(scope.equals("Several seasons / whole show"))for(int n:ns)seasons.put(n);if(scope.equals("Specific episodes"))for(int n:ns){JSONObject e=new JSONObject();DataStore.put(e,"season",season);DataStore.put(e,"episode",n);specific.put(e);}DataStore.put(r,"seasons",seasons);DataStore.put(r,"specificEpisodes",specific);DataStore.put(p,"resolution",quality);DataStore.put(p,"language",audio.equals("Any")?"All":audio);DataStore.put(p,"maxEpisodeBytes",maxEach);DataStore.put(r,"preferences",p);return r;}
    }
    static List<JSONObject> select(List<JSONObject> episodes,int season,String scope,String input,Set<String> saved,long now){Options o=new Options();o.scope=scope.equals("Next N")?"Next N episodes":scope.equals("Several seasons")?"Several seasons / whole show":scope.equals("Unwatched")?"Unwatched only":scope.equals("Missing on phone")?"Missing episodes":scope;o.input=input;Set<Integer> n=numbers(input);o.next=n.isEmpty()?1:Math.max(1,Math.min(25,n.iterator().next()));return select(episodes,season,0,o,saved,Set.of(),now);}
    static List<JSONObject> select(List<JSONObject> episodes,int season,int episode,Options o,Set<String> saved,Set<String> queued,long now){
        List<JSONObject> all=new ArrayList<>(episodes),result=new ArrayList<>();all.sort(Comparator.comparingInt((JSONObject e)->e.optInt("season")).thenComparingInt(e->e.optInt("episode")));int furthest=-1;for(JSONObject e:all)if(DataStore.watched(e)&&(e.optInt("season")>0||o.specials))furthest=Math.max(furthest,e.optInt("season")*10000+e.optInt("episode"));Set<String> seen=new HashSet<>();Set<Integer> ns=numbers(o.input);int from=ns.isEmpty()?1:ns.iterator().next();
        for(JSONObject e:all){int s=e.optInt("season"),n=e.optInt("episode");String key=s+":"+n;if(n<=0||s<0||s==0&&!o.specials||!seen.add(key)||!released(e,now))continue;
            switch(o.scope){case "This episode":case "This movie":if(s!=season||n!=episode)continue;break;case "Whole show":case "Next N episodes":break;case "Several seasons / whole show":if(!ns.contains(s))continue;break;case "Specific episodes":if(s!=season||!ns.contains(n))continue;break;case "From episode N":if(s!=season||n<from)continue;break;default:if(s!=season)continue;}
            if(o.scope.equals("Next N episodes")&&s*10000+n<=furthest)continue;
            if(saved.contains(e.optString("id"))||queued.contains(e.optString("id")))continue;
            if((o.skipWatched||o.scope.equals("Unwatched only"))&&DataStore.watched(e))continue;result.add(e);if(o.scope.equals("Next N episodes")&&result.size()>=Math.max(1,Math.min(25,o.next)))break;
        }return result;
    }
    static boolean[] cap(List<Long> sizes,long maxEach,long maxTotal){boolean[] over=new boolean[sizes.size()];long bytes=0;boolean exceeded=false;for(int n=0;n<sizes.size();n++){long size=sizes.get(n)>0?sizes.get(n):maxEach;if(maxTotal>0&&(exceeded||size>maxTotal-bytes)){exceeded=true;over[n]=true;}else bytes+=Math.max(0,size);}return over;}
    static String episodeId(String group,int season,int episode)throws Exception{return "episode-"+DiscoveryClient.hash(group+":"+season+":"+episode);}
    static List<JSONObject> discoverItems(JSONObject title)throws Exception{
        String imdb=title.optString("id"),type=title.optString("noirType"),group=(type.equals("movie")?"movie:":"show:")+imdb;if(!imdb.matches("tt[0-9]+"))throw new java.io.IOException("Title needs an IMDb identity.");List<JSONObject> result=new ArrayList<>();boolean movie=type.equals("movie");List<JSONObject> videos=movie?List.of(new JSONObject()):DiscoveryClient.episodes(title);
        int index=0;for(JSONObject video:videos){JSONObject item=new JSONObject();int s=movie?0:video.optInt("season"),e=movie?0:video.optInt("episode");DataStore.put(item,"id",index++==0?"discover-"+type+"-"+imdb:episodeId(group,s,e));DataStore.put(item,"group",group);DataStore.put(item,"title",title.optString("name"));DataStore.put(item,"year",year(title.optString("releaseInfo")));DataStore.put(item,"imdb",imdb);DataStore.put(item,"movie",movie);DataStore.put(item,"season",s);DataStore.put(item,"episode",e);DataStore.put(item,"episodeTitle",video.optString("name"));DataStore.put(item,"summary",title.optString("description"));DataStore.put(item,"available",false);DataStore.put(item,"airsAt",video.optString("released",video.optString("firstAired")));DataStore.put(item,"posterUrl",title.optString("poster"));DataStore.put(item,"backdropUrl",title.optString("background"));JSONArray genres=title.optJSONArray("genres");if(genres!=null){List<String> names=new ArrayList<>();for(int g=0;g<genres.length()&&g<4;g++)names.add(genres.optString(g));DataStore.put(item,"genres",String.join(" · ",names));}result.add(item);}return result;
    }
    static int year(String text){try{return Integer.parseInt(text.substring(0,4));}catch(Exception e){return 0;}}
}
