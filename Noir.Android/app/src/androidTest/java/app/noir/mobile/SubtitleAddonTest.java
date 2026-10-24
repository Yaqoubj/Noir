package app.noir.mobile;
import org.junit.Test;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.util.*;
import static org.junit.Assert.*;

public final class SubtitleAddonTest {
 @Test public void normalizeSubtitleAddonLinks()throws Exception{
  String base="https://addon.example/private-token";
  for(String link:new String[]{base,base+"/",base+"/manifest.json","stremio://addon.example/private-token/manifest.json","https://ADDON.example:443/private-token/manifest.json"})assertEquals(base,SubtitleClient.normalizeAddon(link));
  assertEquals("",SubtitleClient.normalizeAddon(" "));
  for(String link:new String[]{"http://addon.example/manifest.json","https://addon.example:8443/manifest.json","https://user:secret@addon.example/manifest.json","https://addon.example/manifest.json?token=secret","https://addon.example/#fragment","https://localhost/manifest.json"})try{SubtitleClient.normalizeAddon(link);fail("Invalid source accepted");}catch(IOException expected){}
 }
 @Test public void sourceSelectionAndSearchPathsArePure()throws Exception{
  String base="https://addon.example/private-token",defaultSource=SubtitleClient.DEFAULT_SOURCE;
  List<String> only=SubtitleClient.searchUrls(base+"/manifest.json",true,"tt123",false,1,2,"Show WEB.mkv","0123456789abcdef");assertEquals(1,only.size());assertEquals(base+"/subtitles/series/tt123%3A1%3A2/filename=Show%20WEB.mkv&videoHash=0123456789abcdef.json",only.get(0));
  List<String> mixed=SubtitleClient.searchUrls(base,false,"tt123",true,0,0,"Movie.mkv","");assertEquals(2,mixed.size());assertTrue(mixed.get(0).startsWith(base+"/subtitles/movie/tt123/"));assertTrue(mixed.get(1).startsWith(defaultSource+"/subtitles/movie/tt123/"));assertFalse(mixed.get(0).contains("videoHash"));
  for(boolean exclusive:new boolean[]{true,false}){List<String> defaults=SubtitleClient.searchUrls("",exclusive,"tt123",true,0,0,"Movie","");assertEquals(1,defaults.size());assertTrue(defaults.get(0).startsWith(defaultSource+"/"));}
  assertEquals(1,SubtitleClient.searchUrls(defaultSource,false,"tt123",true,0,0,"Movie","").size());
 }
 @Test public void manifestResourcesAndLanguageAliases()throws Exception{
  for(String resources:new String[]{"[\"subtitles\"]","[{\"name\":\"subtitles\"}]"})assertEquals("SubDL",SubtitleClient.manifestName(("{\"name\":\"SubDL\",\"resources\":"+resources+"}").getBytes(StandardCharsets.UTF_8)));
  try{SubtitleClient.manifestName("{\"resources\":[\"stream\"]}".getBytes(StandardCharsets.UTF_8));fail("Non-subtitle addon accepted");}catch(IOException expected){}
  for(String value:new String[]{"en","eng","English","ENGLISH"," English "})assertEquals("en",SubtitleClient.language(value));for(String value:new String[]{"ar","ara","Arabic","ARABIC"})assertEquals("ar",SubtitleClient.language(value));
 }
 @Test public void searchesUseThisPhonesEncryptedSourceSettings()throws Exception{
  android.content.Context real=androidx.test.platform.app.InstrumentationRegistry.getInstrumentation().getTargetContext();java.io.File root=new java.io.File(real.getFilesDir(),"subtitle-addon-test-"+UUID.randomUUID());assertTrue(root.mkdirs());
  android.content.Context context=new android.content.ContextWrapper(real){@Override public android.content.Context getApplicationContext(){return this;}@Override public java.io.File getFilesDir(){return root;}@Override public java.io.File getExternalFilesDir(String type){return root;}};
  DataStore data=new DataStore(context);PhoneDownloadSettings storage=new PhoneDownloadSettings(context);String base="https://addon.example/private-token";
  org.json.JSONObject config=new org.json.JSONObject().put("subtitleAddonUrl",base+"/manifest.json").put("subtitleAddonOnly",true);storage.save(config);assertEquals(base,new PhoneDownloadSettings(context).load().optString("subtitleAddonUrl"));assertFalse(new String(java.nio.file.Files.readAllBytes(new java.io.File(root,"phone-download-settings.enc").toPath()),StandardCharsets.UTF_8).contains("private-token"));
  List<String> requests=new ArrayList<>();org.json.JSONObject item=new org.json.JSONObject().put("id","addon-test").put("imdb","tt123").put("movie",true);
  SubtitleClient client=new SubtitleClient(data,item,(url,limit)->{requests.add(url);boolean custom=url.startsWith(base);return ("{\"subtitles\":[{\"id\":\""+(custom?"custom":"default")+"\",\"lang\":\"English\",\"url\":\"https://subs.example/shared\"},{\"id\":\"unique\",\"lang\":\"en\",\"url\":\"https://subs.example/"+(custom?"custom":"default")+"\"}]}").getBytes(StandardCharsets.UTF_8);});client.identityRead=true;
  List<org.json.JSONObject> found=client.search("en");assertEquals(1,requests.size());assertTrue(requests.get(0).startsWith(base+"/"));assertEquals("custom",found.get(0).optString("id"));
  config.put("subtitleAddonOnly",false);storage.save(config);requests.clear();found=client.search("en");assertEquals(2,requests.size());assertTrue(requests.get(0).startsWith(base+"/"));assertTrue(requests.get(1).startsWith(SubtitleClient.DEFAULT_SOURCE+"/"));assertEquals(3,found.size());assertEquals("custom",found.get(0).optString("id"));
  storage.save(new org.json.JSONObject());requests.clear();client.search("en");assertEquals(1,requests.size());assertTrue(requests.get(0).startsWith(SubtitleClient.DEFAULT_SOURCE+"/"));
 }

}
