package app.noir.mobile;
import android.app.*;
import android.content.Intent;
import android.graphics.Typeface;
import android.net.Uri;
import android.os.*;
import android.text.TextUtils;
import android.view.*;
import android.webkit.*;
import android.widget.*;
import org.json.*;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.time.*;
import java.util.*;
import java.util.concurrent.*;

// Everything the PC's "Import" page does, on the phone: import files and lists, connect
// Stremio, Nuvio and media servers, export, back up and restore, and undo imports or whole sources.
public final class HistoryToolsActivity extends Activity {
    static final String BACKUP_FAILED="Couldn't save the safety backup. History was not changed. Check storage and try again.";
    static {Loc.AR.put(BACKUP_FAILED,"تعذر حفظ النسخة الاحتياطية الآمنة. لم يتغير السجل. تحقق من مساحة التخزين وحاول مجددًا.");}
    static final int OPEN_IMPORT=1,SAVE_EXPORT=2,SAVE_BACKUP=3,OPEN_RESTORE=4;
    final ExecutorService work=Executors.newSingleThreadExecutor();final Handler ui=new Handler(Looper.getMainLooper());
    long nuvioCursor;DataStore data;PhoneHistoryIo io;LinearLayout body;TextView status;String exportFormat="noir-json";PhoneHistoryIo.Plan plan;String paste="";volatile boolean busy;
    @Override public void onCreate(Bundle state){super.onCreate(state);data=NoirApp.data;io=new PhoneHistoryIo(data,new DiscoveryClient(this));render();}
    int dp(int v){return Math.round(v*getResources().getDisplayMetrics().density);}
    TextView text(String value,int size,int color){TextView t=new TextView(this);t.setText(Loc.t(value));t.setTextSize(size);t.setTextColor(color);t.setPadding(0,dp(4),0,dp(6));return t;}
    TextView heading(String value){TextView t=text(value,18,MainActivity.WHITE);t.setTypeface(null,Typeface.BOLD);t.setPadding(0,dp(18),0,dp(2));body.addView(t);return t;}
    void note(String value){body.addView(text(value,13,MainActivity.MUTED));}
    Button button(String value,Runnable action,boolean primary){Button b=new Button(this);b.setText(Loc.t(value));b.setAllCaps(false);b.setTextColor(primary?MainActivity.BLACK:MainActivity.WHITE);b.setBackground(MainActivity.bg(primary?MainActivity.GOLD:MainActivity.PANEL,dp(12)));b.setMinHeight(dp(48));b.setEnabled(!busy);b.setOnClickListener(v->action.run());Motion.card(b);LinearLayout.LayoutParams l=new LinearLayout.LayoutParams(-1,-2);l.setMargins(0,dp(5),0,dp(5));b.setLayoutParams(l);body.addView(b);return b;}
    void toast(String s){Toast.makeText(this,s,Toast.LENGTH_LONG).show();}
    void say(String s){ui.post(()->{if(status!=null)status.setText(s);});}
    void render(){
        String message=status==null?"":status.getText().toString();
        LinearLayout root=new LinearLayout(this);root.setOrientation(LinearLayout.VERTICAL);root.setFitsSystemWindows(true);root.setBackgroundColor(MainActivity.BLACK);root.setPadding(dp(20),dp(10),dp(20),dp(10));setContentView(root);
        LinearLayout header=new LinearLayout(this);header.setGravity(Gravity.CENTER_VERTICAL);Button back=new Button(this);back.setText("‹");back.setTextColor(MainActivity.WHITE);back.setBackground(MainActivity.bg(MainActivity.PANEL,dp(12)));back.setContentDescription("Back");back.setOnClickListener(v->onBackPressed());header.addView(back,new LinearLayout.LayoutParams(dp(48),dp(48)));
        TextView title=text("Import, export & backup",22,MainActivity.WHITE);title.setTypeface(null,Typeface.BOLD);title.setPadding(dp(14),0,0,0);header.addView(title);root.addView(header);
        ScrollView scroll=new ScrollView(this);body=new LinearLayout(this);body.setOrientation(LinearLayout.VERTICAL);scroll.addView(body);root.addView(scroll,new LinearLayout.LayoutParams(-1,0,1));status=text(message,12,MainActivity.MUTED);root.addView(status);
        if(plan!=null){review();return;}
        PhoneJustWatched.settings(this);
        heading("Import from another app");
        note("Netflix, Letterboxd, Trakt, TV Time, IMDb, Simkl, a spreadsheet, or a Noir export. Matched on Cinemeta; re-importing never duplicates anything.");
        button("Choose a file…",()->open(OPEN_IMPORT,"*/*"),true);
        button("Open Netflix viewing activity",()->browse("https://www.netflix.com/viewingactivity"),false);
        button("Open Letterboxd data export",()->browse("https://letterboxd.com/settings/data/"),false);
        note("Download the file there, then choose it above.");
        heading("Paste a list");
        EditText box=new EditText(this);box.setMinLines(4);box.setText(paste);box.setTextColor(MainActivity.WHITE);box.setHintTextColor(MainActivity.MUTED);box.setHint("Breaking Bad\nInception (2010)\nThe Office S4\nLost S2E10");body.addView(box);
        button("Match titles",()->{paste=box.getText().toString();List<PhoneHistory.Row> rows=PhoneHistory.list(paste);if(rows.isEmpty()){toast("Paste at least one title.");return;}run("Matching…",()->match("Pasted list",rows,true));},false);
        heading("Connected apps");
        renderNuvio();renderStremio();renderServers();
        heading("Export");
        note("Your whole history from this phone, in formats other apps accept.");
        LinearLayout formats=new LinearLayout(this);String[][] options={{"Noir JSON","noir-json"},{"Noir CSV","noir-csv"},{"Letterboxd","letterboxd"},{"Trakt","trakt"}};
        for(String[] o:options){TextView chip=text(o[0],13,o[1].equals(exportFormat)?MainActivity.BLACK:MainActivity.WHITE);chip.setPadding(dp(12),dp(8),dp(12),dp(8));chip.setBackground(MainActivity.bg(o[1].equals(exportFormat)?MainActivity.GOLD:MainActivity.PANEL,dp(18)));chip.setOnClickListener(v->{exportFormat=o[1];render();});LinearLayout.LayoutParams p=new LinearLayout.LayoutParams(-2,-2);p.setMargins(0,0,dp(6),0);formats.addView(chip,p);}
        HorizontalScrollView fs=new HorizontalScrollView(this);fs.addView(formats);body.addView(fs);
        button("⇩ Export",()->save(SAVE_EXPORT,exportFormat.endsWith("csv")||exportFormat.equals("letterboxd")?"text/csv":"application/json",(exportFormat.equals("letterboxd")?"letterboxd-import.csv":exportFormat.equals("trakt")?"trakt-history.json":exportFormat.equals("noir-csv")?"noir-history.csv":"noir-history.json")),false);
        heading("Back up & restore");
        note("History, ratings, notes and this phone's titles in one file. No passwords or sign-ins. Restore also accepts backups made on your PC.");
        button("⇩ Back up everything",()->save(SAVE_BACKUP,"application/zip","Noir-phone-"+LocalDate.now()+".noirbackup"),true);
        button("Restore from backup…",()->open(OPEN_RESTORE,"*/*"),false);
        File[] autos=autoBackups(this);note("Automatic weekly backups on this phone: "+(autos.length==0?"none yet":autos.length+", newest "+NoirUi.date(autos[0].lastModified())));
        renderSources();
    }
    void browse(String url){try{startActivity(new Intent(Intent.ACTION_VIEW,Uri.parse(url)));}catch(Exception e){toast("No browser is available.");}}
    void open(int code,String type){Intent i=new Intent(Intent.ACTION_OPEN_DOCUMENT);i.addCategory(Intent.CATEGORY_OPENABLE);i.setType(type);startActivityForResult(i,code);}
    void save(int code,String type,String name){Intent i=new Intent(Intent.ACTION_CREATE_DOCUMENT);i.addCategory(Intent.CATEGORY_OPENABLE);i.setType(type);i.putExtra(Intent.EXTRA_TITLE,name);startActivityForResult(i,code);}
    byte[] read(Uri uri)throws IOException{try(InputStream in=getContentResolver().openInputStream(uri)){if(in==null)throw new IOException("That file couldn't be opened.");return DataStore.read(in,256*1024*1024);}}
    String name(Uri uri){try(android.database.Cursor c=getContentResolver().query(uri,new String[]{android.provider.OpenableColumns.DISPLAY_NAME},null,null,null)){if(c!=null&&c.moveToFirst())return c.getString(0);}catch(Exception ignored){}return uri.getLastPathSegment()==null?"history":uri.getLastPathSegment();}
    @Override protected void onActivityResult(int code,int result,Intent intent){
        super.onActivityResult(code,result,intent);if(result!=RESULT_OK||intent==null||intent.getData()==null)return;Uri uri=intent.getData();
        if(code==OPEN_IMPORT||code==OPEN_RESTORE)run("Reading the file…",()->{byte[] bytes=read(uri);String n=name(uri);
            PhoneHistory.Parsed parsed=PhoneHistory.read(n,bytes);if(parsed.backup!=null||code==OPEN_RESTORE){PhoneHistoryIo.Backup b=PhoneHistoryIo.readBackup(parsed.backup!=null?parsed.backup:bytes);ui.post(()->restore(b));return;}
            match(parsed.service,parsed.rows,false);});
        else if(code==SAVE_EXPORT)run("Exporting…",()->{Object[] r=PhoneHistory.export(DataStore.objects(data.items()),data.settings().optJSONObject("showRatings"),exportFormat);try(OutputStream out=getContentResolver().openOutputStream(uri,"wt")){out.write(((String)r[0]).getBytes(StandardCharsets.UTF_8));}
            int skipped=(Integer)r[2];say("Exported "+r[1]+" viewings."+(skipped>0?" "+skipped+" skipped: "+(exportFormat.equals("letterboxd")?"Letterboxd takes movies only.":"Trakt needs an IMDb ID."):""));});
        else if(code==SAVE_BACKUP)run("Saving backup…",()->{try(OutputStream out=getContentResolver().openOutputStream(uri,"wt")){io.writeBackup(out);}say("Backup saved.");});
    }
    interface Job{void run()throws Exception;}
    void run(String message,Job job){if(busy)return;busy=true;say(message);render();work.execute(()->{try{job.run();}catch(PhoneHistoryIo.NuvioExpired e){disconnectNuvio();say(e.getMessage());}catch(Exception e){say(e.getMessage()==null?"That didn't work. Check your connection and try again.":e.getMessage());}finally{busy=false;ui.post(this::render);}});}
    // Resolve on Cinemeta, then show the review. Called on the worker thread.
    void match(String service,List<PhoneHistory.Row> rows,boolean list)throws Exception{
        if(rows.isEmpty()){say("There's nothing new to import from "+service+".");return;}
        say("Matching "+rows.size()+" "+service+" records…");
        PhoneHistoryIo.Plan p=io.resolve(service,rows,(done,total)->say("Matching titles · "+done+" of "+total));if(list)io.expandList(p);
        ui.post(()->{plan=p;when=p.undated()>0?"":"Don't remember";render();});
    }
    String when="";int aroundYear=0;
    void review(){
        heading("From "+plan.service);int movies=0,shows=0;for(PhoneHistoryIo.Title t:plan.titles)if(t.movie())movies++;else shows++;
        TextView big=text(plan.viewings()+" viewings ready to add",22,MainActivity.WHITE);big.setTypeface(null,Typeface.BOLD);body.addView(big);
        note(shows+" shows and "+movies+" movies matched"+(plan.unmatched.isEmpty()?"":" · "+plan.unmatched.size()+" couldn't be matched and will be skipped")+". Viewings you already have are skipped.");
        int undated=plan.undated();
        if(undated>0){heading(undated+" have no date. When did you watch them?");LinearLayout row=new LinearLayout(this);row.setOrientation(LinearLayout.VERTICAL);
            for(String w:PhoneHistoryIo.WHEN){String label=w.equals("Around a year")&&when.equals(w)?"Around "+aroundYear:w;button((when.equals(w)?"✓ ":"")+label,()->{if(w.equals("Around a year")){pickYear();return;}when=w;render();},when.equals(w));}}
        Button go=button("✓ Add "+plan.viewings()+" viewings",()->{PhoneHistoryIo.Plan p=plan;String w=when;int y=aroundYear;run("Adding…",()->{int added=io.apply(p,w,y);ui.post(()->{plan=null;});say(added==0?"Everything from "+p.service+" was already in your history.":"Imported "+added+" viewings from "+p.service+".");
            if(p.service.equals("Nuvio")){if(!PhoneHistoryIo.lookupFailed(p))io.commitNuvioCursor(nuvioCursor);nuvioCursor=0;}});},true);
        go.setEnabled(!busy&&!when.isEmpty()&&plan.viewings()>0);
        button("Cancel",()->{plan=null;render();},false);
        if(!plan.unmatched.isEmpty()){heading("Not matched");for(String u:plan.unmatched.subList(0,Math.min(80,plan.unmatched.size())))note(u);if(plan.unmatched.size()>80)note("…and "+(plan.unmatched.size()-80)+" more");}
    }
    void pickYear(){NumberPicker picker=new NumberPicker(this);int now=LocalDate.now().getYear();picker.setMinValue(1950);picker.setMaxValue(now);picker.setValue(aroundYear>0?aroundYear:now-1);
        new AlertDialog.Builder(this).setTitle("Around which year?").setView(picker).setNegativeButton("Cancel",null).setPositiveButton("OK",(d,w)->{aroundYear=picker.getValue();when="Around a year";render();}).show();}

    // ---- Nuvio ----
    void renderNuvio(){
        JSONObject s=data.stateObject("nuvio");boolean connected=io.nuvioConnected();
        TextView t=text("Nuvio"+(connected?" · "+(s.optString("profileName").isEmpty()?"profile "+s.optInt("profile"):s.optString("profileName"))+(s.optLong("checkedAt")>0?" · checked "+NoirUi.date(s.optLong("checkedAt")):""):" · not connected"),15,connected?MainActivity.GOLD:MainActivity.WHITE);t.setTypeface(null,Typeface.BOLD);body.addView(t);
        note(connected?"Two-way: Nuvio's watches come in every few hours while Noir is open, and what you watch here is marked in Nuvio.":"Link with a code — no password. Watches flow both ways.");
        if(!connected){button("Connect Nuvio",this::connectNuvio,true);return;}
        CheckBox auto=new CheckBox(this);auto.setText("Check Nuvio automatically");auto.setTextColor(MainActivity.WHITE);auto.setChecked(s.optBoolean("auto",true));auto.setOnCheckedChangeListener((v,on)->{JSONObject x=data.stateObject("nuvio");DataStore.put(x,"auto",on);data.stateObject("nuvio",x);});body.addView(auto);
        CheckBox send=new CheckBox(this);send.setText("Mark my watches in Nuvio");send.setTextColor(MainActivity.WHITE);send.setChecked(s.optBoolean("send",true));send.setOnCheckedChangeListener((v,on)->{JSONObject x=data.stateObject("nuvio");DataStore.put(x,"send",on);data.stateObject("nuvio",x);});body.addView(send);
        button("Import from Nuvio now",()->run("Reading your Nuvio history…",()->{Object[] r=io.pullNuvio(s.optLong("cursor")==0);@SuppressWarnings("unchecked")List<PhoneHistory.Row> rows=(List<PhoneHistory.Row>)r[0];
            if(data.stateObject("nuvio").optBoolean("send",true)){int sent=io.pushNuvio();if(sent>0)say("Sent "+sent+" changes to Nuvio.");}
            if((Integer)r[1]>0)say("Removed "+r[1]+" viewings Nuvio no longer has.");nuvioCursor=(Long)r[2];if(rows.isEmpty())io.commitNuvioCursor(nuvioCursor);match("Nuvio",rows,false);}),false);
        button("Disconnect Nuvio",()->{disconnectNuvio();render();},false);
    }
    void disconnectNuvio(){JSONObject s=data.stateObject("nuvio");DataStore.put(s,"refresh","");DataStore.put(s,"access","");DataStore.put(s,"profile",0);data.stateObject("nuvio",s);}
    void connectNuvio(){
        LinearLayout panel=new LinearLayout(this);panel.setOrientation(LinearLayout.VERTICAL);panel.setPadding(dp(20),dp(10),dp(20),0);
        TextView code=text("…",34,MainActivity.GOLD);code.setTypeface(null,Typeface.BOLD);code.setGravity(Gravity.CENTER);panel.addView(code);TextView hint=text("Starting…",13,MainActivity.MUTED);panel.addView(hint);
        AlertDialog dialog=new AlertDialog.Builder(this).setTitle("Connect Nuvio").setView(panel).setNegativeButton("Cancel",null).setPositiveButton("Open nuvio.tv/link",null).create();
        boolean[] open={true};dialog.setOnDismissListener(d->open[0]=false);dialog.show();
        work.execute(()->{try{
            JSONObject link=io.startNuvioLink();String url=link.optString("verification_uri_complete",PhoneHistoryIo.NUVIO_LINK);String c=link.optString("user_code").toUpperCase(Locale.ROOT);String shown=c.length()>3?c.substring(0,3)+"-"+c.substring(3):c;
            ui.post(()->{code.setText(shown);hint.setText("Open nuvio.tv/link, sign in to Nuvio and approve this code. Noir never sees your password.");dialog.getButton(AlertDialog.BUTTON_POSITIVE).setOnClickListener(v->browse(url));});
            int interval=Math.max(2,Math.min(10,link.optInt("poll_interval_seconds",3)));
            for(int i=0;i<200&&open[0];i++){Thread.sleep(interval*1000L);String st;try{st=io.pollNuvioLink(link);}catch(IOException e){continue;}if(st.equals("pending"))continue;if(!st.equals("approved"))throw new IOException("The code expired. Try again.");io.finishNuvioLink(link);
                List<JSONObject> profiles=io.nuvioProfiles();ui.post(()->{dialog.dismiss();chooseProfile(profiles);});return;}
        }catch(Exception e){ui.post(()->{hint.setText(e.getMessage()==null?"Nuvio couldn't be reached.":e.getMessage());code.setText("");});}});
    }
    void chooseProfile(List<JSONObject> profiles){
        if(profiles.isEmpty())profiles=List.of(new JSONObject());
        Runnable[] use=new Runnable[profiles.size()];String[] names=new String[profiles.size()];
        for(int i=0;i<profiles.size();i++){JSONObject p=profiles.get(i);int index=p.optInt("profile_index",1);String n=p.optString("name","Profile "+index);names[i]=n;use[i]=()->{io.useNuvioProfile(index,n);run("Reading your Nuvio history…",()->{Object[] r=io.pullNuvio(true);@SuppressWarnings("unchecked")List<PhoneHistory.Row> rows=(List<PhoneHistory.Row>)r[0];nuvioCursor=(Long)r[2];if(rows.isEmpty())io.commitNuvioCursor(nuvioCursor);match("Nuvio",rows,false);});};}
        if(profiles.size()==1){use[0].run();return;}
        new AlertDialog.Builder(this).setTitle("Which Nuvio profile is yours?").setItems(names,(d,w)->use[w].run()).show();
    }

    // ---- Stremio: sign in on web.stremio.com inside the app; Noir reads the session key, never the password ----
    void renderStremio(){
        JSONObject s=data.stateObject("stremio");boolean connected=!s.optString("key").isEmpty();
        TextView t=text("Stremio"+(connected?" · connected":" · not connected"),15,connected?MainActivity.GOLD:MainActivity.WHITE);t.setTypeface(null,Typeface.BOLD);body.addView(t);
        if(!connected){button("Connect Stremio",this::connectStremio,false);return;}
        button("Import from Stremio",()->run("Reading your Stremio library…",()->match("Stremio",io.stremio(data.stateObject("stremio").optString("key")),false)),false);
        button("Disconnect Stremio",()->{data.stateObject("stremio",null);forgetStremio();render();},false);
    }
    // Signs out of Stremio only: expires Stremio's own cookies and site storage. Trakt and Serializd sign-ins in the same WebView profile stay.
    static final String[] STREMIO_SITES={"https://web.stremio.com","https://www.stremio.com","https://stremio.com","https://api.strem.io","https://www.strem.io","https://strem.io"};
    static void forgetStremio(){CookieManager cookies=CookieManager.getInstance();for(String site:STREMIO_SITES){String all=cookies.getCookie(site);String host=android.net.Uri.parse(site).getHost(),parent=host.startsWith("www.")||host.startsWith("web.")||host.startsWith("api.")?host.substring(host.indexOf('.')+1):host;
        if(all!=null)for(String pair:all.split(";")){String name=pair.split("=",2)[0].trim();if(name.isEmpty())continue;String gone=name+"=; Max-Age=0; Expires=Thu, 01 Jan 1970 00:00:00 GMT; Path=/";cookies.setCookie(site,gone);cookies.setCookie(site,gone+"; Domain="+host);cookies.setCookie(site,gone+"; Domain=."+parent);}
        WebStorage.getInstance().deleteOrigin(site);}cookies.flush();}
    @android.annotation.SuppressLint("SetJavaScriptEnabled")
    void connectStremio(){
        WebView web=new WebView(this);web.getSettings().setJavaScriptEnabled(true);web.getSettings().setDomStorageEnabled(true);web.setWebViewClient(new WebViewClient());
        Dialog dialog=new Dialog(this,android.R.style.Theme_Black_NoTitleBar_Fullscreen);dialog.setContentView(web);dialog.show();web.loadUrl("https://web.stremio.com/#/intro?form=login");
        Runnable[] check=new Runnable[1];check[0]=()->{if(!dialog.isShowing())return;String url=web.getUrl()==null?"":web.getUrl();
            if(url.contains("stremio.com"))web.evaluateJavascript("(()=>{for(const k of ['profile',...Object.keys(localStorage)]){try{const p=JSON.parse(localStorage.getItem(k)||'null');if(p&&p.auth&&typeof p.auth.key==='string')return p.auth.key;}catch(e){}}return '';})()",value->{
                String key=value==null?"":value.replace("\"","");if(key.length()>=8){JSONObject s=new JSONObject();DataStore.put(s,"key",key);data.stateObject("stremio",s);dialog.dismiss();web.destroy();render();run("Reading your Stremio library…",()->match("Stremio",io.stremio(key),false));}else ui.postDelayed(check[0],1500);});
            else ui.postDelayed(check[0],1500);};
        ui.postDelayed(check[0],2000);
    }

    // ---- Media servers ----
    void renderServers(){
        TextView t=text("Plex · Jellyfin · Emby · Kodi",15,MainActivity.WHITE);t.setTypeface(null,Typeface.BOLD);body.addView(t);note("Everything marked played. Read-only: nothing on the server changes.");
        button("Plex · sign in",this::connectPlex,false);
        button("Jellyfin or Emby (https)",this::jellyfin,false);
        if(data.paired())button("Servers your PC knows (incl. Kodi)",()->run("Asking your PC…",()->{List<JSONObject> servers=io.pcServers();ui.post(()->{if(servers.isEmpty()){toast("Your PC has no media servers or Kodi set up.");return;}String[] names=new String[servers.size()];for(int i=0;i<names.length;i++)names[i]=servers.get(i).optString("name");
            new AlertDialog.Builder(this).setTitle("Import watched from").setItems(names,(d,w)->run("Your PC is reading "+names[w]+"…",()->match(names[w],io.pcServerWatched(names[w]),false))).show();});}),false);
    }
    void connectPlex(){
        run("Starting Plex sign-in…",()->{JSONObject pin=io.plexPin();ui.post(()->browse(io.plexAuthUrl(pin)));say("Approve Noir in Plex, then come back here.");
            String token="";for(int i=0;i<120&&token.isEmpty();i++){Thread.sleep(2000);try{token=io.plexPoll(pin);}catch(IOException ignored){}}
            if(token.isEmpty())throw new IOException("Plex sign-in timed out. Try again.");
            List<JSONObject> servers=io.plexServers(token);if(servers.isEmpty())throw new IOException("No Plex server with a secure connection was found.");
            List<PhoneHistory.Row> rows=new ArrayList<>();for(JSONObject server:servers){say("Reading "+server.optString("name")+"…");rows.addAll(io.plexWatched(server));}
            match("Plex",rows,false);});
    }
    void jellyfin(){
        LinearLayout panel=new LinearLayout(this);panel.setOrientation(LinearLayout.VERTICAL);panel.setPadding(dp(20),dp(10),dp(20),0);
        EditText address=new EditText(this);address.setHint("https://jellyfin.example.com");EditText user=new EditText(this);user.setHint("Username");EditText password=new EditText(this);password.setHint("Password");password.setInputType(android.text.InputType.TYPE_CLASS_TEXT|android.text.InputType.TYPE_TEXT_VARIATION_PASSWORD);
        Spinner kind=new Spinner(this);kind.setAdapter(new ArrayAdapter<>(this,android.R.layout.simple_spinner_dropdown_item,new String[]{"Jellyfin","Emby"}));
        for(View v:new View[]{kind,address,user,password})panel.addView(v);
        new AlertDialog.Builder(this).setTitle("Import from Jellyfin or Emby").setView(panel).setNegativeButton("Cancel",null).setPositiveButton("Import",(d,w)->{String a=address.getText().toString(),u=user.getText().toString(),p=password.getText().toString(),k=(String)kind.getSelectedItem();
            run("Reading "+k+"…",()->match(k,io.jellyfinWatched(a,u,p,k),false));}).show();
    }

    // ---- Restore ----
    void restore(PhoneHistoryIo.Backup b){
        String created=b.created.isEmpty()?"":" · made "+b.created.substring(0,Math.min(10,b.created.length()));
        AlertDialog.Builder dialog=new AlertDialog.Builder(this).setTitle("Restore this backup?").setMessage(b.viewings()+" viewings"+created+".\n\nMerge adds what's missing and keeps everything you have; you can undo it."+(b.phone!=null&&!data.paired()?"\n\nReplace makes this phone exactly like the backup.":data.paired()?"\n\nThis phone is paired, so your PC keeps the full library; merged history reaches it on the next sync.":""))
            .setNegativeButton("Cancel",null).setPositiveButton("Merge",(d,w)->run("Restoring…",()->{for(PhoneHistory.Row r:b.rows)if(r.source.isEmpty())r.source="Restored";
                PhoneHistoryIo.Plan p=io.resolve("backup",b.rows,(done,total)->say("Matching titles · "+done+" of "+total));int added=io.apply(p,"Don't remember",0);say(added==0?"Everything in that backup was already here.":"Restored "+added+" viewings.");}));
        if(b.phone!=null&&!data.paired())dialog.setNeutralButton("Replace",(d,w)->new AlertDialog.Builder(this).setTitle("Replace everything on this phone?").setMessage("A copy of what's here now is saved to automatic backups first.").setNegativeButton("Cancel",null).setPositiveButton("Replace",(dd,ww)->run("Replacing…",()->{saveAutoBackup(this,data,"before-restore");data.replaceState(b.phone);
                    // History synced from a PC lives only in history.json; bring it back too.
                    for(PhoneHistory.Row r:b.rows)if(r.source.isEmpty())r.source="Restored";int added=b.rows.isEmpty()?0:io.apply(io.resolve("backup",b.rows,(done,total)->say("Matching titles · "+done+" of "+total)),"Don't remember",0);
                    say("Restored. This phone now matches the backup"+(added>0?" ("+added+" viewings from its history)":"")+".");})).show());
        dialog.show();
    }

    // ---- Sources and recent imports ----
    void renderSources(){
        JSONArray batches=data.stateArray("importBatches");Map<String,int[]> sources=io.sources();if(batches.length()==0&&sources.isEmpty())return;
        heading("Where your history came from");note("Undo a recent import, or remove everything one source added. Your PC gets the change on the next sync.");
        for(int n=batches.length()-1;n>=Math.max(0,batches.length()-8);n--){JSONObject b=batches.optJSONObject(n);int count=b.optJSONArray("viewings")==null?0:b.optJSONArray("viewings").length();
            button("↶ Undo "+b.optString("service")+" · "+count+" viewings · "+NoirUi.date(b.optLong("at")),()->new AlertDialog.Builder(this).setTitle("Undo this import?").setMessage("Removes the "+count+" viewings it added.").setNegativeButton("Cancel",null).setPositiveButton("Undo",(d,w)->{int removed=io.undo(b.optString("id"));toast("Removed "+removed+" viewings.");render();}).show(),false);}
        for(Map.Entry<String,int[]> e:sources.entrySet()){String source=e.getKey();int[] c=e.getValue();
            TextView row=text(label(source)+" · "+c[0]+" viewings of "+c[1]+" items",14,MainActivity.WHITE);row.setOnClickListener(v->new AlertDialog.Builder(this).setTitle("Remove all from “"+label(source)+"”?").setMessage(c[0]+" viewings will be removed. Titles stay. A safety backup is saved first.").setNegativeButton("Cancel",null)
                .setPositiveButton("Remove",(d,w)->run("Removing…",()->{saveAutoBackup(this,data,"before-remove");int n=io.removeSource(source);say("Removed "+n+" viewings.");})).show());row.setPadding(0,dp(10),0,dp(10));body.addView(row);}
        note("Tap a source to remove what it added.");
    }
    static String label(String source){return source.startsWith("Imported from ")?source.substring(14):source.equals("Playback")?"Played in Noir":source.equals("Android")||source.equals("Android playback")||source.equals("Marked on Android")?"Phone":source.equals("Earlier record")?"Noir":source;}

    // ---- Automatic weekly backups in the app's own folder (kept: newest 8) ----
    static File[] autoBackups(android.content.Context c){File dir=new File(c.getExternalFilesDir(null),"backups");File[] files=dir.listFiles((d,n)->n.endsWith(".noirbackup"));if(files==null)return new File[0];Arrays.sort(files,(a,b)->Long.compare(b.lastModified(),a.lastModified()));return files;}
    static void saveAutoBackup(android.content.Context c,DataStore data,String kind)throws IOException{
        File temp=null;
        try{File external=c.getExternalFilesDir(null);if(external==null)throw new IOException("Storage unavailable");File dir=new File(external,"backups");if(!dir.isDirectory()&&!dir.mkdirs())throw new IOException("Backup directory unavailable");File file=new File(dir,"Noir-"+kind+"-"+LocalDate.now()+(kind.equals("auto")?"":"-"+System.currentTimeMillis())+".noirbackup");
            temp=File.createTempFile("Noir-",".tmp",dir);try(OutputStream out=new FileOutputStream(temp)){new PhoneHistoryIo(data,new DiscoveryClient(c)).writeBackup(out);}
            java.nio.file.Files.move(temp.toPath(),file.toPath(),java.nio.file.StandardCopyOption.ATOMIC_MOVE,java.nio.file.StandardCopyOption.REPLACE_EXISTING);
            File[] all=autoBackups(c);int kept=0;for(File f:all)if(f.getName().startsWith("Noir-auto-")==kind.equals("auto")&&++kept>8)f.delete();
        }catch(Exception e){throw new IOException(Loc.t(BACKUP_FAILED),e);}finally{if(temp!=null&&temp.exists())temp.delete();}
    }
    static void weekly(android.content.Context c,DataStore data){File[] all=autoBackups(c);for(File f:all)if(f.getName().startsWith("Noir-auto-")&&f.lastModified()>System.currentTimeMillis()-7*86400000L)return;if(data.items().length()>0)data.network.execute(()->{try{saveAutoBackup(c,data,"auto");}catch(IOException e){android.util.Log.w("Noir","Automatic backup failed",e);}});}
    @Override public void onBackPressed(){if(plan!=null){plan=null;render();return;}super.onBackPressed();}
    @Override public void onDestroy(){work.shutdownNow();ui.removeCallbacksAndMessages(null);super.onDestroy();}
}
