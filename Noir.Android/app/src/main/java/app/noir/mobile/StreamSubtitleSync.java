package app.noir.mobile;

import android.content.Context;
import android.net.ConnectivityManager;
import android.net.Uri;
import org.json.JSONObject;
import org.videolan.libvlc.LibVLC;
import org.videolan.libvlc.Media;
import org.videolan.libvlc.MediaPlayer;
import java.io.*;
import java.net.*;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.*;
import java.util.concurrent.*;
import java.util.regex.*;

/** Corrects a subtitle's timing on the phone by listening to a few short parts of the stream. No PC involved; links and headers stay in memory. */
final class StreamSubtitleSync {
    static {
        String[][] words={
            {"Smart sync saved subtitles","مزامنة ذكية للترجمات المحفوظة"},
            {"Choose subtitle to sync","اختر الترجمة للمزامنة"},
            {"Cancel timing check","إلغاء فحص التوقيت"},
            {"Listening to short parts of the stream…","جارٍ الاستماع إلى أجزاء قصيرة من البث…"},
            {"Listening to part %d of %d…","جارٍ الاستماع إلى الجزء %d من %d…"},
            {"Timing corrected · %+.2fs · original kept","تم تصحيح التوقيت · %+.2f ث · الأصل محفوظ"},
            {"Timing already matches this stream.","التوقيت يطابق هذا البث بالفعل."},
            {"Timing check cancelled. The original subtitle is kept.","تم إلغاء فحص التوقيت. أُبقيت الترجمة الأصلية."},
            {"Couldn't hear enough speech in the parts checked. The original subtitle is kept. Use Adjust timing.","لم يُسمع كلام كافٍ في الأجزاء المفحوصة. أُبقيت الترجمة الأصلية. استخدم ضبط التوقيت."},
            {"The parts checked didn't agree, so the original subtitle is kept. Use Adjust timing.","لم تتفق الأجزاء المفحوصة، لذا أُبقيت الترجمة الأصلية. استخدم ضبط التوقيت."},
            {"This stream can't jump to a position, so its timing can't be checked. Use Adjust timing.","لا يمكن لهذا البث الانتقال إلى موضع محدد، لذا لا يمكن فحص التوقيت. استخدم ضبط التوقيت."},
            {"Timing check stopped at its data limit. The original subtitle is kept.","توقف فحص التوقيت عند حد البيانات. أُبقيت الترجمة الأصلية."},
            {"This stream link expired. Reopen the stream and try again.","انتهت صلاحية رابط هذا البث. أعد فتح البث وحاول مجددًا."},
            {"Couldn't reach the stream. The original subtitle is kept.","تعذر الوصول إلى البث. أُبقيت الترجمة الأصلية."},
            {"Wait until the video has started, then try again.","انتظر حتى يبدأ الفيديو ثم حاول مجددًا."},
            {"This video is too short to check automatically. Use Adjust timing.","هذا الفيديو أقصر من أن يُفحص تلقائيًا. استخدم ضبط التوقيت."},
            {"This subtitle file couldn't be read.","تعذرت قراءة ملف الترجمة هذا."},
            {"Timing check uses mobile data","يستخدم فحص التوقيت بيانات الجوال"},
            {"This listens to about a tenth of the stream's audio over your mobile data. Continue?","يستمع هذا إلى نحو عُشر صوت البث عبر بيانات الجوال. هل تريد المتابعة؟"},
            {"Continue","متابعة"},
            {"Download subtitles while streaming, without your PC. Saved subtitles reload for this release. Smart sync listens to short parts of the stream and keeps the original if it isn't sure.","نزّل الترجمة أثناء البث دون الكمبيوتر. تُحمّل الترجمات المحفوظة مجددًا لهذه النسخة. تستمع المزامنة الذكية إلى أجزاء قصيرة من البث وتبقي الأصل إن لم تكن متأكدة."},
        };
        for(String[] w:words)Loc.AR.put(w[0],w[1]);
    }
    /** Loads the Arabic strings above. */
    static void init(){}
    interface Progress {void update(String text);}
    static final int SAMPLE_SECONDS=45,OVERALL_SECONDS=360;
    private static final Set<String> NEEDS_MORE=new HashSet<>(Arrays.asList("too-little-speech","poor-coverage"));

    final Context context;final String url,agent,referrer;final double duration;final int audioIndex;
    private volatile boolean cancelled;private volatile MediaPlayer activePlayer;private volatile Proxy activeProxy;private final Object lock=new Object();
    private final List<SampledAlignment.Sample> heard=new ArrayList<>();private final List<short[]> pcmHeard=new ArrayList<>();private final List<Double> starts=new ArrayList<>();private boolean extended=false;private long deadline,spentBytes=0,knownTotal=-1;private int spentRequests=0;

    StreamSubtitleSync(Context context,String url,String agent,String referrer,double duration,int audioIndex){this.context=context.getApplicationContext();this.url=url;this.agent=agent;this.referrer=referrer;this.duration=duration;this.audioIndex=audioIndex;}

    static boolean metered(Context c){try{ConnectivityManager m=c.getSystemService(ConnectivityManager.class);return m!=null&&m.isActiveNetworkMetered();}catch(RuntimeException e){return false;}}

    boolean isCancelled(){return cancelled;}
    void cancel(){cancelled=true;MediaPlayer p=activePlayer;if(p!=null)try{p.stop();}catch(RuntimeException ignored){}Proxy proxy=activeProxy;if(proxy!=null)proxy.close();}

    /** Corrected subtitle, saved beside the original. Throws IOException with a plain message when nothing changed. */
    JSONObject sync(JSONObject sub,SubtitleClient client,DataStore data,Progress progress)throws Exception{
        File file=data.subtitle(sub);if(file==null||!file.exists())throw new IOException("This subtitle file couldn't be read.");
        String text=SubtitleClient.decode(SubtitleClient.read(file)),extension=sub.optString("extension",".srt");
        List<double[]> times=SubtitleTiming.cues(text,extension);if(times.isEmpty())throw new IOException("This subtitle file couldn't be read.");
        double[][] cues=times.toArray(new double[0][]);Arrays.sort(cues,Comparator.comparingDouble(c->c[0]));
        if(duration<=0)throw new IOException("Wait until the video has started, then try again.");
        double[][] windows=SampledAlignment.planWindows(duration);if(windows.length==0)throw new IOException("This video is too short to check automatically. Use Adjust timing.");
        deadline=System.currentTimeMillis()+OVERALL_SECONDS*1000L;
        synchronized(lock){
            if(heard.isEmpty())listen(windows,progress);
            SampledAlignment.Result result=SampledAlignment.align(heard,cues);
            if(!result.accepted&&NEEDS_MORE.contains(result.reason)&&!extended){extended=true;listen(SampledAlignment.planExtraWindows(duration),progress);result=SampledAlignment.align(heard,cues);}
            if(!result.accepted)throw new IOException(message(result.reason));
            if(cancelled)throw new IOException("Timing check cancelled. The original subtitle is kept.");
            String corrected=SubtitleTiming.apply(text,extension,result.scale,result.offset);if(!SubtitleClient.validText(corrected,extension))throw new IOException("This subtitle file couldn't be read.");
            JSONObject next=DataStore.copy(sub);if(next.optString("sourceKey").isEmpty())DataStore.put(next,"sourceKey","stream:"+sub.optString("id"));DataStore.put(next,"aligned",true);DataStore.put(next,"originalId",sub.optString("originalId",sub.optString("id")));
            JSONObject saved=client.save(next,corrected);DataStore.put(saved,"timingOffset",result.offset);return saved;
        }
    }
    static String message(String reason){return reason.equals("already-aligned")?"Timing already matches this stream.":reason.equals("too-little-speech")||reason.equals("poor-coverage")?"Couldn't hear enough speech in the parts checked. The original subtitle is kept. Use Adjust timing.":"The parts checked didn't agree, so the original subtitle is kept. Use Adjust timing.";}

    private void listen(double[][] windows,Progress progress)throws Exception{
        if(windows.length==0)return;File dir=new File(context.getCacheDir(),"stream-sync-"+System.nanoTime());dir.mkdirs();
        Proxy proxy=new Proxy(url,agent,referrer);proxy.resume(spentBytes,spentRequests,knownTotal);activeProxy=proxy;proxy.start();
        LibVLC vlc=new LibVLC(context,new ArrayList<>(Arrays.asList("--quiet","--no-sub-autodetect-file","--http-reconnect")));
        try{
            List<short[]> chunks=new ArrayList<>();
            for(int i=0;i<windows.length;i++){
                ensureActive();progress.update(String.format(Locale.ROOT,Loc.t("Listening to part %d of %d…"),i+1,windows.length));
                File wav=new File(dir,i+".wav");extract(vlc,proxy,windows[i][0],windows[i][1],wav);chunks.add(pcm(wav,windows[i][1]));wav.delete();
            }
            ensureActive();pcmHeard.addAll(chunks);for(double[] w:windows)starts.add(w[0]);
            // The detector adapts on everything heard so far, as the PC tool does.
            List<Integer> order=new ArrayList<>();for(int i=0;i<starts.size();i++)order.add(i);order.sort(Comparator.comparingDouble(starts::get));
            List<short[]> sorted=new ArrayList<>();for(int i:order)sorted.add(pcmHeard.get(i));List<boolean[]> masks=SampledAlignment.speechMasks(sorted);
            heard.clear();for(int k=0;k<order.size();k++)heard.add(new SampledAlignment.Sample(starts.get(order.get(k)),masks.get(k)));
        }finally{spentBytes=proxy.relayed();spentRequests=proxy.requests();knownTotal=proxy.total();vlc.release();proxy.close();activeProxy=null;deleteAll(dir);}
    }
    private void ensureActive()throws IOException{if(cancelled)throw new IOException("Timing check cancelled. The original subtitle is kept.");if(System.currentTimeMillis()>deadline)throw new IOException("Couldn't reach the stream. The original subtitle is kept.");}
    void extract(LibVLC vlc,Proxy proxy,double start,double length,File out)throws Exception{
        if(deadline==0)deadline=System.currentTimeMillis()+OVERALL_SECONDS*1000L;
        Media media=new Media(vlc,Uri.parse(proxy.url()));
        media.addOption(":no-video");media.addOption(":no-spu");media.addOption(":start-time="+String.format(Locale.ROOT,"%.3f",start));media.addOption(":stop-time="+String.format(Locale.ROOT,"%.3f",start+length));
        if(audioIndex>=0)media.addOption(":audio-track="+audioIndex);
        media.addOption(":sout=#transcode{vcodec=none,acodec=s16l,channels=1,samplerate=16000}:std{access=file,mux=wav,dst="+out.getAbsolutePath()+"}");media.addOption(":no-sout-all");
        MediaPlayer player=new MediaPlayer(vlc);CountDownLatch done=new CountDownLatch(1);
        player.setEventListener(e->{if(e.type==MediaPlayer.Event.EndReached||e.type==MediaPlayer.Event.Stopped||e.type==MediaPlayer.Event.EncounteredError)done.countDown();});
        activePlayer=player;
        try{player.setMedia(media);media.release();player.play();long wait=Math.min(SAMPLE_SECONDS*1000L,Math.max(1000,deadline-System.currentTimeMillis()));if(!done.await(wait,TimeUnit.MILLISECONDS))throw new IOException("Couldn't reach the stream. The original subtitle is kept.");}
        finally{activePlayer=null;player.setEventListener(null);player.stop();player.release();}
        ensureActive();String failure=proxy.failure;if(failure!=null)throw new IOException(failure);
    }
    /** Samples from a WAV file. Short results count as failed: an incomplete sample would misplace the audio. */
    static short[] pcm(File wav,double expectedSeconds)throws IOException{
        byte[] bytes;try(InputStream in=new FileInputStream(wav)){bytes=DataStore.read(in,(int)Math.min(Integer.MAX_VALUE-8,(long)((expectedSeconds+60)*32000)));}
        short[] samples=wavSamples(bytes);if(samples.length<expectedSeconds*16000*.5)throw new IOException("Couldn't reach the stream. The original subtitle is kept.");
        return trimToPlan(samples,expectedSeconds);
    }
    /** VLC starts at the keyframe before the requested time but stops on time, about 0.12 s late: the extra length tells how much early audio to drop. */
    static short[] trimToPlan(short[] samples,double seconds){
        int planned=(int)(seconds*16000);if(samples.length<=planned)return samples;
        int head=Math.max(0,samples.length-planned-1920);return Arrays.copyOfRange(samples,head,head+planned);
    }
    static short[] wavSamples(byte[] bytes)throws IOException{
        ByteBuffer b=ByteBuffer.wrap(bytes).order(ByteOrder.LITTLE_ENDIAN);
        if(bytes.length<12||b.getInt(0)!=0x46464952||b.getInt(8)!=0x45564157)throw new IOException("Couldn't reach the stream. The original subtitle is kept.");
        int at=12;while(at+8<=bytes.length){int id=b.getInt(at),size=b.getInt(at+4);if(id==0x61746164){int start=at+8,count=size<=0||start+(long)size>bytes.length?bytes.length-start:size;short[] out=new short[count/2];b.position(start);b.asShortBuffer().get(out);return out;}if(size<0)break;at+=8+size+(size&1);}
        throw new IOException("Couldn't reach the stream. The original subtitle is kept.");
    }
    private static void deleteAll(File dir){File[] files=dir.listFiles();if(files!=null)for(File f:files)f.delete();dir.delete();}

    /** Loopback-only relay: VLC sees a meaningless local address. Enforces real range support and a data budget. */
    static final class Proxy {
        static final long MIB=1048576L,WINDOW=16*MIB,MIN_BUDGET=400*MIB;static final int MAX_REQUESTS=400;
        final String upstream,agent,referrer;final String path="/"+UUID.randomUUID().toString().replace("-","");private final ExecutorService pool=Executors.newCachedThreadPool();private ServerSocket server;private volatile boolean closed;
        volatile String failure;private long relayed=0;private long total=-1;private int requests=0;
        Proxy(String upstream,String agent,String referrer){this.upstream=upstream;this.agent=agent;this.referrer=referrer;}
        String url(){return "http://127.0.0.1:"+server.getLocalPort()+path;}
        void start()throws IOException{
            URI u;try{u=new URI(upstream);}catch(Exception e){throw new IOException("Couldn't reach the stream. The original subtitle is kept.");}
            if(u.getScheme()==null||!Arrays.asList("http","https").contains(u.getScheme().toLowerCase(Locale.ROOT)))throw new IOException("Couldn't reach the stream. The original subtitle is kept.");
            server=new ServerSocket(0,16,InetAddress.getByAddress(new byte[]{127,0,0,1}));
            Thread accept=new Thread(()->{while(!closed)try{Socket s=server.accept();pool.execute(()->handle(s));}catch(IOException e){return;}},"stream-sync-proxy");accept.setDaemon(true);accept.start();
        }
        void close(){closed=true;try{if(server!=null)server.close();}catch(IOException ignored){}pool.shutdownNow();}
        private void fail(String message){if(failure==null)failure=message;}
        private synchronized boolean allowRequest(){if(++requests>MAX_REQUESTS){fail("Timing check stopped at its data limit. The original subtitle is kept.");return false;}return true;}
        private synchronized boolean count(long n){relayed+=n;if(relayed>Math.max(MIN_BUDGET,total>0?(long)(total*.35):0)){fail("Timing check stopped at its data limit. The original subtitle is kept.");return false;}return true;}
        synchronized long relayed(){return relayed;}
        synchronized int requests(){return requests;}
        synchronized long total(){return total;}
        /** Both listening passes of one job share one budget. */
        synchronized void resume(long bytes,int count,long size){relayed=bytes;requests=count;total=size;}
        private void handle(Socket socket){
            try(Socket s=socket;InputStream in=new BufferedInputStream(s.getInputStream());OutputStream out=new BufferedOutputStream(s.getOutputStream())){
                s.setSoTimeout(30000);String request=readHeaders(in);if(request==null)return;String[] lines=request.split("\r\n");String[] parts=lines[0].split(" ");
                if(parts.length<2||!parts[1].equals(path)||!(parts[0].equals("GET")||parts[0].equals("HEAD"))){simple(out,404);return;}
                if(!allowRequest()){simple(out,503);return;}
                long start=0,end=-1;for(String line:lines)if(line.toLowerCase(Locale.ROOT).startsWith("range:")){Matcher m=Pattern.compile("bytes=(\\d+)-(\\d*)").matcher(line.substring(6).trim());if(m.find()){start=Long.parseLong(m.group(1));if(!m.group(2).isEmpty())end=Long.parseLong(m.group(2));}}
                long stop=end<0?start+WINDOW-1:Math.min(end,start+WINDOW-1);relay(out,parts[0].equals("HEAD"),start,stop);
            }catch(IOException ignored){}
        }
        private static String readHeaders(InputStream in)throws IOException{StringBuilder b=new StringBuilder();int c;while((c=in.read())>=0&&b.length()<16384){b.append((char)c);if(b.length()>=4&&b.substring(b.length()-4).equals("\r\n\r\n"))return b.toString();}return null;}
        private static void simple(OutputStream out,int status)throws IOException{out.write(("HTTP/1.1 "+status+" Error\r\nContent-Length: 0\r\nConnection: close\r\n\r\n").getBytes("US-ASCII"));out.flush();}
        /** One upstream reply. Plain http goes over a raw socket: Android's cleartext policy would block HttpURLConnection, and the player can already open such links. */
        private static final class Reply implements Closeable {
            int status;final Map<String,String> headers=new HashMap<>();InputStream body;Closeable handle;
            String header(String name){return headers.get(name.toLowerCase(Locale.ROOT));}
            @Override public void close(){try{if(handle!=null)handle.close();}catch(IOException ignored){}}
        }
        private Reply open(String link,long start,long stop)throws IOException{
            URL u=new URL(link);Reply reply=new Reply();
            if(u.getProtocol().equalsIgnoreCase("https")){
                HttpURLConnection c=(HttpURLConnection)u.openConnection();c.setConnectTimeout(10000);c.setReadTimeout(15000);c.setInstanceFollowRedirects(false);c.setRequestProperty("Range","bytes="+start+"-"+stop);c.setRequestProperty("Accept-Encoding","identity");if(agent!=null&&!agent.isEmpty())c.setRequestProperty("User-Agent",agent);if(referrer!=null&&!referrer.isEmpty())c.setRequestProperty("Referer",referrer);
                reply.status=c.getResponseCode();for(Map.Entry<String,List<String>> e:c.getHeaderFields().entrySet())if(e.getKey()!=null&&!e.getValue().isEmpty())reply.headers.put(e.getKey().toLowerCase(Locale.ROOT),e.getValue().get(0));
                reply.handle=c::disconnect;reply.body=reply.status==206?c.getInputStream():null;return reply;
            }
            Socket s=new Socket();s.connect(new InetSocketAddress(u.getHost(),u.getPort()<0?80:u.getPort()),10000);s.setSoTimeout(15000);reply.handle=s;
            String path=u.getFile().isEmpty()?"/":u.getFile();StringBuilder request=new StringBuilder("GET "+path+" HTTP/1.1\r\nHost: "+u.getHost()+(u.getPort()<0?"":":"+u.getPort())+"\r\nRange: bytes="+start+"-"+stop+"\r\nAccept-Encoding: identity\r\nConnection: close\r\n");
            if(agent!=null&&!agent.isEmpty()&&agent.indexOf('\n')<0&&agent.indexOf('\r')<0)request.append("User-Agent: ").append(agent).append("\r\n");if(referrer!=null&&!referrer.isEmpty()&&referrer.indexOf('\n')<0&&referrer.indexOf('\r')<0)request.append("Referer: ").append(referrer).append("\r\n");request.append("\r\n");
            OutputStream out=s.getOutputStream();out.write(request.toString().getBytes("ISO-8859-1"));out.flush();InputStream in=new BufferedInputStream(s.getInputStream());String head=readHeaders(in);
            if(head==null){reply.close();throw new IOException("No reply");}String[] lines=head.split("\r\n");Matcher status=Pattern.compile("HTTP/1\\.[01] (\\d{3})").matcher(lines[0]);if(!status.find()){reply.close();throw new IOException("Bad reply");}reply.status=Integer.parseInt(status.group(1));
            for(int i=1;i<lines.length;i++){int colon=lines[i].indexOf(':');if(colon>0)reply.headers.put(lines[i].substring(0,colon).trim().toLowerCase(Locale.ROOT),lines[i].substring(colon+1).trim());}
            reply.body=in;return reply;
        }
        private void relay(OutputStream out,boolean head,long start,long stop)throws IOException{
            String link=upstream;Reply c=null;
            try{
                int hops=0;
                while(true){
                    c=open(link,start,stop);int status=c.status;
                    if(status>=300&&status<400){String next=c.header("Location");c.close();if(next==null||++hops>3){fail("Couldn't reach the stream. The original subtitle is kept.");simple(out,502);return;}URL resolved=new URL(new URL(link),next);if(!Arrays.asList("http","https").contains(resolved.getProtocol().toLowerCase(Locale.ROOT))){fail("Couldn't reach the stream. The original subtitle is kept.");simple(out,502);return;}link=resolved.toString();continue;}
                    if(status==401||status==403||status==410){fail("This stream link expired. Reopen the stream and try again.");simple(out,502);return;}
                    if(status==416){simple(out,416);return;}
                    if(status!=206){fail(status==200?"This stream can't jump to a position, so its timing can't be checked. Use Adjust timing.":"Couldn't reach the stream. The original subtitle is kept.");simple(out,502);return;}
                    break;
                }
                Matcher range=Pattern.compile("bytes (\\d+)-(\\d+)/(\\d+|\\*)").matcher(String.valueOf(c.header("Content-Range")));
                if(!range.find()||Long.parseLong(range.group(1))!=start){fail("This stream can't jump to a position, so its timing can't be checked. Use Adjust timing.");simple(out,502);return;}
                long first=Long.parseLong(range.group(1)),last=Math.min(Long.parseLong(range.group(2)),stop);if(last<first){fail("This stream can't jump to a position, so its timing can't be checked. Use Adjust timing.");simple(out,502);return;}String size=range.group(3);if(!size.equals("*"))synchronized(this){total=Long.parseLong(size);}
                long length=last-first+1;
                out.write(("HTTP/1.1 206 Partial Content\r\nAccept-Ranges: bytes\r\nContent-Type: application/octet-stream\r\nContent-Length: "+length+"\r\nContent-Range: bytes "+first+"-"+last+"/"+size+"\r\nConnection: close\r\n\r\n").getBytes("US-ASCII"));
                if(head){out.flush();return;}
                InputStream body=c.body;byte[] buffer=new byte[65536];long left=length;int n;while(left>0&&!closed&&(n=body.read(buffer,0,(int)Math.min(buffer.length,left)))>0){out.write(buffer,0,n);left-=n;if(!count(n))return;}
                out.flush();
            }finally{if(c!=null)c.close();}
        }
    }
}
