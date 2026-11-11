"""Validate generated inputs without an emulator or any personal media."""
import json, pathlib, shutil, subprocess, sys
m=pathlib.Path(sys.argv[1]); ff=shutil.which('ffmpeg'); probe=shutil.which('ffprobe')
def info(name):
    return json.loads(subprocess.check_output([probe,'-v','error','-show_format','-show_streams','-of','json',str(m/name)]))
for name,codec,audio,duration in [('i-avc-aac-srt.mkv','h264','aac',600),('ii-hevc10-eac3-ass.mkv','hevc','eac3',600),('iii-avc-aac-tail.mp4','h264','aac',600),('Bright pattern fixture.mp4','h264','aac',480),('Subtitle surface fixture.mkv','h264','aac',240)]:
    d=info(name); streams=d['streams']
    assert next(s for s in streams if s['codec_type']=='video')['codec_name']==codec
    assert next(s for s in streams if s['codec_type']=='audio')['codec_name']==audio
    assert abs(float(d['format']['duration'])-duration)<1
    print('PASS:',name,(m/name).stat().st_size,'bytes')
d=info('Subtitle surface fixture.mkv'); subs=[s for s in d['streams'] if s['codec_type']=='subtitle']
assert [s['tags']['language'] for s in subs]==['eng','ara']
for i in range(2):
    raw=subprocess.check_output([ff,'-v','error','-i',str(m/'Subtitle surface fixture.mkv'),'-map',f'0:s:{i}','-f','srt','-']).decode('utf-8')
    assert '00:00:02,021 --> 00:00:06,021' in raw or '00:00:02,000 --> 00:00:06,000' in raw
    # AAC mux timestamp offset can be 21 ms; compare the reference intervals exactly relative to cue one.
    import re
    times=re.findall(r'(\d\d):(\d\d):(\d\d),(\d\d\d) --> (\d\d):(\d\d):(\d\d),(\d\d\d)',raw)
    def ms(v): return ((int(v[0])*60+int(v[1]))*60+int(v[2]))*1000+int(v[3])
    pairs=[(ms(t[:4]),ms(t[4:])) for t in times]; shift=pairs[0][0]-2000
    assert len(pairs)==19 and abs(shift)<=25
    assert pairs[1:]==[(int(5+n*6)*1000+shift,int(8+n*6)*1000+shift) for n in range(18)]
    if i: assert '\u0627\u062e\u062a\u0628\u0627\u0631' in raw
    else: assert raw.count('NOIR SUBTITLE CHECK')==19
assert float(d['format']['duration'])-(pairs[-1][1]/1000)>100
atoms=json.loads((m/'manifest.json').read_text(encoding='utf-8'))['mp4_atoms']
assert next(a['offset'] for a in atoms if a['type']=='moov')>next(a['offset'] for a in atoms if a['type']=='mdat')
print('PASS: English/Arabic visibility cue, all 18 reference timings, >100 s tail, tail MP4')
