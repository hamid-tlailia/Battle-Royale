using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LastWar
{
    /// <summary>Entry point of the Main scene: sun, sky, camera, map; then the lobby, or a match after PLAY.</summary>
    public class GameBoot : MonoBehaviour
    {
        /// <summary>false = lobby (app start, after a match), true = the next load is a match</summary>
        public static bool InMatch;
        public static GameBoot Instance { get; private set; }
        public MapData Map { get; private set; }
        public PlayerController Player { get; private set; }
        public readonly List<Fighter> Fighters = new List<Fighter>();
        public int AliveCount { get; private set; }
        public int Result { get; private set; }   // 0 playing, 1 victory, -1 eliminated
        public int Rank { get; private set; }
        float startTime;
        public float MatchTime => Time.time - startTime;
        public const int BotCount = 15;
        static readonly string[] BotNames = { "Viper", "Nomad", "Ghost", "Raven", "Falcon", "Cobra", "Wolf", "Shadow", "Hunter", "Blaze", "Storm", "Rogue", "Jackal", "Titan", "Spectre", "Onyx", "Ranger", "Sable", "Mako", "Drift" };
        Text fps;
        float fpsAcc; int fpsFrames;

        void Start()
        {
            Instance = this; startTime = Time.time;
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            Profile.ApplyQuality();
            SoldierBody.LogRig = (Application.absoluteURL ?? "").Contains("rig=1");
            if ((Application.absoluteURL ?? "").Contains("skin=")) { string u = Application.absoluteURL; int si = u.IndexOf("skin=") + 5; int sv = 0; while (si < u.Length && char.IsDigit(u[si])) sv = sv * 10 + (u[si++] - '0'); Profile.Skin = sv; }

            // test switch: ?map=<id> (green / desert / classic)
            { string u = Application.absoluteURL ?? ""; int mi = u.IndexOf("map="); if (mi >= 0) { string id = new string(u.Substring(mi + 4).TakeWhile(char.IsLetter).ToArray()); if (id.Length > 0) Profile.Map = id; } }

            // sun + ambient + fog
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.3f; sun.color = new Color(1f, 0.95f, 0.85f);
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.72f, 0.82f, 0.95f);
            RenderSettings.ambientEquatorColor = new Color(0.55f, 0.6f, 0.55f);
            RenderSettings.ambientGroundColor = new Color(0.3f, 0.32f, 0.25f);
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.83f, 0.91f, 0.95f); RenderSettings.fogStartDistance = 120f; RenderSettings.fogEndDistance = 380f;

            var cam = new GameObject("Camera").AddComponent<Camera>();
            cam.tag = "MainCamera";
            // hazy horizon: the far land fades into the same colour the sky has down there (no white rim on blue)
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = RenderSettings.fogColor;
            cam.fieldOfView = 70f; cam.nearClipPlane = 0.15f; cam.farClipPlane = 1700f;
            BuildSky(cam);
            cam.gameObject.AddComponent<AudioListener>();

            Map = MapData.Load(Profile.Map);
            MapBuilder.Build(Map);
            // test switch: ?play=1 in the WebGL URL goes straight into a match
            if (!InMatch && (Application.absoluteURL ?? "").Contains("play=1")) InMatch = true;
            if (!InMatch) { Lobby.Create(Map, cam); Loader.HideSoon(); return; }

            var controls = TouchControls.Create();
            Vec2 sp = Map.spawns != null && Map.spawns.Length > 0 ? Map.spawns[0] : new Vec2();
            Vector3 pos = Coords.ToUnity(sp.x, Map.HeightAt(sp.x, sp.z) + 0.2f, sp.z);
            // test switches in the WebGL page URL: ?god=1 (no damage), ?house=N (start next to the N-th house)
            string url = Application.absoluteURL ?? "";
            int hq = url.IndexOf("house=");
            if (hq >= 0 && int.TryParse(new string(url.Substring(hq + 6).TakeWhile(char.IsDigit).ToArray()), out int hn))
            {
                int seen = 0;
                foreach (PropData pr in Map.props)
                    if (pr.p.StartsWith("house") && ++seen == hn) { pos = Coords.ToUnity(pr.x + 14f, Map.HeightAt(pr.x + 14f, pr.z) + 0.5f, pr.z); break; }
            }
            Player = PlayerController.Spawn(pos, controls, cam);
            if (url.Contains("sniper=1")) Player.fighter.guns[0] = new GunState("sniper"); // test switch: sniper in the long-range slot
            if (url.Contains("god=1")) Player.fighter.God = true;
            Fighters.Add(Player.fighter);

            // bots on other spawn points, spread over the map
            int n = Map.spawns != null ? Map.spawns.Length : 0;
            for (int b = 0; b < BotCount && n > 1; b++)
            {
                Vec2 s = Map.spawns[1 + (b * (n - 1) / BotCount) % (n - 1)];
                Vector3 p = Coords.ToUnity(s.x, Map.HeightAt(s.x, s.z) + 0.2f, s.z);
                Fighters.Add(BotBrain.Spawn(p, BotNames[b % BotNames.Length], 1000 + b * 7919 + Random.Range(0, 99999)).fighter);
            }
            AliveCount = Fighters.Count;
            Fighter.OnKill += (victim, killer, w, head) =>
            {
                AliveCount = 0; foreach (Fighter f in Fighters) if (f.Alive) AliveCount++;
                if (Result != 0) return;
                if (victim == Player.fighter) { Result = -1; Rank = AliveCount + 1; }
                else if (AliveCount <= 1 && Player.fighter.Alive) { Result = 1; Rank = 1; }
                if (Result != 0) Profile.AddMatch(Player.fighter.kills, Result > 0, Rank);
            };

            // everybody boards the drop plane (?nodrop=1 in the URL: start on the ground, for tests)
            if (!(Application.absoluteURL ?? "").Contains("nodrop=1"))
            {
                SkyDrop.Create(Map);
                foreach (Fighter f in Fighters) f.air = 1;
            }
            Zone.Create(Map.playRadius);
            Tracers.Create();
            Hud.Create(controls, Player);
            Minimap.Create(controls, Player, Map);

            fps = new GameObject("fps", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            fps.transform.SetParent(FindFirstObjectByType<Canvas>().transform, false);
            var rt = (RectTransform)fps.transform; rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1); rt.anchoredPosition = new Vector2(16, -10); rt.sizeDelta = new Vector2(500, 40);
            fps.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); fps.fontSize = 20; fps.color = new Color(1, 1, 1, .6f);
            Loader.HideSoon();
        }

        /// <summary>end of a match: back to the lobby behind a loading card</summary>
        public void Restart()
        {
            Fighter.ClearEvents();
            InMatch = false;
            Loader.Show("Back to the lobby…");
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        Transform sky;
        /// <summary>sky dome that follows the camera: blue overhead, the fog colour at and below the horizon</summary>
        void BuildSky(Camera cam)
        {
            Shader sh = Shader.Find("Sprites/Default");
            if (sh == null) return;
            const int seg = 24, rings = 12; const float R = 1500f;
            var verts = new List<Vector3>(); var cols = new List<Color>(); var tris = new List<int>();
            Color top = new Color(0.36f, 0.58f, 0.9f), mid = new Color(0.6f, 0.77f, 0.94f), haze = RenderSettings.fogColor;
            for (int r = 0; r <= rings; r++)
            {
                float el = Mathf.Lerp(-0.5f, 1f, r / (float)rings) * Mathf.PI / 2f; // from 45 deg below the horizon to straight up
                float t = Mathf.Clamp01(Mathf.Sin(el));
                Color c = el <= 0.03f ? haze : t < 0.3f ? Color.Lerp(haze, mid, t / 0.3f) : Color.Lerp(mid, top, (t - 0.3f) / 0.7f);
                for (int i = 0; i <= seg; i++)
                {
                    float a = i / (float)seg * Mathf.PI * 2f;
                    verts.Add(new Vector3(Mathf.Cos(a) * Mathf.Cos(el), Mathf.Sin(el), Mathf.Sin(a) * Mathf.Cos(el)) * R); cols.Add(c);
                }
            }
            for (int r = 0; r < rings; r++)
                for (int i = 0; i < seg; i++)
                {
                    int a0 = r * (seg + 1) + i, a1 = a0 + 1, b0 = a0 + seg + 1, b1 = b0 + 1;
                    tris.AddRange(new[] { a0, a1, b0, a1, b1, b0, a0, b0, a1, a1, b0, b1 });
                }
            var mesh = new Mesh { name = "sky" }; mesh.SetVertices(verts); mesh.SetColors(cols); mesh.SetTriangles(tris, 0);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
            var go = new GameObject("Sky", typeof(MeshFilter), typeof(MeshRenderer));
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var mat = new Material(sh); mat.renderQueue = 1000; // behind everything, drawn first
            var mr = go.GetComponent<MeshRenderer>(); mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            sky = go.transform;
        }

        void LateUpdate()
        {
            Camera cam = Camera.main;
            if (cam == null || Map == null) return;
            if (sky != null) sky.position = cam.transform.position;
            // the higher the camera, the further the haze starts (web: near 110 + 1.4 up, far 340 + 2.6 up)
            Vector3 p = cam.transform.position;
            float up = Mathf.Max(0f, p.y - Map.HeightAt(-p.x, p.z));
            RenderSettings.fogStartDistance = 110f + up * 1.4f; RenderSettings.fogEndDistance = 340f + up * 2.6f;
        }

        void Update()
        {
            fpsAcc += Time.unscaledDeltaTime; fpsFrames++;
            // ?rig=1: list what is drawn close to the camera / unusually big (hunting stray giant bodies in the plane)
            if (SoldierBody.LogRig && Camera.main != null && (Time.frameCount % 300) == 0)
            {
                Vector3 c = Camera.main.transform.position;
                foreach (Renderer r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                {
                    if (!r.enabled || !r.gameObject.activeInHierarchy || r is ParticleSystemRenderer) continue;
                    Bounds b = r.bounds; float d = Vector3.Distance(b.ClosestPoint(c), c);
                    if (d < 12f || (b.size.magnitude > 30f && b.center.y > 60f))
                    {
                        string path = r.name; Transform t = r.transform.parent; int n = 0; while (t != null && n++ < 6) { path = t.name + "/" + path; t = t.parent; }
                        Debug.Log($"[LastWar] near {path} d={d:0.0} size={b.size} at={b.center} cam={c} scale={r.transform.lossyScale}");
                    }
                }
            }
            if (fpsAcc >= 1f && fps != null && Map != null) { fps.text = $"LAST WAR · {Map.name} · {fpsFrames / fpsAcc:0} fps"; fpsAcc = 0; fpsFrames = 0; }
        }
    }
}
