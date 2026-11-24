// Brings loot / gear props (Sketchfab, CC-BY) into the game: rig baked out, standard materials, textures <= 512 px,
// largest side scaled to the given metres, origin at the bottom centre. Models/<key>.glb
// list.txt: key uid size [col]   size: largest side in metres, or hN = height N metres;
//   col: also write the model's triangles as its collision shape (Data/prefabs.json: walls stop you, doorways let you in)
// Run: node scripts/unity/import-props.mjs <dir with <key>.glb> <list.txt>
import fs from 'node:fs';
import { NodeIO, getBounds } from '@gltf-transform/core';
import { ALL_EXTENSIONS } from '@gltf-transform/extensions';
import { textureCompress, prune, dedup, metalRough, simplify, weld } from '@gltf-transform/functions';
import sharp from 'sharp';
import { bakeSkins, scenePoints, apply } from './gltf-util.mjs';
const io = new NodeIO().registerExtensions(ALL_EXTENSIONS);
const [dir, listFile] = process.argv.slice(2);
// TEX=1024: the colour maps' largest size (default 512; the enterable buildings use 1024 - at 512 their walls looked smeared)
const TEX = +(process.env.TEX || 512);
if (process.argv[1].endsWith("import-props.mjs")) {
const OUT = 'unity/Assets/LastWar/Resources/Models';
for (const line of fs.readFileSync(listFile, 'utf8').trim().split('\n')) {
  const [key, , size, col] = line.trim().split(/\s+/);
  const doc = await io.read(`${dir}/${key}.glb`);
  const scene = doc.getRoot().listScenes()[0];
  bakeSkins(doc, scene);
  const pts = scenePoints(scene);
  const mn = [0, 1, 2].map((i) => Math.min(...pts.map((p) => p[i]))), mx = [0, 1, 2].map((i) => Math.max(...pts.map((p) => p[i])));
  const k = size.startsWith('h') ? +size.slice(1) / (mx[1] - mn[1]) : +size / Math.max(...mx.map((x, i) => x - mn[i]));
  const holder = doc.createNode('prop').setScale([k, k, k]).setTranslation([-(mn[0] + mx[0]) / 2 * k, -mn[1] * k, -(mn[2] + mx[2]) / 2 * k]);
  for (const n of scene.listChildren()) { scene.removeChild(n); holder.addChild(n); }
  scene.addChild(holder);
  await doc.transform(metalRough(), dedup(), prune(), textureCompress({ encoder: sharp, resize: [TEX, TEX], targetFormat: 'jpeg', quality: 90, slots: /^(?!normalTexture).*$/ }));
  for (const m of doc.getRoot().listMaterials()) { m.setNormalTexture(null); m.setOcclusionTexture(null); m.setMetallicRoughnessTexture(null); }
  await doc.transform(prune());
  await io.write(`${OUT}/${key}.glb`, doc);
  if (col === 'col') writeShape(key, scene);
  const b = getBounds(scene);
  console.log(key.padEnd(10), 'size', b.max.map((x, i) => (x - b.min[i]).toFixed(2)).join(' x '), (fs.statSync(`${OUT}/${key}.glb`).size / 1024).toFixed(0) + 'KB');
}
}

// window glass is no wall: see-through / glass parts stay out of the collision shape (you can jump through windows)
export function seeThrough(p) { const m = p.getMaterial(); return !!m && (/glass|window/i.test(m.getName()) || m.getAlphaMode() === 'BLEND'); } // (a function: the list above runs before a const here exists)

// the model's triangles (scene space, welded to 1 cm) as a 'trimesh' collision shape for MapBuilder
export function writeShape(key, scene) {
  const v = [], idx = [], seen = new Map();
  scene.traverse((n) => { const m = n.getMesh(); if (!m) return; const wm = n.getWorldMatrix();
    for (const p of m.listPrimitives()) { if (seeThrough(p)) continue; const a = p.getAttribute('POSITION'), I = p.getIndices(); const e = [];
      const map = new Int32Array(a.getCount());
      for (let i = 0; i < a.getCount(); i++) { const q = apply(wm, a.getElement(i, e)).map((x) => Math.round(x * 100) / 100); const h = q.join(','); let id = seen.get(h); if (id === undefined) { id = v.length / 3; v.push(...q); seen.set(h, id); } map[i] = id; }
      const cnt = I ? I.getCount() : a.getCount();
      for (let t = 0; t < cnt; t += 3) { const A = map[I ? I.getScalar(t) : t], B = map[I ? I.getScalar(t + 1) : t + 1], C = map[I ? I.getScalar(t + 2) : t + 2]; if (A !== B && B !== C && A !== C) idx.push(A, B, C); } } });
  const f = 'unity/Assets/LastWar/Resources/Data/prefabs.json';
  const d = JSON.parse(fs.readFileSync(f, 'utf8'));
  d.shapes = d.shapes.filter((s) => s.name !== key);
  d.shapes.push({ name: key, type: 'trimesh', v, i: idx, radius: 0, height: 0 });
  fs.writeFileSync(f, JSON.stringify(d));
  console.log('  collider', key, v.length / 3, 'verts', idx.length / 3, 'tris');
}
