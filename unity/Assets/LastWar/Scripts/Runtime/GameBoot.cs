using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LastWar
{
    /// <summary>Entry point of the Main scene: sun, sky, camera, map, player, controls.</summary>
    public class GameBoot : MonoBehaviour
    {
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
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.62f, 0.78f, 0.93f);
            cam.fieldOfView = 70f; cam.nearClipPlane = 0.1f; cam.farClipPlane = 500f;
            cam.gameObject.AddComponent<AudioListener>();

            string mapId = PlayerPrefs.GetString("lw.map", "green");
            Map = MapData.Load(mapId);
            MapBuilder.Build(Map);

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
            if (url.Contains("god=1")) Player.fighter.God = true;
            Fighters.Add(Player.fighter);

            // bots on other spawn points, spread over the map
            int n = Map.spawns != null ? Map.spawns.Length : 0;
            for (int b = 0; b < BotCount && n > 1; b++)
            {
                Vec2 s = Map.spawns[1 + (b * (n - 1) / BotCount) % (n - 1)];
                Vector3 p = Coords.ToUnity(s.x, Map.HeightAt(s.x, s.z) + 0.2f, s.z);
                Fighters.Add(BotBrain.Spawn(p, BotNames[b % BotNames.Length], 1000 + b * 7919).fighter);
            }
            AliveCount = Fighters.Count;
            Fighter.OnKill += (victim, killer, w, head) =>
            {
                AliveCount = 0; foreach (Fighter f in Fighters) if (f.Alive) AliveCount++;
                if (Result != 0) return;
                if (victim == Player.fighter) { Result = -1; Rank = AliveCount + 1; }
                else if (AliveCount <= 1 && Player.fighter.Alive) { Result = 1; Rank = 1; }
            };

            Zone.Create(Map.playRadius);
            Tracers.Create();
            Hud.Create(controls, Player);
            Minimap.Create(controls, Player, Map);

            fps = new GameObject("fps", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            fps.transform.SetParent(FindFirstObjectByType<Canvas>().transform, false);
            var rt = (RectTransform)fps.transform; rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1); rt.anchoredPosition = new Vector2(16, -10); rt.sizeDelta = new Vector2(500, 40);
            fps.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); fps.fontSize = 20; fps.color = new Color(1, 1, 1, .6f);
        }

        public void Restart()
        {
            Fighter.ClearEvents();
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        void Update()
        {
            fpsAcc += Time.unscaledDeltaTime; fpsFrames++;
            if (fpsAcc >= 1f && fps != null) { fps.text = $"LAST WAR · {Map.name} · {fpsFrames / fpsAcc:0} fps"; fpsAcc = 0; fpsFrames = 0; }
        }
    }
}
