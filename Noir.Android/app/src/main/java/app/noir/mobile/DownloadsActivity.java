package app.noir.mobile;
import android.app.*;
import android.graphics.Typeface;
import android.os.*;
import android.view.*;
import android.widget.*;
import org.json.*;
public final class DownloadsActivity extends Activity {
 final Handler handler=new Handler(Looper.getMainLooper());LinearLayout body;DownloadHubActivity.JobList list;View empty;final java.util.Map<String,TextView> labels=new java.util.HashMap<>();// job id -> its status line
 final Runnable tick=new Runnable(){public void run(){update();handler.postDelayed(this,1000);}};
 @Override public void onCreate(Bundle b){super.onCreate(b);DownloadHubActivity.words();render();}
 void render(){ScrollView scroll=new ScrollView(this);scroll.setFillViewport(true);scroll.setBackgroundColor(MainActivity.BLACK);scroll.setFitsSystemWindows(true);body=new LinearLayout(this);body.setOrientation(LinearLayout.VERTICAL);int side=DownloadHubActivity.dp(this,PhoneDensity.pageSide());body.setPadding(side,DownloadHubActivity.dp(this,PhoneDensity.headerTop()),side,DownloadHubActivity.dp(this,28));scroll.addView(body);setContentView(scroll);
  TextView back=DownloadHubActivity.text(this,Loc.t("‹ Back"),PhoneDensity.button(),MainActivity.GOLD);back.setGravity(Gravity.CENTER_VERTICAL);back.setMinHeight(DownloadHubActivity.dp(this,44));back.setFocusable(true);back.setClickable(true);back.setContentDescription(Loc.t("‹ Back"));back.setOnClickListener(v->finish());body.addView(back);
  TextView title=DownloadHubActivity.text(this,Loc.t("Offline videos"),PhoneDensity.heading(),MainActivity.WHITE);title.setTypeface(null,Typeface.BOLD);if(Build.VERSION.SDK_INT>=28)title.setAccessibilityHeading(true);body.addView(title);
  TextView note=DownloadHubActivity.text(this,Loc.t("Downloads stay on this phone. Watch history stays when a saved video is removed."),12.5f,MainActivity.MUTED);note.setPadding(0,DownloadHubActivity.dp(this,4),0,DownloadHubActivity.dp(this,4));body.addView(note);
  if(!NoirApp.downloads.queueFailure.isEmpty()){TextView failure=DownloadHubActivity.text(this,Loc.t(NoirApp.downloads.queueFailure),13,DownloadHubActivity.DANGER);failure.setPadding(0,DownloadHubActivity.dp(this,6),0,DownloadHubActivity.dp(this,6));body.addView(failure);}
  list=new DownloadHubActivity.JobList(this,false,this::update);body.addView(list.box);
  TextView none=DownloadHubActivity.text(this,Loc.t("Choose Watch & save offline from an episode's options."),14,MainActivity.MUTED);none.setPadding(0,DownloadHubActivity.dp(this,16),0,0);none.setTag("offline-empty");body.addView(none);empty=none;update();Loc.apply(this);}
 void update(){if(list==null)return;JSONArray all=NoirApp.downloads.jobs();list.update(all);labels.clear();for(java.util.Map.Entry<String,DownloadHubActivity.Row> e:list.rows.entrySet())labels.put(e.getKey(),e.getValue().status);empty.setVisibility(all.length()==0?View.VISIBLE:View.GONE);}
 @Override protected void onResume(){super.onResume();handler.post(tick);}
 @Override protected void onPause(){handler.removeCallbacks(tick);super.onPause();}
}
