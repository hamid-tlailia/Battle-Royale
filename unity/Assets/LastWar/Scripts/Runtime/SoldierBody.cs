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
        public static readonly string[] OutfitFiles = { "swat", "tactical", "fsb", "ksk", "bundeswehr", "sfarmy", "rusoldier", "uasoldier", "ww2", "crouchsold", "armyman" };
        public static readonly string[] OutfitNames = { "SWAT", "Agent", "Operator", "Recon", "Woodland", "Commando", "Rifleman", "Desert", "Veteran", "Ghost", "Infantry" };

        /// <param name="skin">outfit index (OutfitFiles), or -1 for the plain soldier</param>
        public static SoldierBody Attach(GameObject owner, int skin = -1)
        {
            var body = owner.AddComponent<SoldierBody>();
            body.Build(skin);
            return body;
        }

        // ---- outfit driven by the (hidden) animated soldier: same mixamo bone names, rest-pose corrected
        Transform outfitRoot;
        readonly List<Transform> srcBones = new List<Transform>(), dstBones = new List<Transform>();
        Transform srcHips, dstHips; Vector3 srcHipsBind, dstHipsBind;

        static string BoneKey(string n) { int c = n.LastIndexOf(':'); return c >= 0 ? n.Substring(c + 1) : n; }

        void SetupOutfit(int skin)
        {
            if (skin < 0 || skin >= OutfitFiles.Length) return;
            GameObject src = MapBuilder.Model("outfit_" + OutfitFiles[skin]);
            if (src == null) return;
            GameObject o = Instantiate(src, transform, false);
            outfitRoot = o.transform;
            // 1.8 m from the head-top joint, feet on the ground
            var dst = new Dictionary<string, Transform>();
            foreach (Transform t in o.GetComponentsInChildren<Transform>(true)) if (t.name.Contains("mixamorig")) dst[BoneKey(t.name)] = t;
            if (dst.TryGetValue("HeadTop_End", out Transform head))
            {
                float foot = float.MaxValue;
                foreach (string f in new[] { "LeftToeBase", "RightToeBase", "LeftFoot", "RightFoot" }) if (dst.TryGetValue(f, out Transform ft)) foot = Mathf.Min(foot, ft.position.y);
                if (foot == float.MaxValue) foot = transform.position.y;
                float h = head.position.y - foot;
                if (h > 0.1f) { o.transform.localScale *= 1.8f / (h * 1.03f); }
                foot = float.MaxValue;
                foreach (string f in new[] { "LeftToeBase", "RightToeBase", "LeftFoot", "RightFoot" }) if (dst.TryGetValue(f, out Transform ft)) foot = Mathf.Min(foot, ft.position.y);
                if (foot != float.MaxValue) o.transform.position += Vector3.up * (transform.position.y + 0.03f - foot);
            }
            foreach (Renderer r in o.GetComponentsInChildren<Renderer>(true))
            {
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
                var mats = r.sharedMaterials;
                for (int k = 0; k < mats.Length; k++) mats[k] = Lit(mats[k]);
                r.sharedMaterials = mats;
            }
            foreach (Animator a in o.GetComponentsInChildren<Animator>(true)) a.enabled = false;
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
                    srcHips = t; dstHips = d;
                    hipsCorr = Quaternion.Inverse(d.parent.rotation) * t.parent.rotation;
                    if (LogRig) Debug.Log($"[LastWar] rig {OutfitFiles[skin]}: srcHipsW {(Quaternion.Inverse(transform.rotation) * t.rotation).eulerAngles} dstHipsW {(Quaternion.Inverse(transform.rotation) * d.rotation).eulerAngles} srcParentW {(Quaternion.Inverse(transform.rotation) * t.parent.rotation).eulerAngles} dstParentW {(Quaternion.Inverse(transform.rotation) * d.parent.rotation).eulerAngles} srcHipsL {t.localRotation.eulerAngles} dstHipsL {d.localRotation.eulerAngles} corr {hipsCorr.eulerAngles}");
                    srcHipsBind = transform.InverseTransformPoint(t.position); dstHipsBind = transform.InverseTransformPoint(d.position);
                    continue;
                }
                srcBones.Add(t); dstBones.Add(d);
            }
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
            for (int i = 0; i < srcBones.Count; i++) dstBones[i].localRotation = srcBones[i].localRotation;
            if (srcHips != null)
            {
                dstHips.localRotation = hipsCorr * srcHips.localRotation;
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
            AnimationPlayableOutput.Create(graph, "out", animator).SetSourcePlayable(mixer);
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
            foreach (Renderer r in gun.GetComponentsInChildren<Renderer>(true)) { var mats = r.sharedMaterials; for (int k = 0; k < mats.Length; k++) mats[k] = Lit(mats[k]); r.sharedMaterials = mats; }
        }

        void Update()
        {
            if (weights == null || current == null) return;
            int target = clipIndex[current];
            // one-shot clips (jump, death) stop on their last frame instead of looping
            if (OneShot(current))
            {
                var cp = (AnimationClipPlayable)mixer.GetInput(target);
                float len = clips[target].length - 0.02f;
                if (cp.GetTime() >= len) { cp.SetTime(len); cp.SetSpeed(0); }
            }
            float sum = 0, dt = Time.deltaTime;
            for (int k = 0; k < weights.Length; k++) { weights[k] = Mathf.MoveTowards(weights[k], k == target ? 1f : 0f, dt * 6f); sum += weights[k]; }
            for (int k = 0; k < weights.Length; k++) mixer.SetInputWeight(k, sum > 0 ? weights[k] / sum : 0);
        }

        // ---- sky-dive pose, visibility, parachute
        /// <summary>lean forward in degrees around the hips (free fall: ~83 = belly down)</summary>
        public float Tilt;
        float tiltNow; Vector3 outfitBase; bool outfitBaseSet;
        Transform chute; float chuteK;
        bool visible = true;

        public void SetVisible(bool v)
        {
            if (v == visible) return;
            visible = v;
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
                r.enabled = v && (outfitRoot == null || !r.transform.IsChildOf(model));
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

        void ApplyTilt()
        {
            tiltNow = Mathf.MoveTowards(tiltNow, Tilt, Time.deltaTime * 240f);
            Vector3 pivot = Vector3.up * 0.95f;
            Quaternion q = Quaternion.Euler(tiltNow, 0, 0);
            model.localRotation = q; model.localPosition = pivot - q * pivot;
            if (outfitRoot != null)
            {
                if (!outfitBaseSet) { outfitBase = outfitRoot.localPosition; outfitBaseSet = true; }
                outfitRoot.localRotation = q; outfitRoot.localPosition = pivot - q * (pivot - outfitBase);
            }
        }

        void LateUpdate()
        {
            if (model != null && (Tilt != 0 || tiltNow != 0)) ApplyTilt();
            Retarget();
            // gun: grip in the right hand, barrel (model -Z) along the aim
            if (gun == null || handR == null) return;
            gun.position = handR.position;
            gun.rotation = Quaternion.LookRotation(-AimDir, Vector3.up);
        }

        void OnDestroy() { if (graph.IsValid()) graph.Destroy(); srcPose?.Dispose(); dstPose?.Dispose(); }
    }
}
