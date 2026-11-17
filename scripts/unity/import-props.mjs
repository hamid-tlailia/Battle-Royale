// Brings loot / gear props (Sketchfab, CC-BY) into the game: rig baked out, standard materials, textures <= 512 px,
// largest side scaled to the given metres, origin at the bottom centre. Models/<key>.glb
// Run: node scripts/unity/import-props.mjs <dir with <key>.glb> <list.txt: key uid size>
import fs from 'node:fs';
import { NodeIO, getBounds } from '@gltf-transform/core';
import { ALL_EXTENSIONS } from '@gltf-transform/extensions';
import { textureCompress, prune, dedup, metalRough, simplify, weld } from '@gltf-transform/functions';
import sharp from 'sharp';
import { bakeSkins, scenePoints } from './gltf-util.mjs';
const io = new NodeIO().registerExtensions(ALL_EXTENSIONS);
const [dir, listFile] = process.argv.slice(2);
const OUT = 'unity/Assets/LastWar/Resources/Models';
for (const line of fs.readFileSync(listFile, 'utf8').trim().split('\n')) {
  const [key, , size] = line.trim().split(/\s+/);
  const doc = await io.read(`${dir}/${key}.glb`);
  const scene = doc.getRoot().listScenes()[0];
  bakeSkins(doc, scene);
  const pts = scenePoints(scene);
  const mn = [0, 1, 2].map((i) => Math.min(...pts.map((p) => p[i]))), mx = [0, 1, 2].map((i) => Math.max(...pts.map((p) => p[i])));
  const k = +size / Math.max(...mx.map((x, i) => x - mn[i]));
  const holder = doc.createNode('prop').setScale([k, k, k]).setTranslation([-(mn[0] + mx[0]) / 2 * k, -mn[1] * k, -(mn[2] + mx[2]) / 2 * k]);
  for (const n of scene.listChildren()) { scene.removeChild(n); holder.addChild(n); }
  scene.addChild(holder);
  await doc.transform(metalRough(), dedup(), prune(), textureCompress({ encoder: sharp, resize: [512, 512], targetFormat: 'jpeg', slots: /^(?!normalTexture).*$/ }));
  for (const m of doc.getRoot().listMaterials()) { m.setNormalTexture(null); m.setOcclusionTexture(null); m.setMetallicRoughnessTexture(null); }
  await doc.transform(prune());
  await io.write(`${OUT}/${key}.glb`, doc);
  const b = getBounds(scene);
  console.log(key.padEnd(10), 'size', b.max.map((x, i) => (x - b.min[i]).toFixed(2)).join(' x '), (fs.statSync(`${OUT}/${key}.glb`).size / 1024).toFixed(0) + 'KB');
}
