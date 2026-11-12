import { MAP } from '../config/game';
import { fbm, mulberry32, smoothstep } from '../math/rng';
import { PREFABS, type PrefabName } from './prefabs';

/** Axis-aligned (in local space) box rotated around Y. Used for houses, crates, walls. */
export interface BoxPrim {
  x: number; y: number; z: number;
  hx: number; hy: number; hz: number;
  rot: number;
  color: number;
}
/** Instance of an imported model (house, car, tree, rock...). Origin = bottom centre. */
export interface PropInst { p: PrefabName; x: number; y: number; z: number; rot: number; s: number }

export interface MapData {
  seed: number;
  size: number;
  cells: number;
  /** (cells+1)^2 heights, row-major: index = iz*(cells+1)+ix */
  heights: Float32Array;
  boxes: BoxPrim[];
  props: PropInst[];
  spawns: { x: number; z: number }[];
}

export function generateMap(seed: number = MAP.seed): MapData {
  const { size, cells } = MAP;
  const rnd = mulberry32(seed);
  const half = size / 2;
  const cell = size / cells;
  const n = cells + 1;

  // A few interior peaks for verticality.
  const peaks: { x: number; z: number; h: number; r: number }[] = [];
  for (let i = 0; i < 3; i++) {
    const a = rnd() * Math.PI * 2, d = 40 + rnd() * 60;
    peaks.push({ x: Math.cos(a) * d, z: Math.sin(a) * d, h: 12 + rnd() * 10, r: 18 + rnd() * 10 });
  }

  const heightFn = (x: number, z: number) => {
    const r = Math.hypot(x, z) / half;
    let h = (fbm(x * 0.012, z * 0.012, seed) - 0.5) * 9;
    h += (fbm(x * 0.05, z * 0.05, seed + 7) - 0.5) * 1.4;
    const ring = smoothstep(0.82, 1.05, r);
    h += ring * (38 + fbm(x * 0.03, z * 0.03, seed + 13) * 30);
    for (const p of peaks) {
      const d2 = ((x - p.x) ** 2 + (z - p.z) ** 2) / (p.r * p.r);
      h += p.h * Math.exp(-d2 * 1.6);
    }
    return h;
  };

  const heights = new Float32Array(n * n);
  for (let iz = 0; iz < n; iz++) {
    for (let ix = 0; ix < n; ix++) {
      heights[iz * n + ix] = heightFn(-half + ix * cell, -half + iz * cell);
    }
  }

  const data: MapData = { seed, size, cells, heights, boxes: [], props: [], spawns: [] };
  const groundAt = (x: number, z: number) => terrainHeight(data, x, z);

  // Occupancy circles to avoid overlaps between features.
  const occupied: { x: number; z: number; r: number }[] = [];
  const free = (x: number, z: number, r: number) =>
    occupied.every((o) => (o.x - x) ** 2 + (o.z - z) ** 2 > (o.r + r) ** 2);
  const randInValley = (maxR: number) => {
    const a = rnd() * Math.PI * 2, d = Math.sqrt(rnd()) * maxR;
    return { x: Math.cos(a) * d, z: Math.sin(a) * d };
  };

  // ---------- Houses (imported enterable models on a stone foundation) ----------
  const houseTypes: PrefabName[] = ['house_a', 'house_b', 'house_c'];
  let houses = 0;
  for (let tries = 0; tries < 800 && houses < 12; tries++) {
    const p = randInValley(MAP.playRadius - 22);
    const type = houseTypes[houses % houseTypes.length];
    const rotIdx = Math.floor(rnd() * 4);
    const rot = rotIdx * (Math.PI / 2);
    const [sx, , sz] = PREFABS[type].size;
    const w = rotIdx % 2 ? sz : sx, d = rotIdx % 2 ? sx : sz;
    const rad = Math.hypot(w, d) / 2 + 2.5;
    if (!free(p.x, p.z, rad)) continue;
    const corners = [[-1, -1], [1, -1], [-1, 1], [1, 1], [0, 0]].map(([cx, cz]) => groundAt(p.x + (cx * w) / 2, p.z + (cz * d) / 2));
    const lo = Math.min(...corners), hi = Math.max(...corners);
    if (hi - lo > 2.5) continue;
    occupied.push({ x: p.x, z: p.z, r: rad });
    // Level the ground around the house so doors are reachable from every side.
    const avg = corners.reduce((a, b) => a + b, 0) / corners.length;
    flattenTerrain(data, p.x, p.z, w / 2 + 2.5, d / 2 + 2.5, avg, 7);
    const floorY = avg + 0.12;
    data.boxes.push({ x: p.x, y: avg - 0.9, z: p.z, hx: w / 2 + 0.2, hy: 1.02, hz: d / 2 + 0.2, rot: 0, color: 0x8a857a });
    data.props.push({ p: type, x: p.x, y: floorY, z: p.z, rot, s: 1 });
    houses++;
  }

  // ---------- Abandoned cars ----------
  const carTypes: PrefabName[] = ['car1', 'car2', 'car3', 'car4'];
  for (let i = 0, tries = 0; i < 14 && tries < 600; tries++) {
    const p = randInValley(MAP.playRadius - 12);
    if (!free(p.x, p.z, 3.4)) continue;
    const g = Math.min(...[[-1.2, 0], [1.2, 0], [0, -1.2], [0, 1.2]].map(([a, b]) => groundAt(p.x + a, p.z + b)));
    if (groundAt(p.x, p.z) - g > 0.6) continue;
    occupied.push({ x: p.x, z: p.z, r: 3 });
    data.props.push({ p: carTypes[i % carTypes.length], x: p.x, y: g - 0.05, z: p.z, rot: rnd() * Math.PI * 2, s: 1 });
    i++;
  }

  // ---------- Fences ----------
  for (let i = 0, tries = 0; i < 6 && tries < 400; tries++) {
    const p = randInValley(MAP.playRadius - 15);
    if (!free(p.x, p.z, 6.5)) continue;
    const g = groundAt(p.x, p.z);
    const hs = [[-5, -3.6], [5, -3.6], [-5, 3.6], [5, 3.6]].map(([a, b]) => groundAt(p.x + a, p.z + b));
    if (Math.max(...hs) - Math.min(...hs) > 1.2) continue;
    occupied.push({ x: p.x, z: p.z, r: 6 });
    data.props.push({ p: 'fence', x: p.x, y: Math.min(g, ...hs) - 0.05, z: p.z, rot: Math.floor(rnd() * 4) * (Math.PI / 2), s: 1 });
    i++;
  }

  // ---------- Crates & low walls (cover) ----------
  for (let i = 0, tries = 0; i < 45 && tries < 800; tries++) {
    const p = randInValley(MAP.playRadius - 8);
    if (!free(p.x, p.z, 2)) continue;
    occupied.push({ x: p.x, z: p.z, r: 1.6 });
    const g = groundAt(p.x, p.z);
    const rot = rnd() * Math.PI;
    if (rnd() < 0.55) {
      const s = 0.55 + rnd() * 0.2;
      data.boxes.push({ x: p.x, y: g + s - 0.15, z: p.z, hx: s, hy: s, hz: s, rot, color: 0x8a6a3e });
      if (rnd() < 0.4) data.boxes.push({ x: p.x, y: g + s * 3 - 0.15, z: p.z, hx: s * 0.8, hy: s * 0.8, hz: s * 0.8, rot: rot + 0.4, color: 0x9b7748 });
    } else {
      data.boxes.push({ x: p.x, y: g + 0.35, z: p.z, hx: 2.2, hy: 0.85, hz: 0.3, rot, color: 0x8f8f86 });
    }
    i++;
  }

  // ---------- Pine trees ----------
  const pines: PrefabName[] = ['pine1', 'pine2', 'pine4', 'pine5'];
  for (let i = 0, tries = 0; i < 300 && tries < 3500; tries++) {
    const p = randInValley(MAP.playRadius + 15);
    const g = groundAt(p.x, p.z);
    if (g > MAP.snowHeight - 4) continue;
    if (!free(p.x, p.z, 1.3)) continue;
    occupied.push({ x: p.x, z: p.z, r: 0.9 });
    data.props.push({ p: pines[i % pines.length], x: p.x, y: g - 0.25, z: p.z, rot: rnd() * Math.PI * 2, s: 0.8 + rnd() * 0.5 });
    i++;
  }

  // ---------- Rocks, stumps & bushes ----------
  const rocks: PrefabName[] = ['rock1', 'rock2', 'rock3', 'rock4'];
  for (let i = 0, tries = 0; i < 80 && tries < 2000; tries++) {
    const p = randInValley(MAP.playRadius + 20);
    const s = 0.6 + rnd() * 0.9;
    if (!free(p.x, p.z, 2 * s)) continue;
    occupied.push({ x: p.x, z: p.z, r: 1.6 * s });
    data.props.push({ p: rocks[i % rocks.length], x: p.x, y: groundAt(p.x, p.z) - 0.25 * s, z: p.z, rot: rnd() * Math.PI * 2, s });
    i++;
  }
  for (let i = 0, tries = 0; i < 25 && tries < 600; tries++) {
    const p = randInValley(MAP.playRadius);
    if (!free(p.x, p.z, 0.8)) continue;
    occupied.push({ x: p.x, z: p.z, r: 0.6 });
    data.props.push({ p: 'stump', x: p.x, y: groundAt(p.x, p.z) - 0.1, z: p.z, rot: rnd() * Math.PI * 2, s: 0.8 + rnd() * 0.4 });
    i++;
  }
  // Bushes have no collider: good for hiding.
  for (let i = 0, tries = 0; i < 110 && tries < 1500; tries++) {
    const p = randInValley(MAP.playRadius + 5);
    if (!free(p.x, p.z, 0.8)) continue;
    data.props.push({ p: i % 2 ? 'bush1' : 'bush2', x: p.x, y: groundAt(p.x, p.z) - 0.15, z: p.z, rot: rnd() * Math.PI * 2, s: 0.7 + rnd() * 0.6 });
    i++;
  }

  // ---------- Spawn points ----------
  for (let tries = 0; tries < 2000 && data.spawns.length < 40; tries++) {
    const p = randInValley(MAP.playRadius - 15);
    if (!free(p.x, p.z, 1.5)) continue;
    if (data.spawns.some((s) => (s.x - p.x) ** 2 + (s.z - p.z) ** 2 < 15 * 15)) continue;
    data.spawns.push(p);
  }
  return data;
}

/** Flatten a rectangle (half extents hx,hz, axis aligned) to height y, blending out over `blend` metres. */
function flattenTerrain(map: MapData, cx: number, cz: number, hx: number, hz: number, y: number, blend: number) {
  const { size, cells, heights } = map;
  const n = cells + 1, cell = size / cells;
  for (let iz = 0; iz < n; iz++) for (let ix = 0; ix < n; ix++) {
    const x = -size / 2 + ix * cell, z = -size / 2 + iz * cell;
    const dx = Math.max(0, Math.abs(x - cx) - hx), dz = Math.max(0, Math.abs(z - cz) - hz);
    const d = Math.hypot(dx, dz);
    if (d >= blend) continue;
    const t = smoothstep(0, blend, d);
    const i = iz * n + ix;
    heights[i] = y + (heights[i] - y) * t;
  }
}

/** Exact terrain height matching the physics trimesh triangulation. */
export function terrainHeight(map: MapData, x: number, z: number): number {
  const { size, cells, heights } = map;
  const n = cells + 1;
  const cell = size / cells;
  const fx = (x + size / 2) / cell, fz = (z + size / 2) / cell;
  const ix = Math.min(cells - 1, Math.max(0, Math.floor(fx)));
  const iz = Math.min(cells - 1, Math.max(0, Math.floor(fz)));
  const u = Math.min(1, Math.max(0, fx - ix)), v = Math.min(1, Math.max(0, fz - iz));
  const h00 = heights[iz * n + ix], h10 = heights[iz * n + ix + 1];
  const h01 = heights[(iz + 1) * n + ix], h11 = heights[(iz + 1) * n + ix + 1];
  // Triangles: (00,01,10) and (10,01,11) — split along the 01-10 diagonal.
  if (u + v <= 1) return h00 + (h10 - h00) * u + (h01 - h00) * v;
  return h11 + (h01 - h11) * (1 - u) + (h10 - h11) * (1 - v);
}

/** Shared terrain triangulation for physics + rendering. */
export function terrainMesh(map: MapData): { vertices: Float32Array; indices: Uint32Array } {
  const { size, cells, heights } = map;
  const n = cells + 1;
  const cell = size / cells;
  const vertices = new Float32Array(n * n * 3);
  for (let iz = 0; iz < n; iz++) for (let ix = 0; ix < n; ix++) {
    const i = iz * n + ix;
    vertices[i * 3] = -size / 2 + ix * cell;
    vertices[i * 3 + 1] = heights[i];
    vertices[i * 3 + 2] = -size / 2 + iz * cell;
  }
  const indices = new Uint32Array(cells * cells * 6);
  let k = 0;
  for (let iz = 0; iz < cells; iz++) for (let ix = 0; ix < cells; ix++) {
    const a = iz * n + ix, b = a + 1, c = a + n, d = c + 1;
    // counter-clockwise seen from above (+Y): a, c, b and b, c, d
    indices[k++] = a; indices[k++] = c; indices[k++] = b;
    indices[k++] = b; indices[k++] = c; indices[k++] = d;
  }
  return { vertices, indices };
}
