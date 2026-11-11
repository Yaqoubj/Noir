package app.noir.mobile;

import android.content.Context;
import android.view.View;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import androidx.test.platform.app.InstrumentationRegistry;
import org.junit.Assume;
import org.junit.Test;
import java.util.concurrent.*;

/* Opt-in live check (-e live true): a WebView set up like the phone sender (shared settings, wide viewport,
   1280 CSS px layout) loads a public Trakt episode page signed out and reports what the PC script relies on. */
public class LiveTraktProbeTest {
 @Test public void traktEpisodePageAtDesktopWidth()throws Exception{
  Assume.assumeTrue("Opt-in: -e live true","true".equals(InstrumentationRegistry.getArguments().getString("live")));
  Context c=InstrumentationRegistry.getInstrumentation().getTargetContext();
  WebView[] w=new WebView[1];
  InstrumentationRegistry.getInstrumentation().runOnMainSync(()->{w[0]=new WebView(c);WatchSyncSender.configure(w[0]);w[0].getSettings().setUseWideViewPort(true);w[0].getSettings().setLoadWithOverviewMode(true);w[0].setWebViewClient(new WebViewClient());
   float d=c.getResources().getDisplayMetrics().density;int width=Math.round(1280*d),height=Math.round(820*d);
   w[0].measure(View.MeasureSpec.makeMeasureSpec(width,View.MeasureSpec.EXACTLY),View.MeasureSpec.makeMeasureSpec(height,View.MeasureSpec.EXACTLY));w[0].layout(0,0,width,height);
   String url=InstrumentationRegistry.getArguments().getString("url");w[0].loadUrl(url!=null?url:"https://app.trakt.tv/shows/tt0903747?view=episode&season=1&episode=1");});
  String probe="(()=>JSON.stringify({href:location.href,title:document.title,innerWidth:innerWidth,drawers:document.querySelectorAll('.trakt-drawer').length,"
   +"drawerWithMarker:[...document.querySelectorAll('.trakt-drawer')].filter(d=>d.innerText.includes('S1 • E1')).length,"
   +"visibleLabelled:[...document.querySelectorAll('button[aria-label]')].filter(b=>b.getClientRects().length).length,"
   +"markButtons:[...document.querySelectorAll('button[aria-label]')].filter(b=>/^Mark \".+\" as watched$/.test(b.getAttribute('aria-label'))).length,"
   +"signIn:[...document.querySelectorAll('a[href]')].map(a=>a.href).filter(h=>/sign|auth|login/i.test(h)).slice(0,5),buttons:[...document.querySelectorAll('button,a')].map(b=>(b.innerText||'').trim()).filter(t=>/sign|log/i.test(t)).slice(0,6),inputs:document.querySelectorAll('input[type=password],input[type=email]').length}))()";
  String last="";
  for(int n=0;n<12;n++){Thread.sleep(2500);CompletableFuture<String> f=new CompletableFuture<>();
   InstrumentationRegistry.getInstrumentation().runOnMainSync(()->w[0].evaluateJavascript(probe,f::complete));last=f.get(10,TimeUnit.SECONDS);
   android.util.Log.i("NoirLiveProbe","t="+(n+1)*2.5+"s "+last);if(last.contains("\\\"drawerWithMarker\\\":1"))break;}
  InstrumentationRegistry.getInstrumentation().runOnMainSync(()->w[0].destroy());
  System.out.println("NOIR_LIVE_PROBE "+last);
 }
}
