/**
 * Renders the web game's HUD look into PNGs for the Unity version (unity/Assets/LastWar/Resources/UI):
 * round metal buttons (normal / fire / active), every button icon (white), gun silhouettes for the weapon box,
 * and the blood splash of the kill banner — so both versions look the same.
 * Run: npx tsx scripts/unity/export-ui.ts   (needs Playwright's Chromium)
 */
import fs from 'node:fs';
import http from 'node:http';
import path from 'node:path';
import { createRequire } from 'node:module';
import { ICONS } from '../../client/src/ui/icons';

// Playwright is not a project dependency: use the machine's install (NODE_PATH)
const { chromium } = createRequire(import.meta.url)('playwright') as typeof import('playwright');

const OUT = 'unity/Assets/LastWar/Resources/UI';
fs.mkdirSync(OUT, { recursive: true });
const css = fs.readFileSync('client/src/ui/style.css', 'utf8');
const blood = css.match(/\.kb-blood \{[^}]*?background: (url\("data:image\/svg\+xml,[^"]*"\))/)![1];

const BTN = `
.b { width: 100px; height: 100px; border-radius: 50%; position: relative; margin: 14px;
  background: radial-gradient(circle at 50% 35%, rgba(70,80,86,.62), rgba(14,17,20,.72) 70%);
  box-shadow: 0 0 0 2px rgba(0,0,0,.45), inset 0 0 0 2px rgba(205,215,220,.38), inset 0 2px 6px rgba(255,255,255,.15), inset 0 -6px 12px rgba(0,0,0,.45), 0 3px 8px rgba(0,0,0,.35); }
.b.fire { background: radial-gradient(circle at 50% 35%, rgba(230,95,70,.75), rgba(110,25,15,.82) 72%);
  box-shadow: 0 0 0 2px rgba(0,0,0,.45), inset 0 0 0 2px rgba(255,190,170,.6), inset 0 -8px 14px rgba(0,0,0,.35), 0 0 14px rgba(255,80,50,.3); }
.b.active { background: radial-gradient(circle at 50% 35%, rgba(150,220,100,.75), rgba(40,80,20,.8) 70%);
  box-shadow: 0 0 0 2px rgba(0,0,0,.45), inset 0 0 0 2px #c8f59a, 0 0 12px rgba(150,230,90,.45); }
.b.pressed { box-shadow: 0 0 0 2px rgba(0,0,0,.45), inset 0 0 0 2px #ffd34d, inset 0 0 16px rgba(255,211,77,.35); }
.w { display: inline-block; padding: 14px; }
.ic { width: 100px; height: 100px; color: #fff; display: inline-block; filter: drop-shadow(0 1px 1px rgba(0,0,0,.7)); }
.ic svg { width: 100%; height: 100%; }
.blood { width: 96px; height: 84px; background: ${blood} center / contain no-repeat; display: inline-block; }
body { margin: 0; background: transparent; }
canvas { display: block; }`;

const iconIds = ['fire', 'jump', 'crouch', 'prone', 'scope', 'reload', 'sprint', 'heal', 'frost', 'bag', 'settings', 'skull', 'vest', 'fist', 'map', 'holster'] as const;
const html = `<!doctype html><html><head><style>${BTN}</style>
<script type="importmap">{"imports":{"three":"/node_modules/three/build/three.module.js","three/addons/":"/node_modules/three/examples/jsm/"}}</script></head><body>
${['', 'fire', 'active', 'pressed'].map((c) => `<div class="w" id="btn${c ? '_' + c : ''}"><div class="b ${c}"></div></div>`).join('')}
${iconIds.map((id) => `<div class="ic" id="icon_${id}">${(ICONS as Record<string, string>)[id]}</div>`).join('')}
<div class="blood" id="blood"></div>
<div class="ic" id="map_arrow"><svg viewBox="-22 -30 44 52"><path d="M0-26L17 17L0 8L-17 17Z" fill="#ffd34d" stroke="#000" stroke-width="3" stroke-linejoin="round"/></svg></div>
<div class="ic" id="map_cone"><svg viewBox="-100 -100 200 200"><defs><radialGradient id="cg" cx="0" cy="0" r="100" gradientUnits="userSpaceOnUse"><stop offset=".06" stop-color="#fff" stop-opacity=".6"/><stop offset="1" stop-color="#fff" stop-opacity="0"/></radialGradient></defs><path d="M0 0L${(100 * Math.sin(-0.55)).toFixed(2)} ${(-100 * Math.cos(0.55)).toFixed(2)}A100 100 0 0 1 ${(100 * Math.sin(0.55)).toFixed(2)} ${(-100 * Math.cos(0.55)).toFixed(2)}Z" fill="url(#cg)"/></svg></div>
<div id="guns"></div>
<script type="module">
import * as THREE from 'three';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
const W = 384, H = 144;
const r = new THREE.WebGLRenderer({ alpha: true, antialias: true, preserveDrawingBuffer: true });
r.setSize(W, H, false);
const scene = new THREE.Scene();
scene.add(new THREE.HemisphereLight(0xffffff, 0x666666, 2.2));
const out = {};
for (const g of ['pistol', 'rifle', 'shotgun', 'sniper', 'smg']) {
  const gltf = await new GLTFLoader().loadAsync('/unity/Assets/LastWar/Resources/Models/gun_' + g + '.glb');
  const m = gltf.scene; m.rotation.y = Math.PI / 2; scene.add(m); m.updateMatrixWorld(true);
  // white silhouette, like the web slots (brightness(0) invert(1))
  m.traverse((o) => { if (o.isMesh) o.material = new THREE.MeshBasicMaterial({ color: 0xffffff }); });
  const box = new THREE.Box3().setFromObject(m);
  const c = box.getCenter(new THREE.Vector3()), sz = box.getSize(new THREE.Vector3());
  const half = Math.max(sz.x / 2 / (W / H), sz.y / 2) * 1.08;
  const cam = new THREE.OrthographicCamera(-half * (W / H), half * (W / H), half, -half, -10, 10);
  cam.position.set(c.x, c.y, c.z + 2); cam.lookAt(c);
  r.render(scene, cam);
  out[g] = r.domElement.toDataURL('image/png');
  scene.remove(m);
}
window.GUNS = out;
</script></body></html>`;

const server = http.createServer((req, res) => {
  const url = decodeURIComponent((req.url ?? '/').split('?')[0]);
  if (url === '/') { res.setHeader('content-type', 'text/html'); res.end(html); return; }
  const f = path.join(process.cwd(), url);
  if (!fs.existsSync(f)) { res.statusCode = 404; res.end(); return; }
  res.setHeader('content-type', f.endsWith('.js') ? 'text/javascript' : 'application/octet-stream');
  res.end(fs.readFileSync(f));
}).listen(8701);

(async () => {
  const browser = await chromium.launch({ args: ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader'] });
  const page = await (await browser.newContext({ deviceScaleFactor: 2.56, viewport: { width: 1600, height: 900 } })).newPage();
  page.on('console', (m) => console.log('page:', m.text()));
  await page.goto('http://127.0.0.1:8701/');
  await page.waitForFunction(() => (window as unknown as { GUNS?: object }).GUNS, null, { timeout: 60000 });
  for (const id of ['btn', 'btn_fire', 'btn_active', 'btn_pressed', ...iconIds.map((i) => 'icon_' + i), 'blood', 'map_arrow', 'map_cone'])
    await page.locator('#' + id).screenshot({ path: `${OUT}/${id}.png`, omitBackground: true });
  const guns = await page.evaluate(() => (window as unknown as { GUNS: Record<string, string> }).GUNS);
  for (const [g, url] of Object.entries(guns)) fs.writeFileSync(`${OUT}/gun_${g}.png`, Buffer.from(url.split(',')[1], 'base64'));
  await browser.close(); server.close();
  console.log('ui written:', fs.readdirSync(OUT).join(' '));
})();
