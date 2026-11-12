import type * as THREE from 'three';

export interface DebugStats {
  ping: number | null;
  players: number;
  tick: number;
  extra: Record<string, string | number>;
}

/** Hidden debug overlay toggled by a small corner button. */
export class DebugOverlay {
  private panel: HTMLDivElement;
  private visible = false;
  private frames = 0;
  private acc = 0;
  private fps = 0;
  private worstMs = 0;
  private lastUpdate = 0;

  constructor(parent: HTMLElement) {
    const btn = document.createElement('button');
    btn.className = 'dbg-btn';
    btn.textContent = 'DBG';
    this.panel = document.createElement('div');
    this.panel.className = 'dbg-panel';
    this.panel.style.display = 'none';
    const toggle = (e: Event) => {
      e.preventDefault(); e.stopPropagation();
      this.visible = !this.visible;
      this.panel.style.display = this.visible ? 'block' : 'none';
    };
    btn.addEventListener('touchstart', toggle, { passive: false });
    btn.addEventListener('click', toggle);
    window.addEventListener('keydown', (e) => { if (e.code === 'F3' || e.code === 'Backquote') toggle(e); });
    parent.append(btn, this.panel);
  }

  frame(dtMs: number, renderer: THREE.WebGLRenderer, s: DebugStats) {
    this.frames++;
    this.acc += dtMs;
    this.worstMs = Math.max(this.worstMs, dtMs);
    if (this.acc >= 500) {
      this.fps = (this.frames * 1000) / this.acc;
      this.frames = 0; this.acc = 0;
    }
    const now = performance.now();
    if (!this.visible || now - this.lastUpdate < 250) return;
    this.lastUpdate = now;
    const info = renderer.info.render;
    const rows: [string, string | number][] = [
      ['FPS', this.fps.toFixed(0)],
      ['worst frame', `${this.worstMs.toFixed(1)} ms`],
      ['ping', s.ping === null ? 'offline' : `${s.ping.toFixed(0)} ms`],
      ['players', s.players],
      ['tick', s.tick],
      ['draw calls', info.calls],
      ['triangles', info.triangles],
      ['pixel ratio', renderer.getPixelRatio().toFixed(2)],
      ...Object.entries(s.extra),
    ];
    this.panel.innerHTML = rows.map(([k, v]) => `<div><span>${k}</span><b>${v}</b></div>`).join('');
    this.worstMs = 0;
  }
}
