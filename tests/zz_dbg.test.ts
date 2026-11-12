import { it } from 'vitest';
import { BTN, DT, MAP, PhysicsWorld, generateMap, initPhysics, newMoveState, simulateMove, type InputCmd } from '../shared/src';
it('determinism', async () => {
  await initPhysics(); const map = generateMap(MAP.seed);
  const S = new PhysicsWorld(map), C = new PhysicsWorld(map);
  const sp = S.findSpawn(map.spawns[0].x, map.spawns[0].z);
  S.addPlayer(1, sp.x, sp.y, sp.z); C.addPlayer(1, sp.x, sp.y, sp.z);
  const a = newMoveState(sp.x, sp.y, sp.z), b = newMoveState(sp.x, sp.y, sp.z);
  let maxD = 0, first = -1;
  for (let t = 0; t < 240; t++) {
    const inp: InputCmd = { seq: t, mz: t % 60 < 45 ? 1 : 0.4, mx: t % 90 < 30 ? 0.6 : 0, yaw: Math.sin(t / 40) * 1.5, pitch: 0, buttons: t % 75 === 10 ? BTN.JUMP : 0 };
    simulateMove(S, 1, a, inp, DT); S.updateQueries();
    simulateMove(C, 1, b, inp, DT); if (t % 7 === 0) C.updateQueries();
    const d = Math.hypot(a.x - b.x, a.y - b.y, a.z - b.z);
    if (d > 1e-4 && first < 0) first = t;
    maxD = Math.max(maxD, d);
  }
  console.log('max divergence', maxD.toFixed(5), 'first tick', first);
});
