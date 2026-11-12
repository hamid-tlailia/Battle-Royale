import * as THREE from 'three';
import * as SkeletonUtils from 'three/examples/jsm/utils/SkeletonUtils.js';
import { SHAPE } from '@lw/shared';
import type { GameAssets, GunName } from './assets';

type Clip = 'idle' | 'idle_rifle' | 'walk' | 'walk_fire' | 'run' | 'jump' | 'fire' | 'crouch_idle' | 'crouch_walk' | 'crouch_fire' | 'death' | 'death_crouch' | 'prone' | 'death_prone';

/** Outfit colours (multiplied over the camo texture). Index 0 = original woodland camo. Chosen in the lobby. */
export const SKINS = [
  { name: 'Woodland', color: 0xffffff },
  { name: 'Desert', color: 0xffe2b0 },
  { name: 'Arctic', color: 0xdfeeff },
  { name: 'Night', color: 0x8a90a8 },
  { name: 'Jungle', color: 0xbfe8a8 },
  { name: 'Crimson', color: 0xffb0a8 },
  { name: 'Royal', color: 0xc8b8ff },
  { name: 'Gold', color: 0xffe07a },
];
const LOD_DISTANCE = 14;
/** Speed (m/s) at which each locomotion clip looks right — used to scale playback rate. */
const CLIP_SPEED: Partial<Record<Clip, number>> = { walk: 1.6, walk_fire: 2.6, run: 6.0, crouch_walk: 1.6 };

export interface CharacterState {
  x: number; y: number; z: number; yaw: number;
  speed: number; crouch: boolean; grounded: boolean; prone?: boolean; ball?: boolean;
  firing?: boolean; dead?: boolean; pitch?: number;
  /** false = empty-handed (no weapon looted yet) */
  armed?: boolean;
  /** 0 ground, 1 in plane, 2 free-fall, 3 parachute */
  air?: number;
  /** world horizontal velocity, used for directional locomotion */
  vx?: number; vz?: number;
}

/**
 * Animated soldier. Origin of `root` is at the feet. Model faces -Z after the internal flip.
 * Shares geometry/material with every other soldier (SkeletonUtils clone).
 */
export class CharacterView {
  readonly root = new THREE.Group();
  private model: THREE.Object3D;
  private mixer: THREE.AnimationMixer;
  private actions = new Map<Clip, THREE.AnimationAction>();
  private current: Clip | null = null;
  private body: THREE.SkinnedMesh | null = null;
  private bodyLod: THREE.SkinnedMesh | null = null;
  private gun: THREE.Object3D | null = null;
  private wasGrounded = true;
  private bodyOffset = 0;
  private airTime = 0;
  private skipFrames = 0;
  private skipAcc = 0;
  readonly radius = 1.2;

  constructor(assets: GameAssets, skin: number, gunName: GunName = 'rifle') {
    this.model = SkeletonUtils.clone(assets.soldier.scene);
    // Normalise height to 1.8 m and put feet at y=0 (measured on the skinned rest pose).
    this.model.updateMatrixWorld(true);
    const box = new THREE.Box3().setFromObject(this.model, true);
    const h = box.max.y - box.min.y;
    const s = 1.8 / h;
    this.model.scale.multiplyScalar(s);
    this.model.position.y = -box.min.y * s;
    this.model.rotation.y = Math.PI; // asset faces +Z, game forward is -Z
    this.root.add(this.model);

    const skinIdx = ((skin % SKINS.length) + SKINS.length) % SKINS.length;
    const tint = new THREE.Color(SKINS[skinIdx].color);
    this.model.traverse((o) => {
      const sm = o as THREE.SkinnedMesh;
      if (!sm.isSkinnedMesh) return;
      sm.frustumCulled = false;
      if (skinIdx !== 0) {
        const mat = (sm.material as THREE.MeshLambertMaterial).clone();
        mat.color.copy(tint);
        sm.material = mat;
      }
      if (sm.name.includes('lod')) this.bodyLod = sm; else this.body = sm;
    });
    if (this.bodyLod) this.bodyLod.visible = false;

    this.mixer = new THREE.AnimationMixer(this.model);
    for (const clip of assets.soldier.animations) {
      const a = this.mixer.clipAction(clip);
      const once = clip.name === 'jump' || clip.name.startsWith('death');
      if (once) { a.setLoop(THREE.LoopOnce, 1); a.clampWhenFinished = true; }
      this.actions.set(clip.name as Clip, a);
    }
    this.play('idle_rifle', 0);
    this.setGun(assets, gunName);

  }

  setGun(assets: GameAssets, gunName: GunName) {
    if (this.gun) this.gun.removeFromParent();
    // The gun is NOT parented to the hand bone (bone axes differ per animation rig): every frame it is
    // placed at the right hand and aimed along the player's view direction.
    this.gun = assets.guns[gunName].clone();
    this.root.add(this.gun);
    const box = new THREE.Box3().setFromObject(assets.guns[gunName]);
    this.muzzleLocal.set(0, (box.min.y + box.max.y) / 2 + 0.02, box.min.z);
  }
  private muzzleLocal = new THREE.Vector3();

  /** World position of the gun muzzle (falls back to chest height). */
  muzzleWorld(out: THREE.Vector3): THREE.Vector3 {
    if (this.gun && this.gun.visible && this.root.visible) {
      this.gun.updateWorldMatrix(true, false);
      return out.copy(this.muzzleLocal).applyMatrix4(this.gun.matrixWorld);
    }
    return out.set(0, 1.35, -0.5).applyMatrix4(this.root.matrixWorld);
  }

  private play(name: Clip, fade = 0.2) {
    if (this.current === name) return;
    const next = this.actions.get(name);
    if (!next) return;
    next.reset().setEffectiveWeight(1).fadeIn(fade).play();
    if (this.current) this.actions.get(this.current)?.fadeOut(fade);
    this.current = name;
  }

  private chute: THREE.Group | null = null;
  /** Original parachute: striped canopy + lines, built once per character on first use. */
  private buildChute() {
    const g = new THREE.Group();
    const cols = [0xd8432e, 0xf2f2f2];
    for (let i = 0; i < 8; i++) {
      const seg = new THREE.Mesh(
        new THREE.SphereGeometry(3.2, 3, 6, (i / 8) * Math.PI * 2, (Math.PI * 2) / 8, 0, Math.PI * 0.42),
        new THREE.MeshLambertMaterial({ color: cols[i % 2], side: THREE.DoubleSide }),
      );
      seg.scale.set(1, 0.55, 1);
      g.add(seg);
    }
    const pts: THREE.Vector3[] = [];
    for (let i = 0; i < 8; i++) { const a = (i / 8) * Math.PI * 2; pts.push(new THREE.Vector3(0, -4.2, 0), new THREE.Vector3(Math.cos(a) * 2.9, 0.4, Math.sin(a) * 2.9)); }
    g.add(new THREE.LineSegments(new THREE.BufferGeometry().setFromPoints(pts), new THREE.LineBasicMaterial({ color: 0x333333 })));
    g.position.y = 6;
    this.root.add(g);
    return g;
  }

  /** dist = distance to camera (for LOD and animation rate). */
  update(st: CharacterState, dt: number, dist: number, visible: boolean) {
    const air = st.air ?? 0;
    if (air === 1) { this.root.visible = false; return; } // inside the plane
    this.model.rotation.x = air === 2 ? -1.25 : 0; // sky-diving pose
    if (air === 3 && !this.chute) this.chute = this.buildChute();
    if (this.chute) {
      const k = this.chute.scale.x + ((air === 3 ? 1 : 0) - this.chute.scale.x) * Math.min(1, dt * 4);
      this.chute.scale.setScalar(Math.max(0.001, k));
      this.chute.visible = k > 0.02;
    }
    // capsule centre -> feet, for every collision shape (stand / crouch / prone / vault sphere)
    const sh = st.ball ? SHAPE.BALL : st.prone ? SHAPE.PRONE : st.crouch ? SHAPE.CROUCH : SHAPE.STAND;
    const halfTotal = sh.hh + sh.r;
    this.root.position.set(st.x, st.y - halfTotal, st.z);
    this.root.rotation.y = st.yaw;
    this.root.visible = visible;

    // choose clip
    if (!st.grounded) this.airTime += dt; else this.airTime = 0;
    let clip: Clip;
    if (st.dead) clip = st.prone ? 'death_prone' : st.crouch ? 'death_crouch' : 'death';
    else if (st.prone) clip = 'prone';
    else if (air === 2) clip = 'idle';
    else if (air === 3) clip = 'idle';
    else if (this.airTime > 0.15) clip = 'jump';
    else if (st.crouch) clip = st.firing ? 'crouch_fire' : st.speed > 0.4 ? 'crouch_walk' : 'crouch_idle';
    else if (st.speed > 5.2) clip = 'run';
    else if (st.speed > 0.4) clip = 'walk_fire';
    else clip = st.firing ? 'fire' : 'idle_rifle';
    if (clip === 'jump' && this.wasGrounded) this.actions.get('jump')?.reset();
    this.wasGrounded = st.grounded;
    this.play(clip);
    // Directional locomotion: turn the legs/body toward the movement direction (no sideways sliding),
    // play the walk backwards when retreating. The gun keeps pointing at the aim direction.
    let targetOffset = 0, reverse = false;
    if (!st.dead && st.speed > 0.4 && st.vx !== undefined && st.vz !== undefined && clip !== 'jump') {
      const moveYaw = Math.atan2(-st.vx, -st.vz!);
      let rel = moveYaw - st.yaw;
      rel = Math.atan2(Math.sin(rel), Math.cos(rel));
      if (Math.abs(rel) <= 1.95) targetOffset = THREE.MathUtils.clamp(rel, -1.15, 1.15);
      else { reverse = true; let b = rel - Math.sign(rel) * Math.PI; b = Math.atan2(Math.sin(b), Math.cos(b)); targetOffset = THREE.MathUtils.clamp(b, -1.15, 1.15); }
    }
    let d = targetOffset - this.bodyOffset;
    d = Math.atan2(Math.sin(d), Math.cos(d));
    this.bodyOffset += d * Math.min(1, dt * 10);
    this.model.rotation.y = Math.PI + this.bodyOffset;
    const ref = CLIP_SPEED[clip];
    const act = this.actions.get(clip);
    if (act) act.timeScale = (ref ? THREE.MathUtils.clamp(st.speed / ref, 0.6, 1.6) : 1) * (reverse ? -1 : 1);

    // LOD + animation throttling for distant / hidden characters
    const far = dist > LOD_DISTANCE;
    if (this.body && this.bodyLod) { this.body.visible = !far; this.bodyLod.visible = far; }
    this.skipFrames = !visible ? 6 : dist > 60 ? 3 : dist > 30 ? 1 : 0;
    this.skipAcc += dt;
    if (this.skipFrames === 0 || Math.random() < 1 / (this.skipFrames + 1)) {
      this.mixer.update(this.skipAcc);
      this.skipAcc = 0;
    }
    if (visible && this.gun) this.placeGun(st);
  }

  /**
   * The gun is held at a fixed, always-visible spot in front of the right shoulder (not glued to the
   * hand bone, whose pose varies per animation and could bury the gun in the body), aimed with the view.
   */
  private placeGun(st: CharacterState) {
    const gun = this.gun!;
    gun.visible = !st.dead && st.armed !== false;
    if (!gun.visible) return;
    const running = this.current === 'run';
    if (st.prone) {
      gun.position.set(0.12, 0.28, -0.55);
      gun.rotation.set(st.pitch ?? 0, 0, 0, 'YXZ');
    } else {
      const chest = st.crouch ? 0.98 : 1.34;
      if (running) {
        gun.position.set(0.12, chest - 0.12, -0.3);
        gun.rotation.set(-0.35, 0.55, 0.45, 'YXZ'); // carried diagonally while sprinting
      } else {
        gun.position.set(0.17, chest, -0.32);
        gun.rotation.set(st.pitch ?? 0, 0.03, 0, 'YXZ');
      }
    }
  }

  dispose() {
    this.root.removeFromParent();
    this.mixer.stopAllAction();
  }
}
