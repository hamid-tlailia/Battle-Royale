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
        static readonly Dictionary<Material, Material> litCache = new Dictionary<Material, Material>();

        public static SoldierBody Attach(GameObject owner)
        {
            var body = owner.AddComponent<SoldierBody>();
            body.Build();
            return body;
        }

        void Build()
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
        }

        /// <summary>glTF material -> URP Lit (always included in the build), shared per source material</summary>
        public static Material Lit(Material src)
        {
            if (src == null) return MapBuilder.NewLit(Color.white);
            if (litCache.TryGetValue(src, out Material m)) return m;
            Texture tex = null;
            foreach (string prop in new[] { "baseColorTexture", "_BaseMap", "_MainTex" }) if (src.HasProperty(prop) && src.GetTexture(prop) != null) { tex = src.GetTexture(prop); break; }
            m = MapBuilder.NewLit(Color.white, tex);
            litCache[src] = m;
            return m;
        }

        void SetupAnimation(GameObject m)
        {
            Animator animator = m.GetComponentInChildren<Animator>();
            if (animator == null) animator = m.AddComponent<Animator>();
            animator.applyRootMotion = false;
            if (clips == null) clips = Resources.LoadAll<AnimationClip>("Models/soldier");
            if (clips.Length == 0) return;
            graph = PlayableGraph.Create("soldier");
            mixer = AnimationMixerPlayable.Create(graph, clips.Length);
            for (int k = 0; k < clips.Length; k++)
            {
                var cp = AnimationClipPlayable.Create(graph, clips[k]);
                if (clips[k].name.StartsWith("death") || clips[k].name == "jump") cp.SetDuration(clips[k].length); // play once, hold the last pose
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
            if (clip.StartsWith("death") || clip == "jump") { cp.SetTime(0); cp.SetDone(false); }
        }

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
            float sum = 0, dt = Time.deltaTime;
            for (int k = 0; k < weights.Length; k++) { weights[k] = Mathf.MoveTowards(weights[k], k == target ? 1f : 0f, dt * 6f); sum += weights[k]; }
            for (int k = 0; k < weights.Length; k++) mixer.SetInputWeight(k, sum > 0 ? weights[k] / sum : 0);
        }

        void LateUpdate()
        {
            // gun: grip in the right hand, barrel (model -Z) along the aim
            if (gun == null || handR == null) return;
            gun.position = handR.position;
            gun.rotation = Quaternion.LookRotation(-AimDir, Vector3.up);
        }

        void OnDestroy() { if (graph.IsValid()) graph.Destroy(); }
    }
}
