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
```

## Assets
3D models come from Sketchfab (all CC-BY 4.0, see `CREDITS.md`).
`npm run assets:fetch` downloads sources into `assets-src/` (needs `SKETCHFAB_TOKEN`), `npm run assets:build` writes
optimized GLBs (meshopt + WebP) to `client/public/assets/` and collision shapes to `shared/src/map/prefabs.json`.

## Milestones
- [x] M1 offline: map, movement, touch controls, physics
- [x] M1.5 real CC-BY models (soldier w/ 12 animations, houses, guns, cars, trees), performance pass
- [ ] M2 weapons, shooting, damage (offline vs bots)
- [ ] M3 multiplayer server, prediction, interpolation, lag compensation
- [ ] M4 loot, safe zone, match flow, winner screen
- [ ] M5 menus, polish, sound
- [ ] M6 performance pass + Android app (Capacitor)
