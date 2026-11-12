package app.noir.mobile;

import android.app.*;
import android.content.*;
import android.graphics.Rect;
import android.os.*;
import android.text.Spanned;
import android.text.style.ImageSpan;
import android.view.*;
import android.widget.*;
import androidx.test.platform.app.InstrumentationRegistry;
import org.json.*;
import org.junit.*;
import java.io.File;
import java.util.*;
import static org.junit.Assert.*;

/** TV UI decisions U109–U116 (T1–T8). Uses the paired fixture library already on the device. */
public final class TvUiDecisionsTest {
    final Instrumentation test=InstrumentationRegistry.getInstrumentation();
    @Before public void onlyOnTv(){Assume.assumeTrue("TV-only tests",TvUi.isTv(test.getTargetContext()));}
    void key(int code){test.sendKeyDownUpSync(code);test.waitForIdleSync();}
    String language(String lang){DataStore data=NoirApp.data;String before=data.prefs().optString("language","");data.pref("language",lang);Loc.use(data);return before;}
    void restore(String before){NoirApp.data.pref("language",before);Loc.use(NoirApp.data);}
    TvActivity home()throws Exception{
        assertTrue("needs the paired fixture library",NoirApp.data!=null&&NoirApp.data.itemsShared().length()>0);
        TvActivity a=(TvActivity)test.startActivitySync(new Intent(test.getTargetContext(),TvActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK|Intent.FLAG_ACTIVITY_CLEAR_TASK));
        long end=SystemClock.elapsedRealtime()+10000;boolean[] ready={false};
        while(SystemClock.elapsedRealtime()<end){test.runOnMainSync(()->ready[0]=a.model!=null&&!a.visibleRows.isEmpty()&&!a.visibleRows.get(0).tiles.isEmpty()&&a.getCurrentFocus()==a.visibleRows.get(0).tiles.get(0));if(ready[0])break;Thread.sleep(25);}
        assertTrue("Home rows arrive and the first tile has focus",ready[0]);test.waitForIdleSync();return a;
    }
    static Rect seen(View v){Rect r=new Rect();if(!v.getGlobalVisibleRect(r))r.setEmpty();return r;}
    static Rect box(View v){int[] at=new int[2];v.getLocationOnScreen(at);return new Rect(at[0],at[1],at[0]+v.getWidth(),at[1]+v.getHeight());}
    View dialogOf(Activity act){View[] out={null};test.runOnMainSync(()->{for(View r:android.view.inspector.WindowInspector.getGlobalWindowViews())if(r!=act.getWindow().getDecorView()&&r.isShown()&&TvUi.imageActivity(r.getContext())==act)out[0]=r;});return out[0];}
    static View find(View root,java.util.function.Predicate<View> match){if(match.test(root))return root;if(root instanceof ViewGroup){ViewGroup g=(ViewGroup)root;for(int i=0;i<g.getChildCount();i++){View f=find(g.getChildAt(i),match);if(f!=null)return f;}}return null;}

    /** U109 (T1) and the Home part of U112 (T4): rows never draw over the hero, and the focused first tile's ring is not cut at the content edge, in both languages. */
    @Test public void homeRowsStayClearOfTheHeroAndTheFirstRingIsWhole()throws Exception{
        String before=language("en");boolean chosen=TvProfilesActivity.chosen;TvProfilesActivity.chosen=true;TvActivity[] open={null};
        try{for(String lang:new String[]{"en","ar"}){
                language(lang);TvActivity a=home();open[0]=a;Thread.sleep(400);test.waitForIdleSync();
                test.runOnMainSync(()->{View first=a.visibleRows.get(0).tiles.get(0);assertTrue(lang+": the focused tile grows",first.getScaleX()>1.01f);Rect r=seen(first);float drawn=first.getWidth()*first.getScaleX();
                    assertTrue(lang+": the first tile's focus ring is whole (sees "+r.width()+" of "+drawn+" px)",r.width()>=Math.floor(drawn)-1);});
                int[] rows={0};test.runOnMainSync(()->rows[0]=a.visibleRows.size());
                for(int n=0;n<Math.min(2,rows[0]-1);n++){key(KeyEvent.KEYCODE_DPAD_DOWN);Thread.sleep(300);}test.waitForIdleSync();
                test.runOnMainSync(()->{assertTrue(lang+": the page clips the row scroller to its own bounds",a.pageRoot.getClipChildren());View hero=a.pageRoot.getChildAt(0);Rect h=box(hero),s=box(a.scroll);
                    assertTrue(lang+": the hero ends where the rows begin ("+h.bottom+" <= "+s.top+")",h.bottom<=s.top);if(rows[0]>1)assertTrue(lang+": the page scrolled down",a.scroll.getScrollY()>0);
                    for(TvActivity.RowViews row:a.visibleRows){Rect label=row.label==null?new Rect():seen(row.label);if(!label.isEmpty())assertTrue(lang+": row "+row.id+" title stays below the hero ("+label.top+" < "+h.bottom+")",label.top>=h.bottom);
                        for(View tile:row.tiles){Rect r=seen(tile);if(!r.isEmpty())assertTrue(lang+": a tile of row "+row.id+" draws over the hero ("+r.top+" < "+h.bottom+")",r.top>=h.bottom);}}});
                test.runOnMainSync(a::finish);test.waitForIdleSync();open[0]=null;
            }
        }finally{TvActivity a=open[0];test.runOnMainSync(()->{if(a!=null)a.finish();});restore(before);TvProfilesActivity.chosen=chosen;}
    }

    /** U117: on grid pages (a See all shelf, Keep ready) every focused tile's ring is whole on all four sides — first and last column, first row — in both languages. */
    @Test public void gridTilesShowTheirWholeFocusRing()throws Exception{
        String before=language("en");boolean chosen=TvProfilesActivity.chosen;TvProfilesActivity.chosen=true;TvActivity[] open={null};
        try{for(String lang:new String[]{"en","ar"}){
                language(lang);TvActivity a=home();open[0]=a;
                test.runOnMainSync(()->{TvRows.Row pick=null;for(TvRows.Row r:a.model.rows)if(pick==null||r.allTiles.size()>pick.allTiles.size())pick=r;for(TvRows.Row r:a.discoveryRows.values())if(pick==null||r.allTiles.size()>pick.allTiles.size())pick=r;assertNotNull(lang+": Home has a row to open",pick);a.showShelf(pick);});
                Thread.sleep(500);test.waitForIdleSync();wholeRings(a,lang+" See all",2);
                test.runOnMainSync(()->a.showPage("Ready",true));Thread.sleep(500);test.waitForIdleSync();wholeRings(a,lang+" Keep ready",2);
                test.runOnMainSync(a::finish);test.waitForIdleSync();open[0]=null;
            }
        }finally{TvActivity a=open[0];test.runOnMainSync(()->{if(a!=null)a.finish();});restore(before);TvProfilesActivity.chosen=chosen;}
    }
    /** Focuses every tile of the first `rows` grid rows in turn and checks the scaled tile (its ring) is fully drawn. */
    void wholeRings(TvActivity a,String where,int rows)throws Exception{
        int[] counts=new int[rows];int[] found={0};test.runOnMainSync(()->{for(int n=0;n<rows&&n<a.visibleRows.size();n++){assertTrue(where+": a grid page",a.visibleRows.get(n).grid);counts[n]=a.visibleRows.get(n).tiles.size();found[0]=n+1;}});
        assertTrue(where+": the page has tiles",found[0]>0&&counts[0]>0);int checked=0;
        for(int n=0;n<found[0];n++)for(int k=0;k<counts[n];k++){int row=n,col=k;test.runOnMainSync(()->a.visibleRows.get(row).tiles.get(col).requestFocus());Thread.sleep(260);test.waitForIdleSync();
            test.runOnMainSync(()->{View t=a.visibleRows.get(row).tiles.get(col);assertSame(where+": tile "+row+"/"+col+" has focus",t,a.getCurrentFocus());assertTrue(where+": the focused tile grows",t.getScaleX()>1.01f);Rect r=seen(t);float w=t.getWidth()*t.getScaleX(),h=t.getHeight()*t.getScaleY();
                assertTrue(where+": ring of tile "+row+"/"+col+" is whole (sees "+r.width()+"×"+r.height()+" of "+w+"×"+h+")",r.width()>=Math.floor(w)-1&&r.height()>=Math.floor(h)-1);});checked++;}
        Bundle note=new Bundle();note.putString("stream","grid_rings "+where+" checked="+checked+"\n");test.sendStatus(2,note);
    }

    /** U110 (T2): the hold-OK hint is on the focused row only; rows without a menu never show it, and it goes when focus moves to the rail. */
    @Test public void holdHintFollowsTheFocusedRow()throws Exception{
        String before=language("en");boolean chosen=TvProfilesActivity.chosen;TvProfilesActivity.chosen=true;TvActivity a=home();
        try{JSONObject item=NoirApp.data.itemsShared().getJSONObject(0);String hint=Loc.t("Hold OK for options");TvActivity.RowViews[] r=new TvActivity.RowViews[3];
            test.runOnMainSync(()->{String[][] spec={{"hint-a","Continue watching","1"},{"hint-b","Up next","1"},{"hint-c","Recently added","0"}};
                for(int n=0;n<3;n++){boolean actions=spec[n][2].equals("1");TvRows.Row row=new TvRows.Row(spec[n][0],spec[n][1],true,actions);row.add(new TvRows.Tile(item,""));a.addHomeRow(row);r[n]=a.visibleRows.get(a.visibleRows.size()-1);}
                for(TvActivity.RowViews row:r)assertFalse("no hint before a row has focus: "+row.id,row.label.getText().toString().contains(hint));
                r[0].tiles.get(0).requestFocus();});test.waitForIdleSync();
            test.runOnMainSync(()->{assertTrue("focused row A shows the hint",r[0].label.getText().toString().contains(hint));assertFalse("row B does not",r[1].label.getText().toString().contains(hint));r[1].tiles.get(0).requestFocus();});test.waitForIdleSync();
            test.runOnMainSync(()->{assertFalse("row A drops it when focus leaves",r[0].label.getText().toString().contains(hint));assertEquals("row A keeps its title",r[0].title,r[0].label.getText().toString());assertTrue("focused row B shows it",r[1].label.getText().toString().contains(hint));r[2].tiles.get(0).requestFocus();});test.waitForIdleSync();
            test.runOnMainSync(()->{for(TvActivity.RowViews row:r)assertFalse("a row without a menu stays quiet, and the others drop it: "+row.id,row.label.getText().toString().contains(hint));r[0].tiles.get(0).requestFocus();});test.waitForIdleSync();
            /* A control outside the rows (Search field, More titles) takes the hint away too. */
            test.runOnMainSync(()->{assertTrue("row A shows it again",r[0].label.getText().toString().contains(hint));TextView other=TvUi.button(a,"Other",false,()->{});a.body.addView(other);other.requestFocus();});test.waitForIdleSync();
            test.runOnMainSync(()->{for(TvActivity.RowViews row:a.visibleRows)if(row.label!=null)assertFalse("no hint while a control outside the rows has focus: "+row.id,row.label.getText().toString().contains(hint));r[0].tiles.get(0).requestFocus();});test.waitForIdleSync();
            test.runOnMainSync(a::focusRail);test.waitForIdleSync();
            test.runOnMainSync(()->{for(TvActivity.RowViews row:a.visibleRows)if(row.label!=null)assertFalse("no hint while the rail has focus: "+row.id,row.label.getText().toString().contains(hint));});
        }finally{test.runOnMainSync(a::finish);restore(before);TvProfilesActivity.chosen=chosen;}
    }

    /** U111 review: the title page's episode hint comes back when the page first refreshes as a movie-like page (no episodes yet) and later as a show. */
    @Test public void episodeHintReturnsAfterARefreshWithoutEpisodes()throws Exception{
        assertTrue("needs the paired fixture library",NoirApp.data!=null&&NoirApp.data.itemsShared().length()>0);JSONObject show=null;
        for(int n=0;n<NoirApp.data.itemsShared().length();n++){JSONObject i=NoirApp.data.itemsShared().getJSONObject(n);if(!i.optBoolean("movie")){show=i;break;}}
        assertNotNull("the fixture library has an episode",show);
        TvDetailActivity d=(TvDetailActivity)test.startActivitySync(new Intent(test.getTargetContext(),TvDetailActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK).putExtra("group",show.optString("group")));
        try{test.waitForIdleSync();test.runOnMainSync(()->{assertEquals("a show starts with the hint",View.VISIBLE,d.episodeHint.getVisibility());JSONObject saved=d.first,movie=DataStore.copy(saved);DataStore.put(movie,"movie",true);
                d.first=movie;d.refresh(false);assertEquals("hidden while the page has no episodes",View.GONE,d.episodeHint.getVisibility());
                d.first=saved;d.refresh(false);assertEquals("shown again once episodes are there",View.VISIBLE,d.episodeHint.getVisibility());});
        }finally{test.runOnMainSync(d::finish);}
    }

    /** Checks one dialog's frame in Arabic: right-to-left, title in the right half, buttons in the right half. */
    void rightToLeft(String what,View decor){
        assertNotNull(what+" is open",decor);
        test.runOnMainSync(()->{assertEquals(what+": right-to-left frame",View.LAYOUT_DIRECTION_RTL,decor.getLayoutDirection());Rect d=box(decor);int middle=d.centerX();
            int titleId=decor.getResources().getIdentifier("alertTitle","id","android");View title=titleId==0?null:decor.findViewById(titleId);
            if(title instanceof TextView&&title.isShown()){TextView t=(TextView)title;Rect r=box(t);float left=t.getLayout()==null?r.left:r.left+t.getTotalPaddingLeft()+t.getLayout().getLineLeft(0);assertTrue(what+": title text starts in the right half ("+left+" vs middle "+middle+")",left>=middle-d.width()/6||r.left>=middle);}
            List<View> buttons=new ArrayList<>();for(int id:new int[]{android.R.id.button1,android.R.id.button2,android.R.id.button3}){View b=decor.findViewById(id);if(b!=null&&b.isShown())buttons.add(b);}
            if(!buttons.isEmpty()){int rightmost=Integer.MIN_VALUE;for(View b:buttons)rightmost=Math.max(rightmost,box(b).right);assertTrue(what+": buttons start from the right ("+rightmost+" vs middle "+middle+")",rightmost>middle+d.width()/4);}
            View positive=decor.findViewById(android.R.id.button1),negative=decor.findViewById(android.R.id.button2);
            if(positive!=null&&negative!=null&&positive.isShown()&&negative.isShown())assertTrue(what+": the main button is to the right of Cancel",box(positive).left>box(negative).left);});
    }

    /** U112 (T4): Arabic tile menu, PIN keypad, pairing and stream setup use a right-to-left frame; the keypad keeps 1-2-3 left to right and none of its keys is cut, focused or not. */
    @Test public void arabicDialogsUseARightToLeftFrame()throws Exception{
        String before=language("ar");boolean chosen=TvProfilesActivity.chosen;TvProfilesActivity.chosen=true;TvActivity a=home();
        try{test.runOnMainSync(()->a.tileMenu(a.visibleRows.get(0).values.get(0)));test.waitForIdleSync();Thread.sleep(300);
            rightToLeft("tile menu",dialogOf(a));key(KeyEvent.KEYCODE_BACK);
            TvKeypad[] pad={null};test.runOnMainSync(()->pad[0]=new TvKeypad(a,"Guest",(pin,keypad)->{}));test.waitForIdleSync();Thread.sleep(300);
            try{View decor=pad[0].dialog.getWindow().getDecorView();rightToLeft("PIN keypad",decor);
                test.runOnMainSync(()->{LinearLayout top=(LinearLayout)pad[0].keys.getChildAt(0);assertEquals("keypad digits stay 1-2-3 left to right","1",((TextView)top.getChildAt(0)).getText().toString());
                    assertTrue("1 is left of 2",box(top.getChildAt(0)).left<box(top.getChildAt(1)).left);assertTrue("2 is left of 3",box(top.getChildAt(1)).left<box(top.getChildAt(2)).left);});
                for(int line=0;line<4;line++)for(int k=0;k<3;k++){int l=line,c=k;test.runOnMainSync(()->((LinearLayout)pad[0].keys.getChildAt(l)).getChildAt(c).requestFocus());Thread.sleep(220);test.waitForIdleSync();
                    test.runOnMainSync(()->{View b=((LinearLayout)pad[0].keys.getChildAt(l)).getChildAt(c);Rect r=seen(b);float w=b.getWidth()*b.getScaleX(),h=b.getHeight()*b.getScaleY();
                        assertTrue("focused key "+((TextView)b).getText()+" is whole (sees "+r.width()+"×"+r.height()+" of "+w+"×"+h+")",r.width()>=Math.floor(w)-1&&r.height()>=Math.floor(h)-1);});}
            }finally{test.runOnMainSync(()->{if(pad[0]!=null)pad[0].dialog.dismiss();});}
            test.runOnMainSync(a::finish);test.waitForIdleSync();
            TvSetupActivity host=(TvSetupActivity)test.startActivitySync(new Intent(test.getTargetContext(),TvSetupActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
            try{test.runOnMainSync(()->TvSetupActivity.pair(host,()->{}));test.waitForIdleSync();Thread.sleep(300);View d=dialogOf(host);rightToLeft("pairing",d);
                test.runOnMainSync(()->{View field=find(d,v->v instanceof EditText);assertNotNull("pairing has its address field",field);assertEquals("the address field stays left to right",View.TEXT_DIRECTION_LTR,field.getTextDirection());});
                key(KeyEvent.KEYCODE_BACK);test.waitForIdleSync();Thread.sleep(200);
                test.runOnMainSync(()->TvSetupActivity.stream(host,()->{}));test.waitForIdleSync();Thread.sleep(300);rightToLeft("stream setup",dialogOf(host));key(KeyEvent.KEYCODE_BACK);
            }finally{test.runOnMainSync(host::finish);}
        }finally{test.runOnMainSync(()->{if(!a.isFinishing())a.finish();});restore(before);TvProfilesActivity.chosen=chosen;}
    }

    /** U113, U114 and U116 strings: the Arabic names on the TV, and the drawn icons standing in for text characters. */
    @Test public void arabicStringsAndDrawnIcons()throws Exception{
        String before=language("ar");boolean chosen=TvProfilesActivity.chosen;TvProfilesActivity.chosen=true;TvActivity a=home();
        try{assertEquals("Keep ready in Arabic, as on the phone","جاهز للمشاهدة",Loc.t("Keep ready"));assertEquals("Movie in Arabic","فيلم",Loc.t("Movie"));
            test.runOnMainSync(()->{int ready=Arrays.asList(a.pages).indexOf("Ready");LinearLayout b=a.nav.get(ready);
                assertEquals("rail names the page Keep ready","جاهز للمشاهدة",b.getContentDescription().toString());assertEquals("rail label","جاهز للمشاهدة",((TextView)b.getChildAt(1)).getText().toString());
                a.showPage("Ready",true);});test.waitForIdleSync();
            test.runOnMainSync(()->{TextView heading=(TextView)a.pageRoot.getChildAt(0);assertEquals("page heading","جاهز للمشاهدة",heading.getText().toString());});
            /* U114: every glyph that stands for an icon becomes a drawn icon; the character stays for screen readers. */
            test.runOnMainSync(()->{for(String g:new String[]{"✓","✕","＋","▲","▼","◀","▶","›"}){TextView t=TvUi.text(a,g+" x",16,TvUi.WHITE,false);CharSequence s=t.getText();assertTrue(g+" keeps its character",s.toString().startsWith(g));assertTrue(g+" is drawn",s instanceof Spanned&&((Spanned)s).getSpans(0,1,ImageSpan.class).length==1);}
                assertEquals("› points forward, so it mirrors in Arabic",View.LAYOUT_DIRECTION_RTL,((Spanned)TvUi.text(a,"›",16,TvUi.WHITE,false).getText()).getSpans(0,1,ImageSpan.class)[0].getDrawable().getLayoutDirection());
                assertTrue("› is drawn mirrored",((Spanned)TvUi.text(a,"›",16,TvUi.WHITE,false).getText()).getSpans(0,1,ImageSpan.class)[0].getDrawable().isAutoMirrored());});
            test.runOnMainSync(a::finish);test.waitForIdleSync();
            /* U115: TV check codec lines start at the right in Arabic, with a drawn mark, and codec names stay left to right. */
            TvCheckActivity check=(TvCheckActivity)test.startActivitySync(new Intent(test.getTargetContext(),TvCheckActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
            try{long end=SystemClock.elapsedRealtime()+10000;boolean[] up={false};while(SystemClock.elapsedRealtime()<end&&!up[0]){test.runOnMainSync(()->up[0]=check.caps!=null&&check.video.getChildCount()>1);Thread.sleep(50);}assertTrue("TV check shows its codecs",up[0]);test.waitForIdleSync();
                test.runOnMainSync(()->{LinearLayout line=(LinearLayout)check.video.getChildAt(1);assertTrue("the mark is drawn",line.getChildAt(0) instanceof ImageView);TextView name=(TextView)line.getChildAt(1);
                    assertEquals("codec names stay left to right",View.TEXT_DIRECTION_LTR,name.getTextDirection());Rect card=box(check.video),mark=box(line.getChildAt(0));
                    assertTrue("the mark sits at the card's right side ("+mark.right+" in "+card+")",mark.centerX()>card.centerX());assertTrue("the name is left of its mark",box(name).right<=mark.left+1);});
            }finally{test.runOnMainSync(check::finish);}
            /* U113: the player top bar says فيلم and starts at the right. */
            String path=InstrumentationRegistry.getArguments().getString("tvLocalMedia");assertNotNull("runner supplies pushed local TV media",path);
            String id="tv-ui-decisions-movie";NoirApp.data.rememberPhoneItem(new JSONObject().put("id",id).put("title","Decision movie").put("movie",true).put("duration",600));NoirApp.data.localFile(id,android.net.Uri.fromFile(new File(path)).toString());
            PlayerActivity p=(PlayerActivity)test.startActivitySync(new Intent(test.getTargetContext(),PlayerActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK).putExtra("id",id).putExtra("restart",true));
            try{test.waitForIdleSync();test.runOnMainSync(()->{TextView title=(TextView)p.top.getChildAt(0);assertEquals("top bar in Arabic","Decision movie · فيلم",title.getText().toString());
                    assertEquals("top bar runs right to left",View.LAYOUT_DIRECTION_RTL,p.top.getLayoutDirection());assertEquals("title aligned to the right side",View.TEXT_ALIGNMENT_VIEW_START,title.getTextAlignment());
                    TextView hint=(TextView)find(p.controls,v->v instanceof TextView&&((TextView)v).getText().toString().contains("▲"));assertNotNull("remote hint",hint);assertTrue("remote hint arrows are drawn",hint.getText() instanceof Spanned&&((Spanned)hint.getText()).getSpans(0,hint.length(),ImageSpan.class).length>=4);});
            }finally{test.runOnMainSync(p::finish);}
        }finally{test.runOnMainSync(()->{if(!a.isFinishing())a.finish();});restore(before);TvProfilesActivity.chosen=chosen;}
    }
}
