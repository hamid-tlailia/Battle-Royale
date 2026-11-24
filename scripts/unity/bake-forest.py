"""
Bakes the "Forest and fields" photogrammetry scan (Laslo.S, Sketchfab, CC-BY 4.0) into a Last War map for Unity:
  unity/Assets/LastWar/Resources/Maps/forest.json        heights grid, props, spawns, areas
  unity/Assets/LastWar/Resources/Maps/forest_albedo.jpg  the scan's own colours seen from above (unpainted: run
                                                         paint-beaches.py --only=forest --raw=<this picture> after)
A small map (440 m, 320 m across the play circle) at the scan's real size: a river meadow between woods and hedgerows.
The scan's trees are a solid crust over the ground, so the bare ground under them is rebuilt (the crowns masked out,
the ground below filled in smoothly from the open ground round them) and the game's own trees stand where the scan's
crowns were, sized by how tall they were. Farms and a village stand on the open fields.
Run:  python3 -I scripts/unity/bake-forest.py <path to the downloaded model.glb> [<raw albedo out>]
"""
import json, struct, sys, math, random, io
import numpy as np
import scipy.ndimage as nd
from PIL import Image, ImageFilter

GLB = sys.argv[1]; OUT = 'unity/Assets/LastWar/Resources/Maps'
RAW = sys.argv[2] if len(sys.argv) > 2 else f'{OUT}/forest_albedo.jpg'
SIZE, CELLS, PLAY, VSCALE, TEX = 440.0, 176, 160.0, 1.6, 2048
CX, CZ = -37.0, 30.0          # the scan's middle (its own coordinates, metres)
G = 0.5                        # m: the fine raster the ground is worked out on
rnd = random.Random(20261123)
half = SIZE / 2

# ---------------------------------------------------------------- read the glb (4 materials, each its own colour map)
b = open(GLB, 'rb').read()
jl = struct.unpack('<I', b[12:16])[0]; J = json.loads(b[20:20 + jl]); binoff = 20 + jl + 8
def acc(i):
    a = J['accessors'][i]; v = J['bufferViews'][a['bufferView']]
    comp = {5126: np.float32, 5125: np.uint32, 5123: np.uint16}[a['componentType']]
    nc = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4}[a['type']]
    st = v.get('byteStride', 0); s = binoff + v.get('byteOffset', 0) + a.get('byteOffset', 0); it = np.dtype(comp).itemsize * nc
    if st and st != it:
        raw = np.frombuffer(b, np.uint8, count=st * a['count'], offset=s).reshape(a['count'], st)[:, :it]
        return np.frombuffer(raw.tobytes(), comp).reshape(a['count'], nc)
    return np.frombuffer(b, comp, count=a['count'] * nc, offset=s).reshape(a['count'], nc)
M = np.array(J['nodes'][0]['matrix'], np.float64).reshape(4, 4).T  # column-major
def image(ti):
    im = J['images'][J['textures'][ti]['source']]; v = J['bufferViews'][im['bufferView']]; o = binoff + v.get('byteOffset', 0)
    return np.asarray(Image.open(io.BytesIO(b[o:o + v['byteLength']])).convert('RGB'))
texs = [image(m['pbrMetallicRoughness']['baseColorTexture']['index']) for m in J['materials']]

# ---------------------------------------------------------------- the top surface + its colours: points spread over
# every triangle (15 a triangle: the scan's triangles are ~0.6 m, finer than both rasters), the highest one wins
gn = int(SIZE / G); TOP = np.full(gn * gn, -1e9)
A = np.zeros((TEX * TEX, 3), np.uint8); D = np.full(TEX * TEX, -1e9)
bary = [(p / 4, q / 4) for p in range(5) for q in range(5) if p + q <= 4]
for m in J['meshes']:
    pr = m['primitives'][0]; t = texs[pr['material']]; th, tw = t.shape[:2]
    pos = acc(pr['attributes']['POSITION']).astype(np.float64); uv = acc(pr['attributes']['TEXCOORD_0']).astype(np.float64)
    idx = acc(pr['indices']).astype(np.int64).reshape(-1, 3)
    w = (np.c_[pos, np.ones(len(pos))] @ M.T)[:, :3]
    P0, P1, P2 = w[idx[:, 0]], w[idx[:, 1]], w[idx[:, 2]]; U0, U1, U2 = uv[idx[:, 0]], uv[idx[:, 1]], uv[idx[:, 2]]
    for l1, l2 in bary:
        l0 = 1 - l1 - l2; p = P0 * l0 + P1 * l1 + P2 * l2; u = U0 * l0 + U1 * l1 + U2 * l2
        x = p[:, 0] - CX; z = p[:, 2] - CZ; y = p[:, 1]
        gi = ((x + half) / G).astype(np.int64); gj = ((z + half) / G).astype(np.int64)
        ok = (gi >= 0) & (gi < gn) & (gj >= 0) & (gj < gn)
        np.maximum.at(TOP, gj[ok] * gn + gi[ok], y[ok])
        i = ((x + half) / SIZE * TEX).astype(np.int64); j = ((z + half) / SIZE * TEX).astype(np.int64)  # row 0 = the -z edge
        ok = (i >= 0) & (i < TEX) & (j >= 0) & (j < TEX)
        k = j[ok] * TEX + i[ok]; yy = y[ok]
        col = t[np.clip((u[ok, 1] * th).astype(int), 0, th - 1), np.clip((u[ok, 0] * tw).astype(int), 0, tw - 1)]
        o = np.argsort(yy); k, yy, col = k[o], yy[o], col[o]
        hi = yy > D[k]; D[k[hi]] = yy[hi]; A[k[hi]] = col[hi]   # sorted low to high: the last (highest) write stays
TOP = TOP.reshape(gn, gn); A = A.reshape(TEX, TEX, 3); D = D.reshape(TEX, TEX)
print('top surface', round(TOP[TOP > -1e8].min(), 1), '..', round(TOP[TOP > -1e8].max(), 1))

# ---------------------------------------------------------------- the bare ground under the trees
valid = TOP > -1e8
T = np.where(valid, TOP, nd.grey_dilation(np.where(valid, TOP, -1e9), size=5)); valid = T > -1e8   # pin holes
valid = nd.binary_erosion(valid, iterations=4)  # the scan's ragged rim
Tv = np.where(valid, T, 1e9)
trees = np.zeros_like(valid)
for wm in (8, 16, 30, 50):  # anything standing over 1.3 m above what an opening this wide leaves: crowns, hedges
    k = int(wm / G); trees |= valid & (T - nd.grey_opening(Tv, size=(k, k)) > 1.3)
trees = nd.binary_dilation(trees, iterations=6) & valid  # 3 m round the crowns: their skirts, the bushes under them
known = valid & ~trees
def fill(H, known):
    """the masked cells filled smoothly from the known ones (Laplace, coarse to fine)"""
    H = np.where(known, H, H[known].mean()); levels = []; h, kk = H, known
    while h.shape[0] > 20: levels.append((h, kk)); h = h[::2, ::2]; kk = kk[::2, ::2]
    g = np.where(kk, h, h[kk].mean())
    for h0, k0 in [(h, kk)] + levels[::-1]:
        if g.shape != h0.shape:
            g = np.kron(g, np.ones((2, 2)))[:h0.shape[0], :h0.shape[1]]
            g = np.pad(g, ((0, h0.shape[0] - g.shape[0]), (0, h0.shape[1] - g.shape[1])), mode='edge')
        for _ in range(60):
            gp = np.pad(g, 1, mode='edge'); g = np.where(k0, h0, (gp[:-2, 1:-1] + gp[2:, 1:-1] + gp[1:-1, :-2] + gp[1:-1, 2:]) / 4)
    return g
GR = fill(T, known)
for _ in range(3):  # open cells well above the ground round them are tree tops seen through gaps: masked too
    bad = nd.binary_dilation(known & (T - nd.gaussian_filter(GR, 12) > 1.5), iterations=4) & valid
    if not bad.any(): break
    known &= ~bad; trees |= bad; GR = fill(T, known)
GR = nd.gaussian_filter(GR, 3)  # 1.5 m: the mesh's noise, grass tufts
canopy = np.where(valid, T - GR, 0)
print('ground relief', round(GR[valid].max() - GR[valid].min(), 1), 'm, under trees', round(trees.sum() / valid.sum() * 100), '%')

# ---------------------------------------------------------------- the game grid (2.5 m), relief x VSCALE
n = CELLS + 1; cell = SIZE / CELLS
gx = np.clip((np.arange(n) * cell) / G, 0, gn - 1).astype(int)
H = GR[np.ix_(gx, gx)].astype(np.float64)
H = (H - np.median(GR[valid])) * VSCALE
def hat(x, z):
    fx = min(CELLS - 1e-6, max(0, (x + half) / cell)); fz = min(CELLS - 1e-6, max(0, (z + half) / cell))
    i, j = int(fx), int(fz); u, w = fx - i, fz - j
    return (H[j, i] * (1 - u) + H[j, i + 1] * u) * (1 - w) + (H[j + 1, i] * (1 - u) + H[j + 1, i + 1] * u) * w
def at(arr, x, z, res=G):
    i = int(min(arr.shape[1] - 1, max(0, (x + half) / res))); j = int(min(arr.shape[0] - 1, max(0, (z + half) / res))); return arr[j, i]
def rough(x, z, r):
    hs = [hat(x + r * math.cos(t), z + r * math.sin(t)) for t in np.linspace(0, 2 * math.pi, 16, endpoint=False)]
    return max(hs) - min(hs)

# ---------------------------------------------------------------- albedo: the scan's colours; under the crowns the
# forest floor (the game's trees stand there: painted crowns under them read as green smudges)
holes = D < -1e8
for _ in range(4):
    if not holes.any(): break
    A2 = np.asarray(Image.fromarray(A).filter(ImageFilter.MaxFilter(3))); A = np.where(holes[..., None], A2, A)
    holes = holes & (A.sum(2) == 0)
Af = A.astype(np.float32) / 255
cz = np.asarray(Image.fromarray(trees.astype(np.uint8) * 255).resize((TEX, TEX), Image.BILINEAR), np.float32) / 255
cz = nd.gaussian_filter(cz, 4)[..., None]
floor = np.array([0.22, 0.24, 0.13]) * (0.75 + 0.5 * nd.gaussian_filter(np.random.default_rng(3).random((TEX, TEX)), 3)[..., None])
Af = Af * (1 - 0.75 * cz) + floor * 0.75 * cz
out_rgb = Image.fromarray((np.clip(Af, 0, 1) * 255).astype(np.uint8))

# ---------------------------------------------------------------- places on the open fields
openness = nd.uniform_filter((~trees & valid).astype(np.float32), size=int(50 / G))
cands = []
for _ in range(5000):
    a = rnd.random() * 2 * math.pi; r = math.sqrt(rnd.random()) * (PLAY - 32)
    x, z = r * math.cos(a), r * math.sin(a)
    if at(openness, x, z) < 0.78: continue
    cands.append((rough(x, z, 35) + rough(x, z, 18) * 1.5 - at(openness, x, z) * 2, x, z))
cands.sort()
sites = []
for score, x, z in cands:
    if all(math.hypot(x - sx, z - sz) > 88 for _, sx, sz in sites): sites.append((score, x, z))
    if len(sites) == 4: break
kinds = ['village', 'farm', 'farm', 'lodge'][:len(sites)]
names = {'village': ['Oakford'], 'farm': ['Mill Farm', 'Hedge Farm'], 'lodge': ['Ranger Lodge']}
def flatten(cx, cz, r, h):
    for j in range(n):
        for i in range(n):
            x, z = -half + i * cell, -half + j * cell; d = math.hypot(x - cx, z - cz)
            if d > r * 1.6: continue
            t = 1.0 if d < r else 1 - (d - r) / (r * .6); t = t * t * (3 - 2 * t)
            H[j, i] = H[j, i] * (1 - t) + h * t

props, areas, taken = [], [], []   # taken: (x, z, radius) of what stands in the places
def prop(name, x, z, rot, s=1.0, dy=0.0): props.append({'p': name, 'x': round(x, 2), 'y': round(hat(x, z) + dy, 2), 'z': round(z, 2), 'rot': round(rot, 3), 's': s})
def free(x, z, need): return all(math.hypot(x - tx, z - tz) > tr + need for tx, tz, tr in taken)
used = {}
for (score, sx, sz), kind in zip(sites, kinds):
    nm = names[kind][used.get(kind, 0)]; used[kind] = used.get(kind, 0) + 1
    rad = {'village': 54, 'farm': 38, 'lodge': 28}[kind]
    flatten(sx, sz, rad, hat(sx, sz)); areas.append({'name': nm, 'x': sx, 'z': sz, 'kind': kind})
    road = rnd.random() * math.pi
    # Oakwood's own buildings, all enterable (Sketchfab, CC-BY - CREDITS.md): a two-storey wooden house with a balcony
    # (yadrogames), an old two-storey house with rooms and stairs (davidfalke), a 1950s bungalow with rooms
    # (AleixoAlonso), a barn (narighillya), a watch tower (_SeF_) - none of the other maps' houses
    houses = {'village': ['bld_woodhouse', 'bld_oldhouse', 'bld_dawson'], 'farm': ['bld_dawson', 'bld_barn', 'bld_oldhouse'], 'lodge': ['bld_woodhouse', 'bld_tower']}[kind]
    count = {'village': 8, 'farm': 3, 'lodge': 2}[kind]
    for q in range(count):  # two rows along a street
        side = 1 if q % 2 else -1; along = (q // 2 - (count - 1) / 4) * 26
        x = sx + math.cos(road) * along - math.sin(road) * side * 17; z = sz + math.sin(road) * along + math.cos(road) * side * 17
        prop(houses[q % len(houses)], x, z, road + (math.pi if side > 0 else 0) + rnd.uniform(-.08, .08))
        taken.append((x, z, 11))
    extras = {'village': ['well', 'wagon', 'haybale', 'car1', 'car3'], 'farm': ['windmill', 'haybale', 'haybale', 'haybale', 'wagon', 'scarecrow', 'well', 'car2'], 'lodge': ['car4', 'wagon', 'haybale']}[kind]
    for extra in extras:
        need = 13 if extra == 'windmill' else 5
        for _ in range(80):
            x, z = sx + rnd.uniform(-rad, rad) * .9, sz + rnd.uniform(-rad, rad) * .9
            if free(x, z, need): break
        taken.append((x, z, need)); prop(extra, x, z, rnd.random() * 6.28)
        if extra == 'windmill': prop('windmill_blades', x, z, props[-1]['rot'])
    for q in range(6):  # fences round the place
        a = q / 6 * 2 * math.pi + rnd.uniform(-.2, .2); x, z = sx + math.cos(a) * (rad + 6), sz + math.sin(a) * (rad + 6)
        if free(x, z, 2): prop(rnd.choice(['fence', 'fence', 'yardwall']) if kind != 'lodge' else 'fence', x, z, a + math.pi / 2)
def near_site(x, z, extra=0): return any(math.hypot(x - a['x'], z - a['z']) < 55 + extra for a in areas)
# watch towers on the open ground at the woods' edges (a look-out over the meadow), away from the places and each other
towers = []
for _ in range(4000):
    if len(towers) == 4: break
    a = rnd.random() * 2 * math.pi; r = math.sqrt(rnd.random()) * (PLAY - 30); x, z = r * math.cos(a), r * math.sin(a)
    if near_site(x, z, 10) or at(trees, x, z) or not free(x, z, 6) or rough(x, z, 4) > 1.0: continue
    if at(openness, x, z) > 0.75 or any(math.hypot(x - tx, z - tz) < 80 for tx, tz in towers): continue  # (by the trees, not mid-meadow)
    towers.append((x, z)); taken.append((x, z, 6)); prop('bld_tower', x, z, rnd.random() * 6.28)

# ---------------------------------------------------------------- the woods: a tree on each of the scan's crowns
can = nd.gaussian_filter(np.where(trees, canopy, 0), 2)
peaks = (can == nd.maximum_filter(can, size=int(5 / G))) & (can > 3.5)
pj, pi = np.nonzero(peaks)
# then the rest of the woods on a jittered 5 m grid (a crown the peak search merged with its neighbours)
fj, fi = [], []
for z0 in np.arange(-half, half, 5.0):
    for x0 in np.arange(-half, half, 5.0):
        x, z = x0 + rnd.uniform(0, 5), z0 + rnd.uniform(0, 5); i, j = int((x + half) / G), int((z + half) / G)
        if 0 <= i < gn and 0 <= j < gn and trees[j, i] and can[j, i] > 2.5: fi.append(i); fj.append(j)
order = list(np.argsort(-can[pj, pi])) + list(range(len(pj), len(pj) + len(fj)))
pj = np.r_[pj, np.array(fj, int)]; pi = np.r_[pi, np.array(fi, int)]; placed = []
cellsz = 5.0
grid = {}
for o in order:
    x, z = -half + (pi[o] + .5) * G, -half + (pj[o] + .5) * G
    if math.hypot(x, z) > PLAY - 8 or near_site(x, z, -10) or not free(x, z, 3): continue  # (the beach starts 6 m inside the play circle)
    key = (int(x // cellsz), int(z // cellsz))
    if any(math.hypot(x - qx, z - qz) < cellsz for dk in ((a, c) for a in (-1, 0, 1) for c in (-1, 0, 1)) for qx, qz in grid.get((key[0] + dk[0], key[1] + dk[1]), [])): continue
    grid.setdefault(key, []).append((x, z)); h = can[pj[o], pi[o]]
    name = rnd.choice(['toak1', 'toak2', 'toak1', 'toak2', 'tpine1', 'tpine2', 'tpine3', 'tpine4'] if h > 9 else ['toak1', 'toak2', 'tbush1', 'tbush2', 'tbush3', 'tpine3'])
    s = round(float(np.clip(0.75 + h / 22, 0.8, 1.35) * rnd.uniform(.92, 1.08)), 2)
    prop(name, x, z, rnd.random() * 6.28, s if name[1] != 'b' else round(rnd.uniform(1.0, 1.4), 2)); placed.append((x, z))
print('trees on crowns', len(placed))
# undergrowth: bushes and stumps in the woods, a few on the woods' edges; rocks by the stream and in the meadows
for q in range(900):
    a = rnd.random() * 2 * math.pi; r = math.sqrt(rnd.random()) * (PLAY - 8); x, z = r * math.cos(a), r * math.sin(a)
    if near_site(x, z) or not free(x, z, 2): continue
    inwood = at(trees, x, z)
    t = rnd.random()
    if inwood:
        if t < .5: continue
        name = rnd.choice(['tbush1', 'tbush2', 'tbush3']) if t < .85 else 'stump'
    else:
        if t < .8: continue
        name = rnd.choice(['rock1', 'rock2', 'rock3', 'rock4']) if t < .88 else rnd.choice(['tbush1', 'tbush2', 'tbush3']) if t < .96 else rnd.choice(['toak1', 'toak2'])  # lone oaks: cover on the meadow
    if any(math.hypot(x - px, z - pz) < 3 for px, pz in placed[-1:]): continue
    prop(name, x, z, rnd.random() * 6.28, round(rnd.uniform(.7, 1.3), 2) if name.startswith('rock') else round(rnd.uniform(.8, 1.2), 2))
# props stand on the final ground (the places were flattened after some were laid)
for p in props: p['y'] = round(hat(p['x'], p['z']), 2)

spawns = []
while len(spawns) < 64:
    a = rnd.random() * 2 * math.pi; r = math.sqrt(rnd.random()) * (PLAY - 30); x, z = r * math.cos(a), r * math.sin(a)
    if rough(x, z, 3) < 1.5 and free(x, z, 3) and all(math.hypot(x - p['x'], z - p['z']) > 3 for p in props if p['p'][0] == 't'): spawns.append({'x': x, 'z': z})

out = {'id': 'forest', 'name': 'Oakwood', 'size': SIZE, 'cells': CELLS, 'snowHeight': 999, 'playRadius': PLAY,
       'heights': [round(float(h), 2) for h in H.reshape(-1)], 'mask': [], 'albedo': 'forest_albedo',
       'props': props, 'boxes': [], 'spawns': spawns, 'areas': areas}
json.dump(out, open(f'{OUT}/forest.json', 'w'), separators=(',', ':'))
out_rgb.save(RAW, quality=90)
print('places', [(a['name'], round(a['x']), round(a['z'])) for a in areas]); print('props', len(props), 'heights', round(H.min(), 1), '..', round(H.max(), 1))
