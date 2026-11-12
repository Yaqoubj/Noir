package app.noir.mobile;

import android.app.Activity;
import android.content.Context;
import android.net.*;
import android.os.*;
import android.view.*;
import android.widget.*;
import java.io.*;
import java.net.*;
import java.text.DateFormat;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.*;

public final class TvCheckActivity extends Activity {
    final ExecutorService worker=Executors.newFixedThreadPool(3);
    TextView verdict,recommended,connection,measurement,result,details,test;
    LinearLayout screen,video,audio;ProgressBar progress;TvCaps caps;volatile SpeedTest running;
    static {String[][] words={
        {"TV check","فحص التلفزيون"},{"Loading…","جارٍ التحميل…"},{"This TV:","هذا التلفزيون:"},{"Recommended streams:","البث الموصى به:"},{"4K","4K"},{"1080p","1080p"},{"4K WEB-DL","4K WEB-DL"},{"4K remux","4K remux"},{"H.264","H.264"},{"HEVC","HEVC"},{"AV1","AV1"},{"VP9","VP9"},{"HDR10","HDR10"},{"HDR10+","HDR10+"},{"HLG","HLG"},{"Dolby Vision","Dolby Vision"},{"Dolby Digital (AC3)","دولبي ديجيتال (AC3)"},{"Dolby Digital Plus","دولبي ديجيتال بلس"},{"Dolby Atmos (DD+)","دولبي أتموس (DD+)"},{"DTS","DTS"},{"DTS-HD","DTS-HD"},{"Dolby TrueHD","Dolby TrueHD"},
        {"Screen","الشاشة"},{"Video decoding in hardware","فك ترميز الفيديو عتاديًا"},{"Audio to the TV/soundbar","الصوت إلى التلفزيون/جهاز الصوت"},{"HEVC 10-bit","HEVC ‏10 بت"},{"Audio passthrough","تمرير الصوت"},{"On","تشغيل"},{"Off","إيقاف"},{"TV speakers only — Noir converts Dolby/DTS itself","سماعات التلفزيون فقط — يحوّل نوار صوت Dolby/DTS بنفسه"},
        {"Network","الشبكة"},{"Ethernet","إيثرنت"},{"Wi-Fi","واي فاي"},{"Other connection","اتصال آخر"},{"No connection","لا يوجد اتصال"},{"Connection unknown","نوع الاتصال غير معروف"},{"Last measured:","آخر قياس:"},{"Not tested","لم يُختبر"},{"%.1f Mbit/s","%.1f ميغابت/ث"},
        {"Test internet speed","اختبار سرعة الإنترنت"},{"Testing…","جارٍ الاختبار…"},{"Warming up…","جارٍ تهيئة الاتصال…"},{"%.1f Mbit/s · %d / 8 s","%.1f ميغابت/ث · %d / 8 ث"},{"1080p only","1080p فقط"},{"4K WEB-DL OK","مناسب لبث 4K WEB-DL"},{"4K WEB-DL smooth, remux may stutter","بث 4K WEB-DL سلس، وقد يتقطع remux"},{"Anything, including 4K remux","كل الجودات، بما فيها 4K remux"},{"Couldn't test the speed. Check the TV's internet.","تعذر اختبار السرعة. تحقق من إنترنت التلفزيون."},{"%s · Android %s (%d) · %s %s","%s · أندرويد %s (%d) · %s %s"}
    };for(String[] p:words)Loc.AR.put(p[0],p[1]);}
    static String t(String s){return Loc.t(s);}
    int dp(int n){return TvUi.dp(this,n);}
    @Override protected void onCreate(Bundle state){super.onCreate(state);FrameLayout root=TvUi.screen(this);LinearLayout body=new LinearLayout(this);body.setOrientation(LinearLayout.VERTICAL);body.setClipChildren(false);root.addView(body,new FrameLayout.LayoutParams(-1,-1));
        body.addView(TvUi.text(this,t("TV check"),16,TvUi.GOLD,true));verdict=TvUi.text(this,t("Loading…"),24,TvUi.WHITE,true);verdict.setPadding(0,dp(5),0,dp(4));body.addView(verdict);recommended=TvUi.text(this,"",18,TvUi.GOLD,true);body.addView(recommended);
        LinearLayout cards=new LinearLayout(this);cards.setClipChildren(false);LinearLayout.LayoutParams cp=new LinearLayout.LayoutParams(-1,dp(240));cp.setMargins(0,dp(12),0,dp(10));body.addView(cards,cp);screen=card(cards,"Screen",false);video=card(cards,"Video decoding in hardware",true);audio=card(cards,"Audio to the TV/soundbar",true);
        LinearLayout net=new LinearLayout(this);net.setOrientation(LinearLayout.VERTICAL);net.setClipChildren(false);net.setBackground(TvUi.bg(TvUi.PANEL,dp(10)));net.setPadding(dp(14),dp(10),dp(14),dp(10));body.addView(net,new LinearLayout.LayoutParams(-1,-2));
        LinearLayout top=new LinearLayout(this);top.setClipChildren(false);top.setGravity(Gravity.CENTER_VERTICAL);net.addView(top);LinearLayout info=new LinearLayout(this);info.setOrientation(LinearLayout.VERTICAL);top.addView(info,new LinearLayout.LayoutParams(0,-2,1));connection=TvUi.text(this,t("Network")+" · "+t("Loading…"),16,TvUi.WHITE,true);info.addView(connection);measurement=TvUi.text(this,"",14,TvUi.MUTED,false);measurement.setPadding(0,dp(5),dp(8),0);info.addView(measurement);
        test=TvUi.button(this,t("Test internet speed"),true,this::startTest);test.setId(View.generateViewId());LinearLayout.LayoutParams bp=new LinearLayout.LayoutParams(-2,dp(46));bp.setMarginEnd(dp(8));top.addView(test,bp);progress=new ProgressBar(this,null,android.R.attr.progressBarStyleHorizontal);progress.setMax(80);progress.setProgressTintList(android.content.res.ColorStateList.valueOf(TvUi.GOLD));LinearLayout.LayoutParams pp=new LinearLayout.LayoutParams(-1,dp(4));pp.setMargins(0,dp(8),0,0);net.addView(progress,pp);result=TvUi.text(this,"",15,TvUi.GOLD,false);result.setPadding(0,dp(6),0,0);result.setMinHeight(dp(25));result.setAccessibilityLiveRegion(View.ACCESSIBILITY_LIVE_REGION_POLITE);net.addView(result);
        details=TvUi.text(this,"",13,TvUi.MUTED,false);details.setPadding(0,dp(9),0,0);details.setLayoutDirection(View.LAYOUT_DIRECTION_LTR);body.addView(details,new LinearLayout.LayoutParams(-1,-2));setContentView(root);Loc.apply(this);showMeasurement();test.requestFocus();
        worker.execute(()->{TvCaps found=TvCaps.get(getApplicationContext());Connection netInfo=readConnection();runOnUiThread(()->{if(isDestroyed()||isFinishing())return;caps=found;showCaps();showConnection(netInfo);});});
    }
    LinearLayout card(LinearLayout parent,String title,boolean gap){LinearLayout box=new LinearLayout(this);box.setOrientation(LinearLayout.VERTICAL);box.setBackground(TvUi.bg(TvUi.PANEL,dp(10)));box.setPadding(dp(12),dp(12),dp(10),dp(10));LinearLayout.LayoutParams lp=new LinearLayout.LayoutParams(0,-1,1);if(gap)lp.setMarginStart(dp(10));parent.addView(box,lp);TextView heading=TvUi.text(this,t(title),16,TvUi.GOLD,true);heading.setMinHeight(dp(38));box.addView(heading);return box;}
    void fact(LinearLayout box,boolean yes,String label){fact(box,yes,t(label),17,yes?TvUi.WHITE:TvUi.MUTED,true);}
    /** U115: one line per format, a drawn check or cross at the line's start (right in Arabic). Codec names keep left-to-right order. */
    TextView fact(LinearLayout box,boolean yes,String label,int sp,int color,boolean codec){LinearLayout line=new LinearLayout(this);line.setGravity(Gravity.CENTER_VERTICAL);line.setPadding(0,0,0,dp(4));line.setContentDescription((yes?"✓ ":"✕ ")+label);line.addView(NoirIcons.icon(this,yes?"check":"close",sp+1,color));TextView name=TvUi.text(this,label,sp,color,false);if(codec)name.setTextDirection(View.TEXT_DIRECTION_LTR);name.setPaddingRelative(dp(10),0,0,0);line.addView(name,new LinearLayout.LayoutParams(-2,-2));box.addView(line,new LinearLayout.LayoutParams(-1,-2));return name;}
    void showCaps(){String summary=t("This TV:")+" "+t(caps.uhd()?"4K":"1080p");if(caps.hdr10plus)summary+=" · "+t("HDR10+");else if(caps.hdr10)summary+=" · "+t("HDR10");if(caps.hlg)summary+=" · "+t("HLG");if(caps.dolbyVision)summary+=" · "+t("Dolby Vision");verdict.setText(summary);
        fact(screen,true,caps.uhd()?"4K":"1080p");fact(screen,caps.hdr10,"HDR10");fact(screen,caps.hdr10plus,"HDR10+");fact(screen,caps.hlg,"HLG");fact(screen,caps.dolbyVision,"Dolby Vision");
        fact(video,caps.avc,"H.264");fact(video,caps.hevc,"HEVC");fact(video,caps.hevc10,"HEVC 10-bit");fact(video,caps.av1,"AV1");fact(video,caps.vp9,"VP9");fact(video,caps.dolbyVisionDecoder,"Dolby Vision");
        boolean passthrough=TvPrefs.passthrough(this);TextView setting=fact(audio,passthrough,t("Audio passthrough")+" · "+t(passthrough?"On":"Off"),15,TvUi.GOLD,false);((View)setting.getParent()).setPadding(0,0,0,dp(5));
        if(caps.passthrough.isEmpty())audio.addView(TvUi.text(this,t("TV speakers only — Noir converts Dolby/DTS itself"),15,TvUi.MUTED,false));
        else for(String format:caps.passthrough)fact(audio,true,t(audioName(format)),15,TvUi.WHITE,true);
        details.setText(Loc.f("%s · Android %s (%d) · %s %s",caps.toString(),Build.VERSION.RELEASE,Build.VERSION.SDK_INT,Build.MANUFACTURER,Build.MODEL));
    }
    static String audioName(String key){switch(key){case "ac3":return "Dolby Digital (AC3)";case "eac3":return "Dolby Digital Plus";case "eac3-joc":return "Dolby Atmos (DD+)";case "dts":return "DTS";case "dtshd":return "DTS-HD";case "truehd":return "Dolby TrueHD";default:return key;}}
    static final class Connection {final String name;final boolean ethernet;Connection(String name,boolean ethernet){this.name=name;this.ethernet=ethernet;}}
    Connection readConnection(){try{ConnectivityManager cm=(ConnectivityManager)getSystemService(Context.CONNECTIVITY_SERVICE);Network active=cm==null?null:cm.getActiveNetwork();NetworkCapabilities nc=active==null?null:cm.getNetworkCapabilities(active);if(nc==null)return new Connection("No connection",false);boolean wired=nc.hasTransport(NetworkCapabilities.TRANSPORT_ETHERNET);return new Connection(wired?"Ethernet":nc.hasTransport(NetworkCapabilities.TRANSPORT_WIFI)?"Wi-Fi":"Other connection",wired);}catch(Exception e){return new Connection("Connection unknown",false);}}
    void showConnection(Connection c){connection.setText(t("Network")+" · "+t(c.name));if(caps!=null){float speed=TvPrefs.mbps(this);boolean fourK=caps.uhd()&&caps.hevc10&&(speed<=0||speed>=15);recommended.setText(t("Recommended streams:")+" "+t(!fourK?"1080p":speed>=90&&c.ethernet?"4K remux":"4K WEB-DL"));}}
    void showMeasurement(){float speed=TvPrefs.mbps(this);long at=TvPrefs.mbpsAt(this);measurement.setText(t("Last measured:")+" "+(speed>0?Loc.f("%.1f Mbit/s",speed)+(at>0?" · "+DateFormat.getDateTimeInstance(DateFormat.SHORT,DateFormat.SHORT).format(new Date(at)):""):t("Not tested")));if(speed>0)result.setText(t(speedVerdict(speed)));}
    static String speedVerdict(float speed){return speed<15?"1080p only":speed<40?"4K WEB-DL OK":speed<90?"4K WEB-DL smooth, remux may stutter":"Anything, including 4K remux";}
    void startTest(){if(running!=null)return;SpeedTest run=new SpeedTest();running=run;test.setText(t("Testing…"));progress.setProgress(0);result.setText(t("Warming up…"));worker.execute(()->measure(run));}
    void measure(SpeedTest run){run.start=SystemClock.elapsedRealtime();run.deadline=run.start+8000;long end=run.start;
        try{for(int n=0;n<2;n++)worker.execute(()->download(run));while(!run.cancelled){end=SystemClock.elapsedRealtime();if(end>=run.deadline||run.done.getCount()==0)break;long now=end;float current=run.rate(now);runOnUiThread(()->{if(isDestroyed()||running!=run||run.cancelled)return;progress.setProgress((int)Math.min(80,(now-run.start)/100));result.setText(now-run.start<=1000?t("Warming up…"):Loc.f("%.1f Mbit/s · %d / 8 s",current,(int)((now-run.start)/1000)));});run.done.await(200,TimeUnit.MILLISECONDS);}}
        catch(InterruptedException e){run.cancelled=true;Thread.currentThread().interrupt();}
        catch(RejectedExecutionException e){run.cancelled=true;}
        finally{run.stopped=true;end=Math.min(run.deadline,SystemClock.elapsedRealtime());run.close();}
        if(run.cancelled)return;float speed=run.rate(end);boolean ok=speed>0;if(ok)TvPrefs.measured(this,speed,true);Connection netInfo=readConnection();runOnUiThread(()->{if(isDestroyed()||isFinishing()||running!=run||run.cancelled)return;running=null;test.setText(t("Test internet speed"));progress.setProgress(ok?80:0);showMeasurement();showConnection(netInfo);result.setText(t(ok?speedVerdict(speed):"Couldn't test the speed. Check the TV's internet."));});
    }
    // Cloudflare refuses 100 MB or more per request, so each worker fetches 50 MB pieces until the deadline.
    void download(SpeedTest run){try{while(!run.stopped&&!run.cancelled&&SystemClock.elapsedRealtime()<run.deadline)fetch(run);}finally{run.done.countDown();}}
    void fetch(SpeedTest run){HttpURLConnection c=null;try{c=(HttpURLConnection)new URL("https://speed.cloudflare.com/__down?bytes=50000000").openConnection();c.setConnectTimeout(3000);c.setReadTimeout(1500);c.setUseCaches(false);c.setRequestProperty("Accept-Encoding","identity");synchronized(run.connections){if(run.stopped||run.cancelled)return;run.connections.add(c);}if(c.getResponseCode()!=200)throw new IOException("HTTP");try(InputStream in=c.getInputStream()){byte[] buffer=new byte[65536];while(!run.stopped&&!run.cancelled&&SystemClock.elapsedRealtime()<run.deadline){int count=in.read(buffer);if(count<0)break;long now=SystemClock.elapsedRealtime();if(!run.stopped&&now>=run.start+1000&&now<=run.deadline)run.bytes.addAndGet(count);}}}
        catch(Exception e){if(!run.stopped&&!run.cancelled&&SystemClock.elapsedRealtime()<run.deadline){run.errors.incrementAndGet();run.stopped=true;}}
        finally{if(c!=null){synchronized(run.connections){run.connections.remove(c);}c.disconnect();}}
    }
    static final class SpeedTest {volatile boolean cancelled,stopped;long start,deadline;final AtomicLong bytes=new AtomicLong();final AtomicInteger errors=new AtomicInteger();final CountDownLatch done=new CountDownLatch(2);final Set<HttpURLConnection> connections=new HashSet<>();float rate(long now){long elapsed=Math.min(now,deadline)-start-1000;return elapsed<=0?0:(float)(bytes.get()*8.0/(elapsed*1000.0));}void close(){List<HttpURLConnection> open;synchronized(connections){open=new ArrayList<>(connections);connections.clear();}for(HttpURLConnection c:open)c.disconnect();}}
    @Override protected void onDestroy(){SpeedTest run=running;if(run!=null)run.cancelled=true;worker.shutdownNow();super.onDestroy();}
}
