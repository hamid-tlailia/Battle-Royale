using System;
using System.Collections.Generic;
using UnityEngine;

namespace LastWar
{
    /// <summary>Weapon / zone / player numbers exported from the web game (Resources/Data/tuning.json).</summary>
    [Serializable]
    public class WeaponDef
    {
        public string id, name;
        public float damage, headMult, rpm; public bool auto;
        public int mag; public float reloadTime, range, falloffStart, minDamageFrac, hipSpread, adsSpread;
        public int pellets; public float scopeFov; public int startReserve;
        public float Interval => 60f / Mathf.Max(1f, rpm);
        public float DamageAt(float dist, bool head)
        {
            float f = 1f;
            if (dist > falloffStart) f = 1f - (1f - minDamageFrac) * Mathf.Clamp01((dist - falloffStart) / Mathf.Max(1f, range - falloffStart));
            return damage * f * (head ? headMult : 1f);
        }
    }
    [Serializable] public class ZonePhase { public float wait, shrink, radius, dps; }

    [Serializable]
    public class Tuning
    {
        public WeaponDef[] weapons;
        public ZonePhase[] zonePhases;
        public float zoneDelay, zoneStartRadius;

        static Tuning instance;
        static Dictionary<string, WeaponDef> byId;
        public static Tuning Get()
        {
            if (instance != null) return instance;
            instance = JsonUtility.FromJson<Tuning>(Resources.Load<TextAsset>("Data/tuning").text);
            byId = new Dictionary<string, WeaponDef>();
            foreach (WeaponDef w in instance.weapons) byId[w.id] = w;
            return instance;
        }
        public static WeaponDef Weapon(string id) { Get(); return byId.TryGetValue(id, out WeaponDef w) ? w : null; }

        /// <summary>Slots: 0 long range (rifle / sniper), 1 close range (shotgun / SMG), 2 pistol.</summary>
        public static readonly string[] SlotNames = { "LONG RANGE", "CLOSE RANGE", "PISTOL" };
    }
}
