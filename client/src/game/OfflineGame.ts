import * as THREE from 'three';
import {
  BotBrain, DT, MAP, PhysicsWorld, generateMap, newMoveState, simulateMove, eyeOffset,
  type InputCmd, type MapData, type MoveState,
} from '@lw/shared';
import { buildMapView } from '../render/mapView';
import { CharacterView } from '../render/characters';
import type { GameAssets } from '../render/assets';
import { AdaptiveResolution } from '../render/adaptive';
import { Controls } from '../input/controls';
import { DebugOverlay } from '../ui/debug';

interface Actor {
  id: number;
  state: MoveState;
  prev: { x: number; y: number; z: number; yaw: number };
  view: CharacterView;
  brain?: BotBrain;
  speed: number;
}

const BOT_COUNT = 15;

/** M1: fully offline sandbox — same shared simulation the server will run. */
export class OfflineGame {
  private renderer: THREE.WebGLRenderer;
  private scene = new THREE.Scene();
  private camera: THREE.PerspectiveCamera;
  private map: MapData;
  private phys: PhysicsWorld;
  private actors: Actor[] = [];
  private me!: Actor;
  private controls: Controls;
  private debug: DebugOverlay;
  private acc = 0;
  private last = performance.now();
  private tick = 0;
  private seq = 0;
  private camDist = 3.4;
  private running = true;
  private adaptive: AdaptiveResolution;
  private sky: THREE.Object3D | null = null;
  private frustum = new THREE.Frustum();
  private projScreen = new THREE.Matrix4();
  private tmpSphere = new THREE.Sphere();
  private cpuMs = 0;
  private shadows!: THREE.InstancedMesh;
  private shadowMat = new THREE.Matrix4();

  constructor(container: HTMLElement, private hud: HTMLElement, assets: GameAssets) {
    this.renderer = new THREE.WebGLRenderer({ antialias: true, powerPreference: 'high-performance', stencil: false });
    this.renderer.setSize(window.innerWidth, window.innerHeight);
    this.renderer.shadowMap.enabled = false;
    this.renderer.setClearColor(0xd4e8f2);
    container.appendChild(this.renderer.domElement);
    this.adaptive = new AdaptiveResolution(this.renderer);

    // Short far plane + fog: distant geometry is never rasterised.
    this.camera = new THREE.PerspectiveCamera(70, window.innerWidth / window.innerHeight, 0.15, 300);
    this.scene.fog = new THREE.Fog(0xd4e8f2, 70, 240);
    this.scene.add(new THREE.HemisphereLight(0xe6f4ff, 0x4f6b3a, 1.7));
    const sun = new THREE.DirectionalLight(0xfff0d0, 1.9);
    sun.position.set(80, 140, 50);
    this.scene.add(sun);

    this.map = generateMap(MAP.seed);
    this.phys = new PhysicsWorld(this.map);
    const mapView = buildMapView(this.map, assets);
    this.sky = mapView.children.find((o) => (o as THREE.Mesh).material && ((o as THREE.Mesh).material as THREE.Material).side === THREE.BackSide) ?? null;
    if (this.sky) this.sky.matrixAutoUpdate = true;
    this.scene.add(mapView);

    this.controls = new Controls(this.renderer.domElement, hud);
    this.debug = new DebugOverlay(hud);
    this.buildButtons();

    // Spawn player + bots
    for (let i = 0; i <= BOT_COUNT; i++) {
      const sp = this.map.spawns[i % this.map.spawns.length];
      const p = this.phys.findSpawn(sp.x, sp.z);
      this.phys.addPlayer(i, p.x, p.y, p.z);
      const actor: Actor = {
        id: i, state: newMoveState(p.x, p.y, p.z), prev: { x: p.x, y: p.y, z: p.z, yaw: 0 },
        view: new CharacterView(assets, i), speed: 0,
        brain: i === 0 ? undefined : new BotBrain(1000 + i),
      };
      this.scene.add(actor.view.root);
      this.actors.push(actor);
    }
    this.me = this.actors[0];
    // All blob shadows in one draw call
    this.shadows = new THREE.InstancedMesh(
      new THREE.CircleGeometry(0.45, 12).rotateX(-Math.PI / 2),
      new THREE.MeshBasicMaterial({ color: 0x000000, transparent: true, opacity: 0.28, depthWrite: false }),
      this.actors.length,
    );
    this.shadows.frustumCulled = false;
    this.shadows.renderOrder = 1;
    this.scene.add(this.shadows);
    this.phys.updateQueries();
    // Face toward the map centre initially
    this.controls.yaw = Math.atan2(this.me.state.x, this.me.state.z);

    (window as unknown as { __game: unknown }).__game = this; // debug/testing hook
    window.addEventListener('resize', this.onResize);
    requestAnimationFrame(this.frame);
  }

  private buildButtons() {
    const mk = (cls: string, label: string) => {
      const b = document.createElement('div');
      b.className = `btn ${cls}`;
      b.textContent = label;
      this.hud.appendChild(b);
      return b;
    };
    const jump = mk('btn-jump', 'JUMP');
    const crouch = mk('btn-crouch', 'CROUCH');
    const scope = mk('btn-scope', 'SCOPE');
    this.controls.bindButton(jump, () => this.controls.jump());
    this.controls.bindButton(crouch, () => {
      this.controls.toggleCrouch();
      crouch.classList.toggle('active', this.controls.crouchToggle);
    });
    this.controls.bindButton(scope, () => {
      this.controls.setScope(!this.controls.scoped);
      scope.classList.toggle('active', this.controls.scoped);
    });
    const cross = document.createElement('div');
    cross.className = 'crosshair';
    this.hud.appendChild(cross);
    if (!this.controls.isTouch) {
      const help = document.createElement('div');
      help.className = 'help';
      help.textContent = 'Click to look · WASD move · Space jump · C crouch · Right-click scope · ` debug';
      this.hud.appendChild(help);
    }
  }

  private onResize = () => {
    this.camera.aspect = window.innerWidth / window.innerHeight;
    this.camera.updateProjectionMatrix();
    this.renderer.setSize(window.innerWidth, window.innerHeight);
  };

  private stepSim() {
    const { mx, mz } = this.controls.axes();
    const myInput: InputCmd = {
      seq: ++this.seq, mx, mz, yaw: this.controls.yaw, pitch: this.controls.pitch, buttons: this.controls.sampleButtons(),
    };
    for (const a of this.actors) {
      a.prev.x = a.state.x; a.prev.y = a.state.y; a.prev.z = a.state.z; a.prev.yaw = a.state.yaw;
      const input = a.brain ? a.brain.think(a.state, DT) : myInput;
      simulateMove(this.phys, a.id, a.state, input, DT);
      a.speed = Math.hypot(a.state.x - a.prev.x, a.state.z - a.prev.z) / DT;
    }
    this.phys.updateQueries();
    this.tick++;
  }

  private frame = (now: number) => {
    if (!this.running) return;
    requestAnimationFrame(this.frame);
    const frameMs = now - this.last;
    const dt = Math.min(0.1, frameMs / 1000);
    this.last = now;
    this.acc += dt;
    let steps = 0;
    const t0 = performance.now();
    while (this.acc >= DT && steps < 5) { this.stepSim(); this.acc -= DT; steps++; }
    if (steps) this.simCost = this.simCost * 0.9 + ((performance.now() - t0) / steps) * 0.1;
    if (steps === 5) this.acc = 0;
    const alpha = this.acc / DT;

    // Camera first (uses my interpolated position), then cull + animate characters
    const me = this.me;
    this.myPos.set(
      me.prev.x + (me.state.x - me.prev.x) * alpha,
      me.prev.y + (me.state.y - me.prev.y) * alpha,
      me.prev.z + (me.state.z - me.prev.z) * alpha,
    );
    this.updateCamera(dt);
    this.camera.updateMatrixWorld();
    this.projScreen.multiplyMatrices(this.camera.projectionMatrix, this.camera.matrixWorldInverse);
    this.frustum.setFromProjectionMatrix(this.projScreen);
    let visibleChars = 0;
    for (const a of this.actors) {
      const x = a.prev.x + (a.state.x - a.prev.x) * alpha;
      const y = a.prev.y + (a.state.y - a.prev.y) * alpha;
      const z = a.prev.z + (a.state.z - a.prev.z) * alpha;
      const yaw = a === me ? this.controls.yaw : lerpAngle(a.prev.yaw, a.state.yaw, alpha);
      const dist = this.camera.position.distanceTo(this.tmpVec.set(x, y, z));
      this.tmpSphere.center.set(x, y, z); this.tmpSphere.radius = 1.3;
      const visible = dist < 240 && this.frustum.intersectsSphere(this.tmpSphere);
      if (visible) visibleChars++;
      a.view.update({ x, y, z, yaw, pitch: a === me ? this.controls.pitch : a.state.pitch, speed: a.speed, crouch: a.state.crouch, grounded: a.state.grounded }, dt, dist, visible);
      const gy = a.state.grounded ? a.view.root.position.y : this.phys.groundHeight(x, z);
      this.shadowMat.makeTranslation(x, gy + 0.05, z);
      this.shadows.setMatrixAt(a.id, this.shadowMat);
    }
    this.shadows.instanceMatrix.needsUpdate = true;
    if (this.sky) this.sky.position.copy(this.camera.position);
    this.renderer.render(this.scene, this.camera);
    this.cpuMs = this.cpuMs * 0.9 + (performance.now() - now) * 0.1;
    this.adaptive.frame(frameMs);
    const s = this.me.state;
    this.debug.frame(frameMs, this.renderer, {
      ping: null, players: this.actors.length, tick: this.tick,
      extra: {
        'cpu ms/frame': this.cpuMs.toFixed(1),
        'render scale': this.adaptive.scale.toFixed(2),
        'chars drawn': visibleChars,
        pos: `${s.x.toFixed(1)}, ${s.y.toFixed(1)}, ${s.z.toFixed(1)}`,
        grounded: s.grounded ? 'yes' : 'no',
        crouch: s.crouch ? 'yes' : 'no',
        'sim ms/tick': this.simCost.toFixed(2),
      },
    });
  };
  private simCost = 0;

  private camPos = new THREE.Vector3();
  private myPos = new THREE.Vector3();
  private tmpVec = new THREE.Vector3();
  private pivot = new THREE.Vector3();
  private fwd = new THREE.Vector3();
  private right = new THREE.Vector3();
  private dir = new THREE.Vector3();
  private aim = new THREE.Vector3();
  private updateCamera(dt: number) {
    const a = this.me;
    this.pivot.set(this.myPos.x, this.myPos.y + eyeOffset(a.state), this.myPos.z);
    const pivot = this.pivot;
    const yaw = this.controls.yaw, pitch = this.controls.pitch;
    const fwd = this.fwd.set(-Math.sin(yaw) * Math.cos(pitch), Math.sin(pitch), -Math.cos(yaw) * Math.cos(pitch));
    const right = this.right.set(Math.cos(yaw), 0, -Math.sin(yaw));
    const scoped = this.controls.scoped;
    const targetDist = scoped ? 1.5 : 3.6;
    this.camDist += (targetDist - this.camDist) * Math.min(1, dt * 12);
    const shoulder = scoped ? 0.5 : 0.65;
    // Desired camera position: behind the shoulder, slightly above
    const dir = this.dir.copy(right).multiplyScalar(shoulder).addScaledVector(fwd, -this.camDist);
    dir.y += 0.35;
    const len = dir.length();
    dir.divideScalar(len);
    // Camera collision: pull in if a wall is between pivot and camera
    const hit = this.phys.rayWorld(pivot.x, pivot.y, pivot.z, dir.x, dir.y, dir.z, len + 0.2);
    const finalLen = hit !== null ? Math.max(0.3, hit - 0.25) : len;
    this.camPos.copy(pivot).addScaledVector(dir, finalLen);
    this.camera.position.copy(this.camPos);
    // Look at a far point along the aim direction so the crosshair matches the aim ray
    this.aim.copy(pivot).addScaledVector(right, shoulder).addScaledVector(fwd, 100);
    this.camera.lookAt(this.aim);
    const fov = scoped ? 40 : 70;
    if (Math.abs(this.camera.fov - fov) > 0.1) {
      this.camera.fov += (fov - this.camera.fov) * Math.min(1, dt * 12);
      this.camera.updateProjectionMatrix();
    }
  }
}

function lerpAngle(a: number, b: number, t: number) {
  let d = b - a;
  while (d > Math.PI) d -= Math.PI * 2;
  while (d < -Math.PI) d += Math.PI * 2;
  return a + d * t;
}
