package app.noir.mobile;

import org.junit.Test;
import static org.junit.Assert.*;

/** Pairs this device with the PC fixture and syncs once, so device-specific suites (the TV runner) need no unrelated end-to-end assertions first. */
public final class FixturePairTest {
    @Test public void pairsAndSyncsTheFixtureLibrary()throws Exception{
        DataStore data=NoirApp.data;if(!data.paired())data.pair(TestRunner.arguments.getString("pairUri"));else data.sync();
        assertTrue("Paired",data.paired());assertTrue("Fixture library received",data.items().length()>0);data.flush();
    }
}
