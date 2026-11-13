import * as THREE from 'three';

/** Pooled, instanced combat effects: tracers, impact puffs, muzzle flashes (≈3 draw calls total). */
export class Effects {
  private tracers: THREE.InstancedMesh;
  private tracerLife: Float32Array;
  private tracerData: { a: THREE.Vector3; b: THREE.Vector3 }[];
  private puffs: THREE.InstancedMesh;
  private puffVel: THREE.Vector3[] = [];
  private puffPos: THREE.Vector3[] = [];
  private puffLife: Float32Array;
  private puffMax: Float32Array;
  private puffSize: Float32Array;
  private flashes: THREE.InstancedMesh;
  private flashLife: Float32Array;
  private flashPos: THREE.Vector3[] = [];
  private m = new THREE.Matrix4();
  private q = new THREE.Quaternion();
  private v = new THREE.Vector3();
  private s = new THREE.Vector3();
  private up = new THREE.Vector3(0, 0, 1);
  private nextTracer = 0;
  private nextPuff = 0;
  private nextFlash = 0;
  private static readonly T = 48;
  private static readonly P = 160;
  private static readonly F = 24;

  constructor(scene: THREE.Scene) {
    const tGeo = new THREE.BoxGeometry(0.022, 0.022, 1).translate(0, 0, 0.5);
    this.tracers = new THREE.InstancedMesh(tGeo, new THREE.MeshBasicMaterial({ color: 0xffe9a8, fog: false }), Effects.T);
    this.tracerLife = new Float32Array(Effects.T);
    this.tracerData = Array.from({ length: Effects.T }, () => ({ a: new THREE.Vector3(), b: new THREE.Vector3() }));
    const pGeo = new THREE.IcosahedronGeometry(0.5, 0);
    this.puffs = new THREE.InstancedMesh(pGeo, new THREE.MeshLambertMaterial({ color: 0xffffff }), Effects.P);
    this.puffs.instanceColor = new THREE.InstancedBufferAttribute(new Float32Array(Effects.P * 3), 3);
    this.puffLife = new Float32Array(Effects.P); this.puffMax = new Float32Array(Effects.P); this.puffSize = new Float32Array(Effects.P);
    for (let i = 0; i < Effects.P; i++) { this.puffVel.push(new THREE.Vector3()); this.puffPos.push(new THREE.Vector3()); }
    const fGeo = new THREE.OctahedronGeometry(0.16, 0);
    this.flashes = new THREE.InstancedMesh(fGeo, new THREE.MeshBasicMaterial({ color: 0xffd36b, fog: false, transparent: true, opacity: 0.95, blending: THREE.AdditiveBlending, depthWrite: false }), Effects.F);
    this.flashLife = new Float32Array(Effects.F);
    for (let i = 0; i < Effects.F; i++) this.flashPos.push(new THREE.Vector3());
    for (const im of [this.tracers, this.puffs, this.flashes]) { im.count = 0; im.frustumCulled = false; scene.add(im); }
  }

  tracer(a: THREE.Vector3, b: THREE.Vector3) {
    const i = this.nextTracer++ % Effects.T;
    this.tracerData[i].a.copy(a); this.tracerData[i].b.copy(b);
    this.tracerLife[i] = 0.07;
  }

  muzzle(p: THREE.Vector3) {
    const i = this.nextFlash++ % Effects.F;
    this.flashPos[i].copy(p);
    this.flashLife[i] = 0.05;
  }

  /** kind: 'dust' (world) | 'blood' (player) | 'armor' */
  impact(p: THREE.Vector3, kind: 'dust' | 'blood' | 'armor', n = 5) {
    const col = kind === 'blood' ? new THREE.Color(0xa01818) : kind === 'armor' ? new THREE.Color(0x6fb7ff) : new THREE.Color(0xb8ad94);
    for (let k = 0; k < n; k++) {
      const i = this.nextPuff++ % Effects.P;
      this.puffPos[i].copy(p);
      this.puffVel[i].set((Math.random() - 0.5) * (kind === 'dust' ? 1.4 : 2.4), Math.random() * (kind === 'dust' ? 1.5 : 2.2), (Math.random() - 0.5) * (kind === 'dust' ? 1.4 : 2.4));
      this.puffMax[i] = this.puffLife[i] = 0.35 + Math.random() * 0.25;
      this.puffSize[i] = kind === 'dust' ? 0.05 + Math.random() * 0.06 : 0.07 + Math.random() * 0.06; // small grains, not bricks
      this.puffs.setColorAt(i, col);
    }
    this.puffs.instanceColor!.needsUpdate = true;
  }

  update(dt: number) {
    // tracers
    let n = 0;
    for (let i = 0; i < Effects.T; i++) {
      if (this.tracerLife[i] <= 0) continue;
      this.tracerLife[i] -= dt;
      const { a, b } = this.tracerData[i];
      const len = a.distanceTo(b);
      this.v.subVectors(b, a).normalize();
      this.q.setFromUnitVectors(this.up, this.v);
      this.m.compose(a, this.q, this.s.set(1, 1, len));
      this.tracers.setMatrixAt(n++, this.m);
    }
    this.tracers.count = n;
    this.tracers.instanceMatrix.needsUpdate = true;
    // puffs
    n = 0;
    for (let i = 0; i < Effects.P; i++) {
      if (this.puffLife[i] <= 0) continue;
      this.puffLife[i] -= dt;
      this.puffVel[i].y -= 6 * dt;
      this.puffPos[i].addScaledVector(this.puffVel[i], dt);
      const k = Math.max(0, this.puffLife[i] / this.puffMax[i]);
      const sz = this.puffSize[i] * (0.4 + k);
      this.m.compose(this.puffPos[i], this.q.identity(), this.s.set(sz, sz, sz));
      this.puffs.setMatrixAt(n, this.m);
      if (n !== i) { const c = new THREE.Color(); this.puffs.getColorAt(i, c); this.puffs.setColorAt(n, c); }
      n++;
    }
    this.puffs.count = n;
    this.puffs.instanceMatrix.needsUpdate = true;
    if (this.puffs.instanceColor) this.puffs.instanceColor.needsUpdate = true;
    // flashes
    n = 0;
    for (let i = 0; i < Effects.F; i++) {
      if (this.flashLife[i] <= 0) continue;
      this.flashLife[i] -= dt;
      const sz = 0.8 + Math.random() * 0.6;
      this.m.compose(this.flashPos[i], this.q.setFromEuler(new THREE.Euler(Math.random() * 3, Math.random() * 3, 0)), this.s.set(sz, sz, sz * 1.6));
      this.flashes.setMatrixAt(n++, this.m);
    }
    this.flashes.count = n;
    this.flashes.instanceMatrix.needsUpdate = true;
  }
}
