using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LastWar
{
    /// <summary>
    /// In-match HUD with the web game's look (sizes in CSS px x UiKit.K): weapon box (current gun big with ammo
    /// and gun silhouette, the others small below; tap = equip, tap the current one = reload), armour + health
    /// bars, ALIVE / KILLS box and zone banner on top, kill feed top-left, skull-on-blood kill banner, crosshair
    /// dot that turns red on an enemy, hit ring, damage flash, end screen.
    /// </summary>
    public class Hud : MonoBehaviour
    {
        class Slot { public RectTransform rt; public Image bg, gun, line; public Text ammo; }
        TouchControls input; PlayerController player;
        readonly Slot[] slots = new Slot[4]; // 3 guns + the fist (empty hands)
        RectTransform box, hpBgRt, arBgRt, lens; Image lensDot; RawImage glass; readonly Image[] sides = new Image[2]; int lensZoom; Transform full;
        static Texture2D lensCross, lensPlain;
        RectTransform holo; RawImage holoFrame; Image holoRing;

        /// <summary>holo sight frame: thick dark rounded rectangle (18 % / 24 % corners), light inner edge, faint blue glass</summary>
        static Texture2D HoloFrame()
        {
            const int W = 460, H = 340; var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[W * H]; float rx = W * 0.18f, ry = H * 0.24f;
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
                {
                    // distance inside the rounded rectangle (elliptical corners), in pixels
                    float cx = Mathf.Clamp(x + .5f, rx, W - rx), cy = Mathf.Clamp(y + .5f, ry, H - ry);
                    float ex = (x + .5f - cx) / rx, ey = (y + .5f - cy) / ry, e = Mathf.Sqrt(ex * ex + ey * ey);
                    float d = e > 0 ? (1f - e) * Mathf.Min(rx, ry) : Mathf.Min(Mathf.Min(x, W - 1 - x), Mathf.Min(y, H - 1 - y));
                    Color c;
                    if (d < 0) c = new Color(0, 0, 0, 0);
                    else if (d < 2) c = new Color(.05f, .05f, .06f, 1);            // outer dark line
                    else if (d < 16) c = new Color(.14f, .15f, .16f, 1);           // body of the housing
                    else if (d < 20) c = new Color(.29f, .31f, .33f, 1);           // light inner edge
                    else c = new Color(.62f, .8f, 1f, Mathf.Lerp(.12f, .04f, Mathf.Clamp01((d - 20) / 60f))); // tinted glass
                    px[y * W + x] = c;
                }
            tex.SetPixels32(px); tex.Apply();
            return tex;
        }

        /// <summary>edges of the screen darkened toward the corners (the holo sight's surround)</summary>
        static Texture2D Vignette()
        {
            const int S = 128; var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
                {
                    float dx = (x - S / 2f) / (S / 2f), dy = (y - S / 2f) / (S / 2f), d = Mathf.Sqrt(dx * dx + dy * dy);
                    tex.SetPixel(x, y, new Color(0, 0, 0, Mathf.Clamp01((d - 0.4f) / 0.35f) * 0.28f));
                }
            tex.Apply();
            return tex;
        }
        Image hpFill, armorFill, dot, hitRing, flash, reloadFill; RectTransform reloadBar;
        Text hpText, aliveText, zoneText, centerText, bannerCount; Image centerBox;
        Image aliveBox, zoneBox; RectTransform banner;
        readonly List<Image> feedBg = new List<Image>(); readonly List<Text> feedTxt = new List<Text>();
        readonly List<(string text, bool mine, float t)> feed = new List<(string, bool, float)>();
        RectTransform endPanel; Text endTitle, endSub;
        float hitT, flashT, bannerT; bool hitKill;
        string hintText = ""; float hintAt;
        class Floater { public Text t; public Vector3 pos; public float life; }
        readonly List<Floater> floaters = new List<Floater>();
        readonly List<(Text t, float life)> taken = new List<(Text, float)>();
        const float BarW = 130f; // health / armour bar length (CSS px): about half the old one
        static readonly Vector2 BC = new Vector2(.5f, 0), TC = new Vector2(.5f, 1), C = new Vector2(.5f, .5f);

        public static Hud Create(TouchControls input, PlayerController player)
        {
            var h = new GameObject("Hud").AddComponent<Hud>();
            h.input = input; h.player = player; h.Build();
            Fighter.OnHit += h.Hit; Fighter.OnKill += h.Kill;
            return h;
        }

        void Build()
        {
            Transform c = input.Root; full = input.Canvas.transform; // controls on the safe area, full-screen effects on the canvas
            flash = UiKit.Img(full, "Flash", null, C, Vector2.zero, new Vector2(1400, 1400), new Color(0.8f, 0, 0, 0));
            flash.rectTransform.pivot = C; flash.transform.SetAsFirstSibling();

            // ---- bottom centre: bars (bottom 6), reload bar, weapon box
            RectTransform hpBg = hpBgRt = UiKit.Img(c, "HpBg", UiKit.Round, BC, new Vector2(0, 6), new Vector2(BarW, 10), new Color(0, 0, 0, .45f)).rectTransform;
            hpFill = UiKit.Img(hpBg, "Hp", UiKit.Round, new Vector2(0, .5f), Vector2.zero, new Vector2(BarW, 10), new Color(0.48f, 0.85f, 0.32f));
            hpText = UiKit.Txt(hpBg, new Vector2(1, .5f), Vector2.zero, new Vector2(30, 14), 12, TextAnchor.MiddleLeft, Color.white);
            hpText.rectTransform.pivot = new Vector2(0, .5f); hpText.rectTransform.anchoredPosition = new Vector2(5 * UiKit.K, 0);
            RectTransform arBg = arBgRt = UiKit.Img(c, "ArBg", UiKit.Round, BC, new Vector2(0, 18), new Vector2(BarW, 7), new Color(0, 0, 0, .45f)).rectTransform;
            armorFill = UiKit.Img(arBg, "Ar", UiKit.Round, new Vector2(0, .5f), Vector2.zero, new Vector2(BarW, 7), new Color(0.35f, 0.67f, 1f));
            reloadBar = UiKit.Img(c, "Reload", UiKit.Round, BC, new Vector2(0, 28), new Vector2(140, 4), new Color(0, 0, 0, .4f)).rectTransform;
            reloadFill = UiKit.Img(reloadBar, "fill", UiKit.Round, new Vector2(0, .5f), Vector2.zero, new Vector2(0, 4), new Color(1f, 0.83f, 0.3f));

            // weapon box: one piece the layout editor can move (current gun on top, the others + the fist below)
            box = new GameObject("WeaponBox", typeof(RectTransform)).GetComponent<RectTransform>();
            box.SetParent(c, false); box.anchorMin = box.anchorMax = BC; box.pivot = new Vector2(.5f, 0);
            box.anchoredPosition = new Vector2(0, 50) * UiKit.K; // clear of the status row box.sizeDelta = new Vector2(209, 85) * UiKit.K;
            for (int k = 0; k < 4; k++)
            {
                int slot = k;
                var s = new Slot();
                s.bg = UiKit.Img(box, "Slot" + k, UiKit.Round, BC, Vector2.zero, new Vector2(50, 36), new Color(24 / 255f, 32 / 255f, 40 / 255f, .6f), true);
                s.rt = s.bg.rectTransform; s.rt.pivot = new Vector2(.5f, 0);
                s.gun = UiKit.Img(s.rt, k == 3 ? "fist" : "gun", k == 3 ? UiKit.Get("icon_fist") : null, new Vector2(1, 1), Vector2.zero, new Vector2(40, 19), Color.white);
                s.gun.preserveAspect = true;
                s.ammo = UiKit.Txt(s.rt, Vector2.zero, Vector2.zero, new Vector2(80, 20), 10, TextAnchor.LowerLeft, Color.white);
                s.line = UiKit.Img(s.rt, "line", null, Vector2.zero, Vector2.zero, new Vector2(40, 2), new Color(1, 1, 1, .75f));
                var trigger = s.bg.gameObject.AddComponent<EventTrigger>();
                var e = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
                e.callback.AddListener(_ => { if (input.Editing) return; if (slot == 3) input.HolsterPressed = true; else input.SlotPressed = slot; });
                trigger.triggers.Add(e);
                input.RegisterBlocker(s.rt);
                slots[k] = s;
            }
            input.AddEditable("weapons", box);

            // ---- top centre: ALIVE / KILLS box and the zone banner under it
            aliveBox = UiKit.Img(c, "Alive", UiKit.Round, TC, new Vector2(0, -6), new Vector2(160, 26), new Color(0, 0, 0, .4f));
            aliveText = UiKit.Txt(aliveBox.rectTransform, C, Vector2.zero, new Vector2(300, 26), 16, TextAnchor.MiddleCenter, Color.white);
            aliveText.rectTransform.pivot = C;
            zoneBox = UiKit.Img(c, "ZoneTxt", UiKit.Round, TC, new Vector2(0, -36), new Vector2(200, 18), new Color(20 / 255f, 60 / 255f, 140 / 255f, .5f));
            zoneText = UiKit.Txt(zoneBox.rectTransform, C, Vector2.zero, new Vector2(400, 18), 11, TextAnchor.MiddleCenter, Color.white);
            zoneText.rectTransform.pivot = C;
            centerBox = UiKit.Img(c, "HintBox", UiKit.Round, C, new Vector2(0, -30), new Vector2(260, 40), new Color(0, 0, 0, .55f));
            centerBox.rectTransform.pivot = C;
            centerText = UiKit.Txt(centerBox.rectTransform, C, Vector2.zero, new Vector2(236, 20), 12, TextAnchor.MiddleCenter, new Color(1f, 0.42f, 0.36f));
            centerText.rectTransform.pivot = C; centerText.horizontalOverflow = HorizontalWrapMode.Wrap;

            // ---- kill feed, top-left under the buttons row
            for (int k = 0; k < 5; k++)
            {
                var bg = UiKit.Img(c, "Feed" + k, UiKit.Round, new Vector2(0, 1), new Vector2(8, -54 - k * 20), new Vector2(100, 17), new Color(0, 0, 0, .45f));
                var t = UiKit.Txt(bg.rectTransform, new Vector2(0, .5f), new Vector2(8, 0), new Vector2(300, 17), 11, TextAnchor.MiddleLeft, Color.white);
                feedBg.Add(bg); feedTxt.Add(t); bg.gameObject.SetActive(false);
            }

            // ---- kill banner: white skull on a blood splash, kill count bottom-right
            banner = UiKit.Img(c, "KillBanner", null, TC, new Vector2(0, -84), new Vector2(84, 72), Color.clear).rectTransform;
            banner.pivot = new Vector2(.5f, .5f); banner.anchoredPosition = new Vector2(0, -120) * UiKit.K;
            var blood = UiKit.Img(banner, "blood", UiKit.Get("blood"), C, Vector2.zero, new Vector2(96, 84), Color.white); blood.rectTransform.pivot = C;
            var skull = UiKit.Img(banner, "skull", UiKit.Get("icon_skull"), C, Vector2.zero, new Vector2(40, 40), Color.white); skull.rectTransform.pivot = C;
            bannerCount = UiKit.Txt(banner, new Vector2(1, 0), new Vector2(-2, 2), new Vector2(40, 20), 18, TextAnchor.LowerRight, Color.white);
            banner.gameObject.SetActive(false);

            // ---- crosshair: 6 px dot with a dark edge; ring on hits (red on a kill)
            // the crosshair sits at the exact screen centre (where the bullets go), not the centre of the safe area:
            // with a camera notch on one side the safe area is off-centre and the dot was beside the real aim
            var edge = UiKit.Img(full, "DotEdge", UiKit.Disc, C, Vector2.zero, new Vector2(9, 9), new Color(0, 0, 0, .55f)); edge.rectTransform.pivot = C;
            dot = UiKit.Img(edge.rectTransform, "Dot", UiKit.Disc, C, Vector2.zero, new Vector2(6, 6), Color.white); dot.rectTransform.pivot = C;
            hitRing = UiKit.Img(full, "HitRing", UiKit.Ring, C, Vector2.zero, new Vector2(20, 20), new Color(1, 1, 1, 0)); hitRing.rectTransform.pivot = C;

            // ---- optics (web .scope-overlay): round lens, dark rim, the world outside dimmed (x2 / x4) or black (x8)
            lens = new GameObject("Scope", typeof(RectTransform)).GetComponent<RectTransform>();
            lens.SetParent(full, false); lens.anchorMin = Vector2.zero; lens.anchorMax = Vector2.one; lens.offsetMin = lens.offsetMax = Vector2.zero;
            lens.SetAsFirstSibling();
            glass = new GameObject("lens", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            glass.transform.SetParent(lens, false); glass.raycastTarget = false;
            glass.rectTransform.anchorMin = glass.rectTransform.anchorMax = glass.rectTransform.pivot = C;
            for (int i = 0; i < 2; i++)
            {
                var blk = UiKit.Img(lens, "side", null, C, Vector2.zero, Vector2.one, Color.black);
                blk.rectTransform.pivot = new Vector2(i == 0 ? 1 : 0, .5f);
                sides[i] = blk;
            }
            // the rifle's own red-dot sight (web .reddot-overlay): a rounded-rect holo frame, a dot and a ring, the edges darkened
            holo = new GameObject("holo", typeof(RectTransform)).GetComponent<RectTransform>();
            holo.SetParent(lens, false); holo.anchorMin = Vector2.zero; holo.anchorMax = Vector2.one; holo.offsetMin = holo.offsetMax = Vector2.zero;
            var vig = new GameObject("vignette", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            vig.transform.SetParent(holo, false); vig.texture = Vignette(); vig.raycastTarget = false; UiKit.Fill(vig.rectTransform);
            holoFrame = new GameObject("frame", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            holoFrame.transform.SetParent(holo, false); holoFrame.texture = HoloFrame(); holoFrame.raycastTarget = false;
            holoFrame.rectTransform.anchorMin = holoFrame.rectTransform.anchorMax = holoFrame.rectTransform.pivot = C;
            holoRing = UiKit.Img(holo, "ring", UiKit.Ring, C, Vector2.zero, new Vector2(26, 26), new Color(1, 1, 1, .55f)); holoRing.rectTransform.pivot = C;
            lensDot = UiKit.Img(lens, "dot", UiKit.Disc, C, Vector2.zero, new Vector2(3.5f, 3.5f), new Color(.22f, 1f, .42f)); lensDot.rectTransform.pivot = C;
            lens.gameObject.SetActive(false);

            // ---- end screen
            endPanel = UiKit.Img(full, "End", null, C, Vector2.zero, new Vector2(1400, 1400), new Color(0, 0, 0, .6f), true).rectTransform;
            endPanel.pivot = C;
            var card = UiKit.Img(endPanel, "card", UiKit.Round, C, Vector2.zero, new Vector2(300, 170), new Color(0.09f, 0.11f, 0.13f, .95f)); card.rectTransform.pivot = C;
            card.gameObject.AddComponent<Outline>().effectColor = new Color(1f, 0.83f, 0.3f, .45f);
            endTitle = UiKit.Txt(card.rectTransform, C, new Vector2(0, 45), new Vector2(300, 40), 30, TextAnchor.MiddleCenter, Color.white); endTitle.rectTransform.pivot = C;
            endSub = UiKit.Txt(card.rectTransform, C, new Vector2(0, 5), new Vector2(300, 20), 14, TextAnchor.MiddleCenter, Color.white); endSub.rectTransform.pivot = C;
            // eliminated: watch the one who got you (web SPECTATE)
            spectateBtn = UiKit.Img(card.rectTransform, "Spectate", UiKit.Round, C, new Vector2(70, -45), new Vector2(120, 34), new Color(.23f, .26f, .28f), true);
            spectateBtn.rectTransform.pivot = C;
            var sbt = UiKit.Txt(spectateBtn.rectTransform, C, Vector2.zero, new Vector2(120, 34), 14, TextAnchor.MiddleCenter, Color.white); sbt.rectTransform.pivot = C; sbt.text = "SPECTATE";
            spectateBtn.gameObject.AddComponent<Button>().onClick.AddListener(() => { endPanel.gameObject.SetActive(false); spectating = true; player.StartSpectate(lastKiller); });
            var again = UiKit.Img(card.rectTransform, "Again", UiKit.Round, C, new Vector2(0, -45), new Vector2(150, 34), new Color(1f, 0.78f, 0.25f), true);
            again.rectTransform.pivot = C;
            var at = UiKit.Txt(again.rectTransform, C, Vector2.zero, new Vector2(150, 34), 14, TextAnchor.MiddleCenter, new Color(0.1f, 0.08f, 0.02f)); at.rectTransform.pivot = C;
            at.text = "LOBBY"; Destroy(at.GetComponent<Shadow>());
            again.gameObject.AddComponent<Button>().onClick.AddListener(() => GameBoot.Instance.Restart());
            endPanel.gameObject.SetActive(false);

            // spectating: who you watch + back to the lobby, a card at the bottom over the health bar (web .spec-banner)
            specBar = UiKit.Img(c, "SpecBar", UiKit.Panel, BC, new Vector2(0, 40), new Vector2(240, 58), Color.white, true).rectTransform;
            specBar.pivot = new Vector2(.5f, 0);
            specText = UiKit.Txt(specBar, new Vector2(.5f, 1), new Vector2(0, -6), new Vector2(230, 22), 16, TextAnchor.UpperCenter, new Color(1f, .83f, .3f));
            specText.rectTransform.pivot = new Vector2(.5f, 1);
            var sl = UiKit.Img(specBar, "lobby", UiKit.Get("Lobby/mode_on"), new Vector2(.5f, 0), new Vector2(0, 6), new Vector2(110, 22), Color.white, true);
            sl.rectTransform.pivot = new Vector2(.5f, 0);
            var slt = UiKit.Txt(sl.rectTransform, C, Vector2.zero, new Vector2(110, 22), 11, TextAnchor.MiddleCenter, new Color(0.1f, 0.08f, 0.02f)); slt.rectTransform.pivot = C; slt.text = "LOBBY"; Destroy(slt.GetComponent<Shadow>());
            sl.gameObject.AddComponent<Button>().onClick.AddListener(() => GameBoot.Instance.Restart());
            input.RegisterBlocker(specBar);
            specBar.gameObject.SetActive(false);

            // armour + backpack level above the health bar (web .status-row)
            status = new GameObject("Status", typeof(RectTransform)).GetComponent<RectTransform>();
            status.SetParent(c, false); status.anchorMin = status.anchorMax = BC; status.pivot = new Vector2(0, 0);
            status.anchoredPosition = new Vector2(-BarW / 2f, 27) * UiKit.K; status.sizeDelta = new Vector2(160, 18) * UiKit.K;
            vestIcon = UiKit.Img(status, "vest", UiKit.Get("icon_vest"), new Vector2(0, .5f), Vector2.zero, new Vector2(17, 17), Color.white); vestIcon.rectTransform.pivot = new Vector2(0, .5f);
            vestText = UiKit.Txt(status, new Vector2(0, .5f), new Vector2(20, 0), new Vector2(40, 18), 11, TextAnchor.MiddleLeft, Color.white);
            var bagIc = UiKit.Img(status, "bag", UiKit.Get("icon_bag"), new Vector2(0, .5f), new Vector2(58, 0), new Vector2(17, 17), new Color(1f, .83f, .3f)); bagIc.rectTransform.pivot = new Vector2(0, .5f);
            var bagTx = UiKit.Txt(status, new Vector2(0, .5f), new Vector2(78, 0), new Vector2(40, 18), 11, TextAnchor.MiddleLeft, new Color(1f, .83f, .3f)); bagTx.text = "Lv1";

            // things within reach (web .loot-list): a see-through panel to the left of the fire button, picture, name, TAKE
            lootPanel = UiKit.Img(c, "LootList", UiKit.Round, new Vector2(1, .56f), new Vector2(-232, 0), new Vector2(172, 40), new Color(10 / 255f, 18 / 255f, 10 / 255f, .42f), true).rectTransform;
            lootPanel.pivot = new Vector2(1, .5f);
            lootPanel.gameObject.AddComponent<Outline>().effectColor = new Color(1, 1, 1, .14f);
            input.RegisterBlocker(lootPanel);
            for (int i = 0; i < 6; i++)
            {
                int row = i;
                var bg = UiKit.Img(lootPanel, "row" + i, null, new Vector2(.5f, 1), new Vector2(0, -4 - i * 27), new Vector2(164, 26), new Color(1, 1, 1, 0), true);
                bg.rectTransform.pivot = new Vector2(.5f, 1);
                var line = UiKit.Img(bg.rectTransform, "line", null, new Vector2(.5f, 0), Vector2.zero, new Vector2(164, 1), new Color(1, 1, 1, .06f)); line.rectTransform.pivot = new Vector2(.5f, 0);
                var ic = UiKit.Img(bg.rectTransform, "icon", null, new Vector2(0, .5f), new Vector2(4, 0), new Vector2(42, 16), Color.white); ic.preserveAspect = true; ic.rectTransform.pivot = new Vector2(0, .5f);
                var tx = UiKit.Txt(bg.rectTransform, new Vector2(0, .5f), new Vector2(52, 0), new Vector2(76, 26), 11, TextAnchor.MiddleLeft, Color.white);
                var tk = UiKit.Img(bg.rectTransform, "take", UiKit.Round, new Vector2(1, .5f), new Vector2(-3, 0), new Vector2(38, 18), new Color(.45f, .78f, .25f), true);
                tk.rectTransform.pivot = new Vector2(1, .5f);
                var tt = UiKit.Txt(tk.rectTransform, C, Vector2.zero, new Vector2(38, 18), 10, TextAnchor.MiddleCenter, new Color(.06f, .13f, .03f)); tt.rectTransform.pivot = C; tt.text = "TAKE"; Destroy(tt.GetComponent<Shadow>());
                var trg = bg.gameObject.AddComponent<EventTrigger>(); var en = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
                en.callback.AddListener(_ => { if (row < lootRows.Count && lootRows[row].item != null) player.Take(lootRows[row].item); });
                trg.triggers.Add(en);
                lootRows.Add((bg, ic, tx, null));
                bg.gameObject.SetActive(false);
            }
            input.AddEditable("loot", lootPanel);

            healRing = UiKit.Img(full, "HealRing", ThickRing(), C, Vector2.zero, new Vector2(56, 56), new Color(0, 0, 0, .45f)).rectTransform;
            healRing.pivot = C;
            healFg = UiKit.Img(healRing, "fg", ThickRing(), C, Vector2.zero, new Vector2(56, 56), new Color(.42f, .9f, .35f));
            healFg.rectTransform.pivot = C; healFg.type = Image.Type.Filled; healFg.fillMethod = Image.FillMethod.Radial360;
            healFg.fillOrigin = (int)Image.Origin360.Top; healFg.fillClockwise = true; healFg.fillAmount = 0;
            var plus = UiKit.Txt(healRing, C, Vector2.zero, new Vector2(56, 56), 22, TextAnchor.MiddleCenter, new Color(.42f, .9f, .35f)); plus.rectTransform.pivot = C; plus.text = "+";
            healRing.gameObject.SetActive(false);
            input.AddEditable("status", status);
        }
        RectTransform status, lootPanel, healRing; Image vestIcon, healFg; Text vestText;
        static Sprite thickRing;
        static Sprite ThickRing()
        {
            if (thickRing != null) return thickRing;
            const int S = 128; var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(64, 64));
                    float a = Mathf.Clamp01((63f - d) * 1.5f) * Mathf.Clamp01((d - 49f) * 1.5f);
                    px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            tex.SetPixels32(px); tex.Apply();
            return thickRing = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(.5f, .5f));
        }
        RectTransform specBar; Text specText; Image spectateBtn; bool spectating; Fighter lastKiller;
        readonly System.Collections.Generic.List<(Image bg, Image icon, Text text, Loot item)> lootRows = new System.Collections.Generic.List<(Image, Image, Text, Loot)>();

        /// <summary>scope picture: clear glass, dark rim with an inner shadow; x8 has a fine cross with heavy outer posts</summary>
        static Texture2D LensTexture(bool cross, float outside)
        {
            const int n = 512; var tex = new Texture2D(n, n, TextureFormat.RGBA32, false); tex.wrapMode = TextureWrapMode.Clamp;
            var px = new Color32[n * n]; float c0 = (n - 1) / 2f, R = n * 0.47f;
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
                {
                    float dx = x - c0, dy = y - c0, d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = d >= R + 3 ? outside : d > R - 10 ? Mathf.Lerp(0.85f, 1f, (d - (R - 10)) / 13f) : d > R - 46 ? Mathf.Pow((d - (R - 46)) / 36f, 2f) * 0.8f : 0f; // rim + inner shadow
                    if (cross)
                    {
                        bool fine = d < R - 8 && d > 5 && (Mathf.Abs(dx) < 0.8f || Mathf.Abs(dy) < 0.8f);
                        bool thick = d < R - 8 && d > R * 0.42f && (Mathf.Abs(dx) < 2.2f || Mathf.Abs(dy) < 2.2f);
                        if (fine || thick) a = Mathf.Max(a, 0.9f);
                    }
                    px[y * n + x] = new Color(0.02f, 0.03f, 0.02f, a);
                }
            tex.SetPixels32(px); tex.Apply();
            return tex;
        }

        /// <summary>size and look of the optic for its zoom (web: x2 72 %, x4 84 %, x8 94 % of the screen height)</summary>
        void SetLens(int zoom)
        {
            if (lensCross == null) { lensCross = LensTexture(true, 1f); lensPlain = LensTexture(false, 0.45f); }
            lensZoom = zoom;
            float H = ((RectTransform)full).rect.height, L = H * (zoom >= 8 ? 0.94f : zoom >= 4 ? 0.84f : 0.72f);
            bool red = zoom <= 1;
            holo.gameObject.SetActive(red); glass.gameObject.SetActive(!red); sides[0].gameObject.SetActive(!red); sides[1].gameObject.SetActive(!red);
            if (red)
            {
                holoFrame.rectTransform.sizeDelta = new Vector2(H * 0.46f, H * 0.34f);
                lensDot.rectTransform.sizeDelta = Vector2.one * 8f * UiKit.K;
                return;
            }
            glass.texture = zoom >= 8 ? lensCross : lensPlain;
            glass.rectTransform.sizeDelta = new Vector2(L, L);
            Color outside = zoom >= 8 ? Color.black : new Color(0.02f, 0.03f, 0.02f, zoom >= 4 ? 0.5f : 0.42f);
            for (int i = 0; i < 2; i++)
            {
                sides[i].color = outside;
                sides[i].rectTransform.anchoredPosition = new Vector2((i == 0 ? -1 : 1) * (L / 2f - 1f), 0);
                sides[i].rectTransform.sizeDelta = new Vector2(4000, H + 20);
            }
            lensDot.rectTransform.sizeDelta = Vector2.one * (zoom >= 8 ? 3.5f : 6f) * UiKit.K;
        }

        void Hit(Fighter victim, Fighter attacker, float dmg, bool head, Vector3 point)
        {
            if (attacker != null && attacker == player.fighter && victim != attacker)
            {
                hitT = 0.3f; hitKill = !victim.Alive || head;
                // floating damage number (web .dmg: white 18 px, head red 26 px, armour blue)
                bool armor = victim.armor > 0;
                var t = UiKit.Txt(input.Canvas.transform, C, Vector2.zero, new Vector2(60, 30), head ? 26 : 18, TextAnchor.MiddleCenter,
                    head ? new Color(1f, 0.17f, 0.17f) : armor ? new Color(0.55f, 0.78f, 1f) : Color.white);
                t.rectTransform.pivot = C; t.text = Mathf.RoundToInt(dmg).ToString();
                t.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, .6f);
                floaters.Add(new Floater { t = t, pos = point + new Vector3(Random.Range(-.2f, .2f), .3f, 0), life = 0.9f });
            }
            if (victim == player.fighter)
            {
                flashT = Mathf.Min(0.5f, flashT + dmg / 60f);
                // damage you take: red number rising from the health bar (web .dmg-taken)
                var t = UiKit.Txt(input.Canvas.transform, BC, new Vector2(Random.Range(-30f, 30f), 24), new Vector2(60, 24), 20, TextAnchor.MiddleCenter, new Color(1f, .23f, .19f));
                t.rectTransform.pivot = new Vector2(.5f, 0); t.text = "-" + Mathf.RoundToInt(dmg);
                taken.Add((t, 1.1f));
            }
        }

        void Kill(Fighter victim, Fighter killer, string weapon, bool head)
        {
            if (victim == player.fighter) lastKiller = killer;
            WeaponDef w = Tuning.Weapon(weapon);
            string how = weapon == "zone" ? "zone" : w != null ? w.name : weapon;
            bool mine = killer == player.fighter || victim == player.fighter;
            string line = killer != null && killer != victim
                ? $"{killer.displayName}<color=#ffd38a>  [{how}{(head ? " · HEAD" : "")}]  </color>{victim.displayName}"
                : $"{victim.displayName}<color=#ffd38a>  [{how}]</color>";
            feed.Insert(0, (line, mine, Time.time));
            if (feed.Count > 5) feed.RemoveAt(5);
            if (killer == player.fighter && victim != killer)
            {
                bannerT = 2.2f; banner.gameObject.SetActive(true); bannerCount.text = player.fighter.kills.ToString();
                hitKill = true; hitT = 0.4f;
            }
        }

        void Update()
        {
            // spectating: the bars, weapon box and status show the one you watch
            Fighter f = spectating && player.SpectateTarget != null ? player.SpectateTarget : player.fighter; float dt = Time.deltaTime; const float K = UiKit.K; Camera cam = player.cam;
            input.Spectating = spectating;
            vestText.text = f.armor > 0 ? Mathf.RoundToInt(f.armor).ToString() : "-";
            vestText.color = f.armor > 0 ? new Color(.44f, .71f, 1f) : new Color(1, 1, 1, .45f); vestIcon.color = f.armor > 0 ? new Color(.44f, .71f, 1f) : new Color(1, 1, 1, .45f);
            hpFill.rectTransform.sizeDelta = new Vector2(BarW * f.hp / Fighter.MaxHp, 10) * K;
            hpFill.color = f.hp > Fighter.MaxHp * 0.3f ? new Color(0.48f, 0.85f, 0.32f) : new Color(0.94f, 0.36f, 0.3f);
            armorFill.rectTransform.sizeDelta = new Vector2(BarW * f.armor / 100f, 7) * K;
            hpText.text = Mathf.CeilToInt(f.hp).ToString();

            // weapon box (web .slots grid): what is in your hands 209 x 46 on top, the other three 67 x 36 below
            int small = 0;
            bool emptyHands = f.holstered || f.Gun == null;
            int[] seq = { f.order[0], f.order[1], f.order[2], 3 }; // the backpack's order, the fist last
            foreach (int k in seq)
            {
                Slot s = slots[k]; GunState g = k < 3 ? f.guns[k] : null; bool fist = k == 3;
                bool sel = fist ? emptyHands : k == f.slot && !emptyHands;
                float w = sel ? 209 : 67.67f, h = sel ? 46 : 36;
                s.rt.sizeDelta = new Vector2(w, h) * K;
                s.rt.anchoredPosition = sel ? new Vector2(0, 39) * K : new Vector2((small++ - 1) * 70.67f, 0) * K;
                s.bg.color = sel ? new Color(28 / 255f, 38 / 255f, 48 / 255f, .68f) : new Color(24 / 255f, 32 / 255f, 40 / 255f, .6f);
                if (fist)
                {
                    s.gun.rectTransform.sizeDelta = (sel ? new Vector2(28, 28) : new Vector2(19, 19)) * K;
                    s.gun.rectTransform.anchoredPosition = (sel ? new Vector2(-12, -8) : new Vector2(-7, -4)) * K;
                    s.ammo.text = "∞"; s.ammo.fontSize = Mathf.RoundToInt((sel ? 24 : 16) * K);
                    s.ammo.rectTransform.anchoredPosition = (sel ? new Vector2(9, 8) : new Vector2(6, 4)) * K;
                    s.line.enabled = false;
                    continue;
                }
                s.gun.sprite = g != null ? UiKit.Get("gun_" + g.def.id) : null; s.gun.enabled = s.gun.sprite != null;
                s.gun.rectTransform.sizeDelta = (sel ? new Vector2(100, 28) : new Vector2(40, 19)) * K;
                s.gun.rectTransform.anchoredPosition = (sel ? new Vector2(-10, -5) : new Vector2(-3, -3)) * K;
                bool low = g != null && g.mag <= g.def.mag / 4;
                string col = low && sel ? "#ff6b5b" : "#ffffff";
                s.ammo.text = g == null ? $"<color=#ffffff59><size={Mathf.RoundToInt(7.5f * K)}>{(k == 0 ? "LONG" : k == 1 ? "CLOSE" : "PISTOL")}</size></color>" : sel
                    ? $"<color={col}>{g.mag}</color><size={Mathf.RoundToInt(13 * K)}>/{g.reserve}</size>"
                    : $"{g.mag}<size={Mathf.RoundToInt(6.5f * K)}>/{g.reserve}</size>";
                s.ammo.fontSize = Mathf.RoundToInt((sel ? 21 : 10) * K);
                s.ammo.rectTransform.anchoredPosition = (sel ? new Vector2(9, 10) : new Vector2(5, 7)) * K;
                float fill = g == null ? 0 : (float)g.mag / g.def.mag;
                float lw = (w - (sel ? 18 : 10)) * fill;
                s.line.rectTransform.sizeDelta = new Vector2(lw, sel ? 3 : 2) * K;
                s.line.rectTransform.anchoredPosition = new Vector2(sel ? 9 : 5, 4) * K;
                s.line.color = sel ? Color.white : new Color(1, 1, 1, .75f);
            }
            bool reloading = f.reloadEnd > 0 && f.Gun != null && !f.holstered;
            reloadBar.gameObject.SetActive(reloading);
            if (reloading) reloadFill.rectTransform.sizeDelta = new Vector2(140 * (1f - (f.reloadEnd - Time.time) / f.Gun.def.reloadTime), 4) * K;
            input.SetCounts(f.meds, f.frost);
            input.SetAir(f.air);
            // through the optic only FIRE, SCOPE, the joystick and the minimap stay (web #hud.scoped-ui)
            bool scoped = player.Scoped && f.air == 0;
            input.ScopedUi = scoped;
            // every long gun looks through an optic: the rifle's own x2 (or the scope you attached), the sniper's x8
            bool sniperLens = scoped;
            if (lens.gameObject.activeSelf != sniperLens) lens.gameObject.SetActive(sniperLens);
            if (scoped && player.ScopeZoom != lensZoom) SetLens(player.ScopeZoom);
            if (!scoped) lensZoom = 0;
            box.gameObject.SetActive((f.air == 0 && !scoped && !spectating) || input.Editing);
            hpBgRt.gameObject.SetActive(!scoped); arBgRt.gameObject.SetActive(!scoped);
            // plane / sky-dive hints: each shows 2.5 s, then gets out of the way
            string hint = f.air == 1 ? "Tap <b>JUMP</b> to drop" : f.air >= 2 ? "You glide where you look · tap <b>JUMP</b> to drop straight down · the parachute opens by itself" : "";
            if (hint != hintText) { hintText = hint; hintAt = Time.time; }

            // using a medkit: green loading ring in the middle of the screen (web .heal-ring, smaller)
            bool healing = f.healEnd > 0 && !spectating;
            healRing.gameObject.SetActive(healing);
            if (healing) healFg.fillAmount = Mathf.Clamp01(1f - (f.healEnd - Time.time) / 3f);
            if (f.healEnd > 0) { reloadBar.gameObject.SetActive(true); reloadFill.color = new Color(1f, .42f, .42f); reloadFill.rectTransform.sizeDelta = new Vector2(140 * (1f - (f.healEnd - Time.time) / 3f), 4) * K; }
            else reloadFill.color = new Color(1f, .83f, .3f);
            // the scope button only exists while a LONG-RANGE gun is in your hands (rifle / sniper)
            input.ShowScope(f.air == 0 && f.Alive && !f.holstered && f.Gun != null && (f.Gun.def.id == "rifle" || f.Gun.def.id == "sniper"));

            // top: ALIVE n   KILLS n
            GameBoot gb = GameBoot.Instance;
            int ls = Mathf.RoundToInt(10 * K);
            aliveText.text = $"<size={ls}><color=#ffffffb3>ALIVE</color></size>  {gb.AliveCount}     <size={ls}><color=#ffffffb3>KILLS</color></size>  {f.kills}";
            aliveBox.rectTransform.sizeDelta = new Vector2(aliveText.preferredWidth + 24 * K, 26 * K);

            Zone z = Zone.Instance;
            bool outside = false;
            if (z != null)
            {
                int s = Mathf.CeilToInt(z.SecondsLeft); string tm = $"{s / 60}:{s % 60:00}";
                zoneText.text = !z.Active ? $"SAFE ZONE APPEARS IN {tm}" : z.PhaseEnds > 1e30f ? "FINAL ZONE" : z.Shrinking ? $"ZONE SHRINKING {tm}" : $"ZONE SHRINKS IN {tm}";
                outside = z.Active && z.Outside(player.transform.position) && f.Alive;
            }
            zoneBox.color = outside ? new Color(200 / 255f, 30 / 255f, 30 / 255f, .5f + .25f * Mathf.PingPong(Time.time * 2f, 1f)) : new Color(20 / 255f, 60 / 255f, 140 / 255f, .5f);
            zoneBox.rectTransform.sizeDelta = new Vector2(zoneText.preferredWidth + 20 * K, 18 * K);
            centerText.text = outside ? "YOU ARE OUTSIDE THE SAFE ZONE" : hintText != "" && Time.time - hintAt < 2.5f ? hintText : "";
            centerText.color = outside ? new Color(1f, 0.42f, 0.36f) : Color.white;
            centerBox.gameObject.SetActive(centerText.text != "");
            if (centerText.text != "") centerBox.rectTransform.sizeDelta = new Vector2(260 * K, centerText.preferredHeight + 16 * K);

            bool onEnemy = player.OnEnemy != null;
            dot.color = onEnemy ? new Color(1f, 0.23f, 0.19f) : Color.white;
            dot.transform.parent.gameObject.SetActive(f.Alive && !sniperLens && !spectating);
            // the dot is green, red on an enemy's body (every optic)
            lensDot.color = onEnemy ? new Color(1f, .12f, .12f) : new Color(.22f, 1f, .42f);
            holoRing.color = onEnemy ? new Color(1f, .24f, .24f, .8f) : new Color(1, 1, 1, .55f);
            hitT -= dt;
            float ha = Mathf.Clamp01(hitT * 5f);
            hitRing.color = hitKill ? new Color(1f, 0.23f, 0.19f, ha) : new Color(1, 1, 1, ha);
            hitRing.rectTransform.localScale = Vector3.one * (hitKill ? 1.4f : 1.1f);

            for (int k = floaters.Count - 1; k >= 0; k--)
            {
                Floater fl = floaters[k]; fl.life -= dt; fl.pos += Vector3.up * dt * 1.2f;
                Vector3 sp = cam != null ? cam.WorldToScreenPoint(fl.pos) : Vector3.back;
                if (fl.life <= 0 || sp.z <= 0) { Destroy(fl.t.gameObject); floaters.RemoveAt(k); continue; }
                fl.t.rectTransform.anchoredPosition = (new Vector2(sp.x, sp.y) - new Vector2(Screen.width, Screen.height) / 2f) / input.Canvas.scaleFactor;
                Color cc = fl.t.color; cc.a = Mathf.Clamp01(fl.life / 0.3f); fl.t.color = cc;
            }

            for (int k = taken.Count - 1; k >= 0; k--)
            {
                var (tt, life) = taken[k]; life -= dt;
                if (life <= 0) { Destroy(tt.gameObject); taken.RemoveAt(k); continue; }
                taken[k] = (tt, life);
                tt.rectTransform.anchoredPosition += new Vector2(0, 30 * UiKit.K * dt);
                Color cc = tt.color; cc.a = Mathf.Clamp01(life / 0.5f); tt.color = cc;
            }

            flashT = Mathf.Max(0, flashT - dt); flash.color = new Color(.8f, 0, 0, flashT * .6f);

            // kill banner: pops in (scale 2.2 -> 1), fades after 2 s
            if (banner.gameObject.activeSelf)
            {
                bannerT -= dt;
                float age = 2.2f - bannerT;
                banner.localScale = Vector3.one * Mathf.Lerp(2.2f, 1f, Mathf.Clamp01(age / .28f));
                foreach (Graphic gr in banner.GetComponentsInChildren<Graphic>()) { Color cc = gr.color; cc.a = gr == banner.GetComponent<Image>() ? 0 : Mathf.Clamp01(bannerT / .4f); gr.color = cc; }
                if (bannerT <= 0) banner.gameObject.SetActive(false);
            }

            // kill feed: 8 s per line
            for (int k = 0; k < feedBg.Count; k++)
            {
                bool show = k < feed.Count && Time.time - feed[k].t < 8f && !scoped;
                feedBg[k].gameObject.SetActive(show);
                if (!show) continue;
                feedTxt[k].text = feed[k].text;
                feedBg[k].color = feed[k].mine ? new Color(200 / 255f, 150 / 255f, 30 / 255f, .55f) : new Color(0, 0, 0, .45f);
                feedBg[k].rectTransform.sizeDelta = new Vector2(feedTxt[k].preferredWidth + 16 * K, 17 * K);
            }

            // the loot list (hidden while scoped / in the air / dead / watching)
            bool showLoot = !spectating && f.Alive && f.air == 0 && !scoped;
            int shown = showLoot ? Mathf.Min(player.Nearby.Count, lootRows.Count) : 0;
            lootPanel.gameObject.SetActive(shown > 0 || input.Editing);
            lootPanel.sizeDelta = new Vector2(172, 8 + Mathf.Max(1, shown) * 27) * K;
            for (int i = 0; i < lootRows.Count; i++)
            {
                var r = lootRows[i];
                Loot it = showLoot && i < player.Nearby.Count ? player.Nearby[i] : null;
                r.bg.gameObject.SetActive(it != null);
                if (it != null && it != r.item)
                {
                    r.icon.sprite = it.Icon; r.icon.enabled = r.icon.sprite != null;
                    r.text.text = $"{it.Label}\n<size={Mathf.RoundToInt(9 * K)}><color=#ffffffb0>{it.Sub}</color></size>";
                }
                lootRows[i] = (r.bg, r.icon, r.text, it);
            }

            // spectating bar
            specBar.gameObject.SetActive(spectating && player.SpectateTarget != null);
            if (spectating && player.SpectateTarget != null) specText.text = $"<size={Mathf.RoundToInt(9 * K)}><color=#cfd6da>SPECTATING</color></size>  {player.SpectateTarget.displayName}";
            status.gameObject.SetActive(!scoped && f.air == 0);

            if (gb.Result != 0 && !endPanel.gameObject.activeSelf && !spectating)
            {
                spectateBtn.gameObject.SetActive(gb.Result < 0 && gb.AliveCount > 1);
                endPanel.Find("card/Again").GetComponent<RectTransform>().anchoredPosition = new Vector2(gb.Result < 0 && gb.AliveCount > 1 ? -70 : 0, -45) * K;
                endPanel.gameObject.SetActive(true); endPanel.SetAsLastSibling();
                endTitle.text = gb.Result > 0 ? "VICTORY" : "ELIMINATED";
                endTitle.color = gb.Result > 0 ? new Color(1f, 0.83f, 0.3f) : new Color(1f, 0.36f, 0.3f);
                endSub.text = $"#{gb.Rank} <size={Mathf.RoundToInt(10 * K)}>/ {gb.Fighters.Count}</size>     KILLS {f.kills}";
            }
        }
    }
}
