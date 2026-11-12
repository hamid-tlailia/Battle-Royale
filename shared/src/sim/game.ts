import { BTN, DT, TICK_RATE } from '../config/game';
import { ARMOR_ABSORB, MAX_ARMOR, MAX_HP, WEAPONS, WEAPON_IDS, damageAt, fireInterval, type WeaponId } from '../config/weapons';
import { generateMap, type MapData } from '../map/mapgen';
import { mulberry32 } from '../math/rng';
import { BotBrain } from './bot';
import { hitboxOf, rayHitbox, type Hitbox } from './combat';
import { eyeOffset, newMoveState, sanitizeInput, simulateMove, type InputCmd, type MoveState } from './movement';
import { PhysicsWorld } from './physics';
import { outsideZone, zoneAt, type ZoneState } from './zone';

export interface WeaponSlot { id: WeaponId; mag: number; reserve: number }

export interface SimPlayer {
  id: number;
  name: string;
  bot: BotBrain | null;
  move: MoveState;
  hp: number;
  armor: number;
  alive: boolean;
  weapons: WeaponSlot[];
  slot: number;
  nextFireTick: number;
  reloadEndTick: number; // 0 = not reloading
  prevButtons: number;
  /** semi-auto: a trigger press waiting to be fired (survives switch/fire cooldown) */
  triggerArmed: boolean;
  kills: number;
  lastInput: InputCmd;
  /** last processed input sequence (for client reconciliation) */
  ackSeq: number;
  place: number; // finishing position once dead (0 while alive)
  /** horizontal speed last tick (for animation) */
  speed: number;
}

export type SimEvent =
  | { t: 'shot'; id: number; weapon: WeaponId; ox: number; oy: number; oz: number; ends: number[] }
  | { t: 'hit'; attacker: number; target: number; dmg: number; head: boolean; x: number; y: number; z: number; armorHit: boolean }
  | { t: 'kill'; killer: number; victim: number; weapon: WeaponId | 'zone'; head: boolean }
  | { t: 'reload'; id: number }
  | { t: 'zone'; id: number; dmg: number }
  | { t: 'win'; id: number };

/**
 * Returns the hitbox to test for `target` when `shooter` fires. Offline: current position.
 * The server overrides this with rewound positions (lag compensation).
 */
export type HitboxProvider = (target: SimPlayer, shooter: SimPlayer, out: Hitbox) => Hitbox;

export interface GameOptions { seed?: number; zone?: boolean }

/** Authoritative match simulation shared by the offline client and the server. */
export class GameSim {
  readonly map: MapData;
  readonly phys: PhysicsWorld;
  readonly players = new Map<number, SimPlayer>();
  tick = 0;
  zone: ZoneState;
  events: SimEvent[] = [];
  winner: number | null = null;
  started = true;
  hitboxProvider: HitboxProvider = (t, _s, out) => hitboxOf(t.move, out);
  private aliveCount = 0;
  private rnd: () => number;
  private spawnIdx = 0;
  private hb = {} as Hitbox;

  constructor(readonly seed: number, readonly opts: GameOptions = {}) {
    this.map = generateMap(seed);
    this.phys = new PhysicsWorld(this.map);
    this.rnd = mulberry32(seed + 77);
    this.zone = zoneAt(seed, 0);
  }

  get alive() { return this.aliveCount; }

  addPlayer(id: number, name: string, bot: boolean): SimPlayer {
    const sp = this.map.spawns[(this.spawnIdx++ * 7) % this.map.spawns.length];
    const p = this.phys.findSpawn(sp.x, sp.z);
    this.phys.addPlayer(id, p.x, p.y, p.z);
    const move = newMoveState(p.x, p.y, p.z);
    move.yaw = Math.atan2(p.x, p.z); // face the valley centre
    const pl: SimPlayer = {
      id, name, bot: bot ? new BotBrain(this.seed * 31 + id) : null, move,
      hp: MAX_HP, armor: 50, alive: true,
      weapons: WEAPON_IDS.map((w) => ({ id: w, mag: WEAPONS[w].mag, reserve: WEAPONS[w].startReserve })),
      slot: 1, nextFireTick: 0, reloadEndTick: 0, prevButtons: 0, triggerArmed: false, kills: 0,
      lastInput: { seq: 0, mx: 0, mz: 0, yaw: move.yaw, pitch: 0, buttons: 0 }, ackSeq: 0, place: 0, speed: 0,
    };
    this.players.set(id, pl);
    this.aliveCount++;
    return pl;
  }

  /** Queue the latest input for a human player (bots generate their own). */
  setInput(id: number, input: InputCmd) {
    const p = this.players.get(id);
    if (p) p.lastInput = input;
  }

  step() {
    this.events.length = 0;
    this.zone = this.opts.zone === false ? zoneAt(this.seed, 0) : zoneAt(this.seed, this.tick);
    // 1) movement
    for (const p of this.players.values()) {
      if (!p.alive) continue;
      if (p.bot) p.lastInput = p.bot.think(p, this);
      const input = sanitizeInput(p.lastInput);
      const px = p.move.x, pz = p.move.z;
      simulateMove(this.phys, p.id, p.move, input, DT);
      p.speed = Math.hypot(p.move.x - px, p.move.z - pz) / DT;
      p.ackSeq = input.seq;
    }
    this.phys.updateQueries();
    // 2) weapons
    for (const p of this.players.values()) if (p.alive) this.weaponTick(p, sanitizeInput(p.lastInput));
    // 3) zone damage once per second (ignores armor)
    if (this.tick % TICK_RATE === 0 && this.zone.dps > 0) {
      for (const p of this.players.values()) {
        if (!p.alive || !outsideZone(this.zone, p.move.x, p.move.z)) continue;
        p.hp -= this.zone.dps;
        this.events.push({ t: 'zone', id: p.id, dmg: this.zone.dps });
        if (p.hp <= 0) this.kill(p, null, 'zone', false);
      }
    }
    this.tick++;
  }

  private weaponTick(p: SimPlayer, input: InputCmd) {
    // weapon switch
    if (input.weapon !== undefined && input.weapon !== p.slot && p.weapons[input.weapon]) {
      p.slot = input.weapon;
      p.reloadEndTick = 0;
      p.nextFireTick = Math.max(p.nextFireTick, this.tick + 8);
    }
    const slot = p.weapons[p.slot];
    const w = WEAPONS[slot.id];
    // reload finished?
    if (p.reloadEndTick && this.tick >= p.reloadEndTick) {
      const need = w.mag - slot.mag, take = Math.min(need, slot.reserve);
      slot.mag += take; slot.reserve -= take; p.reloadEndTick = 0;
    }
    const pressed = (b: number) => (input.buttons & b) !== 0 && (p.prevButtons & b) === 0;
    const startReload = () => {
      if (!p.reloadEndTick && slot.mag < w.mag && slot.reserve > 0 && !p.move.vault) {
        p.reloadEndTick = this.tick + Math.round(w.reloadTime * TICK_RATE);
        this.events.push({ t: 'reload', id: p.id });
      }
    };
    if (pressed(BTN.RELOAD)) startReload();
    const held = (input.buttons & BTN.FIRE) !== 0;
    if (pressed(BTN.FIRE)) p.triggerArmed = true;
    if (!held) p.triggerArmed = false;
    const wantFire = held && (w.auto || p.triggerArmed);
    if (wantFire && !p.reloadEndTick && !p.move.vault && this.tick >= p.nextFireTick) {
      if (slot.mag > 0) {
        slot.mag--;
        p.nextFireTick = this.tick + fireInterval(w);
        this.fire(p, input, slot.id);
        p.triggerArmed = false;
        if (slot.mag === 0) startReload();
      } else startReload();
    }
    p.prevButtons = input.buttons;
  }

  /** Eye position used as the bullet origin (server-side: never trust a client-sent origin). */
  eye(p: SimPlayer) {
    return { x: p.move.x, y: p.move.y + eyeOffset(p.move), z: p.move.z };
  }

  private fire(p: SimPlayer, input: InputCmd, wid: WeaponId) {
    const w = WEAPONS[wid];
    const o = this.eye(p);
    const scoped = (input.buttons & BTN.SCOPE) !== 0;
    const moving = p.speed > 1;
    const spread = (scoped ? w.adsSpread : w.hipSpread) * (moving ? 1.4 : 1) * (p.move.crouch ? 0.7 : 1) * (p.move.grounded ? 1 : 2);
    const yaw = input.aimYaw ?? input.yaw, pitch = input.aimPitch ?? input.pitch;
    // deterministic per-shot randomness (client prediction can reproduce it)
    const rnd = mulberry32((this.tick * 2654435761) ^ (p.id * 40503));
    const ends: number[] = [];
    const dmgBy = new Map<number, { dmg: number; head: boolean; x: number; y: number; z: number }>();
    for (let k = 0; k < w.pellets; k++) {
      const a = rnd() * Math.PI * 2, r = Math.sqrt(rnd()) * spread;
      const py = pitch + Math.sin(a) * r, yw = yaw + Math.cos(a) * r;
      const dx = -Math.sin(yw) * Math.cos(py), dy = Math.sin(py), dz = -Math.cos(yw) * Math.cos(py);
      const wall = this.phys.rayWorld(o.x, o.y, o.z, dx, dy, dz, w.range);
      let best = wall ?? w.range;
      let target: SimPlayer | null = null, head = false;
      for (const q of this.players.values()) {
        if (q === p || !q.alive) continue;
        const hb = this.hitboxProvider(q, p, this.hb);
        const h = rayHitbox(o.x, o.y, o.z, dx, dy, dz, hb, best);
        if (h && h.t < best) { best = h.t; target = q; head = h.head; }
      }
      ends.push(o.x + dx * best, o.y + dy * best, o.z + dz * best);
      if (target) {
        const d = damageAt(w, best, head);
        const prev = dmgBy.get(target.id);
        if (prev) { prev.dmg += d; prev.head ||= head; } else dmgBy.set(target.id, { dmg: d, head, x: o.x + dx * best, y: o.y + dy * best, z: o.z + dz * best });
      }
    }
    this.events.push({ t: 'shot', id: p.id, weapon: wid, ox: o.x, oy: o.y, oz: o.z, ends });
    for (const [tid, h] of dmgBy) this.damage(this.players.get(tid)!, p, h.dmg, h.head, h.x, h.y, h.z, wid);
  }

  private damage(t: SimPlayer, attacker: SimPlayer, raw: number, head: boolean, x: number, y: number, z: number, wid: WeaponId) {
    const dmg = Math.round(raw);
    let toHp = dmg;
    let armorHit = false;
    if (t.armor > 0) {
      const absorbed = Math.min(t.armor, Math.round(dmg * ARMOR_ABSORB));
      t.armor -= absorbed; toHp -= absorbed; armorHit = absorbed > 0;
    }
    t.hp -= toHp;
    this.events.push({ t: 'hit', attacker: attacker.id, target: t.id, dmg, head, x, y, z, armorHit });
    if (t.hp <= 0) this.kill(t, attacker, wid, head);
  }

  private kill(v: SimPlayer, killer: SimPlayer | null, weapon: WeaponId | 'zone', head: boolean) {
    if (!v.alive) return;
    v.alive = false;
    v.hp = 0;
    v.place = this.aliveCount;
    this.aliveCount--;
    this.phys.removePlayer(v.id);
    if (killer) killer.kills++;
    this.events.push({ t: 'kill', killer: killer ? killer.id : -1, victim: v.id, weapon, head });
    if (this.aliveCount === 1 && this.winner === null) {
      const w = [...this.players.values()].find((p) => p.alive)!;
      w.place = 1;
      this.winner = w.id;
      this.events.push({ t: 'win', id: w.id });
    }
  }

  /** Line of sight between two points against world geometry only. */
  los(ax: number, ay: number, az: number, bx: number, by: number, bz: number): boolean {
    const dx = bx - ax, dy = by - ay, dz = bz - az, L = Math.hypot(dx, dy, dz);
    if (L < 1e-3) return true;
    return this.phys.rayWorld(ax, ay, az, dx / L, dy / L, dz / L, L) === null;
  }

  random() { return this.rnd(); }
}

export { MAX_ARMOR, MAX_HP };
