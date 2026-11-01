package app.noir.mobile;
import android.app.*;
import android.view.*;
import android.widget.*;

/** A remote-only PIN pad. Validation and switching run on the caller's worker. */
final class TvKeypad {
    interface Submit {void run(String pin,TvKeypad pad);}
    final Activity activity;final StringBuilder pin=new StringBuilder();final TextView dots,state;final AlertDialog dialog;final LinearLayout keys;boolean busy;
    static {Loc.AR.put("Delete digit","حذف رقم");Loc.AR.put("Clear PIN","مسح الرمز");Loc.AR.put("Enter PIN","أدخل الرمز");Loc.AR.put("Open","فتح");Loc.AR.put("Checking PIN…","جارٍ التحقق من الرمز…");Loc.AR.put("Enter your PIN first.","أدخل الرمز أولًا.");}
    TvKeypad(Activity a,String name,Submit submit){activity=a;LinearLayout form=TvSetupActivity.column(a);dots=TvUi.text(a,"",24,TvUi.WHITE,true);dots.setGravity(Gravity.CENTER);dots.setMinHeight(TvUi.dp(a,40));form.addView(dots);state=TvSetupActivity.status(a,form);keys=TvSetupActivity.column(a);keys.setLayoutDirection(View.LAYOUT_DIRECTION_LTR);form.addView(keys);
        TextView first=null;for(String[] row:new String[][]{{"1","2","3"},{"4","5","6"},{"7","8","9"},{"Clear PIN","0","Delete digit"}}){LinearLayout line=new LinearLayout(a);line.setGravity(Gravity.CENTER);line.setClipChildren(false);keys.addView(line);for(String key:row){TextView b=TvUi.button(a,Loc.t(key),false,()->{if(busy)return;if(key.equals("Clear PIN"))pin.setLength(0);else if(key.equals("Delete digit")){if(pin.length()>0)pin.deleteCharAt(pin.length()-1);}else if(pin.length()<32)pin.append(key);dots.setText(new String(new char[pin.length()]).replace('\0','•'));state.setText("");});LinearLayout.LayoutParams lp=new LinearLayout.LayoutParams(TvUi.dp(a,150),TvUi.dp(a,48));lp.setMargins(TvUi.dp(a,6),TvUi.dp(a,6),TvUi.dp(a,6),TvUi.dp(a,6));line.addView(b,lp);if(first==null)first=b;}}
        dialog=TvSetupActivity.dialog(a,"Enter PIN",form);dialog.setTitle(Loc.t("Enter PIN")+" · "+name);TextView initial=first;dialog.setOnShowListener(v->{dialog.getButton(-1).setText(Loc.t("Open"));initial.requestFocus();dialog.getButton(-1).setOnClickListener(w->{if(busy)return;if(pin.length()==0){state.setText(Loc.t("Enter your PIN first."));return;}busy=true;dialog.getButton(-1).setEnabled(false);state.setText(Loc.t("Checking PIN…"));dialog.getButton(-2).requestFocus();submit.run(pin.toString(),this);});});dialog.setOnDismissListener(v->pin.setLength(0));dialog.show();
    }
    void error(String message){if(!TvSetupActivity.alive(activity,dialog))return;busy=false;pin.setLength(0);dots.setText("");state.setText(Loc.t(message));dialog.getButton(-1).setEnabled(true);((LinearLayout)keys.getChildAt(0)).getChildAt(0).requestFocus();}
}
