package app.noir.mobile;

import android.app.*;
import android.content.Intent;
import android.content.res.ColorStateList;
import android.graphics.Color;
import android.graphics.drawable.GradientDrawable;
import android.os.Bundle;
import android.text.TextUtils;
import android.view.*;
import android.widget.*;
import org.json.*;
import java.io.File;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.util.*;
import java.util.concurrent.*;

/** Remote-first details. Metadata fills reserved views without replacing the playback controls. */
public final class TvDetailActivity extends Activity {
    static final int PAGE_SIZE=Math.max(1,TvUi.ROW_LIMIT-2);
    static {
        String[][] words={{"Finding a stream…","جارٍ البحث عن بث…"},{"Play","تشغيل"},{"Resume","استئناف"},{"Stream","بث"},{"Start over","البدء من جديد"},{"My List","قائمتي"},{"Mark watched","تحديد كمشاهد"},{"Mark unwatched","تحديد كغير مشاهد"},{"Ready offline","جاهز دون اتصال"},{"On your PC","على الكمبيوتر"},{"On PC","على الكمبيوتر"},{"Downloaded","تم التنزيل"},{"Choose stream","اختيار بث"},{"Season %d","الموسم %d"},{"Specials","حلقات خاصة"},{"Cast","الممثلون"},{"Loading episodes…","جارٍ تحميل الحلقات…"},{"Details unavailable. Press OK to retry.","التفاصيل غير متاحة. اضغط موافق للمحاولة مجددًا."},{"No episodes available.","لا تتوفر حلقات."},{"No synopsis available.","لا يتوفر ملخص."},{"Title unavailable","العنوان غير متاح"},{"This title is no longer in your library.","لم يعد هذا العنوان في مكتبتك."},{"Back","رجوع"},{"Upcoming","قريبًا"},{"%d min left","متبقي %d دقيقة"},{"%d min","%d دقيقة"},{"Previous episodes","الحلقات السابقة"},{"More episodes","المزيد من الحلقات"},{"Could not save changes.","تعذر حفظ التغييرات."},{"Also mark earlier episodes?","تحديد الحلقات السابقة كمشاهدة أيضًا؟"},{"Also unmark later episodes?","إلغاء مشاهدة الحلقات التالية أيضًا؟"},{"This episode only","هذه الحلقة فقط"},{"All episodes","كل الحلقات"},{"Cancel","إلغاء"},{"Set up streaming","إعداد البث"},{"✓ Watched","✓ تمت المشاهدة"},{"Episode %d","الحلقة %d"},{"Hold OK on an episode for more options","اضغط مطولًا على موافق فوق حلقة لمزيد من الخيارات"}};
        for(String[] w:words)Loc.AR.put(w[0],w[1]);
    }
    final ExecutorService local=Executors.newSingleThreadExecutor();
    final ExecutorService metadata=Executors.newSingleThreadExecutor();
    final List<JSONObject> items=new ArrayList<>();
    final Map<Integer,TextView> tabs=new LinkedHashMap<>();
    final List<EpisodeTile> tiles=new ArrayList<>();
    DataStore data;JSONObject discover,first,target,settings;String group="",artKey="";
    FrameLayout screen;LinearLayout content,buttons,seasonRail,episodeRail,cast,castRail;
    HorizontalScrollView episodeScroll;ImageView backdrop;
    TextView name,meta,summary,status,primary,stream,restart,list,watched,episodeMessage,episodeHint;LinearLayout seasonLine;
    int season=-1,page=0;boolean loading=false,loaded=false,launching=false,resumed=false,restartStream=false,streamSearching=false;
    String focusKey="primary";Dialog menu;

    @Override protected void onCreate(Bundle state){
        super.onCreate(state);data=NoirApp.data;
        getWindow().getDecorView().setSystemUiVisibility(View.SYSTEM_UI_FLAG_FULLSCREEN|View.SYSTEM_UI_FLAG_HIDE_NAVIGATION|View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY);
        if(data==null){finish();return;}
        group=getIntent().getStringExtra("group");if(group==null)group="";
        try{String raw=getIntent().getStringExtra("discover");if(raw!=null){discover=new JSONObject(raw);if(discover.optString("noirType").isEmpty())DataStore.put(discover,"noirType",discover.optString("type","series"));}}catch(Exception ignored){}
        if(state!=null){season=state.getInt("season",-1);page=state.getInt("page",0);focusKey=state.getString("focus","primary");}
        readItems();build();refresh(false);restoreFocus(focusKey);
        if(discover!=null)loadDetails();
        if(first!=null)loadCachedCast(DataStore.copy(first));
    }
    @Override protected void onResume(){super.onResume();if(!streamSearching)launching=false;if(screen!=null){readItems();refresh(false);if(resumed)restoreFocus(focusKey);resumed=true;}}
    @Override protected void onPause(){rememberFocus();super.onPause();}
    @Override public void startActivity(Intent intent){if(intent.getComponent()!=null&&intent.getComponent().getClassName().equals(PlayerActivity.class.getName())){if(restartStream)intent.putExtra("restart",true);restartStream=false;streamSearchFinished();}super.startActivity(intent);}
    @Override protected void onSaveInstanceState(Bundle out){rememberFocus();out.putInt("season",season);out.putInt("page",page);out.putString("focus",focusKey);super.onSaveInstanceState(out);}
    @Override protected void onDestroy(){if(menu!=null)menu.dismiss();PhoneStream.requests.remove(this);metadata.shutdownNow();local.shutdown();super.onDestroy();}
    boolean alive(){return !isFinishing()&&!isDestroyed();}
    int dp(float n){return TvUi.dp(this,n);}
    String t(String s){return Loc.t(s);}
    LinearLayout column(){LinearLayout v=new LinearLayout(this);v.setOrientation(LinearLayout.VERTICAL);v.setClipChildren(false);v.setClipToPadding(false);return v;}
    TextView text(String s,int size,int color){return TvUi.text(this,s,size,color,false);}
    String coordinate(JSONObject i){return i.optBoolean("movie")?"movie":i.optInt("season")+":"+i.optInt("episode");}
    String episode(JSONObject i){return i.optBoolean("movie")?"":"S"+i.optInt("season")+"E"+i.optInt("episode");}
    boolean playable(JSONObject i){return i!=null&&(!data.localFile(i.optString("id")).isEmpty()||data.paired()&&data.online&&i.optBoolean("available"));}
    boolean released(JSONObject i){return PhoneSeasonPlan.released(i,System.currentTimeMillis());}
    boolean streaming(JSONObject i){return i!=null&&PhoneStream.available(this)&&i.optString("imdb").matches("tt[0-9]+");}
    double duration(JSONObject i){JSONObject p=i.optJSONObject("progress");double seconds=p==null?0:p.optDouble("duration",0);if(seconds<=0)seconds=i.optDouble("duration",0);if(seconds<=0)seconds=i.optDouble("runtime",0)*60;if(seconds<=0){JSONObject runtimes=settings.optJSONObject("showRuntimes");if(runtimes!=null)seconds=runtimes.optDouble(i.optString("group"),0)*60;}return seconds;}

    void readItems(){
        settings=data.settings();items.clear();Map<String,JSONObject> found=new LinkedHashMap<>();
        for(JSONObject i:DataStore.objects(data.itemsShared()))if(!group.isEmpty()?group.equals(i.optString("group")):discover!=null&&discover.optString("id").equals(i.optString("imdb"))&&PhoneCredits.movie(discover)==i.optBoolean("movie"))found.put(coordinate(i),i);
        JSONObject seed=found.isEmpty()?null:found.values().iterator().next();
        if(seed!=null&&!seed.optBoolean("movie")){
            JSONObject schedules=settings.optJSONObject("schedules");
            for(JSONObject e:DataStore.objects(schedules==null?null:schedules.optJSONArray(seed.optString("group")))){
                JSONObject known=found.get(coordinate(e)),i=known==null?NoirUi.placeholder(seed,e):DataStore.copy(known);
                if(known==null)for(String k:new String[]{"year","genres","summary","showSummary","imdbRating","posterUrl","backdropUrl","listed","listAt","status"})DataStore.put(i,k,seed.opt(k));
                DataStore.put(i,"airsAt",e.optString("airsAt"));if(i.optDouble("runtime",0)<=0)DataStore.put(i,"runtime",e.optDouble("runtime",0));found.put(coordinate(i),i);
            }
        }
        if(discover!=null)try{
            Map<String,JSONObject> videos=new HashMap<>();for(JSONObject v:DiscoveryClient.episodes(DataStore.copy(discover)))videos.put(coordinate(v),v);
            for(JSONObject i:PhoneSeasonPlan.discoverItems(DataStore.copy(discover))){
                JSONObject v=videos.get(coordinate(i));if(v!=null){DataStore.put(i,"episodeImageUrl",v.optString("thumbnail",v.optString("image")));DataStore.put(i,"runtime",number(v.optString("runtime")));}
                if(i.optDouble("runtime",0)<=0)DataStore.put(i,"runtime",number(discover.optString("runtime")));
                DataStore.put(i,"showSummary",discover.optString("description"));DataStore.put(i,"imdbRating",discover.optString("imdbRating"));
                JSONObject existing=found.get(coordinate(i));if(existing==null)found.put(coordinate(i),i);
                else {JSONObject merged=DataStore.copy(existing);for(String k:new String[]{"episodeImageUrl","runtime","imdbRating","showSummary","posterUrl","backdropUrl"})if(merged.optString(k).isEmpty()||merged.optString(k).equals("0.0"))DataStore.put(merged,k,i.opt(k));found.put(coordinate(i),merged);}
            }
        }catch(Exception ignored){}
        items.addAll(found.values());items.sort(Comparator.comparingInt((JSONObject i)->i.optInt("season")==0?999:i.optInt("season")).thenComparingInt(i->i.optInt("episode")));
        first=items.isEmpty()?null:items.get(0);target=pickTarget();
    }
    static double number(String s){try{return Double.parseDouble(s.replaceAll("[^0-9.].*$",""));}catch(Exception e){return 0;}}
    JSONObject pickTarget(){
        if(items.isEmpty())return null;JSONObject last=null;
        for(JSONObject i:items)if(MainActivity.position(i)>0||MainActivity.completed(i)||DataStore.watched(i))if(last==null||DataStore.recent(i)>DataStore.recent(last)||DataStore.recent(i)==DataStore.recent(last)&&PhoneSmart.before(last,i))last=i;
        if(last!=null){
            if(!MainActivity.completed(last)&&(!DataStore.watched(last)||MainActivity.position(last)>0))return last;
            if(!last.optBoolean("movie")){
                for(JSONObject i:items)if((last.optInt("season")==0?i.optInt("season")==0:i.optInt("season")>0)&&PhoneSmart.before(last,i)&&!MainActivity.completed(i)&&!DataStore.watched(i)&&playable(i)&&released(i))return i;
                for(JSONObject i:items)if(i.optInt("season")>0&&PhoneSmart.before(last,i)&&!DataStore.watched(i))return i;
            }
        }
        for(JSONObject i:items)if(!MainActivity.completed(i)&&!DataStore.watched(i)&&playable(i)&&released(i))return i;
        for(JSONObject i:items)if(!DataStore.watched(i)&&released(i))return i;
        return items.get(0);
    }
    void build(){
        FrameLayout full=new FrameLayout(this);full.setBackgroundColor(TvUi.BLACK);
        backdrop=new ImageView(this);backdrop.setScaleType(ImageView.ScaleType.CENTER_CROP);full.addView(backdrop,new FrameLayout.LayoutParams(-1,-1));
        View left=new View(this);left.setBackground(new GradientDrawable(GradientDrawable.Orientation.LEFT_RIGHT,new int[]{0xf70c0c0e,0xb00c0c0e,0x400c0c0e}));full.addView(left,new FrameLayout.LayoutParams(-1,-1));
        View bottom=new View(this);bottom.setBackground(new GradientDrawable(GradientDrawable.Orientation.TOP_BOTTOM,new int[]{0x000c0c0e,0x900c0c0e,TvUi.BLACK}));full.addView(bottom,new FrameLayout.LayoutParams(-1,-1));
        screen=TvUi.screen(this);screen.setBackgroundColor(Color.TRANSPARENT);screen.setLayoutDirection(Loc.arabic?View.LAYOUT_DIRECTION_RTL:View.LAYOUT_DIRECTION_LTR);full.addView(screen,new FrameLayout.LayoutParams(-1,-1));
        ScrollView scroll=new ScrollView(this);scroll.setFocusable(false);scroll.setFillViewport(false);scroll.setVerticalScrollBarEnabled(false);scroll.setClipChildren(false);scroll.setClipToPadding(false);content=column();scroll.addView(content);screen.addView(scroll,new FrameLayout.LayoutParams(-1,-1));
        name=TvUi.text(this,"",36,TvUi.WHITE,true);name.setMaxLines(1);name.setEllipsize(TextUtils.TruncateAt.END);content.addView(name,new LinearLayout.LayoutParams(-1,dp(48)));
        meta=text("",14,TvUi.GOLD);meta.setSingleLine(true);meta.setEllipsize(TextUtils.TruncateAt.END);content.addView(meta,new LinearLayout.LayoutParams(-1,dp(24)));
        summary=text("",15,TvUi.WHITE);summary.setMaxLines(3);summary.setEllipsize(TextUtils.TruncateAt.END);summary.setLineSpacing(dp(2),1);LinearLayout.LayoutParams synopsis=new LinearLayout.LayoutParams(dp(640),dp(64));synopsis.bottomMargin=dp(6);content.addView(summary,synopsis);
        status=text("",16,TvUi.GOLD);status.setAccessibilityLiveRegion(View.ACCESSIBILITY_LIVE_REGION_POLITE);content.addView(status,new LinearLayout.LayoutParams(-1,dp(28)));
        buttons=new LinearLayout(this);buttons.setGravity(Gravity.CENTER_VERTICAL);buttons.setClipChildren(false);buttons.setClipToPadding(false);
        HorizontalScrollView actionScroll=railScroll();actionScroll.addView(buttons);content.addView(actionScroll,new LinearLayout.LayoutParams(-1,dp(62)));
        primary=action("Play",true,"primary",()->{if(target!=null)play(target,false);else if(discover!=null)loadDetails();},242);
        stream=action("▶ "+t("Stream"),false,"stream",()->{if(target!=null)playStream(target,!playable(target),false);},150);
        restart=action("Start over",false,"restart",()->{if(target!=null)play(target,true);},142);
        list=action("My List",false,"list",this::toggleList,132);
        watched=action("Mark watched",false,"watched",()->{if(target!=null)toggleWatched(target);},184);
        seasonRail=new LinearLayout(this);seasonRail.setGravity(Gravity.CENTER_VERTICAL);seasonRail.setClipChildren(false);seasonRail.setClipToPadding(false);HorizontalScrollView seasons=railScroll();seasons.addView(seasonRail);seasonLine=new LinearLayout(this);seasonLine.setGravity(Gravity.CENTER_VERTICAL);seasonLine.setClipChildren(false);seasonLine.setClipToPadding(false);seasonLine.addView(seasons,new LinearLayout.LayoutParams(0,-1,1));episodeHint=text(t("Hold OK on an episode for more options"),13,TvUi.MUTED);episodeHint.setSingleLine(true);episodeHint.setPadding(dp(12),0,dp(4),0);seasonLine.addView(episodeHint,new LinearLayout.LayoutParams(-2,-2));content.addView(seasonLine,new LinearLayout.LayoutParams(-1,dp(56)));
        FrameLayout episodes=new FrameLayout(this);episodes.setClipChildren(false);episodes.setClipToPadding(false);content.addView(episodes,new LinearLayout.LayoutParams(-1,dp(216)));
        episodeMessage=text(t("Loading episodes…"),15,TvUi.MUTED);episodeMessage.setGravity(Gravity.CENTER_VERTICAL);episodes.addView(episodeMessage,new FrameLayout.LayoutParams(-1,-1));
        episodeScroll=railScroll();episodeRail=new LinearLayout(this);episodeRail.setGravity(Gravity.TOP);episodeRail.setClipChildren(false);episodeRail.setClipToPadding(false);episodeScroll.addView(episodeRail);episodes.addView(episodeScroll,new FrameLayout.LayoutParams(-1,-1));
        cast=column();content.addView(cast,new LinearLayout.LayoutParams(-1,dp(112)));cast.setVisibility(View.INVISIBLE);
        setContentView(full);
    }
    HorizontalScrollView railScroll(){HorizontalScrollView s=new HorizontalScrollView(this);s.setFocusable(false);s.setHorizontalScrollBarEnabled(false);s.setClipChildren(false);s.setClipToPadding(false);s.setPadding(dp(4),dp(5),dp(10),dp(5));s.setDescendantFocusability(ViewGroup.FOCUS_AFTER_DESCENDANTS);return s;}
    TextView action(String label,boolean gold,String key,Runnable run,int width){TextView v=TvUi.button(this,t(label),gold,run);v.setTextSize(16);v.setPadding(dp(10),dp(10),dp(10),dp(10));v.setMinWidth(0);v.setSingleLine(true);v.setEllipsize(TextUtils.TruncateAt.END);v.setId(View.generateViewId());v.setTag(key);LinearLayout.LayoutParams p=new LinearLayout.LayoutParams(dp(width),dp(48));p.setMargins(0,0,dp(10),0);buttons.addView(v,p);return v;}
    void visibleAction(TextView v,boolean show){boolean focused=v.hasFocus();v.setVisibility(show?View.VISIBLE:View.GONE);v.setFocusable(show);if(focused&&!show){primary.requestFocus();focusKey="primary";}}
    void refresh(boolean structure){
        JSONObject display=first==null?discover:first;
        name.setText(display==null?t("Title unavailable"):display.optString("title",display.optString("name")));
        meta.setText(facts());String synopsis=discover!=null?discover.optString("description"):first==null?"":first.optString("showSummary");if(synopsis.isEmpty()&&first!=null)synopsis=first.optString("summary");summary.setText(synopsis.isEmpty()?t(first==null&&discover==null?"This title is no longer in your library.":"No synopsis available."):synopsis);
        loadBackdrop();
        if(target==null){primary.setText(t(discover==null?"Back":loading||!loaded?"Loading episodes…":"Details unavailable. Press OK to retry."));primary.setOnClickListener(v->{if(discover==null)finish();else loadDetails();});status.setText("");}
        else{
            String label=t("Play")+(target.optBoolean("movie")?"":" "+episode(target));
            double position=MainActivity.position(target);if(position>0&&!MainActivity.completed(target)){label=t("Resume")+(target.optBoolean("movie")?"":" "+episode(target));double left=duration(target)-position;if(left>0)label+=" · "+Loc.f("%d min left",(int)Math.ceil(left/60));}
            primary.setText(label);primary.setOnClickListener(v->play(target,false));
            status.setText(!released(target)?t("Upcoming"):!data.localFile(target.optString("id")).isEmpty()?t("Ready offline"):target.optBoolean("available")?t("On your PC"):streaming(target)?t("Stream"):t("Set up streaming"));
            if(!released(target))primary.setText(t("Upcoming")+" · "+episode(target));primary.setAlpha(released(target)?1:.5f);
            list.setText((first.optBoolean("listed")?"✓ ":"＋ ")+t("My List"));stream.setText(playable(target)?"▶ "+t("Stream"):t("Choose stream"));watched.setText(t(DataStore.watched(target)?"Mark unwatched":"Mark watched"));
        }
        visibleAction(stream,streaming(target)&&released(target));visibleAction(restart,target!=null&&MainActivity.position(target)>0);visibleAction(list,first!=null);visibleAction(watched,target!=null&&released(target));
        if(streamSearching){primary.setText(t("Finding a stream…"));status.setText(t("Finding a stream…"));}
        boolean show=first==null?discover!=null&&!PhoneCredits.movie(discover):!first.optBoolean("movie");seasonLine.setVisibility(show?View.VISIBLE:View.GONE);((View)episodeScroll.getParent()).setVisibility(show?View.VISIBLE:View.GONE);
        if(show){TreeSet<Integer> known=new TreeSet<>();for(JSONObject i:items)known.add(i.optInt("season"));if(season<0||!known.contains(season)){season=target==null?-1:target.optInt("season");page=targetPage();structure=true;}
            if(!known.equals(tabs.keySet())){renderSeasons(known);structure=true;}
            List<JSONObject> selected=seasonItems();String signature=signature(selected);if(structure||!signature.equals(String.valueOf(episodeRail.getTag())))renderEpisodes(selected);else for(EpisodeTile tile:tiles)tile.refresh();
            episodeMessage.setText(t(items.isEmpty()?(loading||!loaded?"Loading episodes…":"No episodes available."):"No episodes available."));episodeMessage.setVisibility(selected.isEmpty()?View.VISIBLE:View.INVISIBLE);episodeHint.setVisibility(selected.isEmpty()?View.INVISIBLE:View.VISIBLE);linkVertical();
        }else linkVertical();
    }
    String facts(){List<String> values=new ArrayList<>();JSONObject d=first;String year=discover!=null?discover.optString("releaseInfo"):d==null?"":d.optInt("year")>0?d.optString("year"):"";String rating=discover!=null?discover.optString("imdbRating"):d==null?"":d.optString("imdbRating",d.optString("rating"));double minutes=discover!=null?number(discover.optString("runtime")):d==null?0:duration(d)/60;String runtime=minutes>0?Loc.f("%d min",(int)Math.round(minutes)):"";String genres=discover!=null?PhoneCredits.names(discover.opt("genres")):d==null?"":PhoneCredits.names(d.opt("genres"));List<String> translated=new ArrayList<>();for(String genre:genres.split("[·,]"))if(!genre.trim().isEmpty())translated.add(t(genre.trim()));for(String s:new String[]{year,rating.isEmpty()?"":"★ "+rating,runtime,String.join(" · ",translated)})if(!s.isEmpty()&&!s.equals("0"))values.add(s);return String.join(" · ",values);}
    void loadBackdrop(){if(first==null&&discover==null)return;File file=null;String url="";if(first!=null)for(String k:new String[]{"backdrop","poster"}){String s=first.optString(k);if(!s.isEmpty()){File f=data.art(s);if(f.exists()){file=f;break;}}}if(discover!=null)url=discover.optString("background",discover.optString("poster"));if(url.isEmpty()&&first!=null)url=first.optString("backdropUrl",first.optString("posterUrl"));if(url.isEmpty()&&first!=null)url=data.phoneArt(first.optString("imdb"),"backdropUrl");if(url.isEmpty()&&first!=null)url=data.phoneArt(first.optString("imdb"),"posterUrl");String key=file==null?url:file.getPath();if(key.equals(artKey))return;artKey=key;TvUi.loadSource(backdrop,file,url,getResources().getDisplayMetrics().widthPixels,getResources().getDisplayMetrics().heightPixels);}
    List<JSONObject> seasonItems(){List<JSONObject> selected=new ArrayList<>();for(JSONObject i:items)if(i.optInt("season")==season)selected.add(i);return selected;}
    int targetPage(){int n=0;for(JSONObject i:items)if(i.optInt("season")==season){if(target!=null&&coordinate(i).equals(coordinate(target)))return n/PAGE_SIZE;n++;}return 0;}
    String signature(List<JSONObject> selected){StringBuilder b=new StringBuilder().append(season).append('/').append(page);for(JSONObject i:selected)b.append('/').append(i.optString("id"));return b.toString();}
    void renderSeasons(Set<Integer> known){
        String focused=currentFocusKey();seasonRail.removeAllViews();tabs.clear();
        for(int n:known){TextView tab=TvUi.button(this,n==0?t("Specials"):Loc.f("Season %d",n),false,()->selectSeason(n));tab.setTextSize(16);tab.setMinWidth(dp(104));tab.setPadding(dp(14),dp(8),dp(14),dp(8));tab.setId(View.generateViewId());tab.setTag("season:"+n);LinearLayout.LayoutParams lp=new LinearLayout.LayoutParams(-2,dp(42));lp.setMargins(0,0,dp(12),0);seasonRail.addView(tab,lp);tabs.put(n,tab);}
        updateTabs();if(focused.startsWith("season:"))restoreFocus(focused);
    }
    void updateTabs(){for(Map.Entry<Integer,TextView> e:tabs.entrySet()){e.getValue().setTextColor(e.getKey()==season?TvUi.GOLD:TvUi.WHITE);e.getValue().setSelected(e.getKey()==season);}}
    void selectSeason(int selected){if(season==selected)return;season=selected;page=targetPage();updateTabs();renderEpisodes(seasonItems());linkVertical();}
    void renderEpisodes(List<JSONObject> selected){
        String focused=currentFocusKey();episodeRail.removeAllViews();tiles.clear();page=Math.max(0,Math.min(page,Math.max(0,(selected.size()-1)/PAGE_SIZE)));
        if(page>0)addPageButton(false);
        for(int n=page*PAGE_SIZE;n<Math.min(selected.size(),(page+1)*PAGE_SIZE);n++){EpisodeTile tile=new EpisodeTile(selected.get(n));tiles.add(tile);LinearLayout.LayoutParams lp=new LinearLayout.LayoutParams(dp(234),dp(188));lp.setMargins(0,0,dp(14),0);episodeRail.addView(tile.box,lp);}
        if((page+1)*PAGE_SIZE<selected.size())addPageButton(true);
        episodeRail.setTag(signature(selected));episodeScroll.scrollTo(0,0);linkHorizontal(episodeRail);linkVertical();if(focused.startsWith("episode:"))restoreFocus(focused);
    }
    void addPageButton(boolean next){TextView v=TvUi.button(this,t(next?"More episodes":"Previous episodes"),false,()->{page+=next?1:-1;renderEpisodes(seasonItems());View preferred=preferredEpisode();if(preferred!=null)preferred.requestFocus();});v.setTextSize(16);v.setMaxLines(2);v.setId(View.generateViewId());v.setTag(next?"more":"previous");LinearLayout.LayoutParams lp=new LinearLayout.LayoutParams(dp(156),dp(178));lp.setMargins(0,0,dp(14),0);episodeRail.addView(v,lp);}
    void linkHorizontal(LinearLayout rail){for(int n=0;n<rail.getChildCount();n++){View v=rail.getChildAt(n);int before=Math.max(0,n-1),after=Math.min(rail.getChildCount()-1,n+1);v.setNextFocusLeftId(rail.getChildAt(Loc.arabic?after:before).getId());v.setNextFocusRightId(rail.getChildAt(Loc.arabic?before:after).getId());}}
    View preferredEpisode(){for(EpisodeTile tile:tiles)if(target!=null&&coordinate(tile.item).equals(coordinate(target)))return tile.box;for(EpisodeTile tile:tiles)if(!DataStore.watched(fresh(tile.item))&&released(tile.item))return tile.box;return tiles.isEmpty()?null:tiles.get(0).box;}
    void linkVertical(){
        TextView tab=tabs.get(season);View ep=preferredEpisode(),person=castRail!=null&&cast.getVisibility()==View.VISIBLE&&castRail.getChildCount()>0?castRail.getChildAt(0):null;
        List<View> actions=new ArrayList<>();for(int n=0;n<buttons.getChildCount();n++){View b=buttons.getChildAt(n);if(b.getVisibility()==View.VISIBLE)actions.add(b);b.setNextFocusUpId(b.getId());b.setNextFocusDownId(tab!=null?tab.getId():person!=null?person.getId():b.getId());}
        for(int n=0;n<actions.size();n++){View b=actions.get(n);b.setNextFocusLeftId(actions.get(Loc.arabic?Math.min(actions.size()-1,n+1):Math.max(0,n-1)).getId());b.setNextFocusRightId(actions.get(Loc.arabic?Math.max(0,n-1):Math.min(actions.size()-1,n+1)).getId());}
        for(TextView s:tabs.values()){s.setNextFocusUpId(primary.getId());s.setNextFocusDownId(ep==null?s.getId():ep.getId());}
        for(int n=0;n<episodeRail.getChildCount();n++){View v=episodeRail.getChildAt(n);if(tab!=null)v.setNextFocusUpId(tab.getId());v.setNextFocusDownId(person==null?v.getId():person.getId());}
        if(castRail!=null)for(int n=0;n<castRail.getChildCount();n++){View v=castRail.getChildAt(n);v.setNextFocusUpId(ep==null?primary.getId():ep.getId());v.setNextFocusDownId(v.getId());}linkHorizontal(seasonRail);
    }
    JSONObject fresh(JSONObject item){for(JSONObject i:items)if(i.optString("id").equals(item.optString("id")))return i;JSONObject saved=data.item(item.optString("id"));return saved==null?item:saved;}
    String currentFocusKey(){View v=getCurrentFocus();return v!=null&&v.getTag() instanceof String?(String)v.getTag():"";}
    void rememberFocus(){String key=currentFocusKey();if(!key.isEmpty())focusKey=key;}
    void restoreFocus(String key){View v=screen.findViewWithTag(key);if(v==null&&key.startsWith("episode:"))v=preferredEpisode();if(v==null||!v.isFocusable()||v.getVisibility()!=View.VISIBLE)v=primary;v.requestFocus();}

    final class EpisodeTile {
        final JSONObject item;final LinearLayout box;final TextView title,info,badge;final ProgressBar progress;final ImageView image;String imageKey="";
        EpisodeTile(JSONObject i){item=i;box=column();box.setPadding(dp(5),dp(5),dp(5),dp(4));box.setId(View.generateViewId());box.setTag("episode:"+i.optString("id"));TvUi.focusable(box,Color.argb(100,12,12,14),dp(9));
            FrameLayout frame=new FrameLayout(TvDetailActivity.this);image=new ImageView(TvDetailActivity.this);image.setScaleType(ImageView.ScaleType.CENTER_CROP);image.setBackground(TvUi.bg(TvUi.PANEL,dp(6)));image.setClipToOutline(true);frame.addView(image,new FrameLayout.LayoutParams(-1,-1));box.addView(frame,new LinearLayout.LayoutParams(-1,dp(106)));
            badge=text("",14,TvUi.WHITE);badge.setPadding(dp(5),dp(3),dp(5),dp(3));badge.setBackground(TvUi.bg(0xc00c0c0e,dp(4)));FrameLayout.LayoutParams bp=new FrameLayout.LayoutParams(-2,-2,Gravity.TOP|Gravity.END);bp.setMargins(0,dp(4),dp(4),0);frame.addView(badge,bp);
            progress=new ProgressBar(TvDetailActivity.this,null,android.R.attr.progressBarStyleHorizontal);progress.setProgressTintList(ColorStateList.valueOf(TvUi.GOLD));progress.setMax(1000);progress.setFocusable(false);frame.addView(progress,new FrameLayout.LayoutParams(-1,dp(4),Gravity.BOTTOM));
            title=TvUi.text(TvDetailActivity.this,"",16,TvUi.WHITE,true);title.setSingleLine(true);title.setEllipsize(TextUtils.TruncateAt.END);title.setPadding(0,dp(5),0,0);box.addView(title,new LinearLayout.LayoutParams(-1,dp(32)));info=text("",16,TvUi.MUTED);info.setSingleLine(true);info.setEllipsize(TextUtils.TruncateAt.END);box.addView(info,new LinearLayout.LayoutParams(-1,dp(28)));
            box.setOnClickListener(v->play(fresh(item),false));box.setOnLongClickListener(v->{episodeMenu(fresh(item));return true;});box.setOnKeyListener((v,key,event)->{if(key==KeyEvent.KEYCODE_MENU){if(event.getAction()==KeyEvent.ACTION_DOWN&&event.getRepeatCount()==0)episodeMenu(fresh(item));return true;}return false;});
            refresh();
        }
        // Unwatched episodes keep the show artwork when spoilers are hidden, like the phone.
        JSONObject showArt(JSONObject i){JSONObject art=new JSONObject();for(String key:new String[]{"backdrop","backdropUrl","poster","posterUrl","imdb"})try{art.putOpt(key,i.opt(key));}catch(Exception ignored){}return art;}
        void refresh(){JSONObject i=fresh(item);boolean spoiler=settings.optBoolean("hideSpoilers",true)&&!DataStore.watched(i)&&released(i);String titleText=spoiler?"":i.optString("episodeTitle");if(titleText.isEmpty())titleText=Loc.f("Episode %d",i.optInt("episode"));title.setText("E"+i.optInt("episode")+" · "+titleText);String hint=!released(i)?t("Upcoming"):!data.localFile(i.optString("id")).isEmpty()?t("Downloaded"):i.optBoolean("available")?t("On PC"):t("Stream");double seconds=duration(i);info.setText((seconds>0?Loc.f("%d min",(int)Math.round(seconds/60))+" · ":"")+hint);boolean seen=DataStore.watched(i);badge.setText(seen?t("✓ Watched"):"");badge.setVisibility(seen?View.VISIBLE:View.INVISIBLE);double pos=MainActivity.position(i);progress.setVisibility(pos>0&&!MainActivity.completed(i)&&seconds>0?View.VISIBLE:View.INVISIBLE);progress.setProgress((int)Math.min(1000,1000*pos/Math.max(1,seconds)));box.setContentDescription(title.getText()+" · "+info.getText()+(seen?" · "+t("✓ Watched"):""));String key=spoiler+i.optString("episodeImageUrl")+i.optString("episodeImage")+i.optString("backdrop")+i.optString("backdropUrl");if(!key.equals(imageKey)||image.getTag()==null){imageKey=key;String url=spoiler?"":i.optString("episodeImageUrl");if(!url.isEmpty())TvUi.loadSource(image,null,url,dp(224),dp(106));else TvUi.load(image,spoiler?showArt(i):i,true,dp(224),dp(106));}}
    }

    void loadDetails(){
        if(loading||discover==null)return;loading=true;refresh(false);JSONObject seed=DataStore.copy(discover);
        metadata.execute(()->{try{JSONObject details=new DiscoveryClient(this).details(seed);runOnUiThread(()->{if(!alive())return;loading=false;loaded=true;discover=details;readItems();refresh(false);if(first!=null)loadCachedCast(DataStore.copy(first));});}catch(Exception e){runOnUiThread(()->{if(!alive())return;loading=false;loaded=true;refresh(false);if(items.isEmpty())episodeMessage.setText(t("Details unavailable. Press OK to retry."));});}});
    }
    void streamSearchFinished(){streamSearching=false;launching=false;if(alive()&&screen!=null)refresh(false);}
    List<JSONObject> playbackMetadata(){return discover==null?Collections.emptyList():new ArrayList<>(items);}
    JSONObject savedForPlayback(JSONObject i,List<JSONObject> episodes){if(!episodes.isEmpty())data.refreshPhoneTitle(episodes);return saved(i);}
    JSONObject saved(JSONObject i){JSONObject existing=data.item(i.optString("id"));return existing==null?data.rememberPhoneItem(i):existing;}
    void play(JSONObject i,boolean fromStart){
        if(i==null||launching||!released(i))return;rememberFocus();
        if(!playable(i)){playStream(i,false,fromStart);return;}
        restartStream=false;PhoneStream.requests.put(this,PhoneStream.requests.getOrDefault(this,0)+1);launching=true;List<JSONObject> episodes=playbackMetadata();local.execute(()->{try{JSONObject s=savedForPlayback(i,episodes);runOnUiThread(()->{if(!alive())return;startActivity(new Intent(this,PlayerActivity.class).putExtra("id",s.optString("id")).putExtra("restart",fromStart));data.syncSoon();});}catch(Exception e){runOnUiThread(this::streamSearchFinished);saveError();}});
    }
    void playStream(JSONObject i,boolean choose,boolean fromStart){
        if(launching||streamSearching||!released(i))return;rememberFocus();launching=true;streamSearching=true;refresh(false);List<JSONObject> episodes=playbackMetadata();
        local.execute(()->{try{JSONObject selected=savedForPlayback(i,episodes);runOnUiThread(()->{if(alive()){restartStream=fromStart;PhoneStream.start(this,selected,choose);}});}catch(Exception e){runOnUiThread(this::streamSearchFinished);saveError();}});
    }
    void toggleList(){if(first==null)return;rememberFocus();List<JSONObject> values=new ArrayList<>(items);JSONObject item=first;local.execute(()->{try{data.refreshPhoneTitle(values);JSONObject s=saved(item);JSONObject op=data.operation(s.optString("id"),"list");DataStore.put(op,"value",!s.optBoolean("listed"));data.queue(op);data.syncSoon();changed();}catch(Exception e){saveError();}});}
    void toggleWatched(JSONObject item){
        rememberFocus();JSONObject i=fresh(item);boolean seen=DataStore.watched(i);List<JSONObject> related=new ArrayList<>();
        if(!i.optBoolean("movie")&&i.optInt("season")>0)for(JSONObject e:items)if(e.optInt("season")>0&&released(e)&&(seen?PhoneSmart.before(i,e)&&DataStore.watched(e):PhoneSmart.before(e,i)&&!DataStore.watched(e)))related.add(e);
        if(related.isEmpty()){markItems(List.of(i),seen);return;}
        new AlertDialog.Builder(this).setTitle(t(seen?"Also unmark later episodes?":"Also mark earlier episodes?")).setNegativeButton(t("This episode only"),(d,w)->markItems(List.of(i),seen)).setPositiveButton(t("All episodes"),(d,w)->{related.add(i);markItems(related,seen);}).setNeutralButton(t("Cancel"),null).show();
    }
    void markItems(List<JSONObject> values,boolean unmark){
        List<JSONObject> all=new ArrayList<>(items);JSONObject states=settings.optJSONObject("showStates");JSONArray specials=settings.optJSONArray("includeSpecials");String key=values.get(0).optString("group");boolean included=false;for(int n=0;specials!=null&&n<specials.length();n++)if(key.equals(specials.optString(n)))included=true;final boolean includeSpecials=included;
        local.execute(()->{try{
            data.refreshPhoneTitle(values);for(JSONObject i:values){String id=saved(i).optString("id");if(unmark)data.unview(id,"");else if(!DataStore.watched(data.item(id)))data.mark(id,true);}
            JSONObject show=values.get(0);if(!unmark&&!show.optBoolean("movie")&&states!=null&&states.optString(key).equals("Ended")){
                boolean complete=false;for(JSONObject i:all)if(i.optInt("episode")>0&&(i.optInt("season")>0||includeSpecials)&&released(i)){complete=true;JSONObject current=data.item(i.optString("id"));if(!DataStore.watched(current==null?i:current)){complete=false;break;}}
                if(complete){JSONObject op=data.operation(saved(show).optString("id"),"status");DataStore.put(op,"status","Finished");data.queue(op);}
            }data.syncSoon();changed();
        }catch(Exception e){saveError();}});
    }
    void changed(){runOnUiThread(()->{if(alive()){readItems();refresh(false);restoreFocus(focusKey);}});}
    void saveError(){runOnUiThread(()->{if(alive())Toast.makeText(this,t("Could not save changes."),Toast.LENGTH_LONG).show();});}
    void episodeMenu(JSONObject item){
        if(menu!=null)menu.dismiss();rememberFocus();Dialog dialog=new Dialog(this);menu=dialog;dialog.requestWindowFeature(Window.FEATURE_NO_TITLE);LinearLayout body=column();body.setPadding(dp(20),dp(18),dp(20),dp(18));body.setBackground(TvUi.bg(TvUi.PANEL,dp(16)));TextView label=TvUi.text(this,episode(item)+" · "+item.optString("episodeTitle",item.optString("title")),20,TvUi.WHITE,true);label.setMaxLines(2);body.addView(label,new LinearLayout.LayoutParams(dp(350),dp(60)));
        List<Runnable> runs=new ArrayList<>();List<String> labels=new ArrayList<>();labels.add(t("Play"));runs.add(()->play(item,false));if(streaming(item)){labels.add(t("Choose stream"));runs.add(()->playStream(item,true,false));}labels.add(t(DataStore.watched(item)?"Mark unwatched":"Mark watched"));runs.add(()->toggleWatched(item));labels.add(t("Start over"));runs.add(()->play(item,true));
        for(int n=0;n<labels.size();n++){Runnable run=runs.get(n);TextView button=TvUi.button(this,labels.get(n),n==0,()->{dialog.dismiss();run.run();});LinearLayout.LayoutParams lp=new LinearLayout.LayoutParams(-1,dp(46));lp.topMargin=dp(8);body.addView(button,lp);}
        dialog.setContentView(body);dialog.setOnDismissListener(d->{menu=null;if(alive())restoreFocus(focusKey);});dialog.show();Window w=dialog.getWindow();if(w!=null){w.setBackgroundDrawableResource(android.R.color.transparent);w.setLayout(dp(400),-2);}body.getChildAt(1).requestFocus();
    }

    // Read PhoneCredits' existing files directly: its load()/castStrip() can fetch metadata.
    Object cached(File directory,String key){try{File f=new File(directory,DiscoveryClient.hash(key)+".json");if(!f.isFile()||f.length()>2*1024*1024)return null;return new JSONTokener(new String(Files.readAllBytes(f.toPath()),StandardCharsets.UTF_8)).nextValue();}catch(Exception ignored){return null;}}
    void loadCachedCast(JSONObject item){local.execute(()->{
        String imdb=PhoneCredits.imdb(item);if(imdb.isEmpty())return;File credits=new File(data.context.getCacheDir(),"credits");String path="meta/"+(item.optBoolean("movie")?"movie":"series")+"/"+imdb+".json";
        Object root=cached(credits,"https://v3-cinemeta.strem.io/"+path);if(!(root instanceof JSONObject))root=cached(new File(data.context.getFilesDir(),"discover"),path);if(!(root instanceof JSONObject))root=cached(new File(getFilesDir(),"discover"),path);
        JSONObject metadata=root instanceof JSONObject?((JSONObject)root).optJSONObject("meta"):null;JSONArray people=metadata==null?PhoneCredits.parseCinemeta(item):PhoneCredits.parseCinemeta(metadata);
        if(!item.optBoolean("movie")){Object lookup=cached(credits,"https://api.tvmaze.com/lookup/shows?imdb="+imdb);if(lookup instanceof JSONObject){Object rows=cached(credits,"https://api.tvmaze.com/shows/"+((JSONObject)lookup).optInt("id")+"/cast");if(rows instanceof JSONArray)PhoneCredits.merge(people,PhoneCredits.parseTvmaze((JSONArray)rows));}}
        List<JSONObject> result=DataStore.objects(people);runOnUiThread(()->{if(alive())renderCast(result);});
    });}
    void renderCast(List<JSONObject> people){
        if(currentFocusKey().startsWith("cast:"))return;cast.removeAllViews();castRail=null;cast.setVisibility(people.isEmpty()?View.GONE:View.VISIBLE);if(people.isEmpty()){linkVertical();return;}
        cast.addView(TvUi.text(this,t("Cast"),16,TvUi.WHITE,true),new LinearLayout.LayoutParams(-1,dp(24)));HorizontalScrollView scroll=railScroll();LinearLayout rail=new LinearLayout(this);castRail=rail;scroll.addView(rail);cast.addView(scroll,new LinearLayout.LayoutParams(-1,dp(84)));
        for(JSONObject person:people.subList(0,Math.min(people.size(),TvUi.ROW_LIMIT))){LinearLayout card=column();card.setGravity(Gravity.CENTER_HORIZONTAL);ImageView photo=new ImageView(this);photo.setScaleType(ImageView.ScaleType.CENTER_CROP);photo.setBackground(TvUi.bg(TvUi.PANEL,dp(100)));photo.setClipToOutline(true);card.addView(photo,new LinearLayout.LayoutParams(dp(46),dp(46)));TextView label=text(person.optString("name"),16,TvUi.WHITE);label.setSingleLine(true);label.setEllipsize(TextUtils.TruncateAt.END);label.setGravity(Gravity.CENTER);card.addView(label,new LinearLayout.LayoutParams(dp(124),dp(28)));card.setId(View.generateViewId());card.setTag("cast:"+person.optString("name"));card.setContentDescription(person.optString("name"));TvUi.focusable(card,Color.TRANSPARENT,dp(8));card.setClickable(false);LinearLayout.LayoutParams lp=new LinearLayout.LayoutParams(dp(128),dp(80));lp.setMargins(0,0,dp(12),0);rail.addView(card,lp);
            final String url=person.optString("photo");if(!url.isEmpty())local.execute(()->{try{File f=new File(new File(data.context.getFilesDir(),"discover"),DiscoveryClient.hash(url)+".jpg");if(!f.isFile())f=new File(new File(getFilesDir(),"discover"),DiscoveryClient.hash(url)+".jpg");final File source=f;if(source.isFile())runOnUiThread(()->{if(alive())TvUi.loadSource(photo,source,"",dp(46),dp(46));});}catch(Exception ignored){}});
        }linkHorizontal(rail);linkVertical();
    }
}
