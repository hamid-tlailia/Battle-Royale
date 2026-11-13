import type * as THREE from 'three';

export type Quality = 'high' | 'medium' | 'low';
/** render-scale range per graphics setting (render pixels per CSS pixel) */
const RANGE: Record<Quality, [number, number]> = { high: [1.5, 3], medium: [1, 1.5], low: [0.6, 1] };

/**
 * Dynamic resolution inside the range of the chosen quality: lowers the render scale only after
 * several slow seconds in a row, raises it when there is headroom. Changes are rare and in big steps
 * (every change re-allocates the frame buffers, which itself costs a hitch).
 */
export class AdaptiveResolution {
  scale = 1;
  private min = 1;
  private max = 1.5;
  private acc = 0;
  private frames = 0;
  private goodWindows = 0;
  private badWindows = 0;

  constructor(private renderer: THREE.WebGLRenderer, quality: Quality = 'medium') {
    this.setQuality(quality);
  }

  setQuality(q: Quality) {
    const dpr = window.devicePixelRatio || 1;
    const [lo, hi] = RANGE[q] ?? RANGE.medium;
    this.max = Math.min(dpr, hi);
    this.min = Math.min(this.max, lo);
    this.scale = this.max;
    this.renderer.setPixelRatio(this.scale);
    this.goodWindows = this.badWindows = 0;
  }

  frame(frameMs: number) {
    this.acc += frameMs;
    this.frames++;
    if (this.acc < 1000) return;
    const fps = (this.frames * 1000) / this.acc;
    this.acc = 0; this.frames = 0;
    let next = this.scale;
    if (fps < 45) { this.goodWindows = 0; if (++this.badWindows >= 3) { next = Math.max(this.min, this.scale * 0.8); this.badWindows = 0; } }
    else if (fps > 57) { this.badWindows = 0; if (++this.goodWindows >= 5) { next = Math.min(this.max, this.scale * 1.15); this.goodWindows = 0; } }
    else { this.goodWindows = 0; this.badWindows = 0; }
    if (Math.abs(next - this.scale) > 0.05) {
      this.scale = next;
      this.renderer.setPixelRatio(next);
    }
  }
}
