/**
 * Custom HUD layout: every element with [data-hud] can be dragged and resized in edit mode.
 * Positions are stored as fractions of the screen so they survive rotation / other phones.
 */
type Layout = Record<string, { x: number; y: number; size: number }>;
const KEY = 'lw.layout.v1';

function load(): Layout {
  try { return JSON.parse(localStorage.getItem(KEY) || '{}'); } catch { return {}; }
}
function save(l: Layout) { try { localStorage.setItem(KEY, JSON.stringify(l)); } catch { /* ignore */ } }

function place(el: HTMLElement, p: { x: number; y: number; size: number }) {
  el.style.left = `${p.x * 100}vw`;
  el.style.top = `${p.y * 100}vh`;
  el.style.right = 'auto';
  el.style.bottom = 'auto';
  el.style.width = el.style.height = `${p.size}px`;
  el.style.transform = 'translate(-50%, -50%)';
}

export function applyLayout(hud: HTMLElement) {
  const l = load();
  hud.querySelectorAll<HTMLElement>('[data-hud]').forEach((el) => { const p = l[el.dataset.hud!]; if (p) place(el, p); });
}

export function editLayout(hud: HTMLElement) {
  const layout = load();
  const els = [...hud.querySelectorAll<HTMLElement>('[data-hud]')];
  hud.classList.add('editing');
  let selected: HTMLElement | null = null;
  const bar = document.createElement('div');
  bar.className = 'layout-bar';
  bar.innerHTML = `<span>Drag buttons · pick one to resize</span>
    <input type="range" min="40" max="130" step="2" value="70">
    <button data-a="reset">RESET</button><button data-a="cancel">CANCEL</button><button data-a="save" class="ok">SAVE</button>`;
  hud.appendChild(bar);
  const slider = bar.querySelector('input')!;
  const current = (el: HTMLElement) => {
    const r = el.getBoundingClientRect();
    return { x: (r.left + r.width / 2) / window.innerWidth, y: (r.top + r.height / 2) / window.innerHeight, size: Math.round(r.width) };
  };
  const working: Layout = {};
  for (const el of els) working[el.dataset.hud!] = layout[el.dataset.hud!] ?? current(el);
  const select = (el: HTMLElement) => {
    selected?.classList.remove('sel');
    selected = el; el.classList.add('sel');
    slider.value = String(working[el.dataset.hud!].size);
  };
  slider.addEventListener('input', () => {
    if (!selected) return;
    working[selected.dataset.hud!].size = +slider.value;
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
  bar.querySelector('[data-a=save]')!.addEventListener('click', () => { save(working); finish(); });
  bar.querySelector('[data-a=cancel]')!.addEventListener('click', () => { finish(); location.reload(); });
  bar.querySelector('[data-a=reset]')!.addEventListener('click', () => { save({}); finish(); location.reload(); });
}
