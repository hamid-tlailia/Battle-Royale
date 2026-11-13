import * as THREE from 'three';
import { mergeGeometries } from 'three/examples/jsm/utils/BufferGeometryUtils.js';
import { GLTFLoader } from 'three/examples/jsm/loaders/GLTFLoader.js';

function colored(geo: THREE.BufferGeometry, hex: number) {
  const g = geo.index ? geo.toNonIndexed() : geo;
  g.deleteAttribute('uv');
  const c = new THREE.Color(hex), n = g.getAttribute('position').count, a = new Float32Array(n * 3);
  for (let i = 0; i < n; i++) a.set([c.r, c.g, c.b], i * 3);
  g.setAttribute('color', new THREE.BufferAttribute(a, 3));
  return g;
}

/** Original low-poly twin-prop cargo plane (≈22 m), nose toward -Z. */
export class PlaneView {
  readonly root = new THREE.Group();
  private props: THREE.Mesh[] = [];

  constructor(scene: THREE.Scene) {
    const body = 0x6d7f5a, dark = 0x3b4636, glass = 0x2a3a4a, stripe = 0xd8b04a;
    const parts = [
      colored(new THREE.CylinderGeometry(1.9, 1.9, 16, 10).rotateX(Math.PI / 2), body),
      colored(new THREE.ConeGeometry(1.9, 4, 10).rotateX(-Math.PI / 2).translate(0, 0, -10), body),
      colored(new THREE.ConeGeometry(1.9, 6, 10).rotateX(Math.PI / 2).translate(0, 0.6, 11), body),
      colored(new THREE.BoxGeometry(2.4, 0.9, 1.6).translate(0, 1.2, -9.2), glass),
      colored(new THREE.BoxGeometry(24, 0.35, 3.4).translate(0, 1.0, -1.5), body),
      colored(new THREE.BoxGeometry(24.2, 0.36, 0.5).translate(0, 1.0, -3.0), stripe),
      colored(new THREE.BoxGeometry(8, 0.25, 2).translate(0, 1.2, 12.2), body),
      colored(new THREE.BoxGeometry(0.3, 3.2, 2.4).translate(0, 2.8, 12.4), body),
      colored(new THREE.CylinderGeometry(0.7, 0.8, 3.4, 8).rotateX(Math.PI / 2).translate(-5.5, 0.6, -2.4), dark),
      colored(new THREE.CylinderGeometry(0.7, 0.8, 3.4, 8).rotateX(Math.PI / 2).translate(5.5, 0.6, -2.4), dark),
      colored(new THREE.BoxGeometry(3.6, 0.15, 6).translate(0, -1.85, 2), dark), // open cargo ramp
    ];
    const mesh = new THREE.Mesh(mergeGeometries(parts), new THREE.MeshLambertMaterial({ vertexColors: true, fog: false }));
    this.root.add(mesh);
    for (const x of [-5.5, 5.5]) {
      const p = new THREE.Mesh(colored(new THREE.BoxGeometry(4.2, 0.35, 0.12), 0x222222), new THREE.MeshLambertMaterial({ vertexColors: true }));
      p.position.set(x, 0.6, -4.2);
      this.root.add(p);
      this.props.push(p);
    }
    this.root.visible = false;
    scene.add(this.root);
    // real transport plane (C130 by manilov.ap, CC BY): replaces the placeholder once loaded
    new GLTFLoader().load('/assets/plane.glb', (g) => {
      const m = g.scene;
      // the file is turned 126.9° off its axis: nose toward -Z, centred on the plane origin, ~26 m long
      const k = 0.85;
      m.scale.setScalar(k);
      m.rotation.y = -2.2143;
      m.position.set(1.462 * k, -4.065 * k, 4.963 * k);
      m.traverse((o) => { const mesh = o as THREE.Mesh; if (mesh.isMesh) { mesh.frustumCulled = false; const mat = mesh.material as THREE.MeshStandardMaterial; if (mat) mat.fog = false; } });
      mesh.visible = false; for (const p of this.props) p.visible = false;
      this.root.add(m);
    }, undefined, () => { /* keep the placeholder */ });
  }

  update(dt: number, visible: boolean, x: number, y: number, z: number, yaw: number) {
    this.root.visible = visible;
    if (!visible) return;
    this.root.position.set(x, y, z);
    this.root.rotation.set(0, yaw, Math.sin(performance.now() / 1400) * 0.03);
    for (const p of this.props) p.rotation.z += dt * 40;
  }
}
