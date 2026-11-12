import * as THREE from 'three';
import * as SkeletonUtils from 'three/examples/jsm/utils/SkeletonUtils.js';
import { PLAYER } from '@lw/shared';
import type { GameAssets, GunName } from './assets';

type Clip = 'idle' | 'idle_rifle' | 'walk' | 'walk_fire' | 'run' | 'jump' | 'fire' | 'crouch_idle' | 'crouch_walk' | 'crouch_fire' | 'death' | 'death_crouch';

const TINTS = [0xffffff, 0xd8e4ff, 0xffe2c8, 0xd9ffd9, 0xf0d8ff, 0xfff4c0, 0xc8f4ff, 0xffd0d0];
const LOD_DISTANCE = 14;
/** Speed (m/s) at which each locomotion clip looks right — used to scale playback rate. */
const CLIP_SPEED: Partial<Record<Clip, number>> = { walk: 1.6, walk_fire: 1.7, run: 5.2, crouch_walk: 1.6 };

export interface CharacterState {
  x: number; y: number; z: number; yaw: number;
  speed: number; crouch: boolean; grounded: boolean;
  firing?: boolean; dead?: boolean; pitch?: number;
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
  private hand: THREE.Bone | null = null;
  private wasGrounded = true;
  private airTime = 0;
  private skipFrames = 0;
  private skipAcc = 0;
  readonly radius = 1.2;

  constructor(assets: GameAssets, index: number, gunName: GunName = 'rifle') {
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

    const tint = new THREE.Color(TINTS[index % TINTS.length]);
    this.model.traverse((o) => {
      const sm = o as THREE.SkinnedMesh;
      if (!sm.isSkinnedMesh) return;
      sm.frustumCulled = false;
      if (index % TINTS.length !== 0) {
        const mat = (sm.material as THREE.MeshLambertMaterial).clone();
        mat.color.copy(tint);
        sm.material = mat;
      }
      if (sm.name.includes('lod')) this.bodyLod = sm; else this.body = sm;
      const b = sm.skeleton.bones.find((x) => x.name.includes('RightHand'));
      if (b) this.hand = b;
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

  /** dist = distance to camera (for LOD and animation rate). */
  update(st: CharacterState, dt: number, dist: number, visible: boolean) {
    const halfTotal = (st.crouch ? PLAYER.crouchHalfHeight : PLAYER.halfHeight) + PLAYER.radius;
    this.root.position.set(st.x, st.y - halfTotal, st.z);
    this.root.rotation.y = st.yaw;
    this.root.visible = visible;

    // choose clip
    if (!st.grounded) this.airTime += dt; else this.airTime = 0;
    let clip: Clip;
    if (st.dead) clip = st.crouch ? 'death_crouch' : 'death';
    else if (this.airTime > 0.15) clip = 'jump';
    else if (st.crouch) clip = st.firing ? 'crouch_fire' : st.speed > 0.4 ? 'crouch_walk' : 'crouch_idle';
    else if (st.speed > 3.6) clip = 'run';
    else if (st.speed > 0.4) clip = 'walk_fire';
    else clip = st.firing ? 'fire' : 'idle_rifle';
    if (clip === 'jump' && this.wasGrounded) this.actions.get('jump')?.reset();
    this.wasGrounded = st.grounded;
    this.play(clip);
    const ref = CLIP_SPEED[clip];
    const act = this.actions.get(clip);
    if (act) act.timeScale = ref ? THREE.MathUtils.clamp(st.speed / ref, 0.6, 1.6) : 1;

    // LOD + animation throttling for distant / hidden characters
    const far = dist > LOD_DISTANCE;
    if (this.body && this.bodyLod) { this.body.visible = !far; this.bodyLod.visible = far; }
    this.skipFrames = !visible ? 6 : dist > 60 ? 3 : dist > 30 ? 1 : 0;
    this.skipAcc += dt;
    if (this.skipFrames === 0 || Math.random() < 1 / (this.skipFrames + 1)) {
      this.mixer.update(this.skipAcc);
      this.skipAcc = 0;
      if (visible && this.gun && this.hand) this.placeGun(st);
    }
  }

  private tmp = new THREE.Vector3();
  private placeGun(st: CharacterState) {
    const gun = this.gun!;
    gun.visible = !st.dead;
    if (st.dead) return;
    this.root.updateMatrixWorld(true);
    this.hand!.getWorldPosition(this.tmp);
    this.root.worldToLocal(this.tmp);
    gun.position.copy(this.tmp);
    gun.position.y -= 0.02;
    const running = this.current === 'run';
    gun.rotation.set(running ? -0.5 : (st.pitch ?? 0), running ? 0.5 : 0.06, running ? 0.4 : 0, 'YXZ');
  }

  dispose() {
    this.root.removeFromParent();
    this.mixer.stopAllAction();
  }
}
