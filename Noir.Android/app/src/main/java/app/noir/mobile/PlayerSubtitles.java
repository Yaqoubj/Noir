package app.noir.mobile;

import android.app.*;
import android.net.Uri;
import android.view.*;
import android.widget.*;
import org.json.*;
import org.videolan.libvlc.MediaPlayer;
import org.videolan.libvlc.interfaces.IMedia;
import java.util.*;
import java.util.concurrent.*;

/** Player subtitle controls and background work; native calls stay on the UI thread. */
final class PlayerSubtitles {
    final PlayerActivity host;final DataStore data;final SubtitleClient client;final String id;
    final ExecutorService work=Executors.newSingleThreadExecutor();final Set<String> attached=new HashSet<>();
    boolean manual=false,playing=false,busy=false,closed=false;String trackSignature="",notice="";JSONObject selectedFile;
    AlertDialog dialog;LinearLayout body;TextView status;
    PlayerSubtitles(PlayerActivity host){this.host=host;data=host.data;id=host.item.optString("id");client=new SubtitleClient(data,host.item);}
    void start(){attachSaved(false);work.execute(()->{client.refreshFiles();host.runOnUiThread(()->{if(!closed){attachSaved(false);refresh();}});});}
    void playing(){playing=true;host.player.setSpuDelay((long)(data.subtitleDelay(id)*1_000_000));attachSaved(false);poll();}
    void poll(){if(closed||!playing||host.player==null||manual)return;MediaPlayer.TrackDescription[] tracks=host.player.getSpuTracks();if(tracks==null)return;StringBuilder signature=new StringBuilder();for(MediaPlayer.TrackDescription t:tracks)signature.append(t.id).append(':').append(t.name).append('|');String current=signature.toString();if(current.equals(trackSignature))return;trackSignature=current;
        String language=data.subtitleLanguage(id);if(language.equals("Off")){host.player.setSpuTrack(-1);return;}int chosen=-1,best=-1,usable=0,only=-1;for(MediaPlayer.TrackDescription t:tracks)if(t.id>=0){usable++;only=t.id;int score=PlayerActivity.subtitleScore(t.name,language);if(score>best){best=score;chosen=t.id;}}
        // An unlabelled single track is usable; unknown multi-language tracks need an explicit choice.
        if(chosen<0&&usable==1&&PlayerActivity.subtitleScore(tracks[tracks.length-1].name,language.equals("Arabic")?"English":"Arabic")<0)chosen=only;
        if(chosen>=0)host.player.setSpuTrack(chosen);refresh();
    }
    void attachSaved(boolean force){if(closed||host.player==null||!playing)return;String language=data.subtitleLanguage(id);boolean selected=false;List<JSONObject> saved=client.files();String preferred=data.preferredSubtitle(id);saved.sort(Comparator.comparingInt(s->s.optString("id").equals(preferred)?0:1));for(JSONObject sub:saved){
        if(attached.contains(sub.optString("id")))continue;boolean use=!manual&&!selected&&!preferred.equals("embedded")&&!language.equals("Off")&&(PlayerActivity.subtitleScore(sub.optString("name"),language)>=0||sub.optString("name").equals("External subtitles"));
        if(host.player.addSlave(IMedia.Slave.Type.Subtitle,Uri.fromFile(data.subtitle(sub)),use)){attached.add(sub.optString("id"));if(use){selected=true;selectedFile=sub;manual=true;}}}
        if(force&&selectedFile!=null)selectFile(selectedFile);}
    void selectFile(JSONObject sub){if(closed||host.player==null)return;manual=true;String lang=sub.optString("language");if(lang.isEmpty())lang=PlayerActivity.subtitleScore(sub.optString("name"),"Arabic")>=0?"ar":"en";data.subtitlePreference(id,SubtitleClient.label(lang),data.subtitleDelay(id));if(host.player.addSlave(IMedia.Slave.Type.Subtitle,Uri.fromFile(data.subtitle(sub)),true)){selectedFile=sub;data.subtitleFilePreference(id,sub.optString("id"));attached.add(sub.optString("id"));host.player.setSpuDelay((long)(data.subtitleDelay(id)*1_000_000));feedback("Using "+sub.optString("name"));}else feedback("The subtitle is saved. Retry selecting it after playback starts.");refresh();}
    void show(){host.handler.removeCallbacks(host.hide);LinearLayout panel=new LinearLayout(host);panel.setOrientation(LinearLayout.VERTICAL);panel.setPadding(host.dp(18),host.dp(8),host.dp(18),host.dp(12));ScrollView scroll=new ScrollView(host);scroll.addView(panel);body=panel;dialog=new AlertDialog.Builder(host).setTitle("Subtitles").setView(scroll).setNegativeButton("Close",(d,w)->host.show()).create();dialog.setOnDismissListener(d->{dialog=null;host.show();});dialog.show();refresh();}
    void refresh(){if(dialog==null||!dialog.isShowing()||body==null||closed)return;body.removeAllViews();status=host.text(busy?"Working in the background…":!notice.isEmpty()?notice:data.subtitleLanguage(id)+" · timing "+String.format(Locale.ROOT,"%+.2fs",data.subtitleDelay(id)),13);status.setPadding(0,host.dp(8),0,host.dp(12));body.addView(status);
        MediaPlayer.TrackDescription[] tracks=host.player.getSpuTracks();RadioGroup group=new RadioGroup(host);body.addView(group);RadioButton off=radio("Off");group.addView(off);Map<Integer,MediaPlayer.TrackDescription> ids=new HashMap<>();if(host.player.getSpuTrack()<0)group.check(off.getId());
        if(tracks!=null)for(MediaPlayer.TrackDescription track:tracks)if(track.id>=0){RadioButton choice=radio(track.name);group.addView(choice);ids.put(choice.getId(),track);if(track.id==host.player.getSpuTrack())group.check(choice.getId());}
        group.setOnCheckedChangeListener((g,checked)->{manual=true;selectedFile=null;data.subtitleFilePreference(id,"embedded");if(checked==off.getId()){host.player.setSpuTrack(-1);data.subtitlePreference(id,"Off",data.subtitleDelay(id));}else{MediaPlayer.TrackDescription track=ids.get(checked);if(track!=null){host.player.setSpuTrack(track.id);String language=PlayerActivity.subtitleScore(track.name,"Arabic")>=0?"Arabic":PlayerActivity.subtitleScore(track.name,"English")>=0?"English":data.subtitleLanguage(id).equals("Off")?"English":data.subtitleLanguage(id);data.subtitlePreference(id,language,data.subtitleDelay(id));}}});
        List<JSONObject> saved=client.files();if(!saved.isEmpty()){body.addView(host.text("Saved for offline use",15));for(JSONObject sub:saved)body.addView(action(sub.optString("name")+(sub.optBoolean("aligned")?" · synced":""),()->selectFile(sub)));}
        body.addView(action("Download English subtitles",()->search("en")));body.addView(action("Download Arabic subtitles",()->search("ar")));
        body.addView(action("Smart sync saved subtitles",this::syncMenu));body.addView(action("Adjust timing",this::timing));
        TextView note=host.text("Downloads match this movie or episode. Smart sync checks timing on your paired PC and keeps the original when uncertain. Saved subtitles and timing controls work offline.",12);note.setPadding(0,host.dp(12),0,0);body.addView(note);
    }
    RadioButton radio(String label){RadioButton b=new RadioButton(host);b.setId(View.generateViewId());b.setText(label);b.setTextColor(MainActivity.WHITE);return b;}
    Button action(String label,Runnable task){Button b=host.button(label,task);b.setLayoutParams(new LinearLayout.LayoutParams(-1,-2));b.setEnabled(!busy);return b;}
    interface Task{void run()throws Exception;}
    void run(String message,Task task){if(busy||closed)return;busy=true;refresh();feedback(message);work.execute(()->{try{task.run();}catch(Exception e){host.runOnUiThread(()->feedback(e.getMessage()==null?"Couldn't finish subtitle request. Try again.":e.getMessage()));}finally{host.runOnUiThread(()->{busy=false;if(!closed)refresh();});}});}
    void feedback(String text){if(closed)return;notice=text;if(status!=null&&dialog!=null&&dialog.isShowing())status.setText(text);else Toast.makeText(host,text,Toast.LENGTH_LONG).show();}
    void search(String lang){run("Finding "+SubtitleClient.label(lang)+" subtitles…",()->{List<JSONObject> found=client.search(lang);host.runOnUiThread(()->{if(closed)return;busy=false;if(found.isEmpty()){feedback("No "+SubtitleClient.label(lang)+" subtitle found. Try again later.");return;}List<JSONObject> choices=found.subList(0,Math.min(12,found.size()));String[] labels=new String[choices.size()];for(int n=0;n<choices.size();n++){JSONObject c=choices.get(n);String release=c.optString("movieReleaseName");if(release.isEmpty())release=c.optString("subtitleFileName","Available release");labels[n]=(c.optString("m").equals("h")?"Exact video match · ":n==0?"Best available match · ":"")+release;}new AlertDialog.Builder(host).setTitle(SubtitleClient.label(lang)+" subtitles").setItems(labels,(d,n)->download(choices.get(n))).setNegativeButton("Cancel",null).show();});});}
    void download(JSONObject choice){run("Downloading subtitles…",()->{JSONObject sub=client.download(choice);final JSONObject downloaded=sub;host.runOnUiThread(()->{if(!closed){selectFile(downloaded);feedback("Subtitle saved. Checking timing…");}});String result=sub.optBoolean("hashMatch")?"Exact video match · saved for offline use":"Subtitle saved · timing unchanged";
        if(!sub.optBoolean("hashMatch")&&data.paired()){try{sub=client.sync(sub);result="Subtitle saved · timing checked";}catch(Exception e){result="Subtitle saved · "+(e.getMessage()==null?"timing unavailable":e.getMessage());}}
        final JSONObject ready=sub;final String text=result;host.runOnUiThread(()->{if(!closed){if(ready.optBoolean("aligned"))data.subtitlePreference(id,data.subtitleLanguage(id),0);selectFile(ready);feedback(text);}});});}
    void syncMenu(){List<JSONObject> files=client.files();if(files.isEmpty()){feedback("Download or save a subtitle first. Embedded tracks already use the release's timing.");return;}String[] labels=new String[files.size()];for(int n=0;n<files.size();n++)labels[n]=files.get(n).optString("name");new AlertDialog.Builder(host).setTitle("Choose subtitle to sync").setItems(labels,(d,n)->run("Checking subtitle timing on PC…",()->{JSONObject synced=client.sync(files.get(n));host.runOnUiThread(()->{if(!closed){data.subtitlePreference(id,data.subtitleLanguage(id),0);selectFile(synced);feedback("Timing checked · original backed up on phone");}});})).setNegativeButton("Cancel",null).show();}
    void timing(){LinearLayout panel=new LinearLayout(host);panel.setOrientation(LinearLayout.VERTICAL);panel.setPadding(host.dp(20),host.dp(12),host.dp(20),host.dp(12));TextView value=host.text("",18);panel.addView(value);Runnable update=()->value.setText(String.format(Locale.ROOT,"Subtitle timing: %+.2fs",data.subtitleDelay(id)));update.run();panel.addView(host.text("If text appears late, choose Earlier. If text appears too soon, choose Later.",13));for(String label:new String[]{"Earlier · −0.5s","Later · +0.5s","Reset timing"})panel.addView(host.button(label,()->{double delay=label.startsWith("Reset")?0:data.subtitleDelay(id)+(label.startsWith("Earlier")?-.5:.5);data.subtitlePreference(id,data.subtitleLanguage(id),delay);host.player.setSpuDelay((long)(data.subtitleDelay(id)*1_000_000));update.run();}));new AlertDialog.Builder(host).setTitle("Subtitle timing").setView(panel).setPositiveButton("Done",(d,w)->refresh()).show();}
    void close(){closed=true;work.shutdownNow();if(dialog!=null)dialog.dismiss();}
}
