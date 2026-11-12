import { MAP } from '../config/game';
import { fbm, mulberry32, smoothstep } from '../math/rng';

/** Axis-aligned (in local space) box rotated around Y. Used for houses, crates, walls. */
export interface BoxPrim {
  x: number; y: number; z: number;
  hx: number; hy: number; hz: number;
  rot: number;
  color: number;
}
export interface TreeInst { x: number; y: number; z: number; s: number; rot: number }
export interface RockInst { x: number; y: number; z: number; s: number; rot: number; variant: number }

export interface MapData {
  seed: number;
  size: number;
  cells: number;
  /** (cells+1)^2 heights, row-major: index = iz*(cells+1)+ix */
  heights: Float32Array;
  boxes: BoxPrim[];
  trees: TreeInst[];
  rocks: RockInst[];
  spawns: { x: number; z: number }[];
}

export const ROCK_VARIANTS = 4;

/** Base rock shape: jittered icosahedron. Same points used for collider and mesh. */
export function rockVariantPoints(variant: number): number[] {
  const t = (1 + Math.sqrt(5)) / 2;
  const base = [
    [-1, t, 0], [1, t, 0], [-1, -t, 0], [1, -t, 0],
    [0, -1, t], [0, 1, t], [0, -1, -t], [0, 1, -t],
    [t, 0, -1], [t, 0, 1], [-t, 0, -1], [-t, 0, 1],
  ];
  const rnd = mulberry32(9001 + variant * 77);
  const out: number[] = [];
  for (const [x, y, z] of base) {
    const len = Math.hypot(x, y, z);
    const j = 0.75 + rnd() * 0.5;
    out.push((x / len) * j * (1 + rnd() * 0.3), (y / len) * j * 0.7, (z / len) * j);
  }
  return out;
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

  const data: MapData = { seed, size, cells, heights, boxes: [], trees: [], rocks: [], spawns: [] };
  const groundAt = (x: number, z: number) => terrainHeight(data, x, z);

  // Occupancy circles to avoid overlaps between features.
  const occupied: { x: number; z: number; r: number }[] = [];
  const free = (x: number, z: number, r: number) =>
    occupied.every((o) => (o.x - x) ** 2 + (o.z - z) ** 2 > (o.r + r) ** 2);
  const randInValley = (maxR: number) => {
    const a = rnd() * Math.PI * 2, d = Math.sqrt(rnd()) * maxR;
    return { x: Math.cos(a) * d, z: Math.sin(a) * d };
  };

  // ---------- Houses ----------
  const wallColors = [0xd9c9a3, 0xc7b08a, 0xe3dccb, 0xb9a487];
  const roofColors = [0x9c3d2e, 0x6b4a3a, 0x4f5d6b];
  let houses = 0;
  for (let tries = 0; tries < 600 && houses < 14; tries++) {
    const p = randInValley(MAP.playRadius - 20);
    const w = 7 + Math.floor(rnd() * 3) * 1.5, d = 6 + Math.floor(rnd() * 2) * 1.5;
    const rad = Math.hypot(w, d) / 2 + 1.5;
    if (!free(p.x, p.z, rad)) continue;
    const corners = [[-1, -1], [1, -1], [-1, 1], [1, 1]].map(([sx, sz]) => groundAt(p.x + (sx * w) / 2, p.z + (sz * d) / 2));
    const lo = Math.min(...corners), hi = Math.max(...corners);
    if (hi - lo > 1.6) continue;
    occupied.push({ x: p.x, z: p.z, r: rad });
    const rot = Math.floor(rnd() * 4) * (Math.PI / 2);
    addHouse(data.boxes, p.x, hi + 0.15, p.z, w, d, rot, wallColors[houses % wallColors.length], roofColors[houses % roofColors.length], lo);
    houses++;
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

  // ---------- Trees ----------
  for (let i = 0, tries = 0; i < 260 && tries < 3000; tries++) {
    const p = randInValley(MAP.playRadius + 15);
    const g = groundAt(p.x, p.z);
    if (g > MAP.snowHeight - 6) continue;
    if (!free(p.x, p.z, 1.2)) continue;
    occupied.push({ x: p.x, z: p.z, r: 0.9 });
    data.trees.push({ x: p.x, y: g - 0.2, z: p.z, s: 0.8 + rnd() * 0.7, rot: rnd() * Math.PI * 2 });
    i++;
  }

  // ---------- Rocks ----------
  for (let i = 0, tries = 0; i < 90 && tries < 2000; tries++) {
    const p = randInValley(MAP.playRadius + 20);
    const s = 0.8 + rnd() * 1.8;
    if (!free(p.x, p.z, s * 1.3)) continue;
    occupied.push({ x: p.x, z: p.z, r: s * 1.2 });
    data.rocks.push({ x: p.x, y: groundAt(p.x, p.z) + s * 0.15, z: p.z, s, rot: rnd() * Math.PI * 2, variant: i % ROCK_VARIANTS });
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

function addHouse(out: BoxPrim[], cx: number, floorY: number, cz: number, w: number, d: number, rot: number, wall: number, roof: number, lowestGround: number) {
  const c = Math.cos(rot), s = Math.sin(rot);
  const push = (lx: number, y: number, lz: number, hx: number, hy: number, hz: number, color: number) => {
    // rotate local offset around Y (matching three.js rotation.y convention)
    out.push({ x: cx + lx * c + lz * s, y, z: cz - lx * s + lz * c, hx, hy, hz, rot, color });
  };
  const t = 0.15, wallH = 3, door = 1.6, doorH = 2.3;
  // Foundation (reaches below lowest ground so it never floats)
  const fBottom = lowestGround - 1;
  const fh = (floorY - fBottom) / 2;
  push(0, fBottom + fh, 0, w / 2 + 0.2, fh, d / 2 + 0.2, 0x7d7a70);
  const wy = floorY + wallH / 2;
  // back wall
  push(0, wy, -d / 2 + t, w / 2, wallH / 2, t, wall);
  // side walls (with a window gap on one side for sightlines)
  push(-w / 2 + t, wy, 0, t, wallH / 2, d / 2, wall);
  const win = 1.2;
  const segZ = (d - win) / 4;
  push(w / 2 - t, wy, -d / 2 + segZ, t, wallH / 2, segZ, wall);
  push(w / 2 - t, wy, d / 2 - segZ, t, wallH / 2, segZ, wall);
  push(w / 2 - t, floorY + 0.5, 0, t, 0.5, win / 2, wall); // below window
  push(w / 2 - t, floorY + wallH - 0.4, 0, t, 0.4, win / 2, wall); // above window
  // front wall with door gap
  const side = (w - door) / 4;
  push(-w / 2 + side, wy, d / 2 - t, side, wallH / 2, t, wall);
  push(w / 2 - side, wy, d / 2 - t, side, wallH / 2, t, wall);
  push(0, floorY + doorH + (wallH - doorH) / 2, d / 2 - t, door / 2, (wallH - doorH) / 2, t, wall);
  // roof slab
  push(0, floorY + wallH + 0.15, 0, w / 2 + 0.4, 0.15, d / 2 + 0.4, roof);
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
