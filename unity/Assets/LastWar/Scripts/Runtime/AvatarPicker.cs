using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LastWar
{
    /// <summary>
    /// The profile picture: by default a portrait of your own character (the face and shoulders of the outfit you wear,
    /// cut from its picture - it follows the outfit), or one of the game's icons. Tap the picture in the lobby to choose.
    /// </summary>
    public static class AvatarPicker
    {
        /// <summary>the icons that can stand as the profile picture (after the portrait)</summary>
        static readonly string[] Icons = { "emblem", "icon_skull", "icon_trophy", "icon_rank", "icon_fist", "icon_scope", "icon_frost", "icon_heal" };
        static readonly Dictionary<string, Sprite> portraits = new Dictionary<string, Sprite>();
        static GameObject open;

        /// <summary>the picture chosen now</summary>
        public static Sprite Current()
        {
            int a = Profile.Avatar;
            if (a > 0 && a <= Icons.Length) { Sprite s = UiKit.Get(Icons[a - 1]); if (s != null) return s; }
            return Portrait();
        }

        /// <summary>the face and shoulders of the outfit worn (the top square of its full-length picture)</summary>
        public static Sprite Portrait()
        {
            int skin = Profile.Part(1) >= 0 ? Profile.Part(1) : Profile.Skin;
            string file = SoldierBody.OutfitFiles[Mathf.Clamp(skin, 0, SoldierBody.OutfitFiles.Length - 1)];
            if (portraits.TryGetValue(file, out Sprite have) && have != null) return have;
            Sprite full = Resources.Load<Sprite>("UI/Outfits/" + file);
            if (full == null) return null;
            Rect r = full.textureRect; float side = Mathf.Min(r.width, r.height * 0.4f);
            var crop = new Rect(r.x + (r.width - side) / 2f, r.y + r.height - side, side, side);
            Sprite p = Sprite.Create(full.texture, crop, new Vector2(.5f, .5f), full.pixelsPerUnit);
            portraits[file] = p;
            return p;
        }

        public static void Show(Action<Sprite> chosen)
        {
            if (open != null) UnityEngine.Object.Destroy(open);
            var go = new GameObject("AvatarPicker"); open = go;
            var canvas = go.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 500;
            UiKit.Scale(go.AddComponent<CanvasScaler>());
            go.AddComponent<GraphicRaycaster>();
            if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() == null) { var es = new GameObject("EventSystem"); es.AddComponent<EventSystem>(); es.AddComponent<StandaloneInputModule>(); }
            var shade = UiKit.Img(go.transform, "shade", null, new Vector2(.5f, .5f), Vector2.zero, Vector2.one, new Color(0, 0, 0, .55f), true);
            UiKit.Fill(shade.rectTransform);
            SettingsPanel.Tap(shade.gameObject, () => UnityEngine.Object.Destroy(go));
            const int cols = 5; const float tile = 64f, gap = 10f;
            int n = Icons.Length + 1, rows = Mathf.CeilToInt(n / (float)cols);
            var card = UiKit.Img(go.transform, "card", UiKit.Panel, new Vector2(.5f, .5f), Vector2.zero, new Vector2(cols * (tile + gap) + 30, rows * (tile + gap) + 70), Color.white, true);
            card.rectTransform.pivot = new Vector2(.5f, .5f);
            SettingsPanel.Tap(card.gameObject, () => { });
            var t = UiKit.Txt(card.rectTransform, new Vector2(.5f, 1), new Vector2(0, -14), new Vector2(300, 22), 16, TextAnchor.UpperCenter, new Color(1f, .83f, .3f));
            t.rectTransform.pivot = new Vector2(.5f, 1); t.text = "PROFILE PICTURE";
            for (int i = 0; i < n; i++)
            {
                int idx = i;
                Sprite sp = i == 0 ? Portrait() : UiKit.Get(Icons[i - 1]);
                float x = (i % cols - (cols - 1) / 2f) * (tile + gap), y = -48 - (i / cols) * (tile + gap);
                bool on = Profile.Avatar == i || (i == 0 && (Profile.Avatar < 0 || Profile.Avatar > Icons.Length));
                var bg = UiKit.Img(card.rectTransform, "tile" + i, UiKit.Round, new Vector2(.5f, 1), new Vector2(x, y), new Vector2(tile, tile), on ? new Color(1f, .83f, .3f, .9f) : new Color(.16f, .18f, .2f, .95f), true);
                bg.rectTransform.pivot = new Vector2(.5f, 1);
                var pic = UiKit.Img(bg.rectTransform, "pic", sp, new Vector2(.5f, .5f), Vector2.zero, new Vector2(tile - 10, tile - 10), Color.white);
                pic.rectTransform.pivot = new Vector2(.5f, .5f); pic.preserveAspect = true;
                if (i == 0)
                {
                    var me = UiKit.Txt(bg.rectTransform, new Vector2(.5f, 0), new Vector2(0, 2), new Vector2(tile, 12), 8, TextAnchor.LowerCenter, Color.white);
                    me.rectTransform.pivot = new Vector2(.5f, 0); me.text = "MY SOLDIER";
                }
                SettingsPanel.Tap(bg.gameObject, () => { Profile.Avatar = idx; chosen?.Invoke(Current()); UnityEngine.Object.Destroy(go); });
            }
        }
    }
}
