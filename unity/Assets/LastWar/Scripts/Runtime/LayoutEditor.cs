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

        TouchControls tc; RectTransform bar, selected; string selName; Outline mark;
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
            foreach (var (n, rt) in tc.Editable) s.items.Add(new Item { n = n, x = rt.anchoredPosition.x, y = rt.anchoredPosition.y, s = rt.localScale.x, h = rt.localScale.y, a = Alpha.TryGetValue(rt, out float a) ? a : 1f, hide = HiddenSet.Contains(rt) });
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
            var shade = UiKit.Img(c, "EditShade", null, new Vector2(.5f, .5f), Vector2.zero, new Vector2(1400, 1400), new Color(0, 0, 0, .3f));
            shade.rectTransform.pivot = new Vector2(.5f, .5f); shade.transform.SetSiblingIndex(0); shadeGo = shade.gameObject;
            foreach (var (n, rt) in tc.Editable) rt.SetAsLastSibling();
            Rebuild();
        }
        GameObject shadeGo;

        void Rebuild()
        {
            if (bar != null) Destroy(bar.gameObject);
            Transform c = tc.Canvas.transform;
            var panel = UiKit.Img(c, "EditBar", UiKit.Panel, new Vector2(.5f, 1), new Vector2(0, -40), new Vector2(380, selected != null ? 214 : 92), Color.white, true);
            bar = panel.rectTransform; bar.pivot = new Vector2(.5f, 1);
            float y = 12;
            SettingsPanel.Label(bar, ref y, selected == null ? "TAP A BUTTON TO EDIT IT · DRAG TO MOVE" : selName.ToUpper());
            if (selected != null)
            {
                RectTransform sel = selected;
                var wl = SettingsPanel.Label(bar, ref y, $"WIDTH  {sel.localScale.x:0.00}");
                SettingsPanel.Slider(bar, ref y, 0.5f, 1.8f, sel.localScale.x, v => { sel.localScale = new Vector3(v, sel.localScale.y, 1); wl.text = $"WIDTH  {v:0.00}"; });
                var hl = SettingsPanel.Label(bar, ref y, $"HEIGHT  {sel.localScale.y:0.00}");
                SettingsPanel.Slider(bar, ref y, 0.5f, 1.8f, sel.localScale.y, v => { sel.localScale = new Vector3(sel.localScale.x, v, 1); hl.text = $"HEIGHT  {v:0.00}"; });
                float a0 = Alpha.TryGetValue(sel, out float av) ? av : 1f;
                var op = SettingsPanel.Label(bar, ref y, $"SEE-THROUGH  {a0:0.00}");
                SettingsPanel.Slider(bar, ref y, 0.25f, 1f, a0, v => { Alpha[sel] = v; Refresh(sel); op.text = $"SEE-THROUGH  {v:0.00}"; });
                bool hidden = HiddenSet.Contains(sel);
                Button(hidden ? "SHOW" : "HIDE", new Vector2(144, 12), hidden ? new Color(.35f, .6f, .9f) : new Color(.75f, .22f, .17f), Color.white, () => { SetHidden(sel, !HiddenSet.Contains(sel)); Rebuild(); });
            }
            Button("RESET", new Vector2(18, 12), new Color(.4f, .43f, .46f), Color.white, ResetAll);
            Button("DONE", new Vector2(270, 12), new Color(.47f, .78f, .3f), new Color(.06f, .13f, .03f), Done);
        }

        void Button(string label, Vector2 pos, Color bg, Color fg, Action a)
        {
            var b = UiKit.Img(bar, label, UiKit.Round, Vector2.zero, pos, new Vector2(92, 26), bg, true);
            var t = UiKit.Txt(b.rectTransform, new Vector2(.5f, .5f), Vector2.zero, new Vector2(92, 26), 11, TextAnchor.MiddleCenter, fg); t.rectTransform.pivot = new Vector2(.5f, .5f); t.text = label;
            SettingsPanel.Tap(b.gameObject, a);
        }

        void ResetAll()
        {
            PlayerPrefs.DeleteKey(Key);
            foreach (var (n, rt) in tc.Editable) { if (defaults.TryGetValue(n, out Vector2 p)) rt.anchoredPosition = p; rt.localScale = Vector3.one; Alpha[rt] = 1f; HiddenSet.Remove(rt); Refresh(rt); }
            Select(null, null);
        }

        void Done()
        {
            SaveAll();
            Select(null, null);
            tc.Editing = false;
            editingNow = false; foreach (var (_, rt) in tc.Editable) Refresh(rt);
            Destroy(shadeGo); Destroy(bar.gameObject); Destroy(gameObject);
            onDone?.Invoke();
        }

        void Select(string n, RectTransform rt)
        {
            if (mark != null) Destroy(mark);
            selected = rt; selName = n;
            if (rt != null) { var img = rt.GetComponent<Graphic>(); if (img != null) { mark = img.gameObject.AddComponent<Outline>(); mark.effectColor = new Color(1f, .83f, .3f); mark.effectDistance = new Vector2(4, 4); } }
            Rebuild();
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
