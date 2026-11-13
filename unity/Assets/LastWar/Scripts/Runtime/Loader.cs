using UnityEngine;
using UnityEngine.UI;

namespace LastWar
{
    /// <summary>
    /// Full-screen loading card (logo, line of text, bar) like the web game's: covers scene switches
    /// (lobby -> match -> lobby) and fades out once the new scene is drawing.
    /// </summary>
    public class Loader : MonoBehaviour
    {
        static Loader current;
        CanvasGroup group; Image bar; float t, hideAt = -1;

        public static void Show(string text)
        {
            if (current != null) Destroy(current.gameObject);
            var go = new GameObject("Loader");
            DontDestroyOnLoad(go);
            current = go.AddComponent<Loader>();
            var canvas = go.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 1000;
            var sc = go.AddComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 1f;
            go.AddComponent<GraphicRaycaster>();
            current.group = go.AddComponent<CanvasGroup>();
            // web .menu: radial green glow on dark green
            var bg = new GameObject("bg", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            bg.transform.SetParent(go.transform, false); UiKit.Fill(bg.rectTransform); bg.texture = Glow();
            var logo = UiKit.Img(go.transform, "logo", Resources.Load<Sprite>("UI/Lobby/logo"), new Vector2(.5f, .5f), new Vector2(0, 40), new Vector2(240, 125), Color.white);
            logo.rectTransform.pivot = new Vector2(.5f, .5f); logo.preserveAspect = true;
            var txt = UiKit.Txt(go.transform, new Vector2(.5f, .5f), new Vector2(0, -45), new Vector2(500, 20), 13, TextAnchor.MiddleCenter, new Color(1, 1, 1, .85f));
            txt.rectTransform.pivot = new Vector2(.5f, .5f); txt.fontStyle = FontStyle.Normal; txt.text = text;
            var track = UiKit.Img(go.transform, "track", UiKit.Round, new Vector2(.5f, .5f), new Vector2(0, -75), new Vector2(260, 5), new Color(1, 1, 1, .15f));
            track.rectTransform.pivot = new Vector2(.5f, .5f);
            current.bar = UiKit.Img(track.rectTransform, "bar", UiKit.Round, new Vector2(0, .5f), Vector2.zero, new Vector2(0, 5), new Color(.72f, .95f, .49f));
        }

        /// <summary>called by the new scene once it is built: fade out after a few frames</summary>
        public static void HideSoon(float delay = 0.6f) { if (current != null) current.hideAt = Time.unscaledTime + delay; }

        void Update()
        {
            t += Time.unscaledDeltaTime;
            float fill = hideAt > 0 ? 1f : Mathf.Min(0.92f, t / 2.5f);
            bar.rectTransform.sizeDelta = new Vector2(260 * UiKit.K * fill, bar.rectTransform.sizeDelta.y);
            if (hideAt > 0 && Time.unscaledTime > hideAt)
            {
                group.alpha -= Time.unscaledDeltaTime / 0.5f;
                group.blocksRaycasts = false;
                if (group.alpha <= 0) { Destroy(gameObject); if (current == this) current = null; }
            }
        }

        static Texture2D glow;
        public static Texture2D Glow()
        {
            if (glow != null) return glow;
            const int S = 128; glow = new Texture2D(S, S, TextureFormat.RGB24, false) { wrapMode = TextureWrapMode.Clamp };
            Color a = new Color(0x3f / 255f, 0x6b / 255f, 0x3a / 255f), b = new Color(0x1d / 255f, 0x2b / 255f, 0x1f / 255f);
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
                {
                    float dx = (x - S * .5f) / (S * .5f), dy = (y - S * .7f) / (S * .5f);
                    glow.SetPixel(x, y, Color.Lerp(a, b, Mathf.Clamp01(Mathf.Sqrt(dx * dx * .6f + dy * dy) / 0.7f)));
                }
            glow.Apply();
            return glow;
        }
    }
}
