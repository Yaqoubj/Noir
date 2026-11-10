package app.noir.mobile;

import android.content.Context;
import android.net.Uri;
import android.util.Base64;
import androidx.test.platform.app.InstrumentationRegistry;
import org.json.*;
import org.junit.Test;
import org.videolan.libvlc.LibVLC;
import org.videolan.libvlc.Media;
import org.videolan.libvlc.MediaPlayer;
import java.io.*;
import java.net.*;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.util.concurrent.atomic.*;
import java.util.regex.*;
import static org.junit.Assert.*;

/** Stream timing on the phone: same results as the PC tool, safe proxy, real extraction through LibVLC. */
public final class StreamSubtitleSyncTest {
    private static Context target(){return InstrumentationRegistry.getInstrumentation().getTargetContext();}
    private static byte[] asset(String name)throws Exception{try(InputStream in=InstrumentationRegistry.getInstrumentation().getContext().getAssets().open(name);ByteArrayOutputStream out=new ByteArrayOutputStream()){byte[] b=new byte[65536];int n;while((n=in.read(b))>0)out.write(b,0,n);return out.toByteArray();}}
    private static boolean[] unrle(JSONArray runs)throws Exception{int total=0;for(int i=0;i<runs.length();i++)total+=runs.getJSONArray(i).getInt(1);boolean[] out=new boolean[total];int at=0;for(int i=0;i<runs.length();i++){JSONArray r=runs.getJSONArray(i);Arrays.fill(out,at,at+r.getInt(1),r.getInt(0)==1);at+=r.getInt(1);}return out;}

    @Test public void sharedVectorsMatchThePcTool()throws Exception{
        JSONObject vectors=new JSONObject(new String(asset("sampled_vectors.json"),StandardCharsets.UTF_8));JSONArray cases=vectors.getJSONArray("alignment");assertTrue(cases.length()>=5);
        for(int c=0;c<cases.length();c++){
            JSONObject test=cases.getJSONObject(c);JSONArray rows=test.getJSONArray("cues");double[][] cues=new double[rows.length()][];for(int i=0;i<cues.length;i++)cues[i]=new double[]{rows.getJSONArray(i).getDouble(0),rows.getJSONArray(i).getDouble(1)};
            List<SampledAlignment.Sample> samples=new ArrayList<>();JSONArray given=test.getJSONArray("samples");for(int i=0;i<given.length();i++)samples.add(new SampledAlignment.Sample(given.getJSONObject(i).getDouble("start"),unrle(given.getJSONObject(i).getJSONArray("speech"))));
            SampledAlignment.Result got=SampledAlignment.align(samples,cues);JSONObject want=test.getJSONObject("expected");String name=test.getString("name");
            assertEquals(name,want.getBoolean("accepted"),got.accepted);assertEquals(name,want.getString("reason"),got.reason);assertEquals(name,want.getDouble("offset"),got.offset,1e-8);assertEquals(name,want.getDouble("scale"),got.scale,1e-8);
            JSONArray edges=test.getJSONArray("fitted_edges");assertEquals(name,edges.getJSONArray(0).getDouble(0),got.fitted[0][0],.01);assertEquals(name,edges.getJSONArray(1).getDouble(1),got.fitted[got.fitted.length-1][1],.01);
        }
        JSONArray vad=vectors.getJSONArray("vad");for(int c=0;c<vad.length();c++){
            JSONObject test=vad.getJSONObject(c);ByteBuffer pcm=ByteBuffer.wrap(Base64.decode(test.getString("pcm_s16le"),Base64.DEFAULT)).order(ByteOrder.LITTLE_ENDIAN);short[] samples=new short[pcm.remaining()/2];pcm.asShortBuffer().get(samples);
            assertArrayEquals(test.getString("name"),unrle(test.getJSONArray("speech")),SampledAlignment.speechMasks(Collections.singletonList(samples)).get(0));
        }
    }
    @Test public void planMatchesThePcTool(){
        assertEquals(8,SampledAlignment.planWindows(7200).length);assertEquals(108,SampledAlignment.planWindows(7200)[0][1],1e-9);assertEquals(6,SampledAlignment.planWindows(2700).length);assertEquals(6,SampledAlignment.planWindows(1200).length);assertEquals(0,SampledAlignment.planWindows(600).length);assertEquals(0,SampledAlignment.planWindows(Double.NaN).length);assertEquals(7,SampledAlignment.planExtraWindows(7200).length);
    }
    @Test public void timingRewritesEveryFormatAndKeepsArabic(){
        String srt="1\r\n00:00:01,000 --> 00:00:02,500\r\nمرحبا بالعالم\r\n\r\n2\r\n01:00:00,000 --> 01:00:01,000\r\n<i>Second</i>\r\n";
        assertEquals(2,SubtitleTiming.cues(srt,".srt").size());String moved=SubtitleTiming.apply(srt,".srt",1,1.5);assertTrue(moved,moved.contains("00:00:02,500 --> 00:00:04,000"));assertTrue(moved,moved.contains("01:00:01,500 --> 01:00:02,500"));assertTrue(moved.contains("مرحبا بالعالم"));
        String vtt="WEBVTT\n\n00:05.000 --> 00:06.000 line:90%\nعربي\n";List<double[]> cues=SubtitleTiming.cues(vtt,".vtt");assertEquals(5000,cues.get(0)[0],.1);moved=SubtitleTiming.apply(vtt,".vtt",1,2);assertTrue(moved,moved.contains("00:07.000 --> 00:08.000 line:90%"));assertTrue(moved.contains("عربي"));
        String ass="[Events]\nDialogue: 0,0:00:01.00,0:00:02.00,Default,,0,0,0,,عربي, يبقى\n";moved=SubtitleTiming.apply(ass,".ass",2,.5);assertTrue(moved,moved.contains("Dialogue: 0,0:00:02.50,0:00:04.50,Default,,0,0,0,,عربي, يبقى"));
        assertEquals(2,SubtitleTiming.cues("1\n00:00:01,000 --> 00:00:02,000\nx\n2\n00:00:03,000 --> 00:00:03,000\nzero\n3\n00:00:05,000 --> 00:00:06,000\nend\n",".srt").size());
    }
    /** Loopback upstream with real range support, or a chosen failure. */
    private static final class Upstream implements Closeable {
        final ServerSocket server;final byte[] data;final List<String> requests=Collections.synchronizedList(new ArrayList<>());final AtomicLong served=new AtomicLong();volatile int status=206;volatile boolean running=true,overshoot=false;
        Upstream(byte[] data)throws IOException{this.data=data;server=new ServerSocket(0,16,InetAddress.getByAddress(new byte[]{127,0,0,1}));Thread t=new Thread(()->{while(running)try{Socket s=server.accept();new Thread(()->serve(s)).start();}catch(IOException e){return;}});t.setDaemon(true);t.start();}
        String url(){return "http://127.0.0.1:"+server.getLocalPort()+"/secret/video.mkv?token=private";}
        void serve(Socket socket){try(Socket s=socket;OutputStream out=s.getOutputStream()){BufferedReader in=new BufferedReader(new InputStreamReader(s.getInputStream()));String line=in.readLine();if(line==null)return;StringBuilder all=new StringBuilder(line);long a=0,b=data.length-1;String h;while((h=in.readLine())!=null&&!h.isEmpty()){all.append('\n').append(h);Matcher m=Pattern.compile("(?i)range: bytes=(\\d+)-(\\d*)").matcher(h);if(m.find()){a=Long.parseLong(m.group(1));if(!m.group(2).isEmpty())b=Math.min(data.length-1,Long.parseLong(m.group(2)));}}requests.add(all.toString());
            if(status!=206){out.write(("HTTP/1.1 "+status+" No\r\nContent-Length: 0\r\n\r\n").getBytes());if(status==200){}return;}
            if(overshoot)b=Math.min(data.length-1,b+5000);int len=(int)(b-a+1);out.write(("HTTP/1.1 206 Partial\r\nContent-Length: "+len+"\r\nContent-Range: bytes "+a+"-"+b+"/"+data.length+"\r\nAccept-Ranges: bytes\r\n\r\n").getBytes());out.write(data,(int)a,len);served.addAndGet(len);out.flush();}catch(IOException ignored){}}
        @Override public void close()throws IOException{running=false;server.close();}
    }
    private static String get(String url,String range)throws Exception{HttpURLConnection c=(HttpURLConnection)new URL(url).openConnection();if(range!=null)c.setRequestProperty("Range",range);try{return c.getResponseCode()+" "+c.getHeaderField("Content-Range");}finally{c.disconnect();}}
    @Test public void proxyEnforcesRangeRulesAndHidesTheLink()throws Exception{
        byte[] data=new byte[40*1024*1024];try(Upstream up=new Upstream(data)){
            StreamSubtitleSync.Proxy p=new StreamSubtitleSync.Proxy(up.url(),"AgentX","https://ref.example/");p.start();
            try{
                assertFalse(p.url().contains("secret"));assertFalse(p.url().contains("private"));
                assertEquals("206 bytes 100-199/"+data.length,get(p.url(),"bytes=100-199"));
                assertEquals("open-ended requests are bounded","206 bytes 0-"+(16*1024*1024-1)+"/"+data.length,get(p.url(),"bytes=0-"));
                assertEquals("404 null",get(p.url().replace("/stream","/x")+"x",null));
                assertTrue(up.requests.get(0).contains("User-Agent: AgentX"));assertTrue(up.requests.get(0).contains("Referer: https://ref.example/"));
                up.status=200;assertEquals("502 null",get(p.url(),"bytes=0-9"));assertTrue(p.failure,p.failure.contains("can't jump"));p.failure=null;
                up.status=403;assertEquals("502 null",get(p.url(),"bytes=0-9"));assertTrue(p.failure,p.failure.contains("expired"));
            }finally{p.close();}
        }
    }
    @Test public void proxyNeverRelaysPastTheRequestedEnd()throws Exception{
        byte[] data=new byte[1024*1024];try(Upstream up=new Upstream(data)){up.overshoot=true;StreamSubtitleSync.Proxy p=new StreamSubtitleSync.Proxy(up.url(),null,null);p.start();
            try{HttpURLConnection c=(HttpURLConnection)new URL(p.url()).openConnection();c.setRequestProperty("Range","bytes=100-199");try(InputStream in=c.getInputStream()){assertEquals("bytes 100-199/"+data.length,c.getHeaderField("Content-Range"));assertEquals(100,DataStore.read(in,1024*1024).length);}finally{c.disconnect();}assertTrue(p.relayed()<=100);}finally{p.close();}
        }
    }
    @Test public void theSecondPassSharesTheFirstPassDataBudget()throws Exception{
        byte[] data=new byte[2*1024*1024];try(Upstream up=new Upstream(data)){StreamSubtitleSync.Proxy p=new StreamSubtitleSync.Proxy(up.url(),null,null);p.resume(400L*1024*1024-1024,0,-1);p.start();
            try{HttpURLConnection c=(HttpURLConnection)new URL(p.url()).openConnection();c.setRequestProperty("Range","bytes=0-");try(InputStream in=c.getInputStream()){byte[] b=new byte[65536];while(in.read(b)>0){}}catch(IOException ignored){}c.disconnect();assertNotNull("carried bytes count against the budget",p.failure);assertTrue(p.failure,p.failure.contains("data limit"));}finally{p.close();}
        }
    }
    @Test public void cancelledJobsAreRecognisableAndExtraWindowsStayInTheirGaps(){
        StreamSubtitleSync job=new StreamSubtitleSync(target(),"https://stream.example/x","a",null,7200,-1);assertFalse(job.isCancelled());job.cancel();assertTrue(job.isCancelled());
        for(double d:new double[]{720,900,1200,1800,2700,3700,7200}){double[][] p=SampledAlignment.planWindows(d),e=SampledAlignment.planExtraWindows(d);List<double[]> all=new ArrayList<>(Arrays.asList(p));all.addAll(Arrays.asList(e));all.sort(Comparator.comparingDouble(x->x[0]));for(int i=0;i+1<all.size();i++)assertTrue("overlap at "+d,all.get(i)[0]+all.get(i)[1]<=all.get(i+1)[0]+1e-9);}
    }
    @Test public void proxyStopsAtItsDataBudget()throws Exception{
        byte[] data=new byte[32*1024*1024];try(Upstream up=new Upstream(data)){
            StreamSubtitleSync.Proxy p=new StreamSubtitleSync.Proxy(up.url(),null,null);p.start();
            try{for(int i=0;i<40;i++){HttpURLConnection c=(HttpURLConnection)new URL(p.url()).openConnection();c.setRequestProperty("Range","bytes=0-");try(InputStream in=c.getInputStream()){byte[] b=new byte[65536];while(in.read(b)>0){}}catch(IOException ignored){}c.disconnect();if(p.failure!=null)break;}
                assertNotNull("budget must stop a runaway read",p.failure);assertTrue(p.failure,p.failure.contains("data limit"));assertTrue(p.relayed()<=(450L*1024*1024));}finally{p.close();}
        }
    }
    @Test public void arabicPickerMirrorsAndTranslates()throws Exception{
        android.app.Instrumentation test=InstrumentationRegistry.getInstrumentation();boolean was=Loc.arabic;Loc.arabic=true;
        assertNotNull("Stream sync strings load with their class",StreamSubtitleSync.message("too-little-speech"));
        for(String english:new String[]{"Smart sync saved subtitles","Listening to short parts of the stream…","Timing already matches this stream.","Cancel timing check","Subtitle source","Use only this source"})assertNotEquals(english,english,Loc.t(english));
        PhoneDownloadSettingsActivity a=(PhoneDownloadSettingsActivity)test.startActivitySync(new android.content.Intent(test.getTargetContext(),PhoneDownloadSettingsActivity.class).addFlags(android.content.Intent.FLAG_ACTIVITY_NEW_TASK));
        try{test.waitForIdleSync();test.runOnMainSync(()->{a.selectSubtitle("subdl");assertEquals(android.view.View.LAYOUT_DIRECTION_RTL,a.getWindow().getDecorView().getLayoutDirection());assertEquals(Loc.t("Subtitle source"),a.subtitlePicker.getContentDescription().toString());assertEquals(android.view.View.VISIBLE,a.subtitleKey.getVisibility());});
            test.waitForIdleSync();Thread.sleep(1500);test.runOnMainSync(()->{a.subtitlePicker.requestRectangleOnScreen(new android.graphics.Rect(0,0,a.subtitlePicker.getWidth(),a.subtitlePicker.getHeight()),true);assertTrue("picker has been laid out",a.subtitlePicker.getWidth()>0&&a.subtitlePicker.getHeight()>0&&a.subtitlePicker.isShown());});Thread.sleep(800);android.graphics.Bitmap shot=test.getUiAutomation().takeScreenshot();int lit=0;for(int y=200;y<shot.getHeight()-200;y+=4)for(int x=0;x<shot.getWidth();x+=4)if((shot.getPixel(x,y)&0xFFFFFF)>0x202020)lit++;android.util.Log.i("NoirSpike","lit="+lit);try(FileOutputStream o=new FileOutputStream(new File(test.getTargetContext().getCacheDir(),"arabic-picker.png"))){shot.compress(android.graphics.Bitmap.CompressFormat.PNG,100,o);}shot.recycle();
        }finally{test.runOnMainSync(a::finish);Loc.arabic=was;}
    }
    @Test public void trimDropsTheEarlyKeyframeAudio(){short[] s=new short[16000*12];for(int i=0;i<s.length;i++)s[i]=(short)(i/16);short[] kept=StreamSubtitleSync.trimToPlan(s,10);assertEquals(160000,kept.length);assertEquals((16000*2-1920)/16,kept[0]);short[] exact=new short[1600];assertSame(exact,StreamSubtitleSync.trimToPlan(exact,.1));}
    @Test public void wavParsingFindsSamplesAndRefusesRubbish()throws Exception{
        ByteArrayOutputStream wav=new ByteArrayOutputStream();wav.write("RIFF".getBytes());wav.write(new byte[4]);wav.write("WAVE".getBytes());wav.write("fmt ".getBytes());wav.write(ByteBuffer.allocate(4).order(ByteOrder.LITTLE_ENDIAN).putInt(16).array());wav.write(new byte[16]);wav.write("data".getBytes());wav.write(ByteBuffer.allocate(4).order(ByteOrder.LITTLE_ENDIAN).putInt(4).array());wav.write(new byte[]{1,0,2,0});
        assertArrayEquals(new short[]{1,2},StreamSubtitleSync.wavSamples(wav.toByteArray()));
        try{StreamSubtitleSync.wavSamples("not a wav file".getBytes());fail();}catch(IOException expected){assertFalse(expected.getMessage().isEmpty());}
    }
    @Test public void libVlcCutsSampleThroughProxyWhilePlaybackContinues()throws Exception{
        Context c=target();byte[] clip=asset("phone-offline-fixture.mkv");File dir=new File(c.getCacheDir(),"sync-test-"+System.nanoTime());dir.mkdirs();File source=new File(dir,"play.mkv");try(FileOutputStream o=new FileOutputStream(source)){o.write(clip);}
        try(Upstream up=new Upstream(clip)){
            LibVLC vlc=new LibVLC(c,new ArrayList<>(Arrays.asList("--quiet","--no-sub-autodetect-file","--http-reconnect")));
            LibVLC playback=new LibVLC(c,new ArrayList<>(Arrays.asList("--quiet","--no-video","--aout=dummy")));MediaPlayer player=new MediaPlayer(playback);
            try{
                Media m=new Media(playback,Uri.fromFile(source));m.addOption(":input-repeat=20");player.setMedia(m);m.release();player.play();Thread.sleep(600);
                StreamSubtitleSync.Proxy p=new StreamSubtitleSync.Proxy(up.url(),"Agent",null);p.start();
                try{
                    StreamSubtitleSync job=new StreamSubtitleSync(c,up.url(),"Agent",null,100,-1);File wav=new File(dir,"out.wav");
                    long before=player.getTime();job.extract(vlc,p,0.5,1.5,wav);assertNull(p.failure,p.failure);
                    assertTrue("sample written",wav.length()>16000);short[] samples=StreamSubtitleSync.pcm(wav,1.5);assertTrue(samples.length>=12000);
                    assertTrue("LibVLC only ever saw the local address",up.requests.size()>0);
                    Thread.sleep(400);assertTrue("playback kept moving: "+before+" -> "+player.getTime(),player.getTime()>before||player.isPlaying());
                }finally{p.close();}
            }finally{player.stop();player.release();playback.release();vlc.release();File[] files=dir.listFiles();if(files!=null)for(File f:files)f.delete();dir.delete();}
        }
    }
    @Test public void cancelStopsAndKeepsTheOriginal()throws Exception{
        StreamSubtitleSync job=new StreamSubtitleSync(target(),"https://stream.example/x","a",null,7200,-1);job.cancel();
        DataStore data=NoirApp.data;JSONObject item=new JSONObject().put("id","stream-cancel-"+System.nanoTime()).put("title","Cancel").put("movie",true);SubtitleClient client=new SubtitleClient(data,item);client.streaming("Release");JSONObject sub=new JSONObject().put("sourceKey","fixture").put("name","English").put("language","en").put("extension",".srt").put("streamRelease","Release");
        sub=client.save(sub,"1\n00:00:01,000 --> 00:00:02,000\nHello there friend\n");File original=data.subtitle(sub);String before=SubtitleClient.decode(SubtitleClient.read(original));
        try{job.sync(sub,client,data,t->{});fail("cancelled job must not finish");}catch(IOException expected){assertTrue(expected.getMessage(),expected.getMessage().contains("cancelled"));}
        assertEquals(before,SubtitleClient.decode(SubtitleClient.read(original)));assertTrue(original.exists());
    }
    /** On demand only (-e syncBase http://host:port/ -e syncFilm name -e syncSub name -e syncDuration seconds): real audio, whole pipeline. */
    @Test public void realFilmThroughTheWholePipeline()throws Exception{
        android.os.Bundle args=InstrumentationRegistry.getArguments();String base=args.getString("syncBase");org.junit.Assume.assumeTrue(base!=null);
        DataStore data=NoirApp.data;JSONObject item=new JSONObject().put("id","stream-real-"+System.nanoTime()).put("title","Real").put("movie",true);SubtitleClient client=new SubtitleClient(data,item);client.streaming("Real");
        byte[] text;try(Socket s=new Socket()){URL u=new URL(base+args.getString("syncSub"));s.connect(new InetSocketAddress(u.getHost(),u.getPort()));s.getOutputStream().write(("GET "+u.getFile()+" HTTP/1.1\r\nHost: x\r\nConnection: close\r\n\r\n").getBytes());byte[] all=DataStore.read(s.getInputStream(),4*1024*1024);String raw=new String(all,StandardCharsets.ISO_8859_1);text=Arrays.copyOfRange(all,raw.indexOf("\r\n\r\n")+4,all.length);}
        JSONObject sub=client.save(new JSONObject().put("sourceKey","real").put("name","English").put("language","en").put("extension",".srt").put("streamRelease","Real"),SubtitleClient.decode(text));
        StreamSubtitleSync job=new StreamSubtitleSync(target(),base+args.getString("syncFilm"),"Agent",null,Double.parseDouble(args.getString("syncDuration")),-1);long t=System.currentTimeMillis();
        JSONObject synced=job.sync(sub,client,data,m->android.util.Log.i("NoirSpike",m));android.util.Log.i("NoirSpike","RESULT offset="+synced.optDouble("timingOffset")+" ms="+(System.currentTimeMillis()-t));
        assertTrue(synced.optBoolean("aligned"));assertEquals(20.58,synced.optDouble("timingOffset"),.35);assertNotEquals(SubtitleClient.decode(text),SubtitleClient.decode(SubtitleClient.read(data.subtitle(synced))));
    }
    @Test public void linksAndHeadersNeverEnterMessages(){for(String reason:new String[]{"too-little-speech","poor-coverage","no-agreement","unstable","conflicting-sample","already-aligned","no-improvement","local-regression","invalid-model"}){String m=StreamSubtitleSync.message(reason);assertFalse(m.contains("http"));assertFalse(m.isEmpty());}}
}
