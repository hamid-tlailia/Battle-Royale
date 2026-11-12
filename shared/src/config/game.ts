export const GAME_TITLE = 'LAST WAR';

/** Server simulation rate. Client runs the same fixed step for prediction. */
export const TICK_RATE = 30;
export const DT = 1 / TICK_RATE;

export const PLAYER = {
  radius: 0.4,
  halfHeight: 0.5,        // capsule segment half height (standing). Total height = 2*(hh+r) = 1.8m
  crouchHalfHeight: 0.2,  // total crouched height = 1.2m
  walkSpeed: 5.5,
  crouchSpeed: 2.6,
  jumpVelocity: 7.2,
  gravity: -22,
  maxFallSpeed: 40,
  eyeAboveCenter: 0.6,    // camera pivot above capsule center (standing)
  maxPitch: 1.35,
  controllerOffset: 0.02,
  maxSlopeDeg: 50,
  autostepHeight: 0.45,
} as const;

export const MAP = {
  seed: 20261112,
  size: 300,       // metres, square
  cells: 100,      // terrain grid cells per side
  playRadius: 125, // boundary of walkable valley
  snowHeight: 34,
} as const;

/** Collision groups: membership in high 16 bits, filter in low 16 bits. */
export const GROUP_WORLD = 0x0001;
export const GROUP_PLAYER = 0x0002;
export const groups = (member: number, filter: number) => (member << 16) | filter;

export const BTN = {
  JUMP: 1,
  CROUCH: 2,
  FIRE: 4,
  SCOPE: 8,
  RELOAD: 16,
} as const;
