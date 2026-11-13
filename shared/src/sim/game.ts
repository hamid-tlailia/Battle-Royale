import { BTN, DT, GROUP_PLAYER, MAP, TICK_RATE, groups, SKIN_COUNT } from '../config/game';
import { LOOT, LOOT_TABLE, type LootItem } from '../config/loot';
import { PREFABS } from '../map/prefabs';
import { ARMOR_ABSORB, MAX_ARMOR, MAX_HP, SLOT_DEFAULT, SLOT_OF, WEAPONS, WEAPON_IDS, damageAt, fireInterval, scopeBit, type WeaponId } from '../config/weapons';
import { generateMap, type MapData, type MapId } from '../map/mapgen';
import { mulberry32 } from '../math/rng';
import { BotBrain } from './bot';
import { hitboxOf, lerpHitbox, rayHitbox, type Hitbox } from './combat';
import { SKY, eyeOffset, halfTotalOf, newMoveState, sanitizeInput, simulateMove, syncCollider, type InputCmd, type MoveState } from './movement';
import { PhysicsWorld, RAPIER } from './physics';

const quatY = (rot: number) => ({ x: 0, y: Math.sin(rot / 2), z: 0, w: Math.cos(rot / 2) });
import { outsideZone, zoneAt, type ZoneState } from './zone';

export interface WeaponSlot { id: WeaponId; owned: boolean; mag: number; reserve: number }

/** Deployable ice wall (cover). */
export interface FrostWall { id: number; x: number; y: number; z: number; rot: number; hp: number; until: number; handle: number }

export interface SimPlayer {
  id: number;
  name: string;
  /** cosmetic outfit index (lobby choice) */
  skin: number;
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
  /** server: inputs to process this tick (0 = starved, 2 = catch-up). null = use lastInput */
  queue: InputCmd[] | null;
  /** last processed input sequence (for client reconciliation) */
  ackSeq: number;
  place: number; // finishing position once dead (0 while alive)
  /** horizontal speed last tick (for animation) */
  speed: number;
  meds: number;
  frost: number;
  /** scope attached to the long-range gun (0 = iron sight / red dot, 2, 4, 8) */
  scope: number;
  /** scopes carried (bitmask, see scopeBit) */
  scopes: number;
  /** spare ammo of weapon types not in any slot */
  ammo: Record<WeaponId, number>;
  /** medkit in use until this tick (0 = not healing) */
  healEndTick: number;
  /** weapon put away */
  holstered: boolean;
  /** bots: fraction of the flight at which they jump */
  jumpAt: number;
}

/** Inventory drop codes (InputCmd.dropItem). */
/** weapon: slot 0-2 · ammo: WEAPON_IDS index · scope: drop the attached one · scopeUse: attach zoom (0 = none) */
export const DROP = { weapon: (i: number) => 1 + i, ammo: (i: number) => 11 + i, med: 20, frost: 21, scope: 22, scopeUse: (zoom: number) => 30 + zoom } as const;

/** total spare ammo of one weapon type (in its slot or in the pouch) */
export function ammoOf(p: SimPlayer, id: WeaponId): number {
  const s = p.weapons.find((w) => w.id === id);
  return s ? s.reserve : p.ammo[id] ?? 0;
}
function addAmmo(p: SimPlayer, id: WeaponId, n: number) {
  const s = p.weapons.find((w) => w.id === id);
  if (s) s.reserve += n; else p.ammo[id] = (p.ammo[id] ?? 0) + n;
}
/** put weapon type `id` into its slot (ammo follows the weapon type) */
function setSlotWeapon(p: SimPlayer, si: number, id: WeaponId) {
  const slot = p.weapons[si];
  if (slot.id === id) return;
  p.ammo[slot.id] = (p.ammo[slot.id] ?? 0) + slot.reserve;
  slot.id = id; slot.reserve = p.ammo[id] ?? 0; p.ammo[id] = 0;
}
const zeroAmmo = (): Record<WeaponId, number> => ({ pistol: 0, rifle: 0, shotgun: 0, sniper: 0, smg: 0 });

/** Flight path of the drop plane (deterministic from the match seed). */
export function planePath(seed: number) {
  const r = mulberry32(seed ^ 0x9a7e);
  const a = r() * Math.PI * 2, off = (r() - 0.5) * 80;
  const R = MAP.size / 2 + 25;
  const nx = -Math.sin(a), nz = Math.cos(a);
  const ax = Math.cos(a) * R + nx * off, az = Math.sin(a) * R + nz * off;
  const bx = -Math.cos(a) * R + nx * off, bz = -Math.sin(a) * R + nz * off;
  const len = Math.hypot(bx - ax, bz - az);
  const ticks = Math.round((len / SKY.planeSpeed) * TICK_RATE);
  const at = (t: number) => { const k = Math.max(0, Math.min(1, t / ticks)); return { x: ax + (bx - ax) * k, y: SKY.planeAltitude, z: az + (bz - az) * k, k }; };
  return { ax, az, bx, bz, ticks, yaw: Math.atan2(-(bx - ax), -(bz - az)), at };
}

export type SimEvent =
  | { t: 'shot'; id: number; weapon: WeaponId; ox: number; oy: number; oz: number; ends: number[] }
  | { t: 'hit'; attacker: number; target: number; dmg: number; head: boolean; x: number; y: number; z: number; armorHit: boolean }
  | { t: 'kill'; killer: number; victim: number; weapon: WeaponId | 'zone' | 'left'; head: boolean }
  | { t: 'reload'; id: number }
  | { t: 'zone'; id: number; dmg: number }
  | { t: 'win'; id: number }
  | { t: 'loot+'; item: LootItem }
  | { t: 'loot-'; id: number; by: number; kind: LootItem['kind']; item?: LootItem['item']; amount: number }
  | { t: 'heal'; id: number; hp: number }
  | { t: 'jump'; id: number }
  | { t: 'wall+'; wall: Omit<FrostWall, 'handle'> }
  | { t: 'wall-'; id: number; broken: boolean };

/**
 * Weapon state machine for one tick: switching, reload, fire rate, ammo.
 * Shared by the server (authoritative) and the client (prediction of ammo / muzzle effects).
 */
export function weaponLogic(p: SimPlayer, input: InputCmd, tick: number, fire: (w: WeaponId) => void, onReload?: () => void) {
  if (input.weapon === 4) { if (!p.holstered) { p.holstered = true; p.reloadEndTick = 0; } }
  else if (input.weapon !== undefined && p.weapons[input.weapon]?.owned && (input.weapon !== p.slot || p.holstered)) {
    p.holstered = false;
    p.slot = input.weapon;
    p.reloadEndTick = 0;
    p.nextFireTick = Math.max(p.nextFireTick, tick + 8);
  }
  const slot = p.weapons[p.slot];
  const w = WEAPONS[slot.id];
  if (!slot.owned || p.holstered || p.move.air) { p.prevButtons = input.buttons; p.triggerArmed = false; return; }
  if (p.reloadEndTick && tick >= p.reloadEndTick) {
    const take = Math.min(w.mag - slot.mag, slot.reserve);
    slot.mag += take; slot.reserve -= take; p.reloadEndTick = 0;
  }
  const pressed = (b: number) => (input.buttons & b) !== 0 && (p.prevButtons & b) === 0;
  const startReload = () => {
    if (!p.reloadEndTick && slot.mag < w.mag && slot.reserve > 0 && !p.move.vault) {
      p.reloadEndTick = tick + Math.round(w.reloadTime * TICK_RATE);
      onReload?.();
    }
  };
  if (pressed(BTN.RELOAD)) startReload();
  const held = (input.buttons & BTN.FIRE) !== 0;
  if (pressed(BTN.FIRE)) p.triggerArmed = true;
  // semi-auto: a tap made while the gun is still cycling is remembered and fires as soon as it can
  // (only a reload cancels it); full-auto fires while held
  if (!held && (w.auto || p.reloadEndTick)) p.triggerArmed = false;
  const wantFire = w.auto ? held : p.triggerArmed;
  if (wantFire && !p.reloadEndTick && !p.move.vault && tick >= p.nextFireTick) {
    if (slot.mag > 0) {
      slot.mag--;
      p.nextFireTick = tick + fireInterval(w);
      fire(slot.id);
      p.triggerArmed = false;
      if (slot.mag === 0) startReload();
    } else startReload();
  }
  p.prevButtons = input.buttons;
}

/**
 * Lag compensation: how far back (ticks) the server may rewind hitboxes.
 * 30 ticks = 1 s (same order as common shooter engines). Covers 200 ms ping + interpolation + packet loss.
 */
export const MAX_REWIND_TICKS = 30;
const HISTORY = 40;

/**
 * Returns the hitbox to test for `target` when `shooter` fires. Offline: current position.
 * The server overrides this with rewound positions (lag compensation).
 */
export type HitboxProvider = (target: SimPlayer, shooter: SimPlayer, out: Hitbox) => Hitbox;

export interface GameOptions {
  seed?: number; zone?: boolean; lobby?: boolean;
  /** start with every weapon (tests / sandbox). Default: land empty-handed and loot. */
  armed?: boolean;
  /** generate ground loot (default true). Online clients receive loot from the server instead. */
  loot?: boolean;
  /** start the match from the drop plane (default true) */
  skyDrop?: boolean;
  /** which map (default 'classic') */
  map?: MapId;
}

/** Authoritative match simulation shared by the offline client and the server. */
export class GameSim {
  readonly map: MapData;
  readonly phys: PhysicsWorld;
  readonly players = new Map<number, SimPlayer>();
  tick = 0;
  zone: ZoneState;
  events: SimEvent[] = [];
  winner: number | null = null;
  /** lobby: players walk around, no damage, zone frozen. live: the match. */
  phase: 'lobby' | 'live' | 'ended' = 'live';
  startTick = 0;
  /** server: rewind hitboxes to what each shooter saw (lag compensation) */
  lagCompensation = false;
  private history = new Map<number, Hitbox[]>();
  hitboxProvider: HitboxProvider = (t, _s, out) => hitboxOf(t.move, out);
  readonly loot = new Map<number, LootItem>();
  readonly walls = new Map<number, FrostWall>();
  private nextLoot = 1;
  private nextWall = 1;
  private aliveCount = 0;
  private rnd: () => number;
  private spawnIdx = 0;
  private hb = {} as Hitbox;

  constructor(readonly seed: number, readonly opts: GameOptions = {}) {
    this.map = generateMap(seed, opts.map ?? 'classic');
    this.phys = new PhysicsWorld(this.map);
    this.rnd = mulberry32(seed + 77);
    this.zone = zoneAt(seed, 0);
    if (opts.lobby) this.phase = 'lobby';
    if (opts.loot !== false) this.spawnLoot();
  }

  // ------------------------------------------------------------------ loot
  /** Deterministic ground loot: inside every house and scattered across the valley. */
  private spawnLoot() {
    const rnd = mulberry32(this.seed ^ 0x10070);
    const total = LOOT_TABLE.reduce((a, e) => a + e.weight, 0);
    const pick = () => { let r = rnd() * total; for (const e of LOOT_TABLE) { r -= e.weight; if (r <= 0) return e; } return LOOT_TABLE[0]; };
    const place = (x: number, y: number, z: number) => {
      const e = pick();
      const amount = e.kind === 'ammo' ? LOOT.ammoBox[e.item as WeaponId] : 1;
      this.addLoot({ kind: e.kind, item: e.item, amount, x, y, z }, false);
      // a weapon usually comes with some ammo next to it
      if (e.kind === 'weapon' && rnd() < 0.8) this.addLoot({ kind: 'ammo', item: e.item, amount: LOOT.ammoBox[e.item as WeaponId], x: x + 0.5, y, z: z + 0.3 }, false);
    };
    const free = (x: number, y: number, z: number) => !this.phys.shapeBlocked(x, y + 0.35, z, 0.001, 0.25);
    for (const h of this.map.props) {
      if (!h.p.startsWith('house') && h.p !== 'farmhouse' && h.p !== 'yardwall') continue;
      const [sx, , sz] = PREFABS[h.p].size;
      const c = Math.cos(h.rot), sn = Math.sin(h.rot);
      let n = 0;
      for (let k = 0; k < 20 && n < 6; k++) {
        const lx = (rnd() - 0.5) * sx * 0.7, lz = (rnd() - 0.5) * sz * 0.7;
        const x = h.x + lx * c + lz * sn, z = h.z - lx * sn + lz * c;
        const down = this.phys.rayWorld(x, h.y + 1.6, z, 0, -1, 0, 3);
        if (down === null) continue;
        const fy = h.y + 1.6 - down;
        if (Math.abs(fy - h.y) > 0.5 || !free(x, fy, z)) continue;
        place(x, fy + 0.05, z); n++;
      }
    }
    for (let k = 0, n = 0; k < 2000 && n < Math.round(115 * (MAP.playRadius / 125) ** 2); k++) {
      const a = rnd() * Math.PI * 2, d = Math.sqrt(rnd()) * (MAP.playRadius - 8);
      const x = Math.cos(a) * d, z = Math.sin(a) * d, y = this.phys.groundHeight(x, z);
      if (!free(x, y, z)) continue;
      place(x, y + 0.05, z); n++;
    }
  }

  addLoot(it: Omit<LootItem, 'id'>, announce = true): LootItem {
    const item = { ...it, id: this.nextLoot++ };
    this.loot.set(item.id, item);
    if (announce) this.events.push({ t: 'loot+', item });
    return item;
  }

  /** Online client: install the server's loot / walls. */
  setWorldItems(loot: LootItem[], walls: Omit<FrostWall, 'handle'>[]) {
    this.loot.clear();
    for (const l of loot) this.loot.set(l.id, l);
    for (const w of walls) this.addWallCollider(w);
  }

  /** Walk-over pickup. Returns true if the item was (fully) taken. */
  private tryPickup(p: SimPlayer, it: LootItem, manual = false): boolean {
    switch (it.kind) {
      case 'weapon': {
        const id = it.item as WeaponId, si = SLOT_OF[id], slot = p.weapons[si];
        // walking over a gun does nothing if that slot is taken (no endless ammo vacuuming / no surprise swaps)
        if (slot.owned && !manual) return false;
        if (slot.owned && slot.id === id) { // same gun: take its magazine as ammo
          if (slot.reserve >= LOOT.maxReserve[id]) return false;
          slot.reserve = Math.min(LOOT.maxReserve[id], slot.reserve + WEAPONS[id].mag);
          return true;
        }
        if (slot.owned) { // other gun of the same kind: swap (yours goes on the ground)
          p.ammo[slot.id] += slot.mag;
          const old = this.addLoot({ kind: 'weapon', item: slot.id, amount: 1, x: it.x, y: it.y, z: it.z + 0.4 });
          old.lockId = p.id; old.lockUntil = this.tick + 2 * TICK_RATE;
          if (p.slot === si) p.reloadEndTick = 0;
        }
        setSlotWeapon(p, si, id);
        slot.owned = true; slot.mag = WEAPONS[id].mag;
        if (!p.weapons[p.slot].owned) { p.slot = si; p.holstered = false; }
        return true;
      }
      case 'ammo': {
        const id = it.item as WeaponId;
        const room = LOOT.maxReserve[id] - ammoOf(p, id);
        if (room <= 0) return false;
        const take = Math.min(room, it.amount);
        addAmmo(p, id, take); it.amount -= take;
        return it.amount <= 0;
      }
      case 'med': {
        const take = Math.min(LOOT.maxMeds - p.meds, it.amount);
        if (take <= 0) return false;
        p.meds += take; it.amount -= take;
        return it.amount <= 0;
      }
      case 'frost': {
        const take = Math.min(LOOT.maxFrost - p.frost, it.amount);
        if (take <= 0) return false;
        p.frost += take; it.amount -= take;
        return it.amount <= 0;
      }
      case 'armor':
        if (p.armor >= MAX_ARMOR) return false;
        p.armor = Math.min(MAX_ARMOR, p.armor + LOOT.armorVest);
        return true;
      case 'scope': {
        const bit = scopeBit(it.item as number);
        if (!bit || p.scopes & bit) return false;
        p.scopes |= bit;
        if (p.scope === 0) p.scope = it.item as number; // first scope goes straight on the gun
        return true;
      }
    }
  }

  /** Loot menu: take one specific item within reach. */
  private manualPick(p: SimPlayer, id: number) {
    const it = this.loot.get(id);
    if (!it || !p.alive || p.move.air) return;
    const dx = it.x - p.move.x, dz = it.z - p.move.z;
    if (dx * dx + dz * dz > LOOT.menuRadius * LOOT.menuRadius || Math.abs(it.y - (p.move.y - 0.6)) > 1.8) return;
    if (it.lockId === p.id && this.tick < (it.lockUntil ?? 0)) return;
    const before = it.amount;
    if (this.tryPickup(p, it, true)) { this.loot.delete(it.id); this.events.push({ t: 'loot-', id: it.id, by: p.id, kind: it.kind, item: it.item, amount: before }); }
  }

  private pickups(p: SimPlayer) {
    // humans choose (settings): walk-over pickup on/off; bots always pick up
    if (!p.bot && !(p.lastInput.buttons & BTN.AUTOPICK)) return;
    const r2 = LOOT.pickupRadius * LOOT.pickupRadius;
    for (const it of this.loot.values()) {
      const dx = it.x - p.move.x, dz = it.z - p.move.z;
      if (dx * dx + dz * dz > r2 || Math.abs(it.y - (p.move.y - 0.6)) > 1.6) continue;
      if (it.lockId === p.id && this.tick < (it.lockUntil ?? 0)) continue; // just dropped it
      const before = it.amount;
      if (this.tryPickup(p, it)) { this.loot.delete(it.id); this.events.push({ t: 'loot-', id: it.id, by: p.id, kind: it.kind, item: it.item, amount: before }); }
    }
  }

  /** A dead player's inventory spills on the ground. */
  private dropInventory(v: SimPlayer) {
    const drops: Omit<LootItem, 'id' | 'x' | 'y' | 'z'>[] = [];
    v.weapons.forEach((w) => {
      if (w.owned) drops.push({ kind: 'weapon', item: w.id, amount: 1 });
      if (w.reserve + (w.owned ? w.mag : 0) > 0) drops.push({ kind: 'ammo', item: w.id, amount: w.reserve + (w.owned ? w.mag : 0) });
    });
    for (const id of WEAPON_IDS) if (v.ammo[id] > 0) drops.push({ kind: 'ammo', item: id, amount: v.ammo[id] });
    if (v.meds) drops.push({ kind: 'med', amount: v.meds });
    if (v.frost) drops.push({ kind: 'frost', amount: v.frost });
    for (const z of [2, 4, 8]) if (v.scopes & scopeBit(z)) drops.push({ kind: 'scope', item: z, amount: 1 });
    // everything goes into one crate on the ground where the player fell
    const y = this.phys.groundHeight(v.move.x, v.move.z) + 0.05;
    const box = 1 + v.id * 7 + (this.tick % 7);
    drops.forEach((d) => this.addLoot({ ...d, x: v.move.x, y: Math.max(y, v.move.y - halfTotalOf(v.move) + 0.05), z: v.move.z, box }));
  }

  // ------------------------------------------------------------------ medkit & frost wall
  private items(p: SimPlayer, input: InputCmd) {
    const pressed = (b: number) => (input.buttons & b) !== 0 && (p.prevButtons & b) === 0;
    // medkit: channel, cancelled by shooting
    if (p.healEndTick) {
      if (input.buttons & BTN.FIRE) p.healEndTick = 0;
      else if (this.tick >= p.healEndTick) {
        p.healEndTick = 0;
        p.meds--;
        p.hp = Math.min(MAX_HP, p.hp + LOOT.medHeal);
        this.events.push({ t: 'heal', id: p.id, hp: p.hp });
      }
    } else if (pressed(BTN.HEAL) && p.meds > 0 && p.hp < MAX_HP) {
      p.healEndTick = this.tick + Math.round(LOOT.medTime * TICK_RATE);
    }
    if (pressed(BTN.FROST) && p.frost > 0) this.placeWall(p);
    if (input.dropItem) this.dropItem(p, input.dropItem);
    if (input.pick) this.manualPick(p, input.pick);
  }

  /** Backpack: drop one item type in front of the player (cannot re-pick it for 2 s). */
  private dropItem(p: SimPlayer, code: number) {
    const fx = -Math.sin(p.move.yaw), fz = -Math.cos(p.move.yaw);
    const at = { x: p.move.x + fx * 1.6, y: p.move.y - halfTotalOf(p.move) + 0.05, z: p.move.z + fz * 1.6 };
    let item: Omit<LootItem, 'id' | 'x' | 'y' | 'z'> | null = null;
    if (code >= 30 && code <= 38) { // attach a carried scope (or none)
      const z = code - 30;
      if (z === 0 || p.scopes & scopeBit(z)) p.scope = z;
      return;
    }
    if (code >= 1 && code <= 3) {
      const w = p.weapons[code - 1];
      if (!w.owned) return;
      w.owned = false; w.reserve += w.mag; w.mag = 0;
      if (p.slot === code - 1) { const next = p.weapons.findIndex((x) => x.owned); if (next >= 0) p.slot = next; p.reloadEndTick = 0; }
      item = { kind: 'weapon', item: w.id, amount: 1 };
    } else if (code >= 11 && code < 11 + WEAPON_IDS.length) {
      const id = WEAPON_IDS[code - 11], n = ammoOf(p, id);
      if (n <= 0) return;
      item = { kind: 'ammo', item: id, amount: n }; addAmmo(p, id, -n);
    } else if (code === DROP.med && p.meds > 0) { p.meds--; p.healEndTick = 0; item = { kind: 'med', amount: 1 }; }
    else if (code === DROP.frost && p.frost > 0) { p.frost--; item = { kind: 'frost', amount: 1 }; }
    else if (code === DROP.scope && p.scope > 0) { item = { kind: 'scope', item: p.scope, amount: 1 }; p.scopes &= ~scopeBit(p.scope); p.scope = 0; }
    if (!item) return;
    const it = this.addLoot({ ...item, ...at });
    it.lockId = p.id; it.lockUntil = this.tick + 2 * TICK_RATE;
  }

  /** Where an ice wall would go for this player looking along `yaw` (same answer for the preview and the real wall). */
  wallSpot(p: SimPlayer, yaw = p.move.yaw): { x: number; y: number; z: number; rot: number } | null {
    const fx = -Math.sin(yaw), fz = -Math.cos(yaw);
    const eye = this.eye(p);
    for (const dist of [LOOT.frostDistance, LOOT.frostDistance + 0.8, LOOT.frostDistance - 0.6]) {
      // stop in front of walls
      const block = this.phys.rayWorld(eye.x, eye.y, eye.z, fx, 0, fz, dist);
      if (block !== null) continue;
      const x = p.move.x + fx * dist, z = p.move.z + fz * dist;
      // stand on whatever is under that spot at your level (a house floor, not the ground below it)
      const down = this.phys.rayWorld(x, eye.y + 0.5, z, 0, -1, 0, 6);
      if (down === null) continue;
      const y = eye.y + 0.5 - down + LOOT.frostHeight / 2;
      // never trap or overlap a player
      let blocked = false;
      this.phys.world.intersectionsWithShape({ x, y, z }, quatY(yaw), new RAPIER.Cuboid(LOOT.frostWidth / 2, LOOT.frostHeight / 2 - 0.05, LOOT.frostThick / 2),
        () => { blocked = true; return false; }, undefined, groups(0xffff, GROUP_PLAYER));
      if (blocked) continue;
      return { x, y, z, rot: yaw };
    }
    return null;
  }

  private placeWall(p: SimPlayer) {
    const at = this.wallSpot(p);
    if (!at) return;
    const w = { id: this.nextWall++, ...at, hp: LOOT.frostHp, until: this.tick + LOOT.frostLife * TICK_RATE };
    this.addWallCollider(w);
    p.frost--;
    this.events.push({ t: 'wall+', wall: w });
  }

  private addWallCollider(w: Omit<FrostWall, 'handle'>) {
    const col = this.phys.world.createCollider(
      RAPIER.ColliderDesc.cuboid(LOOT.frostWidth / 2, LOOT.frostHeight / 2, LOOT.frostThick / 2)
        .setTranslation(w.x, w.y, w.z).setRotation(quatY(w.rot)).setCollisionGroups(this.phys.worldGroups),
    );
    this.walls.set(w.id, { ...w, handle: col.handle });
  }

  removeWall(id: number, broken: boolean, announce = true) {
    const w = this.walls.get(id);
    if (!w) return;
    const col = this.phys.world.getCollider(w.handle);
    if (col) this.phys.world.removeCollider(col, false);
    this.walls.delete(id);
    if (announce) this.events.push({ t: 'wall-', id, broken });
  }

  private wallByHandle(handle: number) {
    for (const w of this.walls.values()) if (w.handle === handle) return w;
    return null;
  }

  /** Start the match (server, after the lobby countdown). */
  startMatch() {
    if (this.phase !== 'lobby') return;
    this.phase = 'live';
    this.startTick = this.tick;
    const plane = planePath(this.seed).at(0);
    for (const p of this.players.values()) {
      p.hp = MAX_HP;
      if (this.opts.skyDrop !== false) {
        Object.assign(p.move, { x: plane.x, y: plane.y, z: plane.z, vx: 0, vz: 0, vy: 0, air: 1, crouch: false, prone: false, ball: false, vault: 0 });
        syncCollider(this.phys, p.id, p.move);
      }
    }
  }

  /** Plane: carry passengers, let them jump (or throw them out at the end of the route). */
  private planeTick() {
    if (this.phase !== 'live') return;
    const path = planePath(this.seed);
    const t = this.liveTicks;
    const pos = path.at(t);
    const fx = Math.sin(path.yaw) * -1, fz = Math.cos(path.yaw) * -1;
    for (const p of this.players.values()) {
      if (!p.alive || p.move.air !== 1) continue;
      const list = p.queue ?? [p.lastInput];
      // jumping is only possible above the valley; everybody is out before the plane leaves it
      const overMap = Math.hypot(pos.x, pos.z) < MAP.playRadius - 12;
      const leaving = pos.k > 0.5 && !overMap;
      const wantJump = (overMap && (list.some((i) => i.buttons & BTN.JUMP) || (p.bot !== null && pos.k >= p.jumpAt))) || leaving || t >= path.ticks;
      // spread passengers a little so they don't all overlap on exit
      Object.assign(p.move, { x: pos.x, y: pos.y - 3, z: pos.z });
      if (wantJump && t > 15) {
        p.move.air = 2;
        p.move.vx = fx * SKY.planeSpeed * 0.4; p.move.vz = fz * SKY.planeSpeed * 0.4; p.move.vy = 0;
        const side = ((p.id * 0.618) % 1 - 0.5) * 4;
        p.move.x += Math.cos(path.yaw) * side; p.move.z -= Math.sin(path.yaw) * side;
        this.events.push({ t: 'jump', id: p.id });
      }
      syncCollider(this.phys, p.id, p.move);
    }
  }

  /** Ticks since the match started (zone timing). */
  get liveTicks() { return this.phase === 'lobby' ? 0 : this.tick - this.startTick; }

  /** Player disconnected: removed in the lobby, eliminated during the match. */
  removePlayer(id: number) {
    const p = this.players.get(id);
    if (!p) return;
    if (this.phase === 'lobby') {
      if (p.alive) this.aliveCount--;
      this.phys.removePlayer(id);
      this.players.delete(id);
    } else if (p.alive) this.kill(p, null, 'left', false);
    this.history.delete(id);
  }

  /** Hitbox of `id` as it was at (fractional) tick `t`, from the history ring buffer. */
  hitboxAt(id: number, t: number, out: Hitbox): Hitbox | null {
    const h = this.history.get(id);
    if (!h) return null;
    const t0 = Math.floor(t), f = t - t0;
    const a = h[((t0 % HISTORY) + HISTORY) % HISTORY], b = h[(((t0 + 1) % HISTORY) + HISTORY) % HISTORY];
    if (!a || a.tick !== t0) return null; // caller falls back to the current hitbox
    if (!b || b.tick !== t0 + 1 || f <= 0) { Object.assign(out, a); return out; }
    return lerpHitbox(a, b, f, out);
  }

  private recordHistory() {
    const t = this.tick + 1; // state after this step = what snapshot `t` will carry
    for (const p of this.players.values()) {
      if (!p.alive) continue;
      let h = this.history.get(p.id);
      if (!h) { h = new Array(HISTORY); this.history.set(p.id, h); }
      const slot = t % HISTORY;
      const hb = (h[slot] ??= {} as Hitbox);
      hitboxOf(p.move, hb);
      hb.tick = t;
    }
  }

  get alive() { return this.aliveCount; }

  addPlayer(id: number, name: string, bot: boolean): SimPlayer {
    const sp = this.map.spawns[(this.spawnIdx++ * 7) % this.map.spawns.length];
    const p = this.phys.findSpawn(sp.x, sp.z);
    this.phys.addPlayer(id, p.x, p.y, p.z);
    const move = newMoveState(p.x, p.y, p.z);
    move.yaw = Math.atan2(p.x, p.z); // face the valley centre
    const pl: SimPlayer = {
      id, name, skin: bot ? (id * 5 + 3) % SKIN_COUNT : 0, bot: bot ? new BotBrain(this.seed * 31 + id) : null, move,
      hp: MAX_HP, armor: this.opts.armed ? 50 : 0, alive: true,
      weapons: SLOT_DEFAULT.map((w) => ({ id: w, owned: !!this.opts.armed, mag: this.opts.armed ? WEAPONS[w].mag : 0, reserve: this.opts.armed ? WEAPONS[w].startReserve : 0 })),
      ammo: zeroAmmo(), scopes: 0,
      meds: 0, frost: this.opts.armed ? 2 : 0, scope: 0, healEndTick: 0, holstered: false, jumpAt: 0.12 + this.rnd() * 0.75,
      slot: 1, nextFireTick: 0, reloadEndTick: 0, prevButtons: 0, triggerArmed: false, kills: 0,
      lastInput: { seq: 0, mx: 0, mz: 0, yaw: move.yaw, pitch: 0, buttons: 0 }, queue: null, ackSeq: 0, place: 0, speed: 0,
    };
    this.players.set(id, pl);
    this.aliveCount++;
    return pl;
  }

  /** Queue the latest input for a human player (bots generate their own). */
  setInput(id: number, input: InputCmd) {
    const p = this.players.get(id);
    if (p) { p.lastInput = input; p.queue = null; }
  }

  /**
   * Server: exact inputs to process this tick. [] = none arrived (player waits, no guessing, so
   * client prediction stays exact); 2 = catch up after a late packet.
   */
  setInputs(id: number, inputs: InputCmd[]) {
    const p = this.players.get(id);
    if (p) { p.queue = inputs; if (inputs.length) p.lastInput = inputs[inputs.length - 1]; }
  }

  step() {
    this.events.length = 0;
    this.zone = this.opts.zone === false ? zoneAt(this.seed, 0) : zoneAt(this.seed, this.liveTicks);
    // 1) movement
    for (const p of this.players.values()) {
      if (!p.alive) continue;
      if (p.bot) p.lastInput = p.bot.think(p, this);
      const list = p.queue ?? [p.lastInput];
      if (!list.length) { p.speed = 0; continue; }
      const input = sanitizeInput(list[0]);
      const px = p.move.x, pz = p.move.z;
      simulateMove(this.phys, p.id, p.move, input, DT);
      p.speed = Math.hypot(p.move.x - px, p.move.z - pz) / DT;
      p.ackSeq = input.seq;
    }
    this.planeTick();
    this.phys.updateQueries();
    // 2) weapons (+ catch-up inputs, each one fully simulated)
    for (const p of this.players.values()) {
      if (!p.alive) continue;
      const list = p.queue ?? [p.lastInput];
      list.forEach((raw, i) => {
        const input = sanitizeInput(raw);
        if (i > 0) { simulateMove(this.phys, p.id, p.move, input, DT); p.ackSeq = input.seq; }
        if (p.alive) { if (this.phase === 'live' && !p.move.air) this.items(p, input); this.weaponTick(p, input); }
      });
      if (p.alive && this.phase === 'live' && !p.move.air) this.pickups(p);
    }
    // ice walls melt
    for (const w of this.walls.values()) if (this.tick >= w.until) this.removeWall(w.id, false);
    this.recordHistory(); // after catch-up moves: history == what snapshots report
    // 3) zone damage once per second (ignores armor)
    if (this.phase === 'live' && this.liveTicks % TICK_RATE === 0 && this.zone.dps > 0) {
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
    if (this.phase !== 'live' || p.move.air) { p.prevButtons = input.buttons; return; }
    weaponLogic(p, input, this.tick, (wid) => this.fire(p, input, wid), () => this.events.push({ t: 'reload', id: p.id }));
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
      const wallHit = this.phys.rayWorldHit(o.x, o.y, o.z, dx, dy, dz, w.range);
      let best = wallHit ? wallHit.t : w.range;
      const frost = wallHit ? this.wallByHandle(wallHit.handle) : null;
      let target: SimPlayer | null = null, head = false;
      for (const q of this.players.values()) {
        if (q === p || !q.alive || q.move.air === 1) continue;
        let hb: Hitbox | null = null;
        if (this.lagCompensation && !p.bot) {
          const now = this.tick + 1;
          const vt = Math.max(now - MAX_REWIND_TICKS, Math.min(now, input.viewTick ?? now));
          hb = this.hitboxAt(q.id, vt, this.hb);
        }
        if (!hb) hb = this.hitboxProvider(q, p, this.hb);
        const h = rayHitbox(o.x, o.y, o.z, dx, dy, dz, hb, best);
        if (h && h.t < best) { best = h.t; target = q; head = h.head; }
      }
      ends.push(o.x + dx * best, o.y + dy * best, o.z + dz * best);
      if (!target && frost) {
        frost.hp -= damageAt(w, best, false);
        if (frost.hp <= 0) this.removeWall(frost.id, true);
      }
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

  private kill(v: SimPlayer, killer: SimPlayer | null, weapon: WeaponId | 'zone' | 'left', head: boolean) {
    if (!v.alive) return;
    v.alive = false;
    v.hp = 0;
    v.place = this.aliveCount;
    this.aliveCount--;
    this.phys.removePlayer(v.id);
    v.healEndTick = 0;
    if (weapon !== 'left' || this.phase === 'live') this.dropInventory(v);
    if (killer) killer.kills++;
    this.events.push({ t: 'kill', killer: killer ? killer.id : -1, victim: v.id, weapon, head });
    if (this.aliveCount === 1 && this.winner === null && this.phase === 'live') {
      const w = [...this.players.values()].find((p) => p.alive)!;
      w.place = 1;
      this.winner = w.id;
      this.phase = 'ended';
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
