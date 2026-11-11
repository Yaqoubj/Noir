package app.noir.mobile;
import android.os.Bundle;
import androidx.test.runner.AndroidJUnitRunner;
public final class TestRunner extends AndroidJUnitRunner {
    static Bundle arguments;
    @Override public void onCreate(Bundle args){arguments=args;super.onCreate(args);}
    // A fresh emulator shows a one-time "Viewing full screen" notice over the player, which hides video and subtitles from screenshots.
    @Override public void onStart(){try{getUiAutomation().executeShellCommand("settings put secure immersive_mode_confirmations confirmed").close();for(String scale:new String[]{"animator_duration_scale","transition_animation_scale","window_animation_scale"})getUiAutomation().executeShellCommand("settings put global "+scale+" 0").close();}catch(Exception ignored){}super.onStart();}// animations off: the Home hero drift (36 s) otherwise keeps the main thread busy past the 45 s launch wait
}
