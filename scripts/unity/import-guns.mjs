// Brings textured low-poly guns (Sketchfab, TastyTony, CC-BY) into the game's gun convention:
// barrel along -Z, the grip at the origin (about a third of the length from the back, a quarter for pistols),
// real length in metres, textures at most 512 px. One file per armory version: Models/gun_<key>.glb
// Run: node scripts/unity/import-guns.mjs <dir with <key>.glb files> <list.txt: key uid length>
import fs from 'node:fs';
import { NodeIO, getBounds } from '@gltf-transform/core';
import { ALL_EXTENSIONS } from '@gltf-transform/extensions';
import { textureCompress, prune, dedup, metalRough } from '@gltf-transform/functions';
import sharp from 'sharp';
const io = new NodeIO().registerExtensions(ALL_EXTENSIONS);
const [dir, listFile] = process.argv.slice(2);
const OUT = 'unity/Assets/LastWar/Resources/Models';
const mat4mul = (a, b) => { const o = new Array(16).fill(0); for (let c = 0; c < 4; c++) for (let r = 0; r < 4; r++) for (let k = 0; k < 4; k++) o[c * 4 + r] += a[k * 4 + r] * b[c * 4 + k]; return o; };
const apply = (m, v) => [0, 1, 2].map((r) => m[r] * v[0] + m[4 + r] * v[1] + m[8 + r] * v[2] + m[12 + r]);
// rigged guns (for first-person animations): bake the rest pose into plain meshes in scene space, drop the rig
function bakeSkins(doc, scene) {
  const inv = (m) => { // 4x4 inverse (column-major)
    const a = m, o = new Array(16);
    o[0] = a[5]*a[10]*a[15]-a[5]*a[11]*a[14]-a[9]*a[6]*a[15]+a[9]*a[7]*a[14]+a[13]*a[6]*a[11]-a[13]*a[7]*a[10];
    o[4] = -a[4]*a[10]*a[15]+a[4]*a[11]*a[14]+a[8]*a[6]*a[15]-a[8]*a[7]*a[14]-a[12]*a[6]*a[11]+a[12]*a[7]*a[10];
    o[8] = a[4]*a[9]*a[15]-a[4]*a[11]*a[13]-a[8]*a[5]*a[15]+a[8]*a[7]*a[13]+a[12]*a[5]*a[11]-a[12]*a[7]*a[9];
    o[12] = -a[4]*a[9]*a[14]+a[4]*a[10]*a[13]+a[8]*a[5]*a[14]-a[8]*a[6]*a[13]-a[12]*a[5]*a[10]+a[12]*a[6]*a[9];
    o[1] = -a[1]*a[10]*a[15]+a[1]*a[11]*a[14]+a[9]*a[2]*a[15]-a[9]*a[3]*a[14]-a[13]*a[2]*a[11]+a[13]*a[3]*a[10];
    o[5] = a[0]*a[10]*a[15]-a[0]*a[11]*a[14]-a[8]*a[2]*a[15]+a[8]*a[3]*a[14]+a[12]*a[2]*a[11]-a[12]*a[3]*a[10];
    o[9] = -a[0]*a[9]*a[15]+a[0]*a[11]*a[13]+a[8]*a[1]*a[15]-a[8]*a[3]*a[13]-a[12]*a[1]*a[11]+a[12]*a[3]*a[9];
    o[13] = a[0]*a[9]*a[14]-a[0]*a[10]*a[13]-a[8]*a[1]*a[14]+a[8]*a[2]*a[13]+a[12]*a[1]*a[10]-a[12]*a[2]*a[9];
    o[2] = a[1]*a[6]*a[15]-a[1]*a[7]*a[14]-a[5]*a[2]*a[15]+a[5]*a[3]*a[14]+a[13]*a[2]*a[7]-a[13]*a[3]*a[6];
    o[6] = -a[0]*a[6]*a[15]+a[0]*a[7]*a[14]+a[4]*a[2]*a[15]-a[4]*a[3]*a[14]-a[12]*a[2]*a[7]+a[12]*a[3]*a[6];
    o[10] = a[0]*a[5]*a[15]-a[0]*a[7]*a[13]-a[4]*a[1]*a[15]+a[4]*a[3]*a[13]+a[12]*a[1]*a[7]-a[12]*a[3]*a[5];
    o[14] = -a[0]*a[5]*a[14]+a[0]*a[6]*a[13]+a[4]*a[1]*a[14]-a[4]*a[2]*a[13]-a[12]*a[1]*a[6]+a[12]*a[2]*a[5];
    o[3] = -a[1]*a[6]*a[11]+a[1]*a[7]*a[10]+a[5]*a[2]*a[11]-a[5]*a[3]*a[10]-a[9]*a[2]*a[7]+a[9]*a[3]*a[6];
    o[7] = a[0]*a[6]*a[11]-a[0]*a[7]*a[10]-a[4]*a[2]*a[11]+a[4]*a[3]*a[10]+a[8]*a[2]*a[7]-a[8]*a[3]*a[6];
    o[11] = -a[0]*a[5]*a[11]+a[0]*a[7]*a[9]+a[4]*a[1]*a[11]-a[4]*a[3]*a[9]-a[8]*a[1]*a[7]+a[8]*a[3]*a[5];
    o[15] = a[0]*a[5]*a[10]-a[0]*a[6]*a[9]-a[4]*a[1]*a[10]+a[4]*a[2]*a[9]+a[8]*a[1]*a[6]-a[8]*a[2]*a[5];
    const det = a[0]*o[0]+a[1]*o[4]+a[2]*o[8]+a[3]*o[12]; return o.map((x) => x / det);
  };
  const baked = [];
  scene.traverse((n) => {
    const skin = n.getSkin(), mesh = n.getMesh(); if (!skin || !mesh) return;
    const joints = skin.listJoints(), ibm = skin.getInverseBindMatrices();
    const jm = joints.map((j, i) => mat4mul(j.getWorldMatrix(), ibm ? ibm.getElement(i, []) : n.getWorldMatrix()));
    for (const p of mesh.listPrimitives()) {
      const P = p.getAttribute('POSITION'), N = p.getAttribute('NORMAL'), J = p.getAttribute('JOINTS_0'), W = p.getAttribute('WEIGHTS_0');
      if (!J || !W) continue;
      const pos = new Float32Array(P.getCount() * 3), nor = N ? new Float32Array(N.getCount() * 3) : null; const j = [], w = [], v = [], nn = [];
      for (let i = 0; i < P.getCount(); i++) {
        J.getElement(i, j); W.getElement(i, w); P.getElement(i, v); if (N) N.getElement(i, nn);
        const o = [0, 0, 0], on = [0, 0, 0];
        for (let k = 0; k < 4; k++) { if (!w[k]) continue; const m = jm[j[k]]; const q = apply(m, v); for (let c = 0; c < 3; c++) o[c] += q[c] * w[k];
          if (N) { const r = [0, 1, 2].map((rr) => m[rr] * nn[0] + m[4 + rr] * nn[1] + m[8 + rr] * nn[2]); for (let c = 0; c < 3; c++) on[c] += r[c] * w[k]; } }
        pos.set(o, i * 3); if (nor) { const l = Math.hypot(...on) || 1; nor.set(on.map((x) => x / l), i * 3); }
      }
      P.setArray(pos); if (N) N.setArray(nor);
      p.setAttribute('JOINTS_0', null); p.setAttribute('WEIGHTS_0', null);
    }
    baked.push(n);
  });
  // the baked meshes are in scene space now: hang them straight under the scene, the rig goes
  for (const n of baked) {
    const m = n.getMesh(); const fresh = doc.createNode(n.getName() + '_baked').setMesh(m);
    n.setMesh(null); n.setSkin(null); scene.addChild(fresh);
  }
  for (const s of doc.getRoot().listSkins()) s.dispose();
}

for (const line of fs.readFileSync(listFile, 'utf8').trim().split('\n')) {
  const [key, , len] = line.trim().split(/\s+/); const L = +len;
  const doc = await io.read(`${dir}/${key}.glb`);
  const scene = doc.getRoot().listScenes()[0];
  bakeSkins(doc, scene);
  // every vertex in scene space
  const pts = [];
  scene.traverse((n) => { const m = n.getMesh(); if (!m) return; const wm = n.getWorldMatrix();
    for (const p of m.listPrimitives()) { const a = p.getAttribute('POSITION'); const e = []; for (let i = 0; i < a.getCount(); i += 3) pts.push(apply(wm, a.getElement(i, e))); } });
  const mn = [0, 1, 2].map((i) => Math.min(...pts.map((p) => p[i]))), mx = [0, 1, 2].map((i) => Math.max(...pts.map((p) => p[i])));
  const ext = mx.map((x, i) => x - mn[i]);
  const ax = ext[0] >= ext[2] ? 0 : 2; // the length lies along X or Z (Y is up on Sketchfab)
  // muzzle = the end whose last 15 % is thinnest (stocks and grips are tall)
  const slab = (lo, hi) => { const s = pts.filter((p) => p[ax] >= lo && p[ax] <= hi); return s.length ? Math.max(...s.map((p) => p[1])) - Math.min(...s.map((p) => p[1])) : 1e9; };
  const a0 = slab(mn[ax], mn[ax] + ext[ax] * 0.15), a1 = slab(mx[ax] - ext[ax] * 0.15, mx[ax]);
  const muzzlePlus = a1 < a0; // muzzle at the + end of the axis
  // rotation about Y that sends the muzzle to -Z
  let th = 0;
  if (ax === 0) th = muzzlePlus ? Math.PI / 2 : -Math.PI / 2; else th = muzzlePlus ? Math.PI : 0;
  const c = Math.cos(th), s = Math.sin(th), k = L / ext[ax];
  const R = [c * k, 0, -s * k, 0, 0, k, 0, 0, s * k, 0, c * k, 0, 0, 0, 0, 1];
  // after rotating: back end z, front end z, vertical centre -> grip at the origin
  const rp = pts.map((p) => apply(R, p));
  const zmin = Math.min(...rp.map((p) => p[2])), zmax = Math.max(...rp.map((p) => p[2]));
  const ymid = (Math.min(...rp.map((p) => p[1])) + Math.max(...rp.map((p) => p[1]))) / 2, xmid = (Math.min(...rp.map((p) => p[0])) + Math.max(...rp.map((p) => p[0]))) / 2;
  const grip = key.startsWith('pistol') ? 0.23 : 0.33;
  const T = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, -xmid, -ymid, -(zmax - grip * (zmax - zmin)), 1];
  const M = mat4mul(T, R);
  const holder = doc.createNode('gun').setMatrix(M);
  for (const n of scene.listChildren()) { scene.removeChild(n); holder.addChild(n); }
  scene.addChild(holder);
  await doc.transform(metalRough(), dedup(), prune(), textureCompress({ encoder: sharp, resize: [512, 512], targetFormat: 'jpeg', slots: /^(?!normalTexture).*$/ }));
  // normal maps: drop (tiny guns, phones)
  for (const m of doc.getRoot().listMaterials()) { m.setNormalTexture(null); m.setOcclusionTexture(null); m.setMetallicRoughnessTexture(null); }
  await doc.transform(prune());
  await io.write(`${OUT}/gun_${key}.glb`, doc);
  const b = getBounds(scene);
  console.log(key.padEnd(17), 'axis', 'XZ'[ax / 2], 'muzzle', muzzlePlus ? '+' : '-', 'len', (b.max[2] - b.min[2]).toFixed(2), 'z', b.min[2].toFixed(2), b.max[2].toFixed(2), (fs.statSync(`${OUT}/gun_${key}.glb`).size / 1024).toFixed(0) + 'KB');
}
