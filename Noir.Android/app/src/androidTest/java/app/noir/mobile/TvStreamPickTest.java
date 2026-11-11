package app.noir.mobile;
import org.json.*;
import org.junit.Test;
import java.util.*;
import static org.junit.Assert.*;

/** Runs on a phone too: every capability, bitrate and clock reading is synthetic. */
public final class TvStreamPickTest {
 static final double MOVIE=115*60;
 JSONObject option(String name,double gb,boolean cached)throws Exception{return new JSONObject().put("name",name).put("url","https://example.test/"+Integer.toHexString(name.hashCode())).put("service","direct").put("cached",cached).put("size",(long)(gb*1073741824d));}
 TvStreamPick.Profile profile(int height,double mbps,String quality){TvStreamPick.Profile p=new TvStreamPick.Profile();p.maxHeight=height;p.mbps=mbps;p.quality=quality;p.hevc=true;p.hevc10=height>=2000;return p;}
 List<JSONObject> rank(TvStreamPick.Profile p,JSONObject... options){return TvStreamPick.rank(Arrays.asList(options),p,MOVIE);}
 @Test public void parsesReleaseTagsAndBitrates()throws Exception{
  String[][] rows={
   {"Dune.Part.Two.2024.2160p.UHD.BluRay.REMUX.HEVC.DV.HDR10.TrueHD.Atmos.7.1-FraMeSToR","hevc","DV","truehd","remux","2160","true"},
   {"Severance.S02E01.2160p.ATVP.WEB-DL.DDP5.1.Atmos.H.265.HDR10+-NTb","hevc","HDR10+","eac3","web-dl","2160","true"},
   {"Arrival.2016.1080p.BluRay.x264.DTS-HD.MA.5.1-FGT","avc","","dtshd","bluray","1080","false"},
   {"The.Last.of.Us.S01E01.2160p.WEBRip.AV1.10bit.EAC3.5.1","av1","","eac3","webrip","2160","true"},
   {"Interstellar.2014.1080p.BluRay.x265.10-bit.DTS-MA","hevc","","dtshd","bluray","1080","true"},
   {"Planet.Earth.III.S01E01.4K.WEB-DL.HLG.h265.DD+.5.1","hevc","HLG","eac3","web-dl","2160","true"},
   {"The.Matrix.1999.1080p.BluRay.h264.AC3.5.1","avc","","ac3","bluray","1080","false"},
   {"Andor.S02E01.2160p.WEB-DL.DoVi.HEVC.Atmos","hevc","DV","atmos","web-dl","2160","true"}
  };
  for(String[] row:rows){TvStreamPick.Release r=TvStreamPick.parse(option(row[0],14.2,true),MOVIE);assertEquals(row[0],row[1],r.codec);assertEquals(row[2],r.hdr);assertEquals(row[3],r.audio);assertEquals(row[4],r.source);assertEquals(Integer.parseInt(row[5]),r.height);assertEquals(Boolean.parseBoolean(row[6]),r.tenBit);assertEquals(row[4].equals("remux"),r.remux);assertEquals(14.2*1073741824d*8/MOVIE/1e6,r.mbps,.00001);}
  assertEquals("unknown",TvStreamPick.parse(option("Mystery.1080p",0,false),MOVIE).codec);
  assertEquals("dts",TvStreamPick.parse(option("Movie.1080p.x264.DTS.5.1",1,true),MOVIE).audio);
  assertEquals("dtshd",TvStreamPick.parse(option("Movie.2160p.HEVC.DTS:X",1,true),MOVIE).audio);
  assertEquals("ac3",TvStreamPick.parse(option("Movie.1080p.AVC.DD.5.1",1,true),MOVIE).audio);
  assertEquals("eac3",TvStreamPick.parse(option("Movie.2160p.HEVC.DD+5.1",1,true),MOVIE).audio);assertTrue(TvStreamPick.parse(option("Movie.2160p.TrueHD7.1.Atmos",1,true),MOVIE).atmos);
  assertTrue(TvStreamPick.parse(option("Anime.1080p.x265.Hi10",1,true),MOVIE).tenBit);
  assertTrue(TvStreamPick.parse(option("Movie.2160p.HEVC.Dolby.Vision.HDR",1,true),MOVIE).baseHdr);
 }
 @Test public void usesKnownRuntimeThenMovieAndEpisodeDefaults()throws Exception{
  JSONObject movie=new JSONObject().put("movie",true);assertEquals(6900,TvStreamPick.runtime(movie,null),0);assertEquals(2700,TvStreamPick.runtime(new JSONObject(),null),0);
  movie.put("runtime","142 min");assertEquals(8520,TvStreamPick.runtime(movie,null),0);movie.put("duration",8000);assertEquals(8000,TvStreamPick.runtime(movie,null),0);movie.put("progress",new JSONObject().put("duration",8100));assertEquals(8100,TvStreamPick.runtime(movie,null),0);
  JSONObject show=new JSONObject().put("group","show");assertEquals(3000,TvStreamPick.runtime(show,new JSONObject().put("showRuntimes",new JSONObject().put("show",50))),0);
  assertEquals(20,TvStreamPick.parse(new JSONObject().put("size",750000000),300).mbps,0);
 }
 @Test public void screenAndQualityLimits()throws Exception{
  JSONObject uhd=option("Movie.2160p.WEB-DL.HEVC",4,true),hd=option("Movie.1080p.WEB-DL.x264",4,true);
  assertSame(hd,rank(profile(1080,100,"auto"),uhd,hd).get(0));assertSame(hd,rank(profile(2160,100,"1080p"),uhd,hd).get(0));assertSame(uhd,rank(profile(2160,100,"auto"),hd,uhd).get(0));
  TvStreamPick.Profile p=profile(2160,100,"auto");p.hevc10=false;assertSame(hd,rank(p,uhd,hd).get(0));
 }
 @Test public void autoPrefersWebAndBestAllowsRemuxWhenFast()throws Exception{
  JSONObject remux=option("Movie.2160p.BluRay.REMUX.HEVC.HDR10.TrueHD",70,true),web=option("Movie.2160p.WEB-DL.HEVC.HDR10.DDP",14,true);
  TvStreamPick.Profile fast=profile(2160,500,"best");fast.passthrough=true;fast.audio=Set.of("truehd");assertSame(web,rank(profile(2160,50,"auto"),remux,web).get(0));assertSame(remux,rank(fast,remux,web).get(0));assertSame(web,rank(profile(2160,500,"2160p"),remux,web).get(0));
  assertSame(remux,rank(profile(2160,500,"auto"),remux,option("Movie.2160p.WEB-DL.HEVC.HDR10.TrueHD",14,true)).get(0));
 }
 @Test public void hardwareAndDvCompatibilityComeBeforeCache()throws Exception{
  TvStreamPick.Profile p=profile(2160,100,"auto");JSONObject av1=option("Movie.2160p.AV1",4,true),hevc=option("Movie.2160p.HEVC.HDR10",4,true),dv=option("Movie.2160p.HEVC.DV",4,true);
  List<JSONObject> ordered=rank(p,av1,dv,hevc);assertEquals(Arrays.asList(hevc,dv,av1),ordered);
  JSONObject base=option("Movie.2160p.HEVC.DV.HDR10",4,true);assertSame(base,rank(p,base,hevc).get(0));p.dolbyVision=true;assertSame(hevc,rank(p,dv,hevc).get(0));p.dolbyVisionDecoder=true;assertSame(dv,rank(p,dv,hevc).get(0));
  p=profile(1080,100,"auto");JSONObject ten=option("Movie.1080p.x265.10bit",4,true),eight=option("Movie.1080p.x265",4,false);assertSame(eight,rank(p,ten,eight).get(0));
 }
 @Test public void cachedFirstStableTiesAndUnknownSpeedUses25()throws Exception{
  TvStreamPick.Profile p=profile(2160,0,"auto");JSONObject big=option("Movie.2160p.HEVC.HDR10",14.2,true),small=option("Movie.1080p.x264",4,true),uncached=option("Other.1080p.x264",4,false),tie=option("Second.1080p.x264",4,true);
  assertEquals(Arrays.asList(small,tie,big,uncached),rank(p,big,uncached,small,tie));assertEquals(rank(profile(2160,25,"auto"),big,small),rank(p,big,small));
  assertSame(big,rank(profile(2160,100,"auto"),small,big).get(0));
  JSONObject over=option("More.1080p.x264",25,true);assertEquals(Arrays.asList(big,over),rank(p,over,big));
 }
 @Test public void audioIsOnlyTheLastTieBreaker()throws Exception{
  TvStreamPick.Profile p=profile(2160,200,"best");JSONObject truehd=option("Movie.2160p.HEVC.TrueHD.Atmos",10,true),ddp=option("Movie.2160p.HEVC.DDP",10,true),hd=option("Movie.1080p.x264.DDP",10,true);
  assertSame(ddp,rank(p,truehd,ddp).get(0));assertSame(truehd,rank(p,hd,truehd).get(0));p.passthrough=true;p.audio=Set.of("truehd");assertSame(truehd,rank(p,truehd,ddp).get(0));
 }
 @Test public void preservesPhoneEligibilityFilters()throws Exception{
  JSONObject good=option("Good.1080p.x264",4,true),p2p=option("P2P.1080p",4,true).put("streamType","p2p"),live=option("Live.1080p",4,true).put("streamType","live"),error=option("Error.1080p",4,true).put("streamType","error"),cloud=option("Cloud.1080p",4,true).put("service","rd"),headers=option("Headers.1080p",4,true).put("headers",new JSONObject().put("Authorization","test"));
  assertEquals(Collections.singletonList(good),rank(profile(1080,50,"auto"),p2p,live,error,cloud,headers,good));
  good.put("headers",new JSONObject().put("User-Agent","Noir").put("Referer","https://example.test"));assertEquals(1,rank(profile(1080,50,"auto"),good).size());
 }
 // D8 (sharpen round): after a manual pick, the later streams come first (cached first), then the earlier ones; the pick itself is never a fallback.
 @Test public void fallbackSelectionStartsAfterManualPickPrefersCachedFirst()throws Exception{
  JSONObject before=option("Before",1,true),picked=option("Picked",1,true),cold=option("Cold",1,false),ready=option("Ready",1,true).put("headers",new JSONObject().put("Referer","https://example.test")),ready2=option("Ready2",2,true),cold2=option("Cold2",1,false),extra=option("Extra",1,false);
  JSONArray fallbacks=PhoneStream.tvFallbacks(Arrays.asList(before,picked,cold,ready,ready2,cold2,extra),1,MOVIE,profile(1080,100,"auto"));assertEquals(6,fallbacks.length());assertEquals(before.getString("url"),fallbacks.getJSONObject(5).getString("url"));assertEquals(ready.getString("url"),fallbacks.getJSONObject(0).getString("url"));assertEquals(ready2.getString("url"),fallbacks.getJSONObject(1).getString("url"));assertEquals(cold.getString("url"),fallbacks.getJSONObject(2).getString("url"));assertEquals(cold2.getString("url"),fallbacks.getJSONObject(3).getString("url"));assertEquals(extra.getString("url"),fallbacks.getJSONObject(4).getString("url"));assertEquals("https://example.test",fallbacks.getJSONObject(0).getJSONObject("headers").getString("Referer"));assertTrue(fallbacks.getJSONObject(0).getDouble("mbps")>0);
  assertEquals(1,PhoneStream.tvFallbacks(Arrays.asList(before,picked),1,MOVIE,profile(1080,100,"auto")).length());assertEquals(1,PhoneStream.tvFallbacks(Arrays.asList(picked,picked,ready),0,MOVIE,profile(1080,100,"auto")).length());
 }
 @Test public void countsThreeLongStallsOnceEachWithinSixtySeconds(){
  TvStreamPick.Stalls s=new TvStreamPick.Stalls();s.buffering(true,0);assertFalse(s.check(2000));assertFalse(s.check(2001));assertFalse(s.check(9000));assertEquals(1,s.recent.size());s.buffering(false,9001);
  s.buffering(true,10000);assertFalse(s.check(12001));s.buffering(false,12002);s.buffering(true,15000);assertTrue(s.check(17001));
  s.reset();s.buffering(true,0);s.check(2001);s.buffering(false,2002);s.buffering(true,61000);assertFalse(s.check(63001));s.buffering(false,63002);s.buffering(true,65000);assertFalse(s.check(67001));assertEquals(2,s.recent.size());
 }
 @Test public void shortStallsAndPauseDoNotCount(){TvStreamPick.Stalls s=new TvStreamPick.Stalls();s.buffering(true,0);assertFalse(s.check(1999));s.buffering(false,2000);assertFalse(s.check(10000));assertTrue(s.recent.isEmpty());s.buffering(true,11000);s.buffering(false,12000);assertFalse(s.check(20000));}
 @Test public void labelsExplainDecodeAndConnectionRisk()throws Exception{
  TvStreamPick.Profile p=profile(2160,25,"auto");assertTrue(TvStreamPick.label(option("Movie.2160p.AV1",4,true),p,MOVIE).contains(Loc.t("This TV can't decode it smoothly")));assertTrue(TvStreamPick.label(option("Movie.2160p.HEVC.HDR10",14.2,true),p,MOVIE).contains(Loc.t("May stutter on this connection")));assertTrue(TvStreamPick.label(option("Movie.1080p.x264",4,true),p,MOVIE).contains(Loc.t("Good for this TV")));
 }
 @Test public void qualityLimitsBeatCacheAndKeepOverLimitForManualChoice()throws Exception{
  JSONObject uhd=option("Movie.2160p.HEVC",4,true),hd=option("Movie.1080p.x264",4,false),remux=option("Movie.2160p.REMUX.HEVC",14,true),web=option("Movie.2160p.WEB-DL.HEVC",14,false);
  assertEquals(Arrays.asList(hd,uhd),rank(profile(2160,100,"1080p"),uhd,hd));assertEquals(Arrays.asList(web,remux),rank(profile(2160,100,"2160p"),remux,web));
  assertSame(uhd,rank(profile(2160,100,"1080p"),uhd).get(0));assertSame(remux,rank(profile(2160,100,"2160p"),remux).get(0));
 }
 @Test public void missingMetadataLosesToVerifiedWithinBudgetAndHasNoPositiveVerdict()throws Exception{
  TvStreamPick.Profile p=profile(2160,100,"best");JSONObject verified=option("Movie.1080p.x264",4,false),unknownSize=option("Movie.2160p.HEVC",0,true),unknownCodec=option("Movie.2160p",4,true),unknownBoth=option("Mystery.2160p",0,true);
  assertSame(verified,rank(p,unknownSize,unknownCodec,unknownBoth,verified).get(0));assertSame(verified,rank(p,unknownSize,verified).get(0));assertSame(verified,rank(p,unknownCodec,verified).get(0));assertTrue(TvStreamPick.decode(TvStreamPick.parse(unknownCodec,MOVIE),p)>0);
  for(JSONObject o:Arrays.asList(unknownSize,unknownCodec,unknownBoth))assertFalse(TvStreamPick.label(o,p,MOVIE).contains(Loc.t("Good for this TV")));
  assertTrue(TvStreamPick.label(unknownSize,p,MOVIE).contains(Loc.t("Bitrate unknown")));assertTrue(TvStreamPick.label(unknownCodec,p,MOVIE).contains(Loc.t("Compatibility unknown")));
 }
}
