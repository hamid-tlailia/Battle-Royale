using UnityEngine;
using UnityEngine.UI;

namespace LastWar
{
    /// <summary>Entry point of the Main scene: sun, sky, camera, map, player, controls.</summary>
    public class GameBoot : MonoBehaviour
    {
        public static GameBoot Instance { get; private set; }
        public MapData Map { get; private set; }
        public PlayerController Player { get; private set; }
        Text fps;
        float fpsAcc; int fpsFrames;

        void Start()
        {
            Instance = this;
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
            Player = PlayerController.Spawn(pos, controls, cam);

            fps = new GameObject("fps", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            fps.transform.SetParent(FindFirstObjectByType<Canvas>().transform, false);
            var rt = (RectTransform)fps.transform; rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1); rt.anchoredPosition = new Vector2(20, -20); rt.sizeDelta = new Vector2(500, 60);
            fps.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); fps.fontSize = 28; fps.color = Color.white;
        }

        void Update()
        {
            fpsAcc += Time.unscaledDeltaTime; fpsFrames++;
            if (fpsAcc >= 1f && fps != null) { fps.text = $"LAST WAR · {Map.name} · {fpsFrames / fpsAcc:0} fps"; fpsAcc = 0; fpsFrames = 0; }
        }
    }
}
