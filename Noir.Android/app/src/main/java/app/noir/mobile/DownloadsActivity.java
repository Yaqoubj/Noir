package app.noir.mobile;
import android.app.*;
import android.content.*;
import android.os.*;
import android.widget.*;
import org.json.*;
import java.util.*;
public final class DownloadsActivity extends Activity {
 final Handler handler=new Handler(Looper.getMainLooper());LinearLayout body;final Map<String,TextView> labels=new HashMap<>();
 final Runnable tick=new Runnable(){public void run(){for(JSONObject j:DataStore.objects(NoirApp.downloads.jobs())){TextView label=labels.get(j.optString("id"));if(label!=null)label.setText(j.optString("status")+" · "+String.format(Locale.ROOT,"%.0f / %.0f MB",j.optLong("bytes")/1048576d,j.optLong("total")/1048576d)+(j.optString("error").isEmpty()?"":"\n"+j.optString("error")));}handler.postDelayed(this,1000);}};
 @Override public void onCreate(Bundle b){super.onCreate(b);render();}
 void render(){labels.clear();ScrollView scroll=new ScrollView(this);scroll.setBackgroundColor(MainActivity.BLACK);scroll.setFitsSystemWindows(true);body=new LinearLayout(this);body.setOrientation(LinearLayout.VERTICAL);body.setPadding(24,24,24,24);scroll.addView(body);setContentView(scroll);button("‹ Back",this::finish);text("Offline videos",24);text("Downloads stay on this phone. Watch history stays when a saved video is removed.",14);for(JSONObject j:DataStore.objects(NoirApp.downloads.jobs())){String id=j.optString("id");text(j.optString("title"),18);TextView status=text(j.optString("status"),13);labels.put(id,status);button("Pause",()->NoirApp.downloads.pause(id));button(j.optString("status").equals("complete")?"Watch offline":"Resume / retry",()->{JSONObject i=NoirApp.data.item(id);if(i==null)return;if(j.optString("status").equals("complete")){startActivity(new Intent(this,PlayerActivity.class).putExtra("id",id));return;}try{NoirApp.downloads.enqueue(i);startForegroundService(new Intent(this,DownloadService.class));}catch(Exception e){error(e);}});button("Remove saved video",()->new AlertDialog.Builder(this).setTitle("Remove phone copy?").setMessage("Its poster and watched history remain in Noir.").setNegativeButton("Cancel",null).setPositiveButton("Remove",(d,n)->{try{NoirApp.downloads.remove(id);render();}catch(Exception e){error(e);}}).show());}if(NoirApp.downloads.jobs().length()==0)text("Choose Watch & save offline from an episode's options.",16);}
 TextView text(String s,int size){TextView t=new TextView(this);t.setText(s);t.setTextSize(size);t.setTextColor(MainActivity.WHITE);t.setPadding(0,12,0,12);body.addView(t);return t;}
 void button(String s,Runnable action){Button b=new Button(this);b.setText(s);b.setAllCaps(false);b.setOnClickListener(v->action.run());body.addView(b);}
 void error(Exception e){Toast.makeText(this,e.getMessage(),Toast.LENGTH_LONG).show();}
 @Override protected void onResume(){super.onResume();handler.post(tick);}
 @Override protected void onPause(){handler.removeCallbacks(tick);super.onPause();}
}
