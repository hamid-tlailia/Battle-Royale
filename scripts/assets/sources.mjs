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
