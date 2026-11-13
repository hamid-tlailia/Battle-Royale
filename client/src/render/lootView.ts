import * as THREE from 'three';
import { mergeGeometries } from 'three/examples/jsm/utils/BufferGeometryUtils.js';
import { LOOT, WEAPON_IDS, type FrostWall, type LootItem } from '@lw/shared';
import { toFloatGeometry, type GameAssets } from './assets';

const MAX = 400;
const RING_COL: Record<string, number> = { weapon: 0xffd34d, ammo: 0xdddddd, med: 0xff6b6b, armor: 0x5aaaff, scope: 0xc58bff, frost: 0x8fe8ff };

function colored(geo: THREE.BufferGeometry, hex: number) {
  const g = geo.index ? geo.toNonIndexed() : geo;
  g.deleteAttribute('uv');
  const c = new THREE.Color(hex), n = g.getAttribute('position').count, a = new Float32Array(n * 3);
  for (let i = 0; i < n; i++) a.set([c.r, c.g, c.b], i * 3);
  g.setAttribute('color', new THREE.BufferAttribute(a, 3));
  return g;
}

/** Ground loot (instanced per item type, bobbing + spinning) and ice walls. */
export class LootView {
  private group = new THREE.Group();
  private meshes = new Map<string, THREE.InstancedMesh>();
  private ring: THREE.InstancedMesh;
  private items: LootItem[] = [];
  private walls = new Map<number, THREE.Mesh>();
  private wallMat = new THREE.MeshLambertMaterial({ color: 0xbfeaff, emissive: 0x2a6f99, transparent: true, opacity: 0.82 });
  private wallGeo: THREE.BufferGeometry;
  private m = new THREE.Matrix4();
  private q = new THREE.Quaternion();
  private e = new THREE.Euler();
  private s = new THREE.Vector3();
  private p = new THREE.Vector3();
  private c = new THREE.Color();
  private time = 0;
  dirty = true;

  constructor(scene: THREE.Scene, assets: GameAssets) {
    scene.add(this.group);
    const vc = new THREE.MeshLambertMaterial({ vertexColors: true });
    // weapons: the real gun models
    for (const w of WEAPON_IDS) {
      let geo: THREE.BufferGeometry | null = null, mat: THREE.Material = vc;
      assets.guns[w].updateMatrixWorld(true);
      assets.guns[w].traverse((o) => {
        const mm = o as THREE.Mesh;
        if (mm.isMesh && !geo) { geo = toFloatGeometry(mm.geometry).applyMatrix4(mm.matrixWorld); mat = mm.material as THREE.Material; }
      });
      // lie flat on the ground, a bit bigger so it reads from afar
      this.add(`weapon:${w}`, (geo as unknown as THREE.BufferGeometry).rotateZ(Math.PI / 2).rotateX(Math.PI / 2).scale(1.3, 1.3, 1.3), mat);
    }
    // real models for ammo crates, medkits and vests (fallback: simple shapes)
    const real = (kind: string, prefab: string, rotX = 0, fallback: () => THREE.BufferGeometry) => {
      const pf = (assets.prefabs as Record<string, { parts: { geometry: THREE.BufferGeometry; material: THREE.Material }[] } | undefined>)[prefab];
      if (!pf?.parts.length) { this.add(kind, fallback(), vc); return; }
      pf.parts.forEach((part, i) => this.add(i ? `${kind}#${i}` : kind, part.geometry.clone().rotateX(rotX), part.material));
    };
    real('ammo', 'item_ammo', 0, () => colored(new THREE.BoxGeometry(0.34, 0.2, 0.22), 0xffffff));
    real('med', 'item_med', 0, () => mergeGeometries([
      colored(new THREE.BoxGeometry(0.42, 0.18, 0.3), 0xf4f4f4),
      colored(new THREE.BoxGeometry(0.26, 0.02, 0.07).translate(0, 0.1, 0), 0xd62828),
      colored(new THREE.BoxGeometry(0.07, 0.02, 0.22).translate(0, 0.1, 0), 0xd62828),
    ]));
    real('armor', 'item_vest', -Math.PI / 2, () => mergeGeometries([
      colored(new THREE.BoxGeometry(0.46, 0.5, 0.16), 0x3f6fae),
      colored(new THREE.BoxGeometry(0.5, 0.12, 0.18).translate(0, -0.1, 0), 0x2b4f80),
    ]));
    this.add('scope', mergeGeometries([
      colored(new THREE.CylinderGeometry(0.06, 0.06, 0.34, 10).rotateZ(Math.PI / 2), 0x222428),
      colored(new THREE.CylinderGeometry(0.075, 0.075, 0.06, 10).rotateZ(Math.PI / 2).translate(0.17, 0, 0), 0x111111),
    ]), vc);
    this.add('frost', colored(new THREE.OctahedronGeometry(0.2, 0).scale(1, 1.4, 1), 0x9ff0ff), new THREE.MeshLambertMaterial({ vertexColors: true, emissive: 0x2a7fa0 }));
    this.ring = new THREE.InstancedMesh(new THREE.RingGeometry(0.32, 0.4, 20).rotateX(-Math.PI / 2),
      new THREE.MeshBasicMaterial({ color: 0xffffff, transparent: true, opacity: 0.75, depthWrite: false, side: THREE.DoubleSide }), MAX);
    this.ring.instanceColor = new THREE.InstancedBufferAttribute(new Float32Array(MAX * 3), 3);
    this.ring.frustumCulled = false;
    this.group.add(this.ring);
    // ice wall: a slightly irregular slab of blocks
    const parts: THREE.BufferGeometry[] = [];
    const W = LOOT.frostWidth, H = LOOT.frostHeight, T = LOOT.frostThick;
    for (let i = 0; i < 4; i++) for (let j = 0; j < 3; j++) {
      const bw = W / 4, bh = H / 3;
      parts.push(new THREE.BoxGeometry(bw * 0.98, bh * 0.98, T * (0.9 + ((i * 7 + j * 3) % 5) * 0.05))
        .translate(-W / 2 + bw * (i + 0.5), -H / 2 + bh * (j + 0.5), 0));
    }
    this.wallGeo = mergeGeometries(parts);
  }

  private add(key: string, geo: THREE.BufferGeometry, mat: THREE.Material) {
    const im = new THREE.InstancedMesh(geo, mat, MAX);
    im.count = 0;
    im.frustumCulled = false;
    this.meshes.set(key, im);
    this.group.add(im);
  }

  setItems(loot: Iterable<LootItem>) { this.items = [...loot]; this.dirty = false; }

  syncWalls(walls: Map<number, FrostWall>) {
    for (const [id, mesh] of this.walls) if (!walls.has(id)) { mesh.removeFromParent(); this.walls.delete(id); }
    for (const w of walls.values()) {
      if (this.walls.has(w.id)) continue;
      const mesh = new THREE.Mesh(this.wallGeo, this.wallMat);
      mesh.position.set(w.x, w.y, w.z);
      mesh.rotation.y = w.rot;
      mesh.scale.set(1, 0.05, 1);
      this.group.add(mesh);
      this.walls.set(w.id, mesh);
    }
  }

  update(dt: number, camera: THREE.Camera) {
    this.time += dt;
    // walls grow out of the ground
    for (const mesh of this.walls.values()) mesh.scale.y = Math.min(1, mesh.scale.y + dt * 6);
    const counts = new Map<string, number>();
    for (const im of this.meshes.values()) im.count = 0;
    let rings = 0;
    const cx = camera.position.x, cz = camera.position.z;
    for (const it of this.items) {
      const d2 = (it.x - cx) ** 2 + (it.z - cz) ** 2;
      if (d2 > 90 * 90) continue;
      const key = it.kind === 'weapon' ? `weapon:${it.item}` : it.kind;
      const im = this.meshes.get(key);
      if (!im) continue;
      const n = counts.get(key) ?? 0;
      if (n >= MAX) continue;
      // resting on the ground like real objects (scopes / ice-wall charges still hover so they read)
      const small = it.kind === 'scope' || it.kind === 'frost';
      const lift = small ? 0.28 + Math.sin(this.time * 2 + it.id) * 0.06 : it.kind === 'weapon' ? 0.06 : 0.01;
      this.e.set(0, small ? this.time * 1.2 + it.id : it.id * 2.399, 0);
      this.m.compose(this.p.set(it.x, it.y + lift, it.z), this.q.setFromEuler(this.e), this.s.set(1, 1, 1));
      for (let k = 0; ; k++) {
        const part = k ? this.meshes.get(`${key}#${k}`) : im;
        if (!part) break;
        part.setMatrixAt(n, this.m);
        if (k) counts.set(`${key}#${k}`, n + 1);
      }
      counts.set(key, n + 1);
      if (rings < MAX) {
        this.m.compose(this.p.set(it.x, it.y + 0.03, it.z), this.q.identity(), this.s.set(1, 1, 1));
        this.ring.setMatrixAt(rings, this.m);
        this.ring.setColorAt(rings++, this.c.set(RING_COL[it.kind]));
      }
    }
    for (const [key, im] of this.meshes) {
      im.count = counts.get(key) ?? 0;
      im.instanceMatrix.needsUpdate = true;
      if (im.instanceColor) im.instanceColor.needsUpdate = true;
    }
    this.ring.count = rings;
    this.ring.instanceMatrix.needsUpdate = true;
    if (this.ring.instanceColor) this.ring.instanceColor.needsUpdate = true;
  }
}
