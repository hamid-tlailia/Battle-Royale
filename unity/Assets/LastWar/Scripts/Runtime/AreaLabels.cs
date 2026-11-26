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
        public static float FadeNear = 40f, FadeFar = 75f;
        /// <summary>the names' size on the screen (x the HUD's); 0.8 covered the map</summary>
        public static float Size = 0.6f;
        /// <summary>?nolabels=1 (tests: screenshots of the map without names)</summary>
        public static bool NoLabels;

        class Label { public RectTransform rt; public CanvasGroup g; public Vector3 world; public float a, w, h; }
        readonly List<Label> labels = new List<Label>();
        PlayerController pc; RectTransform overlay;

        public static AreaLabels Create(RectTransform root, MapData map, PlayerController pc)
        {
            if (map == null || map.areas == null || map.areas.Length == 0 || root == null) return null;
            // (each label is a small canvas in the world, hung over its area and turned to the camera: the plane and the
            // parachute pass in front of it as they do of the houses - an overlay drew the names over the plane)
            var go = new GameObject("AreaLabels");
            var al = go.AddComponent<AreaLabels>(); al.pc = pc; al.overlay = root;
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
            var go = new GameObject("area " + a.name, typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            var rt = (RectTransform)go.transform; rt.SetParent(transform, false);
            var cv = go.GetComponent<Canvas>(); cv.renderMode = RenderMode.WorldSpace; rt.sizeDelta = new Vector2(10f, 10f);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            var g = go.GetComponent<CanvasGroup>(); g.blocksRaycasts = false; g.interactable = false; g.alpha = 0f;
            string title = a.name.ToUpperInvariant();
            float w = Mathf.Max(120f, title.Length * 13.5f + 34f), h = string.IsNullOrEmpty(kind) ? 34f : 46f;
            // the soft plate: dark, see-through, rounded; a thin line of the kind's colour under the name
            var plate = UiKit.Img(rt, "plate", UiKit.Round, new Vector2(.5f, .5f), Vector2.zero, new Vector2(w, h), new Color(0.03f, 0.04f, 0.05f, 0.78f));
            plate.rectTransform.pivot = new Vector2(.5f, .5f);
            var line = UiKit.Img(rt, "line", UiKit.Round, new Vector2(.5f, .5f), new Vector2(0, string.IsNullOrEmpty(kind) ? -12f : -4f), new Vector2(w * 0.55f, 2.2f), new Color(col.r, col.g, col.b, 0.85f));
            line.rectTransform.pivot = new Vector2(.5f, .5f);
            var t = UiKit.Txt(rt, new Vector2(.5f, .5f), new Vector2(0, string.IsNullOrEmpty(kind) ? 2f : 8f), new Vector2(w, 26f), 19f, TextAnchor.MiddleCenter, col);
            t.text = title;
            var ol = t.gameObject.AddComponent<Outline>(); ol.effectColor = new Color(0, 0, 0, 0.9f); ol.effectDistance = new Vector2(1.6f, -1.6f);
            if (!string.IsNullOrEmpty(kind))
            {
                var k = UiKit.Txt(rt, new Vector2(.5f, .5f), new Vector2(0, -14f), new Vector2(w, 14f), 9.5f, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.82f));
                k.text = kind.ToUpperInvariant();
            }
            // hung ~22 m above the ground there (over the roofs)
            Vector3 world = Coords.ToUnity(a.x, map.HeightAt(a.x, a.z) + 22f, a.z);
            labels.Add(new Label { rt = rt, g = g, world = world, a = 0f, w = w, h = h });
        }

        void LateUpdate()
        {
            Camera cam = pc != null ? pc.TestCam : Camera.main;
            if (cam == null) return;
            Vector3 me = pc != null ? pc.transform.position : cam.transform.position;
            // only in the air (the plane, the fall, the parachute): once on the ground they fade away - the minimap keeps
            // the places' names
            bool hide = NoLabels || (pc != null && pc.Scoped) || Minimap.BigOpen || (pc != null && pc.fighter != null && pc.fighter.air == 0);
            // world size of one screen pixel at 1 m, and the overlay's pixels per UI unit (the labels keep the HUD's size)
            float px1 = 2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1, Screen.height);
            float ui = overlay != null ? overlay.lossyScale.x : 1f;
            float dt = Time.unscaledDeltaTime;
            // nearest first; a name whose box on the screen would touch one already placed waits its turn (they piled on top
            // of each other from the plane)
            Vector3 cp = cam.transform.position; float Key(Label q) => (q.world - cp).sqrMagnitude * (q.a > 0.5f ? 0.3f : 1f); // (one already showing keeps its place: no flicker)
            labels.Sort((x, y) => Key(x).CompareTo(Key(y)));
            var placed = new System.Collections.Generic.List<Rect>();
            foreach (Label l in labels)
            {
                Vector3 sp = cam.WorldToScreenPoint(l.world);
                float d = Vector3.Distance(me, l.world - Vector3.up * 22f);
                float want = hide || sp.z <= 0f ? 0f : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FadeNear, FadeFar, d));
                float s = Size * Mathf.Lerp(1f, 0.8f, Mathf.InverseLerp(150f, 700f, d));
                float bw = l.w * UiKit.K * ui * s, bh = l.h * UiKit.K * ui * s; // (the plate's size on the screen: UiKit sizes are x K - without it the boxes were too small and the names still overlapped)
                var box = new Rect(sp.x - bw * 0.5f - 10f, sp.y - bh * 0.5f - 8f, bw + 20f, bh + 16f);
                if (want > 0f) { foreach (Rect o in placed) if (o.Overlaps(box)) { want = 0f; break; } }
                if (want > 0f) placed.Add(box);
                l.a = Mathf.MoveTowards(l.a, want, dt * (want < l.a ? 8f : 3f)); // (a gentle fade in / out, never a pop)
                l.g.alpha = l.a;
                bool on = l.a > 0.01f;
                if (l.rt.gameObject.activeSelf != on) l.rt.gameObject.SetActive(on);
                if (!on) continue;
                float cd = Vector3.Distance(cam.transform.position, l.world);
                l.rt.position = l.world; l.rt.rotation = cam.transform.rotation;
                l.rt.localScale = Vector3.one * (cd * px1 * ui * s);
            }
        }
    }
}
