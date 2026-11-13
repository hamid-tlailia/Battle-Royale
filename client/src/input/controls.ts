import { layoutOf } from '../ui/layout';
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
  proneToggle = false;
  sprintToggle = false;
  /** sky-dive: glide toward where you look; JUMP toggles a straight drop */
  inAir = false;
  dropStraight = false;
  /** joystick pushed past its rim toward the top: auto-sprint while held */
  sprintLock = false;
  onSprintLock: (on: boolean) => void = () => {};
  onSprintChange: (on: boolean) => void = () => {};
  scoped = false;
  jumpQueued = 0; // ticks remaining to send jump
  reloadQueued = 0;
  healQueued = 0;
  /** backpack: item to drop with the next input */
  dropQueued = 0;
  /** loot menu: ground item to pick with the next input */
  pickQueued = 0;
  /** settings: walk-over pickup */
  autoPickup = true;
  private lastSlot = 1;
  frostQueued = 0;
  fireHeld = false;
  /** a quick tap (shorter than one 33 ms tick) still fires: the trigger stays down for at least 2 ticks */
  fireTap = 0;
  weapon = 0;
  /** user setting (settings panel) */
  sensitivity = 1;
  scopeSensitivity = 1;
  /** set by the game: current fov / 70 (zoomed views turn slower) */
  fovScale = 1;
  /** aim-assist friction (<1 while the crosshair is on an enemy) */
  friction = 1;
  scopedView = false;

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
    this.joyBase.dataset.hud = 'joy';
    hud.appendChild(this.joyBase);
    this.showJoystickIdle();

    surface.addEventListener('touchstart', this.onTouchStart, { passive: false });
    window.addEventListener('touchmove', this.onTouchMove, { passive: false });
    window.addEventListener('touchend', this.onTouchEnd, { passive: false });
    window.addEventListener('touchcancel', this.onTouchEnd, { passive: false });

    window.addEventListener('keydown', (e) => {
      if (e.repeat) return;
      this.keys.add(e.code);
      if (e.code === 'Space') this.jump();
      if (e.code === 'KeyC' || e.code === 'ControlLeft') this.toggleCrouch();
      if (e.code === 'KeyR') this.reload();
      if (e.code === 'KeyZ') this.toggleProne();
      if (e.code.startsWith('Digit')) { const n = +e.code.slice(5) - 1; if (n >= 0 && n < 3) this.weapon = n; }
    });
    window.addEventListener('keyup', (e) => this.keys.delete(e.code));
    window.addEventListener('blur', () => this.keys.clear());
    surface.addEventListener('mousedown', (e) => {
      if (this.isTouch) return;
      if (!this.pointerLocked) { surface.requestPointerLock?.(); return; }
      if (e.button === 2) this.setScope(!this.scoped);
      if (e.button === 0) { if (this.frostAim) this.confirmFrost(); else { this.fireHeld = true; this.fireTap = 2; } }
    });
    window.addEventListener('mouseup', (e) => { if (e.button === 0) this.fireHeld = false; });
    surface.addEventListener('contextmenu', (e) => e.preventDefault());
    document.addEventListener('pointerlockchange', () => { this.pointerLocked = document.pointerLockElement === surface; });
    document.addEventListener('mousemove', (e) => {
      if (!this.pointerLocked) return;
      this.look(e.movementX * 0.25, e.movementY * 0.25);
    });
  }

  bindButton(el: HTMLElement, onDown: () => void, onUp?: () => void) {
    // while the layout editor is open the buttons are only things to move around: they do nothing
    const down = (e: Event) => { e.preventDefault(); e.stopPropagation(); if (el.closest('.editing')) return; el.classList.add('pressed'); onDown(); };
    const up = (e: Event) => { e.preventDefault(); e.stopPropagation(); if (el.closest('.editing')) return; el.classList.remove('pressed'); onUp?.(); };
    el.addEventListener('touchstart', down, { passive: false });
    el.addEventListener('touchend', up, { passive: false });
    el.addEventListener('touchcancel', up, { passive: false });
    el.addEventListener('mousedown', down);
    el.addEventListener('mouseup', up);
  }

  /** Button that also acts as a look-drag area while held (the FIRE button). */
  bindLookButton(el: HTMLElement, onDown: () => void, onUp: () => void) {
    el.addEventListener('touchstart', (e) => {
      e.preventDefault(); e.stopPropagation();
      if (el.closest('.editing')) return;
      el.classList.add('pressed');
      onDown();
      const t = e.changedTouches[0];
      if (this.lookId === null) { this.lookId = t.identifier; this.lookLast = { x: t.clientX, y: t.clientY }; }
      this.buttonTouches.set(t.identifier, () => { el.classList.remove('pressed'); onUp(); });
    }, { passive: false });
    el.addEventListener('mousedown', (e) => { e.preventDefault(); e.stopPropagation(); if (!el.closest('.editing')) onDown(); });
    el.addEventListener('mouseup', (e) => { e.preventDefault(); e.stopPropagation(); if (!el.closest('.editing')) onUp(); });
  }
  private buttonTouches = new Map<number, () => void>();

  jump() { if (this.inAir) { this.dropStraight = !this.dropStraight; return; } this.jumpQueued = 3; }
  reload() { this.reloadQueued = 3; }
  heal() { this.healQueued = 3; }
  /** put the gun away / take it back out */
  toggleHolster() {
    if (this.weapon === 4) this.weapon = this.lastSlot;
    else { this.lastSlot = this.weapon; this.weapon = 4; this.scoped = false; }
  }
  /** returns the queued backpack drop once */
  takeDrop(): number | undefined { const d = this.dropQueued; this.dropQueued = 0; return d || undefined; }
  takePick(): number | undefined { const d = this.pickQueued; this.pickQueued = 0; return d || undefined; }
  /** ice wall: first tap shows a preview you aim by turning, FIRE places it, tapping the button again cancels */
  frostAim = false;
  frost() { this.frostAim = !this.frostAim; }
  confirmFrost() { this.frostAim = false; this.frostQueued = 3; }
  /** crouching / lying down stops auto-run (otherwise the player keeps walking) */
  private stopRun() { if (this.sprintToggle) { this.sprintToggle = false; this.onSprintChange(false); } }
  toggleCrouch() { this.crouchToggle = !this.crouchToggle; if (this.crouchToggle) { this.proneToggle = false; this.stopRun(); } }
  toggleProne() { this.proneToggle = !this.proneToggle; if (this.proneToggle) { this.crouchToggle = false; this.stopRun(); } }
  toggleSprint() { this.sprintToggle = !this.sprintToggle; if (this.sprintToggle) { this.crouchToggle = false; this.proneToggle = false; this.scoped = false; } this.onSprintChange(this.sprintToggle); }
  setScope(v: boolean) { this.scoped = v; }

  private look(dx: number, dy: number) {
    // radians per CSS pixel: ~360° for a 570 px swipe at default sensitivity
    const k = 0.011 * this.sensitivity * this.fovScale * (this.scopedView ? this.scopeSensitivity : 1) * this.friction;
    this.yaw -= dx * k;
    this.pitch = Math.max(-PLAYER.maxPitch, Math.min(PLAYER.maxPitch, this.pitch - dy * k));
  }

  private onTouchStart = (e: TouchEvent) => {
    e.preventDefault();
    if (document.querySelector('.editing')) return; // layout editor open: no moving / looking
    for (const t of Array.from(e.changedTouches)) {
      const leftSide = t.clientX < window.innerWidth * 0.42;
      if (leftSide && this.joyId === null) {
        this.joyId = t.identifier;
        // custom layout: fixed joystick at the saved spot; otherwise it appears under the thumb
        const fixed = layoutOf('joy');
        const ox = fixed ? fixed.x * window.innerWidth : t.clientX, oy = fixed ? fixed.y * window.innerHeight : t.clientY;
        this.joyOrigin = { x: ox, y: oy };
        this.placeJoy(ox, oy, 1);
      } else if (!leftSide && this.lookId === null) {
        this.lookId = t.identifier;
        this.lookLast = { x: t.clientX, y: t.clientY };
      }
    }
  };

  private onTouchMove = (e: TouchEvent) => {
    // let lists (backpack, ground loot, settings) scroll with the finger
    const tgt = e.target as HTMLElement | null;
    if (tgt?.closest?.('.bag-panel, .loot-list, .settings-panel')) return;
    if (e.cancelable) e.preventDefault();
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
        // raw distance (not clamped): pushing well past the rim upward locks sprint
        const rawDx = t.clientX - this.joyOrigin.x, rawDy = t.clientY - this.joyOrigin.y;
        const lock = Math.hypot(rawDx, rawDy) > this.JOY_R * 1.35 && rawDy < 0 && Math.abs(rawDx) < -rawDy * 0.7;
        if (lock !== this.sprintLock) { this.sprintLock = lock; this.onSprintLock(lock); }
      } else if (t.identifier === this.lookId) {
        this.look(t.clientX - this.lookLast.x, t.clientY - this.lookLast.y);
        this.lookLast = { x: t.clientX, y: t.clientY };
      }
    }
  };

  private onTouchEnd = (e: TouchEvent) => {
    for (const t of Array.from(e.changedTouches)) {
      const up = this.buttonTouches.get(t.identifier);
      if (up) { up(); this.buttonTouches.delete(t.identifier); }
      if (t.identifier === this.joyId) {
        this.joyId = null; this.mx = 0; this.mz = 0;
        // letting go while sprint-locked keeps running (auto-run) until you pull back, crouch or aim
        if (this.sprintLock) { this.sprintLock = false; this.onSprintLock(false); if (!this.sprintToggle) { this.sprintToggle = true; this.crouchToggle = false; this.proneToggle = false; this.onSprintChange(true); } }
        this.showJoystickIdle();
      } else if (t.identifier === this.lookId) this.lookId = null;
    }
  };

  private placeJoy(x: number, y: number, opacity: number) {
    this.joyBase.style.transform = 'none'; this.joyBase.style.width = this.joyBase.style.height = '120px';
    this.joyBase.style.left = `${x - this.JOY_R}px`;
    this.joyBase.style.top = `${y - this.JOY_R}px`;
    this.joyBase.style.opacity = String(opacity);
    this.joyKnob.style.transform = 'translate(0px, 0px)';
  }

  private showJoystickIdle() {
    if (!this.isTouch) { this.joyBase.style.display = 'none'; return; }
    const fixed = layoutOf('joy');
    if (fixed) this.placeJoy(fixed.x * window.innerWidth, fixed.y * window.innerHeight, 0.45);
    else this.placeJoy(110, window.innerHeight - 110, 0.45);
  }

  /** Called once per simulation tick. */
  sampleButtons(): number {
    let b = 0;
    if (this.jumpQueued > 0) { this.jumpQueued--; b |= BTN.JUMP; }
    if (this.inAir && this.dropStraight) b |= BTN.JUMP;
    if (this.reloadQueued > 0) { this.reloadQueued--; b |= BTN.RELOAD; }
    if (this.healQueued > 0) { this.healQueued--; b |= BTN.HEAL; }
    if (this.frostQueued > 0) { this.frostQueued--; b |= BTN.FROST; }
    if (this.crouchToggle) b |= BTN.CROUCH;
    if (this.proneToggle) b |= BTN.PRONE;
    if ((this.sprintLock || this.sprintToggle || this.keys.has('ShiftLeft')) && !this.fireHeld && !this.scoped) b |= BTN.SPRINT;
    if (this.scoped) b |= BTN.SCOPE;
    if (this.autoPickup) b |= BTN.AUTOPICK;
    if (this.fireHeld || this.fireTap > 0) b |= BTN.FIRE;
    if (this.fireTap > 0) this.fireTap--;
    return b;
  }

  /** Movement axes including keyboard. Sprint toggle = auto-run forward until cancelled. */
  axes(): { mx: number; mz: number } {
    let mx = this.mx, mz = this.mz;
    if (this.sprintToggle) {
      if (mz < -0.3 || this.fireHeld || this.scoped) { this.sprintToggle = false; this.onSprintChange(false); }
      else { mz = 1; mx *= 0.5; }
    }
    if (this.keys.size) {
      if (this.keys.has('KeyW') || this.keys.has('ArrowUp')) mz += 1;
      if (this.keys.has('KeyS') || this.keys.has('ArrowDown')) mz -= 1;
      if (this.keys.has('KeyD') || this.keys.has('ArrowRight')) mx += 1;
      if (this.keys.has('KeyA') || this.keys.has('ArrowLeft')) mx -= 1;
    }
    // sky-diving with the stick untouched: glide straight toward where the camera looks
    if (this.inAir && !this.dropStraight && Math.hypot(mx, mz) < 0.15) { mx = 0; mz = 1; }
    const len = Math.hypot(mx, mz);
    if (len > 1) { mx /= len; mz /= len; }
    return { mx, mz };
  }
}
