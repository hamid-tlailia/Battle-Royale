// Highlands summit rocks (tors_green.glb, one model of 13 separate rock pieces placed at one height): each piece is lowered
// until its lowest point is EMBED below the terrain under it (green.json heights) - pieces floated up to 1.2 m on the
// slopes. Pieces already that deep are left alone; nothing is raised. Prints each piece's gap before / after.
// Run: node scripts/unity/bed-tors.mjs, then python3 scripts/unity/extern_textures.py convert tors_green.glb (writing the
// model puts its picture back inside it; that moves it out to its texture file again, unchanged)
import fs from 'node:fs';
function invert(m) { // 4x4 column-major inverse (affine)
  const a = m, inv = new Array(16);
  inv[0] = a[5]*a[10]-a[6]*a[9]; inv[1] = a[2]*a[9]-a[1]*a[10]; inv[2] = a[1]*a[6]-a[2]*a[5];
  inv[4] = a[6]*a[8]-a[4]*a[10]; inv[5] = a[0]*a[10]-a[2]*a[8]; inv[6] = a[2]*a[4]-a[0]*a[6];
  inv[8] = a[4]*a[9]-a[5]*a[8]; inv[9] = a[1]*a[8]-a[0]*a[9]; inv[10] = a[0]*a[5]-a[1]*a[4];
  const det = a[0]*inv[0] + a[1]*inv[4] + a[2]*inv[8];
  for (const i of [0, 1, 2, 4, 5, 6, 8, 9, 10]) inv[i] /= det;
  inv[3] = inv[7] = inv[11] = 0; inv[15] = 1;
  inv[12] = -(inv[0]*a[12] + inv[4]*a[13] + inv[8]*a[14]); inv[13] = -(inv[1]*a[12] + inv[5]*a[13] + inv[9]*a[14]); inv[14] = -(inv[2]*a[12] + inv[6]*a[13] + inv[10]*a[14]);
  return inv; }
import { NodeIO } from '@gltf-transform/core';
import { ALL_EXTENSIONS } from '@gltf-transform/extensions';
const io = new NodeIO().registerExtensions(ALL_EXTENSIONS);
const map = JSON.parse(fs.readFileSync('unity/Assets/LastWar/Resources/Maps/green.json'));
const prop = map.props.find((p) => p.p === 'tors_green');
const { size, cells, heights } = map; const n = cells + 1;
const H = (x, z) => { const cell = size / cells, half = size / 2; const fx = Math.min(Math.max((x + half) / cell, 0), cells - 1e-4), fz = Math.min(Math.max((z + half) / cell, 0), cells - 1e-4);
  const ix = Math.floor(fx), iz = Math.floor(fz), u = fx - ix, v = fz - iz; const h = (a, b) => heights[b * n + a];
  return (h(ix, iz) * (1 - u) + h(ix + 1, iz) * u) * (1 - v) + (h(ix, iz + 1) * (1 - u) + h(ix + 1, iz + 1) * u) * v; };
const doc = await io.read('unity/Assets/LastWar/Resources/Models/tors_green.glb');
const out = []; const EMBED = 0.3;
for (const node of doc.getRoot().listNodes()) {
  const mesh = node.getMesh(); if (!mesh) continue; const M = node.getWorldMatrix();
  for (const prim of mesh.listPrimitives()) {
    const pos = prim.getAttribute('POSITION'), idx = prim.getIndices(); const N = pos.getCount();
    const P = []; for (let i = 0; i < N; i++) { const v = pos.getElement(i, []); P.push([M[0]*v[0]+M[4]*v[1]+M[8]*v[2]+M[12], M[1]*v[0]+M[5]*v[1]+M[9]*v[2]+M[13] + prop.y, M[2]*v[0]+M[6]*v[1]+M[10]*v[2]+M[14]]); }
    const par = [...Array(N).keys()]; const f = (a) => { while (par[a] !== a) a = par[a] = par[par[a]]; return a; };
    const I = idx ? idx.getArray() : [...Array(N).keys()];
    for (let i = 0; i < I.length; i += 3) { const a = f(I[i]), b = f(I[i + 1]), c = f(I[i + 2]); par[b] = a; par[f(c)] = a; }
    // also join vertices at the same position (split normals)
    const key = new Map(); for (let i = 0; i < N; i++) { const k = P[i].map((x) => x.toFixed(3)).join(','); if (key.has(k)) par[f(i)] = f(key.get(k)); else key.set(k, i); }
    const comp = new Map(); for (let i = 0; i < N; i++) { const r = f(i); if (!comp.has(r)) comp.set(r, []); comp.get(r).push(i); }
    for (const [r, vs] of comp) { let gapMin = 1e9, lowY = 1e9, hi = -1e9; let cx = 0, cz = 0;
      for (const i of vs) { const [x, y, z] = P[i]; gapMin = Math.min(gapMin, y - H(x, z)); lowY = Math.min(lowY, y); hi = Math.max(hi, y); cx += x; cz += z; }
      const drop = Math.max(0, gapMin + EMBED);
      if (drop > 0) { // lower the piece in the mesh's own space (the node's matrix maps world-y back through its inverse)
        const inv = invert(M);
        for (const i of vs) { const v = pos.getElement(i, []); const w = [P[i][0], P[i][1] - prop.y - drop, P[i][2]];
          pos.setElement(i, [inv[0]*w[0]+inv[4]*w[1]+inv[8]*w[2]+inv[12], inv[1]*w[0]+inv[5]*w[1]+inv[9]*w[2]+inv[13], inv[2]*w[0]+inv[6]*w[1]+inv[10]*w[2]+inv[14]]); }
      }
      out.push({ n: vs.length, gap: gapMin, after: gapMin - drop, h: hi - lowY, x: cx / vs.length, z: cz / vs.length }); }
  }
}
await io.write('unity/Assets/LastWar/Resources/Models/tors_green.glb', doc);
out.sort((a, b) => b.gap - a.gap);
console.log('pieces', out.length, '| floating > 0.1 m:', out.filter((o) => o.gap > 0.1).length, '| worst:');
for (const o of out) console.log(`  at (${o.x.toFixed(0)}, ${o.z.toFixed(0)}): lowest point ${o.gap.toFixed(2)} -> ${o.after.toFixed(2)} m vs the ground, rock ${o.h.toFixed(1)} m tall`);
