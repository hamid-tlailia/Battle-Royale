#!/usr/bin/env python3
"""Sand beaches on the island maps' ground pictures (Resources/Maps/<map>_albedo.jpg).

The game cuts every map's coast at run time (MapBuilder.MakeIsland): from just inside the play circle's edge the ground
eases down into the sea. This paints the same coast onto the map's picture: real beach sand (Poly Haven "Aerial Beach 01",
CC0, 30 m a tile, warmed to a golden tone) where the ground lies within a few metres of the sea, darker wet sand under the
water line; inland stays as it was. Valley (no picture: coloured by height in the game) gets a picture made here the same
way (its height colours + the beaches) and the map points at it.

    python3 scripts/unity/paint-beaches.py <aerial_beach_01_diff_1k.jpg> [--from=<git commit with the unpainted pictures>]
"""
import json, math, subprocess, sys, io
import numpy as np
from PIL import Image

R = 'unity/Assets/LastWar/Resources/Maps/'
SAND = Image.open(sys.argv[1]).convert('RGB')
FROM = next((a[7:] for a in sys.argv if a.startswith('--from=')), '8249f3d')
GOLD = np.array([0.86, 0.78, 0.60])

def coast_params(M, H):
    size, cells = M['size'], M['cells']; half = size / 2; c = size / cells
    xs = -half + np.arange(cells + 1) * c; X, Z = np.meshgrid(xs, xs); Rr = np.hypot(X, Z)
    coast = min(M['playRadius'] - 6, half - 4)
    sea = H[Rr <= coast].min() - 1.2
    return coast, sea, sea - 7

def bilinear(H, M, x, z):
    size, cells = M['size'], M['cells']; half = size / 2; c = size / cells
    fx = np.clip((x + half) / c, 0, cells - 1e-4); fz = np.clip((z + half) / c, 0, cells - 1e-4)
    ix, iz = fx.astype(int), fz.astype(int); u, v = fx - ix, fz - iz
    return (H[iz, ix] * (1 - u) + H[iz, ix + 1] * u) * (1 - v) + (H[iz + 1, ix] * (1 - u) + H[iz + 1, ix + 1] * u) * v

def island_heights(M, H, x, z):
    """the ground after MapBuilder.MakeIsland at web coordinates (x, z) - the same formula"""
    coast, sea, floor = coast_params(M, H)
    land = bilinear(H, M, x, z); r = np.hypot(x, z)
    rs = np.maximum(r, 1e-3); edge = bilinear(H, M, x / rs * coast, z / rs * coast)
    W = np.clip((edge - floor) * 1.6, 40, 150); t = np.clip((r - coast) / W, 0, 1); t = t * t * (3 - 2 * t)
    cy = edge + (floor - edge) * t
    return np.where(r <= coast, land, np.minimum(land, cy)), sea, coast

def sand_tile(px_per_m, W):
    """the sand photo tiled over a W x W picture at its real size (30 m a tile)"""
    n = max(8, int(round(30 * px_per_m)))
    t = np.asarray(SAND.resize((n, n), Image.LANCZOS), dtype=np.float32) / 255
    t = t / t.reshape(-1, 3).mean(0) * GOLD
    reps = W // n + 2
    return np.tile(t, (reps, reps, 1))[:W, :W]

def height_colours(M, h):
    """Valley's colours by height, as MapBuilder.HeightColours draws them"""
    def lerp(a, b, t): t = np.clip(t, 0, 1)[..., None]; return np.array(a) + (np.array(b) - np.array(a)) * t
    il = lambda a, b, v: (v - a) / (b - a)
    col = np.where((h > 18)[..., None], lerp((0.45, 0.47, 0.42), (0.6, 0.6, 0.56), il(18, 40, h)), lerp((0.24, 0.42, 0.18), (0.42, 0.5, 0.26), il(-5, 18, h)))
    return np.where((h > M['snowHeight'])[..., None], np.array([0.9, 0.92, 0.94]), col)

for mid in ['green', 'desert', 'valley', 'classic']:
    M = json.load(open(R + mid + '.json')); n = M['cells'] + 1; H = np.array(M['heights'], dtype=np.float64).reshape(n, n)
    size = M['size']; half = size / 2
    if M.get('albedo'):
        src = subprocess.run(['git', 'show', f'{FROM}:{R}{M["albedo"]}.jpg'], capture_output=True, check=True).stdout
        img = np.asarray(Image.open(io.BytesIO(src)).convert('RGB'), dtype=np.float32) / 255
        Wpx = img.shape[0]
    else:
        Wpx = 2048; img = None
    # pixel (col c, row r) <-> web (x, z): the terrain's uv is ((x + half) / size, 1 - (z + half) / size), row 0 = top = uv.y 1
    g = (np.arange(Wpx) + 0.5) / Wpx * size - half
    X, Z = np.meshgrid(g, g)
    h, sea, coast = island_heights(M, H, X, Z)
    if img is None: img = height_colours(M, h).astype(np.float32)
    above = h - sea; r = np.hypot(X, Z)
    sm = lambda a, b, v: (lambda t: t * t * (3 - 2 * t))(np.clip((v - a) / (b - a), 0, 1))
    # sand: from the coast line out, where the ground is within ~4 m of the water (fading out by 7 m above it); a little
    # sand also on low ground just inside the coast line, so the beach does not start on a hard circle
    w = sm(coast - 10, coast + 4, r) * (1 - sm(3.5, 7.5, above))
    sand = sand_tile(Wpx / size, Wpx)
    wet = np.clip(-above / 4, 0, 1)[..., None]
    sand = sand * (1 - 0.32 * wet) * np.array([1, 1, 1.03]) ** wet
    out = img * (1 - w[..., None]) + sand * w[..., None]
    name = M.get('albedo') or (mid + '_albedo')
    Image.fromarray((np.clip(out, 0, 1) * 255).astype(np.uint8)).save(R + name + '.jpg', quality=90)
    if not M.get('albedo'):
        M['albedo'] = name; json.dump(M, open(R + mid + '.json', 'w'), separators=(',', ':'))
    print(mid, 'coast', round(coast), 'sea', round(sea, 1), 'sand on', round(float((w > 0.5).mean()) * 100, 1), '% of the picture ->', name + '.jpg')
