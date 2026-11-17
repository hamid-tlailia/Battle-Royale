// Copies animations from Quaternius' Universal Animation Library (CC0, Unreal-mannequin bone names) onto the game's
// soldier (mixamo bone names) and adds them to soldier.glb as new clips (name + "_ual").
// Both rigs rest in a T-pose facing +Z, so each bone's world rotation change from its rest is applied to the
// matching soldier bone; the pelvis travel is scaled by the leg length.
// Run: node scripts/unity/retarget-ual.mjs <UAL1_Standard.glb>
import { NodeIO } from '@gltf-transform/core';
import { ALL_EXTENSIONS } from '@gltf-transform/extensions';
const io = new NodeIO().registerExtensions(ALL_EXTENSIONS);
const SOLDIER = 'unity/Assets/LastWar/Resources/Models/soldier.glb';
const CLIPS = { Sprint_Loop: 'sprint_ual', Jog_Fwd_Loop: 'jog_ual', Walk_Loop: 'walk_ual', Crouch_Fwd_Loop: 'crouchwalk_ual', Idle_Loop: 'idle_ual' };
const MAP = { pelvis: 'Hips', spine_01: 'Spine', spine_02: 'Spine1', spine_03: 'Spine2', neck_01: 'Neck', Head: 'Head' };
for (const s of ['l', 'r']) {
  const S = s === 'l' ? 'Left' : 'Right';
  Object.assign(MAP, { [`clavicle_${s}`]: `${S}Shoulder`, [`upperarm_${s}`]: `${S}Arm`, [`lowerarm_${s}`]: `${S}ForeArm`, [`hand_${s}`]: `${S}Hand`,
    [`thigh_${s}`]: `${S}UpLeg`, [`calf_${s}`]: `${S}Leg`, [`foot_${s}`]: `${S}Foot`, [`ball_${s}`]: `${S}ToeBase` });
  for (const f of ['index', 'middle', 'ring', 'pinky', 'thumb']) for (let k = 1; k <= 3; k++) MAP[`${f}_0${k}_${s}`] = `${S}Hand${f[0].toUpperCase() + f.slice(1)}${k}`;
}
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

const ual = await io.read(process.argv[2]);
const sol = await io.read(SOLDIER);
const uRoots = ual.getRoot().listScenes()[0].listChildren(), sRoots = sol.getRoot().listScenes()[0].listChildren();
const uNodes = new Map(ual.getRoot().listNodes().map((n) => [n.getName(), n]));
const sNodes = new Map(sol.getRoot().listNodes().filter((n) => n.getName().includes('mixamorig')).map((n) => [key(n.getName()), n]));
const uRest = solve(uRoots, new Map()), sRest = solve(sRoots, new Map());
// leg length ratio for the pelvis travel (world units of each file)
const len = (W, a, b) => Math.hypot(...W.get(a).p.map((x, i) => x - W.get(b).p[i]));
const kLeg = (len(sRest, sNodes.get('LeftUpLeg'), sNodes.get('LeftLeg')) + len(sRest, sNodes.get('LeftLeg'), sNodes.get('LeftFoot'))) /
             (len(uRest, uNodes.get('thigh_l'), uNodes.get('calf_l')) + len(uRest, uNodes.get('calf_l'), uNodes.get('foot_l')));
// soldier bones top-down (parents first)
const order = []; const visit = (n) => { if (n.getName().includes('mixamorig')) order.push(n); n.listChildren().forEach(visit); }; sRoots.forEach(visit);
const sBuf = sol.getRoot().listBuffers()[0];
for (const anim of ual.getRoot().listAnimations()) {
  const outName = CLIPS[anim.getName()]; if (!outName) continue;
  const chans = new Map(); let dur = 0;
  for (const ch of anim.listChannels()) { const n = ch.getTargetNode(); if (!chans.has(n)) chans.set(n, {}); chans.get(n)[ch.getTargetPath()] = sampler(ch);
    const ti = ch.getSampler().getInput().getArray(); dur = Math.max(dur, ti[ti.length - 1]); }
  const fps = 30, frames = Math.max(2, Math.round(dur * fps) + 1);
  const times = new Float32Array(frames), rots = new Map(order.map((n) => [n, new Float32Array(frames * 4)])), hipsT = new Float32Array(frames * 3);
  const hips = sNodes.get('Hips'), pelvis = uNodes.get('pelvis');
  for (let f = 0; f < frames; f++) {
    const t = Math.min(dur, f / fps); times[f] = t;
    const loc = new Map(); for (const [n, c] of chans) loc.set(n, { r: c.rotation ? c.rotation(t) : n.getRotation(), t: c.translation ? c.translation(t) : n.getTranslation(), s: c.scale ? c.scale(t) : n.getScale() });
    const uW = solve(uRoots, loc);
    const sLoc = new Map(), sW = new Map();
    for (const n of order) {
      const parent = n.listParents().find((p) => p.propertyType === 'Node');
      const pw = parent && sW.has(parent) ? sW.get(parent) : sRest.get(parent) ?? { r: [0, 0, 0, 1] };
      const uName = Object.keys(MAP).find((k) => MAP[k] === key(n.getName()));
      let wr;
      if (uName && uNodes.has(uName)) { const un = uNodes.get(uName); const delta = qmul(uW.get(un).r, qinv(uRest.get(un).r)); wr = qmul(delta, sRest.get(n).r); }
      else wr = qmul(pw.r, n.getRotation());
      const lr = qnorm(qmul(qinv(pw.r), wr));
      sW.set(n, { r: wr }); sLoc.set(n, lr); rots.get(n).set(lr, f * 4);
    }
    // pelvis travel: the world offset from its rest, scaled, into the hips' parent space
    const d = uW.get(pelvis).p.map((x, i) => (x - uRest.get(pelvis).p[i]) * kLeg);
    const hp = hips.listParents().find((p) => p.propertyType === 'Node'); const hw = sRest.get(hp);
    const dl = qrot(qinv(hw.r), d).map((x, i) => x / hw.s[i]);
    hipsT.set(hips.getTranslation().map((x, i) => x + dl[i]), f * 3);
  }
  const A = sol.createAnimation(outName);
  const input = sol.createAccessor().setType('SCALAR').setArray(times).setBuffer(sBuf);
  for (const n of order) {
    const smp = sol.createAnimationSampler().setInput(input).setOutput(sol.createAccessor().setType('VEC4').setArray(rots.get(n)).setBuffer(sBuf)).setInterpolation('LINEAR');
    A.addSampler(smp).addChannel(sol.createAnimationChannel().setTargetNode(n).setTargetPath('rotation').setSampler(smp));
  }
  const ts = sol.createAnimationSampler().setInput(input).setOutput(sol.createAccessor().setType('VEC3').setArray(hipsT).setBuffer(sBuf)).setInterpolation('LINEAR');
  A.addSampler(ts).addChannel(sol.createAnimationChannel().setTargetNode(hips).setTargetPath('translation').setSampler(ts));
  console.log(outName, frames, 'frames', dur.toFixed(2), 's');
}
await io.write(SOLDIER, sol);
