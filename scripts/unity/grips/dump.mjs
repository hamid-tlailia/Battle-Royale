// Step B: each gun model's triangles (glTF scene space) for the grip tools. Run from the repo root:
//   node scripts/unity/grips/dump.mjs <out dir>  ->  <out dir>/gun_<look>.json ; then in <out dir>/..:
//   python3 check.py (points against the geometry) / python3 draw.py markers.json sheet.png / python3 gridview.py looks out.png
// (the python tools read grips/gun_*.json relative to the current directory)
import fs from 'node:fs';
import { NodeIO } from '@gltf-transform/core';
import { ALL_EXTENSIONS } from '@gltf-transform/extensions';
import { apply } from '../gltf-util.mjs';
const io = new NodeIO().registerExtensions(ALL_EXTENSIONS);
const dir = 'unity/Assets/LastWar/Resources/Models', out = process.argv[2];
for (const f of fs.readdirSync(dir).filter((f) => /^gun_.*\.glb$/.test(f))) {
  const d = await io.read(`${dir}/${f}`); const pts = [];
  d.getRoot().listScenes()[0].traverse((n) => { const m = n.getMesh(); if (!m) return; const wm = n.getWorldMatrix();
    for (const p of m.listPrimitives()) { const a = p.getAttribute('POSITION'), I = p.getIndices(); const e = [];
      const V = []; for (let i = 0; i < a.getCount(); i++) V.push(apply(wm, a.getElement(i, e)).map((x) => +x.toFixed(4)));
      const tris = []; if (I) for (let t = 0; t < I.getCount(); t += 3) tris.push([I.getScalar(t), I.getScalar(t + 1), I.getScalar(t + 2)]);
      pts.push({ V, tris }); } });
  fs.writeFileSync(`${out}/${f.replace('.glb', '.json')}`, JSON.stringify(pts));
}
