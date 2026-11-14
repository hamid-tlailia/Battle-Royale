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
        LobbyLayout L; float s; Canvas canvas;
        readonly Dictionary<string, RectTransform> nodes = new Dictionary<string, RectTransform>();
        readonly Dictionary<string, Text> texts = new Dictionary<string, Text>();
        GameObject soldier; float soldierYaw; SoldierBody lobbyBody;
        Transform stage; Camera cam;
        RectTransform shade, sheet; int dragFinger = -1; Vector2 dragLast; bool mouseDrag;
        static readonly string[] Tips = { "Welcome to Last War!", "Land, loot a gun, stay in the zone.", "Tap a weapon to equip it, tap it again to reload.", "Headshots hit twice as hard.", "The shotgun fires two blasts per press." };

        public static Lobby Create(MapData map, Camera cam)
        {
            var l = new GameObject("Lobby").AddComponent<Lobby>();
            l.cam = cam; l.Build3D(map); l.BuildUi();
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
        }

        void SpawnSoldier()
        {
            if (soldier != null) Destroy(soldier);
            soldier = new GameObject("LobbySoldier");
            soldier.transform.SetParent(stage, false); soldier.transform.localPosition = Vector3.up * 0.2f;
            soldier.transform.localRotation = Quaternion.Euler(0, soldierYaw, 0);
            lobbyBody = SoldierBody.Attach(soldier, Profile.Skin);
            lobbyBody.SetGun("rifle");
            lobbyBody.Play("idle_rifle");
            // test switch: ?pose=prone (or death, run, crouch_idle...) in the WebGL URL
            string url = Application.absoluteURL ?? ""; int pq = url.IndexOf("pose=");
            if (pq >= 0) { string clip = url.Substring(pq + 5); int amp = clip.IndexOf('&'); if (amp >= 0) clip = clip.Substring(0, amp); lobbyBody.Play(clip); }
        }

        // ------------------------------------------------------------------ UI from the captured web layout
        void BuildUi()
        {
            canvas = new GameObject("LobbyUI").AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var sc = canvas.gameObject.AddComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 1f;
            canvas.gameObject.AddComponent<GraphicRaycaster>();
            if (FindFirstObjectByType<EventSystem>() == null) { var es = new GameObject("EventSystem"); es.AddComponent<EventSystem>(); es.AddComponent<StandaloneInputModule>(); }

            L = JsonUtility.FromJson<LobbyLayout>(Resources.Load<TextAsset>("UI/Lobby/lobby_layout").text);
            s = 1080f / L.H;
            var sprites = new Dictionary<string, string> {
                { "logo", "Lobby/logo" }, { "banner", "Lobby/banner" }, { "chip0", "Lobby/chip" }, { "chip1", "Lobby/chip" }, { "gear", "Lobby/gear" },
                { "tab0", "Lobby/tab_on" }, { "tab1", "Lobby/tab" }, { "tab2", "Lobby/tab" }, { "tab3", "Lobby/tab" },
                { "mapcard", "Lobby/mapcard_" + (Profile.Map == "desert" || Profile.Map == "classic" ? Profile.Map : "green") }, { "season", "Lobby/season" }, { "tip", "Lobby/tip" },
                { "modes", "Lobby/modes" }, { "play", "Lobby/play" },
                { "rank", "icon_rank" }, { "chip0_icon", "icon_trophy" }, { "chip1_icon", "icon_fire" }, { "gear_icon", "icon_settings" },
                { "tab0_icon", "icon_home" }, { "tab1_icon", "icon_outfit" }, { "tab2_icon", "icon_map" }, { "tab3_icon", "icon_friends" },
                { "mapcard_pin", "icon_pin" }, { "season_icon", "icon_rank" }, { "tip_icon", "icon_chat" },
            };
            var (have, need) = Profile.LevelProgress;
            var dynamic = new Dictionary<string, string> {
                { "name", Profile.Name }, { "level", "LV " + Profile.Level }, { "server", "offline - play vs bots" },
                { "chip0_text", Profile.Wins.ToString() }, { "chip1_text", Profile.Kills.ToString() },
                { "mapcard_name", MapName(Profile.Map) },
                { "season_lv", $"Level {Profile.Level} · {Profile.Matches} matches" }, { "season_xp", $"{have} / {need}" },
                { "tip_text", Tips[UnityEngine.Random.Range(0, Tips.Length)] },
            };
            foreach (LobbyNode n in L.nodes)
            {
                if (n.kind == "text")
                {
                    var t = UiKit.Txt(canvas.transform, Vector2.zero, Vector2.zero, Vector2.one, 1, TextAnchor.MiddleLeft, ParseColor(n.color));
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
                    if (n.id == "avatar") sp = Resources.Load<Sprite>("UI/Outfits/" + SoldierBody.OutfitFiles[Mathf.Clamp(Profile.Skin, 0, SoldierBody.OutfitFiles.Length - 1)]);
                    if (sp == null && n.kind != "img") continue;
                    var img = UiKit.Img(canvas.transform, n.id, sp, Vector2.zero, Vector2.zero, Vector2.one, n.kind == "icon" ? ParseColor(n.color) : Color.white);
                    if (sp == null) img.color = Color.clear;
                    img.preserveAspect = n.id == "logo" || n.id == "avatar" || n.kind == "icon";
                    img.type = Image.Type.Simple;
                    Place(img.rectTransform, n);
                    nodes[n.id] = img.rectTransform;
                    if (n.id == "xpbar" || n.id == "season_bar") Fill(img.rectTransform, have / (float)need);
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
            // keep icons / labels above their panels
            foreach (var kv in texts) kv.Value.transform.SetAsLastSibling();
            foreach (var kv in nodes) if (kv.Key.EndsWith("_icon") || kv.Key == "rank" || kv.Key == "mapcard_pin") kv.Value.SetAsLastSibling();

            Tap("tab1", () => Open("outfit")); Tap("tab2", () => Open("map")); Tap("tab3", () => Open("friends"));
            Tap("gear", () => Open("settings")); Tap("mapcard", () => Open("map")); Tap("mode1", () => Open("friends"));
            Tap("play", Play);
            Tap("tab0", Close);
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
            rt.anchorMin = rt.anchorMax = new Vector2(ax, 1); rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2((n.x - ax * L.W) * s, -n.y * s);
            rt.sizeDelta = new Vector2(n.w * s, n.h * s);
        }

        void Fill(RectTransform track, float k)
        {
            var f = UiKit.Img(track, "fill", UiKit.Round, new Vector2(0, .5f), Vector2.zero, Vector2.zero, new Color(1f, .83f, .3f));
            f.rectTransform.anchorMin = new Vector2(0, 0); f.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(k), 1);
            f.rectTransform.offsetMin = f.rectTransform.offsetMax = Vector2.zero;
        }

        void Tap(string id, Action a)
        {
            if (!nodes.TryGetValue(id, out RectTransform rt)) return;
            var g = rt.GetComponent<Graphic>(); if (g != null) g.raycastTarget = true;
            Clickable.Add(rt.gameObject, a);
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
            for (int i = 0; i < 4; i++)
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
        }

        void Open(string what)
        {
            Close();
            SetTab(what == "outfit" ? 1 : what == "map" ? 2 : what == "friends" ? 3 : 0);
            shade = UiKit.Img(canvas.transform, "shade", null, new Vector2(.5f, .5f), Vector2.zero, new Vector2(1400, 1400), new Color(0, 0, 0, .25f), true).rectTransform;
            shade.pivot = new Vector2(.5f, .5f);
            Clickable.Add(shade.gameObject, () => Close());
            var bg = UiKit.Img(canvas.transform, "sheet", UiKit.Panel, Vector2.zero, Vector2.zero, Vector2.one, Color.white, true);
            sheet = bg.rectTransform;
            Place(sheet, new LobbyNode { x = L.sheet.x, y = L.sheet.y, w = L.sheet.w, h = L.sheet.h });
            // close button
            UiKit.CloseButton(sheet, new Vector2(1, 1), new Vector2(-10, -8) * (s / UiKit.K), 34 * (s / UiKit.K), () => Close());
            string title = what == "friends" ? "PLAY WITH FRIENDS" : what.ToUpper();
            var tt = UiKit.Txt(sheet, new Vector2(0, 1), new Vector2(16, -14) * (s / UiKit.K), new Vector2(300, 22) * (s / UiKit.K), 15 * s / UiKit.K, TextAnchor.UpperLeft, new Color(1f, .83f, .3f));
            tt.text = title;
            switch (what)
            {
                case "outfit": OutfitSheet(); break;
                case "map": MapSheet(); break;
                case "friends": Note("Playing with friends online is coming to the app soon.\nFor now: SOLO battle royale against 15 bots."); break;
                case "settings": SettingsSheet(); break;
            }
        }

        float k => s / UiKit.K; // CSS px -> UiKit units inside the sheet

        RectTransform ScrollArea()
        {
            var view = new GameObject("view", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(ScrollRect)).GetComponent<RectTransform>();
            view.SetParent(sheet, false); view.anchorMin = Vector2.zero; view.anchorMax = Vector2.one;
            view.offsetMin = new Vector2(14, 12) * s; view.offsetMax = new Vector2(-14, -44) * s;
            view.GetComponent<Image>().color = new Color(1, 1, 1, 0.003f); view.GetComponent<Mask>().showMaskGraphic = false;
            var content = new GameObject("content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(view, false); content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(.5f, 1);
            var sr = view.GetComponent<ScrollRect>(); sr.content = content; sr.horizontal = false; sr.movementType = ScrollRect.MovementType.Clamped;
            return content;
        }

        void OutfitSheet()
        {
            var content = ScrollArea();
            float cw = L.card.w * s, ch = L.card.h * s, gap = 8 * s;
            int cols = 3;
            for (int i = 0; i < SoldierBody.OutfitFiles.Length; i++)
            {
                int skin = i;
                bool on = i == Profile.Skin;
                var card = UiKit.Img(content, "card", UiKit.Get(on ? "Lobby/card_on" : "Lobby/card"), new Vector2(0, 1), Vector2.zero, Vector2.one, Color.white, true);
                card.rectTransform.anchoredPosition = new Vector2((i % cols) * (cw + gap), -(i / cols) * (ch + gap));
                card.rectTransform.sizeDelta = new Vector2(cw, ch);
                var pic = UiKit.Img(card.rectTransform, "pic", Resources.Load<Sprite>("UI/Outfits/" + SoldierBody.OutfitFiles[i]), new Vector2(.5f, 1), Vector2.zero, Vector2.one, Color.white);
                pic.preserveAspect = true; pic.rectTransform.pivot = new Vector2(.5f, 1);
                pic.rectTransform.anchoredPosition = new Vector2(0, -2 * s); pic.rectTransform.sizeDelta = new Vector2(cw, ch - 22 * s);
                var name = UiKit.Txt(card.rectTransform, new Vector2(.5f, 0), Vector2.zero, Vector2.one, 10.5f * k, TextAnchor.MiddleCenter, on ? new Color(27 / 255f, 20 / 255f, 4 / 255f) : Color.white);
                name.rectTransform.pivot = new Vector2(.5f, 0); name.rectTransform.sizeDelta = new Vector2(cw, 18 * s); name.text = SoldierBody.OutfitNames[i];
                if (on) { var ck = UiKit.Txt(card.rectTransform, new Vector2(1, 1), new Vector2(-4, -2) * k, new Vector2(20, 20) * k, 12 * k, TextAnchor.MiddleCenter, new Color(27 / 255f, 20 / 255f, 4 / 255f)); ck.rectTransform.pivot = new Vector2(1, 1); ck.text = ""; }
                Clickable.Add(card.gameObject, () => { Profile.Skin = skin; SpawnSoldier(); Open("outfit"); if (nodes.TryGetValue("avatar", out RectTransform av)) av.GetComponent<Image>().sprite = Resources.Load<Sprite>("UI/Outfits/" + SoldierBody.OutfitFiles[skin]); });
            }
            content.sizeDelta = new Vector2(0, Mathf.CeilToInt(SoldierBody.OutfitFiles.Length / (float)cols) * (ch + gap));
        }

        void MapSheet()
        {
            var content = ScrollArea();
            string[] ids = { "green", "desert", "classic" }; string[] names = { "Highlands", "Red Canyon", "Valley" };
            string[] desc = { "Real moorland scan · farm village", "Real desert scan · rock butte", "Pine valley · 25 houses" };
            float w = (L.sheet.w - 28) * s, h = 96 * s;
            for (int i = 0; i < ids.Length; i++)
            {
                string id = ids[i]; bool on = Profile.Map == id;
                var card = UiKit.Img(content, "map", UiKit.Get("Lobby/mapcard_" + id), new Vector2(.5f, 1), Vector2.zero, Vector2.one, on ? Color.white : new Color(.75f, .75f, .75f), true);
                card.rectTransform.pivot = new Vector2(.5f, 1); card.rectTransform.anchoredPosition = new Vector2(0, -i * (h + 10 * s)); card.rectTransform.sizeDelta = new Vector2(w, h);
                if (on) card.gameObject.AddComponent<Outline>().effectColor = new Color(1f, .83f, .3f);
                var t = UiKit.Txt(card.rectTransform, new Vector2(1, 0), new Vector2(-12, 30) * k, new Vector2(300, 28) * k, 17 * k, TextAnchor.LowerRight, Color.white); t.rectTransform.pivot = new Vector2(1, 0); t.text = names[i];
                var d = UiKit.Txt(card.rectTransform, new Vector2(1, 0), new Vector2(-12, 10) * k, new Vector2(300, 16) * k, 10 * k, TextAnchor.LowerRight, new Color(1, 1, 1, .85f)); d.rectTransform.pivot = new Vector2(1, 0); d.fontStyle = FontStyle.Normal; d.text = desc[i];
                Clickable.Add(card.gameObject, () => { if (Profile.Map == id) { Close(); return; } Profile.Map = id; Loader.Go("Loading " + MapName(id) + "…"); });
            }
            content.sizeDelta = new Vector2(0, ids.Length * (h + 10 * s));
        }

        void Note(string text)
        {
            var t = UiKit.Txt(sheet, new Vector2(.5f, .5f), Vector2.zero, new Vector2(L.sheet.w - 40, 120) * k, 13 * k, TextAnchor.MiddleCenter, Color.white);
            t.rectTransform.pivot = new Vector2(.5f, .5f); t.fontStyle = FontStyle.Normal; t.horizontalOverflow = HorizontalWrapMode.Wrap; t.text = text;
        }

        void SettingsSheet()
        {
            var content = ScrollArea();
            float y = 0, w = (L.sheet.w - 28) * s;
            Text Label(string txt) { var t = UiKit.Txt(content, new Vector2(0, 1), Vector2.zero, Vector2.one, 12 * k, TextAnchor.UpperLeft, new Color(.87f, .9f, .91f)); t.rectTransform.anchoredPosition = new Vector2(0, -y); t.rectTransform.sizeDelta = new Vector2(w, 16 * s); t.text = txt; y += 20 * s; return t; }
            // name
            Label("PLAYER NAME");
            var field = new GameObject("name", typeof(RectTransform), typeof(Image), typeof(InputField)).GetComponent<InputField>();
            var fr = (RectTransform)field.transform; fr.SetParent(content, false); fr.anchorMin = fr.anchorMax = fr.pivot = new Vector2(0, 1);
            fr.anchoredPosition = new Vector2(0, -y); fr.sizeDelta = new Vector2(w * .7f, 30 * s);
            field.GetComponent<Image>().color = new Color(.04f, .05f, .06f);
            var ft = UiKit.Txt(fr, new Vector2(0, .5f), new Vector2(8, 0) * k, new Vector2(w * .7f / s - 16, 24) * k, 13 * k, TextAnchor.MiddleLeft, Color.white); ft.supportRichText = false;
            field.textComponent = ft; field.characterLimit = 14; field.text = Profile.Name;
            field.onEndEdit.AddListener(v => { Profile.Name = v; if (texts.TryGetValue("name", out Text nt)) nt.text = Profile.Name; });
            y += 40 * s;
            // sensitivity
            var sens = Label($"LOOK SENSITIVITY  {Profile.Sensitivity:0.0}");
            Slider(content, ref y, w, 0.3f, 2.5f, Profile.Sensitivity, v => { Profile.Sensitivity = v; sens.text = $"LOOK SENSITIVITY  {v:0.0}"; });
            var ssens = Label($"SCOPE SENSITIVITY  {Profile.ScopeSensitivity:0.0}");
            Slider(content, ref y, w, 0.3f, 2.5f, Profile.ScopeSensitivity, v => { Profile.ScopeSensitivity = v; ssens.text = $"SCOPE SENSITIVITY  {v:0.0}"; });
            // aim assist
            Label("AIM");
            Choice(content, ref y, w, new[] { "ASSIST", "FULL CONTROL" }, Profile.AimAssist ? 0 : 1, i => Profile.AimAssist = i == 0);
            Label("GRAPHICS");
            Choice(content, ref y, w, new[] { "LOW", "MEDIUM", "HIGH" }, Profile.Quality, i => Profile.Quality = i);
            // move / resize / fade every in-match control (the same editor as in the match)
            Label("CONTROLS");
            var cb = UiKit.Img(content, "custom", UiKit.Round, new Vector2(0, 1), Vector2.zero, Vector2.one, new Color(.23f, .26f, .28f), true);
            cb.rectTransform.anchoredPosition = new Vector2(0, -y); cb.rectTransform.sizeDelta = new Vector2(w * .9f, 32 * s);
            var cbt = UiKit.Txt(cb.rectTransform, new Vector2(.5f, .5f), Vector2.zero, Vector2.one, 12 * k, TextAnchor.MiddleCenter, new Color(1f, .83f, .3f));
            cbt.rectTransform.pivot = new Vector2(.5f, .5f); cbt.rectTransform.sizeDelta = new Vector2(w * .9f, 32 * s); cbt.text = "CUSTOMIZE BUTTONS";
            Clickable.Add(cb.gameObject, CustomizeButtons);
            y += 42 * s;
            content.sizeDelta = new Vector2(0, y + 10 * s);
        }

        /// <summary>the match controls laid over the lobby, with stand-ins for the weapon box and the minimap, in the layout editor</summary>
        void CustomizeButtons()
        {
            Close();
            canvas.enabled = false;
            TouchControls tc = TouchControls.Create();
            tc.Preview = true;
            RectTransform Stand(string label, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
            {
                var img = UiKit.Img(tc.Canvas.transform, label, UiKit.Round, anchor, pos, size, new Color(24 / 255f, 32 / 255f, 40 / 255f, .7f), true);
                img.rectTransform.pivot = pivot;
                var t = UiKit.Txt(img.rectTransform, new Vector2(.5f, .5f), Vector2.zero, size, 12, TextAnchor.MiddleCenter, Color.white); t.rectTransform.pivot = new Vector2(.5f, .5f); t.text = label;
                return img.rectTransform;
            }
            tc.AddEditable("weapons", Stand("WEAPONS", new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 35), new Vector2(209, 85)));
            tc.AddEditable("minimap", Stand("MAP", new Vector2(1, 1), new Vector2(1, 1), new Vector2(-8, -6), new Vector2(132, 132)));
            tc.AddEditable("fps", Stand("60 fps · ping 0 ms", new Vector2(0, 1), new Vector2(0, 1), new Vector2(16, -10) / UiKit.K, new Vector2(330, 30) / UiKit.K));
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
        public static string MapName(string id) => id == "desert" ? "Red Canyon" : id == "classic" ? "Valley" : "Highlands";

        void Play()
        {

            GameBoot.InMatch = true;
            Loader.Go("Loading " + MapName(Profile.Map) + "…");
        }

        void Update()
        {
            // (a sheet or the button editor is open: their drags belong to them, the soldier stays put)
            if (sheet != null || soldier == null || !canvas.enabled) { dragFinger = -1; mouseDrag = false; return; }
            // drag anywhere in the middle of the screen to turn the soldier
            if (Input.touchCount > 0)
            {
                foreach (Touch t in Input.touches)
                {
                    if (t.phase == TouchPhase.Began && dragFinger < 0 && t.position.x > Screen.width * .3f && t.position.x < Screen.width * .68f && !OverUi(t.fingerId)) { dragFinger = t.fingerId; dragLast = t.position; }
                    if (t.fingerId != dragFinger) continue;
                    soldierYaw -= (t.position.x - dragLast.x) * 0.4f; dragLast = t.position;
                    if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled) dragFinger = -1;
                }
            }
            else
            {
                dragFinger = -1;
                if (Input.GetMouseButtonDown(0) && Input.mousePosition.x > Screen.width * .3f && Input.mousePosition.x < Screen.width * .68f && !OverUi(-1)) { mouseDrag = true; dragLast = Input.mousePosition; }
                if (Input.GetMouseButtonUp(0)) mouseDrag = false;
                if (mouseDrag) { soldierYaw -= (Input.mousePosition.x - dragLast.x) * 0.4f; dragLast = Input.mousePosition; }
            }
            soldier.transform.localRotation = Quaternion.Euler(0, soldierYaw, 0);
            if (lobbyBody != null) lobbyBody.AimDir = soldier.transform.forward;
        }

        static bool OverUi(int finger)
        {
            var es = EventSystem.current;
            return es != null && (finger >= 0 ? es.IsPointerOverGameObject(finger) : es.IsPointerOverGameObject());
        }
    }
}
