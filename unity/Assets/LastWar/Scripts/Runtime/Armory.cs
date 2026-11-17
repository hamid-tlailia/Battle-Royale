using System.Collections.Generic;
using UnityEngine;

namespace LastWar
{
    /// <summary>
    /// The armory: upgraded versions of every gun, bought with coins earned in matches. Each version is better at one
    /// thing and worse at another (damage, fire rate, range, steadiness, magazine, reload). The equipped version is
    /// the one you get whenever you pick that gun up in a match.
    /// </summary>
    public static class Armory
    {
        public class Variant
        {
            public string key, gun, name, perk; public int cost;
            public float dmg = 1, rpm = 1, range = 1, spread = 1, reload = 1; public int mag; public bool? auto;
        }

        public static readonly Variant[] All =
        {
            new Variant { key = "rifle", gun = "rifle", name = "Assault Rifle", perk = "Balanced", cost = 0 },
            new Variant { key = "rifle_marksman", gun = "rifle", name = "AR-7 Marksman", perk = "Hits hard far away · slower fire", cost = 600, dmg = 1.25f, range = 1.35f, rpm = 0.75f, spread = 0.8f },
            new Variant { key = "rifle_storm", gun = "rifle", name = "AR-9 Storm", perk = "Very fast fire · weaker, wilder", cost = 450, rpm = 1.3f, dmg = 0.88f, spread = 1.2f, mag = 10 },
            new Variant { key = "smg", gun = "smg", name = "SMG", perk = "Balanced", cost = 0 },
            new Variant { key = "smg_viper", gun = "smg", name = "Viper", perk = "Fastest fire · short reach", cost = 350, rpm = 1.25f, range = 0.8f, spread = 1.1f },
            new Variant { key = "smg_hornet", gun = "smg", name = "Hornet", perk = "Bigger hits, more rounds · slow reload", cost = 450, dmg = 1.2f, mag = 10, reload = 1.25f },
            new Variant { key = "shotgun", gun = "shotgun", name = "Shotgun", perk = "Balanced", cost = 0 },
            new Variant { key = "shotgun_breacher", gun = "shotgun", name = "Breacher", perk = "Devastating up close · very short", cost = 400, dmg = 1.3f, range = 0.75f },
            new Variant { key = "shotgun_long", gun = "shotgun", name = "Longbarrel", perk = "Tight spread, longer reach · slower", cost = 500, range = 1.4f, spread = 0.75f, rpm = 0.85f },
            new Variant { key = "sniper", gun = "sniper", name = "Sniper", perk = "Balanced", cost = 0 },
            new Variant { key = "sniper_long", gun = "sniper", name = "Longshot", perk = "More damage and reach · long reload", cost = 700, dmg = 1.15f, range = 1.25f, reload = 1.3f },
            new Variant { key = "sniper_swift", gun = "sniper", name = "Swift", perk = "Quick follow-up shots · weaker", cost = 600, rpm = 1.35f, dmg = 0.85f, reload = 0.85f },
            new Variant { key = "pistol", gun = "pistol", name = "Pistol", perk = "Balanced", cost = 0 },
            new Variant { key = "pistol_magnum", gun = "pistol", name = "Magnum", perk = "Huge hits · slow, small magazine", cost = 300, dmg = 1.45f, rpm = 0.7f, mag = -4 },
            new Variant { key = "pistol_auto", gun = "pistol", name = "Auto Pistol", perk = "Fires while held · weaker", cost = 350, auto = true, rpm = 1.4f, dmg = 0.8f },
        };
        public static readonly string[] Guns = { "rifle", "smg", "shotgun", "sniper", "pistol" };

        public static int Coins { get => PlayerPrefs.GetInt("lw.coins", 300); set { PlayerPrefs.SetInt("lw.coins", Mathf.Max(0, value)); PlayerPrefs.Save(); } }
        public static bool Owned(Variant v) => v.cost == 0 || PlayerPrefs.GetInt("lw.own." + v.key, 0) == 1;
        public static string Equipped(string gun) => PlayerPrefs.GetString("lw.eq." + gun, gun);
        public static bool Buy(Variant v)
        {
            if (Owned(v) || Coins < v.cost) return false;
            Coins -= v.cost; PlayerPrefs.SetInt("lw.own." + v.key, 1); PlayerPrefs.Save(); return true;
        }
        public static void Equip(Variant v) { if (Owned(v)) { PlayerPrefs.SetString("lw.eq." + v.gun, v.key); PlayerPrefs.Save(); } }

        static readonly Dictionary<string, WeaponDef> made = new Dictionary<string, WeaponDef>();
        public static Variant Get(string key) { foreach (Variant v in All) if (v.key == key) return v; return null; }

        /// <summary>the player's version of a gun (the equipped variant's numbers)</summary>
        public static WeaponDef DefFor(string gun)
        {
            WeaponDef b = Tuning.Weapon(gun);
            Variant v = Get(Equipped(gun));
            if (b == null || v == null || v.cost == 0) return b;
            if (made.TryGetValue(v.key, out WeaponDef d)) return d;
            d = JsonUtility.FromJson<WeaponDef>(JsonUtility.ToJson(b));
            d.name = v.name; d.model = v.key; d.damage *= v.dmg; d.rpm *= v.rpm; d.range *= v.range; d.falloffStart *= v.range;
            d.hipSpread *= v.spread; d.adsSpread *= v.spread; d.reloadTime *= v.reload; d.mag = Mathf.Max(1, d.mag + v.mag);
            if (v.auto.HasValue) d.auto = v.auto.Value;
            return made[v.key] = d;
        }

        /// <summary>what a variant's numbers come to (for the shop bars)</summary>
        public static (float dmg, float rpm, float range, float steady, int mag) Stats(Variant v)
        {
            WeaponDef b = Tuning.Weapon(v.gun);
            float dmg = b.damage * Mathf.Max(1, b.pellets) * v.dmg, rpm = b.rpm * v.rpm, range = b.range * v.range;
            float steady = 1f / Mathf.Max(0.002f, b.hipSpread * v.spread);
            return (dmg, rpm, range, steady, Mathf.Max(1, b.mag + v.mag));
        }

        /// <summary>coins for a match: something for taking part, more for kills, a lot for winning</summary>
        public static int Reward(int kills, bool win, int place) => 20 + kills * 15 + (win ? 150 : Mathf.Max(0, 30 - place));
    }
}
