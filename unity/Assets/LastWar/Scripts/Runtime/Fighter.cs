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
        public const float MaxHp = 100f, ArmorAbsorb = 0.5f;
        public string displayName = "Soldier";
        public bool isPlayer;
        public float hp = MaxHp, armor = 0f;
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

        public static event Action<Fighter, Fighter, string, bool> OnKill;   // victim, killer, weapon, head
        public static event Action<Fighter, Fighter, float, bool, Vector3> OnHit; // victim, attacker, dmg, head, point
        public static event Action<Fighter, Vector3, Vector3> OnShot;       // shooter, from, to

        public GunState Gun => guns[slot];

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

        /// <summary>use a medkit: +50 HP after 3 s (cancelled by firing)</summary>
        public void Heal()
        {
            if (meds <= 0 || hp >= MaxHp || healEnd > 0 || !Alive) return;
            healEnd = Time.time + 3f;
        }

        void Update()
        {
            if (healEnd > 0 && Time.time >= healEnd) { healEnd = 0; meds--; hp = Mathf.Min(MaxHp, hp + 50f); }
            if (reloadEnd > 0 && Time.time >= reloadEnd)
            {
                GunState g = Gun; int take = Mathf.Min(g.def.mag - g.mag, g.reserve);
                g.mag += take; g.reserve -= take; reloadEnd = 0;
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
            float spread = (scoped ? g.def.adsSpread : g.def.hipSpread) * (moving ? 1.2f : 1f) * (crouch ? 0.7f : 1f) * (grounded ? 1f : 1.6f);
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
                        victim.TakeDamage(g.def.DamageAt(hit.distance, head), this, g.def.id, head, hit.point);
                    }
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
            float toArmor = attacker != null ? Mathf.Min(armor, dmg * ArmorAbsorb) : 0f; // the zone ignores armour
            armor -= toArmor; hp -= dmg - toArmor;
            OnHit?.Invoke(this, attacker, dmg, head, point);
            if (hp <= 0f)
            {
                hp = 0f;
                if (attacker != null && attacker != this) attacker.kills++;
                OnKill?.Invoke(this, attacker, weapon, head);
            }
        }

        public static void ClearEvents() { OnKill = null; OnHit = null; OnShot = null; }
    }
}
