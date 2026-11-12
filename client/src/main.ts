import './ui/style.css';
import { GAME_TITLE, initPhysics } from '@lw/shared';
import { sfx } from './audio/sfx';

const app = document.getElementById('app')!;
app.innerHTML = `
  <div id="game"></div>
  <div id="hud"></div>
  <div id="menu" class="menu">
    <h1>${GAME_TITLE}</h1>
    <p class="sub">Milestone 2 · Weapons, combat &amp; zone</p>
    <button id="play" class="play" disabled>LOADING…</button>
    <div class="bar"><div id="bar"></div></div>
    <p class="note">Offline match: you vs 15 bots. Last one standing wins.</p>
  </div>
  <div id="rotate" class="rotate"><div>⟳</div><p>Rotate your phone to landscape</p></div>
`;

const play = document.getElementById('play') as HTMLButtonElement;
const bar = document.getElementById('bar') as HTMLDivElement;
const assetsReady = import('./render/assets').then((m) => m.loadAssets((f) => { bar.style.width = `${Math.round(f * 100)}%`; }));
const ready = Promise.all([initPhysics(), assetsReady]).then(([, assets]) => {
  play.disabled = false;
  play.textContent = 'PLAY';
  return assets;
}).catch((e) => {
  play.textContent = 'LOAD FAILED';
  console.error(e);
  throw e;
});

play.addEventListener('click', async () => {
  // Fullscreen + landscape lock where supported (Android Chrome)
  try {
    await document.documentElement.requestFullscreen?.({ navigationUI: 'hide' });
    await (screen.orientation as ScreenOrientation & { lock?: (o: string) => Promise<void> }).lock?.('landscape');
  } catch { /* not supported — fine */ }
  sfx.unlock();
  const assets = await ready;
  document.getElementById('menu')!.remove();
  const { Game } = await import('./game/Game');
  const start = () => new Game(document.getElementById('game')!, document.getElementById('hud')!, assets, start);
  start();
});
