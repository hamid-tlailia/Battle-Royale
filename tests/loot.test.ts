import { beforeAll, describe, expect, it } from 'vitest';
import {
  BTN, DT, LOOT, MAP, TICK_RATE, WEAPONS, GameSim, halfTotalOf, initPhysics, simulateMove, type InputCmd, type SimPlayer,
} from '../shared/src';

beforeAll(async () => { await initPhysics(); });
const cmd = (p: Partial<InputCmd> = {}): InputCmd => ({ seq: 0, mx: 0, mz: 0, yaw: 0, pitch: 0, buttons: 0, ...p });

function place(sim: GameSim, p: SimPlayer, x: number, z: number) {
  const y = sim.phys.groundHeight(x, z) + halfTotalOf(p.move) + 0.02;
  Object.assign(p.move, { x, y, z, vx: 0, vz: 0, vy: 0, grounded: true });
  sim.phys.players.get(p.id)!.setTranslation({ x, y, z });
}
function openSpot(sim: GameSim, i = 0) {
  for (const sp of sim.map.spawns.slice(i)) {
    const ok = [0, 1, 2, 3, 4, 5, 6].every((k) => sim.los(sp.x, sim.phys.groundHeight(sp.x, sp.z) + 1.2, sp.z, sp.x + k * 2, sim.phys.groundHeight(sp.x + k * 2, sp.z) + 1.2, sp.z));
    if (ok) return sp;
  }
  throw new Error('no open spot');
}
function live(sim: GameSim) { sim.startMatch(); }

describe('loot', () => {
  it('the map has ground loot, including inside houses', () => {
    const sim = new GameSim(MAP.seed);
    expect(sim.loot.size).toBeGreaterThan(80);
    const kinds = new Set([...sim.loot.values()].map((l) => l.kind));
    for (const k of ['weapon', 'ammo', 'med', 'armor', 'scope', 'frost']) expect(kinds.has(k as never)).toBe(true);
    const inHouses = [...sim.loot.values()].filter((l) => sim.map.props.some((h) => h.p.startsWith('house') && Math.hypot(h.x - l.x, h.z - l.z) < 5)).length;
    expect(inHouses).toBeGreaterThan(15);
  });

  it('players land unarmed and cannot shoot until they pick up a gun', () => {
    const sim = new GameSim(MAP.seed, { loot: false, zone: false });
    const p = sim.addPlayer(1, 'A', false);
    expect(p.weapons.every((w) => !w.owned)).toBe(true);
    expect(p.armor).toBe(0);
    let shots = 0;
    sim.setInput(1, cmd({ buttons: BTN.FIRE, weapon: 1 }));
    for (let i = 0; i < 30; i++) { sim.step(); shots += sim.events.filter((e) => e.t === 'shot').length; }
    expect(shots).toBe(0);
  });

  it('walking over a weapon picks it up, equips it, and ammo fills the reserve up to the cap', () => {
    const sim = new GameSim(MAP.seed, { loot: false, zone: false });
    const p = sim.addPlayer(1, 'A', false);
    const sp = openSpot(sim);
    place(sim, p, sp.x, sp.z);
    sim.addLoot({ kind: 'weapon', item: 'shotgun', amount: 1, x: sp.x + 3, y: p.move.y - 0.9, z: sp.z }, false);
    for (let i = 0; i < 3; i++) sim.addLoot({ kind: 'ammo', item: 'shotgun', amount: LOOT.ammoBox.shotgun, x: sp.x + 3.2, y: p.move.y - 0.9, z: sp.z }, false);
    const yaw = -Math.PI / 2; // face +x
    for (let i = 0; i < 40; i++) { sim.setInput(1, cmd({ mz: 1, yaw, seq: i, buttons: BTN.AUTOPICK })); sim.step(); }
    const sg = p.weapons[2];
    expect(sg.owned).toBe(true);
    expect(sg.mag).toBe(WEAPONS.shotgun.mag);
    expect(p.slot).toBe(2);
    expect(sg.reserve).toBe(Math.min(LOOT.maxReserve.shotgun, 3 * LOOT.ammoBox.shotgun));
  });

  it('auto pick-up off: walking over loot takes nothing; the loot menu takes one item within reach only', () => {
    const sim = new GameSim(MAP.seed, { loot: false, zone: false });
    const p = sim.addPlayer(1, 'A', false);
    const sp = openSpot(sim);
    place(sim, p, sp.x, sp.z);
    const y = p.move.y - 0.9;
    const near = sim.addLoot({ kind: 'weapon', item: 'rifle', amount: 1, x: sp.x + 0.5, y, z: sp.z }, false);
    const far = sim.addLoot({ kind: 'med', amount: 1, x: sp.x + 6, y, z: sp.z }, false);
    for (let i = 0; i < 10; i++) { sim.setInput(1, cmd({ seq: i })); sim.step(); }
    expect(p.weapons[1].owned).toBe(false);
    sim.setInput(1, cmd({ seq: 20, pick: far.id })); sim.step();
    expect(p.meds).toBe(0); // out of reach
    sim.setInput(1, cmd({ seq: 21, pick: near.id })); sim.step();
    expect(p.weapons[1].owned).toBe(true);
    expect(sim.loot.has(near.id)).toBe(false);
  });

  it('auto pick-up stops taking medkits at the cap (5)', () => {
    const sim = new GameSim(MAP.seed, { loot: false, zone: false });
    const p = sim.addPlayer(1, 'A', false);
    const sp = openSpot(sim);
    place(sim, p, sp.x, sp.z);
    for (let k = 0; k < 7; k++) sim.addLoot({ kind: 'med', amount: 1, x: sp.x + 0.3, y: p.move.y - 0.9, z: sp.z }, false);
    for (let i = 0; i < 10; i++) { sim.setInput(1, cmd({ seq: i, buttons: BTN.AUTOPICK })); sim.step(); }
    expect(p.meds).toBe(LOOT.maxMeds);
    expect([...sim.loot.values()].filter((l) => l.kind === 'med').length).toBe(2);
  });

  it('a medkit heals after its use time; shooting cancels it', () => {
    const sim = new GameSim(MAP.seed, { loot: false, zone: false, armed: true });
    live(sim);
    const p = sim.addPlayer(1, 'A', false);
    p.hp = 30; p.meds = 2;
    sim.setInput(1, cmd({ buttons: BTN.HEAL })); sim.step();
    sim.setInput(1, cmd());
    for (let i = 0; i < LOOT.medTime * TICK_RATE - 3; i++) sim.step();
    expect(p.hp).toBe(30);
    for (let i = 0; i < 5; i++) sim.step();
    expect(p.hp).toBe(30 + LOOT.medHeal);
    expect(p.meds).toBe(1);
    // second one, interrupted by firing
    sim.setInput(1, cmd({ buttons: BTN.HEAL, seq: 1 })); sim.step();
    sim.setInput(1, cmd({ buttons: BTN.FIRE, seq: 2, weapon: 1 })); sim.step();
    sim.setInput(1, cmd({ seq: 3 }));
    for (let i = 0; i < LOOT.medTime * TICK_RATE + 5; i++) sim.step();
    expect(p.hp).toBe(30 + LOOT.medHeal);
    expect(p.meds).toBe(1);
  });

  it('dead players drop their inventory on the ground', () => {
    const sim = new GameSim(MAP.seed, { loot: false, zone: false, armed: true });
    live(sim);
    const a = sim.addPlayer(1, 'A', false), b = sim.addPlayer(2, 'B', false);
    void a;
    b.meds = 2;
    sim.removePlayer(2); // eliminated
    const near = [...sim.loot.values()].filter((l) => Math.hypot(l.x - b.move.x, l.z - b.move.z) < 2);
    expect(near.filter((l) => l.kind === 'weapon').length).toBe(4);
    expect(near.some((l) => l.kind === 'med' && l.amount === 2)).toBe(true);
  });
});

describe('ice wall', () => {
  function setup() {
    const sim = new GameSim(MAP.seed, { loot: false, zone: false, armed: true });
    live(sim);
    const a = sim.addPlayer(1, 'A', false), b = sim.addPlayer(2, 'B', false);
    const sp = openSpot(sim);
    place(sim, a, sp.x, sp.z); place(sim, b, sp.x + 12, sp.z);
    a.move.yaw = -Math.PI / 2;
    sim.phys.updateQueries();
    for (let i = 0; i < 3; i++) { sim.setInput(1, cmd({ yaw: -Math.PI / 2 })); sim.step(); }
    return { sim, a, b };
  }

  it('is placed in front of the player and consumes one charge', () => {
    const { sim, a } = setup();
    const before = a.frost;
    sim.setInput(1, cmd({ yaw: -Math.PI / 2, buttons: BTN.FROST })); sim.step();
    expect(sim.walls.size).toBe(1);
    expect(a.frost).toBe(before - 1);
    const w = [...sim.walls.values()][0];
    expect(w.x - a.move.x).toBeGreaterThan(1.5); // in front (+x)
  });

  it('blocks bullets, then breaks after enough damage', () => {
    const { sim, a, b } = setup();
    sim.setInput(1, cmd({ yaw: -Math.PI / 2, buttons: BTN.FROST })); sim.step();
    // b shoots at a through the wall
    const eb = sim.eye(b), ea = sim.eye(a);
    const yaw = Math.atan2(-(ea.x - eb.x), -(ea.z - eb.z)), pitch = Math.atan2(ea.y - 0.3 - eb.y, Math.hypot(ea.x - eb.x, ea.z - eb.z));
    let hitsOnA = 0, i = 0;
    while (sim.walls.size && i < 400) {
      sim.setInput(2, cmd({ yaw, pitch, aimYaw: yaw, aimPitch: pitch, buttons: BTN.FIRE | BTN.SCOPE, weapon: 1, seq: i++ }));
      sim.setInput(1, cmd({ yaw: -Math.PI / 2, seq: i }));
      sim.step();
      if (b.weapons[1].mag === 0 && !b.reloadEndTick) b.weapons[1].mag = 30;
      hitsOnA += sim.events.filter((e) => e.t === 'hit' && e.target === 1).length;
    }
    expect(hitsOnA).toBe(0); // nothing went through while it stood
    expect(sim.walls.size).toBe(0);
    expect(i).toBeGreaterThan(LOOT.frostHp / WEAPONS.rifle.damage - 2); // took ~hp/damage bullets
  });

  it('blocks movement and melts after its lifetime', () => {
    const { sim, a } = setup();
    sim.setInput(1, cmd({ yaw: -Math.PI / 2, buttons: BTN.FROST })); sim.step();
    const w = [...sim.walls.values()][0];
    for (let i = 0; i < 60; i++) { sim.setInput(1, cmd({ yaw: -Math.PI / 2, mz: 1, seq: i })); sim.step(); }
    expect(a.move.x).toBeLessThan(w.x - LOOT.frostThick / 2 - 0.3); // stopped in front of it
    for (let i = 0; i < LOOT.frostLife * TICK_RATE; i++) sim.step();
    expect(sim.walls.size).toBe(0);
    void simulateMove; void DT;
  });
});
