import { BTN, MAP } from '../config/game';
import { mulberry32 } from '../math/rng';
import type { InputCmd, MoveState } from './movement';

/** Very simple wandering bot (M1). Produces the same InputCmd a human client sends. */
export class BotBrain {
  private rnd: () => number;
  private tx = 0; private tz = 0;
  private stuckTimer = 0;
  private lastX = 0; private lastZ = 0;
  private jumpTicks = 0;
  private crouchTicks = 0;
  private seq = 0;

  constructor(seed: number) {
    this.rnd = mulberry32(seed);
    this.pickTarget();
  }

  private pickTarget() {
    const a = this.rnd() * Math.PI * 2, d = Math.sqrt(this.rnd()) * (MAP.playRadius - 10);
    this.tx = Math.cos(a) * d; this.tz = Math.sin(a) * d;
  }

  think(s: MoveState, dt: number): InputCmd {
    const dx = this.tx - s.x, dz = this.tz - s.z;
    const dist = Math.hypot(dx, dz);
    if (dist < 2) this.pickTarget();
    // yaw such that forward (-sin, -cos) points at target
    const yaw = Math.atan2(-dx, -dz);

    this.stuckTimer += dt;
    if (this.stuckTimer > 1) {
      const moved = Math.hypot(s.x - this.lastX, s.z - this.lastZ);
      if (moved < 1.2) {
        this.jumpTicks = 6;
        if (this.rnd() < 0.5) this.pickTarget();
      }
      if (this.rnd() < 0.05) this.crouchTicks = 45;
      this.lastX = s.x; this.lastZ = s.z; this.stuckTimer = 0;
    }
    let buttons = 0;
    if (this.jumpTicks > 0) { this.jumpTicks--; buttons |= BTN.JUMP; }
    if (this.crouchTicks > 0) { this.crouchTicks--; buttons |= BTN.CROUCH; }
    return { seq: ++this.seq, mx: 0, mz: 1, yaw, pitch: 0, buttons };
  }
}
