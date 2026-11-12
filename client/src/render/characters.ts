import * as THREE from 'three';
import { mergeGeometries } from 'three/examples/jsm/utils/BufferGeometryUtils.js';
import { PLAYER } from '@lw/shared';

const SHIRTS = [0xd94f3d, 0x3d7bd9, 0xe0b23a, 0x8a4fd9, 0x3dbf8a, 0xe07a3a, 0xd93d8a, 0x3dc4d9, 0xf0f0f0, 0x6b8e23];
const SKIN = [0xf1c27d, 0xe0ac69, 0xc68642, 0x8d5524];

function colored(geo: THREE.BufferGeometry, hex: number) {
  const g = geo.index ? geo.toNonIndexed() : geo;
  const c = new THREE.Color(hex);
  const n = g.getAttribute('position').count;
  const arr = new Float32Array(n * 3);
  for (let i = 0; i < n; i++) arr.set([c.r, c.g, c.b], i * 3);
  g.setAttribute('color', new THREE.BufferAttribute(arr, 3));
  g.deleteAttribute('uv');
  return g;
}

const sharedMat = new THREE.MeshLambertMaterial({ vertexColors: true, flatShading: true });
const legGeo = colored(new THREE.BoxGeometry(0.2, 0.75, 0.24).translate(0, -0.375, 0), 0x3b3f4a);

/** Low-poly soldier. Origin at the FEET; faces -Z. */
export class CharacterView {
  readonly root = new THREE.Group();
  private upper = new THREE.Group();
  private legL: THREE.Mesh;
  private legR: THREE.Mesh;
  private walkPhase = 0;
  private crouchAmt = 0;
  static readonly HIP = 0.75;

  constructor(index: number) {
    const shirt = SHIRTS[index % SHIRTS.length];
    const skin = SKIN[index % SKIN.length];
    const parts = [
      colored(new THREE.BoxGeometry(0.54, 0.56, 0.3).translate(0, 0.29, 0), shirt), // torso
      colored(new THREE.BoxGeometry(0.3, 0.3, 0.3).translate(0, 0.75, 0), skin), // head
      colored(new THREE.BoxGeometry(0.36, 0.1, 0.36).translate(0, 0.93, 0.02), 0x3d4a2f), // helmet
      colored(new THREE.BoxGeometry(0.15, 0.52, 0.17).translate(-0.35, 0.3, -0.04), shirt), // arms
      colored(new THREE.BoxGeometry(0.15, 0.52, 0.17).translate(0.35, 0.3, -0.04), shirt),
      colored(new THREE.BoxGeometry(0.38, 0.4, 0.16).translate(0, 0.32, 0.22), 0x5a4a32), // backpack
      colored(new THREE.BoxGeometry(0.22, 0.07, 0.04).translate(0, 0.78, -0.16), 0x111111), // visor
    ];
    const body = new THREE.Mesh(mergeGeometries(parts), sharedMat);
    this.upper.add(body);
    this.legL = new THREE.Mesh(legGeo, sharedMat);
    this.legR = new THREE.Mesh(legGeo, sharedMat);
    this.root.add(this.upper, this.legL, this.legR);
    const shadow = new THREE.Mesh(
      new THREE.CircleGeometry(0.45, 10).rotateX(-Math.PI / 2),
      new THREE.MeshBasicMaterial({ color: 0x000000, transparent: true, opacity: 0.25, depthWrite: false }),
    );
    shadow.position.y = 0.03;
    this.root.add(shadow);
  }

  /** (x,y,z) = capsule centre. */
  update(x: number, y: number, z: number, yaw: number, speed: number, crouch: boolean, grounded: boolean, dt: number) {
    const halfTotal = (crouch ? PLAYER.crouchHalfHeight : PLAYER.halfHeight) + PLAYER.radius;
    this.root.position.set(x, y - halfTotal, z);
    this.root.rotation.y = yaw;
    this.crouchAmt += ((crouch ? 1 : 0) - this.crouchAmt) * Math.min(1, dt * 14);
    const legScale = 1 - this.crouchAmt * 0.45;
    const hip = CharacterView.HIP * legScale;
    this.legL.position.set(-0.13, hip, 0);
    this.legR.position.set(0.13, hip, 0);
    this.legL.scale.y = this.legR.scale.y = legScale;
    this.upper.position.y = hip;
    this.upper.scale.setScalar(1 - this.crouchAmt * 0.12);
    this.upper.rotation.x = -this.crouchAmt * 0.25;
    if (grounded && speed > 0.5) this.walkPhase += dt * speed * 2.4;
    else this.walkPhase *= 0.85;
    const swing = Math.sin(this.walkPhase) * 0.6 * Math.min(1, speed / 4);
    this.legL.rotation.x = swing;
    this.legR.rotation.x = -swing;
  }

  dispose() {
    this.root.removeFromParent();
  }
}
