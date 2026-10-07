package app.noir.mobile;
import android.app.*;
import android.os.*;
import android.net.Uri;
import android.view.View;
import android.webkit.*;
import android.widget.*;
import android.content.SharedPreferences;
import org.json.JSONObject;

public final class ReviewsActivity extends Activity {
    WebView web;
    JSONObject item;
    Button paste;
    TextView status;
    String selected="",target="";
    volatile boolean destroyed;
    SharedPreferences links;
    @Override public void onCreate(Bundle b){super.onCreate(b);item=NoirApp.data.item(getIntent().getStringExtra("id"));if(item==null){finish();return;}
        links=getSharedPreferences("community-embeds",MODE_PRIVATE);
        LinearLayout root=new LinearLayout(this);root.setOrientation(LinearLayout.VERTICAL);root.setFitsSystemWindows(true);root.setBackgroundColor(MainActivity.BLACK);setContentView(root);
        TextView title=new TextView(this);title.setText("‹ Library     Reviews & comments");title.setTextColor(MainActivity.WHITE);title.setTextSize(18);title.setPadding(18,18,18,18);title.setOnClickListener(v->finish());root.addView(title);
        Spinner sites=new Spinner(this);String[] labels=item.optBoolean("movie")?new String[]{"IMDb","Letterboxd","Trakt","Reddit"}:new String[]{"IMDb","Serializd","Trakt","Reddit"};sites.setAdapter(new ArrayAdapter<>(this,android.R.layout.simple_spinner_dropdown_item,labels));root.addView(sites);
        LinearLayout modes=new LinearLayout(this);HorizontalScrollView scroll=new HorizontalScrollView(this);scroll.addView(modes);root.addView(scroll);
        paste=button("Use Reddit link",modes,()->chooseLink());
        status=new TextView(this);status.setTextColor(MainActivity.WHITE);status.setTextSize(12);status.setPadding(16,4,16,8);root.addView(status);
        web=new WebView(this);web.setBackgroundColor(MainActivity.BLACK);root.addView(web,new LinearLayout.LayoutParams(-1,0,1));
        web.getSettings().setJavaScriptEnabled(true);web.getSettings().setDomStorageEnabled(true);web.getSettings().setAllowFileAccess(false);web.getSettings().setAllowContentAccess(false);web.getSettings().setMixedContentMode(WebSettings.MIXED_CONTENT_NEVER_ALLOW);
        CookieManager.getInstance().setAcceptCookie(true);CookieManager.getInstance().setAcceptThirdPartyCookies(web,true);
        web.setWebViewClient(new WebViewClient(){
            @Override public boolean shouldOverrideUrlLoading(WebView v,WebResourceRequest r){return !"https".equals(r.getUrl().getScheme());}
            @Override public void onPageStarted(WebView v,String url,android.graphics.Bitmap icon){if(url.startsWith("https://")){target=url;updateModes();String post=topDiscussion(url);if(selected.equals("Reddit")&&post!=null){links.edit().putString(item.optString("id"),post).apply();if(!url.equals(post))web.loadUrl(post);}}}
            @Override public void onPageFinished(WebView v,String url){if(!destroyed)status.setText(selected.equals("Reddit")?"Top comments · Check spoiler tags. Your chosen discussion is remembered.":"Online reviews · Your sign-in is saved.");}
            @Override public void onReceivedError(WebView v,WebResourceRequest r,WebResourceError e){if(r.isForMainFrame())status.setText("Couldn't load this page. Check your connection or try another tab.");}
        });
        sites.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener(){public void onNothingSelected(AdapterView<?> p){}public void onItemSelected(AdapterView<?> p,View v,int n,long id){selected=labels[n];links.edit().putString("tab:"+item.optString("group"),selected).apply();target=source(n);
            String saved=links.getString(item.optString("id"),"");if(selected.equals("Reddit")&&topDiscussion(saved)!=null)target=topDiscussion(saved);
            navigate(target);
        }});
        String tab=links.getString("tab:"+item.optString("group"),"");for(int n=0;n<labels.length;n++)if(labels[n].equals(tab))sites.setSelection(n);
    }
    Button button(String text,LinearLayout parent,Runnable action){Button b=new Button(this);b.setText(text);b.setTextColor(MainActivity.WHITE);parent.addView(b);b.setOnClickListener(v->action.run());return b;}
    String source(int n){String title=item.optString("title"),imdb=item.optString("episodeImdb");if(imdb.isEmpty())imdb=item.optString("imdb");String url;
        if(n==0)url=imdb.matches("tt[0-9]+")?"https://www.imdb.com/title/"+imdb+"/reviews/":"https://www.imdb.com/find/?q="+Uri.encode(title);
        else if(n==1)url=item.optBoolean("movie")?"https://letterboxd.com/search/"+Uri.encode(title)+"/":"https://www.serializd.com/search?query="+Uri.encode(title);
        else if(n==2)url=item.optString("imdb").matches("tt[0-9]+")?"https://trakt.tv/search/imdb/"+item.optString("imdb"):"https://trakt.tv/search?query="+Uri.encode(title);
        else url="https://www.reddit.com/search/?q="+Uri.encode(title+(item.optBoolean("movie")?" discussion":" "+MainActivity.episode(item)+" discussion"));
        JSONObject reviews=item.optJSONObject("reviews");String key=n==0?(item.optBoolean("movie")?"IMDb reviews":"IMDb episode"):selected;return reviews!=null&&!reviews.optString(key).isEmpty()?reviews.optString(key):url;
    }
    static String topDiscussion(String url){String link=RedditDiscussion.reddit(url);return link==null?null:link+"?sort=top";}
    void updateModes(){paste.setVisibility(selected.equals("Reddit")?View.VISIBLE:View.GONE);}
    void navigate(String url){if(!url.startsWith("https://"))return;web.stopLoading();target=url;updateModes();status.setText("Loading reviews…");web.loadUrl(url);}
    void chooseLink(){EditText input=new EditText(this);input.setSingleLine(true);input.setHint("https://www.reddit.com/r/…/comments/…");input.setInputType(android.text.InputType.TYPE_CLASS_TEXT|android.text.InputType.TYPE_TEXT_VARIATION_URI);String current=RedditDiscussion.reddit(target);if(current!=null)input.setText(current);
        AlertDialog dialog=new AlertDialog.Builder(this).setTitle("Choose a Reddit discussion").setMessage("Paste a post or comment link. Noir remembers it for this episode or movie.").setView(input).setNegativeButton("Cancel",null).setPositiveButton("Show discussion",null).create();dialog.setOnShowListener(d->dialog.getButton(AlertDialog.BUTTON_POSITIVE).setOnClickListener(v->{String link=RedditDiscussion.reddit(input.getText().toString().trim());if(link==null){input.setError("Use a full Reddit post or comment link.");return;}target=topDiscussion(link);links.edit().putString(item.optString("id"),target).apply();dialog.dismiss();navigate(target);}));dialog.show();}
    @Override public void onBackPressed(){if(web.canGoBack())web.goBack();else super.onBackPressed();}
    @Override protected void onPause(){CookieManager.getInstance().flush();if(web!=null&&!destroyed){web.onPause();web.pauseTimers();}super.onPause();}
    @Override protected void onResume(){super.onResume();if(web!=null&&!destroyed){web.onResume();web.resumeTimers();}}
    void releaseBrowser(){if(destroyed)return;destroyed=true;if(web!=null){web.stopLoading();web.resumeTimers();android.view.ViewParent parent=web.getParent();if(parent instanceof android.view.ViewGroup)((android.view.ViewGroup)parent).removeView(web);web.destroy();}}
    @Override public void finish(){releaseBrowser();super.finish();}
    @Override protected void onDestroy(){releaseBrowser();super.onDestroy();}
}

