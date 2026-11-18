import json, glob, os, sys
from PIL import Image, ImageDraw, ImageFont
F=ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf',14)
markers = json.load(open(sys.argv[1])) if len(sys.argv)>1 else {}
files=sorted(glob.glob('grips/gun_*.json'))
W,H=560,260; cols=3
sheet=Image.new('RGB',(W*cols,H*((len(files)+cols-1)//cols)),(235,238,242))
for k,f in enumerate(files):
    name=os.path.basename(f)[4:-5]; parts=json.load(open(f))
    allp=[v for p in parts for v in p['V']]
    zs=[v[2] for v in allp]; ys=[v[1] for v in allp]
    z0,z1,y0,y1=min(zs),max(zs),min(ys),max(ys)
    s=min((W-40)/(z1-z0),(H-60)/(y1-y0))
    im=Image.new('RGB',(W,H),(250,250,250)); d=ImageDraw.Draw(im)
    # muzzle (-Z) on the LEFT; screen x = (z - z0)*s ; side view from the gun's right
    P=lambda z,y:(20+(z-z0)*s, H-30-(y-y0)*s)
    for p in parts:
        V=p['V']
        for t in p['tris']:
            d.polygon([P(V[i][2],V[i][1]) for i in t],fill=(120,125,135))
    # origin
    ox,oy=P(0,0); d.line([ox-6,oy,ox+6,oy],fill=(0,0,0)); d.line([ox,oy-6,ox,oy+6],fill=(0,0,0))
    cols_={'RightHandGrip':(220,40,40),'LeftHandGrip':(40,90,230),'Muzzle':(240,160,0),'AimPoint':(30,170,60)}
    for key,c in cols_.items():
        m=markers.get(name,{}).get(key)
        if m: x,y=P(m[2],m[1]); d.ellipse([x-6,y-6,x+6,y+6],outline=c,width=3)
    d.text((8,4),f"{name}  len {z1-z0:.2f} m  (muzzle left)",fill=(0,0,0),font=F)
    sheet.paste(im,((k%cols)*W,(k//cols)*H))
sheet.save(sys.argv[2] if len(sys.argv)>2 else 'grips/sheet.png'); print('ok')
