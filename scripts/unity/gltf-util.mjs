// Shared glTF helpers for the asset importers (scripts/unity/import-*.mjs).
export const mat4mul = (a, b) => { const o = new Array(16).fill(0); for (let c = 0; c < 4; c++) for (let r = 0; r < 4; r++) for (let k = 0; k < 4; k++) o[c * 4 + r] += a[k * 4 + r] * b[c * 4 + k]; return o; };
export const apply = (m, v) => [0, 1, 2].map((r) => m[r] * v[0] + m[4 + r] * v[1] + m[8 + r] * v[2] + m[12 + r]);
// rigged guns (for first-person animations): bake the rest pose into plain meshes in scene space, drop the rig
export function bakeSkins(doc, scene) {
  const inv = (m) => { // 4x4 inverse (column-major)
    const a = m, o = new Array(16);
    o[0] = a[5]*a[10]*a[15]-a[5]*a[11]*a[14]-a[9]*a[6]*a[15]+a[9]*a[7]*a[14]+a[13]*a[6]*a[11]-a[13]*a[7]*a[10];
    o[4] = -a[4]*a[10]*a[15]+a[4]*a[11]*a[14]+a[8]*a[6]*a[15]-a[8]*a[7]*a[14]-a[12]*a[6]*a[11]+a[12]*a[7]*a[10];
    o[8] = a[4]*a[9]*a[15]-a[4]*a[11]*a[13]-a[8]*a[5]*a[15]+a[8]*a[7]*a[13]+a[12]*a[5]*a[11]-a[12]*a[7]*a[9];
    o[12] = -a[4]*a[9]*a[14]+a[4]*a[10]*a[13]+a[8]*a[5]*a[14]-a[8]*a[6]*a[13]-a[12]*a[5]*a[10]+a[12]*a[6]*a[9];
    o[1] = -a[1]*a[10]*a[15]+a[1]*a[11]*a[14]+a[9]*a[2]*a[15]-a[9]*a[3]*a[14]-a[13]*a[2]*a[11]+a[13]*a[3]*a[10];
    o[5] = a[0]*a[10]*a[15]-a[0]*a[11]*a[14]-a[8]*a[2]*a[15]+a[8]*a[3]*a[14]+a[12]*a[2]*a[11]-a[12]*a[3]*a[10];
    o[9] = -a[0]*a[9]*a[15]+a[0]*a[11]*a[13]+a[8]*a[1]*a[15]-a[8]*a[3]*a[13]-a[12]*a[1]*a[11]+a[12]*a[3]*a[9];
    o[13] = a[0]*a[9]*a[14]-a[0]*a[10]*a[13]-a[8]*a[1]*a[14]+a[8]*a[2]*a[13]+a[12]*a[1]*a[10]-a[12]*a[2]*a[9];
    o[2] = a[1]*a[6]*a[15]-a[1]*a[7]*a[14]-a[5]*a[2]*a[15]+a[5]*a[3]*a[14]+a[13]*a[2]*a[7]-a[13]*a[3]*a[6];
    o[6] = -a[0]*a[6]*a[15]+a[0]*a[7]*a[14]+a[4]*a[2]*a[15]-a[4]*a[3]*a[14]-a[12]*a[2]*a[7]+a[12]*a[3]*a[6];
    o[10] = a[0]*a[5]*a[15]-a[0]*a[7]*a[13]-a[4]*a[1]*a[15]+a[4]*a[3]*a[13]+a[12]*a[1]*a[7]-a[12]*a[3]*a[5];
    o[14] = -a[0]*a[5]*a[14]+a[0]*a[6]*a[13]+a[4]*a[1]*a[14]-a[4]*a[2]*a[13]-a[12]*a[1]*a[6]+a[12]*a[2]*a[5];
    o[3] = -a[1]*a[6]*a[11]+a[1]*a[7]*a[10]+a[5]*a[2]*a[11]-a[5]*a[3]*a[10]-a[9]*a[2]*a[7]+a[9]*a[3]*a[6];
    o[7] = a[0]*a[6]*a[11]-a[0]*a[7]*a[10]-a[4]*a[2]*a[11]+a[4]*a[3]*a[10]+a[8]*a[2]*a[7]-a[8]*a[3]*a[6];
    o[11] = -a[0]*a[5]*a[11]+a[0]*a[7]*a[9]+a[4]*a[1]*a[11]-a[4]*a[3]*a[9]-a[8]*a[1]*a[7]+a[8]*a[3]*a[5];
    o[15] = a[0]*a[5]*a[10]-a[0]*a[6]*a[9]-a[4]*a[1]*a[10]+a[4]*a[2]*a[9]+a[8]*a[1]*a[6]-a[8]*a[2]*a[5];
    const det = a[0]*o[0]+a[1]*o[4]+a[2]*o[8]+a[3]*o[12]; return o.map((x) => x / det);
  };
  const baked = [];
  scene.traverse((n) => {
    const skin = n.getSkin(), mesh = n.getMesh(); if (!skin || !mesh) return;
    const joints = skin.listJoints(), ibm = skin.getInverseBindMatrices();
    const jm = joints.map((j, i) => mat4mul(j.getWorldMatrix(), ibm ? ibm.getElement(i, []) : n.getWorldMatrix()));
    for (const p of mesh.listPrimitives()) {
      const P = p.getAttribute('POSITION'), N = p.getAttribute('NORMAL'), J = p.getAttribute('JOINTS_0'), W = p.getAttribute('WEIGHTS_0');
      if (!J || !W) continue;
      const pos = new Float32Array(P.getCount() * 3), nor = N ? new Float32Array(N.getCount() * 3) : null; const j = [], w = [], v = [], nn = [];
      for (let i = 0; i < P.getCount(); i++) {
        J.getElement(i, j); W.getElement(i, w); P.getElement(i, v); if (N) N.getElement(i, nn);
        const o = [0, 0, 0], on = [0, 0, 0];
        for (let k = 0; k < 4; k++) { if (!w[k]) continue; const m = jm[j[k]]; const q = apply(m, v); for (let c = 0; c < 3; c++) o[c] += q[c] * w[k];
          if (N) { const r = [0, 1, 2].map((rr) => m[rr] * nn[0] + m[4 + rr] * nn[1] + m[8 + rr] * nn[2]); for (let c = 0; c < 3; c++) on[c] += r[c] * w[k]; } }
        pos.set(o, i * 3); if (nor) { const l = Math.hypot(...on) || 1; nor.set(on.map((x) => x / l), i * 3); }
      }
      P.setArray(pos); if (N) N.setArray(nor);
      p.setAttribute('JOINTS_0', null); p.setAttribute('WEIGHTS_0', null);
    }
    baked.push(n);
  });
  // the baked meshes are in scene space now: hang them straight under the scene, the rig goes
  for (const n of baked) {
    const m = n.getMesh(); const fresh = doc.createNode(n.getName() + '_baked').setMesh(m);
    n.setMesh(null); n.setSkin(null); scene.addChild(fresh);
  }
  for (const s of doc.getRoot().listSkins()) s.dispose();
}


/** every vertex (every 3rd) of the scene in scene space */
export function scenePoints(scene) {
  const pts = [];
  scene.traverse((n) => { const m = n.getMesh(); if (!m) return; const wm = n.getWorldMatrix();
    for (const p of m.listPrimitives()) { const a = p.getAttribute('POSITION'); const e = []; for (let i = 0; i < a.getCount(); i += 3) pts.push(apply(wm, a.getElement(i, e))); } });
  return pts;
}
