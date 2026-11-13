import { beforeAll, describe, expect, it } from 'vitest';
import { BTN, DT, GameSim, MAP, MAP_LIST, PhysicsWorld, generateMap, initPhysics, newMoveState, simulateMove } from '../shared/src';

beforeAll(async () => { await initPhysics(); });

describe('maps', () => {
  it('every map generates deterministically with houses, cover and spawns', () => {
    for (const { id } of MAP_LIST) {
      const a = generateMap(MAP.seed, id), b = generateMap(MAP.seed, id);
      expect(a.props).toEqual(b.props);
      expect(a.props.filter((p) => p.p.startsWith('house') || p.p === 'farmhouse').length).toBeGreaterThanOrEqual(15);
      expect(a.spawns.length).toBeGreaterThanOrEqual(40);
    }
  });

  it('green map: real scan terrain, farm compounds and scanned rocks', () => {
    const m = generateMap(MAP.seed, 'green');
    expect(m.mask && m.albedo).toBeTruthy();
    expect(m.props.filter((p) => p.p === 'windmill').length).toBeGreaterThanOrEqual(3);
    expect(m.props.some((p) => p.p === 'tors_green')).toBe(true);
    let lo = Infinity, hi = -Infinity; for (const h of m.heights) { lo = Math.min(lo, h); hi = Math.max(hi, h); }
    expect(hi - lo).toBeGreaterThan(30); // real hills, not flat
  });

  it('green map: 12 bots for 40 s never fall through the ground', () => {
    const sim = new GameSim(MAP.seed, { map: 'green', zone: false, armed: true, loot: false, skyDrop: false });
    for (let i = 0; i < 12; i++) sim.addPlayer(i, `bot${i}`, true);
    sim.startMatch?.();
    let minAbove = Infinity;
    for (let t = 0; t < 40 * 30; t++) {
      sim.step();
      if (t % 15) continue;
      for (const p of sim.players.values()) if (p.alive) minAbove = Math.min(minAbove, p.move.y - sim.phys.groundHeight(p.move.x, p.move.z));
    }
    expect(minAbove).toBeGreaterThan(0.2);
  }, 60000);
});

describe('hills are climbable', () => {
  it('sprinting from the valley edge reaches the high ground on the real-terrain maps (no getting stuck on slopes)', () => {
    for (const id of ['green', 'desert'] as const) {
      const map = generateMap(MAP.seed, id); map.props = []; map.boxes = []; // terrain only
      const phys = new PhysicsWorld(map);
      let reached = 0;
      for (let a = 0; a < 8; a++) {
        const ang = (a * Math.PI) / 4, sp = phys.findSpawn(Math.cos(ang) * 120, Math.sin(ang) * 120);
        phys.addPlayer(a + 1, sp.x, sp.y, sp.z);
        const s = newMoveState(sp.x, sp.y, sp.z);
        for (let t = 0; t < 30 * 25 && Math.hypot(s.x, s.z) > 45; t++) {
          simulateMove(phys, a + 1, s, { seq: t, mx: 0, mz: 1, yaw: Math.atan2(s.x, s.z), pitch: 0, buttons: BTN.SPRINT }, DT);
          phys.updateQueries();
        }
        if (Math.hypot(s.x, s.z) <= 45) reached++;
        phys.removePlayer(a + 1);
      }
      expect(reached, id).toBeGreaterThanOrEqual(7);
    }
  }, 60000);
});
