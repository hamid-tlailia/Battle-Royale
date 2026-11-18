// Builds the lobby animation (Resources/Models/lw_lobby.glb), kept apart from lw_base.glb: the Mixamo "Rifle Idle"
// (standing, rifle down, glancing left now and then) and "Rifle Aiming Idle" (standing, rifle up at a target), carried
// onto the LW_BaseCharacter skeleton exactly as build-base.mjs carries the other Mixamo clips (same rig: each bone's
// world rotation change from the rest pose; the pelvis keeps only its height change), then baked into one looping clip
// "mx_lobby": rifle idle -> smooth blend -> aiming -> smooth blend back -> rifle idle ...
// The two source clips are also kept as "mx_lobby_idle" and "mx_lobby_aim".
// Run: node scripts/unity/build-lobby-anim.mjs <dir with rifle_idle.glb and rifle_aiming_idle.glb (FBX2glTF output)>
import { NodeIO } from '@gltf-transform/core';
import { ALL_EXTENSIONS } from '@gltf-transform/extensions';
import { prune } from '@gltf-transform/functions';
const io = new NodeIO().registerExtensions(ALL_EXTENSIONS);
const M = 'unity/Assets/LastWar/Resources/Models';
// timeline (seconds): the whole rifle idle, BLEND into the aim, AIM held, BLEND back (ending where the idle starts)
const BLEND = 0.8, AIM_HOLD = 4.6;
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


const base = await io.read(`${M}/lw_base.glb`); // (read only: its skeleton is copied, the file is not written)
for (const a of base.getRoot().listAnimations()) a.dispose();
await base.transform(prune({ keepLeaves: true }));
const bRoots = base.getRoot().listScenes()[0].listChildren();
const bNodes = new Map(base.getRoot().listNodes().filter((n) => n.getName().includes('mixamorig')).map((n) => [key(n.getName()), n]));
const bRest = solve(bRoots, new Map());
const order = []; const visit = (n) => { if (n.getName().includes('mixamorig')) order.push(n); n.listChildren().forEach(visit); }; bRoots.forEach(visit);
const buf = base.getRoot().listBuffers()[0] ?? base.createBuffer();
const len = (W, a, b) => Math.hypot(...W.get(a).p.map((x, i) => x - W.get(b).p[i]));
const FPS = 30;

// one source clip carried onto the base skeleton: per frame, every bone's local rotation and the hips' translation
function carry(src) {
  const anim = src.getRoot().listAnimations()[0];
  const uRoots = src.getRoot().listScenes()[0].listChildren();
  const uNodes = new Map(src.getRoot().listNodes().filter((n) => n.getName().includes('mixamorig')).map((n) => [key(n.getName()), n]));
  const uRest = solve(uRoots, new Map());
  const kLeg = (len(bRest, bNodes.get('LeftUpLeg'), bNodes.get('LeftLeg')) + len(bRest, bNodes.get('LeftLeg'), bNodes.get('LeftFoot'))) /
               (len(uRest, uNodes.get('LeftUpLeg'), uNodes.get('LeftLeg')) + len(uRest, uNodes.get('LeftLeg'), uNodes.get('LeftFoot')));
  const chans = new Map(); let dur = 0;
  for (const ch of anim.listChannels()) { const n = ch.getTargetNode(); if (!n) continue; if (!chans.has(n)) chans.set(n, {}); chans.get(n)[ch.getTargetPath()] = sampler(ch);
    const ti = ch.getSampler().getInput().getArray(); dur = Math.max(dur, ti[ti.length - 1]); }
  const frames = Math.max(2, Math.round(dur * FPS) + 1), rot = [], hipsT = [];
  const hips = bNodes.get('Hips'), pelvis = uNodes.get('Hips');
  for (let f = 0; f < frames; f++) {
    const t = Math.min(dur, f / FPS);
    const loc = new Map(); for (const [n, c] of chans) loc.set(n, { r: c.rotation ? c.rotation(t) : n.getRotation(), t: c.translation ? c.translation(t) : n.getTranslation(), s: c.scale ? c.scale(t) : n.getScale() });
    const uW = solve(uRoots, loc), sW = new Map(), r = new Map();
    for (const n of order) {
      const parent = n.listParents().find((p) => p.propertyType === 'Node');
      const pw = parent && sW.has(parent) ? sW.get(parent) : bRest.get(parent) ?? { r: [0, 0, 0, 1] };
      const un = uNodes.get(key(n.getName()));
      let wr;
      if (un && uW.has(un)) wr = qmul(qmul(uW.get(un).r, qinv(uRest.get(un).r)), bRest.get(n).r);
      else wr = qmul(pw.r, n.getRotation());
      sW.set(n, { r: wr }); r.set(n, qnorm(qmul(qinv(pw.r), wr)));
    }
    const d = uW.get(pelvis).p.map((x, i) => (x - uRest.get(pelvis).p[i]) * kLeg); d[0] = 0; d[2] = 0;
    const hp = hips.listParents().find((p) => p.propertyType === 'Node'); const hw = bRest.get(hp) ?? { r: [0, 0, 0, 1], s: [1, 1, 1] };
    const dl = qrot(qinv(hw.r), d).map((x, i) => x / hw.s[i]);
    rot.push(r); hipsT.push(hips.getTranslation().map((x, i) => x + dl[i]));
  }
  const len_ = (frames - 1) / FPS;
  // pose at any time (looping), interpolated between baked frames
  const at = (t) => { t = ((t % len_) + len_) % len_; const x = t * FPS, i = Math.floor(x), u = x - i, j = Math.min(i + 1, frames - 1);
    const r = new Map(); for (const n of order) r.set(n, slerp(rot[i].get(n), rot[j].get(n), u));
    return { r, h: lerp(hipsT[i], hipsT[j], u) }; };
  console.log('carried', frames, 'frames', len_.toFixed(2), 's, leg ratio', kLeg.toFixed(3));
  return { at, len: len_ };
}

const dir = process.argv[2];
const idle = carry(await io.read(`${dir}/rifle_idle.glb`)), aim = carry(await io.read(`${dir}/rifle_aiming_idle.glb`));
const hips = bNodes.get('Hips');
function write(name, length, pose) {
  const frames = Math.round(length * FPS) + 1, times = new Float32Array(frames);
  const rots = new Map(order.map((n) => [n, new Float32Array(frames * 4)])), hipsT = new Float32Array(frames * 3);
  for (let f = 0; f < frames; f++) { const t = f / FPS; times[f] = t; const p = pose(t); for (const n of order) rots.get(n).set(p.r.get(n), f * 4); hipsT.set(p.h, f * 3); }
  const A = base.createAnimation(name), input = base.createAccessor().setType('SCALAR').setArray(times).setBuffer(buf);
  for (const n of order) {
    const smp = base.createAnimationSampler().setInput(input).setOutput(base.createAccessor().setType('VEC4').setArray(rots.get(n)).setBuffer(buf)).setInterpolation('LINEAR');
    A.addSampler(smp).addChannel(base.createAnimationChannel().setTargetNode(n).setTargetPath('rotation').setSampler(smp));
  }
  const ts = base.createAnimationSampler().setInput(input).setOutput(base.createAccessor().setType('VEC3').setArray(hipsT).setBuffer(buf)).setInterpolation('LINEAR');
  A.addSampler(ts).addChannel(base.createAnimationChannel().setTargetNode(hips).setTargetPath('translation').setSampler(ts));
  console.log(name.padEnd(14), frames, 'frames', length.toFixed(2), 's');
}
const smooth = (x) => { x = Math.min(1, Math.max(0, x)); return x * x * (3 - 2 * x); };
const blend = (a, b, w) => { const r = new Map(); for (const n of order) r.set(n, slerp(a.r.get(n), b.r.get(n), w)); return { r, h: lerp(a.h, b.h, w) }; };
// idle 0..I, blend I..I+B, aim held, blend back over the last B seconds - the idle runs on through the blend back so the
// clip ends exactly where it starts (seamless loop); the aim's own clock starts when the blend toward it starts
const I = idle.len, T = I + BLEND + AIM_HOLD + BLEND;
write('mx_lobby', T, (t) => {
  if (t <= I) return idle.at(t);
  const ta = t - I;
  if (t <= I + BLEND) return blend(idle.at(t), aim.at(ta), smooth((t - I) / BLEND));
  if (t <= I + BLEND + AIM_HOLD) return aim.at(ta);
  return blend(aim.at(ta), idle.at(I - (T - t)), smooth((t - (T - BLEND)) / BLEND)); // (idle time ends at I = its start)
});
write('mx_lobby_idle', idle.len, idle.at);
write('mx_lobby_aim', aim.len, aim.at);
await io.write(`${M}/lw_lobby.glb`, base);
console.log('lw_lobby.glb', base.getRoot().listAnimations().length, 'clips');
