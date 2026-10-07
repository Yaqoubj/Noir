package app.noir.mobile;
import android.os.Bundle;
import androidx.test.runner.AndroidJUnitRunner;
public final class TestRunner extends AndroidJUnitRunner {
    static Bundle arguments;
    @Override public void onCreate(Bundle args){arguments=args;super.onCreate(args);}
}
