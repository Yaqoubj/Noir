package app.noir.mobile;

import android.app.Dialog;
import android.content.Intent;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.view.Gravity;
import android.view.View;
import android.view.Window;
import android.view.WindowManager;
import android.widget.*;
import org.json.JSONObject;
import java.util.*;

/** Artwork-led episode actions. No web views or network work are created by opening the sheet. */
final class TitleActions {
    final MainActivity a;
    Dialog dialog;
    TitleActions(MainActivity activity) { a=activity; }

    void dismiss() { if(dialog!=null) { dialog.dismiss();dialog=null; } }

    void show(JSONObject item) {
        dismiss();
        Dialog sheet=new Dialog(a);dialog=sheet;
        sheet.requestWindowFeature(Window.FEATURE_NO_TITLE);
        LinearLayout content=a.column();
        GradientDrawable background=MainActivity.bg(MainActivity.BLACK,0);
        float r=a.dp(24);background.setCornerRadii(new float[]{r,r,r,r,0,0,0,0});
        content.setBackground(background);content.setClipToOutline(true);
        FrameLayout top=new FrameLayout(a);
        top.addView(a.poster(item,false),new FrameLayout.LayoutParams(-1,-1));
        View fade=new View(a);fade.setBackground(a.ui.fade(Color.argb(80,0,0,0),MainActivity.BLACK));
        top.addView(fade,new FrameLayout.LayoutParams(-1,-1));
        TextView close=a.text("×",24,MainActivity.WHITE);close.setGravity(Gravity.CENTER);
        close.setBackground(MainActivity.bg(Color.argb(200,12,12,14),a.dp(24)));
        close.setContentDescription("Close episode actions");close.setOnClickListener(v->dismiss());
        FrameLayout.LayoutParams xp=new FrameLayout.LayoutParams(a.dp(48),a.dp(48),Gravity.TOP|Gravity.END);
        xp.setMargins(0,a.dp(10),a.dp(12),0);top.addView(close,xp);
        LinearLayout caption=a.column();caption.setPadding(a.dp(22),a.dp(24),a.dp(22),a.dp(12));
        TextView name=a.text(item.optString("title"),24,MainActivity.WHITE);name.setTypeface(null,Typeface.BOLD);name.setMaxLines(2);caption.addView(name);
        caption.addView(a.text(MainActivity.episode(item)+(item.optBoolean("available")?" · On your PC":" · Saved in your library"),12,MainActivity.GOLD));
        top.addView(caption,new FrameLayout.LayoutParams(-1,-2,Gravity.BOTTOM));

        // Keep the artwork and all actions reachable on small screens and with large system text.
        ScrollView scroll=new ScrollView(a);scroll.setFillViewport(false);scroll.setVerticalScrollBarEnabled(false);
        LinearLayout body=a.column();body.addView(top,new LinearLayout.LayoutParams(-1,a.dp(164)));
        LinearLayout actions=a.column();actions.setPadding(a.dp(18),0,a.dp(18),a.dp(24));body.addView(actions);
        if(!PhoneExperience.trackerOnly(a.data)){if(a.playable(item)) {
            Button play=a.button(MainActivity.position(item)>0&&!MainActivity.completed(item)?"▶ Resume · "+DataStore.time(MainActivity.position(item)):"▶ Watch now",()->choose(item,0),true);
            play.setContentDescription("Watch now or resume");actions.addView(play);
        } else if(PhoneStream.available(a)&&item.optString("imdb").matches("tt\\d+")){
            Button stream=a.button("▶ Stream now",()->{dismiss();PhoneStream.start(a,item,false);},true);stream.setContentDescription("Stream now");actions.addView(stream);
            actions.addView(a.button("Choose a stream…",()->{dismiss();PhoneStream.start(a,item,true);},false));
            if(a.data.paired())actions.addView(a.button("↓ Find a download on PC",()->choose(item,14),false));
        } else actions.addView(a.button("▶ Set up streaming",()->{dismiss();PhoneStream.start(a,item,false);},true));
        if(item.optString("imdb").matches("tt\\d+")){Button stream=a.button("▶ Stream in Stremio",()->{dismiss();a.stremio(item);},false);stream.setContentDescription("Stream in Stremio");actions.addView(stream);
            Button nuvio=a.button("▶ Stream in Nuvio",()->{dismiss();a.nuvio(item);},false);nuvio.setContentDescription("Stream in Nuvio");actions.addView(nuvio);}

        }
        section(actions,"WATCH & ORGANIZE");
        List<Tile> organize=new ArrayList<>(List.of(
            action(MainActivity.completed(item)?"↶":"✓",MainActivity.completed(item)?"Mark unwatched":"Mark watched",()->choose(item,2)),
            listTile(item),
            action("▤","Play queue",()->choose(item,7)),
            action("◉","Viewing status",()->choose(item,8)),
            action("▦","Collections",()->choose(item,9)),
            action("↺","Start over",()->choose(item,1),a.playable(item))));if(PhoneExperience.trackerOnly(a.data))organize.removeIf(t->t.label.equals("Play queue")||t.label.equals("Start over"));tiles(actions,organize);
        if(!PhoneExperience.trackerOnly(a.data)){section(actions,"DOWNLOADS");
        List<Tile> downloads=new ArrayList<>();
        if(a.data.paired())downloads.add(action("↓",item.optBoolean("movie")?"Movie to PC":"Episode to PC",()->choose(item,14)));
        downloads.add(action("↓",item.optBoolean("movie")?"Movie to phone":"Episode to phone",()->{dismiss();PhoneDownloadsActivity.open(a,item,false);}));
        if(a.data.paired()&&!item.optBoolean("movie"))downloads.add(action("⇊","Season to PC",()->choose(item,15)));
        if(!item.optBoolean("movie"))downloads.add(action("⇊","Season to phone",()->{dismiss();PhoneDownloadsActivity.open(a,item,true);}));
        downloads.add(action("⇩","Save on phone",()->choose(item,12),item.optBoolean("available")));
        downloads.add(action("▶","Watch & save",()->choose(item,10),item.optBoolean("available")));
        downloads.add(action("⌁","Link phone file",()->choose(item,3)));
        tiles(actions,downloads);}
        section(actions,"YOUR ACTIVITY & DETAILS");
        tiles(actions,List.of(
            action("☆","Rating & note",()->{dismiss();PhoneTracker.quickRate(a,item);}),
            action(a.data.liked(item.optString("group"))?"♥":"♡","Like this title",()->{a.data.like(item.optString("group"),!a.data.liked(item.optString("group")));a.data.syncSoon();dismiss();a.renderKeep();}),
            action("◷","Viewing dates",()->choose(item,6)),
            action("💬","Comments",()->choose(item,4)),
            action("ⓘ","Description",()->choose(item,13)),
            action("⌕","Correct match",()->choose(item,11))));
        scroll.addView(body);content.addView(scroll,new LinearLayout.LayoutParams(-1,-1));sheet.setContentView(content);
        Window w=sheet.getWindow();
        if(w!=null){w.setBackgroundDrawableResource(android.R.color.transparent);w.addFlags(WindowManager.LayoutParams.FLAG_DIM_BEHIND);WindowManager.LayoutParams p=w.getAttributes();p.gravity=Gravity.BOTTOM;p.dimAmount=.65f;w.setAttributes(p);}
        sheet.setOnDismissListener(d->{if(dialog==sheet)dialog=null;});sheet.show();
        if(w!=null) {
            android.graphics.Rect bounds=new android.graphics.Rect();a.getWindow().getDecorView().getWindowVisibleDisplayFrame(bounds);
            int height=bounds.height()>0?bounds.height():a.getResources().getDisplayMetrics().heightPixels;
            w.setLayout(Math.min(a.getResources().getDisplayMetrics().widthPixels,a.dp(640)),(int)(height*.90));
        }
    }

    void choose(JSONObject item,int option){dismiss();a.option(item,option);}
    static final class Tile {
        java.util.function.BooleanSupplier state;final String icon,label;final Runnable run;final boolean enabled;
        Tile(String icon,String label,Runnable run,boolean enabled){this.icon=icon;this.label=label;this.run=run;this.enabled=enabled;}
    }
    Tile listTile(JSONObject item){Tile t=action(item.optBoolean("listed")?"✓":"＋","My List",()->{JSONObject fresh=a.data.item(item.optString("id"));boolean listed=fresh!=null&&fresh.optBoolean("listed");JSONObject op=a.data.operation(item.optString("id"),"list");DataStore.put(op,"value",!listed);a.data.queue(op);a.data.syncSoon();a.listBadges(item.optString("group"),!listed);a.updateStatus();});t.state=()->{JSONObject fresh=a.data.item(item.optString("id"));return fresh!=null&&fresh.optBoolean("listed");};return t;}
    Tile action(String icon,String label,Runnable run){return action(icon,label,run,true);}
    Tile action(String icon,String label,Runnable run,boolean enabled){return new Tile(icon,label,run,enabled);}
    void section(LinearLayout host,String label){TextView title=a.text(label,10,MainActivity.MUTED);title.setLetterSpacing(.12f);title.setTypeface(null,Typeface.BOLD);title.setPadding(a.dp(4),a.dp(24),0,a.dp(10));host.addView(title);}
    void tiles(LinearLayout host,List<Tile> actions){
        final int columns=3;
        for(int n=0;n<actions.size();n+=columns){
            LinearLayout row=new LinearLayout(a);row.setGravity(Gravity.TOP);host.addView(row);
            for(int k=0;k<columns;k++){
                LinearLayout.LayoutParams p=new LinearLayout.LayoutParams(0,-2,1);p.setMargins(a.dp(3),a.dp(3),a.dp(3),a.dp(3));
                if(n+k>=actions.size()){row.addView(new View(a),p);continue;}
                Tile action=actions.get(n+k);LinearLayout tile=a.column();tile.setGravity(Gravity.CENTER);
                tile.setPadding(a.dp(6),a.dp(12),a.dp(6),a.dp(12));tile.setMinimumHeight(a.dp(88));tile.setBackground(MainActivity.bg(MainActivity.PANEL,a.dp(16)));
                TextView glyph=a.text(action.icon,24,MainActivity.GOLD);glyph.setGravity(Gravity.CENTER);tile.addView(glyph);
                TextView label=a.text(action.label,12,MainActivity.WHITE);label.setGravity(Gravity.CENTER);label.setPadding(0,a.dp(6),0,0);tile.addView(label);
                tile.setEnabled(action.enabled);tile.setAlpha(action.enabled?1:.4f);tile.setFocusable(action.enabled);tile.setContentDescription(action.label);tile.setOnClickListener(v->{action.run.run();if(action.state!=null){glyph.setText(action.state.getAsBoolean()?"✓":"＋");Motion.haptic(tile);}Motion.pop(tile);});Motion.card(tile);row.addView(tile,p);
            }
        }
    }

    void more(){PhoneExperience.settings(a);}
}
