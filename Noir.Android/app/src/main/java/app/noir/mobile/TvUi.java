package app.noir.mobile;
import android.app.*;
import android.content.*;
import android.content.res.Configuration;
import android.graphics.*;
import android.graphics.drawable.GradientDrawable;
import android.util.LruCache;
import android.view.*;
import android.widget.*;
import org.json.*;
import java.io.File;
import java.lang.ref.WeakReference;
import java.util.*;
import android.os.*;
import java.util.concurrent.*;

/** Shared building blocks for every TV screen: colors, sizes, focusable tiles and buttons, and an image loader that decodes at tile size. */
public final class TvUi {
    private TvUi(){}
    // Player labels for its TV-only branch (PlayerPrefs.t falls back to Loc).
    static {Loc.AR.put("Audio & subtitles","الصوت والترجمة");Loc.AR.put("▲ Audio & subtitles  ·  ▼ Chapters  ·  With controls hidden, ◀ ▶ seek (hold for faster)","▲ الصوت والترجمة  ·  ▼ الفصول  ·  عند إخفاء عناصر التحكم، ◀ ▶ للتقديم والإرجاع (اضغط مطولًا للإسراع)");}
    public static final int BLACK=Color.rgb(12,12,14),PANEL=Color.rgb(27,27,30),RAISED=Color.rgb(40,40,44),GOLD=Color.rgb(228,192,132),WHITE=Color.rgb(244,241,235),MUTED=Color.rgb(160,157,151);
    /** Tile sizes in dp. Posters are 2:3, landscape tiles 16:9. */
    public static final int POSTER_W=120,POSTER_H=180,WIDE_W=200,WIDE_H=112;
    static final float FOCUS_SCALE=1.08f;static final int FOCUS_MS=120;
    static final ExecutorService IMAGES=Executors.newFixedThreadPool(2);
    static final Handler IMAGE_UI=new Handler(Looper.getMainLooper());
    static final LruCache<String,Bitmap> CACHE=new LruCache<String,Bitmap>(4*1024*1024){protected int sizeOf(String k,Bitmap b){return b.getAllocationByteCount();}};
    static final Map<String,ImageJob> IMAGE_JOBS=new HashMap<>();static final ArrayDeque<ImageJob> IMAGE_QUEUE=new ArrayDeque<>();
    static final Map<View,ImageWatch> IMAGE_WATCHES=new WeakHashMap<>();static int runningImages;static boolean imagesReady;
    static void initImages(Context context){
        if(imagesReady)return;imagesReady=true;Context app=context.getApplicationContext();ActivityManager manager=(ActivityManager)app.getSystemService(Context.ACTIVITY_SERVICE);
        long budget=(manager==null?64:manager.getMemoryClass())*1024L*1024/8;CACHE.resize((int)Math.max(1024*1024,Math.min(32L*1024*1024,Math.min(budget,Runtime.getRuntime().maxMemory()/8))));
        if(app instanceof Application)((Application)app).registerActivityLifecycleCallbacks(new Application.ActivityLifecycleCallbacks(){
            public void onActivityDestroyed(Activity a){for(ImageWatch watch:new ArrayList<>(IMAGE_WATCHES.values()))for(WeakReference<ImageTarget> ref:new ArrayList<>(watch.targets)){ImageTarget t=ref.get();if(t!=null&&t.owner.get()==a)t.dispose();}}
            public void onActivityCreated(Activity a,Bundle b){}public void onActivityStarted(Activity a){}public void onActivityResumed(Activity a){}public void onActivityPaused(Activity a){}public void onActivityStopped(Activity a){}public void onActivitySaveInstanceState(Activity a,Bundle b){}
        });
    }
    static Activity imageActivity(Context context){while(context instanceof ContextWrapper){if(context instanceof Activity)return (Activity)context;Context next=((ContextWrapper)context).getBaseContext();if(next==context)break;context=next;}return null;}
    static final class ImageTarget implements View.OnAttachStateChangeListener {
        final WeakReference<ImageView> view;final WeakReference<Activity> owner;final boolean activityOwned;final String key,url;final File file;final int w,h;final Context app;final Rect rect=new Rect();ImageJob job;ImageWatch watch;boolean done;
        ImageTarget(ImageView v,File f,String u,int width,int height){view=new WeakReference<>(v);Activity a=imageActivity(v.getContext());owner=new WeakReference<>(a);activityOwned=a!=null;app=v.getContext().getApplicationContext();file=f;url=u;w=Math.max(1,width);h=Math.max(1,height);key=(f==null?u:f.getPath())+"@"+w+"x"+h;}
        boolean valid(){ImageView v=view.get();Activity a=owner.get();return !done&&v!=null&&v.getTag()==this&&(!activityOwned||a!=null&&!a.isDestroyed()&&!a.isFinishing());}
        boolean visible(){ImageView v=view.get();return valid()&&v.isAttachedToWindow()&&v.getWindowVisibility()==View.VISIBLE&&v.isShown()&&v.getGlobalVisibleRect(rect);}
        void attach(){ImageView v=view.get();if(!valid()||v==null)return;View root=v.getRootView();watch=IMAGE_WATCHES.get(root);if(watch==null){watch=new ImageWatch(root);IMAGE_WATCHES.put(root,watch);}watch.targets.add(new WeakReference<>(this));v.invalidate();}
        public void onViewAttachedToWindow(View v){attach();}
        public void onViewDetachedFromWindow(View v){cancelImage(this);if(watch!=null)watch.remove(this);}
        void dispose(){done=true;cancelImage(this);ImageView v=view.get();if(v!=null)v.removeOnAttachStateChangeListener(this);if(watch!=null)watch.remove(this);}
        void apply(Bitmap bitmap,boolean fade){if(!visible())return;ImageView v=view.get();done=true;v.removeOnAttachStateChangeListener(this);if(bitmap!=null){v.animate().cancel();v.setAlpha(fade?0f:1f);v.setImageBitmap(bitmap);if(fade)v.animate().alpha(1f).setDuration(150).start();}}
    }
    static final class ImageWatch implements ViewTreeObserver.OnPreDrawListener {
        final WeakReference<View> root;final List<WeakReference<ImageTarget>> targets=new ArrayList<>();
        ImageWatch(View view){root=new WeakReference<>(view);view.getViewTreeObserver().addOnPreDrawListener(this);}
        void remove(ImageTarget target){for(int i=targets.size()-1;i>=0;i--)if(targets.get(i).get()==null||targets.get(i).get()==target)targets.remove(i);target.watch=null;closeIfEmpty();}
        void closeIfEmpty(){if(!targets.isEmpty())return;View v=root.get();if(v!=null){if(v.getViewTreeObserver().isAlive())v.getViewTreeObserver().removeOnPreDrawListener(this);if(IMAGE_WATCHES.get(v)==this)IMAGE_WATCHES.remove(v);}}
        public boolean onPreDraw(){for(int i=targets.size()-1;i>=0;i--){ImageTarget t=targets.get(i).get();if(t!=null){if(!t.valid()){cancelImage(t);ImageView v=t.view.get();if(v!=null)v.removeOnAttachStateChangeListener(t);}else if(t.visible())requestImage(t);else cancelImage(t);}if(t==null||!t.valid()){targets.remove(i);if(t!=null)t.watch=null;}}closeIfEmpty();return true;}
    }
    static void requestImage(ImageTarget target){
        if(target.job!=null)return;Bitmap hit=CACHE.get(target.key);if(hit!=null){target.apply(hit,false);return;}ImageJob job=IMAGE_JOBS.get(target.key);
        if(job==null){if(runningImages>=2&&IMAGE_QUEUE.size()>=24)return;job=new ImageJob(target);IMAGE_JOBS.put(job.key,job);IMAGE_QUEUE.addFirst(job);}job.targets.add(target);target.job=job;pumpImages();
    }
    static void cancelImage(ImageTarget target){ImageJob job=target.job;if(job==null)return;target.job=null;job.targets.remove(target);if(!job.targets.isEmpty())return;job.cancelled=true;if(IMAGE_JOBS.get(job.key)==job)IMAGE_JOBS.remove(job.key);IMAGE_QUEUE.remove(job);Thread thread=job.thread;if(thread!=null)thread.interrupt();}
    static void pumpImages(){while(runningImages<2&&!IMAGE_QUEUE.isEmpty()){ImageJob job=IMAGE_QUEUE.removeFirst();if(job.cancelled)continue;runningImages++;IMAGES.execute(job);}}
    static final class ImageJob implements Runnable {
        final String key,url;final File file;final int w,h;final Context app;final List<ImageTarget> targets=new ArrayList<>();volatile boolean cancelled;volatile Thread thread;
        ImageJob(ImageTarget t){key=t.key;url=t.url;file=t.file;w=t.w;h=t.h;app=t.app;}
        public void run(){Bitmap result=null;thread=Thread.currentThread();try{if(cancelled)return;result=CACHE.get(key);if(result==null){File source=file;if(source==null){DiscoveryClient d=new DiscoveryClient(app);source=new File(d.directory,DiscoveryClient.hash(url)+".jpg");if(!source.exists()){byte[] bytes=DiscoveryClient.fetch(url,12*1024*1024);if(cancelled)return;DiscoveryClient.write(source,bytes);}}if(cancelled)return;result=decode(source,w,h);if(result!=null&&!cancelled)CACHE.put(key,result);}}catch(Exception ignored){}finally{thread=null;Thread.interrupted();Bitmap bitmap=result;IMAGE_UI.post(()->{runningImages--;if(IMAGE_JOBS.get(key)==this)IMAGE_JOBS.remove(key);for(ImageTarget t:targets){if(t.job!=this)continue;t.job=null;if(!cancelled)t.apply(bitmap,true);}targets.clear();pumpImages();});}}
    }

    public static boolean isTv(Context c){
        UiModeManager m=(UiModeManager)c.getSystemService(Context.UI_MODE_SERVICE);
        return m!=null&&m.getCurrentModeType()==Configuration.UI_MODE_TYPE_TELEVISION||c.getPackageManager().hasSystemFeature("android.software.leanback");
    }
    public static int dp(Context c,float v){return Math.round(v*c.getResources().getDisplayMetrics().density);}
    public static GradientDrawable bg(int color,int radiusPx){GradientDrawable g=new GradientDrawable();g.setColor(color);g.setCornerRadius(radiusPx);return g;}
    static GradientDrawable ring(Context c,int fill,int radius){GradientDrawable g=bg(fill,radius);g.setStroke(dp(c,3),GOLD);return g;}

    /** Full-screen black root with TV overscan-safe padding (48dp sides, 27dp top/bottom). */
    public static FrameLayout screen(Activity a){
        FrameLayout root=new FrameLayout(a);root.setBackgroundColor(BLACK);root.setPadding(dp(a,48),dp(a,27),dp(a,48),dp(a,27));root.setClipToPadding(false);root.setClipChildren(false);
        a.getWindow().setBackgroundDrawable(new android.graphics.drawable.ColorDrawable(BLACK));return root;
    }
    public static TextView text(Context c,String s,float sp,int color,boolean bold){
        TextView t=new TextView(c);t.setText(s);t.setTextSize(sp);t.setTextColor(color);if(bold)t.setTypeface(Typeface.DEFAULT_BOLD);t.setIncludeFontPadding(false);return t;
    }

    /** Makes any view a TV focus target: grows a little and gets a gold ring while focused; OK/Enter runs the click. */
    public static void focusable(View v,int fill,int radiusPx){
        Context c=v.getContext();v.setFocusable(true);v.setFocusableInTouchMode(false);v.setClickable(true);
        GradientDrawable idle=bg(fill,radiusPx),focused=ring(c,fill,radiusPx);v.setBackground(idle);
        v.setOnFocusChangeListener((view,has)->{view.setBackground(has?focused:idle);if(view.getWidth()==0&&has){view.post(()->grow(view,view.hasFocus()));return;}grow(view,has);});
    }
    /** Small things grow when focused; wide rows and buttons only get the ring, so they never spill past the screen edge. */
    static void grow(View view,boolean has){
        Context c=view.getContext();float s=!has||view.getWidth()>dp(c,360)?1f:FOCUS_SCALE;view.animate().scaleX(s).scaleY(s).translationZ(has?dp(c,6):0).setDuration(FOCUS_MS).start();
    }
    /** A focusable pill button. Primary = gold with dark text. */
    public static TextView button(Context c,String label,boolean primary,Runnable click){
        TextView b=text(c,label,16,primary?BLACK:WHITE,true);b.setGravity(Gravity.CENTER);b.setPadding(dp(c,22),dp(c,11),dp(c,22),dp(c,11));b.setMinWidth(dp(c,120));
        focusable(b,primary?GOLD:RAISED,dp(c,24));b.setOnClickListener(v->click.run());return b;
    }

    /** A focusable artwork tile with the title under it. `wide` uses backdrops/episode stills, otherwise posters. `sub` may be empty. */
    public static LinearLayout tile(Activity a,JSONObject item,boolean wide,String title,String sub,Runnable click){
        int w=dp(a,wide?WIDE_W:POSTER_W),h=dp(a,wide?WIDE_H:POSTER_H);
        LinearLayout box=new LinearLayout(a);box.setOrientation(LinearLayout.VERTICAL);box.setPadding(dp(a,4),dp(a,4),dp(a,4),dp(a,6));
        ImageView art=new ImageView(a);art.setScaleType(ImageView.ScaleType.CENTER_CROP);art.setClipToOutline(true);art.setBackground(bg(PANEL,dp(a,8)));
        box.addView(art,new LinearLayout.LayoutParams(w,h));
        TextView name=text(a,title,16,WHITE,true);name.setSingleLine(true);name.setEllipsize(android.text.TextUtils.TruncateAt.END);name.setPadding(0,dp(a,6),0,0);box.addView(name,new LinearLayout.LayoutParams(w,ViewGroup.LayoutParams.WRAP_CONTENT));
        if(sub!=null&&!sub.isEmpty()){TextView s=text(a,sub,14,MUTED,false);s.setSingleLine(true);s.setEllipsize(android.text.TextUtils.TruncateAt.END);s.setPadding(0,dp(a,3),0,0);box.addView(s,new LinearLayout.LayoutParams(w,ViewGroup.LayoutParams.WRAP_CONTENT));}
        focusable(box,Color.TRANSPARENT,dp(a,10));box.setContentDescription(title);box.setOnClickListener(v->click.run());
        load(art,item,wide,w,h);return box;
    }

    /** Loads an item's artwork into `view`, decoded close to w×h. Local art first, then the remote URL (disk-cached by DiscoveryClient). Safe to call again on a reused view. */
    public static void load(ImageView view,JSONObject item,boolean wide,int w,int h){
        DataStore data=NoirApp.data;if(data==null||item==null)return;
        File file=null;for(String name:wide?new String[]{item.optString("episodeImage"),item.optString("backdrop"),item.optString("poster")}:new String[]{item.optString("poster"),item.optString("backdrop")})if(!name.isEmpty()){File f=data.art(name);if(f.exists()){file=f;break;}}
        String url="";if(file==null){for(String key:wide?new String[]{"backdropUrl","posterUrl"}:new String[]{"posterUrl","backdropUrl"}){url=item.optString(key);if(url.isEmpty())url=data.phoneArt(item.optString("imdb"),key);if(!url.isEmpty())break;}}
        loadSource(view,file,url,w,h);
    }
    /** Loads a known local file or https URL into `view` at about w×h. */
    public static void loadSource(ImageView view,File file,String url,int w,int h){
        Object old=view.getTag();if(old instanceof ImageTarget)((ImageTarget)old).dispose();view.animate().cancel();view.setAlpha(1f);view.setImageDrawable(null);view.setTag(null);
        if(file==null&&(url==null||!url.startsWith("https://")))return;initImages(view.getContext());ImageTarget target=new ImageTarget(view,file,url,w,h);view.setTag(target);view.addOnAttachStateChangeListener(target);if(view.isAttachedToWindow())target.attach();
    }

    static Bitmap decode(File f,int w,int h){
        BitmapFactory.Options o=new BitmapFactory.Options();o.inJustDecodeBounds=true;BitmapFactory.decodeFile(f.getPath(),o);if(o.outWidth<=0)return null;
        int sample=1;while(o.outWidth/(sample*2)>=w&&o.outHeight/(sample*2)>=h)sample*=2;
        BitmapFactory.Options d=new BitmapFactory.Options();d.inSampleSize=sample;d.inPreferredConfig=Bitmap.Config.RGB_565;return BitmapFactory.decodeFile(f.getPath(),d);
    }

    /** A horizontal row: title label + a scroller holding `tiles`. Focus moving off either end stays in the row. */
    public static LinearLayout row(Activity a,String title,LinearLayout tiles){
        LinearLayout box=new LinearLayout(a);box.setOrientation(LinearLayout.VERTICAL);box.setClipChildren(false);box.setClipToPadding(false);
        TextView label=text(a,title,18,WHITE,true);label.setPadding(dp(a,4),dp(a,14),0,dp(a,6));box.addView(label);
        HorizontalScrollView scroller=new HorizontalScrollView(a);scroller.setHorizontalScrollBarEnabled(false);scroller.setClipChildren(false);scroller.setClipToPadding(false);scroller.setPadding(0,dp(a,8),dp(a,48),dp(a,8));
        tiles.setOrientation(LinearLayout.HORIZONTAL);tiles.setClipChildren(false);tiles.setClipToPadding(false);
        for(int i=0;i<tiles.getChildCount();i++){View t=tiles.getChildAt(i);t.setId(View.generateViewId());}
        int n=tiles.getChildCount();if(n>0){tiles.getChildAt(0).setNextFocusLeftId(View.NO_ID);tiles.getChildAt(n-1).setNextFocusRightId(tiles.getChildAt(n-1).getId());}
        scroller.addView(tiles);box.addView(scroller);return box;
    }
    /** Max tiles per row: rows are plain views, so keep them short. */
    public static final int ROW_LIMIT=30;
}
