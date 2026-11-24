// Quaternius "Modular Character Outfits - Fantasy" + "Universal Base Characters" (both CC0) -> the game's mix-and-match
// outfits: one file per outfit (outfit_qm_*.glb men, outfit_qf_*.glb women), each piece named like the game's split
// outfits (..._head / ..._torso / ..._legs / ..._feet) so a head, torso, legs and feet can come from different outfits of
// the same body (men with men, women with women: one shared rig each, nothing gaps at the neck, waist or ankles).
//   head  = the base character's head and neck (cut from the body, scripts: qub/heads.mjs) + hair / brows / beard, and
//           the outfit's hood when it has one
//   torso = the outfit's body, arms (hands) and shoulder pieces; legs = its legs; feet = its boots / shoes
// The UE-mannequin bones are renamed to the mixamo names the game drives (pelvis -> Hips, spine_01 -> Spine, ...), a
// HeadTop_End is added at the top of the skull (the game sizes every outfit to 1.8 m from it), the colour texture is kept
// at 1024 px (normal / ORM / roughness maps dropped: the game's character material uses the colour only).
// Run: node scripts/unity/import-modular-outfits.mjs <dir with the outfits' .gltf, Head_Male.glb, Head_Female.glb, hair/>
import { NodeIO } from '@gltf-transform/core';
import { ALL_EXTENSIONS } from '@gltf-transform/extensions';
import { prune, dedup, mergeDocuments, textureCompress, weld } from '@gltf-transform/functions';
import sharp from 'sharp';
import fs from 'node:fs';

const io = new NodeIO().registerExtensions(ALL_EXTENSIONS);
const OUT = 'unity/Assets/LastWar/Resources/Models';
const Q = process.argv[2];

// ---- outfits: file id, outfit .gltf, the extra head pieces
// (SETS=<file.json>: other sets in the same form instead - the Sketchfab clothes fitted onto this rig by
// scripts/unity/static-clothes/ have their list in sets.json)
const SETS = process.env.SETS ? JSON.parse(fs.readFileSync(process.env.SETS, 'utf8')) : [
  ['qm_ranger', 'Male_Ranger.gltf', ['Head_Male.glb', 'hair/Eyebrows_Regular.gltf']],
  ['qm_peasant', 'Male_Peasant.gltf', ['Head_Male.glb', 'hair/Hair_SimpleParted.gltf', 'hair/Eyebrows_Regular.gltf', 'hair/Hair_Beard.gltf']],
  ['qf_ranger', 'Female_Ranger.gltf', ['Head_Female.glb']],
  ['qf_peasant', 'Female_Peasant.gltf', ['Head_Female.glb', 'hair/Hair_Long.gltf']],
];

// ---- UE mannequin -> mixamo bone names
const BONES = { pelvis: 'Hips', spine_01: 'Spine', spine_02: 'Spine1', spine_03: 'Spine2', neck_01: 'Neck', Head: 'Head' };
for (const [s, S] of [['l', 'Left'], ['r', 'Right']]) {
  Object.assign(BONES, { [`clavicle_${s}`]: `${S}Shoulder`, [`upperarm_${s}`]: `${S}Arm`, [`lowerarm_${s}`]: `${S}ForeArm`, [`hand_${s}`]: `${S}Hand`,
    [`thigh_${s}`]: `${S}UpLeg`, [`calf_${s}`]: `${S}Leg`, [`foot_${s}`]: `${S}Foot`, [`ball_${s}`]: `${S}ToeBase`, [`ball_leaf_${s}`]: `${S}Toe_End` });
  for (const f of ['index', 'middle', 'ring', 'pinky', 'thumb']) {
    const F = f[0].toUpperCase() + f.slice(1);
    for (let k = 1; k <= 3; k++) BONES[`${f}_0${k}_${s}`] = `${S}Hand${F}${k}`;
    BONES[`${f}_04_leaf_${s}`] = `${S}Hand${F}4`;
  }
}
const PART = (mesh) => /_Head/.test(mesh) ? 'head' : /_(Body|Arms|Acc)/.test(mesh) ? 'torso' : /_Legs/.test(mesh) ? 'legs' : /_Feet/.test(mesh) ? 'feet' : 'head';
const qinv = (a) => [-a[0], -a[1], -a[2], a[3]];
const qmul = (a, b) => [a[3] * b[0] + a[0] * b[3] + a[1] * b[2] - a[2] * b[1], a[3] * b[1] - a[0] * b[2] + a[1] * b[3] + a[2] * b[0], a[3] * b[2] + a[0] * b[1] - a[1] * b[0] + a[2] * b[3], a[3] * b[3] - a[0] * b[0] - a[1] * b[1] - a[2] * b[2]];
const qrot = (q, v) => { const p = qmul(qmul(q, [v[0], v[1], v[2], 0]), qinv(q)); return [p[0], p[1], p[2]]; };

for (const [id, file, heads] of SETS) {
  const doc = await io.read(`${Q}/${file}`);
  const root = doc.getRoot(), scene = root.getDefaultScene() ?? root.listScenes()[0];
  const bones = new Map(root.listNodes().filter((n) => n.getName() in BONES || n.getName() === 'root').map((n) => [n.getName(), n]));
  const armature = root.listNodes().find((n) => n.getName() === 'Armature');
  // the outfit's own pieces: named for the part they belong to
  for (const n of root.listNodes()) if (n.getMesh()) n.setName(`${id}_${n.getMesh().getName().replace(/[^A-Za-z0-9]+/g, '_')}_${PART(n.getMesh().getName())}`);
  // the head pieces: merged in, skinned to the outfit's own bones (same rig: the same bind pose of head and neck)
  let top = -Infinity;
  for (const h of heads) {
    const src = await io.read(`${Q}/${h}`);
    const before = new Set(root.listNodes()), beforeScenes = new Set(root.listScenes());
    mergeDocuments(doc, src);
    const added = root.listNodes().filter((n) => !before.has(n));
    for (const skin of root.listSkins()) {
      const js = skin.listJoints(); if (!js.some((j) => !before.has(j))) continue;
      for (const j of js) { const t = bones.get(j.getName()); if (!t) throw new Error(`${h}: no bone ${j.getName()} in ${file}`); skin.swap(j, t); }
      if (skin.getSkeleton() && !before.has(skin.getSkeleton())) skin.setSkeleton(bones.get('root') ?? null);
    }
    for (const n of added) {
      if (!n.getMesh()) continue;
      const nm = n.getMesh().getName();
      n.setName(`${id}_${(h.split('/').pop().replace(/\..*$/, '') + '_' + nm).replace(/[^A-Za-z0-9]+/g, '_')}_head`);
      (n.getParentNode() ?? null)?.removeChild(n); for (const s of root.listScenes()) s.removeChild(n);
      armature.addChild(n);
      // the top of the skull: the base head mesh (not hair, not the hood)
      if (h.startsWith('Head_')) for (const p of n.getMesh().listPrimitives()) {
        const a = p.getAttribute('POSITION'), I = p.getIndices(), el = [];
        const idx = I ? new Set(I.getArray()) : null;
        for (let i = 0; i < a.getCount(); i++) { if (idx && !idx.has(i)) continue; a.getElement(i, el); top = Math.max(top, el[1]); }
      }
    }
    for (const n of added) if (!n.getMesh()) n.dispose();
    for (const s of root.listScenes()) if (!beforeScenes.has(s)) s.dispose();
  }
  // a HeadTop_End at the top of the skull, straight above the head bone
  const head = bones.get('Head'), hw = head.getWorldTranslation(), hr = head.getWorldRotation(), hs = head.getWorldScale();
  const off = qrot(qinv(hr), [0, top - hw[1], 0]).map((x, i) => x / hs[i]);
  const topNode = doc.createNode('mixamorig:HeadTop_End').setTranslation(off); head.addChild(topNode);
  // mixamo names
  for (const [k, n] of bones) if (k in BONES) n.setName(`mixamorig:${BONES[k]}`);
  // colour only, 1024 px
  for (const m of root.listMaterials()) { m.setNormalTexture(null); m.setOcclusionTexture(null); m.setMetallicRoughnessTexture(null); m.setMetallicFactor(0); m.setRoughnessFactor(0.85); }
  await doc.transform(prune(), weld(), dedup(), textureCompress({ encoder: sharp, targetFormat: 'jpeg', resize: [1024, 1024], quality: 85 }), prune());
  for (const b of root.listBuffers().slice(1)) { for (const a of root.listAccessors()) if (a.getBuffer() === b) a.setBuffer(root.listBuffers()[0]); b.dispose(); }
  const parts = {}; for (const n of root.listNodes()) if (n.getMesh()) { const p = n.getName().split('_').pop(); parts[p] = (parts[p] ?? 0) + 1; }
  await io.write(`${OUT}/outfit_${id}.glb`, doc);
  console.log(`outfit_${id}.glb`, JSON.stringify(parts), 'head top', top.toFixed(3), 'm');
}
