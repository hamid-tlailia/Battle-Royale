using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LastWar
{
    /// <summary>
    /// Shared HUD helpers. Sizes are written in the web game's CSS pixels and multiplied by K, so the Unity HUD
    /// has the same proportions as the web HUD on a phone (about 363 CSS px tall -> 1080 canvas units).
    /// </summary>
    public static class UiKit
    {
        public const float K = 2.95f;
        static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
        static Font font;
        public static Font Font => font != null ? font : (font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

        /// <summary>picture exported from the web HUD (Resources/UI/name.png)</summary>
        public static Sprite Get(string name)
        {
            if (sprites.TryGetValue(name, out Sprite s)) return s;
            s = Resources.Load<Sprite>("UI/" + name);
            if (s == null) { var t = Resources.Load<Texture2D>("UI/" + name); if (t != null) s = Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f)); }
            sprites[name] = s;
            return s;
        }

        /// <summary>
        /// One rule for every canvas: 1920 x 1080 reference, height-matched on phones, width-matched on squarer
        /// screens (tablets), so nothing overlaps whatever the device.
        /// </summary>
        public static void Scale(CanvasScaler sc)
        {
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080);
            float aspect = Screen.width / (float)Mathf.Max(1, Screen.height);
            sc.matchWidthOrHeight = Mathf.Clamp01((aspect - 1.34f) / (1.75f - 1.34f));
        }

        /// <summary>
        /// The part of the screen clear of the front camera / rounded corners: a full-canvas child inset by the
        /// device's safe area, and at least 2.5 % on the left and right.
        /// </summary>
        public static RectTransform SafeRoot(Canvas canvas)
        {
            var rt = new GameObject("Safe", typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(canvas.transform, false);
            rt.gameObject.AddComponent<SafeArea>();
            return rt;
        }

        static Sprite round, ring, disc, panel;
        /// <summary>modal background (web .lb-sheet): dark blue-grey gradient, 14 px corners, thin gold edge; 9-sliced</summary>
        public static Sprite Panel => panel != null ? panel : (panel = MakePanel());

        static Sprite MakePanel()
        {
            const int S = 96; const float r = 30f;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[S * S];
            Color top = new Color(31 / 255f, 36 / 255f, 41 / 255f), bot = new Color(14 / 255f, 17 / 255f, 20 / 255f), gold = new Color(1f, .83f, .3f);
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
                {
                    float cx = Mathf.Clamp(x + .5f, r, S - r), cy = Mathf.Clamp(y + .5f, r, S - r);
                    float d = r - Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(cx, cy)); // distance inside the edge
                    float a = Mathf.Clamp01(d * 1.5f);
                    Color c = Color.Lerp(bot, top, y / (float)(S - 1));
                    c = Color.Lerp(c, gold * .7f + Color.black * .3f, Mathf.Clamp01(3.2f - d) * .75f); // gold rim ~2 px
                    c.a = a * .97f;
                    px[y * S + x] = c;
                }
            tex.SetPixels32(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, new Vector4(34, 34, 34, 34));
        }

        /// <summary>round close button (web .lb-close): dark disc, grey ring, white X</summary>
        public static Image CloseButton(Transform parent, Vector2 anchor, Vector2 pos, float size, System.Action onTap)
        {
            var b = Img(parent, "close", Disc, anchor, pos, new Vector2(size, size), new Color(.2f, .23f, .25f, .95f), true);
            b.rectTransform.pivot = anchor;
            var ring = Img(b.rectTransform, "ring", Ring, new Vector2(.5f, .5f), Vector2.zero, new Vector2(size, size), new Color(1, 1, 1, .45f)); ring.rectTransform.pivot = new Vector2(.5f, .5f);
            var t = Txt(b.rectTransform, new Vector2(.5f, .5f), Vector2.zero, new Vector2(size, size), size * .5f, TextAnchor.MiddleCenter, Color.white);
            t.rectTransform.pivot = new Vector2(.5f, .5f); t.text = "X";
            if (onTap != null) Clickable.Add(b.gameObject, onTap);
            return b;
        }
        /// <summary>white rounded rectangle, 9-sliced (CSS border-radius ~ 8 px)</summary>
        public static Sprite Round => round != null ? round : (round = Make(64, (x, y) => RoundAlpha(x, y, 64, 22), new Vector4(24, 24, 24, 24)));
        /// <summary>white disc</summary>
        public static Sprite Disc => disc != null ? disc : (disc = Make(128, (x, y) => Mathf.Clamp01((64f - Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(64, 64))) * 1.5f), Vector4.zero));
        /// <summary>white ring (circle outline)</summary>
        public static Sprite Ring => ring != null ? ring : (ring = Make(128, (x, y) => { float d = Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(64, 64)); return Mathf.Clamp01((63.5f - d) * 1.5f) * Mathf.Clamp01((d - 56f) * 1.5f); }, Vector4.zero));

        static float RoundAlpha(int x, int y, int S, float r)
        {
            float cx = Mathf.Clamp(x + .5f, r, S - r), cy = Mathf.Clamp(y + .5f, r, S - r);
            return Mathf.Clamp01((r - Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(cx, cy))) * 1.5f);
        }

        static Sprite Make(int S, System.Func<int, int, float> alpha, Vector4 border)
        {
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++) px[y * S + x] = new Color32(255, 255, 255, (byte)(alpha(x, y) * 255));
            tex.SetPixels32(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, border);
        }

        /// <summary>image in CSS px: anchor + pivot = `anchor`, `pos` from that anchor, `size` in CSS px</summary>
        public static Image Img(Transform parent, string name, Sprite sprite, Vector2 anchor, Vector2 pos, Vector2 size, Color color, bool raycast = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform; rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor; rt.anchoredPosition = pos * K; rt.sizeDelta = size * K;
            var img = go.GetComponent<Image>(); img.sprite = sprite; img.color = color; img.raycastTarget = raycast;
            go.AddComponent<GammaAlpha>();
            if (sprite != null && sprite.border != Vector4.zero) { img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = 1.4f; }
            img.preserveAspect = sprite != null && sprite.border == Vector4.zero && name.StartsWith("gun");
            return img;
        }

        public static Text Txt(Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, float cssFont, TextAnchor align, Color color)
        {
            var t = new GameObject("text", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            var rt = (RectTransform)t.transform; rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor; rt.anchoredPosition = pos * K; rt.sizeDelta = size * K;
            t.font = Font; t.fontSize = Mathf.RoundToInt(cssFont * K); t.alignment = align; t.color = color; t.fontStyle = FontStyle.Bold;
            t.raycastTarget = false; t.supportRichText = true; t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            var sh = t.gameObject.AddComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, 0.75f); sh.effectDistance = new Vector2(0, -1.5f * K * 0.5f);
            return t;
        }

        /// <summary>stretch a child over its parent with an inset (CSS px)</summary>
        public static void Fill(RectTransform rt, float inset = 0)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(.5f, .5f);
            rt.offsetMin = Vector2.one * inset * K; rt.offsetMax = -Vector2.one * inset * K;
        }
    }

    /// <summary>
    /// The project renders in linear colour space, where see-through UI blends lighter than the web's sRGB blending
    /// (a 45 % black panel looks like ~25 %). This re-weights each vertex alpha so translucent colours look as in the web.
    /// </summary>
    public class GammaAlpha : UnityEngine.UI.BaseMeshEffect
    {
        public static float Fix(Color c)
        {
            float a = c.a;
            if (a <= 0.001f || a >= 0.999f) return a;
            const float b = 0.45f, bl = 0.172f; // mid background (sRGB) and its linear value
            float lum = c.r * .3f + c.g * .59f + c.b * .11f;
            float cl = Mathf.Pow(lum, 2.2f), R = Mathf.Pow(a * lum + (1 - a) * b, 2.2f);
            if (Mathf.Abs(cl - bl) < 0.03f) return a;
            return Mathf.Clamp01((R - bl) / (cl - bl));
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive()) return;
            var v = new UIVertex();
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                Color c = v.color; c.a = Fix(c); v.color = c;
                vh.SetUIVertex(v, i);
            }
        }
    }

    /// <summary>keeps its RectTransform on the safe area of the screen (updates on rotation / resize)</summary>
    public class SafeArea : MonoBehaviour
    {
        Rect last; Vector2Int lastSize;
        void Awake() { Apply(); }
        void Update() { if (Screen.safeArea != last || lastSize.x != Screen.width || lastSize.y != Screen.height) Apply(); }
        void Apply()
        {
            last = Screen.safeArea; lastSize = new Vector2Int(Screen.width, Screen.height);
            float w = Mathf.Max(1, Screen.width), h = Mathf.Max(1, Screen.height), margin = 0.025f;
            float l = Mathf.Max(last.xMin / w, margin), r = Mathf.Min(last.xMax / w, 1f - margin);
            var rt = (RectTransform)transform;
            rt.anchorMin = new Vector2(l, last.yMin / h); rt.anchorMax = new Vector2(r, last.yMax / h);
            rt.offsetMin = rt.offsetMax = Vector2.zero; rt.pivot = new Vector2(.5f, .5f);
        }
    }
}
