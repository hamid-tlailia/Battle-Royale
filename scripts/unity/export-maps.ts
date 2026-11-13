/**
 * Exports the game data the Unity version reads (same maps as the web game, bit for bit):
 *   unity/Assets/LastWar/Resources/Maps/<id>.json   heights grid, props, boxes, spawns, areas
 *   unity/Assets/LastWar/Resources/Maps/<id>_albedo.png  aerial photo (real-terrain maps)
 *   unity/Assets/LastWar/Resources/Data/prefabs.json  collider shapes of every model
 * Run: npx tsx scripts/unity/export-maps.ts
 */
import fs from 'node:fs';
import sharp from 'sharp';
import { MAP, MAP_LIST, generateMap, PREFABS, ZONE_PHASES, ZONE_DELAY, ZONE_START_RADIUS, WEAPONS, LOOT, SKY, PLAYER } from '../../shared/src';

const OUT = 'unity/Assets/LastWar/Resources';
fs.mkdirSync(`${OUT}/Maps`, { recursive: true });
fs.mkdirSync(`${OUT}/Data`, { recursive: true });
const r2 = (v: number) => Math.round(v * 100) / 100;

for (const m of MAP_LIST) {
  const map = generateMap(MAP.seed, m.id);
  const json = {
    id: map.id, name: m.name, size: map.size, cells: map.cells, snowHeight: map.snowHeight, playRadius: MAP.playRadius,
    heights: Array.from(map.heights, r2),
    mask: map.mask ? Array.from(map.mask, (v) => Math.round(v * 255)) : [],
    albedo: map.albedo ? `${map.id}_albedo` : '',
    props: map.props.map((p) => ({ p: p.p, x: r2(p.x), y: r2(p.y), z: r2(p.z), rot: Math.round(p.rot * 1000) / 1000, s: Math.round(p.s * 1000) / 1000 })),
    boxes: map.boxes.map((b) => ({ x: r2(b.x), y: r2(b.y), z: r2(b.z), hx: r2(b.hx), hy: r2(b.hy), hz: r2(b.hz), rot: Math.round(b.rot * 1000) / 1000, color: b.color })),
    spawns: map.spawns,
    areas: map.areas,
  };
  fs.writeFileSync(`${OUT}/Maps/${m.id}.json`, JSON.stringify(json));
  if (map.albedo) {
    // aerial photo where the scan has data, generated ground colours (grass / sand / rock by slope) elsewhere
    const W = 2048;
    const { data: photo } = await sharp(`client/public/assets/maps/${map.albedo}`).resize(W, W).removeAlpha().raw().toBuffer({ resolveWithObject: true });
    const n = map.cells + 1, cell = map.size / map.cells, half = map.size / 2;
    const desert = map.id === 'desert';
    const base = desert ? [196, 132, 92] : [92, 122, 58], rock = desert ? [138, 74, 50] : [125, 123, 116];
    const out = Buffer.alloc(W * W * 3);
    for (let r = 0; r < W; r++) for (let c = 0; c < W; c++) {
      const x = -half + ((c + 0.5) / W) * map.size, z = -half + ((r + 0.5) / W) * map.size; // image top row = -z edge
      const fx = Math.min(map.cells - 1e-6, Math.max(0, (x + half) / cell)), fz = Math.min(map.cells - 1e-6, Math.max(0, (z + half) / cell));
      const ix = Math.floor(fx), iz = Math.floor(fz), u = fx - ix, v = fz - iz;
      const g = (a: Float32Array | undefined, i: number) => (a ? a[i] : 0);
      const mk = map.mask ? g(map.mask, iz * n + ix) * (1 - u) * (1 - v) + g(map.mask, iz * n + ix + 1) * u * (1 - v) + g(map.mask, (iz + 1) * n + ix) * (1 - u) * v + g(map.mask, (iz + 1) * n + ix + 1) * u * v : 1;
      const h = (i: number) => map.heights[Math.min(n * n - 1, i)];
      const slope = Math.hypot(h(iz * n + ix + 1) - h(iz * n + ix), h((iz + 1) * n + ix) - h(iz * n + ix)) / cell;
      const k = Math.min(1, Math.max(0, (slope - 0.35) / 0.4)), noise = 0.92 + 0.16 * ((Math.sin(x * 0.11 + Math.cos(z * 0.07) * 2) * Math.cos(z * 0.09 + Math.sin(x * 0.05)) + 1) / 2);
      const o = (r * W + c) * 3;
      for (let ch = 0; ch < 3; ch++) {
        const gen = (base[ch] * (1 - k) + rock[ch] * k) * noise;
        const t = Math.min(1, Math.max(0, (mk - 0.15) / 0.7));
        out[o + ch] = Math.round(photo[o + ch] * t + gen * (1 - t));
      }
    }
    await sharp(out, { raw: { width: W, height: W, channels: 3 } }).jpeg({ quality: 86 }).toFile(`${OUT}/Maps/${m.id}_albedo.jpg`);
  }
  console.log(m.id, 'props', json.props.length, 'boxes', json.boxes.length, (fs.statSync(`${OUT}/Maps/${m.id}.json`).size / 1024).toFixed(0), 'KB');
}

// collider shapes (trimesh / hull / cylinder) per model, same as the web physics
// (array form: Unity's JsonUtility cannot read dictionaries)
const shapes = Object.entries(PREFABS).map(([name, v]) => {
  const c = v.collider as { type: string; v?: number[]; i?: number[]; radius?: number; height?: number };
  return { name, type: c.type, v: c.v ?? [], i: c.i ?? [], radius: c.radius ?? 0, height: c.height ?? 0 };
});
fs.writeFileSync(`${OUT}/Data/prefabs.json`, JSON.stringify({ shapes }));

// tuning tables (weapons, loot, zone, sky-dive, player) so both versions play the same
fs.writeFileSync(`${OUT}/Data/tuning.json`, JSON.stringify({ weapons: WEAPONS, loot: LOOT, zone: { phases: ZONE_PHASES, delay: ZONE_DELAY, startRadius: ZONE_START_RADIUS }, sky: SKY, player: PLAYER }));
console.log('data written');
