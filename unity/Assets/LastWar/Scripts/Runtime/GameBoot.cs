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
        /// <summary>the next match is the benchmark (set from the settings page)</summary>
        public static bool BenchNext;
        public static GameBoot Instance { get; private set; }
        public MapData Map { get; private set; }
        public PlayerController Player { get; private set; }
        public readonly List<Fighter> Fighters = new List<Fighter>();
        public int AliveCount { get; private set; }
        public int Result { get; private set; }   // 0 playing, 1 victory, -1 eliminated
        public int Rank { get; private set; }
        float startTime;
        public float MatchTime => Time.time - startTime;
        public const int BotCount = 30;
        static readonly string[] BotNames = { "Viper", "Nomad", "Ghost", "Raven", "Falcon", "Cobra", "Wolf", "Shadow", "Hunter", "Blaze", "Storm", "Rogue", "Jackal", "Titan", "Spectre", "Onyx", "Ranger", "Sable", "Mako", "Drift", "Havoc", "Reaper", "Bishop", "Kestrel", "Bandit", "Atlas", "Fury", "Lynx", "Talon", "Saber" };
        Text fps;
        float fpsAcc; int fpsFrames;

        /// <summary>first launch of the app: the loading card covers the start until the lobby is drawn</summary>
        static bool launched;

        System.Collections.IEnumerator Start()
        {
            Instance = this; startTime = Time.time;
            if (!launched) { launched = true; Loader.Show("Loading…"); yield return null; yield return null; }
            Loader.Progress(0.35f);
            Application.targetFrameRate = Profile.Fps; // (60 / 90 / 120: settings)
            QualitySettings.vSyncCount = 0;
            Profile.ApplyQuality();
            SoldierBody.LogRig = (Application.absoluteURL ?? "").Contains("rig=1");
            PlayerController.DebugAim = (Application.absoluteURL ?? "").Contains("dbgaim=1");
            SoldierBody.GunDebug = (Application.absoluteURL ?? "").Contains("gundbg=1");
            if ((Application.absoluteURL ?? "").Contains("trigfinger=0")) SoldierBody.UseTriggerFinger = false; // (the index round the grip, as before)
            { string u = Application.absoluteURL ?? ""; int ti = u.IndexOf("trigahead="); if (ti >= 0 && float.TryParse(new string(u.Substring(ti + 10).TakeWhile(ch => char.IsDigit(ch) || ch == '.' || ch == '-').ToArray()), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float ta)) SoldierBody.TriggerAhead = ta;
              ti = u.IndexOf("trigdown="); if (ti >= 0 && float.TryParse(new string(u.Substring(ti + 9).TakeWhile(ch => char.IsDigit(ch) || ch == '.' || ch == '-').ToArray()), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float td)) SoldierBody.TriggerDown = td; }
            if ((Application.absoluteURL ?? "").Contains("wik=0")) SoldierBody.UseWeaponIK = false;
            if ((Application.absoluteURL ?? "").Contains("wikA=0")) SoldierBody.UseShoulderReach = false;
            if ((Application.absoluteURL ?? "").Contains("wikB=0")) SoldierBody.UseHoldBack = false;
            if ((Application.absoluteURL ?? "").Contains("grip=palm")) SoldierBody.UseGripContact = false;
            if ((Application.absoluteURL ?? "").Contains("footdbg=1")) SoldierBody.FootDebug = true;
            if ((Application.absoluteURL ?? "").Contains("heel=0")) SoldierBody.UseHeelLevel = false;
            if ((Application.absoluteURL ?? "").Contains("soledrop=0")) SoldierBody.UseSoleDrop = false;
            if ((Application.absoluteURL ?? "").Contains("runfwd=0")) SoldierBody.UseRunForward = false;
            if ((Application.absoluteURL ?? "").Contains("chestbone=1")) SoldierBody.UseChestFrame = false;
            if ((Application.absoluteURL ?? "").Contains("jog=0")) SoldierBody.UseJog = false;
            if ((Application.absoluteURL ?? "").Contains("hrun=0")) SoldierBody.UseHumanRun = false;
            if ((Application.absoluteURL ?? "").Contains("pronegun=0")) SoldierBody.UseProneGunClear = false;
            if ((Application.absoluteURL ?? "").Contains("joyturn=1")) PlayerController.JoyTurn = true;
            if ((Application.absoluteURL ?? "").Contains("lobbygun=")) { string u = Application.absoluteURL; int gi = u.IndexOf("lobbygun=") + 9; int ge = u.IndexOf('&', gi); Armory.LobbyGun = ge < 0 ? u.Substring(gi) : u.Substring(gi, ge - gi); }
            if ((Application.absoluteURL ?? "").Contains("island=0")) MapBuilder.UseIsland = false;
            if ((Application.absoluteURL ?? "").Contains("fcrawl=0")) SoldierBody.UseFreeCrawl = false;
            if ((Application.absoluteURL ?? "").Contains("rcrawl=0")) SoldierBody.UseEvenRifleCrawl = false;
            if ((Application.absoluteURL ?? "").Contains("pclips=0")) SoldierBody.UsePistolClips = false;
            if ((Application.absoluteURL ?? "").Contains("slope=0")) SoldierBody.UseSlopeLie = false;
            { string u = Application.absoluteURL ?? ""; int pw = u.IndexOf("pwrist="); if (pw >= 0 && float.TryParse(u.Substring(pw + 7).Split('&')[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float pwv)) SoldierBody.PistolWrist = pwv; }
            { string u = Application.absoluteURL ?? ""; int di = u.IndexOf("animdelay="); if (di >= 0) { int dn = 0; for (int k = di + 10; k < u.Length && char.IsDigit(u[k]); k++) dn = dn * 10 + (u[k] - '0'); SoldierBody.AnimDelayFrames = dn; } }
            { string u = Application.absoluteURL ?? ""; int pi = u.IndexOf("posethresh="); if (pi >= 0 && float.TryParse(u.Substring(pi + 11).Split('&')[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float pt)) SoldierBody.PoseThreshold = pt; }
            { string u = Application.absoluteURL ?? ""; int wi = u.IndexOf("wikframes="); if (wi >= 0) { int wn = 0; for (int k = wi + 10; k < u.Length && char.IsDigit(u[k]); k++) wn = wn * 10 + (u[k] - '0'); SoldierBody.WikFrames = wn; } }
            SoldierBody.UseOldBase = (Application.absoluteURL ?? "").Contains("base=old"); // (before any soldier is built: the clips are loaded once)
            // test switch: ?parts=h,t,l,f (outfit per piece: head, torso, legs, feet)
            { string u = Application.absoluteURL ?? ""; int pi = u.IndexOf("parts="); if (pi >= 0) { string[] ps = u.Substring(pi + 6).Split('&')[0].Split(','); for (int i = 0; i < 4 && i < ps.Length; i++) if (int.TryParse(ps[i], out int pv)) Profile.SetPart(i, pv); } }
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

            // test switch: ?play=1 in the WebGL URL goes straight into a match
            if (!InMatch && (Application.absoluteURL ?? "").Contains("play=1")) InMatch = true;
            // the lobby always stands on the Highlands; the match is played on the chosen map
            Map = MapData.Load(InMatch ? Profile.Map : "green");
            Loader.Progress(0.45f); yield return null;
            MapBuilder.Build(Map);
            Loader.Progress(0.75f); yield return null;
            if (!InMatch) { Lobby.Create(Map, cam); Loader.Progress(1f); Loader.HideSoon(); Music.PlayLobby(); yield break; }
            Loader.Progress(0.85f); yield return null;
            Loot.SpawnMap(Map, Map.id.GetHashCode());

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
            bool bench = BenchNext || url.Contains("bench=1");
            Bench.Active = bench; // (before any soldier is built: their animation graphs are then evaluated by the bench, timed)
            Player = PlayerController.Spawn(pos, controls, cam);
            if (url.Contains("sniper=1")) Player.fighter.guns[0] = new GunState("sniper"); // test switch: sniper in the long-range slot
            if (url.Contains("god=1")) Player.fighter.God = true;
            // the player hits harder as the profile level grows (+3 % a level, up to +60 %); bots hit softer
            Player.fighter.DamageMult = 1f + Mathf.Min(0.6f, 0.03f * (Profile.Level - 1));
            Fighters.Add(Player.fighter);

            // bots on other spawn points, spread over the map
            int n = Map.spawns != null ? Map.spawns.Length : 0;
            for (int b = 0; b < BotCount && n > 1; b++)
            {
                Vec2 s = Map.spawns[1 + (b * (n - 1) / BotCount) % (n - 1)];
                Vector3 p = Coords.ToUnity(s.x, Map.HeightAt(s.x, s.z) + 0.2f, s.z);
                { var bf = BotBrain.Spawn(p, BotNames[b % BotNames.Length], 1000 + b * 7919 + Random.Range(0, 99999)).fighter; bf.DamageMult = 0.5f; Fighters.Add(bf); }
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

            if (url.Contains("icetest=1")) { PlayerController.AimAssistOn = false; StartCoroutine(IceWall.Test(Player.transform)); } // (test: an ice wall in front of you, weakened in steps; the view held on it)
            // benchmark (Settings > Benchmark, or ?bench=1): on the ground, you cannot be hurt, bots measured 10 / 20 / 30
            if (bench)
            {
                BenchNext = false; Player.fighter.God = true;
                var bots = new List<Fighter>(); foreach (Fighter f in Fighters) if (f != Player.fighter) bots.Add(f);
                Bench.Begin(bots);
            }
            // everybody boards the drop plane (?nodrop=1 in the URL: start on the ground, for tests)
            if (!bench && !(Application.absoluteURL ?? "").Contains("nodrop=1"))
            {
                SkyDrop.Create(Map);
                Music.Stop(); // the lobby tune ends as the plane appears
                foreach (Fighter f in Fighters) f.air = 1;
            }
            if ((Application.absoluteURL ?? "").Contains("nodrop=1")) Music.Stop();
            Zone.Create(Map.playRadius);
            Tracers.Create();
            Hud.Create(controls, Player);
            Minimap.Create(controls, Player, Map);

            fps = new GameObject("fps", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            fps.transform.SetParent(controls.Root, false);
            var rt = (RectTransform)fps.transform; rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1); rt.anchoredPosition = new Vector2(16, -10); rt.sizeDelta = new Vector2(330, 30);
            fps.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); fps.fontSize = 20; fps.color = new Color(1, 1, 1, .6f);
            fps.raycastTarget = false; fps.horizontalOverflow = HorizontalWrapMode.Overflow;
            controls.AddEditable("fps", rt); // frame rate + ping: movable like the buttons
            Loader.HideSoon();
        }

        /// <summary>end of a match: back to the lobby behind a loading card</summary>
        public void Restart()
        {
            Fighter.ClearEvents();
            InMatch = false;
            if (Profile.MapAuto) { string[] rot = { "green", "forest", "classic", "valley" }; int i = System.Array.IndexOf(rot, Profile.Map); Profile.Map = rot[(i + 1) % rot.Length]; }
            Loader.Go("Back to the lobby…");
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

        /// <summary>round trip to the game server; the offline match runs on this phone, so it is 0</summary>
        public static int PingMs => 0;

        void Update()
        {
            // the phone's back button (Escape): close what is open, else ask before leaving the match / the game
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (Confirm.IsOpen) Confirm.Close();
                else if (Player != null)
                {
                    if (BagPanel.IsOpen || SettingsPanel.IsOpen) { BagPanel.CloseAll(); SettingsPanel.CloseAll(); }
                    else if (Minimap.BigOpen) Minimap.CloseBig();
                    else Confirm.Show("LEAVE MATCH?", "You will be taken back to the lobby.", "LEAVE", Restart);
                }
                else if (Lobby.Current != null && !Lobby.Current.Back())
                    Confirm.Show("QUIT LAST WAR?", "Close the game?", "QUIT", Application.Quit);
            }
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
            if (fpsAcc >= 1f && fps != null && Map != null) { fps.text = $"{fpsFrames / fpsAcc:0} fps · offline (vs bots)"; fpsAcc = 0; fpsFrames = 0; }
        }
    }
}
