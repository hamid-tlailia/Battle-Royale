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
        static readonly string[] Clips = { "idle", "run", "jump", "crouch_idle", "idle_rifle", "fire" };
        public static readonly float[] Holds = { 0.3f, 0.3f, 0.55f, 0.3f, 0.3f, 0.25f }; // the frame shown of each clip
        readonly List<SoldierBody> bodies = new List<SoldierBody>(); readonly List<float> holdAt = new List<float>();
        static readonly string[] ClipNames = { "IDLE", "RUN", "JUMP", "CROUCH", "AIM", "SHOOT" };

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
            float dx = 1.25f, dy = 2.35f;
            for (int r = 0; r < rSkin.Count; r++)
                for (int c = 0; c < Clips.Length; c++)
                {
                    var go = new GameObject($"t{r}_{c}");
                    go.transform.position = origin + new Vector3((c - (Clips.Length - 1) / 2f) * dx, -r * dy, 0);
                    go.transform.rotation = Quaternion.Euler(0, 180f + 35f, 0); // three-quarter view toward the camera
                    var body = SoldierBody.Attach(go, rSkin[r], rParts[r]);
                    body.SetGun(c == 0 ? null : "rifle"); body.Armed = c != 0;
                    body.Play(Clips[c]); body.Speed = c == 1 ? 6f : 0f;
                    body.AimDir = go.transform.forward;
                    bodies.Add(body); holdAt.Add(Holds[c]);
                    Debug.Log($"[LastWar] test row {r} {rLabel[r]} pose {ClipNames[c]}");
                }
            cam.orthographic = true;
            float h = rSkin.Count * dy;
            cam.orthographicSize = zoom ? h * 0.36f : h * 0.56f + 0.2f;
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
                bool gunOk = body.GunAtHand(0.12f), animOk = body.Animating, partsOk = body.VisiblePieces >= 4;
                if (!(gunOk && animOk && partsOk)) fails++;
                Debug.Log($"[LastWar] swap {k}: {SoldierBody.OutfitNames[skin]} parts {string.Join(",", parts)} gun {gunOk} anim {animOk} pieces {body.VisiblePieces}");
            }
            Debug.Log($"[LastWar] swap done: {30 - fails}/30 passed");
        }
    }
}
