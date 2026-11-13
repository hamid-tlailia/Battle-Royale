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

        static Sprite round, ring, disc;
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
}
