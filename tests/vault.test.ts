import { beforeAll, describe, expect, it } from 'vitest';
import {
  BTN, DT, PREFABS, PhysicsWorld, generateMap, initPhysics, newMoveState, simulateMove, halfTotalOf,
  type InputCmd, type MapData, type PropInst,
} from '../shared/src';

let map: MapData;
beforeAll(async () => { await initPhysics(); map = generateMap(); });
const cmd = (p: Partial<InputCmd> = {}): InputCmd => ({ seq: 0, mx: 0, mz: 0, yaw: 0, pitch: 0, buttons: 0, ...p });

function frame(h: PropInst) {
  const [sx, , sz] = PREFABS[h.p].size;
  const c = Math.cos(h.rot), sn = Math.sin(h.rot);
  return {
    sx, sz,
    toWorld: (lx: number, lz: number) => ({ x: h.x + lx * c + lz * sn, z: h.z - lx * sn + lz * c }),
    toLocal: (x: number, z: number) => { const dx = x - h.x, dz = z - h.z; return { lx: dx * c - dz * sn, lz: dx * sn + dz * c }; },
  };
}

/** Find a window on the house front (+z local side): opening at 1.8 m, wall at 0.5 m. */
function findWindow(phys: PhysicsWorld, h: PropInst, f: ReturnType<typeof frame>) {
  const dist = (u: number, hy: number) => {
    const o = f.toWorld(u, f.sz / 2 + 3), t = f.toWorld(u, 0);
    const dx = t.x - o.x, dz = t.z - o.z, L = Math.hypot(dx, dz);
    return phys.rayWorld(o.x, h.y + hy, o.z, dx / L, 0, dz / L, 30) ?? 99;
  };
  // wall at the outer face; the opening must lead at least 2 m into the house
  const blocked = (u: number, hy: number) => hy < 1.85 || hy > 2.5 ? dist(u, hy) < 3.4 : dist(u, hy) < 5.5;
  let run = 0;
  for (let u = -f.sx / 2 + 0.4; u < f.sx / 2 - 0.4; u += 0.05) {
    run = blocked(u, 0.5) && !blocked(u, 1.85) && blocked(u, 3.0) ? run + 1 : 0;
    if (run === 12) return u - 0.28; // centre of a >= 0.6 m opening (smaller ones are too tight to climb)
  }
  return null;
}

describe('vault', () => {
  it('jumping at a house window climbs through it without clipping', () => {
    const phys = new PhysicsWorld(map);
    let tested = 0;
    for (const [hi, h] of map.props.filter((p) => p.p.startsWith('house')).entries()) {
      const f = frame(h);
      const u = findWindow(phys, h, f);
      if (u === null) continue;
      const start = f.toWorld(u + 0.1, f.sz / 2 + 0.75); // slightly misaligned on purpose
      const target = f.toWorld(u, 0);
      const id = 10 + hi;
      const y = phys.groundHeight(start.x, start.z) + 0.95;
      phys.addPlayer(id, start.x, Math.max(y, h.y + 0.95), start.z);
      const s = newMoveState(start.x, Math.max(y, h.y + 0.95), start.z);
      const yaw = Math.atan2(-(target.x - start.x), -(target.z - start.z));
      for (let i = 0; i < 20; i++) simulateMove(phys, id, s, cmd({ yaw }), DT); // settle
      simulateMove(phys, id, s, cmd({ yaw, buttons: BTN.JUMP }), DT);
      expect(s.vault, `house ${hi} vault started`).toBeGreaterThan(0);
      for (let i = 0; i < 60; i++) simulateMove(phys, id, s, cmd({ yaw, mz: i < 10 ? 1 : 0 }), DT);
      const l = f.toLocal(s.x, s.z);
      expect(Math.abs(l.lz), `house ${hi}: ended inside`).toBeLessThan(f.sz / 2 - 0.3);
      expect(phys.shapeBlocked(s.x, s.y, s.z, 0.001, 0.2, phys.players.get(id))).toBe(false);
      expect(s.y - halfTotalOf(s)).toBeGreaterThan(h.y - 0.3); // on the floor, not under it
      phys.removePlayer(id);
      tested++;
    }
    expect(tested).toBeGreaterThanOrEqual(3);
  });

  it('vaults over a low wall / crate', () => {
    const phys = new PhysicsWorld(map);
    const wall = map.boxes.find((b) => b.hy > 0.8 && b.hy < 0.9 && b.hx > 2)!;
    expect(wall).toBeTruthy();
    // approach along the wall's thin (local z) axis
    const nx = Math.sin(wall.rot), nz = Math.cos(wall.rot);
    const sx = wall.x + nx * 1.3, sz = wall.z + nz * 1.3;
    const y = phys.groundHeight(sx, sz) + 0.95;
    phys.addPlayer(1, sx, y, sz);
    const s = newMoveState(sx, y, sz);
    const yaw = Math.atan2(nx, nz); // forward = -n -> toward the wall
    for (let i = 0; i < 20; i++) simulateMove(phys, 1, s, cmd({ yaw }), DT);
    simulateMove(phys, 1, s, cmd({ yaw, buttons: BTN.JUMP }), DT);
    for (let i = 0; i < 50; i++) simulateMove(phys, 1, s, cmd({ yaw }), DT);
    const side = (s.x - wall.x) * nx + (s.z - wall.z) * nz;
    expect(side).toBeLessThan(-0.5); // crossed to the other side
    expect(s.ball).toBe(false); // stood back up
  });

  it('a plain jump in the open does not trigger a vault', () => {
    const phys = new PhysicsWorld(map);
    const sp = phys.findSpawn(map.spawns[0].x, map.spawns[0].z);
    phys.addPlayer(1, sp.x, sp.y, sp.z);
    const s = newMoveState(sp.x, sp.y, sp.z);
    for (let i = 0; i < 20; i++) simulateMove(phys, 1, s, cmd(), DT);
    simulateMove(phys, 1, s, cmd({ buttons: BTN.JUMP }), DT);
    expect(s.vault).toBe(0);
    expect(s.vy).toBeGreaterThan(0);
  });
});
