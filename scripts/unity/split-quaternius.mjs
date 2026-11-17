// Quaternius "Ultimate Modular Characters" (CC0): one outfit file per character, its pieces named like the game's
// split outfits (body_head / body_torso / body_legs / body_feet) and its bones renamed to the mixamo names the game
// drives (the pieces of different characters share one rig: mixing them leaves no gaps).
// Run: node scripts/unity/split-quaternius.mjs <humans.glb converted from Humans_Master.fbx>
import fs from 'node:fs';
import { NodeIO } from '@gltf-transform/core';
import { ALL_EXTENSIONS } from '@gltf-transform/extensions';
import { prune, dedup } from '@gltf-transform/functions';
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
  await doc.transform(dedup(), prune({ keepLeaves: true }));
  const key = 'q_' + ch.toLowerCase();
  await io.write(`${OUT}/outfit_${key}.glb`, doc);
  console.log(key, (fs.statSync(`${OUT}/outfit_${key}.glb`).size / 1024).toFixed(0) + 'KB');
}
