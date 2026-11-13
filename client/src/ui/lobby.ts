import * as THREE from 'three';
import { GAME_TITLE, MAP_LIST, WEAPON_IDS, isMapId, type MapId } from '@lw/shared';
import { loadSettings, settingsPanel } from './settings';
import { editLayoutStandalone } from './layout';
import { createHudButtons } from './buttons';
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
  scene.background = new THREE.Color(0xbcd9ea);
  scene.fog = new THREE.Fog(0xbcd9ea, 18, 60);
  const tl = new THREE.TextureLoader();
  const photo = tl.load('/assets/maps/green.webp'); photo.colorSpace = THREE.SRGBColorSpace;
  photo.repeat.set(0.08, 0.08); photo.offset.set(0.42, 0.5); // a grassy patch of the Highlands photo
  const grain = tl.load('/assets/maps/detail_grass.webp'); grain.wrapS = grain.wrapT = THREE.RepeatWrapping; grain.repeat.set(30, 30);
  const ground = new THREE.Mesh(new THREE.CircleGeometry(70, 48).rotateX(-Math.PI / 2), new THREE.MeshLambertMaterial({ map: photo, color: 0xcfe0b0 }));
  scene.add(ground);
  const detail = new THREE.Mesh(new THREE.CircleGeometry(70, 48).rotateX(-Math.PI / 2), new THREE.MeshBasicMaterial({ map: grain, transparent: true, opacity: 0.25, blending: THREE.MultiplyBlending, depthWrite: false }));
  detail.position.y = 0.01; scene.add(detail);
  const place = (name: string, x: number, z: number, s: number, rot = 0) => {
    const pf = (assets.prefabs as Record<string, { parts: { geometry: THREE.BufferGeometry; material: THREE.Material }[] } | undefined>)[name];
    if (!pf) return;
    for (const part of pf.parts) { const m = new THREE.Mesh(part.geometry, part.material); m.position.set(x, 0, z); m.rotation.y = rot; m.scale.setScalar(s); scene.add(m); }
  };
  // trees and cover behind and around the soldier (camera looks toward -z)
  ([['tpine1', -5, -9, 1], ['toak1', 6, -14, 0.9], ['tpine3', 2.5, -18, 1.1], ['tpine2', -11, -16, 1], ['tpine4', 10, -7, 0.9],
    ['toak2', -16, -26, 1], ['tpine1', 15, -24, 1.1], ['tpine2', -2, -30, 1.2], ['tbush1', -2.8, -3.5, 0.9], ['tbush2', 3.2, -4.2, 1],
    ['tbush3', -6.5, -5, 1.1], ['rock2', 4.5, -2.5, 0.6], ['rock4', -4, -1.8, 0.45], ['tpine3', -22, -12, 1], ['toak1', 22, -16, 1]] as [string, number, number, number][])
    .forEach(([n, x, z, sc], i) => place(n, x, z, sc, i * 1.7));
  // distant green hills
  const hills = new THREE.Mesh(new THREE.CylinderGeometry(60, 60, 14, 40, 1, true), new THREE.MeshBasicMaterial({ color: 0x5f8a58, side: THREE.BackSide, fog: true }));
  hills.position.y = 4; scene.add(hills);
}

export function showLobby(root: HTMLElement, assets: GameAssets, serverOk: Promise<boolean>, inviteRoom: string | null, onAction: (a: LobbyAction, p: Profile) => void) {
  const profile = loadProfile();
  const el = document.createElement('div');
  el.className = 'lobby';
  el.innerHTML = `
    <canvas class="lobby-3d"></canvas>
    <div class="lobby-title"><img class="lobby-logo" src="/logo.png" alt="${GAME_TITLE}"><span class="srv">checking server…</span></div>
    <div class="lobby-settings"></div>
    <div class="lobby-panel">
      <label class="lbl">NAME</label>
      <input class="name" maxlength="14" placeholder="Soldier" value="${profile.name.replace(/"/g, '')}">
      <label class="lbl">OUTFIT</label>
      <div class="skins">${SKINS.map((s, i) => s.outfit
        ? `<button class="outfit" data-skin="${i}">${s.name}</button>`
        : `<button data-skin="${i}" title="${s.name}" style="--c:#${(s.color ?? 0xffffff).toString(16).padStart(6, '0')}"></button>`).join('')}</div>
      <label class="lbl">MAP</label>
      <div class="maps">${MAP_LIST.map((m) => `<button data-map="${m.id}"><b>${m.name}</b><small>${m.desc}</small></button>`).join('')}</div>
      <p class="hint">Everyone lands empty-handed — loot weapons, ammo, medkits, armor, scopes and ice walls.</p>
      ${inviteRoom ? `<button class="big join-invite">JOIN FRIEND'S ROOM</button>` : ''}
      <button class="big quick">PLAY ONLINE</button>
      <div class="row">
        <button class="mid squad">FRIENDS ROOM</button>
        <button class="mid bots">VS BOTS</button>
      </div>
      <div class="row joinrow"><input class="code" placeholder="room code"><button class="mid joinbtn">JOIN</button></div>
      <p class="note"></p>
      <a class="apk-link" href="/download/last-war.apk" download="last-war.apk">⬇ DOWNLOAD ANDROID APP (APK)</a>
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
  };
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
    view!.update({ x: 0, y: 0.9, z: 0, yaw, pitch: 0, speed: 0, crouch: false, grounded: true }, dt, 3, true);
    resize();
    renderer.render(scene, camera);
  };
  requestAnimationFrame(loop);

  // ---- settings (same panel as in game; saved on the device)
  const st = loadSettings();
  settingsPanel(el.querySelector('.lobby-settings') as HTMLElement, st, () => {}, () => editLayoutStandalone((r) => { createHudButtons(r); }));

  // ---- form
  const nameIn = el.querySelector('.name') as HTMLInputElement;
  const sel = () => {
    el.querySelectorAll<HTMLButtonElement>('[data-skin]').forEach((b) => b.classList.toggle('sel', +b.dataset.skin! === profile.skin));
    el.querySelectorAll<HTMLButtonElement>('[data-slot]').forEach((b) => b.classList.toggle('sel', +b.dataset.slot! === profile.slot));
    el.querySelectorAll<HTMLButtonElement>('[data-map]').forEach((b) => b.classList.toggle('sel', b.dataset.map === profile.map));
  };
  sel();
  el.querySelectorAll<HTMLButtonElement>('[data-map]').forEach((b) => b.addEventListener('click', () => { profile.map = b.dataset.map as MapId; saveProfile(profile); sel(); }));
  el.querySelectorAll<HTMLButtonElement>('[data-skin]').forEach((b) => b.addEventListener('click', () => { profile.skin = +b.dataset.skin!; saveProfile(profile); sel(); rebuild(); }));
  el.querySelectorAll<HTMLButtonElement>('[data-slot]').forEach((b) => b.addEventListener('click', () => { profile.slot = +b.dataset.slot!; saveProfile(profile); sel(); rebuild(); }));
  nameIn.addEventListener('input', () => { profile.name = nameIn.value.replace(/[^\w \-]/g, '').slice(0, 14); saveProfile(profile); });

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
