using UnityEngine;

namespace LastWar
{
    /// <summary>
    /// Simple bot: roams inside the safe zone, runs back in when outside, fights the nearest enemy it can see
    /// (aim error that shrinks while tracking, bursts, weapon by distance), walks toward gunfire it hears.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class BotBrain : MonoBehaviour
    {
        public Fighter fighter;
        public SoldierBody body;
        CharacterController cc;
        Vector3 target, velocity;
        Fighter enemy;
        float scanTimer, burstTimer, aimErr, stuckTimer, skill, reactUntil;
        float jumpAt = -1; Vector3 landAt; bool landSet;
        Vector3 lastPos;
        static Vector3 lastGunfire; static float lastGunfireTime = -99f; static Fighter lastShooter;

        public static BotBrain Spawn(Vector3 pos, string name, int seed)
        {
            var go = new GameObject("Bot " + name);
            go.layer = MapBuilder.PlayerLayer;
            go.transform.position = pos;
            var cc = go.AddComponent<CharacterController>();
            cc.height = 1.8f; cc.radius = 0.4f; cc.center = new Vector3(0, 0.9f, 0); cc.slopeLimit = 60f; cc.stepOffset = 0.45f;
            var b = go.AddComponent<BotBrain>();
            b.fighter = go.AddComponent<Fighter>();
            b.fighter.displayName = name;
            var rnd = new System.Random(seed);
            b.fighter.GiveStartingGuns(rnd.Next(4) == 0 ? "sniper" : "rifle", rnd.Next(2) == 0 ? "shotgun" : "smg");
            b.skill = 0.35f + (float)rnd.NextDouble() * 0.5f;
            b.body = SoldierBody.Attach(go, rnd.Next(SoldierBody.OutfitFiles.Length));
            return b;
        }

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            Fighter.OnShot += (s, from, to) => { if (s != null && s.Alive) { lastGunfire = from; lastGunfireTime = Time.time; lastShooter = s; } };
        }

        void Start() { PickTarget(); }

        void PickTarget()
        {
            Zone z = Zone.Instance;
            Vector2 c = z != null ? z.NextCentre : Vector2.zero; float r = z != null ? Mathf.Max(8f, z.NextRadius * 0.8f) : 120f;
            Vector2 p = c + Random.insideUnitCircle * r;
            target = new Vector3(p.x, 0, p.y);
        }

        void Update()
        {
            if (!fighter.Alive) { body.Play("death"); body.SetGun(null); cc.enabled = false; enabled = false; return; }
            float dt = Time.deltaTime;
            if (GameBoot.Instance == null || GameBoot.Instance.Result != 0) { body.Play("idle_rifle"); return; }
            if (fighter.air != 0) { AirUpdate(dt); return; }
            body.Tilt = 0; body.SetChute(false);

            // perception every ~0.3 s
            if ((scanTimer -= dt) <= 0f)
            {
                scanTimer = 0.3f;
                Fighter best = null; float bd = 70f;
                foreach (Fighter f in GameBoot.Instance.Fighters)
                {
                    if (f == fighter || !f.Alive || f.air == 1) continue;
                    float d = Vector3.Distance(f.transform.position, transform.position);
                    if (d < bd && CanSee(f)) { bd = d; best = f; }
                }
                if (best != enemy) { aimErr = (1.3f - skill) * 0.3f; reactUntil = Time.time + Random.Range(0.5f, 1.3f) * (1.4f - skill); }
                enemy = best;
                // heard a gunshot nearby and nobody in sight: go and look
                if (enemy == null && lastShooter != fighter && Time.time - lastGunfireTime < 2f && Vector3.Distance(lastGunfire, transform.position) < 120f)
                    target = lastGunfire + Random.insideUnitSphere * 6f;
            }

            Zone zone = Zone.Instance;
            bool outside = zone != null && zone.Active && zone.Outside(transform.position, 3f);
            // the next circle is already known: walk into it before the shrink starts
            bool outsideNext = zone != null && zone.Active && Vector2.Distance(new Vector2(transform.position.x, transform.position.z), zone.NextCentre) > zone.NextRadius - 2f;
            Vector3 move = Vector3.zero; bool sprint = false;
            if (enemy != null && enemy.Alive && !outside)
            {
                Vector3 to = enemy.transform.position + Vector3.up * 1.2f - (transform.position + Vector3.up * 1.55f);
                float dist = to.magnitude;
                // weapon by distance: close range < 15 m, long range beyond
                fighter.SelectSlot(dist < 15f ? 1 : 0);
                aimErr = Mathf.MoveTowards(aimErr, 0.035f * (1.3f - skill), dt * 0.04f);
                Vector3 aim = (Quaternion.Euler(Random.Range(-1f, 1f) * aimErr * 57f, Random.Range(-1f, 1f) * aimErr * 57f, 0) * to).normalized;
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(new Vector3(to.x, 0, to.z)), dt * 8f);
                body.AimDir = aim;
                if ((burstTimer -= dt) < 0f) burstTimer = Random.Range(0.8f, 2.2f);
                // no shooting in the first seconds of the match, nor before the bot has reacted
                bool mayShoot = Time.time >= reactUntil && GameBoot.Instance.MatchTime > 12f;
                if (mayShoot && burstTimer > 0.6f) fighter.Fire(transform.position + Vector3.up * 1.55f, aim, dist > 40f, false);
                if (dist > 30f) move = new Vector3(to.x, 0, to.z).normalized * 0.5f;
            }
            else
            {
                if (outsideNext && Vector2.Distance(new Vector2(target.x, target.z), zone.NextCentre) > zone.NextRadius * 0.8f) PickTarget();
                sprint = outside || (outsideNext && zone.Shrinking);
                Vector3 to = target - transform.position; to.y = 0;
                if (to.magnitude < 2f) PickTarget();
                move = to.normalized;
                if (move.sqrMagnitude > 0.01f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(move), dt * 6f);
                body.AimDir = transform.forward;
                if (fighter.Gun != null && fighter.Gun.mag < fighter.Gun.def.mag / 2) fighter.Reload();
            }
            body.SetGun(fighter.Gun != null ? fighter.Gun.def.id : null);

            float speed = sprint ? PlayerController.SprintSpeed : PlayerController.WalkSpeed;
            Vector3 flat = Vector3.MoveTowards(new Vector3(velocity.x, 0, velocity.z), move * speed, PlayerController.GroundAccel * dt);
            velocity.x = flat.x; velocity.z = flat.z;
            if (cc.isGrounded && velocity.y < 0) velocity.y = -2f;
            velocity.y += PlayerController.Gravity * dt;
            cc.Move(velocity * dt);

            // stuck against something: jump, then pick another way
            if ((stuckTimer += dt) > 1.2f)
            {
                if (move.sqrMagnitude > 0.1f && Vector3.Distance(transform.position, lastPos) < 0.8f)
                {
                    if (cc.isGrounded) velocity.y = PlayerController.JumpVelocity;
                    if (Random.value < 0.5f) PickTarget();
                }
                lastPos = transform.position; stuckTimer = 0;
            }

            float hs = new Vector3(cc.velocity.x, 0, cc.velocity.z).magnitude;
            body.Play(!cc.isGrounded ? "jump" : hs > 5.2f ? "run" : hs > 0.4f ? "walk_fire" : enemy != null ? "fire" : "idle_rifle");
        }

        /// <summary>ride the plane, jump at its own moment, glide toward its own landing spot (players spread out)</summary>
        void AirUpdate(float dt)
        {
            var sky = SkyDrop.Instance;
            if (sky == null) { fighter.air = 0; cc.enabled = true; return; }
            body.SetGun(null);
            if (fighter.air == 1)
            {
                if (jumpAt < 0) jumpAt = 0.12f + Random.value * 0.75f;
                AirMove.Ride(fighter, cc); body.SetVisible(false);
                if ((sky.OverMap && sky.Progress >= jumpAt) || sky.MustJump) { AirMove.Jump(fighter); body.SetVisible(true); }
                return;
            }
            if (!landSet)
            {
                landSet = true;
                float a = Random.value * Mathf.PI * 2f, d = 40f + Random.value * 180f;
                Vector3 p = transform.position + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * d; p.y = 0;
                float lim = GameBoot.Instance.Map.playRadius - 20f;
                if (p.magnitude > lim) p = p.normalized * lim;
                landAt = p;
            }
            Vector3 to = landAt - transform.position; to.y = 0;
            Vector3 wish = to.magnitude > 6f ? to.normalized * 0.85f : Vector3.zero;
            if (wish.sqrMagnitude > 0) transform.rotation = Quaternion.LookRotation(wish);
            AirMove.Fall(fighter, cc, wish, false, 0f, GameBoot.Instance.Map.playRadius, dt);
            body.Tilt = fighter.air == 2 ? 83f : 0f;
            body.SetChute(fighter.air == 3);
            body.Play("idle");
            if (fighter.air == 0) { velocity = Vector3.zero; PickTarget(); }
        }

        bool CanSee(Fighter f)
        {
            Vector3 eye = transform.position + Vector3.up * 1.55f, t = f.transform.position + Vector3.up * 1.2f;
            Vector3 d = t - eye;
            foreach (RaycastHit h in Physics.RaycastAll(eye, d.normalized, d.magnitude, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.collider.transform.IsChildOf(transform) || h.collider.transform.IsChildOf(f.transform)) continue;
                return false; // something in the way
            }
            return true;
        }
    }
}
