// Brings textured low-poly guns (Sketchfab, TastyTony, CC-BY) into the game's gun convention:
// barrel along -Z, the grip at the origin (about a third of the length from the back, a quarter for pistols),
// real length in metres, textures at most 512 px. One file per armory version: Models/gun_<key>.glb
// Run: node scripts/unity/import-guns.mjs <dir with <key>.glb files> <list.txt: key uid length>
import fs from 'node:fs';
import { NodeIO, getBounds } from '@gltf-transform/core';
import { ALL_EXTENSIONS } from '@gltf-transform/extensions';
import { textureCompress, prune, dedup, metalRough } from '@gltf-transform/functions';
import sharp from 'sharp';
import { mat4mul, apply, bakeSkins } from './gltf-util.mjs';
const io = new NodeIO().registerExtensions(ALL_EXTENSIONS);
const [dir, listFile] = process.argv.slice(2);
const OUT = 'unity/Assets/LastWar/Resources/Models';
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
