import * as THREE from 'three';
import { ICONS } from './icons';
import { MAP, MAX_ARMOR, MAX_HP, WEAPONS, type LootItem, type MapData, type SimPlayer, type ZoneState } from '@lw/shared';

const MINI_PX = 360; // drawing units
const MINI_RES = 2; // canvas pixels per unit: the big map stays sharp
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
  private statusRow!: HTMLDivElement;
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
  onFist: () => void = () => {};
  private fistSlot!: HTMLDivElement;
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
    this.mini.width = this.mini.height = MINI_PX * MINI_RES;
    hud.appendChild(this.mini);
    // tap the minimap to open the big map, tap again to close
    const toggleMap = (e: Event) => {
      if (hud.classList.contains('editing')) return;
      e.preventDefault(); e.stopPropagation();
      if (this.mini.classList.contains('big')) {
        // big map: tap inside = add a route point (tap an existing one to remove it), tap a corner = close
        const t = (e as TouchEvent).changedTouches?.[0] ?? (e as MouseEvent);
        const r = this.mini.getBoundingClientRect();
        const x = ((t.clientX - r.left) / r.width - 0.5) * MAP.size, z = ((t.clientY - r.top) / r.height - 0.5) * MAP.size;
        if (Math.hypot(x, z) < MAP.playRadius + 10) {
          const hit = this.waypoints.findIndex((w) => Math.hypot(w.x - x, w.z - z) < 14);
          if (hit >= 0) this.waypoints.splice(hit, 1);
          else if (this.waypoints.length < 4) this.waypoints.push({ x, z });
          this.onWaypoint(this.waypoints);
          this.miniTimer = 0;
          return;
        }
      }
      this.setBigMap(!this.mini.classList.contains('big'));
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
    // fist: put every gun away (long guns on the back, pistol in the pocket) and run empty-handed
    this.fistSlot = el('slot fist', slotRow, `${ICONS.fist}<i>∞</i>`);
    const fist = (e: Event) => { e.preventDefault(); e.stopPropagation(); this.onFist(); };
    this.fistSlot.addEventListener('touchstart', fist, { passive: false });
    this.fistSlot.addEventListener('mousedown', fist);
    this.ammo = el('ammo', bottom);
    this.reloadBar = el('reload-bar', bottom, '<div></div>');
    // health + status icons: their own HUD element (move / resize / fade it separately)
    const barsWrap = el('bars-wrap', hud);
    barsWrap.dataset.hud = 'health'; barsWrap.dataset.scale = '1';
    const bars = el('bars', barsWrap);
    // small status icons above the health bar: armor vest (level + points) and backpack
    this.statusRow = el('status-row', bars, `<span class="st-armor">${ICONS.vest}<b>0</b></span><span class="st-bag">${ICONS.bag}<b>Lv1</b></span>`);
    const armor = el('bar armor', bars, '<div></div><em>🛡</em><span></span>');
    const hp = el('bar hp', bars, '<div></div><em>❤</em><span></span>');
    this.armorTxt = armor.querySelector('span') as HTMLElement;
    this.toastEl = el('toasts', hud);
    this.armorFill = armor.firstElementChild as HTMLDivElement;
    this.hpFill = hp.firstElementChild as HTMLDivElement;
    this.hpTxt = hp.querySelector('span') as HTMLDivElement;
  }

  private mapShade: HTMLDivElement | null = null;
  /** big map open/closed; while open, a tap anywhere outside it closes it */
  setBigMap(on: boolean) {
    this.mini.classList.toggle('big', on);
    this.miniTimer = 0;
    this.mapShade?.remove(); this.mapShade = null;
    if (on) {
      this.mapShade = el('tap-shade map-shade', this.root);
      const close = (e: Event) => { e.preventDefault(); e.stopPropagation(); this.setBigMap(false); };
      this.mapShade.addEventListener('touchstart', close, { passive: false });
      this.mapShade.addEventListener('mousedown', close);
    }
  }

  private renderMiniBg(): HTMLCanvasElement {
    const c = document.createElement('canvas');
    c.width = c.height = MINI_PX * MINI_RES;
    const g = c.getContext('2d')!;
    g.scale(MINI_RES, MINI_RES);
    const k = MINI_PX / MAP.size;
    // terrain shading by height
    const n = this.map.cells + 1;
    for (let iz = 0; iz < n - 1; iz++) for (let ix = 0; ix < n - 1; ix++) {
      const h = this.map.heights[iz * n + ix];
      const l = Math.max(18, Math.min(80, 38 + h * 1.2));
      g.fillStyle = h > this.map.snowHeight ? '#dfe6ea' : h > 18 ? `hsl(90,8%,${l}%)` : `hsl(${100 - h},40%,${l * 0.8}%)`;
      g.fillRect(ix * (MINI_PX / this.map.cells), iz * (MINI_PX / this.map.cells), MINI_PX / this.map.cells + 1, MINI_PX / this.map.cells + 1);
    }
    g.fillStyle = '#e8d9c0';
    if (this.map.albedo) { // real-terrain map: aerial photo on top of the generated colours
      const img = new Image();
      img.onload = () => { g.imageSmoothingQuality = 'high'; g.drawImage(img, 0, 0, MINI_PX, MINI_PX); g.fillStyle = '#e8d9c0'; for (const p of this.map.props) if (p.p.startsWith('house') || p.p === 'farmhouse') g.fillRect((p.x + MAP.size / 2) * k - 4, (p.z + MAP.size / 2) * k - 4, 8, 8); this.miniTimer = 0; };
      img.src = `/assets/maps/${this.map.albedo}`;
    }
    for (const p of this.map.props) if (p.p.startsWith('house') || p.p === 'farmhouse') g.fillRect((p.x + MAP.size / 2) * k - 4, (p.z + MAP.size / 2) * k - 4, 8, 8);
    return c;
  }

  /** Backpack: everything you carry, each with a DROP button. */
  private bagShade: HTMLDivElement | null = null;
  toggleBag() {
    if (this.bag) { this.bag.remove(); this.bag = null; this.bagShade?.remove(); this.bagShade = null; return; }
    // a tap anywhere outside the backpack closes it
    this.bagShade = el('tap-shade', this.root);
    const close = (e: Event) => { e.preventDefault(); e.stopPropagation(); if (this.bag) this.toggleBag(); };
    this.bagShade.addEventListener('touchstart', close, { passive: false });
    this.bagShade.addEventListener('mousedown', close);
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
    // left: weapons + armor · right: ammo, meds, ice walls, scopes
    let left = '', right = '';
    p.weapons.forEach((w, i) => {
      const img = this.icons[w.id] ? `<img src="${this.icons[w.id]}" alt="">` : '<i class="ph"></i>';
      if (w.owned) left += row(img, names[i], `${w.mag} in magazine`, 1 + i);
      if (w.reserve > 0) right += row('<i class="ammo-ic"></i>', `${names[i]} ammo`, `${w.reserve} rounds`, 11 + i);
    });
    left += row('<i class="armor-ic">🛡</i>', 'Armor', `${Math.round(p.armor)} / 100`, 0);
    if (p.meds) right += row('<i class="med-ic">✚</i>', 'Medkit', `× ${p.meds} (+50 HP each)`, 20);
    if (p.frost) right += row('<i class="frost-ic">❄</i>', 'Ice wall', `× ${p.frost}`, 21);
    if (p.scope) right += row('<i class="scope-ic">◎</i>', `×${p.scope} scope`, 'fits the assault rifle', 22);
    if (!right) right = '<p class="bag-empty">No supplies yet</p>';
    const html = `<div class="bag-head"><b>BACKPACK</b><button class="bag-close">✕</button></div><div class="bag-cols"><div class="bag-list"><h4>WEAPONS & ARMOR</h4>${left}</div><div class="bag-list"><h4>SUPPLIES</h4>${right}</div></div>`;
    this.bag.innerHTML = html;
    this.bag.querySelector('.bag-close')!.addEventListener('click', () => this.toggleBag());
    this.bag.querySelectorAll<HTMLButtonElement>('[data-drop]').forEach((b) => b.addEventListener('click', () => this.onDrop(+b.dataset.drop!)));
  }

  /** Ground-loot menu: everything within reach, tap TAKE (or the row) to pick that item up. */
  onPick: (id: number) => void = () => {};
  private lootList: HTMLDivElement | null = null;
  private lootKey = '';
  setNearbyLoot(items: LootItem[]) {
    const key = items.map((i) => `${i.id}:${i.amount}`).join(',');
    if (key === this.lootKey) return;
    this.lootKey = key;
    if (!items.length) { this.lootList?.remove(); this.lootList = null; return; }
    if (!this.lootList) {
      this.lootList = el('loot-list', this.root);
      this.lootList.dataset.hud = 'loot';
      this.lootList.addEventListener('touchstart', (e) => e.stopPropagation(), { passive: true });
    }
    const names: Record<string, string> = { pistol: 'Pistol', rifle: 'Assault Rifle', shotgun: 'Shotgun', sniper: 'Sniper' };
    const row = (it: LootItem) => {
      let img = '', label = '', sub = '';
      switch (it.kind) {
        case 'weapon': img = this.icons[it.item as string] ? `<img src="${this.icons[it.item as string]}" alt="">` : ''; label = names[it.item as string]; sub = 'weapon'; break;
        case 'ammo': img = `<i class="ammo-ic"></i>`; label = `${names[it.item as string]} ammo`; sub = `${it.amount} rounds`; break;
        case 'med': img = '<i class="med-ic">✚</i>'; label = 'Medkit'; sub = `× ${it.amount}`; break;
        case 'armor': img = '<i class="armor-ic">🛡</i>'; label = 'Armor vest'; sub = '+50 armor'; break;
        case 'scope': img = '<i class="scope-ic">◎</i>'; label = `×${it.item} scope`; sub = 'assault rifle'; break;
        case 'frost': img = '<i class="frost-ic">❄</i>'; label = 'Ice wall'; sub = `× ${it.amount}`; break;
      }
      return `<div class="bag-row" data-pick="${it.id}">${img}<div class="bag-txt"><b>${label}</b><small>${sub}</small></div><button data-pick="${it.id}">TAKE</button></div>`;
    };
    this.lootList.innerHTML = `<div class="loot-head">${items.some((i) => i.box !== undefined) ? 'CRATE' : 'ON THE GROUND'}</div>${items.slice(0, 8).map(row).join('')}`;
    this.lootList.querySelectorAll<HTMLElement>('[data-pick]').forEach((b) => {
      const go = (e: Event) => { e.preventDefault(); e.stopPropagation(); this.onPick(+b.dataset.pick!); b.closest('.bag-row')?.classList.add('taking'); };
      b.addEventListener(b.tagName === 'BUTTON' ? 'touchstart' : 'touchend', go, { passive: false });
      b.addEventListener('click', go);
    });
  }

  private healRing: HTMLDivElement | null = null;
  /** medkit in use: circular loader in the middle of the screen */
  private setHealRing(frac: number) {
    if (frac <= 0) { this.healRing?.remove(); this.healRing = null; return; }
    if (!this.healRing) this.healRing = el('heal-ring', this.root, '<svg viewBox="0 0 40 40"><circle class="bg" cx="20" cy="20" r="16" fill="none" stroke-width="4"/><circle class="fg" cx="20" cy="20" r="16" fill="none" stroke-width="4" stroke-dasharray="100.5" stroke-dashoffset="100.5"/></svg><b>✚</b>');
    (this.healRing.querySelector('.fg') as SVGCircleElement).style.strokeDashoffset = String(100.5 * (1 - frac));
  }

  setSlots(p: SimPlayer, reloadFrac: number, healFrac = 0) {
    this.setHealRing(healFrac);
    this.renderBag(p);
    this.armorTxt.textContent = String(Math.round(p.armor));
    const lv = p.armor <= 0 ? 0 : p.armor > 50 ? 2 : 1;
    const st = this.statusRow.querySelector('.st-armor') as HTMLElement;
    st.classList.toggle('none', lv === 0); st.dataset.lv = String(lv);
    (st.querySelector('b') as HTMLElement).textContent = lv ? `Lv${lv} · ${Math.round(p.armor)}` : '—';
    this.healBtn?.querySelector('b')?.replaceChildren(String(p.meds));
    this.frostBtn?.querySelector('b')?.replaceChildren(String(p.frost));
    this.healBtn?.classList.toggle('empty', p.meds === 0);
    this.frostBtn?.classList.toggle('empty', p.frost === 0);
    if (healFrac > 0) reloadFrac = healFrac;
    this.reloadBar.classList.toggle('healing', healFrac > 0);
    this.fistSlot.classList.toggle('sel', p.holstered || !p.weapons[p.slot].owned);
    p.weapons.forEach((w, i) => {
      this.slots[i].classList.toggle('sel', i === p.slot && w.owned && !p.holstered);
      this.slots[i].classList.toggle('empty', !w.owned);
      const sel = i === p.slot && w.owned && !p.holstered;
      const txt = !w.owned ? (w.reserve ? String(w.reserve) : '') : sel ? `${w.mag}<small>/${w.reserve}</small>` : String(w.mag + w.reserve);
      const ie = this.slots[i].querySelector('i') as HTMLElement;
      if (ie.innerHTML !== txt) ie.innerHTML = txt;
      this.slots[i].style.setProperty('--fill', w.owned ? String(sel ? w.mag / WEAPONS[w.id].mag : 1) : '0');
    });
    const w = p.weapons[p.slot];
    const anyGun = p.weapons.some((x) => x.owned);
    this.ammo.classList.toggle('none', !anyGun);
    this.ammo.innerHTML = anyGun ? '' : '<span class="noweap">NO WEAPON · find one!</span>';
    this.slots.forEach((e, i) => e.classList.toggle('low', i === p.slot && w.owned && w.mag <= Math.ceil(WEAPONS[w.id].mag * 0.2)));
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
    g.setTransform(1, 0, 0, 1, 0, 0);
    g.clearRect(0, 0, this.mini.width, this.mini.height);
    g.drawImage(this.miniBg, 0, 0);
    g.setTransform(MINI_RES, 0, 0, MINI_RES, 0, 0);
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
    for (const m of this.hitMarks) { // who is shooting me: big red dot + red line from me toward them
      const a = Math.min(1, (m.until - now) / 1000);
      const sx = (m.x + o) * k, sz = (m.z + o) * k, mx = (me.x + o) * k, mz = (me.z + o) * k;
      const L = Math.hypot(sx - mx, sz - mz) || 1;
      g.strokeStyle = `rgba(255,30,30,${a})`; g.lineWidth = 9;
      g.beginPath(); g.moveTo(mx + (sx - mx) / L * 24, mz + (sz - mz) / L * 24); g.lineTo(mx + (sx - mx) / L * Math.min(L, 90), mz + (sz - mz) / L * Math.min(L, 90)); g.stroke();
      g.fillStyle = `rgba(255,30,30,${a})`; g.strokeStyle = `rgba(255,255,255,${a})`; g.lineWidth = 3;
      g.beginPath(); g.arc(sx, sz, 24, 0, Math.PI * 2); g.fill(); g.stroke();
    }
    // gunfire nearby: mark + a short line from me toward it
    this.shotMarks = this.shotMarks.filter((m) => m.until > now);
    for (const m of this.shotMarks) {
      const a = Math.min(1, (m.until - now) / 1200);
      const sx = (m.x + o) * k, sz = (m.z + o) * k, mx = (me.x + o) * k, mz = (me.z + o) * k;
      const L = Math.hypot(sx - mx, sz - mz) || 1;
      g.strokeStyle = `rgba(255,150,40,${a})`; g.lineWidth = 7;
      g.beginPath(); g.moveTo(mx + (sx - mx) / L * 24, mz + (sz - mz) / L * 24); g.lineTo(mx + (sx - mx) / L * 56, mz + (sz - mz) / L * 56); g.stroke();
      g.fillStyle = `rgba(255,150,40,${a})`; g.strokeStyle = `rgba(0,0,0,${a * 0.6})`; g.lineWidth = 2; g.beginPath(); g.arc(sx, sz, 12, 0, Math.PI * 2); g.fill(); g.stroke();
    }
    // place names
    g.font = `bold ${this.mini.classList.contains('big') ? 13 : 15}px sans-serif`; g.textAlign = 'center';
    for (const a of this.map.areas) {
      g.lineWidth = 3; g.strokeStyle = 'rgba(0,0,0,.65)'; g.strokeText(a.name, (a.x + o) * k, (a.z + o) * k);
      g.fillStyle = '#fff6d0'; g.fillText(a.name, (a.x + o) * k, (a.z + o) * k);
    }
    // route: me -> point 1 -> point 2 ...
    if (this.waypoints.length) {
      g.strokeStyle = '#ffd34d'; g.lineWidth = 3; g.setLineDash([8, 6]);
      g.beginPath(); g.moveTo((me.x + o) * k, (me.z + o) * k);
      for (const w of this.waypoints) g.lineTo((w.x + o) * k, (w.z + o) * k);
      g.stroke(); g.setLineDash([]);
      this.waypoints.forEach((w, i) => {
        const wx = (w.x + o) * k, wz = (w.z + o) * k;
        g.fillStyle = '#ffd34d'; g.strokeStyle = '#000'; g.lineWidth = 2;
        g.beginPath(); g.moveTo(wx, wz - 12); g.lineTo(wx + 9, wz); g.lineTo(wx, wz + 12); g.lineTo(wx - 9, wz); g.closePath(); g.fill(); g.stroke();
        g.fillStyle = '#000'; g.font = 'bold 12px sans-serif'; g.textAlign = 'center'; g.fillText(String(i + 1), wx, wz + 4);
      });
    }
    // me
    const px = (me.x + o) * k, pz = (me.z + o) * k;
    g.save(); g.translate(px, pz); g.rotate(-me.yaw);
    // view cone: where you are looking
    const cone = g.createRadialGradient(0, 0, 6, 0, 0, 100);
    cone.addColorStop(0, 'rgba(255,255,255,.6)'); cone.addColorStop(1, 'rgba(255,255,255,0)');
    g.fillStyle = cone; g.beginPath(); g.moveTo(0, 0); g.arc(0, 0, 100, -Math.PI / 2 - 0.55, -Math.PI / 2 + 0.55); g.closePath(); g.fill();
    g.fillStyle = '#ffd34d'; g.strokeStyle = '#000'; g.lineWidth = 3;
    g.beginPath(); g.moveTo(0, -26); g.lineTo(17, 17); g.lineTo(0, 8); g.lineTo(-17, 17); g.closePath(); g.fill(); g.stroke();
    g.restore();
  }

  private hitMarks: { x: number; z: number; until: number }[] = [];
  private shotMarks: { x: number; z: number; until: number }[] = [];
  /** someone fired nearby: orange mark on the map where the shot came from (2.5 s) */
  addShotMark(x: number, z: number) {
    const now = performance.now();
    const near = this.shotMarks.find((m) => Math.hypot(m.x - x, m.z - z) < 6);
    if (near) { near.until = now + 2500; return; }
    this.shotMarks.push({ x, z, until: now + 2500 });
    if (this.shotMarks.length > 12) this.shotMarks.shift();
    this.miniTimer = 0;
  }
  /** route points set by tapping the big map (first = next) */
  waypoints: { x: number; z: number }[] = [];
  onWaypoint: (w: { x: number; z: number }[]) => void = () => {};
  private wpArrow: HTMLDivElement | null = null;
  /** arrow at the top of the screen toward the next route point, with the distance */
  setRouteArrow(angle: number | null, dist = 0) {
    if (angle === null) { this.wpArrow?.remove(); this.wpArrow = null; return; }
    if (!this.wpArrow) this.wpArrow = el('wp-arrow', this.root, '<i>▲</i><b></b>');
    (this.wpArrow.firstElementChild as HTMLElement).style.transform = `rotate(${angle}rad)`;
    (this.wpArrow.lastElementChild as HTMLElement).textContent = `${Math.round(dist)} m`;
  }
  private areaEls = new Map<string, HTMLDivElement>();
  /** place names floating over the world while you are high up (from the plane / sky-diving) */
  setAreaLabels(list: { name: string; sx: number; sy: number; alpha: number }[]) {
    const seen = new Set<string>();
    for (const a of list) {
      seen.add(a.name);
      let e = this.areaEls.get(a.name);
      if (!e) { e = el('area-label', this.root); e.textContent = a.name; this.areaEls.set(a.name, e); }
      e.style.transform = `translate(${a.sx}px, ${a.sy}px) translate(-50%, -50%)`;
      e.style.opacity = String(a.alpha);
    }
    for (const [n, e] of this.areaEls) if (!seen.has(n)) { e.remove(); this.areaEls.delete(n); }
  }
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
    const text = inPlane ? 'Tap the map to mark a landing spot · tap <b>JUMP</b> to drop' : falling ? 'You glide where you look · tap <b>JUMP</b> to drop straight down · the parachute opens by itself' : '';
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

  private killEl: HTMLDivElement | null = null;
  private killTimer = 0;
  /** you took someone down: white skull on a red blood splash at the top of the screen */
  killBanner(kills: number) {
    this.killEl?.remove();
    this.killEl = el('kill-banner', this.root, `<div class="kb-blood"></div>${ICONS.skull}<b>${kills}</b>`);
    clearTimeout(this.killTimer);
    const e = this.killEl;
    this.killTimer = window.setTimeout(() => { e.classList.add('out'); setTimeout(() => e.remove(), 400); }, 1800);
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

  /** Victory: banner + confetti for 5 s (the game then returns to the lobby). */
  celebrate(kills: number, total: number) {
    const o = el('celebrate', this.root, `<div class="cb-box"><h1>VICTORY</h1><div class="cb-place">#1 <small>/ ${total}</small></div><p>Last one standing · ${kills} kill${kills === 1 ? '' : 's'}</p></div>`);
    const colors = ['#ffd34d', '#ff6b6b', '#6fcf6f', '#56a8f5', '#ffffff', '#c58bff'];
    for (let i = 0; i < 90; i++) {
      const c = document.createElement('i');
      c.style.left = `${Math.random() * 100}%`;
      c.style.background = colors[i % colors.length];
      c.style.animationDelay = `${Math.random() * 1.6}s`;
      c.style.animationDuration = `${2.2 + Math.random() * 2}s`;
      c.style.transform = `rotate(${Math.random() * 360}deg)`;
      o.appendChild(c);
    }
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
