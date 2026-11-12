import * as THREE from 'three';
import {
  BotBrain, DT, MAP, PLAYER, PhysicsWorld, generateMap, newMoveState, simulateMove, eyeOffset,
  type InputCmd, type MapData, type MoveState,
} from '@lw/shared';
import { buildMapView } from '../render/mapView';
import { CharacterView } from '../render/characters';
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

  constructor(container: HTMLElement, private hud: HTMLElement) {
    this.renderer = new THREE.WebGLRenderer({ antialias: true, powerPreference: 'high-performance' });
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio, 1.25));
    this.renderer.setSize(window.innerWidth, window.innerHeight);
    container.appendChild(this.renderer.domElement);

    this.camera = new THREE.PerspectiveCamera(70, window.innerWidth / window.innerHeight, 0.1, 1200);
    this.scene.fog = new THREE.Fog(0xcfe6f2, 60, 260);
    this.scene.add(new THREE.HemisphereLight(0xdff1ff, 0x4a6b3a, 1.6));
    const sun = new THREE.DirectionalLight(0xfff2d6, 1.8);
    sun.position.set(80, 140, 50);
    this.scene.add(sun);

    this.map = generateMap(MAP.seed);
    this.phys = new PhysicsWorld(this.map);
    this.scene.add(buildMapView(this.map));

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
        view: new CharacterView(i), speed: 0,
        brain: i === 0 ? undefined : new BotBrain(1000 + i),
      };
      this.scene.add(actor.view.root);
      this.actors.push(actor);
    }
    this.me = this.actors[0];
    this.phys.updateQueries();
    // Face toward the map centre initially
    this.controls.yaw = Math.atan2(this.me.state.x, this.me.state.z);

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

    for (const a of this.actors) {
      const x = a.prev.x + (a.state.x - a.prev.x) * alpha;
      const y = a.prev.y + (a.state.y - a.prev.y) * alpha;
      const z = a.prev.z + (a.state.z - a.prev.z) * alpha;
      const yaw = a === this.me ? this.controls.yaw : lerpAngle(a.prev.yaw, a.state.yaw, alpha);
      a.view.update(x, y, z, yaw, a.speed, a.state.crouch, a.state.grounded, dt);
    }
    this.updateCamera(dt);
    this.renderer.render(this.scene, this.camera);
    const s = this.me.state;
    this.debug.frame(frameMs, this.renderer, {
      ping: null, players: this.actors.length, tick: this.tick,
      extra: {
        pos: `${s.x.toFixed(1)}, ${s.y.toFixed(1)}, ${s.z.toFixed(1)}`,
        grounded: s.grounded ? 'yes' : 'no',
        crouch: s.crouch ? 'yes' : 'no',
        'sim ms/tick': this.simCost.toFixed(2),
      },
    });
  };
  private simCost = 0;

  private camPos = new THREE.Vector3();
  private updateCamera(dt: number) {
    const a = this.me;
    const pivot = new THREE.Vector3(
      a.view.root.position.x,
      a.view.root.position.y + (a.state.crouch ? PLAYER.crouchHalfHeight : PLAYER.halfHeight) + PLAYER.radius + eyeOffset(a.state),
      a.view.root.position.z,
    );
    const yaw = this.controls.yaw, pitch = this.controls.pitch;
    const fwd = new THREE.Vector3(-Math.sin(yaw) * Math.cos(pitch), Math.sin(pitch), -Math.cos(yaw) * Math.cos(pitch));
    const right = new THREE.Vector3(Math.cos(yaw), 0, -Math.sin(yaw));
    const scoped = this.controls.scoped;
    const targetDist = scoped ? 1.4 : 3.4;
    this.camDist += (targetDist - this.camDist) * Math.min(1, dt * 12);
    const shoulder = scoped ? 0.45 : 0.6;
    const desired = pivot.clone().addScaledVector(right, shoulder).addScaledVector(fwd, -this.camDist).add(new THREE.Vector3(0, 0.25, 0));
    // Camera collision: pull in if a wall is between pivot and camera
    const dir = desired.clone().sub(pivot);
    const len = dir.length();
    dir.divideScalar(len);
    const hit = this.phys.rayWorld(pivot.x, pivot.y, pivot.z, dir.x, dir.y, dir.z, len + 0.2);
    const finalLen = hit !== null ? Math.max(0.3, hit - 0.25) : len;
    this.camPos.copy(pivot).addScaledVector(dir, finalLen);
    this.camera.position.copy(this.camPos);
    // Look at a far point along the aim direction from the pivot so the crosshair matches the aim ray
    const aimPoint = pivot.clone().addScaledVector(right, shoulder).addScaledVector(fwd, 100);
    this.camera.lookAt(aimPoint);
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
