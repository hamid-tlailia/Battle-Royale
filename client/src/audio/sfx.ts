import type { WeaponId } from '@lw/shared';

/**
 * Real recorded samples (gunshots, footsteps, reload, wind — see CREDITS.md) with WebAudio synthesis fallbacks.
 * Positional: volume falls with distance, far shots are muffled, stereo pan from direction.
 */
export class Sfx {
  private ctx: AudioContext | null = null;
  private master!: GainNode;
  private noise!: AudioBuffer;
  enabled = true;

  /** Must be called from a user gesture (tap) on mobile. */
  unlock() {
    if (this.ctx) { void this.ctx.resume(); return; }
    const AC = window.AudioContext || (window as unknown as { webkitAudioContext: typeof AudioContext }).webkitAudioContext;
    if (!AC) return;
    this.ctx = new AC({ latencyHint: 'interactive' });
    this.master = this.ctx.createGain();
    this.master.gain.value = 0.8;
    const comp = this.ctx.createDynamicsCompressor();
    this.master.connect(comp).connect(this.ctx.destination);
    void this.loadSamples();
    const len = this.ctx.sampleRate * 1.5;
    this.noise = this.ctx.createBuffer(1, len, this.ctx.sampleRate);
    const d = this.noise.getChannelData(0);
    for (let i = 0; i < len; i++) d[i] = Math.random() * 2 - 1;
  }

  private samples = new Map<string, AudioBuffer>();
  /** Real recorded gunshots (CC-BY 3.0, Vincent Sevedge / OpenGameArt — see CREDITS.md). */
  private async loadSamples() {
    const ctx = this.ctx!;
    const names = ['pistol', 'rifle', 'shotgun', 'sniper', 'reload', 'clip', 'wind', 'plane', 'victory',
      ...[1, 2, 3, 4, 5, 6].flatMap((i) => [`step${i}`, `stone${i}`])];
    await Promise.all(names.map(async (n) => {
      try {
        const buf = await (await fetch(`/sfx/${n}.mp3`)).arrayBuffer();
        this.samples.set(n, await ctx.decodeAudioData(buf));
      } catch { /* keep synth fallback */ }
    }));
    this.startWind();
  }

  /** play a recorded sample through a positional chain; false if not loaded */
  private sample(name: string, vol: number, dist: number, pan: number, rate = 1): boolean {
    const buf = this.samples.get(name);
    if (!buf) return false;
    const d = this.chain(vol, dist, pan);
    if (!d) return true;
    const src = this.ctx!.createBufferSource(); src.buffer = buf; src.playbackRate.value = rate;
    src.connect(d); src.start();
    return true;
  }

  /** quiet looping mountain wind (CC-BY 4.0, Anderson dos Santos) */
  private windGain: GainNode | null = null;
  private startWind() {
    const buf = this.samples.get('wind'); const ctx = this.ctx;
    if (!buf || !ctx || this.windGain) return;
    const src = ctx.createBufferSource(); src.buffer = buf; src.loop = true; src.playbackRate.value = 0.8;
    this.windGain = ctx.createGain(); this.windGain.gain.value = this.enabled ? 0.06 : 0;
    src.connect(this.windGain).connect(this.master); src.start();
  }
  /** propeller-plane engine: real recorded loop (CC-BY 3.0, AntumDeluge), synth drone as fallback; level 0..1 */
  private planeGain: GainNode | null = null;
  setPlane(level: number) {
    const ctx = this.ctx; if (!ctx) return;
    if (!this.planeGain) {
      if (level <= 0) return;
      this.planeGain = ctx.createGain(); this.planeGain.gain.value = 0;
      const rec = this.samples.get('plane');
      if (rec) {
        const src = ctx.createBufferSource(); src.buffer = rec; src.loop = true; src.playbackRate.value = 0.85;
        const lp = ctx.createBiquadFilter(); lp.type = 'lowpass'; lp.frequency.value = 2600;
        src.connect(lp).connect(this.planeGain).connect(this.master); src.start();
      } else {
        const lp = ctx.createBiquadFilter(); lp.type = 'lowpass'; lp.frequency.value = 420;
        for (const f of [58, 61.5, 117]) { const o = ctx.createOscillator(); o.type = 'sawtooth'; o.frequency.value = f; const g = ctx.createGain(); g.gain.value = f > 100 ? 0.25 : 0.5; o.connect(g).connect(lp); o.start(); }
        lp.connect(this.planeGain).connect(this.master);
      }
    }
    this.planeGain.gain.setTargetAtTime(this.enabled && !this.quiet ? level * 0.5 : 0, ctx.currentTime, 0.3);
  }

  /** victory: every other sound stops, only the victory music plays */
  private quiet = false;
  victory() {
    const ctx = this.ctx; if (!ctx) return;
    this.quiet = true;
    this.planeGain?.gain.setTargetAtTime(0, ctx.currentTime, 0.1);
    this.rushGain?.gain.setTargetAtTime(0, ctx.currentTime, 0.1);
    if (this.windGain) this.windGain.gain.value = 0;
    const buf = this.samples.get('victory');
    if (!buf || !this.enabled) return;
    const src = ctx.createBufferSource(); src.buffer = buf;
    const g = ctx.createGain(); g.gain.value = 0.8;
    src.connect(g).connect(this.master); src.start();
    this.music = { src, g };
  }
  private music: { src: AudioBufferSourceNode; g: GainNode } | null = null;
  /** leaving the match: fade the victory music out, normal sounds back on */
  stopMusic() {
    const m = this.music; this.music = null; this.quiet = false;
    if (!m || !this.ctx) return;
    m.g.gain.setTargetAtTime(0, this.ctx.currentTime, 0.15);
    try { m.src.stop(this.ctx.currentTime + 0.8); } catch { /* already stopped */ }
  }
  /** back to normal (new match) */
  resume() { this.quiet = false; }

  /** louder wind high up (sky-diving) */
  private rushGain: GainNode | null = null;
  setWind(level: number) {
    if (this.windGain) this.windGain.gain.value = this.enabled ? 0.06 + level * 0.55 : 0;
    // rushing air while sky-diving (filtered noise), on top of the recorded wind
    const ctx = this.ctx;
    if (!ctx || !this.noise) return;
    if (!this.rushGain) {
      if (level <= 0) return;
      const src = ctx.createBufferSource(); src.buffer = this.noise; src.loop = true;
      const bp = ctx.createBiquadFilter(); bp.type = 'bandpass'; bp.frequency.value = 650; bp.Q.value = 0.6;
      const lfo = ctx.createOscillator(); lfo.frequency.value = 0.35; const lg = ctx.createGain(); lg.gain.value = 250; lfo.connect(lg).connect(bp.frequency); lfo.start();
      this.rushGain = ctx.createGain(); this.rushGain.gain.value = 0;
      src.connect(bp).connect(this.rushGain).connect(this.master); src.start();
    }
    this.rushGain.gain.setTargetAtTime(this.enabled ? level * 0.5 : 0, ctx.currentTime, 0.4);
  }

  /** dist in metres, pan -1..1 */
  private chain(vol: number, dist: number, pan: number): AudioNode | null {
    const ctx = this.ctx;
    if (!ctx || !this.enabled || this.quiet) return null;
    const att = vol / (1 + (dist / 12) ** 1.4);
    if (att < 0.004) return null;
    const g = ctx.createGain(); g.gain.value = att;
    const lp = ctx.createBiquadFilter(); lp.type = 'lowpass';
    lp.frequency.value = Math.max(900, 16000 / (1 + dist / 25));
    const p = ctx.createStereoPanner(); p.pan.value = Math.max(-1, Math.min(1, pan));
    lp.connect(g).connect(p).connect(this.master);
    return lp;
  }

  private burst(dest: AudioNode, t: number, dur: number, f: number, q: number, peak: number, decay: number) {
    const ctx = this.ctx!;
    const src = ctx.createBufferSource(); src.buffer = this.noise;
    const bp = ctx.createBiquadFilter(); bp.type = 'bandpass'; bp.frequency.value = f; bp.Q.value = q;
    const g = ctx.createGain();
    g.gain.setValueAtTime(0.0001, t);
    g.gain.exponentialRampToValueAtTime(peak, t + 0.003);
    g.gain.exponentialRampToValueAtTime(0.0001, t + decay);
    src.connect(bp).connect(g).connect(dest);
    src.start(t, Math.random() * 0.5, dur);
  }

  private thump(dest: AudioNode, t: number, f0: number, f1: number, peak: number, decay: number) {
    const ctx = this.ctx!;
    const o = ctx.createOscillator(); o.type = 'sine';
    o.frequency.setValueAtTime(f0, t); o.frequency.exponentialRampToValueAtTime(f1, t + decay);
    const g = ctx.createGain();
    g.gain.setValueAtTime(peak, t); g.gain.exponentialRampToValueAtTime(0.0001, t + decay);
    o.connect(g).connect(dest); o.start(t); o.stop(t + decay + 0.02);
  }

  shot(w: WeaponId, dist: number, pan: number) {
    const d = this.chain(w === 'sniper' ? 1.3 : w === 'shotgun' ? 1.1 : 0.9, dist, pan);
    if (!d) return;
    const t = this.ctx!.currentTime;
    if (dist < 30 && (w === 'sniper' || w === 'shotgun')) { // bolt / pump being worked after the shot
      const k = this.chain(0.35, dist, pan);
      if (k) { this.burst(k, t + 0.55, 0.05, 2600, 3, 0.9, 0.04); this.burst(k, t + 0.78, 0.06, 1900, 3, 1, 0.05); }
    }
    const sample = this.samples.get(w === 'smg' ? 'pistol' : w); // SMG: the pistol report, pitched up and fast
    if (sample) {
      const src = this.ctx!.createBufferSource();
      src.buffer = sample;
      src.playbackRate.value = (w === 'smg' ? 1.18 : 0.94) + Math.random() * 0.12; // no two shots identical
      src.connect(d);
      src.start(t);
      // distant shots get a low rumble tail (echo off the mountains)
      if (dist > 40) this.burst(d, t + 0.05, 0.8, 300, 0.5, 0.35, 0.7);
      return;
    }
    switch (w) {
      case 'smg':
      case 'pistol': this.burst(d, t, 0.15, 2200, 0.8, 1, 0.12); this.thump(d, t, 180, 60, 0.6, 0.08); break;
      case 'rifle': this.burst(d, t, 0.2, 1500, 0.7, 1, 0.16); this.thump(d, t, 140, 45, 0.9, 0.11); break;
      case 'shotgun': this.burst(d, t, 0.4, 900, 0.5, 1, 0.32); this.thump(d, t, 110, 35, 1, 0.22); break;
      case 'sniper': this.burst(d, t, 0.6, 2600, 0.6, 1, 0.08); this.burst(d, t + 0.01, 0.8, 700, 0.4, 0.7, 0.6); this.thump(d, t, 160, 30, 1, 0.3); break;
    }
  }

  step(dist: number, pan: number, soft = false, stone = false) {
    // recorded footsteps (CC0, Fantozzi): grass/sand outdoors, stone on house floors
    const n = `${stone ? 'stone' : 'step'}${1 + Math.floor(Math.random() * 6)}`;
    const fade = Math.max(0, 1 - dist / 26) ** 1.5; // steep falloff: you hear people close to you, not across the map
    if (fade <= 0.01) return;
    if (this.sample(n, (soft ? 0.45 : 0.9) * fade, dist, pan, 0.92 + Math.random() * 0.16)) return;
    const d = this.chain(soft ? 0.18 : 0.35, dist, pan);
    if (!d) return;
    const t = this.ctx!.currentTime;
    this.burst(d, t, 0.08, 300 + Math.random() * 250, 1.2, 1, 0.07);
    this.thump(d, t, 90 + Math.random() * 20, 50, 0.4, 0.06);
  }

  land() { const d = this.chain(0.5, 0, 0); if (d) this.thump(d, this.ctx!.currentTime, 120, 40, 1, 0.12); }

  hitMarker(head: boolean) {
    const ctx = this.ctx; if (!ctx || !this.enabled) return;
    const t = ctx.currentTime;
    const o = ctx.createOscillator(); o.type = 'square';
    o.frequency.value = head ? 1700 : 1100;
    const g = ctx.createGain(); g.gain.setValueAtTime(0.12, t); g.gain.exponentialRampToValueAtTime(0.0001, t + 0.06);
    o.connect(g).connect(this.master); o.start(t); o.stop(t + 0.07);
  }

  kill() {
    const ctx = this.ctx; if (!ctx || !this.enabled) return;
    const t = ctx.currentTime;
    for (const [i, f] of [880, 1320].entries()) {
      const o = ctx.createOscillator(); o.type = 'triangle'; o.frequency.value = f;
      const g = ctx.createGain(); g.gain.setValueAtTime(0.0001, t + i * 0.08);
      g.gain.exponentialRampToValueAtTime(0.25, t + i * 0.08 + 0.01); g.gain.exponentialRampToValueAtTime(0.0001, t + i * 0.08 + 0.25);
      o.connect(g).connect(this.master); o.start(t + i * 0.08); o.stop(t + i * 0.08 + 0.3);
    }
  }

  hurt() { const d = this.chain(0.6, 0, 0); if (d) { const t = this.ctx!.currentTime; this.thump(d, t, 220, 70, 0.8, 0.15); } }

  reload() {
    if (this.sample('reload', 0.8, 0, 0.3)) return; // CC0 gun reload (StarNinjas)
    const d = this.chain(0.4, 0, 0.3); if (!d) return;
    const t = this.ctx!.currentTime;
    this.burst(d, t, 0.05, 3000, 3, 0.8, 0.04);
    this.burst(d, t + 0.35, 0.05, 2400, 3, 0.9, 0.05);
  }

  pickup() {
    if (this.sample('clip', 0.5, 0, 0)) return; // CC0 clip load (Raassh)
    const ctx = this.ctx; if (!ctx || !this.enabled) return;
    const t = ctx.currentTime;
    const o = ctx.createOscillator(); o.type = 'sine';
    o.frequency.setValueAtTime(600, t); o.frequency.exponentialRampToValueAtTime(1200, t + 0.08);
    const g = ctx.createGain(); g.gain.setValueAtTime(0.15, t); g.gain.exponentialRampToValueAtTime(0.0001, t + 0.12);
    o.connect(g).connect(this.master); o.start(t); o.stop(t + 0.13);
  }

  /** optic up / down: short metallic click + cloth swish */
  scope(on: boolean) {
    const d = this.chain(1.0, 0, 0); if (!d) return;
    const t = this.ctx!.currentTime;
    this.burst(d, t, 0.03, on ? 3600 : 2800, 4, 0.9, 0.025);
    this.burst(d, t + 0.02, 0.12, 900, 1, 0.25, 0.1);
  }

  /** drawing / switching a weapon: clip-slap + bolt rack */
  equip() {
    if (this.sample('clip', 1.0, 0, 0, 1.25)) return;
    const d = this.chain(0.9, 0, 0); if (!d) return;
    const t = this.ctx!.currentTime;
    this.burst(d, t, 0.04, 2600, 3, 0.8, 0.035);
    this.burst(d, t + 0.12, 0.05, 3400, 3, 0.7, 0.04);
  }

  frost() {
    const d = this.chain(0.6, 0, 0); if (!d) return;
    const t = this.ctx!.currentTime;
    this.burst(d, t, 0.5, 4000, 2, 0.6, 0.4);
    this.burst(d, t + 0.05, 0.4, 2500, 3, 0.5, 0.35);
  }

  zoneTick() {
    const ctx = this.ctx; if (!ctx || !this.enabled) return;
    const t = ctx.currentTime;
    const o = ctx.createOscillator(); o.type = 'sawtooth'; o.frequency.value = 110;
    const g = ctx.createGain(); g.gain.setValueAtTime(0.08, t); g.gain.exponentialRampToValueAtTime(0.0001, t + 0.25);
    o.connect(g).connect(this.master); o.start(t); o.stop(t + 0.3);
  }
}

export const sfx = new Sfx();
