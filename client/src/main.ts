import './ui/style.css';
import { GAME_TITLE, initPhysics } from '@lw/shared';

const app = document.getElementById('app')!;
app.innerHTML = `
  <div id="game"></div>
  <div id="hud"></div>
  <div id="menu" class="menu">
    <h1>${GAME_TITLE}</h1>
    <p class="sub">Milestone 1 · Movement sandbox</p>
    <button id="play" class="play" disabled>LOADING…</button>
    <p class="note">Offline test: you + 15 wandering bots.</p>
  </div>
  <div id="rotate" class="rotate"><div>⟳</div><p>Rotate your phone to landscape</p></div>
`;

const play = document.getElementById('play') as HTMLButtonElement;
const physicsReady = initPhysics().then(() => {
  play.disabled = false;
  play.textContent = 'PLAY';
});

play.addEventListener('click', async () => {
  // Fullscreen + landscape lock where supported (Android Chrome)
  try {
    await document.documentElement.requestFullscreen?.({ navigationUI: 'hide' });
    await (screen.orientation as ScreenOrientation & { lock?: (o: string) => Promise<void> }).lock?.('landscape');
  } catch { /* not supported — fine */ }
  await physicsReady;
  document.getElementById('menu')!.remove();
  const { OfflineGame } = await import('./game/OfflineGame');
  new OfflineGame(document.getElementById('game')!, document.getElementById('hud')!);
});
