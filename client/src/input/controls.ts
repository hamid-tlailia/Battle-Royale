import { BTN, PLAYER } from '@lw/shared';

/**
 * Unified input: touch (left joystick, right-side look drag, buttons) + keyboard/mouse.
 * Produces movement axes, accumulated yaw/pitch and button bits.
 */
export class Controls {
  yaw = 0;
  pitch = -0.1;
  mx = 0;
  mz = 0;
  crouchToggle = false;
  scoped = false;
  jumpQueued = 0; // ticks remaining to send jump
  sensitivity = 1;

  private keys = new Set<string>();
  private joyId: number | null = null;
  private joyOrigin = { x: 0, y: 0 };
  private lookId: number | null = null;
  private lookLast = { x: 0, y: 0 };
  private joyBase: HTMLDivElement;
  private joyKnob: HTMLDivElement;
  private readonly JOY_R = 60;
  private pointerLocked = false;
  readonly isTouch: boolean;

  constructor(surface: HTMLElement, hud: HTMLElement) {
    this.isTouch = 'ontouchstart' in window || navigator.maxTouchPoints > 0;
    this.joyBase = document.createElement('div');
    this.joyBase.className = 'joy-base';
    this.joyKnob = document.createElement('div');
    this.joyKnob.className = 'joy-knob';
    this.joyBase.appendChild(this.joyKnob);
    hud.appendChild(this.joyBase);
    this.showJoystickIdle();

    surface.addEventListener('touchstart', this.onTouchStart, { passive: false });
    surface.addEventListener('touchmove', this.onTouchMove, { passive: false });
    surface.addEventListener('touchend', this.onTouchEnd, { passive: false });
    surface.addEventListener('touchcancel', this.onTouchEnd, { passive: false });

    window.addEventListener('keydown', (e) => {
      if (e.repeat) return;
      this.keys.add(e.code);
      if (e.code === 'Space') this.jump();
      if (e.code === 'KeyC' || e.code === 'ControlLeft') this.toggleCrouch();
    });
    window.addEventListener('keyup', (e) => this.keys.delete(e.code));
    window.addEventListener('blur', () => this.keys.clear());
    surface.addEventListener('mousedown', (e) => {
      if (this.isTouch) return;
      if (!this.pointerLocked) { surface.requestPointerLock?.(); return; }
      if (e.button === 2) this.setScope(!this.scoped);
    });
    surface.addEventListener('contextmenu', (e) => e.preventDefault());
    document.addEventListener('pointerlockchange', () => { this.pointerLocked = document.pointerLockElement === surface; });
    document.addEventListener('mousemove', (e) => {
      if (!this.pointerLocked) return;
      this.look(e.movementX * 1.2, e.movementY * 1.2);
    });
  }

  bindButton(el: HTMLElement, onDown: () => void, onUp?: () => void) {
    const down = (e: Event) => { e.preventDefault(); e.stopPropagation(); el.classList.add('pressed'); onDown(); };
    const up = (e: Event) => { e.preventDefault(); e.stopPropagation(); el.classList.remove('pressed'); onUp?.(); };
    el.addEventListener('touchstart', down, { passive: false });
    el.addEventListener('touchend', up, { passive: false });
    el.addEventListener('touchcancel', up, { passive: false });
    el.addEventListener('mousedown', down);
    el.addEventListener('mouseup', up);
  }

  jump() { this.jumpQueued = 3; }
  toggleCrouch() { this.crouchToggle = !this.crouchToggle; }
  setScope(v: boolean) { this.scoped = v; }

  private look(dx: number, dy: number) {
    const k = 0.0042 * this.sensitivity * (this.scoped ? 0.45 : 1);
    this.yaw -= dx * k;
    this.pitch = Math.max(-PLAYER.maxPitch, Math.min(PLAYER.maxPitch, this.pitch - dy * k));
  }

  private onTouchStart = (e: TouchEvent) => {
    e.preventDefault();
    for (const t of Array.from(e.changedTouches)) {
      const leftSide = t.clientX < window.innerWidth * 0.42;
      if (leftSide && this.joyId === null) {
        this.joyId = t.identifier;
        this.joyOrigin = { x: t.clientX, y: t.clientY };
        this.placeJoy(t.clientX, t.clientY, 1);
      } else if (!leftSide && this.lookId === null) {
        this.lookId = t.identifier;
        this.lookLast = { x: t.clientX, y: t.clientY };
      }
    }
  };

  private onTouchMove = (e: TouchEvent) => {
    e.preventDefault();
    for (const t of Array.from(e.changedTouches)) {
      if (t.identifier === this.joyId) {
        let dx = t.clientX - this.joyOrigin.x, dy = t.clientY - this.joyOrigin.y;
        const len = Math.hypot(dx, dy);
        if (len > this.JOY_R) { dx *= this.JOY_R / len; dy *= this.JOY_R / len; }
        this.joyKnob.style.transform = `translate(${dx}px, ${dy}px)`;
        // small deadzone, then full speed quickly (mobile thumbs)
        const m = Math.min(1, len / this.JOY_R);
        const mapped = m < 0.12 ? 0 : Math.min(1, (m - 0.12) / 0.6);
        const clen = Math.max(1e-6, Math.hypot(dx, dy));
        this.mx = (dx / clen) * mapped;
        this.mz = (-dy / clen) * mapped;
      } else if (t.identifier === this.lookId) {
        this.look((t.clientX - this.lookLast.x) * 1.6, (t.clientY - this.lookLast.y) * 1.6);
        this.lookLast = { x: t.clientX, y: t.clientY };
      }
    }
  };

  private onTouchEnd = (e: TouchEvent) => {
    for (const t of Array.from(e.changedTouches)) {
      if (t.identifier === this.joyId) {
        this.joyId = null; this.mx = 0; this.mz = 0;
        this.showJoystickIdle();
      } else if (t.identifier === this.lookId) this.lookId = null;
    }
  };

  private placeJoy(x: number, y: number, opacity: number) {
    this.joyBase.style.left = `${x - this.JOY_R}px`;
    this.joyBase.style.top = `${y - this.JOY_R}px`;
    this.joyBase.style.opacity = String(opacity);
    this.joyKnob.style.transform = 'translate(0px, 0px)';
  }

  private showJoystickIdle() {
    if (!this.isTouch) { this.joyBase.style.display = 'none'; return; }
    this.placeJoy(110, window.innerHeight - 110, 0.45);
  }

  /** Called once per simulation tick. */
  sampleButtons(): number {
    let b = 0;
    if (this.jumpQueued > 0) { this.jumpQueued--; b |= BTN.JUMP; }
    if (this.crouchToggle) b |= BTN.CROUCH;
    if (this.scoped) b |= BTN.SCOPE;
    return b;
  }

  /** Movement axes including keyboard. */
  axes(): { mx: number; mz: number } {
    let mx = this.mx, mz = this.mz;
    if (this.keys.size) {
      if (this.keys.has('KeyW') || this.keys.has('ArrowUp')) mz += 1;
      if (this.keys.has('KeyS') || this.keys.has('ArrowDown')) mz -= 1;
      if (this.keys.has('KeyD') || this.keys.has('ArrowRight')) mx += 1;
      if (this.keys.has('KeyA') || this.keys.has('ArrowLeft')) mx -= 1;
    }
    const len = Math.hypot(mx, mz);
    if (len > 1) { mx /= len; mz /= len; }
    return { mx, mz };
  }
}
