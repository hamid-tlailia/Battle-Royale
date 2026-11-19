"""Moves props that leave a soldier wedged: anything solid closer than a soldier's width (0.9 m) to a building wall
or to another solid prop - cars parked into houses, trees growing through yard walls, two rocks with a crack
between them. Each is moved to the nearest free spot (1 m steps out to 20 m) on ground no rougher than where it
stood, at the ground height there (same depth into it as before). Buildings and walls never move.

    python3 scripts/unity/fix-map-clearance.py valley [--check]      (needs numpy + shapely)
"""
import json, math, sys
import numpy as np
from shapely.geometry import MultiPoint, Point, Polygon
from shapely.ops import unary_union
from shapely.strtree import STRtree

R = 'unity/Assets/LastWar/Resources/'
MAP = sys.argv[1]; CHECK = '--check' in sys.argv
GAP = 0.9            # a soldier (0.8 m wide capsule) plus a little
M = json.load(open(R + 'Maps/' + MAP + '.json'))
S = {s['name']: s for s in json.load(open(R + 'Data/prefabs.json'))['shapes']}
size, cells = M['size'], M['cells']; H = np.array(M['heights']).reshape(cells + 1, cells + 1)

def hat(x, z):
    c = size / cells; fx = min(max((x + size / 2) / c, 0), cells - 1e-4); fz = min(max((z + size / 2) / c, 0), cells - 1e-4)
    ix, iz = int(fx), int(fz); u, v = fx - ix, fz - iz
    return (H[iz, ix] * (1 - u) + H[iz, ix + 1] * u) * (1 - v) + (H[iz + 1, ix] * (1 - u) + H[iz + 1, ix + 1] * u) * v

def xf(p, pts, x=None, z=None):
    a = p['rot']; c, s = math.cos(a), math.sin(a); pts = pts * p['s']
    x = p['x'] if x is None else x; z = p['z'] if z is None else z
    return np.stack([pts[:, 0] * c + pts[:, 1] * s + x, -pts[:, 0] * s + pts[:, 1] * c + z], 1)

FIXED = ('house', 'bld_', 'farmhouse', 'yardwall', 'fence', 'well', 'windmill')
def is_fixed(n): return n.startswith(FIXED)

def wall_foot(p):
    # a building's footprint = its walls (triangles between 0.3 m and 2.2 m above its floor), so doorways and
    # courtyards stay open; hull props (farmhouse, well, windmill) use their hull
    s = S[p['p']]; v = np.array(s['v']).reshape(-1, 3)
    if s['type'] == 'hull': return MultiPoint(list(map(tuple, xf(p, v[:, [0, 2]])))).convex_hull
    lo = v[:, 1].min(); parts = []
    for t in np.array(s['i']).reshape(-1, 3):
        tv = v[t]
        if tv[:, 1].max() < lo + 0.3 or tv[:, 1].min() > lo + 2.2: continue
        q = xf(p, tv[:, [0, 2]]); poly = Polygon(q)
        parts.append(poly if poly.area > 1e-4 else MultiPoint(list(map(tuple, q))).buffer(0.08))
    return unary_union(parts)

def foot(p, x=None, z=None):
    s = S.get(p['p'])
    if not s: return None
    if s['type'] == 'cylinder': return Point(p['x'] if x is None else x, p['z'] if z is None else z).buffer(s['radius'] * p['s'], 8)
    if s['type'] == 'hull' and s['v']:
        v = np.array(s['v']).reshape(-1, 3); return MultiPoint(list(map(tuple, xf(p, v[:, [0, 2]], x, z)))).convex_hull
    return None

props = M['props']
geo = {}
for i, p in enumerate(props):
    s = S.get(p['p'])
    if not s or s['type'] == 'none' or (s['type'] != 'cylinder' and not s['v']): continue
    geo[i] = wall_foot(p) if is_fixed(p['p']) else foot(p)
    if geo[i] is None: del geo[i]

def rough(x, z, r):
    hs = [hat(x + math.cos(k * .785) * r, z + math.sin(k * .785) * r) for k in range(8)]; return max(hs) - min(hs)

def problems():
    ids = list(geo); tree = STRtree([geo[i] for i in ids]); out = []
    for a, i in enumerate(ids):
        for b in tree.query(geo[i].buffer(GAP)):
            j = ids[b]
            if j <= i: continue
            fi, fj = is_fixed(props[i]['p']), is_fixed(props[j]['p'])
            if fi and fj: continue
            d = geo[i].distance(geo[j])
            # touching / overlapping boulders or trees make one solid lump: only a crack between them traps;
            # anything inside or against a wall, and any car, has to move
            stuck = (0 < d < GAP) or (d == 0 and (fi or fj or props[i]['p'].startswith('car') or props[j]['p'].startswith('car')))
            if stuck: out.append((round(d, 2), j if fi else i if fj else (i if props[i]['s'] < props[j]['s'] else j), i, j))
    return out

found = problems()
print(MAP, ':', len(found), 'tight spots')
for d, mv, i, j in found: print('  %.2f m  %-10s #%d  /  %-11s #%d   -> moving %s' % (d, props[i]['p'], i, props[j]['p'], j, props[mv]['p']))
if CHECK: sys.exit(0)

from shapely.ops import nearest_points
LUMP = ('rock', 'stump', 'tpine', 'toak')

def put(p, mv, x, z):
    depth = p['y'] - hat(p['x'], p['z'])
    p['x'], p['z'], p['y'] = round(x, 2), round(z, 2), round(hat(x, z) + depth, 2); geo[mv] = foot(p)

# 1. a crack between two boulders (or a boulder and a trunk) is closed by sliding the smaller one into the other:
#    they become one lump and the ground they covered stays covered (Highlands summit). Sliding one can open a new
#    crack beside it, so repeat (each prop slides at most 3 times)
slid = {}
for rnd in range(8):
    todo = [f for f in (found if rnd == 0 else problems()) if f[0] > 0]
    n = 0
    for d, mv, i, j in todo:
        o = j if mv == i else i
        if slid.get(mv, 0) >= 3 or is_fixed(props[o]['p']) or not (props[mv]['p'].startswith(LUMP) and props[o]['p'].startswith(LUMP)): continue
        d = geo[mv].distance(geo[o])
        if not 0 < d < GAP: continue
        pa, pb = nearest_points(geo[mv], geo[o]); dx, dz = pb.x - pa.x, pb.y - pa.y; L = math.hypot(dx, dz) or 1
        k = (d + 0.15) / L; p = props[mv]
        print('  #%d %s slid %.2f m into #%d %s' % (mv, p['p'], d + 0.15, o, props[o]['p']))
        put(p, mv, p['x'] + dx * k, p['z'] + dz * k); slid[mv] = slid.get(mv, 0) + 1; n += 1
    if not n: break

# 2. whatever is still tight (cars against walls, bales at the windmills, trees in yard walls, cracks that would not
#    close) moves away to the nearest free spot
done = set()
for _, mv, _, _ in problems():
    if mv in done: continue
    p = props[mv]; others = [g for k, g in geo.items() if k != mv]; tree = STRtree(others)
    r0 = rough(p['x'], p['z'], 3); best = None
    for rad in range(1, 21):
        for k in range(max(8, rad * 6)):
            a = k / max(8, rad * 6) * 2 * math.pi; x, z = p['x'] + math.cos(a) * rad, p['z'] + math.sin(a) * rad
            if math.hypot(x, z) > M['playRadius'] + 40 or rough(x, z, 3) > max(r0, 0.6) + 0.2: continue
            g = foot(p, x, z)
            if any(others[b].distance(g) < GAP for b in tree.query(g.buffer(GAP))): continue
            best = (x, z); break
        if best: break
    done.add(mv)
    if not best: print('  no free spot near #%d %s - removed' % (mv, p['p'])); p['_drop'] = True; del geo[mv]; continue
    print('  #%d %s moved %.1f m' % (mv, p['p'], math.hypot(best[0] - p['x'], best[1] - p['z'])))
    put(p, mv, *best)

# 3. ground that rises through a building's floor (Death Valley's shop stood where two flattened sites meet, the
#    terrain 1.2 m above its floor at the back) is levelled to the floor under it, blended back over 6 m
c = size / cells; n1 = cells + 1; flat = 0
old = {i: hat(p['x'], p['z']) for i, p in enumerate(props)}
for i, p in enumerate(props):
    if not p['p'].startswith(('house', 'bld_', 'farmhouse')) or i not in geo: continue
    v = np.array(S[p['p']]['v']).reshape(-1, 3); fl = p['y'] + v[:, 1].min() * p['s']; hull = geo[i].convex_hull
    x0, z0, x1, z1 = hull.bounds; cells_in = []
    for iz in range(max(0, int((z0 + size / 2) / c) - 4), min(n1, int((z1 + size / 2) / c) + 6)):
        for ix in range(max(0, int((x0 + size / 2) / c) - 4), min(n1, int((x1 + size / 2) / c) + 6)):
            cells_in.append((ix, iz, hull.distance(Point(ix * c - size / 2, iz * c - size / 2))))
    under = [H[iz, ix] for ix, iz, d in cells_in if d < 1.5]
    # houses sunk on purpose sit on level ground (house_f 0.49 m deep everywhere): only uneven ground counts
    if max(under) - min(under) < 0.3 or max(under) - fl < 0.3: continue
    fl = max(fl, min(under))
    for ix, iz, d in cells_in:
        if d < 7.5 and H[iz, ix] > fl:
            t = 1.0 if d < 1.5 else 1 - (d - 1.5) / 6.0; t = t * t * (3 - 2 * t)
            H[iz, ix] = round(H[iz, ix] + (fl - H[iz, ix]) * t, 2)
    flat += 1; print('  ground levelled under #%d %s' % (i, p['p']))
if flat:
    M['heights'] = [float(h) for h in H.reshape(-1)]
    for i, p in enumerate(props):          # anything standing on the levelled ground follows it down
        dh = hat(p['x'], p['z']) - old[i]
        if abs(dh) > 0.005 and not is_fixed(p['p']): p['y'] = round(p['y'] + dh, 2); print('    #%d %s follows %.2f m' % (i, p['p'], dh))

M['props'] = [p for p in props if not p.get('_drop')]
left = problems(); print('after:', len(left), 'tight spots')
json.dump(M, open(R + 'Maps/' + MAP + '.json', 'w'), separators=(',', ':'))
