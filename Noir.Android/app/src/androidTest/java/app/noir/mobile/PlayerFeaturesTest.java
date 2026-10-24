package app.noir.mobile;
import android.content.*;
import androidx.test.platform.app.InstrumentationRegistry;
import org.junit.Test;
import java.io.File;
import java.util.UUID;
import static org.junit.Assert.*;

public final class PlayerFeaturesTest {
    Context isolated(){Context app=InstrumentationRegistry.getInstrumentation().getTargetContext();File root=new File(app.getFilesDir(),"P3-test-"+UUID.randomUUID());root.mkdirs();return new ContextWrapper(app){@Override public Context getApplicationContext(){return this;}@Override public File getFilesDir(){return root;}@Override public File getExternalFilesDir(String type){return new File(root,"external");}};}
    @Test public void chapterNamesClassify(){assertEquals("recap",PlayerPrefs.classify("Previously on Severance"));assertEquals("recap",PlayerPrefs.classify("Recap"));assertEquals("credits",PlayerPrefs.classify("End Credits"));assertEquals("intro",PlayerPrefs.classify("Opening"));assertEquals("",PlayerPrefs.classify("Chapter 3"));assertEquals("",PlayerPrefs.classify(null));}
    @Test public void skipModesMigrateAndPersist(){DataStore data=new DataStore(isolated());data.phoneSetting("autoSkipIntro",true);assertEquals(PlayerPrefs.AUTO,PlayerPrefs.mode(data,"intro"));assertEquals(PlayerPrefs.MANUAL,PlayerPrefs.mode(data,"recap"));
        PlayerPrefs.mode(data,"intro",PlayerPrefs.OFF);assertEquals(PlayerPrefs.OFF,PlayerPrefs.mode(data,"intro"));assertFalse(data.playbackFlag("autoSkipIntro",true));PlayerPrefs.mode(data,"credits",PlayerPrefs.AUTO);assertEquals(PlayerPrefs.AUTO,PlayerPrefs.mode(data,"credits"));}
    @Test public void stillWatchingAndSpeedRules(){assertFalse(PlayerPrefs.shouldAsk(2,3));assertTrue(PlayerPrefs.shouldAsk(3,3));assertFalse(PlayerPrefs.shouldAsk(9,0));DataStore data=new DataStore(isolated());assertEquals(3,PlayerPrefs.stillAfter(data));data.pref("stillWatchingAfter",7);assertEquals(3,PlayerPrefs.stillAfter(data));data.pref("stillWatchingAfter",5);assertEquals(5,PlayerPrefs.stillAfter(data));
        assertEquals(.5f,PlayerPrefs.clampSpeed(.1),0);assertEquals(2f,PlayerPrefs.clampSpeed(4),0);assertEquals(1.25f,PlayerPrefs.clampSpeed(1.3),0);assertEquals(1f,PlayerPrefs.clampSpeed(Double.NaN),0);assertEquals("1.25×",PlayerPrefs.speedLabel(1.25f));assertEquals("2×",PlayerPrefs.speedLabel(2f));assertEquals(1f,PlayerPrefs.defaultSpeed(data),0);}
}
