import * as THREE from 'three';
import { MAP, MAX_ARMOR, MAX_HP, WEAPONS, type MapData, type SimPlayer, type ZoneState } from '@lw/shared';

const el = (cls: string, parent: HTMLElement, html = '') => {
  const d = document.createElement('div');
  d.className = cls;
  d.innerHTML = html;
  parent.appendChild(d);
  return d;
};

interface Floater { el: HTMLDivElement; pos: THREE.Vector3; life: number }

export class Hud {
  readonly root: HTMLElement;
  private alive: HTMLDivElement;
  private zoneTxt: HTMLDivElement;
  private hpFill: HTMLDivElement;
  private armorFill: HTMLDivElement;
  private hpTxt: HTMLDivElement;
  private slots: HTMLDivElement[] = [];
  private ammo: HTMLDivElement;
  private reloadBar: HTMLDivElement;
  private feed: HTMLDivElement;
  private dmgLayer: HTMLDivElement;
  private floaters: Floater[] = [];
  private cross: HTMLDivElement;
  private hitTimer = 0;
  private scope: HTMLDivElement;
  private reddot: HTMLDivElement;
  private scopeMode = 'none';
  private vignette: HTMLDivElement;
  private hurt: HTMLDivElement;
  private mini: HTMLCanvasElement;
  private miniBg: HTMLCanvasElement;
  private miniTimer = 0;
  private overlay: HTMLDivElement | null = null;
  onSlot: (i: number) => void = () => {};

  constructor(hud: HTMLElement, private map: MapData) {
    this.root = hud;
    this.vignette = el('zone-vignette', hud);
    this.hurt = el('hurt-flash', hud);
    this.scope = el('scope-overlay', hud, '<div class="scope-lens"></div>');
    this.reddot = el('reddot-overlay', hud);
    this.cross = el('crosshair', hud);
    const top = el('top-info', hud);
    this.alive = el('alive', top);
    this.zoneTxt = el('zone-txt', top);
    this.mini = document.createElement('canvas');
    this.mini.className = 'minimap';
    this.mini.width = this.mini.height = 220;
    hud.appendChild(this.mini);
    this.miniBg = this.renderMiniBg();
    this.feed = el('killfeed', hud);
    this.dmgLayer = el('dmg-layer', hud);

    const bottom = el('bottom-bar', hud);
    const slotRow = el('slots', bottom);
    ['pistol', 'rifle', 'shotgun', 'sniper'].forEach((w, i) => {
      const s = el('slot', slotRow, `<b>${i + 1}</b><span>${['Pistol', 'AR', 'Shotgun', 'Sniper'][i]}</span><i></i>`);
      const pick = (e: Event) => { e.preventDefault(); e.stopPropagation(); this.onSlot(i); };
      s.addEventListener('touchstart', pick, { passive: false });
      s.addEventListener('mousedown', pick);
      this.slots.push(s);
    });
    this.ammo = el('ammo', bottom);
    this.reloadBar = el('reload-bar', bottom, '<div></div>');
    const bars = el('bars', bottom);
    const armor = el('bar armor', bars, '<div></div>');
    const hp = el('bar hp', bars, '<div></div><span></span>');
    this.armorFill = armor.firstElementChild as HTMLDivElement;
    this.hpFill = hp.firstElementChild as HTMLDivElement;
    this.hpTxt = hp.querySelector('span') as HTMLDivElement;
  }

  private renderMiniBg(): HTMLCanvasElement {
    const c = document.createElement('canvas');
    c.width = c.height = 220;
    const g = c.getContext('2d')!;
    const k = 220 / MAP.size;
    // terrain shading by height
    const n = this.map.cells + 1;
    for (let iz = 0; iz < n - 1; iz++) for (let ix = 0; ix < n - 1; ix++) {
      const h = this.map.heights[iz * n + ix];
      const l = Math.max(18, Math.min(80, 38 + h * 1.2));
      g.fillStyle = h > MAP.snowHeight ? '#dfe6ea' : h > 18 ? `hsl(90,8%,${l}%)` : `hsl(${100 - h},40%,${l * 0.8}%)`;
      g.fillRect(ix * (220 / this.map.cells), iz * (220 / this.map.cells), 220 / this.map.cells + 1, 220 / this.map.cells + 1);
    }
    g.fillStyle = '#e8d9c0';
    for (const p of this.map.props) if (p.p.startsWith('house')) g.fillRect((p.x + MAP.size / 2) * k - 3, (p.z + MAP.size / 2) * k - 3, 6, 6);
    return c;
  }

  setSlots(p: SimPlayer, reloadFrac: number) {
    p.weapons.forEach((w, i) => {
      this.slots[i].classList.toggle('sel', i === p.slot);
      (this.slots[i].querySelector('i') as HTMLElement).textContent = `${w.mag}/${w.reserve}`;
    });
    const w = p.weapons[p.slot];
    this.ammo.innerHTML = `<b>${w.mag}</b><span>/ ${w.reserve}</span>`;
    this.ammo.classList.toggle('low', w.mag <= Math.ceil(WEAPONS[w.id].mag * 0.2));
    this.reloadBar.style.opacity = reloadFrac > 0 ? '1' : '0';
    (this.reloadBar.firstElementChild as HTMLElement).style.width = `${reloadFrac * 100}%`;
    this.hpFill.style.width = `${(Math.max(0, p.hp) / MAX_HP) * 100}%`;
    this.hpFill.classList.toggle('crit', p.hp < 30);
    this.armorFill.style.width = `${(p.armor / MAX_ARMOR) * 100}%`;
    this.hpTxt.textContent = String(Math.max(0, Math.ceil(p.hp)));
  }

  setInfo(alive: number, kills: number, zone: ZoneState, me: { x: number; z: number; yaw: number }, outside: boolean, dt: number) {
    this.alive.innerHTML = `<span>ALIVE</span><b>${alive}</b><span>KILLS</span><b>${kills}</b>`;
    const m = Math.floor(zone.timer / 60), s = Math.floor(zone.timer % 60);
    const t = `${m}:${String(s).padStart(2, '0')}`;
    const dist = Math.max(0, Math.hypot(me.x - zone.nx, me.z - zone.nz) - zone.nr);
    this.zoneTxt.textContent = zone.phase >= 5 ? 'FINAL ZONE' : zone.shrinking ? `ZONE SHRINKING ${t}` : `ZONE SHRINKS IN ${t}${dist > 0 ? ` · ${Math.round(dist)} m` : ''}`;
    this.zoneTxt.classList.toggle('warn', outside);
    this.vignette.style.opacity = outside ? '1' : '0';
    this.miniTimer -= dt;
    if (this.miniTimer <= 0) { this.miniTimer = 0.15; this.drawMini(zone, me); }
  }

  private drawMini(zone: ZoneState, me: { x: number; z: number; yaw: number }) {
    const g = this.mini.getContext('2d')!;
    const k = 220 / MAP.size, o = MAP.size / 2;
    g.clearRect(0, 0, 220, 220);
    g.drawImage(this.miniBg, 0, 0);
    // outside zone tint
    g.save();
    g.fillStyle = 'rgba(40,90,200,0.35)';
    g.beginPath(); g.rect(0, 0, 220, 220); g.arc((zone.cx + o) * k, (zone.cz + o) * k, zone.r * k, 0, Math.PI * 2, true); g.fill('evenodd');
    g.restore();
    g.lineWidth = 2;
    g.strokeStyle = '#4fb0ff'; g.beginPath(); g.arc((zone.cx + o) * k, (zone.cz + o) * k, zone.r * k, 0, Math.PI * 2); g.stroke();
    g.strokeStyle = '#fff'; g.setLineDash([5, 4]); g.beginPath(); g.arc((zone.nx + o) * k, (zone.nz + o) * k, Math.max(0.5, zone.nr * k), 0, Math.PI * 2); g.stroke(); g.setLineDash([]);
    // me
    const px = (me.x + o) * k, pz = (me.z + o) * k;
    g.save(); g.translate(px, pz); g.rotate(-me.yaw);
    g.fillStyle = '#ffd34d'; g.strokeStyle = '#000'; g.lineWidth = 1.5;
    g.beginPath(); g.moveTo(0, -9); g.lineTo(6, 6); g.lineTo(0, 3); g.lineTo(-6, 6); g.closePath(); g.fill(); g.stroke();
    g.restore();
  }

  killFeed(html: string, mine: boolean) {
    const row = el(`kf${mine ? ' mine' : ''}`, this.feed, html);
    while (this.feed.children.length > 5) this.feed.firstElementChild!.remove();
    setTimeout(() => row.remove(), 5500);
  }

  damageNumber(pos: THREE.Vector3, dmg: number, head: boolean, armor: boolean) {
    const d = document.createElement('div');
    d.className = `dmg${head ? ' head' : ''}${armor ? ' armor' : ''}`;
    d.textContent = String(dmg);
    this.dmgLayer.appendChild(d);
    this.floaters.push({ el: d, pos: pos.clone().add(new THREE.Vector3((Math.random() - 0.5) * 0.4, 0.3, 0)), life: 0.9 });
  }

  hitMarker(kill: boolean) {
    this.cross.classList.remove('hit', 'killhit');
    void this.cross.offsetWidth;
    this.cross.classList.add(kill ? 'killhit' : 'hit');
    this.hitTimer = 0.25;
  }

  hurtFlash() {
    this.hurt.classList.remove('on'); void this.hurt.offsetWidth; this.hurt.classList.add('on');
  }

  setScope(mode: 'none' | 'reddot' | 'scope') {
    if (mode === this.scopeMode) return;
    this.scopeMode = mode;
    this.scope.style.display = mode === 'scope' ? 'block' : 'none';
    this.reddot.style.display = mode === 'reddot' ? 'block' : 'none';
    this.cross.style.display = mode === 'none' ? 'block' : 'none';
  }

  /** crosshair turns red while it is over an enemy */
  setOnEnemy(on: boolean) { this.cross.classList.toggle('enemy', on); }

  update(dt: number, camera: THREE.Camera) {
    const w = window.innerWidth, h = window.innerHeight;
    const v = new THREE.Vector3();
    this.floaters = this.floaters.filter((f) => {
      f.life -= dt;
      if (f.life <= 0) { f.el.remove(); return false; }
      f.pos.y += dt * 0.9;
      v.copy(f.pos).project(camera);
      if (v.z > 1) { f.el.style.display = 'none'; return true; }
      f.el.style.display = 'block';
      f.el.style.transform = `translate(${(v.x * 0.5 + 0.5) * w}px, ${(-v.y * 0.5 + 0.5) * h}px) translate(-50%,-50%) scale(${0.8 + f.life * 0.4})`;
      f.el.style.opacity = String(Math.min(1, f.life * 2.5));
      return true;
    });
    if (this.hitTimer > 0) { this.hitTimer -= dt; if (this.hitTimer <= 0) this.cross.classList.remove('hit', 'killhit'); }
  }

  showEnd(win: boolean, place: number, total: number, kills: number, killer: string | null, onAgain: () => void) {
    this.overlay?.remove();
    this.overlay = el(`end-screen${win ? ' win' : ''}`, this.root, `
      <div class="end-card">
        <h2>${win ? 'VICTORY' : 'ELIMINATED'}</h2>
        <p class="place">#${place} <span>/ ${total}</span></p>
        <p>${win ? 'Last one standing!' : killer ? `Taken down by <b>${killer}</b>` : 'Lost in the zone'}</p>
        <p class="stats">Kills <b>${kills}</b></p>
        <div class="end-btns">${win ? '' : '<button class="spec">SPECTATE</button>'}<button class="again">PLAY AGAIN</button></div>
      </div>`);
    const again = this.overlay.querySelector('.again') as HTMLButtonElement;
    again.addEventListener('click', onAgain);
    again.addEventListener('touchend', (e) => { e.preventDefault(); onAgain(); });
    const spec = this.overlay.querySelector('.spec') as HTMLButtonElement | null;
    const hide = (e: Event) => { e.preventDefault(); this.overlay?.classList.add('mini'); };
    spec?.addEventListener('click', hide);
    spec?.addEventListener('touchend', hide);
  }
}
