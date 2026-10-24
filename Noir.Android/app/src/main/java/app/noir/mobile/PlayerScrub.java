package app.noir.mobile;
import android.graphics.*;
import android.media.MediaMetadataRetriever;
import android.net.Uri;
import android.util.LruCache;
import android.view.*;
import android.widget.*;
import java.io.InputStream;
import java.util.concurrent.*;

// Scrub thumbnails while dragging the seek bar: phone files decode locally, PC files come from the PC's preview cache.
final class PlayerScrub {
 final PlayerActivity a;final LinearLayout box;final ImageView image;final TextView label;
 final ExecutorService worker=Executors.newSingleThreadExecutor();final LruCache<Integer,Bitmap> cache=new LruCache<>(48);
 volatile int wanted=-1;MediaMetadataRetriever retriever;boolean localFailed,remoteFailed;
 PlayerScrub(PlayerActivity a,FrameLayout root){this.a=a;box=new LinearLayout(a);box.setOrientation(LinearLayout.VERTICAL);box.setPadding(a.dp(4),a.dp(4),a.dp(4),a.dp(6));box.setBackground(MainActivity.bg(0xF0161618,a.dp(10)));
  image=new ImageView(a);image.setScaleType(ImageView.ScaleType.CENTER_CROP);image.setBackground(MainActivity.bg(0xff1e1e21,a.dp(7)));box.addView(image,new LinearLayout.LayoutParams(a.dp(160),a.dp(90)));
  label=a.text("",12);label.setGravity(Gravity.CENTER);label.setPadding(0,a.dp(4),0,0);label.setSingleLine();label.setEllipsize(android.text.TextUtils.TruncateAt.END);box.addView(label,new LinearLayout.LayoutParams(a.dp(160),-2));
  box.setVisibility(View.GONE);root.addView(box,new FrameLayout.LayoutParams(-2,-2,Gravity.BOTTOM|Gravity.START));}
 boolean thumbnails(){return !a.data.localFile(a.item.optString("id")).isEmpty()?!localFailed:a.getIntent().getStringExtra("streamUrl")==null&&a.data.paired()&&!remoteFailed;}
 void show(double seconds,String chapter,SeekBar bar,int bottomOffset){
  label.setText(DataStore.time(seconds)+(chapter.isEmpty()?"":" · "+chapter));boolean frames=thumbnails();image.setVisibility(frames?View.VISIBLE:View.GONE);
  int[] at=new int[2];bar.getLocationInWindow(at);int width=a.dp(168);float x=at[0]+bar.getPaddingLeft()+(float)(bar.getWidth()-bar.getPaddingLeft()-bar.getPaddingRight())*bar.getProgress()/Math.max(1,bar.getMax());
  FrameLayout.LayoutParams p=(FrameLayout.LayoutParams)box.getLayoutParams();p.leftMargin=(int)Math.max(a.dp(8),Math.min(a.root.getWidth()-width-a.dp(8),x-width/2f));p.bottomMargin=bottomOffset+a.dp(6);box.setLayoutParams(p);box.setVisibility(View.VISIBLE);
  if(!frames)return;int slot=(int)(Math.max(0,seconds)/10)*10;if(slot==wanted)return;wanted=slot;Bitmap hit=cache.get(slot);image.setImageBitmap(hit);if(hit!=null)return;
  worker.execute(()->{if(wanted!=slot)return;Bitmap b=frame(slot);if(b!=null)cache.put(slot,b);a.handler.post(()->{if(wanted==slot&&b!=null&&!a.destroyed)image.setImageBitmap(b);});});}
 void hide(){wanted=-1;box.setVisibility(View.GONE);}
 Bitmap frame(int slot){String id=a.item.optString("id"),local=a.data.localFile(id);
  try{if(!local.isEmpty()){if(retriever==null){retriever=new MediaMetadataRetriever();if(local.startsWith("file:"))retriever.setDataSource(Uri.parse(local).getPath());else retriever.setDataSource(a,Uri.parse(local));}
    Bitmap raw=retriever.getFrameAtTime(slot*1000000L,MediaMetadataRetriever.OPTION_CLOSEST_SYNC);if(raw==null)return null;Bitmap small=Bitmap.createScaledBitmap(raw,320,Math.max(1,320*raw.getHeight()/Math.max(1,raw.getWidth())),true);if(small!=raw)raw.recycle();return small;}
   javax.net.ssl.HttpsURLConnection c=a.data.previewFrame(id,slot);try{if(c.getResponseCode()!=200)return null;BitmapFactory.Options o=new BitmapFactory.Options();try(InputStream in=c.getInputStream()){return BitmapFactory.decodeStream(new java.io.BufferedInputStream(in,64*1024),null,o);}}finally{c.disconnect();}
  }catch(Exception|OutOfMemoryError e){if(!local.isEmpty())localFailed=true;else if(!(e instanceof java.net.SocketTimeoutException))remoteFailed=true;return null;}}
 void close(){wanted=-1;worker.execute(()->{try{if(retriever!=null)retriever.release();}catch(Exception ignored){}});worker.shutdown();}
}
