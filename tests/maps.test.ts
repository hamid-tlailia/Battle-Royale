import { beforeAll, describe, expect, it } from 'vitest';
import { GameSim, MAP, MAP_LIST, generateMap, initPhysics } from '../shared/src';

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
