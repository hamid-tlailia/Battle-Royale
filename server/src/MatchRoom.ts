import { Room, type Client } from '@colyseus/core';
import {
  DT, MSG, SKIN_COUNT, isMapId, type MapId, TICK_RATE, GameSim, compactEvents, decodeInputs, encodeSnapshot, initPhysics, type InputCmd,
} from '@lw/shared';

export interface MatchOptions {
  /** map id (rooms are matched by map) */
  map?: string;
  /** seconds to wait in the lobby after the first human joins */
  lobbySeconds?: number;
  /** total players once the match starts (bots fill the rest) */
  fillTo?: number;
  seed?: number;
  /** friends room: only joinable with its code; the host starts it */
  private?: boolean;
  /** tests only: disable rewind to prove it matters */
  lagCompensation?: boolean;
  /** tests only: start with all weapons */
  armed?: boolean;
  /** tests only: start on the ground instead of the drop plane */
  skyDrop?: boolean;
}

interface Human { id: number; client: Client; queue: InputCmd[]; lastSeq: number; name: string; credit: number }

const BOT_NAMES = ['Falcon', 'Viper', 'Ghost', 'Raven', 'Cobra', 'Nomad', 'Atlas', 'Blaze', 'Storm', 'Shadow', 'Hunter', 'Titan', 'Fox', 'Wolf', 'Echo', 'Saber', 'Onyx', 'Jackal', 'Rook'];
/** Inputs buffered beyond this are dropped (keeps input latency low if a client bursts). */
const MAX_QUEUE = 45;

/**
 * Authoritative match room. Runs the shared GameSim at 30 Hz; clients only send inputs.
 * Movement, hits (with lag compensation), damage, deaths, zone and loot are decided here.
 */
export class MatchRoom extends Room {
  maxClients = 16;
  sim!: GameSim;
  private humans = new Map<string, Human>();
  private nextId = 0;
  private lobbyEndsAt = 0;
  private endedAt = 0;
  private loop: ReturnType<typeof setInterval> | null = null;
  private acc = 0;
  private last = 0;
  private opts: Required<Omit<MatchOptions, 'lagCompensation' | 'armed' | 'skyDrop' | 'map'>> & { map: MapId } = { lobbySeconds: 20, fillTo: 16, seed: 0, private: false, map: 'classic' };
  private hostId = -1;
  /** stats for the debug overlay / tests */
  tickMs = 0;

  async onCreate(options: MatchOptions = {}) {
    await initPhysics();
    this.opts = {
      // public rooms: clients cannot shorten/lengthen the lobby much
      lobbySeconds: options.private ? 180 : Math.max(1, Math.min(30, options.lobbySeconds ?? 20)),
      fillTo: Math.max(2, Math.min(16, options.fillTo ?? 16)),
      seed: options.seed ?? (Math.random() * 1e9) | 0, private: !!options.private,
      map: isMapId(options.map) ? options.map : 'classic',
    };
    if (this.opts.private) await this.setPrivate(true);
    this.maxClients = Math.max(1, Math.min(16, this.opts.fillTo));
    this.sim = new GameSim(this.opts.seed, { lobby: true, map: this.opts.map, armed: options.armed === true && process.env.NODE_ENV !== 'production', skyDrop: !(options.skyDrop === false && process.env.NODE_ENV !== 'production') });
    this.sim.lagCompensation = options.lagCompensation !== false;
    this.autoDispose = true;

    this.onMessageBytes(MSG.INPUT, (client: Client, bytes: Uint8Array) => this.onInputs(client, bytes));
    this.onMessage(MSG.START, (client: Client) => {
      const h = this.humans.get(client.sessionId);
      if (h && h.id === this.hostId && this.sim.phase === 'lobby') this.lobbyEndsAt = performance.now();
    });
    this.onMessage(MSG.PING, (client: Client, msg: { t: number }) => client.send(MSG.PONG, { t: msg?.t ?? 0, tick: this.sim.tick }));

    this.last = performance.now();
    this.loop = setInterval(() => this.update(), 1000 / TICK_RATE / 2);
  }

  onJoin(client: Client, options: { name?: string; skin?: number; slot?: number } = {}) {
    const id = this.nextId++;
    const name = String(options.name ?? `Player${id}`).replace(/[^\w \-]/g, '').slice(0, 14) || `Player${id}`;
    const p = this.sim.addPlayer(id, name, false);
    p.skin = Math.max(0, Math.min(SKIN_COUNT - 1, Number(options.skin) | 0));
    if (Number.isInteger(options.slot) && options.slot! >= 0 && options.slot! < 4) p.slot = options.slot!;
    if (this.hostId < 0) this.hostId = id;
    this.humans.set(client.sessionId, { id, client, queue: [], lastSeq: 0, name, credit: 0 });
    client.send(MSG.WELCOME, { id, seed: this.opts.seed, tick: this.sim.tick, map: this.opts.map });
    // current loot + ice walls (later changes arrive as events)
    client.send(MSG.WORLD, compactEvents([{
      loot: [...this.sim.loot.values()],
      walls: [...this.sim.walls.values()].map(({ handle: _h, ...w }) => w),
    }])[0]);
    this.broadcastRoster();
    if (this.humans.size === 1) this.lobbyEndsAt = performance.now() + this.opts.lobbySeconds * 1000;
    if (this.humans.size >= this.maxClients) this.lobbyEndsAt = performance.now();
  }

  onLeave(client: Client) {
    const h = this.humans.get(client.sessionId);
    if (!h) return;
    this.humans.delete(client.sessionId);
    this.sim.removePlayer(h.id);
    if (h.id === this.hostId) this.hostId = [...this.humans.values()][0]?.id ?? -1;
    this.broadcastRoster();
  }

  onDispose() {
    if (this.loop) clearInterval(this.loop);
  }

  private onInputs(client: Client, bytes: Uint8Array) {
    const h = this.humans.get(client.sessionId);
    if (!h) return;
    let inputs: InputCmd[];
    try { inputs = decodeInputs(bytes instanceof Uint8Array ? bytes : new Uint8Array(bytes as ArrayBuffer)); } catch { return; }
    for (const i of inputs) {
      if (i.seq <= h.lastSeq) continue; // duplicate (redundancy) or stale
      h.lastSeq = i.seq;
      h.queue.push(i);
    }
    while (h.queue.length > MAX_QUEUE) h.queue.shift();
  }

  private broadcastRoster() {
    this.broadcast(MSG.ROSTER, [...this.sim.players.values()].map((p) => ({ id: p.id, name: p.name, bot: !!p.bot, skin: p.skin })));
  }

  /** Fixed-rate loop with catch-up (never more than 4 ticks per call). */
  private update() {
    const now = performance.now();
    this.acc += (now - this.last) / 1000;
    this.last = now;
    let n = 0;
    while (this.acc >= DT && n < 4) { this.tick(now); this.acc -= DT; n++; }
    if (n === 4) this.acc = 0;
  }

  private tick(now: number) {
    const t0 = performance.now();
    const sim = this.sim;
    // ---- lobby -> live
    if (sim.phase === 'lobby') {
      if (this.humans.size > 0 && now >= this.lobbyEndsAt) {
        let i = 0;
        const names = [...BOT_NAMES].sort(() => Math.random() - 0.5);
        while (sim.players.size < this.opts.fillTo) sim.addPlayer(this.nextId++, names[i++ % names.length], true);
        sim.startMatch();
        void this.lock();
        this.broadcastRoster();
      }
      if (sim.tick % TICK_RATE === 0) {
        this.broadcast(MSG.LOBBY, { phase: sim.phase, countdown: Math.max(0, Math.ceil((this.lobbyEndsAt - now) / 1000)), humans: this.humans.size, max: this.maxClients, private: this.opts.private, hostId: this.hostId });
      }
    }
    // ---- consume exactly one input per human per tick (server-side rate limit)
    // Each tick grants one input credit. Starved: the player waits (no guessing => prediction stays exact)
    // and banks credit; when the late inputs arrive they are caught up at most 2 per tick, never more
    // inputs than ticks elapsed (flooding inputs cannot speed a player up).
    for (const h of this.humans.values()) {
      h.credit = Math.min(h.credit + 1, 45);
      const take = Math.min(h.queue.length, h.queue.length > 2 ? 2 : 1, Math.floor(h.credit));
      h.credit -= take;
      sim.setInputs(h.id, h.queue.splice(0, take));
    }
    sim.step();
    if (sim.events.length) this.broadcast(MSG.EVENTS, compactEvents(sim.events));
    // ---- per-client snapshot (30 Hz)
    for (const h of this.humans.values()) {
      const me = sim.players.get(h.id) ?? null;
      h.client.sendBytes(MSG.SNAP, encodeSnapshot(sim.tick, sim.phase, sim.startTick, sim.alive, me, sim.players.values()));
    }
    // ---- end of match: close the room a bit after the winner
    if (sim.phase === 'ended') {
      if (!this.endedAt) this.endedAt = now;
      else if (now - this.endedAt > 20000) void this.disconnect();
    }
    this.tickMs = this.tickMs * 0.95 + (performance.now() - t0) * 0.05;
  }
}
