package app.noir.mobile;

import android.net.Uri;
import androidx.test.platform.app.InstrumentationRegistry;
import org.junit.*;
import static org.junit.Assert.*;

/** TV pairing by address + code: the certificate read before pairing must be the PC's real pin, and both sides show the same check code. */
public final class TvPairTest {
    @Test public void checkCodeMatchesThePcRule(){assertEquals("AB12 CD34 EF56",DataStore.checkCode("ab12cd34ef567890"+"0".repeat(48)));}
    @Test public void peekReadsThePairingPinWithoutPairing()throws Exception{
        String link=InstrumentationRegistry.getArguments().getString("pairUri");Assume.assumeTrue(link!=null);Uri uri=Uri.parse(link);
        assertTrue("same fingerprint as the PC's pairing link",DataStore.peekPin(uri.getQueryParameter("address")).equalsIgnoreCase(uri.getQueryParameter("pin")));
    }
}
