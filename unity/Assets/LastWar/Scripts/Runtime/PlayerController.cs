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
            pc.body = SoldierBody.Attach(go);
            return pc;
        }

        void Awake() { cc = GetComponent<CharacterController>(); }

        void Update()
        {
            float dt = Time.deltaTime;
            if (!fighter.Alive) { body.Play("death"); body.SetGun(null); if (cc.enabled) cc.enabled = false; return; }
            if (GameBoot.Instance != null && GameBoot.Instance.Result != 0) { body.Play("idle_rifle"); return; }
            float sens = lookSensitivity * (Scoped ? cam.fieldOfView / 70f : 1f);
            yaw += input.LookDelta.x * sens;
            pitch = Mathf.Clamp(pitch + input.LookDelta.y * sens * Mathf.Deg2Rad, -1.2f, 1.2f); // Unity screen Y points up: drag down = look down

            // weapons
            if (input.SlotPressed >= 0) { if (input.SlotPressed == fighter.slot) fighter.Reload(); else fighter.SelectSlot(input.SlotPressed); } // tap the equipped gun = reload
            if (input.ReloadPressed) fighter.Reload();

            // movement relative to the camera's heading
            Vector2 mv = input.Move;
            if (input.Sprint && mv.y > -0.3f && !Scoped) mv = new Vector2(mv.x * 0.5f, 1f);
            bool sprinting = input.Sprint && mv.y > 0.5f && !input.FireHeld && !Scoped;
            float speed = sprinting ? SprintSpeed : WalkSpeed;
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

            transform.rotation = Quaternion.Euler(0, yaw, 0);
            Vector3 aim = AimDirection(out Vector3 eye);
            body.AimDir = aim;
            body.SetGun(fighter.Gun != null ? fighter.Gun.def.id : null);

            // fire through the crosshair: sniper one shot per press, the rest while held
            bool wantFire = fighter.Gun != null && (fighter.Gun.def.auto ? input.FireHeld : input.FirePressed);
            bool fired = wantFire && fighter.Fire(eye, aim, Scoped, flat.sqrMagnitude > 1f);

            float hs = new Vector3(cc.velocity.x, 0, cc.velocity.z).magnitude;
            if (!cc.isGrounded && !NearGround()) body.Play("jump");
            else if (hs > 5.2f || (input.Sprint && hs > 0.5f && !input.FireHeld)) body.Play("run");
            else if (hs > 0.4f) body.Play("walk_fire");
            else body.Play(fired || input.FireHeld ? "fire" : "idle_rifle");
        }

        /// <summary>direction from the soldier's eye to whatever the crosshair (screen centre) is on</summary>
        public Vector3 AimDirection(out Vector3 eye)
        {
            eye = transform.position + Vector3.up * 1.55f;
            Ray r = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
            Vector3 target = r.origin + r.direction * 300f;
            foreach (RaycastHit h in Physics.RaycastAll(r, 300f, ~0, QueryTriggerInteraction.Ignore))
                if (!h.collider.transform.IsChildOf(transform) && Vector3.Distance(r.origin, h.point) < Vector3.Distance(r.origin, target)) target = h.point;
            return (target - eye).normalized;
        }

        bool NearGround() => Physics.Raycast(transform.position + Vector3.up * 0.2f, Vector3.down, 0.5f, ~(1 << MapBuilder.PlayerLayer), QueryTriggerInteraction.Ignore);

        void LateUpdate()
        {
            if (cam == null) return;
            Vector3 pivot = transform.position + Vector3.up * 1.5f;
            Quaternion look = Quaternion.Euler(-pitch * Mathf.Rad2Deg, yaw, 0);
            float targetDist = Scoped ? 1.4f : 3.6f;
            camDist = Mathf.Lerp(camDist, targetDist, Time.deltaTime * 12f);
            Vector3 back = look * new Vector3(Scoped ? 0.55f : 0.65f, Scoped ? 0.15f : 0.35f, -camDist);
            float len = back.magnitude;
            if (Physics.SphereCast(pivot, 0.2f, back / len, out RaycastHit hit, len, ~(1 << MapBuilder.PlayerLayer), QueryTriggerInteraction.Ignore)) len = Mathf.Max(0.4f, hit.distance - 0.1f);
            cam.transform.position = pivot + back.normalized * len;
            cam.transform.rotation = look;
            float fov = Scoped && fighter.Gun != null ? fighter.Gun.def.scopeFov : 70f;
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, fov, Time.deltaTime * 14f);
        }
    }
}
