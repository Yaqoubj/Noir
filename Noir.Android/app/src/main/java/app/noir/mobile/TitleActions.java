package app.noir.mobile;

import android.app.Dialog;
import android.widget.*;
import org.json.JSONObject;
import java.util.*;

/** Artwork-led episode actions on the shared bottom sheet. No web views or network work are created by opening it. */
final class TitleActions {
    final MainActivity a;
    Dialog dialog;NoirSheet sheet;
    TitleActions(MainActivity activity) { a=activity; }

    void dismiss() { if(dialog!=null) { Dialog old=dialog;dialog=null;sheet=null;old.dismiss(); } }

    TextView favouriteChip(JSONObject item){TextView chip=a.ui.chip(a.data.liked(item.optString("group"))?"♥ Favourite":"♡ Favourite",()->{PhoneTracker.toggleFavourite(a.data,item);Motion.haptic(a.body);dismiss();a.renderKeep();},false);chip.setContentDescription(Loc.t(a.data.liked(item.optString("group"))?"♥ Favourite":"♡ Favourite"));chip.setFocusable(true);return chip;}
    void show(JSONObject item) {
        dismiss();
        NoirSheet s=new NoirSheet(a,item.optString("title"),MainActivity.episode(item)+(item.optBoolean("available")?" · On your PC":" · Saved in your library"));sheet=s;dialog=s.dialog;
        s.art(a.poster(item,false),PhoneDensity.pick(116,150));s.closeButton("Close episode actions");
        // The one obvious action is pinned under the list; everything else stays reachable by scrolling.
        if(!PhoneExperience.trackerOnly(a.data)){
            if(a.playable(item)) {
                s.footerPrimary(MainActivity.position(item)>0&&!MainActivity.completed(item)?"▶ Resume · "+DataStore.time(MainActivity.position(item)):"▶ Watch now","Watch now or resume",()->choose(item,0));
            } else if(PhoneStream.available(a)&&item.optString("imdb").matches("tt\\d+")){
                s.footerPrimary("▶ Stream now","Stream now",()->PhoneStream.start(a,item,false));
                s.row("≡","Choose a stream…",null,true,null,()->{dismiss();PhoneStream.start(a,item,true);});
            } else s.footerPrimary("▶ Set up streaming","Set up streaming",()->PhoneStream.start(a,item,false));
            if(item.optString("imdb").matches("tt\\d+")){
                s.row("▶","Stream in Stremio",null,true,null,()->{dismiss();a.stremio(item);}).setContentDescription(Loc.t("Stream in Stremio"));
                s.row("▶","Stream in Nuvio",null,true,null,()->{dismiss();a.nuvio(item);}).setContentDescription(Loc.t("Stream in Nuvio"));
            }
        }
        s.section("WATCH & ORGANIZE");
        List<NoirSheet.Tile> organize=new ArrayList<>(List.of(
            action(MainActivity.completed(item)?"↶":"✓",MainActivity.completed(item)?"Mark unwatched":"Mark watched",()->choose(item,2)),
            listTile(item),
            action("▤","Play queue",()->choose(item,7)),
            action("◉","Viewing status",()->choose(item,8)),
            action("▦","Collections",()->choose(item,9)),
            action("↺","Start over",()->choose(item,1),a.playable(item))));if(PhoneExperience.trackerOnly(a.data))organize.removeIf(t->t.label.equals("Play queue")||t.label.equals("Start over"));s.tiles(3,organize);
        if(!PhoneExperience.trackerOnly(a.data)&&!TvUi.isTv(a)){s.section("DOWNLOADS");Loc.AR.put("Download…","تنزيل…");s.row("↓","Download…",null,true,null,()->{dismiss();PhoneDownloadsActivity.open(a,item,false);});s.row("⌁","Link phone file",null,true,null,()->choose(item,3));}
        s.section("YOUR ACTIVITY & DETAILS");
        s.tiles(3,List.of(
            action("☆","Rating & note",()->{dismiss();PhoneTracker.quickRate(a,item);}),
            action(a.data.liked(item.optString("group"))?"♥":"♡","Favourite",()->{PhoneTracker.toggleFavourite(a.data,item);dismiss();a.renderKeep();}),
            action("◷","Viewing dates",()->choose(item,6)),
            action("💬","Comments",()->choose(item,4)),
            action("ⓘ","Description",()->choose(item,13)),
            action("⌕","Correct match",()->choose(item,11))));
        boolean dropped=item.optString("status").equals("Dropped");
        s.link(dropped?"Bring back":"Stop showing (I dropped this)",false,true,()->{if(dropped)a.tracker.bringBack(item);else a.tracker.stopShowing(item);});
        s.dialog.setOnDismissListener(d->{if(dialog==s.dialog){dialog=null;sheet=null;}});s.show();
    }

    void choose(JSONObject item,int option){dismiss();a.option(item,option);}
    NoirSheet.Tile listTile(JSONObject item){NoirSheet.Tile t=action(item.optBoolean("listed")?"✓":"＋","My List",()->{JSONObject fresh=a.data.item(item.optString("id"));boolean listed=fresh!=null&&fresh.optBoolean("listed");JSONObject op=a.data.operation(item.optString("id"),"list");DataStore.put(op,"value",!listed);a.data.queue(op);a.data.syncSoon();a.listBadges(item.optString("group"),!listed);a.updateStatus();});t.state=()->{JSONObject fresh=a.data.item(item.optString("id"));return fresh!=null&&fresh.optBoolean("listed");};return t;}
    NoirSheet.Tile action(String icon,String label,Runnable run){return action(icon,label,run,true);}
    NoirSheet.Tile action(String icon,String label,Runnable run,boolean enabled){return new NoirSheet.Tile(icon,label,run,enabled);}

    void more(){PhoneExperience.settings(a);}
}
