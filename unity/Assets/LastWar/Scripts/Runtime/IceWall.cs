using System.Collections.Generic;
using UnityEngine;

namespace LastWar
{
    /// <summary>
    /// Ice wall (web frost wall, made quicker): the ice button shows a see-through half-round wall in front of you,
    /// FIRE raises it in a blink - also in the middle of a jump (it stands on the ground under the spot). Each block
    /// stands where it lands. Stops bullets until it has taken 260 damage (~13 rifle bullets), melts after 25 s.
    /// </summary>
    public class IceWall : MonoBehaviour
    {
        public const float Height = 2.05f, Thick = 0.45f, Radius = 2.3f, Arc = 88f, Hp = 260f, Life = 25f, Rise = 0.12f;
        const int Blocks = 5; // (about half the old length)
        public struct Block { public Vector3 pos; public Quaternion rot; public float w, ground; }
        static readonly List<Block> spot = new List<Block>();
        float hp = Hp, until, born;
        readonly List<(Transform t, float w, float baseY)> parts = new List<(Transform, float, float)>();
        static Material ice, ghostMat, crystalMat, glowMat, crackMat, fadeMat;
        Transform look;

        static Material Mat(float a)
        {
            Shader sh = Shader.Find("UI/Default");
            return new Material(sh) { color = new Color(.68f, .92f, 1f, a), renderQueue = 3000 };
        }

        static float BlockW => 2f * Radius * Mathf.Sin(Arc / (Blocks - 1) * 0.5f * Mathf.Deg2Rad) + 0.12f; // blocks overlap: no gaps

        /// <summary>how much farther than right in front of you the wall can be raised (aimed with the crosshair)</summary>
        public const float MaxPush = 3f;

        /// <summary>where the blocks would stand around the front of `who` (open behind); empty when there is no room.
        /// Works in the air: the ground is searched up to 8 m below. With an aim ray (eye, aim) the wall goes where the
        /// crosshair meets the ground, from right in front of you up to <see cref="MaxPush"/> m farther; never through a
        /// wall or rock between you and it.</summary>
        public static List<Block> Spot(Transform who, Vector3 eye = default, Vector3 aim = default)
        {
            spot.Clear();
            int mask = ~(1 << MapBuilder.PlayerLayer);
            Vector3 fwd = who.forward; fwd.y = 0; if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward; fwd.Normalize();
            Vector3 centre = who.position;
            if (aim.sqrMagnitude > 0.5f)
            {
                // the crosshair's ground point, measured along the way you face; looking level or up = the farthest
                float far = Radius + MaxPush, reach = far;
                if (Physics.Raycast(eye, aim, out RaycastHit g, 40f, mask, QueryTriggerInteraction.Ignore))
                    reach = Vector3.Dot(g.point - who.position, fwd);
                float push = Mathf.Clamp(reach - Radius, 0f, MaxPush);
                // not through what stands between you and that spot (house wall, rock): stop half a metre short of it
                Vector3 from = who.position + Vector3.up * 1f;
                if (push > 0f && Physics.Raycast(from, fwd, out RaycastHit wall, push + Radius, mask, QueryTriggerInteraction.Ignore) && wall.collider.GetComponentInParent<IceWall>() == null)
                    push = Mathf.Clamp(wall.distance - Radius - 0.5f, 0f, push);
                centre += fwd * push;
            }
            float baseYaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg, step = Arc / (Blocks - 1), w = BlockW;
            for (int i = 0; i < Blocks; i++)
            {
                float a = baseYaw - Arc / 2f + i * step;
                Vector3 dir = Quaternion.Euler(0, a, 0) * Vector3.forward;
                Vector3 p = centre + dir * Radius + Vector3.up * 1.5f;
                if (!Physics.Raycast(p, Vector3.down, out RaycastHit h, 9.5f, mask, QueryTriggerInteraction.Ignore)) continue;
                if (h.collider.GetComponentInParent<IceWall>() != null) continue;
                Vector3 c = h.point + Vector3.up * (Height / 2f - 0.15f);
                Quaternion rot = Quaternion.LookRotation(dir);
                // a block that would stand inside a house wall / rock is left out (the rest still stands)
                if (Physics.CheckBox(c + Vector3.up * 0.15f, new Vector3(w / 2f - 0.15f, Height / 2f - 0.3f, Thick / 2f - 0.08f), rot, mask, QueryTriggerInteraction.Ignore)) continue;
                spot.Add(new Block { pos = c, rot = rot, w = w, ground = h.point.y });
            }
            return spot;
        }

        /// <summary>test (?icetest=1 in the lobby): a wall raised in front of the lobby soldier, then hit in steps - each
        /// crack stage shows for a few seconds (and stays: it lasts long enough for screenshots)</summary>
        public static System.Collections.IEnumerator Test(Transform who)
        {
            yield return new WaitForSeconds(3f);
            var spot = Spot(who); if (spot.Count == 0) { Debug.Log("[LastWar] icetest: no room"); yield break; }
            Place(new List<Block>(spot), who.position);
            IceWall w = FindFirstObjectByType<IceWall>(); if (w == null) yield break;
            w.until = Time.time + 9999f;
            foreach (float lost in new[] { 0f, 0.3f, 0.55f, 0.8f })
            {
                float want = Hp * (1f - lost); if (w.hp > want) w.Damage(w.hp - want);
                Debug.Log($"[LastWar] icetest: {lost * 100f:0} % of its strength gone");
                yield return new WaitForSeconds(12f);
            }
        }

        public static void Place(List<Block> blocks, Vector3 soundAt)
        {
            if (blocks.Count == 0) return;
            var go = new GameObject("IceWall"); go.transform.position = blocks[blocks.Count / 2].pos;
            var wall = go.AddComponent<IceWall>();
            if (ice == null) ice = Mat(0.72f);
            foreach (Block b in blocks)
            {
                var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
                c.name = "ice"; c.transform.SetParent(go.transform, true);
                c.transform.SetPositionAndRotation(b.pos, b.rot);
                c.transform.localScale = new Vector3(b.w, 0.05f, Thick);
                c.GetComponent<Renderer>().sharedMaterial = ice;
                wall.parts.Add((c.transform, b.w, b.ground));
            }
            foreach (var pt in wall.parts) pt.t.GetComponent<Renderer>().enabled = false; // the blocks only stop bullets; the look is the crystal mesh
            wall.look = BuildLook(blocks, go.transform);
            wall.born = Time.time; wall.until = Time.time + Life;
            Sfx.Play("reload", soundAt, 0.6f, 1.6f);
        }

        /// <summary>the see-through preview (one block per spot, hidden when unused)</summary>
        public static Transform Ghost()
        {
            var root = new GameObject("IceGhost").transform;
            if (ghostMat == null) ghostMat = Mat(0.3f);
            for (int i = 0; i < Blocks; i++)
            {
                var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(c.GetComponent<Collider>());
                c.transform.SetParent(root, false);
                c.GetComponent<Renderer>().sharedMaterial = ghostMat;
            }
            return root;
        }

        public static void ShowGhost(Transform ghost, List<Block> blocks)
        {
            for (int i = 0; i < ghost.childCount; i++)
            {
                Transform c = ghost.GetChild(i);
                bool on = i < blocks.Count;
                if (c.gameObject.activeSelf != on) c.gameObject.SetActive(on);
                if (!on) continue;
                c.SetPositionAndRotation(blocks[i].pos, blocks[i].rot);
                c.localScale = new Vector3(blocks[i].w, Height, Thick);
            }
        }

        /// <summary>solid crystal ice like the concept art: jagged shards of several heights along the arc (faceted,
        /// deep blue at the foot to near white at the tips) and a glowing line where it meets the ground</summary>
        static Transform BuildLook(List<Block> blocks, Transform parent)
        {
            var root = new GameObject("crystal").transform; root.SetParent(parent, false);
            Vector3 origin = blocks[blocks.Count / 2].pos; origin.y = blocks[blocks.Count / 2].ground;
            root.position = origin;
            var rnd = new System.Random(blocks.Count * 7919 + (int)(origin.x * 13) + (int)(origin.z * 17));
            float R() => (float)rnd.NextDouble();
            var v = new List<Vector3>(); var col = new List<Color>(); var tri = new List<int>();
            void Tri(Vector3 a, Vector3 b, Vector3 c, Color ca, Color cb, Color cc)
            {
                float shade = 0.82f + R() * 0.3f; int i = v.Count;
                v.Add(a); v.Add(b); v.Add(c); col.Add(ca * shade); col.Add(cb * shade); col.Add(cc * shade);
                tri.Add(i); tri.Add(i + 1); tri.Add(i + 2); tri.Add(i); tri.Add(i + 2); tri.Add(i + 1); // both sides
            }
            Color cBase = new Color(.28f, .55f, .9f), cMid = new Color(.55f, .82f, 1f), cTop = new Color(.9f, .97f, 1f);
            // cracks: zigzag lines laid on the shards' faces now (hidden), shown in three stages as the wall weakens
            var crackV = new[] { new List<Vector3>(), new List<Vector3>(), new List<Vector3>() };
            var crackT = new[] { new List<int>(), new List<int>(), new List<int>() };
            void Crack(int level, Vector3 r0a, Vector3 r0b, Vector3 r1a, Vector3 r1b, Vector3 centre)
            {
                // a fracture: from a point on the face, thin angular segments running both ways at a slant, with forks
                Vector3 F(float u, float s) => Vector3.Lerp(Vector3.Lerp(r0a, r0b, u), Vector3.Lerp(r1a, r1b, u), s);
                Vector3 n = Vector3.Cross(r0b - r0a, r1a - r0a).normalized;
                if (Vector3.Dot(n, F(.5f, .5f) - centre) < 0) n = -n;
                float fw = Mathf.Max(0.05f, (r0b - r0a).magnitude), fh = Mathf.Max(0.05f, (r1a - r0a).magnitude);
                void Strip(List<Vector3> p, float wdt)
                {
                    var lv = crackV[level]; var lt = crackT[level];
                    for (int k = 0; k + 1 < p.Count; k++)
                    {
                        Vector3 d = (p[k + 1] - p[k]).normalized, side = Vector3.Cross(n, d).normalized * (wdt * (1f - 0.6f * k / p.Count));
                        int b = lv.Count; lv.Add(p[k] - side); lv.Add(p[k] + side); lv.Add(p[k + 1] - side); lv.Add(p[k + 1] + side);
                        lt.AddRange(new[] { b, b + 1, b + 2, b + 1, b + 3, b + 2, b, b + 2, b + 1, b + 1, b + 2, b + 3 }); // (both sides)
                    }
                }
                void Run(float u, float s, float ang, int steps, float wdt, int forks)
                {
                    var pts = new List<Vector3> { F(u, s) + n * 0.012f };
                    for (int k = 0; k < steps; k++)
                    {
                        ang += (R() - .5f) * 1.1f;                                        // (sharp turns: about +-30 deg)
                        float len = 0.05f + R() * 0.07f;
                        u = Mathf.Clamp(u + Mathf.Cos(ang) * len / fw, 0.04f, 0.96f); s = Mathf.Clamp(s + Mathf.Sin(ang) * len / fh, 0.04f, 0.96f);
                        pts.Add(F(u, s) + n * 0.012f);
                        if (forks > 0 && R() < 0.35f) { forks--; Run(u, s, ang + (R() < .5f ? 1 : -1) * (0.6f + R() * 0.5f), steps - k - 1, wdt * 0.7f, 0); }
                    }
                    Strip(pts, wdt);
                }
                float u0 = 0.2f + R() * 0.6f, s0 = 0.2f + R() * 0.5f, a0 = (R() < .5f ? 0.6f : 2.5f) + (R() - .5f) * 0.6f; // a slant, either way
                int st = 3 + level + (int)(R() * 2);
                Run(u0, s0, a0, st, 0.006f, 1 + level);
                Run(u0, s0, a0 + Mathf.PI, st - 1, 0.006f, level);                       // and the other way from the same point
            }
            int shards = blocks.Count * 3 + 1;
            var line = new List<Vector3>();
            for (int k = 0; k < shards; k++)
            {
                float t = k / (float)(shards - 1) * (blocks.Count - 1);
                int i0 = Mathf.Clamp(Mathf.FloorToInt(t), 0, blocks.Count - 1), i1 = Mathf.Min(i0 + 1, blocks.Count - 1);
                float f = t - i0;
                Vector3 c = Vector3.Lerp(blocks[i0].pos, blocks[i1].pos, f); c.y = Mathf.Lerp(blocks[i0].ground, blocks[i1].ground, f) - 0.08f;
                Quaternion rot = Quaternion.Slerp(blocks[i0].rot, blocks[i1].rot, f);
                line.Add(c + Vector3.up * 0.1f);
                float h = Height * (0.65f + R() * 0.4f), r = 0.22f + R() * 0.16f;
                Vector3 tilt = rot * new Vector3((R() - .5f) * 0.35f, 0, (R() - .5f) * 0.25f);
                const int n = 5; var ring0 = new Vector3[n]; var ring1 = new Vector3[n];
                for (int j = 0; j < n; j++)
                {
                    float a = (j / (float)n + R() * 0.08f) * Mathf.PI * 2f;
                    Vector3 d = rot * new Vector3(Mathf.Cos(a) * (r * 1.6f), 0, Mathf.Sin(a) * r); // wider along the wall than through it
                    ring0[j] = c + d * (0.9f + R() * 0.2f);
                    ring1[j] = c + d * (0.55f + R() * 0.2f) + Vector3.up * h * (0.62f + R() * 0.12f) + tilt * 0.6f;
                }
                Vector3 tip = c + Vector3.up * h + tilt + rot * new Vector3((R() - .5f) * 0.2f, 0, 0);
                // this shard's cracks: one face cracks at the first stage, more at the next two
                for (int lv = 0; lv < 3; lv++)
                    for (int q = 0; q < (lv == 0 ? (R() < .5f ? 1 : 0) : lv); q++) { int j = (int)(R() * n) % n; Crack(lv, ring0[j], ring0[(j + 1) % n], ring1[j], ring1[(j + 1) % n], c + Vector3.up * h * 0.4f); }
                for (int j = 0; j < n; j++)
                {
                    int jn = (j + 1) % n;
                    Tri(ring0[j], ring0[jn], ring1[j], cBase, cBase, cMid);
                    Tri(ring1[j], ring0[jn], ring1[jn], cMid, cBase, cMid);
                    Tri(ring1[j], ring1[jn], tip, cMid, cMid, cTop);
                }
            }
            for (int i = 0; i < v.Count; i++) v[i] = root.InverseTransformPoint(v[i]);
            var mesh = new Mesh { name = "ice crystal" };
            mesh.SetVertices(v); mesh.SetColors(col); mesh.SetTriangles(tri, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var go = new GameObject("shards", typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(root, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            if (crackMat == null) crackMat = new Material(Shader.Find("UI/Default")) { color = new Color(.05f, .12f, .24f, .85f), renderQueue = 3002 };
            for (int lv = 0; lv < 3; lv++)
            {
                for (int i = 0; i < crackV[lv].Count; i++) crackV[lv][i] = root.InverseTransformPoint(crackV[lv][i]);
                var cm = new Mesh { name = "ice cracks " + lv }; cm.SetVertices(crackV[lv]); cm.SetTriangles(crackT[lv], 0); cm.RecalculateBounds();
                var cg = new GameObject("cracks" + lv, typeof(MeshFilter), typeof(MeshRenderer)); cg.transform.SetParent(root, false);
                cg.GetComponent<MeshFilter>().sharedMesh = cm; cg.GetComponent<MeshRenderer>().sharedMaterial = crackMat;
                cg.SetActive(false);
            }
            if (crystalMat == null)
            {
                var vb = Resources.Load<Material>("Materials/VertexLit");
                if (vb != null)
                {
                    crystalMat = new Material(vb); crystalMat.SetFloat("_Smoothness", 0.92f);
                    crystalMat.EnableKeyword("_EMISSION"); crystalMat.SetColor("_EmissionColor", new Color(.06f, .16f, .28f));
                }
                else crystalMat = Mat(0.85f);
            }
            go.GetComponent<MeshRenderer>().sharedMaterial = crystalMat;
            // the glowing foot line
            if (line.Count > 1)
            {
                var lv = new List<Vector3>(); var lt = new List<int>();
                for (int i = 0; i < line.Count; i++)
                {
                    Vector3 dir = (line[Mathf.Min(i + 1, line.Count - 1)] - line[Mathf.Max(i - 1, 0)]); dir.y = 0; dir.Normalize();
                    Vector3 side = Vector3.Cross(Vector3.up, dir) * 0.09f;
                    lv.Add(root.InverseTransformPoint(line[i] - side)); lv.Add(root.InverseTransformPoint(line[i] + side));
                    if (i > 0) { int b = i * 2; lt.AddRange(new[] { b - 2, b - 1, b, b - 1, b + 1, b, b - 2, b, b - 1, b - 1, b, b + 1 }); }
                }
                var lm = new Mesh(); lm.SetVertices(lv); lm.SetTriangles(lt, 0); lm.RecalculateBounds();
                var lg = new GameObject("glow", typeof(MeshFilter), typeof(MeshRenderer)); lg.transform.SetParent(parent, false);
                lg.transform.SetPositionAndRotation(root.position, root.rotation);
                lg.GetComponent<MeshFilter>().sharedMesh = lm;
                if (glowMat == null) glowMat = new Material(Shader.Find("UI/Default")) { color = new Color(.45f, .9f, 1f, .95f), renderQueue = 3001 };
                lg.GetComponent<MeshRenderer>().sharedMaterial = glowMat;
            }
            return root;
        }

        /// <summary>hit: cracks appear in three stages (a quarter, half, three quarters of its strength gone) and the ice
        /// turns a little see-through, so you see it is about to give way</summary>
        public void Damage(float d)
        {
            hp -= d;
            if (hp <= 0) { Destroy(gameObject); return; }
            float lost = 1f - hp / Hp;
            if (look == null) return;
            for (int lv = 0; lv < 3; lv++) { Transform c = look.Find("cracks" + lv); if (c != null && !c.gameObject.activeSelf && lost >= 0.25f * (lv + 1)) c.gameObject.SetActive(true); }
            Transform sh = look.Find("shards");
            if (sh == null) return;
            var r = sh.GetComponent<MeshRenderer>();
            if (fadeMat == null) fadeMat = Resources.Load<Material>("Materials/IceFade");
            if (fadeMat == null) { if (!fadeMissingLogged) { fadeMissingLogged = true; Debug.Log("[LastWar] ice wall: IceFade material missing - cracks only"); } return; }
            if (myFade == null) { myFade = new Material(fadeMat); r.sharedMaterial = myFade; Debug.Log($"[LastWar] ice wall: see-through material on ({myFade.shader.name}, supported {myFade.shader.isSupported})"); }
            Color bc = myFade.HasProperty("_BaseColor") ? myFade.GetColor("_BaseColor") : Color.white;
            bc.a = 1f - 0.3f * lost; // (at most 30 % see-through: the cracks do the telling)
            if (myFade.HasProperty("_BaseColor")) myFade.SetColor("_BaseColor", bc);
        }
        Material myFade; static bool fadeMissingLogged;
        void OnDestroy() { if (myFade != null) Destroy(myFade); }

        void Update()
        {
            if (Time.time > until) { Destroy(gameObject); return; }
            // shoots up out of the ground, and the last seconds it melts down
            float h = Height * Mathf.Min(Mathf.Clamp01((Time.time - born) / Rise), Mathf.Max(0.05f, (until - Time.time) / 2f));
            if (look != null) look.localScale = new Vector3(1f, Mathf.Max(0.02f, h / Height), 1f);
            foreach (var (t, w, y) in parts)
            {
                if (t == null) continue;
                t.localScale = new Vector3(w, h, Thick);
                Vector3 p = t.position; p.y = y - 0.15f + h / 2f; t.position = p;
            }
        }
    }
}
