import { ICONS } from './icons';
/** Small settings panel (gear button): look sensitivity, scope sensitivity, aim assist, sound. Saved per device. */
export interface Settings { sens: number; scopeSens: number; aimAssist: boolean; sound: boolean; autoPickup: boolean }
const KEY = 'lw.settings.v1';
const DEFAULTS: Settings = { sens: 1, scopeSens: 1, aimAssist: true, sound: true, autoPickup: true };

export function loadSettings(): Settings {
  try { return { ...DEFAULTS, ...JSON.parse(localStorage.getItem(KEY) || '{}') }; } catch { return { ...DEFAULTS }; }
}
function save(s: Settings) { try { localStorage.setItem(KEY, JSON.stringify(s)); } catch { /* private mode */ } }

export function settingsPanel(parent: HTMLElement, s: Settings, onChange: (s: Settings) => void, onEditLayout?: () => void) {
  const btn = document.createElement('button');
  btn.className = 'gear-btn';
  btn.innerHTML = ICONS.settings;
  const panel = document.createElement('div');
  panel.className = 'settings-panel';
  panel.style.display = 'none';
  const slider = (label: string, key: 'sens' | 'scopeSens') => `
    <label>${label} <b data-v="${key}">${s[key].toFixed(2)}</b>
      <input type="range" min="0.3" max="3" step="0.05" value="${s[key]}" data-k="${key}"></label>`;
  panel.innerHTML = `
    <h3>Settings</h3>
    ${slider('Look sensitivity', 'sens')}
    ${slider('Scope sensitivity', 'scopeSens')}
    <label class="row"><input type="checkbox" data-c="aimAssist" ${s.aimAssist ? 'checked' : ''}> Aim assist</label>
    <label class="row"><input type="checkbox" data-c="sound" ${s.sound ? 'checked' : ''}> Sound</label>
    <label class="row"><input type="checkbox" data-c="autoPickup" ${s.autoPickup ? 'checked' : ''}> Auto pick-up (ammo, meds, armor…)</label>
    <button class="layout">CUSTOMIZE BUTTON LAYOUT</button>
    <button class="close">CLOSE</button>`;
  const toggle = (e: Event) => { e.preventDefault(); e.stopPropagation(); panel.style.display = panel.style.display === 'none' ? 'block' : 'none'; };
  btn.addEventListener('touchstart', toggle, { passive: false });
  btn.addEventListener('click', toggle);
  panel.querySelector('.close')!.addEventListener('click', toggle);
  panel.querySelector('.close')!.addEventListener('touchend', toggle);
  panel.querySelector('.layout')!.addEventListener('click', (e) => { toggle(e); onEditLayout?.(); });
  panel.addEventListener('touchstart', (e) => e.stopPropagation(), { passive: true });
  panel.querySelectorAll<HTMLInputElement>('input[type=range]').forEach((inp) => inp.addEventListener('input', () => {
    const k = inp.dataset.k as 'sens' | 'scopeSens';
    s[k] = +inp.value;
    (panel.querySelector(`[data-v=${k}]`) as HTMLElement).textContent = s[k].toFixed(2);
    save(s); onChange(s);
  }));
  panel.querySelectorAll<HTMLInputElement>('input[type=checkbox]').forEach((inp) => inp.addEventListener('change', () => {
    const k = inp.dataset.c as 'aimAssist' | 'sound';
    s[k] = inp.checked;
    save(s); onChange(s);
  }));
  parent.append(btn, panel);
}
