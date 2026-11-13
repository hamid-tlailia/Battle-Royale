/**
 * Custom HUD layout: every element with [data-hud] can be dragged and resized in edit mode.
 * Positions are stored as fractions of the screen so they survive rotation / other phones.
 */
/** size = width (px for buttons, % scale for panels); h = height (same units, defaults to size) */
type Layout = Record<string, { x: number; y: number; size: number; h?: number; hidden?: boolean; op?: number }>;
/** v2: new default layout (old custom layouts are reset once) */
const KEY = 'lw.layout.v2';

function load(): Layout {
  try { return JSON.parse(localStorage.getItem(KEY) || '{}'); } catch { return {}; }
}
function save(l: Layout) { try { localStorage.setItem(KEY, JSON.stringify(l)); } catch { /* ignore */ } }

/** Buttons are resized in px; panels (weapon bar, minimap, …) are scaled in % (data-scale). */
function place(el: HTMLElement, p: { x: number; y: number; size: number; h?: number; hidden?: boolean; op?: number }) {
  el.classList.toggle('hud-hidden', !!p.hidden);
  // see-through (filter, so it combines with the game's own fade rules)
  el.style.filter = p.op !== undefined && p.op < 1 ? `opacity(${p.op})` : '';
  el.style.left = `${p.x * 100}vw`;
  el.style.top = `${p.y * 100}vh`;
  el.style.right = 'auto';
  el.style.bottom = 'auto';
  const h = p.h ?? p.size;
  if (el.dataset.scale) el.style.transform = `translate(-50%, -50%) scale(${p.size / 100}, ${h / 100})`;
  else { el.style.width = `${p.size}px`; el.style.height = `${h}px`; el.style.transform = 'translate(-50%, -50%)'; }
}

export function layoutOf(id: string) { return load()[id] ?? null; }

export function applyLayout(hud: HTMLElement) {
  const l = load();
  hud.querySelectorAll<HTMLElement>('[data-hud]').forEach((el) => { const p = l[el.dataset.hud!]; if (p) place(el, p); });
}

export function editLayout(hud: HTMLElement, onDone?: () => void) {
  const layout = load();
  const els = [...hud.querySelectorAll<HTMLElement>('[data-hud]')];
  hud.classList.add('editing');
  // the editor must catch every touch (the game's look/joystick handlers stay idle)
  let selected: HTMLElement | null = null;
  const bar = document.createElement('div');
  bar.className = 'layout-bar';
  bar.innerHTML = `<span>Width</span>
    <input class="sz" type="range" min="40" max="130" step="2" value="70">
    <span>Height</span>
    <input class="hh" type="range" min="40" max="130" step="2" value="70">
    <span>See-through</span>
    <input class="op" type="range" min="20" max="100" step="5" value="100">
    <button data-a="hide">HIDE</button><button data-a="reset">RESET</button><button data-a="cancel">CANCEL</button><button data-a="save" class="ok">SAVE</button>`;
  hud.appendChild(bar);
  const slider = bar.querySelector('input.sz') as HTMLInputElement;
  const opSlider = bar.querySelector('input.op') as HTMLInputElement;
  const hSlider = bar.querySelector('input.hh') as HTMLInputElement;
  const current = (el: HTMLElement) => {
    const r = el.getBoundingClientRect();
    return { x: (r.left + r.width / 2) / window.innerWidth, y: (r.top + r.height / 2) / window.innerHeight, size: el.dataset.scale ? 100 : Math.round(r.width) };
  };
  const working: Layout = {};
  for (const el of els) working[el.dataset.hud!] = layout[el.dataset.hud!] ?? current(el);
  const select = (el: HTMLElement) => {
    selected?.classList.remove('sel');
    selected = el; el.classList.add('sel');
    slider.min = el.dataset.scale ? '50' : '40';
    slider.max = el.dataset.scale ? '160' : '130';
    slider.value = String(working[el.dataset.hud!].size);
    hSlider.min = slider.min; hSlider.max = slider.max;
    hSlider.value = String(working[el.dataset.hud!].h ?? working[el.dataset.hud!].size);
    opSlider.value = String(Math.round((working[el.dataset.hud!].op ?? 1) * 100));
    hideBtn.textContent = working[el.dataset.hud!].hidden ? 'SHOW' : 'HIDE';
  };
  const hideBtn = bar.querySelector('[data-a=hide]') as HTMLButtonElement;
  hideBtn.addEventListener('click', () => {
    if (!selected) return;
    const p = working[selected.dataset.hud!];
    p.hidden = !p.hidden;
    place(selected, p);
    hideBtn.textContent = p.hidden ? 'SHOW' : 'HIDE';
  });
  slider.addEventListener('input', () => {
    if (!selected) return;
    const p = working[selected.dataset.hud!];
    if (p.h === undefined) p.h = p.size; // keep the height when only the width changes
    p.size = +slider.value;
    place(selected, p);
  });
  hSlider.addEventListener('input', () => {
    if (!selected) return;
    working[selected.dataset.hud!].h = +hSlider.value;
    place(selected, working[selected.dataset.hud!]);
  });
  opSlider.addEventListener('input', () => {
    if (!selected) return;
    working[selected.dataset.hud!].op = +opSlider.value / 100;
    place(selected, working[selected.dataset.hud!]);
  });
  const handlers: [HTMLElement, (e: PointerEvent) => void][] = [];
  for (const el of els) {
    const down = (e: PointerEvent) => {
      e.preventDefault(); e.stopPropagation();
      select(el);
      el.setPointerCapture(e.pointerId);
      const move = (ev: PointerEvent) => {
        const p = working[el.dataset.hud!];
        p.x = Math.min(0.98, Math.max(0.02, ev.clientX / window.innerWidth));
        p.y = Math.min(0.97, Math.max(0.03, ev.clientY / window.innerHeight));
        place(el, p);
      };
      const up = () => { el.removeEventListener('pointermove', move); el.removeEventListener('pointerup', up); };
      el.addEventListener('pointermove', move);
      el.addEventListener('pointerup', up);
    };
    el.addEventListener('pointerdown', down, { capture: true });
    handlers.push([el, down]);
  }
  const finish = () => {
    for (const [el, h] of handlers) el.removeEventListener('pointerdown', h, { capture: true });
    selected?.classList.remove('sel');
    hud.classList.remove('editing');
    bar.remove();
  };
  const done = () => { if (onDone) onDone(); else location.reload(); };
  bar.querySelector('[data-a=save]')!.addEventListener('click', () => { save(working); finish(); if (onDone) onDone(); });
  bar.querySelector('[data-a=cancel]')!.addEventListener('click', () => { finish(); done(); });
  bar.querySelector('[data-a=reset]')!.addEventListener('click', () => { save({}); finish(); done(); });
}

/** Full-screen mock HUD to arrange the controls from the lobby (no game running). */
export function editLayoutStandalone(createButtons: (root: HTMLElement) => void) {
  const root = document.createElement('div');
  root.className = 'hud-root hud-mock';
  root.innerHTML = `
    <div class="minimap" data-hud="map" data-scale="1"></div>
    <div class="top-info" data-hud="info" data-scale="1"><div class="alive"><span>ALIVE</span><b>16</b><span>KILLS</span><b>0</b></div></div>
    <div class="killfeed" data-hud="feed" data-scale="1"><div class="kf"><b>Falcon</b> <i>Rifle</i> <b>Raven</b></div></div>
    <div class="bottom-bar" data-hud="weapons" data-scale="1"><div class="slots"><div class="slot sel"><i>30<small>/90</small></i></div><div class="slot"><i>12</i></div><div class="slot"><i>5</i></div><div class="slot"><i>5</i></div><div class="slot"><i>∞</i></div></div></div>
    <div class="bars-wrap" data-hud="health" data-scale="1"><div class="bars"><div class="bar hp"><div style="width:100%"></div><span>100</span></div></div></div>
    <button class="gear-btn" data-hud="settings" data-scale="1">⚙</button>
    <button class="dbg-btn" data-hud="debug" data-scale="1">DBG</button>
    <div class="joy-base" data-hud="joy" style="left:50px;bottom:50px;opacity:.6"><div class="joy-knob"></div></div>`;
  document.body.appendChild(root);
  createButtons(root);
  applyLayout(root);
  editLayout(root, () => root.remove());
}
