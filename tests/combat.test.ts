import { beforeAll, describe, expect, it } from 'vitest';
import {
  BTN, DT, MAP, TICK_RATE, WEAPONS, GameSim, damageAt, fireInterval, hitboxOf, initPhysics, newMoveState, rayHitbox,
  sanitizeInput, zoneAt, outsideZone, ZONE_PHASES, halfTotalOf, type InputCmd, type SimPlayer,
} from '../shared/src';

beforeAll(async () => { await initPhysics(); });
const cmd = (p: Partial<InputCmd> = {}): InputCmd => ({ seq: 0, mx: 0, mz: 0, yaw: 0, pitch: 0, buttons: 0, ...p });

/** Put a player at an exact spot on open ground, standing. */
function place(sim: GameSim, p: SimPlayer, x: number, z: number) {
  const y = sim.phys.groundHeight(x, z) + halfTotalOf(p.move) + 0.02;
  Object.assign(p.move, { x, y, z, vy: 0, grounded: true });
  sim.phys.players.get(p.id)!.setTranslation({ x, y, z });
}
/** yaw/pitch from shooter's eye to a point */
function aimAt(sim: GameSim, s: SimPlayer, x: number, y: number, z: number) {
  const e = sim.eye(s);
  const dx = x - e.x, dy = y - e.y, dz = z - e.z;
  return { yaw: Math.atan2(-dx, -dz), pitch: Math.atan2(dy, Math.hypot(dx, dz)) };
}
/** Find an open flat area with clear line of sight over `len` metres along +x. */
function openLane(sim: GameSim, len: number) {
  for (const sp of sim.map.spawns) {
    const y0 = sim.phys.groundHeight(sp.x, sp.z), y1 = sim.phys.groundHeight(sp.x + len, sp.z);
    if (Math.abs(y1 - y0) > 1.5) continue;
    if (sim.los(sp.x, y0 + 1.5, sp.z, sp.x + len, y1 + 1.5, sp.z) && sim.los(sp.x, y0 + 0.6, sp.z, sp.x + len, y1 + 0.6, sp.z)) return { x: sp.x, z: sp.z };
  }
  throw new Error('no open lane');
}
function duel(dist: number) {
  const sim = new GameSim(MAP.seed, { zone: false });
  const a = sim.addPlayer(1, 'A', false), b = sim.addPlayer(2, 'B', false);
  const lane = openLane(sim, dist);
  place(sim, a, lane.x, lane.z); place(sim, b, lane.x + dist, lane.z);
  sim.phys.updateQueries();
  for (let i = 0; i < 5; i++) sim.step(); // settle
  return { sim, a, b };
}

describe('hit detection', () => {
  it('ray hits head sphere before body; misses beside the player', () => {
    const s = newMoveState(0, 0.9, 0);
    const hb = hitboxOf(s);
    const headHit = rayHitbox(0, hb.hy, 10, 0, 0, -1, hb, 100)!;
    expect(headHit.head).toBe(true);
    const bodyHit = rayHitbox(0, 0.9, 10, 0, 0, -1, hb, 100)!;
    expect(bodyHit.head).toBe(false);
    expect(bodyHit.t).toBeCloseTo(10 - 0.3, 1);
    expect(rayHitbox(0.8, 0.9, 10, 0, 0, -1, hb, 100)).toBeNull();
    expect(rayHitbox(0, 2.2, 10, 0, 0, -1, hb, 100)).toBeNull(); // over the head
  });

  it('crouching lowers the head hitbox', () => {
    const st = newMoveState(0, 0.9, 0); const cr = { ...st, crouch: true, y: 0.6 };
    expect(hitboxOf(cr).hy).toBeLessThan(hitboxOf(st).hy - 0.4);
  });

  it('a body shot at 20 m with the rifle registers and deals correct damage', () => {
    const { sim, a, b } = duel(20);
    const aim = aimAt(sim, a, b.move.x, b.move.y, b.move.z);
    sim.setInput(1, cmd({ ...aim, aimYaw: aim.yaw, aimPitch: aim.pitch, buttons: BTN.FIRE | BTN.SCOPE, weapon: 1 }));
    sim.step(); // switch delay may apply on first tick
    for (let i = 0; i < 12 && !sim.events.some((e) => e.t === 'hit'); i++) sim.step();
    const hit = sim.events.find((e) => e.t === 'hit');
    expect(hit).toBeTruthy();
    expect((100 - b.hp) + (50 - b.armor)).toBe((hit as { dmg: number }).dmg * Math.max(1, sim.events.filter((e) => e.t === "hit").length));
  });

  it('headshots deal the head multiplier', () => {
    const { sim, a, b } = duel(15);
    const hb = hitboxOf(b.move);
    const aim = aimAt(sim, a, hb.hx, hb.hy, hb.hz);
    sim.setInput(1, cmd({ ...aim, aimYaw: aim.yaw, aimPitch: aim.pitch, buttons: BTN.FIRE | BTN.SCOPE, weapon: 3 }));
    let hit;
    for (let i = 0; i < 20 && !hit; i++) { sim.step(); hit = sim.events.find((e) => e.t === 'hit'); }
    expect(hit && hit.t === 'hit' && hit.head).toBe(true);
    // sniper headshot kills through 50 armor
    expect(b.alive).toBe(false);
  });

  it('walls stop bullets: a target fully hidden behind a house wall takes no damage', () => {
    let tested = 0;
    for (const h of new GameSim(MAP.seed, { zone: false }).map.props.filter((p) => p.p.startsWith('house'))) {
      for (const lx of [-4, -2, 0, 2, 4]) {
        const sim = new GameSim(MAP.seed, { zone: false });
        const a = sim.addPlayer(1, 'A', false), b = sim.addPlayer(2, 'B', false);
        const c = Math.cos(h.rot), sn = Math.sin(h.rot);
        const W = (x: number, z: number) => ({ x: h.x + x * c + z * sn, z: h.z - x * sn + z * c });
        const pb = W(lx, 0), pa = W(lx, 14);
        Object.assign(b.move, { x: pb.x, y: h.y + 0.92, z: pb.z }); sim.phys.players.get(2)!.setTranslation({ x: pb.x, y: h.y + 0.92, z: pb.z });
        place(sim, a, pa.x, pa.z);
        sim.phys.updateQueries();
        const e = sim.eye(a), hb = hitboxOf(b.move);
        const pts = [[hb.hx, hb.hy, hb.hz], [hb.ax, hb.ay, hb.az], [hb.bx, hb.by, hb.bz], [hb.ax, (hb.ay + hb.by) / 2, hb.az]];
        const hidden = pts.every(([x, y, z]) => [-0.3, 0, 0.3].every((o) => !sim.los(e.x, e.y, e.z, x + o * c, y, z - o * sn)));
        if (!hidden) continue;
        const aim = aimAt(sim, a, hb.ax, (hb.ay + hb.by) / 2, hb.az);
        for (let i = 0; i < 90; i++) { sim.setInput(1, cmd({ ...aim, aimYaw: aim.yaw, aimPitch: aim.pitch, buttons: (i % 2 ? BTN.FIRE : 0) | BTN.SCOPE, weapon: 3, seq: i })); sim.step(); }
        expect(b.hp).toBe(100);
        expect(b.armor).toBe(50);
        tested++;
      }
    }
    expect(tested).toBeGreaterThan(3);
  });
});

describe('damage, armor, falloff', () => {
  it('falloff reduces damage with distance, never below the minimum', () => {
    const w = WEAPONS.rifle;
    expect(damageAt(w, 10, false)).toBe(w.damage);
    expect(damageAt(w, 100, false)).toBeLessThan(w.damage);
    expect(damageAt(w, 1000, false)).toBeCloseTo(w.damage * w.minDamageFrac, 5);
    expect(damageAt(w, 10, true)).toBeCloseTo(w.damage * w.headMult, 5);
  });

  it('armor absorbs half of the damage until depleted', () => {
    const { sim, a, b } = duel(8);
    b.armor = 10;
    const aim = aimAt(sim, a, b.move.x, b.move.y - 0.2, b.move.z);
    sim.setInput(1, cmd({ ...aim, aimYaw: aim.yaw, aimPitch: aim.pitch, buttons: BTN.FIRE | BTN.SCOPE, weapon: 0 }));
    let hit;
    for (let i = 0; i < 20 && !hit; i++) { sim.step(); hit = sim.events.find((e) => e.t === 'hit'); }
    expect(hit).toBeTruthy();
    const dmg = (hit as { dmg: number }).dmg;
    expect(b.armor).toBe(Math.max(0, 10 - Math.round(dmg * 0.5)));
    expect(b.hp).toBe(100 - (dmg - (10 - b.armor)));
  });
});

describe('anti-cheat: fire rate, ammo, aim', () => {
  it('rifle cannot exceed its fire rate and stops when the magazine is empty', () => {
    const { sim, a } = duel(30);
    a.weapons[1].reserve = 0;
    let shots = 0;
    sim.setInput(1, cmd({ yaw: Math.PI / 2, buttons: BTN.FIRE, weapon: 1 }));
    for (let i = 0; i < TICK_RATE * 5; i++) { sim.step(); shots += sim.events.filter((e) => e.t === 'shot').length; }
    expect(shots).toBe(WEAPONS.rifle.mag); // 30 then empty, no reserve
    expect(a.weapons[1].mag).toBe(0);
  });

  it('rate is exactly rpm-limited while holding fire', () => {
    const { sim, a } = duel(30);
    sim.setInput(1, cmd({ yaw: Math.PI / 2, buttons: BTN.FIRE, weapon: 1 }));
    for (let i = 0; i < 10; i++) sim.step(); // switch delay
    let shots = 0;
    for (let i = 0; i < TICK_RATE; i++) { sim.step(); shots += sim.events.filter((e) => e.t === 'shot').length; }
    expect(shots).toBe(Math.floor(TICK_RATE / fireInterval(WEAPONS.rifle)));
    void a;
  });

  it('semi-auto weapons need the trigger released between shots', () => {
    const { sim } = duel(30);
    sim.setInput(1, cmd({ yaw: Math.PI / 2, buttons: BTN.FIRE, weapon: 0 }));
    let shots = 0;
    for (let i = 0; i < TICK_RATE * 2; i++) { sim.step(); shots += sim.events.filter((e) => e.t === 'shot').length; }
    expect(shots).toBe(1);
  });

  it('reload refills from reserve and takes the configured time', () => {
    const { sim, a } = duel(30);
    a.weapons[1].mag = 3;
    sim.setInput(1, cmd({ yaw: 0, buttons: BTN.RELOAD, weapon: 1 }));
    sim.step();
    sim.setInput(1, cmd({ yaw: 0, buttons: 0, weapon: 1 }));
    const ticks = Math.round(WEAPONS.rifle.reloadTime * TICK_RATE);
    for (let i = 0; i < ticks - 2; i++) sim.step();
    expect(a.weapons[1].mag).toBe(3);
    for (let i = 0; i < 4; i++) sim.step();
    expect(a.weapons[1].mag).toBe(30);
  });

  it('aim direction too far from the body orientation is clamped', () => {
    const s = sanitizeInput(cmd({ yaw: 0, pitch: 0, aimYaw: 2, aimPitch: 1 }));
    expect(s.aimYaw).toBeCloseTo(0.35, 5);
    expect(s.aimPitch).toBeCloseTo(0.35, 5);
  });
});

describe('safe zone', () => {
  it('shrinks over time toward a circle inside the previous one', () => {
    let prev = zoneAt(MAP.seed, 0);
    for (let t = 0; t < 400; t += 5) {
      const z = zoneAt(MAP.seed, t * TICK_RATE);
      expect(z.r).toBeLessThanOrEqual(prev.r + 1e-6);
      expect(Math.hypot(z.nx - z.cx, z.nz - z.cz) + z.nr).toBeLessThanOrEqual(z.r + 1e-3);
      prev = z;
    }
    const total = ZONE_PHASES.reduce((a, p) => a + p.wait + p.shrink, 0);
    expect(zoneAt(MAP.seed, (total + 1) * TICK_RATE).r).toBe(0);
  });

  it('damages players outside, not inside', () => {
    const sim = new GameSim(MAP.seed);
    const a = sim.addPlayer(1, 'in', false), b = sim.addPlayer(2, 'out', false);
    // jump to a time when the zone is small
    const t = (ZONE_PHASES[0].wait + ZONE_PHASES[0].shrink + 1) * TICK_RATE;
    sim.tick = t - (t % TICK_RATE);
    const z = zoneAt(MAP.seed, sim.tick);
    place(sim, a, z.cx, z.cz);
    const ang = Math.atan2(-z.cz, -z.cx);
    const ox = z.cx + Math.cos(ang) * (z.r + 8), oz = z.cz + Math.sin(ang) * (z.r + 8);
    place(sim, b, ox, oz);
    expect(outsideZone(z, b.move.x, b.move.z)).toBe(true);
    for (let i = 0; i < TICK_RATE * 3 + 1; i++) sim.step();
    expect(a.hp).toBe(100);
    expect(b.hp).toBeLessThan(100);
    expect(b.armor).toBe(50); // zone ignores armor
  });
});

describe('full offline match', () => {
  it('16 bots with the zone produce exactly one winner, nobody falls out of the world', () => {
    const sim = new GameSim(MAP.seed);
    for (let i = 0; i < 16; i++) sim.addPlayer(i, `bot${i}`, true);
    const total = ZONE_PHASES.reduce((a, p) => a + p.wait + p.shrink, 0) + 60;
    let kills = 0, belowGround = 0;
    for (let t = 0; t < total * TICK_RATE && sim.winner === null; t++) {
      sim.step();
      kills += sim.events.filter((e) => e.t === 'kill').length;
      for (const p of sim.players.values()) if (p.alive && p.move.y < sim.phys.groundHeight(p.move.x, p.move.z) - 0.5) belowGround++;
    }
    console.log(`match length ${(sim.tick / TICK_RATE).toFixed(0)} s, first kill handled`);
    expect(sim.tick / TICK_RATE).toBeGreaterThan(100); // bots are not instant aimbots
    expect(sim.winner).not.toBeNull();
    expect(sim.alive).toBe(1);
    expect(kills).toBe(15);
    expect(belowGround).toBe(0);
    const places = [...sim.players.values()].map((p) => p.place).sort((a, b) => a - b);
    expect(places).toEqual(Array.from({ length: 16 }, (_, i) => i + 1));
  }, 120000);
});
