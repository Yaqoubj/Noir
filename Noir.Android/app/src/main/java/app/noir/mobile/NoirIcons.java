package app.noir.mobile;
import android.content.Context;
import android.graphics.drawable.Drawable;
import android.view.Gravity;
import android.view.View;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.TextView;
import java.util.List;
/** The one drawn icon set: vector drawables ic_noir_*, the PC's 24x24 stroked paths (stroke 1.7, round caps and joins), tinted at run time. Never mirrored. */
final class NoirIcons {
    private NoirIcons(){}
    static int id(String name){
        switch(name){
            case "home":return R.drawable.ic_noir_home;case "library":return R.drawable.ic_noir_library;case "history":return R.drawable.ic_noir_history;case "discover":return R.drawable.ic_noir_discover;case "search":return R.drawable.ic_noir_search;
            case "more":return R.drawable.ic_noir_more;case "check":return R.drawable.ic_noir_check;case "diary":return R.drawable.ic_noir_diary;case "list":return R.drawable.ic_noir_list;case "settings":return R.drawable.ic_noir_settings;
            case "downloads":return R.drawable.ic_noir_downloads;case "experience":return R.drawable.ic_noir_experience;case "playback":return R.drawable.ic_noir_playback;case "previews":return R.drawable.ic_noir_previews;case "subtitles":return R.drawable.ic_noir_subtitles;
            case "streaming":return R.drawable.ic_noir_streaming;case "taste":return R.drawable.ic_noir_taste;case "alerts":return R.drawable.ic_noir_alerts;case "connections":return R.drawable.ic_noir_connections;case "pc":return R.drawable.ic_noir_pc;
            case "language":return R.drawable.ic_noir_language;case "size":return R.drawable.ic_noir_size;case "tips":return R.drawable.ic_noir_tips;case "about":return R.drawable.ic_noir_about;case "clock":return R.drawable.ic_noir_clock;
            case "play":return R.drawable.ic_noir_play;case "pause":return R.drawable.ic_noir_pause;case "back10":return R.drawable.ic_noir_back10;case "fwd10":return R.drawable.ic_noir_fwd10;case "next":return R.drawable.ic_noir_next;case "plus":return R.drawable.ic_noir_plus;
            case "undo":return R.drawable.ic_noir_undo;case "queue":return R.drawable.ic_noir_queue;case "eye":return R.drawable.ic_noir_eye;case "collections":return R.drawable.ic_noir_collections;case "link":return R.drawable.ic_noir_link;case "star":return R.drawable.ic_noir_star;
            case "heart":return R.drawable.ic_noir_heart;case "heartfill":return R.drawable.ic_noir_heartfill;case "comment":return R.drawable.ic_noir_comment;case "info":return R.drawable.ic_noir_info;case "restart":return R.drawable.ic_noir_restart;case "external":return R.drawable.ic_noir_external;case "close":return R.drawable.ic_noir_close;case "chevron":return R.drawable.ic_noir_chevron;case "keyup":return R.drawable.ic_noir_keyup;case "keydown":return R.drawable.ic_noir_keydown;case "keyleft":return R.drawable.ic_noir_keyleft;case "keyright":return R.drawable.ic_noir_keyright;
            default:return 0;
        }
    }
    static boolean has(String name){return name!=null&&id(name)!=0;}
    static Drawable drawable(Context c,String name,int color){Drawable d=c.getDrawable(id(name)).mutate();d.setTint(color);return d;}
    /** A square icon view, dp wide, tinted with color. Decorative: the owning item carries the contentDescription. */
    static ImageView icon(Context c,String name,int dp,int color){
        ImageView v=new ImageView(c);v.setImageDrawable(drawable(c,name,color));v.setScaleType(ImageView.ScaleType.FIT_CENTER);v.setTag(name);v.setImportantForAccessibility(View.IMPORTANT_FOR_ACCESSIBILITY_NO);
        v.setLayoutParams(new LinearLayout.LayoutParams(Math.round(dp*c.getResources().getDisplayMetrics().density),Math.round(dp*c.getResources().getDisplayMetrics().density)));return v;
    }
    static int px(Context c,float dp){return Math.round(dp*c.getResources().getDisplayMetrics().density);}
    /** Swaps the text glyph at the start of a NoirSheet row for a drawn icon (the sheet only draws text glyphs itself). */
    static void rowIcon(View row,String name){
        if(!(row instanceof LinearLayout)||!has(name))return;LinearLayout r=(LinearLayout)row;Context c=r.getContext();
        android.widget.FrameLayout slot=new android.widget.FrameLayout(c);ImageView mark=icon(c,name,20,0xffe4c084);slot.addView(mark,new android.widget.FrameLayout.LayoutParams(mark.getLayoutParams().width,mark.getLayoutParams().height,Gravity.CENTER));
        r.removeViewAt(0);r.addView(slot,0,new LinearLayout.LayoutParams(px(c,36),px(c,32)));
    }
    /** A NoirSheet row whose icon is drawn. */
    static View row(NoirSheet s,String icon,String label,String hint,boolean enabled,String reason,Runnable action){View r=s.row("",label,hint,enabled,reason,action);rowIcon(r,icon);return r;}
    /** NoirSheet tiles whose Tile.icon is an icon name: the grid is drawn by the sheet, then every glyph is swapped for the drawn icon. A toggle tile (state != null) shows plus or check. */
    static void tiles(NoirSheet s,int columns,List<NoirSheet.Tile> actions){
        int before=s.body.getChildCount();s.tiles(columns,actions);Context c=s.body.getContext();
        for(int n=0;n<actions.size();n++){
            View line=s.body.getChildAt(before+n/columns);if(!(line instanceof LinearLayout))continue;View tile=((LinearLayout)line).getChildAt(n%columns);if(!(tile instanceof LinearLayout))continue;
            NoirSheet.Tile t=actions.get(n);ImageView mark=icon(c,t.state!=null?(t.state.getAsBoolean()?"check":"plus"):t.icon,22,0xffe4c084);LinearLayout.LayoutParams lp=(LinearLayout.LayoutParams)mark.getLayoutParams();lp.gravity=Gravity.CENTER_HORIZONTAL;
            ((LinearLayout)tile).removeViewAt(0);((LinearLayout)tile).addView(mark,0,lp);
            tile.setOnClickListener(v->{t.run.run();if(t.state!=null){mark.setImageDrawable(drawable(c,t.state.getAsBoolean()?"check":"plus",0xffe4c084));Motion.haptic(tile);}Motion.pop(tile);});
        }
    }
    /** A pinned primary button with a drawn icon before its label (dark on the gold fill). */
    static void buttonIcon(View button,String name){if(button instanceof TextView&&has(name)){TextView t=(TextView)button;Context c=t.getContext();Drawable d=drawable(c,name,0xff0b0b0d);d.setBounds(0,0,px(c,20),px(c,20));t.setCompoundDrawablesRelative(d,null,null,null);t.setCompoundDrawablePadding(px(c,8));}}
    /** A bottom-bar item: icon over a small label, both in color. The item itself is the click target and carries the description. */
    static LinearLayout barItem(Context c,String name,String label,String description,float sp,int iconDp,int color){
        LinearLayout item=new LinearLayout(c);item.setOrientation(LinearLayout.VERTICAL);item.setGravity(Gravity.CENTER);
        item.addView(icon(c,name,iconDp,color));
        TextView t=new TextView(c);t.setText(label);t.setTextSize(sp);t.setTextColor(color);t.setGravity(Gravity.CENTER);t.setSingleLine();t.setEllipsize(android.text.TextUtils.TruncateAt.END);t.setPadding(0,Math.round(2*c.getResources().getDisplayMetrics().density),0,0);
        item.addView(t,new LinearLayout.LayoutParams(-2,-2));item.setContentDescription(description);return item;
    }
}
