import type { InputCmd, MoveState } from '../sim/movement';
import type { SimPlayer } from '../sim/game';
import { WEAPON_IDS } from '../config/weapons';

/**
 * Network protocol. High-frequency data is binary (inputs, snapshots); rare data is JSON.
 *
 *  client -> server   MSG.INPUT  (bytes)  last 3 inputs (redundancy against loss/late packets)
 *                     MSG.PING   (json)   { t }
 *  server -> client   MSG.SNAP   (bytes)  every tick (30 Hz): my full state + compact others
 *                     MSG.EVENTS (json)   shots / hits / kills / win of that tick
 *                     MSG.WELCOME(json)   { id, seed, tick, map }
 *                     MSG.ROSTER (json)   [{ id, name }]
 *                     MSG.LOBBY  (json)   { phase, countdown, humans, needed }
 *                     MSG.PONG   (json)   { t }
 */
export const MSG = { INPUT: 'i', SNAP: 's', EVENTS: 'e', WELCOME: 'w', ROSTER: 'r', LOBBY: 'l', PING: 'p', PONG: 'q', START: 'g', WORLD: 'o' } as const;

export interface RosterEntry { id: number; name: string; bot: boolean; skin: number }
export interface LobbyInfo { phase: string; countdown: number; humans: number; max: number; private: boolean; hostId: number }

const PITCH_K = 10000;

// ------------------------------------------------------------------ inputs
const INPUT_BYTES = 4 + 1 + 1 + 4 + 2 + 4 + 2 + 2 + 1 + 4 + 1;

export function encodeInputs(inputs: InputCmd[]): Uint8Array {
  const n = Math.min(inputs.length, 8);
  const buf = new ArrayBuffer(1 + n * INPUT_BYTES);
  const v = new DataView(buf);
  v.setUint8(0, n);
  let o = 1;
  for (let k = inputs.length - n; k < inputs.length; k++) {
    const i = inputs[k];
    v.setUint32(o, i.seq >>> 0); o += 4;
    v.setInt8(o, Math.round(i.mx * 127)); o += 1;
    v.setInt8(o, Math.round(i.mz * 127)); o += 1;
    v.setFloat32(o, i.yaw); o += 4;
    v.setInt16(o, Math.round(i.pitch * PITCH_K)); o += 2;
    v.setFloat32(o, i.aimYaw ?? i.yaw); o += 4;
    v.setInt16(o, Math.round((i.aimPitch ?? i.pitch) * PITCH_K)); o += 2;
    v.setUint16(o, i.buttons & 0xffff); o += 2;
    v.setUint8(o, i.weapon ?? 255); o += 1;
    v.setFloat32(o, i.viewTick ?? -1); o += 4;
    v.setUint8(o, i.dropItem ?? 0); o += 1;
  }
  return new Uint8Array(buf);
}

/** Round an input exactly as the wire format will, so client prediction uses the same numbers as the server. */
export function quantizeInput(i: InputCmd): InputCmd {
  return {
    ...i,
    mx: Math.round(i.mx * 127) / 127, mz: Math.round(i.mz * 127) / 127,
    yaw: Math.fround(i.yaw), pitch: Math.round(i.pitch * PITCH_K) / PITCH_K,
    aimYaw: Math.fround(i.aimYaw ?? i.yaw), aimPitch: Math.round((i.aimPitch ?? i.pitch) * PITCH_K) / PITCH_K,
    viewTick: i.viewTick === undefined ? undefined : Math.fround(i.viewTick),
    dropItem: i.dropItem ? i.dropItem & 0xff : undefined,
  };
}

export function decodeInputs(bytes: Uint8Array): InputCmd[] {
  const v = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const n = v.getUint8(0);
  if (bytes.byteLength < 1 + n * INPUT_BYTES) throw new Error('bad input packet');
  const out: InputCmd[] = [];
  let o = 1;
  for (let k = 0; k < n; k++) {
    const seq = v.getUint32(o); o += 4;
    const mx = v.getInt8(o) / 127; o += 1;
    const mz = v.getInt8(o) / 127; o += 1;
    const yaw = v.getFloat32(o); o += 4;
    const pitch = v.getInt16(o) / PITCH_K; o += 2;
    const aimYaw = v.getFloat32(o); o += 4;
    const aimPitch = v.getInt16(o) / PITCH_K; o += 2;
    const buttons = v.getUint16(o); o += 2;
    const w = v.getUint8(o); o += 1;
    const vt = v.getFloat32(o); o += 4;
    const dropItem = v.getUint8(o); o += 1;
    out.push({ seq, mx, mz, yaw, pitch, aimYaw, aimPitch, buttons, weapon: w === 255 ? undefined : w, viewTick: vt < 0 ? undefined : vt, dropItem: dropItem || undefined });
  }
  return out;
}

// ------------------------------------------------------------------ snapshots
export interface RemoteState {
  id: number;
  x: number; y: number; z: number;
  yaw: number; pitch: number;
  vx: number; vz: number;
  alive: boolean; crouch: boolean; grounded: boolean; ball: boolean; prone: boolean; air: number;
  /** 0-3 weapon in hand, 4 holstered, 5 unarmed */
  slot: number;
  hp: number; armor: number;
}

export interface SelfState {
  move: MoveState;
  alive: boolean;
  hp: number; armor: number; slot: number; holstered: boolean;
  weapons: { owned: boolean; mag: number; reserve: number }[];
  meds: number; frost: number; scope: number; healEndTick: number;
  nextFireTick: number; reloadEndTick: number;
  prevButtons: number; triggerArmed: boolean;
  kills: number; place: number;
}

export interface Snapshot {
  tick: number;
  phase: 'lobby' | 'live' | 'ended';
  startTick: number;
  alive: number;
  ack: number;
  self: SelfState | null;
  others: RemoteState[];
}

const PHASES = ['lobby', 'live', 'ended'] as const;
const SELF_BYTES = 4 * 3 + 4 * 3 + 1 + 1 + 4 + 4 + 4 + 4 + 1 + 1 + 1 + 4 * 3 + 4 + 4 + 2 + 1 + 1 + 1 + 1 + 1 + 1 + 1 + 4;
const OTHER_BYTES = 1 + 12 + 2 + 2 + 2 + 2 + 1 + 1 + 2;

export function encodeSnapshot(tick: number, phase: Snapshot['phase'], startTick: number, alive: number, me: SimPlayer | null, players: Iterable<SimPlayer>): Uint8Array {
  const list = [...players].filter((p) => p !== me);
  const buf = new ArrayBuffer(4 + 1 + 4 + 1 + 4 + 1 + 1 + (me ? SELF_BYTES : 0) + list.length * OTHER_BYTES);
  const v = new DataView(buf);
  let o = 0;
  v.setUint32(o, tick); o += 4;
  v.setUint8(o, PHASES.indexOf(phase)); o += 1;
  v.setUint32(o, startTick); o += 4;
  v.setUint8(o, alive); o += 1;
  v.setUint32(o, me ? me.ackSeq >>> 0 : 0); o += 4;
  v.setUint8(o, me ? 1 : 0); o += 1;
  v.setUint8(o, list.length); o += 1;
  if (me) {
    const m = me.move;
    for (const f of [m.x, m.y, m.z, m.vx, m.vz, m.vy]) { v.setFloat32(o, f); o += 4; }
    v.setUint8(o, (m.grounded ? 1 : 0) | (m.crouch ? 2 : 0) | (m.ball ? 4 : 0) | (me.alive ? 8 : 0) | (m.prone ? 16 : 0) | ((m.air & 3) << 5)); o += 1;
    v.setUint8(o, m.vault); o += 1;
    v.setFloat32(o, m.vaultLift); o += 4;
    v.setFloat32(o, m.vaultSide); o += 4;
    v.setFloat32(o, m.yaw); o += 4;
    v.setFloat32(o, m.pitch); o += 4;
    v.setUint8(o, Math.max(0, Math.min(255, Math.ceil(me.hp)))); o += 1;
    v.setUint8(o, Math.max(0, Math.min(255, Math.ceil(me.armor)))); o += 1;
    v.setUint8(o, me.slot); o += 1;
    for (let k = 0; k < 4; k++) { const w = me.weapons[k]; v.setUint8(o, w.mag); v.setUint16(o + 1, w.reserve); o += 3; }
    v.setUint32(o, me.nextFireTick); o += 4;
    v.setUint32(o, me.reloadEndTick); o += 4;
    v.setUint16(o, me.prevButtons); o += 2;
    v.setUint8(o, (me.triggerArmed ? 1 : 0) | (me.holstered ? 2 : 0)); o += 1;
    v.setUint8(o, Math.min(255, me.kills)); o += 1;
    v.setUint8(o, me.place); o += 1;
    v.setUint8(o, me.weapons.reduce((m, w, i) => m | (w.owned ? 1 << i : 0), 0)); o += 1;
    v.setUint8(o, me.meds); o += 1;
    v.setUint8(o, me.frost); o += 1;
    v.setUint8(o, me.scope); o += 1;
    v.setUint32(o, me.healEndTick); o += 4;
  }
  for (const p of list) {
    const m = p.move;
    v.setUint8(o, p.id); o += 1;
    v.setFloat32(o, m.x); v.setFloat32(o + 4, m.y); v.setFloat32(o + 8, m.z); o += 12;
    v.setUint16(o, Math.round((((m.yaw % (Math.PI * 2)) + Math.PI * 2) % (Math.PI * 2)) / (Math.PI * 2) * 65535)); o += 2;
    v.setInt16(o, Math.round(m.pitch * PITCH_K)); o += 2;
    v.setInt16(o, Math.round(Math.max(-327, Math.min(327, m.vx)) * 100)); o += 2;
    v.setInt16(o, Math.round(Math.max(-327, Math.min(327, m.vz)) * 100)); o += 2;
    v.setUint8(o, (p.alive ? 1 : 0) | (m.crouch ? 2 : 0) | (m.grounded ? 4 : 0) | (m.ball ? 8 : 0) | (m.prone ? 16 : 0) | ((m.air & 3) << 5)); o += 1;
    v.setUint8(o, p.holstered ? 4 : p.weapons[p.slot]?.owned ? p.slot : 5); o += 1;
    v.setUint8(o, Math.max(0, Math.min(255, Math.ceil(p.hp)))); o += 1;
    v.setUint8(o, Math.max(0, Math.min(255, Math.ceil(p.armor)))); o += 1;
  }
  return new Uint8Array(buf);
}

export function decodeSnapshot(bytes: Uint8Array): Snapshot {
  const v = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  let o = 0;
  const tick = v.getUint32(o); o += 4;
  const phase = PHASES[v.getUint8(o)] ?? 'live'; o += 1;
  const startTick = v.getUint32(o); o += 4;
  const alive = v.getUint8(o); o += 1;
  const ack = v.getUint32(o); o += 4;
  const hasSelf = v.getUint8(o) === 1; o += 1;
  const n = v.getUint8(o); o += 1;
  let self: SelfState | null = null;
  if (hasSelf) {
    const f = () => { const x = v.getFloat32(o); o += 4; return x; };
    const x = f(), y = f(), z = f(), vx = f(), vz = f(), vy = f();
    const flags = v.getUint8(o); o += 1;
    const vault = v.getUint8(o); o += 1;
    const vaultLift = f(), vaultSide = f(), yaw = f(), pitch = f();
    const hp = v.getUint8(o), armor = v.getUint8(o + 1), slot = v.getUint8(o + 2); o += 3;
    const weapons = [];
    for (let k = 0; k < 4; k++) { weapons.push({ owned: false, mag: v.getUint8(o), reserve: v.getUint16(o + 1) }); o += 3; }
    const nextFireTick = v.getUint32(o); o += 4;
    const reloadEndTick = v.getUint32(o); o += 4;
    const prevButtons = v.getUint16(o); o += 2;
    const tb = v.getUint8(o); o += 1;
    const triggerArmed = !!(tb & 1), holstered = !!(tb & 2);
    const kills = v.getUint8(o); o += 1;
    const place = v.getUint8(o); o += 1;
    const owned = v.getUint8(o); o += 1;
    const meds = v.getUint8(o), frost = v.getUint8(o + 1), scope = v.getUint8(o + 2); o += 3;
    const healEndTick = v.getUint32(o); o += 4;
    weapons.forEach((w, i) => { (w as { owned: boolean }).owned = !!(owned & (1 << i)); });
    self = {
      move: { x, y, z, vx, vz, vy, grounded: !!(flags & 1), crouch: !!(flags & 2), ball: !!(flags & 4), prone: !!(flags & 16), air: (flags >> 5) & 3, yaw, pitch, vault, vaultLift, vaultSide },
      alive: !!(flags & 8), hp, armor, slot, holstered, weapons, meds, frost, scope, healEndTick, nextFireTick, reloadEndTick, prevButtons, triggerArmed, kills, place,
    };
  }
  const others: RemoteState[] = [];
  for (let k = 0; k < n; k++) {
    const id = v.getUint8(o); o += 1;
    const x = v.getFloat32(o), y = v.getFloat32(o + 4), z = v.getFloat32(o + 8); o += 12;
    const yaw = (v.getUint16(o) / 65535) * Math.PI * 2; o += 2;
    const pitch = v.getInt16(o) / PITCH_K; o += 2;
    const vx = v.getInt16(o) / 100; o += 2;
    const vz = v.getInt16(o) / 100; o += 2;
    const flags = v.getUint8(o); o += 1;
    const slot = v.getUint8(o); o += 1;
    const hp = v.getUint8(o), armor = v.getUint8(o + 1); o += 2;
    others.push({ id, x, y, z, yaw, pitch, vx, vz, alive: !!(flags & 1), crouch: !!(flags & 2), grounded: !!(flags & 4), ball: !!(flags & 8), prone: !!(flags & 16), air: (flags >> 5) & 3, slot, hp, armor });
  }
  return { tick, phase, startTick, alive, ack, self, others };
}

/** Apply a decoded self-state onto a SimPlayer (client reconciliation). */
export function applySelf(p: SimPlayer, s: SelfState) {
  Object.assign(p.move, s.move);
  p.alive = s.alive; p.hp = s.hp; p.armor = s.armor; p.slot = s.slot; p.holstered = s.holstered;
  s.weapons.forEach((w, i) => { p.weapons[i].owned = w.owned; p.weapons[i].mag = w.mag; p.weapons[i].reserve = w.reserve; p.weapons[i].id = WEAPON_IDS[i]; });
  p.meds = s.meds; p.frost = s.frost; p.scope = s.scope; p.healEndTick = s.healEndTick;
  p.nextFireTick = s.nextFireTick; p.reloadEndTick = s.reloadEndTick;
  p.prevButtons = s.prevButtons; p.triggerArmed = s.triggerArmed;
  p.kills = s.kills; p.place = s.place;
}

/** Round event coordinates to keep JSON small. */
export function compactEvents<T extends object>(events: T[]): T[] {
  return JSON.parse(JSON.stringify(events, (_k, val) => (typeof val === 'number' && !Number.isInteger(val) ? Math.round(val * 100) / 100 : val)));
}
