package app.noir.mobile;

import java.util.*;

/** Port of Noir.Tools/subtitle-sync/sampled.py: must give the same decision as the PC tool, checked against shared vectors. */
final class SampledAlignment {
    static final class Result {boolean accepted;double offset,scale=1;String reason="too-little-speech";double[][] fitted;}
    static final class Sample {final double start;final boolean[] speech;Sample(double start,boolean[] speech){this.start=start;this.speech=speech;}}
    private static final int RATE=100,SHIFT_RANGE=3000,COUNT=6001;
    private static final double MIN_JOINT_Z=4.5,MAX_RIVAL=.85,MIN_RANK=.80,MAX_MOVE=.35,CONFLICT_Z=6,CONFLICT_RIVAL=.75;
    static final double[] FPS_RATIOS={1,25/23.976,23.976/25,24/23.976,23.976/24,25d/24,24d/25};

    static double[][] planWindows(double duration){
        if(Double.isNaN(duration)||Double.isInfinite(duration)||duration<720)return new double[0][];
        double margin=Math.max(90,.05*duration);int count=duration>=3600?8:6;double length=Math.min(150,Math.max(60,.12*duration/count)),budget=Math.max(.15*duration,360);
        while(count>4&&count*length>budget)count--;
        double span=duration-2*margin;if(count*length>Math.min(budget,span))return new double[0][];
        double[][] out=new double[count][];for(int i=0;i<count;i++)out[i]=new double[]{margin+i*(span-length)/(count-1),length};return out;
    }
    static double[][] planExtraWindows(double duration){
        double[][] p=planWindows(duration);List<double[]> out=new ArrayList<>();
        for(int i=0;i+1<p.length;i++){double a=p[i][0],length=p[i][1],b=p[i+1][0],gap=b-(a+length);if(gap<20)continue;double size=Math.min(length,gap),start=Math.max(a+length,Math.min((a+b)/2,b-size));out.add(new double[]{start,size});}
        return out.toArray(new double[0][]);
    }
    /** One 100 Hz mask per chunk of 16 kHz mono samples, after two passes that let the detector adapt. */
    static List<boolean[]> speechMasks(List<short[]> chunks){return speechMasks(chunks,2);}
    static List<boolean[]> speechMasks(List<short[]> chunks,int passes){
        WebRtcVad vad=new WebRtcVad();
        for(int p=0;p<passes;p++)for(short[] c:chunks)for(int i=0;i+WebRtcVad.FRAME_SAMPLES<=c.length;i+=WebRtcVad.FRAME_SAMPLES)vad.isSpeech(c,i);
        List<boolean[]> out=new ArrayList<>();
        for(short[] c:chunks){boolean[] mask=new boolean[c.length/WebRtcVad.FRAME_SAMPLES];for(int f=0;f<mask.length;f++)mask[f]=vad.isSpeech(c,f*WebRtcVad.FRAME_SAMPLES);out.add(mask);}
        return out;
    }
    private static boolean useful(boolean[] s){if(s.length==0)return false;int n=0;for(boolean b:s)if(b)n++;double m=n/(double)s.length;return n>=1000&&m>.03&&m<.85;}
    private static long[][] intervals(double[][] cues){
        long[][] raw=new long[cues.length][];for(int i=0;i<cues.length;i++)raw[i]=new long[]{(long)Math.floor(cues[i][0]/10),(long)Math.floor(cues[i][1]/10)};
        Arrays.sort(raw,(x,y)->x[0]!=y[0]?Long.compare(x[0],y[0]):Long.compare(x[1],y[1]));
        List<long[]> out=new ArrayList<>();for(long[] r:raw){if(r[1]<=r[0])continue;if(!out.isEmpty()&&r[0]<=out.get(out.size()-1)[1]){long[] last=out.get(out.size()-1);last[1]=Math.max(r[1],last[1]);}else out.add(new long[]{r[0],r[1]});}
        return out.toArray(new long[0][]);
    }
    private static double[] curve(double start,boolean[] speech,double[][] cues,double seed){
        int n=speech.length;List<double[]> near=new ArrayList<>();
        for(double[] c:cues){double a=c[0]*seed-start*1000,b=c[1]*seed-start*1000;if(b>-30000&&a<n*10+30000)near.add(new double[]{a,b});}
        if(near.size()<8)return null;
        long[][] iv=intervals(near.toArray(new double[0][]));long[] prefix=new long[n+1];for(int i=0;i<n;i++)prefix[i+1]=prefix[i]+(speech[i]?1:0);
        double[] phi=new double[COUNT];double heard=prefix[n];
        for(int s=0;s<COUNT;s++){
            long shift=s-SHIFT_RANGE;double shared=0,active=0;
            for(long[] v:iv){int a=(int)Math.max(0,Math.min(n,v[0]+shift)),b=(int)Math.max(0,Math.min(n,v[1]+shift));shared+=prefix[b]-prefix[a];active+=b-a;}
            double denominator=active*(n-active)*heard*(n-heard);
            if(active>=.05*n&&active<=.95*n&&denominator>0)phi[s]=(n*shared-active*heard)/Math.sqrt(denominator);
        }
        return phi;
    }
    private static double[] joint(double[][] curves,int usable,int skip){
        double[] total=new double[COUNT];for(int i=0;i<curves.length;i++)if(curves[i]!=null&&i!=skip)for(int s=0;s<COUNT;s++)total[s]+=curves[i][s];
        double d=Math.max(1,usable-(skip<0?0:1));for(int s=0;s<COUNT;s++)total[s]/=d;return total;
    }
    private static int argmax(double[] v){int best=0;for(int i=1;i<v.length;i++)if(v[i]>v[best])best=i;return best;}
    private static double median(double[] v){double[] c=v.clone();Arrays.sort(c);int m=c.length/2;return c.length%2==1?c[m]:(c[m-1]+c[m])/2;}
    /** index, robust z, rival share. */
    private static double[] peak(double[] v){
        int best=argmax(v);double med=median(v);double[] dev=new double[v.length];for(int i=0;i<v.length;i++)dev[i]=Math.abs(v[i]-med);
        double sigma=Math.max(1e-4,1.4826*median(dev)),far=Double.NEGATIVE_INFINITY;for(int i=0;i<v.length;i++)if(Math.abs(i-best)>100&&v[i]>far)far=v[i];
        double height=Math.max(1e-9,v[best]-med);return new double[]{best,(v[best]-med)/sigma,(far-med)/height};
    }
    private static double[][] counts(List<Sample> samples,double[][] cues){
        double[][] out=new double[samples.size()][3];
        for(int i=0;i<samples.size();i++){
            Sample s=samples.get(i);int n=s.speech.length;boolean[] active=new boolean[n];double[][] moved=new double[cues.length][];for(int c=0;c<cues.length;c++)moved[c]=new double[]{cues[c][0]-s.start*1000,cues[c][1]-s.start*1000};
            for(long[] v:intervals(moved))for(long k=Math.max(0,v[0]);k<Math.max(0,Math.min(n,v[1]));k++)active[(int)k]=true;
            int shared=0,act=0,speech=0;for(int k=0;k<n;k++){if(active[k])act++;if(s.speech[k])speech++;if(active[k]&&s.speech[k])shared++;}
            out[i]=new double[]{shared,act,speech};
        }
        return out;
    }
    private static double[] metrics(double[] c){return new double[]{c[0]/Math.max(1,c[1]),c[0]/Math.max(1,c[2]),2*c[0]/Math.max(1,c[1]+c[2])};}
    private static double[] sum(double[][] c){double[] t=new double[3];for(double[] r:c)for(int i=0;i<3;i++)t[i]+=r[i];return t;}
    private static double[][] copy(double[][] c){double[][] o=new double[c.length][];for(int i=0;i<c.length;i++)o[i]=c[i].clone();return o;}
    private static Result reject(String reason,double[][] cues){Result r=new Result();r.reason=reason;r.fitted=copy(cues);return r;}

    static Result align(List<Sample> given,double[][] cues){
        boolean bad=cues.length==0;
        for(int i=0;i<cues.length&&!bad;i++){if(Double.isNaN(cues[i][0])||Double.isInfinite(cues[i][0])||Double.isNaN(cues[i][1])||Double.isInfinite(cues[i][1])||cues[i][1]<=cues[i][0])bad=true;if(i>0&&(cues[i][0]<cues[i-1][0]||cues[i][1]<cues[i-1][1]))bad=true;}
        if(bad)return reject("invalid-model",cues);
        for(Sample s:given)if(Double.isNaN(s.start)||Double.isInfinite(s.start)||s.start<0)return reject("invalid-model",cues);
        List<Sample> samples=new ArrayList<>(given);samples.sort(Comparator.comparingDouble(x->x.start));
        for(int i=0;i+1<samples.size();i++)if(samples.get(i).start+samples.get(i).speech.length/(double)RATE>samples.get(i+1).start+1e-9)return reject("invalid-model",cues);
        boolean[] ok=new boolean[samples.size()];int usable=0;for(int i=0;i<ok.length;i++){ok[i]=useful(samples.get(i).speech);if(ok[i])usable++;}
        int required=Math.max(4,(int)Math.ceil(.75*usable));if(usable<required)return reject("too-little-speech",cues);
        double bestValue=Double.NEGATIVE_INFINITY,seed=0;double[][] curves=null;double[] joined=null;
        for(double candidate:FPS_RATIOS){
            double[][] c=new double[samples.size()][];for(int i=0;i<c.length;i++)c[i]=ok[i]?curve(samples.get(i).start,samples.get(i).speech,cues,candidate):null;
            double[] j=joint(c,usable,-1);int at=argmax(j);
            if(Math.abs(at-SHIFT_RANGE)<2990&&j[at]>bestValue){bestValue=j[at];seed=candidate;curves=c;joined=j;}
        }
        if(curves==null)return reject("no-agreement",cues);
        double[] pk=peak(joined);int at=(int)pk[0];double offset=(at-SHIFT_RANGE)/(double)RATE;
        List<Double> agreed=new ArrayList<>();boolean conflict=false,drifted=false;
        for(int i=0;i<curves.length;i++){
            double[] curve=curves[i];if(curve==null)continue;
            int below=0;for(double v:curve)if(v<curve[at])below++;double rank=below/(double)COUNT;double[] own=peak(curve);boolean clear=own[1]>=CONFLICT_Z&&own[2]<=CONFLICT_RIVAL;
            if(curve[at]>0&&rank>=MIN_RANK){agreed.add((samples.get(i).start+samples.get(i).speech.length/(double)(2*RATE)-offset)/seed);if(clear&&Math.abs((int)own[0]-at)/(double)RATE>MAX_MOVE)drifted=true;}
            else if(clear&&Math.abs((int)own[0]-at)>100)conflict=true;
        }
        if(pk[1]<MIN_JOINT_Z||pk[2]>MAX_RIVAL||agreed.size()<required)return reject("no-agreement",cues);
        double scale=seed;if(!(Math.abs(offset)<=30&&Math.abs(scale-1)<=.05))return reject("invalid-model",cues);
        double aMin=Collections.min(agreed),aMax=Collections.max(agreed),cMin=Double.POSITIVE_INFINITY,cMax=Double.NEGATIVE_INFINITY;for(double[] c:cues)for(double v:c){cMin=Math.min(cMin,v/1000);cMax=Math.max(cMax,v/1000);}
        if(aMax-aMin<.70*(cMax-cMin))return reject("poor-coverage",cues);
        if(drifted)return reject("unstable",cues);
        for(int i=0;i<curves.length;i++)if(curves[i]!=null&&Math.abs(argmax(joint(curves,usable,i))-at)/(double)RATE>MAX_MOVE)return reject("unstable",cues);
        if(conflict)return reject("conflicting-sample",cues);
        double[][] fitted=new double[cues.length][];double maxMove=0;
        for(int i=0;i<cues.length;i++){fitted[i]=new double[]{Math.rint(cues[i][0]*scale+offset*1000),Math.rint(cues[i][1]*scale+offset*1000)};maxMove=Math.max(maxMove,Math.max(Math.abs(fitted[i][0]-cues[i][0]),Math.abs(fitted[i][1]-cues[i][1])));}
        for(int i=0;i<fitted.length;i++){if(fitted[i][0]<0||fitted[i][1]<0||fitted[i][1]<=fitted[i][0])return reject("invalid-model",cues);if(i>0&&(fitted[i][0]<fitted[i-1][0]||fitted[i][1]<fitted[i-1][1]))return reject("invalid-model",cues);}
        if(Math.abs(offset)<.25&&Math.abs(scale-1)<.0005&&maxMove<250)return reject("already-aligned",cues);
        double[][] before=counts(samples,cues),after=counts(samples,fitted);double[] m0=metrics(sum(before)),m1=metrics(sum(after));
        for(int i=0;i<before.length;i++)if(ok[i]&&metrics(after[i])[2]+.02+1e-9<metrics(before[i])[2])return reject("local-regression",cues);
        if(m1[2]-m0[2]<.08||m1[0]-m0[0]<.03||m1[0]<.30||m1[1]<.50)return reject("no-improvement",cues);
        Result r=new Result();r.accepted=true;r.offset=offset;r.scale=scale;r.reason="applied";r.fitted=fitted;return r;
    }
}
