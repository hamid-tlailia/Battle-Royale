// Builds the extra player outfits (real clothed characters, all Mixamo rigs) into
// client/public/assets/outfit_<name>.glb : one skinned mesh (texture atlas) + LOD, NO animations —
// the game drives them with the soldier's clips by bone name (see render/characters.ts retarget).
import fs from 'node:fs';
import { NodeIO } from '@gltf-transform/core';
import { ALL_EXTENSIONS } from '@gltf-transform/extensions';
import { dedup, prune, meshopt, textureCompress, joinPrimitives, simplifyPrimitive, weld } from '@gltf-transform/functions';
import { MeshoptEncoder, MeshoptSimplifier, MeshoptDecoder } from 'meshoptimizer';
import sharp from 'sharp';
import { OUTFITS } from './sources.mjs';

await MeshoptEncoder.ready; await MeshoptSimplifier.ready; await MeshoptDecoder.ready;
const io = new NodeIO().registerExtensions(ALL_EXTENSIONS)
  .registerDependencies({ 'meshopt.encoder': MeshoptEncoder, 'meshopt.decoder': MeshoptDecoder });
const OUT = 'client/public/assets';
const only = process.argv[2];

for (const [name, o] of Object.entries(OUTFITS)) {
  if (only && only !== name) continue;
  const doc = await io.read(`assets-src/${name}.glb`);
  const root = doc.getRoot();
  const buffer = root.listBuffers()[0];
  root.listNodes().forEach((n) => n.setName(n.getName().replace(/_\d+$/, '')));
  if (o.rename) root.listNodes().forEach((n) => n.setName(n.getName().split(o.rename[0]).join(o.rename[1])));
  root.listAnimations().forEach((a) => a.dispose());
  // only the skinned body; static props (guns, hats on separate rigs) are dropped
  const meshNodes = root.listNodes().filter((n) => n.getMesh() && n.getSkin());
  root.listNodes().filter((n) => n.getMesh() && !n.getSkin()).forEach((n) => n.setMesh(null));
  const skin = meshNodes[0].getSkin();
  const prims = meshNodes.flatMap((n) => n.getMesh().listPrimitives())
    .filter((p) => !(o.drop ?? []).some((d) => (p.getMaterial()?.getName() ?? '').toLowerCase().includes(d)));
  // ---- texture atlas
  const GRID = Math.ceil(Math.sqrt(prims.length)), CELL = GRID > 3 ? 256 : 512;
  const layers = [];
  for (let i = 0; i < prims.length; i++) {
    const p = prims[i], col = i % GRID, row = Math.floor(i / GRID);
    const m = p.getMaterial();
    const f = m?.getBaseColorFactor() ?? [1, 1, 1, 1];
    // Fuse characters use the spec/gloss extension: their colour map is the "diffuse" texture
    const sg = m?.getExtension('KHR_materials_pbrSpecularGlossiness');
    const tex = m?.getBaseColorTexture() ?? sg?.getDiffuseTexture() ?? null;
    if (sg && !m?.getBaseColorTexture()) { const d = sg.getDiffuseFactor(); f[0] = d[0]; f[1] = d[1]; f[2] = d[2]; }
    const gain = o.gain ?? 1;
    let img;
    if (tex) img = await sharp(await sharp(Buffer.from(tex.getImage())).resize(CELL, CELL).removeAlpha().toColourspace('srgb').png().toBuffer()).linear([f[0] * gain, f[1] * gain, f[2] * gain], [0, 0, 0]).toBuffer();
    else img = await sharp({ create: { width: CELL, height: CELL, channels: 3, background: { r: Math.round(f[0] ** (1 / 2.2) * 255), g: Math.round(f[1] ** (1 / 2.2) * 255), b: Math.round(f[2] ** (1 / 2.2) * 255) } } }).png().toBuffer();
    layers.push({ input: await sharp(img).png().toBuffer(), left: col * CELL, top: row * CELL });
    let uv = p.getAttribute('TEXCOORD_0');
    const n = p.getAttribute('POSITION').getCount();
    const a = uv ? new Float32Array(uv.getArray()) : new Float32Array(n * 2).fill(0.5);
    const pad = 3 / CELL;
    for (let j = 0; j < a.length; j += 2) {
      const u = a[j] - Math.floor(a[j]) , v = a[j + 1] - Math.floor(a[j + 1]); // wrap tiling UVs into the cell
      a[j] = (col + pad + (a[j] >= 0 && a[j] <= 1 ? a[j] : u) * (1 - 2 * pad)) / GRID;
      a[j + 1] = (row + pad + (a[j + 1] >= 0 && a[j + 1] <= 1 ? a[j + 1] : v) * (1 - 2 * pad)) / GRID;
    }
    p.setAttribute('TEXCOORD_0', doc.createAccessor().setType('VEC2').setArray(a).setBuffer(buffer));
    for (const sem of p.listSemantics()) if (!['POSITION', 'NORMAL', 'TEXCOORD_0', 'JOINTS_0', 'WEIGHTS_0'].includes(sem)) p.setAttribute(sem, null);
    p.setAttribute('JOINTS_0', doc.createAccessor().setType('VEC4').setArray(new Uint16Array(p.getAttribute('JOINTS_0').getArray())).setBuffer(buffer));
    p.setAttribute('WEIGHTS_0', doc.createAccessor().setType('VEC4').setArray(new Float32Array(p.getAttribute('WEIGHTS_0').getArray())).setBuffer(buffer));
    p.listTargets().forEach((t) => p.removeTarget(t));
    p.setMode(4);
    const idx = p.getIndices();
    p.setIndices(doc.createAccessor().setType('SCALAR').setArray(idx ? new Uint32Array(idx.getArray()) : Uint32Array.from({ length: n }, (_, k) => k)).setBuffer(buffer));
    p.setAttribute('POSITION', doc.createAccessor().setType('VEC3').setArray(new Float32Array(p.getAttribute('POSITION').getArray())).setBuffer(buffer));
    if (p.getAttribute('NORMAL')) p.setAttribute('NORMAL', doc.createAccessor().setType('VEC3').setArray(new Float32Array(p.getAttribute('NORMAL').getArray())).setBuffer(buffer));
    if (!p.getAttribute('NORMAL')) throw new Error(`${name}: primitive without normals`);
  }
  const atlas = await sharp({ create: { width: CELL * GRID, height: CELL * GRID, channels: 3, background: '#666' } }).composite(layers).png().toBuffer();
  const mat = doc.createMaterial('outfit').setBaseColorTexture(doc.createTexture('atlas').setImage(new Uint8Array(atlas)).setMimeType('image/png'))
    .setMetallicFactor(0).setRoughnessFactor(1);
  prims.forEach((p) => p.setMaterial(mat));
  // all body meshes must share one skin to be joined; remap joint indices if a node uses another skin
  for (const n of meshNodes) {
    const s = n.getSkin();
    if (s === skin) continue;
    const map = s.listJoints().map((j) => skin.listJoints().indexOf(j));
    for (const p of n.getMesh().listPrimitives()) {
      const arr = new Uint16Array(p.getAttribute('JOINTS_0').getArray());
      for (let i = 0; i < arr.length; i++) arr[i] = Math.max(0, map[arr[i]]);
      p.setAttribute('JOINTS_0', doc.createAccessor().setType('VEC4').setArray(arr).setBuffer(buffer));
    }
  }
  const joined = joinPrimitives(prims);
  const tris0 = joined.getIndices().getCount() / 3;
  simplifyPrimitive(joined, { simplifier: MeshoptSimplifier, ratio: Math.min(1, o.tris / tris0), error: 0.02, lockBorder: true }); // keep seams (neck, wrists) closed
  const lodPrim = joined.clone();
  for (const s of lodPrim.listSemantics()) lodPrim.setAttribute(s, lodPrim.getAttribute(s).clone());
  lodPrim.setIndices(lodPrim.getIndices().clone());
  simplifyPrimitive(lodPrim, { simplifier: MeshoptSimplifier, ratio: 2200 / (joined.getIndices().getCount() / 3), error: 1, lockBorder: false });
  const parent = meshNodes[0].getParentNode();
  const parentOf = (n) => n.getParentNode() ?? null;
  meshNodes.forEach((n) => n.dispose());
  (parent ?? root.listScenes()[0]).addChild(doc.createNode('body').setMesh(doc.createMesh('body').addPrimitive(joined)).setSkin(skin));
  (parent ?? root.listScenes()[0]).addChild(doc.createNode('body_lod').setMesh(doc.createMesh('body_lod').addPrimitive(lodPrim)).setSkin(skin));
  void parentOf;
  await doc.transform(prune(), dedup());
  for (const m of root.listMaterials()) m.setNormalTexture(null).setMetallicRoughnessTexture(null).setOcclusionTexture(null).setEmissiveTexture(null);
  await doc.transform(
    prune(), dedup(),
    textureCompress({ encoder: sharp, targetFormat: 'webp', resize: [1024, 1024], quality: 80 }),
    meshopt({ encoder: MeshoptEncoder, level: 'medium' }),
  );
  const file = `${OUT}/outfit_${name}.glb`;
  await io.write(file, doc);
  console.log(name, 'tris', tris0, '->', joined.getIndices().getCount() / 3, 'lod', lodPrim.getIndices().getCount() / 3, 'cells', prims.length,
    (fs.statSync(file).size / 1024).toFixed(0), 'KB');
}
