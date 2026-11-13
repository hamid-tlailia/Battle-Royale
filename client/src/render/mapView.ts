import * as THREE from 'three';
import { mergeGeometries } from 'three/examples/jsm/utils/BufferGeometryUtils.js';
import { MapData, fbm, terrainMesh, type PrefabName, type PropInst } from '@lw/shared';
import type { GameAssets } from './assets';

/** Chunk size for instanced props (frustum culling granularity). */
const CHUNK = 150;

export function buildMapView(map: MapData, assets: GameAssets): THREE.Group {
  const g = new THREE.Group();
  g.add(buildTerrain(map));
  g.add(buildBoxes(map));
  g.add(...buildProps(map, assets));
  g.add(buildSky());
  const far = new THREE.Mesh(new THREE.PlaneGeometry(3000, 3000).rotateX(-Math.PI / 2), new THREE.MeshBasicMaterial({ color: 0x5d7d52 }));
  far.position.y = -3;
  g.add(far);
  g.traverse((o) => { o.matrixAutoUpdate = false; o.updateMatrix(); });
  return g;
}

/** Small tiling noise texture to break up flat vertex colours (grass/rock detail). */
function detailTexture(): THREE.Texture {
  const N = 128;
  const c = document.createElement('canvas');
  c.width = c.height = N;
  const ctx = c.getContext('2d')!;
  const img = ctx.createImageData(N, N);
  for (let y = 0; y < N; y++) for (let x = 0; x < N; x++) {
    // tileable: sample noise on a torus-ish wrap
    const v = 0.55 * fbm(x / 16, y / 16, 3) + 0.45 * fbm(x / 5, y / 5, 9);
    const w = (x + 1) % N, h = (y + 1) % N; void w; void h;
    const b = 205 + v * 50 + (Math.random() - 0.5) * 18;
    const i = (y * N + x) * 4;
    img.data[i] = img.data[i + 1] = img.data[i + 2] = Math.max(0, Math.min(255, b));
    img.data[i + 3] = 255;
  }
  ctx.putImageData(img, 0, 0);
  const t = new THREE.CanvasTexture(c);
  t.wrapS = t.wrapT = THREE.RepeatWrapping;
  t.colorSpace = THREE.SRGBColorSpace;
  t.anisotropy = 4;
  return t;
}

function buildTerrain(map: MapData): THREE.Mesh {
  const { vertices, indices } = terrainMesh(map);
  const geo = new THREE.BufferGeometry();
  geo.setAttribute('position', new THREE.BufferAttribute(vertices, 3));
  geo.setIndex(new THREE.BufferAttribute(indices, 1));
  geo.computeVertexNormals();
  const n = vertices.length / 3;
  const col = new Float32Array(n * 3);
  const uv = new Float32Array(n * 2);
  const nrm = geo.getAttribute('normal');
  const grassA = new THREE.Color(0x5b8f37), grassB = new THREE.Color(0x7aa846), grassC = new THREE.Color(0x46752c);
  const dirt = new THREE.Color(0x8a7650), rock = new THREE.Color(0x7d7b74), snow = new THREE.Color(0xf2f5f7);
  const c = new THREE.Color();
  for (let i = 0; i < n; i++) {
    const x = vertices[i * 3], y = vertices[i * 3 + 1], z = vertices[i * 3 + 2];
    const ny = nrm.getY(i);
    const v = fbm(x * 0.03, z * 0.03, 11);
    c.copy(grassA).lerp(v > 0.5 ? grassB : grassC, Math.abs(v - 0.5) * 2.2);
    const d = fbm(x * 0.08, z * 0.08, 21);
    if (d > 0.62) c.lerp(dirt, Math.min(1, (d - 0.62) * 4));
    const rockT = 1 - THREE.MathUtils.smoothstep(ny, 0.72, 0.86);
    c.lerp(rock, rockT);
    const snowT = THREE.MathUtils.smoothstep(y + (fbm(x * 0.05, z * 0.05, 3) - 0.5) * 8, map.snowHeight - 2, map.snowHeight + 3) * THREE.MathUtils.smoothstep(ny, 0.5, 0.7);
    c.lerp(snow, snowT);
    col.set([c.r, c.g, c.b], i * 3);
    uv[i * 2] = x / 7; uv[i * 2 + 1] = z / 7;
  }
  geo.setAttribute('color', new THREE.BufferAttribute(col, 3));
  geo.setAttribute('uv', new THREE.BufferAttribute(uv, 2));
  geo.computeBoundingSphere();
  const mat = new THREE.MeshLambertMaterial({ vertexColors: true, map: detailTexture() });
  if (map.albedo && map.mask) applyPhoto(geo, mat, map);
  return new THREE.Mesh(geo, mat);
}

/**
 * Real-terrain maps: drape the aerial photo of the scan over the terrain (world-space UVs),
 * blended by the scan's coverage mask into the generated colours; the tiling detail texture
 * still adds close-up grain.
 */
const detailCache = new Map<string, THREE.Texture>();
/** greyscale tiling detail (mean 0.5) from /assets/maps */
function detailTex(name: string): THREE.Texture {
  let t = detailCache.get(name);
  if (!t) {
    t = new THREE.TextureLoader().load(`/assets/maps/${name}.webp`);
    t.wrapS = t.wrapT = THREE.RepeatWrapping; t.anisotropy = 4; t.colorSpace = THREE.NoColorSpace;
    detailCache.set(name, t);
  }
  return t;
}

/** Scanned rock outcrops: the scan texture is coarse, so add triplanar lichen-rock grain in world space. */
function rockDetail(mat: THREE.Material) {
  const m = mat as THREE.MeshLambertMaterial;
  const u = { detailRock: { value: detailTex('detail_rock') } };
  m.onBeforeCompile = (sh) => {
    Object.assign(sh.uniforms, u);
    sh.vertexShader = sh.vertexShader
      .replace('#include <common>', '#include <common>\nvarying vec3 vWP;\nvarying vec3 vWN;')
      .replace('#include <worldpos_vertex>', '#include <worldpos_vertex>\nvWP = (modelMatrix * instanceMatrix * vec4(transformed, 1.0)).xyz;\nvWN = normalize(mat3(modelMatrix) * objectNormal);');
    sh.fragmentShader = sh.fragmentShader
      .replace('#include <common>', '#include <common>\nuniform sampler2D detailRock;\nvarying vec3 vWP;\nvarying vec3 vWN;')
      .replace('#include <color_fragment>', `#include <color_fragment>
        vec3 w = abs(vWN); w /= (w.x + w.y + w.z + 1e-4);
        float d = texture2D(detailRock, vWP.zy / 2.0).r * w.x + texture2D(detailRock, vWP.xz / 2.0).r * w.y + texture2D(detailRock, vWP.xy / 2.0).r * w.z;
        diffuseColor.rgb *= mix(1.0, d * 2.0, 0.8);`);
  };
  m.customProgramCacheKey = () => 'rock-detail';
}

function applyPhoto(geo: THREE.BufferGeometry, mat: THREE.MeshLambertMaterial, map: MapData) {
  const pos = geo.getAttribute('position');
  const n = pos.count, size = map.size;
  const puv = new Float32Array(n * 2), m = new Float32Array(n);
  for (let i = 0; i < n; i++) {
    puv[i * 2] = (pos.getX(i) + size / 2) / size;
    puv[i * 2 + 1] = 1 - (pos.getZ(i) + size / 2) / size;
    m[i] = map.mask![i] ?? 0;
  }
  geo.setAttribute('photoUv', new THREE.BufferAttribute(puv, 2));
  geo.setAttribute('photoMask', new THREE.BufferAttribute(m, 1));
  const blank = new THREE.DataTexture(new Uint8Array([0, 0, 0, 0]), 1, 1);
  blank.needsUpdate = true;
  const uniforms = { photoMap: { value: blank as THREE.Texture }, detailGrass: { value: detailTex('detail_grass') }, detailRock: { value: detailTex('detail_rock') } };
  new THREE.TextureLoader().load(`/assets/maps/${map.albedo}`, (t) => {
    t.colorSpace = THREE.SRGBColorSpace; t.anisotropy = 4; t.generateMipmaps = true;
    uniforms.photoMap.value = t;
  });
  mat.onBeforeCompile = (sh) => {
    Object.assign(sh.uniforms, uniforms);
    sh.vertexShader = sh.vertexShader
      .replace('#include <common>', '#include <common>\nattribute vec2 photoUv;\nattribute float photoMask;\nvarying vec2 vPhotoUv;\nvarying float vPhotoMask;\nvarying vec2 vGround;')
      .replace('#include <uv_vertex>', '#include <uv_vertex>\nvPhotoUv = photoUv;\nvPhotoMask = photoMask;\nvGround = position.xz;');
    sh.fragmentShader = sh.fragmentShader
      .replace('#include <common>', '#include <common>\nuniform sampler2D photoMap;\nuniform sampler2D detailGrass;\nuniform sampler2D detailRock;\nvarying vec2 vPhotoUv;\nvarying float vPhotoMask;\nvarying vec2 vGround;')
      .replace('#include <color_fragment>', `#include <color_fragment>
        vec4 ph = texture2D(photoMap, vPhotoUv);
        // close-up grain from real CC0 photos: grass where the scan photo is green, lichen rock where it is grey
        float sat = max(ph.r, max(ph.g, ph.b)) - min(ph.r, min(ph.g, ph.b));
        float rockW = 1.0 - smoothstep(0.03, 0.1, sat);
        float dg = texture2D(detailGrass, vGround / 3.0).r, dr = texture2D(detailRock, vGround / 2.2).r;
        float grain = mix(1.0, mix(dg, dr, rockW) * 2.0, 0.85);
        diffuseColor.rgb = mix(diffuseColor.rgb, ph.rgb * grain * 1.12, clamp(vPhotoMask * ph.a, 0.0, 1.0));`);
  };
  mat.customProgramCacheKey = () => 'photo-terrain';
}

function colorize(geo: THREE.BufferGeometry, hex: number | THREE.Color) {
  const c = new THREE.Color(hex);
  const n = geo.getAttribute('position').count;
  const arr = new Float32Array(n * 3);
  for (let i = 0; i < n; i++) arr.set([c.r, c.g, c.b], i * 3);
  geo.setAttribute('color', new THREE.BufferAttribute(arr, 3));
  return geo;
}

function buildBoxes(map: MapData): THREE.Mesh {
  const m = new THREE.Matrix4(), q = new THREE.Quaternion(), up = new THREE.Vector3(0, 1, 0);
  const parts = map.boxes.map((b) => {
    const geo = new THREE.BoxGeometry(b.hx * 2, b.hy * 2, b.hz * 2);
    geo.deleteAttribute('uv');
    m.compose(new THREE.Vector3(b.x, b.y, b.z), q.setFromAxisAngle(up, b.rot), new THREE.Vector3(1, 1, 1));
    geo.applyMatrix4(m);
    return colorize(geo, b.color);
  });
  return new THREE.Mesh(mergeGeometries(parts), new THREE.MeshLambertMaterial({ vertexColors: true }));
}

/**
 * Props are drawn with InstancedMesh, one per (prefab, material, chunk).
 * Untextured prefabs are pre-merged into a single vertex-coloured geometry (1 draw per chunk).
 */
function buildProps(map: MapData, assets: GameAssets): THREE.Object3D[] {
  const out: THREE.Object3D[] = [];
  const byType = new Map<PrefabName, PropInst[]>();
  for (const p of map.props) { if (!byType.has(p.p)) byType.set(p.p, []); byType.get(p.p)!.push(p); }
  const vcMat = new THREE.MeshLambertMaterial({ vertexColors: true });
  const m = new THREE.Matrix4(), q = new THREE.Quaternion(), up = new THREE.Vector3(0, 1, 0), pos = new THREE.Vector3(), sc = new THREE.Vector3();

  for (const [type, list] of byType) {
    let parts = assets.prefabs[type].parts;
    if (parts.every((p) => !(p.material as THREE.MeshLambertMaterial).map)) {
      const merged = mergeGeometries(parts.map((p) => {
        const geo = p.geometry.index ? p.geometry.toNonIndexed() : p.geometry.clone();
        geo.deleteAttribute('uv');
        return colorize(geo, (p.material as THREE.MeshLambertMaterial).color);
      }));
      parts = [{ geometry: merged, material: vcMat }];
    }
    // chunk instances
    const chunks = new Map<string, PropInst[]>();
    for (const p of list) {
      const key = list.length > 12 ? `${Math.floor(p.x / CHUNK)},${Math.floor(p.z / CHUNK)}` : 'all';
      if (!chunks.has(key)) chunks.set(key, []);
      chunks.get(key)!.push(p);
    }
    for (const inst of chunks.values()) {
      for (const part of parts) {
        if (type === 'tors_green') rockDetail(part.material);
        const im = new THREE.InstancedMesh(part.geometry, part.material, inst.length);
        inst.forEach((p, i) => {
          m.compose(pos.set(p.x, p.y, p.z), q.setFromAxisAngle(up, p.rot), sc.set(p.s, p.s, p.s));
          im.setMatrixAt(i, m);
        });
        const tint = tintFor(type, part.material.name);
        if (tint) inst.forEach((p, i) => im.setColorAt(i, tint[hashProp(p) % tint.length]));
        im.computeBoundingSphere();
        im.name = type;
        out.push(im);
      }
    }
  }
  return out;
}

function buildSky(): THREE.Mesh {
  const R = 250;
  const geo = new THREE.SphereGeometry(R, 16, 10);
  const top = new THREE.Color(0x4f95d6), bottom = new THREE.Color(0xd4e8f2);
  const pos = geo.getAttribute('position');
  const cols = new Float32Array(pos.count * 3);
  const c = new THREE.Color();
  for (let i = 0; i < pos.count; i++) {
    const t = Math.max(0, pos.getY(i) / R);
    c.copy(bottom).lerp(top, Math.pow(t, 0.55));
    cols.set([c.r, c.g, c.b], i * 3);
  }
  geo.setAttribute('color', new THREE.BufferAttribute(cols, 3));
  const sky = new THREE.Mesh(geo, new THREE.MeshBasicMaterial({ vertexColors: true, side: THREE.BackSide, fog: false, depthWrite: false }));
  sky.renderOrder = -1;
  sky.frustumCulled = false;
  return sky;
}

// ---------------------------------------------------------------- variety
const C = (h: number) => new THREE.Color(h);
/** Per-material colour palettes (multiplied with the texture) so houses/cars don't all look the same. */
const HOUSE_WALLS = [0xffffff, 0xffe9b8, 0xffd6c4, 0xd8e6ff, 0xfff4d6, 0xe6ffd0, 0xffc98f, 0xe0d0ff].map(C);
const HOUSE_ROOF = [0xffffff, 0x9c6b5a, 0x7a8a99, 0xc98a5a, 0x8a5a4a, 0x6f7f5f].map(C);
const HOUSE_WOOD = [0xffffff, 0x6fa86f, 0x6f8fd0, 0xd06f5f, 0x9a7a5a, 0x5f5f5f].map(C);
const CAR_PAINT = [0xffffff, 0xff8a7a, 0x8ab4ff, 0xa8e08a, 0xffe08a, 0xb0b0b0, 0x8a6a5a].map(C);

function tintFor(type: string, materialName: string): THREE.Color[] | null {
  if (type.startsWith('house')) {
    const n = materialName.toLowerCase();
    if (n.includes('telha')) return HOUSE_ROOF;
    if (n.includes('madeira')) return HOUSE_WOOD;
    if (n.includes('pedra')) return null;
    return HOUSE_WALLS;
  }
  if (type.startsWith('car')) return CAR_PAINT;
  return null;
}

/** Stable per-instance hash (same house -> same colours on every device). */
function hashProp(p: PropInst): number {
  let h = Math.imul(Math.round(p.x * 10), 73856093) ^ Math.imul(Math.round(p.z * 10), 19349663);
  h = Math.imul(h ^ (h >>> 13), 1274126177);
  return (h ^ (h >>> 16)) >>> 0;
}
