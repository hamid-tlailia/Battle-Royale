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
for (let i = 0; i < a.length; i += 2) {
  const doc = await io.read(a[i]);
  for (const an of doc.getRoot().listAnimations()) an.dispose();
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
