// Copies Mixamo rifle animations (Pro Rifle Pack, played on Mixamo's SWAT skeleton) onto the game's soldier and adds
// them to soldier.glb as clips "mx_<name>". Both rigs are mixamo rigs resting in a T-pose: each bone's world rotation
// change from its rest is applied to the soldier's bone of the same name (their bone axes may differ); the pelvis keeps
// only its up/down travel (the clips run in place).
// Run: node scripts/unity/retarget-mixamo.mjs <dir of converted .glb clips>   (clips converted with FBX2glTF)
import fs from 'node:fs';
import { NodeIO } from '@gltf-transform/core';
import { ALL_EXTENSIONS } from '@gltf-transform/extensions';
const io = new NodeIO().registerExtensions(ALL_EXTENSIONS);
const SOLDIER = 'unity/Assets/LastWar/Resources/Models/soldier.glb';
const CLIPS = { idle: 'mx_idle', idle_aiming: 'mx_aim', walk_forward: 'mx_walk', run_forward: 'mx_run', sprint_forward: 'mx_sprint',
  jump_loop: 'mx_jump', idle_crouching: 'mx_crouch', idle_crouching_aiming: 'mx_crouch_aim', walk_crouching_forward: 'mx_crouch_walk',
  death_from_the_front: 'mx_death', fire_a: 'mx_fire', prone_idle: 'mx_prone', prone_forward: 'mx_crawl', fire_b: 'mx_crouch_fire', prone_fire: 'mx_prone_fire', reload: 'mx_reload' };
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


const sol = await io.read(SOLDIER);
const sRoots = sol.getRoot().listScenes()[0].listChildren();
const sNodes = new Map(sol.getRoot().listNodes().filter((n) => n.getName().includes('mixamorig')).map((n) => [key(n.getName()), n]));
const sRest = solve(sRoots, new Map());
const order = []; const visit = (n) => { if (n.getName().includes('mixamorig')) order.push(n); n.listChildren().forEach(visit); }; sRoots.forEach(visit);
const sBuf = sol.getRoot().listBuffers()[0];
for (const a of sol.getRoot().listAnimations()) if (a.getName().startsWith('mx_')) a.dispose(); // re-runs replace
const len = (W, a, b) => Math.hypot(...W.get(a).p.map((x, i) => x - W.get(b).p[i]));
for (const [file, outName] of Object.entries(CLIPS)) {
  const src = await io.read(`${process.argv[2]}/${file}.glb`);
  const uRoots = src.getRoot().listScenes()[0].listChildren();
  const uNodes = new Map(src.getRoot().listNodes().filter((n) => n.getName().includes('mixamorig')).map((n) => [key(n.getName()), n]));
  const uRest = solve(uRoots, new Map());
  const kLeg = (len(sRest, sNodes.get('LeftUpLeg'), sNodes.get('LeftLeg')) + len(sRest, sNodes.get('LeftLeg'), sNodes.get('LeftFoot'))) /
               (len(uRest, uNodes.get('LeftUpLeg'), uNodes.get('LeftLeg')) + len(uRest, uNodes.get('LeftLeg'), uNodes.get('LeftFoot')));
  const anim = src.getRoot().listAnimations()[0];
  const chans = new Map(); let dur = 0;
  for (const ch of anim.listChannels()) { const n = ch.getTargetNode(); if (!chans.has(n)) chans.set(n, {}); chans.get(n)[ch.getTargetPath()] = sampler(ch);
    const ti = ch.getSampler().getInput().getArray(); dur = Math.max(dur, ti[ti.length - 1]); }
  const fps = 30, frames = Math.max(2, Math.round(dur * fps) + 1);
  const times = new Float32Array(frames), rots = new Map(order.map((n) => [n, new Float32Array(frames * 4)])), hipsT = new Float32Array(frames * 3);
  const hips = sNodes.get('Hips'), pelvis = uNodes.get('Hips');
  for (let f = 0; f < frames; f++) {
    const t = Math.min(dur, f / fps); times[f] = t;
    const loc = new Map(); for (const [n, c] of chans) loc.set(n, { r: c.rotation ? c.rotation(t) : n.getRotation(), t: c.translation ? c.translation(t) : n.getTranslation(), s: c.scale ? c.scale(t) : n.getScale() });
    const uW = solve(uRoots, loc);
    const sW = new Map();
    for (const n of order) {
      const parent = n.listParents().find((p) => p.propertyType === 'Node');
      const pw = parent && sW.has(parent) ? sW.get(parent) : sRest.get(parent) ?? { r: [0, 0, 0, 1] };
      const un = uNodes.get(key(n.getName()));
      let wr;
      if (un && uW.has(un)) { const delta = qmul(uW.get(un).r, qinv(uRest.get(un).r)); wr = qmul(delta, sRest.get(n).r); }
      else wr = qmul(pw.r, n.getRotation());
      const lr = qnorm(qmul(qinv(pw.r), wr));
      sW.set(n, { r: wr }); rots.get(n).set(lr, f * 4);
    }
    // pelvis: only its height change (in place), scaled to the soldier's legs
    const d = uW.get(pelvis).p.map((x, i) => (x - uRest.get(pelvis).p[i]) * kLeg); d[0] = 0; d[2] = 0;
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
