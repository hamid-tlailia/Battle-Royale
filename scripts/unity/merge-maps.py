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
OFF = 165.0          # each map's centre from the middle (north +z, south -z): their play circles meet in the band
SIZE, CELLS = 820.0, 328
RIN = 172.0          # each source map used inside this circle round its own centre (outside: its beach / sea)
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
hN += shiftN; hS += shiftS
w = smooth((Z + BAND) / (2 * BAND))   # 1 north, 0 south
dN = np.hypot(X, Z - OFF); dS = np.hypot(X, Z + OFF)
mN, mS = smooth((RIN - dN) / 35.0), smooth((RIN - dS) / 35.0)   # (inside each map's own play circle)
low = (edgeN + shiftN + edgeS + shiftS) * 0.5 + 1.5 + 3.0 * np.sin(X / 37.0) * np.cos(Z / 53.0) + 2.0 * np.sin(Z / 21.0 + X / 47.0)
hNorth = hN * mN + low * (1 - mN); hSouth = hS * mS + low * (1 - mS)
h = hNorth * w + hSouth * (1 - w)

def moved(m, dz, shift):
    out = {'props': [], 'boxes': [], 'spawns': [], 'areas': []}
    ok = lambda x, z0: math.hypot(x, z0) < RIN - 6
    for p in m['props']:
        z = p['z'] + dz
        if abs(z) < BAND + 6 or not ok(p['x'], p['z']): continue
        q = dict(p); q['z'] = z; q['y'] = p['y'] + shift; out['props'].append(q)
    for b in m['boxes']:
        z = b['z'] + dz
        if abs(z) < BAND + 6 or not ok(b['x'], b['z']): continue
        q = dict(b); q['z'] = z; q['y'] = b['y'] + shift; out['boxes'].append(q)
    for s_ in m['spawns']:
        z = s_['z'] + dz
        if abs(z) < BAND + 6 or not ok(s_['x'], s_['z']): continue
        out['spawns'].append({'x': s_['x'], 'z': z})
    for a_ in m['areas']:
        q = dict(a_); q['z'] = a_['z'] + dz; out['areas'].append(q)
    return out
a, b = moved(N, OFF, shiftN), moved(S, -OFF, shiftS)
spawns = a['spawns'][::2] + b['spawns'][::2]
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
    src = natN if z > 0 else natS
    if not src: continue
    p = dict(src[rngp.integers(len(src))]); p['x'] = round(x, 2); p['z'] = round(z, 2); p['y'] = round(hAt(x, z), 2); p['rot'] = int(rngp.integers(0, 360))
    extra.append(p)
    if len(extra) >= 700: break
mask = [0] * (n * n)
out = {'id': 'frontier', 'name': 'Frontier', 'size': SIZE, 'cells': CELLS, 'snowHeight': 999, 'playRadius': 340.0,
       'heights': [round(float(v), 2) for v in h.ravel()], 'mask': mask, 'albedo': 'frontier_albedo',
       'props': a['props'] + b['props'] + extra, 'boxes': a['boxes'] + b['boxes'], 'spawns': spawns, 'areas': a['areas'] + b['areas']}
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
Image.fromarray(np.clip(col, 0, 255).astype(np.uint8)).save(f'{M}/frontier_albedo.jpg', quality=88)
print('frontier_albedo.jpg', R)

# ---- the lobby card: the two cards side by side, a soft join
L = os.path.join(ROOT, 'UI', 'Lobby')
c1 = Image.open(f'{L}/mapcard_green.png').convert('RGBA'); c2 = Image.open(f'{L}/mapcard_classic.png' if os.path.exists(f'{L}/mapcard_classic.png') else f'{L}/mapcard_green_orig.png').convert('RGBA').resize(c1.size)
W0, H0 = c1.size; m = Image.linear_gradient('L').rotate(90).resize((W0, H0))
card = Image.composite(c1, c2, m); card.save(f'{L}/mapcard_frontier.png'); print('mapcard_frontier.png', card.size)
