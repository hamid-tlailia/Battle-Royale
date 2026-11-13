import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { Server, matchMaker } from '@colyseus/core';
import { WebSocketTransport } from '@colyseus/ws-transport';
import { BTN, MAP, TICK_RATE, halfTotalOf, hitboxOf, initPhysics, type GameSim, type SimEvent } from '../shared/src';
import { MatchRoom } from '../server/src/MatchRoom';
import { NetSession, type NetConditions } from '../client/src/net/session';

/** "200 ms ping, 5% packet loss" as required: 100 ms each way, ±15 ms jitter, 5% retransmits. */
const BAD: NetConditions = { latencyMs: 100, jitterMs: 15, loss: 0.05 };
const PORT = 7900 + Math.floor(Math.random() * 90);
const URL_ = `http://127.0.0.1:${PORT}`;
let gameServer: Server;

beforeAll(async () => {
  await initPhysics();
  gameServer = new Server({ transport: new WebSocketTransport() });
  gameServer.define('match', MatchRoom);
  await gameServer.listen(PORT);
});
afterAll(async () => { await gameServer.gracefullyShutdown(false); });

const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms));

/** Drive a session at 30 Hz with an input function; also advances interpolation. */
async function drive(s: NetSession, seconds: number, inputFn: (t: number) => { mx?: number; mz?: number; yaw?: number; pitch?: number; buttons?: number; weapon?: number; aimYaw?: number; aimPitch?: number }) {
  const ticks = Math.round(seconds * TICK_RATE);
  let last = performance.now();
  for (let t = 0; t < ticks; t++) {
    const now = performance.now();
    s.frame((now - last) / 1000); last = now;
    const i = inputFn(t);
    s.localTick({ mx: i.mx ?? 0, mz: i.mz ?? 0, yaw: i.yaw ?? 0, pitch: i.pitch ?? 0, buttons: i.buttons ?? 0, weapon: i.weapon, aimYaw: i.aimYaw ?? i.yaw ?? 0, aimPitch: i.aimPitch ?? i.pitch ?? 0 });
    await sleep(1000 / TICK_RATE);
  }
}

async function joinPair(opts: Record<string, unknown>, cond: NetConditions) {
  const a = new NetSession(URL_, cond), b = new NetSession(URL_, cond);
  await a.join('A', { lobbySeconds: 1, fillTo: 2, seed: MAP.seed, armed: true, skyDrop: false, ...opts });
  await b.join('B', {}, a.room.roomId);
  const room = matchMaker.getLocalRoomById(a.room.roomId) as unknown as MatchRoom;
  // wait for welcome + first snapshots
  for (let i = 0; i < 100 && !(a.me && b.me && a.snapshot && b.snapshot); i++) { a.frame(0.03); b.frame(0.03); await sleep(30); }
  return { a, b, room };
}

function placeOnServer(sim: GameSim, id: number, x: number, z: number, yaw = 0) {
  const p = sim.players.get(id)!;
  const y = sim.phys.groundHeight(x, z) + halfTotalOf(p.move) + 0.02;
  Object.assign(p.move, { x, y, z, vx: 0, vz: 0, vy: 0, yaw });
  sim.phys.players.get(id)!.setTranslation({ x, y, z });
}

function openLane(sim: GameSim, len: number) {
  for (const sp of sim.map.spawns) {
    const y0 = sim.phys.groundHeight(sp.x, sp.z), y1 = sim.phys.groundHeight(sp.x + len, sp.z);
    if (Math.abs(y1 - y0) > 1) continue;
    let clear = true;
    for (const dz of [-4, -2, 0, 2, 4]) if (!sim.los(sp.x, y0 + 1.5, sp.z + dz, sp.x + len, y1 + 1.5, sp.z + dz) || !sim.los(sp.x, y0 + 0.7, sp.z + dz, sp.x + len, y1 + 0.7, sp.z + dz)) clear = false;
    if (clear) return sp;
  }
  throw new Error('no open lane');
}

describe('netcode under 200 ms ping + 5% loss', () => {
  it('client prediction matches the server: no rubber-banding while running, turning, jumping', async () => {
    const { a, b, room } = await joinPair({}, BAD);
    await sleep(1200); // lobby -> live
    // start in open ground (no rock edges to grind against: those make tiny solver differences)
    const lane = openLane(room.sim, 30);
    placeOnServer(room.sim, a.myId, lane.x, lane.z, -Math.PI / 2);
    await drive(a, 1, () => ({ yaw: -Math.PI / 2 }));
    Object.assign(a.stats, { bigCorrections: 0, maxCorrection: 0, sumCorrection: 0, corrections: 0 });
    const before = { ...a.stats };
    await drive(a, 6, (t) => ({
      mz: t % 60 < 45 ? 1 : 0.4, mx: t % 90 < 30 ? 0.6 : 0, yaw: Math.sin(t / 40) * 1.5,
      buttons: t % 75 === 10 ? BTN.JUMP : 0,
    }));
    await drive(a, 1, () => ({ yaw: Math.sin(6 * 30 / 40) * 1.5 })); // stand still and settle
    const server = room.sim.players.get(a.myId)!.move;
    const client = a.me!.move;
    const err = Math.hypot(server.x - client.x, server.y - client.y, server.z - client.z);
    const snaps = a.stats.snapshots - before.snapshots;
    const corr = a.stats.corrections - before.corrections;
    console.log(`prediction: final error ${err.toFixed(3)} m, corrections ${corr}/${snaps} snapshots, avg ${(a.stats.sumCorrection / Math.max(1, a.stats.corrections) * 100).toFixed(1)} cm, max ${(a.stats.maxCorrection * 100).toFixed(1)} cm, big ${a.stats.bigCorrections}`);
    expect(err).toBeLessThan(0.05);
    expect(a.stats.bigCorrections).toBe(0);
    expect(corr / snaps).toBeLessThan(0.02);
    expect(a.stats.maxCorrection).toBeLessThan(0.05);
    a.leave(); b.leave();
  }, 30000);

  async function shootingRun(lagComp: boolean) {
    const { a, b, room } = await joinPair({ lagCompensation: lagComp }, BAD);
    await sleep(1200);
    const sim = room.sim;
    const lane = openLane(sim, 15);
    placeOnServer(sim, a.myId, lane.x, lane.z, -Math.PI / 2);
    placeOnServer(sim, b.myId, lane.x + 15, lane.z);
    sim.players.get(b.myId)!.hp = 1e6; // keep the target alive for the whole test
    let shots = 0, hits = 0;
    // debug: compare what A rendered vs what the server rewound to
    const seen = new Map<number, { x: number; z: number }>(); // viewTick*100 -> B pos on A's screen
    const origAt = sim.hitboxAt.bind(sim);
    const diffs: number[] = [];
    const rewound: { x: number; y: number; z: number; t: number; now: number }[] = [];
    sim.hitboxAt = (id, t, out) => {
      const r = origAt(id, t, out);
      if (r && id === b.myId) rewound.push({ x: r.ax, y: (r.ay + r.by) / 2, z: r.az, t, now: sim.tick + 1 });
      const c = seen.get(Math.round(t * 100));
      if (r && c && id === b.myId) diffs.push(Math.hypot(r.ax - c.x, r.az - c.z));
      if (!r && id === b.myId) diffs.push(-1);
      return r;
    };
    (globalThis as unknown as { __seen: typeof seen }).__seen = seen;
    room.sim.events; // eslint
    const shotRays: number[][] = [];
    a.onEvents = (ev: SimEvent[]) => { for (const e of ev) { if (e.t === 'shot' && e.id === a.myId) { shots++; shotRays.push([e.ox, e.oy, e.oz, ...e.ends]); } if (e.t === 'hit' && e.attacker === a.myId) hits++; } };
    (globalThis as unknown as { __dbg: unknown }).__dbg = { rewound, shotRays };
    // B strafes left/right fast; A aims at where B *appears* on A's screen (interpolated), like a real player
    const bDrive = drive(b, 7, (t) => ({ mx: Math.floor(t / 20) % 2 ? 1 : -1, yaw: -Math.PI / 2 }));
    await drive(a, 7, (t) => {
      const tb = a.sim.players.get(b.myId)!;
      seen.set(Math.round(a.renderTick * 100), { x: tb.move.x, z: tb.move.z });
      const hb = hitboxOf(tb.move);
      const eye = a.sim.eye(a.me!);
      const tx = hb.ax, ty = (hb.ay + hb.by) / 2, tz = hb.az;
      const dx = tx - eye.x, dy = ty - eye.y, dz = tz - eye.z;
      const yaw = Math.atan2(-dx, -dz), pitch = Math.atan2(dy, Math.hypot(dx, dz));
      return { yaw, pitch, weapon: 0, buttons: BTN.SCOPE | (t > 20 && t % 8 < 2 ? BTN.FIRE : 0) };
    });
    await bDrive;
    await sleep(500);
    const out: string[] = [];
    shotRays.forEach((r, i) => {
      const w = rewound[i]; if (!w) return;
      const dx = r[3] - r[0], dy = r[4] - r[1], dz = r[5] - r[2], L = Math.hypot(dx, dy, dz);
      const px = w.x - r[0], py = w.y - r[1], pz = w.z - r[2];
      const t = (px * dx + py * dy + pz * dz) / L;
      const cx = r[0] + dx / L * t - w.x, cy = r[1] + dy / L * t - w.y, cz = r[2] + dz / L * t - w.z;
      out.push(`${Math.hypot(cx, cy, cz).toFixed(2)}@${(w.now - w.t).toFixed(1)}t L${L.toFixed(1)}`);
    });
    console.log('ray miss distance @rewind:', out.join(' '));
    console.log('rewind vs seen (m):', diffs.map((d) => d.toFixed(2)).join(' '), '| interp', a.interpTicks.toFixed(1));
    a.leave(); b.leave();
    return { shots, hits };
  }

  it('lag compensation: shots at what the shooter sees register (≥ 90%)', async () => {
    const r = await shootingRun(true);
    console.log(`lag comp ON: ${r.hits}/${r.shots} hits`);
    expect(r.shots).toBeGreaterThan(5);
    expect(r.hits / r.shots).toBeGreaterThanOrEqual(0.9);
  }, 30000);

  it('without lag compensation the same shots miss a strafing target (proves it matters)', async () => {
    const r = await shootingRun(false);
    console.log(`lag comp OFF: ${r.hits}/${r.shots} hits`);
    expect(r.hits / Math.max(1, r.shots)).toBeLessThan(0.6);
  }, 30000);

  it('anti-cheat: flooding inputs does not make a player faster than the server tick rate allows', async () => {
    const { a, b, room } = await joinPair({}, { latencyMs: 0, jitterMs: 0, loss: 0 });
    await sleep(1200);
    const p = room.sim.players.get(a.myId)!;
    const lane = openLane(room.sim, 30);
    placeOnServer(room.sim, a.myId, lane.x, lane.z, -Math.PI / 2);
    const x0 = p.move.x, z0 = p.move.z;
    // send 5 inputs per tick for 2 seconds (speed hack attempt)
    for (let t = 0; t < 60; t++) {
      for (let k = 0; k < 5; k++) a.localTick({ mx: 0, mz: 1, yaw: -Math.PI / 2, pitch: 0, buttons: 0 });
      await sleep(1000 / TICK_RATE);
    }
    await sleep(300);
    const moved = Math.hypot(p.move.x - x0, p.move.z - z0);
    console.log(`flood: moved ${moved.toFixed(2)} m in ~2.3 s`);
    expect(moved).toBeGreaterThan(5); // inputs were applied
    expect(moved).toBeLessThan(7.5 * 2.4); // max sprint speed * time, not 5x
    a.leave(); b.leave();
  }, 20000);
});
