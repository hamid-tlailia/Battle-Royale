import * as THREE from 'three';
import {
  BTN, DT, LOOT, MAP, SKY, WEAPONS, GameSim, planePath, zoneAt, eyeOffset, halfTotalOf, hitboxOf, outsideZone, rayHitbox, terrainHeight,
  type Hitbox, type InputCmd, type LootItem, type MapId, type SimEvent, type SimPlayer, type WeaponId,
} from '@lw/shared';
import { buildMapView, updateTreeLod } from '../render/mapView';
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

export interface GameOptions { net?: NetSession; name: string; skin: number; slot: number; map?: MapId }

/** Share a friends-room invite link (native share sheet on phones, clipboard fallback). */
export function shareRoom(code: string) {
  const url = `${location.origin}/?room=${encodeURIComponent(code)}`;
  const text = `Join my Last War squad! Room ${code}`;
  if (navigator.share) void navigator.share({ title: 'Last War', text, url }).catch(() => {});
  else void navigator.clipboard?.writeText(url).then(() => alert('Invite link copied'));
}
import { loadSettings, settingsPanel, type Settings } from '../ui/settings';
import { ICONS } from '../ui/icons';
import { createHudButtons } from '../ui/buttons';
import { LootView } from '../render/lootView';
import { PlaneView } from '../render/planeView';
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
  private v3 = new THREE.Vector3();
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
  private plane!: PlaneView;
  private planePos = new THREE.Vector3();
  private planeYaw = 0;
  private planeViewSet = false;
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

    this.sim = this.net ? this.net.sim : new GameSim(MAP.seed, { lobby: true, map: opts.map ?? 'classic', skyDrop: !new URLSearchParams(location.search).has('nodrop') });
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
    this.hud = new Hud(hudEl, this.sim.map, assets.icons);
    this.hud.onSlot = (i) => { this.controls.weapon = i; };
    this.debug = new DebugOverlay(hudEl);
    const applySettings = (st: Settings) => {
      this.controls.sensitivity = st.sens; this.controls.scopeSensitivity = st.scopeSens; sfx.enabled = st.sound; this.controls.autoPickup = st.autoPickup;
    };
    applySettings(this.settings);
    settingsPanel(hudEl, this.settings, applySettings, () => editLayout(hudEl));
    this.fx = new Effects(this.scene);
    this.loot = new LootView(this.scene, assets);
    this.plane = new PlaneView(this.scene);
    this.buildButtons();

    // players
    if (!this.net) {
      const mine = this.sim.addPlayer(this.myId, opts.name || 'You', false);
      mine.skin = opts.skin; mine.slot = opts.slot;
      const names = [...BOT_NAMES].sort(() => Math.random() - 0.5);
      for (let i = 1; i <= BOT_COUNT; i++) this.sim.addPlayer(i, names[i % names.length], true);
      this.sim.startMatch(); // board the plane
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
    const B = createHudButtons(this.hudEl);
    const lock = document.createElement('div');
    lock.className = 'sprint-lock';
    lock.innerHTML = ICONS.sprint;
    this.hudEl.appendChild(lock);
    this.controls.onSprintLock = (on) => lock.classList.toggle('on', on);
    const sync = () => {
      B.crouch.classList.toggle('active', this.controls.crouchToggle);
      B.prone.classList.toggle('active', this.controls.proneToggle);
      B.sprint.classList.toggle('active', this.controls.sprintToggle);
      B.scope.classList.toggle('active', this.controls.scoped);
      B.frost.classList.toggle('active', this.controls.frostAim);
    };
    this.syncButtons = sync;
    this.hud.healBtn = B.heal; this.hud.frostBtn = B.frost;
    const fireDown = () => {
      if (this.controls.frostAim) { this.controls.confirmFrost(); sync(); return; } // place the previewed ice wall
      this.controls.fireHeld = true; this.controls.fireTap = 2; if (this.controls.weapon === 4) this.controls.toggleHolster(); sync(); };
    this.controls.bindLookButton(B.fire, fireDown, () => { this.controls.fireHeld = false; });
    this.controls.bindButton(B['fire-left'], fireDown, () => { this.controls.fireHeld = false; });
    this.controls.bindButton(B.jump, () => this.controls.jump());
    this.controls.bindButton(B.reload, () => this.controls.reload());
    this.controls.bindButton(B.crouch, () => { this.controls.toggleCrouch(); sync(); });
    this.controls.bindButton(B.prone, () => { this.controls.toggleProne(); sync(); });
    this.controls.bindButton(B.sprint, () => { this.controls.toggleSprint(); sync(); });
    this.controls.bindButton(B.scope, () => { this.controls.setScope(!this.controls.scoped); sync(); });
    this.controls.bindButton(B.heal, () => this.controls.heal());
    this.controls.bindButton(B.frost, () => { if (this.sim.players.get(this.myId)?.frost) this.controls.frost(); sync(); });
    this.hud.onFist = () => { this.controls.toggleHolster(); sync(); };
    this.controls.bindButton(B.bag, () => this.hud.toggleBag());
    this.hud.onDrop = (code) => { this.controls.dropQueued = code; };
    this.hud.onPick = (id) => { this.controls.pickQueued = id; };
    this.hud.onWaypoint = (w) => this.setMarker(w);
    this.controls.onSprintChange = () => sync();
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
      this.net.localTick({ mx, mz, yaw: this.controls.yaw, pitch: this.controls.pitch, buttons: this.controls.sampleButtons(), aimYaw: aim.yaw, aimPitch: aim.pitch, weapon: this.controls.weapon, dropItem: this.controls.takeDrop(), pick: this.controls.takePick() });
      return;
    }
    if (me.alive) {
      const { mx, mz } = this.controls.axes();
      const aim = this.computeAim(me);
      const input: InputCmd = {
        seq: ++this.seq, mx, mz, yaw: this.controls.yaw, pitch: this.controls.pitch,
        buttons: this.controls.sampleButtons(), aimYaw: aim.yaw, aimPitch: aim.pitch, weapon: this.controls.weapon, dropItem: this.controls.takeDrop(), pick: this.controls.takePick(),
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
    if (this.controls.scopedView) muzzle.copy(this.camera.position).addScaledVector(this.camera.getWorldDirection(this.v3), 6);
    else this.fx.muzzle(muzzle);
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
          // looking through your own scope: no flash/tracer right in front of the lens (they covered the view)
          const ownScoped = e.id === this.myId && this.controls.scopedView;
          if (ownScoped) muzzle.copy(this.camera.position).addScaledVector(this.camera.getWorldDirection(this.v3), 6);
          else this.fx.muzzle(muzzle);
          for (let i = 0; i < e.ends.length; i += 3) {
            this.v2.set(e.ends[i], e.ends[i + 1], e.ends[i + 2]);
            if (!ownScoped || i === 0) this.fx.tracer(muzzle, this.v2);
            this.fx.impact(this.v2, 'dust', 2);
          }
          const dist = muzzle.distanceTo(ear);
          sfx.shot(e.weapon, e.id === this.myId ? 0 : dist, this.panOf(muzzle));
          if (e.id !== this.myId && dist < 220) this.hud.addShotMark(e.ox, e.oz);
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
          if (e.target === this.myId) {
            this.hud.hurtFlash(); sfx.hurt();
            const att = this.sim.players.get(e.attacker), me2 = this.sim.players.get(this.myId);
            if (att && me2) {
              const ang = Math.atan2(-(att.move.x - me2.move.x), -(att.move.z - me2.move.z)) - this.controls.yaw;
              this.hud.damageFrom(ang, att.move.x, att.move.z);
            }
          }
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

  private menuTimer = 0;
  /** long guns you own that are not in your hands: slung on the back */
  private backList(p: SimPlayer): GunName[] {
    const out: GunName[] = [];
    p.weapons.forEach((w, i) => { if (i > 0 && w.owned && (p.holstered || i !== p.slot)) out.push(w.id as GunName); });
    return out;
  }
  private frostGhost: THREE.Mesh | null = null;
  /** see-through ice wall where it would be placed (same rule as the server: in front, on the ground) */
  private updateFrostPreview(me: SimPlayer) {
    const on = this.controls.frostAim && me.alive && me.frost > 0 && !me.move.air;
    if (!on) { if (this.controls.frostAim && (!me.alive || me.frost <= 0)) this.controls.frostAim = false; if (this.frostGhost) this.frostGhost.visible = false; return; }
    if (!this.frostGhost) {
      this.frostGhost = new THREE.Mesh(new THREE.BoxGeometry(LOOT.frostWidth, LOOT.frostHeight, LOOT.frostThick),
        new THREE.MeshBasicMaterial({ color: 0x9fe6ff, transparent: true, opacity: 0.35, depthWrite: false }));
      this.frostGhost.add(new THREE.LineSegments(new THREE.EdgesGeometry(this.frostGhost.geometry), new THREE.LineBasicMaterial({ color: 0xffffff })));
      this.scene.add(this.frostGhost);
    }
    const yaw = this.controls.yaw, fx = -Math.sin(yaw), fz = -Math.cos(yaw);
    const x = me.move.x + fx * LOOT.frostDistance, z = me.move.z + fz * LOOT.frostDistance;
    this.frostGhost.position.set(x, this.sim.phys.groundHeight(x, z) + LOOT.frostHeight / 2, z);
    this.frostGhost.rotation.set(0, yaw, 0);
    this.frostGhost.visible = true;
  }
  private areaTimer = 0;
  private marker: THREE.Mesh | null = null;

  /** Tall light beam on the next route point (visible from the plane). */
  private setMarker(list: { x: number; z: number }[]) {
    const w = list[0];
    if (!w) { if (this.marker) this.marker.visible = false; return; }
    if (!this.marker) {
      this.marker = new THREE.Mesh(new THREE.CylinderGeometry(0.7, 0.7, 160, 10, 1, true),
        new THREE.MeshBasicMaterial({ color: 0xffd34d, transparent: true, opacity: 0.32, depthWrite: false, fog: false, side: THREE.DoubleSide }));
      this.scene.add(this.marker);
    }
    this.marker.visible = true;
    this.marker.position.set(w.x, this.sim.phys.groundHeight(w.x, w.z) + 80, w.z);
  }

  /** Place names over the world, only while high up; they fade out as you come down. */
  private updateAreaLabels(me: SimPlayer) {
    const h = this.camera.position.y - this.sim.phys.groundHeight(this.camera.position.x, this.camera.position.z);
    // see the whole valley from the plane; normal (cheap) view distance on the ground
    const up = THREE.MathUtils.clamp(h - 10, 0, 140);
    const far = 380 + up * 2.8;
    if (Math.abs(this.camera.far - far) > 8) { this.camera.far = far; this.camera.updateProjectionMatrix(); }
    const fog = this.scene.fog as THREE.Fog;
    fog.near = 110 + up * 1.4; fog.far = 340 + up * 2.6; // people stay visible ~250 m away
    const k = !me.alive ? 0 : me.move.air === 1 ? 1 : THREE.MathUtils.clamp((h - 22) / 35, 0, 1);
    // route arrow: next point relative to where you look; reached points are removed
    const wp = this.hud.waypoints[0];
    if (wp && me.alive) {
      const d = Math.hypot(wp.x - me.move.x, wp.z - me.move.z);
      if (d < 8 && !me.move.air) { this.hud.waypoints.shift(); this.setMarker(this.hud.waypoints); }
      else {
        const want = Math.atan2(-(wp.x - me.move.x), -(wp.z - me.move.z));
        let rel = this.controls.yaw - want; rel = Math.atan2(Math.sin(rel), Math.cos(rel));
        this.hud.setRouteArrow(rel, d);
      }
    } else this.hud.setRouteArrow(null);
    const out: { name: string; sx: number; sy: number; alpha: number }[] = [];
    if (k > 0) {
      const W = window.innerWidth, H = window.innerHeight;
      for (const a of this.sim.map.areas) {
        this.v1.set(a.x, this.sim.phys.groundHeight(a.x, a.z) + 10, a.z).project(this.camera);
        if (this.v1.z > 1 || Math.abs(this.v1.x) > 1.1 || Math.abs(this.v1.y) > 1.1) continue;
        const sx = (this.v1.x * 0.5 + 0.5) * W, sy = (-this.v1.y * 0.5 + 0.5) * H;
        if (out.some((o) => Math.abs(o.sx - sx) < 130 && Math.abs(o.sy - sy) < 26)) continue; // no overlapping names
        out.push({ name: a.name, sx, sy, alpha: k });
      }
    }
    this.hud.setAreaLabels(out);
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
    // drop plane
    const path = planePath(this.sim.seed);
    const live = this.net ? this.net.phase !== 'lobby' : this.sim.phase !== 'lobby';
    const lt = live ? (this.net ? this.net.serverTick : this.sim.tick + alpha) - this.sim.startTick : -1;
    const planeOn = live && lt >= 0 && lt < path.ticks + 90;
    if (planeOn) {
      const ext = Math.min(lt, path.ticks + 90) / path.ticks; // keeps flying a bit after the route
      this.planePos.set(path.ax + (path.bx - path.ax) * ext, SKY.planeAltitude + 1.5, path.az + (path.bz - path.az) * ext);
      this.planeYaw = path.yaw;
      // first frame in the plane: look the way it flies (not at its tail)
      if (me.move.air === 1 && !this.planeViewSet) { this.planeViewSet = true; this.controls.yaw = path.yaw; this.controls.pitch = -0.22; }
    }
    this.plane.update(dt, planeOn, this.planePos.x, this.planePos.y, this.planePos.z, this.planeYaw);
    this.hud.setPlaneHint(me.alive && me.move.air === 1, me.move.air >= 2);
    sfx.setWind(me.move.air === 2 ? 1 : me.move.air === 3 ? 0.4 : 0);
    // engine drone: loud inside, fading with distance once you have jumped
    sfx.setPlane(!planeOn ? 0 : me.move.air === 1 ? 1 : Math.max(0, 1 - this.planePos.distanceTo(this.camera.position) / 350) * 0.7);
    this.areaTimer -= dt;
    if (this.areaTimer <= 0) { this.areaTimer = 0.06; this.updateAreaLabels(me); updateTreeLod(this.camera.position); }
    const scopedView = me.alive && this.controls.scoped && !me.reloadEndTick && !me.move.vault && !me.move.air && !me.holstered;
    this.controls.scopedView = scopedView;
    this.hudEl.classList.toggle('scoped-ui', scopedView);
    this.hudEl.classList.toggle('sky', me.alive && me.move.air >= 2);
    this.updateFrostPreview(me);
    this.menuTimer -= dt;
    if (this.menuTimer <= 0) { // ground-loot menu
      this.menuTimer = 0.2;
      const near: LootItem[] = [];
      if (me.alive && !me.move.air) {
        const r2 = LOOT.menuRadius * LOOT.menuRadius;
        for (const it of this.sim.loot.values()) {
          const dx = it.x - me.move.x, dz = it.z - me.move.z;
          if (dx * dx + dz * dz <= r2 && Math.abs(it.y - (me.move.y - 0.6)) < 1.8) near.push(it);
        }
        near.sort((a, b) => (a.kind === 'weapon' ? 0 : 1) - (b.kind === 'weapon' ? 0 : 1));
      }
      this.hud.setNearbyLoot(near);
    }
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
      v.view.ownView = p.id === this.myId;
      v.view.update({ x, y, z, yaw, pitch: p.id === this.myId ? this.controls.pitch : p.move.pitch, speed: p.alive ? p.speed : 0, armed: p.weapons.some((w) => w.owned) && !p.holstered && p.weapons[p.slot].owned, back: p.id === this.myId ? this.backList(p) : undefined, air: p.move.air, vx: p.move.vx, vz: p.move.vz, crouch: p.move.crouch, prone: p.move.prone, ball: p.move.ball, grounded: p.move.grounded || !p.alive, firing: v.firingT > 0, dead: !p.alive }, dt, dist, visible);
      // footsteps
      if (p.alive && p.move.grounded && p.speed > 1.2 && dist < 26) { // only nearby footsteps, panned left/right
        v.stepAcc += p.speed * dt;
        const stride = p.move.crouch ? 1.4 : 2.0;
        if (v.stepAcc > stride) { v.stepAcc = 0; sfx.step(p.id === this.myId ? 0 : dist, this.panOf(this.v1.set(x, y, z)), p.move.crouch, y - halfTotalOf(p.move) > terrainHeight(this.sim.map, x, z) + 0.3); }
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
    const shown = !me.alive && focusV !== meV ? focusV.p : me;
    this.hud.setSlots(shown, shown === me ? reloadFrac : 0, shown === me ? healFrac : 0);
    this.hud.setSpectating(shown === me ? null : shown.name);
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
    if (move.air === 1) { // chase camera around the plane
      this.focus.copy(this.planePos);
      const d = 30, fw = this.fwd.set(-Math.sin(yaw) * Math.cos(pitch), Math.sin(pitch), -Math.cos(yaw) * Math.cos(pitch));
      this.camera.position.copy(this.planePos).addScaledVector(fw, -d).add(this.dir.set(0, 7, 0));
      this.camera.lookAt(this.planePos);
      if (Math.abs(this.camera.fov - 70) > 0.1) { this.camera.fov = 70; this.camera.updateProjectionMatrix(); }
      return;
    }
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
      const targetDist = scoped ? 1.6 : move.air ? 6.5 : 3.6;
      this.camDist += (targetDist - this.camDist) * Math.min(1, dt * 12);
      const shoulder = scoped ? 0.55 : 0.65;
      const dir = this.dir.copy(right).multiplyScalar(shoulder).addScaledVector(fwd, -this.camDist);
      dir.y += scoped ? 0.15 : 0.35;
      const len = dir.length();
      dir.divideScalar(len);
      const hit = this.sim.phys.rayWorld(this.pivot.x, this.pivot.y, this.pivot.z, dir.x, dir.y, dir.z, len + 0.2);
      const finalLen = hit !== null ? Math.max(0.3, hit - 0.25) : len;
      this.camPos.copy(this.pivot).addScaledVector(dir, finalLen);
      // under a parachute the canopy hangs ~4-7 m overhead: keep the camera below it so turning never hides the player
      if (move.air === 3 && this.camPos.y > this.pivot.y + 1.6) this.camPos.y = this.pivot.y + 1.6;
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
    this.renderer.dispose(); this.renderer.forceContextLoss();
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
