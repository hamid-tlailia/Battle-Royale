using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace LastWar
{
    /// <summary>
    /// The soldier model of a player or bot: 1.8 m, URP Lit material, the model's 14 clips cross-faded
    /// through a Playables mixer, and the current gun held in the right hand, pointing where the soldier aims.
    /// </summary>
    public class SoldierBody : MonoBehaviour
    {
        Transform model, handR, gun;
        string gunId;
        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        readonly Dictionary<string, int> clipIndex = new Dictionary<string, int>();
        float[] weights;
        string current;
        public Vector3 AimDir = Vector3.forward;
        /// <summary>world position of the barrel end (null without a gun)</summary>
        public Vector3? Muzzle => gun != null ? gun.position + AimDir.normalized * 0.55f : (Vector3?)null;
        static AnimationClip[] clips;

        /// <summary>outfits in the web game's order (SKINS): index = saved skin</summary>
        public static readonly string[] OutfitFiles = { "swat", "tactical", "fsb", "ksk", "bundeswehr", "sfarmy", "rusoldier", "uasoldier", "ww2", "crouchsold", "armyman",
            // Quaternius modular characters (CC0): one rig, so their pieces mix with no gaps
            "q_swat", "q_adventurer", "q_worker", "q_farmer", "q_punk", "q_spacesuit", "q_king", "q_suit", "q_casual", "q_casual2", "q_beach" };
        public static readonly string[] OutfitNames = { "SWAT", "Agent", "Operator", "Recon", "Woodland", "Commando", "Rifleman", "Desert", "Veteran", "Ghost", "Infantry",
            "Tactical", "Ranger", "Worker", "Farmer", "Punk", "Astronaut", "King", "Agent X", "Hoodie", "Casual", "Beach" };

        /// <param name="skin">outfit index (OutfitFiles), or -1 for the plain soldier</param>
        /// <summary>parts: the outfit worn for the head (mask / helmet), torso (shirt), legs (pants) and feet (shoes);
        /// null = the whole of `skin`</summary>
        public static SoldierBody Attach(GameObject owner, int skin = -1, int[] parts = null)
        {
            var body = owner.AddComponent<SoldierBody>();
            body.parts = parts;
            body.Build(skin);
            return body;
        }

        // ---- outfit driven by the (hidden) animated soldier: same mixamo bone names, rest-pose corrected
        Transform outfitRoot;
        readonly List<Transform> srcBones = new List<Transform>(), dstBones = new List<Transform>();
        Transform srcHips, dstHips; Vector3 srcHipsBind, dstHipsBind;

        static string BoneKey(string n) { int c = n.LastIndexOf(':'); return c >= 0 ? n.Substring(c + 1) : n; }

        // ---- mix and match: the torso's outfit is the one the soldier drives (gun hands, poses); the head, legs and feet
        //      may come from other outfits, which copy that outfit's pose every frame
        public static readonly string[] PartNames = { "head", "torso", "legs", "feet" };
        int[] parts;
        readonly HashSet<Renderer> hiddenParts = new HashSet<Renderer>();
        class Extra { public Transform root; public List<Transform> from = new List<Transform>(), to = new List<Transform>(); public Transform hips; public Vector3 hipsOffset; public List<Quaternion> map; }
        List<Quaternion> boneMap = new List<Quaternion>();

        /// <summary>
        /// For each bone pair (both rigs in their rest poses now): the rotation that makes the second bone point where
        /// the first points - to = from * map. The rests may differ (T-pose / A-pose): each bone's direction to its
        /// first paired child is turned onto the other's.
        /// </summary>
        static List<Quaternion> RestMaps(List<Transform> from, List<Transform> to)
        {
            var maps = new List<Quaternion>(from.Count);
            var index = new Dictionary<Transform, int>(); for (int i = 0; i < from.Count; i++) index[from[i]] = i;
            for (int i = 0; i < from.Count; i++)
            {
                Quaternion fix = Quaternion.identity;
                foreach (Transform c in from[i])
                {
                    if (!index.TryGetValue(c, out int ci)) continue;
                    Vector3 a = to[ci].position - to[i].position, b = c.position - from[i].position;
                    if (a.sqrMagnitude > 1e-8f && b.sqrMagnitude > 1e-8f && BoneKey(from[i].name) != "Hips") fix = Quaternion.FromToRotation(a, b);
                    break;
                }
                maps.Add(Quaternion.Inverse(from[i].rotation) * fix * to[i].rotation);
            }
            return maps;
        }
        readonly List<Extra> extras = new List<Extra>();

        /// <summary>an outfit model, 1.8 m tall, feet on the ground; only the pieces in `show` stay visible</summary>
        GameObject PlaceOutfit(int skin, bool[] show, out Dictionary<string, Transform> dst)
        {
            dst = new Dictionary<string, Transform>();
            GameObject src = MapBuilder.Model("outfit_" + OutfitFiles[skin]);
            if (src == null) return null;
            GameObject o = Instantiate(src, transform, false);
            foreach (Transform t in o.GetComponentsInChildren<Transform>(true)) if (t.name.Contains("mixamorig")) dst[BoneKey(t.name)] = t;
            bool hasTop = dst.TryGetValue("HeadTop_End", out Transform head);
            if (!hasTop) dst.TryGetValue("Head", out head);
            if (head != null)
            {
                float foot = float.MaxValue;
                foreach (string f in new[] { "LeftToeBase", "RightToeBase", "LeftFoot", "RightFoot" }) if (dst.TryGetValue(f, out Transform ft)) foot = Mathf.Min(foot, ft.position.y);
                if (foot == float.MaxValue) foot = transform.position.y;
                float h = (head.position.y - foot) / (hasTop ? 1f : 0.89f);
                if (h > 0.001f) { o.transform.localScale *= 1.8f / (h * 1.03f); }
                foot = float.MaxValue;
                foreach (string f in new[] { "LeftToeBase", "RightToeBase", "LeftFoot", "RightFoot" }) if (dst.TryGetValue(f, out Transform ft)) foot = Mathf.Min(foot, ft.position.y);
                if (foot != float.MaxValue) o.transform.position += Vector3.up * (transform.position.y + 0.03f - foot);
            }
            foreach (Renderer r in o.GetComponentsInChildren<Renderer>(true))
            {
                // (split outfits: body_head / body_torso / body_legs / body_feet; an unsplit one shows whole)
                int part = -1; for (int i = 0; i < 4; i++) if (r.name.EndsWith("_" + PartNames[i])) part = i;
                if (part >= 0 && !show[part]) { hiddenParts.Add(r); r.enabled = false; }
            }
            return o;
        }

        void SetupOutfit(int skin)
        {
            if (skin < 0 || skin >= OutfitFiles.Length) return;
            int[] wear = new int[4];
            for (int i = 0; i < 4; i++) wear[i] = parts != null && i < parts.Length && parts[i] >= 0 && parts[i] < OutfitFiles.Length ? parts[i] : skin;
            skin = wear[1]; // the torso's outfit leads
            bool[] mine = new bool[4]; for (int i = 0; i < 4; i++) mine[i] = wear[i] == skin;
            GameObject o = PlaceOutfit(skin, mine, out var dst);
            if (o == null) return;
            outfitRoot = o.transform;
            // the other outfits: only their pieces, posed like the leading one
            var done = new HashSet<int> { skin };
            for (int i = 0; i < 4; i++)
            {
                if (done.Contains(wear[i])) continue;
                done.Add(wear[i]);
                bool[] show = new bool[4]; for (int j = 0; j < 4; j++) show[j] = wear[j] == wear[i];
                GameObject e = PlaceOutfit(wear[i], show, out var ed);
                if (e == null) continue;
                var x = new Extra { root = e.transform };
                foreach (var kv in ed)
                {
                    if (!dst.TryGetValue(kv.Key, out Transform lead)) continue;
                    if (kv.Key == "Hips") { x.hips = kv.Value; x.hipsOffset = transform.InverseTransformVector(kv.Value.position - lead.position); }
                    x.from.Add(lead); x.to.Add(kv.Value);
                }
                extras.Add(x);
            }
            o = outfitRoot.gameObject;
            var allOutfits = new List<GameObject> { o }; foreach (Extra x in extras) allOutfits.Add(x.root.gameObject);
            foreach (GameObject og in allOutfits)
            foreach (Renderer r in og.GetComponentsInChildren<Renderer>(true))
            {
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
                var mats = r.sharedMaterials;
                for (int k = 0; k < mats.Length; k++) mats[k] = Lit(mats[k]);
                r.sharedMaterials = mats;
            }
            foreach (GameObject og in allOutfits) foreach (Animator a in og.GetComponentsInChildren<Animator>(true)) a.enabled = false;
            // preferred: humanoid avatars on both rigs, pose copied through muscle space every frame
            // (humanoid transfer turned the outfits into a hunch: off until the rigs' rest poses are sorted out)
            Avatar srcAv = null, dstAv = null;
            if (UseHumanoid) { srcAv = HumanRig.Build(model); dstAv = HumanRig.Build(outfitRoot); }
            if (srcAv != null && dstAv != null)
            {
                srcPose = new HumanPoseHandler(srcAv, model); dstPose = new HumanPoseHandler(dstAv, outfitRoot);
            }

            // fallback: pair bones by name (the web game's retargetClips): local rotations copy straight across mixamo rigs,
            // the hips get the rest-frame difference of their parents, hips movement is scaled between the bodies
            foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.Contains("mixamorig") || !dst.TryGetValue(BoneKey(t.name), out Transform d)) continue;
                if (BoneKey(t.name) == "Hips")
                {
                    srcHips = t; dstHips = d; srcBones.Add(t); dstBones.Add(d);
                    hipsCorr = Quaternion.Inverse(d.parent.rotation) * t.parent.rotation;
                    if (LogRig) Debug.Log($"[LastWar] rig {OutfitFiles[skin]}: srcHipsW {(Quaternion.Inverse(transform.rotation) * t.rotation).eulerAngles} dstHipsW {(Quaternion.Inverse(transform.rotation) * d.rotation).eulerAngles} srcParentW {(Quaternion.Inverse(transform.rotation) * t.parent.rotation).eulerAngles} dstParentW {(Quaternion.Inverse(transform.rotation) * d.parent.rotation).eulerAngles} srcHipsL {t.localRotation.eulerAngles} dstHipsL {d.localRotation.eulerAngles} corr {hipsCorr.eulerAngles}");
                    srcHipsBind = transform.InverseTransformPoint(t.position); dstHipsBind = transform.InverseTransformPoint(d.position);
                    continue;
                }
                srcBones.Add(t); dstBones.Add(d);
            }
            boneMap = RestMaps(srcBones, dstBones);
            foreach (Extra x in extras) x.map = RestMaps(x.from, x.to);
            // the soldier keeps animating, invisible
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            if (dst.TryGetValue("RightHand", out Transform rh)) handR = rh;
        }

        Quaternion hipsCorr = Quaternion.identity;
        public static bool UseHumanoid = false, LogRig = false;
        HumanPoseHandler srcPose, dstPose; HumanPose pose;
        void Retarget()
        {
            if (outfitRoot == null) return;
            if (srcPose != null)
            {
                srcPose.GetHumanPose(ref pose);
                // body position is relative to each rig's root: keep the outfit where the soldier is
                dstPose.SetHumanPose(ref pose);
                // hips height / travel: the soldier's, scaled (the rigs' roots sit at different heights)
                if (srcHips != null)
                {
                    Vector3 dd = transform.InverseTransformPoint(srcHips.position) - srcHipsBind;
                    float kk = srcHipsBind.y > 0.01f ? dstHipsBind.y / srcHipsBind.y : 1f;
                    dstHips.position = transform.TransformPoint(dstHipsBind + dd * kk);
                }
                return;
            }
            // world space: each outfit bone turns like the soldier's (its own rest pose corrected - A-pose rigs too)
            for (int i = 0; i < srcBones.Count; i++) dstBones[i].rotation = srcBones[i].rotation * boneMap[i];
            if (srcHips != null)
            {
                Vector3 d = transform.InverseTransformPoint(srcHips.position) - srcHipsBind;
                float k = srcHipsBind.y > 0.01f ? dstHipsBind.y / srcHipsBind.y : 1f;
                dstHips.position = transform.TransformPoint(dstHipsBind + d * k);
            }
        }

        void Build(int skin)
        {
            GameObject src = MapBuilder.Model("soldier");
            if (src == null) return;
            GameObject m = Instantiate(src, transform, false);
            model = m.transform;
            Transform head = null;
            foreach (Transform t in m.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.Contains("HeadTop")) head = t;
                if (t.name.EndsWith("RightHand")) handR = t;
            }
            if (head != null) { float h = head.position.y - transform.position.y; if (h > 0.2f) m.transform.localScale *= 1.8f / (h * 1.03f); }
            foreach (SkinnedMeshRenderer smr in m.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                smr.updateWhenOffscreen = true;
                var mats = smr.sharedMaterials;
                for (int k = 0; k < mats.Length; k++) mats[k] = Lit(mats[k]);
                smr.sharedMaterials = mats;
            }
            SetupAnimation(m);
            Play("idle");
            SetupOutfit(skin);
        }

        public static Material Lit(Material src) => MapBuilder.LitFrom(src);

        void SetupAnimation(GameObject m)
        {
            Animator animator = m.GetComponentInChildren<Animator>();
            if (animator == null) animator = m.AddComponent<Animator>();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; // keeps driving an outfit even though its own mesh is hidden
            if (clips == null) clips = Resources.LoadAll<AnimationClip>("Models/soldier");
            if (clips.Length == 0) return;
            graph = PlayableGraph.Create("soldier");
            mixer = AnimationMixerPlayable.Create(graph, clips.Length);
            for (int k = 0; k < clips.Length; k++)
            {
                var cp = AnimationClipPlayable.Create(graph, clips[k]);
                graph.Connect(cp, 0, mixer, k);
                string n = clips[k].name; int bar = n.LastIndexOf('|'); if (bar >= 0) n = n.Substring(bar + 1);
                clipIndex[n] = k;
            }
            weights = new float[clips.Length];
            // natural human locomotion (Quaternius' Universal Animation Library, retargeted onto the soldier): the run is
            // the sprint, the empty-handed walk the relaxed walk; the old run stays for the arms that carry a gun
            if (clipIndex.TryGetValue("run", out int oldRun) && clipIndex.TryGetValue("sprint_ual", out int sprint)) { carryClip = clips[oldRun]; carryLen = carryClip.length; clipIndex["run"] = sprint; }
            if (clipIndex.TryGetValue("walk_ual", out int walkU)) clipIndex["walk"] = walkU;
            // the web game's run_free: the run clip's legs with the walk clip's arms (swinging at the sides),
            // as a second layer that only moves the shoulders, arms and hands
            layers = AnimationLayerMixerPlayable.Create(graph, 2);
            graph.Connect(mixer, 0, layers, 0); layers.SetInputWeight(0, 1f);
            if (clipIndex.TryGetValue("walk", out int wi))
            {
                walkArms = AnimationClipPlayable.Create(graph, clips[wi]); walkArms.SetSpeed(0);
                graph.Connect(walkArms, 0, layers, 1); layers.SetInputWeight(1, 0f);
                var mask = new AvatarMask(); mask.AddTransformPath(animator.transform, true);
                for (int i = 0; i < mask.transformCount; i++)
                {
                    string p = mask.GetTransformPath(i); int sl = p.LastIndexOf('/'); string last = sl >= 0 ? p.Substring(sl + 1) : p;
                    mask.SetTransformActive(i, last.Contains("Shoulder") || last.Contains("Arm") || last.Contains("Hand"));
                }
                layers.SetLayerMaskFromAvatarMask(1, mask);
                walkLen = clips[wi].length; walkClip = clips[wi]; animRoot = animator.gameObject;
                foreach (Transform t in animator.GetComponentsInChildren<Transform>(true))
                {
                    if (!t.name.Contains("mixamorig")) continue;
                    string last = BoneKey(t.name);
                    if (last.Contains("Shoulder") || last.Contains("Arm") || last.Contains("Hand")) armBones.Add(t); else bodyBones.Add(t);
                }
            }
            AnimationPlayableOutput.Create(graph, "out", animator).SetSourcePlayable(layers);
            graph.Play();
        }

        public void Play(string clip)
        {
            if (clip == current || !clipIndex.ContainsKey(clip)) return;
            current = clip;
            // restart one-shot clips (jump, death) from the beginning
            var cp = (AnimationClipPlayable)mixer.GetInput(clipIndex[clip]);
            if (OneShot(clip)) { cp.SetTime(0); cp.SetSpeed(1); }
        }

        static bool OneShot(string clip) => clip.StartsWith("death") || clip == "jump";

        public void SetGun(string id)
        {
            if (id == gunId) return;
            gunId = id;
            if (gun != null) Destroy(gun.gameObject);
            gun = null;
            if (string.IsNullOrEmpty(id)) return;
            GameObject src = MapBuilder.Model("gun_" + id);
            if (src == null) return;
            gun = Instantiate(src, transform, false).transform;
            foreach (Renderer r in gun.GetComponentsInChildren<Renderer>(true)) { var mats = r.sharedMaterials; for (int k = 0; k < mats.Length; k++) mats[k] = Lit(mats[k]); r.sharedMaterials = mats; r.enabled = visible; }
        }

        AnimationLayerMixerPlayable layers; AnimationClipPlayable walkArms; float walkLen, freeW;
        AnimationClip walkClip, carryClip; float carryLen; GameObject animRoot;
        readonly List<Transform> armBones = new List<Transform>(), bodyBones = new List<Transform>();
        Vector3[] keepPos; Quaternion[] keepRot, runArm;

        /// <summary>
        /// The web game's run_free: the run's legs and body with the walk's arms (swinging at the sides). The walk pose is
        /// sampled at the run's phase, then everything but the arms is put back.
        /// </summary>
        void FreeArms()
        {
            if (freeW <= 0f || carryClip == null || animRoot == null) return;
            int nb = bodyBones.Count, na = armBones.Count;
            if (keepPos == null) { keepPos = new Vector3[nb]; keepRot = new Quaternion[nb]; runArm = new Quaternion[na]; }
            for (int i = 0; i < nb; i++) { keepPos[i] = bodyBones[i].localPosition; keepRot[i] = bodyBones[i].localRotation; }
            for (int i = 0; i < na; i++) runArm[i] = armBones[i].localRotation;
            carryClip.SampleAnimation(animRoot, ClipPhase() * carryLen); // the gun-carrying arms of the old run
            for (int i = 0; i < nb; i++) { bodyBones[i].localPosition = keepPos[i]; bodyBones[i].localRotation = keepRot[i]; }
            for (int i = 0; i < na; i++) armBones[i].localRotation = Quaternion.Slerp(runArm[i], armBones[i].localRotation, freeW);
        }

        /// <summary>
        /// A running human, not a machine: the upper body leans into the run, the chest swings against the hips with
        /// every stride, the shoulders roll and the head stays steady. Layered on top of the run clip.
        /// </summary>
        void RunLife()
        {
            runW = Mathf.MoveTowards(runW, carryClip == null && current == "run" && Tilt == 0 && Pose != "crawl" ? 1f : 0f, Time.deltaTime * 5f); // (only the old robotic run needs this: the real sprint moves the body itself)
            if (runW <= 0f) return;
            float ph = ClipPhase() * Mathf.PI * 2f, sw = Mathf.Sin(ph), bob = Mathf.Abs(Mathf.Sin(ph));
            Vector3 up = transform.up, right = transform.right;
            Transform hips = PoseBone("Hips"), sp = PoseBone("Spine"), sp1 = PoseBone("Spine1"), sp2 = PoseBone("Spine2"), neck = PoseBone("Neck"), head = PoseBone("Head");
            float k = runW;
            // hips turn with the leading leg, the chest the other way (the body's natural counter-swing)
            if (hips != null) hips.rotation = Quaternion.AngleAxis(sw * 7f * k, up) * hips.rotation;
            if (sp != null) sp.rotation = Quaternion.AngleAxis((-sw * 6f) * k, up) * Quaternion.AngleAxis((6f + bob * 2f) * k, right) * sp.rotation;
            if (sp1 != null) sp1.rotation = Quaternion.AngleAxis((-sw * 5f) * k, up) * Quaternion.AngleAxis(4f * k, right) * sp1.rotation;
            if (sp2 != null) sp2.rotation = Quaternion.AngleAxis((-sw * 4f) * k, up) * Quaternion.AngleAxis(Mathf.Sin(ph * 2f) * 2f * k, transform.forward) * sp2.rotation;
            // the head looks ahead: take back most of the lean and the swing
            if (neck != null) neck.rotation = Quaternion.AngleAxis(sw * 8f * k, up) * Quaternion.AngleAxis(-7f * k, right) * neck.rotation;
            if (head != null) head.rotation = Quaternion.AngleAxis(-5f * k, right) * head.rotation;
        }
        float runW;

        /// <summary>no feet inside the ground: lift the body when a toe is below the ground under it (web fixFeet)</summary>
        void FixFeet()
        {
            if (!visible || Tilt != 0 || Pose == "sky" || Pose == "chute" || Pose == "crawl" || current == null || current == "prone" || current.StartsWith("death") || current == "jump") return;
            Transform hips = outfitRoot != null ? dstHips : null;
            if (hips == null) { Transform h = PoseBone("Hips"); hips = h; }
            if (hips == null) return;
            int mask = ~(1 << MapBuilder.PlayerLayer);
            float lift = 0f;
            foreach (string toe in new[] { "LeftToeBase", "RightToeBase" })
            {
                Transform t = PoseBone(toe); if (t == null) continue;
                Vector3 p = t.position;
                if (Physics.Raycast(p + Vector3.up * 0.6f, Vector3.down, out RaycastHit hit, 1.2f, mask, QueryTriggerInteraction.Ignore))
                    lift = Mathf.Max(lift, hit.point.y + 0.03f - p.y);
            }
            if (lift > 0.005f) hips.position += Vector3.up * Mathf.Min(lift, 0.45f);
        }
        void Update()
        {
            if (weights == null || current == null) return;
            // free arms while running empty-handed: walk arms in step with the run (the walk's cycle stretched to the run's)
            freeW = Mathf.MoveTowards(freeW, Armed && gun != null && current == "run" ? 1f : 0f, Time.deltaTime * 6f); // empty hands swing free with the sprint
            if (walkArms.IsValid()) layers.SetInputWeight(1, 0f); // (the arms are copied from the walk clip in LateUpdate instead)
            int target = clipIndex[current];
            // one-shot clips (jump, death) stop on their last frame instead of looping
            if (OneShot(current))
            {
                var cp = (AnimationClipPlayable)mixer.GetInput(target);
                float len = clips[target].length - 0.02f;
                if (cp.GetTime() >= len) { cp.SetTime(len); cp.SetSpeed(0); }
            }
            else
            {
                // locomotion follows the real speed; backwards (retreating) the clip is stepped by hand
                var cp = (AnimationClipPlayable)mixer.GetInput(target);
                if (Rate >= 0) cp.SetSpeed(Rate);
                else
                {
                    cp.SetSpeed(0);
                    double len = clips[target].length, t = cp.GetTime() + Rate * Time.deltaTime;
                    if (len > 0.01) { t %= len; if (t < 0) t += len; }
                    cp.SetTime(t);
                }
            }
            float sum = 0, dt = Time.deltaTime;
            for (int k = 0; k < weights.Length; k++) { weights[k] = Mathf.MoveTowards(weights[k], k == target ? 1f : 0f, dt * 6f); sum += weights[k]; }
            for (int k = 0; k < weights.Length; k++) mixer.SetInputWeight(k, sum > 0 ? weights[k] / sum : 0);
        }

        // ---- sky-dive pose, visibility, parachute
        /// <summary>lean forward in degrees around the hips (free fall: ~83 = belly down)</summary>
        public float Tilt;
        /// <summary>body turned toward the movement direction, degrees to the right of where the soldier looks</summary>
        public float Turn;
        /// <summary>speed of the looping clip (1 = as recorded, negative = backwards)</summary>
        public float Rate = 1f;
        /// <summary>false = empty hands (guns put away)</summary>
        public bool Armed = true;
        float tiltNow; Vector3 outfitBase, modelBase; Quaternion outfitBaseRot, modelBaseRot; bool baseSet;
        Transform chute; float chuteK;
        bool visible = true;

        public void SetVisible(bool v)
        {
            if (v == visible) return;
            visible = v;
            ApplyVisible();
        }

        void ApplyVisible()
        {
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
                r.enabled = visible && !hiddenParts.Contains(r) && (outfitRoot == null || model == null || !r.transform.IsChildOf(model));
        }

        /// <summary>open (true) or fold (false) the parachute: striped red / white canopy with lines</summary>
        public void SetChute(bool open)
        {
            if (open && chute == null) chute = BuildChute();
            if (chute == null) return;
            chuteK = Mathf.MoveTowards(chuteK, open ? 1 : 0, Time.deltaTime * 4f);
            chute.localScale = Vector3.one * Mathf.Max(0.001f, chuteK);
            chute.gameObject.SetActive(chuteK > 0.02f);
        }

        Transform BuildChute()
        {
            var g = new GameObject("Chute").transform; g.SetParent(transform, false); g.localPosition = Vector3.up * 3.6f;
            Material red = MapBuilder.NewLit(new Color(0.85f, 0.26f, 0.18f)), white = MapBuilder.NewLit(new Color(0.95f, 0.95f, 0.95f));
            red.SetFloat("_Cull", 0f); white.SetFloat("_Cull", 0f);
            for (int i = 0; i < 8; i++)
            {
                // one gore of a flattened dome (radius 3.2 m)
                var mesh = new Mesh(); var v = new List<Vector3>(); var tri = new List<int>();
                const int seg = 4, ring = 6;
                for (int r = 0; r <= ring; r++) for (int sgi = 0; sgi <= seg; sgi++)
                    {
                        float th = r / (float)ring * Mathf.PI * 0.42f, ph = (i + sgi / (float)seg) / 8f * Mathf.PI * 2f;
                        v.Add(new Vector3(Mathf.Sin(th) * Mathf.Cos(ph) * 3.2f, Mathf.Cos(th) * 3.2f * 0.55f, Mathf.Sin(th) * Mathf.Sin(ph) * 3.2f));
                    }
                for (int r = 0; r < ring; r++) for (int sgi = 0; sgi < seg; sgi++)
                    {
                        int a0 = r * (seg + 1) + sgi, b0 = a0 + seg + 1;
                        tri.AddRange(new[] { a0, b0, a0 + 1, a0 + 1, b0, b0 + 1 });
                    }
                mesh.SetVertices(v); mesh.SetTriangles(tri, 0); mesh.RecalculateNormals();
                var part = new GameObject("gore", typeof(MeshFilter), typeof(MeshRenderer));
                part.transform.SetParent(g, false); part.GetComponent<MeshFilter>().sharedMesh = mesh;
                part.GetComponent<MeshRenderer>().sharedMaterial = i % 2 == 0 ? red : white;
            }
            // lines from the harness to the rim
            Material line = MapBuilder.NewLit(new Color(0.2f, 0.2f, 0.2f));
            Mesh cyl = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
            for (int i = 0; i < 8; i++)
            {
                float a = i / 8f * Mathf.PI * 2f;
                Vector3 top = new Vector3(Mathf.Cos(a) * 2.9f, 0.4f, Mathf.Sin(a) * 2.9f), bottom = new Vector3((i % 2 == 0 ? 0.22f : -0.22f), -1.55f, 0.1f); // down to the raised hands (~2 m)
                var l = new GameObject("line", typeof(MeshFilter), typeof(MeshRenderer)).transform;
                l.SetParent(g, false); l.localPosition = (top + bottom) / 2f; l.localRotation = Quaternion.FromToRotation(Vector3.up, top - bottom);
                l.localScale = new Vector3(0.025f, (top - bottom).magnitude / 2f, 0.025f);
                l.GetComponent<MeshFilter>().sharedMesh = cyl; l.GetComponent<MeshRenderer>().sharedMaterial = line;
            }
            return g;
        }

        /// <summary>lean (free fall) and turn (walking sideways) of the whole model around the hips, on top of its own root turn</summary>
        void ApplyRoot()
        {
            tiltNow = Mathf.MoveTowards(tiltNow, Tilt, Time.deltaTime * 240f);
            Vector3 pivot = Vector3.up * 0.95f;
            // the models' own root turn (glTF up axis, Sketchfab Z-up roots) is kept: lean and turn go on top of it
            if (!baseSet)
            {
                modelBase = model.localPosition; modelBaseRot = model.localRotation;
                if (outfitRoot != null) { outfitBase = outfitRoot.localPosition; outfitBaseRot = outfitRoot.localRotation; }
                baseSet = true;
            }
            Quaternion q = Quaternion.Euler(tiltNow, 0, 0), ry = Quaternion.Euler(0, Turn, 0);
            model.localRotation = ry * q * modelBaseRot; model.localPosition = ry * (pivot - q * (pivot - modelBase));
            if (outfitRoot != null) { outfitRoot.localRotation = ry * q * outfitBaseRot; outfitRoot.localPosition = ry * (pivot - q * (pivot - outfitBase)); }
        }

        // ---- procedural poses on top of the animation (the web game's applyPose)
        /// <summary>"sky" = free fall, belly down with the arms in a W and the legs spread; "chute" = hanging upright, hands up on the lines; null = none</summary>
        public string Pose;
        /// <summary>holding a snowball (aiming the ice wall)</summary>
        public bool Snowball; Transform snowball;
        /// <summary>false: the gun stays where the clip's hand holds it (the lobby's display pose)</summary>
        public bool ShoulderGrip = true;
        string poseKind; float poseW;
        Dictionary<string, Transform> poseBones;

        Transform PoseBone(string name)
        {
            if (poseBones == null)
            {
                poseBones = new Dictionary<string, Transform>();
                Transform root = outfitRoot != null ? outfitRoot : model;
                if (root != null) foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) if (t.name.Contains("mixamorig") && !poseBones.ContainsKey(BoneKey(t.name))) poseBones[BoneKey(t.name)] = t;
            }
            return poseBones.TryGetValue(name, out Transform b) ? b : null;
        }

        /// <summary>turn `bone` so the segment to `child` points along `dir` (body space as in the web game: +Z forward, +Y up, +X the body's left)</summary>
        void Aim(string bone, string child, float x, float y, float z, float w)
        {
            Transform b = PoseBone(bone), c = PoseBone(child);
            if (b == null || c == null) return;
            Vector3 cur = c.position - b.position;
            if (cur.sqrMagnitude < 1e-8f) return;
            Vector3 want = transform.rotation * Quaternion.Euler(0, Turn, 0) * Quaternion.Euler(tiltNow, 0, 0) * new Vector3(-x, y, z);
            Quaternion q = Quaternion.FromToRotation(cur.normalized, want.normalized) * b.rotation;
            b.rotation = Quaternion.Slerp(b.rotation, q, w);
        }

        void ApplyPose()
        {
            if (Pose != null) poseKind = Pose;
            poseW = Mathf.Clamp01(poseW + (Pose != null ? 1 : -1) * Time.deltaTime * 7f);
            float w = poseW;
            if (w <= 0 || poseKind == null) return;
            if (poseKind == "sky")
            {
                Aim("LeftArm", "LeftForeArm", 1, 0.15f, 0.1f, w);
                Aim("LeftForeArm", "LeftHand", 0.35f, 1, 0.15f, w);
                Aim("RightArm", "RightForeArm", -1, 0.15f, 0.1f, w);
                Aim("RightForeArm", "RightHand", -0.35f, 1, 0.15f, w);
                Aim("LeftUpLeg", "LeftLeg", 0.4f, -1, 0, w);
                Aim("LeftLeg", "LeftFoot", 0.25f, -0.55f, -0.8f, w);
                Aim("RightUpLeg", "RightLeg", -0.4f, -1, 0, w);
                Aim("RightLeg", "RightFoot", -0.25f, -0.55f, -0.8f, w);
                Aim("Head", "HeadTop_End", 0, 0.6f, 0.8f, w); // look ahead (down at the ground below)
            }
            else if (poseKind == "chute")
            {
                // upright under the canopy, both hands up on the risers, legs hanging a little forward
                Aim("LeftArm", "LeftForeArm", 0.55f, 1, 0.1f, w);
                Aim("LeftForeArm", "LeftHand", -0.12f, 1, 0.12f, w);
                Aim("RightArm", "RightForeArm", -0.55f, 1, 0.1f, w);
                Aim("RightForeArm", "RightHand", 0.12f, 1, 0.12f, w);
                Aim("LeftUpLeg", "LeftLeg", 0.1f, -1, 0.22f, w);
                Aim("LeftLeg", "LeftFoot", 0.05f, -1, -0.12f, w);
                Aim("RightUpLeg", "RightLeg", -0.1f, -1, 0.22f, w);
                Aim("RightLeg", "RightFoot", -0.05f, -1, -0.12f, w);
            }
            else if (poseKind == "kneel")
            {
                // the web game's kneel: right knee on the ground, shin flat behind; left thigh forward, shin upright, feet still
                Transform hips = PoseBone("Hips"), ul = PoseBone("RightUpLeg"), kn = PoseBone("RightLeg");
                if (hips != null && ul != null && kn != null)
                {
                    float thigh = Vector3.Distance(ul.position, kn.position);
                    Vector3 hp = transform.InverseTransformPoint(hips.position);
                    hp.y += (thigh * 0.92f + 0.1f - hp.y) * w; // hips at kneeling height above the feet
                    hips.position = transform.TransformPoint(hp);
                }
                Aim("RightUpLeg", "RightLeg", -0.04f, -1, 0.12f, w);
                Aim("RightLeg", "RightFoot", 0, -0.06f, -1, w);
                Aim("RightFoot", "RightToeBase", 0, -0.75f, -0.6f, w);
                Aim("LeftUpLeg", "LeftLeg", 0.12f, -0.1f, 1, w);
                Aim("LeftLeg", "LeftFoot", 0.02f, -1, -0.06f, w);
                Aim("LeftFoot", "LeftToeBase", 0.05f, -0.3f, 1, w);
            }
            else if (poseKind == "crawl")
            {
                // crawling on the belly: one elbow reaches forward while the other pulls back, the opposite knee draws
                // up to the side and pushes; the body moves only as far as the limbs pull it
                // about one full cycle per 1.3 m (two pulls), eased so each reach lingers like a real crawl
                crawlPhase += Time.deltaTime * Mathf.Clamp(Speed, 0.5f, 1.2f) * 0.9f; // calm, steady strokes
                float raw = Mathf.Sin(crawlPhase * Mathf.PI * 2f);
                float a = Mathf.Sign(raw) * Mathf.SmoothStep(0, 1, Mathf.Abs(raw)), l = Mathf.Max(0, a), r = Mathf.Max(0, -a);
                // the limbs move the body: it slides forward only while an elbow / knee pulls back (|sin| falling) and
                // stays put while they reach forward again (same average speed as before, all of it in the pulls)
                float pullNow = Mathf.Max(0f, -Mathf.Sign(raw) * Mathf.Cos(crawlPhase * Mathf.PI * 2f));
                CrawlPull = 0.06f + 2.15f * pullNow;
                Aim("LeftArm", "LeftForeArm", Mathf.Lerp(0.55f, 0.3f, l), -0.25f, Mathf.Lerp(0.15f, 1f, l), w);
                Aim("LeftForeArm", "LeftHand", Mathf.Lerp(-0.25f, 0.05f, l), -0.35f, 1, w);
                Aim("RightArm", "RightForeArm", -Mathf.Lerp(0.55f, 0.3f, r), -0.25f, Mathf.Lerp(0.15f, 1f, r), w);
                Aim("RightForeArm", "RightHand", -Mathf.Lerp(-0.25f, 0.05f, r), -0.35f, 1, w);
                // knees: the leg opposite the reaching arm bends up to the side
                Aim("RightUpLeg", "RightLeg", -Mathf.Lerp(0.12f, 0.75f, l), -0.02f, Mathf.Lerp(-1f, -0.45f, l), w);
                Aim("RightLeg", "RightFoot", -Mathf.Lerp(0.05f, -0.25f, l), Mathf.Lerp(0.08f, 0.3f, l), -1, w); // (feet lie on the ground, not in it)
                Aim("LeftUpLeg", "LeftLeg", Mathf.Lerp(0.12f, 0.75f, r), -0.02f, Mathf.Lerp(-1f, -0.45f, r), w);
                Aim("LeftLeg", "LeftFoot", Mathf.Lerp(0.05f, -0.25f, r), Mathf.Lerp(0.08f, 0.3f, r), -1, w);
            }
        }

        /// <summary>how far through its loop the current clip is (0..1)</summary>
        float ClipPhase()
        {
            if (current == null || !clipIndex.TryGetValue(current, out int i)) return 0;
            double len = clips[i].length; if (len < 0.01) return 0;
            double t = ((AnimationClipPlayable)mixer.GetInput(i)).GetTime() % len; if (t < 0) t += len;
            return (float)(t / len);
        }
        float crawlPhase;
        /// <summary>crawling: how much of the crawl speed this moment of the stroke gives (0.3 .. 1)</summary>
        public float CrawlPull = 1f;
        /// <summary>ground speed (m/s), for the crawl cycle</summary>
        public float Speed;

        // ---- guns slung across the back (long guns that are not in the hands)
        readonly List<Transform> backGuns = new List<Transform>(); string backKey = "";
        public void SetBack(List<string> ids)
        {
            string key = ids == null ? "" : string.Join(",", ids);
            if (key == backKey) return;
            backKey = key;
            foreach (Transform g in backGuns) if (g != null) Destroy(g.gameObject);
            backGuns.Clear();
            if (ids == null) return;
            for (int i = 0; i < ids.Count && i < 2; i++)
            {
                GameObject src = MapBuilder.Model("gun_" + ids[i]);
                if (src == null) continue;
                Transform g = Instantiate(src, transform, false).transform;
                foreach (Renderer r in g.GetComponentsInChildren<Renderer>(true)) { var mats = r.sharedMaterials; for (int k = 0; k < mats.Length; k++) mats[k] = Lit(mats[k]); r.sharedMaterials = mats; r.enabled = visible; }
                backGuns.Add(g);
            }
        }

        // ---- the backpack worn on the back (its level's model: straps against the body)
        Transform bagLook; int bagLvShown; Quaternion bagSpineOffset; bool bagOffsetSet;
        public void SetBag(int lv)
        {
            if (lv == bagLvShown) return;
            bagLvShown = lv;
            if (bagLook != null) Destroy(bagLook.gameObject);
            bagLook = null;
            if (lv <= 0) return;
            GameObject src = MapBuilder.Model("bag" + lv);
            if (src == null) return;
            bagLook = Instantiate(src, transform, false).transform;
            bagLook.localScale = Vector3.one * 0.85f;
            foreach (Renderer r in bagLook.GetComponentsInChildren<Renderer>(true)) { var mats = r.sharedMaterials; for (int k = 0; k < mats.Length; k++) mats[k] = Lit(mats[k]); r.sharedMaterials = mats; r.enabled = visible; }
        }

        void PlaceBag()
        {
            if (bagLook == null) return;
            Transform spine = PoseBone("Spine2") ?? PoseBone("Spine1") ?? PoseBone("Spine");
            if (spine == null) return;
            // follows the chest in every pose (standing, running, crawling): the chest's turn from the first frame
            if (!bagOffsetSet) { bagOffsetSet = true; bagSpineOffset = Quaternion.Inverse(spine.rotation) * transform.rotation; }
            Quaternion bodyRot = spine.rotation * bagSpineOffset;
            bagLook.position = spine.position + bodyRot * new Vector3(0f, -0.38f, -0.13f);
            bagLook.rotation = bodyRot * Quaternion.Euler(0, 180f, 0); // pockets out, straps on the back
        }

        void PlaceBack()
        {
            PlaceBag();
            if (backGuns.Count == 0) return;
            Transform spine = PoseBone("Spine2") ?? PoseBone("Spine1") ?? PoseBone("Spine");
            if (spine == null) return;
            Quaternion bodyRot = transform.rotation * Quaternion.Euler(0, Turn, 0) * Quaternion.Euler(tiltNow, 0, 0);
            for (int i = 0; i < backGuns.Count; i++)
            {
                float side = i == 0 ? 1f : -1f;
                // diagonal across the back, the two crossed, flat against the body
                backGuns[i].position = spine.position + bodyRot * new Vector3(-side * 0.04f, -0.05f, -(0.17f + (bagLook != null ? 0.26f : 0f) + i * 0.05f)); // (behind the backpack when there is one)
                Vector3 barrel = bodyRot * new Vector3(side * 0.62f, 0.78f, 0f);
                backGuns[i].rotation = Quaternion.LookRotation(-barrel, bodyRot * Vector3.back);
            }
        }

        // ---- support hand on the gun (two-bone IK on the left arm)
        static float GripAhead(string id) => id.StartsWith("pistol") ? 0.03f : id.StartsWith("smg") ? 0.2f : id.StartsWith("sniper") ? 0.34f : 0.3f;

        void TwoBone(Transform up, Transform mid, Transform end, Vector3 target)
        {
            float a = (mid.position - up.position).magnitude, b = (end.position - mid.position).magnitude;
            if (a < 1e-4f || b < 1e-4f) return;
            float d = Mathf.Clamp((target - up.position).magnitude, Mathf.Abs(a - b) + 0.01f, a + b - 0.005f);
            // elbow angle from the triangle (law of cosines), bent in the plane it is already in
            Vector3 toUp = up.position - mid.position, toEnd = end.position - mid.position;
            float cur = Vector3.Angle(toUp, toEnd);
            float want = Mathf.Acos(Mathf.Clamp((a * a + b * b - d * d) / (2f * a * b), -1f, 1f)) * Mathf.Rad2Deg;
            Vector3 axis = Vector3.Cross(toUp, toEnd);
            if (axis.sqrMagnitude < 1e-8f) axis = Vector3.Cross(toUp, transform.up);
            mid.rotation = Quaternion.AngleAxis(want - cur, axis.normalized) * mid.rotation;
            up.rotation = Quaternion.FromToRotation(end.position - up.position, target - up.position) * up.rotation;
        }

        void LateUpdate()
        {
            if (model != null) ApplyRoot();
            FreeArms();
            Retarget();
            FixFeet();
            ApplyPose();
            RunLife();
            SyncExtras();
            PlaceBack();
            // aiming the ice wall: a small snowball in the right hand instead of the gun
            if (Snowball && handR != null)
            {
                if (snowball == null)
                {
                    snowball = GameObject.CreatePrimitive(PrimitiveType.Sphere).transform; Destroy(snowball.GetComponent<Collider>());
                    snowball.name = "snowball"; snowball.localScale = Vector3.one * 0.13f;
                    snowball.GetComponent<Renderer>().sharedMaterial = MapBuilder.NewLit(new Color(.82f, .94f, 1f));
                }
                snowball.gameObject.SetActive(visible);
                snowball.position = handR.position + transform.forward * 0.06f;
            }
            else if (snowball != null) snowball.gameObject.SetActive(false);
            // gun: grip in the right hand, barrel (model -Z) along the aim
            if (gun == null || handR == null) return;
            gun.position = handR.position;
            Vector3 dir = AimDir.sqrMagnitude > 1e-6f ? AimDir.normalized : transform.forward;
            Transform handL = PoseBone("LeftHand");
            bool carried = current == "run" || current == "prone" || Pose == "crawl";
            // standing / walking / firing: the stock sits at the shoulder and the gun reaches out along the aim, the
            // right hand brought to the grip - so aiming up, down or to the side never pushes the gun into the body
            if (ShoulderGrip && !carried && (Pose == null || Pose == "kneel") && !current.StartsWith("death"))
            {
                Transform ra = PoseBone("RightArm"), rf = PoseBone("RightForeArm");
                if (ra != null && rf != null)
                {
                    Vector3 grip = ra.position + dir * (gunId.StartsWith("pistol") ? 0.42f : 0.26f) - transform.up * 0.07f;
                    grip = Vector3.Lerp(handR.position, grip, 0.85f);
                    TwoBone(ra, rf, handR, grip);
                    gun.position = grip;
                }
            }
            if (carried && handL != null)
            {
                // carried (running / crawling): exactly along the two hands, as the clip holds it
                Vector3 lh = handL.position - handR.position; float len = lh.magnitude;
                if (len > 0.15f && len < 0.75f && Vector3.Dot(lh, transform.forward) > 0.04f) dir = lh / len; else carried = false;
            }
            gun.rotation = Quaternion.LookRotation(-dir, Vector3.up);
            // standing / walking / firing: the barrel follows the view and the support hand comes to the handguard
            if (!carried && handL != null && (Pose == null || Pose == "kneel") && !current.StartsWith("death"))
            {
                Transform la = PoseBone("LeftArm"), lf = PoseBone("LeftForeArm");
                if (la != null && lf != null) TwoBone(la, lf, handL, gun.position + dir * GripAhead(gunId) - gun.up * 0.035f);
            }
        }

        /// <summary>the head / legs / feet from other outfits take the leading outfit's pose</summary>
        void SyncExtras()
        {
            foreach (Extra x in extras)
            {
                for (int i = 0; i < x.from.Count; i++) x.to[i].rotation = x.from[i].rotation * x.map[i];
                if (x.hips != null)
                {
                    int hi = x.to.IndexOf(x.hips);
                    if (hi >= 0) x.hips.position = x.from[hi].position + transform.TransformVector(x.hipsOffset);
                }
            }
        }

        void OnDestroy() { if (snowball != null) Destroy(snowball.gameObject); if (graph.IsValid()) graph.Destroy(); srcPose?.Dispose(); dstPose?.Dispose(); }
    }
}
