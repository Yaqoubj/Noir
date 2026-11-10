package app.noir.mobile;

/**
 * Bit-exact Java port of WebRTC's voice activity detector, for 16 kHz audio in 10 ms frames at
 * aggressiveness 3. Same results as the PC tool. Copyright (c) 2011 The WebRTC project authors,
 * BSD-style license: see THIRD-PARTY-NOTICES.txt. Casts to short reproduce C's int16_t storage.
 */
final class WebRtcVad {
    static final int FRAME_SAMPLES=160;
    private static final int CHANNELS=6,GAUSSIANS=2,TABLE=12,MIN_ENERGY=10;
    private static final short[] WEIGHT={6,8,10,12,14,16},MIN_DIFF={544,544,576,576,576,576},MAX_SPEECH={11392,11392,11520,11520,11520,11520},MIN_MEAN={640,768},MAX_NOISE={9216,9088,8960,8832,8704,8576};
    private static final short[] NOISE_WEIGHTS={34,62,72,66,53,25,94,66,56,62,75,103},SPEECH_WEIGHTS={48,82,45,87,50,47,80,46,83,41,78,81};
    private static final short[] NOISE_MEANS={6738,4892,7065,6715,6771,3369,7646,3863,7820,7266,5020,4362},SPEECH_MEANS={8306,10085,10078,11823,11843,6309,9473,9571,10879,7581,8180,7483};
    private static final short[] NOISE_STDS={378,1064,493,582,688,593,474,697,475,688,421,455},SPEECH_STDS={555,505,567,524,585,1231,509,828,492,1540,1079,850};
    private static final short NOISE_UPDATE=655,SPEECH_UPDATE=6554,BACK_ETA=154,MAX_SPEECH_FRAMES=6,MIN_STD=384;
    private static final short OVERHANG1=6,OVERHANG2=9,INDIVIDUAL=94,TOTAL=1100;
    private static final short[] ALLPASS_Q15={20972,5571},OFFSET={368,368,272,176,176,176},HP_ZERO={6631,-13262,6631},HP_POLE={16384,-7756,5620};
    private static final short ALL13_0=5243,ALL13_1=1392;

    private final short[] noiseMeans=NOISE_MEANS.clone(),speechMeans=SPEECH_MEANS.clone(),noiseStds=NOISE_STDS.clone(),speechStds=SPEECH_STDS.clone();
    private final int[] down=new int[2];
    private final short[] age=new short[96],low=new short[96],median=new short[CHANNELS],upper=new short[5],lower=new short[5],hp=new short[4];
    private int frameCounter;private short overHang,speechRun;
    private final short[] nb=new short[80],hp120=new short[40],lp120=new short[40],hp60=new short[20],lp60=new short[20],features=new short[CHANNELS];
    private final short[] deltaN=new short[TABLE],deltaS=new short[TABLE],ngp=new short[TABLE],sgp=new short[TABLE];
    private short totalEnergy;

    WebRtcVad(){java.util.Arrays.fill(low,(short)10000);java.util.Arrays.fill(median,(short)1600);}

    /** True when the 160 samples starting at offset are speech. */
    boolean isSpeech(short[] pcm,int offset){
        downsample(pcm,offset);calculateFeatures();return gmm()>0;
    }
    private void downsample(short[] in,int at){
        int t1=down[0],t2=down[1];
        for(int n=0;n<80;n++){
            short s=in[at++];short a=(short)((t1>>1)+((ALL13_0*s)>>14));short out=a;t1=s-((ALL13_0*a)>>12);
            s=in[at++];short b=(short)((t2>>1)+((ALL13_1*s)>>14));nb[n]=(short)(out+b);t2=s-((ALL13_1*b)>>12);
        }
        down[0]=t1;down[1]=t2;
    }
    private static short allPass(short[] in,int start,int count,short coef,short state,short[] out){
        int s32=state*65536;short t=0;int idx=start;
        for(int i=0;i<count;i++){int t32=s32+coef*in[idx];t=(short)(t32>>16);out[i]=t;s32=in[idx]*16384-coef*t;s32*=2;idx+=2;}
        return (short)(s32>>16);
    }
    private void split(short[] in,int len,int band,short[] high,short[] lowOut){
        int half=len>>1;upper[band]=allPass(in,0,half,ALLPASS_Q15[0],upper[band],high);lower[band]=allPass(in,1,half,ALLPASS_Q15[1],lower[band],lowOut);
        for(int i=0;i<half;i++){short t=high[i];high[i]=(short)(high[i]-lowOut[i]);lowOut[i]=(short)(lowOut[i]+t);}
    }
    private static int norm32(int a){return a==0?0:Integer.numberOfLeadingZeros(a<0?~a:a)-1;}
    private short logEnergy(short[] in,int len,short off){
        short nbits=(short)(32-Integer.numberOfLeadingZeros(len));short smax=-1;
        for(int i=0;i<len;i++){short sabs=(short)(in[i]>0?in[i]:-in[i]);if(sabs>smax)smax=sabs;}
        int t=norm32(smax*smax);int scaling=smax==0?0:(t>nbits?0:nbits-t);
        int energy=0;for(int i=0;i<len;i++)energy+=(in[i]*in[i])>>scaling;
        int rshifts=scaling;
        if(energy==0)return off;
        int norm=17-Integer.numberOfLeadingZeros(energy);short log2=14336;rshifts+=norm;
        if(norm<0)energy<<=-norm;else energy>>>=norm;
        log2=(short)(log2+(short)((energy&0x3FFF)>>>4));
        short result=(short)(((24660*log2)>>19)+((rshifts*24660)>>9));if(result<0)result=0;
        result=(short)(result+off);
        if(totalEnergy<=MIN_ENERGY){if(rshifts>=0)totalEnergy=(short)(totalEnergy+MIN_ENERGY+1);else totalEnergy=(short)(totalEnergy+(short)(energy>>>-rshifts));}
        return result;
    }
    private void calculateFeatures(){
        totalEnergy=0;
        split(nb,80,0,hp120,lp120);
        split(hp120,40,1,hp60,lp60);features[5]=logEnergy(hp60,20,OFFSET[5]);features[4]=logEnergy(lp60,20,OFFSET[4]);
        split(lp120,40,2,hp60,lp60);features[3]=logEnergy(hp60,20,OFFSET[3]);
        split(lp60,20,3,hp120,lp120);features[2]=logEnergy(hp120,10,OFFSET[2]);
        split(lp120,10,4,hp60,lp60);features[1]=logEnergy(hp60,5,OFFSET[1]);
        for(int i=0;i<5;i++){
            int t=HP_ZERO[0]*lp60[i]+HP_ZERO[1]*hp[0]+HP_ZERO[2]*hp[1];hp[1]=hp[0];hp[0]=lp60[i];
            t-=HP_POLE[1]*hp[2];t-=HP_POLE[2]*hp[3];hp[3]=hp[2];hp[2]=(short)(t>>14);hp120[i]=hp[2];
        }
        features[0]=logEnergy(hp120,5,OFFSET[0]);
    }
    private static int div(int num,short den){return den!=0?num/den:0x7FFFFFFF;}
    private int gaussian(short input,short mean,short std,int deltaIndex,short[] deltas){
        int t32=131072+(std>>1);short inv=(short)div(t32,std);short t16=(short)(inv>>2);short inv2=(short)((t16*t16)>>2);
        t16=(short)(input<<3);t16=(short)(t16-mean);short delta=(short)((inv2*t16)>>10);deltas[deltaIndex]=delta;
        t32=(delta*t16)>>9;short exp=0;
        if(t32<22005){t16=(short)((5909*t32)>>12);t16=(short)-t16;exp=(short)(0x0400|(t16&0x03FF));t16=(short)(t16^0xFFFF);t16=(short)(t16>>10);t16=(short)(t16+1);exp=(short)(exp>>t16);}
        return inv*exp;
    }
    private short findMinimum(short feature,int channel){
        int off=channel<<4;short current=1600,alpha=0;
        for(int i=0;i<16;i++){
            if(age[off+i]!=100)age[off+i]++;
            else{for(int j=i;j<15;j++){low[off+j]=low[off+j+1];age[off+j]=age[off+j+1];}age[off+15]=101;low[off+15]=10000;}
        }
        int position=-1;
        if(feature<low[off+7]){
            if(feature<low[off+3]){
                if(feature<low[off+1])position=feature<low[off]?0:1;
                else position=feature<low[off+2]?2:3;
            }else if(feature<low[off+5])position=feature<low[off+4]?4:5;
            else position=feature<low[off+6]?6:7;
        }else if(feature<low[off+15]){
            if(feature<low[off+11]){
                if(feature<low[off+9])position=feature<low[off+8]?8:9;
                else position=feature<low[off+10]?10:11;
            }else if(feature<low[off+13])position=feature<low[off+12]?12:13;
            else position=feature<low[off+14]?14:15;
        }
        if(position>-1){for(int i=15;i>position;i--){low[off+i]=low[off+i-1];age[off+i]=age[off+i-1];}low[off+position]=feature;age[off+position]=1;}
        if(frameCounter>2)current=low[off+2];else if(frameCounter>0)current=low[off];
        if(frameCounter>0)alpha=current<median[channel]?(short)6553:(short)32439;
        int t=(alpha+1)*median[channel];t+=(32767-alpha)*current;t+=16384;median[channel]=(short)(t>>15);
        return median[channel];
    }
    private static int weighted(short[] data,int channel,int offset,short[] weights){
        int sum=0;for(int k=0;k<GAUSSIANS;k++){int i=channel+k*CHANNELS;data[i]=(short)(data[i]+offset);sum+=data[i]*weights[i];}return sum;
    }
    private short gmm(){
        short flag=0;java.util.Arrays.fill(ngp,(short)0);java.util.Arrays.fill(sgp,(short)0);int[] noiseP=new int[GAUSSIANS],speechP=new int[GAUSSIANS];
        if(totalEnergy>MIN_ENERGY){
            int sum=0;
            for(int ch=0;ch<CHANNELS;ch++){
                int h0Test=0,h1Test=0;
                for(int k=0;k<GAUSSIANS;k++){
                    int g=ch+k*CHANNELS;
                    noiseP[k]=NOISE_WEIGHTS[g]*gaussian(features[ch],noiseMeans[g],noiseStds[g],g,deltaN);h0Test+=noiseP[k];
                    speechP[k]=SPEECH_WEIGHTS[g]*gaussian(features[ch],speechMeans[g],speechStds[g],g,deltaS);h1Test+=speechP[k];
                }
                short s0=h0Test==0?31:(short)norm32(h0Test),s1=h1Test==0?31:(short)norm32(h1Test);short llr=(short)(s0-s1);
                sum+=llr*WEIGHT[ch];if(llr*4>INDIVIDUAL)flag=1;
                short h0=(short)(h0Test>>12);
                if(h0>0){int t=(noiseP[0]&0xFFFFF000)<<2;ngp[ch]=(short)div(t,h0);ngp[ch+CHANNELS]=(short)(16384-ngp[ch]);}else ngp[ch]=16384;
                short h1=(short)(h1Test>>12);
                if(h1>0){int t=(speechP[0]&0xFFFFF000)<<2;sgp[ch]=(short)div(t,h1);sgp[ch+CHANNELS]=(short)(16384-sgp[ch]);}
            }
            flag|=(sum>=TOTAL?1:0);
            short maxspe=12800;
            for(int ch=0;ch<CHANNELS;ch++){
                short fmin=findMinimum(features[ch],ch);
                int noiseGlobal=weighted(noiseMeans,ch,0,NOISE_WEIGHTS);short t1=(short)(noiseGlobal>>6);
                for(int k=0;k<GAUSSIANS;k++){
                    int g=ch+k*CHANNELS;short nmk=noiseMeans[g],smk=speechMeans[g],nsk=noiseStds[g],ssk=speechStds[g];short nmk2=nmk;short delt;
                    if(flag==0){delt=(short)((ngp[g]*deltaN[g])>>11);nmk2=(short)(nmk+(short)((delt*NOISE_UPDATE)>>22));}
                    short ndelt=(short)((fmin<<4)-t1);short nmk3=(short)(nmk2+(short)((ndelt*BACK_ETA)>>9));
                    short t=(short)((k+5)<<7);if(nmk3<t)nmk3=t;
                    t=(short)((72+k-ch)<<7);if(nmk3>t)nmk3=t;
                    noiseMeans[g]=nmk3;
                    if(flag!=0){
                        delt=(short)((sgp[g]*deltaS[g])>>11);t=(short)((delt*SPEECH_UPDATE)>>21);short smk2=(short)(smk+((t+1)>>1));
                        short maxmu=(short)(maxspe+640);if(smk2<MIN_MEAN[k])smk2=MIN_MEAN[k];if(smk2>maxmu)smk2=maxmu;speechMeans[g]=smk2;
                        t=(short)((smk+4)>>3);t=(short)(features[ch]-t);
                        int a=(deltaS[g]*t)>>3;int b=a-4096;t=(short)(sgp[g]>>2);a=t*b;b=a>>4;
                        if(b>0)t=(short)div(b,(short)(ssk*10));else{t=(short)div(-b,(short)(ssk*10));t=(short)-t;}
                        t=(short)(t+128);ssk=(short)(ssk+(t>>8));if(ssk<MIN_STD)ssk=MIN_STD;speechStds[g]=ssk;
                    }else{
                        t=(short)(features[ch]-(nmk>>3));int a=(deltaN[g]*t)>>3;a-=4096;t=(short)((ngp[g]+2)>>2);int b=t*a;a=b>>14;
                        if(a>0)t=(short)div(a,nsk);else{t=(short)div(-a,nsk);t=(short)-t;}
                        t=(short)(t+32);nsk=(short)(nsk+(t>>6));if(nsk<MIN_STD)nsk=MIN_STD;noiseStds[g]=nsk;
                    }
                }
                noiseGlobal=weighted(noiseMeans,ch,0,NOISE_WEIGHTS);int speechGlobal=weighted(speechMeans,ch,0,SPEECH_WEIGHTS);
                short diff=(short)((short)(speechGlobal>>9)-(short)(noiseGlobal>>9));
                if(diff<MIN_DIFF[ch]){
                    short t=(short)(MIN_DIFF[ch]-diff);short t1b=(short)((13*t)>>2),t2b=(short)((3*t)>>2);
                    speechGlobal=weighted(speechMeans,ch,t1b,SPEECH_WEIGHTS);noiseGlobal=weighted(noiseMeans,ch,-t2b,NOISE_WEIGHTS);
                }
                maxspe=MAX_SPEECH[ch];short t2=(short)(speechGlobal>>7);
                if(t2>maxspe){t2=(short)(t2-maxspe);for(int k=0;k<GAUSSIANS;k++){int i=ch+k*CHANNELS;speechMeans[i]=(short)(speechMeans[i]-t2);}}
                t2=(short)(noiseGlobal>>7);
                if(t2>MAX_NOISE[ch]){t2=(short)(t2-MAX_NOISE[ch]);for(int k=0;k<GAUSSIANS;k++){int i=ch+k*CHANNELS;noiseMeans[i]=(short)(noiseMeans[i]-t2);}}
            }
            frameCounter++;
        }
        if(flag==0){
            if(overHang>0){flag=(short)(2+overHang);overHang--;}
            speechRun=0;
        }else{
            speechRun++;
            if(speechRun>MAX_SPEECH_FRAMES){speechRun=MAX_SPEECH_FRAMES;overHang=OVERHANG2;}else overHang=OVERHANG1;
        }
        return flag;
    }
}
