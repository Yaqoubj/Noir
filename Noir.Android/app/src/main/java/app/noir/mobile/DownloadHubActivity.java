package app.noir.mobile;
import android.app.Activity;
import android.content.Intent;
import android.os.Bundle;
import android.view.Gravity;
import android.widget.*;
public final class DownloadHubActivity extends Activity {
    @Override public void onCreate(Bundle saved){super.onCreate(saved);LinearLayout root=new LinearLayout(this);root.setOrientation(LinearLayout.VERTICAL);root.setBackgroundColor(MainActivity.BLACK);root.setFitsSystemWindows(true);int pad=(int)(22*getResources().getDisplayMetrics().density);root.setPadding(pad,pad,pad,pad);setContentView(root);
        TextView title=new TextView(this);title.setText("Downloads");title.setTextColor(MainActivity.WHITE);title.setTextSize(28);root.addView(title);add(root,"Downloads on PC","See progress and control movies or episodes requested from your phone.",PcDownloadsActivity.class);add(root,"Offline on phone","Manage videos saved on this phone for watching without a connection.",DownloadsActivity.class);add(root,"Discover something new","Find a movie or show and choose files to download on your paired PC.",DiscoverActivity.class);Button back=new Button(this);back.setText("‹ Back");back.setAllCaps(false);back.setOnClickListener(v->finish());root.addView(back);
    }
    void add(LinearLayout root,String title,String description,Class<?> activity){Button button=new Button(this);button.setText(title);button.setAllCaps(false);button.setTextColor(MainActivity.WHITE);button.setBackground(MainActivity.bg(MainActivity.PANEL,12));LinearLayout.LayoutParams layout=new LinearLayout.LayoutParams(-1,-2);layout.topMargin=24;button.setLayoutParams(layout);button.setOnClickListener(v->startActivity(new Intent(this,activity)));root.addView(button);TextView text=new TextView(this);text.setText(description);text.setTextSize(14);text.setTextColor(MainActivity.MUTED);text.setPadding(0,10,0,14);root.addView(text);}
}
