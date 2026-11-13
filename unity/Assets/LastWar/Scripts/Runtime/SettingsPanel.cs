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
        static readonly Color Gold = new Color(1f, .83f, .3f), Ink = new Color(27 / 255f, 20 / 255f, 4 / 255f);

        public static void Open(Canvas canvas, bool inMatch)
        {
            if (open != null) { UnityEngine.Object.Destroy(open); open = null; return; }
            Transform c = canvas.transform;
            var shade = UiKit.Img(c, "SettingsShade", null, new Vector2(.5f, .5f), Vector2.zero, new Vector2(1400, 1400), new Color(0, 0, 0, .45f), true);
            shade.rectTransform.pivot = new Vector2(.5f, .5f);
            open = shade.gameObject;
            Tap(shade.gameObject, Close);
            var panel = UiKit.Img(shade.rectTransform, "panel", UiKit.Get("Lobby/sheet"), new Vector2(.5f, .5f), Vector2.zero, new Vector2(340, 290), Color.white, true);
            panel.rectTransform.pivot = new Vector2(.5f, .5f);
            Transform p = panel.transform;
            var title = UiKit.Txt(p, new Vector2(0, 1), new Vector2(18, -14), new Vector2(300, 22), 15, TextAnchor.UpperLeft, Gold); title.text = "SETTINGS";
            float y = 48;
            var sens = Label(p, ref y, $"LOOK SENSITIVITY  {Profile.Sensitivity:0.0}");
            Slider(p, ref y, 0.3f, 2.5f, Profile.Sensitivity, v => { Profile.Sensitivity = v; sens.text = $"LOOK SENSITIVITY  {v:0.0}"; });
            Label(p, ref y, "AIM");
            Choice(p, ref y, new[] { "ASSIST", "FULL CONTROL" }, Profile.AimAssist ? 0 : 1, i => { Profile.AimAssist = i == 0; PlayerController.AimAssistOn = i == 0; });
            Label(p, ref y, "GRAPHICS");
            Choice(p, ref y, new[] { "LOW", "MEDIUM", "HIGH" }, Profile.Quality, i => Profile.Quality = i);
            if (inMatch)
            {
                var leave = UiKit.Img(p, "leave", UiKit.Round, new Vector2(0, 0), new Vector2(18, 14), new Vector2(140, 30), new Color(.82f, .27f, .2f), true);
                var lt = UiKit.Txt(leave.rectTransform, new Vector2(.5f, .5f), Vector2.zero, new Vector2(140, 30), 12, TextAnchor.MiddleCenter, Color.white); lt.rectTransform.pivot = new Vector2(.5f, .5f); lt.text = "LEAVE MATCH";
                Tap(leave.gameObject, () => { Close(); if (GameBoot.Instance != null) GameBoot.Instance.Restart(); });
            }
            var close = UiKit.Img(p, "close", UiKit.Round, new Vector2(1, 0), new Vector2(-18, 14), new Vector2(110, 30), new Color(.47f, .78f, .3f), true);
            close.rectTransform.pivot = new Vector2(1, 0);
            var ct = UiKit.Txt(close.rectTransform, new Vector2(.5f, .5f), Vector2.zero, new Vector2(110, 30), 12, TextAnchor.MiddleCenter, new Color(.06f, .13f, .03f)); ct.rectTransform.pivot = new Vector2(.5f, .5f); ct.text = "CLOSE";
            Tap(close.gameObject, Close);
        }

        static void Close() { if (open != null) UnityEngine.Object.Destroy(open); open = null; }

        static void Tap(GameObject go, Action a)
        {
            var tr = go.AddComponent<EventTrigger>(); var e = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            e.callback.AddListener(ev => { ((PointerEventData)ev).Use(); a(); }); tr.triggers.Add(e);
        }

        static Text Label(Transform p, ref float y, string text)
        {
            var t = UiKit.Txt(p, new Vector2(0, 1), new Vector2(18, -y), new Vector2(300, 16), 11, TextAnchor.UpperLeft, new Color(.87f, .9f, .91f)); t.text = text;
            y += 18; return t;
        }

        static void Slider(Transform p, ref float y, float min, float max, float val, Action<float> on)
        {
            const float K = UiKit.K;
            var go = new GameObject("slider", typeof(RectTransform), typeof(Slider));
            var rt = (RectTransform)go.transform; rt.SetParent(p, false); rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(18, -y) * K; rt.sizeDelta = new Vector2(300, 20) * K;
            var track = UiKit.Img(rt, "track", UiKit.Round, Vector2.zero, Vector2.zero, Vector2.one, new Color(.04f, .05f, .06f));
            track.rectTransform.anchorMin = new Vector2(0, .3f); track.rectTransform.anchorMax = new Vector2(1, .7f); track.rectTransform.offsetMin = track.rectTransform.offsetMax = Vector2.zero;
            var fa = new GameObject("fill", typeof(RectTransform)).GetComponent<RectTransform>(); fa.SetParent(rt, false); fa.anchorMin = new Vector2(0, .3f); fa.anchorMax = new Vector2(1, .7f); fa.offsetMin = fa.offsetMax = Vector2.zero;
            var fill = UiKit.Img(fa, "f", UiKit.Round, Vector2.zero, Vector2.zero, Vector2.one, new Color(1f, .83f, .3f, .7f));
            var ha = new GameObject("handles", typeof(RectTransform)).GetComponent<RectTransform>(); ha.SetParent(rt, false); ha.anchorMin = Vector2.zero; ha.anchorMax = Vector2.one; ha.offsetMin = ha.offsetMax = Vector2.zero;
            var handle = UiKit.Img(ha, "h", null, new Vector2(0, .5f), Vector2.zero, new Vector2(16, 16), Gold, true);
            handle.rectTransform.pivot = new Vector2(.5f, .5f); handle.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
            var sl = go.GetComponent<Slider>(); sl.fillRect = fill.rectTransform; sl.handleRect = handle.rectTransform; sl.targetGraphic = handle;
            sl.minValue = min; sl.maxValue = max; sl.value = val; sl.onValueChanged.AddListener(v => on(v));
            y += 28;
        }

        static void Choice(Transform p, ref float y, string[] opts, int sel, Action<int> on)
        {
            float bw = 300f / opts.Length - 4;
            var imgs = new List<Image>(); var txts = new List<Text>();
            void Paint(int s) { for (int i = 0; i < imgs.Count; i++) { imgs[i].color = i == s ? Color.white : new Color(.13f, .15f, .17f); txts[i].color = i == s ? Ink : new Color(.6f, .64f, .67f); } }
            for (int i = 0; i < opts.Length; i++)
            {
                int idx = i;
                var b = UiKit.Img(p, "opt", UiKit.Get("Lobby/mode_on"), new Vector2(0, 1), new Vector2(18 + i * (bw + 4), -y), new Vector2(bw, 26), Color.white, true);
                var t = UiKit.Txt(b.rectTransform, new Vector2(.5f, .5f), Vector2.zero, new Vector2(bw, 26), 11, TextAnchor.MiddleCenter, Color.white); t.rectTransform.pivot = new Vector2(.5f, .5f); t.text = opts[i];
                imgs.Add(b); txts.Add(t);
                Tap(b.gameObject, () => { on(idx); Paint(idx); });
            }
            Paint(sel);
            y += 34;
        }
    }
}
