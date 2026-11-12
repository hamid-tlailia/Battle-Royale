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

## Milestones
- [x] M1 offline: map, movement, touch controls, physics
- [ ] M2 weapons, shooting, damage (offline vs bots)
- [ ] M3 multiplayer server, prediction, interpolation, lag compensation
- [ ] M4 loot, safe zone, match flow, winner screen
- [ ] M5 menus, polish, sound
- [ ] M6 performance pass + Android app (Capacitor)
