package app.noir.mobile;
import android.app.*;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.os.*;
import android.text.*;
import android.view.*;
import android.widget.*;
import org.json.*;
import java.time.*;
import java.util.*;
import java.util.concurrent.*;

// Log anything you watched, anywhere, without downloading: search, pick, choose when, check it off.
// Works offline from the phone; the PC learns about new titles on the next sync.
public final class LogActivity extends Activity {
    final ExecutorService requests=Executors.newFixedThreadPool(2),images=Executors.newFixedThreadPool(2);
    final Handler handler=new Handler(Looper.getMainLooper());Future<?> current;
    DiscoveryClient client;DataStore data;LinearLayout body,results;TextView feedback;
    String filter="All",query="",when="Just now";JSONArray titles=new JSONArray();JSONObject title;List<JSONObject> saved=new ArrayList<>(),known=new ArrayList<>();int revision=0,season=1;long pickedDay=0;
    final Runnable debounce=this::find;
    @Override public void onCreate(Bundle state){super.onCreate(state);client=new DiscoveryClient(this);data=NoirApp.data;query=getIntent().getStringExtra("query");if(query==null)query="";browse();String sharedId=getIntent().getStringExtra("imdb");if(sharedId!=null&&sharedId.matches("tt[0-9]+"))openShared(sharedId);else find();}
    int dp(int v){return Math.round(v*getResources().getDisplayMetrics().density);}
    TextView text(String value,int size,int color){TextView t=new TextView(this);t.setText(Loc.t(value));t.setTextSize(size);t.setTextColor(color);t.setPadding(0,dp(4),0,dp(6));return t;}
    TextView chip(String label,Runnable act,boolean on){TextView t=text(label,14,on?MainActivity.BLACK:MainActivity.WHITE);t.setTypeface(null,on?Typeface.BOLD:Typeface.NORMAL);t.setSingleLine();t.setGravity(Gravity.CENTER);t.setPadding(dp(16),dp(10),dp(16),dp(10));t.setBackground(MainActivity.bg(on?MainActivity.GOLD:Color.argb(60,255,255,255),dp(22)));t.setOnClickListener(v->act.run());Motion.card(t);LinearLayout.LayoutParams p=new LinearLayout.LayoutParams(-2,-2);p.setMargins(0,dp(4),dp(8),dp(4));t.setLayoutParams(p);return t;}
    HorizontalScrollView row(LinearLayout content){HorizontalScrollView s=new HorizontalScrollView(this);s.setHorizontalScrollBarEnabled(false);s.addView(content);return s;}
    Button button(String value,Runnable action,boolean primary){Button b=new Button(this);b.setText(Loc.t(value));b.setAllCaps(false);b.setTextColor(primary?MainActivity.BLACK:MainActivity.WHITE);b.setBackground(MainActivity.bg(primary?MainActivity.GOLD:MainActivity.PANEL,dp(12)));b.setMinHeight(dp(48));b.setOnClickListener(v->action.run());Motion.card(b);LinearLayout.LayoutParams l=new LinearLayout.LayoutParams(-1,-2);l.setMargins(0,dp(6),0,dp(6));b.setLayoutParams(l);return b;}
    void page(String name){revision++;LinearLayout root=new LinearLayout(this);root.setOrientation(LinearLayout.VERTICAL);root.setFitsSystemWindows(true);root.setBackgroundColor(MainActivity.BLACK);root.setPadding(dp(20),dp(10),dp(20),dp(10));setContentView(root);
        LinearLayout header=new LinearLayout(this);header.setGravity(Gravity.CENTER_VERTICAL);Button back=button("‹",this::back,false);back.setContentDescription("Back");header.addView(back,new LinearLayout.LayoutParams(dp(48),dp(48)));TextView heading=text(name,22,MainActivity.WHITE);heading.setTypeface(null,Typeface.BOLD);heading.setSingleLine();heading.setEllipsize(TextUtils.TruncateAt.END);heading.setPadding(dp(14),0,0,0);header.addView(heading,new LinearLayout.LayoutParams(0,-2,1));root.addView(header);
        ScrollView scroll=new ScrollView(this);scroll.setFillViewport(true);body=new LinearLayout(this);body.setOrientation(LinearLayout.VERTICAL);scroll.addView(body);root.addView(scroll,new LinearLayout.LayoutParams(-1,0,1));feedback=text("",12,MainActivity.MUTED);root.addView(feedback);}

    void browse(){title=null;page("Log what you watched");EditText search=new EditText(this);search.setSingleLine(true);search.setTextColor(MainActivity.WHITE);search.setHintTextColor(MainActivity.MUTED);search.setHint("Search movies & shows you've watched");search.setText(query);body.addView(search);
        search.addTextChangedListener(new TextWatcher(){public void beforeTextChanged(CharSequence s,int a,int b,int c){}public void afterTextChanged(Editable e){}public void onTextChanged(CharSequence v,int a,int b,int c){query=v.toString();handler.removeCallbacks(debounce);handler.postDelayed(debounce,350);}});
        LinearLayout filters=new LinearLayout(this);for(String label:new String[]{"All","Movies","TV shows"})filters.addView(chip(label,()->{filter=label;browse();find();},filter.equals(label)));body.addView(row(filters));
        body.addView(text("Watched on another service, at the cinema or years ago? Pick it, choose when, and check it off. Nothing downloads.",13,MainActivity.MUTED));
        results=new LinearLayout(this);results.setOrientation(LinearLayout.VERTICAL);body.addView(results);if(titles.length()>0)renderResults();}
    void find(){if(title!=null)return;handler.removeCallbacks(debounce);if(current!=null)current.cancel(true);final int view=++revision;final String value=query,kind=filter;feedback.setText(value.isEmpty()?"Popular movies & shows…":"Searching…");
        current=requests.submit(()->{try{JSONArray found=client.search(value,kind);runOnUiThread(()->{if(isFinishing()||view!=revision)return;titles=found;feedback.setText(found.length()+" titles · Cinemeta");renderResults();});}catch(Exception e){runOnUiThread(()->{if(isFinishing()||view!=revision)return;feedback.setText("Couldn't search. Check your internet connection.");results.removeAllViews();results.addView(button("Retry",this::find,false));});}});}
    void renderResults(){results.removeAllViews();loggedImdb.clear();for(JSONObject i:DataStore.objects(data.items()))if(DataStore.watched(i))loggedImdb.add(i.optString("imdb"));if(titles.length()==0){results.addView(text("No matches. Try another title.",15,MainActivity.MUTED));return;}
        for(int i=0;i<Math.min(40,titles.length());i+=2){LinearLayout line=new LinearLayout(this);for(int j=i;j<Math.min(i+2,titles.length());j++){JSONObject value=titles.optJSONObject(j);LinearLayout card=new LinearLayout(this);card.setOrientation(LinearLayout.VERTICAL);LinearLayout.LayoutParams l=new LinearLayout.LayoutParams(0,-2,1);l.setMargins(0,0,dp(8),dp(16));line.addView(card,l);
            FrameLayout art=new FrameLayout(this);art.addView(image(value.optString("poster")),new FrameLayout.LayoutParams(-1,-1));if(logged(value)){TextView tick=text("✓ Logged",11,MainActivity.BLACK);tick.setTypeface(null,Typeface.BOLD);tick.setPadding(dp(8),dp(2),dp(8),dp(2));tick.setBackground(MainActivity.bg(MainActivity.GOLD,dp(8)));FrameLayout.LayoutParams tp=new FrameLayout.LayoutParams(-2,-2,Gravity.TOP|Gravity.END);tp.setMargins(0,dp(8),dp(8),0);art.addView(tick,tp);}card.addView(art,new LinearLayout.LayoutParams(-1,dp(225)));
            card.addView(text(value.optString("name"),15,MainActivity.WHITE));card.addView(text((value.optString("noirType").equals("movie")?"Movie":"TV show")+" · "+value.optString("releaseInfo"),12,MainActivity.MUTED));card.setContentDescription("Log "+value.optString("name"));card.setOnClickListener(v->open(value));Motion.card(card);}
            if(line.getChildCount()==1)line.addView(new TextView(this),new LinearLayout.LayoutParams(0,-2,1));results.addView(line);}}
    final Set<String> loggedImdb=new HashSet<>();
    boolean logged(JSONObject value){return loggedImdb.contains(value.optString("id"));}
    ImageView image(String url){ImageView v=new ImageView(this);v.setScaleType(ImageView.ScaleType.CENTER_CROP);v.setBackground(MainActivity.bg(Motion.placeholder(url),dp(10)));v.setClipToOutline(true);final int view=revision;images.execute(()->{try{android.graphics.Bitmap b=client.art(url);if(b!=null)Motion.remember(url,b);if(b!=null)runOnUiThread(()->{if(!isFinishing()&&view==revision)Motion.loaded(v,b,url);});}catch(Exception ignored){}});return v;}

    void openShared(String id){int view=revision;feedback.setText(Loc.t("Opening shared title…"));current=requests.submit(()->{JSONObject found=null;for(String kind:new String[]{"movie","series"})try{JSONObject seed=new JSONObject();DataStore.put(seed,"id",id);DataStore.put(seed,"noirType",kind);found=client.details(seed);if(found!=null&&!found.optString("name").isEmpty())break;}catch(Exception ignored){}JSONObject result=found;runOnUiThread(()->{if(isFinishing()||isDestroyed()||view!=revision)return;if(result!=null&&!result.optString("name").isEmpty())open(result);else{query=id;browse();find();}});});}
    void open(JSONObject seed){handler.removeCallbacks(debounce);if(current!=null)current.cancel(true);title=seed;page(seed.optString("name"));body.addView(text("Opening…",15,MainActivity.MUTED));int view=revision;
        current=requests.submit(()->{try{JSONObject details=client.details(seed);runOnUiThread(()->{if(isFinishing()||view!=revision)return;title=details;try{saved=new ArrayList<>();for(JSONObject v:PhoneSeasonPlan.discoverItems(details))saved.add(v);known=DataStore.objects(data.items());}catch(Exception e){feedback.setText("This title can't be logged: "+e.getMessage());}
            long oldest=Long.MAX_VALUE;for(JSONObject i:saved){long t=airs(i);if(t>0)oldest=Math.min(oldest,t);}boolean any=false;for(JSONObject i:saved)if(watched(i))any=true;
            when=getIntent().getBooleanExtra("shareToLog",false)?"Just now":!any&&oldest<System.currentTimeMillis()-365L*86400000?"":"Just now";
            int s=Integer.MAX_VALUE;for(JSONObject i:saved)if(i.optInt("season")>0&&!watched(i))s=Math.min(s,i.optInt("season"));season=s==Integer.MAX_VALUE?1:s;details();});}
            catch(Exception e){runOnUiThread(()->{if(isFinishing()||view!=revision)return;feedback.setText("Details unavailable. Check your internet connection.");body.addView(button("Retry",()->open(seed),false));});}});}
    // The phone's or PC's copy of this movie/episode, if it already exists.
    JSONObject existing(JSONObject value){for(JSONObject i:known)if(i.optString("id").equals(value.optString("id"))||!value.optString("imdb").isEmpty()&&i.optString("imdb").equals(value.optString("imdb"))&&i.optBoolean("movie")==value.optBoolean("movie")&&(i.optBoolean("movie")||i.optInt("season")==value.optInt("season")&&i.optInt("episode")==value.optInt("episode")))return i;return null;}
    boolean watched(JSONObject value){JSONObject i=existing(value);return i!=null&&DataStore.watched(i);}
    static long airs(JSONObject i){String at=i.optString("airsAt");if(at.isEmpty()||at.equals("null"))return 0;long t=DataStore.date(at);if(t==0)try{t=LocalDate.parse(at.substring(0,10)).atStartOfDay(ZoneOffset.UTC).toInstant().toEpochMilli();}catch(Exception ignored){}return t;}
    static boolean released(JSONObject i){long t=airs(i);return t==0||t<=System.currentTimeMillis();}
    void details(){
        boolean movie=title.optString("noirType").equals("movie");known=DataStore.objects(data.items());page(title.optString("name"));
        FrameLayout hero=new FrameLayout(this);hero.addView(image(title.optString("background",title.optString("poster"))),new FrameLayout.LayoutParams(-1,-1));hero.setClipToOutline(true);hero.setBackground(MainActivity.bg(MainActivity.PANEL,dp(16)));body.addView(hero,new LinearLayout.LayoutParams(-1,dp(170)));
        body.addView(text((movie?"Movie":"TV show")+" · "+title.optString("releaseInfo")+(title.optString("imdbRating").isEmpty()?"":" · IMDb "+title.optString("imdbRating")),13,MainActivity.GOLD));
        TextView about=text(title.optString("description"),14,MainActivity.MUTED);about.setMaxLines(3);about.setEllipsize(TextUtils.TruncateAt.END);about.setOnClickListener(v->about.setMaxLines(99));body.addView(about);
        JSONObject first=saved.isEmpty()?null:saved.get(0);boolean listed=first!=null&&existing(first)!=null&&existing(first).optBoolean("listed");
        if(first!=null){Button list=button(listed?"✓ My List":"+ My List",()->{},false);list.setOnClickListener(v->{JSONObject item=data.rememberPhoneItem(first);boolean on=item.optBoolean("listed");JSONObject op=data.operation(item.optString("id"),"list");DataStore.put(op,"value",!on);data.queue(op);data.syncSoon();list.setText(Loc.t(!on?"✓ My List":"+ My List"));Motion.pop(list);Motion.haptic(list);});body.addView(list);}
        TextView ask=text("When did you watch it?",16,MainActivity.WHITE);ask.setTypeface(null,Typeface.BOLD);ask.setPadding(0,dp(14),0,dp(2));body.addView(ask);
        LinearLayout whens=new LinearLayout(this);for(String w:movie?new String[]{"Just now","Pick a date","Don't remember"}:new String[]{"Just now","When it aired","Pick a date","Don't remember"})whens.addView(chip(w.equals("Pick a date")&&when.equals(w)&&pickedDay>0?LocalDate.ofEpochDay(pickedDay).format(java.time.format.DateTimeFormatter.ofPattern("d MMM yyyy")):w,()->{if(w.equals("Pick a date"))pickDate();else{when=w;details();}},when.equals(w)));body.addView(row(whens));
        if(movie){if(first==null)return;JSONObject known=existing(first);boolean seen=known!=null&&DataStore.watched(known);
            if(seen){int n=DataStore.objects(known.optJSONArray("viewings")).size();body.addView(text("✓ Watched"+(n>1?" "+n+" times":""),15,MainActivity.GOLD));body.addView(button("↻ Log a rewatch",()->{data.rewatchNext=true;log(List.of(first),"Rewatch logged");},true));body.addView(button("Remove from my history",()->{data.unview(known.optString("id"),"");data.syncSoon();toast("Removed from your history");details();},false));}
            else body.addView(button("✓ Mark watched",()->logAsk(List.of(first),title.optString("name")+" logged"),true));
            feedback.setText("Information & artwork: Cinemeta");return;}
        List<Integer> seasons=new ArrayList<>();for(JSONObject i:saved)if(i.optInt("season")>0&&!seasons.contains(i.optInt("season")))seasons.add(i.optInt("season"));if(seasons.isEmpty()){body.addView(text("No episode list is available yet.",15,MainActivity.MUTED));return;}if(!seasons.contains(season))season=seasons.get(0);
        LinearLayout tabs=new LinearLayout(this);for(int s:seasons){List<JSONObject> eps=episodes(s);boolean all=!eps.isEmpty()&&eps.stream().filter(LogActivity::released).allMatch(this::watched)&&eps.stream().anyMatch(LogActivity::released);tabs.addView(chip("Season "+s+(all?" ✓":""),()->{season=s;details();},s==season));}body.addView(row(tabs));
        List<JSONObject> eps=episodes(season),out=new ArrayList<>();for(JSONObject e:eps)if(released(e))out.add(e);long seen=out.stream().filter(this::watched).count();
        body.addView(text(seen+" of "+out.size()+" released episodes watched · long-press an episode to check everything up to it",12,MainActivity.MUTED));
        if(seen<out.size())body.addView(button("✓ Mark season "+season+" watched",()->{List<JSONObject> todo=new ArrayList<>();for(JSONObject e:out)if(!watched(e))todo.add(e);logAsk(todo,"Season "+season+" logged");},true));
        List<JSONObject> whole=new ArrayList<>();for(JSONObject e:saved)if(e.optInt("season")>0&&released(e)&&!watched(e))whole.add(e);
        if(whole.size()>out.size()-seen)body.addView(button("✓ Mark whole show watched ("+whole.size()+")",()->logAsk(whole,title.optString("name")+": whole show logged"),false));
        else if(!out.isEmpty())body.addView(button("Unmark season "+season,()->{for(JSONObject e:out){JSONObject k=existing(e);if(k!=null)data.unview(k.optString("id"),"");}data.syncSoon();toast("Season "+season+" unmarked");details();},false));
        for(JSONObject e:eps)body.addView(episodeRow(e,out));
        feedback.setText("Information & artwork: Cinemeta");
    }
    List<JSONObject> episodes(int s){List<JSONObject> list=new ArrayList<>();for(JSONObject i:saved)if(i.optInt("season")==s&&i.optInt("episode")>0)list.add(i);list.sort(Comparator.comparingInt(i->i.optInt("episode")));return list;}
    View episodeRow(JSONObject e,List<JSONObject> out){
        boolean on=watched(e),aired=released(e);LinearLayout row=new LinearLayout(this);row.setGravity(Gravity.CENTER_VERTICAL);row.setPadding(dp(12),dp(8),dp(8),dp(8));row.setBackground(MainActivity.bg(MainActivity.PANEL,dp(12)));LinearLayout.LayoutParams l=new LinearLayout.LayoutParams(-1,-2);l.setMargins(0,dp(4),0,dp(4));row.setLayoutParams(l);
        LinearLayout info=new LinearLayout(this);info.setOrientation(LinearLayout.VERTICAL);row.addView(info,new LinearLayout.LayoutParams(0,-2,1));
        boolean spoiler=NoirApp.data.settings().optBoolean("hideSpoilers",true)&&!on&&aired&&!e.optString("episodeTitle").matches("(?i)(episode|chapter)\\s*\\d+");
        TextView name=text(String.format(Locale.ROOT,"E%02d",e.optInt("episode"))+(spoiler||e.optString("episodeTitle").isEmpty()?"":"  "+e.optString("episodeTitle")),15,MainActivity.WHITE);name.setSingleLine();name.setEllipsize(TextUtils.TruncateAt.END);info.addView(name);
        long t=airs(e);info.addView(text(!aired?"Airs "+NoirUi.date(t):t>0?"Aired "+Instant.ofEpochMilli(t).atZone(ZoneId.systemDefault()).toLocalDate().format(java.time.format.DateTimeFormatter.ofPattern("d MMM yyyy")):"",11,aired?MainActivity.MUTED:MainActivity.GOLD));
        if(aired){TextView check=text("✓",18,on?MainActivity.BLACK:MainActivity.MUTED);check.setGravity(Gravity.CENTER);check.setTypeface(null,Typeface.BOLD);GradientDrawable ring=new GradientDrawable();ring.setShape(GradientDrawable.OVAL);ring.setColor(on?MainActivity.GOLD:Color.TRANSPARENT);ring.setStroke(dp(2),on?MainActivity.GOLD:Color.rgb(107,102,94));check.setBackground(ring);check.setContentDescription((on?"Unmark ":"Mark ")+"episode "+e.optInt("episode"));row.addView(check,new LinearLayout.LayoutParams(dp(42),dp(42)));
            Runnable toggle=()->{if(on){JSONObject k=existing(e);if(k!=null)watchedChoice(k,e);}else checkWithEarlier(e);};check.setOnClickListener(v->toggle.run());row.setOnClickListener(v->toggle.run());
            row.setOnLongClickListener(v->{List<JSONObject> todo=new ArrayList<>();for(JSONObject x:saved)if(x.optInt("season")>0&&released(x)&&!watched(x)&&(x.optInt("season")<e.optInt("season")||x.optInt("season")==e.optInt("season")&&x.optInt("episode")<=e.optInt("episode")))todo.add(x);if(!todo.isEmpty())new AlertDialog.Builder(this).setTitle("Mark everything up to here?").setMessage(todo.size()+" unwatched episode"+(todo.size()==1?"":"s")+" up to S"+e.optInt("season")+"E"+e.optInt("episode")+" will be checked off.").setNegativeButton("Cancel",null).setPositiveButton("Check off",(d,w)->log(todo,todo.size()+" episodes logged")).show();return true;});}
        return row;
    }
    // Earlier released, unwatched episodes are offered with the tapped one, as everywhere else.
    void checkWithEarlier(JSONObject e){
        List<JSONObject> before=new ArrayList<>();for(JSONObject x:saved)if(x.optInt("season")>0&&released(x)&&!watched(x)&&PhoneSmart.before(x,e))before.add(x);
        String one=String.format(Locale.ROOT,"S%02dE%02d logged",e.optInt("season"),e.optInt("episode"));
        if(before.isEmpty()){logAsk(List.of(e),one);return;}
        new AlertDialog.Builder(this).setTitle("Did you also watch the "+before.size()+" earlier episode"+(before.size()==1?"":"s")+"?").setMessage(PhoneSmart.range(before)+" "+(before.size()==1?"isn't":"aren't")+" checked off yet.")
            .setNegativeButton("Just "+PhoneSmart.label(e),(d,w)->logAsk(List.of(e),one)).setPositiveButton("Yes, all "+(before.size()+1),(d,w)->{List<JSONObject> all=new ArrayList<>(before);all.add(e);logAsk(all,all.size()+" episodes logged");}).show();
    }
    // No "when" chosen (an old title): ask first, nothing preselected.
    void logAsk(List<JSONObject> items,String message){
        if(!when.isEmpty()){log(items,message);return;}
        new AlertDialog.Builder(this).setTitle("When did you watch "+(items.size()==1?"it":"these "+items.size())+"?").setItems(new String[]{"Just now","When it aired","Pick a date","Don't remember"},(d,n)->{
            if(n==2){LocalDate today=LocalDate.now();DatePickerDialog p=new DatePickerDialog(this,(v,y,m,day)->{pickedDay=LocalDate.of(y,m+1,day).toEpochDay();when="Pick a date";log(items,message);},today.getYear(),today.getMonthValue()-1,today.getDayOfMonth());p.getDatePicker().setMaxDate(System.currentTimeMillis());p.show();}
            else{when=new String[]{"Just now","When it aired","Pick a date","Don't remember"}[n];log(items,message);}}).show();
    }
    void watchedChoice(JSONObject known,JSONObject e){
        new AlertDialog.Builder(this).setTitle(PhoneSmart.label(e)+" is in your history").setPositiveButton("↻ Log a rewatch",(d,w)->{data.rewatch(known.optString("id"));data.syncSoon();toast("Rewatch logged");details();})
            .setNegativeButton("Remove from history",(d,w)->{List<JSONObject> after=new ArrayList<>();for(JSONObject x:saved)if(x.optInt("season")>0&&watched(x)&&PhoneSmart.before(e,x))after.add(x);
                Runnable one=()->{data.unview(known.optString("id"),"");data.syncSoon();details();};
                if(after.isEmpty()){one.run();return;}
                new AlertDialog.Builder(this).setTitle("Also unmark the "+after.size()+" later episode"+(after.size()==1?"":"s")+"?").setNegativeButton("Just "+PhoneSmart.label(e),(dd,ww)->one.run()).setPositiveButton("Unmark all "+(after.size()+1),(dd,ww)->{data.unview(known.optString("id"),"");for(JSONObject x:after){JSONObject k=existing(x);if(k!=null)data.unview(k.optString("id"),"");}data.syncSoon();details();}).show();})
            .setNeutralButton("Cancel",null).show();
    }
    void pickDate(){LocalDate start=pickedDay>0?LocalDate.ofEpochDay(pickedDay):LocalDate.now();DatePickerDialog dialog=new DatePickerDialog(this,(v,y,m,d)->{pickedDay=LocalDate.of(y,m+1,d).toEpochDay();when="Pick a date";details();},start.getYear(),start.getMonthValue()-1,start.getDayOfMonth());dialog.getDatePicker().setMaxDate(System.currentTimeMillis());dialog.show();}
    // Every episode of the title is remembered so the tracker knows what's next and what's coming.
    void log(List<JSONObject> chosen,String message){
        PhoneRewards.Stats before=PhoneRewardCard.stats(data);data.refreshPhoneTitle(saved);known=DataStore.objects(data.items());long now=System.currentTimeMillis();int n=0;
        for(JSONObject v:chosen){JSONObject item=existing(v);if(item==null)item=data.rememberPhoneItem(v);long at=0;boolean unknown=false;
            switch(when){case "When it aired":{long t=airs(v);if(t>0&&t<=now)at=t;else unknown=true;break;}case "Pick a date":at=pickedDay>0?Math.max(LocalDate.ofEpochDay(pickedDay).atTime(20,0).atZone(ZoneId.systemDefault()).toInstant().toEpochMilli(),airs(v)):0;break;case "Don't remember":unknown=true;break;default:at=now-(chosen.size()-1-n)*1000L;}
            data.approximate=when.equals("When it aired")&&!unknown;data.mark(item.optString("id"),true,at,unknown);n++;}
        data.syncSoon();toast("✓ "+message);Motion.haptic(body);PhoneRewardCard.celebrate(this,data,before);if(!chosen.isEmpty()&&!chosen.get(0).optBoolean("movie")){known=DataStore.objects(data.items());Set<Integer> seasons=new HashSet<>();for(JSONObject e:chosen)seasons.add(e.optInt("season"));for(int season:seasons){boolean complete=season>0;for(JSONObject e:saved)if(e.optInt("season")==season&&released(e)){JSONObject fresh=existing(e);if(fresh==null||!DataStore.watched(fresh))complete=false;}if(complete){toast(Loc.t("Season complete"));break;}}}details();if(!chosen.isEmpty()){JSONObject last=existing(chosen.get(chosen.size()-1));if(last!=null)PhoneTracker.reactionRow(this,data,body,last);}
    }
    void toast(String s){Toast.makeText(this,s,Toast.LENGTH_SHORT).show();}
    void back(){handler.removeCallbacks(debounce);if(current!=null)current.cancel(true);if(title!=null){browse();renderResults();}else finish();}
    @Override public void onBackPressed(){back();}
    @Override public void onDestroy(){revision++;handler.removeCallbacksAndMessages(null);requests.shutdownNow();images.shutdownNow();super.onDestroy();}
}
