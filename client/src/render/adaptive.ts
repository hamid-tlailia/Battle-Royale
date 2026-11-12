import type * as THREE from 'three';

/**
 * Dynamic resolution: lowers the render scale when frames are slow, raises it when there is headroom.
 * Scale is relative to CSS pixels (1.0 = one render pixel per CSS pixel).
 */
export class AdaptiveResolution {
  scale: number;
  private readonly min = 0.55;
  private readonly max: number;
  private acc = 0;
  private frames = 0;
  private goodWindows = 0;

  constructor(private renderer: THREE.WebGLRenderer) {
    this.max = Math.min(window.devicePixelRatio || 1, 1.5);
    this.scale = Math.min(1, this.max);
    renderer.setPixelRatio(this.scale);
  }

  frame(frameMs: number) {
    this.acc += frameMs;
    this.frames++;
    if (this.acc < 1000) return;
    const fps = (this.frames * 1000) / this.acc;
    this.acc = 0; this.frames = 0;
    let next = this.scale;
    if (fps < 50) { next = Math.max(this.min, this.scale * (fps < 35 ? 0.8 : 0.9)); this.goodWindows = 0; }
    else if (fps > 57) { if (++this.goodWindows >= 3) { next = Math.min(this.max, this.scale * 1.08); this.goodWindows = 0; } }
    else this.goodWindows = 0;
    if (Math.abs(next - this.scale) > 0.01) {
      this.scale = next;
      this.renderer.setPixelRatio(next);
    }
  }
}
