using System.Collections.Generic;
using UnityEngine;

namespace LastWar
{
    /// <summary>
    /// One thing lying on the ground (the web game's LootItem): a gun, a box of ammo, a medkit, a vest, a scope or an
    /// ice-wall charge. Spawned in every house and across the valley; dropped from the backpack and by the dead.
    /// </summary>
    public class Loot : MonoBehaviour
    {
        public enum Kind { Weapon, Ammo, Med, Armor, Scope, Frost }
        public Kind kind;
        /// <summary>weapon / ammo: weapon id; scope: "2" / "4" / "8"</summary>
        public string item;
        public int amount = 1;
        /// <summary>just dropped by this fighter: they cannot take it back for 2 s</summary>
        public Fighter lockFor; public float lockUntil;
        public int Zoom => int.TryParse(item, out int z) ? z : 0;
        /// <summary>a dead fighter's things share a box id: one crate on the ground, listed together</summary>
        public int box;
        static int nextBox = 1;

        public static readonly List<Loot> All = new List<Loot>();
        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }

        public string Label => kind switch
        {
            Kind.Weapon => Tuning.Weapon(item)?.name ?? item,
            Kind.Ammo => (Tuning.Weapon(item)?.name ?? item) + " ammo",
            Kind.Med => "Medkit",
            Kind.Armor => "Armor vest",
            Kind.Scope => "×" + item + " scope",
            _ => "Ice wall",
        };
        public string Sub => kind switch
        {
            Kind.Weapon => Tuning.SlotNames[Fighter.SlotOf(item)],
            Kind.Ammo => amount + " rounds",
            Kind.Med => (amount > 1 ? "× " + amount + " · " : "") + "+100 HP",
            Kind.Armor => "+50 armor",
            Kind.Scope => "rifle / sniper",
            _ => (amount > 1 ? "× " + amount + " · " : "") + "blocks bullets",
        };
        /// <summary>HUD picture for the list / backpack</summary>
        public Sprite Icon => kind switch
        {
            Kind.Weapon or Kind.Ammo => UiKit.Get("gun_" + item),
            Kind.Med => UiKit.Get("icon_heal"),
            Kind.Armor => UiKit.Get("icon_vest"),
            Kind.Scope => UiKit.Get("icon_scope"),
            _ => UiKit.Get("icon_frost"),
        };

        public static Loot Spawn(Kind kind, string item, int amount, Vector3 at, int box = 0)
        {
            var go = new GameObject("Loot " + kind);
            go.transform.position = at;
            go.transform.rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
            var l = go.AddComponent<Loot>();
            l.kind = kind; l.item = item; l.amount = Mathf.Max(1, amount); l.box = box;
            if (box == 0) l.BuildLook();
            return l;
        }

        /// <summary>from a backpack / a body: falls to the ground under the spot</summary>
        public static Loot Drop(Kind kind, string item, int amount, Vector3 at, Fighter from)
        {
            Vector3 p = at + Vector3.up * 1.2f;
            int mask = ~(1 << MapBuilder.PlayerLayer);
            if (Physics.Raycast(p, Vector3.down, out RaycastHit h, 6f, mask, QueryTriggerInteraction.Ignore)) p = h.point + Vector3.up * 0.04f;
            else p = at;
            var l = Spawn(kind, item, amount, p);
            if (from != null) { l.lockFor = from; l.lockUntil = Time.time + 2f; }
            return l;
        }

        /// <summary>a dead fighter's crate: everything they carried goes in it (web death box)</summary>
        public static int NewCrate(Vector3 at)
        {
            int id = nextBox++;
            int mask = ~(1 << MapBuilder.PlayerLayer);
            if (Physics.Raycast(at + Vector3.up * 1.2f, Vector3.down, out RaycastHit h, 6f, mask, QueryTriggerInteraction.Ignore)) at = h.point;
            var go = new GameObject("DeathBox"); go.transform.position = at; go.transform.rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
            go.AddComponent<Crate>().box = id;
            GameObject src = MapBuilder.Model("item_ammo");
            if (src != null)
            {
                Transform look = Instantiate(src, go.transform, false).transform; look.localScale = new Vector3(2.3f, 2.6f, 2.6f);
                foreach (Renderer r in look.GetComponentsInChildren<Renderer>(true)) { var mats = r.sharedMaterials; for (int k = 0; k < mats.Length; k++) mats[k] = MapBuilder.LitFrom(mats[k]); r.sharedMaterials = mats; }
            }
            Ring(go.transform, new Color(1f, .83f, .3f, .7f), 1.6f);
            return id;
        }
        public static Vector3 CratePos(int box) { foreach (Crate c in Object.FindObjectsByType<Crate>(FindObjectsSortMode.None)) if (c.box == box) return c.transform.position; return Vector3.zero; }

        /// <summary>removes itself once its last item has been taken</summary>
        class Crate : MonoBehaviour
        {
            public int box; float check;
            void Update()
            {
                if ((check -= Time.deltaTime) > 0) return; check = 0.5f;
                foreach (Loot l in All) if (l.box == box) return;
                Destroy(gameObject);
            }
        }

        static void Ring(Transform parent, Color col, float size)
        {
            var marker = new GameObject("glow", typeof(SpriteRenderer)).GetComponent<SpriteRenderer>();
            marker.transform.SetParent(parent, false); marker.transform.localPosition = Vector3.up * 0.02f; marker.transform.localRotation = Quaternion.Euler(90, 0, 0);
            marker.transform.localScale = Vector3.one * size; marker.sprite = UiKit.Ring; marker.color = col;
        }

        static Material scopeMat, frostMat;
        void BuildLook()
        {
            GameObject src = kind switch
            {
                Kind.Weapon => MapBuilder.Model("gun_" + item),
                Kind.Ammo => MapBuilder.Model("item_ammo"),
                Kind.Med => MapBuilder.Model("item_med"),
                Kind.Armor => MapBuilder.Model("item_vest"),
                _ => null,
            };
            Transform look;
            if (src != null)
            {
                look = Instantiate(src, transform, false).transform;
                foreach (Renderer r in look.GetComponentsInChildren<Renderer>(true)) { var mats = r.sharedMaterials; for (int k = 0; k < mats.Length; k++) mats[k] = MapBuilder.LitFrom(mats[k]); r.sharedMaterials = mats; }
                if (kind == Kind.Weapon) { look.localRotation = Quaternion.Euler(0, 0, 90f); look.localPosition = Vector3.up * 0.07f; look.localScale = Vector3.one * 1.3f; } // the gun lies on its side, a bit bigger (web)
            }
            else if (kind == Kind.Scope)
            {
                if (scopeMat == null) scopeMat = MapBuilder.NewLit(new Color(.12f, .13f, .14f));
                look = GameObject.CreatePrimitive(PrimitiveType.Cylinder).transform; Destroy(look.GetComponent<Collider>());
                look.SetParent(transform, false); look.localScale = new Vector3(0.09f, 0.14f, 0.09f); look.localRotation = Quaternion.Euler(90, 0, 0); look.localPosition = Vector3.up * 0.05f;
                look.GetComponent<Renderer>().sharedMaterial = scopeMat;
            }
            else
            {
                if (frostMat == null) frostMat = MapBuilder.NewLit(new Color(.62f, .9f, 1f));
                look = GameObject.CreatePrimitive(PrimitiveType.Cube).transform; Destroy(look.GetComponent<Collider>());
                look.SetParent(transform, false); look.localScale = new Vector3(0.32f, 0.22f, 0.12f); look.localPosition = Vector3.up * 0.11f;
                look.GetComponent<Renderer>().sharedMaterial = frostMat;
            }
            // a ring under it in the item's colour (web RING_COL) so things are easy to spot in the grass
            Color rc = kind switch
            {
                Kind.Weapon => new Color(1f, .83f, .3f), Kind.Ammo => new Color(.87f, .87f, .87f), Kind.Med => new Color(1f, .42f, .42f),
                Kind.Armor => new Color(.35f, .67f, 1f), Kind.Scope => new Color(.77f, .55f, 1f), _ => new Color(.56f, .91f, 1f),
            };
            rc.a = .75f;
            Ring(transform, rc, 0.62f);
        }

        // ---------------------------------------------------------------- the map's loot (web spawnLoot)
        static readonly (Kind kind, string item, int weight)[] Table =
        {
            (Kind.Weapon, "pistol", 8), (Kind.Weapon, "rifle", 8), (Kind.Weapon, "shotgun", 6), (Kind.Weapon, "sniper", 3), (Kind.Weapon, "smg", 6),
            (Kind.Ammo, "pistol", 7), (Kind.Ammo, "rifle", 10), (Kind.Ammo, "shotgun", 5), (Kind.Ammo, "sniper", 4), (Kind.Ammo, "smg", 7),
            (Kind.Med, null, 9), (Kind.Armor, null, 6), (Kind.Scope, "2", 4), (Kind.Scope, "4", 2), (Kind.Scope, "8", 1), (Kind.Frost, null, 6),
        };

        public static void SpawnMap(MapData map, int seed)
        {
            var rnd = new System.Random(seed ^ 0x10070);
            int total = 0; foreach (var e in Table) total += e.weight;
            (Kind, string, int) Pick() { int r = rnd.Next(total); foreach (var e in Table) { r -= e.weight; if (r < 0) return e; } return Table[0]; }
            int mask = ~(1 << MapBuilder.PlayerLayer);
            bool Free(Vector3 p) => !Physics.CheckSphere(p + Vector3.up * 0.35f, 0.25f, mask, QueryTriggerInteraction.Ignore);
            void Place(Vector3 p)
            {
                var e = Pick();
                Spawn(e.Item1, e.Item2, e.Item1 == Kind.Ammo ? Fighter.AmmoBox[e.Item2] : 1, p);
                // a gun usually comes with some ammo next to it
                if (e.Item1 == Kind.Weapon && rnd.NextDouble() < 0.8) Spawn(Kind.Ammo, e.Item2, Fighter.AmmoBox[e.Item2], p + new Vector3(0.5f, 0, 0.3f));
            }
            // inside every house / farm building: up to 6 things on the floor
            foreach (PropData h in map.props)
            {
                if (!h.p.StartsWith("house") && h.p != "farmhouse") continue;
                var bounds = MapBuilder.PropBounds(h);
                int n = 0;
                for (int k = 0; k < 20 && n < 6; k++)
                {
                    Vector3 c = bounds.center + new Vector3((float)(rnd.NextDouble() - .5) * bounds.size.x * 0.7f, 0, (float)(rnd.NextDouble() - .5) * bounds.size.z * 0.7f);
                    Vector3 top = new Vector3(c.x, h.y + 1.6f, c.z);
                    if (!Physics.Raycast(top, Vector3.down, out RaycastHit hit, 3f, mask, QueryTriggerInteraction.Ignore)) continue;
                    if (Mathf.Abs(hit.point.y - h.y) > 0.5f || !Free(hit.point)) continue;
                    Place(hit.point + Vector3.up * 0.04f); n++;
                }
            }
            // and across the valley
            int want = Mathf.RoundToInt(115f * Mathf.Pow(map.playRadius / 125f, 2f));
            for (int k = 0, n = 0; k < 2000 && n < want; k++)
            {
                double a = rnd.NextDouble() * Mathf.PI * 2, d = System.Math.Sqrt(rnd.NextDouble()) * (map.playRadius - 8);
                float x = (float)(System.Math.Cos(a) * d), z = (float)(System.Math.Sin(a) * d);
                Vector3 p = Coords.ToUnity(x, map.HeightAt(x, z), z);
                if (Physics.Raycast(p + Vector3.up * 40f, Vector3.down, out RaycastHit hit, 80f, mask, QueryTriggerInteraction.Ignore)) p = hit.point;
                if (!Free(p)) continue;
                Place(p + Vector3.up * 0.04f); n++;
            }
        }
    }
}
