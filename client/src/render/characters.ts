import * as THREE from 'three';
import * as SkeletonUtils from 'three/examples/jsm/utils/SkeletonUtils.js';
import { SHAPE } from '@lw/shared';
import { toLambert, type GameAssets, type GunName } from './assets';
import { GLTFLoader, type GLTF } from 'three/examples/jsm/loaders/GLTFLoader.js';
import { MeshoptDecoder } from 'three/examples/jsm/libs/meshopt_decoder.module.js';

type Clip = 'idle' | 'idle_rifle' | 'walk' | 'walk_fire' | 'run' | 'run_free' | 'jump' | 'fire' | 'crouch_idle' | 'crouch_walk' | 'crouch_fire' | 'death' | 'death_crouch' | 'prone' | 'death_prone';

/** Outfit colours (multiplied over the camo texture). Index 0 = original woodland camo. Chosen in the lobby. */
export const SKINS: { name: string; color?: number; outfit?: string }[] = [
  { name: 'SWAT', outfit: 'swat' },
  { name: 'Agent', outfit: 'tactical' },
  { name: 'Operator', outfit: 'fsb' },
  { name: 'Recon', outfit: 'ksk' },
  { name: 'Woodland', outfit: 'bundeswehr' },
  { name: 'Commando', outfit: 'sfarmy' },
  { name: 'Rifleman', outfit: 'rusoldier' },
  { name: 'Desert', outfit: 'uasoldier' },
  { name: 'Veteran', outfit: 'ww2' },
  { name: 'Ghost', outfit: 'crouchsold' },
  { name: 'Infantry', outfit: 'armyman' },
]

/** Soft bright rim on characters so players stand out from grass, rocks and shade (visible at range). */
/**
 * Empty-handed sprint: the rifle run for legs and body, with the arms swinging from the walk cycle
 * (time-stretched to the run's stride so arms and legs stay in step). Cached per clip set.
 */
const freeRunCache = new WeakMap<THREE.AnimationClip[], THREE.AnimationClip[]>();
function withFreeRun(clips: THREE.AnimationClip[]): THREE.AnimationClip[] {
  const hit = freeRunCache.get(clips);
  if (hit) return hit;
  const run = clips.find((c) => c.name === 'run'), walk = clips.find((c) => c.name === 'walk');
  if (!run || !walk) { freeRunCache.set(clips, clips); return clips; }
  const isArm = (n: string) => /mixamorig(Left|Right)(Shoulder|Arm|ForeArm|Hand)/.test(n);
  const k = run.duration / walk.duration;
  const tracks = [
    ...run.tracks.filter((t) => !isArm(t.name)),
    ...walk.tracks.filter((t) => isArm(t.name)).map((t) => {
      const T = t.constructor as new (n: string, t: ArrayLike<number>, v: ArrayLike<number>) => THREE.KeyframeTrack;
      return new T(t.name, Array.from(t.times, (x) => x * k), t.values);
    }),
  ];
  const out = [...clips, new THREE.AnimationClip('run_free', run.duration, tracks)];
  freeRunCache.set(clips, out);
  return out;
}

const rimmed = new WeakSet<THREE.Material>();
function addRim(mat: THREE.Material) {
  if (rimmed.has(mat)) return; // (material clones do not inherit onBeforeCompile)
  rimmed.add(mat);
  mat.onBeforeCompile = (sh) => {
    sh.fragmentShader = sh.fragmentShader.replace('#include <opaque_fragment>', `
      float rimK = 1.0 - abs(dot(normal, normalize(vViewPosition)));
      outgoingLight += vec3(1.0, 0.93, 0.8) * pow(rimK, 2.2) * 0.55 + diffuseColor.rgb * 0.12;
      #include <opaque_fragment>`);
  };
  mat.customProgramCacheKey = () => 'char-rim';
  mat.needsUpdate = true;
}

const outfitCache = new Map<string, Promise<GLTF | null>>();
const outfitLoader = new GLTFLoader().setMeshoptDecoder(MeshoptDecoder);
/** Loads (once) a real-clothes outfit model. */
export function loadOutfit(name: string): Promise<GLTF | null> {
  let p = outfitCache.get(name);
  if (!p) {
    p = outfitLoader.loadAsync(`/assets/outfit_${name}.glb`).then((g) => {
      g.scene.traverse((o) => {
        const m = o as THREE.SkinnedMesh;
        if (m.isMesh) { m.material = toLambert(m.material as THREE.Material); m.frustumCulled = false; }
      });
      return g;
    }).catch(() => null);
    outfitCache.set(name, p);
  }
  return p;
}

/**
 * Drive another Mixamo rig with the soldier's clips: bone rotations are copied by name; the hips
 * (whose parent "armature" space differs per file) get their rotation re-expressed in the target's
 * armature space and their position rescaled to the target's hip height.
 */
export function retargetClips(src: GLTF, dst: GLTF): THREE.AnimationClip[] {
  const cached = dst.userData.clips as THREE.AnimationClip[] | undefined;
  if (cached) return cached;
  const sH = src.scene.getObjectByName('mixamorigHips')!, dH = dst.scene.getObjectByName('mixamorigHips');
  if (!dH) return src.animations;
  src.scene.updateMatrixWorld(true); dst.scene.updateMatrixWorld(true);
  const Ps = sH.parent!.matrixWorld.clone(), Po = dH.parent!.matrixWorld.clone();
  const PoInv = Po.clone().invert();
  const qs = new THREE.Quaternion(), qo = new THREE.Quaternion(), tmp = new THREE.Vector3();
  Ps.decompose(tmp, qs, new THREE.Vector3()); Po.decompose(tmp, qo, new THREE.Vector3());
  const qCorr = qo.clone().invert().multiply(qs);
  const sRest = sH.getWorldPosition(new THREE.Vector3()), oRest = dH.getWorldPosition(new THREE.Vector3());
  const sFeet = new THREE.Box3().setFromObject(src.scene, true).min.y, oFeet = new THREE.Box3().setFromObject(dst.scene, true).min.y;
  const k = (oRest.y - oFeet) / Math.max(1e-6, sRest.y - sFeet);
  const has = new Set<string>(); dst.scene.traverse((o) => has.add(o.name));
  const q = new THREE.Quaternion(), v = new THREE.Vector3();
  const out = src.animations.map((clip) => {
    const tracks: THREE.KeyframeTrack[] = [];
    for (const t of clip.tracks) {
      const dot = t.name.lastIndexOf('.');
      const bone = t.name.slice(0, dot), prop = t.name.slice(dot + 1);
      if (!has.has(bone) || prop === 'scale') continue;
      const isHips = bone === 'mixamorigHips';
      if (prop === 'position' && !isHips) continue; // bone lengths come from the target rig
      const values = Float32Array.from(t.values);
      if (isHips && prop === 'quaternion') {
        for (let i = 0; i < values.length; i += 4) { q.fromArray(values, i).premultiply(qCorr); q.toArray(values, i); }
      } else if (isHips && prop === 'position') {
        for (let i = 0; i < values.length; i += 3) {
          v.fromArray(values, i).applyMatrix4(Ps).sub(sRest).multiplyScalar(k).add(oRest).applyMatrix4(PoInv);
          v.toArray(values, i);
        }
      }
      const T = t.constructor as new (n: string, t: ArrayLike<number>, v: ArrayLike<number>) => THREE.KeyframeTrack;
      tracks.push(new T(t.name, t.times, values));
    }
    return new THREE.AnimationClip(clip.name, clip.duration, tracks);
  });
  dst.userData.clips = out;
  return out;
}
const LOD_DISTANCE = 14;
/** Speed (m/s) at which each locomotion clip looks right — used to scale playback rate. */
const CLIP_SPEED: Partial<Record<Clip, number>> = { walk: 2.0, walk_fire: 2.6, run: 6.0, run_free: 6.0, crouch_walk: 1.6 };

export interface CharacterState {
  x: number; y: number; z: number; yaw: number;
  speed: number; crouch: boolean; grounded: boolean; prone?: boolean; ball?: boolean;
  firing?: boolean; dead?: boolean; pitch?: number;
  /** false = empty-handed (no weapon looted yet) */
  armed?: boolean;
  /** long guns slung on the back (holstered / not in hand) */
  back?: GunName[];
  /** 0 ground, 1 in plane, 2 free-fall, 3 parachute */
  air?: number;
  /** world horizontal velocity, used for directional locomotion */
  vx?: number; vz?: number;
  /** wants to run (auto-run on): keeps the run animation even when a wall stops you */
  running?: boolean;
}

/**
 * Animated soldier. Origin of `root` is at the feet. Model faces -Z after the internal flip.
 * Shares geometry/material with every other soldier (SkeletonUtils clone).
 */
export class CharacterView {
  readonly root = new THREE.Group();
  private model!: THREE.Object3D;
  private mixer!: THREE.AnimationMixer;
  private actions = new Map<Clip, THREE.AnimationAction>();
  private current: Clip | null = null;
  private body: THREE.SkinnedMesh | null = null;
  private bodyLod: THREE.SkinnedMesh | null = null;
  private gun: THREE.Object3D | null = null;
  /** ground height under any point (set by the game): keeps feet out of slopes */
  static groundAt: ((x: number, z: number) => number) | null = null;
  private baseY = 0;
  private footLift = 0;
  private static readonly FEET = ['LeftToeBase', 'RightToeBase', 'LeftFoot', 'RightFoot'];
  private static readonly FV = new THREE.Vector3();
  /** on a slope the uphill foot sinks into the ground: lift the body by the deepest foot (never lowers it) */
  private fixFeet(st: CharacterState, dt: number) {
    const ground = CharacterView.groundAt;
    let target = 0;
    if (ground && st.grounded && !st.dead && !st.prone && !(st.air ?? 0)) {
      for (const n of CharacterView.FEET) {
        const b = this.bones.get(n); if (!b) continue;
        b.getWorldPosition(CharacterView.FV);
        const raw = CharacterView.FV.y - this.footLift; // where the foot would be without the lift
        target = Math.max(target, ground(CharacterView.FV.x, CharacterView.FV.z) + 0.03 - raw);
      }
      target = Math.min(target, 0.5);
    }
    this.footLift += (target - this.footLift) * Math.min(1, dt * 12);
    this.model.position.y = this.baseY + this.footLift;
  }
  private wasGrounded = true;
  private bodyOffset = 0;
  private airTime = 0;
  private skipFrames = 0;
  private skipAcc = 0;
  readonly radius = 1.2;

  private disposed = false;

  private assets: GameAssets;

  constructor(assets: GameAssets, skin: number, gunName: GunName = 'rifle') {
    this.assets = assets;
    const def = SKINS[((skin % SKINS.length) + SKINS.length) % SKINS.length];
    // real outfits are separate files, loaded on first use; until then the soldier stands in
    this.build(assets.soldier, assets.soldier.animations, def.outfit ? 0xffffff : def.color ?? 0xffffff);
    if (def.outfit) {
      void loadOutfit(def.outfit).then((g) => {
        if (!this.disposed && g) this.build(g, retargetClips(assets.soldier, g), 0xffffff);
      });
    }
    this.setGun(assets, gunName);
  }

  private build(gltf: GLTF, clips: THREE.AnimationClip[], color: number) {
    const keep = this.current;
    if (this.model) { this.mixer.stopAllAction(); this.model.removeFromParent(); }
    this.model = SkeletonUtils.clone(gltf.scene);
    // Normalise height to 1.8 m and put feet at y=0 (measured on the skinned rest pose).
    this.model.updateMatrixWorld(true);
    const box = new THREE.Box3().setFromObject(this.model, true);
    const h = box.max.y - box.min.y;
    const s = 1.8 / h;
    this.model.scale.multiplyScalar(s);
    this.model.position.y = -box.min.y * s;
    this.baseY = this.model.position.y; this.footLift = 0;
    this.model.rotation.y = Math.PI + this.bodyOffset; // asset faces +Z, game forward is -Z
    this.root.add(this.model);

    this.body = this.bodyLod = null;
    this.model.traverse((o) => {
      const sm = o as THREE.SkinnedMesh;
      if (!sm.isSkinnedMesh) return;
      sm.frustumCulled = false;
      if (color !== 0xffffff) {
        const mat = (sm.material as THREE.MeshLambertMaterial).clone();
        mat.color.set(color);
        sm.material = mat;
      }
      if (sm.name.includes('lod')) this.bodyLod = sm; else this.body = sm;
      addRim(sm.material as THREE.Material);
    });
    const lod = this.bodyLod as THREE.SkinnedMesh | null; // (narrowed to null by TS after the traverse callback)
    if (lod) lod.visible = false;

    this.mixer = new THREE.AnimationMixer(this.model);
    this.actions.clear();
    for (const clip of withFreeRun(clips)) {
      const a = this.mixer.clipAction(clip);
      const once = clip.name === 'jump' || clip.name.startsWith('death');
      if (once) { a.setLoop(THREE.LoopOnce, 1); a.clampWhenFinished = true; }
      this.actions.set(clip.name as Clip, a);
    }
    this.handR = this.model.getObjectByName('mixamorigRightHand') ?? null;
    this.handL = this.model.getObjectByName('mixamorigLeftHand') ?? null;
    this.spine = this.model.getObjectByName('mixamorigSpine2') ?? null;
    this.bones.clear();
    this.model.traverse((o) => { if ((o as THREE.Bone).isBone) { const m = /mixamorig:?(\w+)$/.exec(o.name.replace(/[^\w:]/g, '')); if (m) this.bones.set(m[1], o); } });
    this.current = null;
    this.play(keep ?? 'idle_rifle', 0);
    this.mixer.update(0);
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

  private tmpV = new THREE.Vector3();
  private chute: THREE.Group | null = null;
  /** the local player's own canopy is see-through so it never hides the soldier */
  ownView = false;
  /** Original parachute: striped canopy + lines, built once per character on first use. */
  private buildChute() {
    const g = new THREE.Group();
    const cols = [0xd8432e, 0xf2f2f2];
    for (let i = 0; i < 8; i++) {
      const seg = new THREE.Mesh(
        new THREE.SphereGeometry(3.2, 3, 6, (i / 8) * Math.PI * 2, (Math.PI * 2) / 8, 0, Math.PI * 0.42),
        new THREE.MeshLambertMaterial({ color: cols[i % 2], side: THREE.DoubleSide, transparent: this.ownView, opacity: this.ownView ? 0.4 : 1, depthWrite: !this.ownView }),
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

  // ---------------------------------------------------------------- procedural poses
  private bones = new Map<string, THREE.Object3D>();
  private poseW = 0;
  private poseKind: 'kneel' | 'sky' | null = null;
  private static readonly V = [new THREE.Vector3(), new THREE.Vector3(), new THREE.Vector3()];
  private static readonly Q = [new THREE.Quaternion(), new THREE.Quaternion(), new THREE.Quaternion(), new THREE.Quaternion()];
  /** turn `bone` so the segment to `child` points along `dir` (model space: +Z forward, +Y up, +X the body's left), blended by w */
  private aim(bone: string, child: string, dir: [number, number, number], w: number) {
    const b = this.bones.get(bone), c = this.bones.get(child);
    if (!b || !c || !b.parent) return;
    const [v0, v1, v2] = CharacterView.V, [qm, qw, qp, qn] = CharacterView.Q;
    b.getWorldPosition(v0); c.getWorldPosition(v1);
    v1.sub(v0).normalize();
    this.model.getWorldQuaternion(qm);
    v2.set(dir[0], dir[1], dir[2]).normalize().applyQuaternion(qm);
    qn.setFromUnitVectors(v1, v2);
    b.getWorldQuaternion(qw);
    qn.multiply(qw); // new world rotation
    b.parent.getWorldQuaternion(qp);
    qp.invert().multiply(qn);
    b.quaternion.slerp(qp, w);
    b.updateMatrixWorld(true);
  }
  /** after the animation: kneel (one knee on the ground, feet still) or the free-fall spread */
  private applyPose(kind: 'kneel' | 'sky' | null, dt: number) {
    if (kind) this.poseKind = kind;
    this.poseW = THREE.MathUtils.clamp(this.poseW + (kind ? dt : -dt) * 7, 0, 1);
    const w = this.poseW;
    if (w <= 0 || !this.bones.size) return;
    this.model.updateMatrixWorld(true);
    if (this.poseKind === 'kneel') {
      const hips = this.bones.get('Hips'), ul = this.bones.get('RightUpLeg'), kn = this.bones.get('RightLeg');
      if (hips && ul && kn && hips.parent) {
        const [v0, v1] = CharacterView.V;
        const thigh = ul.getWorldPosition(v0).distanceTo(kn.getWorldPosition(v1));
        // hips at kneeling height above the feet (root origin = feet on the ground)
        hips.getWorldPosition(v0); this.root.worldToLocal(v0);
        v0.y += ((thigh * 0.92 + 0.1) - v0.y) * w;
        this.root.localToWorld(v0); hips.parent.worldToLocal(v0);
        hips.position.copy(v0); hips.updateMatrixWorld(true);
      }
      this.aim('RightUpLeg', 'RightLeg', [-0.04, -1, 0.12], w); // right knee down on the ground
      this.aim('RightLeg', 'RightFoot', [0, -0.06, -1], w); // shin flat behind
      this.aim('RightFoot', 'RightToeBase', [0, -0.75, -0.6], w);
      this.aim('LeftUpLeg', 'LeftLeg', [0.12, -0.1, 1], w); // left thigh forward
      this.aim('LeftLeg', 'LeftFoot', [0.02, -1, -0.06], w); // shin upright, foot flat
      this.aim('LeftFoot', 'LeftToeBase', [0.05, -0.3, 1], w);
    } else if (this.poseKind === 'sky') { // belly down, arms in a W, legs spread with the knees bent
      this.aim('LeftArm', 'LeftForeArm', [1, 0.15, 0.1], w);
      this.aim('LeftForeArm', 'LeftHand', [0.35, 1, 0.15], w);
      this.aim('RightArm', 'RightForeArm', [-1, 0.15, 0.1], w);
      this.aim('RightForeArm', 'RightHand', [-0.35, 1, 0.15], w);
      this.aim('LeftUpLeg', 'LeftLeg', [0.4, -1, 0], w);
      this.aim('LeftLeg', 'LeftFoot', [0.25, -0.55, -0.8], w);
      this.aim('RightUpLeg', 'RightLeg', [-0.4, -1, 0], w);
      this.aim('RightLeg', 'RightFoot', [-0.25, -0.55, -0.8], w);
      this.aim('Head', 'HeadTop_End', [0, 0.6, 0.8], w); // look ahead (down at the ground below)
    }
  }

  /** dist = distance to camera (for LOD and animation rate). */
  update(st: CharacterState, dt: number, dist: number, visible: boolean) {
    const air = st.air ?? 0;
    if (air === 1) { this.root.visible = false; return; } // inside the plane
    this.model.rotation.x = air === 2 ? -1.45 : 0; // sky-diving: belly down
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
    else if (st.speed > 5.2 || (st.running && !st.crouch)) clip = st.armed === false ? 'run_free' : 'run'; // empty hands: arms swing freely
    else if (st.armed === false) clip = st.speed > 0.4 ? 'walk' : 'idle'; // empty hands: no rifle pose
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
      // armed: the body turns less (strafing with the gun still pointing ahead keeps it in the hands and in view)
      const lim = st.armed === false ? 1.15 : 0.55;
      if (Math.abs(rel) <= 1.95) targetOffset = THREE.MathUtils.clamp(rel, -lim, lim);
      else { reverse = true; let b = rel - Math.sign(rel) * Math.PI; b = Math.atan2(Math.sin(b), Math.cos(b)); targetOffset = THREE.MathUtils.clamp(b, -lim, lim); }
    }
    let d = targetOffset - this.bodyOffset;
    d = Math.atan2(Math.sin(d), Math.cos(d));
    this.bodyOffset += d * Math.min(1, dt * 10);
    this.model.rotation.y = Math.PI + this.bodyOffset;
    const ref = CLIP_SPEED[clip];
    const act = this.actions.get(clip);
    if (act) act.timeScale = (ref ? THREE.MathUtils.clamp((st.running && (clip === 'run' || clip === 'run_free') ? Math.max(st.speed, ref) : st.speed) / ref, 0.6, clip === 'walk' ? 2.6 : 1.6) : 1) * (reverse ? -1 : 1);

    // LOD + animation throttling for distant / hidden characters
    const far = dist > LOD_DISTANCE;
    if (this.body && this.bodyLod) { this.body.visible = !far; this.bodyLod.visible = far; }
    this.skipFrames = !visible ? 6 : dist > 60 ? 3 : dist > 30 ? 1 : 0;
    this.skipAcc += dt;
    if (this.skipFrames === 0 || Math.random() < 1 / (this.skipFrames + 1)) {
      this.mixer.update(this.skipAcc);
      // kneel when crouched and still; spread-eagle in free fall (close / visible characters only)
      const pose = st.dead || !visible || dist > 70 ? null : air === 2 ? 'sky' : st.crouch && !st.prone && st.speed <= 0.4 && st.grounded ? 'kneel' : null;
      this.applyPose(pose, this.skipAcc);
      if (dist < 80 && visible) { this.root.updateMatrixWorld(true); this.fixFeet(st, this.skipAcc); }
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
    this.placeBackGuns(st);
    if (!gun.visible) return;
    // grip in the right hand, barrel toward the left hand: the gun follows every pose and outfit
    this.root.updateMatrixWorld(true);
    const rh = this.handR ? this.root.worldToLocal(this.handR.getWorldPosition(this.tmpV)) : this.tmpV.set(0.17, st.crouch ? 0.98 : 1.34, -0.32);
    const dir = this.tmpD.set(0, Math.sin(st.pitch ?? 0), -Math.cos(st.pitch ?? 0)); // aim direction (root space)
    if (this.handL) {
      const lh = this.root.worldToLocal(this.handL.getWorldPosition(this.tmpL)).sub(rh);
      const len = lh.length();
      // rifle holds: the support hand is 15-70 cm ahead of the trigger hand
      if (len > 0.15 && len < 0.75 && lh.z < -0.04) {
        lh.divideScalar(len);
        if (this.current === 'run' || st.prone) dir.copy(lh); // carried: exactly along the hands
        // standing / walking / aiming: the barrel follows the view, grip stays in the hand
      }
    }
    gun.position.copy(rh);
    this.tmpM.lookAt(this.zero, dir, this.upV);
    gun.quaternion.setFromRotationMatrix(this.tmpM);
  }

  /** Holstered long guns are slung across the back (the pistol goes in the pocket). */
  private backGuns: THREE.Object3D[] = [];
  private backKey = '';
  private placeBackGuns(st: CharacterState) {
    const list = st.dead ? [] : (st.back ?? []);
    const key = list.join(',');
    if (key !== this.backKey) {
      this.backKey = key;
      this.backGuns.forEach((g) => g.removeFromParent());
      this.backGuns = list.slice(0, 2).map((n) => { const g = this.assets.guns[n].clone(); this.root.add(g); return g; });
    }
    if (!this.backGuns.length || !this.spine) return;
    const sp = this.root.worldToLocal(this.spine.getWorldPosition(this.tmpS));
    this.backGuns.forEach((g, i) => {
      g.position.set(sp.x + (i ? -0.04 : 0.04), sp.y - 0.05, sp.z + 0.2 + i * 0.05);
      g.rotation.set(0, Math.PI / 2, (i ? -1 : 1) * 0.9, 'YXZ'); // diagonal across the back, crossed
      g.rotateY(Math.PI / 2);
    });
  }
  private handR: THREE.Object3D | null = null;
  private handL: THREE.Object3D | null = null;
  private spine: THREE.Object3D | null = null;
  private tmpD = new THREE.Vector3(); private tmpL = new THREE.Vector3(); private tmpS = new THREE.Vector3();
  private tmpM = new THREE.Matrix4(); private zero = new THREE.Vector3(); private upV = new THREE.Vector3(0, 1, 0);

  dispose() {
    this.disposed = true;
    this.root.removeFromParent();
    this.mixer.stopAllAction();
  }
}
