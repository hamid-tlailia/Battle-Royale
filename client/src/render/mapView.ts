import * as THREE from 'three';
import { mergeGeometries } from 'three/examples/jsm/utils/BufferGeometryUtils.js';
import { ConvexGeometry } from 'three/examples/jsm/geometries/ConvexGeometry.js';
import { MAP, MapData, ROCK_VARIANTS, fbm, rockVariantPoints, terrainMesh } from '@lw/shared';

const mat = () => new THREE.MeshLambertMaterial({ vertexColors: true, flatShading: true });

/** Builds the whole static map in ~10 draw calls. */
export function buildMapView(map: MapData): THREE.Group {
  const g = new THREE.Group();
  g.add(buildTerrain(map));
  g.add(buildBoxes(map));
  g.add(...buildTrees(map));
  g.add(...buildRocks(map));
  g.add(buildSky());
  // Far ground so the horizon never shows a void
  const far = new THREE.Mesh(new THREE.PlaneGeometry(3000, 3000).rotateX(-Math.PI / 2), new THREE.MeshBasicMaterial({ color: 0x4d6b47 }));
  far.position.y = -2;
  g.add(far);
  g.traverse((o) => { o.matrixAutoUpdate = false; o.updateMatrix(); });
  return g;
}

function buildTerrain(map: MapData): THREE.Mesh {
  const { vertices, indices } = terrainMesh(map);
  const triCount = indices.length / 3;
  const pos = new Float32Array(triCount * 9);
  const col = new Float32Array(triCount * 9);
  const a = new THREE.Vector3(), b = new THREE.Vector3(), c = new THREE.Vector3(), n = new THREE.Vector3(), t = new THREE.Vector3();
  const grassA = new THREE.Color(0x5f9a3c), grassB = new THREE.Color(0x7fb04a), grassC = new THREE.Color(0x4a7d33);
  const rock = new THREE.Color(0x7c7a72), rockDark = new THREE.Color(0x5e5c57), snow = new THREE.Color(0xf1f4f6);
  const color = new THREE.Color();
  for (let i = 0; i < triCount; i++) {
    const ia = indices[i * 3], ib = indices[i * 3 + 1], ic = indices[i * 3 + 2];
    a.fromArray(vertices, ia * 3); b.fromArray(vertices, ib * 3); c.fromArray(vertices, ic * 3);
    pos.set([a.x, a.y, a.z, b.x, b.y, b.z, c.x, c.y, c.z], i * 9);
    n.subVectors(c, b).cross(t.subVectors(a, b)).normalize();
    const h = (a.y + b.y + c.y) / 3, cx = (a.x + b.x + c.x) / 3, cz = (a.z + b.z + c.z) / 3;
    const ny = Math.abs(n.y);
    if (h > MAP.snowHeight + (fbm(cx * 0.05, cz * 0.05, 3) - 0.5) * 8 && ny > 0.55) color.copy(snow);
    else if (ny < 0.8) color.copy(rock).lerp(rockDark, fbm(cx * 0.2, cz * 0.2, 5));
    else {
      const v = fbm(cx * 0.03, cz * 0.03, 11);
      color.copy(grassA).lerp(v > 0.5 ? grassB : grassC, Math.abs(v - 0.5) * 2.2);
    }
    const jitter = 0.94 + ((i * 2654435761) % 1000) / 1000 * 0.1;
    color.multiplyScalar(jitter);
    for (let k = 0; k < 3; k++) col.set([color.r, color.g, color.b], i * 9 + k * 3);
  }
  const geo = new THREE.BufferGeometry();
  geo.setAttribute('position', new THREE.BufferAttribute(pos, 3));
  geo.setAttribute('color', new THREE.BufferAttribute(col, 3));
  geo.computeVertexNormals();
  geo.computeBoundingSphere();
  return new THREE.Mesh(geo, mat());
}

function colorize(geo: THREE.BufferGeometry, hex: number) {
  const c = new THREE.Color(hex);
  const n = geo.getAttribute('position').count;
  const arr = new Float32Array(n * 3);
  for (let i = 0; i < n; i++) arr.set([c.r, c.g, c.b], i * 3);
  geo.setAttribute('color', new THREE.BufferAttribute(arr, 3));
  return geo;
}

function buildBoxes(map: MapData): THREE.Mesh {
  const m = new THREE.Matrix4(), q = new THREE.Quaternion(), up = new THREE.Vector3(0, 1, 0);
  const parts = map.boxes.map((b) => {
    const geo = new THREE.BoxGeometry(b.hx * 2, b.hy * 2, b.hz * 2);
    m.compose(new THREE.Vector3(b.x, b.y, b.z), q.setFromAxisAngle(up, b.rot), new THREE.Vector3(1, 1, 1));
    geo.applyMatrix4(m);
    return colorize(geo, b.color);
  });
  return new THREE.Mesh(mergeGeometries(parts), mat());
}

function buildTrees(map: MapData): THREE.InstancedMesh[] {
  const trunkGeo = colorize(new THREE.CylinderGeometry(0.2, 0.3, 3.2, 5).translate(0, 1.6, 0), 0x6b4a2f);
  const leaves = mergeGeometries([
    new THREE.ConeGeometry(1.9, 3.2, 6).translate(0, 3.6, 0).toNonIndexed(),
    new THREE.ConeGeometry(1.45, 2.6, 6).translate(0, 5.0, 0).toNonIndexed(),
    new THREE.ConeGeometry(0.95, 2.0, 6).translate(0, 6.2, 0).toNonIndexed(),
  ]);
  colorize(leaves, 0xffffff);
  const trunk = new THREE.InstancedMesh(trunkGeo, mat(), map.trees.length);
  const leaf = new THREE.InstancedMesh(leaves, mat(), map.trees.length);
  const m = new THREE.Matrix4(), q = new THREE.Quaternion(), up = new THREE.Vector3(0, 1, 0), s = new THREE.Vector3(), p = new THREE.Vector3();
  const greens = [0x2f6b2f, 0x3a7a35, 0x285c2a, 0x46843a].map((h) => new THREE.Color(h));
  map.trees.forEach((t, i) => {
    m.compose(p.set(t.x, t.y, t.z), q.setFromAxisAngle(up, t.rot), s.set(t.s, t.s, t.s));
    trunk.setMatrixAt(i, m); leaf.setMatrixAt(i, m);
    leaf.setColorAt(i, greens[i % greens.length]);
  });
  trunk.computeBoundingSphere(); leaf.computeBoundingSphere();
  return [trunk, leaf];
}

function buildRocks(map: MapData): THREE.InstancedMesh[] {
  const out: THREE.InstancedMesh[] = [];
  const m = new THREE.Matrix4(), q = new THREE.Quaternion(), up = new THREE.Vector3(0, 1, 0), s = new THREE.Vector3(), p = new THREE.Vector3();
  for (let v = 0; v < ROCK_VARIANTS; v++) {
    const pts = rockVariantPoints(v);
    const vecs: THREE.Vector3[] = [];
    for (let i = 0; i < pts.length; i += 3) vecs.push(new THREE.Vector3(pts[i], pts[i + 1], pts[i + 2]));
    const geo = colorize(new ConvexGeometry(vecs), 0x8a877e);
    const list = map.rocks.filter((r) => r.variant === v);
    const im = new THREE.InstancedMesh(geo, mat(), Math.max(1, list.length));
    im.count = list.length;
    list.forEach((r, i) => {
      m.compose(p.set(r.x, r.y, r.z), q.setFromAxisAngle(up, r.rot), s.set(r.s, r.s, r.s));
      im.setMatrixAt(i, m);
    });
    im.computeBoundingSphere();
    out.push(im);
  }
  return out;
}

function buildSky(): THREE.Mesh {
  const geo = new THREE.SphereGeometry(900, 16, 10);
  const top = new THREE.Color(0x5fa3e0), bottom = new THREE.Color(0xcfe6f2);
  const pos = geo.getAttribute('position');
  const cols = new Float32Array(pos.count * 3);
  const c = new THREE.Color();
  for (let i = 0; i < pos.count; i++) {
    const t = Math.max(0, pos.getY(i) / 900);
    c.copy(bottom).lerp(top, Math.pow(t, 0.6));
    cols.set([c.r, c.g, c.b], i * 3);
  }
  geo.setAttribute('color', new THREE.BufferAttribute(cols, 3));
  const sky = new THREE.Mesh(geo, new THREE.MeshBasicMaterial({ vertexColors: true, side: THREE.BackSide, fog: false, depthWrite: false }));
  sky.renderOrder = -1;
  return sky;
}
