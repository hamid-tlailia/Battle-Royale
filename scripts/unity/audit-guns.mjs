// Step A audit: each gun model as stored (glTF scene space): root node transform, scene-space bounds, barrel axis,
// muzzle end (the thinner end), grip (origin) position along the barrel. Run: node scripts/unity/audit-guns.mjs
import fs from 'node:fs';
import { NodeIO } from '@gltf-transform/core';
import { ALL_EXTENSIONS } from '@gltf-transform/extensions';
import { scenePoints } from './gltf-util.mjs';
const io = new NodeIO().registerExtensions(ALL_EXTENSIONS);
const dir = 'unity/Assets/LastWar/Resources/Models';
for (const f of fs.readdirSync(dir).filter((f) => /^gun_.*\.glb$/.test(f)).sort()) {
  const d = await io.read(`${dir}/${f}`); const sc = d.getRoot().listScenes()[0];
  const roots = sc.listChildren(); const r = roots[0];
  const pts = scenePoints(sc);
  const mn = [0, 1, 2].map((i) => Math.min(...pts.map((p) => p[i]))), mx = [0, 1, 2].map((i) => Math.max(...pts.map((p) => p[i])));
  const size = mx.map((x, i) => x - mn[i]); const ax = size.indexOf(Math.max(...size));
  // thickness (other two axes' extent) in the last 15 % of each end
  const L = size[ax], cut = 0.15 * L;
  const thick = (lo) => { const s = pts.filter((p) => lo ? p[ax] < mn[ax] + cut : p[ax] > mx[ax] - cut); const o = [0, 1, 2].filter((i) => i !== ax);
    return o.map((i) => Math.max(...s.map((p) => p[i])) - Math.min(...s.map((p) => p[i]))).reduce((a, b) => a + b, 0); };
  const tMin = thick(true), tMax = thick(false); const muzzle = tMin < tMax ? '-' : '+';
  const gripFrac = (0 - mn[ax]) / L; // 0 = the min end, 1 = the max end
  const rot = r.getRotation(); const yaw = 2 * Math.atan2(rot[1], rot[3]) * 180 / Math.PI;
  console.log(`${f.padEnd(26)} roots ${roots.length} rootRotY ${yaw.toFixed(0).padStart(4)}deg rootScale ${r.getScale()[0].toFixed(4)} | size ${size.map((x) => x.toFixed(2)).join('x')} | barrel axis ${'XYZ'[ax]} muzzle ${muzzle}${'XYZ'[ax]} (end thickness ${tMin.toFixed(3)} / ${tMax.toFixed(3)}) | grip at ${(gripFrac * 100).toFixed(0)}% from ${'XYZ'[ax]}min | height axis ${'XYZ'[[0, 1, 2].filter((i) => i !== ax).sort((a, b) => size[b] - size[a])[0]]}`);
}
