using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LastWar
{
    /// <summary>
    /// Outfit validation bench (WebGL test switches, lobby only):
    ///   ?grid=mil&from=0&n=4  - outfits from..from+n of a group (mil / q) as rows, the poses as columns
    ///   ?grid=mixmil / mixq   - mixed pieces inside one group (rows = head / torso / legs / feet from different outfits)
    ///   &zoom=1               - closer camera (shoulders, elbows, neck, waist, ankles)
    ///   ?swap=1               - one soldier whose outfit changes every 0.4 s at runtime; logs the checks
    /// Poses: idle, run, jump, crouch, aim, shoot.
    /// </summary>
    public class LobbyTests : MonoBehaviour
    {
        static string[] Clips = { "idle", "run", "jump", "crouch_idle", "idle_rifle", "fire" };
        public static float[] Holds = { 0.3f, 0.3f, 0.55f, 0.3f, 0.3f, 0.25f }; // the frame shown of each clip
        readonly List<SoldierBody> bodies = new List<SoldierBody>(); readonly List<float> holdAt = new List<float>();
        static string[] ClipNames = { "IDLE", "RUN", "JUMP", "CROUCH", "AIM", "SHOOT" };
        /// <summary>?clips=a,b,c : the grid plays exactly these clips (no rifle swap), e.g. every clip of the base skeleton</summary>
        public static void SetClips(string list)
        {
            if (string.IsNullOrEmpty(list)) return;
            Clips = list.Split(','); ClipNames = (string[])Clips.Clone(); SoldierBody.UseRifleClips = false; RawClips = true;
            var h = new float[Clips.Length]; for (int i = 0; i < h.Length; i++) h[i] = 0.35f; Holds = h;
        }
        static bool RawClips;
        /// <summary>?gyaw= : which way the grid soldiers face (215 = three-quarter front, 0 = their back to the camera as in the game, 270 = side)</summary>
        public static float GridYaw = 215f;

        public static bool Run(Camera cam, Canvas ui, string grid, int from, int n, bool zoom, bool swap)
        {
            if (grid == null && !swap) return false;
            var t = new GameObject("LobbyTests").AddComponent<LobbyTests>();
            if (ui != null) ui.enabled = false;
            if (swap) t.StartCoroutine(t.Swap(cam)); else t.Grid(cam, grid, from, n, zoom);
            return true;
        }

        static List<int> GroupList(int g) { var l = new List<int>(); for (int i = 0; i < SoldierBody.OutfitFiles.Length; i++) if (SoldierBody.Group(i) == g) l.Add(i); return l; }

        void Grid(Camera cam, string grid, int from, int n, bool zoom)
        {
            var rSkin = new List<int>(); var rParts = new List<int[]>(); var rLabel = new List<string>();
            bool mix = grid.StartsWith("mix");
            var list = GroupList(grid.EndsWith("q") ? 1 : 0);
            if (!mix)
                for (int i = from; i < Mathf.Min(list.Count, from + n); i++) { rSkin.Add(list[i]); rParts.Add(null); rLabel.Add(SoldierBody.OutfitNames[list[i]]); }
            else
            {
                // every row: a different outfit for each piece (head / torso / legs / feet), all inside the group
                for (int r = 0; r < n; r++)
                {
                    int a = list[(from + r) % list.Count], b = list[(from + r + 3) % list.Count], c = list[(from + r + 6) % list.Count], d = list[(from + r + 9) % list.Count];
                    rSkin.Add(b); rParts.Add(new[] { a, b, c, d }); rLabel.Add($"H:{SoldierBody.OutfitNames[a]} T:{SoldierBody.OutfitNames[b]} L:{SoldierBody.OutfitNames[c]} F:{SoldierBody.OutfitNames[d]}");
                }
            }
            Vector3 origin = new Vector3(0, 300f, 0); // above everything: an empty sky behind
            float dx = Clips.Length > 6 ? 1.05f : 1.25f, dy = 2.35f;
            for (int r = 0; r < rSkin.Count; r++)
                for (int c = 0; c < Clips.Length; c++)
                {
                    var go = new GameObject($"t{r}_{c}");
                    go.transform.position = origin + new Vector3((c - (Clips.Length - 1) / 2f) * dx, -r * dy, 0);
                    go.transform.rotation = Quaternion.Euler(0, GridYaw, 0); // default: three-quarter view toward the camera
                    var body = SoldierBody.Attach(go, rSkin[r], rParts[r]);
                    bool gunOn = RawClips ? Clips[c].StartsWith("mx_") : c != 0;
                    body.SetGun(gunOn ? "rifle" : null); body.Armed = gunOn;
                    body.Play(Clips[c]); body.Speed = c == 1 ? 6f : 0f;
                    body.AimDir = go.transform.forward;
                    bodies.Add(body); holdAt.Add(Holds[c]);
                    Debug.Log($"[LastWar] test row {r} {rLabel[r]} pose {ClipNames[c]}");
                    if (c == 0)
                        foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                            Debug.Log($"[LastWar] piece {smr.name} < {(smr.transform.parent ? smr.transform.parent.name : "-")} on={smr.enabled}");
                }
            cam.orthographic = true;
            float h = rSkin.Count * dy;
            cam.orthographicSize = zoom ? h * 0.36f : h * 0.56f + 0.2f;
            if (!zoom) cam.orthographicSize = Mathf.Max(cam.orthographicSize, (Clips.Length * dx + 0.4f) / 2f / Mathf.Max(0.5f, cam.aspect)); // wide rows fit too
            cam.transform.position = origin + new Vector3(0, -(rSkin.Count - 1) * dy / 2f + 0.9f, -30f);
            cam.transform.rotation = Quaternion.identity;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.78f, .84f, .88f);
            cam.farClipPlane = 100f;
            RenderSettings.fog = false;
            StartCoroutine(HoldAll());
        }

        IEnumerator HoldAll()
        {
            yield return new WaitForSeconds(1.0f); // the crossfades settle
            for (int i = 0; i < bodies.Count; i++) bodies[i].Hold(holdAt[i]);
            Debug.Log("[LastWar] test grid ready");
        }

        IEnumerator Swap(Camera cam)
        {
            Vector3 at = new Vector3(0, 300f, 0); GameObject go = null;
            cam.orthographic = true; cam.orthographicSize = 1.2f; cam.transform.position = at + new Vector3(0, 0.95f, -10f); cam.transform.rotation = Quaternion.identity;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.78f, .84f, .88f); RenderSettings.fog = false;
            SoldierBody body = null; int fails = 0;
            var rnd = new System.Random(7);
            for (int k = 0; k < 30; k++)
            {
                // the lobby's way of changing outfit: the old soldier goes, a new one is dressed
                if (go != null) Destroy(go);
                go = new GameObject("swap"); go.transform.position = at; go.transform.rotation = Quaternion.Euler(0, 180f + 35f, 0);
                int skin = rnd.Next(SoldierBody.OutfitFiles.Length);
                var grp = GroupList(SoldierBody.Group(skin));
                int[] parts = { grp[rnd.Next(grp.Count)], skin, grp[rnd.Next(grp.Count)], grp[rnd.Next(grp.Count)] };
                body = SoldierBody.Attach(go, skin, parts);
                body.SetGun(k % 2 == 0 ? "rifle" : "pistol"); body.Armed = true; body.Play(k % 3 == 0 ? "run" : "idle_rifle");
                yield return new WaitForSeconds(0.4f);
                // the checks: the hand holds the gun, the soldier animates, the outfit pieces are there
                bool gunOk = body.GunAtHand(0.12f), animOk = body.Animating, partsOk = body.VisiblePieces >= 1; // (whole characters: 1-3 meshes)
                if (!(gunOk && animOk && partsOk)) fails++;
                Debug.Log($"[LastWar] swap {k}: {SoldierBody.OutfitNames[skin]} parts {string.Join(",", parts)} gun {gunOk} anim {animOk} pieces {body.VisiblePieces}");
            }
            Debug.Log($"[LastWar] swap done: {30 - fails}/30 passed");
        }

        /// <summary>Phase 1 rig audit in Unity (?rigcheck=1): for each outfit - can a Humanoid avatar be built on it, its rest pose,
        /// its arm / leg proportions against the base skeleton</summary>
        public static void RigCheck()
        {
            float Arm(Transform root, out float leg, out float armDown)
            {
                var b = new Dictionary<string, Transform>();
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) if (t.name.Contains("mixamorig")) b[t.name.Substring(t.name.LastIndexOf(':') + 1)] = t;
                float D(string a, string c) => b.ContainsKey(a) && b.ContainsKey(c) ? (b[a].position - b[c].position).magnitude : 0f;
                leg = D("LeftUpLeg", "LeftLeg") + D("LeftLeg", "LeftFoot");
                Vector3 up = b.ContainsKey("LeftForeArm") ? b["LeftForeArm"].position - b["LeftArm"].position : Vector3.right;
                armDown = Mathf.Atan2(-up.y, new Vector2(up.x, up.z).magnitude) * Mathf.Rad2Deg;
                return D("LeftArm", "LeftForeArm") + D("LeftForeArm", "LeftHand");
            }
            var files = new List<string> { SoldierBody.BaseModel, "soldier" }; foreach (string f in SoldierBody.OutfitFiles) files.Add("outfit_" + f);
            foreach (string f in files)
            {
                GameObject src = MapBuilder.Model(f);
                if (src == null) { Debug.Log($"[LastWar] rigcheck {f}: MISSING"); continue; }
                GameObject o = Object.Instantiate(src); o.transform.position = new Vector3(0, -500, 0);
                float arm = Arm(o.transform, out float leg, out float down);
                int bones = 0; foreach (Transform t in o.GetComponentsInChildren<Transform>(true)) if (t.name.Contains("mixamorig")) bones++;
                Avatar av = HumanRig.Build(o.transform);
                int clips = Resources.LoadAll<AnimationClip>("Models/" + f).Length;
                Debug.Log($"[LastWar] rigcheck {f}: bones {bones} humanoid {(av != null ? "valid" : "FAILED")} mapped {HumanRig.LastMapped}/{22 + 30} fingers {HumanRig.LastFingers}/30 missing [{string.Join(",", HumanRig.LastMissing)}] rest {(Mathf.Abs(down) < 15 ? "T-pose" : "A-pose")} ({down:0}deg) arm/leg {(leg > 0 ? arm / leg : 0):0.00} clips {clips}");
                Object.Destroy(o);
            }
        }

        /// <summary>?retcheck=1 : each outfit on the base skeleton, held in several clips; per bone, the angle between the base
        /// bone's direction and the outfit bone's (a wrong mapping or a bad rest correction shows as a large angle);
        /// also which skeleton drives the body and the outfit's height</summary>
        public static void RetCheck(int from = 0, int count = 99) { new GameObject("RetCheck").AddComponent<LobbyTests>().StartCoroutine(RetCheckCo(from, count)); }
        static IEnumerator RetCheckCo(int from, int count)
        {
            string[] clips = { "mx_idle", "mx_aim", "mx_fire", "mx_sprint", "mx_walk", "mx_crouch_aim", "mx_reload", "mx_crawl", "walk_ual", "sprint_ual", "death" };
            float[] phases = { 0.1f, 0.3f, 0.5f, 0.7f, 0.9f };
            string[,] chain = { { "LeftArm", "LeftForeArm" }, { "LeftForeArm", "LeftHand" }, { "RightArm", "RightForeArm" }, { "RightForeArm", "RightHand" },
                { "LeftHand", "LeftHandMiddle1" }, { "RightHand", "RightHandMiddle1" }, { "LeftUpLeg", "LeftLeg" }, { "LeftLeg", "LeftFoot" },
                { "RightUpLeg", "RightLeg" }, { "RightLeg", "RightFoot" }, { "Spine", "Spine2" }, { "Neck", "Head" } };
            SoldierBody.UseRifleClips = false;
            int nb = chain.GetLength(0);
            for (int s = from; s < Mathf.Min(SoldierBody.OutfitFiles.Length, from + count); s++)
            {
                var go = new GameObject("rc" + s); go.transform.position = new Vector3(0, 300, 0);
                var body = SoldierBody.Attach(go, s);
                body.Rate = 0f; // the clip stays exactly where it is put
                yield return null;
                string who = SoldierBody.OutfitNames[s];
                float[] maxA = new float[nb], minIdle = new float[nb], sumA = new float[nb]; int[] over = new int[nb]; string[] maxAt = new string[nb];
                var overAt = new List<string>[nb]; for (int k = 0; k < nb; k++) { minIdle[k] = 999; overAt[k] = new List<string>(); }
                int samples = 0; float total = 0;
                foreach (string clip in clips)
                {
                    body.Play(clip); yield return new WaitForSeconds(0.35f);
                    foreach (float ph in phases)
                    {
                        body.Hold(ph); body.Rate = 0f; yield return null; yield return null;
                        samples++;
                        for (int k = 0; k < nb; k++)
                        {
                            Transform a = body.BaseBone(chain[k, 0]), b = body.BaseBone(chain[k, 1]), c = body.OutfitBone(chain[k, 0]), d = body.OutfitBone(chain[k, 1]);
                            if (a == null || b == null || c == null || d == null) continue;
                            float ang = Vector3.Angle(b.position - a.position, d.position - c.position);
                            sumA[k] += ang; total += ang;
                            if (ang > maxA[k]) { maxA[k] = ang; maxAt[k] = $"{clip}@{ph:0.0}"; }
                            if (clip == "mx_idle") minIdle[k] = Mathf.Min(minIdle[k], ang);
                            if (ang > 8f) { over[k]++; if (overAt[k].Count < 6) overAt[k].Add($"{clip}@{ph:0.0}={ang:0}"); }
                        }
                    }
                }
                float all = 0; foreach (float m in maxA) all = Mathf.Max(all, m);
                Transform head = body.OutfitBone("Head");
                Debug.Log($"[LastWar] retcheck {who}: driven by '{body.BaseName}', head {(head != null ? head.position.y - 300f : 0):0.00} m, mean {(total / Mathf.Max(1, samples * nb)):0.0} deg, worst {all:0.0} deg, samples {samples} per bone");
                for (int k = 0; k < nb; k++)
                {
                    if (maxA[k] < 5f) continue;
                    // persistent = already off in the standing idle at every phase; transient = only in some frames of some clips
                    string kind = minIdle[k] > 6f ? "PERSISTENT" : "transient";
                    Debug.Log($"[LastWar] retcheck   {who} {chain[k, 0]}->{chain[k, 1]}: max {maxA[k]:0.0} at {maxAt[k]}, mean {sumA[k] / samples:0.0}, idle min {minIdle[k]:0.0}, >8deg in {over[k]}/{samples} samples [{string.Join(" ", overAt[k])}] -> {kind}");
                }
                Object.Destroy(go);
                yield return null;
            }
            Debug.Log("[LastWar] retcheck done");
        }

        /// <summary>?trans=1 : one soldier stepping through clip changes (the game's own names, rifle swap on); every frame the
        /// pose is checked for broken values (NaN, the body flying off or sinking) - the blend itself is the transition</summary>
        public static void TransCheck(Camera cam) { new GameObject("TransCheck").AddComponent<LobbyTests>().StartCoroutine(TransCo(cam)); }
        static IEnumerator TransCo(Camera cam)
        {
            string[] seq = { "idle_rifle", "fire", "walk_fire", "run", "jump", "idle_rifle", "crouch_idle", "crouch_fire", "crouch_walk", "prone", "idle_rifle", "fire", "run", "idle" };
            Vector3 at = new Vector3(0, 300, 0);
            cam.orthographic = true; cam.orthographicSize = 1.2f; cam.transform.position = at + new Vector3(0, 0.95f, -10f); cam.transform.rotation = Quaternion.identity;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.78f, .84f, .88f); RenderSettings.fog = false;
            var go = new GameObject("trans"); go.transform.position = at; go.transform.rotation = Quaternion.Euler(0, 215f, 0);
            var body = SoldierBody.Attach(go, 0); body.SetGun("rifle"); body.Armed = true;
            int bad = 0;
            foreach (string clip in seq)
            {
                body.Play(clip); body.Speed = clip == "run" ? 6f : clip.Contains("walk") ? 2f : 0f;
                for (float t = 0; t < 0.7f; t += Time.deltaTime)
                {
                    yield return null;
                    Transform hips = body.OutfitBone("Hips"), hr = body.OutfitBone("RightHand");
                    if (hips == null || hr == null) continue;
                    float hy = hips.position.y - at.y;
                    bool nan = float.IsNaN(hips.position.x) || float.IsNaN(hr.position.x);
                    if (nan || hy < -0.1f || hy > 1.6f || (hr.position - hips.position).magnitude > 1.6f) { bad++; if (bad < 6) Debug.Log($"[LastWar] trans BAD in {clip}: hips {hy:0.00} hand-hips {(hr.position - hips.position).magnitude:0.00} nan {nan}"); }
                }
                Debug.Log($"[LastWar] trans {clip} -> plays {body.PlayingClip}");
            }
            Debug.Log($"[LastWar] trans done: {bad} bad frames");
        }
    }
}
