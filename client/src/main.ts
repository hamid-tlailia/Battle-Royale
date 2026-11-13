import './ui/style.css';
import { GAME_TITLE, MAP_LIST, initPhysics } from '@lw/shared';
import { sfx } from './audio/sfx';
import type { GameAssets } from './render/assets';

/** Game server (Colyseus). Set at build time with VITE_SERVER_URL; ?server= overrides for testing. */
const params = new URLSearchParams(location.search);
const SERVER_URL = params.get('server') || (import.meta.env.VITE_SERVER_URL as string | undefined) || '';
/** Network conditioner for testing on a real phone: ?lag=100&loss=0.05 (one-way ms, loss ratio). */
const COND = { latencyMs: +(params.get('lag') ?? 0), jitterMs: +(params.get('jitter') ?? 0), loss: +(params.get('loss') ?? 0) };

const app = document.getElementById('app')!;
app.innerHTML = `
  <div id="game"></div>
  <div id="hud"></div>
  <div id="boot" class="menu">
    <img class="boot-logo" src="/logo.png" alt="${GAME_TITLE}">
    <p class="sub">Loading…</p>
    <div class="bar"><div id="bar"></div></div>
  </div>
  <div id="rotate" class="rotate"><div>⟳</div><p>Rotate your phone to landscape</p></div>
`;

// static logo from index.html (inlined, shown before anything loads): reuse it so the boot screen never flashes blank
const inlineLogo = (document.querySelector('#preboot img') as HTMLImageElement | null)?.src;
if (inlineLogo) (document.querySelector('#boot .boot-logo') as HTMLImageElement).src = inlineLogo;
document.getElementById('preboot')?.remove();
// installed app: the native logo splash stays up until the page has drawn its own logo (no blank green screen)
requestAnimationFrame(() => (window as unknown as { Capacitor?: { Plugins?: { SplashScreen?: { hide: (o?: object) => void } } } }).Capacitor?.Plugins?.SplashScreen?.hide({ fadeOutDuration: 250 }));
const bar = document.getElementById('bar') as HTMLDivElement;
/** If loading fails or hangs, say why on screen (instead of an endless loading bar). */
const bootMsg = (t: string) => { const sub = document.querySelector('#boot .sub'); if (sub) { sub.textContent = t; (sub as HTMLElement).style.color = '#ffb0a0'; } };
window.addEventListener('error', (e) => bootMsg(`Error: ${e.message}`));
window.addEventListener('unhandledrejection', (e) => bootMsg(`Error: ${(e.reason as Error)?.message ?? e.reason}`));
setTimeout(() => { if (document.getElementById('boot')) bootMsg(`Still loading (${bar.style.width || '0%'})… check your connection`); }, 45000);
const assetsP = import('./render/assets').then((m) => m.loadAssets((f) => { bar.style.width = `${Math.round(f * 100)}%`; }));

async function checkServer(): Promise<boolean> {
  if (!SERVER_URL) return false;
  try {
    const ctl = new AbortController();
    setTimeout(() => ctl.abort(), 70000); // free hosts can take ~1 min to wake up
    const r = await fetch(`${SERVER_URL.replace(/\/$/, '')}/health`, { signal: ctl.signal });
    return r.ok;
  } catch { return false; }
}

async function enterFullscreen() {
  try {
    await document.documentElement.requestFullscreen?.({ navigationUI: 'hide' });
    await (screen.orientation as ScreenOrientation & { lock?: (o: string) => Promise<void> }).lock?.('landscape');
  } catch { /* not supported */ }
}

async function boot() {
  const [, assets] = await Promise.all([initPhysics(), assetsP]);
  document.getElementById('boot')!.remove();
  showMenu(assets);
}

function showMenu(assets: GameAssets) {
  const invite = params.get('room');
  void import('./ui/lobby').then(({ showLobby }) => showLobby(app, assets, checkServer(), invite, async (action, profile) => {
    void enterFullscreen();
    sfx.unlock();
    const { Game } = await import('./game/Game');
    const back = () => showMenu(assets);
    const gameEl = document.getElementById('game')!, hudEl = document.getElementById('hud')!;
    if (action.kind === 'offline') {
      // loading screen (logo + map + bar), the match is built behind it, then it fades into the plane
      const load = document.createElement('div');
      load.className = 'menu match-load';
      load.innerHTML = `<img class="boot-logo" src="/logo.png" alt=""><p class="sub">Loading ${MAP_LIST.find((m) => m.id === profile.map)?.name ?? ''}…</p><div class="bar"><div class="ml-bar"></div></div>`;
      app.appendChild(load);
      const barEl = load.querySelector('.ml-bar') as HTMLElement;
      const t0 = performance.now();
      const tick = () => { if (!load.isConnected) return; barEl.style.width = `${Math.min(95, (performance.now() - t0) / 18)}%`; requestAnimationFrame(tick); };
      tick();
      await new Promise((r) => requestAnimationFrame(() => requestAnimationFrame(r))); // let it paint first
      const game = new Game(gameEl, hudEl, assets, back, { name: profile.name, skin: profile.skin, slot: profile.slot, map: profile.map });
      game.warmUp();
      await new Promise((r) => setTimeout(r, Math.max(300, 1400 - (performance.now() - t0))));
      barEl.style.width = '100%';
      load.classList.add('out');
      setTimeout(() => load.remove(), 700);
      return;
    }
    const status = document.createElement('div');
    status.className = 'menu';
    status.innerHTML = `<h2>Connecting…</h2><p class="note">The free server may need up to a minute to wake up.</p>`;
    app.appendChild(status);
    try {
      const { NetSession } = await import('./net/session');
      const net = new NetSession(SERVER_URL, COND);
      const ready = new Promise<void>((res) => { net.onReady = res; });
      const opts = { skin: profile.skin, slot: profile.slot, map: profile.map };
      if (action.kind === 'join') await net.join(profile.name, opts, action.room);
      else await net.join(profile.name, opts, undefined, action.kind === 'create');
      await ready;
      status.remove();
      new Game(gameEl, hudEl, assets, () => { history.replaceState(null, '', location.pathname); back(); }, { net, name: profile.name, skin: profile.skin, slot: profile.slot, map: profile.map });
    } catch (e) {
      status.innerHTML = `<h2>Could not join</h2><p class="note">${String((e as Error)?.message ?? e).slice(0, 120)}</p><button class="play">BACK</button>`;
      status.querySelector('button')!.addEventListener('click', () => { status.remove(); back(); });
    }
  }));
}

void boot();
