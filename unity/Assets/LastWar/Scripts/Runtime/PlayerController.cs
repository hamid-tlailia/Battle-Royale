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
        public bool Scoped => input.Scoped && fighter.Alive;
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
            if (!fighter.Alive) { body.Play("death"); body.SetGun(null); if (cc.enabled) cc.enabled = false; return; }
            if (GameBoot.Instance != null && GameBoot.Instance.Result != 0) { body.Play("idle_rifle"); return; }
            float sens = lookSensitivity * Profile.Sensitivity * (Scoped ? cam.fieldOfView / 70f : 1f) * (OnEnemy != null && AimAssistOn ? 0.55f : 1f);
            yaw += input.LookDelta.x * sens;
            pitch = Mathf.Clamp(pitch + input.LookDelta.y * sens * Mathf.Deg2Rad, -1.2f, 1.2f); // Unity screen Y points up: drag down = look down

            // weapons
            if (input.SlotPressed >= 0) { if (input.SlotPressed == fighter.slot) fighter.Reload(); else fighter.SelectSlot(input.SlotPressed); } // tap the equipped gun = reload
            if (input.ReloadPressed) fighter.Reload();
            if (input.HealPressed) fighter.Heal();
            if (input.BagPressed) BagPanel.Toggle(input, fighter);
            if (input.GearPressed) SettingsPanel.Open(input.Canvas, true);

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
            body.SetGun(fighter.Gun != null ? fighter.Gun.def.id : null);

            // fire through the crosshair: sniper one shot per press, the rest while held
            bool wantFire = fighter.Gun != null && (fighter.Gun.def.auto ? input.FireHeld : input.FirePressed);
            bool fired = wantFire && fighter.Fire(eye, aim, Scoped, flat.sqrMagnitude > 1f);

            float hs = new Vector3(cc.velocity.x, 0, cc.velocity.z).magnitude;
            if (input.Prone) body.Play("prone");
            else if (input.Crouch) body.Play(hs > 0.4f ? "crouch_walk" : fired || input.FireHeld ? "crouch_fire" : "crouch_idle");
            else if (!cc.isGrounded && !NearGround()) body.Play("jump");
            else if (hs > 5.2f || (input.Sprint && hs > 0.5f && !input.FireHeld)) body.Play("run");
            else if (hs > 0.4f) body.Play("walk_fire");
            else body.Play(fired || input.FireHeld ? "fire" : "idle_rifle");
        }

        /// <summary>direction from the soldier's eye to whatever the crosshair (screen centre) is on</summary>
        public Vector3 AimDirection(out Vector3 eye)
        {
            eye = transform.position + Vector3.up * (cc.height - 0.25f);
            Ray r = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
            Vector3 target = r.origin + r.direction * 300f;
            foreach (RaycastHit h in Physics.RaycastAll(r, 300f, ~0, QueryTriggerInteraction.Ignore))
                if (!h.collider.transform.IsChildOf(transform) && Vector3.Distance(r.origin, h.point) < Vector3.Distance(r.origin, target)) target = h.point;
            return (target - eye).normalized;
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
            Ray r = cam.ViewportPointToRay(new Vector3(.5f, .5f, 0));
            float best = float.MaxValue;
            foreach (RaycastHit h in Physics.RaycastAll(r, 250f, ~0, QueryTriggerInteraction.Ignore))
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
                if (!lockTarget.Alive || dist > 150f || lockOff > 0.6f || !Los(cam.transform.position, t, lockTarget)) lockTarget = null;
                else
                {
                    yaw += Mathf.DeltaAngle(lockYaw, qy);
                    pitch = Mathf.Clamp(pitch + (qp - lockPitch), -1.2f, 1.2f);
                    lockYaw = qy; lockPitch = qp;
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
                if (dist > 110f) continue;
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

        bool NearGround() => Physics.Raycast(transform.position + Vector3.up * 0.2f, Vector3.down, 0.5f, ~(1 << MapBuilder.PlayerLayer), QueryTriggerInteraction.Ignore);

        void LateUpdate()
        {
            if (cam == null) return;
            Vector3 pivot = transform.position + Vector3.up * (cc.height - 0.3f);
            Quaternion look = Quaternion.Euler(-pitch * Mathf.Rad2Deg, yaw, 0);
            float targetDist = Scoped ? 1.4f : 3.6f;
            camDist = Mathf.Lerp(camDist, targetDist, Time.deltaTime * 12f);
            Vector3 back = look * new Vector3(Scoped ? 0.55f : 1.05f, Scoped ? 0.15f : 0.45f, -camDist);
            float len = back.magnitude;
            if (Physics.SphereCast(pivot, 0.2f, back / len, out RaycastHit hit, len, ~(1 << MapBuilder.PlayerLayer), QueryTriggerInteraction.Ignore)) len = Mathf.Max(0.4f, hit.distance - 0.1f);
            cam.transform.position = pivot + back.normalized * len;
            cam.transform.rotation = look;
            float fov = Scoped && fighter.Gun != null ? fighter.Gun.def.scopeFov : 70f;
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, fov, Time.deltaTime * 14f);
        }
    }
}
