import * as THREE from 'three';
import {
  BTN, DT, MAP, WEAPONS, GameSim, eyeOffset, halfTotalOf, hitboxOf, outsideZone, rayHitbox,
  type Hitbox, type InputCmd, type SimEvent, type SimPlayer,
} from '@lw/shared';
import { buildMapView } from '../render/mapView';
import { CharacterView } from '../render/characters';
import type { GameAssets, GunName } from '../render/assets';
import { AdaptiveResolution } from '../render/adaptive';
import { Effects } from '../render/effects';
import { Controls } from '../input/controls';
import { DebugOverlay } from '../ui/debug';
import { Hud } from '../ui/hud';
import { sfx } from '../audio/sfx';
import { loadSettings, settingsPanel, type Settings } from '../ui/settings';

const BOT_COUNT = 15;
const BOT_NAMES = ['Falcon', 'Viper', 'Ghost', 'Raven', 'Cobra', 'Nomad', 'Atlas', 'Blaze', 'Storm', 'Shadow', 'Hunter', 'Titan', 'Fox', 'Wolf', 'Echo', 'Saber', 'Onyx', 'Jackal', 'Rook'];
const ME = 0;

interface View {
  p: SimPlayer;
  view: CharacterView;
  prev: { x: number; y: number; z: number; yaw: number };
  gun: GunName;
  firingT: number;
  stepAcc: number;
  wasGrounded: boolean;
}

/** Offline match vs bots, running the same GameSim the server will run. */
export class Game {
  private renderer: THREE.WebGLRenderer;
  private scene = new THREE.Scene();
  private camera: THREE.PerspectiveCamera;
  private sim: GameSim;
  private views = new Map<number, View>();
  private controls: Controls;
  private debug: DebugOverlay;
  private hud: Hud;
  private fx: Effects;
  private adaptive: AdaptiveResolution;
  private sky: THREE.Object3D | null = null;
  private zoneWall: THREE.Mesh;
  private shadows: THREE.InstancedMesh;
  private acc = 0;
  private last = performance.now();
  private seq = 0;
  private camDist = 3.6;
  private running = true;
  private cpuMs = 0;
  private simCost = 0;
  private spectate: number | null = null;
  private ended = false;
  private killerName: string | null = null;
  // scratch
  private frustum = new THREE.Frustum();
  private projScreen = new THREE.Matrix4();
  private sphere = new THREE.Sphere();
  private tmpM = new THREE.Matrix4();
  private v1 = new THREE.Vector3();
  private v2 = new THREE.Vector3();
  private camPos = new THREE.Vector3();
  private focus = new THREE.Vector3();
  private pivot = new THREE.Vector3();
  private fwd = new THREE.Vector3();
  private right = new THREE.Vector3();
  private dir = new THREE.Vector3();
  private aim = new THREE.Vector3();
  private hb = {} as Hitbox;
  private settings: Settings = loadSettings();

  constructor(container: HTMLElement, private hudEl: HTMLElement, private assets: GameAssets, private onRestart: () => void) {
    this.renderer = new THREE.WebGLRenderer({ antialias: true, powerPreference: 'high-performance', stencil: false });
    this.renderer.setSize(window.innerWidth, window.innerHeight);
    this.renderer.setClearColor(0xd4e8f2);
    container.appendChild(this.renderer.domElement);
    this.adaptive = new AdaptiveResolution(this.renderer);

    this.camera = new THREE.PerspectiveCamera(70, window.innerWidth / window.innerHeight, 0.1, 300);
    this.scene.fog = new THREE.Fog(0xd4e8f2, 70, 240);
    this.scene.add(new THREE.HemisphereLight(0xe6f4ff, 0x4f6b3a, 1.7));
    const sun = new THREE.DirectionalLight(0xfff0d0, 1.9);
    sun.position.set(80, 140, 50);
    this.scene.add(sun);

    this.sim = new GameSim(MAP.seed);
    const mapView = buildMapView(this.sim.map, assets);
    this.sky = mapView.children.find((o) => (o as THREE.Mesh).material && ((o as THREE.Mesh).material as THREE.Material).side === THREE.BackSide) ?? null;
    if (this.sky) this.sky.matrixAutoUpdate = true;
    this.scene.add(mapView);

    // Zone wall: translucent cylinder
    const zGeo = new THREE.CylinderGeometry(1, 1, 160, 64, 1, true);
    this.zoneWall = new THREE.Mesh(zGeo, new THREE.MeshBasicMaterial({ color: 0x3f8cff, transparent: true, opacity: 0.28, side: THREE.DoubleSide, depthWrite: false, fog: false }));
    this.zoneWall.renderOrder = 2;
    this.scene.add(this.zoneWall);

    this.controls = new Controls(this.renderer.domElement, hudEl);
    this.hud = new Hud(hudEl, this.sim.map);
    this.hud.onSlot = (i) => { this.controls.weapon = i; };
    this.debug = new DebugOverlay(hudEl);
    const applySettings = (st: Settings) => {
      this.controls.sensitivity = st.sens; this.controls.scopeSensitivity = st.scopeSens; sfx.enabled = st.sound;
    };
    applySettings(this.settings);
    settingsPanel(hudEl, this.settings, applySettings);
    this.fx = new Effects(this.scene);
    this.buildButtons();

    // players
    this.addPlayer(this.sim.addPlayer(ME, 'You', false));
    const names = [...BOT_NAMES].sort(() => Math.random() - 0.5);
    for (let i = 1; i <= BOT_COUNT; i++) this.addPlayer(this.sim.addPlayer(i, names[i % names.length], true));
    this.sim.phys.updateQueries();
    const me = this.sim.players.get(ME)!;
    this.controls.yaw = me.move.yaw;
    this.controls.weapon = me.slot;

    this.shadows = new THREE.InstancedMesh(
      new THREE.CircleGeometry(0.45, 12).rotateX(-Math.PI / 2),
      new THREE.MeshBasicMaterial({ color: 0x000000, transparent: true, opacity: 0.28, depthWrite: false }),
      this.views.size,
    );
    this.shadows.frustumCulled = false;
    this.shadows.renderOrder = 1;
    this.scene.add(this.shadows);

    (window as unknown as { __game: unknown }).__game = this; // test hook
    window.addEventListener('resize', this.onResize);
    requestAnimationFrame(this.frame);
  }

  private addPlayer(p: SimPlayer) {
    const gun = p.weapons[p.slot].id as GunName;
    const view = new CharacterView(this.assets, p.id, gun);
    this.scene.add(view.root);
    this.views.set(p.id, { p, view, prev: { x: p.move.x, y: p.move.y, z: p.move.z, yaw: p.move.yaw }, gun, firingT: 0, stepAcc: 0, wasGrounded: true });
  }

  private buildButtons() {
    const mk = (cls: string, label: string) => {
      const b = document.createElement('div');
      b.className = `btn ${cls}`;
      b.innerHTML = label;
      this.hudEl.appendChild(b);
      return b;
    };
    const fire = mk('btn-fire', '<svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="7" fill="none" stroke="currentColor" stroke-width="2"/><path d="M12 2v6M12 16v6M2 12h6M16 12h6" stroke="currentColor" stroke-width="2"/></svg>');
    const fireL = mk('btn-fire-left', '<svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="6" fill="none" stroke="currentColor" stroke-width="2"/><path d="M12 3v5M12 16v5M3 12h5M16 12h5" stroke="currentColor" stroke-width="2"/></svg>');
    const jump = mk('btn-jump', 'JUMP');
    const crouch = mk('btn-crouch', 'CROUCH');
    const scope = mk('btn-scope', 'SCOPE');
    const reload = mk('btn-reload', 'RELOAD');
    this.controls.bindLookButton(fire, () => { this.controls.fireHeld = true; }, () => { this.controls.fireHeld = false; });
    this.controls.bindButton(fireL, () => { this.controls.fireHeld = true; }, () => { this.controls.fireHeld = false; });
    this.controls.bindButton(jump, () => this.controls.jump());
    this.controls.bindButton(reload, () => this.controls.reload());
    this.controls.bindButton(crouch, () => { this.controls.toggleCrouch(); crouch.classList.toggle('active', this.controls.crouchToggle); });
    this.controls.bindButton(scope, () => { this.controls.setScope(!this.controls.scoped); scope.classList.toggle('active', this.controls.scoped); });
    if (!this.controls.isTouch) {
      const help = document.createElement('div');
      help.className = 'help';
      help.textContent = 'Click to look · WASD · LMB fire · RMB scope · R reload · 1-4 weapons · Space jump/vault · C crouch';
      this.hudEl.appendChild(help);
    }
  }

  private onResize = () => {
    this.camera.aspect = window.innerWidth / window.innerHeight;
    this.camera.updateProjectionMatrix();
    this.renderer.setSize(window.innerWidth, window.innerHeight);
  };

  // ------------------------------------------------------------------ simulation tick
  private stepSim() {
    const me = this.sim.players.get(ME)!;
    if (me.alive) {
      const { mx, mz } = this.controls.axes();
      const aim = this.computeAim(me);
      const input: InputCmd = {
        seq: ++this.seq, mx, mz, yaw: this.controls.yaw, pitch: this.controls.pitch,
        buttons: this.controls.sampleButtons(), aimYaw: aim.yaw, aimPitch: aim.pitch, weapon: this.controls.weapon,
      };
      this.sim.setInput(ME, input);
    }
    for (const v of this.views.values()) { v.prev.x = v.p.move.x; v.prev.y = v.p.move.y; v.prev.z = v.p.move.z; v.prev.yaw = v.p.move.yaw; }
    this.sim.step();
    this.handleEvents(this.sim.events);
  }

  /** Direction from my eye to whatever is under the crosshair (third-person parallax correction). */
  private computeAim(me: SimPlayer) {
    const cam = this.camera;
    cam.getWorldDirection(this.dir);
    const start = this.v1.copy(cam.position).addScaledVector(this.dir, Math.max(0, this.camDist - 0.3));
    let best = this.sim.phys.rayWorld(start.x, start.y, start.z, this.dir.x, this.dir.y, this.dir.z, 300) ?? 300;
    for (const q of this.sim.players.values()) {
      if (q === me || !q.alive) continue;
      const h = rayHitbox(start.x, start.y, start.z, this.dir.x, this.dir.y, this.dir.z, hitboxOf(q.move, this.hb), best);
      if (h) best = h.t;
    }
    const target = this.aim.copy(start).addScaledVector(this.dir, best);
    const e = this.sim.eye(me);
    const dx = target.x - e.x, dy = target.y - e.y, dz = target.z - e.z;
    if (Math.hypot(dx, dz) < 1.2) return { yaw: this.controls.yaw, pitch: this.controls.pitch };
    return { yaw: Math.atan2(-dx, -dz), pitch: Math.atan2(dy, Math.hypot(dx, dz)) };
  }

  private nameOf(id: number) { return this.sim.players.get(id)?.name ?? '?'; }

  private handleEvents(events: SimEvent[]) {
    const me = this.sim.players.get(ME)!;
    const ear = this.camera.position;
    for (const e of events) {
      switch (e.t) {
        case 'shot': {
          const v = this.views.get(e.id)!;
          v.firingT = 0.35;
          const muzzle = v.view.muzzleWorld(this.v1);
          this.fx.muzzle(muzzle);
          for (let i = 0; i < e.ends.length; i += 3) {
            this.v2.set(e.ends[i], e.ends[i + 1], e.ends[i + 2]);
            this.fx.tracer(muzzle, this.v2);
            this.fx.impact(this.v2, 'dust', 2);
          }
          const dist = muzzle.distanceTo(ear);
          sfx.shot(e.weapon, e.id === ME ? 0 : dist, this.panOf(muzzle));
          break;
        }
        case 'hit': {
          this.v1.set(e.x, e.y, e.z);
          this.fx.impact(this.v1, e.armorHit ? 'armor' : 'blood', 5);
          if (e.attacker === ME) {
            this.hud.damageNumber(this.v1, e.dmg, e.head, e.armorHit);
            this.hud.hitMarker(false);
            sfx.hitMarker(e.head);
          }
          if (e.target === ME) { this.hud.hurtFlash(); sfx.hurt(); }
          break;
        }
        case 'kill': {
          const weapon = e.weapon === 'zone' ? 'ZONE' : WEAPONS[e.weapon].name;
          const killer = e.killer >= 0 ? this.nameOf(e.killer) : '';
          this.hud.killFeed(e.killer >= 0
            ? `<b>${killer}</b> <i>${weapon}${e.head ? ' ✦' : ''}</i> <b>${this.nameOf(e.victim)}</b>`
            : `<b>${this.nameOf(e.victim)}</b> <i>died in the zone</i>`, e.killer === ME || e.victim === ME);
          if (e.killer === ME) { this.hud.hitMarker(true); sfx.kill(); }
          if (e.victim === ME) {
            this.killerName = e.killer >= 0 ? killer : null;
            this.spectate = e.killer >= 0 ? e.killer : null;
            setTimeout(() => this.showEnd(false), 1200);
          }
          if (e.victim === this.spectate) this.spectate = e.killer >= 0 ? e.killer : null;
          break;
        }
        case 'reload': if (e.id === ME) sfx.reload(); break;
        case 'zone': if (e.id === ME) { this.hud.hurtFlash(); sfx.zoneTick(); } break;
        case 'win': if (e.id === ME) setTimeout(() => this.showEnd(true), 800); else if (!me.alive) this.spectate = e.id; break;
      }
    }
  }

  private showEnd(win: boolean) {
    if (this.ended && !win) return;
    this.ended = true;
    const me = this.sim.players.get(ME)!;
    this.controls.fireHeld = false;
    this.hud.showEnd(win, win ? 1 : me.place, this.sim.players.size, me.kills, this.killerName, () => {
      this.dispose();
      this.onRestart();
    });
  }

  private panOf(p: THREE.Vector3) {
    this.v2.subVectors(p, this.camera.position);
    const right = this.right.set(Math.cos(this.controls.yaw), 0, -Math.sin(this.controls.yaw));
    const len = this.v2.length();
    return len < 0.5 ? 0 : this.v2.dot(right) / len;
  }

  // ------------------------------------------------------------------ frame
  private frame = (now: number) => {
    if (!this.running) return;
    requestAnimationFrame(this.frame);
    const frameMs = now - this.last;
    const dt = Math.min(0.1, frameMs / 1000);
    this.last = now;
    this.acc += dt;
    let steps = 0;
    const t0 = performance.now();
    while (this.acc >= DT && steps < 4) { this.stepSim(); this.acc -= DT; steps++; }
    if (steps) this.simCost = this.simCost * 0.9 + ((performance.now() - t0) / steps) * 0.1;
    if (steps === 4) this.acc = 0;
    const alpha = this.acc / DT;

    const me = this.sim.players.get(ME)!;
    const meV = this.views.get(ME)!;
    const focusV = me.alive || this.spectate === null ? meV : this.views.get(this.spectate) ?? meV;
    const lerp = (v: View) => this.focus.set(
      v.prev.x + (v.p.move.x - v.prev.x) * alpha,
      v.prev.y + (v.p.move.y - v.prev.y) * alpha,
      v.prev.z + (v.p.move.z - v.prev.z) * alpha,
    );
    lerp(focusV);
    // Weapon / scope modes
    const slot = me.weapons[me.slot];
    // Scoped = first-person through the optic (red dot for pistol/AR/shotgun, full scope for the sniper)
    const scopedView = me.alive && this.controls.scoped && !me.reloadEndTick && !me.move.vault;
    this.controls.scopedView = scopedView;
    this.hud.setScope(!scopedView ? 'none' : slot.id === 'sniper' ? 'scope' : 'reddot');
    if (me.alive) this.aimAssist(dt, me);
    this.updateCamera(dt, focusV, focusV === meV ? this.controls.yaw : focusV.p.move.yaw, focusV === meV ? this.controls.pitch : focusV.p.move.pitch, scopedView);
    this.controls.fovScale = this.camera.fov / 70;
    this.camera.updateMatrixWorld();
    this.projScreen.multiplyMatrices(this.camera.projectionMatrix, this.camera.matrixWorldInverse);
    this.frustum.setFromProjectionMatrix(this.projScreen);

    let drawn = 0, si = 0;
    for (const v of this.views.values()) {
      const p = v.p;
      const x = v.prev.x + (p.move.x - v.prev.x) * alpha;
      const y = v.prev.y + (p.move.y - v.prev.y) * alpha;
      const z = v.prev.z + (p.move.z - v.prev.z) * alpha;
      const yaw = p.id === ME && p.alive ? this.controls.yaw : lerpAngle(v.prev.yaw, p.move.yaw, alpha);
      const dist = this.camera.position.distanceTo(this.v1.set(x, y, z));
      this.sphere.center.set(x, y, z); this.sphere.radius = 1.3;
      let visible = dist < 240 && this.frustum.intersectsSphere(this.sphere);
      if (p.id === ME && scopedView) visible = false; // first-person scope
      if (visible) drawn++;
      v.firingT = Math.max(0, v.firingT - dt);
      const want = p.weapons[p.slot].id as GunName;
      if (want !== v.gun) { v.gun = want; v.view.setGun(this.assets, want); }
      v.view.update({ x, y, z, yaw, pitch: p.id === ME ? this.controls.pitch : p.move.pitch, speed: p.alive ? p.speed : 0, vx: p.move.vx, vz: p.move.vz, crouch: p.move.crouch, grounded: p.move.grounded || !p.alive, firing: v.firingT > 0, dead: !p.alive }, dt, dist, visible);
      // footsteps
      if (p.alive && p.move.grounded && p.speed > 1.2 && dist < 40) {
        v.stepAcc += p.speed * dt;
        const stride = p.move.crouch ? 1.4 : 2.0;
        if (v.stepAcc > stride) { v.stepAcc = 0; sfx.step(p.id === ME ? 0 : dist, this.panOf(this.v1.set(x, y, z)), p.move.crouch); }
      }
      if (p.id === ME && p.alive && p.move.grounded && !v.wasGrounded) sfx.land();
      v.wasGrounded = p.move.grounded;
      const gy = p.move.grounded || !p.alive ? y - halfTotalOf(p.move) : this.sim.phys.groundHeight(x, z);
      this.tmpM.makeTranslation(x, gy + 0.05, z);
      this.shadows.setMatrixAt(si++, this.tmpM);
    }
    this.shadows.instanceMatrix.needsUpdate = true;

    // zone wall
    const zone = this.sim.zone;
    this.zoneWall.position.set(zone.cx, 40, zone.cz);
    this.zoneWall.scale.set(Math.max(0.1, zone.r), 1, Math.max(0.1, zone.r));
    if (this.sky) this.sky.position.copy(this.camera.position);

    this.fx.update(dt);
    this.renderer.render(this.scene, this.camera);

    // HUD
    const reloadFrac = me.reloadEndTick ? 1 - (me.reloadEndTick - this.sim.tick) / (WEAPONS[slot.id].reloadTime * 30) : 0;
    this.hud.setSlots(me, reloadFrac);
    const out = me.alive && outsideZone(zone, me.move.x, me.move.z);
    this.hud.setInfo(this.sim.alive, me.kills, zone, { x: meV.view.root.position.x, z: meV.view.root.position.z, yaw: this.controls.yaw }, out, dt);
    this.hud.update(dt, this.camera);

    this.cpuMs = this.cpuMs * 0.9 + (performance.now() - now) * 0.1;
    this.adaptive.frame(frameMs);
    const s = me.move;
    this.debug.frame(frameMs, this.renderer, {
      ping: null, players: this.sim.alive, tick: this.sim.tick,
      extra: {
        'cpu ms/frame': this.cpuMs.toFixed(1),
        'sim ms/tick': this.simCost.toFixed(2),
        'render scale': this.adaptive.scale.toFixed(2),
        'chars drawn': drawn,
        pos: `${s.x.toFixed(1)}, ${s.y.toFixed(1)}, ${s.z.toFixed(1)}`,
        state: s.vault ? 'vault' : s.ball ? 'small' : s.crouch ? 'crouch' : s.grounded ? 'ground' : 'air',
      },
    });
  };

  private updateCamera(dt: number, v: View, yaw: number, pitch: number, firstPerson: boolean) {
    const move = v.p.move;
    this.pivot.set(this.focus.x, this.focus.y + eyeOffset(move), this.focus.z);
    const fwd = this.fwd.set(-Math.sin(yaw) * Math.cos(pitch), Math.sin(pitch), -Math.cos(yaw) * Math.cos(pitch));
    const right = this.right.set(Math.cos(yaw), 0, -Math.sin(yaw));
    const scoped = this.controls.scoped && v.p.id === ME && v.p.alive;
    const w = WEAPONS[v.p.weapons[v.p.slot].id];
    let fov = 70;
    if (firstPerson) {
      this.camDist = 0;
      this.camera.position.copy(this.pivot).addScaledVector(fwd, 0.25);
      fov = w.scopeFov;
    } else {
      const targetDist = scoped ? 1.6 : 3.6;
      this.camDist += (targetDist - this.camDist) * Math.min(1, dt * 12);
      const shoulder = scoped ? 0.55 : 0.65;
      const dir = this.dir.copy(right).multiplyScalar(shoulder).addScaledVector(fwd, -this.camDist);
      dir.y += scoped ? 0.15 : 0.35;
      const len = dir.length();
      dir.divideScalar(len);
      const hit = this.sim.phys.rayWorld(this.pivot.x, this.pivot.y, this.pivot.z, dir.x, dir.y, dir.z, len + 0.2);
      const finalLen = hit !== null ? Math.max(0.3, hit - 0.25) : len;
      this.camPos.copy(this.pivot).addScaledVector(dir, finalLen);
      this.camera.position.copy(this.camPos);
      if (scoped) fov = Math.max(w.scopeFov, 45);
    }
    this.aim.copy(this.pivot).addScaledVector(right, firstPerson ? 0 : this.controls.scoped ? 0.55 : 0.65).addScaledVector(fwd, 100);
    this.camera.lookAt(this.aim);
    if (Math.abs(this.camera.fov - fov) > 0.05) {
      this.camera.fov += (fov - this.camera.fov) * Math.min(1, dt * 20);
      if (Math.abs(this.camera.fov - fov) < 0.3) this.camera.fov = fov;
      this.camera.updateProjectionMatrix();
    }
  }

  /**
   * Mobile-style aim assist (client-side input help, like most mobile shooters):
   * - crosshair turns red over an enemy
   * - friction: look slows down while on target
   * - magnetism: while firing/scoped the view is gently pulled toward the nearest enemy near the crosshair
   */
  private aimAssist(dt: number, me: SimPlayer) {
    const cam = this.camera.position;
    const yaw = this.controls.yaw, pitch = this.controls.pitch;
    this.fwd.set(-Math.sin(yaw) * Math.cos(pitch), Math.sin(pitch), -Math.cos(yaw) * Math.cos(pitch));
    // 1) is the crosshair on an enemy right now?
    let onEnemy = false;
    const wall = this.sim.phys.rayWorld(cam.x, cam.y, cam.z, this.fwd.x, this.fwd.y, this.fwd.z, 250) ?? 250;
    for (const q of this.sim.players.values()) {
      if (q === me || !q.alive) continue;
      if (rayHitbox(cam.x, cam.y, cam.z, this.fwd.x, this.fwd.y, this.fwd.z, hitboxOf(q.move, this.hb), wall)) { onEnemy = true; break; }
    }
    this.hud.setOnEnemy(onEnemy);
    this.controls.friction = onEnemy && this.settings.aimAssist ? 0.55 : 1;
    if (!this.settings.aimAssist) return;
    // 2) magnetism toward the closest enemy inside a small cone
    const engaged = this.controls.fireHeld || this.controls.scoped;
    if (!engaged) return;
    let best: { dy: number; dp: number; ang: number } | null = null;
    for (const q of this.sim.players.values()) {
      if (q === me || !q.alive) continue;
      const tx = q.move.x, ty = q.move.y + 0.15, tz = q.move.z;
      const dx = tx - cam.x, dy = ty - cam.y, dz = tz - cam.z;
      const dist = Math.hypot(dx, dy, dz);
      if (dist > 110) continue;
      let dyaw = Math.atan2(-dx, -dz) - yaw; dyaw = Math.atan2(Math.sin(dyaw), Math.cos(dyaw));
      const dp = Math.atan2(dy, Math.hypot(dx, dz)) - pitch;
      const ang = Math.hypot(dyaw, dp);
      const cone = Math.max(0.05, Math.atan(1.0 / dist)) * (this.controls.scoped ? 1.2 : 1.8);
      if (ang > cone || (best && ang > best.ang)) continue;
      if (!this.sim.los(cam.x, cam.y, cam.z, tx, ty, tz)) continue;
      best = { dy: dyaw, dp, ang };
    }
    if (!best) return;
    const rate = this.controls.fireHeld ? 7 : 3.5;
    const k = Math.min(1, rate * dt);
    this.controls.yaw += best.dy * k;
    this.controls.pitch += best.dp * k * 0.6;
  }

  dispose() {
    this.running = false;
    window.removeEventListener('resize', this.onResize);
    this.renderer.dispose();
    this.renderer.domElement.remove();
    this.hudEl.innerHTML = '';
  }
}

function lerpAngle(a: number, b: number, t: number) {
  let d = b - a;
  while (d > Math.PI) d -= Math.PI * 2;
  while (d < -Math.PI) d += Math.PI * 2;
  return a + d * t;
}

export { BTN };
