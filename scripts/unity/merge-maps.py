#!/usr/bin/env python3
"""Frontier: Highlands (snowy, north) and Valley (green, south) joined into one bigger island.

Highlands (green.json, its snow ground photo) is laid at z +OFF, Valley (classic.json) at z -OFF; between them a band
where the ground and the photo blend from one into the other (the snow thinning out into grass); past the two maps' sides
a smooth lowland (each side's edge eased down) so the round island has ground everywhere. Props, crates, spawns and
area names come from both, moved with their map (none inside the blend band, where the ground was reshaped).

  python3 scripts/unity/merge-maps.py
writes Resources/Maps/frontier.json, frontier_albedo.jpg and Resources/UI/Lobby/mapcard_frontier.png
"""
import json, os, math
import numpy as np
from PIL import Image

ROOT = os.path.join(os.path.dirname(__file__), '..', '..', 'unity', 'Assets', 'LastWar', 'Resources')
M = os.path.join(ROOT, 'Maps')
N, S = json.load(open(f'{M}/green.json')), json.load(open(f'{M}/classic.json'))
OFF = 192.0          # each map's centre from the middle (north +z, south -z): their play circles meet in the band
SIZE, CELLS = 820.0, 328
RIN = 152.0          # each source map used inside this circle round its own centre (outside: its beach / sea)
BAND = 35.0          # half width of the blend between them
HALF_SRC = 220.0

def grid(m):
    n = m['cells'] + 1; return np.array(m['heights'], np.float32).reshape(n, n)

def sample(m, g, x, z):
    """bilinear height of map m at its own coords (arrays), clamped at its edge"""
    size = m['size']; cells = m['cells']; cell = size / cells; half = size / 2
    fx = np.clip((x + half) / cell, 0, cells - 1e-4); fz = np.clip((z + half) / cell, 0, cells - 1e-4)
    ix, iz = fx.astype(int), fz.astype(int); u, v = fx - ix, fz - iz
    h00, h10, h01, h11 = g[iz, ix], g[iz, ix + 1], g[iz + 1, ix], g[iz + 1, ix + 1]
    return (h00 * (1 - u) + h10 * u) * (1 - v) + (h01 * (1 - u) + h11 * u) * v

def smooth(t): t = np.clip(t, 0, 1); return t * t * (3 - 2 * t)

gN, gS = grid(N), grid(S)
n = CELLS + 1; cell = SIZE / CELLS
xs = -SIZE / 2 + np.arange(n) * cell
X, Z = np.meshgrid(xs, xs)            # [iz, ix]
hN = sample(N, gN, X, Z - OFF); hS = sample(S, gS, X, Z + OFF)
# the two maps sit at different levels: the south raised / lowered so the band joins (median edge heights)
edgeN = np.median(gN[:8, :]); edgeS = np.median(gS[-8:, :]); shiftS = float(edgeN - edgeS) * 0.5; shiftN = -shiftS
medN, medS = float(np.median(gN)), float(np.median(gS))
LAND = 3.5           # the plateau's height above the sea (sea level 0)
def flat(hh, med, k): return LAND + np.clip((hh - med) * k, -0.6, 14.0)
hN, hS = flat(hN, medN, 0.45), flat(hS, medS, 0.3)          # (the north keeps more of its hills: snowy highlands)
w = smooth((Z + BAND) / (2 * BAND))   # 1 north, 0 south
dN = np.hypot(X, Z - OFF); dS = np.hypot(X, Z + OFF)
mN, mS = smooth((RIN - dN) / 35.0), smooth((RIN - dS) / 35.0)
low = LAND + 0.8 * np.sin(X / 37.0) * np.cos(Z / 53.0) + 0.6 * np.sin(Z / 21.0 + X / 47.0)
hNorth = hN * mN + low * (1 - mN); hSouth = hS * mS + low * (1 - mS)
h = hNorth * w + hSouth * (1 - w)
# 2) water (sea level 0, 1.6 m deep): a strait between the snowy north and the green south with a small island in its
# middle, and a river winding down through the south splitting it in two; sandy banks
STRAIT, ISLE = 44.0, 30.0
def rivx(z): return 22.0 * np.sin(z / 47.0) + 8.0 * np.sin(z / 19.0)
dist_strait = np.abs(Z) - STRAIT                         # <0 inside the strait
dist_isle = ISLE - np.hypot(X, Z)                         # >0 on the island
dist_river = np.where(Z < -STRAIT + 6, np.abs(X - rivx(Z)) - 15.0, 99.0)   # <0 in the river
water = np.minimum(np.where(dist_isle > 0, 99.0, dist_strait), dist_river)  # signed: <0 water, >0 land
bank = smooth((water + 2.0) / 10.0)                       # 0 in the water .. 1 ten metres up the bank
h = -1.6 * (1 - bank) + h * bank
h = np.where(dist_isle > 0, np.maximum(h, LAND * smooth(dist_isle / 10.0) + -1.6 * (1 - smooth(dist_isle / 10.0))), h)
SEA = 0.0
def groundAt(arr, x, z):
    fx = np.clip((x + SIZE / 2) / cell, 0, CELLS - 1e-4); fz = np.clip((z + SIZE / 2) / cell, 0, CELLS - 1e-4)
    ix, iz = int(fx), int(fz); u, v = fx - ix, fz - iz
    return float((arr[iz, ix] * (1 - u) + arr[iz, ix + 1] * u) * (1 - v) + (arr[iz + 1, ix] * (1 - u) + arr[iz + 1, ix + 1] * u) * v)

def moved(m, g, dz):
    out = {'props': [], 'boxes': [], 'spawns': [], 'areas': []}
    ok = lambda x, z0: math.hypot(x, z0) < RIN - 6
    def dry(x, z): return groundAt(h, x, z) > SEA + 0.8
    def reseat(o, z):
        old = float(sample(m, g, np.array([o['x']]), np.array([o['z']]))[0])
        q = dict(o); q['z'] = z; q['y'] = round(groundAt(h, o['x'], z) + (o['y'] - old), 2); return q
    for p in m['props']:
        z = p['z'] + dz
        if not ok(p['x'], p['z']) or not dry(p['x'], z): continue
        out['props'].append(reseat(p, z))
    for b_ in m['boxes']:
        z = b_['z'] + dz
        if not ok(b_['x'], b_['z']) or not dry(b_['x'], z): continue
        out['boxes'].append(reseat(b_, z))
    for s_ in m['spawns']:
        z = s_['z'] + dz
        if not ok(s_['x'], s_['z']) or not dry(s_['x'], z): continue
        out['spawns'].append({'x': s_['x'], 'z': z})
    for a_ in m['areas']:
        q = dict(a_); q['z'] = a_['z'] + dz; out['areas'].append(q)
    return out
a, b = moved(N, gN, OFF), moved(S, gS, -OFF)
spawns = a['spawns'][::2] + b['spawns'][::2]
# area names: none over the water; the south's names that the north already has renamed (two Brookends, two Moorgates)
NEW = iter(['Riverside', 'Southgate', 'Fernhill', 'Willowby', 'Ashford'])
seen = set(); areas = []
for ar in a['areas'] + b['areas']:
    if groundAt(h, ar['x'], ar['z']) < SEA + 0.8: continue
    if ar['name'] in seen: ar = dict(ar); ar['name'] = next(NEW)
    seen.add(ar['name']); areas.append(ar)
# the open land round them: trees, bushes and rocks of the near map scattered about (not bare)
rngp = np.random.default_rng(11)
NATURE = ('tpine', 'toak', 'tbush', 'rock', 'stump')
natN = [p for p in N['props'] if p['p'].startswith(NATURE)]; natS = [p for p in S['props'] if p['p'].startswith(NATURE)]
extra = []
def hAt(x, z):
    fx = np.clip((x + SIZE / 2) / cell, 0, CELLS - 1e-4); fz = np.clip((z + SIZE / 2) / cell, 0, CELLS - 1e-4)
    ix, iz = int(fx), int(fz); u, v = fx - ix, fz - iz
    return float((h[iz, ix] * (1 - u) + h[iz, ix + 1] * u) * (1 - v) + (h[iz + 1, ix] * (1 - u) + h[iz + 1, ix + 1] * u) * v)
for k in range(2600):
    x, z = rngp.uniform(-330, 330), rngp.uniform(-345, 345)
    if math.hypot(x, z) > 335: continue
    if math.hypot(x, z - OFF) < RIN + 4 or math.hypot(x, z + OFF) < RIN + 4: continue
    if hAt(x, z) < SEA + 0.8: continue
    src = natN if z > 0 else natS
    if not src: continue
    p = dict(src[rngp.integers(len(src))]); p['x'] = round(x, 2); p['z'] = round(z, 2); p['y'] = round(hAt(x, z), 2); p['rot'] = int(rngp.integers(0, 360))
    extra.append(p)
    if len(extra) >= 700: break
mask = [0] * (n * n)
bridges = [{'x1': 0.0, 'z1': ISLE - 6, 'x2': 0.0, 'z2': STRAIT + 14, 'w': 7.0},
           {'x1': 0.0, 'z1': -(ISLE - 6), 'x2': 0.0, 'z2': -(STRAIT + 14), 'w': 7.0}]
for zb in (-150.0, -260.0):
    xc = float(rivx(zb)); bridges.append({'x1': xc - 26, 'z1': zb, 'x2': xc + 26, 'z2': zb, 'w': 6.0})
# and two longer crossings of the strait out east and west
for xb in (-210.0, 210.0): bridges.append({'x1': xb, 'z1': -(STRAIT + 12), 'x2': xb, 'z2': STRAIT + 12, 'w': 6.0})
out = {'id': 'frontier', 'name': 'Frontier', 'size': SIZE, 'cells': CELLS, 'snowHeight': 999, 'playRadius': 340.0,
       'heights': [round(float(v), 2) for v in h.ravel()], 'mask': mask, 'albedo': 'frontier_albedo',
       'props': a['props'] + b['props'] + extra, 'seaLevel': SEA, 'bridges': bridges, 'boxes': a['boxes'] + b['boxes'], 'spawns': spawns, 'areas': areas}
json.dump(out, open(f'{M}/frontier.json', 'w'), separators=(',', ':'))
print('frontier.json', len(out['props']), 'props', len(out['boxes']), 'boxes', len(spawns), 'spawns', len(out['areas']), 'areas',
      'heights', float(h.min()), float(h.max()))

# ---- the ground photo: rows grow with z, columns with x (MapBuilder.BuildTerrain's uv)
R = 2048
aN = np.asarray(Image.open(f'{M}/green_albedo_snow.jpg').convert('RGB'), np.float32)
aS = np.asarray(Image.open(f'{M}/classic_albedo.jpg').convert('RGB'), np.float32)
px = -SIZE / 2 + (np.arange(R) + 0.5) / R * SIZE
PX, PZ = np.meshgrid(px, px)          # [row(z), col(x)]
def photo(img, x, z):
    H, W = img.shape[:2]
    c = np.clip(((x + HALF_SRC) / (2 * HALF_SRC) * W).astype(int), 0, W - 1)
    r = np.clip(((z + HALF_SRC) / (2 * HALF_SRC) * H).astype(int), 0, H - 1)
    return img[r, c]
cN, cS = photo(aN, PX, PZ - OFF), photo(aS, PX, PZ + OFF)
rng = np.random.default_rng(5)
nz = np.asarray(Image.fromarray((rng.random((64, 64)) * 255).astype(np.uint8)).resize((R, R), Image.BICUBIC), np.float32) / 255
wp = smooth((PZ + BAND + (nz - 0.5) * 40) / (2 * BAND))[..., None]   # (the snow thinning out in patches into the grass)
# outside the circles: the map's own ground colour, soft and patchy (its inner ground blurred - no beach, no sea ring,
# no repeated features), the photo eased into it at the circle's edge
from PIL import ImageFilter
def soft(img):
    H, W = img.shape[:2]; c = img[H // 4: 3 * H // 4, W // 4: 3 * W // 4]
    small = Image.fromarray(c.astype(np.uint8)).resize((48, 48), Image.BILINEAR).filter(ImageFilter.GaussianBlur(2))
    return np.asarray(small.resize((R, R), Image.BICUBIC), np.float32)
grain = np.asarray(Image.fromarray((rng.random((512, 512)) * 255).astype(np.uint8)).resize((R, R), Image.BILINEAR), np.float32)[..., None] / 255
fN, fS = soft(aN) * (0.95 + 0.1 * grain), soft(aS) * (0.95 + 0.1 * grain)
dNp = np.hypot(PX, PZ - OFF); dSp = np.hypot(PX, PZ + OFF)
mNp, mSp = smooth((RIN - dNp) / 30.0)[..., None], smooth((RIN - dSp) / 30.0)[..., None]
col = (cN * mNp + fN * (1 - mNp)) * wp + (cS * mSp + fS * (1 - mSp)) * (1 - wp)
HH = np.asarray(Image.fromarray(h.astype(np.float32), 'F').resize((R, R), Image.BILINEAR))
sand = np.array([196, 178, 136], np.float32) * (0.92 + 0.12 * grain)
sandw = smooth((SEA + 1.4 - HH) / 1.2)[..., None]
col = col * (1 - sandw) + sand * sandw
# under the water: the water's own blue (the small map shows the straits and the river as water, not sand)
deep = smooth((SEA - 0.2 - HH) / 0.8)[..., None]
col = col * (1 - deep) + np.array([38, 112, 150], np.float32) * deep
Image.fromarray(np.clip(col, 0, 255).astype(np.uint8)).save(f'{M}/frontier_albedo.jpg', quality=88)
print('frontier_albedo.jpg', R)

# ---- the lobby card: the two cards side by side, a soft join
L = os.path.join(ROOT, 'UI', 'Lobby')
c1 = Image.open(f'{L}/mapcard_green.png').convert('RGBA'); c2 = Image.open(f'{L}/mapcard_classic.png' if os.path.exists(f'{L}/mapcard_classic.png') else f'{L}/mapcard_green_orig.png').convert('RGBA').resize(c1.size)
W0, H0 = c1.size; m = Image.linear_gradient('L').rotate(90).resize((W0, H0))
card = Image.composite(c1, c2, m); card.save(f'{L}/mapcard_frontier.png'); print('mapcard_frontier.png', card.size)
