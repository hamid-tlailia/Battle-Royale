// Copies the game's models into the Unity project in a form glTFast imports in the editor:
// meshopt compression and mesh quantization removed (plain float meshes), WebP textures re-encoded as PNG (alpha) / JPEG.
import fs from 'node:fs';
import { NodeIO } from '@gltf-transform/core';
import { ALL_EXTENSIONS } from '@gltf-transform/extensions';
import { dequantize } from '@gltf-transform/functions';
import { MeshoptDecoder } from 'meshoptimizer';
import sharp from 'sharp';
await MeshoptDecoder.ready;
const io = new NodeIO().registerExtensions(ALL_EXTENSIONS).registerDependencies({ 'meshopt.decoder': MeshoptDecoder });
const SRC = 'client/public/assets', OUT = 'unity/Assets/LastWar/Resources/Models';
fs.mkdirSync(OUT, { recursive: true });
const files = fs.readdirSync(SRC).filter((f) => f.endsWith('.glb'));
let total = 0;
for (const f of files) {
  const doc = await io.read(`${SRC}/${f}`);
  const root = doc.getRoot();
  for (const ext of root.listExtensionsUsed()) if (['EXT_meshopt_compression', 'EXT_texture_webp'].includes(ext.extensionName)) ext.dispose();
  // quantized positions need the node scale glTFast does not always apply: store plain floats
  await doc.transform(dequantize());
  for (const ext of root.listExtensionsUsed()) if (ext.extensionName === 'KHR_mesh_quantization') ext.dispose();
  for (const t of root.listTextures()) {
    const img = t.getImage(); if (!img) continue;
    const meta = await sharp(Buffer.from(img)).metadata();
    const out = meta.hasAlpha ? await sharp(Buffer.from(img)).png().toBuffer() : await sharp(Buffer.from(img)).jpeg({ quality: 90 }).toBuffer();
    t.setImage(new Uint8Array(out)).setMimeType(meta.hasAlpha ? 'image/png' : 'image/jpeg');
    if (t.getURI()) t.setURI(t.getURI().replace(/\.webp$/, meta.hasAlpha ? '.png' : '.jpg'));
  }
  await io.write(`${OUT}/${f}`, doc);
  total += fs.statSync(`${OUT}/${f}`).size;
}
console.log(files.length, 'models', (total / 1e6).toFixed(1), 'MB');
