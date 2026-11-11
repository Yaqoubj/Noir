"""Synthetic 600 s, 1080p24 fixtures, no user media. Uses an 8 s repeating test pattern.
All generated files, ffprobe manifests and logs are local to this project.
"""
import argparse, json, pathlib, shutil, subprocess, struct
ROOT = pathlib.Path(__file__).resolve().parent

def execute(args, log):
    log.write(json.dumps(args) + '\n'); log.flush()
    subprocess.run(args, check=True, stdout=log, stderr=log)

def generate_phone(ff,media,log):
    def stamp(t): return f"00:{t//60:02d}:{t%60:02d},000"
    # Downloaded test cues start at 7+6n; embedded references are exactly 2 s earlier.
    for lang,text in [('en','NOIR SUBTITLE CHECK'),('ar','اختبار ترجمة نوار')]:
        cues=[f"1\n00:00:02,000 --> 00:00:06,000\n{text}\n"]
        cues += [f"{n+2}\n{stamp(5+n*6)} --> {stamp(8+n*6)}\n{text}\n" for n in range(18)]
        (media/(lang+'.srt')).write_text('\n'.join(cues),encoding='utf-8')
    # Phone player fixtures: a real 720p encode (not the looped 1080p ultrafast TV segment, which the emulator's decoder
    # leaves on its first frame after a seek), with a keyframe every second so seeks land on a fresh picture quickly.
    execute([ff,'-y','-f','lavfi','-i','testsrc2=size=1280x720:rate=24','-f','lavfi','-i','sine=frequency=660:sample_rate=48000','-t','480','-map','0:v','-map','1:a','-c:v','libx264','-preset','veryfast','-crf','23','-pix_fmt','yuv420p','-g','24','-c:a','aac','-b:a','96k','-movflags','+faststart',str(media/'Bright pattern fixture.mp4')],log)
    execute([ff,'-y','-i',str(media/'Bright pattern fixture.mp4'),'-i',str(media/'en.srt'),'-i',str(media/'ar.srt'),'-t','240','-map','0:v','-map','0:a','-map','1:0','-map','2:0','-c','copy','-c:s','srt','-metadata:s:s:0','language=eng','-metadata:s:s:0','title=English','-metadata:s:s:1','language=ara','-metadata:s:s:1','title=Arabic','-disposition:s:0','default',str(media/'Subtitle surface fixture.mkv')],log)

def main():
    p=argparse.ArgumentParser(); p.add_argument('out_dir',type=pathlib.Path); p.add_argument('--ffmpeg'); a=p.parse_args()
    ff=a.ffmpeg or shutil.which('ffmpeg')
    if not ff:
        candidate=ROOT.parents[1]/'outputs/Noir/Tools/subtitle-sync/ffmpeg/ffmpeg.exe'
        if candidate.exists(): ff=str(candidate)
    if not ff: raise SystemExit('ffmpeg unavailable: specify --ffmpeg')
    probe=str(pathlib.Path(ff).with_name('ffprobe.exe' if ff.endswith('.exe') else 'ffprobe'))
    media=a.out_dir.resolve(); media.mkdir(parents=True,exist_ok=True)
    if not any(media.is_relative_to(ROOT.parents[1]/folder) for folder in ('work','NoirValidation')): raise SystemExit('Output must be under ignored work/ or NoirValidation/')
    (media/'test.srt').write_text('1\n00:00:00,200 --> 00:09:59,000\nNOIR embedded SRT test\n',encoding='utf-8')
    (media/'test.ass').write_text('''[Script Info]
ScriptType: v4.00+
PlayResX: 1920
PlayResY: 1080
[V4+ Styles]
Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
Style: Default,Arial,64,&H0000FFFF,&H000000FF,&H00000000,&H00000000,-1,0,0,0,100,100,0,0,1,3,1,2,20,20,40,1
[Events]
Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
Dialogue: 0,0:00:00.20,0:09:59.00,Default,,0,0,0,,{\\i1}NOIR embedded ASS test{\\i0} {\\c&H00FF00&}GREEN
''',encoding='utf-8')
    with (media/'generate.log').open('w',encoding='utf-8') as log:
        for name,codec,pixel,bitrate in [('avc','libx264','yuv420p','4000k'),('hevc10','libx265','yuv420p10le','2500k')]:
            segment=media/(name+'-segment.mkv')
            cmd=[ff,'-y','-f','lavfi','-i','testsrc2=size=1920x1080:rate=24','-t','8','-an','-c:v',codec,'-preset','ultrafast','-pix_fmt',pixel,'-b:v',bitrate,'-g','48','-threads','4']
            if codec=='libx265': cmd+=['-x265-params','pools=4:frame-threads=2:keyint=48:min-keyint=48:scenecut=0']
            else: cmd+=['-sc_threshold','0']
            execute(cmd+[str(segment)],log)
        for filename,segment,acodec,sub in [('i-avc-aac-srt.mkv','avc','aac','srt'),('ii-hevc10-eac3-ass.mkv','hevc10','eac3','ass'),('iii-avc-aac-tail.mp4','avc','aac',None)]:
            cmd=[ff,'-y','-stream_loop','-1','-i',str(media/(segment+'-segment.mkv')),'-f','lavfi','-i','sine=frequency=440:sample_rate=48000']
            if sub: cmd+=['-i',str(media/('test.'+sub))]
            cmd+=['-t','600','-map','0:v:0','-map','1:a:0']
            if sub: cmd+=['-map','2:0','-c:s',sub,'-metadata:s:s:0','language=eng','-disposition:s:0','default']
            cmd+=['-c:v','copy','-c:a',acodec,'-b:a','192k','-ac','2',str(media/filename)]
            execute(cmd,log)
        generate_phone(ff,media,log)
        # Additional short audio fixtures isolate codec support from HEVC video support.
        for codec in ['ac3','dca']:
            cmd=[ff,'-y','-i',str(media/'i-avc-aac-srt.mkv'),'-t','12','-map','0:v','-map','0:a','-c:v','copy','-c:a',codec,'-b:a','384k']
            if codec=='dca': cmd+=['-strict','-2']
            execute(cmd+[str(media/('audio-'+codec+'.mkv'))],log)
    manifests={}
    for f in media.glob('*.m*'):
        manifests[f.name]=json.loads(subprocess.check_output([probe,'-v','error','-show_format','-show_streams','-of','json',str(f)],text=True))
    mp4=media/'iii-avc-aac-tail.mp4'; atoms=[]
    with mp4.open('rb') as f:
        while f.tell()<mp4.stat().st_size:
            offset=f.tell(); header=f.read(8)
            if len(header)<8: break
            size,kind=struct.unpack('>I4s',header)
            if size==1: size=struct.unpack('>Q',f.read(8))[0]
            if size==0: size=mp4.stat().st_size-offset
            atoms.append({'offset':offset,'size':size,'type':kind.decode('ascii')}); f.seek(offset+size)
    (media/'manifest.json').write_text(json.dumps({'files':manifests,'mp4_atoms':atoms,'total_bytes':sum(f.stat().st_size for f in media.iterdir() if f.is_file())},indent=2),encoding='utf-8')
    assert next(x['offset'] for x in atoms if x['type']=='moov')>next(x['offset'] for x in atoms if x['type']=='mdat')
    import hashlib
    (media/'recipe.sha256').write_text(hashlib.sha256(pathlib.Path(__file__).read_bytes()).hexdigest(),encoding='ascii')
    for f in sorted(media.iterdir()):
        if f.is_file(): print(f.name, f.stat().st_size, 'bytes')
    print('Generated',sum(f.stat().st_size for f in media.iterdir() if f.is_file()),'bytes')
if __name__=='__main__': main()
