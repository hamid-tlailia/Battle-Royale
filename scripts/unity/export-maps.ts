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
  if (map.albedo) await sharp(`client/public/assets/maps/${map.albedo}`).jpeg({ quality: 88 }).toFile(`${OUT}/Maps/${m.id}_albedo.jpg`);
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
