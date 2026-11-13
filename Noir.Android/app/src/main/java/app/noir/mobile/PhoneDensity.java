package app.noir.mobile;
import android.content.Context;
import android.content.SharedPreferences;

// Phone size tokens. Compact (default) is the calmer, denser scale; Comfortable is the pre-0.17 scale exactly.
// Per device (not per profile, not synced). TV never uses these: TvUi keeps its own 10-foot sizes.
final class PhoneDensity {
 static volatile boolean compact=true;static boolean loaded;
 static {String[][] words={{"Size","الحجم"},{"Compact","مضغوط"},{"Comfortable","مريح"},{"Smaller text and cards, more on screen","نص وبطاقات أصغر، ومحتوى أكثر على الشاشة"},{"Larger text and cards","نص وبطاقات أكبر"}};for(String[] w:words)Loc.AR.put(w[0],w[1]);}
 static SharedPreferences prefs(Context c){return ProfileStore.base(c).getSharedPreferences("ui-density",0);}
 static void load(Context c){compact=!"comfortable".equals(prefs(c).getString("density",""));loaded=true;}
 static void set(Context c,boolean value){compact=value;loaded=true;prefs(c).edit().putString("density",value?"compact":"comfortable").apply();}
 static float pick(float compactValue,float comfortableValue){return compact?compactValue:comfortableValue;}
 // Type (sp)
 static float heading(){return pick(19,23);}
 static float paragraph(){return pick(13.5f,14);}
 static float railTitle(){return pick(17,20);}
 static float railSubtitle(){return pick(11.5f,12);}
 static float button(){return pick(13.5f,14);}
 static float chip(){return pick(13,14);}
 static float small(){return pick(12,12);}
 static float logo(){return pick(19,22);}
 static float cardTitle(){return pick(14,15);}
 static float heroTitle(boolean home){return home?pick(26,30):pick(24,28);}
 // Geometry (dp)
 static float pageSide(){return pick(16,22);}
 static float headerTop(){return pick(10,12);}
 static float headingTop(){return pick(18,24);}
 static float headingBottom(){return pick(10,12);}
 static float buttonHeight(){return pick(42,48);}
 static float buttonRadius(){return pick(11,12);}
 static float chipPadH(){return pick(14,18);}
 static float chipPadV(){return pick(8,11);}
 static float smallMin(){return pick(44,48);}
 static float navItem(){return pick(52,56);}
 static float railTop(){return pick(20,26);}
 static float railBottom(){return pick(10,12);}
 // Home tile size (Edit Home, U152): 1 on every other page. MainActivity.render sets it before a page is built.
 static volatile float cardScale=1;
 static float continueWidth(){return pick(232,264)*cardScale;}
 static float continueArt(){return pick(130,148)*cardScale;}
 static float posterWidth(){return pick(112,128)*cardScale;}
 static float posterArt(){return pick(168,192)*cardScale;}
 static float latestWidth(){return pick(240,270);}
 static float latestArt(){return pick(134,150);}
 static float hero(boolean home){return home?pick(340,420):pick(300,360);}
 static float cardGap(){return pick(12,14);}
 static float episodeThumbW(){return pick(112,124);}
 static float episodeThumbH(){return pick(63,70);}
 static float shelfWidth(){return pick(220,248);}
 static float shelfArt(){return pick(124,142);}
}
