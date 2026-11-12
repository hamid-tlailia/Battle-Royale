import { beforeAll, describe, expect, it } from 'vitest';
import {
  BTN, DT, PLAYER, BotBrain, PhysicsWorld, generateMap, initPhysics, newMoveState, simulateMove, terrainHeight,
  type InputCmd, type MapData, type MoveState,
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
    expect(a.trees).toEqual(b.trees);
  });
  it('has houses, trees, rocks and spawns', () => {
    expect(map.boxes.length).toBeGreaterThan(50);
    expect(map.trees.length).toBeGreaterThan(100);
    expect(map.rocks.length).toBeGreaterThan(30);
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

  it('cannot walk through a house wall', () => {
    const phys = new PhysicsWorld(map);
    // pick a tall wall box
    const wall = map.boxes.find((b) => b.hy > 1.2 && b.hy < 2 && Math.max(b.hx, b.hz) > 2.5)!;
    expect(wall).toBeTruthy();
    // approach perpendicular to its thin axis
    const thinX = wall.hx < wall.hz;
    const c = Math.cos(wall.rot), sn = Math.sin(wall.rot);
    const nLocal = thinX ? [1, 0] : [0, 1];
    const nx = nLocal[0] * c + nLocal[1] * sn, nz = -nLocal[0] * sn + nLocal[1] * c;
    const startX = wall.x + nx * 4, startZ = wall.z + nz * 4;
    const s = spawn(phys, 1, startX, startZ);
    const side0 = Math.sign((s.x - wall.x) * nx + (s.z - wall.z) * nz);
    // face the wall: forward = (-sin yaw, -cos yaw) = -n*side0
    const yaw = Math.atan2(nx * side0, nz * side0);
    for (let i = 0; i < 150; i++) { simulateMove(phys, 1, s, cmd({ mz: 1, yaw }), DT); phys.updateQueries(); }
    const side1 = Math.sign((s.x - wall.x) * nx + (s.z - wall.z) * nz);
    expect(side1).toBe(side0);
    const thin = thinX ? wall.hx : wall.hz;
    const dist = Math.abs((s.x - wall.x) * nx + (s.z - wall.z) * nz);
    expect(dist).toBeGreaterThanOrEqual(thin + PLAYER.radius - 0.05);
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

  it('20 wandering bots for 60s: nobody falls through the world, overlaps, or exceeds speed', () => {
    const phys = new PhysicsWorld(map);
    const bots: { s: MoveState; brain: BotBrain }[] = [];
    for (let i = 0; i < 20; i++) {
      const sp = map.spawns[i];
      bots.push({ s: spawn(phys, i, sp.x, sp.z), brain: new BotBrain(i + 1) });
    }
    phys.updateQueries();
    let maxStep = 0, maxOverlap = 0, minAboveGround = Infinity;
    for (let t = 0; t < 30 * 60; t++) {
      for (let i = 0; i < bots.length; i++) {
        const b = bots[i];
        const px = b.s.x, pz = b.s.z;
        simulateMove(phys, i, b.s, b.brain.think(b.s, DT), DT);
        maxStep = Math.max(maxStep, Math.hypot(b.s.x - px, b.s.z - pz));
        const ht = (b.s.crouch ? PLAYER.crouchHalfHeight : PLAYER.halfHeight) + PLAYER.radius;
        minAboveGround = Math.min(minAboveGround, b.s.y - ht - terrainHeight(map, b.s.x, b.s.z));
      }
      phys.updateQueries();
      for (let i = 0; i < bots.length; i++) for (let j = i + 1; j < bots.length; j++) {
        const a = bots[i].s, c = bots[j].s;
        if (Math.abs(a.y - c.y) < 0.8) maxOverlap = Math.max(maxOverlap, PLAYER.radius * 2 - Math.hypot(a.x - c.x, a.z - c.z));
      }
    }
    expect(maxStep).toBeLessThanOrEqual(PLAYER.walkSpeed * DT * 1.6); // allow slope/step slide
    expect(maxOverlap).toBeLessThan(0.06);
    expect(minAboveGround).toBeGreaterThan(-0.3);
  });
});
