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
            // a model stored back to front (its grip profile says so) turns about Y once, on a layer of its own
            Transform under = holder;
            float yaw = WeaponGrips.Get(look).frameYaw;
            if (Mathf.Abs(yaw) > 0.01f) { under = new GameObject("frameFix").transform; under.SetParent(holder, false); under.localRotation = Quaternion.Euler(0, yaw, 0); }
            Object.Instantiate(src, under, false); // keeps the model root's own local rotation / position / scale
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

        /// <summary>?gripview=1 : the 15 guns side by side (seen from the shooter's right, muzzles to the right), each with its
        /// grip profile markers: red = right hand, blue = left hand, orange = muzzle, green = aim point; the log checks
        /// each point against the model's own box</summary>
        public static void GripView(Camera cam)
        {
            Vector3 origin = new Vector3(0, 400f, 0);
            Color[] col = { new Color(.9f, .15f, .15f), new Color(.15f, .35f, .95f), new Color(1f, .6f, 0f), new Color(.1f, .7f, .25f) };
            var mats = new Material[4];
            Shader un = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            for (int k = 0; k < 4; k++) { mats[k] = new Material(un); mats[k].color = col[k]; if (mats[k].HasProperty("_BaseColor")) mats[k].SetColor("_BaseColor", col[k]); }
            Mesh sphere = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
            int cols = 3;
            for (int i = 0; i < Looks.Length; i++)
            {
                string look = Looks[i];
                Transform h = Make(look, null);
                if (h == null) continue;
                foreach (Renderer r in h.GetComponentsInChildren<Renderer>(true)) { var ms = r.sharedMaterials; for (int k = 0; k < ms.Length; k++) ms[k] = MapBuilder.LitFrom(ms[k]); r.sharedMaterials = ms; }
                h.position = origin + new Vector3(0, -(i / cols) * 0.55f, -((i % cols) - 1) * 1.7f);
                var p = WeaponGrips.Get(look);
                Vector3[] pts = { p.rightHandGrip, p.leftHandGrip, p.muzzle, p.aimPoint };
                for (int k = 0; k < 4; k++)
                {
                    var m = new GameObject("marker", typeof(MeshFilter), typeof(MeshRenderer));
                    m.GetComponent<MeshFilter>().sharedMesh = sphere; m.GetComponent<MeshRenderer>().sharedMaterial = mats[k];
                    m.transform.SetParent(h, false); m.transform.localPosition = pts[k]; m.transform.localScale = Vector3.one * 0.028f;
                    m.transform.position += Vector3.left * 0.08f; // just in front of the gun (toward the camera)
                }
                Bounds b = LocalBounds(h);
                bool muzzleFront = Mathf.Abs(p.muzzle.z - b.min.z) < 0.012f;
                bool inBox = b.Contains(p.rightHandGrip) && b.Contains(p.aimPoint + Vector3.down * 0.02f);
                Debug.Log($"[LastWar] gripview {p} | box z {b.min.z:0.000}..{b.max.z:0.000} y {b.min.y:0.000}..{b.max.y:0.000} | muzzle at the front end {muzzleFront} | right hand + sight inside the gun {inBox}");
            }
            cam.orthographic = true; cam.orthographicSize = 1.55f;
            cam.transform.position = origin + new Vector3(-5f, -1.1f, 0f);
            cam.transform.rotation = Quaternion.LookRotation(Vector3.right, Vector3.up); // from the shooter's right: muzzles to the screen's right
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.93f, .94f, .96f); cam.farClipPlane = 50f;
            RenderSettings.fog = false;
            Debug.Log("[LastWar] gripview done");
        }
    }
}
