import * as THREE from 'three';
import {
  BTN, DT, LOOT, MAP, WEAPONS, GameSim, zoneAt, eyeOffset, halfTotalOf, hitboxOf, outsideZone, rayHitbox,
  type Hitbox, type InputCmd, type SimEvent, type SimPlayer, type WeaponId,
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
import type { NetSession } from '../net/session';

/** Vertical field of view per optic (smaller = more zoom). */
const SCOPE_FOV: Record<string, number> = { reddot: 52, x2: 34, x4: 18, x8: 10 };

export interface GameOptions { net?: NetSession; name: string; skin: number; slot: number }

/** Share a friends-room invite link (native share sheet on phones, clipboard fallback). */
export function shareRoom(code: string) {
  const url = `${location.origin}/?room=${encodeURIComponent(code)}`;
  const text = `Join my Last War squad! Room ${code}`;
  if (navigator.share) void navigator.share({ title: 'Last War', text, url }).catch(() => {});
  else void navigator.clipboard?.writeText(url).then(() => alert('Invite link copied'));
}
import { loadSettings, settingsPanel, type Settings } from '../ui/settings';
import { ICONS } from '../ui/icons';
import { LootView } from '../render/lootView';
import { applyLayout, editLayout } from '../ui/layout';

const BOT_COUNT = 15;
const BOT_NAMES = ['Falcon', 'Viper', 'Ghost', 'Raven', 'Cobra', 'Nomad', 'Atlas', 'Blaze', 'Storm', 'Shadow', 'Hunter', 'Titan', 'Fox', 'Wolf', 'Echo', 'Saber', 'Onyx', 'Jackal', 'Rook'];

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
  private killerId: number | null = null;
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
  private scopeMode: 'none' | 'reddot' | 'x2' | 'x4' | 'x8' = 'none';
  private loot!: LootView;
  private lootTimer = 0;

  private myId = 0;
  private net: NetSession | null;
  private lobbyEl: HTMLDivElement | null = null;

  constructor(container: HTMLElement, private hudEl: HTMLElement, private assets: GameAssets, private onRestart: () => void, opts: GameOptions) {
    this.net = opts.net ?? null;
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

    this.sim = this.net ? this.net.sim : new GameSim(MAP.seed);
    if (this.net) this.myId = this.net.myId;
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
    settingsPanel(hudEl, this.settings, applySettings, () => editLayout(hudEl));
    this.fx = new Effects(this.scene);
    this.loot = new LootView(this.scene, assets);
    this.buildButtons();

    // players
    if (!this.net) {
      const mine = this.sim.addPlayer(this.myId, opts.name || 'You', false);
      mine.skin = opts.skin; mine.slot = opts.slot;
      const names = [...BOT_NAMES].sort(() => Math.random() - 0.5);
      for (let i = 1; i <= BOT_COUNT; i++) this.sim.addPlayer(i, names[i % names.length], true);
    } else {
      this.net.onEvents = (ev) => this.handleEvents(ev);
      this.net.onCosmeticShot = (w) => this.cosmeticShot(w);
      this.net.onClose = () => { if (!this.ended) this.hud.killFeed('<b>Disconnected from server</b>', true); };
      this.lobbyEl = document.createElement('div');
      this.lobbyEl.className = 'wait-lobby';
      hudEl.appendChild(this.lobbyEl);
    }
    this.ensureViews();
    this.sim.phys.updateQueries();
    const me = this.sim.players.get(this.myId)!;
    this.controls.yaw = me.move.yaw;
    this.controls.weapon = me.slot;

    this.shadows = new THREE.InstancedMesh(
      new THREE.CircleGeometry(0.45, 12).rotateX(-Math.PI / 2),
      new THREE.MeshBasicMaterial({ color: 0x000000, transparent: true, opacity: 0.28, depthWrite: false }),
      24,
    );
    this.shadows.frustumCulled = false;
    this.shadows.renderOrder = 1;
    this.scene.add(this.shadows);

    (window as unknown as { __game: unknown }).__game = this; // test hook
    window.addEventListener('resize', this.onResize);
    requestAnimationFrame(this.frame);
  }

  /** Create views for players that appeared (online: roster updates, bots at match start). */
  private ensureViews() {
    for (const p of this.sim.players.values()) if (!this.views.has(p.id)) this.addPlayer(p);
  }

  private addPlayer(p: SimPlayer) {
    const gun = p.weapons[p.slot].id as GunName;
    const view = new CharacterView(this.assets, p.skin, gun);
    this.scene.add(view.root);
    this.views.set(p.id, { p, view, prev: { x: p.move.x, y: p.move.y, z: p.move.z, yaw: p.move.yaw }, gun, firingT: 0, stepAcc: 0, wasGrounded: true });
  }

  private buildButtons() {
    const mk = (id: string, icon: string, label = '') => {
      const b = document.createElement('div');
      b.className = `btn btn-${id}`;
      b.dataset.hud = id;
      b.innerHTML = icon + (label ? `<small>${label}</small>` : '');
      this.hudEl.appendChild(b);
      return b;
    };
    const fire = mk('fire', ICONS.fire);
    const fireL = mk('fire-left', ICONS.fire);
    const jump = mk('jump', ICONS.jump);
    const crouch = mk('crouch', ICONS.crouch);
    const prone = mk('prone', ICONS.prone);
    const scope = mk('scope', ICONS.scope);
    const reload = mk('reload', ICONS.reload);
    const heal = mk('heal', ICONS.heal + '<b>0</b>');
    const frost = mk('frost', ICONS.frost + '<b>0</b>');
    this.hud.healBtn = heal; this.hud.frostBtn = frost;
    this.controls.bindButton(heal, () => this.controls.heal());
    this.controls.bindButton(frost, () => this.controls.frost());
    this.controls.onSprintChange = () => sync();
    const sprint = mk('sprint', ICONS.sprint);
    const lock = document.createElement('div');
    lock.className = 'sprint-lock';
    lock.innerHTML = ICONS.sprint;
    this.hudEl.appendChild(lock);
    this.controls.onSprintLock = (on) => lock.classList.toggle('on', on);
    const sync = () => {
      crouch.classList.toggle('active', this.controls.crouchToggle);
      prone.classList.toggle('active', this.controls.proneToggle);
      sprint.classList.toggle('active', this.controls.sprintToggle);
      scope.classList.toggle('active', this.controls.scoped);
    };
    this.syncButtons = sync;
    this.controls.bindLookButton(fire, () => { this.controls.fireHeld = true; this.controls.sprintToggle = false; sync(); }, () => { this.controls.fireHeld = false; });
    this.controls.bindButton(fireL, () => { this.controls.fireHeld = true; this.controls.sprintToggle = false; sync(); }, () => { this.controls.fireHeld = false; });
    this.controls.bindButton(jump, () => this.controls.jump());
    this.controls.bindButton(reload, () => this.controls.reload());
    this.controls.bindButton(crouch, () => { this.controls.toggleCrouch(); sync(); });
    this.controls.bindButton(prone, () => { this.controls.toggleProne(); sync(); });
    this.controls.bindButton(sprint, () => { this.controls.toggleSprint(); sync(); });
    this.controls.bindButton(scope, () => { this.controls.setScope(!this.controls.scoped); sync(); });
    applyLayout(this.hudEl);
    if (!this.controls.isTouch) {
      const help = document.createElement('div');
      help.className = 'help';
      help.textContent = 'Click to look · WASD · Shift sprint · LMB fire · RMB scope · R reload · 1-4 weapons · Space jump/vault · C crouch · Z prone';
      this.hudEl.appendChild(help);
    }
  }
  private syncButtons = () => {};

  private onResize = () => {
    this.camera.aspect = window.innerWidth / window.innerHeight;
    this.camera.updateProjectionMatrix();
    this.renderer.setSize(window.innerWidth, window.innerHeight);
  };

  // ------------------------------------------------------------------ simulation tick
  private stepSim() {
    const me = this.sim.players.get(this.myId)!;
    if (this.net) {
      const v = this.views.get(this.myId);
      if (v) { v.prev.x = me.move.x; v.prev.y = me.move.y; v.prev.z = me.move.z; v.prev.yaw = me.move.yaw; }
      const { mx, mz } = this.controls.axes();
      const aim = me.alive ? this.computeAim(me) : { yaw: this.controls.yaw, pitch: this.controls.pitch };
      this.net.localTick({ mx, mz, yaw: this.controls.yaw, pitch: this.controls.pitch, buttons: this.controls.sampleButtons(), aimYaw: aim.yaw, aimPitch: aim.pitch, weapon: this.controls.weapon });
      return;
    }
    if (me.alive) {
      const { mx, mz } = this.controls.axes();
      const aim = this.computeAim(me);
      const input: InputCmd = {
        seq: ++this.seq, mx, mz, yaw: this.controls.yaw, pitch: this.controls.pitch,
        buttons: this.controls.sampleButtons(), aimYaw: aim.yaw, aimPitch: aim.pitch, weapon: this.controls.weapon,
      };
      this.sim.setInput(this.myId, input);
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

  /** Online: my shot is shown immediately (muzzle, tracer, sound); hits/damage come from the server. */
  private cosmeticShot(w: WeaponId) {
    const v = this.views.get(this.myId);
    if (!v) return;
    v.firingT = 0.35;
    const muzzle = v.view.muzzleWorld(this.v1);
    this.fx.muzzle(muzzle);
    this.fx.tracer(muzzle, this.aim);
    sfx.shot(w, 0, 0);
  }

  private nameOf(id: number) { return this.sim.players.get(id)?.name ?? '?'; }

  private handleEvents(events: SimEvent[]) {
    const me = this.sim.players.get(this.myId)!;
    const ear = this.camera.position;
    for (const e of events) {
      switch (e.t) {
        case 'shot': {
          if (this.net && e.id === this.myId) break; // already shown instantly (prediction)
          const v = this.views.get(e.id);
          if (!v) break;
          v.firingT = 0.35;
          const muzzle = v.view.muzzleWorld(this.v1);
          this.fx.muzzle(muzzle);
          for (let i = 0; i < e.ends.length; i += 3) {
            this.v2.set(e.ends[i], e.ends[i + 1], e.ends[i + 2]);
            this.fx.tracer(muzzle, this.v2);
            this.fx.impact(this.v2, 'dust', 2);
          }
          const dist = muzzle.distanceTo(ear);
          sfx.shot(e.weapon, e.id === this.myId ? 0 : dist, this.panOf(muzzle));
          break;
        }
        case 'hit': {
          this.v1.set(e.x, e.y, e.z);
          this.fx.impact(this.v1, e.armorHit ? 'armor' : 'blood', 5);
          if (e.attacker === this.myId) {
            this.hud.damageNumber(this.v1, e.dmg, e.head, e.armorHit);
            this.hud.hitMarker(false);
            sfx.hitMarker(e.head);
          }
          if (e.target === this.myId) { this.hud.hurtFlash(); sfx.hurt(); }
          break;
        }
        case 'kill': {
          if (e.weapon === 'left') { this.hud.killFeed(`<b>${this.nameOf(e.victim)}</b> <i>left the match</i>`, false); break; }
          const weapon = e.weapon === 'zone' ? 'ZONE' : WEAPONS[e.weapon].name;
          const killer = e.killer >= 0 ? this.nameOf(e.killer) : '';
          this.hud.killFeed(e.killer >= 0
            ? `<b>${killer}</b> <i>${weapon}${e.head ? ' ✦' : ''}</i> <b>${this.nameOf(e.victim)}</b>`
            : `<b>${this.nameOf(e.victim)}</b> <i>died in the zone</i>`, e.killer === this.myId || e.victim === this.myId);
          if (e.killer === this.myId) { this.hud.hitMarker(true); sfx.kill(); }
          if (e.victim === this.myId) {
            this.killerName = e.killer >= 0 ? killer : null;
            this.spectate = null; // watch your own soldier fall
            this.killerId = e.killer >= 0 ? e.killer : null;
            setTimeout(() => this.showEnd(false), 2600);
          }
          if (e.victim === this.spectate) this.spectate = e.killer >= 0 ? e.killer : null;
          break;
        }
        case 'reload': if (e.id === this.myId) sfx.reload(); break;
        case 'loot+': this.loot.dirty = true; break;
        case 'loot-':
          this.loot.dirty = true;
          if (e.by === this.myId) { this.hud.toast(lootLabel(e)); sfx.pickup(); }
          break;
        case 'heal': if (e.id === this.myId) this.hud.toast(`❤ healed · ${Math.round(e.hp)} HP`); break;
        case 'wall+': if (e.wall) sfx.frost(); break;
        case 'zone': if (e.id === this.myId) { this.hud.hurtFlash(); sfx.zoneTick(); } break;
        case 'win': if (e.id === this.myId) setTimeout(() => this.showEnd(true), 800); else if (!me.alive) this.spectate = e.id; break;
      }
    }
  }

  private aliveCount() { return this.net ? this.net.snapshot?.alive ?? 0 : this.sim.alive; }

  private updateLobbyOverlay() {
    const net = this.net!, el = this.lobbyEl!;
    if (net.phase !== 'lobby') { el.style.display = 'none'; return; }
    el.style.display = 'block';
    const l = net.lobby;
    const code = net.room.roomId;
    const isHost = l.hostId === this.myId;
    const key = `${l.countdown}|${l.humans}|${isHost}|${l.private}`;
    if (el.dataset.k === key) return;
    el.dataset.k = key;
    el.innerHTML = `<b>${l.private ? 'FRIENDS ROOM' : 'MATCHMAKING'}</b>
      <div>${l.humans}/${l.max} players · ${l.private ? (isHost ? 'start when your squad is in' : 'waiting for the host') : `starting in ${l.countdown}s`}</div>
      <div class="hint">Empty slots are filled with bots</div>
      ${l.private ? `<div class="code">Room code: <b>${code}</b></div><button class="share">INVITE FRIENDS</button>` : ''}
      ${l.private && isHost ? '<button class="go">START MATCH</button>' : ''}`;
    el.querySelector('.share')?.addEventListener('click', () => shareRoom(code));
    el.querySelector('.go')?.addEventListener('click', () => net.startNow());
  }

  private showEnd(win: boolean) {
    if (this.ended && !win) return;
    this.ended = true;
    const me = this.sim.players.get(this.myId)!;
    this.controls.fireHeld = false;
    this.hud.showEnd(win, win ? 1 : me.place, this.sim.players.size, me.kills, this.killerName, () => {
      this.dispose();
      this.onRestart();
    }, () => {
      const alive = [...this.sim.players.values()].filter((p) => p.alive);
      this.spectate = this.killerId !== null && this.sim.players.get(this.killerId)?.alive ? this.killerId : alive[0]?.id ?? null;
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
    if (this.net) {
      this.net.frame(dt);
      this.ensureViews();
      const live = this.net.phase !== 'lobby';
      this.sim.zone = zoneAt(this.sim.seed, live ? Math.max(0, Math.floor(this.net.serverTick) - this.sim.startTick) : 0);
      this.updateLobbyOverlay();
      if (this.net.phase === 'ended' && !this.ended) { const me = this.sim.players.get(this.myId); if (me?.alive) setTimeout(() => this.showEnd(true), 800); this.ended = true; }
    }
    this.acc += dt;
    let steps = 0;
    const t0 = performance.now();
    while (this.acc >= DT && steps < 4) { this.stepSim(); this.acc -= DT; steps++; }
    if (steps) this.simCost = this.simCost * 0.9 + ((performance.now() - t0) / steps) * 0.1;
    if (steps === 4) this.acc = 0;
    const alpha = this.acc / DT;

    const me = this.sim.players.get(this.myId)!;
    const meV = this.views.get(this.myId)!;
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
    this.scopeMode = !scopedView ? 'none' : slot.id === 'sniper' ? 'x8' : slot.id === 'rifle' && me.scope ? (me.scope >= 4 ? 'x4' : 'x2') : 'reddot';
    this.hud.setScope(this.scopeMode);
    if (me.alive) this.aimAssist(dt, me);
    this.updateCamera(dt, focusV, focusV === meV ? this.controls.yaw : focusV.p.move.yaw, focusV === meV ? this.controls.pitch : focusV.p.move.pitch, scopedView);
    this.controls.fovScale = this.camera.fov / 70;
    this.camera.updateMatrixWorld();
    this.projScreen.multiplyMatrices(this.camera.projectionMatrix, this.camera.matrixWorldInverse);
    this.frustum.setFromProjectionMatrix(this.projScreen);

    let drawn = 0, si = 0;
    for (const v of this.views.values()) {
      const p = v.p;
      if (this.net && p.id !== this.myId) { v.prev.x = p.move.x; v.prev.y = p.move.y; v.prev.z = p.move.z; v.prev.yaw = p.move.yaw; }
      let x = v.prev.x + (p.move.x - v.prev.x) * alpha;
      let y = v.prev.y + (p.move.y - v.prev.y) * alpha;
      let z = v.prev.z + (p.move.z - v.prev.z) * alpha;
      if (this.net && p.id === this.myId) { x += this.net.correction.x; y += this.net.correction.y; z += this.net.correction.z; }
      const yaw = p.id === this.myId && p.alive ? this.controls.yaw : lerpAngle(v.prev.yaw, p.move.yaw, alpha);
      const dist = this.camera.position.distanceTo(this.v1.set(x, y, z));
      this.sphere.center.set(x, y, z); this.sphere.radius = 1.3;
      let visible = dist < 240 && this.frustum.intersectsSphere(this.sphere);
      if (p.id === this.myId && scopedView) visible = false; // first-person scope
      if (visible) drawn++;
      v.firingT = Math.max(0, v.firingT - dt);
      const want = p.weapons[p.slot].id as GunName;
      if (want !== v.gun) { v.gun = want; v.view.setGun(this.assets, want); }
      v.view.update({ x, y, z, yaw, pitch: p.id === this.myId ? this.controls.pitch : p.move.pitch, speed: p.alive ? p.speed : 0, armed: p.weapons.some((w) => w.owned), vx: p.move.vx, vz: p.move.vz, crouch: p.move.crouch, prone: p.move.prone, ball: p.move.ball, grounded: p.move.grounded || !p.alive, firing: v.firingT > 0, dead: !p.alive }, dt, dist, visible);
      // footsteps
      if (p.alive && p.move.grounded && p.speed > 1.2 && dist < 40) {
        v.stepAcc += p.speed * dt;
        const stride = p.move.crouch ? 1.4 : 2.0;
        if (v.stepAcc > stride) { v.stepAcc = 0; sfx.step(p.id === this.myId ? 0 : dist, this.panOf(this.v1.set(x, y, z)), p.move.crouch); }
      }
      if (p.id === this.myId && p.alive && p.move.grounded && !v.wasGrounded) sfx.land();
      v.wasGrounded = p.move.grounded;
      const gy = p.move.grounded || !p.alive ? y - halfTotalOf(p.move) : this.sim.phys.groundHeight(x, z);
      this.tmpM.makeTranslation(x, gy + 0.05, z);
      this.shadows.setMatrixAt(si++, this.tmpM);
    }
    this.shadows.instanceMatrix.needsUpdate = true;
    this.syncButtons();

    // zone wall
    const zone = this.sim.zone;
    this.zoneWall.position.set(zone.cx, 40, zone.cz);
    this.zoneWall.scale.set(Math.max(0.1, zone.r), 1, Math.max(0.1, zone.r));
    if (this.sky) this.sky.position.copy(this.camera.position);

    this.fx.update(dt);
    this.lootTimer -= dt;
    if (this.loot.dirty || this.lootTimer <= 0) { this.loot.setItems(this.sim.loot.values()); this.lootTimer = 0.5; }
    this.loot.syncWalls(this.sim.walls);
    this.loot.update(dt, this.camera);
    this.renderer.render(this.scene, this.camera);

    // HUD
    const reloadFrac = me.reloadEndTick ? 1 - (me.reloadEndTick - this.sim.tick) / (WEAPONS[slot.id].reloadTime * 30) : 0;
    const healFrac = me.healEndTick ? 1 - Math.max(0, me.healEndTick - (this.net ? this.net.serverTick : this.sim.tick)) / (LOOT.medTime * 30) : 0;
    this.hud.setSlots(me, reloadFrac, healFrac);
    const out = me.alive && outsideZone(zone, me.move.x, me.move.z);
    this.hud.setInfo(this.aliveCount(), me.kills, zone, { x: meV.view.root.position.x, z: meV.view.root.position.z, yaw: this.controls.yaw }, out, dt);
    this.hud.update(dt, this.camera);

    this.cpuMs = this.cpuMs * 0.9 + (performance.now() - now) * 0.1;
    this.adaptive.frame(frameMs);
    const s = me.move;
    this.debug.frame(frameMs, this.renderer, {
      ping: this.net ? this.net.ping : null, players: this.aliveCount(), tick: this.net ? Math.floor(this.net.serverTick) : this.sim.tick,
      extra: {
        ...(this.net ? { 'interp (ticks)': this.net.interpTicks.toFixed(1), corrections: this.net.stats.corrections } : {}),
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
    const scoped = this.controls.scoped && v.p.id === this.myId && v.p.alive;
    const w = WEAPONS[v.p.weapons[v.p.slot].id];
    let fov = 70;
    if (firstPerson) {
      this.camDist = 0;
      this.camera.position.copy(this.pivot).addScaledVector(fwd, 0.25);
      fov = SCOPE_FOV[this.scopeMode] ?? w.scopeFov;
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
    this.net?.leave();
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

function lootLabel(e: Extract<SimEvent, { t: 'loot-' }>): string {
  const wname = (w: unknown) => WEAPONS[w as keyof typeof WEAPONS]?.name ?? '';
  switch (e.kind) {
    case 'weapon': return `+ ${wname(e.item)}`;
    case 'ammo': return `+ ${wname(e.item)} ammo`;
    case 'med': return '+ Medkit';
    case 'armor': return '+ Armor';
    case 'scope': return `+ ×${e.item} scope`;
    case 'frost': return '+ Ice wall';
  }
}
