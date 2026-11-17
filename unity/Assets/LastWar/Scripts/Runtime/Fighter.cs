using System;
using UnityEngine;

namespace LastWar
{
    /// <summary>One gun in a slot: which weapon, rounds in the magazine, spare rounds.</summary>
    [Serializable]
    public class GunState
    {
        public WeaponDef def; public int mag, reserve;
        public GunState(string id) { def = Tuning.Weapon(id); mag = def.mag; reserve = def.startReserve; }
    }

    /// <summary>
    /// Health, armour and the three weapon slots of a player or bot; hitscan shooting with spread,
    /// distance fall-off and headshots (same numbers as the web game).
    /// </summary>
    public class Fighter : MonoBehaviour
    {
        public const float MaxHp = 200f, ArmorAbsorb = 0.5f, MaxArmor = 100f, MedHeal = 100f;
        public string displayName = "Soldier";
        public bool isPlayer;
        public float hp = MaxHp, armor = 0f;
        /// <summary>vest level 0 (none) .. 3: a better vest takes a bigger share of every hit</summary>
        public int armorLv;
        public static readonly float[] ArmorShare = { 0f, .35f, .5f, .65f };
        /// <summary>damage this fighter deals x this (the player grows with the profile level, bots hit softer)</summary>
        public float DamageMult = 1f;
        public int kills;
        public int meds, frost;          // medkits, ice walls
        public int air;                  // 0 ground, 1 in the plane, 2 free fall, 3 parachute
        public Vector3 airVel;
        public float healEnd;            // Time.time when the medkit finishes (0 = not healing)
        public readonly GunState[] guns = new GunState[3];
        public int slot;
        public bool Alive => hp > 0f;
        public float reloadEnd;   // Time.time when the reload finishes (0 = none)
        float nextShot;
        /// <summary>guns put away: long guns on the back, pistol in the pocket (the web game's fist slot)</summary>
        public bool holstered;
        /// <summary>shotgun: the second blast of the same press is due (0.15 s after the first)</summary>
        public bool BurstPending { get; private set; }
        public float lastShotAt = -10f;
        /// <summary>scope on the long gun (0 = its own sight: x2 rifle / x8 sniper) and the scopes in the backpack (bits: x2, x4, x8)</summary>
        public int scope, scopes;
        /// <summary>spare rounds for guns you do not carry (they move onto the gun when you pick one up)</summary>
        public readonly System.Collections.Generic.Dictionary<string, int> spare = new System.Collections.Generic.Dictionary<string, int>();
        /// <summary>order of the three slots in the weapon box (the backpack's arrows move them)</summary>
        public readonly int[] order = { 0, 1, 2 };
        public bool HasGun => guns[0] != null || guns[1] != null || guns[2] != null;
        public static int ScopeBit(int zoom) => zoom == 2 ? 1 : zoom == 4 ? 2 : zoom == 8 ? 4 : 0;
        public static int SlotOf(string id) => id == "rifle" || id == "sniper" ? 0 : id == "shotgun" || id == "smg" ? 1 : 2;
        public static readonly System.Collections.Generic.Dictionary<string, int> MaxReserve = new System.Collections.Generic.Dictionary<string, int> { { "pistol", 96 }, { "rifle", 240 }, { "shotgun", 40 }, { "sniper", 40 }, { "smg", 256 } };
        public static readonly System.Collections.Generic.Dictionary<string, int> AmmoBox = new System.Collections.Generic.Dictionary<string, int> { { "pistol", 24 }, { "rifle", 60 }, { "shotgun", 10 }, { "sniper", 10 }, { "smg", 64 } };
        public const int MaxMeds = 5, MaxFrost = 4;
        /// <summary>helmet level 0..3 and its points: it takes a share of every headshot (Lv1 30 %, Lv2 45 %, Lv3 60 %)</summary>
        public int helmetLv; public float helmet;
        public static readonly float[] HelmetShare = { 0f, .3f, .45f, .6f };
        /// <summary>backpack level 0..3: carries more ammo, medkits and ice (x1, x1.5, x2, x2.5)</summary>
        public int bagLv;
        public static readonly float[] BagMult = { 1f, 1.5f, 2f, 2.5f };
        public int AmmoMax(string id) => Mathf.RoundToInt(MaxReserve[id] * BagMult[Mathf.Clamp(bagLv, 0, 3)]);
        public int MedsMax => Mathf.RoundToInt(MaxMeds * BagMult[Mathf.Clamp(bagLv, 0, 3)]);
        public int FrostMax => Mathf.RoundToInt(MaxFrost * BagMult[Mathf.Clamp(bagLv, 0, 3)]);
        /// <summary>what the backpack holds, in capacity units (a round 1, a medkit / ice wall 10, a scope 5) and its room</summary>
        public int Load { get { int u = meds * 10 + frost * 10; foreach (var kv in MaxReserve) u += AmmoOf(kv.Key); for (int z = 2; z <= 8; z *= 2) if ((scopes & ScopeBit(z)) != 0) u += 5; return u; } }
        public int Capacity { get { int u = (MedsMax + FrostMax) * 10 + 15; foreach (var kv in MaxReserve) u += AmmoMax(kv.Key); return u; } }

        public bool Carries(string id) { for (int k = 0; k < 3; k++) if (guns[k] != null && guns[k].def.id == id) return true; return false; }
        /// <summary>spare rounds for a weapon type (on the gun if you carry it, else in the backpack)</summary>
        public int AmmoOf(string id)
        {
            for (int k = 0; k < 3; k++) if (guns[k] != null && guns[k].def.id == id) return guns[k].reserve;
            return spare.TryGetValue(id, out int n) ? n : 0;
        }
        void AddAmmo(string id, int n)
        {
            for (int k = 0; k < 3; k++) if (guns[k] != null && guns[k].def.id == id) { guns[k].reserve = Mathf.Max(0, guns[k].reserve + n); return; }
            spare[id] = Mathf.Max(0, (spare.TryGetValue(id, out int s) ? s : 0) + n);
        }

        /// <summary>walk-over / TAKE pickup (the web game's rules). True when the item is used up.</summary>
        public bool TryPickup(Loot it, bool manual)
        {
            if (!Alive || air != 0 || it == null) return false;
            if (it.lockFor == this && Time.time < it.lockUntil) return false;
            switch (it.kind)
            {
                case Loot.Kind.Weapon:
                {
                    string id = it.item; int si = SlotOf(id); GunState cur = guns[si];
                    if (cur != null && !manual) return false; // walking over a gun never swaps yours
                    if (cur != null && cur.def.id == id)
                    {
                        if (cur.reserve >= AmmoMax(id)) return false;
                        cur.reserve = Mathf.Min(AmmoMax(id), cur.reserve + cur.def.mag); // same gun: its magazine as ammo
                        Picked(); return true;
                    }
                    if (cur != null) // another gun of that kind: swap, yours goes on the ground
                    {
                        spare[cur.def.id] = (spare.TryGetValue(cur.def.id, out int sp) ? sp : 0) + cur.reserve + cur.mag;
                        Loot.Drop(Loot.Kind.Weapon, cur.def.id, 1, it.transform.position + new Vector3(0, 0, 0.4f), this);
                        if (slot == si) reloadEnd = 0;
                    }
                    var g = new GunState(id) { reserve = spare.TryGetValue(id, out int have) ? have : 0 };
                    if (isPlayer) { g.def = Armory.DefFor(id); g.mag = g.def.mag; } // your armory version of it
                    spare[id] = 0;
                    guns[si] = g;
                    if (guns[slot] == null || holstered && !HadGunBefore(si)) { slot = si; holstered = false; }
                    if (isPlayer) Sfx.Play2D("clip", 1f, 1.25f);
                    return true;
                }
                case Loot.Kind.Ammo:
                {
                    int room = AmmoMax(it.item) - AmmoOf(it.item);
                    if (room <= 0) return false;
                    int take = Mathf.Min(room, it.amount); AddAmmo(it.item, take); it.amount -= take;
                    Picked(); return it.amount <= 0;
                }
                case Loot.Kind.Med:
                {
                    int take = Mathf.Min(MedsMax - meds, it.amount); if (take <= 0) return false;
                    meds += take; it.amount -= take; Picked(); return it.amount <= 0;
                }
                case Loot.Kind.Frost:
                {
                    int take = Mathf.Min(FrostMax - frost, it.amount); if (take <= 0) return false;
                    frost += take; it.amount -= take; Picked(); return it.amount <= 0;
                }
                case Loot.Kind.Armor:
                {
                    int lv = Mathf.Clamp(int.TryParse(it.item, out int l) ? l : 1, 1, 3);
                    if (lv > armorLv) { armorLv = lv; armor = MaxArmor; Picked(); return true; } // a better vest: put it on, full
                    if (armor >= MaxArmor) return false;
                    if (armorLv == 0) armorLv = lv;
                    armor = Mathf.Min(MaxArmor, armor + 50f); Picked(); return true;               // same / lower: patches yours
                }
                case Loot.Kind.Helmet:
                {
                    int lv = Mathf.Clamp(int.TryParse(it.item, out int hl) ? hl : 1, 1, 3);
                    if (lv > helmetLv) { if (helmetLv > 0) Loot.Drop(Loot.Kind.Helmet, helmetLv.ToString(), 1, it.transform.position + new Vector3(0.4f, 0, 0), this); helmetLv = lv; helmet = 100f; Picked(); return true; }
                    if (helmet >= 100f || lv < helmetLv) return false;
                    helmet = 100f; Picked(); return true; // same level: a fresh one
                }
                case Loot.Kind.Bag:
                {
                    int lv = Mathf.Clamp(int.TryParse(it.item, out int bl) ? bl : 1, 1, 3);
                    if (lv <= bagLv) return false;
                    if (bagLv > 0) Loot.Drop(Loot.Kind.Bag, bagLv.ToString(), 1, it.transform.position + new Vector3(0.4f, 0, 0), this);
                    bagLv = lv; Picked(); return true;
                }
                case Loot.Kind.Scope:
                {
                    int z = it.Zoom, bit = ScopeBit(z);
                    if (bit == 0 || (scopes & bit) != 0) return false;
                    scopes |= bit; if (scope == 0) scope = z; // the first scope goes straight on the gun
                    Picked(); return true;
                }
            }
            return false;
        }
        bool HadGunBefore(int si) { for (int k = 0; k < 3; k++) if (k != si && guns[k] != null) return true; return false; }
        void Picked() { if (isPlayer) Sfx.Play2D("clip", 0.55f, 1f); }

        // ---- backpack: drop / use
        Vector3 DropSpot() => transform.position + transform.forward * 0.9f + new Vector3(UnityEngine.Random.Range(-.3f, .3f), 0.05f, UnityEngine.Random.Range(-.3f, .3f));
        public void DropWeapon(int s)
        {
            GunState g = guns[s]; if (g == null) return;
            spare[g.def.id] = (spare.TryGetValue(g.def.id, out int sp) ? sp : 0) + g.reserve + g.mag;
            guns[s] = null; if (slot == s) { reloadEnd = 0; BurstPending = false; }
            Loot.Drop(Loot.Kind.Weapon, g.def.id, 1, DropSpot(), this);
            if (guns[slot] == null) { for (int k = 0; k < 3; k++) if (guns[k] != null) { slot = k; break; } }
        }
        public void DropAmmo(string id)
        {
            int n = AmmoOf(id); if (n <= 0) return;
            AddAmmo(id, -n); Loot.Drop(Loot.Kind.Ammo, id, n, DropSpot(), this);
        }
        public void DropMed() { if (meds <= 0) return; meds--; healEnd = 0; Loot.Drop(Loot.Kind.Med, null, 1, DropSpot(), this); }
        public void DropFrost() { if (frost <= 0) return; frost--; Loot.Drop(Loot.Kind.Frost, null, 1, DropSpot(), this); }
        public void DropScope(int z)
        {
            int bit = ScopeBit(z); if ((scopes & bit) == 0) return;
            scopes &= ~bit; if (scope == z) scope = 0;
            Loot.Drop(Loot.Kind.Scope, z.ToString(), 1, DropSpot(), this);
        }
        public void DropArmor() { if (armor <= 0) return; Loot.Drop(Loot.Kind.Armor, Mathf.Max(1, armorLv).ToString(), 1, DropSpot(), this); armor = 0; armorLv = 0; }
        public void DropHelmet() { if (helmetLv <= 0) return; Loot.Drop(Loot.Kind.Helmet, helmetLv.ToString(), 1, DropSpot(), this); helmetLv = 0; helmet = 0; }
        public void DropBag()
        {
            if (bagLv <= 0) return;
            Loot.Drop(Loot.Kind.Bag, bagLv.ToString(), 1, DropSpot(), this); bagLv = 0;
            // what no longer fits falls out with it
            foreach (var kv in MaxReserve) { int over = AmmoOf(kv.Key) - AmmoMax(kv.Key); if (over > 0) { AddAmmo(kv.Key, -over); Loot.Drop(Loot.Kind.Ammo, kv.Key, over, DropSpot(), this); } }
            while (meds > MedsMax) DropMed();
            while (frost > FrostMax) DropFrost();
        }
        /// <summary>put a scope from the backpack on the long gun (0 = take it off)</summary>
        public void UseScope(int z) { if (z == 0 || (scopes & ScopeBit(z)) != 0) scope = z; }

        /// <summary>everything you carried spills on the ground where you fell</summary>
        public void DropAll()
        {
            // one crate where the body lies, holding everything (web death box)
            bool any = HasGun || meds > 0 || frost > 0 || armor > 0 || scopes != 0 || helmetLv > 0 || bagLv > 0;
            foreach (var kv in spare) if (kv.Value > 0) any = true;
            if (!any) return;
            int box = Loot.NewCrate(transform.position + transform.forward * 0.6f);
            Vector3 c = Loot.CratePos(box);
            void Put(Loot.Kind k, string id, int n) => Loot.Spawn(k, id, n, c, box);
            for (int k = 0; k < 3; k++) if (guns[k] != null) { Put(Loot.Kind.Weapon, guns[k].def.id, 1); AddSpare(guns[k].def.id, guns[k].reserve + guns[k].mag); guns[k] = null; }
            foreach (var kv in new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, int>>(spare)) if (kv.Value > 0) Put(Loot.Kind.Ammo, kv.Key, kv.Value);
            spare.Clear();
            if (meds > 0) Put(Loot.Kind.Med, null, meds);
            if (frost > 0) Put(Loot.Kind.Frost, null, frost);
            if (armor > 0) Put(Loot.Kind.Armor, Mathf.Max(1, armorLv).ToString(), 1);
            if (helmetLv > 0) Put(Loot.Kind.Helmet, helmetLv.ToString(), 1);
            if (bagLv > 0) Put(Loot.Kind.Bag, bagLv.ToString(), 1);
            foreach (int z in new[] { 2, 4, 8 }) if ((scopes & ScopeBit(z)) != 0) Put(Loot.Kind.Scope, z.ToString(), 1);
            meds = frost = scopes = scope = 0; armor = 0; armorLv = 0; helmetLv = 0; helmet = 0; bagLv = 0;
        }
        void AddSpare(string id, int n) { spare[id] = (spare.TryGetValue(id, out int s) ? s : 0) + n; }

        public static event Action<Fighter, Fighter, string, bool> OnKill;   // victim, killer, weapon, head
        public static event Action<Fighter, Fighter, float, bool, Vector3> OnHit; // victim, attacker, dmg, head, point
        public static event Action<Fighter, Vector3, Vector3> OnShot;       // shooter, from, to

        public GunState Gun => guns[Mathf.Clamp(slot, 0, 2)];

        public void GiveStartingGuns(string longGun, string closeGun)
        {
            guns[0] = new GunState(longGun); guns[1] = new GunState(closeGun); guns[2] = new GunState("pistol");
            slot = 0;
        }

        public void SelectSlot(int s)
        {
            if (s < 0 || s > 2 || guns[s] == null || (s == slot && !holstered)) return;
            slot = s; holstered = false; reloadEnd = 0; BurstPending = false; nextShot = Mathf.Max(nextShot, Time.time + 0.27f);
            if (isPlayer) Sfx.Play2D("clip", 1f, 1.25f); // drawing / switching a weapon (web sfx.equip)
        }

        /// <summary>put the guns away / take the last one back out</summary>
        public void ToggleHolster()
        {
            if (holstered) { SelectSlot(slot); return; }
            holstered = true; reloadEnd = 0; BurstPending = false;
            if (isPlayer) Sfx.Play2D("clip", 0.7f, 0.9f);
        }

        public void Reload()
        {
            GunState g = Gun;
            if (g == null || holstered || reloadEnd > 0 || g.mag >= g.def.mag || g.reserve <= 0) return;
            BurstPending = false;
            reloadEnd = Time.time + g.def.reloadTime;
            if (isPlayer) Sfx.Play("reload", transform.position, 0.7f);
        }

        /// <summary>use a medkit: +100 HP after 3 s (cancelled by firing)</summary>
        public void Heal()
        {
            if (meds <= 0 || hp >= MaxHp || healEnd > 0 || !Alive) return;
            healEnd = Time.time + 3f;
        }

        void Update()
        {
            if (healEnd > 0 && Time.time >= healEnd) { healEnd = 0; meds--; hp = Mathf.Min(MaxHp, hp + MedHeal); }
            if (reloadEnd > 0 && Time.time >= reloadEnd)
            {
                GunState g = Gun;
                if (g != null) { int take = Mathf.Min(g.def.mag - g.mag, g.reserve); g.mag += take; g.reserve -= take; }
                reloadEnd = 0;
            }
        }

        public bool CanFire => Alive && air == 0 && !holstered && Gun != null && reloadEnd == 0 && Time.time >= nextShot;

        /// <summary>Fire one shot from `eye` toward `aimDir` (pellets for the shotgun). Returns false if it couldn't.</summary>
        public bool Fire(Vector3 eye, Vector3 aimDir, bool scoped, bool moving, bool crouch = false, bool grounded = true)
        {
            if (!CanFire) return false;
            healEnd = 0; // shooting cancels a medkit
            GunState g = Gun;
            if (g.mag <= 0) { BurstPending = false; Reload(); return false; }
            g.mag--; lastShotAt = Time.time;
            // shotgun: two blasts per press, 0.15 s apart (the second needs no holding)
            bool second = BurstPending;
            BurstPending = g.def.id == "shotgun" && !second && g.mag > 0;
            nextShot = Time.time + (BurstPending ? 0.15f : g.def.Interval);
            // the web game's cone: wider on the move and in the air, tighter crouched
            float spread = (scoped ? g.def.adsSpread : g.def.hipSpread) * (moving ? 1.2f : 1f) * (crouch ? 0.7f : 1f) * (grounded ? 1f : 1.05f); // (shots from a jump stay tight)
            for (int p = 0; p < Mathf.Max(1, g.def.pellets); p++)
            {
                Vector3 dir = Spread(aimDir, spread);
                Vector3 end = eye + dir * g.def.range;
                if (FirstHit(eye, dir, g.def.range, out RaycastHit hit))
                {
                    end = hit.point;
                    Fighter victim = hit.collider.GetComponentInParent<Fighter>();
                    if (victim != null && victim != this && victim.Alive)
                    {
                        // head = top 30 cm of the capsule
                        bool head = hit.point.y > victim.transform.position.y + victim.HeadHeight - 0.3f;
                        victim.TakeDamage(g.def.DamageAt(hit.distance, head) * DamageMult * UnityEngine.Random.Range(0.93f, 1.07f), this, g.def.id, head, hit.point);
                    }
                    else if (victim == null) { IceWall wall = hit.collider.GetComponentInParent<IceWall>(); if (wall != null) wall.Damage(g.def.DamageAt(hit.distance, false)); }
                }
                OnShot?.Invoke(this, eye, end);
            }
            Sfx.Play(g.def.id == "smg" ? "pistol" : g.def.id, eye, isPlayer ? 0.9f : 0.6f, g.def.id == "smg" ? 1.18f : 1f);
            if (g.mag == 0) { BurstPending = false; Reload(); }
            return true;
        }

        public float HeadHeight { get; set; } = 1.8f;

        /// <summary>nearest hit that is not the shooter itself (the camera ray starts behind / inside your own capsule)</summary>
        bool FirstHit(Vector3 from, Vector3 dir, float range, out RaycastHit best)
        {
            best = default; float bd = float.MaxValue; bool any = false;
            foreach (RaycastHit h in Physics.RaycastAll(from, dir, range, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.collider.transform.IsChildOf(transform) || h.distance >= bd) continue;
                bd = h.distance; best = h; any = true;
            }
            return any;
        }

        static Vector3 Spread(Vector3 dir, float cone)
        {
            if (cone <= 0f) return dir.normalized;
            Vector2 r = UnityEngine.Random.insideUnitCircle * cone;
            Quaternion q = Quaternion.LookRotation(dir);
            return (q * new Vector3(r.x, r.y, 1f)).normalized;
        }

        /// <summary>test switch (?god=1 in the WebGL URL): takes no damage</summary>
        public bool God;

        public void TakeDamage(float dmg, Fighter attacker, string weapon, bool head, Vector3 point)
        {
            if (!Alive || God) return;
            float toArmor = 0f;
            if (attacker != null && head && helmet > 0f)
            {
                // a headshot: the helmet takes its share (and wears out)
                toArmor = Mathf.Min(helmet, dmg * HelmetShare[Mathf.Clamp(helmetLv, 1, 3)]); helmet -= toArmor;
                if (helmet <= 0f) helmetLv = 0;
            }
            else if (attacker != null) { toArmor = Mathf.Min(armor, dmg * ArmorShare[Mathf.Clamp(armorLv, armor > 0 ? 1 : 0, 3)]); armor -= toArmor; } // the zone ignores armour
            hp -= dmg - toArmor;
            OnHit?.Invoke(this, attacker, dmg, head, point);
            if (hp <= 0f)
            {
                hp = 0f;
                if (attacker != null && attacker != this) attacker.kills++;
                OnKill?.Invoke(this, attacker, weapon, head);
                DropAll(); // what they carried lies around the body
            }
        }

        public static void ClearEvents() { OnKill = null; OnHit = null; OnShot = null; }
    }
}
