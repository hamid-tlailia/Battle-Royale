import { BTN, PLAYER } from '../config/game';
import type { PhysicsWorld } from './physics';

export interface InputCmd {
  seq: number;
  /** strafe -1..1 (right positive) */
  mx: number;
  /** forward -1..1 (forward positive) */
  mz: number;
  yaw: number;
  pitch: number;
  buttons: number;
}

export interface MoveState {
  x: number; y: number; z: number;
  vy: number;
  grounded: boolean;
  crouch: boolean;
  yaw: number;
  pitch: number;
}

export const newMoveState = (x: number, y: number, z: number): MoveState => ({
  x, y, z, vy: 0, grounded: false, crouch: false, yaw: 0, pitch: 0,
});

export const copyMoveState = (s: MoveState): MoveState => ({ ...s });

const CROUCH_DROP = PLAYER.halfHeight - PLAYER.crouchHalfHeight;
const scratch = { x: 0, y: 0, z: 0 };

/** Sanitise untrusted input (server uses this; client too so prediction matches). */
export function sanitizeInput(i: InputCmd): InputCmd {
  let mx = Number.isFinite(i.mx) ? i.mx : 0;
  let mz = Number.isFinite(i.mz) ? i.mz : 0;
  const len = Math.hypot(mx, mz);
  if (len > 1) { mx /= len; mz /= len; }
  const yaw = Number.isFinite(i.yaw) ? i.yaw % (Math.PI * 2) : 0;
  const pitch = Number.isFinite(i.pitch) ? Math.max(-PLAYER.maxPitch, Math.min(PLAYER.maxPitch, i.pitch)) : 0;
  return { seq: i.seq | 0, mx, mz, yaw, pitch, buttons: i.buttons & 0xff };
}

/**
 * Deterministic single-tick movement. Identical code runs on server (authoritative)
 * and client (prediction / reconciliation replay).
 * Mutates `s` and moves the player's collider.
 */
export function simulateMove(phys: PhysicsWorld, id: number, s: MoveState, raw: InputCmd, dt: number): void {
  const col = phys.players.get(id);
  if (!col) return;
  const input = sanitizeInput(raw);
  s.yaw = input.yaw;
  s.pitch = input.pitch;

  // ---- Crouch (keep feet planted) ----
  const wantCrouch = (input.buttons & BTN.CROUCH) !== 0;
  if (wantCrouch && !s.crouch) {
    s.crouch = true;
    col.setHalfHeight(PLAYER.crouchHalfHeight);
    s.y -= CROUCH_DROP;
  } else if (!wantCrouch && s.crouch) {
    if (!phys.capsuleBlocked(s.x, s.y + CROUCH_DROP, s.z, PLAYER.halfHeight, col)) {
      s.crouch = false;
      col.setHalfHeight(PLAYER.halfHeight);
      s.y += CROUCH_DROP;
    }
  }
  col.setTranslation({ x: s.x, y: s.y, z: s.z });

  // ---- Horizontal ----
  const speed = s.crouch ? PLAYER.crouchSpeed : PLAYER.walkSpeed;
  const sin = Math.sin(s.yaw), cos = Math.cos(s.yaw);
  // forward = (-sin, -cos), right = (cos, -sin)  (three.js camera convention)
  const vx = (-sin * input.mz + cos * input.mx) * speed;
  const vz = (-cos * input.mz - sin * input.mx) * speed;

  // ---- Vertical ----
  if (s.grounded && (input.buttons & BTN.JUMP) && !s.crouch) {
    s.vy = PLAYER.jumpVelocity;
    s.grounded = false;
  }
  s.vy = Math.max(-PLAYER.maxFallSpeed, s.vy + PLAYER.gravity * dt);

  scratch.x = vx * dt; scratch.y = s.vy * dt; scratch.z = vz * dt;
  phys.controller.computeColliderMovement(col, scratch, undefined, undefined);
  const mv = phys.controller.computedMovement();
  const grounded = phys.controller.computedGrounded();

  // Hit ceiling while rising
  if (s.vy > 0 && mv.y < scratch.y * 0.5) s.vy = 0;
  s.grounded = grounded;
  if (grounded && s.vy < 0) s.vy = 0;

  s.x += mv.x; s.y += mv.y; s.z += mv.z;

  // ---- Safety net: never below the terrain ----
  const halfTotal = (s.crouch ? PLAYER.crouchHalfHeight : PLAYER.halfHeight) + PLAYER.radius;
  const floor = phys.groundHeight(s.x, s.z) + halfTotal;
  if (s.y < floor - 0.5) { s.y = floor + 0.05; s.vy = 0; }

  col.setTranslation({ x: s.x, y: s.y, z: s.z });
}

/** Eye / camera pivot height above the capsule centre. */
export const eyeOffset = (s: MoveState) => PLAYER.eyeAboveCenter - (s.crouch ? CROUCH_DROP : 0);
