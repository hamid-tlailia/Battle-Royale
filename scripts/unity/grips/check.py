# Each point checked against the real geometry (side projection z/y, triangles of the model):
#  R inside the grip, L within 1.5 cm of the surface (inside or just under), M at the front end of the barrel, A on/just above the top.
import json, sys
sys.path.insert(0,'grips'); from manual import P
def inside(pt,tri):
    (x,y)=pt; (a,b,c)=tri
    def s(p,q,r): return (p[0]-r[0])*(q[1]-r[1])-(q[0]-r[0])*(p[1]-r[1])
    d1,d2,d3=s(pt,a,b),s(pt,b,c),s(pt,c,a)
    return not ((d1<0 or d2<0 or d3<0) and (d1>0 or d2>0 or d3>0))
def dist_seg(p,a,b):
    import math
    ax,ay=a; bx,by=b; px,py=p; dx,dy=bx-ax,by-ay; L=dx*dx+dy*dy
    t=0 if L==0 else max(0,min(1,((px-ax)*dx+(py-ay)*dy)/L)); return math.hypot(px-ax-t*dx,py-ay-t*dy)
rep={}
for name,p in P.items():
    parts=json.load(open(f'grips/gun_{name}.json'))
    tris=[[(V[i][2],V[i][1]) for i in t] for part in parts for V in [part['V']] for t in part['tris']]
    zs=[v[0] for t in tris for v in t]; z0,z1=min(zs),max(zs)
    def ins(q): return any(inside(q,t) for t in tris)
    def near(q): return min(min(dist_seg(q,t[0],t[1]),dist_seg(q,t[1],t[2]),dist_seg(q,t[2],t[0])) for t in tris)
    R=(p['R'][2],p['R'][1]); L=(p['L'][2],p['L'][1]); M=(p['M'][2],p['M'][1]); A=(p['A'][2],p['A'][1])
    front = z1 if p.get('flip') else z0
    res={'R inside grip':ins(R),'L on surface':ins(L) or near(L)<0.015,'M at front end':abs(M[0]-front)<0.01 and ins((M[0]+(-0.005 if p.get('flip') else 0.005),M[1])),
         'A on/above top':ins(A) or near(A)<0.02}
    rep[name]=res
    print(f"{name:17}", '  '.join(f"{k}:{'OK' if v else 'FAIL'}" for k,v in res.items()))
