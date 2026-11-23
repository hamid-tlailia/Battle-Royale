// Mixamo characters (FBX for Unity, converted with FBX2glTF) -> game outfits outfit_mx_<name>.glb: animations dropped,
// textures shrunk to 1024 px (the downloads carry 2048-4096 px maps), spec/gloss materials turned metal/rough.
// Run: node scripts/unity/import-mixamo-chars.mjs <in.glb> <name> [<in.glb> <name> ...]
// Also Sketchfab downloads of Mixamo-rigged characters: their bones come numbered ("mixamorig:Hips_01",
// "mixamorig:RightHandRing4_00"): the number is dropped so the game finds them by their mixamo names.
import fs from 'node:fs';
import sharp from 'sharp';
import { NodeIO } from '@gltf-transform/core';
import { ALL_EXTENSIONS } from '@gltf-transform/extensions';
import { prune, dedup, metalRough, textureCompress } from '@gltf-transform/functions';
const io = new NodeIO().registerExtensions(ALL_EXTENSIONS);
const OUT = 'unity/Assets/LastWar/Resources/Models';
const a = process.argv.slice(2);

// The pieces' world scale brought to ~1 (the drawn result unchanged): Striker's FBX node scaled by 0.01 and everything
// below it was in centimetres - the meshes at a world scale of 0.01, and the game's sole measure (it bakes the posed
// mesh) put the soldier metres below the ground (gone from the lobby, low in the rows). The 0.01 node becomes 1 and
// every length below it (node positions, vertices, the skins' inverse-bind translations) is divided by 100.
function unitScale(doc) {
  const root = doc.getRoot(), skinned = root.listNodes().filter((n) => n.getSkin() && n.getMesh());
  if (!skinned.length) return;
  const ws = (n) => { const m = n.getWorldMatrix(); return Math.hypot(m[0], m[1], m[2]); };
  if (ws(skinned[0]) > 0.1) return;
  let top = skinned[0]; for (let n = top; n; n = n.getParentNode()) if (Math.abs(n.getScale()[0] - 0.01) < 1e-4) top = n;
  if (Math.abs(top.getScale()[0] - 0.01) > 1e-4) return;
  const f = 0.01, below = []; const walk = (n) => { for (const c of n.listChildren()) { below.push(c); walk(c); } }; walk(top);
  for (const n of below) n.setTranslation(n.getTranslation().map((x) => x * f));
  top.setScale([1, 1, 1]);
  const accs = new Set(), skins = new Set();
  for (const n of below) { if (n.getMesh()) for (const p of n.getMesh().listPrimitives()) { accs.add(p.getAttribute('POSITION')); for (const t of p.listTargets()) accs.add(t.getAttribute('POSITION')); } if (n.getSkin()) skins.add(n.getSkin()); }
  for (const acc of accs) { if (!acc) continue; const arr = acc.getArray().slice(); for (let i = 0; i < arr.length; i++) arr[i] *= f; acc.setArray(arr); }
  for (const sk of skins) { const ib = sk.getInverseBindMatrices(); if (!ib) continue; const arr = ib.getArray().slice(); for (let i = 0; i < arr.length; i += 16) { arr[i + 12] *= f; arr[i + 13] *= f; arr[i + 14] *= f; } ib.setArray(arr); }
  console.log('unit scale: below', top.getName(), 'lengths x', f, '- piece world scale now', ws(skinned[0]).toFixed(3));
}
for (let i = 0; i < a.length; i += 2) {
  const doc = await io.read(a[i]);
  for (const an of doc.getRoot().listAnimations()) an.dispose();
  unitScale(doc);
  for (const n of doc.getRoot().listNodes()) { const m = /^(mixamorig\d*:[A-Za-z]+\d?(?:_End)?)_\d+$/.exec(n.getName()); if (m) n.setName(m[1].replace(/^mixamorig\d*:/, 'mixamorig:')); }
  // (the game's character material uses the colour texture only: normal / roughness / occlusion / emission maps are
  // dropped - a Sketchfab soldier carried 24 maps, 22 MB)
  if (process.env.COLOR_ONLY) for (const m of doc.getRoot().listMaterials()) { m.setNormalTexture(null); m.setOcclusionTexture(null); m.setMetallicRoughnessTexture(null); m.setEmissiveTexture(null); }
  await doc.transform(metalRough(), dedup(), prune({ keepLeaves: true }),
    textureCompress({ encoder: sharp, resize: [1024, 1024], targetFormat: undefined }));
  const out = `${OUT}/outfit_mx_${a[i + 1]}.glb`;
  await io.write(out, doc);
  console.log(out, (fs.statSync(out).size / 1048576).toFixed(1) + 'MB');
}
