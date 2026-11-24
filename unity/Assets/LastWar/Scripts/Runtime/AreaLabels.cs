using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LastWar
{
    /// <summary>
    /// The map's areas named over the world: each area's name hangs above it on the screen - big, bold, in its kind's colour
    /// (village gold, farm green, factory orange, market pink, lodge / camp sky blue, city violet...) on a soft dark
    /// rounded plate, the kind written small under it. Plain to read from the plane and from far off; it fades out as you
    /// come near (gone inside ~45 m of the area's middle, full from ~110 m), behind you it is not drawn, through the scope
    /// and on the big map it steps aside. Sits under the buttons (first in the controls' canvas).
    /// </summary>
    public class AreaLabels : MonoBehaviour
    {
        public static float FadeNear = 45f, FadeFar = 110f;

        class Label { public RectTransform rt; public CanvasGroup g; public Vector3 world; public float a; }
        readonly List<Label> labels = new List<Label>();
        PlayerController pc; RectTransform root;

        public static AreaLabels Create(RectTransform root, MapData map, PlayerController pc)
        {
            if (map == null || map.areas == null || map.areas.Length == 0 || root == null) return null;
            var go = new GameObject("AreaLabels", typeof(RectTransform));
            var rt = (RectTransform)go.transform; rt.SetParent(root, false); rt.SetAsFirstSibling();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            var al = go.AddComponent<AreaLabels>(); al.pc = pc; al.root = rt;
            foreach (AreaData a in map.areas) al.Add(a, map);
            return al;
        }

        static Color KindColour(string kind)
        {
            switch (kind)
            {
                case "village": return new Color(1f, 0.83f, 0.3f);
                case "farm": return new Color(0.62f, 0.9f, 0.36f);
                case "factory": return new Color(1f, 0.55f, 0.24f);
                case "market": return new Color(1f, 0.42f, 0.66f);
                case "lodge": case "camp": return new Color(0.38f, 0.83f, 1f);
                case "city": return new Color(0.78f, 0.66f, 1f);
                case "desert": case "town": return new Color(1f, 0.7f, 0.45f);
                case "hill": return new Color(0.86f, 0.9f, 0.95f);
                case "outpost": return new Color(0.5f, 0.95f, 0.85f);
                default: return new Color(1f, 0.9f, 0.55f);
            }
        }

        void Add(AreaData a, MapData map)
        {
            string kind = string.IsNullOrEmpty(a.kind) ? "" : a.kind;
            Color col = KindColour(kind);
            var go = new GameObject("area " + a.name, typeof(RectTransform), typeof(CanvasGroup));
            var rt = (RectTransform)go.transform; rt.SetParent(root, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            var g = go.GetComponent<CanvasGroup>(); g.blocksRaycasts = false; g.interactable = false; g.alpha = 0f;
            string title = a.name.ToUpperInvariant();
            float w = Mathf.Max(120f, title.Length * 13.5f + 34f), h = string.IsNullOrEmpty(kind) ? 34f : 46f;
            // the soft plate: dark, see-through, rounded; a thin line of the kind's colour under the name
            var plate = UiKit.Img(rt, "plate", UiKit.Round, new Vector2(.5f, .5f), Vector2.zero, new Vector2(w, h), new Color(0.05f, 0.07f, 0.09f, 0.46f));
            plate.rectTransform.pivot = new Vector2(.5f, .5f);
            var line = UiKit.Img(rt, "line", UiKit.Round, new Vector2(.5f, .5f), new Vector2(0, string.IsNullOrEmpty(kind) ? -12f : -4f), new Vector2(w * 0.55f, 2.2f), new Color(col.r, col.g, col.b, 0.85f));
            line.rectTransform.pivot = new Vector2(.5f, .5f);
            var t = UiKit.Txt(rt, new Vector2(.5f, .5f), new Vector2(0, string.IsNullOrEmpty(kind) ? 2f : 8f), new Vector2(w, 26f), 19f, TextAnchor.MiddleCenter, col);
            t.text = title;
            var ol = t.gameObject.AddComponent<Outline>(); ol.effectColor = new Color(0, 0, 0, 0.55f); ol.effectDistance = new Vector2(1.6f, -1.6f);
            if (!string.IsNullOrEmpty(kind))
            {
                var k = UiKit.Txt(rt, new Vector2(.5f, .5f), new Vector2(0, -14f), new Vector2(w, 14f), 9.5f, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.82f));
                k.text = kind.ToUpperInvariant();
            }
            // hung ~22 m above the ground there (over the roofs)
            Vector3 world = Coords.ToUnity(a.x, map.HeightAt(a.x, a.z) + 22f, a.z);
            labels.Add(new Label { rt = rt, g = g, world = world, a = 0f });
        }

        void LateUpdate()
        {
            Camera cam = pc != null ? pc.TestCam : Camera.main;
            if (cam == null) return;
            Vector3 me = pc != null ? pc.transform.position : cam.transform.position;
            bool hide = (pc != null && pc.Scoped) || Minimap.BigOpen;
            float dt = Time.unscaledDeltaTime;
            foreach (Label l in labels)
            {
                Vector3 sp = cam.WorldToScreenPoint(l.world);
                float d = Vector3.Distance(me, l.world - Vector3.up * 22f); // (from the plane, high over it, the name stays)
                float want = hide || sp.z <= 0f ? 0f : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FadeNear, FadeFar, d));
                l.a = Mathf.MoveTowards(l.a, want, dt * 2.5f); // (a gentle fade in / out, never a pop)
                l.g.alpha = l.a;
                bool on = l.a > 0.01f;
                if (l.rt.gameObject.activeSelf != on) l.rt.gameObject.SetActive(on);
                if (!on) continue;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(root, sp, null, out Vector2 lp)) l.rt.anchoredPosition = lp;
                // far ones a little smaller (the plane sees the whole island), never tiny
                float s = Mathf.Lerp(1f, 0.72f, Mathf.InverseLerp(150f, 700f, d));
                l.rt.localScale = Vector3.one * s;
            }
        }
    }
}
