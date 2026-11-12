package app.noir.mobile;

import android.app.*;
import android.content.*;
import android.os.*;
import android.view.*;
import androidx.test.platform.app.InstrumentationRegistry;
import org.json.*;
import org.junit.*;
import java.util.*;
import java.io.*;
import java.util.concurrent.*;
import android.graphics.*;
import android.widget.*;
import static org.junit.Assert.*;

/** TV screens with a remote. Uses the paired fixture library already on the device; skips when it is empty. */
public final class TvTest {
    final Instrumentation test=InstrumentationRegistry.getInstrumentation();
    Activity top;
    void key(int code){test.sendKeyDownUpSync(code);test.waitForIdleSync();}
    View focused(Activity a){final View[] v=new View[1];test.runOnMainSync(()->v[0]=a.getWindow().getCurrentFocus());return v[0];}
    Activity resumed(){final Activity[] r=new Activity[1];test.runOnMainSync(()->{for(Activity a:androidx.test.runner.lifecycle.ActivityLifecycleMonitorRegistry.getInstance().getActivitiesInStage(androidx.test.runner.lifecycle.Stage.RESUMED))r[0]=a;});return r[0];}
    Activity waitFor(Class<?> c,long ms)throws Exception{long end=SystemClock.elapsedRealtime()+ms;while(SystemClock.elapsedRealtime()<end){Activity a=resumed();if(c.isInstance(a))return a;Thread.sleep(50);}return null;}
    @Before public void onlyOnTv(){Assume.assumeTrue("TV-only tests",TvUi.isTv(test.getTargetContext()));}
    boolean hasLibrary(){return NoirApp.data!=null&&NoirApp.data.itemsShared().length()>0;}

    @Test public void homeTitleAndPlayerWorkWithTheRemote()throws Exception{
        Assume.assumeTrue("needs the paired fixture library",hasLibrary());
        long start=SystemClock.elapsedRealtime();
        Activity home=test.startActivitySync(new Intent(test.getTargetContext(),TvActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK|Intent.FLAG_ACTIVITY_CLEAR_TASK));
        try{
            long end=SystemClock.elapsedRealtime()+10000;boolean[] ready={false};while(SystemClock.elapsedRealtime()<end){test.runOnMainSync(()->ready[0]=((TvActivity)home).model!=null&&!((TvActivity)home).visibleRows.isEmpty());if(ready[0])break;Thread.sleep(25);}assertTrue("Home rows arrive",ready[0]);test.waitForIdleSync();android.util.Log.i("NoirTvTest","home_ready_ms="+(SystemClock.elapsedRealtime()-start));
            View first=focused(home);assertNotNull("Home has an initial focus",first);
            key(KeyEvent.KEYCODE_DPAD_RIGHT);View second=focused(home);assertNotNull(second);assertNotSame("→ moves along the row",first,second);
            key(KeyEvent.KEYCODE_DPAD_LEFT);key(KeyEvent.KEYCODE_DPAD_LEFT);key(KeyEvent.KEYCODE_DPAD_LEFT);
            View rail=focused(home);assertNotNull("← reaches the nav rail",rail);
            key(KeyEvent.KEYCODE_DPAD_RIGHT);assertNotNull(focused(home));
            key(KeyEvent.KEYCODE_DPAD_DOWN);assertNotNull("↓ keeps a focus",focused(home));key(KeyEvent.KEYCODE_DPAD_UP);
            key(KeyEvent.KEYCODE_DPAD_CENTER);
            Activity detail=waitFor(TvDetailActivity.class,5000);assertNotNull("OK on a tile opens the title page",detail);
            assertNotNull("Title page has an initial focus",focused(detail));
            key(KeyEvent.KEYCODE_BACK);assertNotNull("Back returns Home",waitFor(TvActivity.class,5000));
        }finally{Activity a=resumed();test.runOnMainSync(()->{if(a!=null&&a!=home)a.finish();home.finish();});}
    }

    @Test public void homeShellWorksBeforeProjectionAndThenFocusesFirstTile()throws Exception{
        Assume.assumeTrue("needs the paired fixture library",hasLibrary());DataStore data=NoirApp.data;Assume.assumeFalse(TvProfilesActivity.needed(data)||TvSetupActivity.needed(data));
        CountDownLatch blocked=new CountDownLatch(2),release=new CountDownLatch(1);for(int n=0;n<2;n++)data.images.execute(()->{blocked.countDown();try{release.await(20,TimeUnit.SECONDS);}catch(InterruptedException ignored){}});Activity[] home={null};
        try{assertTrue("projection workers blocked",blocked.await(10,TimeUnit.SECONDS));home[0]=test.startActivitySync(new Intent(test.getTargetContext(),TvActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK|Intent.FLAG_ACTIVITY_CLEAR_TASK));TvActivity a=(TvActivity)home[0];
            test.runOnMainSync(()->{assertNull("initial projection stays off the UI thread",a.model);assertNotNull("rail renders immediately",a.rail);assertTrue("loading placeholder renders",a.content.getChildCount()>0);a.showPage("My List",true);assertEquals("My List",a.page);a.showPage("Home",true);});
            release.countDown();long end=SystemClock.elapsedRealtime()+10000;boolean[] ready={false};while(SystemClock.elapsedRealtime()<end){test.runOnMainSync(()->ready[0]=a.model!=null&&!a.visibleRows.isEmpty());if(ready[0])break;Thread.sleep(25);}assertTrue("rows arrive after worker release",ready[0]);test.waitForIdleSync();test.runOnMainSync(()->assertSame("first tile receives initial focus",a.visibleRows.get(0).tiles.get(0),a.getCurrentFocus()));
        }finally{release.countDown();test.runOnMainSync(()->{if(home[0]!=null)home[0].finish();});}
    }

    @Test public void hiddenActionsAreSignpostedAndEmptyPagesKeepFocus()throws Exception{
        Assume.assumeTrue("needs the paired fixture library",hasLibrary());
        TvActivity a=(TvActivity)test.startActivitySync(new Intent(test.getTargetContext(),TvActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK|Intent.FLAG_ACTIVITY_CLEAR_TASK));
        try{long end=SystemClock.elapsedRealtime()+10000;boolean[] ready={false};while(SystemClock.elapsedRealtime()<end){test.runOnMainSync(()->ready[0]=a.model!=null&&a.body!=null);if(ready[0])break;Thread.sleep(25);}assertTrue("Home model arrives",ready[0]);
            JSONObject item=NoirApp.data.itemsShared().getJSONObject(0);
            test.runOnMainSync(()->{TvRows.Row row=new TvRows.Row("hint-test","Continue watching",true,true);row.add(new TvRows.Tile(item,""));a.addHomeRow(row);LinearLayout wrapper=(LinearLayout)a.visibleRows.get(a.visibleRows.size()-1).view;a.visibleRows.get(a.visibleRows.size()-1).tiles.get(0).requestFocus();assertTrue("hold-OK rows say so once focused (U110)",((TextView)wrapper.getChildAt(0)).getText().toString().contains(Loc.t("Hold OK for options")));
                TvRows.Row plain=new TvRows.Row("plain-test","Recently added",false,false);plain.add(new TvRows.Tile(item,""));a.addHomeRow(plain);wrapper=(LinearLayout)a.visibleRows.get(a.visibleRows.size()-1).view;a.visibleRows.get(a.visibleRows.size()-1).tiles.get(0).requestFocus();assertFalse("rows without a menu stay quiet",((TextView)wrapper.getChildAt(0)).getText().toString().contains(Loc.t("Hold OK for options")));
                a.showPage("My List",true);a.body.removeAllViews();a.visibleRows.clear();a.gridTiles=new ArrayList<>();a.gridCount=0;a.appendGrid();a.restoreFocus(false);});
            test.waitForIdleSync();View f=focused(a);assertTrue("empty My List focuses its Search button: "+f,f instanceof TextView&&((TextView)f).getText().toString().equals(Loc.t("Search")));
            assertTrue("empty My List explains how to fill it",textOf(a.body).contains(Loc.t("Open any title and choose ＋ My List to save it here.")));
            key(KeyEvent.KEYCODE_DPAD_LEFT);assertTrue("◀ returns to the rail",TvActivity.inside(a.rail,focused(a)));
            test.runOnMainSync(()->f.requestFocus());test.waitForIdleSync();key(KeyEvent.KEYCODE_DPAD_CENTER);test.runOnMainSync(()->assertEquals("Search button opens Search","Search",a.page));
            test.runOnMainSync(()->{a.query="";a.showPage("Search",true);});test.waitForIdleSync();assertFalse("no 'No results' before typing",textOf(a.body).contains(Loc.t("No results yet")));
        }finally{test.runOnMainSync(a::finish);}
    }

    /** D9: Arabic Home is mirrored like the title page: rail on the right, rows right to left, Left/Right swapped. English stays as it was. */
    @Test public void arabicHomeIsMirroredWithSwappedArrows()throws Exception{
        Assume.assumeTrue("needs the paired fixture library",hasLibrary());DataStore data=NoirApp.data;String before=data.prefs().optString("language","");boolean chosen=TvProfilesActivity.chosen;TvProfilesActivity.chosen=true;TvActivity[] open={null};
        try{for(boolean arabic:new boolean[]{false,true}){
                data.pref("language",arabic?"ar":"en");Loc.use(data);String where=arabic?"Arabic":"English";
                TvActivity a=(TvActivity)test.startActivitySync(new Intent(test.getTargetContext(),TvActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK|Intent.FLAG_ACTIVITY_CLEAR_TASK));open[0]=a;
                long end=SystemClock.elapsedRealtime()+10000;boolean[] ready={false};while(SystemClock.elapsedRealtime()<end){test.runOnMainSync(()->ready[0]=a.model!=null&&!a.visibleRows.isEmpty()&&a.visibleRows.get(0).tiles.size()>=2&&a.getCurrentFocus()==a.visibleRows.get(0).tiles.get(0));if(ready[0])break;Thread.sleep(25);}assertTrue(where+": Home rows arrive and the first tile has focus",ready[0]);test.waitForIdleSync();
                View[] tiles=new View[2];
                test.runOnMainSync(()->{tiles[0]=a.visibleRows.get(0).tiles.get(0);tiles[1]=a.visibleRows.get(0).tiles.get(1);
                    assertEquals(where+": Home layout direction",arabic?View.LAYOUT_DIRECTION_RTL:View.LAYOUT_DIRECTION_LTR,a.root.getLayoutDirection());
                    int[] rail=new int[2],content=new int[2],first=new int[2],second=new int[2];a.rail.getLocationOnScreen(rail);a.content.getLocationOnScreen(content);tiles[0].getLocationOnScreen(first);tiles[1].getLocationOnScreen(second);
                    assertEquals(where+": the rail is on the right only in Arabic (rail x "+rail[0]+", content x "+content[0]+")",arabic,rail[0]>content[0]);
                    assertEquals(where+": the second tile is left of the first only in Arabic (first x "+first[0]+", second x "+second[0]+")",arabic,second[0]<first[0]);
                    assertEquals(where+": rail names Home in the chosen language",arabic?"الرئيسية":"Home",a.nav.get(0).getContentDescription().toString());
                    assertEquals(where+": rail label text",arabic?"الرئيسية":"Home",((TextView)a.nav.get(0).getChildAt(1)).getText().toString());});
                int along=arabic?KeyEvent.KEYCODE_DPAD_LEFT:KeyEvent.KEYCODE_DPAD_RIGHT,toRail=arabic?KeyEvent.KEYCODE_DPAD_RIGHT:KeyEvent.KEYCODE_DPAD_LEFT;
                key(along);assertSame(where+": the arrow pointing away from the rail moves to the second tile",tiles[1],focused(a));
                key(toRail);assertSame(where+": the arrow pointing at the rail moves back to the first tile",tiles[0],focused(a));
                key(toRail);assertTrue(where+": the same arrow on the first tile opens the rail",TvActivity.inside(a.rail,focused(a)));
                key(toRail);assertTrue(where+": pressing it again stays on the rail",TvActivity.inside(a.rail,focused(a)));
                key(along);test.waitForIdleSync();assertTrue(where+": the other arrow returns to the content",TvActivity.inside(a.content,focused(a)));
                /* Walk the longest row to its end: every tile must scroll fully into view in either direction. */
                int[] longest={0};test.runOnMainSync(()->{for(int n=0;n<a.visibleRows.size();n++)if(!a.visibleRows.get(n).grid&&a.visibleRows.get(n).tiles.size()>a.visibleRows.get(longest[0]).tiles.size())longest[0]=n;a.visibleRows.get(longest[0]).tiles.get(0).requestFocus();});test.waitForIdleSync();
                /* A Discover row is rebuilt when its catalog arrives, so the row is looked up by id and the tile by index at every step. */
                String[] rowId={""};int[] count={0};boolean[] wider={false};test.runOnMainSync(()->{rowId[0]=a.visibleRows.get(longest[0]).id;count[0]=a.visibleRows.get(longest[0]).tiles.size();});
                for(int n=1;n<count[0];n++){key(along);Thread.sleep(200);test.waitForIdleSync();int index=n;
                    test.runOnMainSync(()->{TvActivity.RowViews row=null;for(TvActivity.RowViews r:a.visibleRows)if(r.id.equals(rowId[0]))row=r;assertNotNull(where+": row "+rowId[0]+" is still on Home",row);count[0]=row.tiles.size();if(index>=row.tiles.size())return;View tile=row.tiles.get(index);
                        assertSame(where+": tile "+index+" of row "+rowId[0]+" takes focus",tile,a.getCurrentFocus());
                        Rect seen=new Rect(),area=new Rect();assertTrue(tile.getGlobalVisibleRect(seen));a.content.getGlobalVisibleRect(area);int[] at=new int[2];View first=row.tiles.get(0);first.getLocationOnScreen(at);if(at[0]+first.getWidth()<area.left||at[0]>area.right)wider[0]=true;
                        assertTrue(where+": focused tile is scrolled into view (sees "+seen.width()+" of "+tile.getWidth()+" px)",seen.width()>=tile.getWidth()*9/10);});}
                Bundle walked=new Bundle();walked.putString("stream","home_row_walk "+where+" row="+rowId[0]+" tiles="+count[0]+" scrolled_past_first="+wider[0]+"\n");test.sendStatus(2,walked);
                test.runOnMainSync(a::finish);test.waitForIdleSync();open[0]=null;
            }
        }finally{TvActivity a=open[0];test.runOnMainSync(()->{if(a!=null)a.finish();});data.pref("language",before);Loc.use(data);TvProfilesActivity.chosen=chosen;}
    }

    @Test public void titlePageSignpostsEpisodeOptionsAndStreamChoice()throws Exception{
        Assume.assumeTrue("needs the paired fixture library",hasLibrary());
        JSONObject item=NoirApp.data.itemsShared().getJSONObject(0);
        TvDetailActivity d=(TvDetailActivity)test.startActivitySync(new Intent(test.getTargetContext(),TvDetailActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK).putExtra("group",item.optString("group")));
        try{test.waitForIdleSync();test.runOnMainSync(()->{
                assertEquals("episode hint only on shows",d.first!=null&&!d.first.optBoolean("movie"),d.seasonLine.getVisibility()==View.VISIBLE);
                assertEquals(Loc.t("Hold OK on an episode for more options"),d.episodeHint.getText().toString());
                if(d.stream.getVisibility()==View.VISIBLE)assertEquals("Stream offers the picker when Play already streams",d.playable(d.target)?"▶ "+Loc.t("Stream"):Loc.t("Choose stream"),d.stream.getText().toString());
            });
        }finally{test.runOnMainSync(d::finish);}
    }

    /** Opt-in (-e tvMedia …): audio/subtitles are a visible, focusable control, not only the ▲ key. */
    @Test public void playerTracksButtonIsReachableWithArrows()throws Exception{
        String path=InstrumentationRegistry.getArguments().getString("tvLocalMedia");assertNotNull("runner supplies pushed local TV media",path);assertTrue("local TV fixture exists",new File(path).isFile());
        String id="tv-local-tracks-fixture";NoirApp.data.rememberPhoneItem(new JSONObject().put("id",id).put("title","TV local tracks fixture").put("duration",600));NoirApp.data.localFile(id,android.net.Uri.fromFile(new File(path)).toString());
        PlayerActivity p=(PlayerActivity)test.startActivitySync(new Intent(test.getTargetContext(),PlayerActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK).putExtra("id",id).putExtra("restart",true));
        try{String outcome=waitForVideoOutcome(p,20000);assertEquals("Local video starts before testing arrows: "+outcome,"frame",outcome);
            test.runOnMainSync(()->{assertNotNull("TV has an Audio & subtitles button",p.tvTracksButton);assertTrue(p.tvTracksButton.isFocusable());assertTrue("remote hint shown",textOf(p.controls).contains("▼"));p.tvPlay(false);p.pause.requestFocus();});test.waitForIdleSync();
            for(int n=0;n<6&&focused(p)!=p.tvTracksButton;n++)key(KeyEvent.KEYCODE_DPAD_RIGHT);
            assertSame("→ from Pause reaches Audio & subtitles",p.tvTracksButton,focused(p));
            key(KeyEvent.KEYCODE_DPAD_CENTER);test.runOnMainSync(()->assertEquals("OK opens the tracks menu",1,p.tvPanelDepth));
            key(KeyEvent.KEYCODE_BACK);test.runOnMainSync(()->assertEquals("Back closes it",0,p.tvPanelDepth));
        }finally{test.runOnMainSync(p::finish);}
    }

    String waitForVideoOutcome(PlayerActivity p,long timeout)throws Exception{
        long end=SystemClock.elapsedRealtime()+timeout;String[] outcome={"pending"};
        while(SystemClock.elapsedRealtime()<end){test.runOnMainSync(()->{if(p.firstVideo)outcome[0]="frame";else if(p.playbackError&&p.message.getText().length()>0&&p.player!=null&&!p.player.isPlaying())outcome[0]="error";else if(p.destroyed||p.isFinishing())outcome[0]="unexpected finish";});if(!outcome[0].equals("pending"))return outcome[0];Thread.sleep(25);}
        return "hang";
    }
    @Test public void coldHttpStartReportsTiming()throws Exception{
        String base=InstrumentationRegistry.getArguments().getString("tvMedia");assertNotNull("runner supplies HTTP media",base);assertTrue("paired fixture library",hasLibrary());
        String id=DataStore.objects(NoirApp.data.itemsShared()).stream().filter(i->i.optString("title").equals("Bright pattern fixture")).findFirst().orElseThrow(()->new AssertionError("Missing bright fixture")).optString("id");
        String local=NoirApp.data.localFile(id);NoirApp.data.localFile(id,"");long start=SystemClock.elapsedRealtime();
        PlayerActivity p=(PlayerActivity)test.startActivitySync(new Intent(test.getTargetContext(),PlayerActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK).putExtra("id",id).putExtra("streamUrl",base+"i-avc-aac-srt.mkv").putExtra("streamRelease","cold HTTP timing").putExtra("restart",true));
        try{String outcome=waitForVideoOutcome(p,20000);Bundle status=new Bundle();test.runOnMainSync(()->{
                status.putString("stream","cold_http outcome="+outcome+" elapsed_ms="+(SystemClock.elapsedRealtime()-start)+" since_create_ms="+(SystemClock.elapsedRealtime()-p.createAt)+" first_vout="+p.firstVideo+" vouts="+p.vouts+" startup_failed="+p.tvStreamFailed+" buffer_since="+p.tvBufferSince+" state="+(p.player==null?-1:p.player.getPlayerState())+" message="+p.message.getText()+"\n");
            });test.sendStatus(2,status); /* 2 = progress note; 0 would be counted as an extra passed test by the runner ("Test count mismatch") */assertTrue("Cold HTTP ends in first Vout or a visible, stopped error: "+outcome,outcome.equals("frame")||outcome.equals("error"));
        }finally{test.runOnMainSync(p::finish);NoirApp.data.localFile(id,local);}
    }

    @Test public void artworkCoalescesAndCancelsReusedOrDetachedTargets()throws Exception{
        Activity host=test.startActivitySync(new Intent(test.getTargetContext(),TvSettingsActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
        File art=File.createTempFile("tv-art-",".png",test.getTargetContext().getCacheDir());Bitmap source=Bitmap.createBitmap(80,80,Bitmap.Config.RGB_565);try(FileOutputStream out=new FileOutputStream(art)){source.compress(Bitmap.CompressFormat.PNG,100,out);}source.recycle();
        CountDownLatch blocked=new CountDownLatch(2),release=new CountDownLatch(1);for(int n=0;n<2;n++)TvUi.IMAGES.execute(()->{blocked.countDown();try{release.await(10,TimeUnit.SECONDS);}catch(InterruptedException ignored){}});
        FrameLayout[] root={null};ImageView[] images=new ImageView[4];TvUi.ImageTarget[] targets=new TvUi.ImageTarget[4];
        try{assertTrue("image workers blocked",blocked.await(10,TimeUnit.SECONDS));
            test.runOnMainSync(()->{root[0]=new FrameLayout(host);host.setContentView(root[0]);for(int n=0;n<4;n++){images[n]=new ImageView(host);FrameLayout.LayoutParams lp=new FrameLayout.LayoutParams(80,80);lp.leftMargin=n*90;root[0].addView(images[n],lp);TvUi.loadSource(images[n],art,"",40,n==2?80:40);targets[n]=(TvUi.ImageTarget)images[n].getTag();}images[3].setTranslationX(100000);});
            test.waitForIdleSync();test.runOnMainSync(()->{
                for(TvUi.ImageTarget t:targets)if(t.watch!=null)t.watch.onPreDraw();
                assertNotNull(targets[0].job);assertSame("same source and size share a job",targets[0].job,targets[1].job);assertNotSame("height belongs to the cache key",targets[0].job,targets[2].job);assertNull("offscreen artwork stays unqueued",targets[3].job);
                TvUi.ImageJob shared=targets[0].job;TvUi.loadSource(images[0],null,"",40,40);assertNull("reuse cancels the old target",targets[0].job);assertFalse("other target keeps shared work alive",shared.cancelled);
                root[0].removeView(images[1]);assertTrue("last detach cancels shared work",shared.cancelled);assertNull(targets[1].job);assertTrue("queue remains bounded",TvUi.IMAGE_QUEUE.size()<=24);
                host.finish();
            });test.waitForIdleSync();test.runOnMainSync(()->assertNull("destroyed screen releases work",targets[2].job));
        }finally{release.countDown();test.runOnMainSync(host::finish);art.delete();}
    }

    @Test public void settingsTogglesWithTheRemote()throws Exception{
        Activity s=test.startActivitySync(new Intent(test.getTargetContext(),TvSettingsActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
        try{test.waitForIdleSync();assertNotNull("Settings has an initial focus",focused(s));boolean before=TvPrefs.passthrough(s);
            View f=focused(s);for(int n=0;n<6&&f!=null&&!String.valueOf(f.getContentDescription()).toLowerCase(Locale.ROOT).contains("passthrough")&&!textOf(f).toLowerCase(Locale.ROOT).contains("passthrough");n++){key(KeyEvent.KEYCODE_DPAD_DOWN);f=focused(s);}
            assertTrue("passthrough row reachable",f!=null&&(textOf(f)+f.getContentDescription()).toLowerCase(Locale.ROOT).contains("passthrough"));
            key(KeyEvent.KEYCODE_DPAD_CENTER);assertNotEquals("OK flips passthrough",before,TvPrefs.passthrough(s));key(KeyEvent.KEYCODE_DPAD_CENTER);assertEquals(before,TvPrefs.passthrough(s));
        }finally{test.runOnMainSync(s::finish);}
    }
    static String textOf(View v){StringBuilder b=new StringBuilder();if(v instanceof android.widget.TextView)b.append(((android.widget.TextView)v).getText());if(v instanceof ViewGroup)for(int n=0;n<((ViewGroup)v).getChildCount();n++)b.append(' ').append(textOf(((ViewGroup)v).getChildAt(n)));return b.toString();}

    /** Opt-in (-e tvMedia …): a stream that can't play hands over to the next one by itself, at the same place. */
    @Test public void brokenStreamSwitchesToTheNextOne()throws Exception{
        String base=InstrumentationRegistry.getArguments().getString("tvMedia");Assume.assumeTrue(base!=null&&hasLibrary());
        String id=NoirApp.data.itemsShared().getJSONObject(0).optString("id");
        JSONArray fallbacks=new JSONArray().put(new JSONObject().put("url",base+"i-avc-aac-srt.mkv").put("name","good").put("mbps",5));
        // startActivitySync may hand back the replacement player (same component), so tell them apart by their stream.
        test.getTargetContext().startActivity(new Intent(test.getTargetContext(),PlayerActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK).putExtra("id",id).putExtra("streamUrl",base+"missing.mkv").putExtra("streamRelease","broken").putExtra("streamFallbacks",fallbacks.toString()).putExtra("streamAttempt",0).putExtra("streamTotal",2).putExtra("restart",true));
        PlayerActivity next=null;long end=SystemClock.elapsedRealtime()+20000;
        while(SystemClock.elapsedRealtime()<end){Activity a=resumed();if(a instanceof PlayerActivity&&(base+"i-avc-aac-srt.mkv").equals(a.getIntent().getStringExtra("streamUrl"))){next=(PlayerActivity)a;if(next.firstVideo)break;}Thread.sleep(50);}
        try{assertNotNull("switched to the fallback stream",next);assertEquals(1,next.getIntent().getIntExtra("streamAttempt",0));assertTrue("fallback shows video",next.firstVideo);}
        finally{PlayerActivity n=next;test.runOnMainSync(()->{if(n!=null)n.finish();});}
    }

    /** Opt-in speed check: -e tvMedia http://10.0.2.2:18765/lan/ (the tv-speed range server). Logs first_vout per file. */
    @Test public void firstFrameSpeed()throws Exception{
        String base=InstrumentationRegistry.getArguments().getString("tvMedia");Assume.assumeTrue(base!=null&&hasLibrary());
        String id=NoirApp.data.itemsShared().getJSONObject(0).optString("id");
        for(String file:new String[]{"i-avc-aac-srt.mkv","ii-hevc10-eac3-ass.mkv","iii-avc-aac-tail.mp4"})for(int run=0;run<5;run++){
            PlayerActivity p=(PlayerActivity)test.startActivitySync(new Intent(test.getTargetContext(),PlayerActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK).putExtra("id",id).putExtra("streamUrl",base+file).putExtra("streamRelease","speed test").putExtra("restart",true));
            long end=SystemClock.elapsedRealtime()+10000;long ms=-1;while(SystemClock.elapsedRealtime()<end){if(p.firstVideo){ms=SystemClock.elapsedRealtime()-p.createAt;break;}Thread.sleep(5);}
            android.util.Log.i("NoirTvTest","speed file="+file+" run="+run+" first_vout_ms="+ms);
            test.runOnMainSync(p::finish);Thread.sleep(1500);
            assertTrue(file+" shows video",ms>0);
        }
    }
}
