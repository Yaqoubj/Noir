package app.noir.mobile;
import android.app.Activity;
import android.content.*;
import android.graphics.Typeface;
import android.os.Bundle;
import android.webkit.*;
import android.widget.*;

/** P3: the site's own sign-in page, visible, with the sender's WebView settings; cookies stay in the app's WebView profile.
 A bot check, if the site shows one, is passed here by the person; Noir never automates it. */
public final class WatchSignInActivity extends Activity {
 WebView web;String provider,opened="";boolean released;
 static Intent intent(Context c,String provider){return new Intent(c,WatchSignInActivity.class).putExtra("provider",provider);}
 @Override public void onCreate(Bundle b){super.onCreate(b);provider="Serializd".equals(getIntent().getStringExtra("provider"))?"Serializd":"Trakt";
  LinearLayout root=new LinearLayout(this);root.setOrientation(LinearLayout.VERTICAL);root.setFitsSystemWindows(true);root.setBackgroundColor(MainActivity.BLACK);setContentView(root);
  TextView title=new TextView(this);title.setText(Loc.t("‹ Connections")+"     "+Loc.f("Sign in to %s",provider));title.setTextColor(MainActivity.WHITE);title.setTextSize(18);title.setTypeface(null,Typeface.BOLD);title.setPadding(18,18,18,8);title.setOnClickListener(v->finish());root.addView(title);
  TextView note=new TextView(this);note.setText(Loc.t("If the site shows a security check, complete it yourself here.")+"\n"+Loc.t(WatchSync.GOOGLE));note.setTextColor(MainActivity.MUTED);note.setTextSize(12);note.setPadding(18,0,18,8);root.addView(note);
  web=new WebView(this);web.setBackgroundColor(MainActivity.BLACK);WatchSyncSender.configure(web);root.addView(web,new LinearLayout.LayoutParams(-1,0,1));
  web.setWebViewClient(new WebViewClient(){
   @Override public WebResourceResponse shouldInterceptRequest(WebView v,WebResourceRequest r){java.util.function.Function<android.net.Uri,WebResourceResponse> pages=WatchSyncSender.testPages;if(pages==null)return null;WebResourceResponse page=pages.apply(r.getUrl());return page!=null?page:new WebResourceResponse("text/plain","utf-8",404,"Not Found",new java.util.HashMap<>(),new java.io.ByteArrayInputStream(new byte[0]));}
   @Override public boolean shouldOverrideUrlLoading(WebView v,WebResourceRequest r){return !"https".equals(r.getUrl().getScheme());}});
  opened=WatchSync.signInUrl(provider);web.loadUrl(opened);}
 void release(){if(released)return;released=true;CookieManager.getInstance().flush();DataStore d=NoirApp.data;if(d!=null){WatchSync.retry(d,provider);WatchSyncSender.wake();}if(web!=null){web.stopLoading();android.view.ViewParent p=web.getParent();if(p instanceof android.view.ViewGroup)((android.view.ViewGroup)p).removeView(web);web.destroy();}}
 @Override public void onBackPressed(){if(web!=null&&web.canGoBack())web.goBack();else super.onBackPressed();}
 @Override protected void onPause(){CookieManager.getInstance().flush();super.onPause();}
 @Override public void finish(){release();super.finish();}
 @Override protected void onDestroy(){release();super.onDestroy();}
}
