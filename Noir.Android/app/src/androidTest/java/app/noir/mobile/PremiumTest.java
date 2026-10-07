package app.noir.mobile;
import androidx.test.platform.app.InstrumentationRegistry;
import org.junit.Test;
import static org.junit.Assert.*;
import org.json.*;
import java.io.*;
import java.util.*;
import javax.net.ssl.HttpsURLConnection;
public final class PremiumTest {
 @Test public void testOrganizationSync()throws Exception {DataStore data=NoirApp.data;if(!data.paired())data.pair(TestRunner.arguments.getString("pairUri"));else data.sync();JSONObject item=DataStore.objects(data.items()).get(0);String id=item.optString("id");JSONObject status=data.operation(id,"status");DataStore.put(status,"status","Dropped");data.queue(status);JSONObject collection=data.operation(id,"collection");DataStore.put(collection,"name","Phone favorites");DataStore.put(collection,"value",true);data.queue(collection);data.queueVideo(id);data.queueVideo(id);DataStore restored=new DataStore(InstrumentationRegistry.getInstrumentation().getTargetContext());assertEquals("Dropped",restored.item(id).optString("status"));assertTrue(restored.item(id).optJSONArray("collections").toString().contains("Phone favorites"));assertTrue(restored.playQueue().toString().contains(id));data.sync();assertEquals("Dropped",data.item(id).optString("status"));assertTrue(data.item(id).optJSONArray("collections").toString().contains("Phone favorites"));data.consumeVideo(id);assertFalse(data.playQueue().toString().contains(id));}
 @Test public void testResumableDownload()throws Exception {
  DataStore data=NoirApp.data;if(!data.paired())data.pair(TestRunner.arguments.getString("pairUri"));else data.sync();JSONObject item=DataStore.objects(data.items()).stream().filter(i->i.optString("title").equals("Download fixture")).findFirst().get();String id=item.optString("id");DownloadStore downloads=NoirApp.downloads;downloads.enqueue(item);
  HttpsURLConnection c=data.media(id,"bytes=0-4095");String etag;long total;byte[] prefix;try{assertEquals(206,c.getResponseCode());etag=c.getHeaderField("ETag");total=DownloadStore.range(c.getHeaderField("Content-Range"))[2];prefix=DataStore.read(c.getInputStream(),4096);}finally{c.disconnect();}assertNotNull(etag);
  try(FileOutputStream out=new FileOutputStream(downloads.file(id,true))){out.write(prefix);}downloads.update(id,"paused",4096,total,etag,"");DownloadStore restored=new DownloadStore(InstrumentationRegistry.getInstrumentation().getTargetContext());assertEquals("paused",restored.job(id).optString("status"));assertEquals(4096,restored.file(id,true).length());restored.enqueue(item);restored.transfer(id,j->{});assertEquals("complete",restored.job(id).optString("status"));assertEquals(total,restored.file(id,false).length());try(InputStream in=new FileInputStream(restored.file(id,false))){byte[] bytes=DataStore.read(in,2*1024*1024);for(int n=0;n<bytes.length;n++)if(bytes[n]!=(byte)(n%251))fail("Resumed file differs at byte "+n);}assertTrue(data.localFile(id).startsWith("file:"));assertFalse("Saving a file does not mark it watched",DataStore.watched(data.item(id)));restored.remove(id);assertEquals("",data.localFile(id));
  downloads.enqueue(item);downloads.transfer(id,j->{if(j.optString("status").equals("downloading"))downloads.pause(id);});assertEquals("paused",downloads.job(id).optString("status"));assertFalse(downloads.file(id,false).exists());downloads.remove(id);
  for(String bad:new String[]{"bytes 4-2/6","bytes 0-10/5","bytes */50","bad"})try{DownloadStore.range(bad);fail("Invalid range accepted");}catch(IOException expected){}
 }
}
