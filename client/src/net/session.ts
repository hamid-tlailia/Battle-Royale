import { Client as ColyseusClient, type Room } from '@colyseus/sdk';
import {
  DT, MSG, TICK_RATE, GameSim, applySelf, quantizeInput, decodeSnapshot, encodeInputs, simulateMove, syncCollider, weaponLogic,
  type FrostWall, type InputCmd, type LobbyInfo, type LootItem, type RemoteState, type RosterEntry, type SimEvent, type Snapshot, type WeaponId,
} from '@lw/shared';

/**
 * Simulated bad network (testing): latency is one-way per direction; "loss" on a reliable
 * WebSocket (TCP) shows up as a retransmission delay, so a lost packet arrives ~RTT later
 * and holds back everything behind it (head-of-line blocking).
 */
export interface NetConditions { latencyMs: number; jitterMs: number; loss: number }

class Conditioner {
  private queue: { due: number; fn: () => void }[] = [];
  private lastDue = 0;
  private timer: ReturnType<typeof setTimeout> | null = null;
  constructor(private c: NetConditions) {}
  run(fn: () => void) {
    if (this.c.latencyMs <= 0 && this.c.loss <= 0) { fn(); return; }
    let delay = this.c.latencyMs + (Math.random() * 2 - 1) * this.c.jitterMs;
    if (Math.random() < this.c.loss) delay += this.c.latencyMs * 2 + 50; // TCP retransmit
    const due = Math.max(performance.now() + delay, this.lastDue); // in-order delivery like TCP
    this.lastDue = due;
    this.queue.push({ due, fn });
    this.schedule();
  }
  /** One timer, FIFO queue: messages can never overtake each other (setTimeout rounding could). */
  private schedule() {
    if (this.timer || !this.queue.length) return;
    this.timer = setTimeout(() => {
      this.timer = null;
      const now = performance.now();
      while (this.queue.length && this.queue[0].due <= now + 0.5) this.queue.shift()!.fn();
      this.schedule();
    }, Math.max(0, this.queue[0].due - performance.now()));
  }
}

interface Buffered { tick: number; s: RemoteState; at: number }

/**
 * Online match state on the client:
 *  - my player: client-side prediction (same shared simulateMove/weaponLogic as the server)
 *    + reconciliation when the authoritative snapshot arrives (replay unacknowledged inputs)
 *  - other players: interpolated ~100 ms in the past between snapshots
 *  - every input carries the render tick so the server can rewind hitboxes (lag compensation)
 */
export class NetSession {
  room!: Room;
  sim!: GameSim;
  myId = -1;
  seed = 0;
  connected = false;
  phase: Snapshot['phase'] = 'lobby';
  lobby: LobbyInfo = { phase: 'lobby', countdown: 0, humans: 0, max: 16, private: false, hostId: -1 };
  roster: RosterEntry[] = [];
  /** estimated server tick (float), advanced locally, nudged by snapshots */
  serverTick = 0;
  renderTick = 0;
  interpTicks = 3;
  ping = 0;
  /** smoothed visual correction applied to my rendered position after reconciliation */
  correction = { x: 0, y: 0, z: 0 };
  stats = { corrections: 0, bigCorrections: 0, snapshots: 0, lastSnapGapMs: 0, maxCorrection: 0, sumCorrection: 0 };
  onEvents: (e: SimEvent[]) => void = () => {};
  onCosmeticShot: (w: WeaponId) => void = () => {};
  onReady: () => void = () => {};
  onClose: (code: number) => void = () => {};

  private inC: Conditioner;
  private outC: Conditioner;
  private pending: InputCmd[] = [];
  private seq = 0;
  private buffers = new Map<number, Buffered[]>();
  private lastSnapAt = 0;
  private jitter = 0;
  private ackTick = 0;
  private gotFirstSnap = false;
  private latest: Snapshot | null = null;
  private tmpMove = {} as GameSim['players'] extends Map<number, infer P> ? P extends { move: infer M } ? M : never : never;
  /** largest recent gap between snapshots (ticks), decays slowly */
  private spike = 1;

  constructor(private url: string, conditions: NetConditions = { latencyMs: 0, jitterMs: 0, loss: 0 }) {
    this.inC = new Conditioner(conditions);
    this.outC = new Conditioner(conditions);
  }

  /** mode: quick match (joinOrCreate), 'create' a private friends room, or join a room id. */
  async join(name: string, opts: Record<string, unknown> = {}, roomId?: string, create = false): Promise<void> {
    const client = new ColyseusClient(this.url);
    this.room = roomId ? await client.joinById(roomId, { name, ...opts })
      : create ? await client.create('match', { name, ...opts, private: true })
      : await client.joinOrCreate('match', { name, ...opts });
    this.room.onMessage(MSG.WELCOME, (m: { id: number; seed: number; tick: number }) => this.inC.run(() => {
      this.myId = m.id; this.seed = m.seed; this.serverTick = m.tick;
      this.sim = new GameSim(m.seed, { lobby: true, loot: false }); // loot comes from the server
      for (const r of this.roster) this.ensurePlayer(r.id, r.name, r.skin);
      this.connected = true;
    }));
    this.room.onMessage(MSG.ROSTER, (r: RosterEntry[]) => this.inC.run(() => {
      this.roster = r;
      if (this.sim) for (const p of r) this.ensurePlayer(p.id, p.name, p.skin);
    }));
    this.room.onMessage(MSG.LOBBY, (l: LobbyInfo) => this.inC.run(() => { this.lobby = l; }));
    this.room.onMessage(MSG.SNAP, (b: Uint8Array) => this.inC.run(() => this.onSnapshot(decodeSnapshot(b))));
    this.room.onMessage(MSG.WORLD, (w: { loot: LootItem[]; walls: Omit<FrostWall, 'handle'>[] }) => this.inC.run(() => { this.sim?.setWorldItems(w.loot, w.walls); }));
    this.room.onMessage(MSG.EVENTS, (e: SimEvent[]) => this.inC.run(() => { this.applyWorldEvents(e); this.onEvents(e); }));
    this.room.onMessage(MSG.PONG, (m: { t: number }) => this.inC.run(() => { const rtt = performance.now() - m.t; this.ping = this.ping ? this.ping * 0.8 + rtt * 0.2 : rtt; }));
    this.room.onLeave((code: number) => { this.connected = false; this.onClose(code); });
    setInterval(() => { if (this.connected) this.outC.run(() => this.room.send(MSG.PING, { t: performance.now() })); }, 1000);
  }

  leave() { void this.room?.leave(); }

  private ensurePlayer(id: number, name: string, skin = 0) {
    const p = this.sim.players.get(id) ?? this.sim.addPlayer(id, name, false);
    p.name = name; p.skin = skin;
  }

  /** Keep the client copy of loot and ice walls (colliders: my prediction collides with walls). */
  private applyWorldEvents(events: SimEvent[]) {
    if (!this.sim) return;
    for (const e of events) {
      if (e.t === 'loot+') this.sim.loot.set(e.item.id, e.item);
      else if (e.t === 'loot-') this.sim.loot.delete(e.id);
      else if (e.t === 'wall+') this.sim.setWorldItems([...this.sim.loot.values()], [e.wall]);
      else if (e.t === 'wall-') this.sim.removeWall(e.id, e.broken, false);
    }
  }

  /** Host of a friends room starts the match early. */
  startNow() { this.outC.run(() => this.room.send(MSG.START, {})); }

  get me() { return this.sim?.players.get(this.myId); }

  // ------------------------------------------------------------------ snapshots
  private lastSnapTick = -1;
  private onSnapshot(s: Snapshot) {
    if (!this.sim) return;
    // Never apply an older snapshot after a newer one (would roll my player back).
    if (s.tick <= this.lastSnapTick) return;
    this.lastSnapTick = s.tick;
    const now = performance.now();
    if (this.lastSnapAt) {
      const gap = now - this.lastSnapAt;
      this.stats.lastSnapGapMs = gap;
      this.jitter = this.jitter * 0.9 + Math.abs(gap - 1000 / TICK_RATE) * 0.1;
    }
    this.lastSnapAt = now;
    this.stats.snapshots++;
    this.latest = s;
    this.phase = s.phase;
    this.sim.phase = s.phase;
    this.sim.startTick = s.startTick;
    this.sim.tick = s.tick;
    // clock: keep our estimate close to the newest snapshot tick
    const err = s.tick - this.serverTick;
    if (!this.gotFirstSnap || Math.abs(err) > 8) this.serverTick = s.tick; else this.serverTick += err * 0.1;
    // Interpolation buffer adapts to the network: it covers the largest recent gap between snapshots
    // (packet loss = retransmission delay) so remote players are interpolated, not guessed.
    const gapTicks = this.stats.lastSnapGapMs * TICK_RATE / 1000;
    this.spike = Math.max(this.spike * 0.985, gapTicks);
    this.interpTicks = Math.min(8, Math.max(3, this.spike + 1 + (this.jitter * TICK_RATE) / 1000));

    for (const r of s.others) {
      let b = this.buffers.get(r.id);
      if (!b) { b = []; this.buffers.set(r.id, b); }
      if (b.length && b[b.length - 1].tick >= s.tick) continue;
      b.push({ tick: s.tick, s: r, at: now });
      if (b.length > 40) b.shift();
      const p = this.sim.players.get(r.id);
      if (p) {
        p.alive = r.alive; p.slot = r.slot;
        // keep the remote collider near its latest position so my prediction collides with it
        if (r.alive) { Object.assign(this.tmpMove, p.move, { x: r.x, y: r.y, z: r.z, crouch: r.crouch, ball: r.ball, prone: r.prone }); syncCollider(this.sim.phys, r.id, this.tmpMove); }
        else this.sim.phys.removePlayer(r.id);
      }
    }
    this.sim.phys.updateQueries();
    if (s.self && !this.me) this.ensurePlayer(this.myId, 'You');
    if (s.self) this.reconcile(s);
    this.gotFirstSnap = true;
    if (s.self && !this.readyFired) { this.readyFired = true; this.onReady(); }
  }
  private readyFired = false;


  private reconcile(s: Snapshot) {
    const me = this.me!;
    const before = { x: me.move.x, y: me.move.y, z: me.move.z };
    while (this.pending.length && this.pending[0].seq <= s.ack) this.pending.shift();
    applySelf(me, s.self!);
    me.ackSeq = s.ack;
    this.ackTick = s.tick;
    if (!me.alive) { this.sim.phys.removePlayer(this.myId); this.pending.length = 0; return; }
    syncCollider(this.sim.phys, this.myId, me.move);
    // replay inputs the server has not processed yet
    let t = s.tick;
    for (const input of this.pending) {
      simulateMove(this.sim.phys, this.myId, me.move, input, DT);
      if (this.phase === 'live') weaponLogic(me, input, t, () => {});
      t++;
    }
    const dx = before.x - me.move.x, dy = before.y - me.move.y, dz = before.z - me.move.z;
    const err = Math.hypot(dx, dy, dz);
    if (err > 0.01) { this.stats.corrections++; this.stats.sumCorrection += err; this.stats.maxCorrection = Math.max(this.stats.maxCorrection, err); }
    if (err > 2) { this.stats.bigCorrections++; this.correction = { x: 0, y: 0, z: 0 }; }
    else { this.correction.x += dx; this.correction.y += dy; this.correction.z += dz; }
  }

  // ------------------------------------------------------------------ local tick (30 Hz)
  /** Predict one tick locally and send the input. Returns the input used. */
  localTick(input: Omit<InputCmd, 'seq' | 'viewTick'>): InputCmd | null {
    const me = this.me;
    if (!this.connected || !me || !this.gotFirstSnap) return null;
    const cmd: InputCmd = quantizeInput({ ...input, seq: ++this.seq, viewTick: this.renderTick });
    if (me.alive) {
      this.pending.push(cmd);
      if (this.pending.length > 90) this.pending.shift();
      simulateMove(this.sim.phys, this.myId, me.move, cmd, DT);
      if (this.phase === 'live') weaponLogic(me, cmd, this.ackTick + this.pending.length, (w) => this.onCosmeticShot(w));
      else me.prevButtons = cmd.buttons;
    }
    const send = this.pending.slice(-3);
    const bytes = encodeInputs(send.length ? send : [cmd]);
    this.outC.run(() => { if (this.connected) this.room.sendBytes(MSG.INPUT, bytes); });
    return cmd;
  }

  // ------------------------------------------------------------------ per frame
  /** Advance clocks and write interpolated remote states into sim.players[*].move. */
  frame(dt: number) {
    if (!this.sim) return;
    this.serverTick += dt * TICK_RATE;
    this.renderTick = this.serverTick - this.interpTicks;
    const k = Math.min(1, dt * 8);
    this.correction.x -= this.correction.x * k; this.correction.y -= this.correction.y * k; this.correction.z -= this.correction.z * k;
    for (const [id, b] of this.buffers) {
      if (id === this.myId || !b.length) continue;
      const p = this.sim.players.get(id);
      if (!p) continue;
      const rt = this.renderTick;
      let a = b[0], c = b[b.length - 1], st: RemoteState;
      if (rt <= a.tick) st = a.s;
      else if (rt >= c.tick) {
        // extrapolate at most 150 ms using velocity
        const ex = Math.min(rt - c.tick, 0.15 * TICK_RATE) / TICK_RATE;
        st = { ...c.s, x: c.s.x + c.s.vx * ex, z: c.s.z + c.s.vz * ex };
      } else {
        for (let i = b.length - 1; i >= 0; i--) if (b[i].tick <= rt) { a = b[i]; c = b[i + 1]; break; }
        const f = (rt - a.tick) / (c.tick - a.tick);
        let dy = c.s.yaw - a.s.yaw; dy = Math.atan2(Math.sin(dy), Math.cos(dy));
        st = {
          ...a.s,
          x: a.s.x + (c.s.x - a.s.x) * f, y: a.s.y + (c.s.y - a.s.y) * f, z: a.s.z + (c.s.z - a.s.z) * f,
          yaw: a.s.yaw + dy * f, pitch: a.s.pitch + (c.s.pitch - a.s.pitch) * f,
          vx: a.s.vx + (c.s.vx - a.s.vx) * f, vz: a.s.vz + (c.s.vz - a.s.vz) * f,
          alive: f < 0.5 ? a.s.alive : c.s.alive, crouch: f < 0.5 ? a.s.crouch : c.s.crouch, ball: f < 0.5 ? a.s.ball : c.s.ball, prone: f < 0.5 ? a.s.prone : c.s.prone,
        };
      }
      const m = p.move;
      m.x = st.x; m.y = st.y; m.z = st.z; m.yaw = st.yaw; m.pitch = st.pitch; m.vx = st.vx; m.vz = st.vz;
      m.crouch = st.crouch; m.ball = st.ball; m.prone = st.prone; m.grounded = st.grounded;
      p.speed = Math.hypot(st.vx, st.vz);
    }
  }

  /** Latest authoritative alive count etc. */
  get snapshot() { return this.latest; }
}
