package app.noir.mobile;

import android.app.Activity;
import android.view.*;
import android.widget.*;

/** Programmatic Noir styling shared by the phone download screens. */
abstract class PhoneScreen extends Activity {
    LinearLayout body;TextView feedback;
    int dp(int n){return Math.round(n*getResources().getDisplayMetrics().density);}
    TextView text(String value,int size){TextView view=new TextView(this);view.setText(Loc.t(value));view.setTextSize(size);view.setTextColor(MainActivity.WHITE);view.setPadding(0,dp(8),0,dp(8));return view;}
    Button button(String value,Runnable action,boolean primary){Button view=new Button(this);view.setText(value);view.setAllCaps(false);view.setMinHeight(dp(48));view.setTextColor(primary?MainActivity.BLACK:MainActivity.WHITE);view.setBackground(MainActivity.bg(primary?MainActivity.GOLD:MainActivity.PANEL,dp(12)));view.setOnClickListener(v->action.run());LinearLayout.LayoutParams layout=new LinearLayout.LayoutParams(-1,-2);layout.setMargins(0,dp(6),0,dp(6));view.setLayoutParams(layout);return view;}
    LinearLayout column(){LinearLayout view=new LinearLayout(this);view.setOrientation(LinearLayout.VERTICAL);return view;}
    void page(String title){LinearLayout root=column();root.setBackgroundColor(MainActivity.BLACK);root.setFitsSystemWindows(true);root.setPadding(dp(20),dp(12),dp(20),dp(12));root.addView(button("‹ Back",this::finish,false));root.addView(text(title,26));ScrollView scroll=new ScrollView(this);body=column();scroll.addView(body);root.addView(scroll,new LinearLayout.LayoutParams(-1,0,1));feedback=text("",13);feedback.setTextColor(MainActivity.GOLD);root.addView(feedback);setContentView(root);}
    EditText input(String label,String value,boolean secret){body.addView(text(label,13));EditText view=new EditText(this);view.setTextColor(MainActivity.WHITE);view.setHintTextColor(MainActivity.MUTED);view.setSingleLine(true);view.setSaveEnabled(false);view.setImportantForAutofill(View.IMPORTANT_FOR_AUTOFILL_NO_EXCLUDE_DESCENDANTS);view.setInputType(android.text.InputType.TYPE_CLASS_TEXT|(secret?android.text.InputType.TYPE_TEXT_VARIATION_PASSWORD:android.text.InputType.TYPE_TEXT_VARIATION_NORMAL));view.setText(value);body.addView(view);return view;}
    Spinner spinner(String label,String[] values,String selected){body.addView(text(label,13));Spinner view=new Spinner(this);ArrayAdapter<String> adapter=new ArrayAdapter<String>(this,android.R.layout.simple_spinner_item,values){public View getView(int p,View v,ViewGroup parent){TextView row=(TextView)super.getView(p,v,parent);row.setTextColor(MainActivity.WHITE);row.setPadding(dp(10),dp(12),dp(10),dp(12));return row;}public View getDropDownView(int p,View v,ViewGroup parent){TextView row=(TextView)super.getDropDownView(p,v,parent);row.setTextColor(MainActivity.WHITE);row.setBackgroundColor(MainActivity.PANEL);return row;}};adapter.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item);view.setAdapter(adapter);view.setSelection(Math.max(0,java.util.Arrays.asList(values).indexOf(selected)));body.addView(view);return view;}
}
