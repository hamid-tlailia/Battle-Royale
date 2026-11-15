using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LastWar
{
    /// <summary>In-match settings (web .settings-panel look): look sensitivity, aim assist, graphics, leave match.</summary>
    public static class SettingsPanel
    {
        static GameObject open;
        public static bool IsOpen => open != null;
        public static void CloseAll() => Close();
        static readonly Color Gold = new Color(1f, .83f, .3f), Ink = new Color(27 / 255f, 20 / 255f, 4 / 255f);

        public static void Open(Canvas canvas, bool inMatch)
        {
            if (open != null) { UnityEngine.Object.Destroy(open); open = null; return; }
            Transform c = canvas.transform;
            var shade = UiKit.Img(c, "SettingsShade", null, new Vector2(.5f, .5f), Vector2.zero, new Vector2(1400, 1400), new Color(0, 0, 0, .45f), true);
            shade.rectTransform.pivot = new Vector2(.5f, .5f);
            open = shade.gameObject;
            Tap(shade.gameObject, Close);
            var panel = UiKit.Img(shade.rectTransform, "panel", UiKit.Panel, new Vector2(.5f, .5f), Vector2.zero, new Vector2(340, 330), Color.white, true);
            panel.rectTransform.pivot = new Vector2(.5f, .5f);
            Tap(panel.gameObject, () => { }); // a tap on the panel is not a tap on the shade behind it
            Transform p = panel.transform;
            var title = UiKit.Txt(p, new Vector2(0, 1), new Vector2(18, -12), new Vector2(300, 22), 15, TextAnchor.UpperLeft, Gold); title.text = "SETTINGS";
            UiKit.CloseButton(p, new Vector2(1, 1), new Vector2(-10, -8), 28, Close);
            // the settings scroll inside the panel (they are taller than a phone screen)
            var view = new GameObject("view", typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(ScrollRect)).GetComponent<RectTransform>();
            view.SetParent(p, false); view.anchorMin = Vector2.zero; view.anchorMax = Vector2.one;
            view.offsetMin = new Vector2(14, inMatch ? 92 : 52) * UiKit.K; view.offsetMax = new Vector2(-14, -40) * UiKit.K;
            view.GetComponent<Image>().color = new Color(1, 1, 1, 0);
            var content = new GameObject("content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(view, false); content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(.5f, 1);
            var sr = view.GetComponent<ScrollRect>(); sr.content = content; sr.horizontal = false; sr.movementType = ScrollRect.MovementType.Clamped;
            content.sizeDelta = new Vector2(0, Fill(content, 0, 0, 312 * UiKit.K, UiKit.K) + 6 * UiKit.K);
            if (inMatch)
            {
                var leave = UiKit.Img(p, "leave", UiKit.Round, new Vector2(0, 0), new Vector2(18, 14), new Vector2(140, 30), new Color(.82f, .27f, .2f), true);
                var lt = UiKit.Txt(leave.rectTransform, new Vector2(.5f, .5f), Vector2.zero, new Vector2(140, 30), 12, TextAnchor.MiddleCenter, Color.white); lt.rectTransform.pivot = new Vector2(.5f, .5f); lt.text = "LEAVE MATCH";
                Tap(leave.gameObject, () => { Close(); if (GameBoot.Instance != null) GameBoot.Instance.Restart(); });
            }
            if (inMatch)
            {
                var cust = UiKit.Img(p, "custom", UiKit.Round, new Vector2(.5f, 0), new Vector2(0, 52), new Vector2(200, 28), new Color(.23f, .26f, .28f), true);
                cust.rectTransform.pivot = new Vector2(.5f, 0);
                var cu = UiKit.Txt(cust.rectTransform, new Vector2(.5f, .5f), Vector2.zero, new Vector2(200, 28), 11, TextAnchor.MiddleCenter, Gold); cu.rectTransform.pivot = new Vector2(.5f, .5f); cu.text = "CUSTOMIZE BUTTONS";
                Tap(cust.gameObject, () => { Close(); var tc = UnityEngine.Object.FindFirstObjectByType<TouchControls>(); if (tc != null) LayoutEditor.Open(tc); });
            }
            var close = UiKit.Img(p, "close", UiKit.Round, new Vector2(1, 0), new Vector2(-18, 14), new Vector2(110, 30), new Color(.47f, .78f, .3f), true);
            close.rectTransform.pivot = new Vector2(1, 0);
            var ct = UiKit.Txt(close.rectTransform, new Vector2(.5f, .5f), Vector2.zero, new Vector2(110, 30), 12, TextAnchor.MiddleCenter, new Color(.06f, .13f, .03f)); ct.rectTransform.pivot = new Vector2(.5f, .5f); ct.text = "CLOSE";
            Tap(close.gameObject, Close);
        }

        static void Close() { if (open != null) UnityEngine.Object.Destroy(open); open = null; }

        internal static void Tap(GameObject go, Action a)
        {
            var tr = go.AddComponent<EventTrigger>(); var e = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            e.callback.AddListener(ev => { ((PointerEventData)ev).Use(); a(); }); tr.triggers.Add(e);
        }

        internal static Text Label(Transform p, ref float y, string text)
        {
            var t = UiKit.Txt(p, new Vector2(0, 1), new Vector2(18, -y), new Vector2(300, 16), 11, TextAnchor.UpperLeft, new Color(.87f, .9f, .91f)); t.text = text;
            y += 18; return t;
        }

        /// <summary>web range: bold label left, gold value right; an 8px inset groove with a fading gold fill and a
        /// small gold diamond thumb (20px square turned 45deg, like the web). x / y / w in canvas units (top-left of the
        /// parent), fk = canvas units per CSS px. Returns the height used.</summary>
        internal static float Range(Transform p, float x, float y, float w, float fk, string label, float min, float max, float val, string fmt, Action<float> on)
        {
            const float K = UiKit.K;
            var l = UiKit.Txt(p, new Vector2(0, 1), Vector2.zero, Vector2.one, 12 * fk / K, TextAnchor.MiddleLeft, new Color(.87f, .9f, .91f));
            l.fontStyle = FontStyle.Bold;
            l.rectTransform.anchoredPosition = new Vector2(x, -y); l.rectTransform.sizeDelta = new Vector2(w, 18 * fk); l.text = label;
            var v = UiKit.Txt(p, new Vector2(0, 1), Vector2.zero, Vector2.one, 12 * fk / K, TextAnchor.MiddleRight, Gold);
            v.fontStyle = FontStyle.Bold;
            v.rectTransform.anchoredPosition = new Vector2(x, -y); v.rectTransform.sizeDelta = new Vector2(w, 18 * fk); v.text = val.ToString(fmt);
            y += 20 * fk;
            // the slider's own box: 22px tall, the full width (the thumb's half-width is kept inside)
            var go = new GameObject("slider", typeof(RectTransform), typeof(Image), typeof(ScrollSlider));
            var rt = (RectTransform)go.transform; rt.SetParent(p, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y); rt.sizeDelta = new Vector2(w, 24 * fk);
            go.GetComponent<Image>().color = new Color(0, 0, 0, 0); // catches taps on the whole row height
            var groove = UiKit.Img(rt, "groove", null, Vector2.zero, Vector2.zero, Vector2.one, new Color(11 / 255f, 13 / 255f, 15 / 255f));
            groove.rectTransform.anchorMin = new Vector2(0, .5f); groove.rectTransform.anchorMax = new Vector2(1, .5f);
            groove.rectTransform.pivot = new Vector2(.5f, .5f); groove.rectTransform.anchoredPosition = Vector2.zero; groove.rectTransform.sizeDelta = new Vector2(0, 8 * fk);
            groove.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 1f);
            var fa = new GameObject("fill", typeof(RectTransform)).GetComponent<RectTransform>(); fa.SetParent(groove.rectTransform, false);
            fa.anchorMin = Vector2.zero; fa.anchorMax = Vector2.one; fa.offsetMin = fa.offsetMax = Vector2.zero;
            var fill = UiKit.Img(fa, "f", null, Vector2.zero, Vector2.zero, Vector2.one, new Color(1f, .83f, .3f, .55f));
            var ha = new GameObject("handles", typeof(RectTransform)).GetComponent<RectTransform>(); ha.SetParent(rt, false);
            ha.anchorMin = Vector2.zero; ha.anchorMax = Vector2.one; ha.offsetMin = new Vector2(10 * fk, 0); ha.offsetMax = new Vector2(-10 * fk, 0);
            // (the Slider stretches its handle over the full height: the diamond is a fixed-size child of it)
            var handle = UiKit.Img(ha, "h", null, new Vector2(0, .5f), Vector2.zero, Vector2.one, new Color(0, 0, 0, 0), true);
            handle.rectTransform.pivot = new Vector2(.5f, .5f); handle.rectTransform.sizeDelta = new Vector2(26 * fk, 0);
            var gem = UiKit.Img(handle.rectTransform, "gem", UiKit.Diamond, new Vector2(.5f, .5f), Vector2.zero, Vector2.one, Color.white);
            gem.rectTransform.pivot = new Vector2(.5f, .5f); gem.rectTransform.sizeDelta = new Vector2(26, 22) * fk; // a diamond, not a tall spike
            var sl = go.GetComponent<ScrollSlider>(); sl.fillRect = fill.rectTransform; sl.handleRect = handle.rectTransform; sl.targetGraphic = handle;
            sl.transition = Selectable.Transition.None;
            sl.minValue = min; sl.maxValue = max; sl.value = val;
            sl.onValueChanged.AddListener(nv => { v.text = nv.ToString(fmt); on(nv); });
            return 20 * fk + 24 * fk + 14 * fk;
        }

        /// <summary>the web's switch (checkbox): a dark 42x22 groove, the knob grey on the left when off, gold on the
        /// right over a green groove when on, the label after it. Returns the height used (canvas units).</summary>
        internal static float Toggle(Transform p, float x, float y, float w, float fk, string label, bool val, Action<bool> on)
        {
            var sw = UiKit.Img(p, "switch", null, new Vector2(0, 1), Vector2.zero, Vector2.one, new Color(11 / 255f, 13 / 255f, 15 / 255f), true);
            sw.rectTransform.anchoredPosition = new Vector2(x, -y); sw.rectTransform.sizeDelta = new Vector2(42, 22) * fk;
            sw.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 1f);
            var knob = UiKit.Img(sw.rectTransform, "knob", null, new Vector2(0, .5f), Vector2.zero, Vector2.one, Color.white);
            knob.rectTransform.pivot = new Vector2(0, .5f); knob.rectTransform.sizeDelta = new Vector2(18, 16) * fk;
            var l = UiKit.Txt(p, new Vector2(0, 1), Vector2.zero, Vector2.one, 13 * fk / UiKit.K, TextAnchor.MiddleLeft, new Color(.87f, .9f, .91f));
            l.fontStyle = FontStyle.Bold;
            l.rectTransform.anchoredPosition = new Vector2(x + 52 * fk, -y); l.rectTransform.sizeDelta = new Vector2(w - 52 * fk, 22 * fk); l.text = label;
            l.raycastTarget = true;
            bool cur = val;
            void Paint()
            {
                sw.color = cur ? new Color(.33f, .6f, .19f) : new Color(11 / 255f, 13 / 255f, 15 / 255f);
                knob.color = cur ? Gold : new Color(.42f, .46f, .49f);
                knob.rectTransform.anchoredPosition = new Vector2((cur ? 21 : 3) * fk, 0);
            }
            void Flip() { cur = !cur; Paint(); on(cur); }
            Clickable.Add(sw.gameObject, Flip); Clickable.Add(l.gameObject, Flip);
            Paint();
            return 22 * fk + 14 * fk;
        }

        /// <summary>a section title followed by a segmented choice (web .qual)</summary>
        internal static float ChoiceRow(Transform p, float x, float y, float w, float fk, string label, string[] opts, int sel, Action<int> on)
        {
            var l = UiKit.Txt(p, new Vector2(0, 1), Vector2.zero, Vector2.one, 12 * fk / UiKit.K, TextAnchor.UpperLeft, new Color(.87f, .9f, .91f));
            l.rectTransform.anchoredPosition = new Vector2(x, -y); l.rectTransform.sizeDelta = new Vector2(w, 18 * fk); l.text = label;
            Segments(p, new Vector2(x, -(y + 22 * fk)), new Vector2(w, 30 * fk), 11 * fk / UiKit.K, opts, sel, on);
            return 22 * fk + 30 * fk + 12 * fk;
        }

        /// <summary>every setting, in the web's order (lobby page and the in-match panel share it); returns the height</summary>
        internal static float Fill(Transform p, float x, float y0, float w, float fk)
        {
            float y = y0;
            y += ChoiceRow(p, x, y, w, fk, "GRAPHICS", new[] { "HIGH", "MEDIUM", "LOW" }, 2 - Profile.Quality, i => Profile.Quality = 2 - i);
            y += Range(p, x, y, w, fk, "LOOK SENSITIVITY", 0.3f, 2.5f, Profile.Sensitivity, "0.00", v => Profile.Sensitivity = v);
            y += Range(p, x, y, w, fk, "SCOPE SENSITIVITY", 0.3f, 2.5f, Profile.ScopeSensitivity, "0.00", v => Profile.ScopeSensitivity = v);
            y += ChoiceRow(p, x, y, w, fk, "AIM", new[] { "ASSIST", "FULL CONTROL" }, Profile.AimAssist ? 0 : 1, i => { Profile.AimAssist = i == 0; PlayerController.AimAssistOn = i == 0; });
            y += Toggle(p, x, y, w, fk, "Auto pick-up (ammo, meds, guns...)", Profile.AutoPickup, v => Profile.AutoPickup = v);
            y += Toggle(p, x, y, w, fk, "Music", Profile.MusicOn, v => { Profile.MusicOn = v; if (v) Music.PlayLobbyIfIdle(); else Music.Stop(0f); });
            y += Toggle(p, x, y, w, fk, "Sound", Profile.SoundOn, v => Profile.SoundOn = v);
            return y - y0;
        }

        static void Choice(Transform p, ref float y, string[] opts, int sel, Action<int> on)
        {
            Segments(p, new Vector2(18, -y) * UiKit.K, new Vector2(300, 28) * UiKit.K, 11, opts, sel, on);
            y += 36;
        }

        /// <summary>
        /// The web's segmented choice (.settings-panel .qual): one near-black inset strip, the options plain grey text on it,
        /// the chosen one a gold slanted plate. pos / size in canvas units, font in CSS px.
        /// </summary>
        internal static void Segments(Transform p, Vector2 pos, Vector2 size, float font, string[] opts, int sel, Action<int> on)
        {
            var strip = UiKit.Img(p, "segments", null, new Vector2(0, 1), Vector2.zero, Vector2.one, new Color(11 / 255f, 13 / 255f, 15 / 255f), true);
            strip.rectTransform.anchoredPosition = pos; strip.rectTransform.sizeDelta = size;
            strip.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, .9f);
            float pad = 2f * UiKit.K * size.y / (28 * UiKit.K), bw = (size.x - pad * 2) / opts.Length;
            var imgs = new List<Image>(); var txts = new List<Text>();
            for (int i = 0; i < opts.Length; i++)
            {
                int idx = i;
                var b = UiKit.Img(strip.rectTransform, "opt", UiKit.GoldPlate, new Vector2(0, 1), Vector2.zero, Vector2.one, Color.white, true);
                b.rectTransform.anchoredPosition = new Vector2(pad + i * bw, -pad); b.rectTransform.sizeDelta = new Vector2(bw, size.y - pad * 2);
                var t = UiKit.Txt(b.rectTransform, new Vector2(.5f, .5f), Vector2.zero, Vector2.one, font, TextAnchor.MiddleCenter, Color.white);
                t.rectTransform.pivot = new Vector2(.5f, .5f); t.rectTransform.sizeDelta = b.rectTransform.sizeDelta; t.text = opts[i];
                imgs.Add(b); txts.Add(t);
                Clickable.Add(b.gameObject, () => { on(idx); Paint(idx); });
            }
            void Paint(int s)
            {
                for (int i = 0; i < imgs.Count; i++)
                {
                    bool chosen = i == s;
                    imgs[i].color = chosen ? Color.white : new Color(1, 1, 1, 0); // unchosen: just the text on the dark strip
                    txts[i].color = chosen ? new Color(27 / 255f, 20 / 255f, 4 / 255f) : new Color(154 / 255f, 164 / 255f, 170 / 255f);
                    var sh = txts[i].GetComponent<Shadow>(); if (sh != null) sh.enabled = !chosen;
                }
            }
            Paint(sel);
        }
    }

    /// <summary>a slider inside a scrolling page: a mostly-vertical drag scrolls the page instead of moving the thumb</summary>
    public class ScrollSlider : Slider, IBeginDragHandler, IEndDragHandler
    {
        ScrollRect scroll; bool passing;
        public void OnBeginDrag(PointerEventData e)
        {
            scroll = GetComponentInParent<ScrollRect>();
            passing = scroll != null && Mathf.Abs(e.delta.y) > Mathf.Abs(e.delta.x);
            if (passing) scroll.OnBeginDrag(e);
        }
        public override void OnDrag(PointerEventData e) { if (passing) scroll.OnDrag(e); else base.OnDrag(e); }
        public void OnEndDrag(PointerEventData e) { if (passing) scroll.OnEndDrag(e); passing = false; }
    }
}
