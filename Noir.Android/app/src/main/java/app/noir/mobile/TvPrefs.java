package app.noir.mobile;
import android.content.*;
import java.util.Arrays;

/** TV-only playback settings, shared by the player (TV mode) and the TV settings screen. */
public final class TvPrefs {
    private TvPrefs(){}
    static SharedPreferences prefs(Context c){return c.getApplicationContext().getSharedPreferences("tv-prefs",Context.MODE_PRIVATE);}
    /** Send AC3/E-AC3/DTS untouched to the TV or soundbar (HDMI/optical). Off by default: not every TV handles it. */
    public static boolean passthrough(Context c){return prefs(c).getBoolean("passthrough",false);}
    public static void passthrough(Context c,boolean on){prefs(c).edit().putBoolean("passthrough",on).apply();}
    /** Switch the display to the video's frame rate (Android 11+). On by default. */
    public static boolean matchFrameRate(Context c){return prefs(c).getBoolean("matchFrameRate",true);}
    public static void matchFrameRate(Context c,boolean on){prefs(c).edit().putBoolean("matchFrameRate",on).apply();}
    /** Seek step for ←/→ in seconds (10 by default). */
    public static int seekStep(Context c){return prefs(c).getInt("seekStep",10);}
    public static void seekStep(Context c,int seconds){prefs(c).edit().putInt("seekStep",Math.max(5,Math.min(60,seconds))).apply();}
    /** Stream quality limit: "auto" (fit the TV and connection), "1080p", "2160p" (4K, no remux), "best" (allow remux). */
    public static final String[] QUALITIES={"auto","1080p","2160p","best"};
    public static String quality(Context c){String q=prefs(c).getString("quality","auto");return Arrays.asList(QUALITIES).contains(q)?q:"auto";}
    public static void quality(Context c,String q){prefs(c).edit().putString("quality",Arrays.asList(QUALITIES).contains(q)?q:"auto").apply();}
    /** Internet download speed in Mbit/s from the TV check or from real streams (0 = not measured yet). Newer measurements are blended in so one slow moment doesn't stick. */
    public static float mbps(Context c){return prefs(c).getFloat("mbps",0f);}
    public static long mbpsAt(Context c){return prefs(c).getLong("mbpsAt",0L);}
    public static void measured(Context c,float mbps,boolean replace){if(!(mbps>0)||mbps>10000)return;float old=mbps(c);float value=replace||old<=0?mbps:old*0.6f+mbps*0.4f;prefs(c).edit().putFloat("mbps",value).putLong("mbpsAt",System.currentTimeMillis()).apply();}
}
