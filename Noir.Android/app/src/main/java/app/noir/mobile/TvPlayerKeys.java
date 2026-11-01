package app.noir.mobile;

/** Pure remote policy. Key numbers are Android KeyEvent values; no framework calls or state. */
public final class TvPlayerKeys {
    private TvPlayerKeys(){}
    public enum Action { NONE,CONSUME,SHOW_TOGGLE,ACTIVATE,SEEK_BACK,SEEK_FORWARD,COMMIT_SEEK,TRACKS,CHAPTERS,CLOSE_PANEL,HIDE_CONTROLS,EXIT,TOGGLE,PLAY,PAUSE,NEXT }
    public static boolean seekKey(int key){return key==21||key==22||key==89||key==90;}
    public static boolean mediaKey(int key){return key==85||key==86||key==87||key==89||key==90||key==126||key==127;}
    public static Action action(int key,boolean down,int repeats,boolean controls,boolean panel,boolean prompt,boolean seeking){
        if(!down){if(seekKey(key)&&seeking)return Action.COMMIT_SEEK;return mediaKey(key)||key==4||key==19||key==20||key==23||key==66||key==160?Action.CONSUME:Action.NONE;}
        if(seekKey(key)&&(seeking||key==89||key==90||!panel&&!prompt&&!controls))return key==21||key==89?Action.SEEK_BACK:Action.SEEK_FORWARD;
        if(repeats>0)return mediaKey(key)||key==4||key==19||key==20||key==23||key==66||key==160?Action.CONSUME:Action.NONE;
        switch(key){
            case 86:return Action.EXIT;
            case 85:return Action.TOGGLE;
            case 126:return Action.PLAY;
            case 127:return Action.PAUSE;
            case 87:return Action.NEXT;
            case 89:case 90:return Action.CONSUME;
            case 4:return panel||prompt?Action.CLOSE_PANEL:seeking||controls?Action.HIDE_CONTROLS:Action.EXIT;
            case 23:case 66:case 160:return panel||prompt||controls?Action.ACTIVATE:Action.SHOW_TOGGLE;
            case 19:return panel||prompt?Action.NONE:Action.TRACKS;
            case 20:return panel||prompt?Action.NONE:Action.CHAPTERS;
            default:return Action.NONE;
        }
    }
    /** Initial preference, then 30 and 60 second steps after 1 and 3 seconds held. */
    public static int seekSeconds(int preference,long heldMs){int base=Math.max(5,Math.min(60,preference));return heldMs>=3000?Math.max(base,60):heldMs>=1000?Math.max(base,30):base;}
    public static double target(double position,int direction,int step,double duration){return Math.max(0,Math.min(Math.max(0,duration-.3),position+direction*(double)step));}
}
