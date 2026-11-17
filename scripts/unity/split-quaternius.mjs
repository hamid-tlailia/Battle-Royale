// Quaternius "Ultimate Modular Characters" (CC0): one outfit file per character, its pieces named like the game's
// split outfits (body_head / body_torso / body_legs / body_feet) and its bones renamed to the mixamo names the game
// drives (the pieces of different characters share one rig: mixing them leaves no gaps).
// Run: node scripts/unity/split-quaternius.mjs <humans.glb converted from Humans_Master.fbx>
import fs from 'node:fs';
import { NodeIO } from '@gltf-transform/core';
import { ALL_EXTENSIONS } from '@gltf-transform/extensions';
import { prune, dedup } from '@gltf-transform/functions';
import { mat4mul } from './gltf-util.mjs';

// 4x4 inverse (column-major)
function inv(m) {
  const [a00, a01, a02, a03, a10, a11, a12, a13, a20, a21, a22, a23, a30, a31, a32, a33] = m;
  const b00 = a00 * a11 - a01 * a10, b01 = a00 * a12 - a02 * a10, b02 = a00 * a13 - a03 * a10, b03 = a01 * a12 - a02 * a11;
  const b04 = a01 * a13 - a03 * a11, b05 = a02 * a13 - a03 * a12, b06 = a20 * a31 - a21 * a30, b07 = a20 * a32 - a22 * a30;
  const b08 = a20 * a33 - a23 * a30, b09 = a21 * a32 - a22 * a31, b10 = a21 * a33 - a23 * a31, b11 = a22 * a33 - a23 * a32;
  const d = 1 / (b00 * b11 - b01 * b10 + b02 * b09 + b03 * b08 - b04 * b07 + b05 * b06);
  return [(a11 * b11 - a12 * b10 + a13 * b09) * d, (a02 * b10 - a01 * b11 - a03 * b09) * d, (a31 * b05 - a32 * b04 + a33 * b03) * d, (a22 * b04 - a21 * b05 - a23 * b03) * d,
    (a12 * b08 - a10 * b11 - a13 * b07) * d, (a00 * b11 - a02 * b08 + a03 * b07) * d, (a32 * b02 - a30 * b05 - a33 * b01) * d, (a20 * b05 - a22 * b02 + a23 * b01) * d,
    (a10 * b10 - a11 * b08 + a13 * b06) * d, (a01 * b08 - a00 * b10 - a03 * b06) * d, (a30 * b04 - a31 * b02 + a33 * b00) * d, (a21 * b02 - a20 * b04 - a23 * b00) * d,
    (a11 * b07 - a10 * b09 - a12 * b06) * d, (a00 * b09 - a01 * b07 + a02 * b06) * d, (a31 * b01 - a30 * b03 - a32 * b00) * d, (a20 * b03 - a21 * b01 + a22 * b00) * d];
}
// move a joint under another, keeping where it is in the world (the skin's inverse binds stay valid)
function reparent(node, parent) {
  const w = node.getWorldMatrix();
  const old = node.getParentNode(); if (old) old.removeChild(node);
  parent.addChild(node);
  node.setMatrix(mat4mul(inv(parent.getWorldMatrix()), w));
}
const io = new NodeIO().registerExtensions(ALL_EXTENSIONS);
const OUT = 'unity/Assets/LastWar/Resources/Models';
const BONES = { Hips: 'Hips', Abdomen: 'Spine', Torso: 'Spine1', Chest: 'Spine2', Neck: 'Neck', Head: 'Head' };
for (const [s, S] of [['L', 'Left'], ['R', 'Right']]) {
  Object.assign(BONES, { [`Shoulder.${s}`]: `${S}Shoulder`, [`UpperArm.${s}`]: `${S}Arm`, [`LowerArm.${s}`]: `${S}ForeArm`, [`Wrist.${s}`]: `${S}Hand`,
    [`UpperLeg.${s}`]: `${S}UpLeg`, [`LowerLeg.${s}`]: `${S}Leg`, [`Foot.${s}`]: `${S}Foot` });
  for (const f of ['Index', 'Middle', 'Ring', 'Pinky', 'Thumb']) for (let k = 1; k <= 3; k++) BONES[`${f}${k}.${s}`] = `${S}Hand${f}${k}`;
}
const PART = (n) => /_Head$/.test(n) ? 'head' : /_Body$/.test(n) ? 'torso' : /_(Legs|Pants)$/.test(n) ? 'legs' : /_Feet$/.test(n) ? 'feet' : null;
const src = process.argv[2];
const probe = await io.read(src);
const chars = [...new Set(probe.getRoot().listNodes().map((n) => n.getMesh() && PART(n.getName()) ? n.getName().replace(/_(Head|Body|Legs|Pants|Feet)$/, '') : null).filter(Boolean))];
for (const ch of chars) {
  const doc = await io.read(src);
  const r = doc.getRoot();
  for (const a of r.listAnimations()) a.dispose();
  for (const n of r.listNodes()) {
    if (BONES[n.getName()]) n.setName('mixamorig:' + BONES[n.getName()]);
    const m = n.getMesh(); if (!m) continue;
    const part = PART(n.getName());
    if (!part || !n.getName().startsWith(ch + '_')) { n.setMesh(null); n.setSkin(null); continue; }
    n.setName('body_' + part); m.setName('body_' + part);
  }
  // the Quaternius rig is an IK rig: the feet hang from the root and the legs from a "Body" bone above the hips.
  // Make it a plain mixamo chain (hips -> legs -> feet) so turning the hips and legs carries the feet along.
  const byName = Object.fromEntries(r.listNodes().map((n) => [n.getName(), n]));
  for (const S of ['Left', 'Right']) {
    reparent(byName[`mixamorig:${S}UpLeg`], byName['mixamorig:Hips']);
    reparent(byName[`mixamorig:${S}Foot`], byName[`mixamorig:${S}Leg`]);
  }
  await doc.transform(dedup(), prune({ keepLeaves: true }));
  const key = 'q_' + ch.toLowerCase();
  await io.write(`${OUT}/outfit_${key}.glb`, doc);
  console.log(key, (fs.statSync(`${OUT}/outfit_${key}.glb`).size / 1024).toFixed(0) + 'KB');
}
