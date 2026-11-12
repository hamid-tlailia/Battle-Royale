import RAPIER from '@dimforge/rapier3d-compat';
import { GROUP_PLAYER, GROUP_WORLD, MAP, PLAYER, groups } from '../config/game';
import { MapData, terrainHeight, terrainMesh } from '../map/mapgen';
import { PREFABS } from '../map/prefabs';

export { RAPIER };

let initPromise: Promise<void> | null = null;
/** Must be awaited once before creating a PhysicsWorld. */
export function initPhysics(): Promise<void> {
  if (!initPromise) initPromise = RAPIER.init();
  return initPromise;
}

const quatY = (rot: number) => ({ x: 0, y: Math.sin(rot / 2), z: 0, w: Math.cos(rot / 2) });

/**
 * Wraps a Rapier world containing static map geometry and one capsule collider per player.
 * Identical on client and server: built only from MapData.
 */
export class PhysicsWorld {
  readonly world: RAPIER.World;
  readonly controller: RAPIER.KinematicCharacterController;
  readonly players = new Map<number, RAPIER.Collider>();
  readonly worldGroups = groups(GROUP_WORLD, 0xffff);
  readonly playerGroups = groups(GROUP_PLAYER, 0xffff);

  constructor(readonly map: MapData) {
    this.world = new RAPIER.World({ x: 0, y: 0, z: 0 });
    this.world.timestep = 1 / 30;

    const { vertices, indices } = terrainMesh(map);
    this.world.createCollider(RAPIER.ColliderDesc.trimesh(vertices, indices).setCollisionGroups(this.worldGroups));

    for (const b of map.boxes) {
      this.world.createCollider(
        RAPIER.ColliderDesc.cuboid(b.hx, b.hy, b.hz)
          .setTranslation(b.x, b.y, b.z)
          .setRotation(quatY(b.rot))
          .setCollisionGroups(this.worldGroups),
      );
    }
    // Imported models: colliders generated from the actual meshes (scripts/assets/build.mjs)
    const scaled = new Map<string, Float32Array>();
    const shapeVerts = (key: string, v: number[], sc: number) => {
      const k = `${key}@${sc.toFixed(3)}`;
      let arr = scaled.get(k);
      if (!arr) { arr = new Float32Array(v.length); for (let i = 0; i < v.length; i++) arr[i] = v[i] * sc; scaled.set(k, arr); }
      return arr;
    };
    for (const pr of map.props) {
      const c = PREFABS[pr.p].collider;
      let desc: RAPIER.ColliderDesc | null = null;
      if (c.type === 'trimesh') desc = RAPIER.ColliderDesc.trimesh(shapeVerts(pr.p, c.v, pr.s), new Uint32Array(c.i));
      else if (c.type === 'hull') desc = RAPIER.ColliderDesc.convexHull(shapeVerts(pr.p, c.v, pr.s));
      else if (c.type === 'cylinder') {
        const h = c.height * pr.s;
        this.world.createCollider(RAPIER.ColliderDesc.cylinder(h / 2, c.radius * pr.s).setTranslation(pr.x, pr.y + h / 2, pr.z).setCollisionGroups(this.worldGroups));
        continue;
      }
      if (!desc) continue;
      this.world.createCollider(desc.setTranslation(pr.x, pr.y, pr.z).setRotation(quatY(pr.rot)).setCollisionGroups(this.worldGroups));
    }
    // Invisible boundary: a ring of tall boxes around the valley.
    const segs = 32, R = MAP.playRadius + 8;
    for (let i = 0; i < segs; i++) {
      const a = (i / segs) * Math.PI * 2;
      const len = (2 * Math.PI * R) / segs;
      this.world.createCollider(
        RAPIER.ColliderDesc.cuboid(len / 2 + 1, 80, 1)
          .setTranslation(Math.cos(a) * R, 40, Math.sin(a) * R)
          .setRotation(quatY(-a + Math.PI / 2))
          .setCollisionGroups(this.worldGroups),
      );
    }

    const c = this.world.createCharacterController(PLAYER.controllerOffset);
    c.setUp({ x: 0, y: 1, z: 0 });
    c.setMaxSlopeClimbAngle((PLAYER.maxSlopeDeg * Math.PI) / 180);
    c.setMinSlopeSlideAngle(((PLAYER.maxSlopeDeg + 5) * Math.PI) / 180);
    c.enableAutostep(PLAYER.autostepHeight, 0.2, false);
    c.enableSnapToGround(0.4);
    c.setSlideEnabled(true);
    c.setApplyImpulsesToDynamicBodies(false);
    this.controller = c;

    this.world.step(); // build acceleration structures
  }

  addPlayer(id: number, x: number, y: number, z: number): RAPIER.Collider {
    const col = this.world.createCollider(
      RAPIER.ColliderDesc.capsule(PLAYER.halfHeight, PLAYER.radius)
        .setTranslation(x, y, z)
        .setCollisionGroups(this.playerGroups),
    );
    this.players.set(id, col);
    return col;
  }

  removePlayer(id: number) {
    const c = this.players.get(id);
    if (c) this.world.removeCollider(c, false);
    this.players.delete(id);
  }

  /** Refresh broad-phase after colliders moved (call once per tick). */
  updateQueries() {
    this.world.step();
  }

  groundHeight(x: number, z: number) {
    return terrainHeight(this.map, x, z);
  }

  /** True if a player capsule at (x,y,z) overlaps anything except `exclude`. */
  capsuleBlocked(x: number, y: number, z: number, halfHeight: number, exclude?: RAPIER.Collider): boolean {
    return this.shapeBlocked(x, y, z, halfHeight, PLAYER.radius, exclude);
  }

  private shapeCache = new Map<string, RAPIER.Capsule>();
  private capsule(hh: number, r: number) {
    const k = `${hh},${r}`;
    let c = this.shapeCache.get(k);
    if (!c) { c = new RAPIER.Capsule(hh, r); this.shapeCache.set(k, c); }
    return c;
  }

  shapeBlocked(x: number, y: number, z: number, hh: number, r: number, exclude?: RAPIER.Collider): boolean {
    let hit = false;
    this.world.intersectionsWithShape(
      { x, y, z }, { x: 0, y: 0, z: 0, w: 1 }, this.capsule(hh, r),
      () => { hit = true; return false; },
      undefined, undefined, exclude,
    );
    return hit;
  }

  /** Sweep a sphere; returns hit distance or null. Hits world AND players (except `exclude`). */
  sphereCast(x: number, y: number, z: number, dx: number, dy: number, dz: number, dist: number, r: number, exclude?: RAPIER.Collider): number | null {
    if (dist <= 0) return null;
    const hit = this.world.castShape(
      { x, y, z }, { x: 0, y: 0, z: 0, w: 1 }, { x: dx, y: dy, z: dz }, this.capsule(0.001, r),
      0, dist, true, undefined, undefined, exclude,
    );
    return hit ? hit.time_of_impact : null;
  }

  /** Find a free standing position near (x,z). */
  findSpawn(x: number, z: number): { x: number; y: number; z: number } {
    const standOff = PLAYER.halfHeight + PLAYER.radius + 0.05;
    for (let ring = 0; ring < 12; ring++) {
      for (let k = 0; k < 8; k++) {
        const a = (k / 8) * Math.PI * 2;
        const px = x + Math.cos(a) * ring * 1.5, pz = z + Math.sin(a) * ring * 1.5;
        const py = this.groundHeight(px, pz) + standOff;
        if (!this.capsuleBlocked(px, py, pz, PLAYER.halfHeight)) return { x: px, y: py, z: pz };
        if (ring === 0) break;
      }
    }
    return { x, y: this.groundHeight(x, z) + standOff + 2, z };
  }

  /** Raycast against world geometry only (camera collision, line of sight). */
  rayWorld(ox: number, oy: number, oz: number, dx: number, dy: number, dz: number, maxDist: number): number | null {
    const hit = this.world.castRay(new RAPIER.Ray({ x: ox, y: oy, z: oz }, { x: dx, y: dy, z: dz }), maxDist, true, undefined, groups(0xffff, GROUP_WORLD));
    return hit ? hit.timeOfImpact : null;
  }
}
