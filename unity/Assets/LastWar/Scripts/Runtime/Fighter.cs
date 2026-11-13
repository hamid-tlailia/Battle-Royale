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
        public readonly GunState[] guns = new GunState[3];
        public int slot;
        public bool Alive => hp > 0f;
        public float reloadEnd;   // Time.time when the reload finishes (0 = none)
        float nextShot;

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
            if (s < 0 || s > 2 || guns[s] == null || s == slot) return;
            slot = s; reloadEnd = 0; nextShot = Mathf.Max(nextShot, Time.time + 0.25f);
        }

        public void Reload()
        {
            GunState g = Gun;
            if (g == null || reloadEnd > 0 || g.mag >= g.def.mag || g.reserve <= 0) return;
            reloadEnd = Time.time + g.def.reloadTime;
            if (isPlayer) Sfx.Play("reload", transform.position, 0.7f);
        }

        void Update()
        {
            if (reloadEnd > 0 && Time.time >= reloadEnd)
            {
                GunState g = Gun; int take = Mathf.Min(g.def.mag - g.mag, g.reserve);
                g.mag += take; g.reserve -= take; reloadEnd = 0;
            }
        }

        public bool CanFire => Alive && Gun != null && reloadEnd == 0 && Time.time >= nextShot;

        /// <summary>Fire one shot from `eye` toward `aimDir` (pellets for the shotgun). Returns false if it couldn't.</summary>
        public bool Fire(Vector3 eye, Vector3 aimDir, bool scoped, bool moving)
        {
            if (!CanFire) return false;
            GunState g = Gun;
            if (g.mag <= 0) { Reload(); return false; }
            g.mag--; nextShot = Time.time + g.def.Interval;
            float spread = (scoped ? g.def.adsSpread : g.def.hipSpread) * (moving ? 1.4f : 1f);
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
            Sfx.Play(g.def.id == "smg" ? "pistol" : g.def.id, eye, isPlayer ? 0.9f : 0.6f);
            if (g.mag == 0) Reload();
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

        public void TakeDamage(float dmg, Fighter attacker, string weapon, bool head, Vector3 point)
        {
            if (!Alive) return;
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
