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
/** level n needs 100 * n * (n - 1) / 2 XP in total: 0, 100, 300, 600, ... */
export function levelOf(xp: number) {
  let lv = 1; while (xp >= (100 * (lv + 1) * lv) / 2) lv++;
  const base = (100 * lv * (lv - 1)) / 2, next = (100 * (lv + 1) * lv) / 2;
  return { level: lv, frac: (xp - base) / (next - base) };
}
