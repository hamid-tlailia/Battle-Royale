using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LastWar
{
    /// <summary>
    /// Backpack (web .bag-panel): weapons and armour on the left (picture, magazine, move up, DROP), supplies on the
    /// right (ammo, medkits, ice walls with DROP; scopes with USE / TAKE OFF). Slides in from the left.
    /// </summary>
    public class BagPanel : MonoBehaviour
    {
        static BagPanel open;
        public static bool IsOpen => open != null;
        TouchControls input; Fighter f; RectTransform panel, left, right; float slide; string key = "";
        static readonly Color Gold = new Color(1f, .83f, .3f), Ink = new Color(.1f, .08f, .02f), Row = new Color(1, 1, 1, .06f);

        public static void Toggle(TouchControls input, Fighter f)
        {
            if (open != null) { Destroy(open.gameObject); open = null; return; }
            var shade = UiKit.Img(input.Root, "BagShade", null, new Vector2(.5f, .5f), Vector2.zero, new Vector2(1400, 1400), new Color(0, 0, 0, .25f), true);
            UiKit.Fill(shade.rectTransform); // the whole screen: the panel's left edge is the screen's
            open = shade.gameObject.AddComponent<BagPanel>();
            open.input = input; open.f = f;
            var tr = shade.gameObject.AddComponent<EventTrigger>(); var e = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            e.callback.AddListener(_ => { Destroy(open.gameObject); open = null; }); tr.triggers.Add(e);
            open.Build();
        }

        void Build()
        {
            panel = UiKit.Img(transform, "bag", UiKit.Panel, new Vector2(0, .5f), Vector2.zero, new Vector2(460, 320), Color.white, true).rectTransform;
            panel.pivot = new Vector2(0, .5f);
            SettingsPanel.Tap(panel.gameObject, () => { }); // taps on the panel itself must not reach the shade (which closes it)
            UiKit.CloseButton(panel, new Vector2(1, 1), new Vector2(-10, -8), 28, () => { Destroy(gameObject); open = null; });
            var title = UiKit.Txt(panel, new Vector2(0, 1), new Vector2(16, -12), new Vector2(300, 22), 15, TextAnchor.UpperLeft, Gold); title.text = "BACKPACK";
            var h1 = UiKit.Txt(panel, new Vector2(0, 1), new Vector2(16, -40), new Vector2(210, 16), 10, TextAnchor.UpperLeft, new Color(1, 1, 1, .6f)); h1.text = "WEAPONS & ARMOR";
            var h2 = UiKit.Txt(panel, new Vector2(0, 1), new Vector2(238, -40), new Vector2(210, 16), 10, TextAnchor.UpperLeft, new Color(1, 1, 1, .6f)); h2.text = "SUPPLIES";
            left = Column(16); right = Column(238);
        }

        RectTransform Column(float x)
        {
            var rt = new GameObject("col", typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(panel, false); rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -58) * UiKit.K; rt.sizeDelta = new Vector2(208, 250) * UiKit.K;
            return rt;
        }

        /// <summary>one line: picture, name + small line, buttons on the right</summary>
        void AddRow(RectTransform col, ref float y, Sprite pic, string name, string sub, params (string label, bool gold, System.Action act)[] buttons)
        {
            const float K = UiKit.K, H = 34;
            var bg = UiKit.Img(col, "row", UiKit.Round, new Vector2(0, 1), new Vector2(0, -y), new Vector2(208, H), Row);
            if (pic != null) { var p = UiKit.Img(bg.rectTransform, "pic", pic, new Vector2(0, .5f), new Vector2(4, 0), new Vector2(44, 22), Color.white); p.preserveAspect = true; p.rectTransform.pivot = new Vector2(0, .5f); }
            var t = UiKit.Txt(bg.rectTransform, new Vector2(0, .5f), new Vector2(pic != null ? 52 : 8, 0), new Vector2(96, H), 10.5f, TextAnchor.MiddleLeft, Color.white);
            t.text = $"{name}\n<size={Mathf.RoundToInt(8 * K)}><color=#ffffffa0>{sub}</color></size>";
            float bx = -3;
            for (int i = buttons.Length - 1; i >= 0; i--)
            {
                var (label, gold, act) = buttons[i];
                float w = label.Length <= 2 ? 28 : label.Length > 4 ? 52 : 40;
                var b = UiKit.Img(bg.rectTransform, label, UiKit.Round, new Vector2(1, .5f), new Vector2(bx, 0), new Vector2(w, 22), gold ? Gold : new Color(.75f, .22f, .17f), true);
                b.rectTransform.pivot = new Vector2(1, .5f);
                var bt = UiKit.Txt(b.rectTransform, new Vector2(.5f, .5f), Vector2.zero, new Vector2(w, 22), 9, TextAnchor.MiddleCenter, gold ? Ink : Color.white);
                bt.rectTransform.pivot = new Vector2(.5f, .5f); bt.text = label; Destroy(bt.GetComponent<Shadow>());
                SettingsPanel.Tap(b.gameObject, () => { act(); key = ""; });
                bx -= w + 4;
            }
            y += H + 4;
        }

        string State()
        {
            var sb = new System.Text.StringBuilder();
            for (int k = 0; k < 3; k++) { var g = f.guns[k]; sb.Append(g == null ? "-" : g.def.id + g.mag + "/" + g.reserve).Append(f.order[k]).Append('|'); }
            foreach (var kv in f.spare) sb.Append(kv.Key).Append(kv.Value);
            sb.Append(f.meds).Append(',').Append(f.frost).Append(',').Append(Mathf.RoundToInt(f.armor)).Append(',').Append(f.scopes).Append(',').Append(f.scope);
            return sb.ToString();
        }

        void Rebuild()
        {
            foreach (Transform c in left) Destroy(c.gameObject);
            foreach (Transform c in right) Destroy(c.gameObject);
            float y = 0;
            // weapons in the weapon box's order; the arrow moves one up
            for (int k = 0; k < 3; k++)
            {
                int s = f.order[k], pos = k;
                GunState g = f.guns[s];
                var buttons = new List<(string, bool, System.Action)>();
                if (pos > 0) buttons.Add(("UP", true, () => { int a = f.order[pos - 1]; f.order[pos - 1] = f.order[pos]; f.order[pos] = a; }));
                if (g != null) buttons.Add(("DROP", false, () => f.DropWeapon(s)));
                if (g == null) AddRow(left, ref y, null, $"<color=#ffffff80>{Tuning.SlotNames[s]}</color>", "empty", buttons.ToArray());
                else AddRow(left, ref y, UiKit.Get("gun_" + g.def.id), g.def.name, $"{g.mag} in magazine", buttons.ToArray());
            }
            AddRow(left, ref y, UiKit.Get("icon_vest"), "Armor", $"{Mathf.RoundToInt(f.armor)} / 100");

            y = 0;
            foreach (string id in new[] { "rifle", "sniper", "shotgun", "smg", "pistol" })
            {
                int n = f.AmmoOf(id); if (n <= 0) continue;
                string wid = id;
                AddRow(right, ref y, UiKit.Get("gun_" + id), Tuning.Weapon(id).name + " ammo", n + " rounds", ("DROP", false, () => f.DropAmmo(wid)));
            }
            if (f.meds > 0) AddRow(right, ref y, UiKit.Get("icon_heal"), $"Medkit  × {f.meds}", "+100 HP · 3 s", ("DROP", false, () => f.DropMed()));
            if (f.frost > 0) AddRow(right, ref y, UiKit.Get("icon_frost"), $"Ice wall  × {f.frost}", "stops bullets · 25 s", ("DROP", false, () => f.DropFrost()));
            foreach (int z in new[] { 2, 4, 8 })
            {
                if ((f.scopes & Fighter.ScopeBit(z)) == 0) continue;
                int zz = z; bool on = f.scope == z;
                AddRow(right, ref y, UiKit.Get("icon_scope"), $"×{z} scope", on ? "on your long gun" : "rifle / sniper",
                    (on ? "TAKE OFF" : "USE", true, () => f.UseScope(on ? 0 : zz)), ("DROP", false, () => f.DropScope(zz)));
            }
            if (y == 0) { var t = UiKit.Txt(right, new Vector2(0, 1), Vector2.zero, new Vector2(200, 20), 11, TextAnchor.UpperLeft, new Color(1, 1, 1, .5f)); t.text = "No supplies yet"; }
        }

        void Update()
        {
            // slide in from the left
            slide = Mathf.MoveTowards(slide, 1, Time.deltaTime * 6f);
            panel.anchoredPosition = new Vector2(Mathf.Lerp(-480, 6, 1 - (1 - slide) * (1 - slide)) * UiKit.K, 0);
            if (!f.Alive || f.air != 0) { Destroy(gameObject); open = null; return; }
            string k = State();
            if (k != key) { key = k; Rebuild(); }
        }

        void OnDestroy() { if (open == this) open = null; }
    }
}
