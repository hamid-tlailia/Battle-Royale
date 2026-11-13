import RAPIER from '@dimforge/rapier3d-compat';
import { BTN, MAP, PLAYER } from '../config/game';
import type { PhysicsWorld } from './physics';
import { terrainHeight } from '../map/mapgen';

export interface InputCmd {
  seq: number;
  /** strafe -1..1 (right positive) */
  mx: number;
  /** forward -1..1 (forward positive) */
  mz: number;
  /** body/camera orientation */
  yaw: number;
  pitch: number;
  buttons: number;
  /** precise shot direction (eye -> crosshair target). Server clamps it near yaw/pitch. */
  aimYaw?: number;
  aimPitch?: number;
  /** desired weapon slot */
  weapon?: number;
  /** drop an inventory item on the ground (see DROP codes) — one-shot */
  dropItem?: number;
  /** pick up this ground loot id (loot menu) */
  pick?: number;
  /** (fractional) server tick the client was rendering other players at — for lag compensation */
  viewTick?: number;
}

/** Collision shape of the character. */
export const SHAPE = {
  STAND: { hh: PLAYER.halfHeight, r: PLAYER.radius },
  CROUCH: { hh: PLAYER.crouchHalfHeight, r: PLAYER.radius },
  /** small sphere used while vaulting through windows / over obstacles */
  BALL: { hh: 0.001, r: 0.27 },
  /** lying down: low sphere at the torso */
  PRONE: { hh: 0.001, r: 0.3 },
} as const;

export interface MoveState {
  x: number; y: number; z: number;
  /** horizontal velocity (m/s) — accelerates smoothly instead of snapping to full speed */
  vx: number; vz: number;
  vy: number;
  grounded: boolean;
  crouch: boolean;
  /** true while using the small vault shape */
  ball: boolean;
  prone: boolean;
  yaw: number;
  pitch: number;
  /** remaining vault ticks (0 = not vaulting) */
  vault: number;
  /** 0 on foot, 1 inside the plane, 2 free-fall, 3 under parachute */
  air: number;
  vaultLift: number;
  vaultSide: number;
}

export const newMoveState = (x: number, y: number, z: number): MoveState => ({
  x, y, z, vx: 0, vz: 0, vy: 0, grounded: false, crouch: false, ball: false, prone: false, yaw: 0, pitch: 0, vault: 0, vaultLift: 0, vaultSide: 0, air: 0,
});

export const copyMoveState = (s: MoveState): MoveState => ({ ...s });

export const shapeOf = (s: MoveState) => (s.ball ? SHAPE.BALL : s.prone ? SHAPE.PRONE : s.crouch ? SHAPE.CROUCH : SHAPE.STAND);
/** Distance from capsule centre to feet. */
export const halfTotalOf = (s: MoveState) => { const sh = shapeOf(s); return sh.hh + sh.r; };
export const feetY = (s: MoveState) => s.y - halfTotalOf(s);

/** Sky-drop tuning (m/s, m). */
export const SKY = {
  planeAltitude: 115,
  planeSpeed: 24,
  fallSpeed: 16,
  diveSpeed: 30,
  /** horizontal glide while sky-diving / under the canopy: ~250 m of reach from the plane */
  fallSpeedH: 20,
  chuteSpeed: 5,
  chuteSpeedH: 17,
  chuteHeight: 60,
} as const;

const VAULT_LIFT_TICKS = 5;
const VAULT_FWD_TICKS = 9;
const VAULT_DIST = 1.9;
const scratch = { x: 0, y: 0, z: 0 };

/** Sanitise untrusted input (server uses this; client too so prediction matches). */
export function sanitizeInput(i: InputCmd): InputCmd {
  let mx = Number.isFinite(i.mx) ? i.mx : 0;
  let mz = Number.isFinite(i.mz) ? i.mz : 0;
  const len = Math.hypot(mx, mz);
  if (len > 1) { mx /= len; mz /= len; }
  const yaw = Number.isFinite(i.yaw) ? i.yaw % (Math.PI * 2) : 0;
  const clampP = (p: number) => Math.max(-PLAYER.maxPitch, Math.min(PLAYER.maxPitch, p));
  const pitch = Number.isFinite(i.pitch) ? clampP(i.pitch) : 0;
  // Aim direction may differ slightly from the body (third-person parallax) but not arbitrarily.
  let aimYaw = Number.isFinite(i.aimYaw) ? (i.aimYaw as number) : yaw;
  let dy = aimYaw - yaw;
  dy = Math.atan2(Math.sin(dy), Math.cos(dy));
  if (Math.abs(dy) > 0.35) aimYaw = yaw + Math.sign(dy) * 0.35;
  let aimPitch = Number.isFinite(i.aimPitch) ? clampP(i.aimPitch as number) : pitch;
  if (Math.abs(aimPitch - pitch) > 0.35) aimPitch = pitch + Math.sign(aimPitch - pitch) * 0.35;
  const weapon = Number.isFinite(i.weapon) ? Math.max(0, Math.min(4, (i.weapon as number) | 0)) : undefined; // 4 = holster
  const dropItem = Number.isFinite(i.dropItem) && (i.dropItem as number) > 0 ? (i.dropItem as number) | 0 : undefined;
  const pick = Number.isFinite(i.pick) && (i.pick as number) > 0 ? (i.pick as number) >>> 0 : undefined;
  const viewTick = Number.isFinite(i.viewTick) ? (i.viewTick as number) : undefined;
  return { seq: i.seq | 0, mx, mz, yaw, pitch, buttons: i.buttons & 0xffff, aimYaw, aimPitch, weapon, viewTick, dropItem, pick };
}

type ShapeName = 'stand' | 'crouch' | 'ball' | 'prone';
const SHAPES: Record<ShapeName, { hh: number; r: number }> = { stand: SHAPE.STAND, crouch: SHAPE.CROUCH, ball: SHAPE.BALL, prone: SHAPE.PRONE };
const shapeName = (s: MoveState): ShapeName => (s.ball ? 'ball' : s.prone ? 'prone' : s.crouch ? 'crouch' : 'stand');

function setShape(phys: PhysicsWorld, col: RAPIER.Collider, s: MoveState, shape: ShapeName) {
  const feet = feetY(s);
  s.crouch = shape === 'crouch';
  s.ball = shape === 'ball';
  s.prone = shape === 'prone';
  const sh = shapeOf(s);
  col.setHalfHeight(sh.hh);
  col.setRadius(sh.r);
  s.y = feet + sh.hh + sh.r;
  void phys;
}

/** Can a shape stand with its feet at `feet`? */
function fits(phys: PhysicsWorld, col: RAPIER.Collider, s: MoveState, sh: { hh: number; r: number }, feet: number) {
  return !phys.shapeBlocked(s.x, feet + sh.hh + sh.r, s.z, sh.hh, sh.r, col);
}

/**
 * Looks for a gap in front of the player that the small vault sphere can pass through
 * (window, low wall, crate, car bonnet). Returns lift height (centre above feet) and lateral offset.
 */
function findVault(phys: PhysicsWorld, col: RAPIER.Collider, s: MoveState): { h: number; side: number } | null {
  const fx = -Math.sin(s.yaw), fz = -Math.cos(s.yaw);
  const rx = Math.cos(s.yaw), rz = -Math.sin(s.yaw);
  const feet = feetY(s);
  // Must be something directly in front, otherwise it's a normal jump.
  // (a wall, crate, car or window ledge, never the hillside itself)
  const near = (h: number) => phys.rayObstacle(s.x, feet + h, s.z, fx, 0, fz, PLAYER.radius + 0.9);
  if (near(0.35) === null && near(1.0) === null && near(1.6) === null) return null;
  const r = SHAPE.BALL.r;
  for (let h = 0.5; h <= 2.3; h += 0.15) {
    for (const side of [0, 0.12, -0.12, 0.24, -0.24, 0.36, -0.36]) {
      const cx = s.x + rx * side, cy = feet + h, cz = s.z + rz * side;
      // vertical clearance from current spot up to the gap height
      if (phys.sphereCast(s.x, feet + r + 0.05, s.z, 0, 1, 0, Math.max(0, cy - (feet + r + 0.05)), r * 0.95, col) !== null) continue;
      if (phys.shapeBlocked(cx, cy, cz, SHAPE.BALL.hh, r * 0.95, col)) continue;
      if (phys.sphereCast(cx, cy, cz, fx, 0, fz, VAULT_DIST, r * 0.95, col) !== null) continue;
      return { h, side };
    }
  }
  return null;
}

/**
 * Deterministic single-tick movement. Identical code runs on server (authoritative)
 * and client (prediction / reconciliation replay). Mutates `s` and moves the player's collider.
 */
export function simulateMove(phys: PhysicsWorld, id: number, s: MoveState, raw: InputCmd, dt: number): void {
  const col = phys.players.get(id);
  if (!col) return;
  const input = sanitizeInput(raw);
  s.yaw = input.yaw;
  s.pitch = input.pitch;
  col.setTranslation({ x: s.x, y: s.y, z: s.z });
  const sin = Math.sin(s.yaw), cos = Math.cos(s.yaw);

  // ---------------- Plane / free-fall / parachute ----------------
  if (s.air === 1) return; // in the plane: the server moves you along the flight path
  if (s.air >= 2) {
    const above = s.y - phys.groundHeight(s.x, s.z);
    if (s.air === 2 && above < SKY.chuteHeight) s.air = 3; // parachute opens automatically
    // JUMP held while in the air = drop straight down (no glide)
    const straight = (input.buttons & BTN.JUMP) !== 0;
    const hs = straight ? 0 : s.air === 2 ? SKY.fallSpeedH : SKY.chuteSpeedH;
    const tvx = (-sin * input.mz + cos * input.mx) * hs, tvz = (-cos * input.mz - sin * input.mx) * hs;
    const k = Math.min(1, dt * 3);
    s.vx += (tvx - s.vx) * k; s.vz += (tvz - s.vz) * k;
    const dive = straight; // straight drop falls fast; otherwise glide
    // steering always comes down too: moving under the canopy sinks faster, looking down dives steeper
    const steer = Math.min(1, Math.hypot(input.mx, input.mz)), down = Math.max(0, -input.pitch);
    const targetVy = s.air === 2 ? (dive ? -SKY.diveSpeed : -(SKY.fallSpeed + down * 8)) : -(SKY.chuteSpeed + steer * 1.5 + down * 3);
    s.vy += (targetVy - s.vy) * Math.min(1, dt * 2.5);
    scratch.x = s.vx * dt; scratch.y = s.vy * dt; scratch.z = s.vz * dt;
    phys.controller.computeColliderMovement(col, scratch);
    const mv = phys.controller.computedMovement();
    s.x += mv.x; s.y += mv.y; s.z += mv.z;
    // stay above the valley while gliding
    const r = Math.hypot(s.x, s.z), lim = MAP.playRadius - 4;
    if (r > lim) { s.x *= lim / r; s.z *= lim / r; }
    if (phys.controller.computedGrounded() || above < 0.3) { s.air = 0; s.vy = 0; s.grounded = true; s.vx *= 0.3; s.vz *= 0.3; }
    const floor = phys.groundHeight(s.x, s.z) + halfTotalOf(s);
    if (s.y < floor - 0.5) { s.y = floor + 0.05; s.vy = 0; s.air = 0; }
    col.setTranslation({ x: s.x, y: s.y, z: s.z });
    return;
  }

  // ---------------- Vault in progress ----------------
  if (s.vault > 0) {
    const step = VAULT_LIFT_TICKS + VAULT_FWD_TICKS - s.vault;
    if (step < VAULT_LIFT_TICKS) {
      scratch.x = (cos * s.vaultSide) / VAULT_LIFT_TICKS;
      scratch.y = s.vaultLift / VAULT_LIFT_TICKS;
      scratch.z = (-sin * s.vaultSide) / VAULT_LIFT_TICKS;
    } else {
      scratch.x = (-sin * VAULT_DIST) / VAULT_FWD_TICKS;
      scratch.y = 0;
      scratch.z = (-cos * VAULT_DIST) / VAULT_FWD_TICKS;
    }
    phys.controller.computeColliderMovement(col, scratch);
    const mv = phys.controller.computedMovement();
    s.x += mv.x; s.y += mv.y; s.z += mv.z;
    s.vy = 0; s.vx = 0; s.vz = 0;
    s.grounded = false;
    s.vault--;
    col.setTranslation({ x: s.x, y: s.y, z: s.z });
    return;
  }

  // ---------------- Shape changes: stand / crouch / prone (and recovering from a vault) ----------------
  const wantCrouch = (input.buttons & BTN.CROUCH) !== 0;
  const wantProne = (input.buttons & BTN.PRONE) !== 0;
  const target: ShapeName = wantProne ? 'prone' : wantCrouch ? 'crouch' : 'stand';
  const cur = shapeName(s);
  if (cur !== target) {
    const feet = feetY(s);
    // growing needs head room; if standing is blocked, settle for crouching
    for (const cand of target === 'stand' ? (['stand', 'crouch'] as ShapeName[]) : [target]) {
      if (cand === cur) break;
      if (fits(phys, col, s, SHAPES[cand], feet)) { setShape(phys, col, s, cand); break; }
    }
  }
  col.setTranslation({ x: s.x, y: s.y, z: s.z });

  // ---------------- Jump or vault ----------------
  // on hillsides the controller's ground contact flickers: feet within 30 cm of the terrain can jump too
  const nearGround = s.grounded || (s.vy <= 0.5 && feetY(s) - phys.groundHeight(s.x, s.z) < 0.3);
  if (nearGround && (input.buttons & BTN.JUMP) && !s.crouch && !s.ball && !s.prone) {
    const v = findVault(phys, col, s);
    if (v) {
      const f = feetY(s);
      setShape(phys, col, s, 'ball');
      s.y = f + SHAPE.BALL.r + SHAPE.BALL.hh;
      col.setTranslation({ x: s.x, y: s.y, z: s.z });
      s.vault = VAULT_LIFT_TICKS + VAULT_FWD_TICKS;
      s.vaultLift = v.h - (s.y - f);
      s.vaultSide = v.side;
      s.vy = 0;
      s.grounded = false;
      return;
    }
    s.vy = PLAYER.jumpVelocity;
    s.grounded = false;
  }

  // ---------------- Walk ----------------
  const sprint = (input.buttons & BTN.SPRINT) !== 0 && input.mz > 0.5 && !s.crouch && !s.ball && !s.prone;
  const speed = s.prone ? PLAYER.proneSpeed : s.crouch || s.ball ? PLAYER.crouchSpeed : sprint ? PLAYER.sprintSpeed : PLAYER.walkSpeed;
  // forward = (-sin, -cos), right = (cos, -sin)  (three.js camera convention)
  const tx = (-sin * input.mz + cos * input.mx) * speed;
  const tz = (-cos * input.mz - sin * input.mx) * speed;
  // accelerate toward the target velocity (fast on the ground, weak in the air)
  const accel = (s.grounded ? PLAYER.groundAccel : PLAYER.airAccel) * dt;
  const ddx = tx - s.vx, ddz = tz - s.vz, dl = Math.hypot(ddx, ddz);
  if (dl <= accel) { s.vx = tx; s.vz = tz; } else { s.vx += (ddx / dl) * accel; s.vz += (ddz / dl) * accel; }
  s.vy = Math.max(-PLAYER.maxFallSpeed, s.vy + PLAYER.gravity * dt);

  scratch.x = s.vx * dt; scratch.y = s.vy * dt; scratch.z = s.vz * dt;
  phys.controller.computeColliderMovement(col, scratch);
  let mv = phys.controller.computedMovement();
  let grounded = phys.controller.computedGrounded();
  // climb assist: walking up a steep hillside the controller loses most of the horizontal motion;
  // lift a little and retry so you keep your pace on any slope you can stand on (real cliffs still block)
  const want = Math.hypot(scratch.x, scratch.z), got = Math.hypot(mv.x, mv.z);
  // only where the terrain itself rises ahead (hills), never against walls / props on flat ground
  const ax = s.x + (scratch.x / Math.max(want, 1e-6)) * 0.7, az = s.z + (scratch.z / Math.max(want, 1e-6)) * 0.7;
  const rising = want > 0.02 && terrainHeight(phys.map, ax, az) - terrainHeight(phys.map, s.x, s.z) > 0.12;
  if (s.vault === 0 && rising && (grounded || s.grounded) && got < want * 0.7) {
    const lift = (want - got) * 1.3;
    const tx0 = mv.x, ty0 = mv.y, tz0 = mv.z, g0 = grounded;
    phys.controller.computeColliderMovement(col, { x: scratch.x, y: Math.max(scratch.y, 0) + lift, z: scratch.z });
    const m2 = phys.controller.computedMovement();
    const got2 = Math.hypot(m2.x, m2.z);
    if (got2 > got * 1.15 && m2.y > -0.01) { mv = { x: m2.x, y: m2.y, z: m2.z }; grounded = true; }
    else { mv = { x: tx0, y: ty0, z: tz0 }; grounded = g0; }
  }
  // on bumpy hillsides the controller's contact flickers: feet within a few cm of the ground count as grounded
  // (walking uphill keeps full ground acceleration and jumping works on slopes)
  if (!grounded && s.vy <= 0.5) {
    const feet = s.y + mv.y - halfTotalOf(s);
    if (feet - phys.groundHeight(s.x + mv.x, s.z + mv.z) < 0.18) grounded = true;
  }
  if (s.vy > 0 && mv.y < scratch.y * 0.5) s.vy = 0; // hit ceiling
  s.grounded = grounded;
  if (grounded && s.vy < 0) s.vy = 0;
  s.x += mv.x; s.y += mv.y; s.z += mv.z;
  // keep the velocity we actually achieved (no speed build-up against walls)
  s.vx = mv.x / dt; s.vz = mv.z / dt;

  // ---------------- Safety net: never below the terrain ----------------
  const floor = phys.groundHeight(s.x, s.z) + halfTotalOf(s);
  if (s.y < floor - 0.5) { s.y = floor + 0.05; s.vy = 0; }

  col.setTranslation({ x: s.x, y: s.y, z: s.z });
}

/** Eye / camera pivot height above the capsule centre. */
export const eyeOffset = (s: MoveState) => (s.ball ? 0.1 : s.prone ? 0.08 : s.crouch ? PLAYER.eyeAboveCenter - (PLAYER.halfHeight - PLAYER.crouchHalfHeight) : PLAYER.eyeAboveCenter);

/** Make the physics collider match a (server-provided) movement state: shape + position. */
export function syncCollider(phys: PhysicsWorld, id: number, s: MoveState) {
  const col = phys.players.get(id);
  if (!col) return;
  const sh = shapeOf(s);
  col.setHalfHeight(sh.hh);
  col.setRadius(sh.r);
  col.setTranslation({ x: s.x, y: s.y, z: s.z });
}
