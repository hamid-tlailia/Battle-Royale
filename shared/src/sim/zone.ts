import { MAP, TICK_RATE } from '../config/game';
import { mulberry32 } from '../math/rng';

/** Shrinking safe zone. Fully determined by (seed, tick) so client and server always agree. */
export interface ZonePhase { wait: number; shrink: number; radius: number; dps: number }

/** seconds / metres / damage per second outside the zone */
export const ZONE_PHASES: ZonePhase[] = [
  { wait: 80, shrink: 45, radius: 120, dps: 1 }, // first wait includes the plane + parachute drop
  { wait: 40, shrink: 35, radius: 70, dps: 2 },
  { wait: 35, shrink: 30, radius: 36, dps: 4 },
  { wait: 25, shrink: 25, radius: 14, dps: 7 },
  { wait: 15, shrink: 20, radius: 0, dps: 12 },
];
export const ZONE_START_RADIUS = MAP.playRadius + 15;

export interface ZoneState {
  /** current circle */
  cx: number; cz: number; r: number;
  /** circle it is shrinking toward */
  nx: number; nz: number; nr: number;
  phase: number;
  shrinking: boolean;
  /** seconds until the next shrink starts (or ends, while shrinking) */
  timer: number;
  dps: number;
}

const circlesCache = new Map<number, { x: number; z: number; r: number }[]>();
function circles(seed: number) {
  let c = circlesCache.get(seed);
  if (c) return c;
  const rnd = mulberry32(seed ^ 0x5eed);
  c = [{ x: 0, z: 0, r: ZONE_START_RADIUS }];
  for (const p of ZONE_PHASES) {
    const prev = c[c.length - 1];
    const maxOff = Math.max(0, Math.min(prev.r - p.radius, MAP.playRadius - 10 - p.radius));
    const a = rnd() * Math.PI * 2, d = Math.sqrt(rnd()) * maxOff;
    let x = prev.x + Math.cos(a) * d, z = prev.z + Math.sin(a) * d;
    // keep inside the valley
    const L = Math.hypot(x, z), lim = Math.max(0, MAP.playRadius - 10 - p.radius);
    if (L > lim) { x *= lim / L; z *= lim / L; }
    c.push({ x, z, r: p.radius });
  }
  circlesCache.set(seed, c);
  return c;
}

export function zoneAt(seed: number, tick: number): ZoneState {
  const c = circles(seed);
  let t = tick / TICK_RATE;
  for (let i = 0; i < ZONE_PHASES.length; i++) {
    const p = ZONE_PHASES[i];
    const from = c[i], to = c[i + 1];
    if (t < p.wait) return { cx: from.x, cz: from.z, r: from.r, nx: to.x, nz: to.z, nr: to.r, phase: i, shrinking: false, timer: p.wait - t, dps: i === 0 ? 0 : ZONE_PHASES[i - 1].dps };
    t -= p.wait;
    if (t < p.shrink) {
      const k = t / p.shrink;
      return {
        cx: from.x + (to.x - from.x) * k, cz: from.z + (to.z - from.z) * k, r: from.r + (to.r - from.r) * k,
        nx: to.x, nz: to.z, nr: to.r, phase: i, shrinking: true, timer: p.shrink - t, dps: p.dps,
      };
    }
    t -= p.shrink;
  }
  const last = c[c.length - 1];
  return { cx: last.x, cz: last.z, r: last.r, nx: last.x, nz: last.z, nr: last.r, phase: ZONE_PHASES.length, shrinking: false, timer: 0, dps: ZONE_PHASES[ZONE_PHASES.length - 1].dps };
}

export const outsideZone = (z: ZoneState, x: number, zz: number) => Math.hypot(x - z.cx, zz - z.cz) > z.r;
