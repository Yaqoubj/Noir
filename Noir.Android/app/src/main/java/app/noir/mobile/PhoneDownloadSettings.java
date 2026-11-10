package app.noir.mobile;

import android.content.Context;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.util.AtomicFile;
import android.util.Base64;
import org.json.JSONObject;
import javax.crypto.*;
import javax.crypto.spec.GCMParameterSpec;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.security.KeyStore;

/** Installation-local secrets. Neither credentials nor signed source URLs enter plain preferences. */
final class PhoneDownloadSettings {
    private static final Object KEY_LOCK=new Object();
    private static final String ALIAS="noir-phone-downloads-v1";
    final Context context;
    PhoneDownloadSettings(Context c){context=c.getApplicationContext();}
    private javax.crypto.SecretKey key()throws Exception{ synchronized(KEY_LOCK){
        KeyStore store=KeyStore.getInstance("AndroidKeyStore");store.load(null);
        if(!store.containsAlias(ALIAS)){KeyGenerator generator=KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES,"AndroidKeyStore");generator.init(new KeyGenParameterSpec.Builder(ALIAS,KeyProperties.PURPOSE_ENCRYPT|KeyProperties.PURPOSE_DECRYPT).setBlockModes(KeyProperties.BLOCK_MODE_GCM).setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE).build());generator.generateKey();}
        return (javax.crypto.SecretKey)store.getKey(ALIAS,null);
    }}
    synchronized String seal(String text)throws Exception{Cipher cipher=Cipher.getInstance("AES/GCM/NoPadding");cipher.init(Cipher.ENCRYPT_MODE,key());JSONObject result=new JSONObject();result.put("iv",Base64.encodeToString(cipher.getIV(),Base64.NO_WRAP));result.put("data",Base64.encodeToString(cipher.doFinal(text.getBytes(StandardCharsets.UTF_8)),Base64.NO_WRAP));return result.toString();}
    synchronized String unseal(String text)throws Exception{JSONObject value=new JSONObject(text);Cipher cipher=Cipher.getInstance("AES/GCM/NoPadding");cipher.init(Cipher.DECRYPT_MODE,key(),new GCMParameterSpec(128,Base64.decode(value.getString("iv"),Base64.NO_WRAP)));return new String(cipher.doFinal(Base64.decode(value.getString("data"),Base64.NO_WRAP)),StandardCharsets.UTF_8);}
    static JSONObject defaults(JSONObject value){if(!value.has("torrents"))DataStore.put(value,"torrents",false);if(!value.has("torrentMobile"))DataStore.put(value,"torrentMobile",false);if(!value.has("torrentSharing"))DataStore.put(value,"torrentSharing","ratio");if(!value.has("torrentUploadKB"))DataStore.put(value,"torrentUploadKB",512);if(!value.has("torrentAutoSwitch"))DataStore.put(value,"torrentAutoSwitch",true);return value;}
    synchronized JSONObject load()throws Exception{File file=new File(context.getFilesDir(),"phone-download-settings.enc");return defaults(!file.exists()?new JSONObject():new JSONObject(unseal(new String(new AtomicFile(file).readFully(),StandardCharsets.UTF_8))));}
    synchronized void save(JSONObject value)throws Exception{
        defaults(value);if(!java.util.Arrays.asList("off","ratio","forever").contains(value.optString("torrentSharing"))||value.optInt("torrentUploadKB")<1||value.optInt("torrentUploadKB")>1048576)throw new IOException("Check the sharing upload limit.");
        String manifest=value.optString("manifest").trim();if(!manifest.isEmpty())PhoneDownloadProvider.streamUri(manifest,"tt1",true,0,0);
        DataStore.put(value,"subtitleAddonUrl",SubtitleClient.normalizeAddon(value.optString("subtitleAddonUrl")));
        if(!java.util.Arrays.asList("","opensubtitles","subdl","subsource","wyzie","addon").contains(value.optString("subtitleSource")))throw new IOException("Choose a subtitle source.");
        if(value.has("subtitleSiteKeys")){JSONObject clean=new JSONObject(),keys=value.optJSONObject("subtitleSiteKeys");for(String site:SubtitleSources.SITES)if(keys!=null&&!keys.optString(site).isEmpty())DataStore.put(clean,site,keys.optString(site));DataStore.put(value,"subtitleSiteKeys",clean);}
        if(value.optLong("maxEpisodeBytes")<0)throw new IOException("Enter a positive maximum size.");
        DiscoveryClient.write(new File(context.getFilesDir(),"phone-download-settings.enc"),seal(value.toString()).getBytes(StandardCharsets.UTF_8));PhoneHarbor.phoneCache=null;PhoneHarbor.phoneAt=0;
    }
    static String safe(String value,JSONObject settings){String text=value.replaceAll("(?i)https?://[^\\s<>]+","[link]");for(String field:new String[]{"manifest","torBoxKey","realDebridKey","subtitleAddonUrl","subtitleApiKey","subtitleUsername","subtitlePassword","subtitleAddonAuth"}){String secret=settings.optString(field);if(!secret.isEmpty())text=text.replace(secret,"[private]");}JSONObject keys=settings.optJSONObject("subtitleSiteKeys");if(keys!=null)for(String site:SubtitleSources.SITES){String secret=keys.optString(site);if(!secret.isEmpty())text=text.replace(secret,"[private]");}return text.substring(0,Math.min(600,text.length()));}
    /** Writes the chosen subtitle source into a settings object. Only the chosen source's fields are kept; every site's key stays in the per-site map. */
    static void applySubtitle(JSONObject value,SubtitleSources.Selection sel,JSONObject siteKeys){
        JSONObject keys=new JSONObject();for(String site:SubtitleSources.SITES)if(siteKeys!=null&&!siteKeys.optString(site).isEmpty())DataStore.put(keys,site,siteKeys.optString(site));if(sel.site())DataStore.put(keys,sel.source,sel.apiKey);
        DataStore.put(value,"subtitleSource",sel.source);DataStore.put(value,"subtitleApiKey",sel.site()?sel.apiKey:"");DataStore.put(value,"subtitleUsername",sel.username);DataStore.put(value,"subtitlePassword",sel.password);DataStore.put(value,"subtitleAddonUrl",sel.addonUrl);DataStore.put(value,"subtitleAddonAuth",sel.addonAuth);DataStore.put(value,"subtitleAddonOnly",sel.only);DataStore.put(value,"subtitleSiteKeys",keys);
    }
    /** Settings sent by the paired PC. Older PCs send only an addon link (and its "only" flag): that means the addon. This phone's own per-site keys are kept. */
    JSONObject imported(JSONObject payload){
        JSONObject existing;try{existing=load();}catch(Exception e){existing=new JSONObject();}
        JSONObject value=DataStore.copy(payload),keys=new JSONObject(),old=existing.optJSONObject("subtitleSiteKeys"),sent=value.optJSONObject("subtitleSiteKeys");
        String source=value.optString("subtitleSource");if(source.isEmpty()&&!value.optString("subtitleAddonUrl").isEmpty())source="addon";if(!java.util.Arrays.asList("","opensubtitles","subdl","subsource","wyzie","addon").contains(source))source="";
        for(String site:SubtitleSources.SITES){String key=sent!=null&&!sent.optString(site).isEmpty()?sent.optString(site):old==null?"":old.optString(site);if(!key.isEmpty())DataStore.put(keys,site,key);}
        if(java.util.Arrays.asList(SubtitleSources.SITES).contains(source)&&!value.optString("subtitleApiKey").isEmpty())DataStore.put(keys,source,value.optString("subtitleApiKey"));
        DataStore.put(value,"subtitleSource",source);DataStore.put(value,"subtitleSiteKeys",keys);return value;
    }
}
