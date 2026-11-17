"""
Bakes the "Death Valley - Terrain" scan (artfromheath, Sketchfab, CC-BY 4.0) into a Last War map for Unity:
  unity/Assets/LastWar/Resources/Maps/valley.json        heights grid, props, boxes, spawns, areas
  unity/Assets/LastWar/Resources/Maps/valley_albedo.jpg  the scan's own colours seen from above
A big map (1200 m, 1080 m across the play circle): two villages, a desert town and a small city district are laid
on flattened ground, with rocks, bushes, dead trees and cars around.
Run:  python3 -I scripts/unity/bake-deathvalley.py <path to the downloaded model.glb>
"""
import json, struct, sys, math, random
import numpy as np
from PIL import Image

GLB, OUT = sys.argv[1], 'unity/Assets/LastWar/Resources/Maps'
SIZE, CELLS, PLAY, VSCALE, TEX = 1200.0, 300, 540.0, 0.55, 2048
rnd = random.Random(20261117)

# ---------------------------------------------------------------- read the glb
b = open(GLB, 'rb').read()
jl = struct.unpack('<I', b[12:16])[0]; J = json.loads(b[20:20 + jl])
binoff = 20 + jl + 8
def acc(i):
    a = J['accessors'][i]; v = J['bufferViews'][a['bufferView']]
    comp = {5126: np.float32, 5125: np.uint32, 5123: np.uint16}[a['componentType']]
    ncomp = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4}[a['type']]
    stride = v.get('byteStride', 0); start = binoff + v.get('byteOffset', 0) + a.get('byteOffset', 0)
    item = np.dtype(comp).itemsize * ncomp
    if stride and stride != item:
        raw = np.frombuffer(b, np.uint8, count=stride * a['count'], offset=start).reshape(a['count'], stride)[:, :item]
        return np.frombuffer(raw.tobytes(), comp).reshape(a['count'], ncomp)
    return np.frombuffer(b, comp, count=a['count'] * ncomp, offset=start).reshape(a['count'], ncomp)
M = np.array(J['nodes'][0]['matrix'], dtype=np.float64).reshape(4, 4).T  # column-major
P, U, T = [], [], []; base = 0
for m in J['meshes']:
    pr = m['primitives'][0]
    pos = acc(pr['attributes']['POSITION']).astype(np.float64); uv = acc(pr['attributes']['TEXCOORD_0']).astype(np.float64)
    idx = acc(pr['indices']).astype(np.int64).reshape(-1, 3)
    w = (np.c_[pos, np.ones(len(pos))] @ M.T)[:, :3]
    P.append(w); U.append(uv); T.append(idx + base); base += len(pos)
P = np.vstack(P); U = np.vstack(U); T = np.vstack(T)
img = J['images'][J['textures'][J['materials'][0]['pbrMetallicRoughness']['baseColorTexture']['index']]['source']]
v = J['bufferViews'][img['bufferView']]
import io
tex = np.asarray(Image.open(io.BytesIO(b[binoff + v.get('byteOffset', 0): binoff + v.get('byteOffset', 0) + v['byteLength']])).convert('RGB'))
print('verts', len(P), 'tris', len(T), 'texture', tex.shape)

# model units -> metres: the tile's width becomes the map size, heights x VSCALE (walkable valleys)
x0, x1, z0, z1 = P[:, 0].min(), P[:, 0].max(), P[:, 2].min(), P[:, 2].max()
span = max(x1 - x0, z1 - z0); k = SIZE / span
X = (P[:, 0] - (x0 + x1) / 2) * k; Z = (P[:, 2] - (z0 + z1) / 2) * k; Y = (P[:, 1] - P[:, 1].min()) * k * VSCALE
print('relief m', round(Y.max(), 1))

# ---------------------------------------------------------------- heights: rasterise the triangles (highest surface)
n = CELLS + 1; cell = SIZE / CELLS; half = SIZE / 2
H = np.full((n, n), -1e9)
gx = (X + half) / cell; gz = (Z + half) / cell
for a, bb, c in T:
    xs = (gx[a], gx[bb], gx[c]); zs = (gz[a], gz[bb], gz[c])
    i0, i1 = max(0, math.ceil(min(xs))), min(CELLS, math.floor(max(xs)))
    j0, j1 = max(0, math.ceil(min(zs))), min(CELLS, math.floor(max(zs)))
    if i0 > i1 or j0 > j1: continue
    d = (zs[1] - zs[2]) * (xs[0] - xs[2]) + (xs[2] - xs[1]) * (zs[0] - zs[2])
    if abs(d) < 1e-12: continue
    for j in range(j0, j1 + 1):
        for i in range(i0, i1 + 1):
            l0 = ((zs[1] - zs[2]) * (i - xs[2]) + (xs[2] - xs[1]) * (j - zs[2])) / d
            l1 = ((zs[2] - zs[0]) * (i - xs[2]) + (xs[0] - xs[2]) * (j - zs[2])) / d
            l2 = 1 - l0 - l1
            if l0 < -1e-6 or l1 < -1e-6 or l2 < -1e-6: continue
            h = l0 * Y[a] + l1 * Y[bb] + l2 * Y[c]
            if h > H[j, i]: H[j, i] = h
# holes (none expected): nearest filled neighbour
for _ in range(20):
    miss = H < -1e8
    if not miss.any(): break
    Hp = np.pad(H, 1, mode='edge'); best = np.full_like(H, -1e9)
    for dj in (-1, 0, 1):
        for di in (-1, 0, 1): best = np.maximum(best, Hp[1 + dj:1 + dj + n, 1 + di:1 + di + n])
    H[miss] = best[miss]
H -= np.median(H)  # the valley floor near 0
# soften the scan's sharp gullies (playable slopes), keeping the big shapes
for _ in range(3):
    Hp = np.pad(H, 2, mode='edge'); acc2 = np.zeros_like(H)
    for dj in range(-2, 3):
        for di in range(-2, 3): acc2 += Hp[2 + dj:2 + dj + n, 2 + di:2 + di + n]
    H = acc2 / 25.0

# ---------------------------------------------------------------- albedo: the scan's colours from above
A = np.zeros((TEX, TEX, 3), np.uint8); D = np.full((TEX, TEX), -1e9)
px = (X + half) / SIZE * TEX; pz = (Z + half) / SIZE * TEX  # image row 0 = the -z edge (export-maps convention)
th, tw = tex.shape[:2]
for a, bb, c in T:
    xs = (px[a], px[bb], px[c]); zs = (pz[a], pz[bb], pz[c])
    i0, i1 = max(0, int(math.floor(min(xs)))), min(TEX - 1, int(math.ceil(max(xs))))
    j0, j1 = max(0, int(math.floor(min(zs)))), min(TEX - 1, int(math.ceil(max(zs))))
    d = (zs[1] - zs[2]) * (xs[0] - xs[2]) + (xs[2] - xs[1]) * (zs[0] - zs[2])
    if abs(d) < 1e-12: continue
    for j in range(j0, j1 + 1):
        for i in range(i0, i1 + 1):
            cx, cz = i + .5, j + .5
            l0 = ((zs[1] - zs[2]) * (cx - xs[2]) + (xs[2] - xs[1]) * (cz - zs[2])) / d
            l1 = ((zs[2] - zs[0]) * (cx - xs[2]) + (xs[0] - xs[2]) * (cz - zs[2])) / d
            l2 = 1 - l0 - l1
            if l0 < -.02 or l1 < -.02 or l2 < -.02: continue
            h = l0 * Y[a] + l1 * Y[bb] + l2 * Y[c]
            if h <= D[j, i]: continue
            D[j, i] = h
            u = l0 * U[a] + l1 * U[bb] + l2 * U[c]
            A[j, i] = tex[min(th - 1, max(0, int(u[1] * th))), min(tw - 1, max(0, int(u[0] * tw)))]
holes = D < -1e8
if holes.any():
    img2 = Image.fromarray(A).filter(__import__('PIL.ImageFilter', fromlist=['x']).MaxFilter(3)); A2 = np.asarray(img2); A[holes] = A2[holes]

# ---------------------------------------------------------------- places: flat ground for the settlements
def hat(x, z):
    fx = min(CELLS - 1e-6, max(0, (x + half) / cell)); fz = min(CELLS - 1e-6, max(0, (z + half) / cell))
    i, j = int(fx), int(fz); u, w = fx - i, fz - j
    return (H[j, i] * (1 - u) + H[j, i + 1] * u) * (1 - w) + (H[j + 1, i] * (1 - u) + H[j + 1, i + 1] * u) * w
def rough(x, z, r):
    hs = [hat(x + r * math.cos(t), z + r * math.sin(t)) for t in np.linspace(0, 2 * math.pi, 16, endpoint=False)]
    return max(hs) - min(hs)
cands = []
for _ in range(4000):
    a = rnd.random() * 2 * math.pi; r = math.sqrt(rnd.random()) * (PLAY - 90)
    x, z = r * math.cos(a), r * math.sin(a)
    cands.append((rough(x, z, 45) + rough(x, z, 22) * 1.5, x, z))
cands.sort()
sites = []
for score, x, z in cands:
    if all(math.hypot(x - sx, z - sz) > 230 for _, sx, sz in sites): sites.append((score, x, z))
    if len(sites) == 5: break
kinds = ['village', 'desert', 'city', 'village', 'outpost']
names = {'village': ['Furnace Creek', 'Stovepipe Wells'], 'desert': ['Badwater Town'], 'city': ['Panamint City'], 'outpost': ['Ubehebe Post']}
def flatten(cx, cz, r, h):
    for j in range(n):
        for i in range(n):
            x, z = -half + i * cell, -half + j * cell; d = math.hypot(x - cx, z - cz)
            if d > r * 1.6: continue
            t = 1.0 if d < r else 1 - (d - r) / (r * .6); t = t * t * (3 - 2 * t)
            H[j, i] = H[j, i] * (1 - t) + h * t

props, boxes, areas = [], [], []
taken = []  # (x, z, radius) of what stands in the settlements
def prop(name, x, z, rot, s=1.0, dy=0.0): props.append({'p': name, 'x': round(x, 2), 'y': round(hat(x, z) + dy, 2), 'z': round(z, 2), 'rot': round(rot, 3), 's': s})
def box(x, z, w, h, d, rot, color):
    boxes.append({'x': round(x, 2), 'y': round(hat(x, z) + h / 2 - 0.1, 2), 'z': round(z, 2), 'hx': round(w / 2, 2), 'hy': round(h / 2, 2), 'hz': round(d / 2, 2), 'rot': round(rot, 3), 'color': color})
used = {}
for (score, sx, sz), kind in zip(sites, kinds):
    nm = names[kind][used.get(kind, 0)]; used[kind] = used.get(kind, 0) + 1
    rad = {'village': 55, 'desert': 70, 'city': 85, 'outpost': 30}[kind]
    flatten(sx, sz, rad, hat(sx, sz)); areas.append({'name': nm, 'x': sx, 'z': sz})
    road = rnd.random() * math.pi
    if kind in ('village', 'outpost', 'desert'):
        # the desert town: enterable shanties (stairs to a second floor) and wooden mansions, a shop on its square
        houses = ['house_a', 'house_b', 'house_c', 'house_d', 'farmhouse'] if kind == 'village' else ['bld_shanty', 'bld_mansion', 'bld_shanty', 'house_e'] if kind == 'desert' else ['bld_shanty', 'house_a']
        count = {'village': 9, 'desert': 8, 'outpost': 3}[kind]
        gap = 24 if kind == 'desert' else 22
        for q in range(count):
            # two rows along a street
            side = 1 if q % 2 else -1; along = (q // 2 - count / 4) * gap
            off = 17 if kind == 'desert' else 15
            x = sx + math.cos(road) * along - math.sin(road) * side * off; z = sz + math.sin(road) * along + math.cos(road) * side * off
            prop(houses[q % len(houses)] if kind == 'desert' else rnd.choice(houses), x, z, road + (math.pi if side > 0 else 0) + rnd.uniform(-.08, .08))
            taken.append((x, z, 9))
        if kind == 'desert':
            prop('bld_shop', sx + math.cos(road) * (count / 4 * gap + 26), sz + math.sin(road) * (count / 4 * gap + 26), road)
        for q in range(4):
            prop(rnd.choice(['car1', 'car2', 'car3', 'car4']), sx + rnd.uniform(-rad, rad) * .7, sz + rnd.uniform(-rad, rad) * .7, rnd.random() * 6.28)
        if kind == 'village':
            for extra in ['well', 'wagon', 'haybale', 'haybale', 'windmill']:
                # a free spot: never inside a house (the windmill needs the most room)
                need = 13 if extra == 'windmill' else 5
                for _ in range(60):
                    x, z = sx + rnd.uniform(-rad, rad) * .9, sz + rnd.uniform(-rad, rad) * .9
                    if all(math.hypot(x - tx, z - tz) > tr + need for tx, tz, tr in taken): break
                taken.append((x, z, need))
                prop(extra, x, z, rnd.random() * 6.28)
                if extra == 'windmill': prop('windmill_blades', x, z, props[-1]['rot'])
        for q in range(6):  # yard walls / fences round the place
            a = q / 6 * 2 * math.pi; prop(rnd.choice(['yardwall', 'fence']), sx + math.cos(a) * (rad + 6), sz + math.sin(a) * (rad + 6), a + math.pi / 2)
    else:
        # the city: blocks of concrete towers (solid cover) round a square, a few houses to loot between them
        greys = [0x8a8f94, 0x9c968c, 0x7d838a, 0xb0a99c, 0x6f7479]
        for gx2 in range(-2, 3):
            for gz2 in range(-1, 2):
                if gx2 == 0 and gz2 == 0: continue
                cxl, czl = gx2 * 30, gz2 * 34
                x = sx + math.cos(road) * cxl - math.sin(road) * czl; z = sz + math.sin(road) * cxl + math.cos(road) * czl
                # enterable apartment blocks (climb the stairs, shoot from the windows) between solid concrete towers
                if (gx2 + gz2) % 2 == 0: prop('bld_panel', x, z, road + rnd.choice([0, math.pi])); continue
                box(x, z, rnd.uniform(10, 14), rnd.uniform(9, 26), rnd.uniform(10, 14), road, rnd.choice(greys))
        for q in range(8): prop(rnd.choice(['car1', 'car2', 'car3', 'car4']), sx + rnd.uniform(-60, 60), sz + rnd.uniform(-60, 60), road + rnd.choice([0, math.pi]))

# the wild: rocks, dry bushes, dead trees - away from the settlements and off the steep walls
def near_site(x, z, extra=25): return any(math.hypot(x - s[1], z - s[2]) < 75 + extra for s in sites)
for q in range(900):
    a = rnd.random() * 2 * math.pi; r = math.sqrt(rnd.random()) * (PLAY + 30); x, z = r * math.cos(a), r * math.sin(a)
    if near_site(x, z) or rough(x, z, 4) > 4.5: continue
    t = rnd.random()
    name = rnd.choice(['rock1', 'rock2', 'rock3', 'rock4']) if t < .45 else rnd.choice(['tbush1', 'tbush2', 'tbush3']) if t < .8 else 'stump' if t < .9 else rnd.choice(['toak1', 'toak2'])
    prop(name, x, z, rnd.random() * 6.28, round(rnd.uniform(.8, 1.5), 2) if name.startswith('rock') else 1.0)
for q in range(14):  # wrecks along the valley
    a = rnd.random() * 2 * math.pi; r = math.sqrt(rnd.random()) * PLAY; x, z = r * math.cos(a), r * math.sin(a)
    if not near_site(x, z, 0) and rough(x, z, 4) < 2: prop(rnd.choice(['car1', 'car2', 'car3', 'car4']), x, z, rnd.random() * 6.28)

spawns = []
while len(spawns) < 64:
    a = rnd.random() * 2 * math.pi; r = math.sqrt(rnd.random()) * (PLAY - 40); x, z = r * math.cos(a), r * math.sin(a)
    if rough(x, z, 3) < 2.5: spawns.append({'x': x, 'z': z})

out = {'id': 'valley', 'name': 'Death Valley', 'size': SIZE, 'cells': CELLS, 'snowHeight': 999, 'playRadius': PLAY,
       'heights': [round(float(h), 2) for h in H.reshape(-1)], 'mask': [], 'albedo': 'valley_albedo',
       'props': props, 'boxes': boxes, 'spawns': spawns, 'areas': areas}
json.dump(out, open(f'{OUT}/valley.json', 'w'), separators=(',', ':'))
Image.fromarray(A).save(f'{OUT}/valley_albedo.jpg', quality=86)
print('sites', [(a['name'], round(a['x']), round(a['z'])) for a in areas]); print('props', len(props), 'boxes', len(boxes))
