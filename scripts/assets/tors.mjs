// Cuts the real rock outcrops ("tors") out of the green-map terrain scan: every triangle that rises
// clearly above the smoothed height grid. Output: client/public/assets/tors_green.glb (+ collider in
// shared/src/map/tors_green.json), in map coordinates relative to the scan base (y = scan height - lowest).
import fs from 'node:fs';
import { NodeIO } from '@gltf-transform/core';
import { ALL_EXTENSIONS } from '@gltf-transform/extensions';
import { prune, dedup, weld, simplifyPrimitive, textureCompress, meshopt } from '@gltf-transform/functions';
import { MeshoptEncoder, MeshoptSimplifier } from 'meshoptimizer';
import sharp from 'sharp';
await MeshoptEncoder.ready; await MeshoptSimplifier.ready;
const io = new NodeIO().registerExtensions(ALL_EXTENSIONS).registerDependencies({ 'meshopt.encoder': MeshoptEncoder });
const src = fs.readFileSync('shared/src/map/data/green.ts', 'utf8');
const cells = +src.match(/cells: (\d+)/)[1], size = +src.match(/size: (\d+)/)[1];
const hb = Buffer.from(src.match(/heights: '([^']+)'/)[1], 'base64');
const n = cells + 1, cell = size / cells;
const mb = Buffer.from(src.match(/mask: '([^']+)'/)[1], 'base64');
const maskAt = (x, z) => { const ix = Math.round((x + size / 2) / cell), iz = Math.round((z + size / 2) / cell); return ix < 0 || iz < 0 || ix >= n || iz >= n ? 0 : mb[iz * n + ix] / 255; };
const grid = (x, z) => { // bilinear on the smoothed scan grid (map coords)
  const fx = Math.max(0, Math.min(cells - 1e-6, (x + size / 2) / cell)), fz = Math.max(0, Math.min(cells - 1e-6, (z + size / 2) / cell));
  const ix = Math.floor(fx), iz = Math.floor(fz), u = fx - ix, v = fz - iz;
  const h = (a, b) => hb.readUInt16LE(((iz + b) * n + ix + a) * 2) / 100;
  return h(0, 0) * (1 - u) * (1 - v) + h(1, 0) * u * (1 - v) + h(0, 1) * (1 - u) * v + h(1, 1) * u * v;
};
const CX = -16.6, CZ = -6.8;
const doc = await io.read('assets-src/maps/dartmoor.glb');
const root = doc.getRoot(), buffer = root.listBuffers()[0];
// lowest scan point (same base as heightmap.mjs)
let lo = Infinity;
for (const node of root.listNodes()) { const mesh = node.getMesh(); if (!mesh) continue; const m = node.getWorldMatrix();
  for (const p of mesh.listPrimitives()) { const a = p.getAttribute('POSITION'), v = [0, 0, 0]; for (let i = 0; i < a.getCount(); i++) { a.getElement(i, v); lo = Math.min(lo, m[1] * v[0] + m[5] * v[1] + m[9] * v[2] + m[13]); } } }
const pos = [], uv = [], idx = [];
let mat = null;
for (const node of root.listNodes()) {
  const mesh = node.getMesh(); if (!mesh) continue; const m = node.getWorldMatrix();
  for (const p of mesh.listPrimitives()) {
    mat = p.getMaterial();
    const a = p.getAttribute('POSITION'), t = p.getAttribute('TEXCOORD_0'), ind = p.getIndices();
    const W = [], v = [0, 0, 0], tv = [0, 0];
    for (let i = 0; i < a.getCount(); i++) {
      a.getElement(i, v);
      const x = m[0] * v[0] + m[4] * v[1] + m[8] * v[2] + m[12] - CX, y = m[1] * v[0] + m[5] * v[1] + m[9] * v[2] + m[13] - lo, z = m[2] * v[0] + m[6] * v[1] + m[10] * v[2] + m[14] - CZ;
      W.push([x, y, z, y - grid(x, z)]);
    }
    const cnt = ind ? ind.getCount() : a.getCount();
    for (let k = 0; k < cnt; k += 3) {
      const tri = [0, 1, 2].map((j) => (ind ? ind.getScalar(k + j) : k + j));
      if (Math.max(...tri.map((q) => W[q][3])) < 1.2) continue; // part of the rolling ground
      const [qx, , qz] = W[tri[0]];
      if (Math.hypot(qx, qz) > 175 || maskAt(qx, qz) < 0.95) continue; // scan edges / outside the valley
      for (const q of tri) { pos.push(W[q][0], W[q][1], W[q][2]); t.getElement(q, tv); uv.push(tv[0], tv[1]); idx.push(idx.length); }
    }
  }
}
console.log('tor triangles', idx.length / 3, 'base', lo.toFixed(2));
const prim = doc.createPrimitive()
  .setAttribute('POSITION', doc.createAccessor().setType('VEC3').setArray(new Float32Array(pos)).setBuffer(buffer))
  .setAttribute('TEXCOORD_0', doc.createAccessor().setType('VEC2').setArray(new Float32Array(uv)).setBuffer(buffer))
  .setIndices(doc.createAccessor().setType('SCALAR').setArray(new Uint32Array(idx)).setBuffer(buffer))
  .setMaterial(mat);
for (const s of root.listScenes()) for (const c of s.listChildren()) s.removeChild(c);
root.listNodes().forEach((x) => x.dispose());
const out = doc.createMesh('tors').addPrimitive(prim);
root.listScenes()[0].addChild(doc.createNode('prefab').setMesh(out));
await doc.transform(weld({ tolerance: 0.0001 }));
const target = 40000;
simplifyPrimitive(prim, { simplifier: MeshoptSimplifier, ratio: Math.min(1, target / (prim.getIndices().getCount() / 3)), error: 0.002, lockBorder: true });
// normals for lighting
const P = prim.getAttribute('POSITION').getArray(), I = prim.getIndices().getArray(), N = new Float32Array(P.length);
for (let i = 0; i < I.length; i += 3) {
  const [a, b, c] = [I[i] * 3, I[i + 1] * 3, I[i + 2] * 3];
  const ux = P[b] - P[a], uy = P[b + 1] - P[a + 1], uz = P[b + 2] - P[a + 2], vx = P[c] - P[a], vy = P[c + 1] - P[a + 1], vz = P[c + 2] - P[a + 2];
  const nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
  for (const q of [a, b, c]) { N[q] += nx; N[q + 1] += ny; N[q + 2] += nz; }
}
for (let i = 0; i < N.length; i += 3) { const l = Math.hypot(N[i], N[i + 1], N[i + 2]) || 1; N[i] /= l; N[i + 1] /= l; N[i + 2] /= l; }
prim.setAttribute('NORMAL', doc.createAccessor().setType('VEC3').setArray(N).setBuffer(buffer));
mat.setNormalTexture(null).setMetallicRoughnessTexture(null).setOcclusionTexture(null).setMetallicFactor(0).setRoughnessFactor(1).setDoubleSided(true);
// collider: coarser copy
const ci = Array.from(I), cv = Array.from(P);
const [simp] = MeshoptSimplifier.simplify(new Uint32Array(ci), new Float32Array(cv), 3, Math.floor(ci.length * 0.25), 0.01, ['LockBorder']);
const remap = new Map(), v2 = [], i2 = [];
for (const q of simp) { if (!remap.has(q)) { remap.set(q, v2.length / 3); v2.push(cv[q * 3], cv[q * 3 + 1], cv[q * 3 + 2]); } i2.push(remap.get(q)); }
fs.writeFileSync('shared/src/map/tors_green.json', JSON.stringify({ base: +lo.toFixed(3), collider: { type: 'trimesh', v: v2.map((x) => Math.round(x * 100) / 100), i: i2 } }));
await doc.transform(prune({ keepAttributes: true }), dedup(), textureCompress({ encoder: sharp, targetFormat: 'webp', resize: [1024, 1024], quality: 88 }), meshopt({ encoder: MeshoptEncoder, level: 'medium' }));
await io.write('client/public/assets/tors_green.glb', doc);
console.log('tris', prim.getIndices().getCount() / 3, 'collider tris', simp.length / 3, 'glb', (fs.statSync('client/public/assets/tors_green.glb').size / 1024).toFixed(0), 'KB', 'json', (fs.statSync('shared/src/map/tors_green.json').size / 1024).toFixed(0), 'KB');
