import * as THREE from 'three';
import { GAME_TITLE, WEAPON_IDS } from '@lw/shared';
import { loadSettings, settingsPanel } from './settings';
import { editLayoutStandalone } from './layout';
import { createHudButtons } from './buttons';
import type { GameAssets, GunName } from '../render/assets';
import { CharacterView, SKINS } from '../render/characters';

export interface Profile { name: string; skin: number; slot: number }
const KEY = 'lw.profile.v1';
export function loadProfile(): Profile {
  try { return { name: '', skin: 0, slot: 1, ...JSON.parse(localStorage.getItem(KEY) || '{}') }; } catch { return { name: '', skin: 0, slot: 1 }; }
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
export function showLobby(root: HTMLElement, assets: GameAssets, serverOk: Promise<boolean>, inviteRoom: string | null, onAction: (a: LobbyAction, p: Profile) => void) {
  const profile = loadProfile();
  const el = document.createElement('div');
  el.className = 'lobby';
  el.innerHTML = `
    <canvas class="lobby-3d"></canvas>
    <div class="lobby-title"><h1>${GAME_TITLE}</h1><span class="srv">checking server…</span></div>
    <div class="lobby-settings"></div>
    <div class="lobby-panel">
      <label class="lbl">NAME</label>
      <input class="name" maxlength="14" placeholder="Soldier" value="${profile.name.replace(/"/g, '')}">
      <label class="lbl">OUTFIT</label>
      <div class="skins">${SKINS.map((s, i) => `<button data-skin="${i}" title="${s.name}" style="--c:#${s.color.toString(16).padStart(6, '0')}"></button>`).join('')}</div>
      <p class="hint">Everyone lands empty-handed — loot weapons, ammo, medkits, armor, scopes and ice walls.</p>
      ${inviteRoom ? `<button class="big join-invite">JOIN FRIEND'S ROOM</button>` : ''}
      <button class="big quick">PLAY ONLINE</button>
      <div class="row">
        <button class="mid squad">FRIENDS ROOM</button>
        <button class="mid bots">VS BOTS</button>
      </div>
      <div class="row joinrow"><input class="code" placeholder="room code"><button class="mid joinbtn">JOIN</button></div>
      <p class="note"></p>
    </div>`;
  root.appendChild(el);

  // ---- 3D preview
  const canvas = el.querySelector('.lobby-3d') as HTMLCanvasElement;
  const renderer = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: true });
  renderer.setPixelRatio(Math.min(devicePixelRatio, 1.5));
  const scene = new THREE.Scene();
  scene.add(new THREE.HemisphereLight(0xe6f4ff, 0x3f5a30, 1.9));
  const sun = new THREE.DirectionalLight(0xfff0d0, 2.2); sun.position.set(3, 5, 4); scene.add(sun);
  const pad = new THREE.Mesh(new THREE.CylinderGeometry(1.1, 1.25, 0.18, 40), new THREE.MeshLambertMaterial({ color: 0x36502c }));
  pad.position.y = -0.09; scene.add(pad);
  const ring = new THREE.Mesh(new THREE.TorusGeometry(1.15, 0.025, 8, 60).rotateX(Math.PI / 2), new THREE.MeshBasicMaterial({ color: 0xb8f27c }));
  scene.add(ring);
  const camera = new THREE.PerspectiveCamera(30, 1, 0.1, 50);
  camera.position.set(0, 1.3, 5.6); camera.lookAt(0, 0.95, 0);
  let view: CharacterView | null = null;
  const rebuild = () => {
    view?.dispose();
    view = new CharacterView(assets, profile.skin, WEAPON_IDS[profile.slot] as GunName);
    scene.add(view.root);
  };
  rebuild();
  let yaw = 0.5, last = performance.now(), alive = true, drag: number | null = null;
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
    if (drag === null) yaw += dt * 0.35;
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
  };
  sel();
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
    renderer.dispose();
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
