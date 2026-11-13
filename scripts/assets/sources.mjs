/**
 * Every third-party asset used by the game. `npm run assets:fetch` downloads them from
 * Sketchfab (requires a Sketchfab API token in SKETCHFAB_TOKEN, or the dev proxy),
 * `npm run assets:build` converts them into optimized game files, and CREDITS.md is generated.
 */
export const SOURCES = {
  soldier: { uid: '2bfce585144d4403a3ddf76a0e65767a', use: 'Player character mesh + combat/death/jump animations' },
  soldierAnims: { uid: '5ebade1ccbff4adb91ddd3a4d1fcd541', use: 'Idle / walk / run / crouch animations (retargeted)' },
  guns: { uid: 'd6548cd2f49140e5b40d84a9551f359c', use: 'Pistol, assault rifle, shotgun, sniper models' },
  houseA: { uid: '28262619cb304134830b4ebeafdf090b', use: 'House (enterable)' },
  houseB: { uid: '4e2355f65bf942879113dd3640bba962', use: 'House (enterable)' },
  houseC: { uid: '994e1e752ac04d02a8918763fbaf3651', use: 'House (enterable)' },
  cars: { uid: 'a6052152abde4da1bb103f09eb1b8b7c', use: 'Abandoned cars' },
  pines: { uid: '46c3811b9a9445cdb2ff3e663d1f2c11', use: 'Pine trees' },
  nature: { uid: '4e0463f5df36420bb53079f9de35e81f', use: 'Rocks, bushes, stump' },
  fence: { uid: 'd35a038f74ea47819459cabae8c28c62', use: 'Wooden fence' },
};

/** Extra player outfits (Mixamo-rigged characters, animated with the soldier's clips). `tris` = body budget. */
export const OUTFITS = {
  swat: { uid: '9e82fabf26194896b5ad4a364d864eab', use: 'Outfit: SWAT', label: 'SWAT', tris: 12000, gain: 2.4 },
  tactical: { uid: '68213c7d97a04b1b963599180210f5bd', use: 'Outfit: Tactical', label: 'Tactical', tris: 10000 },
  fsb: { uid: '43a561e941704eefb1ab0614be4f0049', use: 'Outfit: Operator', label: 'Operator', tris: 12000 },
  ksk: { uid: '110daf3d1884412989e41dc95866c579', use: 'Outfit: Recon', label: 'Recon', tris: 10000 },
  bundeswehr: { uid: '3d7944fea17b494abf220cc11ddabfd1', use: 'Outfit: Woodland', label: 'Woodland', tris: 9000 },
  sfarmy: { uid: '4edb7058bfec48a696e260a36b900b1b', use: 'Outfit: Commando', label: 'Commando', tris: 10000 },
  rusoldier: { uid: 'e9aa211b67264dc086af1d2f4d4e3d84', use: 'Outfit: Rifleman', label: 'Rifleman', tris: 10000 },
  uasoldier: { uid: '94f74f6bdd994733a8a40c7ff088e12f', use: 'Outfit: Desert', label: 'Desert', tris: 10000 },
  ww2: { uid: '6a104431bcb8400080b79446ec019696', use: 'Outfit: Veteran', label: 'Veteran', tris: 10000 },
  crouchsold: { uid: '9b73b36ba2184bd1bde5b42810752b69', use: 'Outfit: Ghost', label: 'Ghost', tris: 12000 },
  armyman: { uid: 'cc995450452744ec8f852d8bf5f335d6', use: 'Outfit: Infantry', label: 'Infantry', tris: 9000, rename: ['mott_var01:', 'mixamorig:'] },
};
