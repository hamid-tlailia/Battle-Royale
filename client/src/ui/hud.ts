import * as THREE from 'three';
import { MAP, MAX_ARMOR, MAX_HP, WEAPONS, type MapData, type SimPlayer, type ZoneState } from '@lw/shared';

const MINI_PX = 360;
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
  private armorTxt!: HTMLElement;
  private toastEl!: HTMLDivElement;
  healBtn: HTMLElement | null = null;
  frostBtn: HTMLElement | null = null;
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
  private scopeMode: string = 'none';
  private vignette: HTMLDivElement;
  private hurt: HTMLDivElement;
  private mini: HTMLCanvasElement;
  private miniBg: HTMLCanvasElement;
  private miniTimer = 0;
  private overlay: HTMLDivElement | null = null;
  onSlot: (i: number) => void = () => {};

  onDrop: (code: number) => void = () => {};
  private bag: HTMLDivElement | null = null;
  private bagKey = '';

  constructor(hud: HTMLElement, private map: MapData, private icons: Partial<Record<string, string>> = {}) {
    this.root = hud;
    this.vignette = el('zone-vignette', hud);
    this.hurt = el('hurt-flash', hud);
    this.scope = el('scope-overlay', hud, `<div class="scope-lens">
      <svg class="reticle r8" viewBox="-100 -100 200 200">
        <path d="M-100 0H-14M14 0H100M0 -100V-14M0 14V100" stroke="#000" stroke-width="1.2"/>
        <path d="M-100 0H-60M60 0H100M0 60V100" stroke="#000" stroke-width="5"/>
        ${[-40, -30, -20, 20, 30, 40].map((x) => `<circle cx="${x}" cy="0" r="1.6" fill="#000"/><circle cx="0" cy="${x}" r="1.6" fill="#000"/>`).join('')}
        <circle class="dot" cx="0" cy="0" r="1.8"/>
      </svg>
      <svg class="reticle r4" viewBox="-100 -100 200 200">
        <path d="M-100 0H-8M8 0H100M0 8V100" stroke="#111" stroke-width="1"/>
        ${[12, 20, 28, 36].map((y) => `<path d="M-${y / 4} ${y}H${y / 4}" stroke="#111" stroke-width="1"/>`).join('')}
        <path class="dot" d="M-5 6L0 0L5 6" fill="none" stroke-width="1.6"/>
      </svg>
      <svg class="reticle r2" viewBox="-100 -100 200 200">
        <circle cx="0" cy="0" r="22" fill="none" stroke="#111" stroke-width="1.4"/>
        <path d="M-22 0H-12M12 0H22M0 22V12" stroke="#111" stroke-width="1.4"/>
        <circle class="dot" cx="0" cy="0" r="2.2"/>
      </svg>
      <div class="zoom-tag"></div></div>`);
    this.reddot = el('reddot-overlay', hud, '<div class="holo"><i></i></div>');
    this.cross = el('crosshair', hud);
    const top = el('top-info', hud);
    top.dataset.hud = 'info'; top.dataset.scale = '1';
    this.alive = el('alive', top);
    this.zoneTxt = el('zone-txt', top);
    this.mini = document.createElement('canvas');
    this.mini.className = 'minimap';
    this.mini.dataset.hud = 'map'; this.mini.dataset.scale = '1';
    this.mini.width = this.mini.height = MINI_PX;
    hud.appendChild(this.mini);
    // tap the minimap to open the big map, tap again to close
    const toggleMap = (e: Event) => {
      if (hud.classList.contains('editing')) return;
      e.preventDefault(); e.stopPropagation();
      this.mini.classList.toggle('big');
    };
    this.mini.addEventListener('touchstart', toggleMap, { passive: false });
    this.mini.addEventListener('mousedown', toggleMap);
    this.miniBg = this.renderMiniBg();
    this.feed = el('killfeed', hud);
    this.feed.dataset.hud = 'feed'; this.feed.dataset.scale = '1';
    this.dmgLayer = el('dmg-layer', hud);

    const bottom = el('bottom-bar', hud);
    bottom.dataset.hud = 'weapons'; bottom.dataset.scale = '1';
    const slotRow = el('slots', bottom);
    ['pistol', 'rifle', 'shotgun', 'sniper'].forEach((w, i) => {
      const pic = this.icons[w] ? `<img src="${this.icons[w]}" alt="">` : `<span>${['Pistol', 'AR', 'Shotgun', 'Sniper'][i]}</span>`;
      const s = el('slot', slotRow, `<b>${i + 1}</b>${pic}<i></i>`);
      const pick = (e: Event) => { e.preventDefault(); e.stopPropagation(); this.onSlot(i); };
      s.addEventListener('touchstart', pick, { passive: false });
      s.addEventListener('mousedown', pick);
      this.slots.push(s);
    });
    this.ammo = el('ammo', bottom);
    this.reloadBar = el('reload-bar', bottom, '<div></div>');
    const bars = el('bars', bottom);
    const armor = el('bar armor', bars, '<div></div><em>🛡</em><span></span>');
    const hp = el('bar hp', bars, '<div></div><em>❤</em><span></span>');
    this.armorTxt = armor.querySelector('span') as HTMLElement;
    this.toastEl = el('toasts', hud);
    this.armorFill = armor.firstElementChild as HTMLDivElement;
    this.hpFill = hp.firstElementChild as HTMLDivElement;
    this.hpTxt = hp.querySelector('span') as HTMLDivElement;
  }

  private renderMiniBg(): HTMLCanvasElement {
    const c = document.createElement('canvas');
    c.width = c.height = MINI_PX;
    const g = c.getContext('2d')!;
    const k = MINI_PX / MAP.size;
    // terrain shading by height
    const n = this.map.cells + 1;
    for (let iz = 0; iz < n - 1; iz++) for (let ix = 0; ix < n - 1; ix++) {
      const h = this.map.heights[iz * n + ix];
      const l = Math.max(18, Math.min(80, 38 + h * 1.2));
      g.fillStyle = h > MAP.snowHeight ? '#dfe6ea' : h > 18 ? `hsl(90,8%,${l}%)` : `hsl(${100 - h},40%,${l * 0.8}%)`;
      g.fillRect(ix * (MINI_PX / this.map.cells), iz * (MINI_PX / this.map.cells), MINI_PX / this.map.cells + 1, MINI_PX / this.map.cells + 1);
    }
    g.fillStyle = '#e8d9c0';
    for (const p of this.map.props) if (p.p.startsWith('house')) g.fillRect((p.x + MAP.size / 2) * k - 4, (p.z + MAP.size / 2) * k - 4, 8, 8);
    return c;
  }

  /** Backpack: everything you carry, each with a DROP button. */
  toggleBag() {
    if (this.bag) { this.bag.remove(); this.bag = null; return; }
    this.bag = el('bag-panel', this.root);
    this.bag.addEventListener('touchstart', (e) => e.stopPropagation(), { passive: true });
    this.bagKey = '';
  }

  private renderBag(p: SimPlayer) {
    if (!this.bag) return;
    const key = JSON.stringify([p.weapons, p.meds, p.frost, p.scope, p.armor]);
    if (key === this.bagKey) return;
    this.bagKey = key;
    const names = ['Pistol', 'Assault Rifle', 'Shotgun', 'Sniper'];
    const row = (img: string, label: string, sub: string, code: number) =>
      `<div class="bag-row">${img}<div class="bag-txt"><b>${label}</b><small>${sub}</small></div>${code ? `<button data-drop="${code}">DROP</button>` : ''}</div>`;
    let html = '<div class="bag-head"><b>BACKPACK</b><button class="bag-close">✕</button></div><div class="bag-list">';
    p.weapons.forEach((w, i) => {
      const img = this.icons[w.id] ? `<img src="${this.icons[w.id]}" alt="">` : '<i class="ph"></i>';
      if (w.owned) html += row(img, names[i], `${w.mag} in magazine`, 1 + i);
      if (w.reserve > 0) html += row('<i class="ammo-ic"></i>', `${names[i]} ammo`, `${w.reserve} rounds`, 11 + i);
    });
    if (p.meds) html += row('<i class="med-ic">✚</i>', 'Medkit', `× ${p.meds} (+50 HP each)`, 20);
    if (p.frost) html += row('<i class="frost-ic">❄</i>', 'Ice wall', `× ${p.frost}`, 21);
    if (p.scope) html += row('<i class="scope-ic">◎</i>', `×${p.scope} scope`, 'fits the assault rifle', 22);
    html += row('<i class="armor-ic">🛡</i>', 'Armor', `${Math.round(p.armor)} / 100`, 0);
    html += '</div>';
    this.bag.innerHTML = html;
    this.bag.querySelector('.bag-close')!.addEventListener('click', () => this.toggleBag());
    this.bag.querySelectorAll<HTMLButtonElement>('[data-drop]').forEach((b) => b.addEventListener('click', () => this.onDrop(+b.dataset.drop!)));
  }

  setSlots(p: SimPlayer, reloadFrac: number, healFrac = 0) {
    this.renderBag(p);
    this.armorTxt.textContent = String(Math.round(p.armor));
    this.healBtn?.querySelector('b')?.replaceChildren(String(p.meds));
    this.frostBtn?.querySelector('b')?.replaceChildren(String(p.frost));
    this.healBtn?.classList.toggle('empty', p.meds === 0);
    this.frostBtn?.classList.toggle('empty', p.frost === 0);
    if (healFrac > 0) reloadFrac = healFrac;
    this.reloadBar.classList.toggle('healing', healFrac > 0);
    p.weapons.forEach((w, i) => {
      this.slots[i].classList.toggle('sel', i === p.slot && w.owned);
      this.slots[i].classList.toggle('empty', !w.owned);
      (this.slots[i].querySelector('i') as HTMLElement).textContent = w.owned ? `${w.mag}/${w.reserve}` : w.reserve ? `— /${w.reserve}` : '—';
    });
    const w = p.weapons[p.slot];
    this.ammo.innerHTML = w.owned ? `<b>${w.mag}</b><span>/ ${w.reserve}</span>` : '<span class="noweap">NO WEAPON · find one!</span>';
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
    const k = MINI_PX / MAP.size, o = MAP.size / 2;
    g.clearRect(0, 0, MINI_PX, MINI_PX);
    g.drawImage(this.miniBg, 0, 0);
    // outside zone tint
    g.save();
    g.fillStyle = 'rgba(40,90,200,0.35)';
    g.beginPath(); g.rect(0, 0, MINI_PX, MINI_PX); g.arc((zone.cx + o) * k, (zone.cz + o) * k, zone.r * k, 0, Math.PI * 2, true); g.fill('evenodd');
    g.restore();
    g.lineWidth = 3;
    g.strokeStyle = '#4fb0ff'; g.beginPath(); g.arc((zone.cx + o) * k, (zone.cz + o) * k, zone.r * k, 0, Math.PI * 2); g.stroke();
    g.strokeStyle = '#fff'; g.setLineDash([5, 4]); g.beginPath(); g.arc((zone.nx + o) * k, (zone.nz + o) * k, Math.max(0.5, zone.nr * k), 0, Math.PI * 2); g.stroke(); g.setLineDash([]);
    // who shot me recently
    const now = performance.now();
    this.hitMarks = this.hitMarks.filter((m) => m.until > now);
    for (const m of this.hitMarks) {
      g.fillStyle = `rgba(255,40,40,${Math.min(1, (m.until - now) / 1000)})`;
      g.beginPath(); g.arc((m.x + o) * k, (m.z + o) * k, 9, 0, Math.PI * 2); g.fill();
    }
    // me
    const px = (me.x + o) * k, pz = (me.z + o) * k;
    g.save(); g.translate(px, pz); g.rotate(-me.yaw);
    g.fillStyle = '#ffd34d'; g.strokeStyle = '#000'; g.lineWidth = 1.5;
    g.beginPath(); g.moveTo(0, -14); g.lineTo(9, 9); g.lineTo(0, 4); g.lineTo(-9, 9); g.closePath(); g.fill(); g.stroke();
    g.restore();
  }

  private hitMarks: { x: number; z: number; until: number }[] = [];
  private planeHint: HTMLDivElement | null = null;
  private specEl: HTMLDivElement | null = null;

  /** Red arc pointing toward whoever is shooting you (+ red dot on the minimap for 3 s). */
  damageFrom(angle: number, ax: number, az: number) {
    const arc = el('dmg-dir', this.root);
    arc.style.transform = `translate(-50%, -50%) rotate(${-angle}rad)`;
    setTimeout(() => arc.remove(), 1400);
    this.hitMarks.push({ x: ax, z: az, until: performance.now() + 3000 });
    this.miniTimer = 0;
  }

  setPlaneHint(inPlane: boolean, falling: boolean) {
    const text = inPlane ? 'Tap <b>JUMP</b> to drop' : falling ? 'Push forward to dive · parachute opens automatically' : '';
    if (!text) { this.planeHint?.remove(); this.planeHint = null; return; }
    if (!this.planeHint) this.planeHint = el('plane-hint', this.root);
    if (this.planeHint.innerHTML !== text) this.planeHint.innerHTML = text;
    this.root.classList.toggle('in-plane', inPlane);
  }

  setSpectating(name: string | null) {
    if (!name) { this.specEl?.remove(); this.specEl = null; return; }
    if (!this.specEl) this.specEl = el('spec-banner', this.root);
    const t = `SPECTATING · <b>${name}</b>`;
    if (this.specEl.innerHTML !== t) this.specEl.innerHTML = t;
  }

  /** Short pickup / info message above the weapon bar. */
  toast(text: string) {
    const t = el('toast', this.toastEl, text);
    while (this.toastEl.children.length > 3) this.toastEl.firstElementChild!.remove();
    setTimeout(() => t.remove(), 1800);
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

  /** none / red-dot sight / magnified optics (x2, x4, x8 — each with its own lens size & reticle). */
  setScope(mode: 'none' | 'reddot' | 'x2' | 'x4' | 'x8') {
    if (mode === this.scopeMode) return;
    this.scopeMode = mode;
    const optic = mode === 'x2' || mode === 'x4' || mode === 'x8';
    this.scope.style.display = optic ? 'block' : 'none';
    this.scope.dataset.zoom = mode;
    (this.scope.querySelector('.zoom-tag') as HTMLElement).textContent = optic ? mode.replace('x', '×') : '';
    this.reddot.style.display = mode === 'reddot' ? 'block' : 'none';
    this.cross.style.display = mode === 'none' ? 'block' : 'none';
  }

  /** crosshair turns red while it is over an enemy */
  setOnEnemy(on: boolean) {
    this.cross.classList.toggle('enemy', on);
    this.reddot.classList.toggle('enemy', on);
    this.scope.classList.toggle('enemy', on);
  }

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

  showEnd(win: boolean, place: number, total: number, kills: number, killer: string | null, onAgain: () => void, onSpectate?: () => void) {
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
    const hide = (e: Event) => { e.preventDefault(); this.overlay?.classList.add('mini'); onSpectate?.(); };
    spec?.addEventListener('click', hide);
    spec?.addEventListener('touchend', hide);
  }
}
