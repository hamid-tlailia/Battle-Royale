import { beforeAll, describe, expect, it } from 'vitest';
import { BTN, MAP, TICK_RATE, GameSim, initPhysics, planePath, halfTotalOf, DROP, type InputCmd } from '../shared/src';

beforeAll(async () => { await initPhysics(); });
const cmd = (p: Partial<InputCmd> = {}): InputCmd => ({ seq: 0, mx: 0, mz: 0, yaw: 0, pitch: 0, buttons: 0, ...p });

describe('sky drop', () => {
  it('everyone boards the plane, jumps over the valley, glides, opens the parachute and lands safely', () => {
    const sim = new GameSim(MAP.seed, { lobby: true });
    for (let i = 0; i < 12; i++) sim.addPlayer(i, `b${i}`, true);
    const human = sim.addPlayer(99, 'me', false);
    sim.startMatch();
    expect([...sim.players.values()].every((p) => p.move.air === 1)).toBe(true);
    const path = planePath(sim.seed);
    let sawChute = false, maxAirTicks = 0;
    for (let t = 0; t < path.ticks + 60 * TICK_RATE; t++) {
      // the human jumps mid-route and dives forward
      sim.setInput(99, cmd({ seq: t, buttons: t === Math.floor(path.ticks * 0.5) ? BTN.JUMP : 0, mz: human.move.air >= 2 ? 1 : 0 }));
      sim.step();
      for (const p of sim.players.values()) if (p.move.air === 3) sawChute = true;
      if ([...sim.players.values()].some((p) => p.move.air !== 0)) maxAirTicks = t;
    }
    expect(sawChute).toBe(true);
    for (const p of sim.players.values()) {
      expect(p.move.air).toBe(0);
      expect(Math.hypot(p.move.x, p.move.z)).toBeLessThan(MAP.playRadius);
      const feet = p.move.y - halfTotalOf(p.move);
      expect(feet).toBeGreaterThan(sim.phys.groundHeight(p.move.x, p.move.z) - 0.4); // not under the terrain
    }
    expect(maxAirTicks / TICK_RATE).toBeLessThan(60); // whole drop done within a minute
  }, 60000);

  it('gliding reaches 170+ m from the jump point; players who jump together land together', () => {
    const sim = new GameSim(MAP.seed, { lobby: true, zone: false });
    const a = sim.addPlayer(1, 'A', false), b = sim.addPlayer(2, 'B', false);
    sim.startMatch();
    const path = planePath(sim.seed);
    const jumpT = Math.floor(path.ticks * 0.45);
    let ja: { x: number; z: number } | null = null, landA = -1, landB = -1;
    for (let t = 0; t < path.ticks + 70 * TICK_RATE; t++) {
      const yawOut = ja ? Math.atan2(-(0 - ja.x), -(0 - ja.z)) : 0; // A glides toward the centre (stays inside the valley)
      sim.setInput(1, cmd({ seq: t, buttons: t === jumpT ? BTN.JUMP : 0, mz: a.move.air >= 2 ? 1 : 0, yaw: yawOut }));
      sim.setInput(2, cmd({ seq: t, buttons: t === jumpT ? BTN.JUMP : 0 })); // B falls straight down
      sim.step();
      if (!ja && a.move.air === 2) ja = { x: a.move.x, z: a.move.z };
      if (landA < 0 && ja && a.move.air === 0) landA = t;
      if (landB < 0 && t > jumpT + 5 && b.move.air === 0) landB = t;
    }
    const reach = Math.hypot(a.move.x - ja!.x, a.move.z - ja!.z);
    console.log(`glide reach ${reach.toFixed(0)} m, A lands after ${((landA - jumpT) / TICK_RATE).toFixed(1)} s, B after ${((landB - jumpT) / TICK_RATE).toFixed(1)} s`);
    expect(reach).toBeGreaterThan(170); // steering also descends now (no flat gliding), still covers most of the map from the flight line
    expect(Math.abs(landA - landB) / TICK_RATE).toBeLessThan(6); // gliding far does not make you land much later
  });

  it('nobody can be shot while inside the plane', () => {
    const sim = new GameSim(MAP.seed, { lobby: true, armed: true, loot: false });
    const a = sim.addPlayer(1, 'A', false), b = sim.addPlayer(2, 'B', false);
    sim.startMatch();
    void a; void b;
    let hits = 0;
    for (let t = 0; t < 60; t++) { sim.setInput(1, cmd({ seq: t, buttons: t % 2 ? BTN.FIRE : 0, pitch: -1 })); sim.step(); hits += sim.events.filter((e) => e.t === 'hit' || e.t === 'shot').length; }
    expect(hits).toBe(0);
  });
});

describe('backpack & holster', () => {
  it('dropping an item puts it on the ground and it is not instantly picked up again', () => {
    const sim = new GameSim(MAP.seed, { armed: true, loot: false, zone: false });
    const p = sim.addPlayer(1, 'A', false);
    p.meds = 2;
    sim.setInput(1, cmd({ seq: 1, dropItem: DROP.med })); sim.step();
    expect(p.meds).toBe(1);
    expect([...sim.loot.values()].some((l) => l.kind === 'med')).toBe(true);
    sim.setInput(1, cmd({ seq: 2, dropItem: DROP.weapon(1) })); sim.step();
    expect(p.weapons[1].owned).toBe(false);
    for (let i = 0; i < 20; i++) { sim.setInput(1, cmd({ seq: 3 + i, mz: -1 })); sim.step(); } // walk back over the items
    expect(p.meds).toBe(1);
    expect(p.weapons[1].owned).toBe(false);
  });

  it('a holstered weapon cannot fire; drawing it again works', () => {
    const sim = new GameSim(MAP.seed, { armed: true, loot: false, zone: false });
    const p = sim.addPlayer(1, 'A', false);
    let shots = 0;
    sim.setInput(1, cmd({ seq: 1, weapon: 4 })); sim.step();
    expect(p.holstered).toBe(true);
    for (let i = 0; i < 20; i++) { sim.setInput(1, cmd({ seq: 2 + i, weapon: 4, buttons: BTN.FIRE })); sim.step(); shots += sim.events.filter((e) => e.t === 'shot').length; }
    expect(shots).toBe(0);
    for (let i = 0; i < 20; i++) { sim.setInput(1, cmd({ seq: 30 + i, weapon: 1, buttons: BTN.FIRE })); sim.step(); shots += sim.events.filter((e) => e.t === 'shot').length; }
    expect(p.holstered).toBe(false);
    expect(shots).toBeGreaterThan(0);
  });
});
