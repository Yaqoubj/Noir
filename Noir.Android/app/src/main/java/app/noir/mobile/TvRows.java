package app.noir.mobile;

import org.json.*;
import java.util.*;

/** Local, read-only Home projection. Shared DataStore items are never modified. */
final class TvRows {
    static final class Tile {
        final JSONObject item,discover;final String key,sub;
        Tile(JSONObject value,String subtitle){item=value;discover=null;key=value.optString("group")+":"+value.optString("id");sub=subtitle;}
        Tile(JSONObject value){discover=value;item=art(value);key="discover:"+value.optString("noirType")+":"+value.optString("id");sub=value.optString("releaseInfo",value.optString("year"));}
        String title(){return item.optString("title");}
    }
    static final class Row {
        final String id,title;final boolean wide,actions;final List<Tile> tiles=new ArrayList<>(),allTiles=new ArrayList<>();
        Row(String key,String name,boolean landscape,boolean menu){id=key;title=Loc.t(name);wide=landscape;actions=menu;}
        void add(Tile tile){allTiles.add(tile);if(tiles.size()<TvUi.ROW_LIMIT)tiles.add(tile);}
    }
    final DataStore data;final long revision;final JSONObject settings,prefs;
    final List<List<JSONObject>> groups=new ArrayList<>();final Map<String,List<JSONObject>> index=new LinkedHashMap<>();
    final List<Row> rows=new ArrayList<>();final List<Tile> listed=new ArrayList<>(),ready=new ArrayList<>();
    final Set<String> offline=new HashSet<>();final boolean pc,stream;final long now=System.currentTimeMillis();
    TvRows(DataStore source,boolean canStream,Map<String,List<String>> credits){
        data=source;revision=data.version;settings=data.settings();prefs=data.prefs();pc=data.paired()&&data.online;stream=canStream;
        for(JSONObject i:DataStore.objects(data.itemsShared())){index.computeIfAbsent(i.optString("group"),k->new ArrayList<>()).add(i);if(!data.localFile(i.optString("id")).isEmpty())offline.add(i.optString("id"));}
        groups.addAll(index.values());groups.sort(Comparator.comparing(g->g.get(0).optString("title"),String.CASE_INSENSITIVE_ORDER));
        for(List<JSONObject> g:groups)g.sort(Comparator.comparingInt((JSONObject i)->i.optInt("season")==0?999:i.optInt("season")).thenComparingInt(i->i.optInt("episode")));
        build(credits);
    }
    boolean playable(JSONObject i){return offline.contains(i.optString("id"))||pc&&i.optBoolean("available");}
    boolean smartPlayable(JSONObject i){return playable(i)||stream&&SmartLists.streamReady(data.profileId,i.optString("id"),now);}
    boolean inLibrary(List<JSONObject> g){for(JSONObject i:g)if(i.optBoolean("inLibrary")||offline.contains(i.optString("id"))||NoirApp.downloads!=null&&NoirApp.downloads.job(i.optString("id"))!=null)return true;return false;}
    Tile poster(List<JSONObject> g){JSONObject i=g.get(0);return new Tile(i,i.optInt("year")>0?String.valueOf(i.optInt("year")):Loc.t(i.optBoolean("movie")?"Movie":"TV show"));}
    Row shelf(String id,String title,List<List<JSONObject>> list){Row row=new Row(id,title,false,false);for(List<JSONObject> g:list)row.add(poster(g));return row;}
    void build(Map<String,List<String>> credits){
        Row continuing=new Row("continue","Continue watching",true,true),next=new Row("up-next","Up next",true,true);
        List<NoirUi.Next> candidates=new ArrayList<>();
        for(List<JSONObject> g:groups){
            JSONObject first=g.get(0);if(first.optBoolean("listed"))listed.add(poster(g));
            for(JSONObject i:g)if(offline.contains(i.optString("id")))ready.add(new Tile(i,(i.optBoolean("movie")?"":MainActivity.episode(i)+" · ")+Loc.t("Ready to watch offline")));
            if(!first.optString("status").equals("Dropped")){NoirUi.Next n=next(g);if(n!=null&&(!(n.kind.equals(NoirUi.MISSING)||n.kind.equals(NoirUi.UPCOMING))||n.activity>=now-120L*86400000))candidates.add(n);}
        }
        candidates.sort((x,y)->{boolean a=x.kind.equals(NoirUi.UPCOMING),b=y.kind.equals(NoirUi.UPCOMING);return a!=b?(a?1:-1):Long.compare(y.activity,x.activity);});
        for(NoirUi.Next n:candidates){JSONObject i=n.item;boolean resume=MainActivity.position(i)>0&&!MainActivity.completed(i);String sub=i.optBoolean("movie")?"":MainActivity.episode(i)+" · ";sub+=resume?Loc.t("Resume")+" "+DataStore.time(MainActivity.position(i)):n.kind.equals(NoirUi.UPCOMING)?Loc.t("Coming up"):Loc.t("Next episode");(resume?continuing:next).add(new Tile(i,sub));}
        if(!continuing.tiles.isEmpty())rows.add(continuing);if(!next.tiles.isEmpty())rows.add(next);
        Map<String,Row> available=new LinkedHashMap<>();Row r=new Row("ready","Keep ready",false,false);for(Tile t:ready)r.add(t);available.put(r.id,r);
        r=new Row("list","My List",false,false);for(Tile t:listed)r.add(t);available.put(r.id,r);
        List<SmartLists.Title> titles=new ArrayList<>();for(List<JSONObject> g:groups)titles.add(SmartLists.read(g,settings,credits.getOrDefault(g.get(0).optString("group"),Collections.emptyList()),this::smartPlayable));
        Map<String,List<SmartLists.Pick>> smart=SmartLists.build(titles,now,Loc.arabic);
        for(String id:SmartLists.IDS){r=new Row(id,SmartLists.name(id),false,false);for(SmartLists.Pick pick:smart.get(id)){List<JSONObject> g=index.get(pick.key);if(g!=null)r.add(new Tile(g.get(0),pick.reason));}available.put(id,r);}
        List<List<JSONObject>> recent=new ArrayList<>(groups);recent.sort((x,y)->Long.compare(MainActivity.recent(y),MainActivity.recent(x)));
        List<List<JSONObject>> watched=new ArrayList<>(),shows=new ArrayList<>(),movies=new ArrayList<>(),added=new ArrayList<>();Set<String> genres=new TreeSet<>(),collections=new TreeSet<>();
        for(List<JSONObject> g:recent){JSONObject i=g.get(0);if(g.stream().anyMatch(DataStore::watched))watched.add(g);if(inLibrary(g)){(i.optBoolean("movie")?movies:shows).add(g);genres.addAll(HomeLayout.genre(i));}for(Object v:HomeLayout.jsonValues(i.optJSONArray("collections")))collections.add(String.valueOf(v));if(HomeLayout.added(g)>0)added.add(g);}
        added.sort((x,y)->Long.compare(HomeLayout.added(y),HomeLayout.added(x)));
        available.put("recently-added",shelf("recently-added","Recently added",added));available.put("recently-watched",shelf("recently-watched","Recently watched",watched));available.put("shows",shelf("shows","Your shows",shows));available.put("movies",shelf("movies","Your movies",movies));
        r=new Row("queue","Your play queue",false,false);for(Object v:HomeLayout.jsonValues(data.playQueue())){JSONObject i=data.item(String.valueOf(v));if(i!=null)r.add(new Tile(i,MainActivity.episode(i)));}available.put(r.id,r);
        if(!watched.isEmpty()){JSONObject seed=watched.get(0).get(0);List<List<JSONObject>> picks=new ArrayList<>();for(List<JSONObject> g:groups)if(!g.get(0).optString("group").equals(seed.optString("group"))&&g.stream().noneMatch(DataStore::watched)&&!Collections.disjoint(HomeLayout.genre(seed),HomeLayout.genre(g.get(0))))picks.add(g);available.put("because",shelf("because",Loc.f("Because you watched %s",seed.optString("title")),picks));}
        List<Row> genreRows=new ArrayList<>(),collectionRows=new ArrayList<>();
        for(String name:genres){List<List<JSONObject>> picks=new ArrayList<>();for(List<JSONObject> g:groups)if(HomeLayout.genre(g.get(0)).contains(name))picks.add(g);r=shelf("genre:"+name,name,picks);available.put(r.id,r);genreRows.add(r);}
        for(String name:collections){List<List<JSONObject>> picks=new ArrayList<>();for(List<JSONObject> g:groups)if(HomeLayout.jsonValues(g.get(0).optJSONArray("collections")).contains(name))picks.add(g);r=shelf("collection:"+name,name,picks);available.put(r.id,r);collectionRows.add(r);}
        JSONArray saved=prefs.optJSONArray("homeLayout");if(saved==null||saved.length()==0){saved=new JSONArray();for(String id:new String[]{"continue","top10","because","unfinished","comfort","directors","short-tonight","list","ready","recently-added"}){JSONObject entry=new JSONObject();DataStore.put(entry,"id",id);saved.put(entry);}}
        Set<String> used=new HashSet<>();for(HomeLayout.Row layout:HomeLayout.resolve(HomeLayout.migrate(saved),available.keySet())){
            if(!layout.visible)continue;
            if(layout.id.equals("genres")||layout.id.equals("collections")){List<Row> expanded=layout.id.equals("genres")?genreRows:collectionRows;for(int n=0;n<Math.min(3,expanded.size());n++)add(rows,expanded.get(n),used);}
            else add(rows,available.get(layout.id),used);
        }
    }
    static void add(List<Row> rows,Row row,Set<String> used){if(row!=null&&!row.tiles.isEmpty()&&used.add(row.id))rows.add(row);}
    // The phone's NoirUi.next/continuation rules, with the Activity-bound calls replaced by local predicates.
    NoirUi.Next next(List<JSONObject> g){
        JSONObject last=null;long at=0;for(JSONObject i:g)if(MainActivity.position(i)>0||MainActivity.completed(i)||DataStore.watched(i)){long r=DataStore.recent(i);if(last==null||r>at||r==at&&PhoneSmart.before(last,i)){last=i;at=r;}}
        if(last==null)return null;boolean done=MainActivity.completed(last)||DataStore.watched(last)&&MainActivity.position(last)<=0;
        if(!done)return new NoirUi.Next(last,playable(last)?NoirUi.RESUME:NoirUi.MISSING,at,0);if(last.optBoolean("movie"))return null;
        JSONObject progressLast=null;for(JSONObject i:g)if(i.optJSONObject("progress")!=null&&(MainActivity.position(i)>0||MainActivity.completed(i)))if(progressLast==null||DataStore.recent(i)>DataStore.recent(progressLast)||DataStore.recent(i)==DataStore.recent(progressLast)&&PhoneSmart.before(progressLast,i))progressLast=i;
        if(progressLast!=null){if(!MainActivity.completed(progressLast)){if(playable(progressLast))return new NoirUi.Next(progressLast,NoirUi.READY,at,0);}else for(JSONObject i:g)if(playable(i)&&!MainActivity.completed(i)&&(progressLast.optInt("season")==0?i.optInt("season")==0:i.optInt("season")>0)&&PhoneSmart.before(progressLast,i))return new NoirUi.Next(i,NoirUi.READY,at,0);}
        JSONObject best=null;for(JSONObject ep:g)if(ep.optInt("season")>0&&ep.optInt("episode")>0&&PhoneSmart.before(last,ep)&&!DataStore.watched(ep)&&(best==null||PhoneSmart.before(ep,best)))best=ep;
        JSONObject schedules=settings.optJSONObject("schedules");for(JSONObject ep:DataStore.objects(schedules==null?null:schedules.optJSONArray(last.optString("group")))){
            if(ep.optInt("season")<=0||ep.optInt("episode")<=0||(last.optInt("season")==0)!=(ep.optInt("season")==0)||!PhoneSmart.before(last,ep))continue;JSONObject known=find(g,ep);if(known!=null&&DataStore.watched(known))continue;if(best==null||PhoneSmart.before(ep,best))best=ep;
        }
        if(best==null)return null;JSONObject known=find(g,best),item=known==null?NoirUi.placeholder(last,best):known;long airs=DataStore.date(best.optString("airsAt"));return new NoirUi.Next(item,airs>now?NoirUi.UPCOMING:playable(item)?NoirUi.READY:NoirUi.MISSING,at,airs);
    }
    static JSONObject find(List<JSONObject> g,JSONObject ep){for(JSONObject i:g)if(i.optInt("season")==ep.optInt("season")&&i.optInt("episode")==ep.optInt("episode"))return i;return null;}
    List<Tile> search(String query){List<Tile> found=new ArrayList<>();String q=query.trim().toLowerCase(Locale.ROOT);if(q.isEmpty())return found;for(List<JSONObject> g:groups){JSONObject i=g.get(0);if((i.optString("title")+" "+i.optString("originalTitle")+" "+i.optString("year")).toLowerCase(Locale.ROOT).contains(q))found.add(poster(g));}return found;}
    static List<Tile> discovered(JSONArray titles,TvRows local){List<Tile> out=new ArrayList<>();Set<String> known=new HashSet<>();for(List<JSONObject> g:local.groups)for(JSONObject i:g)if(!i.optString("imdb").isEmpty())known.add((i.optBoolean("movie")?"movie":"series")+":"+i.optString("imdb"));for(JSONObject t:DataStore.objects(titles))if(!known.contains(t.optString("noirType")+":"+t.optString("id")))out.add(new Tile(t));return out;}
    static JSONObject art(JSONObject title){JSONObject i=DataStore.copy(title);DataStore.put(i,"title",title.optString("name",title.optString("title")));DataStore.put(i,"posterUrl",title.optString("poster"));DataStore.put(i,"backdropUrl",title.optString("background",title.optString("poster")));DataStore.put(i,"poster","");DataStore.put(i,"backdrop","");DataStore.put(i,"summary",title.optString("description"));DataStore.put(i,"movie",title.optString("noirType").equals("movie"));return i;}
    static Map<String,List<String>> credits(DataStore data,List<List<JSONObject>> groups){Map<String,List<String>> map=new HashMap<>();for(List<JSONObject> g:groups){List<String> names=PhoneCatchUp.directors(PhoneCatchUp.cachedMeta(data,g.get(0)));if(!names.isEmpty())map.put(g.get(0).optString("group"),names);}return map;}
}
