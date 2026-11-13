using UnityEngine;

namespace LastWar
{
    /// <summary>
    /// Third-person soldier: walk / auto-run / jump with the web game's numbers, camera over the shoulder
    /// that avoids walls, legacy animation clips from the soldier model (idle, walk, run, jump).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        public const float WalkSpeed = 4.6f, SprintSpeed = 6.6f, JumpVelocity = 7.2f, Gravity = -22f, GroundAccel = 40f, AirAccel = 10f;
        public TouchControls input;
        public Camera cam;
        public float yaw, pitch = -0.15f;
        public float lookSensitivity = 0.2f; // degrees per screen pixel

        CharacterController cc;
        Vector3 velocity;
        Transform model;
        Animation anim;
        string currentClip;
        float camDist = 3.6f;

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
            pc.AttachModel();
            return pc;
        }

        void AttachModel()
        {
            GameObject src = MapBuilder.Model("soldier");
            if (src == null) return;
            var m = Instantiate(src, transform, false);
            // normalise to 1.8 m tall, feet on the ground
            var rs = m.GetComponentsInChildren<Renderer>();
            if (rs.Length > 0)
            {
                Bounds b = rs[0].bounds; foreach (Renderer r in rs) b.Encapsulate(r.bounds);
                float s = 1.8f / Mathf.Max(0.01f, b.size.y);
                m.transform.localScale *= s;
                m.transform.localPosition = new Vector3(0, -(b.min.y - transform.position.y) * s, 0);
            }
            model = m.transform;
            anim = m.GetComponentInChildren<Animation>();
            Play("idle");
        }

        void Awake() { cc = GetComponent<CharacterController>(); }

        void Play(string clip)
        {
            if (anim == null || clip == currentClip || anim.GetClip(clip) == null) return;
            anim.CrossFade(clip, 0.2f); currentClip = clip;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            yaw += input.LookDelta.x * lookSensitivity;
            pitch = Mathf.Clamp(pitch - input.LookDelta.y * lookSensitivity * Mathf.Deg2Rad, -1.2f, 1.2f);

            // movement relative to the camera's heading
            Vector2 mv = input.Move;
            if (input.Sprint && mv.y > -0.3f) mv = new Vector2(mv.x * 0.5f, 1f);
            bool sprinting = input.Sprint && mv.y > 0.5f;
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

            // body faces where you look; animation by speed
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            float hs = new Vector3(cc.velocity.x, 0, cc.velocity.z).magnitude;
            if (!cc.isGrounded && !NearGround()) Play("jump");
            else Play(hs > 5.2f || (input.Sprint && hs > 0.5f) ? "run" : hs > 0.4f ? "walk" : "idle");
        }

        bool NearGround() => Physics.Raycast(transform.position + Vector3.up * 0.2f, Vector3.down, 0.5f, ~(1 << MapBuilder.PlayerLayer), QueryTriggerInteraction.Ignore);

        void LateUpdate()
        {
            if (cam == null) return;
            Vector3 pivot = transform.position + Vector3.up * 1.5f;
            Quaternion look = Quaternion.Euler(-pitch * Mathf.Rad2Deg, yaw, 0);
            Vector3 back = look * new Vector3(0.65f, 0.35f, -camDist);
            float len = back.magnitude;
            if (Physics.SphereCast(pivot, 0.2f, back / len, out RaycastHit hit, len, ~(1 << MapBuilder.PlayerLayer), QueryTriggerInteraction.Ignore)) len = Mathf.Max(0.4f, hit.distance - 0.1f);
            cam.transform.position = pivot + back.normalized * len;
            cam.transform.rotation = look;
        }
    }
}
