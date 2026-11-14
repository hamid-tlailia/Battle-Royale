import type { WeaponId } from './weapons';

/** Everything that can lie on the ground. Data-driven so balance changes don't touch code. */
export type LootKind = 'weapon' | 'ammo' | 'med' | 'armor' | 'scope' | 'frost';

export interface LootItem {
  /** items spilled by a dead player share a box id (drawn as one crate, listed together) */
  box?: number;
  id: number;
  kind: LootKind;
  /** weapon / ammo: which weapon. scope: zoom level (2, 4, 8). */
  item?: WeaponId | number;
  amount: number;
  x: number; y: number; z: number;
  /** dropped by this player: they can't pick it up again until lockUntil (tick) */
  lockId?: number;
  lockUntil?: number;
}

export const LOOT = {
  /** pick-up radius (m), horizontal */
  pickupRadius: 1.4,
  /** ammo found in one box, per weapon */
  ammoBox: { pistol: 24, rifle: 60, shotgun: 10, sniper: 10, smg: 64 } as Record<WeaponId, number>,
  /** max spare ammo carried per weapon */
  maxReserve: { pistol: 96, rifle: 240, shotgun: 40, sniper: 40, smg: 256 } as Record<WeaponId, number>,
  maxMeds: 5,
  /** loot menu reach (m) */
  menuRadius: 2.6,
  maxFrost: 4,
  medHeal: 100,
  /** seconds to use a medkit (cancelled by shooting) */
  medTime: 3,
  armorVest: 50,
  /** frost wall */
  /** ~13 rifle bullets, 3 sniper shots or 2-3 shotgun blasts */
  frostHp: 260,
  frostLife: 25,
  frostWidth: 3.2,
  frostHeight: 2.3,
  frostThick: 0.5,
  frostDistance: 2.4,
} as const;

/** Relative spawn weights for ground loot. */
export const LOOT_TABLE: { kind: LootKind; item?: WeaponId | number; weight: number }[] = [
  { kind: 'weapon', item: 'pistol', weight: 8 },
  { kind: 'weapon', item: 'rifle', weight: 8 },
  { kind: 'weapon', item: 'shotgun', weight: 6 },
  { kind: 'weapon', item: 'sniper', weight: 3 },
  { kind: 'weapon', item: 'smg', weight: 6 },
  { kind: 'ammo', item: 'pistol', weight: 7 },
  { kind: 'ammo', item: 'rifle', weight: 10 },
  { kind: 'ammo', item: 'shotgun', weight: 5 },
  { kind: 'ammo', item: 'sniper', weight: 4 },
  { kind: 'ammo', item: 'smg', weight: 7 },
  { kind: 'med', weight: 9 },
  { kind: 'armor', weight: 6 },
  { kind: 'scope', item: 2, weight: 4 },
  { kind: 'scope', item: 4, weight: 2 },
  { kind: 'scope', item: 8, weight: 1 },
  { kind: 'frost', weight: 6 },
];
