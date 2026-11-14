using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LastWar
{
    /// <summary>Yes / no question over everything (web .leave-modal): the phone's back button asks before leaving.</summary>
    public static class Confirm
    {
        static GameObject open;
        public static bool IsOpen => open != null;
        public static void Close() { if (open != null) UnityEngine.Object.Destroy(open); open = null; }

        public static void Show(string title, string text, string yes, Action onYes)
        {
            Close();
            var go = new GameObject("Confirm"); open = go;
            var canvas = go.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 500;
            UiKit.Scale(go.AddComponent<CanvasScaler>());
            go.AddComponent<GraphicRaycaster>();
            if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() == null) { var es = new GameObject("EventSystem"); es.AddComponent<EventSystem>(); es.AddComponent<StandaloneInputModule>(); }
            var shade = UiKit.Img(go.transform, "shade", null, new Vector2(.5f, .5f), Vector2.zero, Vector2.one, new Color(0, 0, 0, .55f), true);
            UiKit.Fill(shade.rectTransform);
            SettingsPanel.Tap(shade.gameObject, Close);
            var card = UiKit.Img(go.transform, "card", UiKit.Panel, new Vector2(.5f, .5f), Vector2.zero, new Vector2(300, 150), Color.white, true);
            card.rectTransform.pivot = new Vector2(.5f, .5f);
            SettingsPanel.Tap(card.gameObject, () => { });
            var t = UiKit.Txt(card.rectTransform, new Vector2(.5f, 1), new Vector2(0, -16), new Vector2(280, 24), 17, TextAnchor.UpperCenter, new Color(1f, .83f, .3f));
            t.rectTransform.pivot = new Vector2(.5f, 1); t.text = title;
            var b = UiKit.Txt(card.rectTransform, new Vector2(.5f, 1), new Vector2(0, -46), new Vector2(270, 40), 12, TextAnchor.UpperCenter, new Color(.87f, .9f, .91f));
            b.rectTransform.pivot = new Vector2(.5f, 1); b.fontStyle = FontStyle.Normal; b.horizontalOverflow = HorizontalWrapMode.Wrap; b.text = text;
            Btn(card.rectTransform, new Vector2(-70, 18), "CANCEL", new Color(.27f, .29f, .31f), Color.white, Close);
            Btn(card.rectTransform, new Vector2(70, 18), yes, new Color(.82f, .27f, .2f), Color.white, () => { Close(); onYes(); });
        }

        static void Btn(RectTransform p, Vector2 pos, string label, Color bg, Color fg, Action a)
        {
            var img = UiKit.Img(p, label, UiKit.Round, new Vector2(.5f, 0), pos, new Vector2(120, 32), bg, true);
            img.rectTransform.pivot = new Vector2(.5f, 0);
            var t = UiKit.Txt(img.rectTransform, new Vector2(.5f, .5f), Vector2.zero, new Vector2(120, 32), 13, TextAnchor.MiddleCenter, fg);
            t.rectTransform.pivot = new Vector2(.5f, .5f); t.text = label;
            SettingsPanel.Tap(img.gameObject, a);
        }
    }
}
