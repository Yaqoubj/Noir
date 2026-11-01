package app.noir.mobile;
import android.content.Context;
import android.hardware.display.DisplayManager;
import android.media.*;
import android.os.Build;
import android.view.Display;
import java.util.*;

/** What this TV can play smoothly: hardware video decoders, screen size, HDR formats and the audio formats the TV/soundbar accepts over HDMI. Read once per process. */
public final class TvCaps {
    /** Hardware decoders. `hevc10` = HEVC Main10 (most 4K HDR releases). */
    public final boolean avc,hevc,hevc10,av1,vp9,dolbyVisionDecoder;
    /** Tallest display mode (1080 or 2160 on most TVs). */
    public final int maxHeight;
    public final boolean hdr10,hdr10plus,hlg,dolbyVision;
    /** Audio formats HDMI/ARC accepts as-is: ac3, eac3, eac3-joc, dts, dtshd, truehd. Empty on TV speakers without passthrough. */
    public final Set<String> passthrough;
    private static TvCaps cached;

    private TvCaps(Context c){
        boolean[] v=new boolean[6];
        try{for(MediaCodecInfo info:new MediaCodecList(MediaCodecList.REGULAR_CODECS).getCodecInfos()){if(info.isEncoder()||!hardware(info))continue;
            for(String type:info.getSupportedTypes()){String t=type.toLowerCase(Locale.ROOT);
                if(t.equals("video/avc"))v[0]=true;
                else if(t.equals("video/hevc")){v[1]=true;try{for(MediaCodecInfo.CodecProfileLevel p:info.getCapabilitiesForType(type).profileLevels)if(p.profile==MediaCodecInfo.CodecProfileLevel.HEVCProfileMain10||p.profile==MediaCodecInfo.CodecProfileLevel.HEVCProfileMain10HDR10||p.profile==MediaCodecInfo.CodecProfileLevel.HEVCProfileMain10HDR10Plus)v[2]=true;}catch(Exception ignored){}}
                else if(t.equals("video/av01"))v[3]=true;else if(t.equals("video/x-vnd.on2.vp9"))v[4]=true;else if(t.equals("video/dolby-vision"))v[5]=true;}}}catch(Exception ignored){}
        avc=v[0];hevc=v[1];hevc10=v[2];av1=v[3];vp9=v[4];dolbyVisionDecoder=v[5];
        int height=1080;boolean h10=false,h10p=false,h=false,dv=false;
        try{Display d=((DisplayManager)c.getSystemService(Context.DISPLAY_SERVICE)).getDisplay(Display.DEFAULT_DISPLAY);
            if(d!=null){int max=0;for(Display.Mode m:d.getSupportedModes())max=Math.max(max,Math.min(m.getPhysicalWidth(),m.getPhysicalHeight()));if(max>0)height=max;
                Display.HdrCapabilities hdr=d.getHdrCapabilities();if(hdr!=null)for(int type:hdr.getSupportedHdrTypes()){if(type==Display.HdrCapabilities.HDR_TYPE_HDR10)h10=true;else if(type==Display.HdrCapabilities.HDR_TYPE_HDR10_PLUS)h10p=true;else if(type==Display.HdrCapabilities.HDR_TYPE_HLG)h=true;else if(type==Display.HdrCapabilities.HDR_TYPE_DOLBY_VISION)dv=true;}}}catch(Exception ignored){}
        maxHeight=height;hdr10=h10;hdr10plus=h10p;hlg=h;dolbyVision=dv;
        Set<String> audio=new LinkedHashSet<>();
        try{AudioManager am=(AudioManager)c.getSystemService(Context.AUDIO_SERVICE);for(AudioDeviceInfo d:am.getDevices(AudioManager.GET_DEVICES_OUTPUTS)){int type=d.getType();if(type!=AudioDeviceInfo.TYPE_HDMI&&type!=AudioDeviceInfo.TYPE_HDMI_ARC&&type!=29/*TYPE_HDMI_EARC*/)continue;
            for(int e:d.getEncodings())switch(e){case AudioFormat.ENCODING_AC3:audio.add("ac3");break;case AudioFormat.ENCODING_E_AC3:audio.add("eac3");break;case 18/*E_AC3_JOC*/:audio.add("eac3-joc");break;case AudioFormat.ENCODING_DTS:audio.add("dts");break;case AudioFormat.ENCODING_DTS_HD:audio.add("dtshd");break;case 14/*DOLBY_TRUEHD*/:audio.add("truehd");break;default:break;}}}catch(Exception ignored){}
        passthrough=Collections.unmodifiableSet(audio);
    }
    static boolean hardware(MediaCodecInfo info){
        if(Build.VERSION.SDK_INT>=29)return info.isHardwareAccelerated();
        String n=info.getName().toLowerCase(Locale.ROOT);return !n.startsWith("omx.google.")&&!n.startsWith("c2.android.")&&!n.contains(".sw.");
    }
    public static synchronized TvCaps get(Context c){if(cached==null)cached=new TvCaps(c.getApplicationContext());return cached;}
    public boolean uhd(){return maxHeight>=2000;}
    public boolean hdr(){return hdr10||hdr10plus||hlg||dolbyVision;}
    @Override public String toString(){return "video avc="+avc+" hevc="+hevc+" hevc10="+hevc10+" av1="+av1+" vp9="+vp9+" dvDecoder="+dolbyVisionDecoder+" · screen "+maxHeight+"p hdr10="+hdr10+" hdr10+="+hdr10plus+" hlg="+hlg+" dv="+dolbyVision+" · passthrough "+passthrough;}
}
