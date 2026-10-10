package app.noir.mobile;
import android.app.Instrumentation;
import android.content.Intent;
import androidx.test.platform.app.InstrumentationRegistry;
import org.junit.Test;
import static org.junit.Assert.*;
import org.json.*;
public final class EmbedTest {
 @Test public void testTopLinks(){assertEquals("https://www.reddit.com/r/tv/comments/abc123/discussion/comment42/?sort=top",ReviewsActivity.topDiscussion("https://old.reddit.com/r/tv/comments/abc123/discussion/comment42/?context=3"));assertNull(ReviewsActivity.topDiscussion("https://evil.test/r/tv/comments/abc123/"));}
 @Test public void testReviewLifecycle()throws Exception {
  Instrumentation ins=InstrumentationRegistry.getInstrumentation();java.lang.reflect.Field field=DataStore.class.getDeclaredField("state");field.setAccessible(true);DataStore original=NoirApp.data;DataStore fixture=new DataStore(ins.getTargetContext());NoirApp.data=fixture;JSONObject film=new JSONObject("{\"id\":\"review-fixture\",\"group\":\"review-group\",\"title\":\"Film\",\"movie\":true}");field.set(NoirApp.data,new JSONObject().put("library",new JSONObject().put("items",new JSONArray().put(film))));ReviewsActivity activity=null;
  try {Intent intent=new Intent(ins.getTargetContext(),ReviewsActivity.class).putExtra("id","review-fixture").addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);activity=(ReviewsActivity)ins.startActivitySync(intent);ReviewsActivity a=activity;ins.waitForIdleSync();String discussion="https://www.reddit.com/r/tv/comments/abc123/discussion/?sort=top";ins.runOnMainSync(()->{a.selected="Reddit";a.links.edit().putString("review-fixture",discussion).putString("tab:review-group","Reddit").commit();a.navigate(discussion);});assertEquals(discussion,a.target);ins.runOnMainSync(a::finish);assertTrue(a.destroyed);activity=(ReviewsActivity)ins.startActivitySync(intent);ins.waitForIdleSync();for(int n=0;n<100&&activity.selected.isEmpty();n++)Thread.sleep(50);assertEquals("Reddit",activity.selected);assertEquals(discussion,activity.target);
  }finally {if(activity!=null){ReviewsActivity a=activity;ins.runOnMainSync(a::finish);}NoirApp.data=original;fixture.network.shutdownNow();fixture.images.shutdownNow();}
 }
}
