from PIL import Image, ImageDraw
from pathlib import Path
out=Path('Noir/Assets')
out.mkdir(exist_ok=True)
im=Image.new('RGBA',(256,256),(14,14,16,255))
d=ImageDraw.Draw(im)
d.rounded_rectangle((18,18,238,238),radius=48,fill=(28,26,24,255))
d.line([(72,187),(72,70),(180,187),(180,70)],fill=(228,192,132,255),width=24)
im.save(out/'noir.ico',format='ICO',sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])
im.save(out/'noir.png')
