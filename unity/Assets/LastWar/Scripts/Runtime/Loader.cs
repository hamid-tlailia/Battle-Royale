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
        CanvasGroup group; Image bar; float t, hideAt = -1, target = 0.05f, shown;

        public static void Show(string text)
        {
            if (current != null) Destroy(current.gameObject);
            var go = new GameObject("Loader");
            DontDestroyOnLoad(go);
            current = go.AddComponent<Loader>();
            var canvas = go.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 1000;
            UiKit.Scale(go.AddComponent<CanvasScaler>());
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

        /// <summary>how far the loading has got (0..1); the bar glides toward it and creeps on while a step takes long</summary>
        public static void Progress(float p) { if (current != null) current.target = Mathf.Max(current.target, Mathf.Clamp01(p)); }

        /// <summary>show the card, let it draw, then reload the game scene in the background (the bar keeps moving)</summary>
        public static void Go(string text)
        {
            Show(text);
            current.StartCoroutine(current.Switch());
        }

        System.Collections.IEnumerator Switch()
        {
            yield return null; yield return null; // the card is on screen before the heavy work starts
            var op = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
            while (op != null && !op.isDone) { Progress(op.progress * 0.3f); yield return null; }
        }

        /// <summary>called by the new scene once it is built: fade out after a few frames</summary>
        public static void HideSoon(float delay = 0.6f) { if (current != null) current.hideAt = Time.unscaledTime + delay; }

        void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f); // a long blocking step must not make the bar jump
            t += dt;
            // glide toward the reported progress; between reports creep slowly so it never looks stuck
            float goal = hideAt > 0 ? 1f : Mathf.Min(0.97f, Mathf.Max(target, shown + dt * 0.04f));
            shown = Mathf.MoveTowards(shown, goal, dt * (hideAt > 0 ? 1.6f : 0.9f));
            float fill = shown;
            bar.rectTransform.sizeDelta = new Vector2(260 * UiKit.K * fill, bar.rectTransform.sizeDelta.y);
            if (hideAt > 0 && Time.unscaledTime > hideAt && shown >= 0.999f)
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
