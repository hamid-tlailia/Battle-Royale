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
        RectTransform box, hpBgRt, arBgRt, lens; Image lensDot;
        Image hpFill, armorFill, dot, hitRing, flash, reloadFill; RectTransform reloadBar;
        Text hpText, aliveText, zoneText, centerText, bannerCount;
        Image aliveBox, zoneBox; RectTransform banner;
        readonly List<Image> feedBg = new List<Image>(); readonly List<Text> feedTxt = new List<Text>();
        readonly List<(string text, bool mine, float t)> feed = new List<(string, bool, float)>();
        RectTransform endPanel; Text endTitle, endSub;
        float hitT, flashT, bannerT; bool hitKill;
        string hintText = ""; float hintAt;
        class Floater { public Text t; public Vector3 pos; public float life; }
        readonly List<Floater> floaters = new List<Floater>();
        readonly List<(Text t, float life)> taken = new List<(Text, float)>();
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
            Transform c = input.Canvas.transform;
            flash = UiKit.Img(c, "Flash", null, C, Vector2.zero, new Vector2(1400, 1400), new Color(0.8f, 0, 0, 0));
            flash.rectTransform.pivot = C; flash.transform.SetAsFirstSibling();

            // ---- bottom centre: bars (bottom 6), reload bar, weapon box
            RectTransform hpBg = hpBgRt = UiKit.Img(c, "HpBg", UiKit.Round, BC, new Vector2(0, 6), new Vector2(240, 10), new Color(0, 0, 0, .45f)).rectTransform;
            hpFill = UiKit.Img(hpBg, "Hp", UiKit.Round, new Vector2(0, .5f), Vector2.zero, new Vector2(240, 10), new Color(0.48f, 0.85f, 0.32f));
            hpText = UiKit.Txt(hpBg, new Vector2(1, .5f), Vector2.zero, new Vector2(30, 14), 12, TextAnchor.MiddleLeft, Color.white);
            hpText.rectTransform.pivot = new Vector2(0, .5f); hpText.rectTransform.anchoredPosition = new Vector2(5 * UiKit.K, 0);
            RectTransform arBg = arBgRt = UiKit.Img(c, "ArBg", UiKit.Round, BC, new Vector2(0, 18), new Vector2(240, 7), new Color(0, 0, 0, .45f)).rectTransform;
            armorFill = UiKit.Img(arBg, "Ar", UiKit.Round, new Vector2(0, .5f), Vector2.zero, new Vector2(240, 7), new Color(0.35f, 0.67f, 1f));
            reloadBar = UiKit.Img(c, "Reload", UiKit.Round, BC, new Vector2(0, 28), new Vector2(140, 4), new Color(0, 0, 0, .4f)).rectTransform;
            reloadFill = UiKit.Img(reloadBar, "fill", UiKit.Round, new Vector2(0, .5f), Vector2.zero, new Vector2(0, 4), new Color(1f, 0.83f, 0.3f));

            // weapon box: one piece the layout editor can move (current gun on top, the others + the fist below)
            box = new GameObject("WeaponBox", typeof(RectTransform)).GetComponent<RectTransform>();
            box.SetParent(c, false); box.anchorMin = box.anchorMax = BC; box.pivot = new Vector2(.5f, 0);
            box.anchoredPosition = new Vector2(0, 35) * UiKit.K; box.sizeDelta = new Vector2(209, 85) * UiKit.K;
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
            centerText = UiKit.Txt(c, C, new Vector2(0, 60), new Vector2(400, 20), 13, TextAnchor.MiddleCenter, new Color(1f, 0.42f, 0.36f));
            centerText.rectTransform.pivot = C;

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
            var edge = UiKit.Img(c, "DotEdge", UiKit.Disc, C, Vector2.zero, new Vector2(9, 9), new Color(0, 0, 0, .55f)); edge.rectTransform.pivot = C;
            dot = UiKit.Img(edge.rectTransform, "Dot", UiKit.Disc, C, Vector2.zero, new Vector2(6, 6), Color.white); dot.rectTransform.pivot = C;
            hitRing = UiKit.Img(c, "HitRing", UiKit.Ring, C, Vector2.zero, new Vector2(20, 20), new Color(1, 1, 1, 0)); hitRing.rectTransform.pivot = C;

            // ---- sniper scope: black surround with a round lens, thin cross and a centre dot (web .scope-overlay)
            lens = new GameObject("Scope", typeof(RectTransform)).GetComponent<RectTransform>();
            lens.SetParent(c, false); lens.anchorMin = Vector2.zero; lens.anchorMax = Vector2.one; lens.offsetMin = lens.offsetMax = Vector2.zero;
            lens.SetAsFirstSibling();
            var glass = new GameObject("lens", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            glass.transform.SetParent(lens, false); glass.texture = LensTexture(); glass.raycastTarget = false;
            var gr = glass.rectTransform; gr.anchorMin = gr.anchorMax = gr.pivot = C; gr.sizeDelta = new Vector2(1080, 1080);
            foreach (int side in new[] { -1, 1 })
            {
                var blk = UiKit.Img(lens, "side", null, C, Vector2.zero, Vector2.one, Color.black);
                blk.rectTransform.pivot = new Vector2(side < 0 ? 1 : 0, .5f); blk.rectTransform.anchoredPosition = new Vector2(side * 539f, 0); blk.rectTransform.sizeDelta = new Vector2(2400, 1090);
            }
            lensDot = UiKit.Img(lens, "dot", UiKit.Disc, C, Vector2.zero, new Vector2(3.5f, 3.5f), new Color(.22f, 1f, .42f)); lensDot.rectTransform.pivot = C;
            lens.gameObject.SetActive(false);

            // ---- end screen
            endPanel = UiKit.Img(c, "End", null, C, Vector2.zero, new Vector2(1400, 1400), new Color(0, 0, 0, .6f), true).rectTransform;
            endPanel.pivot = C;
            var card = UiKit.Img(endPanel, "card", UiKit.Round, C, Vector2.zero, new Vector2(300, 170), new Color(0.09f, 0.11f, 0.13f, .95f)); card.rectTransform.pivot = C;
            card.gameObject.AddComponent<Outline>().effectColor = new Color(1f, 0.83f, 0.3f, .45f);
            endTitle = UiKit.Txt(card.rectTransform, C, new Vector2(0, 45), new Vector2(300, 40), 30, TextAnchor.MiddleCenter, Color.white); endTitle.rectTransform.pivot = C;
            endSub = UiKit.Txt(card.rectTransform, C, new Vector2(0, 5), new Vector2(300, 20), 14, TextAnchor.MiddleCenter, Color.white); endSub.rectTransform.pivot = C;
            var again = UiKit.Img(card.rectTransform, "Again", UiKit.Round, C, new Vector2(0, -45), new Vector2(150, 34), new Color(1f, 0.78f, 0.25f), true);
            again.rectTransform.pivot = C;
            var at = UiKit.Txt(again.rectTransform, C, Vector2.zero, new Vector2(150, 34), 14, TextAnchor.MiddleCenter, new Color(0.1f, 0.08f, 0.02f)); at.rectTransform.pivot = C;
            at.text = "LOBBY"; Destroy(at.GetComponent<Shadow>());
            again.gameObject.AddComponent<Button>().onClick.AddListener(() => GameBoot.Instance.Restart());
            endPanel.gameObject.SetActive(false);
        }

        /// <summary>1024 px scope picture: clear glass in the middle, dark rim, black outside, hair-thin cross</summary>
        static Texture2D LensTexture()
        {
            const int n = 512; var tex = new Texture2D(n, n, TextureFormat.RGBA32, false); tex.wrapMode = TextureWrapMode.Clamp;
            var px = new Color32[n * n]; float c0 = (n - 1) / 2f, R = n * 0.47f;
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
                {
                    float dx = x - c0, dy = y - c0, d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = d >= R ? 1f : d > R - 10 ? Mathf.Lerp(0.85f, 1f, (d - (R - 10)) / 10f) : d > R - 46 ? Mathf.Pow((d - (R - 46)) / 36f, 2f) * 0.8f : 0f; // rim + inner shadow
                    bool cross = d < R - 8 && d > 5 && (Mathf.Abs(dx) < 0.8f || Mathf.Abs(dy) < 0.8f);
                    bool thick = d < R - 8 && d > R * 0.42f && (Mathf.Abs(dx) < 2.2f || Mathf.Abs(dy) < 2.2f); // heavy outer posts
                    if (cross || thick) a = Mathf.Max(a, 0.9f);
                    px[y * n + x] = new Color(0.02f, 0.03f, 0.02f, a);
                }
            tex.SetPixels32(px); tex.Apply();
            return tex;
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
            Fighter f = player.fighter; float dt = Time.deltaTime; const float K = UiKit.K; Camera cam = player.cam;
            hpFill.rectTransform.sizeDelta = new Vector2(240 * f.hp / Fighter.MaxHp, 10) * K;
            hpFill.color = f.hp > 30 ? new Color(0.48f, 0.85f, 0.32f) : new Color(0.94f, 0.36f, 0.3f);
            armorFill.rectTransform.sizeDelta = new Vector2(240 * f.armor / 100f, 7) * K;
            hpText.text = Mathf.CeilToInt(f.hp).ToString();

            // weapon box (web .slots grid): what is in your hands 209 x 46 on top, the other three 67 x 36 below
            int small = 0;
            for (int k = 0; k < 4; k++)
            {
                Slot s = slots[k]; GunState g = k < 3 ? f.guns[k] : null; bool fist = k == 3;
                bool sel = fist ? f.holstered : k == f.slot && !f.holstered;
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
                s.ammo.text = g == null ? "" : sel
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
            bool sniperLens = scoped && f.Gun != null && f.Gun.def.id == "sniper";
            if (lens.gameObject.activeSelf != sniperLens) lens.gameObject.SetActive(sniperLens);
            box.gameObject.SetActive((f.air == 0 && !scoped) || input.Editing);
            hpBgRt.gameObject.SetActive(!scoped); arBgRt.gameObject.SetActive(!scoped);
            // plane / sky-dive hints: each shows 2.5 s, then gets out of the way
            string hint = f.air == 1 ? "Tap <b>JUMP</b> to drop" : f.air >= 2 ? "You glide where you look · tap <b>JUMP</b> to drop straight down · the parachute opens by itself" : "";
            if (hint != hintText) { hintText = hint; hintAt = Time.time; }

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

            bool onEnemy = player.OnEnemy != null;
            dot.color = onEnemy ? new Color(1f, 0.23f, 0.19f) : Color.white;
            dot.transform.parent.gameObject.SetActive(f.Alive && !sniperLens);
            lensDot.color = onEnemy ? new Color(1f, .12f, .12f) : new Color(.22f, 1f, .42f);
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

            if (gb.Result != 0 && !endPanel.gameObject.activeSelf)
            {
                endPanel.gameObject.SetActive(true); endPanel.SetAsLastSibling();
                endTitle.text = gb.Result > 0 ? "VICTORY" : "ELIMINATED";
                endTitle.color = gb.Result > 0 ? new Color(1f, 0.83f, 0.3f) : new Color(1f, 0.36f, 0.3f);
                endSub.text = $"#{gb.Rank} <size={Mathf.RoundToInt(10 * K)}>/ {gb.Fighters.Count}</size>     KILLS {f.kills}";
            }
        }
    }
}
