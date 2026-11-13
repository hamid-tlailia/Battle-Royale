using System.Collections.Generic;
using UnityEngine;

namespace LastWar
{
    /// <summary>
    /// The drop plane and the sky-dive, with the web game's numbers: the plane crosses the map at 115 m and
    /// 24 m/s on a random line; everyone rides it, jumps (JUMP, bots at their own moment, everybody before it
    /// leaves the valley), free-falls gliding toward where they look (JUMP held = straight down), and the
    /// parachute opens by itself 60 m above the ground.
    /// </summary>
    public class SkyDrop : MonoBehaviour
    {
        public const float Altitude = 115f, PlaneSpeed = 24f, FallSpeed = 16f, DiveSpeed = 30f, FallSpeedH = 20f, ChuteSpeed = 5f, ChuteSpeedH = 17f, ChuteHeight = 60f;
        public static SkyDrop Instance { get; private set; }
        public Vector3 A, B;         // route ends (Unity coordinates)
        public float Duration, T;   // seconds on the route, time since take-off
        public Vector3 PlanePos => Vector3.Lerp(A, B, Mathf.Clamp01(T / Duration)) + Dir * Mathf.Max(0, T - Duration) * PlaneSpeed;
        public Vector3 Dir => (B - A).normalized;
        public float Progress => Mathf.Clamp01(T / Duration);
        public bool Flying => T < Duration + 4f;
        Transform plane; readonly List<Transform> props = new List<Transform>();
        float playRadius; AudioSource engine, wind;

        public static SkyDrop Create(MapData map)
        {
            var s = new GameObject("SkyDrop").AddComponent<SkyDrop>();
            Instance = s; s.playRadius = map.playRadius;
            // the web game's planePath: random heading, offset up to 40 m from the centre, starts outside the map
            float a = Random.value * Mathf.PI * 2f, off = (Random.value - 0.5f) * 80f, R = map.size / 2f + 25f;
            float nx = -Mathf.Sin(a), nz = Mathf.Cos(a);
            Vector2 wa = new Vector2(Mathf.Cos(a) * R + nx * off, Mathf.Sin(a) * R + nz * off), wb = new Vector2(-Mathf.Cos(a) * R + nx * off, -Mathf.Sin(a) * R + nz * off);
            s.A = Coords.ToUnity(wa.x, Altitude, wa.y); s.B = Coords.ToUnity(wb.x, Altitude, wb.y);
            s.Duration = Vector3.Distance(s.A, s.B) / PlaneSpeed;
            s.BuildPlane();
            s.engine = s.Loop("plane", 0.9f); s.wind = s.Loop("wind", 0f);
            return s;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        AudioSource Loop(string clip, float vol)
        {
            var src = gameObject.AddComponent<AudioSource>();
            src.clip = Resources.Load<AudioClip>("Audio/" + clip); src.loop = true; src.volume = vol; src.spatialBlend = 0f;
            if (src.clip != null) src.Play();
            return src;
        }

        /// <summary>original low-poly twin-prop cargo plane (~22 m), nose toward +Z (the web PlaneView)</summary>
        void BuildPlane()
        {
            plane = new GameObject("Plane").transform;
            // real transport plane (C130 by manilov.ap, CC BY), nose turned to +Z; the box plane below is only a fallback
            GameObject model = MapBuilder.Model("plane");
            if (model != null)
            {
                Transform m = Instantiate(model, plane, false).transform;
                // the file is turned off its axis (and glTFast mirrors X): nose to +Z, centred, ~26 m long
                const float ps = 0.85f;
                m.localScale = Vector3.one * ps; m.localRotation = Quaternion.Euler(0, -53.13f, 0);
                m.localPosition = new Vector3(1.462f * ps, -4.065f * ps, -4.963f * ps);
                foreach (Renderer r in m.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = r.sharedMaterials;
                    for (int k = 0; k < mats.Length; k++) mats[k] = MapBuilder.LitFrom(mats[k]);
                    r.sharedMaterials = mats; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                return;
            }
            Mesh cyl = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx"), sph = Resources.GetBuiltinResource<Mesh>("Sphere.fbx"), cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            Color body = new Color(0x6d / 255f, 0x7f / 255f, 0x5a / 255f), dark = new Color(0x3b / 255f, 0x46 / 255f, 0x36 / 255f), glass = new Color(0x2a / 255f, 0x3a / 255f, 0x4a / 255f), stripe = new Color(0xd8 / 255f, 0xb0 / 255f, 0x4a / 255f);
            Transform Part(Mesh m, Color c, Vector3 pos, Vector3 scale, Vector3 euler, Transform parent = null)
            {
                var g = new GameObject("part", typeof(MeshFilter), typeof(MeshRenderer));
                g.transform.SetParent(parent ?? plane, false); g.transform.localPosition = pos; g.transform.localScale = scale; g.transform.localEulerAngles = euler;
                g.GetComponent<MeshFilter>().sharedMesh = m; var r = g.GetComponent<MeshRenderer>(); r.sharedMaterial = MapBuilder.NewLit(c);
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                return g.transform;
            }
            Part(cyl, body, Vector3.zero, new Vector3(3.8f, 8f, 3.8f), new Vector3(90, 0, 0));          // fuselage 16 m
            Part(sph, body, new Vector3(0, 0, 8f), new Vector3(3.8f, 3.8f, 5f), Vector3.zero);           // nose
            Part(sph, body, new Vector3(0, 0.6f, -9f), new Vector3(3.2f, 3.2f, 8f), Vector3.zero);       // tail cone
            Part(cube, glass, new Vector3(0, 1.2f, 9.2f), new Vector3(2.4f, 0.9f, 1.6f), Vector3.zero);  // cockpit
            Part(cube, body, new Vector3(0, 1f, 1.5f), new Vector3(24f, 0.35f, 3.4f), Vector3.zero);     // wings
            Part(cube, stripe, new Vector3(0, 1f, 3f), new Vector3(24.2f, 0.36f, 0.5f), Vector3.zero);
            Part(cube, body, new Vector3(0, 1.2f, -12.2f), new Vector3(8f, 0.25f, 2f), Vector3.zero);    // tail plane
            Part(cube, body, new Vector3(0, 2.8f, -12.4f), new Vector3(0.3f, 3.2f, 2.4f), Vector3.zero); // fin
            Part(cube, dark, new Vector3(0, -1.85f, -2f), new Vector3(3.6f, 0.15f, 6f), Vector3.zero);   // open cargo ramp
            foreach (float x in new[] { -5.5f, 5.5f })
            {
                Part(cyl, dark, new Vector3(x, 0.6f, 2.4f), new Vector3(1.5f, 1.7f, 1.5f), new Vector3(90, 0, 0)); // engine
                var hub = new GameObject("prop").transform; hub.SetParent(plane, false); hub.localPosition = new Vector3(x, 0.6f, 4.2f);
                Part(cube, new Color(.13f, .13f, .13f), Vector3.zero, new Vector3(4.2f, 0.35f, 0.12f), Vector3.zero, hub);
                props.Add(hub);
            }
        }

        void Update()
        {
            T += Time.deltaTime;
            plane.gameObject.SetActive(T < Duration + 6f);
            if (plane.gameObject.activeSelf)
            {
                plane.position = PlanePos;
                plane.rotation = Quaternion.LookRotation(Dir) * Quaternion.Euler(0, 0, Mathf.Sin(Time.time / 1.4f) * 1.7f);
                foreach (Transform p in props) p.Rotate(0, 0, Time.deltaTime * 2300f, Space.Self);
            }
            // sound: the engine loud inside, fading once you are out; wind while falling
            Fighter me = GameBoot.Instance != null && GameBoot.Instance.Player != null ? GameBoot.Instance.Player.fighter : null;
            int air = me != null ? me.air : 0;
            float d = Camera.main != null ? Vector3.Distance(Camera.main.transform.position, PlanePos) : 999f;
            engine.volume = !plane.gameObject.activeSelf ? Mathf.MoveTowards(engine.volume, 0, Time.deltaTime) : air == 1 ? 0.9f : Mathf.Max(0, 1 - d / 350f) * 0.6f;
            wind.volume = Mathf.MoveTowards(wind.volume, air == 2 ? 0.8f : air == 3 ? 0.3f : 0f, Time.deltaTime * 2f);
        }

        /// <summary>may a passenger jump now? only above the valley</summary>
        public bool OverMap => new Vector2(PlanePos.x, PlanePos.z).magnitude < playRadius - 12f;
        /// <summary>everybody must be out: the plane is leaving the valley or the route is over</summary>
        public bool MustJump => (Progress > 0.5f && !OverMap) || T >= Duration;
    }
}

namespace LastWar
{
    /// <summary>Plane ride, free fall and parachute for one fighter (player or bot): the web game's simulateMove air branch.</summary>
    public static class AirMove
    {
        /// <summary>ride the plane (hidden inside it)</summary>
        public static void Ride(Fighter f, CharacterController cc)
        {
            var sky = SkyDrop.Instance;
            if (cc.enabled) cc.enabled = false;
            f.transform.position = sky.PlanePos + Vector3.down * 3f;
        }

        public static void Jump(Fighter f)
        {
            var sky = SkyDrop.Instance;
            f.air = 2;
            float side = ((f.GetInstanceID() * 0.618f) % 1f - 0.5f) * 4f;
            f.transform.position = sky.PlanePos + Vector3.down * 3f + Vector3.Cross(Vector3.up, sky.Dir) * side;
            f.airVel = sky.Dir * SkyDrop.PlaneSpeed * 0.4f;
        }

        /// <summary>free fall / parachute step. wish: horizontal steering (world, length 0..1); straight: drop straight down; down: how far the view looks down (0..1)</summary>
        public static void Fall(Fighter f, CharacterController cc, Vector3 wish, bool straight, float down, float playRadius, float dt)
        {
            Transform t = f.transform;
            float ground = GroundBelow(t.position);
            float above = t.position.y - ground;
            if (f.air == 2 && above < SkyDrop.ChuteHeight) f.air = 3; // the parachute opens by itself
            float hs = straight ? 0 : f.air == 2 ? SkyDrop.FallSpeedH : SkyDrop.ChuteSpeedH;
            Vector3 target = wish * hs;
            float k = Mathf.Min(1, dt * 3);
            f.airVel.x += (target.x - f.airVel.x) * k; f.airVel.z += (target.z - f.airVel.z) * k;
            float steer = Mathf.Min(1, wish.magnitude);
            float vyT = f.air == 2 ? (straight ? -SkyDrop.DiveSpeed : -(SkyDrop.FallSpeed + down * 8)) : -(SkyDrop.ChuteSpeed + steer * 1.5f + down * 3);
            f.airVel.y += (vyT - f.airVel.y) * Mathf.Min(1, dt * 2.5f);
            Vector3 p = t.position + f.airVel * dt;
            // stay above the valley
            Vector2 flat = new Vector2(p.x, p.z); float lim = playRadius - 4;
            if (flat.magnitude > lim) { flat = flat.normalized * lim; p.x = flat.x; p.z = flat.y; }
            float g2 = GroundBelow(p);
            if (p.y - g2 < 0.3f)
            {
                // touch down (ground or a roof)
                p.y = g2 + 0.05f; f.air = 0; f.airVel = Vector3.zero;
                t.position = p; cc.enabled = true;
                return;
            }
            t.position = p;
        }

        static float GroundBelow(Vector3 p)
        {
            int mask = ~(1 << MapBuilder.PlayerLayer);
            if (Physics.Raycast(p + Vector3.up * 0.5f, Vector3.down, out RaycastHit h, 600f, mask, QueryTriggerInteraction.Ignore)) return h.point.y;
            MapData m = GameBoot.Instance != null ? GameBoot.Instance.Map : null;
            return m != null ? m.HeightAt(-p.x, p.z) : 0f;
        }
    }
}
