import { BTN, MAP, TICK_RATE } from '../config/game';
import { mulberry32 } from '../math/rng';
import type { GameSim, SimPlayer } from './game';
import { eyeOffset, type InputCmd } from './movement';

/**
 * Bot AI. Produces exactly the same InputCmd a human client sends, so bots obey every
 * server rule (speed, fire rate, ammo, line of sight).
 */
export class BotBrain {
  private rnd: () => number;
  private tx = 0; private tz = 0;
  private stuckTimer = 0;
  private lastX = 0; private lastZ = 0;
  private jumpTicks = 0;
  private seq = 0;
  private targetId: number | null = null;
  private seenTicks = 0;
  private scanCooldown = 0;
  private aimErrYaw = 0; private aimErrPitch = 0;
  private strafe = 1; private strafeTicks = 0;
  private burst = 0; private burstPause = 0;
  private crouchTicks = 0;
  /** skill 0..1 (accuracy & reaction) */
  private skill: number;

  constructor(seed: number) {
    this.rnd = mulberry32(seed);
    this.skill = 0.35 + this.rnd() * 0.45;
    this.pickTarget(0, 0, MAP.playRadius - 10);
  }

  private pickTarget(cx: number, cz: number, r: number) {
    const a = this.rnd() * Math.PI * 2, d = Math.sqrt(this.rnd()) * Math.max(2, r);
    this.tx = cx + Math.cos(a) * d; this.tz = cz + Math.sin(a) * d;
  }

  think(p: SimPlayer, sim: GameSim): InputCmd {
    const s = p.move;
    const dt = 1 / TICK_RATE;
    let buttons = 0, mx = 0, mz = 0;
    let yaw = s.yaw, pitch = 0;
    let weapon = p.slot;

    // ---------- perception (every ~0.3 s) ----------
    if (--this.scanCooldown <= 0) {
      this.scanCooldown = 8 + Math.floor(this.rnd() * 4);
      const eye = sim.eye(p);
      let best: SimPlayer | null = null, bestD = 50;
      for (const q of sim.players.values()) {
        if (q === p || !q.alive) continue;
        const d = Math.hypot(q.move.x - s.x, q.move.z - s.z);
        if (d > bestD) continue;
        if (!sim.los(eye.x, eye.y, eye.z, q.move.x, q.move.y + 0.2, q.move.z)) continue;
        best = q; bestD = d;
      }
      const newTarget = best ? best.id : null;
      if (newTarget !== this.targetId) {
        this.seenTicks = 0;
        // fresh target: large initial aim error that shrinks while tracking
        this.aimErrYaw = (this.rnd() < 0.5 ? -1 : 1) * (0.12 + this.rnd() * 0.25) * (1.4 - this.skill);
        this.aimErrPitch = (this.rnd() - 0.5) * 0.25 * (1.4 - this.skill);
      }
      this.targetId = newTarget;
    }
    const target = this.targetId !== null ? sim.players.get(this.targetId) : undefined;
    const zone = sim.zone;
    const distToZone = Math.hypot(s.x - zone.nx, s.z - zone.nz);
    const mustRun = distToZone > zone.nr * 0.85 && (zone.shrinking || zone.timer < 20) || Math.hypot(s.x - zone.cx, s.z - zone.cz) > zone.r - 3;

    if (target && target.alive && !mustRun) {
      // ---------- combat ----------
      this.seenTicks++;
      const eye = sim.eye(p);
      const tx = target.move.x, tz = target.move.z, ty = target.move.y + eyeOffset(target.move) - 0.35;
      const dx = tx - eye.x, dy = ty - eye.y, dz = tz - eye.z;
      const flat = Math.hypot(dx, dz);
      // error decays while tracking
      const decay = 0.985 - this.skill * 0.015;
      this.aimErrYaw *= decay; this.aimErrPitch *= decay;
      const wobble = (1.2 - this.skill) * 0.06;
      yaw = Math.atan2(-dx, -dz) + this.aimErrYaw + (this.rnd() - 0.5) * wobble;
      pitch = Math.atan2(dy, flat) + this.aimErrPitch + (this.rnd() - 0.5) * wobble;
      weapon = flat < 10 ? 2 : flat < 55 ? 1 : 3;
      if (p.weapons[weapon].mag + p.weapons[weapon].reserve === 0) weapon = 0;
      // strafe & occasionally crouch
      if (--this.strafeTicks <= 0) { this.strafe = this.rnd() < 0.5 ? -1 : 1; this.strafeTicks = 15 + Math.floor(this.rnd() * 40); }
      mx = this.strafe * 0.8;
      mz = flat > 30 ? 0.6 : flat < 6 ? -0.4 : 0;
      if (this.crouchTicks > 0) { this.crouchTicks--; buttons |= BTN.CROUCH; mx = 0; }
      else if (this.rnd() < 0.004) this.crouchTicks = 40;
      // reaction time, then bursts
      const reaction = Math.round((0.9 - this.skill * 0.4) * TICK_RATE);
      if (this.seenTicks > reaction) {
        if (this.burstPause > 0) this.burstPause--;
        else {
          buttons |= BTN.FIRE;
          if (++this.burst > 3 + this.rnd() * 4) { this.burst = 0; this.burstPause = 14 + Math.floor(this.rnd() * 22); }
        }
        if (weapon === 3 && flat > 25) buttons |= BTN.SCOPE;
      }
      // semi-auto weapons need the trigger released between shots
      if (weapon !== 1 && (p.prevButtons & BTN.FIRE)) buttons &= ~BTN.FIRE;
    } else {
      // ---------- travel ----------
      this.targetId = mustRun ? null : this.targetId;
      if (mustRun && Math.hypot(this.tx - zone.nx, this.tz - zone.nz) > zone.nr * 0.7) this.pickTarget(zone.nx, zone.nz, zone.nr * 0.6);
      const dx = this.tx - s.x, dz = this.tz - s.z;
      if (Math.hypot(dx, dz) < 2.5) this.pickTarget(zone.nx, zone.nz, Math.min(zone.nr, MAP.playRadius - 10) * 0.9);
      yaw = Math.atan2(-dx, -dz);
      mz = 1;
      // top up the magazine while walking
      const slot = p.weapons[p.slot];
      if (slot.mag < 8 && slot.reserve > 0 && !p.reloadEndTick && this.rnd() < 0.05) buttons |= BTN.RELOAD;
    }

    // ---------- unstick (jump / vault) ----------
    this.stuckTimer += dt;
    if (this.stuckTimer > 1) {
      const moved = Math.hypot(s.x - this.lastX, s.z - this.lastZ);
      if (moved < 1.0 && (mz !== 0 || mx !== 0)) {
        this.jumpTicks = 4;
        if (this.rnd() < 0.4) this.pickTarget(s.x, s.z, 25);
      }
      this.lastX = s.x; this.lastZ = s.z; this.stuckTimer = 0;
    }
    if (this.jumpTicks > 0) { this.jumpTicks--; buttons |= BTN.JUMP; }

    return { seq: ++this.seq, mx, mz, yaw, pitch, buttons, aimYaw: yaw, aimPitch: pitch, weapon };
  }
}
