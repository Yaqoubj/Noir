package app.noir.mobile;
import java.io.*;
import java.net.*;
import java.util.*;
import java.util.concurrent.*;
import javax.net.ssl.HttpsURLConnection;

// VLC connects only to this loopback bridge. The bridge pins the PC's HTTPS certificate and
// adds the paired device token; neither is exposed in VLC URLs or sent over cleartext Wi-Fi.
final class MediaProxy implements AutoCloseable {
    final ServerSocket server;final DataStore data;final String id,key=UUID.randomUUID().toString();
    final ExecutorService pool=Executors.newFixedThreadPool(4);final Set<Socket> sockets=ConcurrentHashMap.newKeySet();volatile boolean closed=false;
    MediaProxy(DataStore data,String id)throws IOException{this.data=data;this.id=id;server=new ServerSocket(0,8,InetAddress.getByName("127.0.0.1"));Thread accept=new Thread(()->{while(!closed)try{Socket s=server.accept();s.setSoTimeout(10000);sockets.add(s);pool.execute(()->serve(s));}catch(Exception e){if(closed)break;}},"Noir media bridge");accept.setDaemon(true);accept.start();}
    String url(){return "http://127.0.0.1:"+server.getLocalPort()+"/"+key;}
    void serve(Socket socket){HttpsURLConnection upstream=null;try(socket){BufferedReader input=new BufferedReader(new InputStreamReader(socket.getInputStream(),java.nio.charset.StandardCharsets.US_ASCII));String first=input.readLine();if(first==null)return;String[] request=first.split(" ");OutputStream out=socket.getOutputStream();if(request.length!=3||!request[1].equals("/"+key)||!request[0].matches("GET|HEAD")){out.write("HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n".getBytes());return;}String range=null,line;int lines=0;while((line=input.readLine())!=null&&!line.isEmpty()){if(++lines>50||line.length()>4096)throw new IOException();if(line.toLowerCase(Locale.ROOT).startsWith("range:")){String candidate=line.substring(6).trim();if(candidate.matches("bytes=(?:[0-9]+-[0-9]*|-[0-9]+)"))range=candidate;}}
        upstream=data.media(id,range);upstream.setRequestMethod(request[0]);int code=upstream.getResponseCode();StringBuilder headers=new StringBuilder("HTTP/1.1 "+code+" "+(code==206?"Partial Content":code==200?"OK":"Unavailable")+"\r\nConnection: close\r\nContent-Type: application/octet-stream\r\n");for(String h:new String[]{"Content-Length","Content-Range","Accept-Ranges"}){String value=upstream.getHeaderField(h);if(value!=null&&!value.contains("\r")&&!value.contains("\n"))headers.append(h).append(": ").append(value).append("\r\n");}headers.append("\r\n");out.write(headers.toString().getBytes(java.nio.charset.StandardCharsets.US_ASCII));out.flush();if(request[0].equals("GET")&&(code==200||code==206)){try(InputStream in=upstream.getInputStream()){byte[] b=new byte[65536];int n;while(!closed&&(n=in.read(b))!=-1)out.write(b,0,n);}}out.flush();
    }catch(Exception ignored){}finally{if(upstream!=null)upstream.disconnect();sockets.remove(socket);}}
    public void close(){closed=true;try{server.close();}catch(Exception ignored){}for(Socket s:sockets)try{s.close();}catch(Exception ignored){}pool.shutdownNow();}
}
