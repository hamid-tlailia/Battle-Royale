import { beforeAll, describe, expect, it } from 'vitest';
import {
  BTN, DT, MAP, PLAYER, GameSim, PhysicsWorld, generateMap, initPhysics, newMoveState, simulateMove, terrainHeight,
  PREFABS, shapeOf, halfTotalOf, type InputCmd, type MapData, type MoveState, type PropInst,
} from '../shared/src';

let map: MapData;
beforeAll(async () => { await initPhysics(); map = generateMap(); });

const cmd = (p: Partial<InputCmd> = {}): InputCmd => ({ seq: 0, mx: 0, mz: 0, yaw: 0, pitch: 0, buttons: 0, ...p });
const halfTotal = PLAYER.halfHeight + PLAYER.radius;

function spawn(phys: PhysicsWorld, id: number, x: number, z: number): MoveState {
  const p = phys.findSpawn(x, z);
  phys.addPlayer(id, p.x, p.y, p.z);
  return newMoveState(p.x, p.y, p.z);
}

describe('map generation', () => {
  it('is deterministic from seed', () => {
    const a = generateMap(42), b = generateMap(42);
    expect(Array.from(a.heights.slice(0, 50))).toEqual(Array.from(b.heights.slice(0, 50)));
    expect(a.boxes.length).toBe(b.boxes.length);
    expect(a.props).toEqual(b.props);
  });
  it('has houses, cars, trees, rocks and spawns', () => {
    const count = (pre: string) => map.props.filter((p) => p.p.startsWith(pre)).length;
    expect(count('house')).toBeGreaterThanOrEqual(8);
    expect(count('car')).toBeGreaterThanOrEqual(8);
    expect(count('pine')).toBeGreaterThan(150);
    expect(count('rock')).toBeGreaterThan(30);
    expect(map.spawns.length).toBeGreaterThanOrEqual(20);
  });
});

describe('movement & collision', () => {
  it('settles on the ground and never falls through it', () => {
    const phys = new PhysicsWorld(map);
    const s = spawn(phys, 1, 10, 10);
    s.y += 5; // drop from height
    for (let i = 0; i < 120; i++) { simulateMove(phys, 1, s, cmd(), DT); phys.updateQueries(); }
    expect(s.grounded).toBe(true);
    expect(s.y).toBeGreaterThan(terrainHeight(map, s.x, s.z) + halfTotal - 0.3);
  });

  it('walks at bounded speed', () => {
    const phys = new PhysicsWorld(map);
    const s = spawn(phys, 1, 0, 0);
    for (let i = 0; i < 10; i++) simulateMove(phys, 1, s, cmd(), DT);
    const x0 = s.x, z0 = s.z;
    // try to cheat with huge input vector — must be clamped
    for (let i = 0; i < 30; i++) { simulateMove(phys, 1, s, cmd({ mx: 50, mz: 50 }), DT); phys.updateQueries(); }
    const d = Math.hypot(s.x - x0, s.z - z0);
    expect(d).toBeLessThanOrEqual(PLAYER.walkSpeed * 1.0 + 0.05);
  });

  function houseFrame(h: PropInst) {
    const [sx, , sz] = PREFABS[h.p].size;
    const c = Math.cos(h.rot), sn = Math.sin(h.rot);
    // local -> world (three.js Y rotation convention)
    const toWorld = (lx: number, lz: number) => ({ x: h.x + lx * c + lz * sn, z: h.z - lx * sn + lz * c });
    const toLocal = (x: number, z: number) => { const dx = x - h.x, dz = z - h.z; return { lx: dx * c - dz * sn, lz: dx * sn + dz * c }; };
    return { sx, sz, toWorld, toLocal };
  }

  it('cannot walk through a house wall (side without doors)', () => {
    const phys = new PhysicsWorld(map);
    let tested = 0;
    for (const h of map.props.filter((p) => p.p.startsWith('house'))) {
      const f = houseFrame(h);
      // walk from outside toward the house centre along each local axis; most walls stop us
      for (const [ax, az] of [[1, 0], [-1, 0], [0, 1], [0, -1]]) {
        const startL = { lx: ax * (f.sx / 2 + 3), lz: az * (f.sz / 2 + 3) };
        const w = f.toWorld(startL.lx, startL.lz);
        const id = 100 + tested;
        const s = spawn(phys, id, w.x, w.z);
        const yaw = Math.atan2(-(h.x - s.x), -(h.z - s.z));
        for (let i = 0; i < 90; i++) { simulateMove(phys, id, s, cmd({ mz: 1, yaw }), DT); phys.updateQueries(); }
        const l = f.toLocal(s.x, s.z);
        // Never end up embedded inside a wall: either clearly outside the footprint or properly inside
        const inside = Math.abs(l.lx) < f.sx / 2 - 0.3 && Math.abs(l.lz) < f.sz / 2 - 0.3;
        const blocked = phys.capsuleBlocked(s.x, s.y, s.z, PLAYER.halfHeight, phys.players.get(id));
        expect(blocked).toBe(false);
        if (!inside) tested++;
        phys.removePlayer(id);
      }
    }
    expect(tested).toBeGreaterThan(10); // walls actually stopped players
  });

  it('every house has a full-height door that a player can walk through', () => {
    const phys = new PhysicsWorld(map);
    const houses = map.props.filter((p) => p.p.startsWith('house'));
    let pid = 900;
    for (const [hi, h] of houses.entries()) {
      const f = houseFrame(h);
      // Find door columns: blocked high up (wall above), free at 0.4 m and 2.0 m (opening)
      const doors: { side: number[]; u: number }[] = [];
      for (const side of [[0, 1], [0, -1], [1, 0], [-1, 0]]) {
        const along = side[0] === 0 ? f.sx : f.sz;
        const rayAt = (u: number, hy: number) => {
          const o = side[0] === 0 ? f.toWorld(u, side[1] * (f.sz / 2 + 3)) : f.toWorld(side[0] * (f.sx / 2 + 3), u);
          const t = side[0] === 0 ? f.toWorld(u, 0) : f.toWorld(0, u);
          const dx = t.x - o.x, dz = t.z - o.z, L = Math.hypot(dx, dz);
          const hit = phys.rayWorld(o.x, h.y + hy, o.z, dx / L, 0, dz / L, 30);
          return hit !== null && hit < 3.5; // hit the outer wall
        };
        let run = 0;
        for (let u = -along / 2 + 0.5; u <= along / 2 - 0.5; u += 0.1) {
          const open = rayAt(u, 3.4) && !rayAt(u, 0.4) && !rayAt(u, 2.0);
          run = open ? run + 1 : 0;
          if (run === 9) doors.push({ side, u: u - 0.4 }); // >= 0.9 m wide opening, centre
        }
      }
      expect(doors.length, `house ${hi} (${h.p}) has a door`).toBeGreaterThan(0);
      // Walk in through the first door
      const d = doors[0];
      const outside = d.side[0] === 0 ? f.toWorld(d.u, d.side[1] * (f.sz / 2 + 2)) : f.toWorld(d.side[0] * (f.sx / 2 + 2), d.u);
      const target = d.side[0] === 0 ? f.toWorld(d.u, 0) : f.toWorld(0, d.u);
      const y = phys.groundHeight(outside.x, outside.z) + halfTotal + 0.05;
      const id = pid++;
      phys.addPlayer(id, outside.x, Math.max(y, h.y + halfTotal + 0.05), outside.z);
      const s = newMoveState(outside.x, Math.max(y, h.y + halfTotal + 0.05), outside.z);
      const yaw = Math.atan2(-(target.x - outside.x), -(target.z - outside.z));
      for (let i = 0; i < 60; i++) simulateMove(phys, id, s, cmd({ mz: 1, yaw }), DT);
      const l = f.toLocal(s.x, s.z);
      const inside = Math.abs(l.lx) < f.sx / 2 - 0.5 && Math.abs(l.lz) < f.sz / 2 - 0.5;
      expect(inside, `entered house ${hi} (${h.p}) via door at side ${d.side} u=${d.u.toFixed(1)}`).toBe(true);
      phys.removePlayer(id);
    }
  });

  it('players cannot overlap each other', () => {
    const phys = new PhysicsWorld(map);
    const a = spawn(phys, 1, 0, 0);
    const b = spawn(phys, 2, a.x + 6, a.z);
    phys.updateQueries();
    const yawA = Math.atan2(-(b.x - a.x), -(b.z - a.z));
    let minDist = Infinity;
    for (let i = 0; i < 90; i++) {
      const yawToB = Math.atan2(-(b.x - a.x), -(b.z - a.z));
      const yawToA = Math.atan2(-(a.x - b.x), -(a.z - b.z));
      simulateMove(phys, 1, a, cmd({ mz: 1, yaw: i === 0 ? yawA : yawToB }), DT);
      simulateMove(phys, 2, b, cmd({ mz: 1, yaw: yawToA }), DT);
      phys.updateQueries();
      const dy = Math.abs(a.y - b.y);
      if (dy < PLAYER.halfHeight * 2) minDist = Math.min(minDist, Math.hypot(a.x - b.x, a.z - b.z));
    }
    expect(minDist).toBeGreaterThanOrEqual(PLAYER.radius * 2 - 0.05);
  });

  it('jumps and lands', () => {
    const phys = new PhysicsWorld(map);
    const s = spawn(phys, 1, -20, 15);
    for (let i = 0; i < 30; i++) { simulateMove(phys, 1, s, cmd(), DT); phys.updateQueries(); }
    const y0 = s.y;
    simulateMove(phys, 1, s, cmd({ buttons: BTN.JUMP }), DT);
    let maxY = s.y;
    for (let i = 0; i < 60; i++) { simulateMove(phys, 1, s, cmd(), DT); maxY = Math.max(maxY, s.y); }
    expect(maxY - y0).toBeGreaterThan(0.9);
    expect(Math.abs(s.y - y0)).toBeLessThan(0.1);
    expect(s.grounded).toBe(true);
  });

  it('crouch lowers the capsule and cannot stand up under a low ceiling', () => {
    const phys = new PhysicsWorld(map);
    const s = spawn(phys, 1, 30, -30);
    for (let i = 0; i < 30; i++) simulateMove(phys, 1, s, cmd(), DT);
    const y0 = s.y;
    simulateMove(phys, 1, s, cmd({ buttons: BTN.CROUCH }), DT);
    expect(s.crouch).toBe(true);
    expect(s.y).toBeLessThan(y0);
    simulateMove(phys, 1, s, cmd(), DT);
    expect(s.crouch).toBe(false);
  });

  it('sprint is faster than walking, only when moving forward and standing', () => {
    const phys = new PhysicsWorld(map);
    const lane = map.spawns[3];
    const run = (buttons: number, mz: number, mx = 0) => {
      const s = spawn(phys, 77, lane.x, lane.z);
      for (let i = 0; i < 10; i++) simulateMove(phys, 77, s, cmd(), DT);
      const x0 = s.x, z0 = s.z;
      for (let i = 0; i < 30; i++) { simulateMove(phys, 77, s, cmd({ mz, mx, buttons }), DT); phys.updateQueries(); }
      phys.removePlayer(77);
      return Math.hypot(s.x - x0, s.z - z0);
    };
    const walk = run(0, 1), sprint = run(BTN.SPRINT, 1), sideSprint = run(BTN.SPRINT, 0, 1), crouchSprint = run(BTN.SPRINT | BTN.CROUCH, 1);
    expect(sprint).toBeGreaterThan(walk * 1.25);
    expect(sprint).toBeLessThanOrEqual(PLAYER.sprintSpeed + 0.05);
    expect(sideSprint).toBeLessThanOrEqual(PLAYER.walkSpeed + 0.05);
    expect(crouchSprint).toBeLessThanOrEqual(PLAYER.crouchSpeed + 0.05);
  });

  it('prone lies down (low, slow) and stands back up', () => {
    const phys = new PhysicsWorld(map);
    const s = spawn(phys, 1, 30, -30);
    for (let i = 0; i < 20; i++) simulateMove(phys, 1, s, cmd(), DT);
    const feet0 = s.y - halfTotalOf(s);
    simulateMove(phys, 1, s, cmd({ buttons: BTN.PRONE }), DT);
    expect(s.prone).toBe(true);
    expect(halfTotalOf(s) * 2).toBeLessThan(0.7);
    expect(Math.abs(s.y - halfTotalOf(s) - feet0)).toBeLessThan(0.05); // feet stay on the ground
    const x0 = s.x;
    for (let i = 0; i < 30; i++) simulateMove(phys, 1, s, cmd({ buttons: BTN.PRONE, mz: 1, yaw: -Math.PI / 2 }), DT);
    expect(Math.abs(s.x - x0)).toBeLessThanOrEqual(PLAYER.proneSpeed + 0.05);
    simulateMove(phys, 1, s, cmd(), DT);
    expect(s.prone).toBe(false);
    expect(s.crouch).toBe(false);
  });

  it('20 bots for 60s (zone off, no friendly fire limits): nobody falls through the world, overlaps, or exceeds speed', () => {
    const sim = new GameSim(MAP.seed, { zone: false });
    for (let i = 0; i < 20; i++) sim.addPlayer(i, `bot${i}`, true);
    let maxStep = 0, maxOverlap = 0, minAboveGround = Infinity;
    const prev = new Map<number, { x: number; z: number }>();
    for (let t = 0; t < 30 * 60; t++) {
      for (const p of sim.players.values()) prev.set(p.id, { x: p.move.x, z: p.move.z });
      sim.step();
      const alive = [...sim.players.values()].filter((p) => p.alive);
      for (const p of alive) {
        const q = prev.get(p.id)!;
        maxStep = Math.max(maxStep, Math.hypot(p.move.x - q.x, p.move.z - q.z));
        minAboveGround = Math.min(minAboveGround, p.move.y - halfTotalOf(p.move) - terrainHeight(map, p.move.x, p.move.z));
      }
      for (let i = 0; i < alive.length; i++) for (let j = i + 1; j < alive.length; j++) {
        const a = alive[i].move, c = alive[j].move;
        if (Math.abs(a.y - c.y) < 0.5) maxOverlap = Math.max(maxOverlap, shapeOf(a).r + shapeOf(c).r - Math.hypot(a.x - c.x, a.z - c.z));
      }
    }
    expect(maxStep).toBeLessThanOrEqual(PLAYER.walkSpeed * DT * 2); // allow slope/step slide, catch teleports
    expect(maxOverlap).toBeLessThan(0.06);
    expect(minAboveGround).toBeGreaterThan(-0.3);
  });
});
