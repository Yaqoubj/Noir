package app.noir.mobile;
import android.app.*;
import android.content.*;
import android.graphics.Color;
import android.graphics.drawable.GradientDrawable;
import android.os.Bundle;
import android.view.*;
import android.widget.*;
import org.json.*;

public final class TvProfilesActivity extends Activity {
    static volatile boolean chosen;ProfileStore profiles;TextView state;boolean switching;
    static final class ProfileAttempt {boolean cancelled,committed;}
    ProfileAttempt attempt;
    static {String[][] words={{"Who's watching?","من يشاهد؟"},{"Choose your profile to continue.","اختر ملفك للمتابعة."},{"PIN protected","محمي برمز"},{"Switching profile…","جارٍ تبديل الملف…"},{"Couldn't switch profile. Try again.","تعذر تبديل الملف. حاول مجددًا."},{"Too many PIN attempts. Try again in five minutes.","محاولات رمز كثيرة. حاول بعد خمس دقائق."},{"That PIN isn't right.","الرمز غير صحيح."},{"Downloads are pausing. Try again in a moment.","جارٍ إيقاف التنزيلات مؤقتًا. حاول بعد لحظة."}};for(String[] p:words)Loc.AR.put(p[0],p[1]);}
    public static boolean needed(DataStore data){return data!=null&&!chosen&&data.profiles.list().length()>=2;} // "Who's watching?" with 2+ profiles; one profile opens like the phone (no startup PIN).
    @Override protected void onCreate(Bundle b){super.onCreate(b);profiles=NoirApp.data.profiles;FrameLayout root=TvUi.screen(this);LinearLayout body=TvSetupActivity.column(this);body.setGravity(Gravity.CENTER_VERTICAL);root.addView(body,new FrameLayout.LayoutParams(-1,-1));body.addView(TvUi.text(this,Loc.t("Who's watching?"),34,TvUi.WHITE,true));TextView subtitle=TvUi.text(this,Loc.t("Choose your profile to continue."),18,TvUi.MUTED,false);subtitle.setPadding(0,TvUi.dp(this,12),0,TvUi.dp(this,24));body.addView(subtitle);
        LinearLayout row=new LinearLayout(this);View initial=null;String current=profiles.current();for(JSONObject p:DataStore.objects(profiles.list())){String id=p.optString("id"),name=p.optString("name");LinearLayout tile=TvSetupActivity.column(this);tile.setGravity(Gravity.CENTER);TextView avatar=TvUi.text(this,name.isEmpty()?"?":name.substring(0,name.offsetByCodePoints(0,1)).toUpperCase(java.util.Locale.getDefault()),42,TvUi.BLACK,true);avatar.setGravity(Gravity.CENTER);GradientDrawable circle=new GradientDrawable();circle.setShape(GradientDrawable.OVAL);int color=TvUi.GOLD;try{color=Color.parseColor(p.optString("color"));}catch(IllegalArgumentException ignored){}circle.setColor(color);avatar.setBackground(circle);tile.addView(avatar,new LinearLayout.LayoutParams(TvUi.dp(this,104),TvUi.dp(this,104)));TextView label=TvUi.text(this,name,18,TvUi.WHITE,true);label.setGravity(Gravity.CENTER);label.setMaxLines(2);label.setEllipsize(android.text.TextUtils.TruncateAt.END);label.setPadding(0,TvUi.dp(this,14),0,TvUi.dp(this,8));tile.addView(label,new LinearLayout.LayoutParams(TvUi.dp(this,140),-2));if(!p.optString("pinHash").isEmpty())tile.addView(TvUi.text(this,Loc.t("PIN protected"),13,TvUi.MUTED,false));TvUi.focusable(tile,TvUi.PANEL,TvUi.dp(this,16));tile.setContentDescription(name);tile.setOnClickListener(v->choose(p));LinearLayout.LayoutParams lp=new LinearLayout.LayoutParams(TvUi.dp(this,168),TvUi.dp(this,220));lp.setMargins(TvUi.dp(this,10),TvUi.dp(this,12),TvUi.dp(this,10),TvUi.dp(this,12));row.addView(tile,lp);if(initial==null||id.equals(current))initial=tile;}
        body.addView(TvUi.row(this,"",row));for(int n=0;n<row.getChildCount();n++){View tile=row.getChildAt(n);int previous=row.getChildAt(Math.max(0,n-1)).getId(),next=row.getChildAt(Math.min(row.getChildCount()-1,n+1)).getId();tile.setNextFocusLeftId(Loc.arabic?next:previous);tile.setNextFocusRightId(Loc.arabic?previous:next);}state=TvSetupActivity.status(this,body);setContentView(root);Loc.apply(this);if(initial!=null)initial.requestFocus();
    }
    SharedPreferences attempts(){return ProfileStore.base(this).getSharedPreferences("tv-profile-pin",MODE_PRIVATE);}
    boolean locked(String id){return attempts().getLong(id+"-until",0)>System.currentTimeMillis();}
    void choose(JSONObject p){if(switching)return;String id=p.optString("id");if(locked(id)){state.setText(Loc.t("Too many PIN attempts. Try again in five minutes."));return;}if(p.optString("pinHash").isEmpty())switchTo(id,"",null);else new TvKeypad(this,p.optString("name"),(pin,pad)->switchTo(id,pin,pad));}
    void cancel(ProfileAttempt work){synchronized(work){work.cancelled=true;if(work.committed)return;}if(attempt==work){switching=false;state.setText("");}}
    @Override protected void onDestroy(){if(attempt!=null)cancel(attempt);super.onDestroy();}
    void switchTo(String id,String pin,TvKeypad pad){if(switching)return;ProfileAttempt work=new ProfileAttempt();attempt=work;if(pad!=null)pad.dismissed=()->cancel(work);switching=true;state.setText(Loc.t("Switching profile…"));DataStore old=NoirApp.data;old.network.execute(()->{try{
            synchronized(work){if(work.cancelled||isFinishing()||isDestroyed())return;}
            if(locked(id))throw new IllegalArgumentException("Too many PIN attempts. Try again in five minutes.");
            if(!profiles.checkPin(id,pin)){SharedPreferences prefs=attempts();int count=prefs.getInt(id+"-count",0)+1;boolean lock=count>=5;prefs.edit().putInt(id+"-count",lock?0:count).putLong(id+"-until",lock?System.currentTimeMillis()+300000:0).commit();throw new IllegalArgumentException(lock?"Too many PIN attempts. Try again in five minutes.":"That PIN isn't right.");}
            synchronized(work){if(work.cancelled||isFinishing()||isDestroyed()||NoirApp.data!=old)return;NoirApp.switchProfile(this,id,pin);work.committed=true;}attempts().edit().remove(id+"-count").remove(id+"-until").apply();PhoneHarbor.phoneCache=null;PhoneHarbor.phoneAt=0;chosen=true;runOnUiThread(()->{if(isFinishing()||isDestroyed())return;if(pad!=null)pad.dialog.dismiss();setResult(RESULT_OK);startActivity(new Intent(this,TvActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK|Intent.FLAG_ACTIVITY_CLEAR_TASK));finish();});
        }catch(Exception e){String message=e.getMessage()==null?"Couldn't switch profile. Try again.":e.getMessage();runOnUiThread(()->{if(attempt!=work||work.cancelled||isFinishing()||isDestroyed())return;switching=false;state.setText(Loc.t(message));if(pad!=null)pad.error(message);});}});}
}
