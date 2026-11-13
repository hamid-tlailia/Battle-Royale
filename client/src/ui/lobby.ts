import * as THREE from 'three';
import { GAME_TITLE, MAP_LIST, WEAPON_IDS, isMapId, type MapId } from '@lw/shared';
import { loadSettings, settingsPanel } from './settings';
import { editLayoutStandalone } from './layout';
import { createHudButtons } from './buttons';
import { ICONS } from './icons';
import { levelOf, loadProgress } from '../meta/progress';
import type { GameAssets, GunName } from '../render/assets';
import { CharacterView, SKINS } from '../render/characters';

export interface Profile { name: string; skin: number; slot: number; map: MapId }
const KEY = 'lw.profile.v1';
export function loadProfile(): Profile {
  const def: Profile = { name: '', skin: 0, slot: 1, map: 'green' };
  try { const p = { ...def, ...JSON.parse(localStorage.getItem(KEY) || '{}') } as Profile; if (!isMapId(p.map)) p.map = 'green'; return p; } catch { return def; }
}
function saveProfile(p: Profile) { try { localStorage.setItem(KEY, JSON.stringify(p)); } catch { /* ignore */ } }

export type LobbyAction =
  | { kind: 'offline' }
  | { kind: 'quick' }
  | { kind: 'create' }
  | { kind: 'join'; room: string };

/**
 * Pre-match lobby: 3D preview of your soldier (outfit + weapon), name, loadout,
 * quick match / vs bots / friends room (invite link) / join by code.
 */
/**
 * A small real scene behind the soldier: aerial-photo ground with grass grain, real pines / oaks / bushes /
 * rocks from the game, hills and a sky gradient with haze.
 */
function buildLobbyBackdrop(scene: THREE.Scene, assets: GameAssets) {
  // a different scene each time you come back to the lobby: forest, farm, desert
  let idx = 0;
  try { idx = (+(localStorage.getItem('lw.lobbyScene') ?? '-1') + 1) % 3; localStorage.setItem('lw.lobbyScene', String(idx)); } catch { /* ignore */ }
  const kind = (['forest', 'farm', 'desert'] as const)[idx];
  const desert = kind === 'desert';
  const sky = desert ? 0xe9d3b0 : 0xbcd9ea;
  scene.background = new THREE.Color(sky);
  scene.fog = new THREE.Fog(sky, 18, desert ? 80 : 60);
  const tl = new THREE.TextureLoader();
  const photo = tl.load(desert ? '/assets/maps/desert.webp' : '/assets/maps/green.webp'); photo.colorSpace = THREE.SRGBColorSpace;
  photo.repeat.set(0.08, 0.08); photo.offset.set(desert ? 0.2 : 0.42, desert ? 0.62 : 0.5);
  const grain = tl.load(desert ? '/assets/maps/detail_sand.webp' : '/assets/maps/detail_grass.webp'); grain.wrapS = grain.wrapT = THREE.RepeatWrapping; grain.repeat.set(30, 30);
  const ground = new THREE.Mesh(new THREE.CircleGeometry(90, 48).rotateX(-Math.PI / 2), new THREE.MeshLambertMaterial({ map: photo, color: desert ? 0xffe2c8 : 0xcfe0b0 }));
  scene.add(ground);
  const detail = new THREE.Mesh(new THREE.CircleGeometry(90, 48).rotateX(-Math.PI / 2), new THREE.MeshBasicMaterial({ map: grain, transparent: true, opacity: 0.25, blending: THREE.MultiplyBlending, premultipliedAlpha: true, depthWrite: false }));
  detail.position.y = 0.01; scene.add(detail);
  const place = (name: string, x: number, z: number, s: number, rot = 0, tint?: number) => {
    const pf = (assets.prefabs as Record<string, { parts: { geometry: THREE.BufferGeometry; material: THREE.Material }[] } | undefined>)[name];
    if (!pf) return;
    for (const part of pf.parts) {
      let mat = part.material;
      if (tint !== undefined) { mat = (mat as THREE.MeshLambertMaterial).clone(); (mat as THREE.MeshLambertMaterial).color.multiply(new THREE.Color(tint)); }
      const m = new THREE.Mesh(part.geometry, mat); m.position.set(x, 0, z); m.rotation.y = rot; m.scale.setScalar(s); scene.add(m);
    }
  };
  type P = [string, number, number, number, number?, number?];
  const sets: Record<typeof kind, P[]> = {
    forest: [['tpine1', -5, -9, 1], ['toak1', 6, -14, 0.9], ['tpine3', 2.5, -18, 1.1], ['tpine2', -11, -16, 1], ['tpine4', 10, -7, 0.9],
      ['toak2', -16, -26, 1], ['tpine1', 15, -24, 1.1], ['tpine2', -2, -30, 1.2], ['tbush1', -2.8, -3.5, 0.9], ['tbush2', 3.2, -4.2, 1],
      ['tbush3', -6.5, -5, 1.1], ['rock2', 4.5, -2.5, 0.6], ['rock4', -4, -1.8, 0.45], ['tpine3', -22, -12, 1], ['toak1', 22, -16, 1]],
    farm: [['windmill', 7, -16, 1, 0.6], ['windmill_blades', 7, -16, 1, 0.6], ['farmhouse', -9, -13, 1, 0.4], ['yardwall', 0, -24, 1, 0],
      ['haybale', 2.8, -3.5, 1.4, 0.3], ['haybale', 3.9, -3.2, 1.4, 1.2], ['wagon', -4.2, -4.5, 1, 0.8], ['well', -1.5, -9, 1],
      ['toak1', -20, -22, 1], ['toak2', 18, -26, 1.1], ['tbush2', 5.5, -6, 1], ['tbush1', -6.5, -6.5, 1], ['scarecrow', 9, -8, 1, -0.4]],
    desert: [['house_e', -12, -20, 0.8, 0.5], ['house_d', 12, -22, 0.8, -0.6], ['rock1', 4, -4, 1.3, 0, 0xd58a62], ['rock3', -4.5, -3, 1.1, 1, 0xd58a62],
      ['rock2', -9, -9, 1.6, 2, 0xc77a55], ['rock4', 9, -10, 1.5, 0.5, 0xc77a55], ['toak2', 18, -12, 0.7], ['car2', 3, -11, 1, 0.9], ['rock1', -16, -14, 2.2, 0, 0xc77a55]],
  };
  sets[kind].forEach(([n, x, z, sc, r, t]) => place(n, x, z, sc, r ?? 0, t));
  // distant hills / mesas
  const hills = new THREE.Mesh(new THREE.CylinderGeometry(70, 70, desert ? 22 : 14, 40, 1, true), new THREE.MeshBasicMaterial({ color: desert ? 0xb06a48 : 0x5f8a58, side: THREE.BackSide, fog: true }));
  hills.position.y = desert ? 7 : 4; scene.add(hills);
}

export function showLobby(root: HTMLElement, assets: GameAssets, serverOk: Promise<boolean>, inviteRoom: string | null, onAction: (a: LobbyAction, p: Profile) => void) {
  const profile = loadProfile();
  const el = document.createElement('div');
  el.className = 'lobby';
  const tab = (id: string, icon: string, label: string) => `<button class="lb-tab" data-tab="${id}">${icon}<span>${label}</span></button>`;
  el.innerHTML = `
    <canvas class="lobby-3d"></canvas>
    <div class="lb-top">
      <div class="lb-banner">
        <canvas class="lb-avatar" width="96" height="96"></canvas>
        <div class="lb-who"><b class="lb-name"></b><div class="lb-lv"><span class="lb-level"></span><i><em></em></i></div><span class="srv">checking server…</span></div>
      </div>
      <img class="lobby-logo" src="/logo.png" alt="${GAME_TITLE}">
    </div>
    <button class="lb-gear" data-tab="settings" aria-label="Settings">${ICONS.settings}</button>
    <nav class="lb-nav">
      ${tab('outfit', ICONS.outfit, 'OUTFIT')}
      ${tab('map', ICONS.map, 'MAP')}
      ${tab('friends', ICONS.friends, 'FRIENDS')}
    </nav>
    <div class="lb-sheet">
      <section data-sec="outfit"><h3>OUTFIT</h3>
        <div class="skins">${SKINS.map((s, i) => s.outfit
          ? `<button class="outfit" data-skin="${i}">${s.name}</button>`
          : `<button data-skin="${i}" title="${s.name}" style="--c:#${(s.color ?? 0xffffff).toString(16).padStart(6, '0')}"></button>`).join('')}</div>
        <p class="hint">Drag the soldier to turn him around.</p></section>
      <section data-sec="map"><h3>MAP</h3>
        <div class="maps">${MAP_LIST.map((m) => `<button data-map="${m.id}"><b>${m.name}</b><small>${m.desc}</small></button>`).join('')}</div>
        <p class="hint">Everyone lands empty-handed — loot weapons, ammo, medkits, armor, scopes and ice walls.</p></section>
      <section data-sec="friends"><h3>PLAY WITH FRIENDS</h3>
        ${inviteRoom ? `<button class="big join-invite">JOIN FRIEND'S ROOM</button>` : ''}
        <button class="mid squad">CREATE FRIENDS ROOM</button>
        <div class="row joinrow"><input class="code" placeholder="room code"><button class="mid joinbtn">JOIN</button></div>
        <p class="note"></p>
        <a class="apk-link" href="/download/last-war.apk" download="last-war.apk">⬇ DOWNLOAD ANDROID APP (APK)</a></section>
      <section data-sec="settings"><h3>SETTINGS</h3>
        <label class="lbl">PLAYER NAME</label><input class="name" maxlength="14" placeholder="Your name" value="${profile.name.replace(/"/g, '')}">
        <div class="lobby-settings"></div></section>
    </div>
    <div class="lb-play">
      <div class="lb-mapname"></div>
      <button class="big bots">PLAY <small>vs bots</small></button>
      <button class="mid quick">PLAY ONLINE</button>
    </div>`;
  root.appendChild(el);
  // inside the installed app the download link makes no sense
  if ((window as unknown as { Capacitor?: { isNativePlatform?: () => boolean } }).Capacitor?.isNativePlatform?.()) el.querySelector('.apk-link')?.remove();

  // ---- 3D preview
  const canvas = el.querySelector('.lobby-3d') as HTMLCanvasElement;
  const renderer = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: true });
  renderer.setPixelRatio(Math.min(devicePixelRatio, 1.5));
  // if the phone drops the 3D context (too many open), let it come back instead of staying blank
  canvas.addEventListener('webglcontextlost', (e) => e.preventDefault());
  const scene = new THREE.Scene();
  scene.add(new THREE.HemisphereLight(0xe6f4ff, 0x3f5a30, 1.9));
  const sun = new THREE.DirectionalLight(0xfff0d0, 2.2); sun.position.set(3, 5, 4); scene.add(sun);
  buildLobbyBackdrop(scene, assets);
  const camera = new THREE.PerspectiveCamera(30, 1, 0.1, 50);
  camera.position.set(0, 1.3, 5.6); camera.lookAt(0, 0.95, 0);
  let view: CharacterView | null = null;
  const rebuild = () => {
    view?.dispose();
    view = new CharacterView(assets, profile.skin, WEAPON_IDS[profile.slot] as GunName);
    scene.add(view.root);
    portraitFrames = 90; // refresh the banner portrait once the (possibly loading) outfit is shown
  };
  let portraitFrames = 0;
  rebuild();
  // facing you; turns only when you drag it
  let yaw = Math.PI, last = performance.now(), alive = true, drag: number | null = null;
  canvas.addEventListener('pointerdown', (e) => { drag = e.clientX; });
  window.addEventListener('pointerup', () => { drag = null; });
  canvas.addEventListener('pointermove', (e) => { if (drag !== null) { yaw += (e.clientX - drag) * 0.01; drag = e.clientX; } });
  const resize = () => {
    const w = canvas.clientWidth, h = canvas.clientHeight;
    renderer.setSize(w, h, false); camera.aspect = w / Math.max(1, h); camera.updateProjectionMatrix();
  };
  const loop = (now: number) => {
    if (!alive) return;
    requestAnimationFrame(loop);
    const dt = Math.min(0.05, (now - last) / 1000); last = now;
    view!.update({ x: 0, y: 0.9, z: 0, yaw, pitch: -0.55, speed: 0, crouch: false, grounded: true }, dt, 3, true); // gun at low ready
    resize();
    renderer.render(scene, camera);
    if (portraitFrames > 0 && --portraitFrames % 30 === 0) drawPortrait();
  };
  const avatar = el.querySelector('.lb-avatar') as HTMLCanvasElement;
  const drawPortrait = () => { // head & shoulders from the 3D view, right after it was rendered
    const head = new THREE.Vector3(0, 1.62, 0).project(camera);
    const W = canvas.width, H = canvas.height, sx = (head.x * 0.5 + 0.5) * W, sy = (-head.y * 0.5 + 0.5) * H, r = H * 0.09;
    const g = avatar.getContext('2d')!;
    g.fillStyle = '#20301c'; g.fillRect(0, 0, 96, 96);
    try { g.drawImage(canvas, sx - r, sy - r * 0.8, r * 2, r * 2, 0, 0, 96, 96); } catch { /* ignore */ }
  };
  requestAnimationFrame(loop);

  // ---- settings (same panel as in game; saved on the device)
  const st = loadSettings();
  const setHost = el.querySelector('.lobby-settings') as HTMLElement;
  settingsPanel(setHost, st, () => {}, () => editLayoutStandalone((r) => { createHudButtons(r); }));
  (setHost.querySelector('.gear-btn') as HTMLElement).style.display = 'none'; // the SETTINGS tab opens it
  const setPanel = setHost.querySelector('.settings-panel') as HTMLElement;
  // ---- sections: one panel at a time, nothing to scroll past
  const sheet = el.querySelector('.lb-sheet') as HTMLElement;
  let open: string | null = null;
  const show = (id: string | null) => {
    open = open === id ? null : id;
    sheet.classList.toggle('open', open !== null);
    el.querySelectorAll<HTMLElement>('[data-sec]').forEach((x) => x.classList.toggle('on', x.dataset.sec === open));
    el.querySelectorAll<HTMLElement>('[data-tab]').forEach((x) => x.classList.toggle('on', x.dataset.tab === open));
    if (open === 'settings') setPanel.style.display = 'block';
  };
  el.querySelectorAll<HTMLElement>('[data-tab]').forEach((b) => b.addEventListener('click', () => show(b.dataset.tab!)));
  setPanel.querySelector('.close')?.addEventListener('click', () => { if (open === 'settings') show('settings'); });
  if (inviteRoom) show('friends');

  // ---- form
  const nameIn = el.querySelector('.name') as HTMLInputElement;
  const sel = () => {
    el.querySelectorAll<HTMLButtonElement>('[data-skin]').forEach((b) => b.classList.toggle('sel', +b.dataset.skin! === profile.skin));
    el.querySelectorAll<HTMLButtonElement>('[data-slot]').forEach((b) => b.classList.toggle('sel', +b.dataset.slot! === profile.slot));
    el.querySelectorAll<HTMLButtonElement>('[data-map]').forEach((b) => b.classList.toggle('sel', b.dataset.map === profile.map));
    (el.querySelector('.lb-mapname') as HTMLElement).textContent = `MAP · ${MAP_LIST.find((m) => m.id === profile.map)?.name ?? ''}`;
  };
  sel();
  el.querySelectorAll<HTMLButtonElement>('[data-map]').forEach((b) => b.addEventListener('click', () => { profile.map = b.dataset.map as MapId; saveProfile(profile); sel(); }));
  el.querySelectorAll<HTMLButtonElement>('[data-skin]').forEach((b) => b.addEventListener('click', () => { profile.skin = +b.dataset.skin!; saveProfile(profile); sel(); rebuild(); }));
  el.querySelectorAll<HTMLButtonElement>('[data-slot]').forEach((b) => b.addEventListener('click', () => { profile.slot = +b.dataset.slot!; saveProfile(profile); sel(); rebuild(); }));
  const nameEl = el.querySelector('.lb-name') as HTMLElement;
  const showName = () => { nameEl.textContent = profile.name || 'Soldier'; };
  showName();
  nameIn.addEventListener('input', () => { profile.name = nameIn.value.replace(/[^\w \-]/g, '').slice(0, 14); saveProfile(profile); showName(); });
  const prog = levelOf(loadProgress().xp);
  (el.querySelector('.lb-level') as HTMLElement).textContent = `LV ${prog.level}`;
  (el.querySelector('.lb-lv em') as HTMLElement).style.width = `${Math.round(prog.frac * 100)}%`;

  const note = el.querySelector('.note') as HTMLElement;
  const online = [el.querySelector('.quick'), el.querySelector('.squad'), el.querySelector('.joinbtn'), el.querySelector('.join-invite')].filter(Boolean) as HTMLButtonElement[];
  online.forEach((b) => { b.disabled = true; });
  void serverOk.then((ok) => {
    const s = el.querySelector('.srv') as HTMLElement;
    s.textContent = ok ? '● server online' : '● server offline — play vs bots';
    s.classList.toggle('ok', ok);
    online.forEach((b) => { b.disabled = !ok; });
  });
  const go = (a: LobbyAction) => {
    if (!profile.name) profile.name = `Soldier${Math.floor(Math.random() * 900 + 100)}`;
    saveProfile(profile);
    alive = false;
    renderer.dispose(); renderer.forceContextLoss();
    el.remove();
    onAction(a, profile);
  };
  el.querySelector('.bots')!.addEventListener('click', () => go({ kind: 'offline' }));
  el.querySelector('.quick')!.addEventListener('click', () => go({ kind: 'quick' }));
  el.querySelector('.squad')!.addEventListener('click', () => go({ kind: 'create' }));
  el.querySelector('.join-invite')?.addEventListener('click', () => go({ kind: 'join', room: inviteRoom! }));
  el.querySelector('.joinbtn')!.addEventListener('click', () => {
    const code = (el.querySelector('.code') as HTMLInputElement).value.trim();
    if (code) go({ kind: 'join', room: code }); else note.textContent = 'Enter the room code your friend shared';
  });
}
