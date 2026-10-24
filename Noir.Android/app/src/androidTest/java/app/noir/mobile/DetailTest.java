package app.noir.mobile;
import org.json.*;
import org.junit.Test;
import static org.junit.Assert.*;

public final class DetailTest {
    @Test public void cinemetaCastShapes()throws Exception{JSONArray rows=PhoneCredits.parseCinemeta(new JSONObject("{\"app_extras\":{\"cast\":[{\"name\":\"Ada\",\"character\":\"Pilot\",\"photo\":\"https://example.org/a.jpg\"}]}}"));assertEquals("Ada",rows.getJSONObject(0).getString("name"));assertEquals("Pilot",rows.getJSONObject(0).getString("character"));assertEquals("Bea",PhoneCredits.parseCinemeta(new JSONObject("{\"credits_cast\":[{\"name\":\"Bea\",\"role\":\"Doctor\"}]}" )).getJSONObject(0).getString("name"));assertEquals(2,PhoneCredits.parseCinemeta(new JSONObject("{\"cast\":[\"Ada\",\"Bea\"]}")).length());}
    @Test public void tvmazePhotosAndIds()throws Exception{JSONArray cast=PhoneCredits.parseTvmaze(new JSONArray("[{\"person\":{\"id\":42,\"name\":\"Ada\",\"image\":{\"medium\":\"https://example.org/photo.jpg\"}},\"character\":{\"name\":\"Pilot\"}}]"));assertEquals(42,cast.getJSONObject(0).getInt("tvmaze"));assertEquals("https://example.org/photo.jpg",cast.getJSONObject(0).getString("photo"));}
    @Test public void trailerOrderingAndValidation()throws Exception{assertEquals("abcdefghijk",PhonePreview.trailer(new JSONObject("{\"trailers\":[{\"source\":\"lmnopqrstuv\",\"type\":\"Teaser\"},{\"source\":\"abcdefghijk\",\"type\":\"Trailer\"}]}")));assertEquals("abcdefghijk",PhonePreview.videoId("https://youtu.be/abcdefghijk"));assertEquals("",PhonePreview.videoId("https://evil.example/abcdefghijk"));assertEquals("",PhonePreview.videoId("javascript:abcdefghijk"));}
    @Test public void spoilerPositionAndSettings(){assertEquals(0,PhonePreview.startPosition(1000,0,true,false),0);assertEquals(70,PhonePreview.startPosition(1000,70,true,false),0);assertEquals(200,PhonePreview.startPosition(1000,70,true,true),0);assertEquals(200,PhonePreview.startPosition(1000,0,false,false),0);assertEquals("trailer",PhonePreview.normalize(null));assertEquals("off",PhonePreview.normalize(false));assertEquals("title",PhonePreview.normalize("The title itself"));assertEquals("off",PhonePreview.normalize(2));}
}
