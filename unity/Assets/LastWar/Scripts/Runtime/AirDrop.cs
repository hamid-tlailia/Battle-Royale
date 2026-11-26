using System.Collections.Generic;
using UnityEngine;

namespace LastWar
{
    /// <summary>
    /// The supply drop: twice a match (when the zone starts its 2nd and 4th phase) a crate floats down under a red and
    /// white parachute somewhere inside the next safe circle. "AIRDROP INCOMING" on the screen, a red mark on the map;
    /// once it lands, red smoke rises from it until it is emptied. Inside: rare things - a Barrett, a level 3 vest,
    /// helmet and backpack, the x8 scope, medkits and ammo.
    /// ?droptest=1: one drop right away, 25 m in front of you.
    /// </summary>
    public class AirDrop : MonoBehaviour
    {
        public static readonly List<AirDrop> All = new List<AirDrop>();
        /// <summary>where it falls / lies (Unity world), for the map</summary>
        public Vector3 Target;
        public bool Landed;
        const float Start = 130f, Speed = 5.5f;
        Transform chute, box; int crate; float t, smokeEnd = -1f, landedTime; ParticleSystem smoke;

        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }

        /// <summary>watches the zone and sends the drops</summary>
        public class Sender : MonoBehaviour
        {
            int sent; bool test; float landedAt = -1f;
            public static void Begin(bool test) { var s = new GameObject("AirDrops").AddComponent<Sender>(); s.test = test; }
            void Update()
            {
                var gb = GameBoot.Instance; var z = Zone.Instance;
                if (gb == null || gb.Player == null || gb.Result != 0) return;
                if (test && sent == 0 && gb.Player.fighter.air == 0)
                {
                    Transform p = gb.Player.transform; sent++;
                    Drop(p.position + p.forward * 25f, 70f); return;
                }
                if (z == null) return;
                // mid-match, not at the end: the first ~70 s after you land, the second ~100 s later, while many still fight
                if (landedAt < 0f) { if (gb.Player.fighter.air == 0) landedAt = Time.time; return; }
                float since = Time.time - landedAt;
                int want = since > 170f ? 2 : since > 70f ? 1 : 0;
                if (sent >= want || gb.AliveCount <= 2) return;
                sent++;
                // somewhere inside the next circle, on dry ground, not in the sea
                for (int k = 0; k < 40; k++)
                {
                    Vector2 r = Random.insideUnitCircle * (z.Active ? z.NextRadius : z.Radius * 0.5f) * 0.7f, c = (z.Active ? z.NextCentre : z.Centre) + r;
                    Vector3 at = new Vector3(c.x, 0, c.y);
                    if (!Ground(ref at)) continue;
                    if (MapBuilder.SeaLevel > -999f && at.y < MapBuilder.SeaLevel + 0.8f) continue;
                    Drop(at, Start); return;
                }
            }
        }

        /// <summary>the ground (or a roof) under x / z; false when it is too steep to stand a crate on</summary>
        static bool Ground(ref Vector3 at)
        {
            int mask = ~(1 << MapBuilder.PlayerLayer);
            if (!Physics.Raycast(new Vector3(at.x, 400f, at.z), Vector3.down, out RaycastHit h, 800f, mask, QueryTriggerInteraction.Ignore)) return false;
            if (h.normal.y < 0.8f) return false;
            at = h.point; return true;
        }

        public static void Drop(Vector3 at, float height)
        {
            Ground(ref at);
            var go = new GameObject("AirDrop");
            var d = go.AddComponent<AirDrop>(); d.Target = at;
            go.transform.position = at + Vector3.up * height;
            d.Build();
            Hud.Notice("AIRDROP INCOMING");
            Sfx.Play("plane", at + Vector3.up * height, 0.6f, 1f, 600f);
        }

        static Material red, white, wood;
        void Build()
        {
            if (red == null) { red = MapBuilder.NewLit(new Color(.86f, .16f, .12f)); white = MapBuilder.NewLit(new Color(.95f, .95f, .93f)); wood = MapBuilder.NewLit(new Color(.42f, .5f, .3f)); }
            // the crate: a big military box (the ammo crate's model, larger), a frame of dark straps
            box = new GameObject("box").transform; box.SetParent(transform, false);
            GameObject src = MapBuilder.Model("item_ammo");
            if (src != null)
            {
                var look = Instantiate(src, box, false).transform; look.localScale = Vector3.one * 1.25f;
                foreach (Renderer r in look.GetComponentsInChildren<Renderer>(true)) { var mats = r.sharedMaterials; for (int k = 0; k < mats.Length; k++) mats[k] = MapBuilder.LitFrom(mats[k]); r.sharedMaterials = mats; }
            }
            else
            {
                var c = GameObject.CreatePrimitive(PrimitiveType.Cube); c.transform.SetParent(box, false);
                c.transform.localScale = new Vector3(1.1f, .8f, .8f); c.transform.localPosition = Vector3.up * .4f; c.GetComponent<Renderer>().sharedMaterial = wood;
            }
            // the parachute: a dome of red and white panels, cords down to the crate
            chute = new GameObject("chute").transform; chute.SetParent(transform, false); chute.localPosition = Vector3.up * 4.2f;
            var mf = chute.gameObject.AddComponent<MeshFilter>(); var mr = chute.gameObject.AddComponent<MeshRenderer>();
            mf.sharedMesh = Dome(); mr.sharedMaterials = new[] { red, white };
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            for (int i = 0; i < 6; i++)
            {
                float a = i / 6f * Mathf.PI * 2f;
                var cord = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Destroy(cord.GetComponent<Collider>());
                cord.transform.SetParent(transform, false);
                Vector3 top = new Vector3(Mathf.Cos(a) * 2.5f, 4.2f, Mathf.Sin(a) * 2.5f), bottom = new Vector3(Mathf.Cos(a) * .35f, .8f, Mathf.Sin(a) * .3f);
                cord.transform.localPosition = (top + bottom) / 2f; cord.transform.up = (top - bottom).normalized;
                cord.transform.localScale = new Vector3(.025f, (top - bottom).magnitude / 2f, .025f);
                cord.GetComponent<Renderer>().sharedMaterial = white;
            }
        }

        /// <summary>a parachute canopy: 12 panels round, alternately red and white (two sub-meshes), open underneath</summary>
        static Mesh Dome()
        {
            const int seg = 12, rings = 5; const float R = 2.7f, H = 1.5f;
            var v = new List<Vector3>(); var t0 = new List<int>(); var t1 = new List<int>();
            for (int s = 0; s < seg; s++)
                for (int r = 0; r < rings; r++)
                {
                    // each quad its own corners (flat-shaded panels, two colours)
                    Vector3 P(int ss, int rr) { float a = ss / (float)seg * Mathf.PI * 2f, e = rr / (float)rings * Mathf.PI * 0.5f; return new Vector3(Mathf.Cos(a) * Mathf.Cos(e) * R, Mathf.Sin(e) * H - (rr == 0 ? .25f : 0f), Mathf.Sin(a) * Mathf.Cos(e) * R); }
                    int b = v.Count; v.Add(P(s, r)); v.Add(P(s + 1, r)); v.Add(P(s, r + 1)); v.Add(P(s + 1, r + 1));
                    var t = s % 2 == 0 ? t0 : t1;
                    t.AddRange(new[] { b, b + 2, b + 1, b + 1, b + 2, b + 3, b, b + 1, b + 2, b + 1, b + 3, b + 2 }); // (both sides)
                }
            var m = new Mesh { name = "chute", subMeshCount = 2 }; m.SetVertices(v); m.SetTriangles(t0, 0); m.SetTriangles(t1, 1);
            m.RecalculateNormals(); m.RecalculateBounds(); return m;
        }

        void Update()
        {
            t += Time.deltaTime;
            if (!Landed)
            {
                // floating down, swinging a little under the canopy
                Vector3 p = transform.position; p.y = Mathf.Max(Target.y, p.y - Speed * Time.deltaTime);
                transform.position = new Vector3(Target.x, p.y, Target.z);
                transform.rotation = Quaternion.Euler(Mathf.Sin(t * 1.1f) * 6f, t * 8f, Mathf.Cos(t * 0.9f) * 6f);
                if (p.y <= Target.y + 0.01f) Land();
                return;
            }
            // the canopy sinks down beside the crate and goes, then the smoke until the crate is empty
            if (chute != null)
            {
                chute.localPosition += new Vector3(0.6f, -1.6f, 0) * Time.deltaTime;
                chute.localScale = Vector3.one * Mathf.Max(0.01f, chute.localScale.x - Time.deltaTime * 0.35f);
                if (chute.localScale.x < 0.05f) { foreach (Transform c in transform) if (c != box) Destroy(c.gameObject); chute = null; }
            }
            bool empty = true; foreach (Loot l in Loot.All) if (l.box == crate) { empty = false; break; }
            float lying = Time.time - landedTime;
            if (smokeEnd < 0f && ((empty && lying > 25f) || lying > 180f)) { smokeEnd = Time.time + 6f; if (smoke != null) smoke.Stop(); }
            if (smokeEnd > 0f && Time.time > smokeEnd && empty) Destroy(gameObject);
        }

        void Land()
        {
            Landed = true; landedTime = Time.time; transform.rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0); transform.position = Target;
            Sfx.Play("land", Target, 0.9f);
            // what's inside
            crate = Loot.NewCrate(Target + Vector3.up * 0.2f, false);
            string gun = Random.value < 0.5f ? "sniper_long" : "rifle_marksman";
            Loot.Spawn(Loot.Kind.Weapon, gun, 1, Target, crate);
            Loot.Spawn(Loot.Kind.Ammo, Armory.GunOf(gun), 60, Target, crate);
            Loot.Spawn(Loot.Kind.Armor, "3", 1, Target, crate);
            Loot.Spawn(Loot.Kind.Helmet, "3", 1, Target, crate);
            Loot.Spawn(Loot.Kind.Bag, "3", 1, Target, crate);
            Loot.Spawn(Loot.Kind.Scope, "8", 1, Target, crate);
            Loot.Spawn(Loot.Kind.Med, "", 3, Target, crate);
            // a collider so you can't walk through it (after the crate found the ground)
            var col = box.gameObject.AddComponent<BoxCollider>(); col.center = Vector3.up * .45f; col.size = new Vector3(1.2f, .9f, .9f);
            // red smoke, rising and drifting, seen from far
            var sgo = new GameObject("smoke"); sgo.transform.SetParent(transform, false); sgo.transform.localPosition = Vector3.up * 0.9f;
            smoke = sgo.AddComponent<ParticleSystem>();
            smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = smoke.main; main.duration = 5f; main.loop = true; main.startLifetime = 7f; main.startSpeed = 3.2f; main.startSize = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(.9f, .15f, .12f, .75f), new Color(1f, .35f, .25f, .55f)); main.maxParticles = 160; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.05f;
            var em = smoke.emission; em.rateOverTime = 14f;
            var sh = smoke.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 8f; sh.radius = 0.2f; sh.rotation = new Vector3(-90, 0, 0);
            var vel = smoke.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World; vel.x = new ParticleSystem.MinMaxCurve(0.6f, 1.4f); vel.y = 0f; vel.z = new ParticleSystem.MinMaxCurve(0.2f, 0.8f);
            var sz = smoke.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.6f, 1, 3.2f));
            var colt = smoke.colorOverLifetime; colt.enabled = true;
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(new Color(1f, .8f, .8f), 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .1f), new GradientAlphaKey(.6f, .6f), new GradientAlphaKey(0, 1) });
            colt.color = g;
            var pr = sgo.GetComponent<ParticleSystemRenderer>();
            var smat = Resources.Load<Material>("Materials/IceFade");
            if (smat != null) { pr.sharedMaterial = new Material(smat) { name = "smoke" }; pr.sharedMaterial.DisableKeyword("_EMISSION"); pr.sharedMaterial.SetTexture("_BaseMap", SmokePuff()); pr.sharedMaterial.SetColor("_BaseColor", Color.white); }
            pr.renderMode = ParticleSystemRenderMode.Billboard; pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            smoke.Play();
        }

        static Texture2D puff;
        static Texture2D SmokePuff()
        {
            if (puff != null) return puff;
            const int n = 64; puff = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "puff" };
            var px = new Color32[n * n]; var rnd = new System.Random(3);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + .5f) / n * 2 - 1, dy = (y + .5f) / n * 2 - 1, r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - r); a = a * a * (0.85f + 0.15f * (float)rnd.NextDouble());
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            puff.SetPixels32(px); puff.Apply(); return puff;
        }
    }
}
