# Last War

Original low-poly mobile battle royale (Three.js + Rapier + Colyseus).

- `shared/` — simulation code used by both client and server (map generation, physics, movement, bots)
- `client/` — Vite + Three.js web client
- `server/` — Colyseus authoritative server (from M3)
- `tests/` — Vitest suites

```
npm install
npm run dev     # client dev server
npm test        # all tests
npm run server  # game server on :7860 (or: docker build -t last-war . && docker run -p 7860:7860 last-war)
# client -> server: build with VITE_SERVER_URL=https://your-server or open the site with ?server=https://your-server
```

## Assets
3D models come from Sketchfab (all CC-BY 4.0, see `CREDITS.md`).
`npm run assets:fetch` downloads sources into `assets-src/` (needs `SKETCHFAB_TOKEN`), `npm run assets:build` writes
optimized GLBs (meshopt + WebP) to `client/public/assets/` and collision shapes to `shared/src/map/prefabs.json`.

## Milestones
- [x] M1 offline: map, movement, touch controls, physics
- [x] M1.5 real CC-BY models (soldier w/ 12 animations, houses, guns, cars, trees), performance pass
- [x] M2 weapons, shooting, damage, real scope, vault, safe zone, sounds, kill feed, winner screen (offline vs bots)
- [x] M3 multiplayer server, prediction, interpolation, lag compensation (tested at 200 ms + 5% loss) — needs a host
- [x] M4 loot (weapons, ammo, medkits, armor, scopes ×2/×4, ice walls), death drops, safe zone, match flow, winner screen
- [ ] M5 menus, polish, sound
- [ ] M6 performance pass + Android app (Capacitor)
