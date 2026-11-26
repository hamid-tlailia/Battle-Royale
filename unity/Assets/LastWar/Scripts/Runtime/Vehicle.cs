using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LastWar
{
    /// <summary>
    /// Jeeps to cross the big maps (GAZ-67, CC0). A few stand on open ground round the map; walk up to one and DRIVE
    /// appears: the joystick drives (up = gas, down = brake / reverse, sideways = steer), the view follows behind,
    /// EXIT gets you out beside it. The driver sits in the seat and can be shot; the jeep stops at walls, trees and
    /// the water's edge, follows the ground's slopes, and its wheels turn and steer.
    /// ?cartest=1: a jeep put 6 m in front of you once you land.
    /// </summary>
    public class Vehicle : MonoBehaviour
    {
        public static readonly List<Vehicle> All = new List<Vehicle>();
        /// <summary>the jeep the player drives (null on foot)</summary>
        public static Vehicle Mine;
        public Fighter driver;
        public float Speed; public float Yaw;
        const float Length = 3.7f, MaxSpeed = 20f, MaxBack = 6f, Accel = 7f, Brake = 14f, SteerMax = 32f, Wheelbase = 2.1f, WheelR = 0.38f;
        Transform model; Transform[] wheels = new Transform[4]; Quaternion[] wheelRest = new Quaternion[4]; float spin, steer;
        Vector3 seat; Collider[] own; AudioSource engine;

        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }

        public static void SpawnMap(MapData map, int seed, bool test)
        {
            Mine = null;
            var rnd = new System.Random(seed ^ 0x5eed);
            int want = Mathf.Clamp(Mathf.RoundToInt(map.playRadius / 45f), 4, 10), made = 0;
            for (int k = 0; k < 400 && made < want; k++)
            {
                float a = (float)rnd.NextDouble() * Mathf.PI * 2f, r = Mathf.Sqrt((float)rnd.NextDouble()) * map.playRadius * 0.85f;
                Vector3 at = new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
                if (!OpenGround(map, ref at, 3.2f)) continue;
                bool near = false; foreach (Vehicle v in All) if ((v.transform.position - at).sqrMagnitude < 90f * 90f) near = true;
                if (near) continue;
                Make(at, (float)rnd.NextDouble() * 360f); made++;
            }
            if (test) new GameObject("CarTest").AddComponent<Test>();
        }

        class Test : MonoBehaviour
        {
            void Update()
            {
                var gb = GameBoot.Instance; if (gb == null || gb.Player == null || gb.Player.fighter.air != 0) return;
                Transform p = gb.Player.transform; Vector3 at = p.position + p.forward * 4f + p.right * 1.5f;
                OpenGround(gb.Map, ref at, 0f); Make(at, p.eulerAngles.y + 90f); Destroy(gameObject);
            }
        }

        /// <summary>flat open ground (no house, rock or tree within r), above the water</summary>
        static bool OpenGround(MapData map, ref Vector3 at, float r)
        {
            int mask = ~(1 << MapBuilder.PlayerLayer);
            if (!Physics.Raycast(new Vector3(at.x, 400f, at.z), Vector3.down, out RaycastHit h, 800f, mask, QueryTriggerInteraction.Ignore)) return false;
            float terrain = map.HeightAt(-h.point.x, h.point.z);
            if (r > 0f && (Mathf.Abs(h.point.y - terrain) > 0.35f || h.normal.y < 0.94f)) return false; // (a roof, a rock, a slope)
            if (MapBuilder.SeaLevel > -999f && h.point.y < MapBuilder.SeaLevel + 1f) return false;
            // something standing there besides the ground itself (a house, a tree, a rock, another jeep)
            if (r > 0f)
                foreach (Collider c in Physics.OverlapSphere(h.point + Vector3.up * (r * 0.5f + 0.6f), r, mask, QueryTriggerInteraction.Ignore))
                    if (c != MapBuilder.Ground && c.GetComponentInParent<Fighter>() == null) return false;
            at = h.point; return true;
        }

        public static Vehicle Make(Vector3 at, float yaw)
        {
            var go = new GameObject("Jeep"); go.transform.position = at;
            var v = go.AddComponent<Vehicle>(); v.Yaw = yaw; v.Build();
            v.Settle(0f);
            return v;
        }

        void Build()
        {
            GameObject src = MapBuilder.Model("veh_jeep");
            model = src != null ? Instantiate(src, transform, false).transform : GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
            model.SetParent(transform, false);
            foreach (Collider c in model.GetComponentsInChildren<Collider>()) Destroy(c);
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true)) { var mats = r.sharedMaterials; for (int k = 0; k < mats.Length; k++) mats[k] = MapBuilder.LitFrom(mats[k]); r.sharedMaterials = mats; }
            // its size: the model's front is its -Z (glTF); turned to face +Z, scaled to a jeep's length, wheels on the ground
            model.localRotation = Quaternion.Euler(0, 180f, 0);
            Bounds b = Box(model);
            float sc = Length / Mathf.Max(0.01f, Mathf.Max(b.size.x, b.size.z));
            if (b.size.x > b.size.z) model.localRotation *= Quaternion.Euler(0, 90f, 0);
            model.localScale *= sc; b = Box(model);
            model.localPosition -= new Vector3(b.center.x, b.min.y, b.center.z) - transform.position;
            string[] names = { "wheel FL", "wheel FR", "wheel RL", "wheel RR" };
            for (int i = 0; i < 4; i++) foreach (Transform t in model.GetComponentsInChildren<Transform>()) if (t.name == names[i]) { wheels[i] = t; wheelRest[i] = t.localRotation; }
            b = Box(model);
            var col = gameObject.AddComponent<BoxCollider>();
            col.center = transform.InverseTransformPoint(b.center) + Vector3.up * 0.1f; col.size = new Vector3(b.size.x * 0.92f, b.size.y * 0.55f, b.size.z * 0.95f);
            own = GetComponentsInChildren<Collider>();
            // the driver's seat: left of the middle, a little behind (the steering wheel's side)
            seat = new Vector3(-b.size.x * 0.2f, 0.2f, -0.15f);
            Transform sw = null; foreach (Transform t in model.GetComponentsInChildren<Transform>()) if (t.name == "steer wheel") sw = t;
            if (sw != null) { Vector3 l = transform.InverseTransformPoint(sw.position); seat = new Vector3(l.x, 0.2f, l.z - 0.55f); }
            var clip = Resources.Load<AudioClip>("Audio/plane");
            if (clip != null) { engine = gameObject.AddComponent<AudioSource>(); engine.clip = clip; engine.loop = true; engine.spatialBlend = 0.9f; engine.maxDistance = 120f; engine.volume = 0f; engine.pitch = 0.6f; engine.Play(); }
        }

        static Bounds Box(Transform t)
        {
            bool any = false; Bounds b = new Bounds(t.position, Vector3.zero);
            foreach (Renderer r in t.GetComponentsInChildren<Renderer>()) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
            return b;
        }

        /// <summary>the ground under the four wheels: height and tilt</summary>
        bool Settle(float dt)
        {
            Quaternion yawQ = Quaternion.Euler(0, Yaw, 0);
            Vector3 p = transform.position; float hw = 0.75f, hl = Wheelbase * 0.5f;
            Vector3[] pts = { new Vector3(-hw, 0, hl), new Vector3(hw, 0, hl), new Vector3(-hw, 0, -hl), new Vector3(hw, 0, -hl) };
            float[] y = new float[4]; int ok = 0;
            for (int i = 0; i < 4; i++)
            {
                Vector3 w = p + yawQ * pts[i];
                // the highest surface under the wheel that it can climb (a kerb, a step - not a branch overhead)
                y[i] = float.NegativeInfinity;
                foreach (var h in Physics.RaycastAll(w + Vector3.up * 2.5f, Vector3.down, 6f, ~(1 << MapBuilder.PlayerLayer), QueryTriggerInteraction.Ignore))
                {
                    if (System.Array.IndexOf(own, h.collider) >= 0 || h.collider.GetComponentInParent<Fighter>() != null || h.collider.GetComponentInParent<Vehicle>() != null) continue;
                    if (h.point.y > p.y + 0.9f) continue;
                    if (h.point.y > y[i]) y[i] = h.point.y;
                }
                if (float.IsNegativeInfinity(y[i])) y[i] = p.y; else ok++;
            }
            if (ok < 3) return false;
            float front = (y[0] + y[1]) * 0.5f, back = (y[2] + y[3]) * 0.5f, left = (y[0] + y[2]) * 0.5f, right = (y[1] + y[3]) * 0.5f;
            float pitch = -Mathf.Atan2(front - back, Wheelbase) * Mathf.Rad2Deg, roll = Mathf.Atan2(left - right, hw * 2f) * Mathf.Rad2Deg;
            Quaternion want = yawQ * Quaternion.Euler(pitch, 0, roll);
            float gy = (y[0] + y[1] + y[2] + y[3]) * 0.25f;
            transform.position = new Vector3(p.x, gy, p.z);
            transform.rotation = dt <= 0f ? want : Quaternion.Slerp(transform.rotation, want, 1f - Mathf.Exp(-dt * 10f));
            return true;
        }

        public Vector3 SeatWorld => transform.TransformPoint(seat);

        /// <summary>one step of driving: stick y = gas / brake / reverse, x = steer</summary>
        public void Drive(Vector2 stick, float dt)
        {
            float target = stick.y > 0.1f ? stick.y * MaxSpeed : stick.y < -0.1f ? (Speed > 0.5f ? 0f : stick.y * MaxBack) : 0f;
            float rate = (Mathf.Sign(target - Speed) != Mathf.Sign(Speed) && Mathf.Abs(Speed) > 0.3f) || Mathf.Abs(stick.y) < 0.1f ? Brake * (Mathf.Abs(stick.y) < 0.1f ? 0.35f : 1f) : Accel;
            Speed = Mathf.MoveTowards(Speed, target, rate * dt);
            steer = Mathf.MoveTowards(steer, stick.x * SteerMax * Mathf.Lerp(1f, 0.45f, Mathf.Abs(Speed) / MaxSpeed), 140f * dt);
            Yaw += Speed / Wheelbase * Mathf.Tan(steer * Mathf.Deg2Rad) * Mathf.Rad2Deg * dt;
            Vector3 fwd = Quaternion.Euler(0, Yaw, 0) * Vector3.forward, step = fwd * Speed * dt;
            if (Mathf.Abs(Speed) > 0.01f)
            {
                // stopped by walls, trees, rocks and other jeeps (not by people: they are pushed aside below)
                Vector3 from = transform.position + Vector3.up * 0.9f; float reach = Length * 0.5f + 0.1f;
                foreach (var h in Physics.SphereCastAll(from, 0.7f, step.normalized * Mathf.Sign(1f), step.magnitude + reach, ~(1 << MapBuilder.PlayerLayer), QueryTriggerInteraction.Ignore))
                {
                    if (System.Array.IndexOf(own, h.collider) >= 0 || h.collider.GetComponentInParent<Fighter>() != null || h.collider == MapBuilder.Ground || h.distance <= 0f) continue;
                    if (h.normal.y > 0.7f) continue; // (a gentle ramp: driven over)
                    if (Mathf.Abs(Speed) > 9f) Sfx.Play("land", h.point, 1f, 0.6f);
                    Speed = -Speed * 0.15f; step = Vector3.zero; break;
                }
                Vector3 next = transform.position + step;
                // not into the sea
                if (MapBuilder.SeaLevel > -999f && GameBoot.Instance != null && GameBoot.Instance.Map.HeightAt(-next.x, next.z) < MapBuilder.SeaLevel + 0.3f) { Speed = 0f; step = Vector3.zero; }
                transform.position += step;
            }
            if (!Settle(dt)) { transform.position -= step; Speed = 0f; }
            // people in the way are knocked aside (and hurt when hit fast)
            if (Mathf.Abs(Speed) > 3f && GameBoot.Instance != null)
                foreach (Fighter f in GameBoot.Instance.Fighters)
                {
                    if (f == driver || !f.Alive || f.air != 0) continue;
                    Vector3 d = f.transform.position - transform.position; d.y = 0;
                    if (Mathf.Abs(Vector3.Dot(d, fwd)) < Length * 0.55f && Mathf.Abs(Vector3.Dot(d, Quaternion.Euler(0, 90, 0) * fwd)) < 1.1f && Vector3.Dot(d, fwd) * Speed > 0f)
                    {
                        var cc = f.GetComponent<CharacterController>(); Vector3 side = Vector3.Cross(Vector3.up, fwd) * Mathf.Sign(Vector3.Dot(d, Vector3.Cross(Vector3.up, fwd)) + 0.001f);
                        if (cc != null && cc.enabled) cc.Move(side * 1.6f + fwd * Mathf.Sign(Speed) * 0.6f);
                        if (Mathf.Abs(Speed) > 8f) f.TakeDamage(Mathf.Abs(Speed) * 4f, driver, "rifle", false, f.transform.position + Vector3.up);
                    }
                }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (driver == null) { Speed = Mathf.MoveTowards(Speed, 0f, Brake * dt); if (Mathf.Abs(Speed) > 0.01f) Drive(Vector2.zero, dt); }
            spin += Speed / WheelR * Mathf.Rad2Deg * dt;
            for (int i = 0; i < 4; i++)
                if (wheels[i] != null) wheels[i].localRotation = wheelRest[i] * Quaternion.Euler(0, i < 2 ? -steer : 0f, 0) * Quaternion.Euler(spin, 0, 0);
            if (engine != null) { engine.volume = driver != null ? 0.25f + Mathf.Abs(Speed) / MaxSpeed * 0.3f : 0f; engine.pitch = 0.55f + Mathf.Abs(Speed) / MaxSpeed * 0.5f; }
        }

        /// <summary>the jeep near enough to get in (null if none)</summary>
        public static Vehicle Near(Vector3 p)
        {
            Vehicle best = null; float bd = 4.6f * 4.6f;
            foreach (Vehicle v in All) { if (v.driver != null) continue; float d = (v.transform.position - p).sqrMagnitude; if (d < bd) { bd = d; best = v; } }
            return best;
        }

        /// <summary>where you step out: beside the driver's door, on the ground</summary>
        public Vector3 ExitPoint()
        {
            Vector3 side = transform.TransformPoint(new Vector3(-1.9f, 0f, -0.2f));
            if (Physics.Raycast(side + Vector3.up * 3f, Vector3.down, out RaycastHit h, 8f, ~(1 << MapBuilder.PlayerLayer), QueryTriggerInteraction.Ignore)) side = h.point;
            return side + Vector3.up * 0.1f;
        }

        // ---------------------------------------------------------------- the DRIVE / EXIT button (on the HUD)
        public class Button : MonoBehaviour
        {
            PlayerController player; Image img; Text txt;
            public static void Create(TouchControls input, PlayerController p)
            {
                var b = new GameObject("CarButton", typeof(RectTransform)).AddComponent<Button>(); b.player = p;
                b.transform.SetParent(input.Root, false);
                b.img = UiKit.Img(b.transform, "bg", UiKit.Round, new Vector2(.5f, .5f), Vector2.zero, new Vector2(84, 30), new Color(.1f, .12f, .14f, .8f), true);
                var rt = (RectTransform)b.transform; rt.anchorMin = rt.anchorMax = new Vector2(.6f, .62f); // (clear of the fire / scope buttons) rt.sizeDelta = Vector2.zero;
                b.img.gameObject.AddComponent<Outline>().effectColor = new Color(1f, .83f, .3f, .8f);
                b.txt = UiKit.Txt(b.img.rectTransform, new Vector2(.5f, .5f), Vector2.zero, new Vector2(84, 30), 12, TextAnchor.MiddleCenter, new Color(1f, .83f, .3f));
                b.txt.rectTransform.pivot = new Vector2(.5f, .5f);
                Clickable.Add(b.img.gameObject, b.Tap);
            }
            void Tap()
            {
                if (Mine != null) player.ExitVehicle();
                else { var v = Near(player.transform.position); if (v != null) player.EnterVehicle(v); }
            }
            void Update()
            {
                bool show = player != null && player.fighter.Alive && player.fighter.air == 0 && (Mine != null || Near(player.transform.position) != null) && !Hud.Celebrating;
                img.gameObject.SetActive(show);
                if (show && Input.GetKeyDown(KeyCode.F)) Tap(); // (keyboard: F)
                if (show) txt.text = Mine != null ? "EXIT" : "DRIVE";
            }
        }
    }
}
