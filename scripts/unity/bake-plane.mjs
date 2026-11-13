// Bakes the drop plane's orientation into its vertices: nose toward -Z, centred, ~26 m long, every node
// transform identity. The web game and Unity then place it without any per-engine rotation tricks.
// node scripts/unity/bake-plane.mjs <in.glb> <out.glb>
import { NodeIO } from '@gltf-transform/core';
import { ALL_EXTENSIONS } from '@gltf-transform/extensions';
import { transformPrimitive } from '@gltf-transform/functions';
// tiny column-major 4x4 helpers (glTF matrix layout)
const mul = (a, b) => { const o = new Array(16).fill(0); for (let c = 0; c < 4; c++) for (let r = 0; r < 4; r++) for (let k = 0; k < 4; k++) o[c * 4 + r] += a[k * 4 + r] * b[c * 4 + k]; return o; };
const ident = () => [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
const io = new NodeIO().registerExtensions(ALL_EXTENSIONS);
const doc = await io.read(process.argv[2]);
const root = doc.getRoot();
const scene = root.getDefaultScene() || root.listScenes()[0];
// fixed correction measured on this model: turn -126.87 deg about Y, centre, scale 0.85
const k = 0.85, a = -2.2143;
const T = ident(); T[12] = 1.462 * k; T[13] = -4.065 * k; T[14] = 4.963 * k;
const c = Math.cos(a), sn = Math.sin(a);
const R = [c, 0, -sn, 0, 0, 1, 0, 0, sn, 0, c, 0, 0, 0, 0, 1]; // rotation about Y
const S = ident(); S[0] = S[5] = S[10] = k;
const extra = mul(T, mul(R, S));
const done = new Set();
const nodes = root.listNodes().filter((n) => n.getMesh());
for (const n of nodes) {
  const world = mul(extra, Array.from(n.getWorldMatrix()));
  let mesh = n.getMesh();
  if (done.has(mesh)) { mesh = mesh.clone(); n.setMesh(mesh); }
  done.add(mesh);
  for (const p of mesh.listPrimitives()) {
    // shared accessors between primitives would be transformed twice: give each its own copy
    for (const s of p.listSemantics()) { const acc = p.getAttribute(s); if (acc.listParents().filter((x) => x.propertyType === 'Primitive').length > 1) p.setAttribute(s, acc.clone()); }
    transformPrimitive(p, world);
  }
  scene.addChild(n);
  n.setMatrix(ident());
}
// centre on the bounding box (the vertex-weighted guess above leaves it a few metres off)
{
  const mn = [Infinity, Infinity, Infinity], mx = [-Infinity, -Infinity, -Infinity], v = [0, 0, 0], seen = new Set();
  for (const n of nodes) for (const p of n.getMesh().listPrimitives()) { const acc = p.getAttribute('POSITION'); if (seen.has(acc)) continue; seen.add(acc); for (let i = 0; i < acc.getCount(); i++) { acc.getElement(i, v); for (let j = 0; j < 3; j++) { mn[j] = Math.min(mn[j], v[j]); mx[j] = Math.max(mx[j], v[j]); } } }
  const T2 = ident(); for (let j = 0; j < 3; j++) T2[12 + j] = -(mn[j] + mx[j]) / 2;
  seen.clear();
  for (const n of nodes) for (const p of n.getMesh().listPrimitives()) { if (seen.has(p)) continue; seen.add(p); transformPrimitive(p, T2); }
}
// drop the old transform-only nodes
for (const n of root.listNodes()) if (!n.getMesh() && !n.listChildren().length) n.dispose();
for (const n of scene.listChildren()) if (!n.getMesh() && n.listChildren().every((c) => !c.getMesh())) n.dispose();
await io.write(process.argv[3], doc);
console.log('baked', nodes.length, 'meshes');
