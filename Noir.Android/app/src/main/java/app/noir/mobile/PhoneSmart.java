package app.noir.mobile;
import android.app.*;
import android.widget.*;
import org.json.*;
import java.time.*;
import java.util.*;

// The phone tracker's judgement, matching the PC: checking off S2E8 asks about earlier episodes,
// bulk marks ask when they were watched, un-checking asks about later ones, a finished season or
// show offers a rating, and dates never fall before an episode aired.
final class PhoneSmart {
 PhoneRewards.Stats rewardsBefore;
 final MainActivity a;
 PhoneSmart(MainActivity activity){a=activity;}
 static final String[] WHEN={"Just now","When they aired","Pick a date","Around a year","Over a period","Don't remember"};

 boolean counts(JSONObject i){if(i.optBoolean("movie")||i.optInt("episode")<=0)return false;if(i.optInt("season")>0)return true;JSONArray specials=a.browsingSettings().optJSONArray("includeSpecials");if(specials!=null)for(int n=0;n<specials.length();n++)if(specials.optString(n).equals(i.optString("group")))return true;return false;}
 static boolean before(JSONObject x,JSONObject y){return x.optInt("season")<y.optInt("season")||x.optInt("season")==y.optInt("season")&&x.optInt("episode")<y.optInt("episode");}
 // One pass over the items for this show only; the full library is never copied per tap.
 List<JSONObject> group(JSONObject item){String key=item.optString("group");List<JSONObject> g=new ArrayList<>();for(JSONObject i:DataStore.objects(a.data.itemsShared()))if(i.optString("group").equals(key))g.add(i);return g;}
 List<JSONObject> sorted(List<JSONObject> list){List<JSONObject> r=new ArrayList<>(list);r.sort(Comparator.comparingInt((JSONObject i)->i.optInt("season")).thenComparingInt(i->i.optInt("episode")));return r;}
 List<JSONObject> released(JSONObject item){if(a.tracker.airMap.isEmpty())a.tracker.prepare();long now=System.currentTimeMillis();List<JSONObject> r=new ArrayList<>();for(JSONObject i:group(item))if(counts(i)&&a.tracker.aired(i,now))r.add(i);return sorted(r);}
 List<JSONObject> earlier(JSONObject item){List<JSONObject> r=new ArrayList<>();if(item.optBoolean("movie")||item.optInt("season")<=0)return r;for(JSONObject i:released(item))if(i.optInt("season")>0&&before(i,item)&&!DataStore.watched(i))r.add(i);return r;}
 List<JSONObject> later(JSONObject item){List<JSONObject> r=new ArrayList<>();for(JSONObject i:sorted(group(item)))if(counts(i)&&before(item,i)&&DataStore.watched(i))r.add(i);return r;}
 static String label(JSONObject i){return i.optBoolean("movie")?i.optString("title"):String.format(Locale.ROOT,"S%02dE%02d",i.optInt("season"),i.optInt("episode"));}
 static String range(List<JSONObject> l){return l.size()==1?label(l.get(0)):label(l.get(0))+"–"+label(l.get(l.size()-1));}

 // Marks a batch with the chosen "when"; returns the viewings so one Undo removes them all.
 List<String[]> log(List<JSONObject> items,String when,long pickedDay){
  if(rewardsBefore==null)rewardsBefore=PhoneRewardCard.stats(a.data);long now=System.currentTimeMillis();List<String[]> done=new ArrayList<>();List<JSONObject> list=sorted(items);
  long cursor=when.equals("Pick a date")||when.equals("Around a year")?LocalDate.ofEpochDay(pickedDay).atTime(22,0).atZone(ZoneId.systemDefault()).toInstant().toEpochMilli():now;cursor=Math.min(cursor,now);
  // "Over a period:2018-2020" spreads the batch evenly across those years, in watching order.
  long spanStart=0,spanEnd=0;if(when.startsWith("Over a period:")){String[] years=when.substring(14).split("-");spanStart=LocalDate.of(Integer.parseInt(years[0]),1,1).atTime(21,0).atZone(ZoneId.systemDefault()).toInstant().toEpochMilli();spanEnd=Math.min(now,LocalDate.of(Integer.parseInt(years[1]),12,31).atTime(21,0).atZone(ZoneId.systemDefault()).toInstant().toEpochMilli());}
  long[] at=new long[list.size()];boolean[] unknown=new boolean[list.size()],approx=new boolean[list.size()];
  for(int n=list.size()-1;n>=0;n--){JSONObject i=list.get(n);long aired=a.tracker.airs(i);
   if(spanEnd>0){at[n]=spanStart+(long)((spanEnd-spanStart)*((n+.5)/list.size()));approx[n]=true;continue;}
   if(when.equals("Around a year"))approx[n]=true;
   switch(when){case "When they aired":if(aired>0&&aired<=now)at[n]=aired;else unknown[n]=true;approx[n]=true;break;case "Don't remember":unknown[n]=true;break;default:at[n]=Math.max(cursor,aired>0&&aired<=now?aired:0);}
   cursor-=(long)(a.ui.runtime(i)*1000);}
  for(int n=0;n<list.size();n++){String id=a.data.item(list.get(n).optString("id"))!=null?list.get(n).optString("id"):a.data.rememberPhoneItem(list.get(n)).optString("id");a.data.approximate=approx[n];done.add(new String[]{id,a.data.mark(id,true,unknown[n]?0:at[n],unknown[n])});}
  a.data.syncSoon();return done;
 }
 void finish(List<String[]> done,String message,JSONObject focus){a.undoable("✓ "+message,()->{for(String[] d:done)a.data.unview(d[0],d[1]);a.data.syncSoon();});if(rewardsBefore!=null){PhoneRewardCard.celebrate(a,a.data,rewardsBefore);rewardsBefore=null;}if(!a.tracker.refreshAfterLog(focus))a.renderKeep();if(focus!=null){updateCompletion(focus);Motion.after(a.body,250,()->PhoneTracker.reactionRow(a,focus));}}
 void updateCompletion(JSONObject focus){if(focus.optBoolean("movie"))return;List<JSONObject> released=released(focus);boolean all=!released.isEmpty(),season=true;for(JSONObject i:released){boolean watched=DataStore.watched(i);if(!watched)all=false;if(i.optInt("season")==focus.optInt("season")&&!watched)season=false;}if(season)a.toast(Loc.t("Season complete"));JSONObject states=a.browsingSettings().optJSONObject("showStates");if(all&&states!=null&&"Ended".equals(states.optString(focus.optString("group")))){JSONObject op=a.data.operation(focus.optString("id"),"status");DataStore.put(op,"status","Finished");a.data.queue(op);a.data.syncSoon();}}

 void askWhen(List<JSONObject> items,String question,java.util.function.BiConsumer<String,Long> chosen){
  new AlertDialog.Builder(a).setTitle(question).setItems(WHEN,(d,n)->{
   if(n==3){year("Around which year?",1950,y->chosen.accept("Around a year",LocalDate.of(y,7,1).toEpochDay()));return;}
   if(n==4){year("From which year?",1950,from->year("Until which year?",from,to->chosen.accept("Over a period:"+from+"-"+to,0L)));return;}
   if(n==2){LocalDate today=LocalDate.now();DatePickerDialog p=new DatePickerDialog(a,(v,y,m,day)->chosen.accept("Pick a date",LocalDate.of(y,m+1,day).toEpochDay()),today.getYear(),today.getMonthValue()-1,today.getDayOfMonth());p.getDatePicker().setMaxDate(System.currentTimeMillis());p.show();}else chosen.accept(WHEN[n],0L);}).setNegativeButton("Cancel",(d,w)->a.renderKeep()).setOnCancelListener(d->a.renderKeep()).show();
 }
 void year(String title,int earliest,java.util.function.IntConsumer chosen){int now=LocalDate.now().getYear();String[] years=new String[now-Math.max(1950,earliest)+1];for(int n=0;n<years.length;n++)years[n]=String.valueOf(now-n);new AlertDialog.Builder(a).setTitle(title).setItems(years,(d,n)->chosen.accept(now-n)).show();}
 // One episode tapped: ask about unwatched earlier episodes, like TV Time.
 void checkOff(JSONObject item,String message){
  List<JSONObject> before=earlier(item);
  if(before.isEmpty()){finish(log(List.of(item),"Just now",0),message,item);return;}
  new AlertDialog.Builder(a).setTitle("Did you also watch the "+before.size()+" earlier episode"+(before.size()==1?"":"s")+"?").setMessage(range(before)+" "+(before.size()==1?"isn't":"aren't")+" checked off yet.")
   .setNegativeButton("Just "+label(item),(d,w)->finish(log(List.of(item),"Just now",0),message,item))
   .setPositiveButton("Yes, all "+(before.size()+1),(d,w)->askWhen(before,"When did you watch the earlier ones?",(when,day)->{List<JSONObject> all=new ArrayList<>(before);all.add(item);List<String[]> done;if(when.equals("Just now"))done=log(all,when,0);else{done=log(before,when,day);done.addAll(log(List.of(item),"Just now",0));}finish(done,item.optString("title")+": "+(before.size()+1)+" episodes up to "+label(item)+" watched",item);}))
   .setOnCancelListener(d->a.renderKeep()).show();
 }
 void bulk(List<JSONObject> items,String message){List<JSONObject> todo=new ArrayList<>();for(JSONObject i:items)if(!DataStore.watched(i))todo.add(i);if(todo.isEmpty())return;todo=sorted(todo);final List<JSONObject> list=todo;
  askWhen(list,"When did you watch "+(list.size()==1?"it":"these "+list.size())+"?",(when,day)->finish(log(list,when,day),message,list.get(list.size()-1)));}
 void wholeShow(JSONObject show){bulk(released(show),show.optString("title")+": whole show marked watched");}
 // An already-watched item: log a rewatch, or remove it (and offer to unmark later episodes).
 void watchedClick(JSONObject item){
  new AlertDialog.Builder(a).setTitle(item.optString("title")+(item.optBoolean("movie")?"":" "+label(item))).setMessage("Already in your history.")
   .setPositiveButton("↻ Log a rewatch",(d,w)->{String id=item.optString("id");PhoneRewards.Stats before=PhoneRewardCard.stats(a.data);String v=a.data.rewatch(id);a.data.syncSoon();PhoneRewardCard.celebrate(a,a.data,before);a.undoable("✓ Rewatch logged",()->{a.data.unview(id,v);a.data.syncSoon();});a.renderKeep();})
   .setNegativeButton("Remove from history",(d,w)->uncheck(item)).setNeutralButton("Cancel",null).show();
 }
 void uncheck(JSONObject item){
  List<JSONObject> after=later(item);
  if(after.isEmpty()){a.data.unview(item.optString("id"),"");a.data.syncSoon();a.toast("Unmarked "+label(item));a.renderKeep();return;}
  new AlertDialog.Builder(a).setTitle("Also unmark the "+after.size()+" later episode"+(after.size()==1?"":"s")+"?").setMessage(range(after)+" "+(after.size()==1?"is":"are")+" still checked off.")
   .setNegativeButton("Just "+label(item),(d,w)->{a.data.unview(item.optString("id"),"");a.data.syncSoon();a.renderKeep();})
   .setPositiveButton("Unmark all "+(after.size()+1),(d,w)->{a.data.unview(item.optString("id"),"");for(JSONObject i:after)a.data.unview(i.optString("id"),"");a.data.syncSoon();a.renderKeep();}).show();
 }
 // A just-finished season or show: an ended show becomes Finished, and a rating is offered.
 void rateIfDone(JSONObject focus){
  if(focus.optBoolean("movie")||!a.active)return;List<JSONObject> all=new ArrayList<>(),season=new ArrayList<>();
  for(JSONObject i:released(focus)){JSONObject fresh=a.data.item(i.optString("id"));if(fresh==null)fresh=i;all.add(fresh);if(i.optInt("season")==focus.optInt("season"))season.add(fresh);}
  boolean show=!all.isEmpty()&&all.stream().allMatch(DataStore::watched),seasonDone=!season.isEmpty()&&season.stream().allMatch(DataStore::watched);if(!show&&!seasonDone)return;
  String group=focus.optString("group");JSONObject states=a.browsingSettings().optJSONObject("showStates");boolean ended=states!=null&&"Ended".equals(states.optString(group));
  if(show&&ended&&!"Finished".equals(focus.optString("status"))){JSONObject op=a.data.operation(focus.optString("id"),"status");DataStore.put(op,"status","Finished");a.data.queue(op);}
  a.toast("Season complete");
  String suffix=show?"":"S"+focus.optInt("season"),key=show?group:group+":"+suffix;RatingBar stars=new RatingBar(a,null,android.R.attr.ratingBarStyle);stars.setNumStars(5);stars.setStepSize(.5f);stars.setRating(show?a.data.rating10(focus)/2f:a.data.seasonRating(group,focus.optInt("season"))/2f);
  LinearLayout box=a.column();box.setPadding(a.dp(20),a.dp(8),a.dp(20),0);box.addView(stars,new LinearLayout.LayoutParams(-2,-2));
  new AlertDialog.Builder(a).setTitle(show?(ended?"Finished "+focus.optString("title")+"!":"All caught up on "+focus.optString("title")):"Season "+focus.optInt("season")+" done").setMessage("How was it?").setView(box)
   .setNegativeButton("Not now",null).setPositiveButton("Save",(d,w)->{if(show)a.data.review10(focus.optString("id"),Math.round(stars.getRating()*2),"");else a.data.seasonRating(group,focus.optInt("season"),Math.round(stars.getRating()*2));a.data.syncSoon();a.renderKeep();}).show();
 }
 int rating(String key){JSONObject pc=a.browsingSettings().optJSONObject("showRatings"),times=a.browsingSettings().optJSONObject("showRatingTimes"),r=a.data.prefs().optJSONObject("showRatings"),local=r==null?null:r.optJSONObject(key);
  if(local!=null&&(times==null||local.optLong("at")>times.optLong(key)))return local.optInt("value");return pc==null?0:pc.optInt(key);}
 // The PC builds the full key from its own group, so phone-only shows rate correctly too.
 void rate(JSONObject item,String key,String suffix,int value){JSONObject op=a.data.operation(item.optString("id"),"showRating");DataStore.put(op,"name",suffix);DataStore.put(op,"rating",value);a.data.queue(op);JSONObject r=a.data.prefs().optJSONObject("showRatings");if(r==null)r=new JSONObject();JSONObject local=new JSONObject();DataStore.put(local,"value",value);DataStore.put(local,"at",op.optLong("at"));DataStore.put(r,key,local);a.data.pref("showRatings",r);a.data.syncSoon();}
}
