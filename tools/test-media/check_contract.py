from pathlib import Path
r=Path(__file__).resolve().parents[2]
p=(r/'Noir.MobileChecks/Program.cs').read_text(encoding='utf-8')
assert 'FixtureMedia.Ensure' in p, 'fixture must generate and register required media unconditionally'
t=r/'Noir.Android/app/src/androidTest/java/app/noir/mobile'
for name in ['IntegrationTest','PlayerResumeTest','ReachPlayerTest']:
    s=(t/(name+'.java')).read_text(encoding='utf-8')
    assert 'Bright pattern fixture' in s, name+' must select deterministic media'
    assert 'waitPosition(p,' in s, name+' must wait for actual seek position'
s=(t/'ReachPlayerTest.java').read_text(encoding='utf-8')
assert 'sendPointerSync' not in s and 'dispatchTouchEvent' in s
s=(t/'TvTest.java').read_text(encoding='utf-8')
assert 'coldHttpStartReportsTiming' in s and 'tvLocalMedia' in s
s=(r/'work/test-tv.ps1').read_text(encoding='utf-8')
assert 'tools/test-media' in s and 'work/tv-speed' not in s
print('PASS: fixture registration, deterministic selections, seek waits, local gestures, TV separation and tracked runner inputs')
