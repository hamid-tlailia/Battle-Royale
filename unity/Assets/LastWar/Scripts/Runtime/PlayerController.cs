using UnityEngine;

namespace LastWar
{
    /// <summary>
    /// Third-person soldier: walk / auto-run / jump with the web game's numbers, camera over the shoulder that
    /// avoids walls, shooting through the crosshair (the dot in the middle of the screen), scope zoom.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        public const float WalkSpeed = 4.6f, SprintSpeed = 6.6f, JumpVelocity = 7.2f, Gravity = -22f, GroundAccel = 40f, AirAccel = 10f;
        public TouchControls input;
        public Camera cam;
        public float yaw, pitch = -0.15f;
        public float lookSensitivity = 0.2f; // degrees per screen pixel
        public Fighter fighter;
        public SoldierBody body;

        CharacterController cc;
        Vector3 velocity;
        float camDist = 3.6f;
        /// <summary>looking through the optic (first person): long-range gun in the hands, on the ground</summary>
        public bool Scoped => input.Scoped && fighter.Alive && fighter.air == 0 && !fighter.holstered && fighter.Gun != null && (fighter.Gun.def.id == "rifle" || fighter.Gun.def.id == "sniper");
        /// <summary>the web game's optics: red dot on the rifle (52 deg), x8 on the sniper (10 deg)</summary>
        float ScopeFov => fighter.Gun != null && fighter.Gun.def.id == "sniper" ? 10f : 52f;
        bool sniperHold, sniperAuto, wasScoped, wasGrounded = true; float unscopeAt, stepAcc, airTime, turn;
        /// <summary>the crosshair is on a living enemy (no wall in between)</summary>
        public Fighter OnEnemy { get; private set; }
        public static bool AimAssistOn = true; // false = full control (settings, phase 3)
        Fighter lockTarget; float lockYaw, lockPitch, lockOff;

        public static PlayerController Spawn(Vector3 pos, TouchControls input, Camera cam)
        {
            var go = new GameObject("Player");
            go.layer = MapBuilder.PlayerLayer;
            go.transform.position = pos;
            var cc = go.AddComponent<CharacterController>();
            cc.height = 1.8f; cc.radius = 0.4f; cc.center = new Vector3(0, 0.9f, 0);
            cc.slopeLimit = 60f; cc.stepOffset = 0.45f; cc.skinWidth = 0.04f;
            var pc = go.AddComponent<PlayerController>();
            pc.input = input; pc.cam = cam;
            pc.fighter = go.AddComponent<Fighter>();
            pc.fighter.isPlayer = true; pc.fighter.displayName = "You";
            pc.fighter.GiveStartingGuns("rifle", "shotgun");
            pc.body = SoldierBody.Attach(go, Profile.Skin);
            return pc;
        }

        void Awake() { cc = GetComponent<CharacterController>(); }

        void Update()
        {
            float dt = Time.deltaTime;
            if (!fighter.Alive) { body.Play("death"); body.SetGun(null); body.Tilt = 0; body.Pose = null; body.SetChute(false); if (cc.enabled) cc.enabled = false; return; }
            if (GameBoot.Instance != null && GameBoot.Instance.Result != 0) { body.Play("idle_rifle"); return; }
            // (web: radians per pixel x sensitivity x zoom x scope sensitivity x 0.8 while the dot is red)
            float sens = lookSensitivity * Profile.Sensitivity * (Scoped ? cam.fieldOfView / 70f * Profile.ScopeSensitivity : 1f) * (OnEnemy != null && AimAssistOn ? 0.8f : 1f);
            yaw += input.LookDelta.x * sens;
            pitch = Mathf.Clamp(pitch + input.LookDelta.y * sens * Mathf.Deg2Rad, -1.2f, 1.2f); // Unity screen Y points up: drag down = look down
            if (fighter.air != 0) { sniperHold = false; AirUpdate(dt); return; }
            body.Tilt = 0; body.Pose = null; body.SetChute(false);

            // weapons
            if (input.HolsterPressed) fighter.ToggleHolster();
            if (input.SlotPressed >= 0) { if (input.SlotPressed == fighter.slot && !fighter.holstered) fighter.Reload(); else fighter.SelectSlot(input.SlotPressed); } // tap the equipped gun = reload
            if (input.ReloadPressed) fighter.Reload();
            if (input.HealPressed) fighter.Heal();
            if (input.BagPressed) BagPanel.Toggle(input, fighter);
            if (input.GearPressed) SettingsPanel.Open(input.Canvas, true);
            bool armed = !fighter.holstered && fighter.Gun != null;

            // posture: stand 1.8 m, crouch 1.2 m, prone 0.7 m (jump = stand up)
            if (input.JumpPressed && (input.Crouch || input.Prone)) { input.Crouch = input.Prone = false; input.JumpPressed = false; }
            float targetH = input.Prone ? 0.7f : input.Crouch ? 1.2f : 1.8f;
            if (!Mathf.Approximately(cc.height, targetH))
            {
                float h = Mathf.MoveTowards(cc.height, targetH, dt * 6f);
                // standing up under something low: stay down
                if (h > cc.height && Physics.SphereCast(transform.position + Vector3.up * 0.3f, 0.3f, Vector3.up, out _, h, ~(1 << MapBuilder.PlayerLayer), QueryTriggerInteraction.Ignore)) h = cc.height;
                cc.height = h; cc.radius = Mathf.Min(0.4f, h / 2f); cc.center = new Vector3(0, h / 2f, 0);
                fighter.HeadHeight = h;
            }

            // movement relative to the camera's heading
            Vector2 mv = input.Move;
            if (input.Sprint && mv.y > -0.3f && !Scoped) mv = new Vector2(mv.x * 0.5f, 1f);
            bool sprinting = input.Sprint && mv.y > 0.5f && !input.FireHeld && !Scoped;
            float speed = (sprinting ? SprintSpeed : WalkSpeed) * (input.Prone ? 0.25f : input.Crouch ? 0.55f : 1f);
            Quaternion heading = Quaternion.Euler(0, yaw, 0);
            Vector3 want = heading * new Vector3(mv.x, 0, mv.y) * speed;
            bool grounded = cc.isGrounded;
            Vector3 flat = new Vector3(velocity.x, 0, velocity.z);
            flat = Vector3.MoveTowards(flat, want, (grounded ? GroundAccel : AirAccel) * dt);
            velocity.x = flat.x; velocity.z = flat.z;
            if (grounded && velocity.y < 0) velocity.y = -2f;
            if (input.JumpPressed && (grounded || NearGround())) velocity.y = JumpVelocity;
            velocity.y += Gravity * dt;
            cc.Move(velocity * dt);

            AimAssist(dt);
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            Vector3 aim = AimDirection(out Vector3 eye);
            body.AimDir = aim;
            body.SetGun(armed ? fighter.Gun.def.id : null);
            // long guns that are not in the hands hang across the back (the pistol goes in the pocket)
            backList.Clear();
            for (int k = 0; k < 3; k++) { GunState bg = fighter.guns[k]; if (bg != null && bg.def.id != "pistol" && (fighter.holstered || k != fighter.slot)) backList.Add(bg.def.id); }
            body.SetBack(backList);

            bool onGround = cc.isGrounded || NearGround();
            bool moving = flat.sqrMagnitude > 1f;
            bool fired = false;
            GunState gun = armed ? fighter.Gun : null;
            if (gun != null && gun.def.id == "sniper")
            {
                // sniper: FIRE down opens the scope, letting go takes the shot (the scope closes again if FIRE opened it)
                if (input.FirePressed) { sniperAuto = !input.Scoped; input.Scoped = true; sniperHold = true; unscopeAt = 0; }
                if (sniperHold && input.FireReleased)
                {
                    sniperHold = false;
                    fired = fighter.Fire(eye, aim, true, moving, input.Crouch || input.Prone, onGround);
                    if (sniperAuto) unscopeAt = Time.time + 0.35f;
                }
            }
            else
            {
                sniperHold = false;
                // fire through the crosshair while held; the shotgun's second blast comes by itself
                bool wantFire = gun != null && (input.FireHeld || input.FirePressed || fighter.BurstPending);
                fired = wantFire && fighter.Fire(eye, aim, Scoped, moving, input.Crouch || input.Prone, onGround);
            }
            if (unscopeAt > 0 && Time.time >= unscopeAt) { unscopeAt = 0; input.Scoped = false; }
            bool firing = Time.time - fighter.lastShotAt < 0.35f || (input.FireHeld && gun != null && gun.def.id != "sniper");

            // sounds: optic up / down, footsteps (softer crouched, stone on floors), landing
            if (Scoped != wasScoped) { wasScoped = Scoped; Sfx.Play2D(Scoped ? "scope_on" : "scope_off", 1f); }
            float hs = new Vector3(cc.velocity.x, 0, cc.velocity.z).magnitude;
            if (cc.isGrounded && hs > 1.2f && !input.Prone)
            {
                stepAcc += hs * dt;
                if (stepAcc > (input.Crouch ? 1.4f : 2.0f)) { stepAcc = 0; Sfx.Step(transform.position, true, input.Crouch, OnFloor(transform.position)); }
            }
            if (!onGround) airTime += dt;
            else { if (!wasGrounded && airTime > 0.25f) Sfx.Play2D("land", 0.5f); airTime = 0; }
            wasGrounded = onGround;

            // animation: the web game's clip rules (empty hands swing free, armed walks in the firing stance)
            string clip;
            if (input.Prone) clip = "prone";
            else if (airTime > 0.15f) clip = "jump";
            else if (input.Crouch) clip = firing ? "crouch_fire" : hs > 0.4f ? "crouch_walk" : "crouch_idle";
            else if (hs > 5.2f || (input.Sprint && hs > 0.5f && !input.FireHeld)) clip = "run";
            else if (!armed) clip = hs > 0.4f ? "walk" : "idle";
            else if (hs > 0.4f) clip = "walk_fire";
            else clip = firing ? "fire" : "idle_rifle";
            body.Play(clip);
            body.Armed = armed;
            Locomotion(body, cc.velocity, yaw, hs, clip, armed, input.Sprint, ref turn, dt);
        }

        readonly System.Collections.Generic.List<string> backList = new System.Collections.Generic.List<string>();

        /// <summary>
        /// Directional locomotion (web CharacterView): the legs / body turn toward where the soldier moves (no sideways
        /// sliding), the walk plays backwards when retreating, and the clip speed follows the real speed.
        /// </summary>
        public static void Locomotion(SoldierBody body, Vector3 vel, float yawDeg, float hs, string clip, bool armed, bool running, ref float turn, float dt)
        {
            float target = 0; bool reverse = false;
            if (hs > 0.4f && clip != "jump")
            {
                float rel = Mathf.DeltaAngle(yawDeg, Mathf.Atan2(vel.x, vel.z) * Mathf.Rad2Deg);
                // armed: the body turns less, so the gun stays in the hands and in view while strafing
                float lim = armed ? 31.5f : 66f;
                if (Mathf.Abs(rel) <= 112f) target = Mathf.Clamp(rel, -lim, lim);
                else { reverse = true; target = Mathf.Clamp(Mathf.DeltaAngle(0, rel - Mathf.Sign(rel) * 180f), -lim, lim); }
            }
            turn += Mathf.DeltaAngle(turn, target) * Mathf.Min(1f, dt * 10f);
            body.Turn = turn;
            float reference = clip == "walk" ? 2.0f : clip == "walk_fire" ? 2.6f : clip == "run" ? 6.0f : clip == "crouch_walk" ? 1.6f : 0f;
            float rate = reference > 0 ? Mathf.Clamp((running && clip == "run" ? Mathf.Max(hs, reference) : hs) / reference, 0.6f, clip == "walk" ? 2.6f : 1.6f) : 1f;
            body.Rate = rate * (reverse ? -1f : 1f);
        }

        /// <summary>standing on a house floor / roof rather than the ground (stone footsteps)</summary>
        public static bool OnFloor(Vector3 p)
        {
            MapData m = GameBoot.Instance != null ? GameBoot.Instance.Map : null;
            return m != null && p.y > m.HeightAt(-p.x, p.z) + 0.3f;
        }

        /// <summary>direction from the soldier's eye to whatever the crosshair (screen centre) is on</summary>
        public Vector3 AimDirection(out Vector3 eye)
        {
            eye = transform.position + Vector3.up * (cc.height - 0.25f);
            Ray r = ViewRay();
            float best = 300f;
            foreach (RaycastHit h in Physics.RaycastAll(r, 300f, ~0, QueryTriggerInteraction.Ignore))
                if (!h.collider.transform.IsChildOf(transform) && h.distance < best) best = h.distance;
            Vector3 target = r.origin + r.direction * best;
            Vector3 d = target - eye;
            // something right in front of the muzzle: shoot straight ahead rather than sideways into it
            if (new Vector2(d.x, d.z).magnitude < 1.2f) return r.direction;
            return d.normalized;
        }

        /// <summary>
        /// The crosshair ray, started level with the soldier instead of at the camera: a trunk, rock or slope between
        /// the camera and your own back must not become the target (it sent hip-fire bullets off to the side).
        /// </summary>
        Ray ViewRay()
        {
            Ray r = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
            Vector3 head = transform.position + Vector3.up * (cc.height - 0.25f);
            float skip = Mathf.Max(0f, Vector3.Dot(head - r.origin, r.direction) - 0.3f);
            return new Ray(r.origin + r.direction * skip, r.direction);
        }

        // ------------------------------------------------------------ aim assist (same rules as the web game)
        void Angles(Fighter q, out float qYaw, out float qPitch, out float dist, out Vector3 t)
        {
            Vector3 c = cam.transform.position; t = q.transform.position + Vector3.up * 1.05f;
            Vector3 d = t - c; dist = d.magnitude;
            qYaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; qPitch = Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude);
        }

        bool Los(Vector3 from, Vector3 to, Fighter q)
        {
            Vector3 d = to - from;
            foreach (RaycastHit h in Physics.RaycastAll(from, d.normalized, d.magnitude, ~0, QueryTriggerInteraction.Ignore))
                if (!h.collider.transform.IsChildOf(transform) && !h.collider.transform.IsChildOf(q.transform)) return false;
            return true;
        }

        void AimAssist(float dt)
        {
            OnEnemy = null;
            if (cam == null) return;
            // 1) is the crosshair on an enemy right now?
            Ray r = ViewRay();
            float best = float.MaxValue;
            foreach (RaycastHit h in Physics.RaycastAll(r, Scoped ? 320f : 250f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.collider.transform.IsChildOf(transform) || h.distance >= best) continue;
                best = h.distance;
                Fighter q = h.collider.GetComponentInParent<Fighter>();
                OnEnemy = q != null && q != fighter && q.Alive ? q : null;
            }
            if (!AimAssistOn) { lockTarget = null; return; }
            // 2) sticky aim: lock on when the dot turns red and follow the target's movement
            if (OnEnemy != null && lockTarget != OnEnemy) { Angles(OnEnemy, out lockYaw, out lockPitch, out _, out _); lockTarget = OnEnemy; lockOff = 0; }
            if (lockTarget != null)
            {
                Angles(lockTarget, out float qy, out float qp, out float dist, out Vector3 t);
                // still on them? anywhere on / just around the body, head included (moving from chest to head keeps the lock)
                float off = Vector3.Angle(cam.transform.forward, t - cam.transform.position) * Mathf.Deg2Rad;
                bool near = off < Mathf.Atan(0.9f / Mathf.Max(1f, dist));
                lockOff = OnEnemy != null || near ? 0 : lockOff + dt;
                // (scoped: long range too - the sniper reaches 320 m)
                if (!lockTarget.Alive || dist > (Scoped ? 320f : 150f) || lockOff > 0.6f || !Los(cam.transform.position, t, lockTarget)) lockTarget = null;
                else
                {
                    yaw += Mathf.DeltaAngle(lockYaw, qy);
                    pitch = Mathf.Clamp(pitch + (qp - lockPitch), -1.2f, 1.2f);
                    lockYaw = qy; lockPitch = qp;
                    // through the scope the lock also pulls the aim back onto the body when it drifts off
                    if (Scoped && OnEnemy == null)
                    {
                        float kk = Mathf.Min(1f, dt * 5f);
                        yaw += Mathf.DeltaAngle(yaw, qy) * kk; pitch += (qp - pitch) * kk * 0.7f;
                    }
                    return;
                }
            }
            // 3) gentle pull toward the closest enemy in a small cone while firing / scoped
            bool engaged = input.FireHeld || Scoped;
            if (!engaged || GameBoot.Instance == null) return;
            float bestAng = float.MaxValue, by = 0, bp = 0;
            foreach (Fighter q in GameBoot.Instance.Fighters)
            {
                if (q == fighter || !q.Alive) continue;
                Angles(q, out float qy, out float qp, out float dist, out Vector3 t);
                if (dist > (Scoped ? 260f : 110f)) continue;
                float dyaw = Mathf.DeltaAngle(yaw, qy) * Mathf.Deg2Rad, dp = qp - pitch;
                float ang = Mathf.Sqrt(dyaw * dyaw + dp * dp);
                float cone = Mathf.Max(0.05f, Mathf.Atan(1f / dist)) * (Scoped ? 1.2f : 1.8f);
                if (ang > cone || ang > bestAng || !Los(cam.transform.position, t, q)) continue;
                bestAng = ang; by = dyaw; bp = dp;
            }
            if (bestAng == float.MaxValue) return;
            float k = Mathf.Min(1f, (input.FireHeld ? 7f : 3.5f) * dt);
            yaw += by * k * Mathf.Rad2Deg; pitch += bp * k * 0.6f;
        }

        bool dropStraight, planeViewSet;
        /// <summary>in the plane / falling / under the parachute</summary>
        void AirUpdate(float dt)
        {
            var sky = SkyDrop.Instance;
            if (sky == null) { fighter.air = 0; cc.enabled = true; return; }
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            body.SetGun(null);
            if (fighter.air == 1)
            {
                // first frame: look the way the plane flies, a little down
                if (!planeViewSet) { planeViewSet = true; Vector3 d = sky.Dir; yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; pitch = -0.22f; }
                AirMove.Ride(fighter, cc); body.SetVisible(false);
                if ((input.JumpPressed && sky.OverMap && sky.T > 0.5f) || sky.MustJump) { AirMove.Jump(fighter); body.SetVisible(true); dropStraight = false; }
                return;
            }
            // JUMP while falling: drop straight down (tap again to glide)
            if (input.JumpPressed) dropStraight = !dropStraight;
            Vector2 mv = input.Move;
            Quaternion heading = Quaternion.Euler(0, yaw, 0);
            // stick untouched: glide toward where you look
            Vector3 wish = mv.sqrMagnitude > 0.01f ? heading * new Vector3(mv.x, 0, mv.y) : heading * Vector3.forward;
            AirMove.Fall(fighter, cc, Vector3.ClampMagnitude(wish, 1f), dropStraight, Mathf.Max(0, -pitch), GameBoot.Instance.Map.playRadius, dt);
            body.Tilt = fighter.air == 2 ? 83f : 0f; body.Pose = fighter.air == 2 ? "sky" : fighter.air == 3 ? "chute" : null;
            body.SetChute(fighter.air == 3);
            body.Play("idle");
            body.AimDir = transform.forward;
            if (fighter.air == 0) { velocity = Vector3.zero; dropStraight = false; }
        }

        bool NearGround() => Physics.Raycast(transform.position + Vector3.up * 0.2f, Vector3.down, 0.5f, ~(1 << MapBuilder.PlayerLayer), QueryTriggerInteraction.Ignore);

        void LateUpdate()
        {
            if (cam == null) return;
            if (fighter.air == 1 && SkyDrop.Instance != null)
            {
                // chase camera around the plane
                Vector3 pp = SkyDrop.Instance.PlanePos;
                Quaternion lk = Quaternion.Euler(-pitch * Mathf.Rad2Deg, yaw, 0);
                cam.transform.position = pp - lk * Vector3.forward * 30f + Vector3.up * 7f;
                cam.transform.LookAt(pp);
                cam.fieldOfView = 70f;
                return;
            }
            Vector3 pivot = transform.position + Vector3.up * Mathf.Max(1.0f, cc.height - 0.3f); // (prone: the camera stays above the body, not in the ground)
            Quaternion look = Quaternion.Euler(-pitch * Mathf.Rad2Deg, yaw, 0);
            body.SetVisible(!Scoped && fighter.air != 1);
            if (Scoped)
            {
                // first person through the optic: the eye, a little forward; your own body is hidden
                camDist = 0f;
                cam.transform.position = transform.position + Vector3.up * (cc.height - 0.25f) + look * Vector3.forward * 0.25f;
                cam.transform.rotation = look;
                cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, ScopeFov, Time.deltaTime * 20f);
                return;
            }
            float targetDist = fighter.air != 0 ? 6.5f : 3.6f;
            camDist = Mathf.Lerp(camDist, targetDist, Time.deltaTime * 12f);
            Vector3 back = look * new Vector3(fighter.air != 0 ? 0.65f : 1.05f, 0.45f, -camDist);
            float len = back.magnitude;
            if (Physics.SphereCast(pivot, 0.2f, back / len, out RaycastHit hit, len, ~(1 << MapBuilder.PlayerLayer), QueryTriggerInteraction.Ignore)) len = Mathf.Max(0.4f, hit.distance - 0.1f);
            cam.transform.position = pivot + back.normalized * len;
            // under the canopy: keep the camera below it so turning never hides the soldier
            if (fighter.air == 3 && cam.transform.position.y > pivot.y + 1.6f) cam.transform.position = new Vector3(cam.transform.position.x, pivot.y + 1.6f, cam.transform.position.z);
            cam.transform.rotation = look;
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, 70f, Time.deltaTime * 14f);
        }
    }
}
