using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LastWar
{
    /// <summary>
    /// The backpack, three columns: GEAR (vest, helmet, backpack - level, points), WEAPONS (the guns in their box
    /// order with real pictures, magazine / spare rounds, scope) and CAPACITY (a grid of supplies, "used / room").
    /// Drag anything out of the panel to throw it on the ground; drag a weapon up or down to reorder it.
    /// Scopes: tap to put on / take off. Slides in from the left.
    /// </summary>
    public class BagPanel : MonoBehaviour
    {
        static BagPanel open;
        public static bool IsOpen => open != null;
        public static void CloseAll() { if (open != null) Destroy(open.gameObject); open = null; }
        TouchControls input; Fighter f; RectTransform panel, gear, guns, supply; Text capText; float slide; string key = "";
        static readonly Color Gold = new Color(1f, .83f, .3f), Ink = new Color(.1f, .08f, .02f), Card = new Color(1, 1, 1, .07f), Dim = new Color(1, 1, 1, .45f);
        static readonly Color[] LvCol = { new Color(1, 1, 1, .5f), new Color(.44f, .71f, 1f), new Color(.77f, .55f, 1f), new Color(1f, .83f, .3f) };
        const float W = 650, H = 330;

        public static void Toggle(TouchControls input, Fighter f)
        {
            if (open != null) { Destroy(open.gameObject); open = null; return; }
            var shade = UiKit.Img(input.Root, "BagShade", null, new Vector2(.5f, .5f), Vector2.zero, new Vector2(1400, 1400), new Color(0, 0, 0, .3f), true);
            UiKit.Fill(shade.rectTransform);
            open = shade.gameObject.AddComponent<BagPanel>();
            open.input = input; open.f = f;
            var tr = shade.gameObject.AddComponent<EventTrigger>(); var e = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            e.callback.AddListener(_ => { Destroy(open.gameObject); open = null; }); tr.triggers.Add(e);
            open.Build();
        }

        RectTransform Column(float x, float w, string title)
        {
            var t = UiKit.Txt(panel, new Vector2(0, 1), new Vector2(x, -12), new Vector2(w, 18), 11, TextAnchor.UpperLeft, new Color(1, 1, 1, .75f)); t.text = title;
            var rt = new GameObject("col", typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(panel, false); rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -34) * UiKit.K; rt.sizeDelta = new Vector2(w, H - 50) * UiKit.K;
            return rt;
        }

        void Build()
        {
            panel = UiKit.Img(transform, "bag", UiKit.Panel, new Vector2(0, .5f), Vector2.zero, new Vector2(W, H), Color.white, true).rectTransform;
            panel.pivot = new Vector2(0, .5f);
            SettingsPanel.Tap(panel.gameObject, () => { }); // taps on the panel itself must not reach the shade (which closes it)
            UiKit.CloseButton(panel, new Vector2(1, 1), new Vector2(-8, -6), 26, () => { Destroy(gameObject); open = null; });
            gear = Column(12, 120, "GEAR"); guns = Column(142, 250, "WEAPONS"); supply = Column(402, 214, "CAPACITY");
            capText = UiKit.Txt(panel, new Vector2(0, 1), new Vector2(480, -12), new Vector2(130, 18), 11, TextAnchor.UpperRight, Gold);
            var hint = UiKit.Txt(panel, new Vector2(0, 0), new Vector2(14, 6), new Vector2(600, 14), 8.5f, TextAnchor.LowerLeft, new Color(1, 1, 1, .45f));
            hint.fontStyle = FontStyle.Normal; hint.text = "Drag out of the backpack to drop · drag a weapon up / down to reorder · tap a scope to put it on";
        }

        // ------------------------------------------------------------------ pieces
        Image Tile(RectTransform col, Vector2 pos, Vector2 size)
        {
            var bg = UiKit.Img(col, "tile", UiKit.Round, new Vector2(0, 1), pos, size, Card, true);
            return bg;
        }

        Image Pic(RectTransform parent, Sprite sp, Vector2 anchor, Vector2 pos, Vector2 size, Color col)
        {
            var p = UiKit.Img(parent, "pic", sp, anchor, pos, size, col); p.preserveAspect = true; p.rectTransform.pivot = anchor;
            return p;
        }

        Text Label(RectTransform parent, Vector2 anchor, Vector2 pos, Vector2 size, float font, TextAnchor align, Color col, string text)
        {
            var t = UiKit.Txt(parent, anchor, pos, size, font, align, col); t.rectTransform.pivot = anchor; t.text = text; return t;
        }

        static Sprite Item(string name) => Resources.Load<Sprite>("UI/Items/" + name);

        // GEAR: one card per piece (empty: a faded picture and "none")
        void GearCard(ref float y, Sprite pic, string name, int lv, string sub, System.Action drop)
        {
            var c = Tile(gear, new Vector2(0, -y), new Vector2(120, 84));
            RectTransform rt = c.rectTransform;
            Pic(rt, pic, new Vector2(.5f, 1), new Vector2(0, -4), new Vector2(64, 48), lv > 0 ? Color.white : new Color(1, 1, 1, .2f));
            Label(rt, new Vector2(.5f, 0), new Vector2(0, 16), new Vector2(118, 14), 9.5f, TextAnchor.MiddleCenter, lv > 0 ? LvCol[Mathf.Clamp(lv, 0, 3)] : Dim, lv > 0 ? $"{name} Lv{lv}" : name);
            Label(rt, new Vector2(.5f, 0), new Vector2(0, 3), new Vector2(118, 12), 8, TextAnchor.MiddleCenter, new Color(1, 1, 1, .7f), sub);
            if (lv > 0) Draggable(c, -1, drop);
            y += 90;
        }

        // WEAPONS: the box order; picture, name, magazine / spare, the scope on long guns
        void GunCard(ref float y, int pos)
        {
            int s = f.order[pos]; GunState g = f.guns[s];
            var c = Tile(guns, new Vector2(0, -y), new Vector2(250, 84));
            RectTransform rt = c.rectTransform;
            if (g == null)
            {
                Label(rt, new Vector2(.5f, .5f), Vector2.zero, new Vector2(240, 20), 11, TextAnchor.MiddleCenter, Dim, Tuning.SlotNames[s] + "  ·  empty");
                Draggable(c, pos, null);
            }
            else
            {
                Sprite sp = Item("gun_" + g.def.Look) ?? UiKit.Get("gun_" + g.def.id);
                Pic(rt, sp, new Vector2(1, .5f), new Vector2(-8, 4), new Vector2(170, 66), Color.white);
                Label(rt, new Vector2(0, 1), new Vector2(8, -6), new Vector2(160, 16), 11, TextAnchor.UpperLeft, Color.white, g.def.name);
                Label(rt, new Vector2(0, 0), new Vector2(8, 6), new Vector2(120, 18), 13, TextAnchor.LowerLeft, Gold, $"{g.mag}<size={Mathf.RoundToInt(9 * UiKit.K)}><color=#ffffffb0> / {g.reserve}</color></size>");
                if ((g.def.id == "rifle" || g.def.id == "sniper") && (f.scope > 0 || g.def.id == "sniper"))
                {
                    int z = g.def.id == "sniper" ? Mathf.Max(8, f.scope) : f.scope;
                    var chip = UiKit.Img(rt, "scope", UiKit.Round, new Vector2(0, 0), new Vector2(92, 6), new Vector2(34, 16), new Color(.77f, .55f, 1f, .85f));
                    Label(chip.rectTransform, new Vector2(.5f, .5f), Vector2.zero, new Vector2(34, 16), 9, TextAnchor.MiddleCenter, Color.white, "×" + z);
                }
                int ss = s; Draggable(c, pos, () => f.DropWeapon(ss));
            }
            y += 90;
        }

        // CAPACITY: a 2-column grid of supplies
        int tileN;
        void Supply(Sprite pic, string name, string count, System.Action drop, System.Action tap = null, bool on = false)
        {
            float x = (tileN % 2) * 108, yy = (tileN / 2) * 74; tileN++;
            var c = Tile(supply, new Vector2(x, -yy), new Vector2(104, 70));
            if (on) c.color = new Color(.77f, .55f, 1f, .3f);
            RectTransform rt = c.rectTransform;
            Pic(rt, pic, new Vector2(.5f, 1), new Vector2(0, -16), new Vector2(54, 34), Color.white);
            Label(rt, new Vector2(.5f, 1), new Vector2(0, -2), new Vector2(102, 14), 8.5f, TextAnchor.UpperCenter, new Color(1, 1, 1, .85f), name);
            Label(rt, new Vector2(1, 0), new Vector2(-5, 3), new Vector2(60, 14), 10, TextAnchor.LowerRight, Gold, count);
            if (tap != null) SettingsPanel.Tap(c.gameObject, () => { tap(); key = ""; });
            Draggable(c, -1, drop);
        }

        bool dragging;
        /// <summary>drag a card: up / down among the weapons to change their order, out of the panel to throw it on the ground</summary>
        void Draggable(Image bg, int pos, System.Action dropOutside)
        {
            bg.raycastTarget = true;
            RectTransform rt = bg.rectTransform; Vector2 start = Vector2.zero;
            var tr = bg.gameObject.GetComponent<EventTrigger>(); if (tr == null) tr = bg.gameObject.AddComponent<EventTrigger>();
            void On(EventTriggerType t, System.Action<PointerEventData> a) { var en = new EventTrigger.Entry { eventID = t }; en.callback.AddListener(d => a((PointerEventData)d)); tr.triggers.Add(en); }
            On(EventTriggerType.BeginDrag, e => { dragging = true; start = rt.anchoredPosition; rt.SetAsLastSibling(); bg.color = new Color(1f, .83f, .3f, .3f); });
            On(EventTriggerType.Drag, e => rt.anchoredPosition += e.delta / input.Canvas.scaleFactor);
            On(EventTriggerType.EndDrag, e =>
            {
                dragging = false;
                if (!RectTransformUtility.RectangleContainsScreenPoint(panel, e.position)) dropOutside?.Invoke();
                else if (pos >= 0)
                {
                    int to = Mathf.Clamp(Mathf.RoundToInt(-rt.anchoredPosition.y / (90 * UiKit.K)), 0, 2);
                    if (to != pos) { var l = new List<int>(f.order); int v = l[pos]; l.RemoveAt(pos); l.Insert(to, v); for (int i = 0; i < 3; i++) f.order[i] = l[i]; }
                }
                key = "";
            });
        }

        string State()
        {
            var sb = new System.Text.StringBuilder();
            for (int k = 0; k < 3; k++) { var g = f.guns[k]; sb.Append(g == null ? "-" : g.def.Look + g.mag + "/" + g.reserve).Append(f.order[k]).Append('|'); }
            foreach (var kv in f.spare) sb.Append(kv.Key).Append(kv.Value);
            sb.Append(f.meds).Append(',').Append(f.frost).Append(',').Append(Mathf.RoundToInt(f.armor)).Append(f.armorLv).Append(',').Append(f.scopes).Append(',').Append(f.scope)
              .Append(',').Append(f.helmetLv).Append(Mathf.RoundToInt(f.helmet)).Append(',').Append(f.bagLv);
            return sb.ToString();
        }

        void Rebuild()
        {
            foreach (Transform c in gear) Destroy(c.gameObject);
            foreach (Transform c in guns) Destroy(c.gameObject);
            foreach (Transform c in supply) Destroy(c.gameObject);
            float y = 0;
            GearCard(ref y, UiKit.Get("icon_vest"), "Vest", f.armor > 0 ? Mathf.Max(1, f.armorLv) : 0, f.armor > 0 ? $"{Mathf.RoundToInt(f.armor)} / 100" : "none", () => f.DropArmor());
            GearCard(ref y, Item("helmet" + Mathf.Max(1, f.helmetLv)), "Helmet", f.helmetLv, f.helmetLv > 0 ? $"{Mathf.RoundToInt(f.helmet)} / 100" : "none", () => f.DropHelmet());
            GearCard(ref y, Item("bag" + Mathf.Max(1, f.bagLv)), "Backpack", f.bagLv, f.bagLv > 0 ? $"carries x{Fighter.BagMult[f.bagLv]:0.#}" : "basic", () => f.DropBag());
            y = 0;
            for (int k = 0; k < 3; k++) GunCard(ref y, k);
            tileN = 0;
            foreach (string id in new[] { "rifle", "smg", "shotgun", "sniper", "pistol" })
            {
                int n = f.AmmoOf(id); if (n <= 0) continue;
                string wid = id;
                Supply(UiKit.Get("icon_ammo"), Tuning.Weapon(id).name + " ammo", n.ToString(), () => f.DropAmmo(wid));
            }
            if (f.meds > 0) Supply(UiKit.Get("icon_heal"), "Medkit", "×" + f.meds, () => f.DropMed());
            if (f.frost > 0) Supply(UiKit.Get("icon_frost"), "Ice wall", "×" + f.frost, () => f.DropFrost());
            foreach (int z in new[] { 2, 4, 8 })
            {
                if ((f.scopes & Fighter.ScopeBit(z)) == 0) continue;
                int zz = z; bool on = f.scope == z;
                Supply(UiKit.Get("icon_scope"), $"×{z} scope", on ? "ON" : "", () => f.DropScope(zz), () => f.UseScope(on ? 0 : zz), on);
            }
            if (tileN == 0) Label(supply, new Vector2(0, 1), Vector2.zero, new Vector2(200, 20), 11, TextAnchor.UpperLeft, Dim, "No supplies yet");
            capText.text = $"{f.Load} / {f.Capacity}";
        }

        void Update()
        {
            // slide in from the left
            slide = Mathf.MoveTowards(slide, 1, Time.deltaTime * 6f);
            panel.anchoredPosition = new Vector2(Mathf.Lerp(-W - 20, 6, 1 - (1 - slide) * (1 - slide)) * UiKit.K, 0);
            if (!f.Alive || f.air != 0) { Destroy(gameObject); open = null; return; }
            string k = State();
            if (k != key && !dragging) { key = k; Rebuild(); }
        }

        void OnDestroy() { if (open == this) open = null; }
    }
}
