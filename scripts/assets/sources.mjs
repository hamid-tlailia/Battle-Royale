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
  ranger: { uid: 'ef698ce36b1545a78ce592dd3db4c7ed', use: 'Outfit: Ranger', label: 'Ranger', tris: 10000 },
  jumpsuit: { uid: 'da288adbddf949aa85962968732f0999', use: 'Outfit: Jumpsuit', label: 'Jumpsuit', tris: 9000 },
  civilian: { uid: '6fc4bddf4d364e6b9fa2f050011e03ce', use: 'Outfit: Civilian', label: 'Civilian', tris: 9000 },
  hoodie: { uid: '671bd73d045f47b4952fe2899772e124', use: 'Outfit: Hoodie', label: 'Hoodie', tris: 9000 },
  survivor: { uid: 'c6436897da494b4e8e1f8188aea620c6', use: 'Outfit: Survivor', label: 'Survivor', tris: 9000 },
};
