package app.noir.mobile;
import org.json.*;
import org.junit.Test;
import java.util.*;
import static org.junit.Assert.*;

/** Selection and recovery policy without network, player or TV hardware. */
public final class TvStreamPolicyTest {
 static final double MOVIE=115*60;
 JSONObject option(String name,boolean cached,double mbps)throws Exception{return new JSONObject().put("name",name).put("url","https://example.test/"+Integer.toHexString(name.hashCode())).put("service","direct").put("cached",cached).put("size",(long)(mbps*MOVIE*1000000/8)).put("mbps",mbps);}
 TvStreamPick.Profile profile(){TvStreamPick.Profile p=new TvStreamPick.Profile();p.maxHeight=2160;p.hevc=true;p.hevc10=true;p.quality="1080p";p.mbps=100;return p;}
 List<JSONObject> rank(JSONObject... options){return TvStreamPick.rank(Arrays.asList(options),profile(),MOVIE);}
 boolean pick(List<JSONObject> options){return PhoneStream.shouldPick(true,false,options,profile(),MOVIE);}
 @Test public void allOverLimitCachedResultsRequireExplicitChoice()throws Exception{
  List<JSONObject> options=rank(option("Movie.2160p.HEVC",true,12),option("Other.4k.x264",true,10));assertEquals(2,options.size());assertTrue(pick(options));assertEquals("Above your quality limit",TvStreamPick.pickerReason(options,profile(),MOVIE));
 }
 @Test public void uncachedWithinLimitStaysAheadOfCachedOverLimit()throws Exception{
  JSONObject hd=option("Movie.1080p.x264",false,10),uhd=option("Movie.2160p.HEVC",true,12);List<JSONObject> options=rank(uhd,hd);assertSame(hd,options.get(0));assertTrue("uncached still requires the existing picker",pick(options));assertEquals("",TvStreamPick.pickerReason(options,profile(),MOVIE));assertEquals(0,PhoneStream.tvFallbacks(options,0,MOVIE,profile()).length());
 }
 @Test public void compatibleCachedWithinLimitAutoplaysUnlessChoosing()throws Exception{
  List<JSONObject> options=rank(option("Movie.1080p.x264",true,10));assertFalse(pick(options));assertTrue(PhoneStream.shouldPick(true,true,options,profile(),MOVIE));
 }
 @Test public void incompatibleUnknownAndEmptyResultsOpenPicker()throws Exception{
  for(List<JSONObject> options:Arrays.asList(rank(option("Movie.1080p.AV1",true,10)),rank(option("Mystery.1080p",true,10)),Collections.<JSONObject>emptyList())){assertTrue(pick(options));assertFalse(TvStreamPick.pickerReason(options,profile(),MOVIE).isEmpty());}
 }
 @Test public void manualOverLimitChoiceRemainsAvailableWithOnlyCompliantFallbacks()throws Exception{
  JSONObject uhd=option("Movie.2160p.HEVC",true,12),hd=option("Movie.1080p.x264",true,10);List<JSONObject> shown=rank(hd,uhd);assertSame(uhd,shown.get(1));JSONObject choice=PhoneStream.tvCandidate(shown.get(1),MOVIE);assertEquals(uhd.getString("url"),choice.getString("url"));assertTrue(TvStreamPick.limited(TvStreamPick.parse(choice,MOVIE),profile()));assertEquals(0,PhoneStream.tvFallbacks(shown,1,MOVIE,profile()).length());
  JSONArray remaining=PhoneStream.tvFallbacks(Arrays.asList(uhd,hd,option("Other.2160p.HEVC",true,8)),0,MOVIE,profile());assertEquals(1,remaining.length());assertEquals(hd.getString("url"),remaining.getJSONObject(0).getString("url"));
 }
 @Test public void fallbacksFilterBeforeCachedRegroupingAndKeepWithinLimitOrder()throws Exception{
  JSONObject start=option("Start.1080p.x264",true,20),cold=option("Cold.720p.x264",false,6),uhd=option("Cached.2160p.HEVC",true,8),ready=option("Ready.1080p.x264",true,12),cold2=option("Cold2.720p.x264",false,5);JSONArray all=PhoneStream.tvFallbacks(Arrays.asList(start,cold,uhd,ready,cold2),0,MOVIE,profile());assertEquals(3,all.length());assertEquals(ready.getString("url"),all.getJSONObject(0).getString("url"));assertEquals(cold.getString("url"),all.getJSONObject(1).getString("url"));assertEquals(cold2.getString("url"),all.getJSONObject(2).getString("url"));
 }
 @Test public void playerRechecksLimitAndKeepsLighterStallRecovery()throws Exception{
  JSONArray all=new JSONArray().put(option("Over.2160p.HEVC",true,5)).put(option("Same.1080p.x264",true,29)).put(option("Unknown.720p.x264",true,0)).put(option("Lighter.1080p.x264",false,18));assertEquals(3,PlayerActivity.fallbackIndex(all,true,30,profile(),MOVIE,0));assertEquals(1,PlayerActivity.fallbackIndex(all,false,30,profile(),MOVIE,0));assertEquals(1,PlayerActivity.fallbackIndex(all,true,0,profile(),MOVIE,0));assertEquals(2,PlayerActivity.fallbackIndex(new JSONArray().put(JSONObject.NULL).put(option("Over.2160p.HEVC",true,5)).put(option("Unknown.1080p.x264",true,0)),true,30,profile(),MOVIE,0));
 }
 @Test public void invalidOrOverLimitFallbacksCannotStartAutomatically()throws Exception{
  JSONArray all=new JSONArray().put(JSONObject.NULL).put(option("Bad.1080p.x264",true,5).put("url","file:///bad")).put(option("Headers.1080p.x264",true,5).put("headers",new JSONObject().put("Authorization","test"))).put(option("Over.2160p.HEVC",true,5));assertEquals(-1,PlayerActivity.fallbackIndex(all,false,20,profile(),MOVIE,0));
 }
 @Test public void automaticRecoveryStopsAfterFourAttempts()throws Exception{
  List<JSONObject> options=new ArrayList<>();for(int n=0;n<10;n++)options.add(option("Movie"+n+".1080p.x264",true,10-n*.5));JSONArray all=PhoneStream.tvFallbacks(options,0,MOVIE,profile());assertEquals(9,all.length());for(int attempt=0;attempt<4;attempt++){int next=PlayerActivity.fallbackIndex(all,false,20,profile(),MOVIE,attempt);assertTrue(next>=0);all.remove(next);}assertTrue(all.length()>0);assertEquals(-1,PlayerActivity.fallbackIndex(all,false,20,profile(),MOVIE,4));assertEquals(-1,PlayerActivity.fallbackIndex(all,true,20,profile(),MOVIE,5));
 }
 @Test public void phoneStillPrefersCachedAndIgnoresTvLimitAndDecoderPolicy()throws Exception{
  JSONObject hd=option("Movie.1080p.x264",false,10).put("resolution","1080p"),uhd=option("Movie.2160p.AV1",true,12).put("resolution","2160p");List<JSONObject> options=PhoneStream.rank(Arrays.asList(hd,uhd),"1080p",true);assertSame(uhd,options.get(0));assertFalse(PhoneStream.shouldPick(false,false,options,null,0));assertTrue(PhoneStream.shouldPick(false,true,options,null,0));assertTrue(PhoneStream.shouldPick(false,false,Collections.singletonList(hd),null,0));
 }
}
