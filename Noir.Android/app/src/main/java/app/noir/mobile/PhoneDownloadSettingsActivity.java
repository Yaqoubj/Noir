package app.noir.mobile;

import android.content.Intent;
import android.os.Bundle;
import android.view.View;
import android.view.WindowManager;
import android.widget.*;
import org.json.JSONObject;
import java.util.Arrays;
import java.util.concurrent.*;

public final class PhoneDownloadSettingsActivity extends PhoneScreen {
    Switch torrents,torrentMobile,torrentAutoSwitch;Spinner sharing;EditText upload;
    static {Loc.AR.put("Preferred uploader (optional)","الرافع المفضّل (اختياري)");}
    static {String[][] words={{"Download torrents directly (no debrid)","تنزيل التورنت مباشرة (دون خدمة وسيطة)"},{"Other people sharing a torrent can see your IP address.","يمكن للآخرين المشاركين في التورنت رؤية عنوان IP الخاص بك."},{"Use mobile data for torrents","استخدام بيانات الهاتف للتورنت"},{"Sharing back","المشاركة مع الآخرين"},{"Don't share","عدم المشاركة"},{"Until ratio 1.0 or 2 hours","حتى نسبة 1.0 أو ساعتين"},{"Until I stop it","حتى أوقفها"},{"Upload limit (KB/s)","حد الرفع (ك.ب/ث)"},{"Switch to another release if stuck","التبديل إلى إصدار آخر عند توقف التنزيل"},{"Check the sharing upload limit.","تحقق من حد الرفع للمشاركة."}};for(String[] w:words)Loc.AR.put(w[0],w[1]);}
    void torrentFields(){if(TvUi.isTv(this))return;torrents=DownloadHubActivity.toggle(this,"Download torrents directly (no debrid)",config.optBoolean("torrents"),null);body.addView(torrents);body.addView(text(Loc.t("Other people sharing a torrent can see your IP address."),12));torrentMobile=DownloadHubActivity.toggle(this,"Use mobile data for torrents",config.optBoolean("torrentMobile"),null);body.addView(torrentMobile);sharing=spinner(Loc.t("Sharing back"),new String[]{Loc.t("Don't share"),Loc.t("Until ratio 1.0 or 2 hours"),Loc.t("Until I stop it")},Loc.t("Until ratio 1.0 or 2 hours"));sharing.setSelection(config.optString("torrentSharing").equals("off")?0:config.optString("torrentSharing").equals("forever")?2:1);sharing.setContentDescription(Loc.t("Sharing back"));upload=input(Loc.t("Upload limit (KB/s)"),Integer.toString(config.optInt("torrentUploadKB",512)),false);upload.setInputType(android.text.InputType.TYPE_CLASS_NUMBER);upload.setContentDescription(Loc.t("Upload limit (KB/s)"));torrentAutoSwitch=DownloadHubActivity.toggle(this,"Switch to another release if stuck",config.optBoolean("torrentAutoSwitch",true),null);body.addView(torrentAutoSwitch);}
    void saveTorrents(JSONObject value){if(torrents==null)return;DataStore.put(value,"torrents",torrents.isChecked());DataStore.put(value,"torrentMobile",torrentMobile.isChecked());DataStore.put(value,"torrentSharing",new String[]{"off","ratio","forever"}[sharing.getSelectedItemPosition()]);DataStore.put(value,"torrentUploadKB",Integer.parseInt(upload.getText().toString()));DataStore.put(value,"torrentAutoSwitch",torrentAutoSwitch.isChecked());}
    final ExecutorService worker=Executors.newSingleThreadExecutor();PhoneDownloadSettings storage;JSONObject config;boolean copying,checking;EditText subtitleUploader,subtitleLink,subtitleKey,subtitleUser,subtitlePass,subtitleAuth;Switch subtitleOnly;TextView subtitleStatus,subtitleNote;Spinner subtitlePicker;Button subtitleGetKey;JSONObject siteKeys=new JSONObject();String selectedSubtitle="";boolean onlyWasEnabled;
    static final String[] SUBTITLE_IDS={"","opensubtitles","subdl","subsource","wyzie","addon"};
    /** Tests replace this so the Check button never touches the network. */
    static java.util.function.Supplier<SubtitleSources> sourcesFactory=()->new SubtitleSources(new SubtitleTransport());
    static void open(android.content.Context c,String url){try{c.startActivity(new Intent(Intent.ACTION_VIEW,android.net.Uri.parse(url)).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));}catch(Exception ignored){}}
    View labelOf(View field){int at=body.indexOfChild(field);return at>0?body.getChildAt(at-1):field;}
    void show(View field,boolean on){field.setVisibility(on?View.VISIBLE:View.GONE);labelOf(field).setVisibility(on?View.VISIBLE:View.GONE);}
    EditText described(EditText field,String label){field.setContentDescription(Loc.t(label));return field;}
    boolean onlyAllowed(){return selectedSubtitle.equals("addon")?!subtitleLink.getText().toString().trim().isEmpty():!selectedSubtitle.isEmpty()&&!subtitleKey.getText().toString().trim().isEmpty();}
    /** Auto-ticks "Use only this source" the moment a source becomes usable, as on the PC. */
    void refreshOnly(boolean typed){boolean allowed=onlyAllowed();if(typed&&allowed&&!onlyWasEnabled)subtitleOnly.setChecked(true);onlyWasEnabled=allowed;subtitleOnly.setEnabled(allowed);subtitleStatus.setText("");}
    void selectSubtitle(String id){
        if(id.equals(selectedSubtitle))return;
        if(Arrays.asList(SubtitleSources.SITES).contains(selectedSubtitle))DataStore.put(siteKeys,selectedSubtitle,subtitleKey.getText().toString());
        selectedSubtitle=id;boolean site=Arrays.asList(SubtitleSources.SITES).contains(id);subtitleKey.setText(siteKeys.optString(id));
        show(subtitleKey,site);show(subtitleUploader,!id.isEmpty());subtitleGetKey.setVisibility(site?View.VISIBLE:View.GONE);show(subtitleUser,id.equals("opensubtitles"));show(subtitlePass,id.equals("opensubtitles"));show(subtitleLink,id.equals("addon"));show(subtitleAuth,id.equals("addon"));subtitleNote.setVisibility(id.equals("wyzie")?View.VISIBLE:View.GONE);
        refreshOnly(true);
    }
    void subtitleControls(){
        SubtitleSources.Selection initial=SubtitleSources.Selection.from(config);selectedSubtitle=initial.source;
        JSONObject saved=config.optJSONObject("subtitleSiteKeys");siteKeys=saved==null?new JSONObject():DataStore.copy(saved);if(initial.site()&&!initial.apiKey.isEmpty())DataStore.put(siteKeys,initial.source,initial.apiKey);
        String[] names={Loc.t("Free default"),"OpenSubtitles.com","SubDL","SubSource","Wyzie Subs",Loc.t("Stremio addon")};
        subtitlePicker=spinner("Subtitle source",names,names[Math.max(0,Arrays.asList(SUBTITLE_IDS).indexOf(initial.source))]);subtitlePicker.setContentDescription(Loc.t("Subtitle source"));
        subtitleKey=described(input("Subtitle site API key",siteKeys.optString(initial.source),true),"Subtitle site API key");
        subtitleUser=described(input("Username · optional",initial.username,false),"Username · optional");subtitlePass=described(input("Password · optional",initial.password,true),"Password · optional");
        subtitleLink=described(input("Stremio addon link",config.optString("subtitleAddonUrl"),true),"Stremio addon link");subtitleAuth=described(input("Authorization · optional",initial.addonAuth,true),"Authorization · optional");
        subtitleNote=text("Your Wyzie key stays on this phone, encrypted.",12);body.addView(subtitleNote);
        subtitleGetKey=button(Loc.t("Get a free key"),()->{String url=SubtitleSources.keyPage(selectedSubtitle);if(!url.isEmpty())open(this,url);},false);subtitleGetKey.setContentDescription(Loc.t("Get a free key"));body.addView(subtitleGetKey);
        subtitleOnly=new Switch(this);subtitleOnly.setText(Loc.t("Use only this source"));subtitleOnly.setTextColor(MainActivity.WHITE);subtitleOnly.setChecked(config.optBoolean("subtitleAddonOnly",true));body.addView(subtitleOnly);subtitleUploader=described(input("Preferred uploader (optional)",config.optString("subtitleUploader"),false),"Preferred uploader (optional)");subtitleStatus=text("",13);subtitleStatus.setAccessibilityLiveRegion(View.ACCESSIBILITY_LIVE_REGION_POLITE);body.addView(subtitleStatus);
        android.text.TextWatcher watcher=new android.text.TextWatcher(){public void beforeTextChanged(CharSequence s,int start,int count,int after){}public void onTextChanged(CharSequence s,int start,int before,int count){refreshOnly(true);}public void afterTextChanged(android.text.Editable s){}};subtitleKey.addTextChangedListener(watcher);subtitleLink.addTextChangedListener(watcher);
        String first=selectedSubtitle;selectedSubtitle="\0";selectSubtitle(first);onlyWasEnabled=onlyAllowed();subtitleOnly.setEnabled(onlyWasEnabled);subtitleOnly.setChecked(config.optBoolean("subtitleAddonOnly",true));subtitleStatus.setText("");
        subtitlePicker.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener(){public void onItemSelected(AdapterView<?> parent,View view,int position,long id){selectSubtitle(SUBTITLE_IDS[position]);}public void onNothingSelected(AdapterView<?> parent){}});
    }
    SubtitleSources.Selection readSubtitle()throws java.io.IOException{return new SubtitleSources.Selection(selectedSubtitle,selectedSubtitle.isEmpty()||selectedSubtitle.equals("addon")?"":subtitleKey.getText().toString(),subtitleUser.getText().toString(),subtitlePass.getText().toString(),subtitleLink.getText().toString(),subtitleAuth.getText().toString(),subtitleOnly.isChecked()).validate();}
    @Override public void onCreate(Bundle state){super.onCreate(state);getWindow().addFlags(WindowManager.LayoutParams.FLAG_SECURE);storage=new PhoneDownloadSettings(this);render();}
    void render(){page("Phone download settings");try{config=storage.load();}catch(Exception e){feedback.setText("Couldn't decrypt settings on this installation.");return;}
        body.addView(text("Stream and download using this phone's internet connection. No PC or pairing required. Saved credentials are encrypted on this phone.",14));
        torrentFields();
        EditText manifest=input("AIOStreams manifest URL · required", "",true),torbox=input("TorBox API key · optional", "",true),rd=input("Real-Debrid API key · optional","",true);
        manifest.setHint(config.optString("manifest").isEmpty()?"HTTPS installation URL":"Saved · leave blank to keep");torbox.setHint(config.optString("torBoxKey").isEmpty()?"Optional":"Saved · leave blank to keep");rd.setHint(config.optString("realDebridKey").isEmpty()?"Optional":"Saved · leave blank to keep");
        Spinner resolution=spinner("Preferred resolution",new String[]{"1080p","720p","2160p","Any"},config.optString("preferredResolution","1080p"));Spinner language=spinner("Preferred audio language",new String[]{"Any","English","Arabic"},config.optString("language","Any"));EditText maximum=input("Maximum episode size in MB · 0 for no limit",Long.toString(config.optLong("maxEpisodeBytes")/1048576),false);maximum.setInputType(android.text.InputType.TYPE_CLASS_NUMBER);
        subtitleControls();
        body.addView(button(Loc.t("Check"),()->checkSubtitle(DataStore.copy(config),false),false));
        body.addView(button(Loc.t("Save subtitle source"),()->checkSubtitle(DataStore.copy(config),true),false));
        body.addView(button("Save on this phone",()->{try{JSONObject value=DataStore.copy(config);for(String[] entry:new String[][]{{"manifest",manifest.getText().toString().trim()},{"torBoxKey",torbox.getText().toString().trim()},{"realDebridKey",rd.getText().toString().trim()}})if(!entry[1].isEmpty())DataStore.put(value,entry[0],entry[1]);DataStore.put(value,"preferredResolution",resolution.getSelectedItem().toString());DataStore.put(value,"language",language.getSelectedItem().toString());DataStore.put(value,"maxEpisodeBytes",Math.multiplyExact(Long.parseLong(maximum.getText().toString()),1048576L));checkSubtitle(value,true);}catch(Exception e){feedback.setText("Couldn't save. Check the manifest URL and maximum size.");}},true));
        if(NoirApp.data.paired())body.addView(button("Copy download settings from PC",()->{if(copying||checking)return;copying=true;feedback.setText("Waiting for confirmation on your PC · up to 2 minutes…");worker.execute(()->{try{JSONObject value=storage.imported(NoirApp.data.pcRequest("/v1/phone-downloads/settings",new JSONObject()));storage.save(value);runOnUiThread(()->{copying=false;if(!isFinishing()){render();feedback.setText("Copied once and encrypted on this phone.");}});}catch(Exception e){runOnUiThread(()->{copying=false;if(!isFinishing())feedback.setText("Settings weren't shared. Keep the paired PC reachable and click Send there within 2 minutes.");});}});},false));
        body.addView(button("Clear saved download credentials",()->{if(checking||copying)return;new android.app.AlertDialog.Builder(this).setTitle("Clear phone download settings?").setNegativeButton("Cancel",null).setPositiveButton("Clear",(d,w)->{try{storage.save(new JSONObject());render();}catch(Exception e){feedback.setText("Couldn't clear settings.");}}).show();},false));
        body.addView(button("Phone downloads",()->startActivity(new Intent(this,DownloadsActivity.class)),false));
    }
    void checkSubtitle(JSONObject value,boolean save){
        if(save)try{saveTorrents(value);}catch(Exception e){feedback.setText(Loc.t("Check the sharing upload limit."));return;}
        if(checking||copying)return;SubtitleSources.Selection selection;
        try{selection=readSubtitle();}catch(java.io.IOException e){subtitleStatus.setText(Loc.t(e.getMessage()));return;}
        if(!selection.chosen()){if(save)try{PhoneDownloadSettings.applySubtitle(value,selection,siteKeys);DataStore.put(value,"subtitleUploader",subtitleUploader.getText().toString().trim());storage.save(value);render();feedback.setText(Loc.t("Subtitle source saved securely."));subtitleStatus.setText(Loc.t("Default subtitle source"));}catch(Exception e){subtitleStatus.setText(Loc.t("Couldn't save subtitle source."));}else subtitleStatus.setText(Loc.t("Default subtitle source"));return;}
        final SubtitleSources.Selection checked=selection;checking=true;subtitleStatus.setText(Loc.t("Checking subtitle source…"));SubtitleSources sources=sourcesFactory.get();
        worker.execute(()->{String message,name="";boolean ok=false;
            try{name=sources.check(checked);ok=true;message=checked.name()+" · "+Loc.t("works")+(name.isEmpty()?"":" · "+name);}
            catch(SubtitleSources.SubtitleFailure e){message=e.getMessage();}
            catch(java.io.IOException e){message=Loc.t(e.getMessage()==null?"Couldn't check the subtitle source. Check the key and connection.":e.getMessage());}
            catch(Exception e){message=Loc.t("Couldn't check the subtitle source. Check the key and connection.");}
            final String shown=message;final boolean works=ok;
            runOnUiThread(()->{checking=false;if(isFinishing())return;
                SubtitleSources.Selection now;try{now=readSubtitle();}catch(java.io.IOException e){now=null;}
                if(!checked.equals(now)){subtitleStatus.setText(Loc.t("Subtitle source changed. Check again."));return;}
                if(works&&save)try{PhoneDownloadSettings.applySubtitle(value,checked,siteKeys);DataStore.put(value,"subtitleUploader",subtitleUploader.getText().toString().trim());storage.save(value);render();feedback.setText(Loc.t("Subtitle source saved securely."));}catch(Exception e){subtitleStatus.setText(Loc.t("Couldn't save subtitle source."));return;}
                subtitleStatus.setText(shown);
            });
        });
    }
    @Override public void onDestroy(){worker.shutdownNow();super.onDestroy();}
}
