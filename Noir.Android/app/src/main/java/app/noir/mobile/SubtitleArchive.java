package app.noir.mobile;

import java.io.*;
import java.util.*;
import java.util.regex.*;
import java.util.zip.*;

/** Subtitle sites that ship zips are read in memory only: counted by real inflated bytes, never written to disk. */
final class SubtitleArchive {
    static final class Picked {final byte[] bytes;final String name;Picked(byte[] bytes,String name){this.bytes=bytes;this.name=name;}}
    static final int MAX_ENTRIES=200,ENTRY=4*1024*1024,TOTAL=16*1024*1024;
    static final Pattern HAS_EPISODE=Pattern.compile("(?i)s\\d{1,2}[ ._-]*e\\d|\\d{1,2}x\\d|(?:^|[ ._-])(?:e|ep|episode)[ ._-]*\\d");
    static final Pattern FULL=Pattern.compile("(?i)(?:s(\\d{1,2})[ ._-]*e(\\d{1,3})|(?<![\\da-z])(\\d{1,2})x(\\d{1,3}))(?!\\d)");
    static final Pattern E_ONLY=Pattern.compile("(?i)(?<![a-z])e(\\d{1,3})(?!\\d)");
    static final Pattern RANGE=Pattern.compile("(?i)(?:(?<![a-z])e\\d{1,3}|(?<!\\d)\\d{1,2}x\\d{1,3})-\\d");
    static final Pattern SEASONS=Pattern.compile("(?i)(?:^|[^a-z0-9])(?:s|season[ ._-]*)(\\d{1,2})(?!\\d)");
    static final Pattern EPISODES=Pattern.compile("(?i)(?:^|[ ._\\-])(?:e|ep|episode)[ ._-]*(\\d{1,3})(?!\\d)");
    static boolean hasEpisodeName(String name){return HAS_EPISODE.matcher(name).find();}
    static boolean episodeMatches(String name,int season,int episode){
        Matcher single=E_ONLY.matcher(name);while(single.find())if(Integer.parseInt(single.group(1))!=episode)return false;
        if(RANGE.matcher(name).find())return false;
        Matcher full=FULL.matcher(name);boolean any=false;
        while(full.find()){any=true;boolean a=full.group(1)!=null;int s=Integer.parseInt(a?full.group(1):full.group(3)),e=Integer.parseInt(a?full.group(2):full.group(4));if(s!=season||e!=episode)return false;}
        if(any)return true;
        Matcher seasons=SEASONS.matcher(name);while(seasons.find())if(Integer.parseInt(seasons.group(1))!=season)return false;
        Matcher eps=EPISODES.matcher(name);int count=0;while(eps.find()){count++;if(Integer.parseInt(eps.group(1))!=episode)return false;}return count>0;
    }
    static boolean textSubtitle(String name){String n=name.toLowerCase(Locale.ROOT);return n.endsWith(".srt")||n.endsWith(".ass")||n.endsWith(".ssa")||n.endsWith(".vtt");}
    static Picked pick(byte[] bytes,boolean movie,int season,int episode,String release)throws IOException{
        int entries=0;long total=0;byte[] bestBytes=null;String bestName=null;int bestScore=-1;byte[] buffer=new byte[8192];
        try(ZipInputStream zip=new ZipInputStream(new ByteArrayInputStream(bytes))){
            ZipEntry entry;
            while((entry=zip.getNextEntry())!=null){
                if(Thread.currentThread().isInterrupted())throw new InterruptedIOException();
                if(++entries>MAX_ENTRIES)throw new IOException("Subtitle archive too large.");
                ByteArrayOutputStream out=new ByteArrayOutputStream();int n;
                while((n=zip.read(buffer))!=-1){total+=n;if(out.size()+(long)n>ENTRY||total>TOTAL)throw new IOException("Subtitle archive too large.");out.write(buffer,0,n);}
                if(entry.isDirectory())continue;
                String full=entry.getName().replace('\\','/');String name=full.substring(full.lastIndexOf('/')+1);
                if(!textSubtitle(name)||!movie&&!episodeMatches(name,season,episode))continue;
                int score=SubtitleClient.releaseScore(name,release);
                if(score>bestScore){bestScore=score;bestBytes=out.toByteArray();bestName=name;}
            }
        }
        if(bestBytes==null)throw new IOException("No matching subtitle in archive.");
        return new Picked(bestBytes,bestName);
    }
}
