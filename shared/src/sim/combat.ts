import { feetY, type MoveState } from './movement';

/**
 * Hitboxes: a capsule for the body and a sphere for the head, derived from the movement state.
 * These are what bullets hit (not the physics capsule) and what the server rewinds for lag compensation.
 */
export interface Hitbox {
  /** body capsule segment (bottom & top centres) + radius */
  ax: number; ay: number; az: number;
  bx: number; by: number; bz: number;
  br: number;
  /** head sphere */
  hx: number; hy: number; hz: number;
  hr: number;
  /** history: tick this hitbox belongs to */
  tick?: number;
}

export function hitboxOf(s: MoveState, out: Hitbox = {} as Hitbox): Hitbox {
  const f = feetY(s);
  // head is slightly forward of the body centre (the model leans into the rifle)
  const fx = -Math.sin(s.yaw) * 0.06, fz = -Math.cos(s.yaw) * 0.06;
  let bodyLo: number, bodyHi: number, head: number;
  if (s.prone) {
    // lying along the facing direction
    const dx = -Math.sin(s.yaw), dz = -Math.cos(s.yaw);
    out.ax = s.x - dx * 0.6; out.ay = f + 0.22; out.az = s.z - dz * 0.6;
    out.bx = s.x + dx * 0.35; out.by = f + 0.25; out.bz = s.z + dz * 0.35;
    out.br = 0.22;
    out.hx = s.x + dx * 0.68; out.hy = f + 0.3; out.hz = s.z + dz * 0.68;
    out.hr = 0.15;
    return out;
  }
  if (s.ball) { bodyLo = f + 0.25; bodyHi = f + 0.35; head = f + 0.62; }
  else if (s.crouch) { bodyLo = f + 0.3; bodyHi = f + 0.82; head = f + 1.05; }
  else { bodyLo = f + 0.35; bodyHi = f + 1.28; head = f + 1.6; }
  out.ax = s.x; out.ay = bodyLo; out.az = s.z;
  out.bx = s.x; out.by = bodyHi; out.bz = s.z;
  out.br = 0.3;
  out.hx = s.x + fx; out.hy = head; out.hz = s.z + fz;
  out.hr = 0.16;
  return out;
}

export function lerpHitbox(a: Hitbox, b: Hitbox, t: number, out: Hitbox = {} as Hitbox): Hitbox {
  for (const k of ['ax', 'ay', 'az', 'bx', 'by', 'bz', 'br', 'hx', 'hy', 'hz', 'hr'] as const) out[k] = a[k] + (b[k] - a[k]) * t;
  return out;
}

/** Ray vs sphere. Returns distance or -1. (d must be normalised) */
export function raySphere(ox: number, oy: number, oz: number, dx: number, dy: number, dz: number,
  cx: number, cy: number, cz: number, r: number): number {
  const mx = ox - cx, my = oy - cy, mz = oz - cz;
  const b = mx * dx + my * dy + mz * dz;
  const c = mx * mx + my * my + mz * mz - r * r;
  if (c > 0 && b > 0) return -1;
  const disc = b * b - c;
  if (disc < 0) return -1;
  const t = -b - Math.sqrt(disc);
  return t < 0 ? 0 : t;
}

/** Ray vs capsule (segment a-b, radius r) = cylinder body + two end spheres. Returns distance or -1. */
export function rayCapsule(ox: number, oy: number, oz: number, dx: number, dy: number, dz: number,
  ax: number, ay: number, az: number, bx: number, by: number, bz: number, r: number): number {
  let best = -1;
  const consider = (t: number) => { if (t >= 0 && (best < 0 || t < best)) best = t; };
  // finite cylinder
  const bax = bx - ax, bay = by - ay, baz = bz - az;
  const oax = ox - ax, oay = oy - ay, oaz = oz - az;
  const baba = bax * bax + bay * bay + baz * baz;
  const bard = bax * dx + bay * dy + baz * dz;
  const baoa = bax * oax + bay * oay + baz * oaz;
  const rdoa = dx * oax + dy * oay + dz * oaz;
  const oaoa = oax * oax + oay * oay + oaz * oaz;
  const a = baba - bard * bard;
  if (a > 1e-9) {
    const b = baba * rdoa - baoa * bard;
    const c = baba * oaoa - baoa * baoa - r * r * baba;
    const h = b * b - a * c;
    if (h >= 0) {
      const t = (-b - Math.sqrt(h)) / a;
      const y = baoa + t * bard;
      if (y > 0 && y < baba) consider(t);
    }
  }
  consider(raySphere(ox, oy, oz, dx, dy, dz, ax, ay, az, r));
  consider(raySphere(ox, oy, oz, dx, dy, dz, bx, by, bz, r));
  return best;
}

/** Ray vs full hitbox. Returns { t, head } or null. Head wins if hit first. */
export function rayHitbox(ox: number, oy: number, oz: number, dx: number, dy: number, dz: number, hb: Hitbox, maxDist: number): { t: number; head: boolean } | null {
  const th = raySphere(ox, oy, oz, dx, dy, dz, hb.hx, hb.hy, hb.hz, hb.hr);
  const tb = rayCapsule(ox, oy, oz, dx, dy, dz, hb.ax, hb.ay, hb.az, hb.bx, hb.by, hb.bz, hb.br);
  let best: { t: number; head: boolean } | null = null;
  if (th >= 0 && th <= maxDist) best = { t: th, head: true };
  if (tb >= 0 && tb <= maxDist && (!best || tb < best.t)) best = { t: tb, head: false };
  return best;
}
