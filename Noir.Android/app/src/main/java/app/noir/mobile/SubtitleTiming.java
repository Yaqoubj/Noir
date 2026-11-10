package app.noir.mobile;

import java.util.*;
import java.util.regex.*;

/** Reads and shifts cue times in SRT, VTT and ASS text, leaving everything else (including Arabic text) untouched. Times are milliseconds. */
final class SubtitleTiming {
    private static final Pattern ARROW=Pattern.compile("(?:(\\d{1,2}):)?(\\d{1,2}):(\\d{2})([,.])(\\d{1,3})(\\s*-->\\s*)(?:(\\d{1,2}):)?(\\d{1,2}):(\\d{2})([,.])(\\d{1,3})");
    private static final Pattern ASS=Pattern.compile("(?m)^(Dialogue:\\s*[^,\\r\\n]*,)(\\d+):(\\d{2}):(\\d{2})\\.(\\d{1,3})(,)(\\d+):(\\d{2}):(\\d{2})\\.(\\d{1,3})");
    private static boolean ass(String ext){return ext.equals(".ass")||ext.equals(".ssa");}
    private static long ms(String h,String m,String s,String frac){StringBuilder f=new StringBuilder(frac);while(f.length()<3)f.append('0');long hours=h==null?0:Long.parseLong(h);return (hours*3600+Long.parseLong(m)*60+Long.parseLong(s))*1000+Long.parseLong(f.substring(0,3));}
    /** Cues with end after start, in file order. */
    static List<double[]> cues(String text,String ext){
        List<double[]> out=new ArrayList<>();Matcher m=(ass(ext)?ASS:ARROW).matcher(text);
        while(m.find()){long a,b;if(ass(ext)){a=ms(m.group(2),m.group(3),m.group(4),m.group(5));b=ms(m.group(7),m.group(8),m.group(9),m.group(10));}else{a=ms(m.group(1),m.group(2),m.group(3),m.group(5));b=ms(m.group(7),m.group(8),m.group(9),m.group(11));}if(b>a)out.add(new double[]{a,b});}
        return out;
    }
    private static String stamp(long ms,boolean hours,String sep){
        long total=ms/1000,mil=ms%1000,h=total/3600,m=(total%3600)/60,s=total%60;String body=String.format(Locale.ROOT,"%02d:%02d%s%03d",m,s,sep,mil);
        return hours||h>0?String.format(Locale.ROOT,"%02d:",h)+body:body;
    }
    private static String stampAss(long ms){long cs=Math.round(ms/10d),total=cs/100,frac=cs%100,h=total/3600,m=(total%3600)/60,s=total%60;return String.format(Locale.ROOT,"%d:%02d:%02d.%02d",h,m,s,frac);}
    private static long moved(long value,double scale,double offset){return (long)Math.rint(value*scale+offset*1000);}
    static String apply(String text,String ext,double scale,double offset){
        Matcher m=(ass(ext)?ASS:ARROW).matcher(text);StringBuffer out=new StringBuffer();
        while(m.find()){
            String replacement;
            if(ass(ext)){long a=moved(ms(m.group(2),m.group(3),m.group(4),m.group(5)),scale,offset),b=moved(ms(m.group(7),m.group(8),m.group(9),m.group(10)),scale,offset);replacement=m.group(1)+stampAss(Math.max(0,a))+m.group(6)+stampAss(Math.max(0,b));}
            else{long a=moved(ms(m.group(1),m.group(2),m.group(3),m.group(5)),scale,offset),b=moved(ms(m.group(7),m.group(8),m.group(9),m.group(11)),scale,offset);replacement=stamp(Math.max(0,a),m.group(1)!=null,m.group(4))+m.group(6)+stamp(Math.max(0,b),m.group(7)!=null,m.group(10));}
            m.appendReplacement(out,Matcher.quoteReplacement(replacement));
        }
        m.appendTail(out);return out.toString();
    }
}
