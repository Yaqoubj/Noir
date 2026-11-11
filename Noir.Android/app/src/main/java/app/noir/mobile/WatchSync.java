package app.noir.mobile;
import android.content.Context;
import org.json.*;
import java.util.*;

/** The phone's own watch-sync queue (P1), the PC's control scripts ported byte for byte (P2, checked against the shared golden file) and provider backoff (P5).
 Stored in the profile's library.json under "watchSync": jobs keyed provider:itemId like the PC's WatchSync, provider backoff, Serializd season links. */
final class WatchSync {
 static final String[] PROVIDERS={"Trakt","Serializd"};
 /* Site-level failures wait 5 min, 30 min, 2 h, 12 h, then daily; a success or Retry resets. */
 static final long[] BACKOFF={5*60000L,30*60000L,2*3600000L,12*3600000L,24*3600000L};
 static final long DAY=24*3600000L;
 interface Clock{long now();void sleep(long ms)throws InterruptedException;}
 static final Clock SYSTEM=new Clock(){public long now(){return System.currentTimeMillis();}public void sleep(long ms)throws InterruptedException{Thread.sleep(ms);}};
 /* Test seams: the clock (backoff and waits) and TV detection. */
 static volatile Clock clock=SYSTEM;
 static volatile java.util.function.Predicate<Context> tv=TvUi::isTv;

 static String key(JSONObject job){return job.optString("provider")+":"+job.optString("itemId");}
 /* The live "watchSync" object: read and change it only while holding the DataStore lock, then save. Copying the whole queue per step was too slow for thousands of jobs. */
 static JSONObject state(DataStore d){return d.live("watchSync");}
 static JSONObject section(JSONObject s,String key){JSONObject o=s.optJSONObject(key);if(o==null){o=new JSONObject();DataStore.put(s,key,o);}return o;}
 /* The switches are the PC's settings (synced in the library as syncTrakt / syncSerializd); a phone switch overrides them. */
 static boolean enabled(DataStore d,String p){return d.playbackFlag("sync"+p,false);}
 static void enable(DataStore d,String p,boolean on){d.phoneSetting("sync"+p,on);if(on)retry(d,p);}
 static String name(JSONObject i){String r=i.optString("reviewTitle");return r.isEmpty()?i.optString("title"):r;}
 static String titleKey(JSONObject i){return i.optString("imdb")+"|"+i.optBoolean("movie")+"|"+i.optInt("season")+"|"+i.optInt("episode")+"|"+name(i)+"|"+i.optInt("year");}
 /* P4: the time a site confirmed any viewing of this item, as the other device reported it ("" = unknown). */
 static String syncedAt(JSONObject item,String p){if(item==null)return "";for(JSONObject v:DataStore.objects(item.optJSONArray("viewings"))){JSONObject s=v.optJSONObject("synced");if(s!=null&&!s.optString(p).isEmpty())return s.optString(p);}return "";}
 static long parse(String iso){try{return java.time.Instant.parse(iso).toEpochMilli();}catch(Exception e){try{return java.time.OffsetDateTime.parse(iso).toInstant().toEpochMilli();}catch(Exception x){return 0;}}}

 /* Called by DataStore.queue for every operation: a finished playback or tracker check-off queues one job per enabled site; an unchecked mark cancels waiting jobs. Never on TV. */
 static boolean recorded(DataStore d,JSONObject op){String kind=op.optString("kind");
  if(!kind.equals("mark")&&!kind.equals("progress"))return false;
  if(kind.equals("mark")&&!op.optBoolean("completed")){cancelPending(d,op.optString("itemId"));return false;}
  if(!op.optBoolean("completed")||op.optBoolean("dateUnknown")||!op.optString("source").isEmpty()||tv.test(d.context))return false;
  JSONObject item=d.item(op.optString("itemId"));if(item==null)return false;
  long at=kind.equals("mark")&&op.optLong("watchedAt")>0?op.optLong("watchedAt"):op.optLong("at");
  return enqueue(d,item,op.optString("viewingId"),at);}
 static boolean enqueue(DataStore d,JSONObject item,String viewingId,long watchedAt){synchronized(d){
  if(item.optString("id").isEmpty())return false;JSONObject s=state(d),jobs=section(s,"jobs");boolean added=false;
  for(String p:PROVIDERS){if(!enabled(d,p)||p.equals("Serializd")&&item.optBoolean("movie"))continue;String key=p+":"+item.optString("id");if(jobs.has(key)||!syncedAt(item,p).isEmpty())continue;
   JSONObject j=new JSONObject();DataStore.put(j,"provider",p);DataStore.put(j,"itemId",item.optString("id"));DataStore.put(j,"viewingId",viewingId);DataStore.put(j,"watchedAt",watchedAt);DataStore.put(j,"retryAt",0);DataStore.put(j,"attempts",0);DataStore.put(j,"message","Waiting to sync");DataStore.put(jobs,key,j);added=true;}
  if(added)d.liveChanged();return added;}}
 static void cancelPending(DataStore d,String itemId){synchronized(d){JSONObject s=state(d),jobs=section(s,"jobs");boolean changed=false;for(Iterator<String> k=jobs.keys();k.hasNext();){JSONObject j=jobs.optJSONObject(k.next());if(j!=null&&j.optString("itemId").equals(itemId)&&!j.has("syncedAt")){k.remove();changed=true;}}if(changed)d.liveChanged();}}
 static List<JSONObject> jobs(DataStore d){synchronized(d){List<JSONObject> list=new ArrayList<>();JSONObject jobs=state(d).optJSONObject("jobs");if(jobs!=null)for(Iterator<String> k=jobs.keys();k.hasNext();){JSONObject j=jobs.optJSONObject(k.next());if(j!=null)list.add(DataStore.copy(j));}return list;}}
 static List<JSONObject> live(DataStore d){List<JSONObject> list=new ArrayList<>();JSONObject jobs=state(d).optJSONObject("jobs");if(jobs!=null)for(Iterator<String> k=jobs.keys();k.hasNext();){JSONObject j=jobs.optJSONObject(k.next());if(j!=null)list.add(j);}return list;}
 static JSONObject job(DataStore d,String key){synchronized(d){JSONObject jobs=state(d).optJSONObject("jobs"),j=jobs==null?null:jobs.optJSONObject(key);return j==null?null:DataStore.copy(j);}}
 static JSONObject provider(DataStore d,String p){synchronized(d){JSONObject pr=state(d).optJSONObject("providers");JSONObject o=pr==null?null:pr.optJSONObject(p);return o==null?new JSONObject():DataStore.copy(o);}}
 interface Edit{void apply(JSONObject value);}
 static void editJob(DataStore d,String key,Edit e){synchronized(d){JSONObject s=state(d),j=section(s,"jobs").optJSONObject(key);if(j==null)return;e.apply(j);d.liveChanged();}}
 static void editProvider(DataStore d,String p,Edit e){synchronized(d){JSONObject s=state(d);e.apply(section(section(s,"providers"),p));d.liveChanged();}}

 /* Serializd stops after the backoff has reached daily: the site does not take the phone's session; it keeps syncing through the PC. Retry or Sign in starts again. */
 static boolean refused(DataStore d,String p){return p.equals("Serializd")&&provider(d,p).optInt("failures")>=BACKOFF.length;}
 static boolean waiting(DataStore d,String p,long now){return provider(d,p).optLong("retryAt")>now||refused(d,p);}
 /* A job whose title changed since its per-title failure is tried again. */
 static boolean held(JSONObject job,JSONObject item){return job.optBoolean("held")&&(item==null||job.optString("titleKey").equals(titleKey(item)));}
 static List<JSONObject> due(DataStore d,long now){List<JSONObject> list=new ArrayList<>();
  synchronized(d){Set<String> open=new HashSet<>();for(String p:PROVIDERS)if(enabled(d,p)&&!waiting(d,p,now))open.add(p);
  for(JSONObject j:live(d)){if(j.has("syncedAt")||!open.contains(j.optString("provider"))||j.optLong("retryAt")>now)continue;if(held(j,d.item(j.optString("itemId"))))continue;list.add(DataStore.copy(j));}}
  /* S1: newest first, so today's episode never waits behind years of backlog. */
  Collections.sort(list,(a,b)->Long.compare(b.optLong("watchedAt"),a.optLong("watchedAt")));return list;}
 /* S2: each provider takes at most 30 marks an hour and 200 a day on this phone (rolling, persisted); a read that finds "already watched" counts too. The rest waits. */
 static final int PER_HOUR=30,PER_DAY=200;static final long SPACING=3000;
 static int sent(DataStore d,String p,long now,long window){int n=0;JSONArray marks=provider(d,p).optJSONArray("marks");if(marks!=null)for(int i=0;i<marks.length();i++){long at=marks.optLong(i);if(at>now-window&&at<=now)n++;}return n;}
 static boolean paced(DataStore d,String p,long now){return sent(d,p,now,3600000L)>=PER_HOUR||sent(d,p,now,DAY)>=PER_DAY;}
 static void counted(DataStore d,String p){long now=clock.now();editProvider(d,p,pr->{JSONArray old=pr.optJSONArray("marks"),kept=new JSONArray();if(old!=null)for(int i=0;i<old.length();i++)if(old.optLong(i)>now-DAY)kept.put(old.optLong(i));kept.put(now);DataStore.put(pr,"marks",kept);});}
 static boolean hasDue(DataStore d,long now){return !due(d,now).isEmpty();}
 static boolean current(DataStore d,JSONObject job){if(!enabled(d,job.optString("provider"))||tv.test(d.context))return false;JSONObject now=job(d,key(job));if(now==null||now.has("syncedAt")||!now.optString("viewingId").equals(job.optString("viewingId")))return false;JSONObject item=d.item(job.optString("itemId"));return item!=null&&DataStore.watched(item);}

 /* P4: jobs the other device already confirmed are done without loading a page. */
 static int absorb(DataStore d){synchronized(d){int n=0;for(JSONObject j:live(d)){if(j.has("syncedAt"))continue;String at=syncedAt(d.item(j.optString("itemId")),j.optString("provider"));if(at.isEmpty())continue;long when=parse(at);DataStore.put(j,"syncedAt",when>0?when:clock.now());DataStore.put(j,"message","Watched ✓");DataStore.put(j,"by","PC");n++;}if(n>0)d.liveChanged();return n;}}
 static void started(DataStore d,String key){editJob(d,key,j->{DataStore.put(j,"attempts",j.optInt("attempts")+1);DataStore.put(j,"message","Syncing…");});}
 static void interrupted(DataStore d,String key){editJob(d,key,j->DataStore.put(j,"message","Waiting to sync"));}
 static void done(DataStore d,String key){long now=clock.now();String[] sent=new String[3];editJob(d,key,j->{DataStore.put(j,"syncedAt",now);DataStore.put(j,"message","Watched ✓");j.remove("held");sent[0]=j.optString("provider");sent[1]=j.optString("itemId");sent[2]=j.optString("viewingId");});if(sent[0]!=null)tellPc(d,sent[0],sent[1],sent[2],now);}
 /* Per-title failure: waits for Retry or a title change; the provider does not back off. */
 static void titleFailed(DataStore d,String key,JSONObject item,String message){editJob(d,key,j->{DataStore.put(j,"held",true);DataStore.put(j,"titleKey",item==null?"":titleKey(item));DataStore.put(j,"message",message);});}
 /* Site-level failure: the provider waits 5 min, 30 min, 2 h, 12 h, then daily. */
 static long siteFailed(DataStore d,String p,String key,String message){long now=clock.now();long[] at=new long[1];editProvider(d,p,pr->{int n=pr.optInt("failures")+1;DataStore.put(pr,"failures",n);at[0]=now+BACKOFF[Math.min(n,BACKOFF.length)-1];DataStore.put(pr,"retryAt",at[0]);if(!pr.has("failingSince"))DataStore.put(pr,"failingSince",now);DataStore.put(pr,"message",message);});if(key!=null)editJob(d,key,j->DataStore.put(j,"message",message));return at[0];}
 static void siteWorked(DataStore d,String p){long now=clock.now();editProvider(d,p,pr->{DataStore.put(pr,"failures",0);DataStore.put(pr,"retryAt",0);pr.remove("failingSince");pr.remove("message");DataStore.put(pr,"lastSuccess",now);});}
 /* Retry (or a new sign-in) resets the provider's backoff and every waiting job; p null = both. */
 static void retry(DataStore d,String p){synchronized(d){JSONObject s=state(d);for(String name:PROVIDERS){if(p!=null&&!p.equals(name))continue;JSONObject pr=section(section(s,"providers"),name);DataStore.put(pr,"failures",0);DataStore.put(pr,"retryAt",0);pr.remove("failingSince");pr.remove("message");}
  JSONObject jobs=section(s,"jobs");for(Iterator<String> k=jobs.keys();k.hasNext();){JSONObject j=jobs.optJSONObject(k.next());if(j==null||j.has("syncedAt")||p!=null&&!p.equals(j.optString("provider")))continue;j.remove("held");DataStore.put(j,"retryAt",0);DataStore.put(j,"message","Waiting to sync");}d.liveChanged();}}
 /* P4, phone to PC: a "synced" operation carries the confirmation; mark/progress ops still waiting to be sent carry it too. */
 static void tellPc(DataStore d,String p,String itemId,String viewingId,long at){if(viewingId.isEmpty())return;JSONObject synced=new JSONObject();DataStore.put(synced,p,java.time.Instant.ofEpochMilli(at).toString());JSONObject op=d.operation(itemId,"synced");DataStore.put(op,"viewingId",viewingId);DataStore.put(op,"synced",synced);d.attachSynced(viewingId,p,synced.optString(p));d.queue(op);}

 static int count(DataStore d,String p,boolean synced){synchronized(d){int n=0;for(JSONObject j:live(d))if(j.optString("provider").equals(p)&&j.has("syncedAt")==synced)n++;return n;}}
 /* F13: the site has failed for a day. */
 static boolean failingForADay(DataStore d,String p,long now){long since=provider(d,p).optLong("failingSince");return since>0&&now-since>=DAY;}

 static String serializdSeasonKey(JSONObject item){return item.optString("group")+":season:"+item.optInt("season");}
 static String serializdEpisode(DataStore d,JSONObject item){if(item.optBoolean("movie")||item.optInt("episode")<1)return "";String season;synchronized(d){JSONObject seasons=state(d).optJSONObject("serializdSeasons");season=seasons==null?"":seasons.optString(serializdSeasonKey(item));}return season.matches("^https://www\\.serializd\\.com/show/[^/?#]+/season/\\d+/"+item.optInt("season")+"$")?season+"/episode/"+item.optInt("episode"):"";}
 static void serializdSeason(DataStore d,JSONObject item,String url){synchronized(d){JSONObject s=state(d);DataStore.put(section(s,"serializdSeasons"),serializdSeasonKey(item),url);d.liveChanged();}}

 // ---- The PC's scripts (CommunityPages.TraktControl / TraktDateControl / SerializdControl), byte for byte. ----
 /* System.Text.Json's default string encoding: HTML-sensitive and non-ASCII characters as uppercase \\uXXXX. */
 static String json(String s){StringBuilder b=new StringBuilder("\"");for(int n=0;n<s.length();n++){char c=s.charAt(n);switch(c){case '\\':b.append("\\\\");break;case '\b':b.append("\\b");break;case '\t':b.append("\\t");break;case '\n':b.append("\\n");break;case '\f':b.append("\\f");break;case '\r':b.append("\\r");break;default:if(c<0x20||c>0x7E||"\"&'+<>`".indexOf(c)>=0)b.append(String.format(Locale.ROOT,"\\u%04X",(int)c));else b.append(c);}}return b.append('"').toString();}
 static String serializdControl(boolean click){return "(()=>{const label=b=>[...b.querySelectorAll('span')].map(s=>s.textContent.trim().toLowerCase()).find(t=>t==='watched'||t==='mark watched')||b.textContent.replace(/[^\\p{L}\\p{N}\\s]/gu,'').trim().toLowerCase();const a=[...document.querySelectorAll('.common-show-interact-button')].filter(b=>b.getClientRects().length&&['watched','mark watched'].includes(label(b)));if(a.length!==1)return [...document.querySelectorAll('a[href]')].some(a=>a.getClientRects().length&&new URL(a.href).host===location.host&&new URL(a.href).pathname==='/login')?'signed-out':'waiting';if(label(a[0])==='watched')return 'watched';"
  +(click?"a[0].click();return 'clicked';":"return 'ready';")+"})()";}
 static final String TRAKT_DATE_CONTROL="(()=>{const b=[...document.querySelectorAll('.trakt-drawer .trakt-dropdown-group button,.trakt-drawer .trakt-dropdown-group [role=button]')].filter(b=>b.getClientRects().length&&!b.disabled&&b.getAttribute('disabled')!=='true'&&b.getAttribute('aria-disabled')!=='true'&&[b.getAttribute('aria-label'),b.getAttribute('label'),b.textContent?.trim()].includes('Just now'));if(b.length!==1)return false;b[0].click();return true;})()";
 static String traktControl(JSONObject item,boolean click){boolean movie=item.optBoolean("movie");int season=item.optInt("season"),episode=item.optInt("episode");String id=json(item.optString("imdb")),marker=json("S"+season+" \u2022 E"+episode),movieTitle=json(name(item));
  return "(()=>{const u=new URL(location.href);if(u.host!=='app.trakt.tv')return 'mismatch';const visible=b=>b.getClientRects().length&&!b.disabled;let root=document;"
   +(movie?"if(u.pathname!=='/movies/'+"+id+")return 'mismatch';"
    :"if(u.pathname!=='/shows/'+"+id+"||u.searchParams.get('view')!=='episode'||Number(u.searchParams.get('season'))!=="+season+"||Number(u.searchParams.get('episode'))!=="+episode+")return 'mismatch';const d=[...document.querySelectorAll('.trakt-drawer')].filter(d=>d.innerText.includes("+marker+"));if(d.length!==1)return 'waiting';root=d[0];")
   +"const buttons=[...root.querySelectorAll('button[aria-label]')].filter(visible)"
   +(movie?".filter(b=>[\"Mark \\\"\"+"+movieTitle+"+\"\\\" as watched\",\"Remove \\\"\"+"+movieTitle+"+\"\\\" from your History\"].includes(b.getAttribute('aria-label')))":"")
   +";const watched=buttons.filter(b=>/^Remove \".+\" from your History$/.test(b.getAttribute('aria-label')));if(watched.length===1)return 'watched';const marks=buttons.filter(b=>/^Mark \".+\" as watched$/.test(b.getAttribute('aria-label')));if(marks.length!==1)return 'waiting';"
   +(click?"marks[0].click();return 'clicked';":"return 'ready';")+"})()";}
 /* CommunityPages: Serializd search results, show details and episode details read from #__NEXT_DATA__, and the Cloudflare check. */
 static final String SERIALIZD_RESULTS="[...document.querySelectorAll('a[href]')].filter(a=>/^https:\\/\\/www.serializd.com\\/show\\/[^/?#]+$/.test(a.href)).map(a=>({url:a.href,name:(a.querySelector('img')?.alt?.replace(/^Poster for /,'')||a.innerText.split('\\n')[0]).replace(/\\s*\\(\\d{4}\\)\\s*$/,'')}))";
 static final String SERIALIZD_SHOW="(()=>{try {const d=JSON.parse(document.querySelector('#__NEXT_DATA__').textContent).props.pageProps.data.showDetails;return {name:d.name,date:d.premiereDate,seasons:d.seasons?.map(s=>({id:s.seasonId||s.id,number:s.seasonNumber}))};}catch{return null;}})()";
 static final String SERIALIZD_EPISODE="(()=>{try{const p=JSON.parse(document.querySelector('#__NEXT_DATA__').textContent).props.pageProps;const d=p.data;return {show:p.showId,season:Number(p.seasonNumber),episode:Number(p.episodeNumber),runtime:d.runtime,name:d.showDetails.name};}catch{return null;}})()";
 static final String CHALLENGE="document.title.startsWith('Just a moment')||location.pathname.startsWith('/cdn-cgi/')||!!document.querySelector('#challenge-form,#challenge-running,#cf-challenge-running,[name=\"cf-turnstile-response\"],script[src*=\"challenges.cloudflare.com\"]')";
 static String normalize(String name){return name.toLowerCase(Locale.ROOT).replaceAll("[^\\p{L}\\p{N}]","");}
 static String traktUrl(JSONObject item){String imdb=item.optString("imdb");if(!imdb.matches("tt\\d+"))return "https://app.trakt.tv/search?q="+android.net.Uri.encode(name(item));return item.optBoolean("movie")?"https://app.trakt.tv/movies/"+imdb:"https://app.trakt.tv/shows/"+imdb+"/seasons/"+item.optInt("season")+"/episodes/"+item.optInt("episode");}
 /* Trakt Web keeps its own sign-in: its home page has the Login button (trakt.tv/auth/signin now redirects to a 404 on app.trakt.tv, checked live 2026-11-11) */
 static final String TRAKT_SIGN_IN="https://app.trakt.tv/",SERIALIZD_SIGN_IN="https://www.serializd.com/login";
 static String signInUrl(String p){return p.equals("Serializd")?SERIALIZD_SIGN_IN:TRAKT_SIGN_IN;}
 static final String GOOGLE="Google sign-in cannot work inside the app (Google blocks it in app web views). Set a password on the site and sign in with that.";

 static {String[][] words={
  {"Waiting to sync","بانتظار المزامنة"},{"Syncing…","جارٍ المزامنة…"},{"Watched ✓","تمت المشاهدة ✓"},
  {"Specials need a confirmed Trakt match. Mark the correct special on its website; your Noir history remains saved.","الحلقات الخاصة تحتاج إلى مطابقة مؤكدة على Trakt. ضع العلامة على الحلقة الخاصة الصحيحة في موقعه؛ سجلك في نوار محفوظ."},
  {"Waiting for a verified title ID. Refresh information on your PC.","بانتظار معرّف عنوان موثّق. حدّث المعلومات على الكمبيوتر."},
  {"Serializd tracks TV episodes only.","يتتبع Serializd حلقات المسلسلات فقط."},
  {"No exact Serializd title match yet. Choose the correct episode in its review tab.","لا توجد مطابقة دقيقة للعنوان على Serializd بعد. اختر الحلقة الصحيحة في تبويب المراجعات."},
  {"More than one show matches. Open Serializd and select the correct episode first.","أكثر من مسلسل يطابق. افتح Serializd واختر الحلقة الصحيحة أولًا."},
  {"Serializd's title/year could not be verified. Select its exact episode in Reviews & comments.","تعذر التحقق من عنوان/سنة Serializd. اختر الحلقة بالضبط في المراجعات والتعليقات."},
  {"Serializd does not have this season yet.","لا يحتوي Serializd على هذا الموسم بعد."},
  {"Serializd's episode could not be verified.","تعذر التحقق من حلقة Serializd."},
  {"Episode match needs checking in Reviews & comments.","مطابقة الحلقة تحتاج إلى مراجعة في المراجعات والتعليقات."},
  {"The website could not load. Noir will retry later.","تعذر تحميل الموقع. سيعيد نوار المحاولة لاحقًا."},
  {"The website's security check wasn't completed. Sign in under More → Connections.","لم يكتمل الفحص الأمني للموقع. سجّل الدخول من المزيد ← الاتصالات."},
  {"Your website session has expired. Sign in once under More → Connections; Noir resumes syncing automatically.","انتهت جلستك على الموقع. سجّل الدخول مرة من المزيد ← الاتصالات؛ يستأنف نوار المزامنة تلقائيًا."},
  {"The website's watched controls are unavailable. Noir will retry automatically.","أزرار المشاهدة في الموقع غير متاحة. سيعيد نوار المحاولة تلقائيًا."},
  {"The watched control changed. Noir will retry later.","تغيّر زر المشاهدة. سيعيد نوار المحاولة لاحقًا."},
  {"Trakt's watched-date control is unavailable. Noir will retry automatically.","زر تاريخ المشاهدة في Trakt غير متاح. سيعيد نوار المحاولة تلقائيًا."},
  {"The website hasn't confirmed the watched mark. Noir will recheck later.","لم يؤكد الموقع علامة المشاهدة. سيعيد نوار التحقق لاحقًا."},
  {"Website unavailable. Noir will retry later.","الموقع غير متاح. سيعيد نوار المحاولة لاحقًا."},
  {"‹ Connections","‹ الاتصالات"},{"Sign in to %s","سجّل الدخول إلى %s"},{"If the site shows a security check, complete it yourself here.","إذا عرض الموقع فحصًا أمنيًا، فأكمله بنفسك هنا."},
  {GOOGLE,"تسجيل الدخول عبر Google لا يعمل داخل التطبيق (تحظره Google في متصفحات التطبيقات). عيّن كلمة مرور على الموقع وسجّل الدخول بها."}};
  for(String[] w:words)Loc.AR.put(w[0],w[1]);}
}
