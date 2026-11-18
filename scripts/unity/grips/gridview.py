import json, glob, os, sys
from PIL import Image, ImageDraw, ImageFont
F=ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf',12)
names=sys.argv[1].split(','); markers=json.load(open(sys.argv[2])) if len(sys.argv)>3 else {}
out=sys.argv[-1]
ims=[]
for name in names:
    parts=json.load(open(f'grips/gun_{name}.json'))
    allp=[v for p in parts for v in p['V']]
    z0,z1=min(v[2] for v in allp),max(v[2] for v in allp); y0,y1=min(v[1] for v in allp),max(v[1] for v in allp)
    s=900/(z1-z0) if z1-z0>0.5 else 2400*(z1-z0)/(z1-z0)/ (1 if z1-z0>0.3 else 1)
    s=min(s, 3000); s= 1100/(z1-z0)
    W=int((z1-z0)*s)+80; H=int((y1-y0)*s)+80
    im=Image.new('RGB',(W,H),(250,250,250)); d=ImageDraw.Draw(im)
    P=lambda z,y:(40+(z-z0)*s, H-40-(y-y0)*s)
    import math
    for zz in [x/100 for x in range(int(math.floor(z0*20))*5, int(z1*100)+6, 5)]:
        x,_=P(zz,0); d.line([x,0,x,H],fill=(215,215,225)); d.text((x+2,H-14),f"{zz:+.2f}",fill=(90,90,90),font=F)
    for yy in [x/100 for x in range(int(math.floor(y0*20))*5, int(y1*100)+6, 5)]:
        _,y=P(0,yy); d.line([0,y,W,y],fill=(215,215,225)); d.text((2,y-12),f"{yy:+.2f}",fill=(90,90,90),font=F)
    for p in parts:
        V=p['V']
        for t in p['tris']: d.polygon([P(V[i][2],V[i][1]) for i in t],fill=(120,125,135))
    cols_={'RightHandGrip':(220,40,40),'LeftHandGrip':(40,90,230),'Muzzle':(240,160,0),'AimPoint':(30,170,60)}
    for key,c in cols_.items():
        m=markers.get(name,{}).get(key)
        if m: x,y=P(m[2],m[1]); d.ellipse([x-8,y-8,x+8,y+8],outline=c,width=4); d.text((x+9,y-8),key[:5],fill=c,font=F)
    d.text((6,4),f"{name}  side view from the gun's right, z horizontal (m), y vertical (m)",fill=(0,0,0),font=F)
    ims.append(im)
W=max(i.width for i in ims); H=sum(i.height for i in ims)
o=Image.new('RGB',(W,H),(255,255,255)); y=0
for i in ims: o.paste(i,(0,y)); y+=i.height
o.save(out)
