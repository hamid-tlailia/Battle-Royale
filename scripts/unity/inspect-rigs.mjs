// Phase 1 rig audit: for each character glb, the mixamo skeleton it carries and how it rests.
// Run: node scripts/unity/inspect-rigs.mjs <glb...>
import { NodeIO } from '@gltf-transform/core';
import { ALL_EXTENSIONS } from '@gltf-transform/extensions';
const io = new NodeIO().registerExtensions(ALL_EXTENSIONS);
const CORE = ['Hips','Spine','Spine1','Spine2','Neck','Head','LeftShoulder','LeftArm','LeftForeArm','LeftHand','RightShoulder','RightArm','RightForeArm','RightHand',
  'LeftUpLeg','LeftLeg','LeftFoot','LeftToeBase','RightUpLeg','RightLeg','RightFoot','RightToeBase'];
const FING = ['Thumb','Index','Middle','Ring','Pinky'].flatMap(f=>[1,2,3].flatMap(k=>['Left','Right'].map(s=>`${s}Hand${f}${k}`)));
const key = (n) => n.slice(n.lastIndexOf(':') + 1);
const sub=(a,b)=>a.map((v,i)=>v-b[i]), len=(a)=>Math.hypot(...a);
for (const f of process.argv.slice(2)) {
  const d = await io.read(f); const r = d.getRoot();
  const N = {}; for (const n of r.listNodes()) if (n.getName().includes('mixamorig')) N[key(n.getName())] = n;
  const W = (k) => { const m = N[k].getWorldMatrix(); return [m[12], m[13], m[14]]; };
  const missing = CORE.filter(k => !N[k]); const fingers = FING.filter(k => N[k]).length;
  const extra = Object.keys(N).filter(k => !CORE.includes(k) && !FING.includes(k) && !/End$|4$|HeadTop/.test(k));
  let info = '';
  if (!missing.length) {
    const h = W('Head')[1] - Math.min(W('LeftToeBase')[1], W('RightToeBase')[1]);
    const arm = sub(W('LeftForeArm'), W('LeftArm')); const armDown = Math.round(Math.atan2(-arm[1], Math.hypot(arm[0], arm[2])) * 180 / Math.PI);
    const leg = len(sub(W('LeftUpLeg'), W('LeftLeg'))) + len(sub(W('LeftLeg'), W('LeftFoot')));
    const armL = len(sub(W('LeftArm'), W('LeftForeArm'))) + len(sub(W('LeftForeArm'), W('LeftHand')));
    const scale = r.listNodes().find(n=>key(n.getName())==='Hips')?.getWorldMatrix()[5];
    info = `height(units) ${h.toFixed(2)} | arm down ${armDown}deg (${Math.abs(armDown) < 12 ? 'T-pose' : 'A-pose'}) | arm/leg ${(armL/leg).toFixed(2)} | hips scale ${scale?.toFixed(3)}`;
  }
  console.log(`${f.split('/').pop().padEnd(26)} bones ${Object.keys(N).length} | core missing: ${missing.join(',') || 'none'} | fingers ${fingers}/30 | skins ${r.listSkins().length} meshes ${r.listMeshes().length} | ${info}${extra.length ? ' | extra: ' + extra.slice(0,6).join(',') + (extra.length>6?'…':'') : ''}`);
}
