using System;
using System.Collections.Generic;
using UnityEngine;

namespace LastWar
{
    /// <summary>
    /// Weapon grip profiles (Resources/Data/weapon_grips.json): per class (rifle, smg, shotgun, sniper, pistol) the hand,
    /// muzzle and sight points in GunFrame space, measured on each class's base model; a weapon overrides what its own
    /// geometry puts elsewhere. Pistols are their own class (one-handed), never a rifle profile.
    /// </summary>
    public static class WeaponGrips
    {
        [Serializable] class Entry { public string name, look, category, leftHandStyle; public bool oneHanded; public float frameYaw; public float[] rightHandGrip, leftHandGrip, muzzle, aimPoint; }
        [Serializable] class Doc { public Entry[] categories, weapons; }

        public class Profile
        {
            public string look, category, leftHandStyle = "under"; public bool oneHanded; public float frameYaw;
            public Vector3 rightHandGrip, leftHandGrip, muzzle, aimPoint;
            public override string ToString() => $"{look} [{category}{(oneHanded ? ", one-handed" : "")}{(frameYaw != 0 ? $", frame turned {frameYaw:0}" : "")}] R {rightHandGrip:F3} L {leftHandGrip:F3} M {muzzle:F3} A {aimPoint:F3}";
        }

        static Dictionary<string, Profile> cache; static Dictionary<string, Entry> cats, weps;

        static void Load()
        {
            cats = new Dictionary<string, Entry>(); weps = new Dictionary<string, Entry>(); cache = new Dictionary<string, Profile>();
            var ta = Resources.Load<TextAsset>("Data/weapon_grips");
            if (ta == null) { Debug.LogWarning("[LastWar] weapon_grips.json missing"); return; }
            var d = JsonUtility.FromJson<Doc>(ta.text);
            if (d.categories != null) foreach (var c in d.categories) cats[c.name] = c;
            if (d.weapons != null) foreach (var w in d.weapons) weps[w.look] = w;
        }

        static Vector3 V(float[] a, Vector3 fallback) => a != null && a.Length == 3 ? new Vector3(a[0], a[1], a[2]) : fallback;
        public static string CategoryOf(string look) { int u = string.IsNullOrEmpty(look) ? -1 : look.IndexOf('_'); return u > 0 ? look.Substring(0, u) : look ?? ""; }

        /// <summary>the profile for a gun model key (gun_&lt;look&gt;): its class defaults with its own overrides</summary>
        public static Profile Get(string look)
        {
            if (cache == null) Load();
            if (look == null) look = "";
            if (cache.TryGetValue(look, out Profile p)) return p;
            weps.TryGetValue(look, out Entry w);
            string cat = w != null && !string.IsNullOrEmpty(w.category) ? w.category : CategoryOf(look);
            cats.TryGetValue(cat, out Entry c);
            p = new Profile { look = look, category = cat, oneHanded = c != null && c.oneHanded, frameYaw = w != null ? w.frameYaw : 0f,
                leftHandStyle = !string.IsNullOrEmpty(w?.leftHandStyle) ? w.leftHandStyle : !string.IsNullOrEmpty(c?.leftHandStyle) ? c.leftHandStyle : (c != null && c.oneHanded ? "cup" : "under") };
            p.rightHandGrip = V(w?.rightHandGrip, V(c?.rightHandGrip, Vector3.zero));
            p.leftHandGrip = V(w?.leftHandGrip, V(c?.leftHandGrip, new Vector3(0, 0.03f, -0.3f)));
            p.muzzle = V(w?.muzzle, V(c?.muzzle, new Vector3(0, 0.05f, -0.6f)));
            p.aimPoint = V(w?.aimPoint, V(c?.aimPoint, new Vector3(0, 0.1f, -0.1f)));
            cache[look] = p;
            return p;
        }
    }
}
