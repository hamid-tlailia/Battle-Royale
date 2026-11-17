// Splits every outfit's skinned body into four skinned meshes - head (mask / helmet), torso (shirt, vest, arms),
// legs (pants) and feet (shoes) - by the bone each vertex follows most. The pieces share the skin and the textures,
// so the file stays about the same size; the game shows one piece from each chosen outfit (mix and match).
// Run once on the Unity models: node scripts/unity/split-outfits.mjs
import fs from 'node:fs';
import { NodeIO } from '@gltf-transform/core';
import { ALL_EXTENSIONS } from '@gltf-transform/extensions';
import { compactPrimitive, prune } from '@gltf-transform/functions';
const io = new NodeIO().registerExtensions(ALL_EXTENSIONS);
const DIR = 'unity/Assets/LastWar/Resources/Models';
const PARTS = ['head', 'torso', 'legs', 'feet'];
const region = (name) => {
  const n = name.slice(name.lastIndexOf(':') + 1);
  if (/Neck|Head|Eye|Jaw/.test(n)) return 0;
  if (/Foot|Toe/.test(n)) return 3;
  if (/UpLeg|Leg|Hips/.test(n)) return 2;
  return 1;
};
for (const f of fs.readdirSync(DIR).filter((x) => x.startsWith('outfit_') && x.endsWith('.glb'))) {
  const doc = await io.read(`${DIR}/${f}`);
  const root = doc.getRoot();
  const nodes = root.listNodes().filter((n) => n.getMesh() && n.getSkin() && !PARTS.some((p) => n.getName().endsWith('_' + p)));
  if (!nodes.length) { console.log(f, 'already split'); continue; }
  for (const node of nodes) {
    const skin = node.getSkin(), jr = skin.listJoints().map((j) => region(j.getName()));
    const mesh = node.getMesh(), parent = node.listParents().find((p) => p.propertyType === 'Node');
    const pieces = PARTS.map((p) => {
      const m = doc.createMesh(`${mesh.getName()}_${p}`);
      const n = doc.createNode(`${node.getName()}_${p}`).setMesh(m).setSkin(skin)
        .setTranslation(node.getTranslation()).setRotation(node.getRotation()).setScale(node.getScale());
      (parent ?? root.listScenes()[0]).addChild(n);
      return m;
    });
    for (const prim of mesh.listPrimitives()) {
      const J = prim.getAttribute('JOINTS_0'), W = prim.getAttribute('WEIGHTS_0'), I = prim.getIndices();
      const vr = new Uint8Array(J.getCount()); const j = [], w = [];
      for (let v = 0; v < J.getCount(); v++) {
        J.getElement(v, j); W.getElement(v, w);
        let best = 0; for (let k = 1; k < 4; k++) if (w[k] > w[best]) best = k;
        vr[v] = jr[j[best]] ?? 1;
      }
      const lists = PARTS.map(() => []);
      for (let t = 0; t < I.getCount(); t += 3) {
        const a = I.getScalar(t), b = I.getScalar(t + 1), c = I.getScalar(t + 2);
        // a triangle goes with the majority of its corners (the torso when they all differ)
        const r = vr[a] === vr[b] || vr[a] === vr[c] ? vr[a] : vr[b] === vr[c] ? vr[b] : 1;
        lists[r].push(a, b, c);
      }
      lists.forEach((idx, r) => {
        if (!idx.length) return;
        const p = prim.clone();
        p.setIndices(doc.createAccessor().setType('SCALAR').setArray(new Uint32Array(idx)).setBuffer(I.getBuffer()));
        compactPrimitive(p);
        pieces[r].addPrimitive(p);
      });
    }
    node.detach(); node.dispose(); mesh.dispose();
  }
  await doc.transform(prune({ propertyTypes: ['Accessor', 'Primitive', 'Mesh'], keepLeaves: true }));
  await io.write(`${DIR}/${f}`, doc);
  console.log(f, (fs.statSync(`${DIR}/${f}`).size / 1e6).toFixed(2), 'MB');
}
