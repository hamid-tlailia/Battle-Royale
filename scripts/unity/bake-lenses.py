"""The web game's optics (rim + per-zoom reticle, the green mark apart) drawn once as PNGs
(Resources/Lens/rim<z>.png.bytes, mark<z>.png.bytes), so the game never draws them while you play.
Same drawing as Hud.WebLens. Run: python3 scripts/unity/bake-lenses.py"""
import numpy as np
from PIL import Image
n=1024; u=n/200; c0=(n-1)/2
y,x=np.mgrid[0:n,0:n]; px=(x-c0)/u; py=(y-c0)/u; d=np.hypot(px,py); tx=1/u  # image row 0 = top = the SVG's -y
def seg(ax,ay,bx,by,w):
    abx,aby=bx-ax,by-ay; L=max(1e-4,abx*abx+aby*aby); t=np.clip(((px-ax)*abx+(py-ay)*aby)/L,0,1)
    return np.clip((w/2-np.hypot(px-(ax+abx*t),py-(ay+aby*t)))/tx+.5,0,1)
def dot(cx,cy,r): return np.clip((r-np.hypot(px-cx,py-cy))/tx+.5,0,1)
for z in (2,4,8):
    rimW,edgeW=(10.8,2.3) if z==2 else (8,2) if z==4 else (6,1.2)
    shade=.85 if z==8 else .8 if z==4 else .7; shadeW=24 if z==2 else 30
    R=100; col=np.zeros((n,n,4))
    rim=(d<=R)&(d>R-rimW); edge=(d<=R-rimW)&(d>R-rimW-edgeW); inner=d<=R-rimW-edgeW
    col[rim]=[18/255,19/255,21/255,1]; col[edge]=[46/255,49/255,53/255,1]
    k=np.clip((d-(R-rimW-edgeW-shadeW))/shadeW,0,1); col[inner,3]=(k*k*shade)[inner]
    a=np.zeros((n,n)); m=np.zeros((n,n))
    if z==8:
        for s in [(-100,0,-14,0,1.2),(14,0,100,0,1.2),(0,-100,0,-14,1.2),(0,14,0,100,1.2),(-100,0,-60,0,5),(60,0,100,0,5),(0,60,0,100,5)]: a=np.maximum(a,seg(*s))
        for v in (-40,-30,-20,20,30,40): a=np.maximum(a,dot(v,0,1.6)); a=np.maximum(a,dot(0,v,1.6))
        m=dot(0,0,1.8)
    elif z==4:
        for s in [(-100,0,-8,0,1),(8,0,100,0,1),(0,8,0,100,1)]: a=np.maximum(a,seg(*s))
        for v in (12,20,28,36): a=np.maximum(a,seg(-v/4,v,v/4,v,1))
        m=np.maximum(seg(-5,6,0,0,1.6),seg(0,0,5,6,1.6))
    else:
        a=np.clip((0.7-np.abs(d-22))/tx+.5,0,1)
        for s in [(-22,0,-12,0,1.4),(12,0,22,0,1.4),(0,22,0,12,1.4)]: a=np.maximum(a,seg(*s))
        m=dot(0,0,2.2)
    # (the SVG's y grows downward: py is already flipped, image row 0 = top)
    inside=d<R-rimW; a=a*inside; m=m*inside
    out=col*(1-a[...,None])+np.array([.07,.07,.07,1])*a[...,None]
    Image.fromarray((np.clip(out,0,1)*255).astype(np.uint8),'RGBA').save(f'unity/Assets/LastWar/Resources/Lens/rim{z}.png.bytes',format='PNG',optimize=True)
    mk=np.zeros((n,n,4)); mk[...,:3]=1; mk[...,3]=m
    Image.fromarray((mk*255).astype(np.uint8),'RGBA').save(f'unity/Assets/LastWar/Resources/Lens/mark{z}.png.bytes',format='PNG',optimize=True)
print('ok')
