#!/usr/bin/env python3
"""
Highlands summit rocks: the one 'tors_green' model (13 jagged overhanging pieces with a detailed collision mesh - soldiers
got wedged in the crevices between them, legs went into them lying down, and from below the overhangs showed gaps) is
replaced in green.json by groups of the map's own boulders (rock1 / rock2 / rock4: simple convex collision, bedded on the
ground under them by MapBuilder.BedRock). Each tors piece's footprint is filled with boulders on a grid of cells about
7 m wide, sized to the cell and to the piece's height, turned at random (fixed seed: the same map every time).
Run: python3 scripts/unity/tors-to-boulders.py <piece boxes json: [[x0,y0,z0,x1,y1,z1], ...]>
"""
import json, math, random, sys, os
MAP = os.path.join(os.path.dirname(__file__), '..', '..', 'unity', 'Assets', 'LastWar', 'Resources', 'Maps', 'green.json')
ROCKS = {'rock1': (2.35, 1.47, 2.40), 'rock2': (3.20, 1.72, 2.98), 'rock4': (2.80, 2.99, 2.54)}  # native size x, y, z (m)
boxes = json.load(open(sys.argv[1]))
d = json.load(open(MAP))
size, cells, H = d['size'], d['cells'], d['heights']; n = cells + 1
def height(x, z):
    cell = size / cells; half = size / 2
    fx = min(max((x + half) / cell, 0), cells - 1e-4); fz = min(max((z + half) / cell, 0), cells - 1e-4)
    ix, iz = int(fx), int(fz); u, v = fx - ix, fz - iz; h = lambda a, b: H[b * n + a]
    return (h(ix, iz) * (1 - u) + h(ix + 1, iz) * u) * (1 - v) + (h(ix, iz + 1) * (1 - u) + h(ix + 1, iz + 1) * u) * v
rnd = random.Random(7)
props = [p for p in d['props'] if p['p'] != 'tors_green']
added = []
for (x0, y0, z0, x1, y1, z1) in boxes:
    w, dz = x1 - x0, z1 - z0
    nx, nz = max(1, round(w / 7)), max(1, round(dz / 7))
    cw, cd = w / nx, dz / nz
    for i in range(nx):
        for k in range(nz):
            cx, cz = x0 + (i + 0.5) * cw, z0 + (k + 0.5) * cd
            g = height(cx, cz)
            # the boulder is bedded to the lowest ground under it (BedRock): sized from there up to the piece's top, so
            # on a steep slope it still stands as high as the rock did (at most 8x, at most 1.6 cells wide)
            gmin = min(height(cx + a * cw / 2, cz + b * cd / 2) for a in (-1, 0, 1) for b in (-1, 0, 1))
            top = y1 - gmin
            if top < 1.0: continue
            name = rnd.choice(list(ROCKS)); rx, ry, rz = ROCKS[name]
            s = min(top * 0.95 / ry, max(cw, cd) * 1.6 / max(rx, rz), 8.0)
            s = round(max(1.2, s) * rnd.uniform(0.9, 1.1), 2)
            added.append({'p': name, 'x': round(cx + rnd.uniform(-0.15, 0.15) * cw, 2), 'y': round(g, 2), 'z': round(cz + rnd.uniform(-0.15, 0.15) * cd, 2), 'rot': round(rnd.uniform(0, 6.283), 3), 's': s})
d['props'] = props + added
json.dump(d, open(MAP, 'w'), separators=(',', ':'))
print('tors pieces', len(boxes), '-> boulders', len(added), '| scale', min(a['s'] for a in added), '-', max(a['s'] for a in added))
