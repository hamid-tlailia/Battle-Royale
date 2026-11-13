/** Player level from match XP, saved on the device. */
const KEY = 'lw.progress.v1';
export interface Progress { xp: number; matches: number; wins: number; kills: number }
export function loadProgress(): Progress {
  try { return { xp: 0, matches: 0, wins: 0, kills: 0, ...JSON.parse(localStorage.getItem(KEY) || '{}') }; } catch { return { xp: 0, matches: 0, wins: 0, kills: 0 }; }
}
/** XP for one match: kills, how long you lasted (placement) and a win bonus. */
export function addMatch(kills: number, place: number, total: number, win: boolean): Progress {
  const p = loadProgress();
  const survive = Math.max(0, Math.round((1 - (place - 1) / Math.max(1, total - 1)) * 120));
  p.xp += 40 + kills * 60 + survive + (win ? 300 : 0);
  p.matches++; p.kills += kills; if (win) p.wins++;
  try { localStorage.setItem(KEY, JSON.stringify(p)); } catch { /* ignore */ }
  return p;
}
/** XP from one level to the next: 60, 80, 100 ... capped at 200 (a level every 1-3 matches) */
const stepOf = (lv: number) => Math.min(200, 60 + 20 * (lv - 1));
export function levelOf(xp: number) {
  let lv = 1, base = 0;
  while (xp >= base + stepOf(lv)) { base += stepOf(lv); lv++; }
  const need = stepOf(lv);
  return { level: lv, frac: (xp - base) / need, cur: xp - base, need };
}
