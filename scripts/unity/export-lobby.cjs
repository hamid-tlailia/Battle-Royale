/**
 * Captures the web lobby for the Unity version: every panel / button background as a PNG (text and icons
 * hidden), plus lobby_layout.json with the exact position, size and text style of every piece at a phone
 * landscape size (Pixel 7, 360 CSS px tall). Unity rebuilds the lobby from these, so both look the same.
 * Run with the web build served: npx vite preview client --port 4173 ; node scripts/unity/export-lobby.cjs
 */
const fs = require('fs');
const { chromium, devices } = require('playwright');
const OUT = 'unity/Assets/LastWar/Resources/UI/Lobby';
const URL = process.argv[2] || 'http://127.0.0.1:4173/';
fs.mkdirSync(OUT, { recursive: true });

(async () => {
  const browser = await chromium.launch({ args: ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader'] });
  const ctx = await browser.newContext({ ...devices['Pixel 7 landscape'], deviceScaleFactor: 3 });
  await ctx.addInitScript(() => { localStorage.setItem('lw.profile.v1', JSON.stringify({ name: 'Soldier', skin: 0, map: 'green' })); });
  const page = await ctx.newPage();
  await page.goto(URL);
  await page.waitForSelector('.lobby .bots', { timeout: 90000 });
  await page.waitForTimeout(1500);

  // ---- layout: rect + text style of each piece (CSS px)
  const layout = await page.evaluate(() => {
    const W = innerWidth, H = innerHeight, out = [];
    const add = (id, sel, kind, extra = {}) => {
      const e = document.querySelector(sel); if (!e) return;
      const r = e.getBoundingClientRect(); if (!r.width) return;
      const cs = getComputedStyle(e);
      out.push({ id, kind, x: r.left, y: r.top, w: r.width, h: r.height, text: kind === 'text' ? e.textContent.trim() : '',
        size: parseFloat(cs.fontSize), color: cs.color, weight: cs.fontWeight, spacing: parseFloat(cs.letterSpacing) || 0, align: cs.textAlign, ...extra });
    };
    add('logo', '.lobby-logo', 'img');
    add('banner', '.lb-banner', 'img'); add('avatar', '.lb-avatar', 'img'); add('name', '.lb-name', 'text'); add('level', '.lb-level', 'text');
    add('xpbar', '.lb-lv i', 'img'); add('server', '.srv', 'text'); add('rank', '.lb-rank svg', 'icon');
    document.querySelectorAll('.lb-chip').forEach((c, i) => { const id = 'chip' + i; c.id = id; add(id, '#' + id, 'img'); add(id + '_icon', '#' + id + ' svg', 'icon'); add(id + '_text', '#' + id + ' b', 'text'); });
    add('gear', '.lb-gear', 'img'); add('gear_icon', '.lb-gear svg', 'icon');
    document.querySelectorAll('.lb-tab').forEach((t, i) => { const id = 'tab' + i; t.id = id; add(id, '#' + id, 'img'); add(id + '_icon', '#' + id + ' svg', 'icon'); add(id + '_text', '#' + id + ' span', 'text'); });
    add('mapcard', '.lb-mapcard', 'img'); add('mapcard_pin', '.lb-pin svg', 'icon'); add('mapcard_name', '.lb-mapname', 'text'); add('mapcard_sub', '.lb-mapcard small', 'text');
    add('season', '.lb-season', 'img'); add('season_icon', '.lb-season-top svg', 'icon'); add('season_title', '.lb-season-top b', 'text'); add('season_lv', '.lb-season-lv', 'text');
    add('season_star', '.lb-season-bar span', 'text'); add('season_bar', '.lb-season-bar i', 'img'); add('season_xp', '.lb-season-xp', 'text');
    add('tip', '.lb-tip', 'img'); add('tip_icon', '.lb-tip svg', 'icon'); add('tip_text', '.lb-tip-txt', 'text');
    add('modes', '.lb-modes', 'img');
    document.querySelectorAll('.lb-mode').forEach((m, i) => { const id = 'mode' + i; m.id = id; add(id, '#' + id, 'text'); });
    add('play', '.lobby button.big.bots', 'img'); add('play_sub', '.lobby button.big.bots small', 'text');
    return { W, H, nodes: out };
  });
  // the PLAY label: the button text without the subtitle
  const play = layout.nodes.find((n) => n.id === 'play');
  layout.nodes.push({ ...play, id: 'play_text', kind: 'text', text: 'PLAY', size: await page.$eval('.lobby button.big.bots', (e) => parseFloat(getComputedStyle(e).fontSize)), color: 'rgb(27, 20, 4)', h: play.h * 0.62 });
  fs.writeFileSync(`${OUT}/lobby_layout.json`, JSON.stringify(layout));

  // ---- backgrounds: hide text + icons + the 3D view, then shoot each piece
  await page.addStyleTag({ content: `.lobby-3d{display:none!important} html,body,#app,.lobby{background:transparent!important}
    .lobby *{color:transparent!important;text-shadow:none!important} .lobby svg,.lobby .lb-avatar,.lobby-logo{visibility:hidden!important}
    .lb-lv i em,.lb-season-bar i em{width:0!important}` });
  const shot = async (sel, name) => { const e = await page.$(sel); if (e) await e.screenshot({ path: `${OUT}/${name}.png`, omitBackground: true }); };
  await shot('.lb-banner', 'banner'); await shot('.lb-lv i', 'xpbar'); await shot('.lb-chip', 'chip'); await shot('.lb-gear', 'gear');
  await shot('.lb-tab.on', 'tab_on'); await shot('.lb-tab[data-tab="outfit"]', 'tab');
  await shot('.lb-season', 'season'); await shot('.lb-season-bar i', 'bar'); await shot('.lb-tip', 'tip');
  await shot('.lb-modes', 'modes'); await shot('.lb-mode.on', 'mode_on'); await shot('.lobby button.big.bots', 'play');
  await shot('.lb-mapcard', 'mapcard_green');
  // the desert card: pick the map, shoot again
  await page.evaluate(() => { const p = JSON.parse(localStorage.getItem('lw.profile.v1')); p.map = 'desert'; localStorage.setItem('lw.profile.v1', JSON.stringify(p)); });
  await page.evaluate(() => { document.querySelector('.lb-mapimg').src = '/assets/maps/desert.webp'; }); await page.waitForTimeout(800);
  await shot('.lb-mapcard', 'mapcard_desert');
  // sheet (outfit list) + cards
  await page.tap('.lb-tab[data-tab="outfit"]'); await page.waitForTimeout(600);
  await page.addStyleTag({ content: '.lobby .ocard img{visibility:hidden!important} .lb-sheet section{visibility:hidden} .lb-sheet .lb-x{visibility:visible}' });
  const sheetRect = await page.$eval('.lb-sheet', (e) => { const r = e.getBoundingClientRect(); return { x: r.left, y: r.top, w: r.width, h: r.height }; });
  await shot('.lb-sheet', 'sheet'); await shot('.lb-x', 'close');
  await page.addStyleTag({ content: '.lb-sheet section{visibility:visible}' });
  await shot('.ocard:nth-child(2)', 'card'); await shot('.ocard:nth-child(1)', 'card_on');
  const cardRect = await page.$eval('.ocard', (e) => { const r = e.getBoundingClientRect(); return { w: r.width, h: r.height }; });
  layout.sheet = sheetRect; layout.card = cardRect;
  fs.writeFileSync(`${OUT}/lobby_layout.json`, JSON.stringify(layout));
  // the logo and outfit pictures as they are
  fs.copyFileSync('client/public/logo.png', `${OUT}/logo.png`);
  await browser.close();
  console.log('lobby captured:', fs.readdirSync(OUT).join(' '));
})();
