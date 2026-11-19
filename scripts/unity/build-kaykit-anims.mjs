// Carries clips of KayKit's Character Animations 1.1 (Kay Lousberg, CC0; Rig_Medium, bone names root/hips/spine/chest/
// head, upperarm/lowerarm/wrist/hand, upperleg/lowerleg/foot/toes .l/.r) onto the LW_BaseCharacter skeleton and adds them
// to lw_extra.glb (kept apart from lw_base.glb; clips already in lw_extra.glb are kept, same-named ones replaced).
// Both rigs rest in a T-pose facing +Z (checked: the KayKit rest equals its own T-Pose clip), so, as in build-extra-anims,
// each mapped bone gets its world rotation change from the rest; bones with no KayKit match (Spine1, Neck, shoulders,
// fingers) keep their rest relative to their parent. The pelvis keeps its height change, scaled by the leg length.
// Run: node scripts/unity/build-kaykit-anims.mjs <Rig_Medium_*.glb> [--dry] name=Clip [name=Clip ...]
//   e.g. kk_crawl=Crawling     --dry: only prints the carried pose (no file written)
import { NodeIO } from '@gltf-transform/core';
import { ALL_EXTENSIONS } from '@gltf-transform/extensions';
import { prune } from '@gltf-transform/functions';
const io = new NodeIO().registerExtensions(ALL_EXTENSIONS);
const M = 'unity/Assets/LastWar/Resources/Models';
// ---- quaternion / vector helpers ([x,y,z,w])
const qmul = (a, b) => [a[3] * b[0] + a[0] * b[3] + a[1] * b[2] - a[2] * b[1], a[3] * b[1] - a[0] * b[2] + a[1] * b[3] + a[2] * b[0], a[3] * b[2] + a[0] * b[1] - a[1] * b[0] + a[2] * b[3], a[3] * b[3] - a[0] * b[0] - a[1] * b[1] - a[2] * b[2]];
const qinv = (a) => [-a[0], -a[1], -a[2], a[3]];
const qrot = (q, v) => { const p = qmul(qmul(q, [v[0], v[1], v[2], 0]), qinv(q)); return [p[0], p[1], p[2]]; };
const qnorm = (q) => { const l = Math.hypot(...q) || 1; return q.map((x) => x / l); };
const slerp = (a, b, t) => { let d = a[0] * b[0] + a[1] * b[1] + a[2] * b[2] + a[3] * b[3]; if (d < 0) { b = b.map((x) => -x); d = -d; }
  if (d > 0.9995) return qnorm(a.map((x, i) => x + (b[i] - x) * t)); const th = Math.acos(d), s = Math.sin(th);
  return a.map((x, i) => (x * Math.sin((1 - t) * th) + b[i] * Math.sin(t * th)) / s); };
const lerp = (a, b, t) => a.map((x, i) => x + (b[i] - x) * t);
const key = (n) => n.slice(n.lastIndexOf(':') + 1);

function sampler(ch) {
  const s = ch.getSampler(), times = s.getInput().getArray(), out = s.getOutput(), n = out.getElementSize(), el = [];
  const vals = []; for (let i = 0; i < out.getCount(); i++) vals.push(out.getElement(i, el).slice());
  return (t) => { if (t <= times[0]) return vals[0]; const L = times.length - 1; if (t >= times[L]) return vals[L];
    let i = 1; while (times[i] < t) i++; const u = (t - times[i - 1]) / (times[i] - times[i - 1]);
    return n === 4 ? slerp(vals[i - 1], vals[i], u) : lerp(vals[i - 1], vals[i], u); };
}
// world transforms of a scene for given local overrides
function solve(roots, local) {
  const W = new Map();
  const walk = (n, pr, pp, ps) => {
    const L = local.get(n) ?? { r: n.getRotation(), t: n.getTranslation(), s: n.getScale() };
    const p = qrot(pr, L.t.map((x, i) => x * ps[i])).map((x, i) => x + pp[i]);
    const r = qmul(pr, L.r), s = ps.map((x, i) => x * L.s[i]);
    W.set(n, { r, p, s }); n.listChildren().forEach((c) => walk(c, r, p, s));
  };
  roots.forEach((n) => walk(n, [0, 0, 0, 1], [0, 0, 0], [1, 1, 1]));
  return W;
}



const MAP = { hips: 'Hips', spine: 'Spine', chest: 'Spine2', head: 'Head' };
for (const s of ['l', 'r']) { const S = s === 'l' ? 'Left' : 'Right';
  Object.assign(MAP, { [`upperarm.${s}`]: `${S}Arm`, [`lowerarm.${s}`]: `${S}ForeArm`, [`wrist.${s}`]: `${S}Hand`,
    [`upperleg.${s}`]: `${S}UpLeg`, [`lowerleg.${s}`]: `${S}Leg`, [`foot.${s}`]: `${S}Foot`, [`toes.${s}`]: `${S}ToeBase` }); }
const BACK = Object.fromEntries(Object.entries(MAP).map(([a, b]) => [b, a]));

const doc = await io.read(`${M}/lw_extra.glb`);
const bRoots = doc.getRoot().listScenes()[0].listChildren();
const bNodes = new Map(doc.getRoot().listNodes().filter((n) => n.getName().includes('mixamorig')).map((n) => [key(n.getName()), n]));
const bRest = solve(bRoots, new Map());
const order = []; const visit = (n) => { if (n.getName().includes('mixamorig')) order.push(n); n.listChildren().forEach(visit); }; bRoots.forEach(visit);
const buf = doc.getRoot().listBuffers()[0];
const len = (W, a, b) => Math.hypot(...W.get(a).p.map((x, i) => x - W.get(b).p[i]));
const FPS = 30;
const src = await io.read(process.argv[2]);
const uRoots = src.getRoot().listScenes()[0].listChildren();
const uNodes = new Map(src.getRoot().listNodes().map((n) => [n.getName(), n]));
const uRest = solve(uRoots, new Map());
const kLeg = (len(bRest, bNodes.get('LeftUpLeg'), bNodes.get('LeftLeg')) + len(bRest, bNodes.get('LeftLeg'), bNodes.get('LeftFoot'))) /
             (len(uRest, uNodes.get('upperleg.l'), uNodes.get('lowerleg.l')) + len(uRest, uNodes.get('lowerleg.l'), uNodes.get('foot.l')));
const dry = process.argv.includes('--dry');
const hips = bNodes.get('Hips'), pelvis = uNodes.get('hips');
const ground = Math.min(bRest.get(bNodes.get('LeftToeBase')).p[1], bRest.get(bNodes.get('LeftFoot')).p[1]);

function carry(anim) {
  const chans = new Map(); let dur = 0;
  for (const ch of anim.listChannels()) { const n = ch.getTargetNode(); if (!n) continue; if (!chans.has(n)) chans.set(n, {}); chans.get(n)[ch.getTargetPath()] = sampler(ch);
    const ti = ch.getSampler().getInput().getArray(); dur = Math.max(dur, ti[ti.length - 1]); }
  const frames = Math.max(2, Math.round(dur * FPS) + 1), rot = [], hipsT = [];
  for (let f = 0; f < frames; f++) {
    const t = Math.min(dur, f / FPS);
    const loc = new Map(); for (const [n, c] of chans) loc.set(n, { r: c.rotation ? c.rotation(t) : n.getRotation(), t: c.translation ? c.translation(t) : n.getTranslation(), s: c.scale ? c.scale(t) : n.getScale() });
    const uW = solve(uRoots, loc), sW = new Map(), r = new Map();
    for (const n of order) {
      const parent = n.listParents().find((p) => p.propertyType === 'Node');
      const pw = parent && sW.has(parent) ? sW.get(parent) : bRest.get(parent) ?? { r: [0, 0, 0, 1] };
      const un = uNodes.get(BACK[key(n.getName())]);
      const wr = un ? qmul(qmul(uW.get(un).r, qinv(uRest.get(un).r)), bRest.get(n).r) : qmul(pw.r, n.getRotation());
      sW.set(n, { r: wr }); r.set(n, qnorm(qmul(qinv(pw.r), wr)));
    }
    const d = uW.get(pelvis).p.map((x, i) => (x - uRest.get(pelvis).p[i]) * kLeg); d[0] = 0; d[2] = 0;
    const hp = hips.listParents().find((p) => p.propertyType === 'Node'); const hw = bRest.get(hp) ?? { r: [0, 0, 0, 1], s: [1, 1, 1] };
    const dl = qrot(qinv(hw.r), d).map((x, i) => x / hw.s[i]);
    rot.push(r); hipsT.push(hips.getTranslation().map((x, i) => x + dl[i]));
  }
  return { frames, rot, hipsT, len: (frames - 1) / FPS };
}
// the carried pose on the base skeleton: heights above the rest ground (m), for a check before anything is written
function report(c) {
  const J = ['Hips', 'Head', 'LeftHand', 'RightHand', 'LeftLeg', 'RightLeg', 'LeftToeBase', 'RightToeBase', 'LeftForeArm', 'RightForeArm'];
  console.log('frame  ' + J.join(' '));
  for (let f = 0; f < c.frames; f += 4) {
    const loc = new Map(); for (const n of order) loc.set(n, { r: c.rot[f].get(n), t: n === hips ? c.hipsT[f] : n.getTranslation(), s: n.getScale() });
    const W = solve(bRoots, loc);
    console.log(String(f).padStart(3), J.map((j) => (W.get(bNodes.get(j)).p[1] - ground).toFixed(2).padStart(6)).join(' '), ' hipsZ', W.get(hips).p[2].toFixed(2), ' handZ', W.get(bNodes.get('LeftHand')).p[2].toFixed(2));
  }
}
console.log('leg ratio', kLeg.toFixed(3), 'base hips', (bRest.get(hips).p[1] - ground).toFixed(2), 'm');
for (const arg of process.argv.slice(3).filter((a) => a.includes('='))) {
  const [name, clip] = arg.split('=');
  const a = src.getRoot().listAnimations().find((x) => x.getName() === clip);
  if (!a) { console.log('no clip', clip); continue; }
  const c = carry(a); report(c);
  const dump = process.argv.find((x) => x.startsWith('--dump='));
  if (dump) { // (side-view check picture: the carried pose's bone positions, and the KayKit original's, per frame)
    const out = { base: [], kay: [], ground };
    const bones = [['Hips','Spine'],['Spine','Spine2'],['Spine2','Head'],['Spine2','LeftArm'],['LeftArm','LeftForeArm'],['LeftForeArm','LeftHand'],['Spine2','RightArm'],['RightArm','RightForeArm'],['RightForeArm','RightHand'],
      ['Hips','LeftUpLeg'],['LeftUpLeg','LeftLeg'],['LeftLeg','LeftFoot'],['LeftFoot','LeftToeBase'],['Hips','RightUpLeg'],['RightUpLeg','RightLeg'],['RightLeg','RightFoot'],['RightFoot','RightToeBase']];
    const chans = new Map(); for (const ch of a.listChannels()) { const n = ch.getTargetNode(); if (!chans.has(n)) chans.set(n, {}); chans.get(n)[ch.getTargetPath()] = sampler(ch); }
    for (let f = 0; f < c.frames; f += 8) {
      const loc = new Map(); for (const n of order) loc.set(n, { r: c.rot[f].get(n), t: n === hips ? c.hipsT[f] : n.getTranslation(), s: n.getScale() });
      const W = solve(bRoots, loc); out.base.push(bones.map(([x, y]) => [W.get(bNodes.get(x)).p, W.get(bNodes.get(y)).p]));
      const ul = new Map(); for (const [n, cc] of chans) ul.set(n, { r: cc.rotation ? cc.rotation(f / FPS) : n.getRotation(), t: cc.translation ? cc.translation(f / FPS) : n.getTranslation(), s: cc.scale ? cc.scale(f / FPS) : n.getScale() });
      const U = solve(uRoots, ul); out.kay.push(bones.map(([x, y]) => [U.get(uNodes.get(BACK[x] ?? (x === 'Spine2' ? 'chest' : x.endsWith('Arm') && !x.includes('Fore') ? (x.startsWith('Left') ? 'upperarm.l' : 'upperarm.r') : 'hips'))).p, U.get(uNodes.get(BACK[y] ?? 'chest')).p]));
    }
    (await import('fs')).writeFileSync(dump.slice(7), JSON.stringify(out));
  }
  if (dry) continue;
  for (const old of doc.getRoot().listAnimations()) if (old.getName() === name) old.dispose();
  const times = new Float32Array(c.frames); for (let f = 0; f < c.frames; f++) times[f] = f / FPS;
  const A = doc.createAnimation(name), input = doc.createAccessor().setType('SCALAR').setArray(times).setBuffer(buf);
  for (const n of order) {
    const arr = new Float32Array(c.frames * 4); for (let f = 0; f < c.frames; f++) arr.set(c.rot[f].get(n), f * 4);
    const smp = doc.createAnimationSampler().setInput(input).setOutput(doc.createAccessor().setType('VEC4').setArray(arr).setBuffer(buf)).setInterpolation('LINEAR');
    A.addSampler(smp).addChannel(doc.createAnimationChannel().setTargetNode(n).setTargetPath('rotation').setSampler(smp));
  }
  const ht = new Float32Array(c.frames * 3); for (let f = 0; f < c.frames; f++) ht.set(c.hipsT[f], f * 3);
  const ts = doc.createAnimationSampler().setInput(input).setOutput(doc.createAccessor().setType('VEC3').setArray(ht).setBuffer(buf)).setInterpolation('LINEAR');
  A.addSampler(ts).addChannel(doc.createAnimationChannel().setTargetNode(hips).setTargetPath('translation').setSampler(ts));
  console.log(name, c.frames, 'frames', c.len.toFixed(2), 's');
}
if (!dry) { await io.write(`${M}/lw_extra.glb`, doc); console.log('lw_extra.glb', doc.getRoot().listAnimations().map((a) => a.getName()).join(', ')); }
