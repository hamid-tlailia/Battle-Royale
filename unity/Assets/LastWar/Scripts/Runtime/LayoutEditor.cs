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
        [Serializable] class Item { public string n; public float x, y, s = 1, a = 1; }
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
                e.rt.anchoredPosition = new Vector2(it.x, it.y);
                e.rt.localScale = Vector3.one * Mathf.Clamp(it.s, 0.5f, 1.8f);
                Group(e.rt).alpha = Mathf.Clamp(it.a, 0.25f, 1f);
            }
        }

        static Saved Load()
        {
            try { string j = PlayerPrefs.GetString(Key, ""); if (!string.IsNullOrEmpty(j)) return JsonUtility.FromJson<Saved>(j) ?? new Saved(); } catch { }
            return new Saved();
        }

        void SaveAll()
        {
            var s = new Saved();
            foreach (var (n, rt) in tc.Editable) s.items.Add(new Item { n = n, x = rt.anchoredPosition.x, y = rt.anchoredPosition.y, s = rt.localScale.x, a = Group(rt).alpha });
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(s)); PlayerPrefs.Save();
        }

        public static void Open(TouchControls tc)
        {
            var go = new GameObject("LayoutEditor");
            var ed = go.AddComponent<LayoutEditor>();
            ed.tc = tc; tc.Editing = true;
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
            var panel = UiKit.Img(c, "EditBar", UiKit.Get("Lobby/sheet"), new Vector2(.5f, 1), new Vector2(0, -50), new Vector2(360, selected != null ? 170 : 92), Color.white, true);
            bar = panel.rectTransform; bar.pivot = new Vector2(.5f, 1);
            float y = 12;
            SettingsPanel.Label(bar, ref y, selected == null ? "TAP A BUTTON TO EDIT IT · DRAG TO MOVE" : selName.ToUpper());
            if (selected != null)
            {
                var size = SettingsPanel.Label(bar, ref y, $"SIZE  {selected.localScale.x:0.00}");
                SettingsPanel.Slider(bar, ref y, 0.5f, 1.8f, selected.localScale.x, v => { selected.localScale = Vector3.one * v; size.text = $"SIZE  {v:0.00}"; });
                var op = SettingsPanel.Label(bar, ref y, $"OPACITY  {Group(selected).alpha:0.00}");
                SettingsPanel.Slider(bar, ref y, 0.25f, 1f, Group(selected).alpha, v => { Group(selected).alpha = v; op.text = $"OPACITY  {v:0.00}"; });
            }
            Button("RESET", new Vector2(18, 12), new Color(.82f, .27f, .2f), Color.white, ResetAll);
            Button("DONE", new Vector2(250, 12), new Color(.47f, .78f, .3f), new Color(.06f, .13f, .03f), Done);
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
            foreach (var (n, rt) in tc.Editable) { if (defaults.TryGetValue(n, out Vector2 p)) rt.anchoredPosition = p; rt.localScale = Vector3.one; Group(rt).alpha = 1f; }
            Select(null, null);
        }

        void Done()
        {
            SaveAll();
            Select(null, null);
            tc.Editing = false;
            Destroy(shadeGo); Destroy(bar.gameObject); Destroy(gameObject);
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
