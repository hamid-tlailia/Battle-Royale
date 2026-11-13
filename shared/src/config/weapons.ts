import { TICK_RATE } from './game';

export type WeaponId = 'pistol' | 'rifle' | 'shotgun' | 'sniper' | 'smg';
export const WEAPON_IDS: WeaponId[] = ['pistol', 'rifle', 'shotgun', 'sniper', 'smg'];
/** Three carry slots: 0 = long range (rifle / sniper), 1 = close range (shotgun / SMG), 2 = pistol. */
export const SLOT_COUNT = 3;
export const SLOT_OF: Record<WeaponId, number> = { rifle: 0, sniper: 0, shotgun: 1, smg: 1, pistol: 2 };
export const SLOT_DEFAULT: WeaponId[] = ['rifle', 'shotgun', 'pistol'];
export const SLOT_NAMES = ['LONG RANGE', 'CLOSE RANGE', 'PISTOL'];
/** scope zoom levels a player can own (bitmask bit per level) and attach to the long-range gun */
export const SCOPE_LEVELS = [2, 4, 8] as const;
export const scopeBit = (zoom: number) => (zoom === 2 ? 1 : zoom === 4 ? 2 : zoom === 8 ? 4 : 0);

/** Data-driven weapon definitions. Server and client read the same table. */
export interface WeaponDef {
  id: WeaponId;
  name: string;
  /** damage per bullet/pellet at close range */
  damage: number;
  headMult: number;
  rpm: number;
  auto: boolean;
  mag: number;
  /** seconds */
  reloadTime: number;
  /** metres: bullets stop here */
  range: number;
  /** damage starts dropping after this distance… */
  falloffStart: number;
  /** …down to this fraction at max range */
  minDamageFrac: number;
  /** cone half-angle (radians) when firing from the hip */
  hipSpread: number;
  /** cone half-angle when scoped / aiming down sights */
  adsSpread: number;
  pellets: number;
  /** vertical fov (degrees) while scoped */
  scopeFov: number;
  /** spare ammo carried at spawn (offline sandbox) */
  startReserve: number;
}

export const WEAPONS: Record<WeaponId, WeaponDef> = {
  pistol: {
    id: 'pistol', name: 'Pistol', damage: 24, headMult: 1.8, rpm: 300, auto: true, mag: 12, reloadTime: 1.3,
    range: 90, falloffStart: 25, minDamageFrac: 0.5, hipSpread: 0.025, adsSpread: 0.007, pellets: 1, scopeFov: 50, startReserve: 48,
  },
  rifle: {
    id: 'rifle', name: 'Assault Rifle', damage: 19, headMult: 1.8, rpm: 600, auto: true, mag: 30, reloadTime: 2.2,
    range: 220, falloffStart: 60, minDamageFrac: 0.55, hipSpread: 0.04, adsSpread: 0.005, pellets: 1, scopeFov: 34, startReserve: 120,
  },
  shotgun: {
    id: 'shotgun', name: 'Shotgun', damage: 12, headMult: 1.5, rpm: 70, auto: true, mag: 5, reloadTime: 2.6,
    range: 35, falloffStart: 7, minDamageFrac: 0.25, hipSpread: 0.085, adsSpread: 0.065, pellets: 9, scopeFov: 52, startReserve: 25,
  },
  sniper: {
    id: 'sniper', name: 'Sniper', damage: 90, headMult: 2.2, rpm: 36, auto: false, mag: 5, reloadTime: 3.0,
    range: 320, falloffStart: 160, minDamageFrac: 0.85, hipSpread: 0.07, adsSpread: 0.0, pellets: 1, scopeFov: 12, startReserve: 20,
  },
  smg: {
    id: 'smg', name: 'SMG', damage: 15, headMult: 1.6, rpm: 780, auto: true, mag: 32, reloadTime: 2.0,
    range: 75, falloffStart: 18, minDamageFrac: 0.45, hipSpread: 0.042, adsSpread: 0.016, pellets: 1, scopeFov: 48, startReserve: 96,
  },
};

/** Ticks between shots. */
export const fireInterval = (w: WeaponDef) => Math.max(1, Math.round((60 / w.rpm) * TICK_RATE));

/** Damage after distance falloff (per bullet/pellet). */
export function damageAt(w: WeaponDef, dist: number, head: boolean): number {
  let f = 1;
  if (dist > w.falloffStart) {
    const t = Math.min(1, (dist - w.falloffStart) / Math.max(1, w.range - w.falloffStart));
    f = 1 - (1 - w.minDamageFrac) * t;
  }
  return w.damage * f * (head ? w.headMult : 1);
}

export const MAX_HP = 100;
export const MAX_ARMOR = 100;
/** Fraction of incoming damage absorbed by armor while it lasts. */
export const ARMOR_ABSORB = 0.5;
