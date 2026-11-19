// Highlands summit rocks (tors_green.glb): the pieces overhang the slopes (the ground under some of them up to 8 m below
// their underside - from the side they look like slabs floating in the air). The vertices of a piece that face down
// (normal below -0.25), and those of its sides (normal below +0.35) in the lower third of the piece, are brought down to
// EMBED under the ground beneath them, so the rock meets the terrain all round. Nothing is raised; the tops are untouched.
// Then the rocks' collision shape (Data/prefabs.json, "tors_green" trimesh) is rebuilt from this same final geometry, so
// what is seen and what is walked on / shot at match (the old collision mesh was a separate, coarser copy that had not
// followed the earlier lowering either).
// Run: node scripts/unity/drape-tors.mjs, then python3 scripts/unity/extern_textures.py convert tors_green.glb
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

const EMBED = 0.3;
const colV = [], colI = []; let moved = 0, total = 0;
for (const node of doc.getRoot().listNodes()) {
  const mesh = node.getMesh(); if (!mesh) continue; const M = node.getWorldMatrix(); const inv = invert(M);
  for (const prim of mesh.listPrimitives()) {
    const pos = prim.getAttribute('POSITION'), nrm = prim.getAttribute('NORMAL'), idx = prim.getIndices(); const N = pos.getCount();
    const P = [], down = new Array(N).fill(false), side = new Array(N).fill(false);
    for (let i = 0; i < N; i++) { const v = pos.getElement(i, []); P.push([M[0]*v[0]+M[4]*v[1]+M[8]*v[2]+M[12], M[1]*v[0]+M[5]*v[1]+M[9]*v[2]+M[13] + prop.y, M[2]*v[0]+M[6]*v[1]+M[10]*v[2]+M[14]]);
      const n = nrm.getElement(i, []); const ny = M[1]*n[0]+M[5]*n[1]+M[9]*n[2], nl = Math.hypot(M[0]*n[0]+M[4]*n[1]+M[8]*n[2], ny, M[2]*n[0]+M[6]*n[1]+M[10]*n[2]) || 1;
      down[i] = ny / nl < -0.25; side[i] = ny / nl < 0.35; }
    // vertices at one spot (split normals) move together; pieces = connected parts (their own height range)
    const key = new Map(); for (let i = 0; i < N; i++) { const k = P[i].map((x) => x.toFixed(3)).join(','); if (!key.has(k)) key.set(k, []); key.get(k).push(i); }
    const par = [...Array(N).keys()]; const f = (a) => { while (par[a] !== a) a = par[a] = par[par[a]]; return a; };
    const I = idx.getArray(); for (let i = 0; i < I.length; i += 3) { const a = f(I[i]); par[f(I[i + 1])] = a; par[f(I[i + 2])] = a; }
    for (const ids of key.values()) for (const i of ids) par[f(i)] = f(ids[0]);
    const lo = new Map(), hi = new Map(); for (let i = 0; i < N; i++) { const r = f(i); lo.set(r, Math.min(lo.get(r) ?? 1e9, P[i][1])); hi.set(r, Math.max(hi.get(r) ?? -1e9, P[i][1])); }
    for (const ids of key.values()) {
      // what moves: faces turned down (the underside), and the not-upward faces (sides) in the lower third of the piece -
      // the sides then reach down to the ground instead of overhanging the slope
      const r = f(ids[0]), y0 = P[ids[0]][1], low = y0 < lo.get(r) + 0.33 * (hi.get(r) - lo.get(r));
      if (!ids.some((i) => down[i] || (low && side[i]))) continue;
      const [x, y, z] = P[ids[0]]; const g = H(x, z) - EMBED; if (y <= g) continue;
      for (const i of ids) { P[i][1] = g; const w = [P[i][0], P[i][1] - prop.y, P[i][2]];
        pos.setElement(i, [inv[0]*w[0]+inv[4]*w[1]+inv[8]*w[2]+inv[12], inv[1]*w[0]+inv[5]*w[1]+inv[9]*w[2]+inv[13], inv[2]*w[0]+inv[6]*w[1]+inv[10]*w[2]+inv[14]]); }
      moved += ids.length;
    }
    total += N;
    const base = colV.length / 3;
    for (let i = 0; i < N; i++) colV.push(+(P[i][0] - prop.x).toFixed(3), +(P[i][1] - prop.y).toFixed(3), +(P[i][2] - prop.z).toFixed(3));
    for (const k of idx.getArray()) colI.push(base + k);
  }
}
await io.write('unity/Assets/LastWar/Resources/Models/tors_green.glb', doc);
const pf = 'unity/Assets/LastWar/Resources/Data/prefabs.json', pd = JSON.parse(fs.readFileSync(pf));
const sh = pd.shapes.find((s) => s.name === 'tors_green'); const before = sh.v.length / 3;
sh.v = colV; sh.i = colI; fs.writeFileSync(pf, JSON.stringify(pd));
console.log(`moved ${moved} of ${total} vertices down to the ground; collision mesh ${before} -> ${colV.length / 3} vertices, ${colI.length / 3} triangles`);
