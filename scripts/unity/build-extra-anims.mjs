// Builds extra Mixamo clips (Resources/Models/lw_extra.glb), kept apart from lw_base.glb, carried onto the
// LW_BaseCharacter skeleton exactly as build-base.mjs carries the others (each bone's world rotation change from the rest
// pose; the pelvis keeps only its height change, so the clips run in place). Used for previews / trials of new moves
// before any of them is given a role in the game.
// Run: node scripts/unity/build-extra-anims.mjs <dir of FBX2glTF .glb clips> name=file [name=file ...]
//   e.g. mx_rifle_run=rifle_run mx_running=running
//   name=file+relax=other : the fingers taken from clip file `other` (its first frame) instead - open, relaxed hands
//   name=file+legsR : the left leg made the mirror image of the right leg half a cycle later (upper body as recorded)
//   name=file+sym : made left / right symmetric - each frame is the halfway blend of the pose and its mirror image half
//                   a cycle later (world rotation changes from the rest, mirrored across the body's middle plane)
//   name=file+air=a-b : only the frames from a to b seconds, the pelvis at its standing height (a jump's time in the air)
//   name=file+upper=other : legs / hips / lower back from file, chest, head, arms, hands from `other` (stretched to its cycle)
//   --frames : with --check / --report, the hips and toe heights frame by frame
//   --out=lw_pistol : write that file instead of lw_extra.glb
//   --report: also print the carried pose of each clip (as --check)
//   --check: write nothing; print the carried pose per frame (heights above the ground, left / right symmetry, finger bend)
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
  return { at, len: len_, frames, rot, hipsT };
}

const dir = process.argv[2];
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
const check = process.argv.includes('--check');
// the carried pose on the base skeleton, for a look before anything is written
function report(c) {
  const ground = Math.min(...['LeftToeBase', 'RightToeBase', 'LeftFoot', 'RightFoot'].map((j) => bRest.get(bNodes.get(j)).p[1]));
  const J = ['Hips', 'Spine2', 'Head', 'LeftForeArm', 'RightForeArm', 'LeftHand', 'RightHand', 'LeftLeg', 'RightLeg', 'LeftToeBase', 'RightToeBase'];
  const ser = Object.fromEntries(J.map((j) => [j, []]));
  const fingers = ['Index', 'Middle', 'Ring', 'Pinky'];
  const bend = { Left: [], Right: [] }; // mean bend of the finger joints (deg): 0 = straight, ~90 = a fist around a grip
  const W0 = solve(bRoots, new Map());
  for (let f = 0; f < c.frames; f++) {
    const loc = new Map(); for (const n of order) loc.set(n, { r: c.rot[f].get(n), t: n === hips ? c.hipsT[f] : n.getTranslation(), s: n.getScale() });
    const W = solve(bRoots, loc);
    for (const j of J) ser[j].push(W.get(bNodes.get(j)).p.map((x, i) => i === 1 ? x - ground : x - W.get(hips).p[i]));
    for (const sd of ['Left', 'Right']) { let a = 0, k = 0;
      for (const fg of fingers) for (let q = 1; q <= 2; q++) { const A = bNodes.get(`${sd}Hand${fg}${q}`), B = bNodes.get(`${sd}Hand${fg}${q + 1}`), C = bNodes.get(`${sd}Hand${fg}${q + 2}`) ?? bNodes.get(`${sd}Hand${fg}4`);
        if (!A || !B || !C || B === C) continue; const u = W.get(B).p.map((x, i) => x - W.get(A).p[i]), v = W.get(C).p.map((x, i) => x - W.get(B).p[i]);
        a += Math.acos(Math.max(-1, Math.min(1, (u[0] * v[0] + u[1] * v[1] + u[2] * v[2]) / (Math.hypot(...u) * Math.hypot(...v))))) * 57.3; k++; }
      bend[sd].push(k ? a / k : NaN); }
  }
  const rng = (a, i) => { const v = a.map((x) => x[i]); return `${Math.min(...v).toFixed(2)}..${Math.max(...v).toFixed(2)}`; };
  console.log('  heights above the ground (m), min..max over the clip:');
  for (const j of J) console.log('   ', j.padEnd(13), rng(ser[j], 1));
  // symmetry: the left limb's track against the right limb's, mirrored (x -> -x) and shifted by half a cycle
  const half = Math.round((c.frames - 1) / 2), n = c.frames - 1;
  for (const [l, r] of [['LeftHand', 'RightHand'], ['LeftLeg', 'RightLeg'], ['LeftToeBase', 'RightToeBase']]) {
    let e = 0; for (let f = 0; f < n; f++) { const a = ser[l][f], b = ser[r][(f + half) % n]; e += Math.hypot(a[0] + b[0], a[1] - b[1], a[2] - b[2]); }
    const sw = (s) => Math.max(...s.map((x) => x[2])) - Math.min(...s.map((x) => x[2]));
    console.log(`  symmetry ${l.replace('Left', '')}: mean gap left vs mirrored right half a cycle later ${(e / n * 100).toFixed(1)} cm; forward swing left ${(sw(ser[l]) * 100).toFixed(0)} cm, right ${(sw(ser[r]) * 100).toFixed(0)} cm`);
  }
  // --frames: hips / toe heights frame by frame (where a jump leaves and meets the ground)
  if (process.argv.includes('--frames')) for (let f = 0; f < c.frames; f++)
    console.log(`  ${(f / FPS).toFixed(2)} s  hips ${ser.Hips[f][1].toFixed(2)}  toes L ${ser.LeftToeBase[f][1].toFixed(2)} R ${ser.RightToeBase[f][1].toFixed(2)}`);
  const avg = (a) => a.reduce((x, y) => x + y, 0) / a.length;
  console.log(`  finger bend (mean joint angle): left ${avg(bend.Left).toFixed(0)} deg, right ${avg(bend.Right).toFixed(0)} deg`);
}
// ---- variants of a carried clip (made on the baked frames; nothing else changes)
const parentOf = (n) => n.listParents().find((p) => p.propertyType === 'Node');
const worlds = (loc) => { const W = new Map(); for (const n of order) { const p = parentOf(n); const pw = W.get(p) ?? bRest.get(p)?.r ?? [0, 0, 0, 1]; W.set(n, qmul(pw, loc.get(n))); } return W; };
const locals = (W) => { const L = new Map(); for (const n of order) { const p = parentOf(n); const pw = W.get(p) ?? bRest.get(p)?.r ?? [0, 0, 0, 1]; L.set(n, qnorm(qmul(qinv(pw), W.get(n)))); } return L; };
const mirrorOf = new Map(order.map((n) => { const k = key(n.getName()); const m = k.startsWith('Left') ? 'Right' + k.slice(4) : k.startsWith('Right') ? 'Left' + k.slice(5) : k; return [n, bNodes.get(m) ?? n]; }));
const mirrorQ = (q) => [q[0], -q[1], -q[2], q[3]]; // a world rotation seen in the mirror (x -> -x)
function symmetric(c) {
  const n = c.frames - 1, half = Math.round(n / 2);
  const D = c.rot.map((loc) => { const W = worlds(loc); return new Map(order.map((b) => [b, qmul(W.get(b), qinv(bRest.get(b).r))])); });
  const rot = [], hipsT = [];
  for (let f = 0; f < c.frames; f++) {
    const g = (f + half) % n, W = new Map();
    for (const b of order) W.set(b, qmul(slerp(D[f % n].get(b), mirrorQ(D[g].get(mirrorOf.get(b))), 0.5), bRest.get(b).r));
    rot.push(locals(W)); hipsT.push(lerp(c.hipsT[f % n], c.hipsT[g], 0.5));
  }
  return { ...c, rot, hipsT };
}
// legs only: the left leg made the mirror image of the right leg half a cycle later (a crawl pushed by one leg becomes
// one pushed by both in turn, with the same stroke); the pelvis roll made even; spine, arms and hands as recorded
const LEG = /(Up)?Leg$|Foot$|ToeBase$|Toe_End$/;
function legsFromRight(c) {
  const n = c.frames - 1, half = Math.round(n / 2);
  const D = c.rot.map((loc) => { const W = worlds(loc); return new Map(order.map((b) => [b, qmul(W.get(b), qinv(bRest.get(b).r))])); });
  const rot = [], hipsT = [];
  for (let f = 0; f < c.frames; f++) {
    const g = (f + half) % n, W = new Map();
    for (const b of order) { const k = key(b.getName()); const left = k.startsWith('Left') && LEG.test(k);
      // the pelvis: its sideways roll made even (the halfway blend with its mirror image half a cycle later), so both
      // hip joints sit alike over the ground; everything above keeps its recorded turn in the world
      const d = k === 'Hips' ? slerp(D[f % n].get(b), mirrorQ(D[g].get(b)), 0.5) : left ? mirrorQ(D[g].get(mirrorOf.get(b))) : D[f % n].get(b);
      W.set(b, qmul(d, bRest.get(b).r)); }
    rot.push(locals(W)); hipsT.push(lerp(c.hipsT[f % n], c.hipsT[g], 0.5));
  }
  return { ...c, rot, hipsT };
}
// a one-shot cut: only the frames from a to b seconds (a jump's time in the air, without the crouch before it and the
// landing after it), the pelvis held at its standing height of the first frame - the game lifts the body itself
function cut(c, a, b) {
  const fa = Math.max(0, Math.round(a * FPS)), fb = Math.min(c.frames - 1, Math.round(b * FPS));
  const rot = c.rot.slice(fa, fb + 1), hipsT = rot.map(() => c.hipsT[0].slice());
  return { ...c, frames: rot.length, len: (rot.length - 1) / FPS, rot, hipsT };
}
// a run made of two: the legs, hips and lower back of this clip (its stride), the chest, neck, head, arms and hands of
// clip `other` (its gun hold), that clip stretched to this one's cycle (both loop: the same fraction of each cycle)
const UPPER = /Spine2|Neck|Head|Shoulder|Arm|Hand/;
function upperFrom(c, o) {
  const rot = c.rot.map((loc, f) => { const g = Math.round(f / (c.frames - 1) * (o.frames - 1)) % o.frames;
    return new Map(order.map((b) => [b, UPPER.test(key(b.getName())) ? o.rot[g].get(b) : loc.get(b)])); });
  return { ...c, rot };
}
const FINGER = /Hand(Index|Middle|Ring|Pinky|Thumb)/;
function relaxed(c, ref) { return { ...c, rot: c.rot.map((loc) => new Map(order.map((b) => [b, FINGER.test(b.getName()) ? ref.rot[0].get(b) : loc.get(b)]))) }; }
const atOf = (c) => (t) => { const len_ = c.len; t = ((t % len_) + len_) % len_; const x = t * FPS, i = Math.floor(x), u = x - i, j = Math.min(i + 1, c.frames - 1);
  const r = new Map(); for (const n of order) r.set(n, slerp(c.rot[i].get(n), c.rot[j].get(n), u)); return { r, h: lerp(c.hipsT[i], c.hipsT[j], u) }; };
const done = new Map();
for (const arg of process.argv.slice(3).filter((a) => a.includes('=') && !a.startsWith('--'))) {
  const [name, spec] = arg.split(/=(.*)/s);
  const [file, ...opts] = spec.split('+');
  let c = done.get(file) ?? carry(await io.read(`${dir}/${file}.glb`)); done.set(file, c);
  for (const o of opts) {
    if (o === 'sym') c = symmetric(c);
    else if (o === 'legsR') c = legsFromRight(c);
    else if (o.startsWith('upper=')) { const uf = o.slice(6); const ref = done.get(uf) ?? carry(await io.read(`${dir}/${uf}.glb`)); done.set(uf, ref); c = upperFrom(c, ref); }
    else if (o.startsWith('air=')) { const [x, y] = o.slice(4).split('-').map(Number); c = cut(c, x, y); }
    else if (o.startsWith('relax=')) { const rf = o.slice(6); const ref = done.get(rf) ?? carry(await io.read(`${dir}/${rf}.glb`)); done.set(rf, ref); c = relaxed(c, ref); }
  }
  if (check || process.argv.includes('--report')) { console.log(name); report(c); }
  if (check) continue;
  write(name, c.len, atOf(c));
}
// --out=name: write Resources/Models/<name>.glb instead of lw_extra.glb (e.g. lw_pistol: the pistol clips, a file apart)
const outArg = process.argv.find((a) => a.startsWith('--out='));
if (!check) await io.write(`${M}/${outArg ? outArg.slice(6) : 'lw_extra'}.glb`, base);
console.log(outArg ? outArg.slice(6) + '.glb' : 'lw_extra.glb', base.getRoot().listAnimations().length, 'clips');
