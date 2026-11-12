import * as THREE from 'three';
import { GLTFLoader, type GLTF } from 'three/examples/jsm/loaders/GLTFLoader.js';
import { MeshoptDecoder } from 'three/examples/jsm/libs/meshopt_decoder.module.js';
import { PREFAB_NAMES, type PrefabName } from '@lw/shared';

export const GUNS = ['pistol', 'rifle', 'shotgun', 'sniper'] as const;
export type GunName = (typeof GUNS)[number];

export interface PrefabParts {
  /** one entry per material: geometry in prefab space + Lambert material */
  parts: { geometry: THREE.BufferGeometry; material: THREE.Material }[];
}

export interface GameAssets {
  prefabs: Record<PrefabName, PrefabParts>;
  soldier: GLTF;
  guns: Record<GunName, THREE.Object3D>;
}

const loader = new GLTFLoader().setMeshoptDecoder(MeshoptDecoder);

/** Physically based materials are expensive on phones: convert everything to Lambert. */
export function toLambert(m: THREE.Material): THREE.MeshLambertMaterial {
  const src = m as THREE.MeshStandardMaterial;
  const out = new THREE.MeshLambertMaterial({
    map: src.map ?? null,
    color: src.color ? src.color.clone() : new THREE.Color(0xffffff),
    transparent: src.transparent,
    alphaTest: src.alphaTest || (src.transparent ? 0.5 : 0),
    side: src.side,
    vertexColors: src.vertexColors,
  });
  if (out.map) { out.map.anisotropy = 2; out.map.colorSpace = THREE.SRGBColorSpace; }
  out.name = m.name;
  return out;
}

export async function loadAssets(onProgress: (f: number) => void): Promise<GameAssets> {
  const files = [...PREFAB_NAMES.map((n) => `${n}.glb`), 'soldier.glb', ...GUNS.map((g) => `gun_${g}.glb`)];
  let done = 0;
  const load = async (f: string) => {
    const g = await loader.loadAsync(`/assets/${f}`);
    onProgress(++done / files.length);
    return g;
  };
  const results = await Promise.all(files.map(load));
  const byFile = new Map(files.map((f, i) => [f, results[i]]));

  const prefabs = {} as Record<PrefabName, PrefabParts>;
  for (const name of PREFAB_NAMES) {
    const g = byFile.get(`${name}.glb`)!;
    const parts: PrefabParts['parts'] = [];
    g.scene.updateMatrixWorld(true);
    g.scene.traverse((o) => {
      const mesh = o as THREE.Mesh;
      if (!mesh.isMesh) return;
      const geo = toFloatGeometry(mesh.geometry).applyMatrix4(mesh.matrixWorld);
      parts.push({ geometry: geo, material: toLambert(mesh.material as THREE.Material) });
    });
    prefabs[name] = { parts };
  }

  const guns = {} as Record<GunName, THREE.Object3D>;
  for (const gName of GUNS) {
    const scene = byFile.get(`gun_${gName}.glb`)!.scene;
    scene.traverse((o) => { const m = o as THREE.Mesh; if (m.isMesh) m.material = toLambert(m.material as THREE.Material); });
    guns[gName] = scene;
  }

  const soldier = byFile.get('soldier.glb')!;
  soldier.scene.traverse((o) => {
    const m = o as THREE.SkinnedMesh;
    if (m.isMesh) { m.material = toLambert(m.material as THREE.Material); m.frustumCulled = false; }
  });
  return { prefabs, soldier, guns };
}

/**
 * Quantized (meshopt / KHR_mesh_quantization) attributes are normalized integers: transforming them
 * in place would clamp to [-1,1]. Convert every attribute to plain Float32 first.
 */
function toFloatGeometry(src: THREE.BufferGeometry): THREE.BufferGeometry {
  const geo = new THREE.BufferGeometry();
  for (const [name, attr] of Object.entries(src.attributes)) {
    const a = attr as THREE.BufferAttribute | THREE.InterleavedBufferAttribute;
    const n = a.count, size = a.itemSize;
    const arr = new Float32Array(n * size);
    const get = [a.getX, a.getY, a.getZ, a.getW];
    for (let i = 0; i < n; i++) for (let k = 0; k < size; k++) arr[i * size + k] = get[k].call(a, i);
    geo.setAttribute(name, new THREE.BufferAttribute(arr, size));
  }
  if (src.index) geo.setIndex(new THREE.BufferAttribute(new Uint32Array(src.index.array), 1));
  return geo;
}
