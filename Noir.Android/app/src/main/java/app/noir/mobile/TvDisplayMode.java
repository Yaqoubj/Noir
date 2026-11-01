package app.noir.mobile;
import android.app.Activity;
import android.os.Build;
import android.view.Display;
import android.view.WindowManager;

/** Display matching is TV opt-in; the policy below is also callable from plain Java tests. */
public final class TvDisplayMode {
    public static final class Mode {
        public final int id,width,height;public final double hz;
        public Mode(int id,int width,int height,double hz){this.id=id;this.width=width;this.height=height;this.hz=hz;}
    }
    private final Activity activity;private final int original;private boolean changed;
    public TvDisplayMode(Activity activity){this.activity=activity;original=activity.getWindow().getAttributes().preferredDisplayModeId;}
    private static boolean valid(double n){return n>0&&!Double.isNaN(n)&&!Double.isInfinite(n);}
    public static boolean matches(double hz,double fps){if(!valid(hz)||!valid(fps))return false;long multiple=Math.round(hz/fps);return multiple>=1&&Math.abs(hz/(fps*multiple)-1)<=.0015;}
    /** Keep an already compatible mode. Otherwise use the lowest integer multiple at the current resolution. */
    public static int choose(Mode current,Mode[] available,double fps){
        if(current==null)return 0;if(!valid(fps)||matches(current.hz,fps)||available==null)return current.id;
        Mode best=null;long bestMultiple=Long.MAX_VALUE;double bestError=Double.MAX_VALUE;
        for(Mode m:available){if(m==null||m.width!=current.width||m.height!=current.height||!matches(m.hz,fps))continue;long multiple=Math.round(m.hz/fps);double error=Math.abs(m.hz/fps-multiple);if(multiple<bestMultiple||multiple==bestMultiple&&error<bestError){best=m;bestMultiple=multiple;bestError=error;}}
        return best==null?current.id:best.id;
    }
    @android.annotation.TargetApi(30) public void apply(double fps){
        if(Build.VERSION.SDK_INT<30)return;Display display=activity.getDisplay();if(display==null)return;Display.Mode c=display.getMode();Display.Mode[] modes=display.getSupportedModes();Mode[] choices=new Mode[modes.length];for(int n=0;n<modes.length;n++)choices[n]=mode(modes[n]);int id=choose(mode(c),choices,fps);if(id==c.getModeId())return;
        WindowManager.LayoutParams p=activity.getWindow().getAttributes();if(p.preferredDisplayModeId==id)return;p.preferredDisplayModeId=id;activity.getWindow().setAttributes(p);changed=true;
    }
    private static Mode mode(Display.Mode m){return new Mode(m.getModeId(),m.getPhysicalWidth(),m.getPhysicalHeight(),m.getRefreshRate());}
    public void restore(){if(!changed)return;WindowManager.LayoutParams p=activity.getWindow().getAttributes();p.preferredDisplayModeId=original;activity.getWindow().setAttributes(p);changed=false;}
}
