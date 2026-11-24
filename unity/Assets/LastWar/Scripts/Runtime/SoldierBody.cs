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
    public partial class SoldierBody : MonoBehaviour
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

        /// <summary>the outfits (index = saved skin): whole Mixamo characters, all animated with the same rifle clips; then the
        /// modular outfits (Quaternius, import-modular-outfits.mjs): qm_ men, qf_ women - head, top, pants and shoes can each
        /// come from another outfit of the same body</summary>
        public static readonly string[] OutfitFiles = { "mx_swat", "mx_marine", "mx_exo", "mx_shadow", "mx_hazmat", "mx_knight", "mx_archer", "tactical",
            "qm_ranger", "qm_peasant", "qf_ranger", "qf_peasant",
            "mx_cyber", "mx_recon", // (Sketchfab, 1799danly, CC-BY: Mixamo-rigged, worn whole; Striker dropped: its gloves, sleeves and skeleton were bound in three different poses)
            // 1799danly's loose clothes (Sketchfab, CC-BY: no skeleton) fitted onto the Quaternius man's rig (scripts/unity/static-clothes):
            // their head / top / pants / shoes mix with Ranger's and Villager's
            "qm_gorka", "qm_pilot", "qm_neon", "qm_scout", "qm_street", "qm_biker", "qm_field", "qm_silver", "qm_operator" };
        public static readonly string[] OutfitNames = { "Spec Ops", "Marine", "Exo", "Shadow", "Hazmat", "Knight", "Archer", "Agent",
            "Ranger", "Villager", "Huntress", "Wanderer",
            "Cyber", "Recon",
            "Gorka", "Pilot", "Neon", "Scout", "Street", "Biker", "Field", "Silver", "Operator" };

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
        // ---- checks for the outfit test bench
        public bool GunAtHand(float tol) => gun != null && handR != null && (gun.position - handR.position).magnitude < tol + 0.3f;
        public bool Animating => graph.IsValid() && graph.IsPlaying();
        public int VisiblePieces { get { int k = 0; foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (r.enabled && !hiddenParts.Contains(r) && (model == null || !r.transform.IsChildOf(model))) k++; return k; } }

        /// <summary>0 = the military outfits (each its own mixamo rig, worn whole), 1 = the modular men, 2 = the modular women
        /// (one shared rig each). Pieces are only ever mixed inside one group.</summary>
        public static int Group(int skin) => skin < 0 || skin >= OutfitFiles.Length ? 0 : OutfitFiles[skin].StartsWith("qm_") ? 1 : OutfitFiles[skin].StartsWith("qf_") ? 2 : 0;
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
            var fixes = new Quaternion[from.Count];
            for (int i = 0; i < from.Count; i++)
            {
                Quaternion fix = Quaternion.identity; bool found = false;
                // the bone's main axis: toward its paired child - the middle finger for a hand (not whichever finger comes
                // first), and through unpaired in-between bones (SWAT's Neck1) down to the next paired one (the head)
                int ci = UseOldBase ? FirstChild(from[i], index) : AxisChild(from[i], index); // (?base=old keeps its old behaviour)
                if (ci >= 0)
                {
                    Vector3 a = to[ci].position - to[i].position, b = from[ci].position - from[i].position;
                    if (a.sqrMagnitude > 1e-8f && b.sqrMagnitude > 1e-8f && BoneKey(from[i].name) != "Hips") fix = Quaternion.FromToRotation(a, b);
                    found = true;
                }
                // an end bone (a hand whose fingers keep their rest, a foot without toes): it turns as its parent was turned
                if (!found && from[i].parent != null && index.TryGetValue(from[i].parent, out int pi) && pi < i) fix = fixes[pi];
                fixes[i] = fix;
                maps.Add(Quaternion.Inverse(from[i].rotation) * fix * to[i].rotation);
            }
            return maps;
        }
        static int FirstChild(Transform bone, Dictionary<Transform, int> index) { foreach (Transform c in bone) if (index.TryGetValue(c, out int ci)) return ci; return -1; }
        /// <summary>the paired bone that gives a bone's direction (middle finger first for hands; searched below unpaired bones)</summary>
        static int AxisChild(Transform bone, Dictionary<Transform, int> index)
        {
            string k = BoneKey(bone.name);
            if (k.EndsWith("Hand"))
                foreach (Transform c in bone) if (BoneKey(c.name).Contains("Middle1") && index.TryGetValue(c, out int mi)) return mi;
            var queue = new Queue<Transform>(); foreach (Transform c in bone) queue.Enqueue(c);
            while (queue.Count > 0)
            {
                Transform c = queue.Dequeue();
                if (index.TryGetValue(c, out int ci)) return ci;
                if (c.name.Contains("mixamorig")) foreach (Transform g in c) queue.Enqueue(g); // through an unpaired bone only
            }
            return -1;
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
                // stand on the soles: the lowest point of the posed meshes (baked once), not the toe bones' height
                float sole = float.MaxValue;
                var baked = new Mesh();
                foreach (SkinnedMeshRenderer smr in o.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (smr.sharedMesh == null || !smr.sharedMesh.isReadable) continue;
                    smr.BakeMesh(baked, true);
                    var vs = baked.vertices; Matrix4x4 m = smr.transform.localToWorldMatrix;
                    // (BakeMesh with scale: vertices in the renderer's space without its scale twice)
                    m = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                    for (int vi = 0; vi < vs.Length; vi += 3) sole = Mathf.Min(sole, m.MultiplyPoint3x4(vs[vi]).y);
                }
                Destroy(baked);
                if (sole == float.MaxValue)
                {
                    foot = float.MaxValue;
                    foreach (string f in new[] { "LeftToeBase", "RightToeBase", "LeftFoot", "RightFoot" }) if (dst.TryGetValue(f, out Transform ft)) foot = Mathf.Min(foot, ft.position.y);
                    if (foot != float.MaxValue) sole = foot - 0.03f;
                }
                if (sole != float.MaxValue) o.transform.position += Vector3.up * (transform.position.y + 0.005f - sole);
            }
            foreach (Renderer r in o.GetComponentsInChildren<Renderer>(true))
            {
                // (split outfits: body_head / body_torso / body_legs / body_feet; an unsplit one shows whole)
                // the piece's name is on the renderer or on a parent (a piece with several materials is split in sub-objects)
                int part = -1;
                for (Transform t = r.transform; t != null && t != o.transform.parent && part < 0; t = t.parent)
                {
                    string n = System.Text.RegularExpressions.Regex.Replace(t.name, @"(_\d+|\s*\(\d+\)|_?[Pp]rimitive\d*)$", "");
                    for (int i = 0; i < 4; i++) if (n.EndsWith("_" + PartNames[i])) part = i;
                }
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
            for (int i = 0; i < 4; i++) if (Group(wear[i]) != Group(skin) || Group(skin) == 0) wear[i] = skin; // groups never mix; military outfits are worn whole
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
                for (int k = 0; k < mats.Length; k++) mats[k] = CharLit(mats[k]);
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
            CacheFingerRest();
            SnapshotRest();
            { float m = SoleY(); if (m != float.MaxValue) restSole = m - transform.position.y; }
            // the chest's turn in the rest pose (upright, facing ahead): the back gear follows the chest from it
            { Transform sp = PoseBone("Spine2") ?? PoseBone("Spine1") ?? PoseBone("Spine"); if (sp != null) { chestRestOff = Quaternion.Inverse(sp.rotation) * transform.rotation; chestRestSet = true; } }
            { Quaternion? cf = ChestFrame(); if (cf.HasValue) { chestFrameOff = Quaternion.Inverse(cf.Value) * transform.rotation; chestFrameSet = true; } }
            // how far the wearer's back stands out behind the chest bone, from the outfit's own mesh in its upright rest pose
            // (measured once here: measured when a pack was first worn, a crouch / run / pick-up pose could leave the pack -
            // and the guns behind it - floating off the back for good, seen from the side)
            {
                Transform sp = PoseBone("Spine2") ?? PoseBone("Spine1") ?? PoseBone("Spine"), hp = PoseBone("Hips"), nk = PoseBone("Neck"), sl = PoseBone("LeftArm"), sr = PoseBone("RightArm");
                if (sp != null && nk != null)
                {
                    Vector3 up = transform.up, bk = -transform.forward;
                    float torso = hp != null ? (nk.position - hp.position).magnitude : 0.5f, shoulders = sl != null && sr != null ? (sl.position - sr.position).magnitude : 0.35f;
                    restBack = MeasureBack(sp, up, bk, nk.position - up * 0.07f, torso * 0.71f, shoulders * 0.85f); restBackSet = true;
                }
            }
        }

        Quaternion hipsCorr = Quaternion.identity;
        public static bool UseHumanoid = false, LogRig = false, QFingers = false, ExtraPos = true;
        static bool IsFinger(string n) => n.Contains("Index") || n.Contains("Middle") || n.Contains("Ring") || n.Contains("Pinky") || n.Contains("Thumb");
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

        int builtFrame = -1; // (weapon IK waits until the animation has posed the body at least once)
        // ---- "has the animation posed this body yet?" Measured on the hidden source skeleton the outfit copies (lw_base),
        // all its bones against their bind rotations. Latched: once posed, a clip change never turns it back off.
        Quaternion[] restSrc; public bool AnimPosed { get; private set; }
        public static float PoseThreshold = 0.05f; // degrees, any one bone (test switch ?posethresh=)
        public string PosedBy { get; private set; } = "";
        double lastClipTime = -1; int clipAdvances, lastClipFrame = -1;
        /// <summary>readout: the largest bind-to-now angle on the source skeleton at the last check, and the time ticks seen</summary>
        public float SrcMaxAngle { get; private set; } public int ClipAdvances => clipAdvances;
        void SnapshotRest()
        {
            restSrc = new Quaternion[srcBones.Count];
            for (int i = 0; i < srcBones.Count; i++) restSrc[i] = srcBones[i].localRotation;
        }
        /// <summary>
        /// Two independent pieces of evidence, either is enough:
        ///  (1) pose: any bone of the source skeleton has left its bind rotation (beyond PoseThreshold) - every real clip
        ///      does this somewhere (spine, legs, fingers) even when its arms stay near the rest pose;
        ///  (2) time: the playing clip's time has advanced on two separate frames - the graph runs and the Animator writes
        ///      its output (on the spawn frame the graph can tick once before the new Animator is bound, so one tick is
        ///      not proof). This covers a clip that holds the bind pose exactly.
        /// </summary>
        public bool CheckPosed()
        {
            if (AnimPosed) return true;
            // (called from LateUpdate: the Animator has already written this frame's pose into lw_base's transforms)
            float maxA = 0f; int maxI = -1;
            if (restSrc != null)
                for (int i = 0; i < restSrc.Length && i < srcBones.Count; i++)
                {
                    float a = Quaternion.Angle(restSrc[i], srcBones[i].localRotation);
                    if (a > maxA) { maxA = a; maxI = i; }
                }
            SrcMaxAngle = maxA;
            if (maxA > PoseThreshold) { AnimPosed = true; PosedBy = $"pose: {BoneKey(srcBones[maxI].name)} {maxA:0.0} deg off its bind rotation"; return true; }
            string key = playing ?? current;
            if (graph.IsValid() && graph.IsPlaying() && key != null && clipIndex.TryGetValue(key, out int ci) && Time.frameCount != lastClipFrame)
            {
                // one read per rendered frame: two advances = two distinct evaluated frames
                lastClipFrame = Time.frameCount;
                double t = ((AnimationClipPlayable)mixer.GetInput(ci)).GetTime();
                if (t > 1e-4 && lastClipTime >= 0 && t != lastClipTime) clipAdvances++;
                lastClipTime = t;
                if (clipAdvances >= 2) { AnimPosed = true; PosedBy = "time: the clip advanced on two distinct frames"; }
            }
            return AnimPosed;
        }
        void Build(int skin)
        {
            builtFrame = Time.frameCount;
            // LW_BaseCharacter: the Mixamo SWAT skeleton carrying every clip (scripts/unity/build-base.mjs); the old soldier rig is the fallback
            GameObject src = UseOldBase ? null : MapBuilder.Model(BaseModel);
            if (src == null) src = MapBuilder.Model("soldier");
            if (src == null) return;
            GameObject m = Instantiate(src, transform, false);
            m.name = src.name; // (keeps the base's name: the rig check reports which skeleton drives the body)
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

        public const string BaseModel = "lw_base";
        // ---- validation hooks (LobbyTests): the base skeleton driving the body, its bones and the outfit's
        public string BaseName => model != null ? model.name : "";
        public Transform BaseBone(string key) => SrcBone(key);
        public Transform OutfitBone(string key) => PoseBone(key);
        public string PlayingClip => playing;
        public bool HasClip(string clip) => clipIndex.ContainsKey(clip);
        public static bool UseOldBase; // test switch ?base=old
        public static Material Lit(Material src) => MapBuilder.LitFrom(src);
        /// <summary>characters a little brighter than the world (dark suits vanished in shade and against the sand)</summary>
        static readonly Dictionary<Material, Material> charMats = new Dictionary<Material, Material>();
        public static Material CharLit(Material src)
        {
            if (src != null && charMats.TryGetValue(src, out Material m)) return m;
            m = new Material(Lit(src));
            Color c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.white;
            float lum = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f, k = lum < 0.35f ? 1.7f : 1.35f;
            m.SetColor("_BaseColor", new Color(c.r * k, c.g * k, c.b * k, c.a));
            if (src != null) charMats[src] = m;
            return m;
        }

        void SetupAnimation(GameObject m)
        {
            Animator animator = m.GetComponentInChildren<Animator>();
            if (animator == null) animator = m.AddComponent<Animator>();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; // keeps driving an outfit even though its own mesh is hidden
            if (clips == null)
            {
                clips = UseOldBase ? new AnimationClip[0] : Resources.LoadAll<AnimationClip>("Models/" + BaseModel);
                if (clips.Length == 0) clips = Resources.LoadAll<AnimationClip>("Models/soldier");
                // the lobby's standing clips (lw_lobby.glb, same skeleton, kept apart from lw_base.glb): mx_lobby = rifle idle,
                // smooth blend into aiming, aim held, blend back - one seamless loop; mx_lobby_idle / mx_lobby_aim = the parts
                else if (!UseOldBase)
                {
                    // (and lw_extra.glb: further Mixamo clips on the same skeleton, on trial - e.g. mx_rifle_run, mx_running)
                    foreach (string extra in new[] { "lw_lobby", "lw_extra", "lw_pistol" }) // (lw_pistol.glb: Mixamo's Pistol Handgun Locomotion clips, Step E)
                    {
                        var more = Resources.LoadAll<AnimationClip>("Models/" + extra);
                        if (more.Length > 0) { var all = new AnimationClip[clips.Length + more.Length]; clips.CopyTo(all, 0); more.CopyTo(all, clips.Length); clips = all; }
                    }
                }
            }
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
            // running with empty hands: the Mixamo "Running" (lw_extra.glb, arms swinging free; 5.6 m/s as recorded, close
            // to the 6 m/s the run's playback rate is set for). With a gun the rifle clips stand in as before.
            if (clipIndex.TryGetValue("mx_running", out int runFree)) clipIndex["run"] = runFree;
            // jogging with empty hands (a full push of the stick): Mixamo's Jog Forward (lw_extra.glb mx_jog)
            if (clipIndex.TryGetValue("mx_jog", out int jogI)) { clipIndex["jog"] = jogI; HasJog = UseJog; }
            // the web game's run_free: the run clip's legs with the walk clip's arms (swinging at the sides),
            // as a second layer that only moves the shoulders, arms and hands
            layers = AnimationLayerMixerPlayable.Create(graph, 3);
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
            // layer 2: the rifle aiming pose on the upper body only (spine and everything above it), for firing in the air
            if (clipIndex.TryGetValue("mx_aim", out int ai))
            {
                aimUpper = AnimationClipPlayable.Create(graph, clips[ai]);
                graph.Connect(aimUpper, 0, layers, 2); layers.SetInputWeight(2, 0f);
                var um = new AvatarMask(); um.AddTransformPath(animator.transform, true);
                for (int i = 0; i < um.transformCount; i++) um.SetTransformActive(i, um.GetTransformPath(i).Contains("Spine"));
                layers.SetLayerMaskFromAvatarMask(2, um);
            }
            AnimationPlayableOutput.Create(graph, "out", animator).SetSourcePlayable(layers);
            // test switch ?animdelay=N: the body is built but its animation starts N frames later (a slow-loading device)
            if (Bench.Active) { graph.SetTimeUpdateMode(DirectorUpdateMode.Manual); if (!Bench.Bodies.Contains(this)) Bench.Bodies.Add(this); }
            if (AnimDelayFrames > 0) { delayedUntil = Time.frameCount + AnimDelayFrames; Debug.Log($"[LastWar] wikf {name}: animation held back {AnimDelayFrames} frames"); }
            else graph.Play();
        }

        // ---- rifle animations (Mixamo Pro Rifle Pack, retargeted): with a gun in the hands the game's clips are swapped
        // for these - the hands sit on the gun as the animator placed them
        static readonly Dictionary<string, string> RifleClips = new Dictionary<string, string> {
            { "idle", "mx_idle" }, { "idle_rifle", "mx_idle" }, { "fire", "mx_fire" }, { "walk_fire", "mx_walk" }, { "walk", "mx_walk" },
            { "run", "mx_sprint" }, { "jump", "mx_jump" }, { "crouch_idle", "mx_crouch" }, { "crouch_fire", "mx_crouch_fire" },
            { "crouch_walk", "mx_crouch_walk" }, { "prone", "mx_prone" } };
        public static bool UseRifleClips = true;
        /// <summary>the game's clips -> the pistol clips (lw_pistol.glb); the others (crouch walk, lying down) as before</summary>
        static readonly Dictionary<string, string> PistolClips = new Dictionary<string, string> {
            { "idle", "p_idle" }, { "idle_rifle", "p_idle" }, { "fire", "p_idle" }, { "walk", "p_walk" }, { "walk_fire", "p_walk" },
            { "run", "p_run" }, { "crouch_idle", "p_kneel" }, { "crouch_fire", "p_kneel" },
            // jumping: Pistol Jump's time in the air only (0.8-1.43 s of its 2 s: no crouch before, no landing after - about
            // the game's own 0.65 s in the air), played once and held on its last frame like the other jumps
            { "jump", "p_jump" },
            // lying down / crawling with a pistol: the rifle's prone clips (the crawl made even), the pistol forward in the right hand
            { "prone", "mx_prone" } };
        public static bool UsePistolClips = true;
        /// <summary>false (?fcrawl=0): crawling / crouching with empty hands use the earlier hand-made crawl / kneel</summary>
        public static bool UseFreeCrawl = true, UseEvenRifleCrawl = true;
        /// <summary>false (?grip=palm): the gun's RightHandGrip on the palm point as before, for comparison</summary>
        public static bool UseGripContact = true;
        /// <summary>clips recorded at another ground speed than the 6 m/s the run's playback rate assumes: played faster /
        /// slower by 6 / their own speed so the feet keep pace (Rifle Run 4.13 m/s, Running 5.63 m/s, measured from their hips)</summary>
        public static bool UseHumanRun = true, UseRunForward = true;
        /// <summary>the jog clip is there (and not turned off with ?jog=0)</summary>
        public static bool HasJog, UseJog = true;
        static readonly Dictionary<string, float> ClipPace = new Dictionary<string, float> { { "mx_rifle_run", 6f / 4.13f }, { "mx_rifle_run_h", 6f / 5.63f }, { "mx_run", 6f / 4.61f }, { "run", 1f },
            // crawling (option A): the body goes CrawlSpeed m/s and each crawl clip plays at the pace its own limbs keep there
            // (Crawl Forward 0.29 m/s as recorded, the rifle prone crawl 0.23 m/s - measured from their hips)
            // the pistol clips at the speeds the walk / run playback rates assume (walk 2.6, run 6 m/s; recorded 2.55 / 4.74)
            { "p_walk", 2.6f / 2.55f }, { "p_run", 6f / 4.74f },
            { "fc_sym", CrawlSpeed / 0.292f }, { "mx_crawl_sym", CrawlSpeed / 0.234f }, { "mx_crawl", CrawlSpeed / 0.234f } };
        /// <summary>crawling speed with the crawl clips (m/s): slow enough for the clips' own strokes (was 0.92)</summary>
        public const float CrawlSpeed = 0.45f;
        /// <summary>the player's crawl: walk speed x 0.2 x CrawlPull - with a clip, CrawlPull gives CrawlSpeed</summary>
        static float ClipCrawlPull => CrawlSpeed / (PlayerController.WalkSpeed * 0.2f);
        /// <summary>reloading: standing still (or aiming / firing / walking) the rifle reload clip plays, timed to the reload</summary>
        public bool Reloading; public float ReloadTime = 2f;
        // (standing only: walking, the reload clip - feet still - played over the walk and he slid along; on the move the
        // walk / run goes on and the hands reload on top as the gun IK has them)
        static readonly HashSet<string> ReloadOver = new HashSet<string> { "mx_idle", "mx_aim", "mx_fire" };
        string playing; // the clip the mixer plays for `current` (a rifle clip when one stands in)
        bool RifleClip => playing != null && playing.StartsWith("mx_");
        bool PistolClip => playing != null && playing.StartsWith("p_");

        public void Play(string clip)
        {
            if (!clipIndex.ContainsKey(clip)) return;
            string key = clip;
            if (UseRifleClips && Armed && gun != null && !gunId.StartsWith("pistol") && RifleClips.TryGetValue(clip, out string mx) && clipIndex.ContainsKey(mx)) key = mx;
            // a pistol in the hands: Mixamo's pistol clips (lw_pistol.glb - aiming with both hands, walking / running with it
            // up, kneeling); the hands stay on the pistol as before (?pclips=0: the earlier clips)
            if (UsePistolClips && Armed && gun != null && gunId.StartsWith("pistol") && PistolClips.TryGetValue(clip, out string pc) && clipIndex.ContainsKey(pc)) key = pc;
            // running with a long gun: the Mixamo Rifle Run (lw_extra.glb: gun low across the chest) when present
            // running with a long gun: the Pro Rifle Pack's Run Forward (lw_base.glb mx_run: the gun up at the chest,
            // chosen by the project owner; ?runfwd=0: the Rifle Run below as before)
            if (key == "mx_sprint" && UseRunForward && clipIndex.ContainsKey("mx_run")) key = "mx_run";
            if (key == "mx_sprint" && clipIndex.ContainsKey("mx_rifle_run")) key = "mx_rifle_run";
            // ... its legs, hips and lower back from Mixamo's "Running" (the free run's long human stride and bounce), the
            // chest, arms and gun hold as Rifle Run (lw_extra.glb mx_rifle_run_h; ?hrun=0: the Rifle Run as it was)
            if (key == "mx_rifle_run" && UseHumanRun && clipIndex.ContainsKey("mx_rifle_run_h")) key = "mx_rifle_run_h";
            // lying down with a rifle: the Mixamo prone clips (still / crawling forward; backward = the crawl stepped back)
            if (key == "mx_prone" && Mathf.Abs(Speed) > 0.25f && clipIndex.ContainsKey("mx_crawl")) key = "mx_crawl";
            // ... made even (lw_extra.glb mx_crawl_sym): both legs push in turn - the recorded crawl pushed with the right leg
            // only; the pelvis roll evened out; the arms and the gun hold as recorded (?rcrawl=0: the recorded crawl)
            if (key == "mx_crawl" && UseEvenRifleCrawl && clipIndex.ContainsKey("mx_crawl_sym")) key = "mx_crawl_sym";
            // crawling with empty hands: Mixamo's Crawl Forward (lw_extra.glb fc_sym - low on elbows and knees, made
            // left / right symmetric, fingers open) instead of the hand-made crawl; with a pistol the hand-made one stays
            if (key == "prone" && UseFreeCrawl && (gun == null || !Armed) && Mathf.Abs(Speed) > 0.25f && clipIndex.ContainsKey("fc_sym")) key = "fc_sym";
            // crouched and still with empty hands: Mixamo's Male Crouch Pose (lw_extra.glb kn_free - right knee down, left
            // hand on the left knee, hands open) instead of the rifle-held crouch clip with the hand-made kneel on top
            if (key == "crouch_idle" && UseFreeCrawl && (gun == null || !Armed) && Mathf.Abs(Speed) < 0.4f && clipIndex.ContainsKey("kn_free")) key = "kn_free";
            if (Reloading && ReloadOver.Contains(key) && clipIndex.ContainsKey("mx_reload")) key = "mx_reload";
            if (clip == current && key == playing) return;
            current = clip; playing = key;
            // restart one-shot clips (jump, death) from the beginning
            var cp = (AnimationClipPlayable)mixer.GetInput(clipIndex[key]);
            if (OneShot(key) || key == "mx_reload") { cp.SetTime(0); cp.SetSpeed(1); }
        }

        /// <summary>test bench: stop the current clip at a fraction of its length (a fixed frame for screenshots)</summary>
        public void Hold(float fraction)
        {
            if (playing == null || !clipIndex.ContainsKey(playing)) return;
            var cp = (AnimationClipPlayable)mixer.GetInput(clipIndex[playing]);
            var c = cp.GetAnimationClip(); cp.SetTime(c != null ? c.length * fraction : 0); cp.SetSpeed(0);
        }

        bool dead0() => current != null && current.StartsWith("death");
        static bool OneShot(string clip) => clip.StartsWith("death") || clip == "jump" || clip == "p_jump";

        public void SetGun(string id)
        {
            if (id == gunId) return;
            gunId = id;
            if (gun != null) Destroy(gun.gameObject);
            gun = null;
            if (string.IsNullOrEmpty(id)) return;
            // (GunFrame: the holder is moved, never the model's own root - that root carries the import's turn)
            gun = GunFrame.Make(id, transform, "gun");
            if (gun == null) return;
            foreach (Renderer r in gun.GetComponentsInChildren<Renderer>(true)) { var mats = r.sharedMaterials; for (int k = 0; k < mats.Length; k++) mats[k] = Lit(mats[k]); r.sharedMaterials = mats; r.enabled = visible; }
        }

        AnimationLayerMixerPlayable layers; AnimationClipPlayable walkArms; float walkLen, freeW;
        /// <summary>firing in the air (a long gun): the legs keep the jump, the upper body (spine up) takes the aiming
        /// pose as a masked layer, so the gun points at the target; set each frame by the controller</summary>
        public bool AirAim;
        AnimationClipPlayable aimUpper; float aimUpperW;
        /// <summary>weight of the upper-body aiming layer (0..1) - the weapon IK turns the chest toward the aim by it</summary>
        public float UpperAimWeight => aimUpperW;
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

        static readonly string[] ToeChecks = { "LeftToeBase", "RightToeBase" }, KneelChecks = { "LeftToeBase", "RightToeBase", "LeftFoot", "RightFoot" };
        /// <summary>no feet inside the ground: lift the body when a toe is below the ground under it (web fixFeet)</summary>
        void FixFeet() => FixFeet(false);
        /// <summary>afterPose: run again once the kneel pose is laid on (on a slope the kneeling legs, set against a flat
        /// ground, put a foot into the rising side): the ankles count too, kept 4 cm above the ground (the kneeling shin lies about that high on flat ground: no change there)</summary>
        void FixFeet(bool afterPose)
        {
            if (!visible || Tilt != 0 || Pose == "sky" || Pose == "chute" || Pose == "crawl" || current == null || current == "prone" || current.StartsWith("death") || current == "jump") return;
            Transform hips = outfitRoot != null ? dstHips : null;
            if (hips == null) { Transform h = PoseBone("Hips"); hips = h; }
            if (hips == null) return;
            int mask = ~(1 << MapBuilder.PlayerLayer);
            float lift = 0f;
            foreach (string toe in afterPose ? KneelChecks : ToeChecks)
            {
                Transform t = PoseBone(toe); if (t == null) continue;
                Vector3 p = t.position; float keep = toe.EndsWith("Foot") ? 0.04f : 0.03f;
                if (Physics.Raycast(p + Vector3.up * 0.6f, Vector3.down, out RaycastHit hit, 1.2f, mask, QueryTriggerInteraction.Ignore))
                    lift = Mathf.Max(lift, hit.point.y + keep - p.y);
            }
            if (lift > 0.005f) hips.position += Vector3.up * Mathf.Min(lift, 0.45f);
        }

        // ---- standing on a flat stage (the lobby): the lower foot rests exactly on the floor, never sinks or floats
        public bool Plant;
        float restSole = float.NaN;
        static readonly string[] Soles = { "LeftToeBase", "RightToeBase", "LeftFoot", "RightFoot" };
        float SoleY()
        {
            float m = float.MaxValue;
            foreach (string f in Soles) { Transform t = PoseBone(f); if (t != null) m = Mathf.Min(m, t.position.y); }
            return m;
        }
        void PlantFeet()
        {
            if (!Plant) return;
            Transform hips = outfitRoot != null ? dstHips : PoseBone("Hips");
            if (hips == null || float.IsNaN(restSole)) return;
            float m = SoleY(); if (m == float.MaxValue) return;
            hips.position += Vector3.up * (transform.position.y + restSole - m);
        }
        // ---- heels on the ground when standing still. The outfits' feet are carried over from the SWAT skeleton, and on most
        // outfits the sole sits at an angle to the foot bone different from SWAT's: standing, the toes touch the ground and
        // the heel floats 1.5-5 cm (measured, ?footdbg=1). Once per outfit, standing, the lowest point of each heel and of
        // each toe is taken from the outfit's own mesh and kept in the foot bone's space; then, only in the standing clips
        // (lobby / idle / aim / fire / reload, not moving, no special pose), each foot turns about its ankle until those two
        // points are level, and the body lowers by what the lowest of them rose (the lowest point stays where it was).
        // Walking, running, jumping, crouching, lying: untouched. Arms, hands and gun: not touched here (the gun hold runs
        // after this, on the body as it now stands). ?heel=0 turns it off for comparison.
        public static bool UseHeelLevel = true;
        static readonly HashSet<string> StandClips = new HashSet<string> { "mx_lobby", "mx_lobby_idle", "mx_lobby_aim", "mx_idle", "mx_aim", "mx_fire", "mx_reload", "idle", "idle_rifle", "fire", "p_idle" };
        Transform soleFor; float soleWait; int soleFrames; bool soleOk; float heelW;
        readonly Vector3[] soleHeel = new Vector3[2], soleBall = new Vector3[2];
        readonly Transform[] soleFoot = new Transform[2];
        float[] heelDeg = new float[2];
        bool StandingStill => Pose == null && Tilt == 0 && Mathf.Abs(Speed) < 0.15f && playing != null && StandClips.Contains(playing) && !AirAim;

        void LevelSoles()
        {
            if (!UseHeelLevel || outfitRoot == null) return;
            if (soleFor != outfitRoot) { soleFor = outfitRoot; soleOk = false; soleWait = 0f; soleFrames = 0; heelW = 0f; }
            bool stand = visible && StandingStill;
            if (!soleOk)
            {
                // measured once the outfit has stood still for a while (the clip and any crossfade settled: at least 1 s and
                // 20 frames - on a slow device the first frames come seconds apart), before any levelling
                if (stand) { soleWait += Time.deltaTime; soleFrames++; } else { soleWait = 0f; soleFrames = 0; }
                if (soleWait < 1f || soleFrames < 20) return;
                soleOk = MeasureSoles();
                if (!soleOk) { soleFor = null; return; }
            }
            heelW = Mathf.MoveTowards(heelW, stand ? 1f : 0f, Time.deltaTime * 5f);
            if (heelW <= 0f) return;
            Vector3 up = transform.up;
            float before = float.MaxValue, after = float.MaxValue;
            for (int i = 0; i < 2; i++)
            {
                Transform f = soleFoot[i]; if (f == null) continue;
                Vector3 h = f.TransformPoint(soleHeel[i]), b = f.TransformPoint(soleBall[i]);
                before = Mathf.Min(before, Mathf.Min(Vector3.Dot(h, up), Vector3.Dot(b, up)));
                Vector3 v = b - h, flat = Vector3.ProjectOnPlane(v, up);
                if (flat.sqrMagnitude < 1e-6f) continue;
                // (a pitch about the foot's side axis, at most 25 degrees - measured need up to about 20 on Marine / Hazmat: the outfit's sole angle, not a pose)
                float ang = Mathf.Clamp(Vector3.SignedAngle(v, flat, Vector3.Cross(up, flat).normalized), -25f, 25f);
                heelDeg[i] = ang;
                f.rotation = Quaternion.AngleAxis(ang * heelW, Vector3.Cross(up, flat).normalized) * f.rotation;
                h = f.TransformPoint(soleHeel[i]); b = f.TransformPoint(soleBall[i]);
                after = Mathf.Min(after, Mathf.Min(Vector3.Dot(h, up), Vector3.Dot(b, up)));
            }
            if (before == float.MaxValue || after == float.MaxValue || dstHips == null) return;
            dstHips.position -= up * (after - before);
            // the other foot: levelling turns each sole about its own ankle, so on a foot that carried little weight in the
            // clip (the lobby's right foot on Marine) the toe rises with the heel and the whole sole hangs above the ground
            // (up to 3 cm) once the body has come down onto the lower foot. That foot is brought down onto the ground by
            // bending its knee (the leg's two bones, the foot keeping its turn), at most 4 cm; a straight leg stays as it is.
            // Standing still only, like the levelling; ?heel=0 turns both off.
            if (!UseSoleDrop) return;
            float g = Vector3.Dot(transform.position, up);
            for (int i = 0; i < 2; i++)
            {
                Transform f = soleFoot[i]; if (f == null) continue;
                float low = Mathf.Min(Vector3.Dot(f.TransformPoint(soleHeel[i]), up), Vector3.Dot(f.TransformPoint(soleBall[i]), up)) - g;
                soleGap[i] = low;
                if (low < 0.008f) continue;
                string sd = i == 0 ? "Left" : "Right";
                Transform thigh = PoseBone(sd + "UpLeg"), shin = PoseBone(sd + "Leg");
                if (thigh == null || shin == null || !f.IsChildOf(shin)) continue;
                Quaternion keep = f.rotation;
                TwoBone(thigh, shin, f, f.position - up * (Mathf.Min(low, 0.04f) * heelW));
                f.rotation = keep;
                soleGap[i] = Mathf.Min(Vector3.Dot(f.TransformPoint(soleHeel[i]), up), Vector3.Dot(f.TransformPoint(soleBall[i]), up)) - g;
            }
        }
        /// <summary>false (?soledrop=0): the higher foot is left where the levelling put it</summary>
        public static bool UseSoleDrop = true;
        readonly float[] soleGap = new float[2];

        /// <summary>the lowest point of each heel and of each toe on the outfit's mesh, in the foot bone's space</summary>
        bool MeasureSoles()
        {
            var pts = new List<Vector3>(); var baked = new Mesh();
            foreach (SkinnedMeshRenderer r in outfitRoot.GetComponentsInChildren<SkinnedMeshRenderer>(false))
            {
                if (!r.enabled) continue;
                r.BakeMesh(baked); Matrix4x4 m = r.transform.localToWorldMatrix;
                foreach (Vector3 v in baked.vertices) pts.Add(m.MultiplyPoint3x4(v));
            }
            foreach (Extra x in extras)
                foreach (SkinnedMeshRenderer r in x.root.GetComponentsInChildren<SkinnedMeshRenderer>(false))
                {
                    if (!r.enabled) continue;
                    r.BakeMesh(baked); Matrix4x4 m = r.transform.localToWorldMatrix;
                    foreach (Vector3 v in baked.vertices) pts.Add(m.MultiplyPoint3x4(v));
                }
            Destroy(baked);
            Vector3 up = transform.up; bool any = false;
            for (int i = 0; i < 2; i++)
            {
                string sd = i == 0 ? "Left" : "Right";
                Transform ankle = PoseBone(sd + "Foot"), toe = PoseBone(sd + "ToeBase");
                soleFoot[i] = null;
                if (ankle == null || toe == null) continue;
                Vector3 fw = Vector3.ProjectOnPlane(toe.position - ankle.position, up); float len = fw.magnitude; if (len < 1e-3f) continue; fw /= len;
                Vector3 side = Vector3.Cross(up, fw);
                float heel = float.MaxValue, ball = float.MaxValue; Vector3 hp = Vector3.zero, bp = Vector3.zero;
                foreach (Vector3 p in pts)
                {
                    Vector3 d = p - ankle.position; float hy = Vector3.Dot(d, up);
                    if (hy > 0.03f || Mathf.Abs(Vector3.Dot(d, side)) > 0.06f) continue;
                    float a = Vector3.Dot(d, fw);
                    if (a < -0.12f || a > len + 0.12f) continue;
                    if (a < len * 0.25f) { if (hy < heel) { heel = hy; hp = p; } }
                    else if (a > len * 0.75f) { if (hy < ball) { ball = hy; bp = p; } }
                }
                if (heel == float.MaxValue || ball == float.MaxValue) continue;
                soleFoot[i] = ankle; soleHeel[i] = ankle.InverseTransformPoint(hp); soleBall[i] = ankle.InverseTransformPoint(bp); any = true;
            }
            if (FootDebug) Debug.Log($"[LastWar] soles {name} {outfitRoot.name.Replace("(Clone)", "")} {playing}: measured {(any ? "ok" : "no feet found")} after {soleWait:0.0} s / {soleFrames} frames; same bones as the mesh: {(soleFoot[0] != null && soleFoot[0].IsChildOf(outfitRoot))}");
            return any;
        }

        /// <summary>?footdbg=1 (readout only): every 2 s, how high the lowest point of each heel and of each toe sits above the
        /// soldier's ground (cm), from the outfit's own mesh baked in the pose of the moment</summary>
        public static bool FootDebug; float nextFootLog;
        void LogFeet()
        {
            Transform root = outfitRoot != null ? outfitRoot : (model != null ? model : transform);
            var baked = new Mesh(); var pts = new List<Vector3>();
            foreach (SkinnedMeshRenderer r in root.GetComponentsInChildren<SkinnedMeshRenderer>(false))
            {
                if (!r.enabled) continue;
                r.BakeMesh(baked); Matrix4x4 m = r.transform.localToWorldMatrix;
                foreach (Vector3 v in baked.vertices) pts.Add(m.MultiplyPoint3x4(v));
            }
            Destroy(baked);
            float ground = transform.position.y; var sb = new System.Text.StringBuilder($"[LastWar] feet {name} {(outfitRoot != null ? outfitRoot.name.Replace("(Clone)", "") : "-")} {playing}:");
            foreach (string sd in new[] { "Left", "Right" })
            {
                Transform ankle = PoseBone(sd + "Foot"), toe = PoseBone(sd + "ToeBase");
                if (ankle == null || toe == null) continue;
                Vector3 f = toe.position - ankle.position; f.y = 0; float len = f.magnitude; if (len < 1e-3f) continue; f /= len;
                Vector3 side = Vector3.Cross(Vector3.up, f);
                float heel = float.MaxValue, ball = float.MaxValue;
                foreach (Vector3 p in pts)
                {
                    Vector3 d = p - ankle.position;
                    if (d.y > 0.03f || Mathf.Abs(Vector3.Dot(d, side)) > 0.06f) continue;
                    float a = Vector3.Dot(d, f);
                    if (a < -0.12f || a > len + 0.12f) continue;
                    if (a < len * 0.25f) heel = Mathf.Min(heel, p.y); else if (a > len * 0.75f) ball = Mathf.Min(ball, p.y);
                }
                sb.Append($" {sd} heel {(heel - ground) * 100f:0.0} cm toe {(ball - ground) * 100f:0.0} cm |");
            }
            if (UseHeelLevel)
            {
                sb.Append($" level {heelW:0.00} ({heelDeg[0]:0.0} / {heelDeg[1]:0.0} deg)");
                // (the two measured sole points now, cm above the ground: they should sit level and low)
                sb.Append($" drop gap L {soleGap[0] * 100f:0.0} R {soleGap[1] * 100f:0.0} cm");
                for (int i = 0; i < 2; i++) if (soleOk && soleFoot[i] != null)
                    sb.Append($" {(i == 0 ? "L" : "R")} pts heel {(soleFoot[i].TransformPoint(soleHeel[i]).y - ground) * 100f:0.0} ball {(soleFoot[i].TransformPoint(soleBall[i]).y - ground) * 100f:0.0}");
            }
            Debug.Log(sb.ToString());
        }
        public static int AnimDelayFrames; int delayedUntil = -1;
        void Update()
        {
            if (delayedUntil > 0 && Time.frameCount >= delayedUntil && graph.IsValid()) { delayedUntil = -1; graph.Play(); Debug.Log($"[LastWar] wikf {name}: animation starts now (frame {Time.frameCount - builtFrame} after spawn)"); }
            if (weights == null || current == null) return;
            // free arms while running empty-handed: walk arms in step with the run (the walk's cycle stretched to the run's)
            freeW = Mathf.MoveTowards(freeW, Armed && gun != null && current == "run" && !RifleClip && !PistolClip ? 1f : 0f, Time.deltaTime * 6f); // empty hands swing free with the sprint
            if (walkArms.IsValid()) layers.SetInputWeight(1, 0f); // (the arms are copied from the walk clip in LateUpdate instead)
            // firing in the air with a long gun: the aiming pose fades in over the upper body (about 0.12 s), and out on landing
            aimUpperW = Mathf.MoveTowards(aimUpperW, AirAim && playing == "mx_jump" && Armed && gun != null && !dead0() ? 1f : 0f, Time.deltaTime * 8f);
            if (aimUpper.IsValid()) layers.SetInputWeight(2, aimUpperW);
            int target = clipIndex[playing ?? current];
            // one-shot clips (jump, death) stop on their last frame instead of looping
            if (OneShot(playing ?? current))
            {
                var cp = (AnimationClipPlayable)mixer.GetInput(target);
                float len = clips[target].length - 0.02f;
                if (cp.GetTime() >= len) { cp.SetTime(len); cp.SetSpeed(0); }
            }
            else
            {
                // locomotion follows the real speed; backwards (retreating) the clip is stepped by hand
                var cp = (AnimationClipPlayable)mixer.GetInput(target);
                if (playing == "mx_reload") cp.SetSpeed(clips[target].length / Mathf.Max(0.3f, ReloadTime));
                else if (Rate >= 0) cp.SetSpeed(Rate * (ClipPace.TryGetValue(playing ?? current, out float pace) ? pace : 1f));
                else
                {
                    cp.SetSpeed(0);
                    double len = clips[target].length, t = cp.GetTime() + Rate * (ClipPace.TryGetValue(playing ?? current, out float bpace) ? bpace : 1f) * Time.deltaTime; // (backwards at the same pace)
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
        /// <summary>the fallen soldier vanishes after the fall; what they carried stays on the ground</summary>
        public void HideAfter(float seconds) => Invoke(nameof(HideNow), seconds);
        void HideNow() { SetVisible(false); PlaceBack(); }

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
            // the rifle's prone clips crawl by themselves: no hand-made crawl on top
            if (RifleClip && (playing == "mx_prone" || playing == "mx_crawl" || playing == "mx_crawl_sym")) { poseW = 0; CrawlPull = ClipCrawlPull; return; }
            if (playing == "fc_sym") { poseW = 0; CrawlPull = ClipCrawlPull; return; } // (the empty-handed crawl clip: no hand-made crawl on top)
            if (playing == "kn_free" || playing == "p_kneel") { poseW = 0; return; } // (the crouch / pistol kneel clips: no hand-made kneel on top)
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
            if (playing == null || !clipIndex.TryGetValue(playing, out int i)) return 0;
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
                Transform g = GunFrame.Make(ids[i], transform, "backgun");
                if (g == null) continue;
                foreach (Renderer r in g.GetComponentsInChildren<Renderer>(true)) { var mats = r.sharedMaterials; for (int k = 0; k < mats.Length; k++) mats[k] = Lit(mats[k]); r.sharedMaterials = mats; r.enabled = visible; }
                backGuns.Add(g);
            }
        }

        // ---- the backpack worn on the back (its level's model: straps against the body)
        float restBack; bool restBackSet; float bagDepth;
        Transform bagLook; int bagLvShown; Quaternion bagSpineOffset; bool bagOffsetSet; float bagFit, bagBack = -1f; Bounds bagNative;

        /// <summary>how far behind the chest bone the wearer's back surface reaches, over the pack's own height and width
        /// (all the outfit's skinned meshes baked in their current pose; falls back to 4 cm)</summary>
        float MeasureBack(Transform spine, Vector3 up, Vector3 back, Vector3 top, float height, float width)
        {
            float far = float.MinValue;
            Vector3 right = Vector3.Cross(up, back).normalized;
            Transform root = outfitRoot != null ? outfitRoot : (model != null ? model : transform);
            var baked = new Mesh();
            foreach (SkinnedMeshRenderer r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                // only the pieces worn: a mixed outfit leaves its own hidden ones in place (the Ranger's hood, hanging
                // down the back, held the pack 25 cm off the back of a Ranger wearing the Villager's head)
                if (hiddenParts.Contains(r)) continue;
                r.BakeMesh(baked);
                var vs = baked.vertices; Matrix4x4 m = r.transform.localToWorldMatrix;
                for (int i = 0; i < vs.Length; i++)
                {
                    Vector3 p = m.MultiplyPoint3x4(vs[i]);
                    float dy = Vector3.Dot(p - top, up);                  // 0 at the pack's top, -height at its bottom
                    if (dy > 0.02f || dy < -height) continue;
                    Vector3 rel = p - spine.position;
                    if (Mathf.Abs(Vector3.Dot(rel, right)) > width * 0.5f) continue;
                    float d = Vector3.Dot(rel, back);
                    if (d > far && d < 0.35f) far = d;                   // (an arm or a weapon well behind is not the back)
                }
            }
            Destroy(baked);
            return far == float.MinValue ? 0.04f : Mathf.Max(0f, far);
        }
        /// <summary>the backpack worn on the back in a match: hidden (it stood off the back); ?showbag=1 brings it back.
        /// The bag level itself (room for supplies) is not touched - only its model</summary>
        public static bool ShowBagInMatch = false;
        public void SetBag(int lv) => SetBag(lv, false);
        /// <summary>force: shown even while hidden in matches (the outfit test grid's ?wbag=)</summary>
        public void SetBag(int lv, bool force)
        {
            if (!ShowBagInMatch && !force) lv = 0;
            if (lv == bagLvShown) return;
            bagLvShown = lv;
            if (bagLook != null) Destroy(bagLook.gameObject);
            bagLook = null;
            if (lv <= 0) return;
            GameObject src = MapBuilder.Model("bag" + lv);
            if (src == null) return;
            bagLook = Instantiate(src, transform, false).transform;
            bagLook.localScale = Vector3.one; bagFit = 0f; bagBack = -1f; bagNative = GunFrame.LocalBounds(bagLook); // (sized to the wearer in PlaceBag)
            foreach (Renderer r in bagLook.GetComponentsInChildren<Renderer>(true)) { var mats = r.sharedMaterials; for (int k = 0; k < mats.Length; k++) mats[k] = Lit(mats[k]); r.sharedMaterials = mats; r.enabled = visible; }
        }

        void PlaceBag()
        {
            if (bagLook == null) return;
            Transform spine = PoseBone("Spine2") ?? PoseBone("Spine1") ?? PoseBone("Spine");
            if (spine == null) return;
            // follows the chest in every pose (standing, running, crawling, the chest turned by the rifle stance / aim)
            Quaternion bodyRot = ChestRot(spine);
            // sized to the wearer (all eight outfits differ): its height a share of the torso (hips to neck) - Lv1 55 %,
            // Lv2 63 %, Lv3 71 % - never wider than 85 % of the shoulders nor deeper than 28 cm; worked out once, from the
            // outfit's own bones
            Transform hips = PoseBone("Hips"), neck = PoseBone("Neck"), sl = PoseBone("LeftArm"), sr = PoseBone("RightArm");
            if (bagFit <= 0f)
            {
                float torso = hips != null && neck != null ? (neck.position - hips.position).magnitude : 0.5f;
                float shoulders = sl != null && sr != null ? (sl.position - sr.position).magnitude : 0.35f;
                Vector3 n = Vector3.Scale(bagNative.size, Vector3.one); // (model size at scale 1, its own axes)
                float wantH = torso * (0.55f + 0.08f * (Mathf.Clamp(bagLvShown, 1, 3) - 1)), maxW = shoulders * 0.85f;
                bagFit = Mathf.Min(Mathf.Min(n.y > 1e-3f ? wantH / n.y : 0.55f, n.x > 1e-3f ? maxW / n.x : 0.55f), n.z > 1e-3f ? 0.28f / n.z : 0.55f); // (and at most 28 cm deep)
                bagLook.localScale = Vector3.one * bagFit / Mathf.Max(1e-4f, transform.lossyScale.x);
            }
            float bh = bagNative.size.y * bagFit, bd = bagNative.size.z * bagFit;
            Vector3 up = bodyRot * Vector3.up, back = bodyRot * Vector3.back;
            Vector3 top = neck != null ? neck.position - up * 0.07f : spine.position + up * 0.12f;   // just under the neck
            // the back itself, from the wearer's own mesh: the farthest point behind the chest bone over the area the pack
            // covers (a tactical vest or a broad back stands out further than a thin suit); measured once, in the pose of the
            // moment it is first worn
            if (bagBack < 0f) bagBack = restBackSet ? restBack : MeasureBack(spine, up, back, top, bh, bagNative.size.x * bagFit);
            bagDepth = bd;
            // the player's own soldier and the lobby's: the back re-read from the mesh as it is now, five times a second -
            // stooping, slouching or leaning the back is not where it was standing straight (the empty-handed idle's back
            // is rounded: the pack stood off it), so the pack and the guns on it stay against it in every pose
            float torsoBack = (LiveBack && liveBackSet ? liveBack : bagBack) + 0.01f;
            Vector3 centreOff = bagNative.center * bagFit;                                           // (the model's own centre)
            // straps against the back, pockets out: the Lv1 / Lv3 models face the other way from Lv2 (their straps showed outside)
            bagLook.rotation = bodyRot * Quaternion.Euler(0, bagLvShown == 2 ? 180f : 0f, 0);
            bagLook.position = top - up * (bh / 2f) + back * (torsoBack + bd / 2f) - bagLook.rotation * centreOff;
        }

        Quaternion chestRestOff = Quaternion.identity; bool chestRestSet;
        Quaternion chestFrameOff = Quaternion.identity; bool chestFrameSet;
        public static bool UseChestFrame = true;
        Quaternion? ChestFrame()
        {
            Transform lo = PoseBone("Spine1") ?? PoseBone("Spine") ?? PoseBone("Hips"), nk = PoseBone("Neck"), la = PoseBone("LeftArm"), ra = PoseBone("RightArm");
            if (lo == null || nk == null || la == null || ra == null) return null;
            Vector3 up = nk.position - lo.position, side = ra.position - la.position;
            if (up.sqrMagnitude < 1e-6f || side.sqrMagnitude < 1e-6f) return null;
            Vector3 fwd = Vector3.Cross(side, up);
            if (fwd.sqrMagnitude < 1e-8f) return null;
            return Quaternion.LookRotation(fwd, up);
        }
        /// <summary>the chest's facing now (up = along the spine, forward = out of the chest), from the chest bone's turn
        /// since the rest pose - so the backpack and the guns on the back sit on the back however the chest is turned</summary>
        Quaternion ChestRot(Transform spine)
        {
            // from where the bones are, not how the chest bone is turned: a clip may twist that bone about its own length
            // without the body moving (the empty-handed idle did, and the pack and the guns on the back swung off behind
            // the shoulder). Up = lower spine to neck, across = shoulder to shoulder. (?chestbone=1: the bone's turn)
            if (UseChestFrame && chestFrameSet) { Quaternion? cf = ChestFrame(); if (cf.HasValue) return cf.Value * chestFrameOff; }
            if (!chestRestSet) { if (!bagOffsetSet) { bagOffsetSet = true; bagSpineOffset = Quaternion.Inverse(spine.rotation) * transform.rotation; } return spine.rotation * bagSpineOffset; }
            return spine.rotation * chestRestOff;
        }

        void PlaceBack()
        {
            ReadBackNow();
            PlaceBag();
            if (backGuns.Count == 0) return;
            Transform spine = PoseBone("Spine2") ?? PoseBone("Spine1") ?? PoseBone("Spine");
            if (spine == null) return;
            Quaternion bodyRot = ChestRot(spine);
            for (int i = 0; i < backGuns.Count; i++)
            {
                float side = i == 0 ? 1f : -1f;
                // diagonal across the back, the two crossed, flat against the body
                // against the back (its measured depth) - or just behind the backpack when there is one
                float rb = LiveBack && liveBackSet ? liveBack : restBack;
                float behind = restBackSet ? (bagLook != null ? rb + 0.01f + bagDepth + 0.03f : rb + 0.04f) : 0.17f + (bagLook != null ? 0.17f : 0f);
                backGuns[i].position = spine.position + bodyRot * new Vector3(-side * 0.04f, -0.05f, -(behind + i * 0.05f));
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

        /// <summary>benchmark mode: the animation graph is evaluated by Bench (timed) instead of by the Animator</summary>
        public void EvaluateGraph(float dt) { if (graph.IsValid() && graph.IsPlaying()) graph.Evaluate(dt); }
        void OnEnable() { if (Bench.Active && !Bench.Bodies.Contains(this)) Bench.Bodies.Add(this); }

        void LateUpdate()
        {
            long t0 = Bench.Now();
            LateUpdateBody();
            PlaceBack(); // (last: after the gun hold has turned the chest)
            Bench.Add(ref Bench.BodyTicks, t0);
        }

        void LateUpdateBody()
        {
            if (model != null) ApplyRoot();
            FreeArms();
            long tr = Bench.Now();
            Retarget();
            Bench.Add(ref Bench.TransferTicks, tr);
            FixFeet();
            ApplyPose();
            SlopeLie();
            if (Pose == "kneel" || (current != null && current.StartsWith("crouch"))) FixFeet(true); // (crouching on a slope)
            RunLife();
            RelaxEmptyHands();
            PlantFeet();
            LevelSoles();
            if (FootDebug && Time.time >= nextFootLog) { nextFootLog = Time.time + 2f; LogFeet(); }
            tr = Bench.Now();
            SyncExtras();
            Bench.Add(ref Bench.TransferTicks, tr);
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
            // gun: the right hand closes on the pistol grip, the left hand holds the handguard from below
            if (gun == null || handR == null) return;
            Vector3 dir = AimDir.sqrMagnitude > 1e-6f ? AimDir.normalized : transform.forward;
            Transform handL = PoseBone("LeftHand");
            bool carried = current == "run" || current == "prone" || Pose == "crawl";
            bool dead = current.StartsWith("death"), pistol = gunId.StartsWith("pistol");
            // long guns: held in the right hand as the rifle clips expect (also lying down / crawling)
            if (!dead && !pistol && clipIndex.ContainsKey("mx_aim") && (RifleClip || current == "prone" || Pose == "crawl"))
            {
                long ti = Bench.Now();
                ProneGunClear();
                if (UseWeaponIK) { if (weaponIK == null) weaponIK = new WeaponIKController(this); weaponIK.Apply(dir); }
                else RifleHold(dir); // (?wik=0: the previous hold, kept for comparison)
                Bench.Add(ref Bench.IKTicks, ti);
                return;
            }
            if (weaponIK != null) weaponIK.Release();
            // (the pistol clips hold the pistol up in front in every state, running too: held the same way, elbows a little bent)
            if (pistol && PistolClip) carried = false;
            bool aim = ShoulderGrip && !carried && (Pose == null || Pose == "kneel") && !dead;
            Vector3 grip = Vector3.zero; bool hasGrip = false;
            // standing / walking / firing: the stock sits at the shoulder and the gun reaches out along the aim
            if (aim)
            {
                Transform ra = PoseBone("RightArm");
                if (ra != null)
                {
                    // (pistol: held out with the arm nearly straight - 90 % of the arm's length - as a two-hand pistol stance;
                    // at 0.42 m the elbow folded and the forearm pointed up, bending the wrist)
                    Transform rfa = PoseBone("RightForeArm");
                    float armLen = rfa != null ? (rfa.position - ra.position).magnitude + (handR.position - rfa.position).magnitude : 0.5f;
                    // (running with it: 78 % - the elbows bend a little, as one runs holding a pistol)
                    float reach = playing == "p_run" ? 0.78f : 0.9f;
                    grip = Vector3.Lerp(handR.position, ra.position + dir * (pistol ? Mathf.Clamp(armLen * reach, 0.36f, 0.6f) : 0.26f) - transform.up * 0.07f + transform.TransformVector(GripOffset), 0.85f);
                    hasGrip = true;
                }
            }
            if (carried && pistol && (playing == "mx_prone" || playing == "mx_crawl" || playing == "mx_crawl_sym"))
            {
                // lying down with a pistol: held out ahead in the right hand (the clip's hands are set for a rifle's length)
                dir = Vector3.RotateTowards(transform.forward, dir, 25f * Mathf.Deg2Rad, 0f);
                carried = false; // (the left hand stays as the clip has it)
                hasGrip = false;
                proneOneHand = true;
            }
            else proneOneHand = false;
            if (carried && handL != null)
            {
                // carried (running / crawling): along the two hands, as the clip holds it, never swung out to the side
                Vector3 lh = handL.position - handR.position; float len = lh.magnitude;
                if (len > 0.15f && len < 0.75f && Vector3.Dot(lh, transform.forward) > 0.04f)
                {
                    Vector3 front = (transform.forward - transform.up * 0.35f).normalized;
                    dir = Vector3.RotateTowards(front, lh / len, 30f * Mathf.Deg2Rad, 0f);
                }
                else carried = false;
            }
            gun.rotation = Quaternion.LookRotation(-dir, Vector3.up);
            Vector3 up = gun.up, side = Vector3.Cross(up, dir).normalized; // side: the shooter's right
            if (!HandGrip || dead) gun.position = handR.position;
            else
            {
                // right hand: the back of the hand nearly in line with the forearm (a pistol grip is held with a straight wrist:
                // the knuckles PistolWrist degrees below the aim, was 63 - the wrist looked broken), fingers round the grip,
                // palm against its right side; long guns as before
                float wr = pistol ? PistolWrist : 63f;
                Vector3 k = (dir * Mathf.Cos(wr * Mathf.Deg2Rad) - up * Mathf.Sin(wr * Mathf.Deg2Rad)).normalized, n = -side;
                float palm = PalmLen(handR, "Right");
                Transform rf = PoseBone("RightForeArm"), ra = PoseBone("RightArm");
                if (hasGrip && ra != null && rf != null) TwoBone(ra, rf, handR, grip - n * 0.022f - k * palm);
                // (pistol: the hand never bends more than 25 degrees off its forearm)
                if (pistol && rf != null) { Vector3 fa = (handR.position - rf.position).normalized; k = Vector3.RotateTowards(fa, k, 25f * Mathf.Deg2Rad, 0f); n = Vector3.ProjectOnPlane(n, k).normalized; }
                if (rf != null) OrientHand(handR, rf, "Right", k, n);
                Curl("Right", 78f, 82f, 48f, 30f);
                // the pistol grip (a little below the gun's origin) sits in the closed hand
                Vector3 g = handR.position + HandDir(handR, "Right") * palm + PalmNormal(handR, "Right") * 0.022f;
                gun.position = g + up * (pistol ? 0.035f : 0.05f);
                // pistol: the index finger out of the fist, on the trigger (ahead of the grip, just below the slide); the
                // middle, ring and little fingers stay round the grip
                if (pistol && UseTriggerFinger) TriggerFinger(gun.position + dir * TriggerAhead - up * TriggerDown, dir);
            }
            // standing / walking / firing: the support hand comes to the gun
            if (!carried && !proneOneHand && handL != null && (Pose == null || Pose == "kneel") && !dead)
            {
                Transform la = PoseBone("LeftArm"), lf = PoseBone("LeftForeArm");
                if (la != null && lf != null)
                {
                    if (!HandGrip) { TwoBone(la, lf, handL, gun.position + dir * GripAhead(gunId) - gun.up * 0.035f); return; }
                    float palm = PalmLen(handL, "Left");
                    if (pistol) PistolSupport(handL, la, lf, palm, dir, up, side);
                    else
                    {
                        // under the handguard: palm up, fingers round its right side
                        Vector3 k = (side * 0.75f + dir * 0.66f).normalized, n = up;
                        Vector3 c = gun.position + dir * GripAhead(gunId) - up * 0.015f;
                        TwoBone(la, lf, handL, c - k * palm * 0.7f);
                        OrientHand(handL, lf, "Left", k, n);
                        Curl("Left", 48f, 55f, 32f, 0f, dir);
                    }
                }
            }
            else if (HandGrip && handL != null && !dead) Curl("Left", 50f, 55f, 35f, 20f);
            if (GunDebug && pistol && Time.time >= nextWristLog) SleeveProbe();
            if (GunDebug && pistol && Time.time >= nextWristLog)
            {
                nextWristLog = Time.time + 1.5f;
                Debug.Log($"[LastWar] wrist {name} {(outfitRoot != null ? outfitRoot.name.Replace("(Clone)", "") : "-")} {playing}: right {WristBend(handR, "Right"):0} deg left {WristBend(handL, "Left"):0} deg (pistol wrist {PistolWrist:0}) trigger gap {lastTriggerGap * 100f:0.0} cm grip gap {GripGap() * 100f:0.0} cm left hand to right {(handL != null && handR != null ? (handL.position - handR.position).magnitude * 100f : -1f):0.0} cm, left palm off the grip {supportErr * 100f:0.0} cm (side {supportSide * 100f:0.0} up {supportUp * 100f:0.0} fwd {supportFwd * 100f:0.0}), left wrist bend {supportBend:0} deg");
            }
        }

        bool proneOneHand;

        /// <summary>the closed right hand's centre to the pistol's grip (below its origin): 0 = held in the hand</summary>
        /// <summary>?gundbg=1: each outfit mesh's vertices bound mostly to the right forearm, baked as drawn now - how far
        /// they sit from the forearm the hold placed (elbow to wrist), and whether the mesh moves with that very bone</summary>
        void SleeveProbe()
        {
            Transform fa = PoseBone("RightForeArm"), hd = PoseBone("RightHand");
            if (fa == null || hd == null || outfitRoot == null) return;
            foreach (var smr in outfitRoot.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                Mesh m = smr.sharedMesh; if (m == null || !m.isReadable) { Debug.Log($"[LastWar] sleeve {smr.name}: mesh not readable"); continue; }
                int bi = System.Array.IndexOf(smr.bones, fa);
                string sameName = "";
                if (bi < 0) for (int i = 0; i < smr.bones.Length; i++) if (smr.bones[i] != null && smr.bones[i].name == fa.name) { bi = i; sameName = $" (a different bone with its name, {(smr.bones[i].position - fa.position).magnitude * 100f:0.0} cm off)"; }
                if (bi < 0) { Debug.Log($"[LastWar] sleeve {smr.name}: no forearm bone"); continue; }
                var bw = m.boneWeights; var baked = new Mesh(); smr.BakeMesh(baked, true); var v = baked.vertices;
                Matrix4x4 w = smr.transform.localToWorldMatrix; double sum = 0; int n = 0; float worst = 0f;
                Vector3 a = fa.position, b = hd.position, ab = b - a;
                for (int i = 0; i < bw.Length && i < v.Length; i++)
                {
                    if (bw[i].boneIndex0 != bi || bw[i].weight0 < 0.6f) continue;
                    Vector3 p = w.MultiplyPoint3x4(v[i]); float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
                    float d = (p - (a + ab * t)).magnitude; sum += d; n++; if (d > worst) worst = d;
                }
                Destroy(baked);
                Debug.Log($"[LastWar] sleeve {name} {smr.name}: {n} forearm vertices, mean {(n > 0 ? sum / n * 100 : -1):0.0} cm max {worst * 100f:0.0} cm from the forearm{sameName}; quality {smr.quality}, {QualitySettings.skinWeights}");
            }
        }

        /// <summary>pistol: how far the left palm ended from where it should be, and the left wrist's bend (?gundbg=1 log)</summary>
        float supportErr, supportSide, supportUp, supportFwd, supportBend;
        /// <summary>pistol, two hands (thumbs forward): the left palm pressed on the left side of the grip, over the right
        /// hand's fingers (a little low: it holds the bottom of the grip); its knuckles 40 degrees below the aim, the fingers
        /// wrapped round the front of the right hand's fingers, the thumb forward along the frame under the slide. The wrist
        /// bends at most 50 degrees off the forearm; the palm is then put back on its place (the arm reaches again), so the
        /// limit never pulls the hand off the gun (it did: the hand turned about the wrist and stood off the right hand)</summary>
        void PistolSupport(Transform handL, Transform la, Transform lf, float palm, Vector3 dir, Vector3 up, Vector3 side)
        {
            Vector3 grip = gun.position - up * 0.035f;
            Vector3 want = grip - side * SupportSide - up * 0.012f + dir * 0.004f;   // the palm's surface
            float lw = SupportKnuckles * Mathf.Deg2Rad;
            Vector3 k0 = (dir * Mathf.Cos(lw) - up * Mathf.Sin(lw)).normalized, n0 = side;
            Vector3 wrist = want - k0 * palm * 0.6f - n0 * 0.012f;
            Vector3 k = k0, n = n0;
            for (int it = 0; it < 3; it++)
            {
                TwoBone(la, lf, handL, wrist);
                Vector3 fa = (handL.position - lf.position).normalized;
                k = Vector3.RotateTowards(fa, k0, SupportBendMax * Mathf.Deg2Rad, 0f);
                n = Vector3.ProjectOnPlane(n0, k).normalized;
                OrientHand(handL, lf, "Left", k, n);
                Vector3 at = handL.position + HandDir(handL, "Left") * palm * 0.6f + PalmNormal(handL, "Left") * 0.012f;
                Vector3 e = want - at;
                if (e.sqrMagnitude < 1e-6f) break;
                wrist = handL.position + e;
            }
            Vector3 got = handL.position + HandDir(handL, "Left") * palm * 0.6f + PalmNormal(handL, "Left") * 0.012f, d = got - want;
            supportErr = d.magnitude; supportSide = Vector3.Dot(d, side); supportUp = Vector3.Dot(d, up); supportFwd = Vector3.Dot(d, dir);
            supportBend = Vector3.Angle(HandDir(handL, "Left"), (handL.position - lf.position));
            Curl("Left", SupportCurl, SupportCurl + 25f, SupportCurl, 0f, dir);
        }
        /// <summary>?psup=side,knuckles,bend,curl : the left palm's distance left of the grip's middle (m), its knuckles below
        /// the aim (deg), the wrist's most bend (deg), the finger curl (deg)</summary>
        public static float SupportSide = 0.04f, SupportKnuckles = 40f, SupportBendMax = 50f, SupportCurl = 45f;
        /// <summary>tests (?lhk= ?lhn= ?hwmax=): the left hand on a hanging rifle (WeaponIKController)</summary>
        public static Vector3 HangGripK => WeaponIKController.HangK;
        public static Vector3 HangGripN => WeaponIKController.HangN;
        public static void HangGrip(Vector3 k, Vector3 n) { WeaponIKController.HangK = k; WeaponIKController.HangN = n; }
        public static float HangWrist { get => WeaponIKController.HangWristMax; set => WeaponIKController.HangWristMax = value; }

        float GripGap()
        {
            if (gun == null || handR == null) return -1f;
            float palm = PalmLen(handR, "Right");
            Vector3 g = handR.position + HandDir(handR, "Right") * palm + PalmNormal(handR, "Right") * 0.022f;
            return (g - (gun.position - gun.up * 0.035f)).magnitude;
        }

        /// <summary>the torso as posed now: hips to the base of the neck (aim assist aims at it); false = no skeleton</summary>
        public bool TorsoLine(out Vector3 lo, out Vector3 hi)
        {
            Transform h = PoseBone("Hips"), n = PoseBone("Neck") ?? PoseBone("Spine2");
            lo = h != null ? h.position : transform.position; hi = n != null ? n.position : lo;
            if (h == null || n == null) return false;
            hi = Vector3.Lerp(lo, hi, 0.92f);
            return true;
        }

        /// <summary>?trigfinger=0: the index finger curled round the grip with the others, as before</summary>
        public static bool UseTriggerFinger = true;
        /// <summary>the pistol's trigger from its origin: ahead along the barrel and down (m)</summary>
        public static float TriggerAhead = 0.028f, TriggerDown = 0.022f;
        float lastTriggerGap = -1f;

        /// <summary>the right index finger laid on the trigger at `t`: its first bone turned from the knuckle toward a point a
        /// little short of the trigger, the two outer bones bent round it (a finger hooked on a trigger)</summary>
        void TriggerFinger(Vector3 t, Vector3 dir)
        {
            Transform i1 = PoseBone("RightHandIndex" + knuckle), i2 = PoseBone("RightHandIndex" + (knuckle + 1)), i3 = knuckle + 2 <= 3 ? PoseBone("RightHandIndex" + (knuckle + 2)) : null;
            if (i1 == null || i2 == null || fingerRest == null) return;
            foreach (var kv in fingerRest) if (kv.Key == i1 || kv.Key == i2 || kv.Key == i3) kv.Key.localRotation = kv.Value;
            float seg1 = (i2.position - i1.position).magnitude;
            Vector3 k = HandDir(handR, "Right"), n = PalmNormal(handR, "Right"), axis = Vector3.Cross(k, n).normalized;
            // the first bone toward the trigger (it reaches the trigger with its middle joint, then hooks round it)
            Vector3 want = t - i1.position; if (want.magnitude < 1e-4f) return;
            Vector3 cur = i2.position - i1.position;
            i1.rotation = Quaternion.FromToRotation(cur, want.normalized * seg1) * i1.rotation;
            i2.rotation = Quaternion.AngleAxis(48f, axis) * i2.rotation;
            if (i3 != null) i3.rotation = Quaternion.AngleAxis(30f, axis) * i3.rotation;
            // (measure: how far the finger's middle joint sits from the trigger)
            lastTriggerGap = (i2.position - t).magnitude;
        }

        // ---- Step F: lying down / crawling, the body follows the slope it lies on (the clips lie on flat ground: on a hill
        // the head end dug in or hung in the air). The ground is read ahead of / behind the hips and to their left / right;
        // the whole body turns about the point under the hips by the slope's pitch and roll (each at most 35 degrees),
        // eased in and out. Standing poses are not touched. ?slope=0 turns it off.
        public static bool UseSlopeLie = true;
        float slopeW, nextSlopeLog; Quaternion slopeQ = Quaternion.identity;
        static bool Lying(string clip) => clip == "mx_prone" || clip == "mx_crawl" || clip == "mx_crawl_sym" || clip == "fc_sym";
        void SlopeLie()
        {
            bool lying = UseSlopeLie && visible && (Lying(playing) || Pose == "crawl" || current == "prone");
            slopeW = Mathf.MoveTowards(slopeW, lying ? 1f : 0f, Time.deltaTime * 4f);
            if (slopeW <= 0f || dstHips == null) return;
            Quaternion face = transform.rotation * Quaternion.Euler(0, Turn, 0);
            Vector3 f = face * Vector3.forward, r = face * Vector3.right, c = transform.position;
            float hF = GroundY(c + f * 0.75f, c.y), hB = GroundY(c - f * 0.75f, c.y), hL = GroundY(c - r * 0.3f, c.y), hR = GroundY(c + r * 0.3f, c.y);
            float pitch = Mathf.Clamp(Mathf.Atan2(hF - hB, 1.5f) * Mathf.Rad2Deg, -35f, 35f), roll = Mathf.Clamp(Mathf.Atan2(hR - hL, 0.6f) * Mathf.Rad2Deg, -35f, 35f);
            // (front higher: the head end turns up; right side higher: the body rolls onto it)
            Quaternion want = Quaternion.AngleAxis(-pitch, r) * Quaternion.AngleAxis(roll, f);
            slopeQ = Quaternion.Slerp(slopeQ, want, Mathf.Min(1f, Time.deltaTime * 8f));
            Quaternion q = Quaternion.Slerp(Quaternion.identity, slopeQ, slopeW);
            Vector3 pivot = new Vector3(c.x, GroundY(c, c.y), c.z);
            dstHips.position = pivot + q * (dstHips.position - pivot);
            dstHips.rotation = q * dstHips.rotation;
            if (GunDebug && Time.time >= nextSlopeLog) { nextSlopeLog = Time.time + 1.5f; Debug.Log($"[LastWar] slope {name} {playing}: ground pitch {pitch:0} roll {roll:0} deg, applied {slopeW:0.00}"); }
        }
        float GroundY(Vector3 p, float fallback)
        {
            int mask = ~(1 << MapBuilder.PlayerLayer);
            return Physics.Raycast(p + Vector3.up * 1.2f, Vector3.down, out RaycastHit h, 3f, mask, QueryTriggerInteraction.Ignore) ? h.point.y : fallback;
        }
        /// <summary>pistol: how far (degrees) the back of the right hand points below the aim line (?pwrist= to try others)</summary>
        public static float PistolWrist = 22f;
        /// <summary>readout (?gundbg=1): the wrist's bend, forearm against the back of the hand (degrees)</summary>
        float WristBend(Transform hand, string sd)
        {
            Transform fa = PoseBone(sd + "ForeArm"); if (fa == null || hand == null) return float.NaN;
            return Vector3.Angle(hand.position - fa.position, HandDir(hand, sd));
        }
        float nextWristLog;
        // ---- the rifle clips hold the hands where a rifle goes: the gun lies from the right palm to the left one
        Vector3 Palm(string sd)
        {
            Transform h = sd == "Right" ? handR : PoseBone("LeftHand"), m = PoseBone(sd + "HandMiddle1");
            if (h == null) return Vector3.zero;
            return m != null ? Vector3.Lerp(h.position, m.position, 0.8f) : h.position;
        }

        // The gun is fixed in the right hand the way the rifle clips were made for (measured once on the soldier from the
        // aiming clip: grip in the right palm, barrel toward the left hand), so it turns with the wrist like a held gun;
        // the left hand is then put on the handguard. While aiming, the chest turns so the barrel meets the crosshair.
        bool rifleCal; Quaternion gunInHand = Quaternion.identity; Vector3 gunAtHand = Vector3.zero;
        Transform SrcBone(string key) { foreach (Transform t in srcBones) if (BoneKey(t.name) == key) return t; return null; }

        /// <summary>
        /// The right hand's grip contact in the current source pose, in the outfit right hand's own space: the outfit's middle
        /// finger chain (Middle1..Middle4) laid out from the hand with the source pose's bone rotations (as the transfer turns
        /// them) and the outfit's own bone lengths - nothing is moved. Contact = halfway between the palm point (80% of the
        /// way to the middle knuckle) and the middle fingertip.
        /// </summary>
        internal bool TryGripContact(int handIndex, out Vector3 contact, out Vector3 palm)
        {
            contact = palm = Vector3.zero;
            if (handIndex < 0 || handIndex >= boneMap.Count || handR == null) return false;
            Quaternion hand = srcBones[handIndex].rotation * boneMap[handIndex], rot = hand;
            Vector3 pos = Vector3.zero, knuckle = Vector3.zero; int n = 0;
            Transform parent = handR;
            foreach (string f in new[] { "RightHandMiddle1", "RightHandMiddle2", "RightHandMiddle3", "RightHandMiddle4" })
            {
                Transform d = PoseBone(f);
                if (d == null || d.parent != parent) break;                        // (the chain must be the outfit's own)
                pos += rot * (d.localPosition * parent.lossyScale.x);
                if (n == 0) knuckle = pos;
                Transform src = SrcBone(f); int si = src != null ? srcBones.IndexOf(src) : -1;
                rot = si >= 0 && si < boneMap.Count ? src.rotation * boneMap[si] : rot * d.localRotation;
                parent = d; n++;
            }
            if (n < 3) return false;                                               // (no usable finger chain: keep the palm point)
            Quaternion inv = Quaternion.Inverse(hand);
            palm = inv * (knuckle * 0.8f);
            contact = inv * ((knuckle * 0.8f + pos) * 0.5f);
            return true;
        }

        void CalibrateRifle()
        {
            rifleCal = true;
            if (!clipIndex.TryGetValue("mx_aim", out int ai) || animRoot == null) return;
            Transform sh = SrcBone("RightHand"), sl = SrcBone("LeftHand"), smr = SrcBone("RightHandMiddle1"), sml = SrcBone("LeftHandMiddle1");
            if (sh == null || sl == null) return;
            int hi = srcBones.IndexOf(sh); if (hi < 0 || hi >= boneMap.Count) return;
            Vector3 before = sh.position;
            clips[ai].SampleAnimation(animRoot, 0.4f);
            if (GunDebug) Debug.Log($"[LastWar] gundbg calibrate: sample moved the base right hand {(sh.position - before).magnitude:0.000} m (0 = SampleAnimation did nothing), playing {playing}");
            Vector3 rp = smr != null ? Vector3.Lerp(sh.position, smr.position, 0.8f) : sh.position;
            Vector3 lp = sml != null ? Vector3.Lerp(sl.position, sml.position, 0.8f) : sl.position;
            Vector3 d = (lp - rp).normalized;
            Quaternion g = Quaternion.LookRotation(-d, transform.up);
            Vector3 gp = rp + g * Vector3.up * 0.05f;
            Quaternion hand = sh.rotation * boneMap[hi]; // the outfit's right hand in that pose
            gunInHand = Quaternion.Inverse(hand) * g;
            gunAtHand = Quaternion.Inverse(hand) * (gp - sh.position);
        }

        void RifleHold(Vector3 aim)
        {
            if (!rifleCal) CalibrateRifle();
            Transform hl = PoseBone("LeftHand");
            Quaternion gr = handR.rotation * gunInHand;
            // aiming / firing / walking with the gun up: the chest turns (over the three spine bones) to the crosshair
            if (ShoulderGrip && (playing == "mx_aim" || playing == "mx_fire" || playing == "mx_crouch_aim" || playing == "mx_crouch_fire" || playing == "mx_walk"))
            {
                Vector3 barrel = gr * Vector3.back;
                Quaternion q = Quaternion.FromToRotation(barrel, aim);
                q.ToAngleAxis(out float ang, out Vector3 ax); if (ang > 180f) ang -= 360f;
                if (Mathf.Abs(ang) > 0.5f && ax.sqrMagnitude > 0.5f)
                {
                    Quaternion part = Quaternion.AngleAxis(Mathf.Clamp(ang, -70f, 70f) / 3f, ax);
                    foreach (string b in new[] { "Spine", "Spine1", "Spine2" }) { Transform t = PoseBone(b); if (t != null) t.rotation = part * t.rotation; }
                    gr = handR.rotation * gunInHand;
                }
            }
            gun.rotation = gr;
            gun.position = handR.position + handR.rotation * gunAtHand;
            if (GunDebug && Time.time > nextGunLog)
            {
                nextGunLog = Time.time + 1.5f;
                Vector3 barrel = gr * Vector3.back, lp = Palm("Left"), rp = Palm("Right");
                Vector3 f = transform.forward, r = transform.right;
                Vector3 stock = gun.position - barrel * 0.35f;
                Transform chest = PoseBone("Spine2");
                Debug.Log($"[LastWar] gundbg {playing}: barrel vs aim {Vector3.Angle(barrel, aim):0} deg, barrel yaw vs body {Vector3.SignedAngle(Vector3.ProjectOnPlane(f, Vector3.up), Vector3.ProjectOnPlane(barrel, Vector3.up), Vector3.up):0} deg (+ = to the right), pitch {Mathf.Asin(Mathf.Clamp(barrel.y, -1, 1)) * Mathf.Rad2Deg:0}, palm line vs barrel {Vector3.Angle(lp - rp, barrel):0} deg, stock side {(chest != null ? Vector3.Dot(stock - chest.position, r) : 0):0.00} m (+ = right), grip-left palm {(lp - rp).magnitude:0.00} m");
                // what is actually drawn: the gun mesh's world box (its long side = the barrel's axis), the hips' facing
                Bounds gb = new Bounds(gun.position, Vector3.zero); foreach (Renderer gr2 in gun.GetComponentsInChildren<Renderer>()) gb.Encapsulate(gr2.bounds);
                Transform hipsB = PoseBone("Hips"), head = PoseBone("Head");
                Vector3 hl2 = PoseBone("LeftArm") != null && PoseBone("RightArm") != null ? PoseBone("LeftArm").position - PoseBone("RightArm").position : Vector3.zero;
                Debug.Log($"[LastWar] gundbg2 {name}: transform fwd {transform.forward:F2} right {transform.right:F2} | gun box size {gb.size:F2} centre-from-body {transform.InverseTransformPoint(gb.center):F2} | barrel {barrel:F2} | right->left shoulder {hl2.normalized:F2} | gun child local rot {(gun.childCount > 0 ? gun.GetChild(0).localEulerAngles : Vector3.zero):F0} | head-from-body {(head != null ? transform.InverseTransformPoint(head.position) : Vector3.zero):F2}");
            }
            // the support hand on the handguard (not while it changes the magazine)
            if (hl != null && playing != "mx_reload")
            {
                Transform la = PoseBone("LeftArm"), lf = PoseBone("LeftForeArm");
                Vector3 barrel = gr * Vector3.back;
                Vector3 guard = gun.position + barrel * GripAhead(gunId) - gun.up * 0.035f;
                Vector3 palmOff = Palm("Left") - hl.position;
                if (la != null && lf != null && (Palm("Left") - guard).magnitude < 0.45f) TwoBone(la, lf, hl, guard - palmOff);
            }
        }

        // ---- hands closed on the gun (works for both rigs: built from the knuckles' positions, not the bone axes)
        public static bool HandGrip = true;
        // ---- lying down with a long gun: the clips lie on flat ground, so on grass rising ahead (or with a long barrel)
        // the gun went into the ground. Where the gun lay last frame is checked against the ground under it (three points
        // along it); when it dips in, the chest is raised (the spine bends back, up to 18 degrees, eased) just enough for
        // the gun - held from the hands as always - to lie 3 cm above it. Standing / crouching: untouched. ?pronegun=0: off.
        public static bool UseProneGunClear = true;
        float proneLift; Renderer[] gunRends; Transform gunRendsOf;
        void ProneGunClear()
        {
            bool lying = UseProneGunClear && visible && gun != null && (Lying(playing) || current == "prone" || Pose == "crawl");
            float want = 0f;
            if (lying)
            {
                if (gunRendsOf != gun) { gunRends = gun.GetComponentsInChildren<Renderer>(true); gunRendsOf = gun; }
                Bounds b = default; bool any = false;
                foreach (Renderer r in gunRends) { if (r == null) continue; if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
                Transform sp = PoseBone("Spine");
                if (any && sp != null)
                {
                    Vector3 along = gun.rotation * Vector3.back; float len = Vector3.Dot(b.extents, new Vector3(Mathf.Abs(along.x), Mathf.Abs(along.y), Mathf.Abs(along.z)));
                    int mask = ~(1 << MapBuilder.PlayerLayer); float need = float.MinValue;
                    for (int i = -1; i <= 1; i++)
                    {
                        Vector3 pnt = b.center + along * (len * 0.8f * i);
                        if (Physics.Raycast(pnt + Vector3.up * 1.5f, Vector3.down, out RaycastHit h, 3f, mask, QueryTriggerInteraction.Ignore))
                        {
                            // (the gun's lowest edge near that point: its centre line minus half its height)
                            float low = pnt.y - Mathf.Min(b.extents.y, 0.06f);
                            float dist = Mathf.Max(0.3f, Vector3.ProjectOnPlane(pnt - sp.position, transform.up).magnitude);
                            need = Mathf.Max(need, Mathf.Atan2(h.point.y + 0.03f - low, dist) * Mathf.Rad2Deg);
                        }
                    }
                    // (need < 0: the gun is clear - the chest comes back down toward the clip's own pose)
                    want = need == float.MinValue ? proneLift : Mathf.Clamp(proneLift + need, 0f, 18f);
                }
            }
            proneLift = Mathf.MoveTowards(proneLift, want, Time.deltaTime * (want > proneLift ? 60f : 25f));
            if (proneLift < 0.05f) return;
            Vector3 side = Quaternion.Euler(0, Turn, 0) * transform.right;
            Transform s0 = PoseBone("Spine"), s1 = PoseBone("Spine1");
            if (s0 != null) s0.rotation = Quaternion.AngleAxis(-proneLift * 0.6f, side) * s0.rotation;
            if (s1 != null) s1.rotation = Quaternion.AngleAxis(-proneLift * 0.4f, side) * s1.rotation;
        }
        public static bool UseWeaponIK = true, UseShoulderReach = true, UseHoldBack = true; WeaponIKController weaponIK; // (?wikA=0 / ?wikB=0)
        public static int WikFrames { get => WeaponIKController.FramesToLog; set => WeaponIKController.FramesToLog = value; }
        public static bool GunDebug; float nextGunLog; // ?gundbg=1
        /// <summary>where the grip sits relative to the usual shoulder-aim spot (body space); the lobby moves it down to the waist</summary>
        public Vector3 GripOffset;
        /// <summary>the back re-read from the mesh as it is now (the player's and the lobby's soldier: see PlaceBag)</summary>
        public bool LiveBack; float liveBack, nextBackRead; bool liveBackSet;
        /// <summary>the player's own soldier and the lobby's: how far the back stands out behind the chest bone, re-read from
        /// the mesh as it is now, five times a second (as the rest measure: under the neck, 71 % of the torso down, 85 % of
        /// the shoulders across). Stooping or slouching the back is not where it was standing straight - the empty-handed
        /// idle's back is rounded and the pack and the guns stood off it - so they stay against it in every pose.</summary>
        void ReadBackNow()
        {
            if (!LiveBack || !visible || outfitRoot == null || Time.time < nextBackRead) return;
            nextBackRead = Time.time + 0.2f;
            Transform sp = PoseBone("Spine2") ?? PoseBone("Spine1") ?? PoseBone("Spine"), hp = PoseBone("Hips"), nk = PoseBone("Neck"), sl = PoseBone("LeftArm"), sr = PoseBone("RightArm");
            if (sp == null || nk == null) return;
            Quaternion rot = ChestRot(sp); Vector3 up = rot * Vector3.up, bk = rot * Vector3.back;
            float torso = hp != null ? (nk.position - hp.position).magnitude : 0.5f, shoulders = sl != null && sr != null ? (sl.position - sr.position).magnitude : 0.35f;
            float m = MeasureBack(sp, up, bk, nk.position - up * 0.07f, torso * 0.71f, shoulders * 0.85f);
            liveBack = liveBackSet ? Mathf.Lerp(liveBack, m, 0.6f) : m; liveBackSet = true;
        }
        static readonly string[] FingerNames = { "Index", "Middle", "Ring", "Pinky" };
        Dictionary<Transform, Quaternion> fingerRest;

        int knuckle = 1; // the bone level at the knuckles: 1 on mixamo rigs, 2 on Quaternius (its first finger bones all start in the palm)

        void CacheFingerRest()
        {
            fingerRest = new Dictionary<Transform, Quaternion>();
            Transform i1 = PoseBone("RightHandIndex1"), p1 = PoseBone("RightHandPinky1"), m1 = PoseBone("RightHandMiddle1");
            knuckle = i1 != null && p1 != null && m1 != null && handR != null && PoseBone("RightHandIndex2") != null
                && (i1.position - p1.position).magnitude < 0.25f * (m1.position - handR.position).magnitude + 1e-4f ? 2 : 1;
            foreach (string sd in new[] { "Left", "Right" })
                foreach (string f in new[] { "Index", "Middle", "Ring", "Pinky", "Thumb" })
                    for (int i = 1; i <= 3; i++) { Transform b = PoseBone(sd + "Hand" + f + i); if (b != null) fingerRest[b] = b.localRotation; }
        }

        /// <summary>wrist to the knuckles, along the fingers</summary>
        Vector3 HandDir(Transform hand, string sd)
        {
            Transform m = PoseBone(sd + "HandMiddle" + knuckle);
            Vector3 d = m != null ? m.position - hand.position : hand.rotation * Vector3.up;
            return d.sqrMagnitude > 1e-8f ? d.normalized : hand.up;
        }

        float PalmLen(Transform hand, string sd)
        {
            Transform m = PoseBone(sd + "HandMiddle" + knuckle);
            return m != null ? Mathf.Clamp((m.position - hand.position).magnitude * 0.8f, 0.04f, 0.12f) : 0.07f;
        }

        /// <summary>out of the palm (right hand: knuckles x (pinky to index); the left hand is mirrored)</summary>
        Vector3 PalmNormal(Transform hand, string sd)
        {
            Transform ix = PoseBone(sd + "HandIndex" + knuckle), pk = PoseBone(sd + "HandPinky" + knuckle);
            Vector3 k = HandDir(hand, sd);
            if (ix == null || pk == null) return sd == "Right" ? -hand.right : hand.right;
            Vector3 n = Vector3.Cross(k, ix.position - pk.position);
            if (sd == "Left") n = -n;
            return n.sqrMagnitude > 1e-10f ? n.normalized : -hand.forward;
        }

        /// <summary>turns the hand so its fingers point along k and its palm faces n; half the twist is taken by the forearm</summary>
        void OrientHand(Transform hand, Transform fore, string sd, Vector3 k, Vector3 n)
        {
            Vector3 k0 = HandDir(hand, sd), n0 = PalmNormal(hand, sd);
            n0 = Vector3.ProjectOnPlane(n0, k0); n = Vector3.ProjectOnPlane(n, k);
            if (n0.sqrMagnitude < 1e-8f || n.sqrMagnitude < 1e-8f) return;
            Quaternion delta = Quaternion.LookRotation(k, n) * Quaternion.Inverse(Quaternion.LookRotation(k0, n0));
            Quaternion want = delta * hand.rotation;
            Vector3 axis = hand.position - fore.position;
            if (axis.sqrMagnitude > 1e-8f)
            {
                axis.Normalize();
                Vector3 p = Vector3.Project(new Vector3(delta.x, delta.y, delta.z), axis);
                var twist = new Quaternion(p.x, p.y, p.z, delta.w);
                float m = Mathf.Sqrt(twist.x * twist.x + twist.y * twist.y + twist.z * twist.z + twist.w * twist.w);
                if (m > 1e-6f)
                {
                    twist = new Quaternion(twist.x / m, twist.y / m, twist.z / m, twist.w / m);
                    fore.rotation = Quaternion.Slerp(Quaternion.identity, twist, 0.5f) * fore.rotation;
                }
            }
            hand.rotation = want;
        }

        /// <summary>crouching with empty hands: the crouch clips were recorded holding a rifle (fists round a grip) - with
        /// nothing in the hands the fingers rest open, lightly bent (the snowball hand is left as it is)</summary>
        void RelaxEmptyHands()
        {
            if (!visible || current == null || (gun != null && Armed)) return;
            if (!(current.StartsWith("crouch") || Pose == "kneel") || playing == "kn_free") return; // (the crouch pose has its own open hands)
            Curl("Left", 14f, 18f, 10f, 0f);
            if (!Snowball) Curl("Right", 14f, 18f, 10f, 0f);
        }

        /// <summary>closes the fingers toward the palm (base, middle, tip angles) and brings the thumb round</summary>
        void Curl(string sd, float a1, float a2, float a3, float thumb, Vector3 thumbAlong = default)
        {
            if (fingerRest == null) return;
            Transform hand = sd == "Right" ? handR : PoseBone("LeftHand");
            if (hand == null) return;
            foreach (var kv in fingerRest) if (kv.Key.name.Contains(sd + "Hand")) kv.Key.localRotation = kv.Value;
            Vector3 k = HandDir(hand, sd), n = PalmNormal(hand, sd);
            Vector3 axis = Vector3.Cross(k, n).normalized;
            float[] ang = { a1, a2, a3 };
            foreach (string f in FingerNames)
                for (int i = knuckle; i <= 3; i++) { Transform b = PoseBone(sd + "Hand" + f + i); if (b != null) b.rotation = Quaternion.AngleAxis(ang[i - knuckle], axis) * b.rotation; }
            // the thumb laid along a direction (the left thumb along the handguard)
            Transform t1 = PoseBone(sd + "HandThumb" + knuckle), t2 = PoseBone(sd + "HandThumb" + (knuckle + 1));
            if (thumbAlong != default && t1 != null && t2 != null)
            {
                Vector3 td = t2.position - t1.position;
                if (td.sqrMagnitude > 1e-10f) t1.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(td, thumbAlong), 0.75f) * t1.rotation;
                return;
            }
            for (int i = 1; i <= 3; i++)
            {
                Transform b = PoseBone(sd + "HandThumb" + i), c = PoseBone(sd + "HandThumb" + (i + 1));
                if (b == null) break;
                Vector3 td = (c != null ? c.position : b.position + (b.position - b.parent.position)) - b.position;
                Vector3 ax = Vector3.Cross(td, n);
                if (ax.sqrMagnitude > 1e-10f) b.rotation = Quaternion.AngleAxis(thumb, ax.normalized) * b.rotation;
            }
        }

        /// <summary>the head / legs / feet from other outfits take the leading outfit's pose</summary>
        void SyncExtras()
        {
            foreach (Extra x in extras)
            {
                // the pieces of another outfit share the leading skeleton: each joint sits where the leading joint is
                // (another body's bone lengths would otherwise leave gaps at the waist / ankles), turned by the rest map
                for (int i = 0; i < x.from.Count; i++) { x.to[i].rotation = x.from[i].rotation * x.map[i]; if (ExtraPos) x.to[i].position = x.from[i].position; }
                if (x.hips != null)
                {
                    int hi = x.to.IndexOf(x.hips);
                    if (hi >= 0 && !ExtraPos) x.hips.position = x.from[hi].position + transform.TransformVector(x.hipsOffset);
                }
            }
        }

        void OnDestroy() { if (snowball != null) Destroy(snowball.gameObject); if (graph.IsValid()) graph.Destroy(); srcPose?.Dispose(); dstPose?.Dispose(); }
    }
}
