package app.noir.mobile;
import org.json.*;
import java.io.*;
import java.util.*;
import java.util.zip.*;

/** Stremio's series watched field, the PC's StremioWatchedBitField line for line (stremio-core stremio-watched-bitfield):
 "<anchorVideoId>:<anchorLength>:<base64(zlib(bytes))>", one bit per video in Stremio's order (season, episode, release), bit i in byte i/8 at i%8.
 The anchor is the last watched video and its 1-based index, so a grown list realigns. Bits are only ever set: there is no way to clear one. */
final class StremioWriterBits {
 byte[] values;final List<String> ids;final int remoteBits;
 StremioWriterBits(byte[] v,List<String> ids,int remote){values=v;this.ids=ids;remoteBits=remote;}
 int length(){return ids.size();}
 int setBits(){return count(values);}
 /* Writing this back would erase watched episodes from the account. */
 boolean lostRemoteBits(){return setBits()<remoteBits;}
 static StremioWriterBits empty(List<String> ids){return new StremioWriterBits(new byte[(ids.size()+7)/8],ids,0);}
 static StremioWriterBits fromArray(boolean[] watched,List<String> ids){StremioWriterBits f=empty(ids);for(int i=0;i<watched.length;i++)if(watched[i])f.set(i);return f;}
 /* construct_with_videos: realign a serialized field onto the current video list. */
 static StremioWriterBits parse(String serialized,List<String> ids)throws IOException{
  int last=serialized.lastIndexOf(':'),previous=last>0?serialized.lastIndexOf(':',last-1):-1;
  if(previous<=0)throw new IOException("Invalid Stremio watched field.");String number=serialized.substring(previous+1,last);
  if(number.isEmpty()||!number.matches("[0-9]+"))throw new IOException("Invalid Stremio watched field.");int anchorLength;try{anchorLength=Integer.parseInt(number);}catch(NumberFormatException e){throw new IOException("Invalid Stremio watched field.");}
  String anchorVideo=serialized.substring(0,previous);byte[] remote=decompress(serialized.substring(last+1));int bits=count(remote),size=(ids.size()+7)/8,anchor=ids.indexOf(anchorVideo);
  if(anchor<0)return new StremioWriterBits(new byte[size],ids,bits);
  long offset=(long)anchorLength-anchor-1;
  if(offset==0)return new StremioWriterBits(remote.length>=size?remote:Arrays.copyOf(remote,size),ids,bits);
  byte[] moved=new byte[size];for(int i=0;i<ids.size();i++){long from=i+offset;if(from>=0&&from<ids.size()&&get(remote,from))moved[i/8]|=(byte)(1<<(i%8));}
  return new StremioWriterBits(moved,ids,bits);}
 boolean get(int i){return get(values,i);}
 boolean watched(String id){int i=ids.indexOf(id);return i>=0&&get(i);}
 /* false when the video is not in the list (Stremio's set_video is then a no-op too) */
 boolean mark(String id){int i=ids.indexOf(id);if(i<0)return false;set(i);return true;}
 void set(int i){if(i<0)throw new IndexOutOfBoundsException();if(i/8>=values.length)values=Arrays.copyOf(values,i/8+1);values[i/8]|=(byte)(1<<(i%8));}
 int lastWatched(){for(int i=length()-1;i>=0;i--)if(get(i))return i;return -1;}
 /* anchor = last watched video (index 0 when none), length 1-based, "undefined" for an empty list */
 @Override public String toString(){int lastId=Math.max(0,lastWatched());String anchor=lastId<ids.size()?ids.get(lastId):"undefined";return anchor+":"+(lastId+1)+":"+compress(values);}
 /* zlib at the default level, like flate2 and the PC; an empty input is zlib's own empty stream. */
 static String compress(byte[] bytes){if(bytes.length==0)return "eJwDAAAAAAE=";Deflater d=new Deflater(Deflater.DEFAULT_COMPRESSION);try{d.setInput(bytes);d.finish();ByteArrayOutputStream out=new ByteArrayOutputStream();byte[] buf=new byte[256];while(!d.finished()){int n=d.deflate(buf);out.write(buf,0,n);}return Base64.getEncoder().encodeToString(out.toByteArray());}finally{d.end();}}
 static byte[] decompress(String base64)throws IOException{byte[] in;try{in=Base64.getDecoder().decode(base64);}catch(IllegalArgumentException e){throw new IOException("Invalid Stremio watched field.");}
  Inflater f=new Inflater();try{f.setInput(in);ByteArrayOutputStream out=new ByteArrayOutputStream();byte[] buf=new byte[256];while(!f.finished()){int n=f.inflate(buf);if(n==0&&!f.finished()&&(f.needsInput()||f.needsDictionary()))throw new IOException("Invalid Stremio watched field.");out.write(buf,0,n);if(out.size()>1<<20)throw new IOException("Invalid Stremio watched field.");}return out.toByteArray();}catch(DataFormatException e){throw new IOException("Invalid Stremio watched field.");}finally{f.end();}}
 /* Stremio's order: season, episode (or number), release date; missing values first; stable otherwise. */
 static List<String> order(List<JSONObject> videos){List<JSONObject> list=new ArrayList<>();for(JSONObject v:videos)if(!v.optString("id").isEmpty())list.add(v);
  Collections.sort(list,(a,b)->{int c=Long.compare(num(a,"season"),num(b,"season"));if(c!=0)return c;c=Long.compare(episode(a),episode(b));return c!=0?c:Long.compare(released(a),released(b));});
  List<String> ids=new ArrayList<>();for(JSONObject v:list)ids.add(v.optString("id"));return ids;}
 static long num(JSONObject v,String name){Object o=v.opt(name);return o instanceof Number?((Number)o).longValue():Long.MIN_VALUE;}
 static long episode(JSONObject v){long e=num(v,"episode");return e!=Long.MIN_VALUE?e:num(v,"number");}
 static long released(JSONObject v){String r=v.optString("released");if(r.isEmpty())return Long.MIN_VALUE;long at=WatchSync.parse(r);return at==0&&!r.startsWith("1970")?Long.MIN_VALUE:at;}
 static boolean get(byte[] bytes,long i){return i>=0&&i/8<bytes.length&&((bytes[(int)(i/8)]>>(int)(i%8))&1)==1;}
 static int count(byte[] bytes){int n=0;for(byte b:bytes)n+=Integer.bitCount(b&0xFF);return n;}
}
