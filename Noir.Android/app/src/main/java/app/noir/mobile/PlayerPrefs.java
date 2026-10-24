package app.noir.mobile;
import android.app.Activity;
import android.app.AlertDialog;
import java.util.*;

// Phone player choices: skip intro/recap/credits (manual, auto, off), "Still watching?" and default speed.
final class PlayerPrefs {
 static final String MANUAL="manual",AUTO="auto",OFF="off";
 static final String[] MODES={MANUAL,AUTO,OFF};
 static final int[] STILL={0,2,3,5};
 static final float[] SPEEDS={.5f,.75f,1f,1.25f,1.5f,1.75f,2f};
 static final Map<String,String> AR=new HashMap<>();
 static{String[][] w={{"Intros","المقدمات"},{"Recaps","الملخصات"},{"End credits","شارة النهاية"},{"Show button","زر التخطي"},{"Skip automatically","تخطٍّ تلقائي"},{"Off","إيقاف"},{"Ask “Still watching?”","اسأل «هل ما زلت تشاهد؟»"},{"Never","أبداً"},{"After 2 episodes","بعد حلقتين"},{"After 3 episodes","بعد ٣ حلقات"},{"After 5 episodes","بعد ٥ حلقات"},{"Default speed","السرعة الافتراضية"},{"Play the next episode automatically","شغّل الحلقة التالية تلقائياً"},{"Mark watched at credits","اعتبرها مُشاهدة عند الشارة"},{"Skip intro","تخطَّ المقدمة"},{"Skip recap","تخطَّ الملخص"},{"Skip credits","تخطَّ الشارة"},{"Skipping intro…","جارٍ تخطي المقدمة…"},{"Skipping recap…","جارٍ تخطي الملخص…"},{"Skipping credits…","جارٍ تخطي الشارة…"},{"Undo","تراجع"},{"UP NEXT","التالي"},{"Play now","شغّل الآن"},{"Cancel","إلغاء"},{"Playing in %d s","تبدأ خلال %d ث"},{"Still watching %s?","هل ما زلت تشاهد %s؟"},{"You've watched %d episodes in a row.","شاهدت %d حلقات متتالية."},{"Continue watching","متابعة المشاهدة"},{"Back to Noir","العودة إلى Noir"},{"Speed","السرعة"},{"Chapter %d","الفصل %d"},{"Normal","عادية"},{"Playback","التشغيل"},{"Done","تم"},{"Skip choices","خيارات التخطي"},{"Credits are playing","الشارة تعمل"}};for(String[] p:w)AR.put(p[0],p[1]);}
 static String t(String s){String v=Loc.arabic?AR.get(s):null;return v!=null?v:Loc.t(s);}
 static String f(String s,Object... v){return String.format(Locale.getDefault(),t(s),v);}
 static String mode(DataStore d,String kind){String v=d.prefs().optString("skip_"+kind,"");if(Arrays.asList(MODES).contains(v))return v;return kind.equals("intro")&&d.playbackFlag("autoSkipIntro",true)?AUTO:MANUAL;}
 static void mode(DataStore d,String kind,String value){d.pref("skip_"+kind,value);if(kind.equals("intro"))d.phoneSetting("autoSkipIntro",value.equals(AUTO));}
 static int stillAfter(DataStore d){int v=d.prefs().optInt("stillWatchingAfter",3);for(int s:STILL)if(s==v)return v;return 3;}
 static float clampSpeed(double v){if(Double.isNaN(v)||Double.isInfinite(v))return 1f;return (float)Math.max(.5,Math.min(2,Math.round(v*4)/4d));}
 static float defaultSpeed(DataStore d){return clampSpeed(d.prefs().optDouble("defaultSpeed",1));}
 static String speedLabel(float s){return (s==(int)s?String.valueOf((int)s):String.valueOf(s).replaceAll("0+$",""))+"×";}
 // Pure: chapter names that mean recap or credits.
 static String classify(String name){String n=name==null?"":name.trim();if(n.isEmpty())return "";if(n.matches("(?i)(OP|Intro|Opening|Intro credits|Opening credits|Opening theme)"))return "intro";if(n.matches("(?i)(Recap|Previously( on .*)?|Résumé|Resume|Last time.*)"))return "recap";if(n.matches("(?i)(ED|ED/Outro|Ending|Ending credits|End credits|Closing credits|Credits|Outro)"))return "credits";return "";}
 static boolean shouldAsk(int autoCount,int limit){return limit>0&&autoCount>=limit;}
 static void settings(Activity a,DataStore d){
  String[] kinds={"intro","recap","credits"},titles={"Intros","Recaps","End credits"};String[] items=new String[7];
  String[] modeNames={t("Show button"),t("Skip automatically"),t("Off")};
  for(int n=0;n<3;n++)items[n]=t(titles[n])+" · "+modeNames[Arrays.asList(MODES).indexOf(mode(d,kinds[n]))];
  int still=stillAfter(d);String[] stillLabels={t("Never"),t("After 2 episodes"),t("After 3 episodes"),t("After 5 episodes")};int stillAt=0;for(int s=0;s<STILL.length;s++)if(STILL[s]==still)stillAt=s;final int stillIndex=stillAt;items[3]=t("Ask “Still watching?”")+" · "+stillLabels[stillIndex];
  items[4]=t("Default speed")+" · "+speedLabel(defaultSpeed(d));
  items[5]=(d.playbackFlag("autoNext",true)?"✓ ":"○ ")+t("Play the next episode automatically");items[6]=(d.playbackFlag("markWatchedAtCredits",true)?"✓ ":"○ ")+t("Mark watched at credits");
  new AlertDialog.Builder(a).setTitle(t("Playback")).setItems(items,(dlg,n)->{
   if(n<3){int k=n;new AlertDialog.Builder(a).setTitle(t(titles[k])).setSingleChoiceItems(modeNames,Arrays.asList(MODES).indexOf(mode(d,kinds[k])),(x,m)->{mode(d,kinds[k],MODES[m]);x.dismiss();settings(a,d);}).show();}
   else if(n==3){int at=stillIndex;new AlertDialog.Builder(a).setTitle(t("Ask “Still watching?”")).setSingleChoiceItems(stillLabels,at,(x,m)->{d.pref("stillWatchingAfter",STILL[m]);x.dismiss();settings(a,d);}).show();}
   else if(n==4){String[] labels=new String[SPEEDS.length];int at=2;for(int s=0;s<SPEEDS.length;s++){labels[s]=speedLabel(SPEEDS[s]);if(SPEEDS[s]==defaultSpeed(d))at=s;}new AlertDialog.Builder(a).setTitle(t("Default speed")).setSingleChoiceItems(labels,at,(x,m)->{d.pref("defaultSpeed",(double)SPEEDS[m]);x.dismiss();settings(a,d);}).show();}
   else{String key=n==5?"autoNext":"markWatchedAtCredits";d.phoneSetting(key,!d.playbackFlag(key,true));settings(a,d);}
  }).setPositiveButton(t("Done"),null).show();
 }
}
