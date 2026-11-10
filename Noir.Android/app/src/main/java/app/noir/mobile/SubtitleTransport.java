package app.noir.mobile;

import javax.net.ssl.HttpsURLConnection;
import java.io.*;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.time.*;
import java.time.format.DateTimeFormatter;
import java.util.*;
import java.util.concurrent.*;

/** Network policy for subtitle sites: per-request headers, fixed hosts, manual validated redirects, public addresses only, hard caps and a wall-clock budget. */
final class SubtitleTransport {
    static final int CAP=4*1024*1024,MANIFEST_CAP=256*1024;static final long BUDGET_NANOS=35_000_000_000L;
    static final class Request {final String method,url;final Map<String,String> headers=new LinkedHashMap<>();final byte[] body;Request(String method,String url,byte[] body){this.method=method;this.url=url;this.body=body;}}
    /** Header names are lower case. */
    static final class Response {final int status;final Map<String,String> headers=new HashMap<>();final byte[] body;Response(int status,Map<String,String> headers,byte[] body){this.status=status;if(headers!=null)for(Map.Entry<String,String> e:headers.entrySet())if(e.getKey()!=null)this.headers.put(e.getKey().toLowerCase(Locale.ROOT),e.getValue());this.body=body==null?new byte[0]:body;}}
    interface Http {Response send(Request request,int cap,long deadline)throws IOException;}
    interface Resolver {InetAddress[] resolve(String host)throws IOException;}
    final Http http;final Resolver resolver;
    SubtitleTransport(){this(SubtitleTransport::connect,InetAddress::getAllByName);}
    SubtitleTransport(Http http,Resolver resolver){this.http=http;this.resolver=resolver;}
    static long deadline(){return System.nanoTime()+BUDGET_NANOS;}
    static long left(long deadline)throws IOException{long ms=(deadline-System.nanoTime())/1_000_000L;if(ms<=0)throw new SocketTimeoutException("Subtitle request timed out.");return ms;}
    /** Global unicast only: loopback, private, link-local, CGNAT, multicast, unspecified and IPv4-mapped forms of those are refused. */
    static boolean publicAddress(InetAddress address){
        byte[] b=address.getAddress();
        if(b.length==16){boolean mapped=true;for(int i=0;i<10;i++)if(b[i]!=0)mapped=false;if(mapped&&(b[10]&255)==255&&(b[11]&255)==255)b=Arrays.copyOfRange(b,12,16);}
        if(b.length==4){int a=b[0]&255,c=b[1]&255;return a!=0&&a!=10&&a!=127&&a<224&&!(a==100&&c>=64&&c<=127)&&!(a==169&&c==254)&&!(a==172&&c>=16&&c<=31)&&!(a==192&&c==168);}
        if(address.isLoopbackAddress()||address.isLinkLocalAddress()||address.isMulticastAddress()||address.isSiteLocalAddress()||address.isAnyLocalAddress())return false;
        return (b[0]&255)>=0x20&&(b[0]&255)<=0x3f;
    }
    static boolean validUrl(String link){
        try{
            URI u=new URI(link);String host=u.getHost();
            if(!"https".equalsIgnoreCase(u.getScheme())||host==null||host.isEmpty()||(u.getPort()!=-1&&u.getPort()!=443)||u.getRawUserInfo()!=null||u.getRawFragment()!=null)return false;
            String lower=host.toLowerCase(Locale.ROOT);if(lower.equals("localhost")||lower.endsWith(".localhost"))return false;
            if(lower.startsWith("[")){return publicAddress(InetAddress.getByName(lower));}
            if(lower.matches("[0-9.]+")){if(!lower.matches("(?:[0-9]{1,3}\\.){3}[0-9]{1,3}"))return false;for(String part:lower.split("\\."))if(Integer.parseInt(part)>255)return false;return publicAddress(InetAddress.getByName(lower));}
            return true;
        }catch(Exception e){return false;}
    }
    /** Best effort: HttpsURLConnection cannot be pinned to the address checked here, so the name is resolved and checked right before each request. */
    void checkAddresses(String host)throws IOException{
        String name=host.startsWith("[")&&host.endsWith("]")?host.substring(1,host.length()-1):host;InetAddress[] all=resolver.resolve(name);
        if(all==null||all.length==0)throw new IOException("Subtitle connection refused.");for(InetAddress a:all)if(!publicAddress(a))throw new IOException("Subtitle connection refused.");
    }
    static boolean ownHost(SubtitleSources.Selection source,URI uri){
        String host=uri.getHost()==null?"":uri.getHost().toLowerCase(Locale.ROOT);
        switch(source.source){
            case "opensubtitles":return host.equals("api.opensubtitles.com")||host.equals("vip-api.opensubtitles.com");
            case "subdl":return host.equals("api.subdl.com");
            case "subsource":return host.equals("api.subsource.net");
            case "wyzie":return host.equals("sub.wyzie.io");
            case "addon":try{return host.equals(new URI(source.addonUrl).getHost().toLowerCase(Locale.ROOT));}catch(Exception e){return false;}
            default:return false;
        }
    }
    byte[] send(SubtitleSources.Selection source,String url,boolean credentialed,String method,String body,int cap,boolean empty404,String token,boolean json,long deadline)throws IOException{
        try{
            URI original=new URI(url);
            for(int hop=0;hop<=3;hop++){
                if(!validUrl(url))throw new IOException();URI uri=new URI(url);
                if(credentialed&&(!ownHost(source,uri)||!uri.getHost().equalsIgnoreCase(original.getHost())))throw new IOException();
                checkAddresses(uri.getHost());
                Request request=new Request(method,url,body==null?null:body.getBytes(StandardCharsets.UTF_8));
                request.headers.put("User-Agent","Noir-Android/1.0");if(json)request.headers.put("Accept","application/json");if(body!=null)request.headers.put("Content-Type","application/json");
                if(credentialed)switch(source.source){
                    case "opensubtitles":request.headers.put("Api-Key",source.apiKey);if(!token.isEmpty())request.headers.put("Authorization","Bearer "+token);break;
                    case "subsource":request.headers.put("X-API-Key",source.apiKey);break;
                    case "addon":if(!source.addonAuth.isEmpty())request.headers.put("Authorization",source.addonAuth);break;
                    default:break;
                }
                Response response=http.send(request,cap,deadline);
                if(response.status==301||response.status==302||response.status==303||response.status==307||response.status==308){
                    String location=response.headers.get("location");if(hop==3||location==null||location.isEmpty())throw new IOException();
                    url=uri.resolve(location).toString();if(credentialed&&!new URI(url).getHost().equalsIgnoreCase(original.getHost()))throw new IOException();continue;
                }
                if(empty404&&response.status==404)return "[]".getBytes(StandardCharsets.UTF_8);
                if(response.body.length>cap)throw new IOException();
                if(response.status<200||response.status>=300){
                    int status=response.status;SubtitleSources.SubtitleFailure.Kind kind=status==401||status==403?SubtitleSources.SubtitleFailure.Kind.BAD_KEY:status==402||status==406||status==429?SubtitleSources.SubtitleFailure.Kind.LIMIT:SubtitleSources.SubtitleFailure.Kind.UNAVAILABLE;
                    throw new SubtitleSources.SubtitleFailure(source,kind,kind==SubtitleSources.SubtitleFailure.Kind.LIMIT?reset(response):"",status);
                }
                return response.body;
            }
            throw new IOException();
        }catch(SubtitleSources.SubtitleFailure e){throw e;}
        catch(IOException|URISyntaxException|RuntimeException e){if(Thread.currentThread().isInterrupted())throw new InterruptedIOException("Subtitle request cancelled.");throw new SubtitleSources.SubtitleFailure(source,SubtitleSources.SubtitleFailure.Kind.UNAVAILABLE,"",0);}
    }
    static final DateTimeFormatter UTC=DateTimeFormatter.ofPattern("yyyy-MM-dd HH:mm:ss'Z'").withZone(ZoneOffset.UTC);
    static Instant time(String value){
        if(value==null||value.trim().isEmpty())return null;String text=value.trim();
        try{return OffsetDateTime.parse(text).toInstant();}catch(Exception e){}
        try{return Instant.parse(text);}catch(Exception e){}
        try{return LocalDateTime.parse(text.replace(' ','T')).toInstant(ZoneOffset.UTC);}catch(Exception e){}
        return null;
    }
    /** Normalized to UTC; unparseable values are omitted, never echoed. */
    static String reset(Response response){
        try{Object doc=new org.json.JSONTokener(new String(response.body,StandardCharsets.UTF_8)).nextValue();
            if(doc instanceof org.json.JSONObject)for(String field:new String[]{"reset_time","reset_at"}){Instant at=time(String.valueOf(((org.json.JSONObject)doc).opt(field)));if(at!=null&&((org.json.JSONObject)doc).has(field))return UTC.format(at);}
        }catch(Exception e){}
        String retry=response.headers.get("retry-after");
        if(retry!=null){retry=retry.trim();try{long seconds=Long.parseLong(retry);if(seconds>=0&&seconds<=315_360_000L)return UTC.format(Instant.now().plusSeconds(seconds));}catch(NumberFormatException e){try{return UTC.format(ZonedDateTime.parse(retry,DateTimeFormatter.RFC_1123_DATE_TIME).toInstant());}catch(Exception ignored){}}}
        String epoch=response.headers.get("x-ratelimit-reset");
        if(epoch!=null)try{long value=Long.parseLong(epoch.trim());if(value>0&&value<253402300800L)return UTC.format(Instant.ofEpochSecond(value));}catch(Exception e){}
        return "";
    }
    static final ScheduledExecutorService WATCHDOG=Executors.newSingleThreadScheduledExecutor(r->{Thread t=new Thread(r,"noir-subtitle-watchdog");t.setDaemon(true);return t;});
    static byte[] readCapped(InputStream in,int cap,long deadline)throws IOException{
        ByteArrayOutputStream out=new ByteArrayOutputStream();byte[] buffer=new byte[8192];int n;
        while((n=in.read(buffer))!=-1){if(System.nanoTime()-deadline>0)throw new SocketTimeoutException();if(Thread.currentThread().isInterrupted())throw new InterruptedIOException();if(out.size()+(long)n>cap)throw new IOException("Subtitle response too large.");out.write(buffer,0,n);}
        return out.toByteArray();
    }
    static Response connect(Request request,int cap,long deadline)throws IOException{
        long left=left(deadline);HttpsURLConnection c=(HttpsURLConnection)new URL(request.url).openConnection(Proxy.NO_PROXY);ScheduledFuture<?> guard=WATCHDOG.schedule(c::disconnect,left,TimeUnit.MILLISECONDS);
        try{
            c.setInstanceFollowRedirects(false);c.setConnectTimeout((int)Math.min(7000,left));c.setReadTimeout((int)Math.min(25000,left));c.setRequestMethod(request.method);
            for(Map.Entry<String,String> header:request.headers.entrySet())c.setRequestProperty(header.getKey(),header.getValue());
            if(request.body!=null){c.setDoOutput(true);c.setFixedLengthStreamingMode(request.body.length);try(OutputStream out=c.getOutputStream()){out.write(request.body);}}
            int status=c.getResponseCode();Map<String,String> headers=new HashMap<>();
            for(Map.Entry<String,List<String>> e:c.getHeaderFields().entrySet())if(e.getKey()!=null&&!e.getValue().isEmpty())headers.put(e.getKey(),e.getValue().get(e.getValue().size()-1));
            if(status>=300&&status<400)return new Response(status,headers,null);
            if(c.getContentLengthLong()>cap&&status<400)throw new IOException("Subtitle response too large.");
            InputStream in=status>=400?c.getErrorStream():c.getInputStream();byte[] body=new byte[0];
            if(in!=null)try(InputStream stream=in){body=readCapped(stream,cap,deadline);}catch(IOException e){if(status<400)throw e;}
            return new Response(status,headers,body);
        }finally{guard.cancel(false);c.disconnect();}
    }
}
