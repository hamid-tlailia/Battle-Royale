using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LastWar
{
    /// <summary>
    /// HUD layout editor (the web game's): drag any button or the joystick, then set its size and opacity;
    /// saved on the device and applied every match. RESET puts everything back.
    /// </summary>
    public class LayoutEditor : MonoBehaviour
    {
        /// <summary>s = width, h = height (scale; 0 = same as the width), a = opacity, hide = not shown in the match</summary>
        [Serializable] class Item { public string n; public float x, y, s = 1, h, a = 1; public bool hide; }
        [Serializable] class Saved { public List<Item> items = new List<Item>(); }
        const string Key = "lw.layout";
        static readonly Dictionary<string, Vector2> defaults = new Dictionary<string, Vector2>();

        TouchControls tc; RectTransform bar, selected; string selName;
        Vector2 last; bool dragging;

        static CanvasGroup Group(RectTransform rt) => rt.GetComponent<CanvasGroup>() ?? rt.gameObject.AddComponent<CanvasGroup>();

        /// <summary>apply the saved layout (called once the controls are built)</summary>
        public static void Apply(TouchControls tc)
        {
            defaults.Clear();
            foreach (var (n, rt) in tc.Editable) defaults[n] = rt.anchoredPosition;
            Saved s = Load();
            foreach (Item it in s.items)
            {
                var e = tc.Editable.Find(x => x.name == it.n);
                if (e.rt == null) continue;
                Put(e.rt, it);
            }
        }

        static void Put(RectTransform rt, Item it)
        {
            rt.anchoredPosition = new Vector2(it.x, it.y);
            float w = Mathf.Clamp(it.s, 0.5f, 1.8f), h = Mathf.Clamp(it.h > 0 ? it.h : it.s, 0.5f, 1.8f);
            rt.localScale = new Vector3(w, h, 1);
            Alpha[rt] = Mathf.Clamp(it.a, 0.25f, 1f);
            SetHidden(rt, it.hide);
        }

        /// <summary>the opacity chosen for each control (the hidden ones keep it for when they are shown again)</summary>
        static readonly Dictionary<RectTransform, float> Alpha = new Dictionary<RectTransform, float>();
        static readonly HashSet<RectTransform> HiddenSet = new HashSet<RectTransform>();
        public static bool IsHidden(RectTransform rt) => rt != null && HiddenSet.Contains(rt);
        static bool editingNow;

        /// <summary>hidden: invisible and not touchable in the match; in the editor faded with the rest so it can be shown again</summary>
        static void SetHidden(RectTransform rt, bool hide)
        {
            if (hide) HiddenSet.Add(rt); else HiddenSet.Remove(rt);
            Refresh(rt);
        }

        static void Refresh(RectTransform rt)
        {
            var g = Group(rt);
            float a = Alpha.TryGetValue(rt, out float v) ? v : 1f;
            bool hide = HiddenSet.Contains(rt);
            g.alpha = hide ? (editingNow ? 0.25f : 0f) : a;
            g.blocksRaycasts = !hide || editingNow;
        }

        /// <summary>a piece added after the controls were built (weapon box, minimap)</summary>
        public static void ApplyOne(string n, RectTransform rt)
        {
            defaults[n] = rt.anchoredPosition;
            Item it = Load().items.Find(x => x.n == n);
            if (it == null) return;
            Put(rt, it);
        }

        static Saved Load()
        {
            try { string j = PlayerPrefs.GetString(Key, ""); if (!string.IsNullOrEmpty(j)) return JsonUtility.FromJson<Saved>(j) ?? new Saved(); } catch { }
            return new Saved();
        }

        void SaveAll()
        {
            var s = new Saved();
            foreach (var (n, rt) in tc.Editable) s.items.Add(new Item { n = n, x = rt.anchoredPosition.x, y = rt.anchoredPosition.y, s = rt.localScale.x, h = rt.localScale.y, a = Alpha.TryGetValue(rt, out float av) ? av : 1f, hide = HiddenSet.Contains(rt) });
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(s)); PlayerPrefs.Save();
        }

        Action onDone;
        public static void Open(TouchControls tc, Action onDone = null)
        {
            var go = new GameObject("LayoutEditor");
            var ed = go.AddComponent<LayoutEditor>();
            ed.tc = tc; tc.Editing = true; ed.onDone = onDone;
            editingNow = true; foreach (var (_, rt) in tc.Editable) Refresh(rt);
            ed.BuildBar();
        }

        void BuildBar()
        {
            Transform c = tc.Canvas.transform;
            var shade = UiKit.Img(c, "EditShade", null, new Vector2(.5f, .5f), Vector2.zero, new Vector2(1400, 1400), new Color(0, 0, 0, .25f));
            shade.rectTransform.pivot = new Vector2(.5f, .5f); UiKit.Fill(shade.rectTransform); shade.transform.SetSiblingIndex(0); shadeGo = shade.gameObject;
            foreach (var (n, rt) in tc.Editable) rt.SetAsLastSibling();
            // dashed gold frame around everything you can move (web #hud.editing [data-hud])
            foreach (var (n, rt) in tc.Editable)
            {
                var fr = new GameObject("editFrame", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                fr.transform.SetParent(rt, false); fr.raycastTarget = false; fr.sprite = Dashed; fr.type = Image.Type.Tiled; fr.color = Gold;
                fr.rectTransform.anchorMin = Vector2.zero; fr.rectTransform.anchorMax = Vector2.one;
                bool btn = rt.rect.width > 0 && Mathf.Abs(rt.rect.width - rt.rect.height) < 2f && rt.GetComponent<Image>() != null && rt.GetComponent<Image>().sprite == UiKit.Get("btn");
                float inset = btn ? rt.rect.width * 0.11f : -4f * UiKit.K; // the button pictures carry a shadow margin
                fr.rectTransform.offsetMin = new Vector2(inset, inset); fr.rectTransform.offsetMax = new Vector2(-inset, -inset);
                frames[rt] = fr;
            }
            BuildTopBar();
        }
        GameObject shadeGo;
        readonly Dictionary<RectTransform, Image> frames = new Dictionary<RectTransform, Image>();
        static readonly Color Gold = new Color(1f, .83f, .3f), Green = new Color(.44f, 1f, .44f);
        static Sprite dashed;
        /// <summary>tiled square with a dashed 3 px edge</summary>
        static Sprite Dashed
        {
            get
            {
                if (dashed != null) return dashed;
                const int S = 24, W = 3; var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
                var px = new Color32[S * S];
                for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
                    {
                        bool edgeX = x < W || x >= S - W, edgeY = y < W || y >= S - W;
                        bool on = (edgeY && (x % 8) < 5) || (edgeX && (y % 8) < 5);
                        px[y * S + x] = new Color32(255, 255, 255, (byte)(on ? 255 : 0));
                    }
                tex.SetPixels32(px); tex.Apply();
                return dashed = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, new Vector4(W, W, W, W));
            }
        }

        Slider wS, hS, aS; Text hideText; Image hideBg;
        /// <summary>the web's layout bar: one dark strip across the top - Width, Height, See-through, HIDE, RESET, CANCEL, SAVE</summary>
        void BuildTopBar()
        {
            Transform c = tc.Root;
            float avail = ((RectTransform)c).rect.width / UiKit.K - 16, need = 930, f = Mathf.Min(1f, avail / need);
            var panel = UiKit.Img(c, "EditBar", UiKit.Round, new Vector2(.5f, 1), new Vector2(0, -8), new Vector2(need * f, 44 * f), new Color(10 / 255f, 14 / 255f, 12 / 255f, .94f), true);
            bar = panel.rectTransform; bar.pivot = new Vector2(.5f, 1);
            panel.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, .6f);
            float x = 14;
            Text Lbl(string t) { var l = UiKit.Txt(bar, new Vector2(0, .5f), new Vector2(x * f, 0), new Vector2(90, 30) * f, 14 * f, TextAnchor.MiddleLeft, new Color(.87f, .9f, .91f)); l.fontStyle = FontStyle.Normal; l.text = t; l.horizontalOverflow = HorizontalWrapMode.Overflow; x += l.preferredWidth / UiKit.K / f + 10; return l; }
            Slider Sl(float min, float max, Action<float> on) { var s = MakeSlider(bar, new Vector2(x, 0) * f, 96 * f, min, max, on); x += 106; return s; }
            Lbl("Width"); wS = Sl(0.5f, 1.8f, v => { if (selected) selected.localScale = new Vector3(v, selected.localScale.y, 1); });
            Lbl("Height"); hS = Sl(0.5f, 1.8f, v => { if (selected) selected.localScale = new Vector3(selected.localScale.x, v, 1); });
            Lbl("See-through"); aS = Sl(0.25f, 1f, v => { if (selected) { Alpha[selected] = v; Refresh(selected); } });
            hideBg = Btn(ref x, f, "HIDE", new Color(.75f, .2f, .16f), Color.white, () => { if (selected) { SetHidden(selected, !HiddenSet.Contains(selected)); Sync(); } });
            hideText = hideBg.GetComponentInChildren<Text>();
            Btn(ref x, f, "RESET", new Color(.27f, .29f, .31f), Color.white, ResetAll);
            Btn(ref x, f, "CANCEL", new Color(.27f, .29f, .31f), Color.white, Cancel);
            Btn(ref x, f, "SAVE", new Color(.42f, .76f, .24f), new Color(.06f, .13f, .03f), Done);
            Sync();
        }

        Image Btn(ref float x, float f, string label, Color bg, Color fg, Action a)
        {
            float w = label.Length * 13 + 26;
            var b = UiKit.Img(bar, label, UiKit.Round, new Vector2(0, .5f), new Vector2(x * f, 0), new Vector2(w, 32) * f, bg, true);
            b.rectTransform.pivot = new Vector2(0, .5f);
            var t = UiKit.Txt(b.rectTransform, new Vector2(.5f, .5f), Vector2.zero, new Vector2(w, 32) * f, 15 * f, TextAnchor.MiddleCenter, fg); t.rectTransform.pivot = new Vector2(.5f, .5f); t.text = label;
            if (fg != Color.white) Destroy(t.GetComponent<Shadow>());
            SettingsPanel.Tap(b.gameObject, a);
            x += w + 8;
            return b;
        }

        static Slider MakeSlider(RectTransform p, Vector2 pos, float width, float min, float max, Action<float> on)
        {
            const float K = UiKit.K;
            var go = new GameObject("slider", typeof(RectTransform), typeof(Slider));
            var rt = (RectTransform)go.transform; rt.SetParent(p, false); rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, .5f);
            rt.anchoredPosition = pos * K; rt.sizeDelta = new Vector2(width, 22) * K;
            var track = UiKit.Img(rt, "track", UiKit.Round, Vector2.zero, Vector2.zero, Vector2.one, new Color(.04f, .05f, .06f));
            track.rectTransform.anchorMin = new Vector2(0, .35f); track.rectTransform.anchorMax = new Vector2(1, .65f); track.rectTransform.offsetMin = track.rectTransform.offsetMax = Vector2.zero;
            var fa = new GameObject("fill", typeof(RectTransform)).GetComponent<RectTransform>(); fa.SetParent(rt, false); fa.anchorMin = new Vector2(0, .35f); fa.anchorMax = new Vector2(1, .65f); fa.offsetMin = fa.offsetMax = Vector2.zero;
            var fill = UiKit.Img(fa, "f", UiKit.Round, Vector2.zero, Vector2.zero, Vector2.one, new Color(1f, .83f, .3f, .7f));
            var ha = new GameObject("handles", typeof(RectTransform)).GetComponent<RectTransform>(); ha.SetParent(rt, false); ha.anchorMin = Vector2.zero; ha.anchorMax = Vector2.one; ha.offsetMin = ha.offsetMax = Vector2.zero;
            var handle = UiKit.Img(ha, "h", null, new Vector2(0, .5f), Vector2.zero, new Vector2(22, 22), new Color(0, 0, 0, 0), true);
            handle.rectTransform.pivot = new Vector2(.5f, .5f);
            var diamond = UiKit.Img(handle.rectTransform, "d", null, new Vector2(.5f, .5f), Vector2.zero, new Vector2(14, 14), Gold);
            diamond.rectTransform.pivot = new Vector2(.5f, .5f); diamond.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
            diamond.gameObject.AddComponent<Outline>().effectColor = new Color(.16f, .11f, .02f);
            var sl = go.GetComponent<Slider>(); sl.fillRect = fill.rectTransform; sl.handleRect = handle.rectTransform; sl.targetGraphic = handle;
            sl.minValue = min; sl.maxValue = max; sl.onValueChanged.AddListener(v => on(v));
            return sl;
        }

        /// <summary>sliders / HIDE follow the selected piece; frames: gold, the selected one green, hidden ones red</summary>
        void Sync()
        {
            bool has = selected != null;
            foreach (var s in new[] { wS, hS, aS }) s.interactable = has;
            if (has)
            {
                wS.SetValueWithoutNotify(selected.localScale.x); hS.SetValueWithoutNotify(selected.localScale.y);
                aS.SetValueWithoutNotify(Alpha.TryGetValue(selected, out float a) ? a : 1f);
            }
            bool hidden = has && HiddenSet.Contains(selected);
            hideText.text = hidden ? "SHOW" : "HIDE"; hideBg.color = hidden ? new Color(.3f, .55f, .9f) : new Color(.75f, .2f, .16f);
            foreach (var kv in frames) kv.Value.color = kv.Key == selected ? Green : HiddenSet.Contains(kv.Key) ? new Color(1f, .42f, .42f) : Gold;
        }

        void ResetAll()
        {
            PlayerPrefs.DeleteKey(Key);
            foreach (var (n, rt) in tc.Editable) { if (defaults.TryGetValue(n, out Vector2 p)) rt.anchoredPosition = p; rt.localScale = Vector3.one; Alpha[rt] = 1f; HiddenSet.Remove(rt); Refresh(rt); }
            Select(null, null);
        }

        /// <summary>leave without saving: everything back to the saved layout</summary>
        void Cancel()
        {
            Saved s = Load();
            foreach (var (n, rt) in tc.Editable)
            {
                Item it = s.items.Find(x => x.n == n);
                if (it != null) Put(rt, it);
                else { if (defaults.TryGetValue(n, out Vector2 p)) rt.anchoredPosition = p; rt.localScale = Vector3.one; Alpha[rt] = 1f; HiddenSet.Remove(rt); }
            }
            Finish();
        }

        void Done() { SaveAll(); Finish(); }

        void Finish()
        {
            selected = null;
            tc.Editing = false;
            editingNow = false; foreach (var (_, rt) in tc.Editable) Refresh(rt);
            foreach (var kv in frames) if (kv.Value != null) Destroy(kv.Value.gameObject);
            frames.Clear();
            Destroy(shadeGo); Destroy(bar.gameObject); Destroy(gameObject);
            onDone?.Invoke();
        }

        void Select(string n, RectTransform rt)
        {
            selected = rt; selName = n;
            Sync();
        }

        void Update()
        {
            bool down, held; Vector2 p;
            if (Input.touchCount > 0) { Touch t = Input.GetTouch(0); p = t.position; down = t.phase == TouchPhase.Began; held = t.phase != TouchPhase.Ended && t.phase != TouchPhase.Canceled; }
            else { p = Input.mousePosition; down = Input.GetMouseButtonDown(0); held = Input.GetMouseButton(0); }
            if (down)
            {
                dragging = false;
                if (bar != null && RectTransformUtility.RectangleContainsScreenPoint(bar, p)) return;
                for (int i = tc.Editable.Count - 1; i >= 0; i--)
                {
                    var (n, rt) = tc.Editable[i];
                    if (rt.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(rt, p)) { if (rt != selected) Select(n, rt); dragging = true; last = p; break; }
                }
            }
            else if (held && dragging && selected != null)
            {
                selected.anchoredPosition += (p - last) / tc.Canvas.scaleFactor; last = p;
            }
            if (!held) dragging = false;
        }
    }
}
