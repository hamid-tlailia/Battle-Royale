using System.Collections.Generic;
using UnityEngine;

namespace LastWar
{
    /// <summary>
    /// The armory: upgraded versions of every gun, bought with coins earned in matches. Each version is better at one
    /// thing and worse at another (damage, fire rate, range, steadiness, magazine, reload). Every version is its own real
    /// gun (AKM, SCAR-H, M4A1 ...) and all of them lie about the map: picking one up gives its plain numbers, or the
    /// upgraded ones when you bought and equipped that version (any number of versions can be equipped at once).
    /// One gun you own is shown in your soldier's hands in the lobby.
    /// </summary>
    public static class Armory
    {
        public class Variant
        {
            public string key, gun, name, perk; public int cost;
            /// <summary>the real gun its model is (AKM, SCAR-H ...): its name on the ground and in the hands</summary>
            public string real;
            public float dmg = 1, rpm = 1, range = 1, spread = 1, reload = 1; public int mag; public bool? auto;
        }

        public static readonly Variant[] All =
        {
            new Variant { key = "rifle", real = "AKM", gun = "rifle", name = "Assault Rifle", perk = "Balanced", cost = 0 },
            new Variant { key = "rifle_marksman", real = "SCAR-H", gun = "rifle", name = "AR-7 Marksman", perk = "Hits hard far away · slower fire", cost = 600, dmg = 1.25f, range = 1.35f, rpm = 0.75f, spread = 0.8f },
            new Variant { key = "rifle_storm", real = "M4A1", gun = "rifle", name = "AR-9 Storm", perk = "Very fast fire · weaker, wilder", cost = 450, rpm = 1.3f, dmg = 0.88f, spread = 1.2f, mag = 10 },
            new Variant { key = "smg", real = "MP5", gun = "smg", name = "SMG", perk = "Balanced", cost = 0 },
            new Variant { key = "smg_viper", real = "Uzi", gun = "smg", name = "Viper", perk = "Fastest fire · short reach", cost = 350, rpm = 1.25f, range = 0.8f, spread = 1.1f },
            new Variant { key = "smg_hornet", real = "MP7", gun = "smg", name = "Hornet", perk = "Bigger hits, more rounds · slow reload", cost = 450, dmg = 1.2f, mag = 10, reload = 1.25f },
            new Variant { key = "shotgun", real = "Benelli M4", gun = "shotgun", name = "Shotgun", perk = "Balanced", cost = 0 },
            new Variant { key = "shotgun_breacher", real = "SPAS-12", gun = "shotgun", name = "Breacher", perk = "Devastating up close · very short", cost = 400, dmg = 1.3f, range = 0.75f },
            new Variant { key = "shotgun_long", real = "Mossberg 590", gun = "shotgun", name = "Longbarrel", perk = "Tight spread, longer reach · slower", cost = 500, range = 1.4f, spread = 0.75f, rpm = 0.85f },
            new Variant { key = "sniper", real = "SVD", gun = "sniper", name = "Sniper", perk = "Balanced", cost = 0 },
            new Variant { key = "sniper_long", real = "Barrett M82", gun = "sniper", name = "Longshot", perk = "More damage and reach · long reload", cost = 700, dmg = 1.15f, range = 1.25f, reload = 1.3f },
            new Variant { key = "sniper_swift", real = "Barrett M98B", gun = "sniper", name = "Swift", perk = "Quick follow-up shots · weaker", cost = 600, rpm = 1.35f, dmg = 0.85f, reload = 0.85f },
            new Variant { key = "pistol", real = "Glock 17", gun = "pistol", name = "Pistol", perk = "Balanced", cost = 0 },
            new Variant { key = "pistol_magnum", real = "Desert Eagle", gun = "pistol", name = "Magnum", perk = "Huge hits · slow, small magazine", cost = 300, dmg = 1.45f, rpm = 0.7f, mag = -4 },
            new Variant { key = "pistol_auto", real = "Beretta 93R", gun = "pistol", name = "Auto Pistol", perk = "Fires while held · weaker", cost = 350, auto = true, rpm = 1.4f, dmg = 0.8f },
        };
        public static readonly string[] Guns = { "rifle", "smg", "shotgun", "sniper", "pistol" };

        public static int Coins { get => PlayerPrefs.GetInt("lw.coins", 300); set { PlayerPrefs.SetInt("lw.coins", Mathf.Max(0, value)); PlayerPrefs.Save(); } }
        public static bool Owned(Variant v) => v.cost == 0 || PlayerPrefs.GetInt("lw.own." + v.key, 0) == 1;
        /// <summary>(before several could be equipped: the one equipped version of each gun)</summary>
        static string OldEquipped(string gun) => PlayerPrefs.GetString("lw.eq." + gun, gun);
        /// <summary>an upgraded version's numbers are used when you pick that gun up (the plain versions: always theirs)</summary>
        public static bool IsEquipped(Variant v) => v != null && (v.cost == 0 || (Owned(v) && PlayerPrefs.GetInt("lw.eqv." + v.key, OldEquipped(v.gun) == v.key ? 1 : 0) == 1));
        public static void SetEquipped(Variant v, bool on) { if (v == null || v.cost == 0 || !Owned(v)) return; PlayerPrefs.SetInt("lw.eqv." + v.key, on ? 1 : 0); PlayerPrefs.Save(); }
        /// <summary>the gun in the soldier's hands in the lobby (one you own)</summary>
        public static string LobbyGun
        {
            get { string k = PlayerPrefs.GetString("lw.lobbygun", OldEquipped("rifle")); Variant v = Get(k); return v != null && Owned(v) ? k : "rifle"; }
            set { PlayerPrefs.SetString("lw.lobbygun", value); PlayerPrefs.Save(); }
        }
        /// <summary>the gun kind (rifle, smg ...) of a version key or gun id</summary>
        public static string GunOf(string key) => Get(key)?.gun ?? key;
        public static bool Buy(Variant v)
        {
            if (Owned(v) || Coins < v.cost) return false;
            Coins -= v.cost; PlayerPrefs.SetInt("lw.own." + v.key, 1); PlayerPrefs.Save(); return true;
        }

        static readonly Dictionary<string, WeaponDef> made = new Dictionary<string, WeaponDef>();
        public static Variant Get(string key) { foreach (Variant v in All) if (v.key == key) return v; return null; }

        /// <summary>the numbers of the gun `key` (a version key, or a gun id = its plain version) for whoever picks it up:
        /// the player gets the upgraded numbers of a version bought and equipped; otherwise the gun's plain numbers, in
        /// that version's own model and name</summary>
        public static WeaponDef DefFor(string key, bool player)
        {
            Variant v = Get(key); if (v == null) return Tuning.Weapon(key);
            WeaponDef b = Tuning.Weapon(v.gun); if (b == null) return null;
            bool up = player && v.cost > 0 && IsEquipped(v);
            string ck = (up ? "up:" : "plain:") + v.key;
            if (made.TryGetValue(ck, out WeaponDef d)) return d;
            d = JsonUtility.FromJson<WeaponDef>(JsonUtility.ToJson(b));
            d.model = v.cost > 0 ? v.key : null; d.name = up ? v.real + " " + v.name : v.real ?? b.name;
            if (up)
            {
                d.damage *= v.dmg; d.rpm *= v.rpm; d.range *= v.range; d.falloffStart *= v.range;
                d.hipSpread *= v.spread; d.adsSpread *= v.spread; d.reloadTime *= v.reload; d.mag = Mathf.Max(1, d.mag + v.mag);
                if (v.auto.HasValue) d.auto = v.auto.Value;
            }
            return made[ck] = d;
        }
        /// <summary>the versions of a gun kind (for the map's loot: each gun lying about is one of them)</summary>
        public static List<string> KeysOf(string gun) { var l = new List<string>(); foreach (Variant v in All) if (v.gun == gun) l.Add(v.key); return l; }

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
