using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LastWar
{
    [Serializable] public class LobbyNode { public string id, kind, text, color, weight, align; public float x, y, w, h, size; }
    [Serializable] public class LobbyRect { public float x, y, w, h; }
    [Serializable] public class LobbyLayout { public float W, H; public LobbyNode[] nodes; public LobbyRect sheet, card; }

    /// <summary>
    /// The lobby, rebuilt from the web lobby (Resources/UI/Lobby: pictures + lobby_layout.json captured from the
    /// web page): soldier on a pedestal in the map, profile banner, LOBBY / OUTFIT / MAP / FRIENDS tabs, wins and
    /// kills, settings, map card, SOLO / FRIENDS / ONLINE, PLAY, season card and tips; sheets for outfit, map,
    /// friends and settings.
    /// </summary>
    public class Lobby : MonoBehaviour
    {
        LobbyLayout L; float s; Canvas canvas; RectTransform root;
        readonly Dictionary<string, RectTransform> nodes = new Dictionary<string, RectTransform>();
        readonly Dictionary<string, Text> texts = new Dictionary<string, Text>();
        GameObject soldier; float soldierYaw; SoldierBody lobbyBody;
        Transform stage; Camera cam;
        Vector3 stageAt, camHome; Quaternion camHomeRot; bool wardrobe;
        string forcedPose;
        RectTransform shade, sheet; int dragFinger = -1; Vector2 dragLast; bool mouseDrag;
        static readonly string[] Tips = { "Welcome to Last War!", "Land, loot a gun, stay in the zone.", "Tap a weapon to equip it, tap it again to reload.", "Headshots hit twice as hard.", "The shotgun fires two blasts per press." };

        public static Lobby Current;
        static float LobbyGunTurn = 55f, LobbyGunUp = 38f; static Vector3 LobbyGrip = new Vector3(0.04f, -0.45f, 0.1f);
        /// <summary>back button: closes the open page; false when there was none</summary>
        public bool Back() { if (matching != null) { CancelMatch(); return true; } if (sheet == null) return false; Close(); return true; }

        public static Lobby Create(MapData map, Camera cam)
        {
            var l = new GameObject("Lobby").AddComponent<Lobby>(); Current = l;
            l.cam = cam; l.Build3D(map); l.BuildUi();
            if (l.lcamTest && l.canvas != null) l.canvas.gameObject.AddComponent<CanvasGroup>().alpha = 0f; // (?lcam=: buttons unseen but working - the lobby's own Update still poses the rifle)
            l.StartCoroutine(MapBuilder.Preload(System.Array.ConvertAll(SoldierBody.OutfitFiles, f => "outfit_" + f)));
            // test switches: ?open=settings|outfit|map|edit opens that panel; ?lpose=kneel|crawl|runfree shows that pose
            string url = Application.absoluteURL ?? "";
            string Arg(string key) { int i = url.IndexOf(key + "="); if (i < 0) return null; string v = url.Substring(i + key.Length + 1); int e = v.IndexOf('&'); return e >= 0 ? v.Substring(0, e) : v; }
            string open = Arg("open"), lp = Arg("lpose");
            if (float.TryParse(Arg("lyaw") ?? "x", out float ly)) l.soldierYaw = ly;
            if (float.TryParse(Arg("lgun") ?? "x", out float lg)) LobbyGunTurn = lg;
            if (float.TryParse(Arg("lgunup") ?? "x", out float lu)) LobbyGunUp = lu;
            if (float.TryParse(Arg("lgripy") ?? "x", System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float gy)) LobbyGrip.y = gy;
            // outfit validation bench (?grid= / ?swap=1)
            if (Arg("qfingers") == "1") SoldierBody.QFingers = true;
            if (Arg("rigcheck") == "1") { LobbyTests.RigCheck(); }
            if (Arg("gunaudit") == "1") { GunFrame.Audit(); }
            if (Arg("gripview") == "1") { GunFrame.GripView(cam); if (l.canvas != null) l.canvas.enabled = false; return l; }
            if (Arg("retcheck") == "1") { int.TryParse(Arg("rcfrom") ?? "0", out int rf); int.TryParse(Arg("rcn") ?? "99", out int rn); LobbyTests.RetCheck(rf, rn); if (l.canvas != null) l.canvas.enabled = false; return l; }
            if (Arg("trans") == "1") { LobbyTests.TransCheck(cam); if (l.canvas != null) l.canvas.enabled = false; return l; }
            LobbyTests.SetClips(Arg("clips"));
            LobbyTests.SetHolds(Arg("cholds"));
            float.TryParse(Arg("gpitch") ?? "x", System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out LobbyTests.GridPitch);
            float.TryParse(Arg("gdx") ?? "x", System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out LobbyTests.GridDx);
            LobbyTests.GridGround = Arg("gground") == "1";
            LobbyTests.GridBack = Arg("wback") == "1";
            float.TryParse(Arg("wspeed") ?? "x", System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out LobbyTests.GridSpeed);
            float.TryParse(Arg("wramp") ?? "x", System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out LobbyTests.GridRamp);
            if (float.TryParse(Arg("gyaw") ?? "x", out float gyaw)) LobbyTests.GridYaw = gyaw;
            if (Arg("ggun") != null) LobbyTests.GridGun = Arg("ggun");
            if (Arg("wik") == "0") SoldierBody.UseWeaponIK = false;
            int.TryParse(Arg("wbag") ?? "0", out LobbyTests.GridBag);
            if (!string.IsNullOrEmpty(Arg("wonly"))) LobbyTests.GridOnly = System.Array.ConvertAll(Arg("wonly").Split(','), int.Parse);
            if (float.TryParse(Arg("wzoom") ?? "x", System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float wz)) LobbyTests.GridZoom = wz;
            if (LobbyTests.WGrid(cam, Arg("wgrid"), Arg("wclip"))) { if (l.canvas != null) l.canvas.enabled = false; return l; }
            if (Arg("xpos") == "0") SoldierBody.ExtraPos = false;
            if (Arg("hgrip") == "0") SoldierBody.HandGrip = false;
            if (float.TryParse(Arg("jumpat") ?? "", System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float ja)) LobbyTests.Holds[2] = ja;
            int.TryParse(Arg("from") ?? "0", out int tFrom); int.TryParse(Arg("n") ?? "4", out int tN);
            if (LobbyTests.Run(cam, l.canvas, Arg("grid"), tFrom, tN, Arg("zoom") == "1", Arg("swap") == "1")) return l;
            if (Arg("icetest") == "1" && l.soldier != null) l.StartCoroutine(IceWall.Test(l.soldier.transform)); // (cracks, stage by stage)
            if (open == "edit") l.CustomizeButtons(); else if (open != null) l.Open(open);
            if (lp != null && l.lobbyBody != null)
            {
                l.lobbyBody.Play(lp == "kneel" ? "crouch_idle" : lp == "crawl" ? "prone" : lp == "runfree" ? "run" : lp);
                l.forcedPose = lp; if (lp == "runfree") l.lobbyBody.SetGun(null);
            }
            return l;
        }

        // ------------------------------------------------------------------ 3D: pedestal + soldier in the map
        void Build3D(MapData map)
        {
            Vec2 sp = map.spawns != null && map.spawns.Length > 0 ? map.spawns[0] : new Vec2();
            Vector3 p = Coords.ToUnity(sp.x, map.HeightAt(sp.x, sp.z), sp.z);
            Vector3 dir = new Vector3(p.x, 0, p.z); dir = dir.sqrMagnitude > 1 ? dir.normalized : Vector3.back; // camera outside, looking toward the middle of the map
            stage = new GameObject("Stage").transform; stage.position = p + Vector3.up * 0.05f;
            stage.rotation = Quaternion.LookRotation(dir);
            Mesh cyl = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
            void Disc(float d, float h, Color c)
            {
                var g = new GameObject("disc", typeof(MeshFilter), typeof(MeshRenderer));
                g.transform.SetParent(stage, false); g.transform.localScale = new Vector3(d, h / 2f, d); g.transform.localPosition = Vector3.up * h / 2f;
                g.GetComponent<MeshFilter>().sharedMesh = cyl; g.GetComponent<MeshRenderer>().sharedMaterial = MapBuilder.NewLit(c);
            }
            Disc(2.5f, 0.16f, new Color(0.35f, 0.75f, 1f));    // cyan rim
            Disc(2.36f, 0.18f, new Color(0.12f, 0.14f, 0.16f));
            Disc(1.7f, 0.19f, new Color(1f, 0.8f, 0.25f));     // gold ring
            Disc(1.56f, 0.2f, new Color(0.12f, 0.14f, 0.16f));
            SpawnSoldier();
            Vector3 camPos = p + dir * 6.4f + Vector3.up * 1.25f;
            Vector2 cw = new Vector2(-camPos.x, camPos.z);
            camPos.y = Mathf.Max(camPos.y, map.HeightAt(cw.x, cw.y) + 0.8f);
            cam.transform.position = camPos; cam.transform.LookAt(p + Vector3.up * 1.18f);
            cam.fieldOfView = 30f;
            stageAt = p; camHome = cam.transform.position; camHomeRot = cam.transform.rotation;
            // (tests: ?lcam=<fov> - the lobby camera narrowed onto the hands and the gun, the buttons hidden)
            string lu = Application.absoluteURL ?? ""; int lci = lu.IndexOf("lcam="); string lcv = lci < 0 ? "x" : lu.Substring(lci + 5).Split('&')[0];
            if (float.TryParse(lcv, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float lf) && lf > 0f)
            { cam.fieldOfView = lf; cam.transform.LookAt(p + Vector3.up * 1.05f); lcamTest = true; }
        }
        bool lcamTest;
        float lobbyAimAt = -1f, lobbyAimEnd = -1f;

        void SpawnSoldier()
        {
            if (soldier != null) Destroy(soldier);
            soldier = new GameObject("LobbySoldier");
            soldier.transform.SetParent(stage, false); soldier.transform.localPosition = Vector3.up * 0.2f;
            soldier.transform.localRotation = Quaternion.Euler(0, soldierYaw, 0);
            lobbyBody = SoldierBody.Attach(soldier, Profile.Skin, Profile.Parts);
            StartCoroutine(ShowWhenPlanted(lobbyBody)); // (unseen until its feet are measured on the stage: it showed high for a moment, then dropped)
            lobbyBody.Plant = true;
            lobbyBody.GripOffset = LobbyGrip; // (the armory version you carry)
            LobbyGunPose(Armory.LobbyGun);
            // test switch: ?pose=prone (or death, run, crouch_idle...) in the WebGL URL
            string url = Application.absoluteURL ?? ""; int pq = url.IndexOf("pose=");
            if (pq >= 0) { string clip = url.Substring(pq + 5); int amp = clip.IndexOf('&'); if (amp >= 0) clip = clip.Substring(0, amp); lobbyBody.Play(clip); }
        }

        /// <summary>the gun shown in the lobby and the stance for it: a long gun - the rifle stand, down and now and then up at
        /// a target; a pistol - Mixamo's pistol stand, held up in both hands as in a match (the rifle stand with a pistol
        /// in it crossed the arms in front of the face)</summary>
        void LobbyGunPose(string key)
        {
            if (lobbyBody == null) return;
            bool pistol = Armory.GunOf(key) == "pistol";
            lobbyBody.SetGun(key); lobbyBody.Armed = true; lobbyBody.ShoulderGrip = pistol;
            lobbyBody.StandTall = !pistol; // (straight knees and back while standing)
            // (the lobby's grip offset places a rifle held down across the body: with a pistol it pulled the hands 45 cm
            // down, the pistol held low against the chest)
            lobbyBody.GripOffset = Vector3.zero; // (the rifle held as in a match - the lobby's own low carry left the left hand bent off the handguard)
            lobbyBody.LiveBack = true;
            if (pistol) { lobbyBody.AimDir = soldier.transform.forward; lobbyBody.Play("idle"); }
            else { lobbyBody.AimDir = soldier.transform.forward; lobbyBody.Play("idle_rifle"); lobbyAimAt = Time.time + 4f; } // standing as in a match: the rifle across the body, both hands on it; now and then up to the shoulder (Update)
        }

        // ------------------------------------------------------------------ UI from the captured web layout
        void BuildUi()
        {
            canvas = new GameObject("LobbyUI").AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            UiKit.Scale(canvas.gameObject.AddComponent<CanvasScaler>());
            root = UiKit.SafeRoot(canvas);
            canvas.gameObject.AddComponent<GraphicRaycaster>();
            if (FindFirstObjectByType<EventSystem>() == null) { var es = new GameObject("EventSystem"); es.AddComponent<EventSystem>(); es.AddComponent<StandaloneInputModule>(); }

            L = JsonUtility.FromJson<LobbyLayout>(Resources.Load<TextAsset>("UI/Lobby/lobby_layout").text);
            // the captured web layout, scaled to the screen's height - or to its width when the screen is narrower
            // a fifth tab under FRIENDS: the ARMORY (a copy of the FRIENDS tab, one row lower)
            {
                var extra = new List<LobbyNode>(L.nodes);
                foreach (LobbyNode n in L.nodes)
                    if (n.id == "tab3" || n.id == "tab3_icon" || n.id == "tab3_text")
                        extra.Add(new LobbyNode { id = n.id.Replace("tab3", "tab4"), kind = n.kind, text = n.id == "tab3_text" ? "ARMORY" : n.text, color = n.color, weight = n.weight, align = n.align, x = n.x, y = n.y + 38, w = n.id == "tab3_text" ? 70 : n.w, h = n.h, size = n.size });
                L.nodes = extra.ToArray();
            }
            foreach (LobbyNode n in L.nodes)
            {
                // the gear is as big as the trophy / kills chips beside it
                if (n.id == "gear") { n.x = 805; n.y = 16; n.w = 34; n.h = 34; }
                if (n.id == "gear_icon") { n.x = 812; n.y = 23; n.w = 20; n.h = 20; }
                // season: the xp numbers on the "Level" line (right), the bar alone below across the card
                if (n.id == "season_xp") { n.y = 300; n.h = 14; }
                if (n.id == "season_bar") n.w = 218 - n.x;
            }
            Canvas.ForceUpdateCanvases();
            Rect safe = root.rect;
            if (safe.height < 10f) safe = new Rect(0, 0, 1080f * Screen.width / Mathf.Max(1f, Screen.height) * 0.95f, 1080f);
            s = Mathf.Min(safe.height / L.H, safe.width / L.W);
            var sprites = new Dictionary<string, string> {
                { "logo", "Lobby/logo" }, { "banner", "Lobby/banner" }, { "chip0", "Lobby/chip" }, { "chip1", "Lobby/chip" }, { "gear", "Lobby/gear" },
                { "tab0", "Lobby/tab_on" }, { "tab1", "Lobby/tab" }, { "tab2", "Lobby/tab" }, { "tab3", "Lobby/tab" }, { "tab4", "Lobby/tab" }, { "tab4_icon", "icon_armory" },
                { "mapcard", "Lobby/mapcard_" + (Profile.Map == "forest" || Profile.Map == "classic" || Profile.Map == "valley" ? Profile.Map : "green") }, { "season", "Lobby/season" }, { "tip", "Lobby/tip" },
                { "modes", "Lobby/modes" }, { "play", "Lobby/play" },
                { "rank", "icon_rank" }, { "chip0_icon", "icon_trophy" }, { "chip1_icon", "icon_fire" }, { "gear_icon", "icon_settings" },
                { "tab0_icon", "icon_home" }, { "tab1_icon", "icon_outfit" }, { "tab2_icon", "icon_map" }, { "tab3_icon", "icon_friends" },
                { "mapcard_pin", "icon_pin" }, { "season_icon", "icon_rank" }, { "tip_icon", "icon_chat" },
            };
            var (have, need) = Profile.LevelProgress;
            var dynamic = new Dictionary<string, string> {
                { "name", Profile.Name }, { "level", "LV " + Profile.Level }, { "server", "offline - play vs bots" },
                { "chip0_text", Profile.Wins.ToString() }, { "chip1_text", Profile.Kills.ToString() },
                { "mapcard_name", MapName(Profile.Map) }, { "mapcard_sub", Profile.MapAuto ? "AUTO ROTATE · Select map >" : "Select map >" },
                { "season_lv", $"Level {Profile.Level} · {Profile.Matches} matches" }, { "season_xp", $"{have} / {need}" },
                { "tip_text", Tips[UnityEngine.Random.Range(0, Tips.Length)] },
            };
            foreach (LobbyNode n in L.nodes)
            {
                if ((n.id ?? "").StartsWith("tip")) continue; // no tip strip under the soldier
                if (n.kind == "text")
                {
                    var t = UiKit.Txt(root, Vector2.zero, Vector2.zero, Vector2.one, 1, TextAnchor.MiddleLeft, ParseColor(n.color));
                    Place(t.rectTransform, n);
                    t.fontSize = Mathf.RoundToInt(n.size * s);
                    t.fontStyle = int.TryParse(n.weight, out int wt) && wt >= 600 ? FontStyle.Bold : FontStyle.Normal;
                    bool centred = n.id.StartsWith("mode") || n.id.StartsWith("play") || n.align == "center";
                    t.alignment = centred ? TextAnchor.MiddleCenter : n.align == "right" || n.align == "end" ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
                    t.text = (dynamic.TryGetValue(n.id, out string dv) ? dv : n.text).Replace("›", ">").Replace("—", "-").Replace("●", "");
                    if (n.id == "mode2") t.color = new Color(1, 1, 1, .45f); // ONLINE: not in the app yet
                    if (n.id == "play_text") { t.alignment = TextAnchor.LowerCenter; Destroy(t.GetComponent<Shadow>()); }
                    if (n.id == "play_sub") Destroy(t.GetComponent<Shadow>());
                    texts[n.id] = t; nodes[n.id] = t.rectTransform;
                }
                else
                {
                    Sprite sp = null;
                    if (sprites.TryGetValue(n.id, out string sn)) sp = UiKit.Get(sn);
                    if (n.id == "avatar") sp = AvatarPicker.Current(); // (your soldier's portrait, or the icon chosen)
                    if (sp == null && n.kind != "img") continue;
                    var img = UiKit.Img(root, n.id, sp, Vector2.zero, Vector2.zero, Vector2.one, n.kind == "icon" ? ParseColor(n.color) : Color.white);
                    if (sp == null) img.color = Color.clear;
                    img.preserveAspect = n.id == "logo" || n.id == "avatar" || n.kind == "icon";
                    img.type = Image.Type.Simple;
                    Place(img.rectTransform, n);
                    nodes[n.id] = img.rectTransform;
                    if (n.id == "avatar") { img.raycastTarget = true; var avImg = img; SettingsPanel.Tap(img.gameObject, () => AvatarPicker.Show(s2 => avImg.sprite = s2)); } // (tap: choose the picture)
                    if (n.id == "xpbar" || n.id == "season_bar")
                    {
                        // the bar's own track (the one baked in the card picture was removed: it no longer matched)
                        img.sprite = UiKit.Round; img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = 6f; img.color = new Color(.27f, .29f, .31f, 1f);
                        Fill(img.rectTransform, have / (float)need);
                    }
                }
            }
            // the tip strip grows with its text (long tips ran out of the box) and stays centred under the soldier
            if (nodes.TryGetValue("tip", out RectTransform tipRt) && texts.TryGetValue("tip_text", out Text tipText))
            {
                RectTransform tr = tipText.rectTransform;
                float textX = tr.anchoredPosition.x - tipRt.anchoredPosition.x, want = tipText.preferredWidth;
                float width = textX + want + 13 * s, centre = tipRt.anchoredPosition.x + tipRt.sizeDelta.x / 2f, dx = centre - width / 2f - tipRt.anchoredPosition.x;
                tipRt.sizeDelta = new Vector2(width, tipRt.sizeDelta.y);
                tr.sizeDelta = new Vector2(want + 4, tr.sizeDelta.y);
                foreach (var key in new[] { "tip", "tip_icon", "tip_text" }) if (nodes.TryGetValue(key, out RectTransform r)) r.anchoredPosition += new Vector2(dx, 0);
                var tipImg = tipRt.GetComponent<Image>();
                if (tipImg != null) { tipImg.sprite = UiKit.Panel; tipImg.type = Image.Type.Sliced; tipImg.pixelsPerUnitMultiplier = 3f; }
            }
            // season card: the xp numbers end at the card's right edge, on the "Level" line
            if (texts.TryGetValue("season_xp", out Text xp))
            {
                xp.alignment = TextAnchor.MiddleRight; xp.horizontalOverflow = HorizontalWrapMode.Overflow;
                float right = (10 + 220 - 12) * s, wTxt = xp.preferredWidth + 2;
                RectTransform xr = xp.rectTransform; xr.sizeDelta = new Vector2(wTxt, xr.sizeDelta.y);
                xr.anchoredPosition = new Vector2(right - wTxt, xr.anchoredPosition.y);
            }
            // "LV 10": the xp bar starts after the level text, whatever its length
            if (texts.TryGetValue("level", out Text lvT) && nodes.TryGetValue("xpbar", out RectTransform xb))
            {
                float end = xb.anchoredPosition.x + xb.sizeDelta.x, start = lvT.rectTransform.anchoredPosition.x + lvT.preferredWidth + 8 * s;
                lvT.rectTransform.sizeDelta = new Vector2(lvT.preferredWidth + 2, lvT.rectTransform.sizeDelta.y);
                xb.anchoredPosition = new Vector2(start, xb.anchoredPosition.y); xb.sizeDelta = new Vector2(Mathf.Max(30 * s, end - start), xb.sizeDelta.y);
            }
            // trophy / kills chips: one width that fits the longer number (100, 1000...), laid right to left before the gear
            if (nodes.TryGetValue("chip0", out RectTransform c0) && nodes.TryGetValue("chip1", out RectTransform c1) && nodes.TryGetValue("gear", out RectTransform gr)
                && texts.TryGetValue("chip0_text", out Text t0) && texts.TryGetValue("chip1_text", out Text t1))
            {
                float w = Mathf.Max(c0.sizeDelta.x, Mathf.Max(t0.preferredWidth, t1.preferredWidth) + 39 * s + 13 * s), right = gr.anchoredPosition.x - 8 * s;
                foreach (var (chip, txt, icon) in new[] { (c1, t1, "chip1_icon"), (c0, t0, "chip0_icon") })
                {
                    float x = right - w;
                    chip.anchoredPosition = new Vector2(x, chip.anchoredPosition.y); chip.sizeDelta = new Vector2(w, chip.sizeDelta.y);
                    if (nodes.TryGetValue(icon, out RectTransform ic)) ic.anchoredPosition = new Vector2(x + 13 * s, ic.anchoredPosition.y);
                    txt.horizontalOverflow = HorizontalWrapMode.Overflow;
                    txt.rectTransform.anchoredPosition = new Vector2(x + 39 * s, txt.rectTransform.anchoredPosition.y);
                    txt.rectTransform.sizeDelta = new Vector2(w - 39 * s - 8 * s, txt.rectTransform.sizeDelta.y);
                    right = x - 8 * s;
                }
            }
            // keep icons / labels above their panels
            foreach (var kv in texts) kv.Value.transform.SetAsLastSibling();
            foreach (var kv in nodes) if (kv.Key.EndsWith("_icon") || kv.Key == "rank" || kv.Key == "mapcard_pin") kv.Value.SetAsLastSibling();

            Tap("tab1", () => Open("outfit")); Tap("tab2", () => Open("map")); Tap("tab3", () => Open("friends")); Tap("tab4", () => Open("armory"));
            Tap("gear", () => Open("settings")); Tap("mapcard", () => Open("map")); Tap("mode1", () => Open("friends"));
            Tap("play", Play);
            Tap("tab0", Close);
            DanceButton();
        }

        float danceEnd = -1f; Text danceLabel;
        /// <summary>an emote button beside the soldier: he puts the gun on his back and dances (Entry), then takes it up again</summary>
        void DanceButton()
        {
            var b = UiKit.Img(root, "dance", UiKit.Round, new Vector2(.5f, .5f), Vector2.zero, Vector2.one, new Color(.1f, .12f, .14f, .78f), true);
            b.rectTransform.anchorMin = b.rectTransform.anchorMax = new Vector2(.5f, 0f); b.rectTransform.pivot = new Vector2(.5f, 0f);
            b.rectTransform.anchoredPosition = new Vector2(150f * s, 150f * s); b.rectTransform.sizeDelta = new Vector2(96f, 30f) * s;
            b.gameObject.AddComponent<Outline>().effectColor = new Color(1f, .83f, .3f, .8f);
            var t = UiKit.Txt(b.rectTransform, new Vector2(.5f, .5f), Vector2.zero, Vector2.one, 12f * s / UiKit.K, TextAnchor.MiddleCenter, new Color(1f, .83f, .3f));
            t.rectTransform.pivot = new Vector2(.5f, .5f); t.rectTransform.sizeDelta = new Vector2(96f, 30f) * s; t.text = "DANCE";
            Clickable.Add(b.gameObject, () =>
            {
                if (lobbyBody == null || !lobbyBody.HasClip("mx_dance")) return;
                // tap: your dance; tap while dancing: the next one (the last one you saw is your victory dance)
                if (danceEnd > 0f) Profile.Dance = (Profile.Dance + 1) % 3;
                lobbyBody.SetGun(null); lobbyBody.DanceChoice = -1; lobbyBody.Dance(); // (empty hands to dance) danceEnd = Time.time + SoldierBody.DanceLength[Profile.Dance]; lobbyAimEnd = -1f;
                danceLabel.text = SoldierBody.DanceNames[Profile.Dance];
            });
            danceLabel = t;
        }
        void DanceOver()
        {
            danceEnd = -1f; if (danceLabel != null) danceLabel.text = "DANCE"; if (lobbyBody == null) return;
            LobbyGunPose(Armory.LobbyGun); // (the gun back in his hands, standing as before)
        }

        void Place(RectTransform rt, LobbyNode n)
        {
            // anchor by group so the pieces of one card stay together on any screen width
            string id = n.id ?? "";
            float ax;
            if (id.StartsWith("chip") || id.StartsWith("gear") || id.StartsWith("mapcard") || id.StartsWith("mode") || id.StartsWith("play")) ax = 1;
            else if (id.StartsWith("tip")) ax = 0.5f;
            else if (id == "" ) { float cx = n.x + n.w / 2f; ax = cx < L.W * 0.42f ? 0 : cx > L.W * 0.58f ? 1 : 0.5f; }
            else ax = 0;
            // the season card, the modes and PLAY sit on the bottom edge like the web, whatever the screen height
            bool bottom = id.StartsWith("season") || id.StartsWith("mode") || id.StartsWith("play");
            rt.anchorMin = rt.anchorMax = new Vector2(ax, bottom ? 0 : 1); rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2((n.x - ax * L.W) * s, bottom ? (L.H - n.y) * s : -n.y * s);
            rt.sizeDelta = new Vector2(n.w * s, n.h * s);
        }

        void Fill(RectTransform track, float k)
        {
            var f = UiKit.Img(track, "fill", UiKit.Round, new Vector2(0, .5f), Vector2.zero, Vector2.zero, new Color(1f, .83f, .3f));
            f.rectTransform.anchorMin = new Vector2(0, 0); f.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(k), 1);
            f.rectTransform.offsetMin = f.rectTransform.offsetMax = Vector2.zero; f.pixelsPerUnitMultiplier = 6f;
        }

        void Tap(string id, Action a)
        {
            if (!nodes.TryGetValue(id, out RectTransform rt)) return;
            var g = rt.GetComponent<Graphic>(); if (g != null) g.raycastTarget = true;
            Clickable.Add(rt.gameObject, a).silent = id == "play";
        }

        static Color ParseColor(string css)
        {
            if (string.IsNullOrEmpty(css)) return Color.white;
            int a = css.IndexOf('('), b = css.IndexOf(')');
            if (a < 0 || b < a) return Color.white;
            string[] p = css.Substring(a + 1, b - a - 1).Split(',');
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            float r = float.Parse(p[0].Trim(), ci) / 255f, g = float.Parse(p[1].Trim(), ci) / 255f, bl = float.Parse(p[2].Trim(), ci) / 255f, al = p.Length > 3 ? float.Parse(p[3].Trim(), ci) : 1f;
            return new Color(r, g, bl, al);
        }

        void SetTab(int on)
        {
            for (int i = 0; i < 5; i++)
            {
                if (!nodes.TryGetValue("tab" + i, out RectTransform rt)) continue;
                rt.GetComponent<Image>().sprite = UiKit.Get(i == on ? "Lobby/tab_on" : "Lobby/tab");
                Color c = i == on ? new Color(27 / 255f, 20 / 255f, 4 / 255f) : Color.white;
                if (texts.TryGetValue("tab" + i + "_text", out Text t)) t.color = c;
                if (nodes.TryGetValue("tab" + i + "_icon", out RectTransform ic)) ic.GetComponent<Image>().color = c;
            }
        }

        // ------------------------------------------------------------------ sheets
        void Close()
        {
            if (sheet != null) Destroy(sheet.gameObject);
            if (shade != null) Destroy(shade.gameObject);
            sheet = shade = null; SetTab(0);
            if (wardrobe) { wardrobe = false; if (root != null) root.gameObject.SetActive(true); if (cam != null && !lcamTest) { cam.transform.position = camHome; cam.transform.rotation = camHomeRot; } }
        }

        void Open(string what)
        {
            Close();
            SetTab(what == "outfit" ? 1 : what == "map" ? 2 : what == "friends" ? 3 : what == "armory" ? 4 : 0);
            // full-screen page over the lobby (nothing behind it takes the drags), BACK top-left
            shade = UiKit.Img(canvas.transform, "shade", null, new Vector2(.5f, .5f), Vector2.zero, new Vector2(1400, 1400), new Color(0.05f, 0.06f, 0.07f, 1f), true).rectTransform;
            shade.pivot = new Vector2(.5f, .5f); UiKit.Fill(shade); shade.SetAsLastSibling();
            var bg = UiKit.Img(shade, "sheet", UiKit.Panel, Vector2.zero, Vector2.zero, Vector2.one, Color.white, true);
            sheet = bg.rectTransform;
            sheet.anchorMin = root.anchorMin; sheet.anchorMax = root.anchorMax; sheet.pivot = new Vector2(.5f, .5f);
            sheet.offsetMin = new Vector2(0, 8 * s); sheet.offsetMax = new Vector2(0, -8 * s);
            if (what == "outfit")
            {
                // the wardrobe: the page on the left 60 %, the soldier large on the right over the stage (drag him to turn
                // him), the lobby's own buttons put away meanwhile
                wardrobe = true;
                var si = shade.GetComponent<Image>(); si.color = new Color(0.04f, 0.03f, 0.08f, 0.35f); si.raycastTarget = false;
                root.gameObject.SetActive(false);
                float wpx = root.rect.width > 10 ? root.rect.width : 1700f;
                sheet.offsetMax = new Vector2(-wpx * 0.4f, -8 * s);
                bg.color = new Color(1f, 1f, 1f, 0.97f);
                if (cam != null && !lcamTest)
                {
                    Vector3 fwd = camHomeRot * Vector3.forward, right = camHomeRot * Vector3.right;
                    Vector3 aim = stageAt + Vector3.up * 1.0f - right * 1.15f;
                    cam.transform.position = aim - fwd * 4.3f; cam.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
                }
            }
            var back = UiKit.Img(sheet, "back", UiKit.Round, new Vector2(0, 1), new Vector2(12, -10) * k, new Vector2(96, 30) * k, new Color(1f, .83f, .3f), true);
            var bt = UiKit.Txt(back.rectTransform, new Vector2(.5f, .5f), Vector2.zero, new Vector2(96, 30) * k, 13 * k, TextAnchor.MiddleCenter, new Color(.1f, .08f, .02f));
            bt.rectTransform.pivot = new Vector2(.5f, .5f); bt.text = "< BACK"; Destroy(bt.GetComponent<Shadow>());
            Clickable.Add(back.gameObject, () => Close());
            string title = what == "friends" ? "PLAY WITH FRIENDS" : what == "outfit" ? "WARDROBE" : what.ToUpper();
            var tt = UiKit.Txt(sheet, new Vector2(0, 1), new Vector2(124, -15) * k, new Vector2(400, 22) * k, 17 * k, TextAnchor.UpperLeft, new Color(1f, .83f, .3f));
            tt.text = title;
            switch (what)
            {
                case "outfit": OutfitSheet(); break;
                case "map": MapSheet(); break;
                case "friends": Note("Playing with friends online is coming to the app soon.\nFor now: SOLO battle royale against 30 bots."); break;
                case "settings": SettingsSheet(); break;
                case "armory": ArmorySheet(); break;
            }
        }

        float k => s / UiKit.K; // CSS px -> UiKit units inside the sheet

        /// <summary>width of the page's scrolling column (canvas units)</summary>
        float PageW => Mathf.Max(200f, (root.rect.width > 10 ? root.rect.width : 1700f) - 32 * s);
        float ColW => Mathf.Min(PageW, 560 * s);

        /// <summary>scrolling column, centred on the page (width w, default: the narrow settings column)</summary>
        RectTransform ScrollArea(float w = -1)
        {
            if (w < 0) w = ColW;
            var view = new GameObject("view", typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(ScrollRect)).GetComponent<RectTransform>();
            // the view is the whole page (a drag anywhere on it scrolls), the content a centred column of width w
            view.SetParent(sheet, false); view.anchorMin = Vector2.zero; view.anchorMax = Vector2.one; view.pivot = new Vector2(.5f, .5f);
            view.offsetMin = new Vector2(0, 12 * s); view.offsetMax = new Vector2(0, -52 * s);
            view.GetComponent<Image>().color = new Color(1, 1, 1, 0f); // catches the drags anywhere on the page
            var content = new GameObject("content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(view, false); content.anchorMin = content.anchorMax = content.pivot = new Vector2(.5f, 1);
            content.sizeDelta = new Vector2(w, 0);
            var sr = view.GetComponent<ScrollRect>(); sr.content = content; sr.horizontal = false; sr.movementType = ScrollRect.MovementType.Clamped;
            return content;
        }

        /// <summary>an outfit's rarity: its name and colour (the cards' strip, the label over the soldier)</summary>
        static (string, Color) Rarity(int i)
        {
            if (SoldierBody.Group(i) == 0) return ("LEGENDARY", new Color(1f, .74f, .18f));
            if (i >= 14) return ("EPIC", new Color(.72f, .38f, 1f));
            return ("RARE", new Color(.3f, .62f, 1f));
        }

        void OutfitSheet()
        {
            // the wardrobe (Free Fire's layout in our own colours): categories down the left, the cards in a grid, the soldier
            // large on the right with the chosen outfit's name and rarity
            float W = sheet.rect.width > 10 ? sheet.rect.width : 1000f;
            float catW = 132 * k, pad = 12 * k, topY = 52 * s;
            // ---- categories
            for (int t = 0; t < OutfitTabs.Length; t++)
            {
                bool act = t == outfitTab; int tt = t;
                var b = UiKit.Img(sheet, "cat" + t, UiKit.Round, new Vector2(0, 1), new Vector2(pad, -52 * k - t * 46 * k), new Vector2(catW, 40 * k),
                    act ? new Color(1f, .8f, .24f) : new Color(.1f, .11f, .14f, .95f), true);
                if (act) { var bar = UiKit.Img(b.rectTransform, "bar", null, new Vector2(0, .5f), new Vector2(0, 0), new Vector2(4 * k, 40 * k), new Color(.55f, .32f, .02f)); bar.rectTransform.pivot = new Vector2(0, .5f); }
                var tx = UiKit.Txt(b.rectTransform, new Vector2(0, .5f), new Vector2(14 * k, 0), new Vector2(catW - 18 * k, 40 * k), 12.5f * k, TextAnchor.MiddleLeft, act ? new Color(.12f, .08f, .01f) : new Color(.9f, .92f, .95f));
                tx.rectTransform.pivot = new Vector2(0, .5f); tx.text = OutfitTabs[t];
                Clickable.Add(b.gameObject, () => { outfitTab = tt; Open("outfit"); });
            }
            // ---- the grid (scrolls)
            float gx = (pad * 2 + catW) * UiKit.K, gw = W - gx - pad * UiKit.K; // (canvas units: pad / catW are the UiKit calls' css x k)
            var view = new GameObject("view", typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(ScrollRect)).GetComponent<RectTransform>();
            view.SetParent(sheet, false); view.anchorMin = Vector2.zero; view.anchorMax = Vector2.one; view.pivot = new Vector2(0, 1);
            view.offsetMin = new Vector2(gx, 12 * s); view.offsetMax = new Vector2(-pad * UiKit.K, -topY);
            view.GetComponent<Image>().color = new Color(1, 1, 1, 0f);
            var content = new GameObject("content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(view, false); content.anchorMin = content.anchorMax = content.pivot = new Vector2(0, 1);
            var sr = view.GetComponent<ScrollRect>(); sr.content = content; sr.horizontal = false; sr.movementType = ScrollRect.MovementType.Clamped;
            float gap = 8 * s; int cols = Mathf.Clamp(Mathf.FloorToInt((gw + gap) / (L.card.w * s + gap)), 2, 5);
            float cw = (gw - gap * (cols - 1)) / cols, ch = cw * L.card.h / L.card.w;
            content.sizeDelta = new Vector2(gw, 0);
            int part = outfitTab - 1;
            int slot = 0;
            for (int i = 0; i < SoldierBody.OutfitFiles.Length; i++)
            {
                if (part >= 0 && SoldierBody.Group(i) == 0) continue; // (every modular piece, whatever is worn: one for the other body switches to that body with the piece on)
                int skin = i, at = slot++;
                int wearing = part < 0 ? Profile.Skin : (Profile.Part(part) >= 0 ? Profile.Part(part) : Profile.Skin);
                bool on = part < 0 ? (i == Profile.Skin && Profile.Part(0) < 0 && Profile.Part(1) < 0 && Profile.Part(2) < 0 && Profile.Part(3) < 0) : i == wearing;
                var (rname, rcol) = Rarity(i);
                // the card: dark, lit from below in the rarity's colour, a strip of it along the bottom; gold rim when worn
                var card = UiKit.Img(content, "card", UiKit.Round, new Vector2(0, 1), Vector2.zero, Vector2.one, on ? new Color(1f, .8f, .24f) : new Color(.16f, .17f, .21f), true);
                card.rectTransform.anchoredPosition = new Vector2((at % cols) * (cw + gap), -(at / cols) * (ch + gap));
                card.rectTransform.sizeDelta = new Vector2(cw, ch);
                var inner = UiKit.Img(card.rectTransform, "inner", UiKit.Round, new Vector2(.5f, .5f), Vector2.zero, new Vector2(cw - 4 * s, ch - 4 * s) / UiKit.K, Color.Lerp(new Color(.07f, .07f, .1f), rcol, .16f));
                inner.rectTransform.pivot = new Vector2(.5f, .5f);
                var glow = UiKit.Img(card.rectTransform, "glow", UiKit.Disc, new Vector2(.5f, 0), new Vector2(0, 14 * s) / UiKit.K, new Vector2(cw * 1.1f, ch * 0.55f) / UiKit.K, new Color(rcol.r, rcol.g, rcol.b, .22f));
                glow.rectTransform.pivot = new Vector2(.5f, .5f);
                Sprite sp = Resources.Load<Sprite>("UI/Outfits/" + SoldierBody.OutfitFiles[i]);
                Sprite pieceSp = part >= 0 ? Resources.Load<Sprite>("UI/Outfits/" + SoldierBody.OutfitFiles[i] + "_" + SoldierBody.PartNames[part]) : null;
                Sprite show = part < 0 ? sp : pieceSp;
                if (show != null)
                {
                    var pic = UiKit.Img(card.rectTransform, "pic", show, new Vector2(.5f, 1), Vector2.zero, Vector2.one, Color.white);
                    pic.preserveAspect = true; pic.rectTransform.pivot = new Vector2(.5f, 1);
                    pic.rectTransform.anchoredPosition = new Vector2(0, -4 * s); pic.rectTransform.sizeDelta = new Vector2(cw - 10 * s, ch - 30 * s);
                }
                else if (sp != null)
                {
                    float[] y0 = { .78f, .53f, .16f, 0f }, y1 = { 1f, .84f, .5f, .17f };
                    var raw = new GameObject("pic", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
                    raw.transform.SetParent(card.rectTransform, false); raw.raycastTarget = false; raw.texture = sp.texture;
                    Rect tr = sp.textureRect; float tw = sp.texture.width, th = sp.texture.height;
                    float bandH = (y1[part] - y0[part]) * tr.height, bandW = Mathf.Min(tr.width, bandH * cw / (ch - 30 * s));
                    raw.uvRect = new Rect((tr.x + (tr.width - bandW) / 2f) / tw, (tr.y + y0[part] * tr.height) / th, bandW / tw, bandH / th);
                    var rr = raw.rectTransform; rr.anchorMin = rr.anchorMax = rr.pivot = new Vector2(.5f, 1);
                    rr.anchoredPosition = new Vector2(0, -4 * s); rr.sizeDelta = new Vector2(cw - 10 * s, ch - 30 * s);
                }
                var strip = UiKit.Img(card.rectTransform, "rarity", null, new Vector2(.5f, 0), new Vector2(0, 2 * s) / UiKit.K, new Vector2(cw - 4 * s, 3.5f * s) / UiKit.K, rcol);
                strip.rectTransform.pivot = new Vector2(.5f, 0);
                var name = UiKit.Txt(card.rectTransform, new Vector2(.5f, 0), new Vector2(0, 6 * s) / UiKit.K, Vector2.one, 10.5f * k, TextAnchor.MiddleCenter, Color.white);
                name.rectTransform.pivot = new Vector2(.5f, 0); name.rectTransform.sizeDelta = new Vector2(cw, 18 * s); name.text = SoldierBody.OutfitNames[i];
                if (on) Check(card.rectTransform, cw);
                Clickable.Add(card.gameObject, () =>
                {
                    if (part < 0) { if (Profile.Skin == skin && Profile.Part(0) < 0 && Profile.Part(1) < 0 && Profile.Part(2) < 0 && Profile.Part(3) < 0) return; Profile.Skin = skin; Profile.ClearParts(); }
                    else
                    {
                        if (SoldierBody.Group(Profile.Skin) != SoldierBody.Group(skin)) { Profile.Skin = skin; Profile.ClearParts(); }
                        else Profile.SetPart(part, skin);
                    }
                    Open("outfit");
                    if (nodes.TryGetValue("avatar", out RectTransform av)) av.GetComponent<Image>().sprite = AvatarPicker.Current();
                    StartCoroutine(SpawnNextFrame());
                });
            }
            content.sizeDelta = new Vector2(gw, Mathf.CeilToInt(slot / (float)cols) * (ch + gap));
            // ---- over the soldier: the outfit's name and rarity, and how to turn him
            var (wr, wc) = Rarity(Profile.Skin);
            var info = new GameObject("worn", typeof(RectTransform)).GetComponent<RectTransform>();
            // (in the top right corner, small: over the soldier's head it hid him)
            info.SetParent(shade, false); info.anchorMin = info.anchorMax = new Vector2(1, 1); info.pivot = new Vector2(1, 1);
            info.anchoredPosition = new Vector2(-18 * s, -16 * s); info.sizeDelta = new Vector2(220 * k, 60 * k);
            var rl = UiKit.Img(info, "rar", UiKit.Round, new Vector2(1, 1), Vector2.zero, new Vector2(84 * k, 18 * k), new Color(wc.r, wc.g, wc.b, .9f));
            rl.rectTransform.pivot = new Vector2(1, 1);
            var rlt = UiKit.Txt(rl.rectTransform, new Vector2(.5f, .5f), Vector2.zero, new Vector2(84 * k, 18 * k), 9 * k, TextAnchor.MiddleCenter, new Color(.08f, .06f, .02f));
            rlt.rectTransform.pivot = new Vector2(.5f, .5f); rlt.text = wr;
            var nm = UiKit.Txt(info, new Vector2(1, 1), new Vector2(0, -21 * k), new Vector2(220 * k, 24 * k), 16 * k, TextAnchor.MiddleRight, Color.white);
            nm.rectTransform.pivot = new Vector2(1, 1); nm.text = SoldierBody.OutfitNames[Mathf.Clamp(Profile.Skin, 0, SoldierBody.OutfitNames.Length - 1)].ToUpper();
            var hint = UiKit.Txt(shade, new Vector2(.8f, 0), new Vector2(0, 22 * s) / UiKit.K, new Vector2(300 * k, 18 * k), 10 * k, TextAnchor.MiddleCenter, new Color(1, 1, 1, .6f));
            hint.rectTransform.pivot = new Vector2(.5f, 0); hint.text = "< DRAG TO TURN >";
        }

        static readonly string[] OutfitTabs = { "FULL OUTFIT", "HEAD", "TOP", "PANTS", "SHOES" };
        int outfitTab;
        System.Collections.IEnumerator ShowWhenPlanted(SoldierBody b)
        {
            var rs = b.GetComponentsInChildren<Renderer>(true); foreach (var r in rs) r.forceRenderingOff = true;
            for (int i = 0; i < 40 && b != null && !b.Planted; i++) yield return null;
            yield return null; yield return null; // (two more frames: the plant applied)
            if (b == null) yield break;
            foreach (var r in b.GetComponentsInChildren<Renderer>(true)) if (r != null) r.forceRenderingOff = false;
        }
        System.Collections.IEnumerator SpawnNextFrame() { yield return null; SpawnSoldier(); }

        /// <summary>a dark tick on the chosen card's gold corner badge</summary>
        void Check(RectTransform card, float cw)
        {
            float u = cw / 96f; // card picture units (the card is 96 css px wide)
            Vector2 c = new Vector2(-13f * u, -13f * u); // (inside the card: at -9 its top / right were cut off by the grid's mask)
            // its own gold disc under the tick, over the picture: the card's printed badge hid behind a piece's picture
            // (the tick looked different from card to card, some half covered)
            var disc = UiKit.Img(card, "badge", UiKit.Disc, new Vector2(1, 1), c, new Vector2(10f * u, 10f * u), new Color(1f, .83f, .3f)); // (was 17: it covered the card)
            disc.rectTransform.pivot = new Vector2(.5f, .5f); disc.rectTransform.anchoredPosition = c;
            disc.gameObject.AddComponent<Outline>().effectColor = new Color(27 / 255f, 20 / 255f, 4 / 255f, .5f);
            void Bar(Vector2 from, Vector2 to)
            {
                var b = UiKit.Img(card, "tick", null, new Vector2(1, 1), Vector2.zero, Vector2.one, new Color(27 / 255f, 20 / 255f, 4 / 255f));
                Vector2 d = to - from; var rt = b.rectTransform;
                rt.pivot = new Vector2(0, .5f); rt.anchoredPosition = c + from * u; rt.sizeDelta = new Vector2(d.magnitude * u + 1.2f * u, 1.7f * u);
                rt.localEulerAngles = new Vector3(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            }
            Bar(new Vector2(-2.7f, 0.3f), new Vector2(-0.8f, -1.7f));
            Bar(new Vector2(-1.1f, -1.7f), new Vector2(2.9f, 2.2f));
        }

        void MapSheet()
        {
            var content = ScrollArea();
            string[] ids = { "valley", "green", "forest", "classic" }; string[] names = { "Death Valley", "Highlands", "Oakwood", "Valley" };
            string[] desc = { "BIG · real desert scan · 2 villages, a desert town, a city", "Real moorland scan · farm village", "Real forest scan · woods, river, farms", "Pine valley · 25 houses" };
            float w = ColW, h = 96 * s;
            // first: the next map is picked by itself after every match, or the one you choose stays
            var lbl = UiKit.Txt(content, new Vector2(.5f, 1), Vector2.zero, Vector2.one, 12 * k, TextAnchor.UpperLeft, new Color(.87f, .9f, .91f));
            lbl.rectTransform.pivot = new Vector2(.5f, 1); lbl.rectTransform.sizeDelta = new Vector2(w, 16 * s); lbl.text = "MAP EACH MATCH";
            var segRoot = new GameObject("seg", typeof(RectTransform)).GetComponent<RectTransform>();
            segRoot.SetParent(content, false); segRoot.anchorMin = segRoot.anchorMax = segRoot.pivot = new Vector2(.5f, 1);
            segRoot.anchoredPosition = new Vector2(0, -20 * s); segRoot.sizeDelta = new Vector2(w, 32 * s);
            SettingsPanel.Segments(segRoot, new Vector2(0, 0), new Vector2(w, 32 * s), 11 * k, new[] { "ROTATE (NEW MAP EVERY MATCH)", "KEEP MY CHOICE" }, Profile.MapAuto ? 0 : 1, i => Profile.MapAuto = i == 0);
            float top = 64 * s;
            for (int i = 0; i < ids.Length; i++)
            {
                string id = ids[i]; bool on = Profile.Map == id;
                var card = UiKit.Img(content, "map", UiKit.Get("Lobby/mapcard_" + id), new Vector2(.5f, 1), Vector2.zero, Vector2.one, on ? Color.white : new Color(.75f, .75f, .75f), true);
                card.rectTransform.pivot = new Vector2(.5f, 1); card.rectTransform.anchoredPosition = new Vector2(0, -top - i * (h + 10 * s)); card.rectTransform.sizeDelta = new Vector2(w, h);
                if (on) card.gameObject.AddComponent<Outline>().effectColor = new Color(1f, .83f, .3f);
                var t = UiKit.Txt(card.rectTransform, new Vector2(1, 0), new Vector2(-12, 30) * k, new Vector2(300, 28) * k, 17 * k, TextAnchor.LowerRight, Color.white); t.rectTransform.pivot = new Vector2(1, 0); t.text = names[i];
                var d = UiKit.Txt(card.rectTransform, new Vector2(1, 0), new Vector2(-12, 10) * k, new Vector2(300, 16) * k, 10 * k, TextAnchor.LowerRight, new Color(1, 1, 1, .85f)); d.rectTransform.pivot = new Vector2(1, 0); d.fontStyle = FontStyle.Normal; d.text = desc[i];
                Clickable.Add(card.gameObject, () => { Profile.MapAuto = false; if (Profile.Map == id) { Close(); return; } Profile.Map = id; Loader.Go("Loading " + MapName(id) + "…"); });
            }
            content.sizeDelta = new Vector2(content.sizeDelta.x, top + ids.Length * (h + 10 * s));
        }

        void Note(string text)
        {
            var t = UiKit.Txt(sheet, new Vector2(.5f, .5f), Vector2.zero, new Vector2(520, 120) * k, 13 * k, TextAnchor.MiddleCenter, Color.white);
            t.rectTransform.pivot = new Vector2(.5f, .5f); t.fontStyle = FontStyle.Normal; t.horizontalOverflow = HorizontalWrapMode.Wrap; t.text = text;
        }

        // ------------------------------------------------------------------ ARMORY: upgraded guns for coins
        int armoryGun;
        void ArmorySheet()
        {
            float pw = Mathf.Min(PageW, 760 * s);
            var content = ScrollArea(pw);
            float w = pw, y = 0;
            Color gold = new Color(1f, .83f, .3f), ink = new Color(27 / 255f, 20 / 255f, 4 / 255f);
            // coins, top right of the page
            var coins = UiKit.Txt(sheet, new Vector2(1, 1), new Vector2(-24, -16) * k, new Vector2(260, 24) * k, 15 * k, TextAnchor.UpperRight, gold);
            coins.rectTransform.pivot = new Vector2(1, 1); coins.text = $"COINS  {Armory.Coins}";
            // the gun kinds
            SettingsPanel.Segments(content, Vector2.zero, new Vector2(w, 30 * s), 11 * k, new[] { "RIFLE", "SMG", "SHOTGUN", "SNIPER", "PISTOL" }, armoryGun, i => { armoryGun = i; Open("armory"); });
            y += 42 * s;
            string gun = Armory.Guns[armoryGun];
            // the best of each number among this gun's versions fills its bar
            float mD = 0, mR = 0, mG = 0, mS = 0; int mM = 0;
            foreach (var v in Armory.All) if (v.gun == gun) { var st = Armory.Stats(v); mD = Mathf.Max(mD, st.dmg); mR = Mathf.Max(mR, st.rpm); mG = Mathf.Max(mG, st.range); mS = Mathf.Max(mS, st.steady); mM = Mathf.Max(mM, st.mag); }
            float cardH = 132 * s;
            foreach (var v in Armory.All)
            {
                if (v.gun != gun) continue;
                var vv = v;
                bool owned = Armory.Owned(v), on = v.cost > 0 && Armory.IsEquipped(v), shown = Armory.LobbyGun == v.key;
                var card = UiKit.Img(content, "card", UiKit.Round, new Vector2(0, 1), Vector2.zero, Vector2.one, on ? new Color(.3f, .26f, .1f, .95f) : new Color(.13f, .15f, .17f, .95f), true);
                card.rectTransform.anchoredPosition = new Vector2(0, -y); card.rectTransform.sizeDelta = new Vector2(w, cardH);
                if (on) card.gameObject.AddComponent<Outline>().effectColor = gold;
                Transform c = card.transform;
                var pic = UiKit.Img(c, "gun", Resources.Load<Sprite>("UI/Items/gun_" + v.key) ?? UiKit.Get("gun_" + gun), new Vector2(0, .5f), new Vector2(10, 4) * k, new Vector2(165, 70) * k, Color.white); // its own real model, in colour
                pic.rectTransform.pivot = new Vector2(0, .5f); pic.preserveAspect = true;
                var nm = UiKit.Txt(c, new Vector2(0, 1), new Vector2(14, -10) * k, new Vector2(300, 20) * k, 14 * k, TextAnchor.UpperLeft, on ? gold : Color.white); nm.text = v.real; nm.rectTransform.sizeDelta = new Vector2(165, 20) * k;
                var pk = UiKit.Txt(c, new Vector2(0, 0), new Vector2(14, 10) * k, new Vector2(300, 16) * k, 10 * k, TextAnchor.LowerLeft, new Color(1, 1, 1, .7f)); pk.fontStyle = FontStyle.Normal; pk.text = (v.cost > 0 ? v.name + " · " : "") + v.perk; // (the upgrade's name with its perk, in the picture's column)
                // the line stays in the picture's column (it ran under the bars), wrapping onto a second line if it must
                pk.rectTransform.pivot = new Vector2(0, 0); pk.rectTransform.sizeDelta = new Vector2(160 * s, 28 * s); pk.horizontalOverflow = HorizontalWrapMode.Wrap;
                // the numbers as bars
                var st2 = Armory.Stats(v);
                (string, float, string)[] bars = {
                    ("DAMAGE", st2.dmg / mD, Mathf.RoundToInt(st2.dmg).ToString()), ("FIRE RATE", st2.rpm / mR, Mathf.RoundToInt(st2.rpm) + "/min"),
                    ("RANGE", st2.range / mG, Mathf.RoundToInt(st2.range) + " m"), ("STEADINESS", st2.steady / mS, ""), ("MAGAZINE", st2.mag / (float)mM, st2.mag.ToString()) };
                float bx = 180 * s, bw = w - bx - 150 * s;
                for (int i = 0; i < bars.Length; i++)
                {
                    float by = -14 * s - i * 22 * s;
                    var lb = UiKit.Txt(c, new Vector2(0, 1), Vector2.zero, Vector2.one, 9 * k, TextAnchor.MiddleLeft, new Color(.8f, .84f, .86f));
                    lb.rectTransform.anchoredPosition = new Vector2(bx, by); lb.rectTransform.sizeDelta = new Vector2(90 * s, 16 * s); lb.text = bars[i].Item1;
                    var tr = UiKit.Img(c, "bar", UiKit.Round, new Vector2(0, 1), Vector2.zero, Vector2.one, new Color(.05f, .06f, .07f, 1f));
                    tr.pixelsPerUnitMultiplier = 6f; tr.rectTransform.anchoredPosition = new Vector2(bx + 92 * s, by - 5 * s); tr.rectTransform.sizeDelta = new Vector2(bw - 140 * s, 7 * s);
                    var fl = UiKit.Img(tr.rectTransform, "f", UiKit.Round, new Vector2(0, 1), Vector2.zero, Vector2.one, gold); fl.pixelsPerUnitMultiplier = 6f;
                    fl.rectTransform.anchorMin = Vector2.zero; fl.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(bars[i].Item2), 1); fl.rectTransform.offsetMin = fl.rectTransform.offsetMax = Vector2.zero;
                    var val = UiKit.Txt(c, new Vector2(0, 1), Vector2.zero, Vector2.one, 9 * k, TextAnchor.MiddleLeft, Color.white);
                    val.rectTransform.anchoredPosition = new Vector2(bx + 92 * s + bw - 132 * s, by); val.rectTransform.sizeDelta = new Vector2(60 * s, 16 * s); val.text = bars[i].Item3;
                }
                // BUY / EQUIP / EQUIPPED (several versions can be equipped: each one's numbers when you pick that gun up;
                // tap EQUIPPED to take it off) and SHOW IN LOBBY (the one gun in your soldier's hands in the lobby)
                Image Btn(float yOff, string label, bool can, bool lit, System.Action act)
                {
                    var bb = UiKit.Img(c, "act", UiKit.GoldPlate, new Vector2(1, .5f), Vector2.zero, Vector2.one, lit ? Color.white : can ? new Color(.78f, .8f, .82f) : new Color(1, 1, 1, .35f), true);
                    bb.rectTransform.pivot = new Vector2(1, .5f); bb.rectTransform.anchoredPosition = new Vector2(-14 * s, yOff); bb.rectTransform.sizeDelta = new Vector2(124 * s, 32 * s);
                    var t = UiKit.Txt(bb.rectTransform, new Vector2(.5f, .5f), Vector2.zero, Vector2.one, 10 * k, TextAnchor.MiddleCenter, ink);
                    t.rectTransform.pivot = new Vector2(.5f, .5f); t.rectTransform.sizeDelta = bb.rectTransform.sizeDelta; t.text = label; Destroy(t.GetComponent<Shadow>());
                    if (can) Clickable.Add(bb.gameObject, () => { act(); Open("armory"); });
                    return bb;
                }
                if (!owned) Btn(20 * s, $"BUY  {v.cost}", Armory.Coins >= v.cost, true, () => { if (Armory.Buy(vv)) Armory.SetEquipped(vv, true); });
                else if (v.cost > 0) Btn(20 * s, on ? "EQUIPPED" : "EQUIP", true, on, () => Armory.SetEquipped(vv, !Armory.IsEquipped(vv)));
                else Btn(20 * s, "STANDARD", false, false, () => { });
                Btn(-20 * s, shown ? "IN LOBBY" : "SHOW IN LOBBY", owned && !shown, shown, () => { Armory.LobbyGun = vv.key; LobbyGunPose(vv.key); });
                y += cardH + 10 * s;
            }
            var note = UiKit.Txt(content, new Vector2(0, 1), new Vector2(0, -y / UiKit.K), new Vector2(w / UiKit.K, 20 * s / UiKit.K), 10 * k, TextAnchor.UpperLeft, new Color(1, 1, 1, .6f));
            note.fontStyle = FontStyle.Normal; note.text = "Coins: +20 a match, +15 a kill, +150 for a win. Every version lies about the map: picking one up gives your equipped upgrade of it, or its standard numbers.";
            content.sizeDelta = new Vector2(content.sizeDelta.x, y + 40 * s);
        }

        void SettingsSheet()
        {
            var content = ScrollArea();
            float y = 0, w = ColW;
            Text Label(string txt) { var t = UiKit.Txt(content, new Vector2(0, 1), Vector2.zero, Vector2.one, 12 * k, TextAnchor.UpperLeft, new Color(.87f, .9f, .91f)); t.rectTransform.anchoredPosition = new Vector2(0, -y); t.rectTransform.sizeDelta = new Vector2(w, 16 * s); t.text = txt; y += 20 * s; return t; }
            // name
            Label("PLAYER NAME");
            var field = new GameObject("name", typeof(RectTransform), typeof(Image), typeof(InputField)).GetComponent<InputField>();
            var fr = (RectTransform)field.transform; fr.SetParent(content, false); fr.anchorMin = fr.anchorMax = fr.pivot = new Vector2(0, 1);
            fr.anchoredPosition = new Vector2(0, -y); fr.sizeDelta = new Vector2(w * .7f, 30 * s);
            { var fi = field.GetComponent<Image>(); fi.sprite = UiKit.Frame; fi.type = Image.Type.Sliced; fi.color = Color.white; }
            var ft = UiKit.Txt(fr, new Vector2(0, .5f), new Vector2(8, 0) * k, new Vector2(w * .7f / s - 16, 24) * k, 13 * k, TextAnchor.MiddleLeft, Color.white); ft.supportRichText = false;
            field.textComponent = ft; field.characterLimit = 14; field.text = Profile.Name;
            field.onEndEdit.AddListener(v => { Profile.Name = v; if (texts.TryGetValue("name", out Text nt)) nt.text = Profile.Name; });
            y += 40 * s;
            y += SettingsPanel.Fill(content, 0, y, w, s);
            // move / resize / fade every in-match control (the same editor as in the match)
            Label("CONTROLS");
            var cb = UiKit.Img(content, "custom", UiKit.Round, new Vector2(0, 1), Vector2.zero, Vector2.one, new Color(.23f, .26f, .28f), true);
            cb.rectTransform.anchoredPosition = new Vector2(0, -y); cb.rectTransform.sizeDelta = new Vector2(w * .9f, 32 * s);
            var cbt = UiKit.Txt(cb.rectTransform, new Vector2(.5f, .5f), Vector2.zero, Vector2.one, 12 * k, TextAnchor.MiddleCenter, new Color(1f, .83f, .3f));
            cbt.rectTransform.pivot = new Vector2(.5f, .5f); cbt.rectTransform.sizeDelta = new Vector2(w * .9f, 32 * s); cbt.text = "CUSTOMIZE BUTTONS";
            Clickable.Add(cb.gameObject, CustomizeButtons);
            y += 42 * s;
            content.sizeDelta = new Vector2(content.sizeDelta.x, y + 10 * s);
        }

        /// <summary>the match controls laid over the lobby, with stand-ins for the weapon box and the minimap, in the layout editor</summary>
        void CustomizeButtons()
        {
            Close();
            canvas.enabled = false;
            TouchControls tc = TouchControls.Create();
            tc.Preview = true;
            // stand-ins for the HUD pieces, drawn with their pictures (no words)
            RectTransform Stand(string name, string icon, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Vector2 iconSize)
            {
                var img = UiKit.Img(tc.Root, name, UiKit.Round, anchor, pos, size, new Color(24 / 255f, 32 / 255f, 40 / 255f, .7f), true);
                img.rectTransform.pivot = pivot;
                var ic = UiKit.Img(img.rectTransform, "icon", UiKit.Get(icon), new Vector2(.5f, .5f), Vector2.zero, iconSize, Color.white);
                ic.rectTransform.pivot = new Vector2(.5f, .5f); ic.preserveAspect = true;
                return img.rectTransform;
            }
            tc.AddEditable("weapons", Stand("WEAPONS", "gun_rifle", new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 50), new Vector2(209, 85), new Vector2(110, 40)));
            tc.AddEditable("minimap", Stand("MAP", "icon_map", new Vector2(1, 1), new Vector2(1, 1), new Vector2(-8, -6), new Vector2(132, 132), new Vector2(46, 46)));
            tc.AddEditable("loot", Stand("LOOT", "gun_pistol", new Vector2(1, .56f), new Vector2(1, .5f), new Vector2(-232, 0), new Vector2(172, 62), new Vector2(60, 26)));
            tc.AddEditable("killfeed", Stand("FEED", "icon_skull", new Vector2(0, 1), new Vector2(0, 1), new Vector2(8, -54), new Vector2(200, 40), new Vector2(26, 26)));
            tc.AddEditable("status", Stand("STATUS", "icon_bag", new Vector2(.5f, 0), new Vector2(0, 0), new Vector2(-65, 27), new Vector2(160, 18), new Vector2(16, 16)));
            var fpsT = UiKit.Txt(tc.Root, new Vector2(0, 1), new Vector2(16, -10) / UiKit.K, new Vector2(330, 30) / UiKit.K, 20f / UiKit.K, TextAnchor.UpperLeft, new Color(1, 1, 1, .6f));
            fpsT.text = "60 fps"; fpsT.raycastTarget = true;
            tc.AddEditable("fps", fpsT.rectTransform);
            LayoutEditor.Open(tc, () => { Destroy(tc.Canvas.gameObject); Destroy(tc.gameObject); canvas.enabled = true; });
        }

        void Slider(RectTransform parent, ref float y, float w, float min, float max, float val, Action<float> on)
        {
            var go = new GameObject("slider", typeof(RectTransform), typeof(Slider));
            var rt = (RectTransform)go.transform; rt.SetParent(parent, false); rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(0, -y); rt.sizeDelta = new Vector2(w * .9f, 22 * s);
            var track = UiKit.Img(rt, "track", UiKit.Round, new Vector2(0, .5f), Vector2.zero, Vector2.one, new Color(.04f, .05f, .06f)); track.rectTransform.anchorMin = new Vector2(0, .3f); track.rectTransform.anchorMax = new Vector2(1, .7f); track.rectTransform.offsetMin = track.rectTransform.offsetMax = Vector2.zero;
            var fillArea = new GameObject("fill", typeof(RectTransform)).GetComponent<RectTransform>(); fillArea.SetParent(rt, false); fillArea.anchorMin = new Vector2(0, .3f); fillArea.anchorMax = new Vector2(1, .7f); fillArea.offsetMin = fillArea.offsetMax = Vector2.zero;
            var fill = UiKit.Img(fillArea, "f", UiKit.Round, Vector2.zero, Vector2.zero, Vector2.one, new Color(1f, .83f, .3f, .7f));
            var handleArea = new GameObject("handles", typeof(RectTransform)).GetComponent<RectTransform>(); handleArea.SetParent(rt, false); handleArea.anchorMin = Vector2.zero; handleArea.anchorMax = Vector2.one; handleArea.offsetMin = handleArea.offsetMax = Vector2.zero;
            var handle = UiKit.Img(handleArea, "h", null, new Vector2(0, .5f), Vector2.zero, new Vector2(20, 20) * k, new Color(0, 0, 0, 0), true);
            handle.rectTransform.pivot = new Vector2(.5f, .5f);
            var diamond = UiKit.Img(handle.rectTransform, "d", null, new Vector2(.5f, .5f), Vector2.zero, new Vector2(13, 13) * k, new Color(1f, .83f, .3f));
            diamond.rectTransform.pivot = new Vector2(.5f, .5f); diamond.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
            diamond.gameObject.AddComponent<Outline>().effectColor = new Color(.16f, .11f, .02f);
            var sl = go.GetComponent<Slider>(); sl.fillRect = fill.rectTransform; sl.handleRect = handle.rectTransform; sl.targetGraphic = handle;
            sl.minValue = min; sl.maxValue = max; sl.value = val; sl.onValueChanged.AddListener(v => on(v));
            y += 32 * s;
        }

        void Choice(RectTransform parent, ref float y, float w, string[] opts, int sel, Action<int> on)
        {
            SettingsPanel.Segments(parent, new Vector2(0, -y), new Vector2(w, 30 * s), 11 * k, opts, sel, on);
            y += 40 * s;
        }

        // ------------------------------------------------------------------ PLAY + turning the soldier
        public static string MapName(string id) => id == "forest" ? "Oakwood" : id == "classic" ? "Valley" : id == "valley" ? "Death Valley" : "Highlands";

        // ------------------------------------------------------------------ PLAY: a short matchmaking wait (cancellable)
        Coroutine matching; RectTransform spinner, spinArc; Text spinText; float matchStart;

        /// <summary>PLAY: a yellow ring spins in the middle (the game emblem inside) while the game checks the network,
        /// loads the map and gathers the players; PLAY turns into CANCEL meanwhile.</summary>
        void Play()
        {
            if (matching != null) { Sfx.Play2D("ui_click", 0.55f); CancelMatch(); return; }
            Sfx.Play2D("ui_start", 0.8f);
            matching = StartCoroutine(Matchmake());
        }

        void CancelMatch()
        {
            if (matching != null) StopCoroutine(matching);
            matching = null;
            if (spinner != null) Destroy(spinner.gameObject);
            SetPlayButton(false);
        }

        void SetPlayButton(bool cancel)
        {
            if (texts.TryGetValue("play_text", out Text pt)) pt.text = cancel ? "CANCEL" : "PLAY";
            if (texts.TryGetValue("play_sub", out Text ps)) ps.text = cancel ? "Tap to stop searching" : "Battle royale · vs bots";
            if (nodes.TryGetValue("play", out RectTransform pr)) pr.GetComponent<Image>().color = cancel ? new Color(.85f, .55f, .5f) : Color.white;
        }

        System.Collections.IEnumerator Matchmake()
        {
            SetPlayButton(true);
            matchStart = Time.unscaledTime;
            const float K = UiKit.K; float u = s / K;
            spinner = new GameObject("Matchmaking", typeof(RectTransform)).GetComponent<RectTransform>();
            spinner.SetParent(root, false); spinner.anchorMin = spinner.anchorMax = spinner.pivot = new Vector2(.5f, .5f);
            spinner.anchoredPosition = new Vector2(0, 10 * s); spinner.sizeDelta = Vector2.one * 92 * s;
            var bgRing = UiKit.Img(spinner, "track", UiKit.Ring, new Vector2(.5f, .5f), Vector2.zero, new Vector2(92, 92) * u, new Color(0, 0, 0, .45f)); bgRing.rectTransform.pivot = new Vector2(.5f, .5f);
            var disc = UiKit.Img(spinner, "back", UiKit.Disc, new Vector2(.5f, .5f), Vector2.zero, new Vector2(84, 84) * u, new Color(.06f, .07f, .08f, .78f)); disc.rectTransform.pivot = new Vector2(.5f, .5f);
            var arc = UiKit.Img(spinner, "arc", UiKit.Ring, new Vector2(.5f, .5f), Vector2.zero, new Vector2(92, 92) * u, new Color(1f, .83f, .3f));
            arc.rectTransform.pivot = new Vector2(.5f, .5f); arc.type = Image.Type.Filled; arc.fillMethod = Image.FillMethod.Radial360; arc.fillAmount = 0.28f;
            spinArc = arc.rectTransform;
            var icon = UiKit.Img(spinner, "emblem", Resources.Load<Sprite>("UI/emblem"), new Vector2(.5f, .5f), Vector2.zero, new Vector2(52, 52) * u, Color.white);
            icon.rectTransform.pivot = new Vector2(.5f, .5f); icon.preserveAspect = true;
            spinText = UiKit.Txt(spinner, new Vector2(.5f, 0), new Vector2(0, -8) * u, new Vector2(320, 20) * u, 12 * u, TextAnchor.UpperCenter, Color.white);
            spinText.rectTransform.pivot = new Vector2(.5f, 1);

            // 1) the network (the match itself runs offline against bots when there is none)
            spinText.text = "Checking connection…";
            bool online = Application.internetReachability != NetworkReachability.NotReachable;
            yield return new WaitForSecondsRealtime(0.6f);
            // 2) the map and the soldiers (read in the background, the wait shows meanwhile)
            spinText.text = "Loading " + MapName(Profile.Map) + "…";
            var req = Resources.LoadAsync<TextAsset>("Maps/" + Profile.Map);
            while (!req.isDone) yield return null;
            yield return new WaitForSecondsRealtime(0.5f);
            // 3) the players
            float until = Time.unscaledTime + UnityEngine.Random.Range(1.2f, 2.2f);
            while (Time.unscaledTime < until)
            {
                int found = Mathf.Min(GameBoot.BotCount + 1, 1 + Mathf.RoundToInt((1f - (until - Time.unscaledTime) / 2.2f) * GameBoot.BotCount));
                spinText.text = $"Finding players… {found}/{GameBoot.BotCount + 1}" + (online ? "" : "  ·  offline");
                yield return null;
            }
            spinText.text = "Match ready!";
            yield return new WaitForSecondsRealtime(0.35f);
            matching = null;
            GameBoot.InMatch = true;
            Loader.Go("Loading " + MapName(Profile.Map) + "…");
        }

        void Update()
        {
            if (spinArc != null) spinArc.localEulerAngles = new Vector3(0, 0, -Time.unscaledTime * 300f);
            if (lobbyBody != null && soldier != null && lobbyBody.ShoulderGrip) lobbyBody.AimDir = soldier.transform.forward; // (the pistol points where he faces as he is turned)
            // (a sheet or the button editor is open: their drags belong to them, the soldier stays put)
            if ((sheet != null && !wardrobe) || soldier == null || !canvas.enabled) { dragFinger = -1; mouseDrag = false; return; }
            // drag anywhere off the buttons to turn the soldier
            if (Input.touchCount > 0)
            {
                foreach (Touch t in Input.touches)
                {
                    if (t.phase == TouchPhase.Began && dragFinger < 0 && !OverUi(t.fingerId)) { dragFinger = t.fingerId; dragLast = t.position; }
                    if (t.fingerId != dragFinger) continue;
                    soldierYaw -= (t.position.x - dragLast.x) * 0.4f; dragLast = t.position;
                    if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled) dragFinger = -1;
                }
            }
            else
            {
                dragFinger = -1;
                if (Input.GetMouseButtonDown(0) && !OverUi(-1)) { mouseDrag = true; dragLast = Input.mousePosition; }
                if (Input.GetMouseButtonUp(0)) mouseDrag = false;
                if (mouseDrag) { soldierYaw -= (Input.mousePosition.x - dragLast.x) * 0.4f; dragLast = Input.mousePosition; }
            }
            soldier.transform.localRotation = Quaternion.Euler(0, soldierYaw, 0);
            // rifle held diagonally across the chest (port arms): muzzle up and to the soldier's left, grip at the waist
            // (a long gun only: the pistol is aimed straight ahead above - this ran after that and turned the pistol 55 degrees
            // left and 38 up, the arms folded over the face, the pistol by / behind the head)
            if (lobbyBody != null && !lobbyBody.ShoulderGrip) lobbyBody.AimDir = soldier.transform.forward; // (the rifle points where he faces, as in a match)
            // the rifle raised to the shoulder now and then (aiming at a target), as the lobby did, then down again
            if (danceEnd > 0f) { if (Time.time >= danceEnd) DanceOver(); else return; } // (dancing: nothing else)
            if (lobbyBody != null && !lobbyBody.ShoulderGrip && forcedPose == null && lobbyAimAt > 0f)
            {
                if (lobbyAimEnd < 0f && Time.time >= lobbyAimAt) { lobbyBody.Play(lobbyBody.HasClip("mx_aim") ? "mx_aim" : "fire"); lobbyBody.StandTall = false; lobbyAimEnd = Time.time + 2.6f; }
                else if (lobbyAimEnd > 0f && Time.time >= lobbyAimEnd) { lobbyBody.Play("idle_rifle"); lobbyBody.StandTall = true; lobbyAimEnd = -1f; lobbyAimAt = Time.time + UnityEngine.Random.Range(5f, 8f); }
            }
            if (forcedPose != null && lobbyBody != null) { lobbyBody.Pose = forcedPose; lobbyBody.Speed = 1.1f; lobbyBody.Armed = forcedPose != "runfree"; }
        }

        static bool OverUi(int finger)
        {
            var es = EventSystem.current;
            return es != null && (finger >= 0 ? es.IsPointerOverGameObject(finger) : es.IsPointerOverGameObject());
        }
    }
}
