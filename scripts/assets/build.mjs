// Converts assets-src/*.glb into optimized game assets:
//   client/public/assets/*.glb  (meshopt-compressed, WebP textures, real-world scale)
//   shared/src/map/prefabs.json (bounds + collision shapes, used by client AND server physics)
import fs from 'node:fs';
import { NodeIO } from '@gltf-transform/core';
import { ALL_EXTENSIONS } from '@gltf-transform/extensions';
import {
  dedup, prune, weld, resample, meshopt, textureCompress, transformPrimitive, joinPrimitives,
  simplifyPrimitive, getBounds,
} from '@gltf-transform/functions';
import { MeshoptEncoder, MeshoptSimplifier } from 'meshoptimizer';
import sharp from 'sharp';

await MeshoptEncoder.ready;
await MeshoptSimplifier.ready;
const io = new NodeIO().registerExtensions(ALL_EXTENSIONS).registerDependencies({ 'meshopt.encoder': MeshoptEncoder });
const SRC = 'assets-src';
const OUT = 'client/public/assets';
fs.mkdirSync(OUT, { recursive: true });

// ---------------------------------------------------------------- math helpers
const qMul = (a, b) => [
  a[3] * b[0] + a[0] * b[3] + a[1] * b[2] - a[2] * b[1],
  a[3] * b[1] - a[0] * b[2] + a[1] * b[3] + a[2] * b[0],
  a[3] * b[2] + a[0] * b[1] - a[1] * b[0] + a[2] * b[3],
  a[3] * b[3] - a[0] * b[0] - a[1] * b[1] - a[2] * b[2],
];
const qInv = (q) => [-q[0], -q[1], -q[2], q[3]];
const qRot = (q, v) => { const p = qMul(qMul(q, [v[0], v[1], v[2], 0]), qInv(q)); return [p[0], p[1], p[2]]; };
const mat4 = (s, r, t) => { // column-major TRS matrix
  const [x, y, z, w] = r;
  return [
    (1 - 2 * (y * y + z * z)) * s[0], 2 * (x * y + z * w) * s[0], 2 * (x * z - y * w) * s[0], 0,
    2 * (x * y - z * w) * s[1], (1 - 2 * (x * x + z * z)) * s[1], 2 * (y * z + x * w) * s[1], 0,
    2 * (x * z + y * w) * s[2], 2 * (y * z - x * w) * s[2], (1 - 2 * (x * x + y * y)) * s[2], 0,
    t[0], t[1], t[2], 1,
  ];
};
const stripSuffix = (doc) => doc.getRoot().listNodes().forEach((n) => n.setName(n.getName().replace(/_\d+$/, '')));

/** Keep only baseColor textures; Lambert shading in game. */
function simplifyMaterials(doc) {
  for (const m of doc.getRoot().listMaterials()) {
    m.setNormalTexture(null).setMetallicRoughnessTexture(null).setOcclusionTexture(null).setEmissiveTexture(null);
    m.setMetallicFactor(0).setRoughnessFactor(1);
  }
  for (const p of doc.getRoot().listMeshes().flatMap((m) => m.listPrimitives())) {
    for (const s of ['TANGENT', 'TEXCOORD_1']) if (p.getAttribute(s)) p.setAttribute(s, null);
  }
}

async function finalize(doc, file, texSize = 512) {
  simplifyMaterials(doc);
  await doc.transform(
    prune(), dedup(),
    textureCompress({ encoder: sharp, targetFormat: 'webp', resize: [texSize, texSize], quality: 82 }),
    meshopt({ encoder: MeshoptEncoder, level: 'medium' }),
  );
  await io.write(file, doc);
  console.log('wrote', file, (fs.statSync(file).size / 1024).toFixed(0), 'KB');
}

// ---------------------------------------------------------------- static prefabs
/**
 * Bakes the meshes under `nodeName` (world transforms) into a single node at the origin:
 * bottom-centre at (0,0,0), scaled so that `fit` axis matches `size` metres. One primitive per material.
 * Returns { doc, size:[x,y,z], positions(Float32Array of tris) } for collider generation.
 */
async function bakePrefab(srcFile, nodeName, { fit = 'y', size, rotY = 0, dropMaterials = [], centerOnBase = false, scale, origin, simplify }) {
  const doc = await io.read(srcFile);
  const root = doc.getRoot();
  const scene = root.getDefaultScene() || root.listScenes()[0];
  const names = Array.isArray(nodeName) ? nodeName : nodeName ? [nodeName] : [];
  const targets = names.map((nm) => { const t = root.listNodes().find((n) => n.getName() === nm); if (!t) throw new Error(`node ${nm} not found in ${srcFile}`); return t; });
  const prims = [];
  const visit = (n) => {
    const mesh = n.getMesh();
    if (mesh) for (const p of mesh.listPrimitives()) {
      if (dropMaterials.includes(p.getMaterial()?.getName())) continue;
      const c = doc.createPrimitive().setMode(p.getMode()).setMaterial(p.getMaterial());
      for (const s of p.listSemantics()) c.setAttribute(s, p.getAttribute(s).clone());
      if (p.getIndices()) c.setIndices(p.getIndices().clone());
      transformPrimitive(c, n.getWorldMatrix());
      prims.push(c);
    }
    n.listChildren().forEach(visit);
  };
  (targets.length ? targets : scene.listChildren()).forEach(visit);
  // bounds
  const mn = [Infinity, Infinity, Infinity], mx = [-Infinity, -Infinity, -Infinity];
  const ry = [0, Math.sin(rotY / 2), 0, Math.cos(rotY / 2)];
  for (const p of prims) {
    transformPrimitive(p, mat4([1, 1, 1], ry, [0, 0, 0]));
    const a = p.getAttribute('POSITION');
    for (let i = 0; i < a.getCount(); i++) { const v = a.getElement(i, []); for (let k = 0; k < 3; k++) { mn[k] = Math.min(mn[k], v[k]); mx[k] = Math.max(mx[k], v[k]); } }
  }
  const axis = { x: 0, y: 1, z: 2, max: -1 }[fit];
  const extent = axis >= 0 ? mx[axis] - mn[axis] : Math.max(mx[0] - mn[0], mx[2] - mn[2]);
  const s = scale ?? size / extent;
  let cx = (mn[0] + mx[0]) / 2, cz = (mn[2] + mx[2]) / 2;
  if (origin) { cx = origin[0]; cz = origin[2]; mn[1] = origin[1]; } // keep pieces of one model aligned
  if (centerOnBase) { // centre on the trunk: centroid of the lowest vertices
    let sx = 0, sz = 0, n = 0;
    const lim = mn[1] + (mx[1] - mn[1]) * 0.05;
    for (const p of prims) { const a = p.getAttribute('POSITION'); for (let i = 0; i < a.getCount(); i++) { const v = a.getElement(i, []); if (v[1] <= lim) { sx += v[0]; sz += v[2]; n++; } } }
    if (n) { cx = sx / n; cz = sz / n; }
  }
  const norm = mat4([s, s, s], [0, 0, 0, 1], [-cx * s, -mn[1] * s, -cz * s]);
  prims.forEach((p) => transformPrimitive(p, norm));
  // group by material
  const byMat = new Map();
  for (const p of prims) { const k = p.getMaterial(); if (!byMat.has(k)) byMat.set(k, []); byMat.get(k).push(p); }
  const mesh = doc.createMesh(names[0] || 'prefab');
  for (const list of byMat.values()) {
    for (const p of list) { // unify attribute sets
      if (!p.getAttribute('TEXCOORD_0')) p.setAttribute('TEXCOORD_0', doc.createAccessor().setType('VEC2').setArray(new Float32Array(p.getAttribute('POSITION').getCount() * 2)));
      for (const sem of p.listSemantics()) if (!['POSITION', 'NORMAL', 'TEXCOORD_0'].includes(sem)) p.setAttribute(sem, null);
    }
    const prim = list.length > 1 ? joinPrimitives(list) : list[0];
    if (simplify && prim.getIndices()) simplifyPrimitive(prim, { simplifier: MeshoptSimplifier, ratio: simplify, error: 0.01, lockBorder: false });
    mesh.addPrimitive(prim);
  }
  // replace scene content
  for (const n of scene.listChildren()) scene.removeChild(n);
  for (const n of root.listNodes()) n.dispose();
  const node = doc.createNode('prefab').setMesh(mesh);
  scene.addChild(node);
  // collect triangles for colliders
  const tris = [];
  for (const p of mesh.listPrimitives()) {
    const pos = p.getAttribute('POSITION'), idx = p.getIndices();
    const n = idx ? idx.getCount() : pos.getCount();
    for (let i = 0; i < n; i++) tris.push(...pos.getElement(idx ? idx.getScalar(i) : i, []));
  }
  const size3 = [(mx[0] - mn[0]) * s, (mx[1] - mn[1]) * s, (mx[2] - mn[2]) * s];
  return { doc, size: size3, tris: new Float32Array(tris) };
}

/** Weld + simplify a triangle soup for the physics trimesh. */
function colliderMesh(tris, ratio = 1) {
  const map = new Map(), verts = [], idx = [];
  for (let i = 0; i < tris.length; i += 3) {
    const key = `${Math.round(tris[i] * 50)},${Math.round(tris[i + 1] * 50)},${Math.round(tris[i + 2] * 50)}`;
    let id = map.get(key);
    if (id === undefined) { id = verts.length / 3; map.set(key, id); verts.push(tris[i], tris[i + 1], tris[i + 2]); }
    idx.push(id);
  }
  // drop degenerate triangles
  let ind = [];
  for (let i = 0; i < idx.length; i += 3) if (idx[i] !== idx[i + 1] && idx[i + 1] !== idx[i + 2] && idx[i] !== idx[i + 2]) ind.push(idx[i], idx[i + 1], idx[i + 2]);
  if (ratio < 1) {
    const [out] = MeshoptSimplifier.simplify(new Uint32Array(ind), new Float32Array(verts), 3, Math.floor(ind.length * ratio / 3) * 3, 0.05, []); // target must be a multiple of 3
    ind = Array.from(out);
  }
  // keep only the vertices the triangles use
  const remap = new Map(), v2 = [], i2 = [];
  for (const q of ind) { if (!remap.has(q)) { remap.set(q, v2.length / 3); v2.push(verts[q * 3], verts[q * 3 + 1], verts[q * 3 + 2]); } i2.push(remap.get(q)); }
  return { v: v2.map((x) => Math.round(x * 100) / 100), i: i2 };
}

const prefabs = {};
async function prefab(name, src, node, opts, collider) {
  const { doc, size, tris } = await bakePrefab(`${SRC}/${src}.glb`, node, opts);
  const info = { size: size.map((v) => Math.round(v * 100) / 100) };
  if (collider === 'trimesh') info.collider = { type: 'trimesh', ...colliderMesh(tris, opts.colliderRatio ?? 1) };
  else if (collider === 'hull') info.collider = { type: 'hull', v: colliderMesh(tris, 1).v };
  else if (collider?.type) info.collider = collider;
  prefabs[name] = info;
  await finalize(doc, `${OUT}/${name}.glb`, opts.tex ?? 512);
}

// Houses: door height in the source ~37% of wall height. Doors measured 1.55m at 6.3m -> 9.2m gives 2.25m doors.
await prefab('house_a', 'houseA', null, { fit: 'y', size: 9.2 }, 'trimesh');
await prefab('house_b', 'houseB', null, { fit: 'y', size: 9.2 }, 'trimesh');
await prefab('house_c', 'houseC', null, { fit: 'y', size: 9.2 }, 'trimesh');
// Cars (old wrecks) — ~4.6 m long
for (const c of ['car1', 'car2', 'car3', 'car4']) await prefab(c, 'cars', c, { fit: 'max', size: 4.6, tex: 512 }, 'hull');
// Pines (full trees only)
for (const [n, h] of [['Pine1', 9], ['Pine2', 7], ['Pine4', 10], ['Pine5', 8]]) {
  await prefab(n.toLowerCase(), 'pines', n, { fit: 'y', size: h, centerOnBase: true }, { type: 'cylinder', radius: 0.3, height: 3.5 });
}
// Rocks / bushes / stump from the nature pack
for (const [n, s] of [['Rock1', 2.4], ['Rock2', 3.2], ['Rock3', 1.8], ['Rock4', 2.8]]) await prefab(n.toLowerCase(), 'nature', n, { fit: 'max', size: s }, 'hull');
for (const n of ['Bush1', 'Bush2']) await prefab(n.toLowerCase(), 'nature', n, { fit: 'max', size: 1.8 }, { type: 'none' });
await prefab('stump', 'nature', 'Stump1', { fit: 'max', size: 1.0, centerOnBase: true }, 'hull');
await prefab('fence', 'fence', null, { fit: 'y', size: 1.4, dropMaterials: ['cell_grass'] }, 'trimesh');
// Realistic trees & bushes (LOLIPOP packs, real scale). Near model = the pack's LOD2, far = its billboard.
const TREES = [
  ['tpine1', 'pinesR', 'Pine_big_1', 0.75, 0.32], ['tpine2', 'pinesR', 'Pine_big_2', 0.75, 0.32],
  ['tpine3', 'pinesR', 'Pine_big_3', 0.72, 0.34], ['tpine4', 'pinesR', 'Pine_medium_1', 0.85, 0.28],
  ['toak1', 'oaks', 'Big_oak_tree__001', 0.8, 0.5], ['toak2', 'oaks', 'Big_oak_tree__002', 0.8, 0.5],
];
for (const [name, src, node, sc, r] of TREES) {
  const far = src === 'oaks' ? `${node}_Billboard` : `${node}_Billboard_LOD3`;
  await prefab(name, src, [`${node}_LOD2`], { scale: sc, centerOnBase: true, tex: 512 }, { type: 'cylinder', radius: r, height: 9 });
  await prefab(`${name}_far`, src, [far], { scale: sc, centerOnBase: false, tex: 256 }, { type: 'none' });
}
for (const [name, node] of [['tbush1', 'Lilac_bush_1'], ['tbush2', 'Lilac_bush_2'], ['tbush3', 'Lilac_bush_3']]) {
  await prefab(name, 'lilac', [`${node}_LOD2`], { scale: 0.5, centerOnBase: true, tex: 256 }, { type: 'none' });
  await prefab(`${name}_far`, 'lilac', [`${node}_LOD3`], { scale: 0.5, tex: 128 }, { type: 'none' });
}
// More real houses (all real scale)
await prefab('house_d', 'houseD', null, { scale: 1, tex: 512, simplify: 0.35, colliderRatio: 0.2 }, 'trimesh');
await prefab('house_e', 'shop', ['Wall', 'Roof', 'Irons', 'Tiles', 'Glass', 'Woods'], { scale: 1, tex: 512, simplify: 0.5, colliderRatio: 0.4 }, 'trimesh');
await prefab('house_f', 'farmhouseB', null, { scale: 1, simplify: 0.05, tex: 1024, colliderRatio: 0.1 }, 'trimesh');

// Farm village (green map). One scale for the whole set (scarecrow ~1.9 m) so pieces stay in proportion.
const VS = 105; // metres per source unit
const V = 'maps/village';
await prefab('farmhouse', V, ['House', 'Roof', 'Shape002', 'Line007', 'Shape003', 'Shape004', 'Cylinder003', 'Cylinder004'], { scale: VS, simplify: 0.35, tex: 512 }, 'hull');
await prefab('windmill', V, ['Windmill'], { scale: VS, tex: 512, origin: [0.026, -0.002, -0.104] }, 'hull');
await prefab('windmill_blades', V, ['WindmillPropellor'], { scale: VS, tex: 512, origin: [0.026, -0.002, -0.104] }, { type: 'none' });
await prefab('well', V, ['Well', 'wellwater'], { scale: VS, simplify: 0.12, tex: 256 }, 'hull');
await prefab('yardwall', V, ['Wall', 'WallRoof', 'Thatched'], { scale: VS, simplify: 0.25, tex: 512 }, 'trimesh');
await prefab('wagon', V, ['Wagon'], { scale: VS, tex: 256 }, 'hull');
await prefab('haybale', V, ['Hay Bale03'], { scale: VS, simplify: 0.3, tex: 256 }, 'hull');
await prefab('scarecrow', V, ['Scarecrow001'], { scale: VS, simplify: 0.4, tex: 256 }, { type: 'none' });

// ---------------------------------------------------------------- weapons
// Each gun: barrel along -Z, grip near origin, real length.
const GUNS = { pistol: ['PMm_1', 0.22], rifle: ['Cylinder.002_13', 0.88], shotgun: ['Cylinder.005_19', 1.0], sniper: ['Cylinder.008_5', 1.2] };
for (const [name, [node, len]] of Object.entries(GUNS)) {
  const { doc } = await bakePrefab(`${SRC}/guns.glb`, node, { fit: 'max', size: len });
  bakeColorsAndMerge(doc);
  orientGun(doc, name === 'pistol' ? 0.22 : 0.33);
  await finalize(doc, `${OUT}/gun_${name}.glb`, 256);
}

// ---------------------------------------------------------------- character
await buildSoldier();

fs.writeFileSync('shared/src/map/prefabs.json', JSON.stringify(prefabs));
console.log('prefabs.json', (fs.statSync('shared/src/map/prefabs.json').size / 1024).toFixed(0), 'KB');

async function buildSoldier() {
  const doc = await io.read(`${SRC}/soldier.glb`);
  const anim = await io.read(`${SRC}/soldierAnims.glb`);
  stripSuffix(doc); stripSuffix(anim);
  const root = doc.getRoot();
  const buffer = root.listBuffers()[0];
  const byName = new Map(root.listNodes().map((n) => [n.getName(), n]));
  const findNode = (d, name) => d.getRoot().listNodes().find((n) => n.getName() === name);

  // --- Armature spaces for hips retargeting
  const armT = findNode(doc, 'mixamorig:Hips').getParentNode().getParentNode().getParentNode(); // Armature
  const armA = findNode(anim, 'mixamorig:Hips').getParentNode().getParentNode().getParentNode();
  const rT = armT.getRotation(), sT = armT.getScale()[0];
  const rA = armA.getRotation(), sA = armA.getScale()[0];
  const hipsRestT = findNode(doc, 'mixamorig:Hips').getTranslation();
  const hipsRestA = findNode(anim, 'mixamorig:Hips').getTranslation();
  const upT = qRot(rT, hipsRestT).map((v) => v * sT); // hips rest in "scene-root" space
  const upA = qRot(rA, hipsRestA).map((v) => v * sA);
  const k = upT[1] / upA[1];
  const corrRot = qMul(qInv(rT), rA); // anim armature space -> target armature space

  // --- Keep / rename native clips
  const keep = { jump_up: 'jump', standing_firing: 'fire', crouch_firing: 'crouch_fire', walking_firing: 'walk_fire', death_from_front_headshot: 'death', crouch_death: 'death_crouch', prone_firing: 'prone', prone_death: 'death_prone' };
  for (const a of root.listAnimations()) {
    if (keep[a.getName()]) a.setName(keep[a.getName()]); else a.dispose();
  }
  // --- Import clips from the second rig
  const imports = { Idle: 'idle', Walk: 'walk', Rifle_run: 'run', Rifle_stand: 'idle_rifle', Rifle_crouch: 'crouch_idle', Crouch: 'crouch_walk' };
  for (const src of anim.getRoot().listAnimations()) {
    const name = imports[src.getName()];
    if (!name) continue;
    const dst = doc.createAnimation(name);
    for (const ch of src.listChannels()) {
      const tgt = byName.get(ch.getTargetNode()?.getName());
      if (!tgt) continue;
      const path = ch.getTargetPath();
      const isHips = tgt.getName() === 'mixamorig:Hips';
      if (path === 'scale') continue;
      if (path === 'translation' && !isHips) continue; // bone lengths come from target rig
      const s = ch.getSampler();
      const input = doc.createAccessor().setType('SCALAR').setArray(new Float32Array(s.getInput().getArray())).setBuffer(buffer);
      let out = new Float32Array(s.getOutput().getArray());
      if (isHips && path === 'rotation') {
        for (let i = 0; i < out.length; i += 4) out.set(qMul(corrRot, [out[i], out[i + 1], out[i + 2], out[i + 3]]), i);
      }
      if (isHips && path === 'translation') {
        const restUp = qRot(rA, hipsRestA);
        for (let i = 0; i < out.length; i += 3) {
          let v = qRot(rA, [out[i], out[i + 1], out[i + 2]]); // to scene space (unscaled)
          v = [restUp[0], v[1], restUp[2]]; // remove horizontal root motion
          v = v.map((x) => (x * sA * k) / sT);
          out.set(qRot(qInv(rT), v), i);
        }
      }
      const output = doc.createAccessor().setType(path === 'rotation' ? 'VEC4' : 'VEC3').setArray(out).setBuffer(buffer);
      const sampler = doc.createAnimationSampler().setInput(input).setOutput(output).setInterpolation(s.getInterpolation());
      dst.addSampler(sampler).addChannel(doc.createAnimationChannel().setTargetNode(tgt).setTargetPath(path).setSampler(sampler));
    }
  }
  // --- Remove horizontal root motion from native clips too
  const hips = byName.get('mixamorig:Hips');
  for (const a of root.listAnimations()) {
    if (Object.values(imports).includes(a.getName())) continue;
    for (const ch of a.listChannels()) {
      if (ch.getTargetNode() !== hips || ch.getTargetPath() !== 'translation') continue;
      const acc = ch.getSampler().getOutput();
      const out = new Float32Array(acc.getArray());
      const restUp = qRot(rT, hipsRestT);
      const keepY = !a.getName().startsWith('death');
      for (let i = 0; i < out.length; i += 3) {
        const v = qRot(rT, [out[i], out[i + 1], out[i + 2]]);
        const w = keepY ? [restUp[0], v[1], restUp[2]] : [restUp[0] + (v[0] - restUp[0]) * 0.3, v[1], restUp[2] + (v[2] - restUp[2]) * 0.3];
        out.set(qRot(qInv(rT), w), i);
      }
      ch.getSampler().setOutput(doc.createAccessor().setType('VEC3').setArray(out).setBuffer(buffer));
    }
  }

  // --- Texture atlas: 7 base-colour maps -> one 1536x1536 (3x3 grid of 512)
  simplifyMaterials(doc);
  const meshNodes = root.listNodes().filter((n) => n.getMesh() && n.getSkin());
  const prims = meshNodes.flatMap((n) => n.getMesh().listPrimitives());
  const CELL = 512, GRID = 3;
  const composites = [];
  prims.forEach((p, i) => {
    const col = i % GRID, row = Math.floor(i / GRID);
    const tex = p.getMaterial().getBaseColorTexture();
    composites.push({ tex, col, row, factor: p.getMaterial().getBaseColorFactor() });
    const uv = p.getAttribute('TEXCOORD_0');
    const a = new Float32Array(uv.getArray());
    const pad = 4 / CELL;
    for (let j = 0; j < a.length; j += 2) {
      a[j] = (col + pad + Math.min(1, Math.max(0, a[j])) * (1 - 2 * pad)) / GRID;
      a[j + 1] = (row + pad + Math.min(1, Math.max(0, a[j + 1])) * (1 - 2 * pad)) / GRID;
    }
    p.setAttribute('TEXCOORD_0', doc.createAccessor().setType('VEC2').setArray(a).setBuffer(buffer));
  });
  const layers = await Promise.all(composites.map(async (c) => ({
    input: await sharp(Buffer.from(c.tex.getImage())).resize(CELL, CELL).toBuffer(),
    left: c.col * CELL, top: c.row * CELL,
  })));
  const atlas = await sharp({ create: { width: CELL * GRID, height: CELL * GRID, channels: 3, background: '#555' } })
    .composite(layers).png().toBuffer();
  const atlasTex = doc.createTexture('soldier_atlas').setImage(new Uint8Array(atlas)).setMimeType('image/png');
  const mat = doc.createMaterial('soldier').setBaseColorTexture(atlasTex).setMetallicFactor(0).setRoughnessFactor(1);
  // --- Join all skinned primitives into one (same skin)
  for (const p of prims) {
    p.setMaterial(mat);
    for (const sem of p.listSemantics()) if (!['POSITION', 'NORMAL', 'TEXCOORD_0', 'JOINTS_0', 'WEIGHTS_0'].includes(sem)) p.setAttribute(sem, null);
    // normalise JOINTS to uint16 for joining
    const j = p.getAttribute('JOINTS_0');
    p.setAttribute('JOINTS_0', doc.createAccessor().setType('VEC4').setArray(new Uint16Array(j.getArray())).setBuffer(buffer));
    const w = p.getAttribute('WEIGHTS_0');
    p.setAttribute('WEIGHTS_0', doc.createAccessor().setType('VEC4').setArray(new Float32Array(w.getArray())).setBuffer(buffer));
  }
  const joined = joinPrimitives(prims);
  await weldAndSimplify(doc, joined, 0.5);
  const skin = meshNodes[0].getSkin();
  const body = doc.createMesh('body').addPrimitive(joined);
  const lodPrim = joined.clone();
  for (const s of lodPrim.listSemantics()) lodPrim.setAttribute(s, lodPrim.getAttribute(s).clone());
  lodPrim.setIndices(lodPrim.getIndices().clone());
  await weldAndSimplify(doc, lodPrim, 0.24); // of the already halved mesh
  const lod = doc.createMesh('body_lod').addPrimitive(lodPrim);
  const parent = meshNodes[0].getParentNode();
  meshNodes.forEach((n) => n.dispose());
  parent.addChild(doc.createNode('body').setMesh(body).setSkin(skin));
  parent.addChild(doc.createNode('body_lod').setMesh(lod).setSkin(skin));

  await doc.transform(prune(), dedup(), resample({ tolerance: 0.002 }));
  console.log('soldier: clips', root.listAnimations().map((a) => a.getName()).join(','),
    'verts', joined.getAttribute('POSITION').getCount(), 'lod tris', lodPrim.getIndices().getCount() / 3);
  await finalize(doc, `${OUT}/soldier.glb`, 1536);
}

/**
 * Normalise a gun: muzzle toward -Z, origin at the grip (gripFrac of the length from the back),
 * vertically at the middle of the body. The muzzle is the thinner end of the long axis.
 */
function orientGun(doc, gripFrac) {
  const prim = doc.getRoot().listNodes().find((n) => n.getName() === 'prefab').getMesh().listPrimitives()[0];
  const pos = prim.getAttribute('POSITION');
  const pts = []; for (let i = 0; i < pos.getCount(); i++) pts.push(pos.getElement(i, []));
  const ext = (k) => [Math.min(...pts.map((p) => p[k])), Math.max(...pts.map((p) => p[k]))];
  const ex = ext(0), ez = ext(2);
  const axis = ex[1] - ex[0] > ez[1] - ez[0] ? 0 : 2;
  const [lo, hi] = axis === 0 ? ex : ez;
  const len = hi - lo;
  // thickness (y extent) of the last 15% at each end
  const thick = (from, to) => { const ys = pts.filter((p) => p[axis] >= from && p[axis] <= to).map((p) => p[1]); return ys.length ? Math.max(...ys) - Math.min(...ys) : 0; };
  const tLo = thick(lo, lo + len * 0.15), tHi = thick(hi - len * 0.15, hi);
  const muzzleAtHi = tHi < tLo;
  // rotation about Y taking the muzzle direction to -Z
  let rot;
  if (axis === 0) rot = muzzleAtHi ? Math.PI / 2 : -Math.PI / 2; // +X -> -Z needs +90deg
  else rot = muzzleAtHi ? Math.PI : 0;
  transformPrimitive(prim, mat4([1, 1, 1], [0, Math.sin(rot / 2), 0, Math.cos(rot / 2)], [0, 0, 0]));
  const p2 = []; for (let i = 0; i < pos.getCount(); i++) p2.push(prim.getAttribute('POSITION').getElement(i, []));
  const zMax = Math.max(...p2.map((p) => p[2])), ys = p2.map((p) => p[1]);
  const xs = p2.map((p) => p[0]);
  const t = [-(Math.min(...xs) + Math.max(...xs)) / 2, -(Math.min(...ys) + Math.max(...ys)) / 2, -(zMax - len * gripFrac)];
  transformPrimitive(prim, mat4([1, 1, 1], [0, 0, 0, 1], t));
  console.log('gun axis', axis ? 'z' : 'x', 'muzzle', muzzleAtHi ? 'hi' : 'lo', 'len', len.toFixed(2));
}

/** Untextured models: material colour -> vertex colour, then one primitive (1 draw call). */
function bakeColorsAndMerge(doc) {
  const mesh = doc.getRoot().listNodes().find((n) => n.getName() === 'prefab').getMesh();
  const prims = mesh.listPrimitives();
  for (const p of prims) {
    const c = p.getMaterial()?.getBaseColorFactor() ?? [1, 1, 1, 1];
    const n = p.getAttribute('POSITION').getCount();
    const col = new Float32Array(n * 3);
    for (let i = 0; i < n; i++) col.set([c[0], c[1], c[2]], i * 3);
    p.setAttribute('COLOR_0', doc.createAccessor().setType('VEC3').setArray(col).setBuffer(doc.getRoot().listBuffers()[0]));
    if (p.getAttribute('TEXCOORD_0')) p.setAttribute('TEXCOORD_0', null);
  }
  const mat = doc.createMaterial('vc').setBaseColorFactor([1, 1, 1, 1]).setMetallicFactor(0).setRoughnessFactor(1);
  prims.forEach((p) => p.setMaterial(mat));
  const joined = joinPrimitives(prims);
  prims.forEach((p) => mesh.removePrimitive(p));
  mesh.addPrimitive(joined);
}

async function weldAndSimplify(doc, prim, ratio) {
  // simplifyPrimitive requires indexed geometry
  simplifyPrimitive(prim, { simplifier: MeshoptSimplifier, ratio, error: 0.01, lockBorder: false });
}
