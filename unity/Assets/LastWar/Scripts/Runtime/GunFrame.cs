using System.Collections.Generic;
using UnityEngine;

namespace LastWar
{
    /// <summary>
    /// The gun models (scripts/unity/import-guns.mjs) are stored in one frame: barrel along -Z, muzzle at the -Z end,
    /// up +Y, the pistol grip at the origin. Each model keeps that frame through the transform of its own root node
    /// (a quarter turn about Y, a scale, an offset). Code must never overwrite that root transform: a gun is placed by a
    /// holder around it. Make() returns the holder; moving and turning the holder is moving and turning the gun in the
    /// common frame. The model asset is not changed, and no extra turn is added (the root keeps exactly what was imported).
    /// </summary>
    public static class GunFrame
    {
        public static readonly string[] Looks = { "pistol", "pistol_auto", "pistol_magnum", "rifle", "rifle_marksman", "rifle_storm",
            "shotgun", "shotgun_breacher", "shotgun_long", "smg", "smg_hornet", "smg_viper", "sniper", "sniper_long", "sniper_swift" };

        public static Transform Make(string look, Transform parent, string name = null)
        {
            GameObject src = MapBuilder.Model("gun_" + look);
            if (src == null) return null;
            var holder = new GameObject(name ?? ("gun_" + look)).transform;
            holder.SetParent(parent, false);
            Object.Instantiate(src, holder, false); // keeps the model root's own local rotation / position / scale
            return holder;
        }

        /// <summary>the box of the gun's meshes in the holder's own space (from the mesh bounds: no readable meshes needed)</summary>
        public static Bounds LocalBounds(Transform holder)
        {
            bool any = false; var b = new Bounds();
            Matrix4x4 toHolder = holder.worldToLocalMatrix;
            foreach (Renderer r in holder.GetComponentsInChildren<Renderer>(true))
            {
                Mesh m = r is SkinnedMeshRenderer s ? s.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                if (m == null) continue;
                Bounds mb = m.bounds; Matrix4x4 k = toHolder * r.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = k.MultiplyPoint3x4(c);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            return b;
        }

        /// <summary>?gunaudit=1 : every gun measured as the code used to hold it (root overwritten) and through the holder</summary>
        public static void Audit()
        {
            foreach (string look in Looks)
            {
                GameObject src = MapBuilder.Model("gun_" + look);
                if (src == null) { Debug.Log($"[LastWar] gunaudit {look}: MISSING"); continue; }
                // the old way: the instance's root turned / moved by the caller (identity here)
                Transform old = Object.Instantiate(src).transform; Vector3 rootRot = old.localEulerAngles, rootPos = old.localPosition, rootScale = old.localScale;
                old.position = Vector3.zero; old.rotation = Quaternion.identity;
                var oldHolder = new GameObject("o").transform; old.SetParent(oldHolder, true);
                Bounds ob = LocalBounds(oldHolder);
                Transform h = Make(look, null);
                Bounds nb = LocalBounds(h);
                Debug.Log($"[LastWar] gunaudit {look}: root rot {rootRot:F0} pos {rootPos:F3} scale {rootScale.x:0.0000} | OLD (root overwritten): {Describe(ob)} | HOLDER: {Describe(nb)}");
                Object.Destroy(oldHolder.gameObject); Object.Destroy(h.gameObject);
            }
            Debug.Log("[LastWar] gunaudit done");
        }

        static string Describe(Bounds b)
        {
            Vector3 s = b.size; int ax = s.x >= s.y && s.x >= s.z ? 0 : s.y >= s.z ? 1 : 2;
            float lo = b.min[ax], hi = b.max[ax];
            // grip at the origin: the long end ahead of it is the muzzle side
            string muzzle = Mathf.Abs(lo) > Mathf.Abs(hi) ? "-" : "+";
            return $"size {s.x:0.00}x{s.y:0.00}x{s.z:0.00}, long axis {"XYZ"[ax]}, muzzle {muzzle}{"XYZ"[ax]}, origin at {(-lo / (hi - lo)) * 100f:0}% from the {"XYZ"[ax]}-min end, centre {b.center:F2}";
        }
    }
}
