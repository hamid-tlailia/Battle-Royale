#!/usr/bin/env python3
"""Season looks for the green maps (they looked alike): Highlands under snow, Oakwood in autumn, Valley as it is.

Writes, from the game's own textures:
  Resources/Maps/<albedo>_<look>.jpg          the ground (green_albedo_snow, forest_albedo_autumn)
  Resources/Maps/detail_snow.png, detail_grass_autumn.png   the close-up grain
  Resources/Looks/<look>/<model>_<tex>.png    tree / bush leaves (MapBuilder.LookMat swaps them in by name)
Snow: the ground white where it was grass, rock and paths showing through in part; every leaf dusted with snow.
Autumn: grass turned golden / dry; oak and bush leaves orange, red and yellow (one colour per model, each with its own
shades); pines stay green (evergreen).

  python3 scripts/unity/map-looks.py
"""
import os, colorsys
import numpy as np
from PIL import Image

ROOT = os.path.join(os.path.dirname(__file__), '..', '..', 'unity', 'Assets', 'LastWar')
MAPS = os.path.join(ROOT, 'Resources', 'Maps'); TEX = os.path.join(ROOT, 'Textures'); LOOKS = os.path.join(ROOT, 'Resources', 'Looks')
rng = np.random.default_rng(7)


def hsv(a):
    """float RGB (h, w, 3) 0..1 -> h, s, v arrays"""
    mx, mn = a.max(2), a.min(2); d = mx - mn + 1e-6; r, g, b = a[..., 0], a[..., 1], a[..., 2]
    h = np.where(mx == r, ((g - b) / d) % 6, np.where(mx == g, (b - r) / d + 2, (r - g) / d + 4)) / 6.0
    s = np.where(mx > 0, (mx - mn) / (mx + 1e-6), 0); return h, s, mx


def rgb(h, s, v):
    i = np.floor(h * 6).astype(int) % 6; f = h * 6 - np.floor(h * 6)
    p, q, t = v * (1 - s), v * (1 - f * s), v * (1 - (1 - f) * s)
    out = np.zeros(h.shape + (3,))
    for k, (r, g, b) in enumerate([(v, t, p), (q, v, p), (p, v, t), (p, q, v), (t, p, v), (v, p, q)]):
        m = i == k; out[m, 0], out[m, 1], out[m, 2] = r[m], g[m], b[m]
    return out


def noise(h, w, scale=48):
    """soft blotches 0..1 (patches of colour, not a flat tint)"""
    n = rng.random((max(2, h // scale), max(2, w // scale)))
    return np.asarray(Image.fromarray((n * 255).astype(np.uint8)).resize((w, h), Image.BICUBIC), np.float32) / 255


def greenness(a):
    h, s, v = hsv(a)
    return np.clip((s - 0.08) / 0.25, 0, 1) * np.clip(1 - np.abs(h - 0.27) / 0.16, 0, 1)  # (hue ~ 100 degrees: grass / leaves)


def snow_ground(a):
    g = greenness(a); lum = a.mean(2, keepdims=True)
    amt = np.clip(0.55 + 0.4 * g[..., None] + 0.15 * (noise(*a.shape[:2], 64)[..., None] - 0.5), 0, 0.97)
    white = np.array([0.9, 0.93, 0.97]) * (0.82 + 0.3 * lum)  # (the ground's own shading kept in the snow)
    navy = (a[..., 2] > a[..., 0] + 0.02) & (lum[..., 0] < 0.25)  # (the dark sea floor round the island stays)
    amt[navy] = 0
    return a * (1 - amt) + white * amt


def autumn_ground(a):
    h, s, v = hsv(a); g = greenness(a); n = noise(*a.shape[:2], 80)
    th = 0.09 + 0.05 * n  # golden-yellow to straw
    h2 = h * (1 - g) + th * g; s2 = s * (1 - g) + np.clip(s * 1.05, 0, 0.75) * g; v2 = v * (1 - g * 0.05)
    return rgb(h2 % 1, s2, v2)


def snow_leaves(a):
    lum = a[..., :3].mean(2, keepdims=True)
    up = noise(*a.shape[:2], 24)[..., None]
    amt = np.clip(0.45 + 0.35 * up, 0, 0.85)
    out = a.copy(); out[..., :3] = a[..., :3] * (1 - amt) + np.array([0.9, 0.93, 0.98]) * (0.75 + 0.4 * lum) * amt
    return out


def autumn_leaves(a, hue):
    h, s, v = hsv(a[..., :3]); n = noise(*a.shape[:2], 20)
    g = np.clip(greenness(a[..., :3]) * 1.4, 0, 1)
    h2 = h * (1 - g) + (hue + 0.05 * (n - 0.5)) * g
    s2 = s * (1 - g) + np.clip(s * 1.4 + 0.2, 0, 0.95) * g; v2 = v * (1 - g) + np.clip(v * 1.25, 0, 1) * g
    out = a.copy(); out[..., :3] = rgb(h2 % 1, s2, v2); return out


def load(p):
    im = Image.open(p); mode = im.mode
    return np.asarray(im.convert('RGBA' if mode == 'RGBA' else 'RGB'), np.float32) / 255, mode


def save(a, p, mode='RGB', q=90):
    os.makedirs(os.path.dirname(p), exist_ok=True)
    im = Image.fromarray((np.clip(a, 0, 1) * 255).astype(np.uint8), 'RGBA' if a.shape[2] == 4 else 'RGB')
    im.save(p, quality=q) if p.endswith('.jpg') else im.save(p); print('wrote', os.path.relpath(p, ROOT), im.size)


# the ground
a, _ = load(f'{MAPS}/green_albedo.jpg'); save(snow_ground(a), f'{MAPS}/green_albedo_snow.jpg')
a, _ = load(f'{MAPS}/forest_albedo.jpg'); save(autumn_ground(a), f'{MAPS}/forest_albedo_autumn.jpg')
a, _ = load(f'{MAPS}/detail_grass.png'); save(snow_ground(a), f'{MAPS}/detail_snow.png'); save(autumn_ground(a), f'{MAPS}/detail_grass_autumn.png')
# the leaves (texture 1 of each tree / bush model; 0 is the bark)
AUTUMN = {'toak1': 0.07, 'toak2': 0.02, 'tbush1': 0.12, 'tbush2': 0.075, 'tbush3': 0.0}  # orange, red-orange, yellow, orange, red
for model in ['tpine1', 'tpine2', 'tpine3', 'tpine4', 'toak1', 'toak2', 'tbush1', 'tbush2', 'tbush3']:
    src = f'{TEX}/{model}/1_c.png'
    if not os.path.exists(src): continue
    a, _ = load(src)
    save(snow_leaves(a), f'{LOOKS}/snow/{model}_1_c.png')
    if model in AUTUMN: save(autumn_leaves(a, AUTUMN[model]), f'{LOOKS}/autumn/{model}_1_c.png')

# the lobby's map cards in the same seasons (their pictures were green as well)
UI = os.path.join(ROOT, 'Resources', 'UI', 'Lobby')
for card, fn in (('mapcard_green', snow_ground), ('mapcard_forest', autumn_ground)):
    src = f'{UI}/{card}_orig.png'
    if not os.path.exists(src): os.rename(f'{UI}/{card}.png', src)  # (the green original kept beside it)
    a, mode = load(src)
    out = a.copy(); out[..., :3] = fn(a[..., :3]); save(out, f'{UI}/{card}.png')
