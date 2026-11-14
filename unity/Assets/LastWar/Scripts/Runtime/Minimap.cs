using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LastWar
{
    /// <summary>
    /// The web game's minimap (top-right, 132 CSS px): aerial photo of the whole map, house squares, place names,
    /// the safe zone (blue circle + shaded outside) and the next zone (white dashed), your yellow arrow with a view
    /// cone, red dots where gunfire was heard, a big red dot + line toward whoever shoots you. Tap = big map.
    /// Drawing units are the web's: 360 units for the whole map.
    /// </summary>
    public class Minimap : MonoBehaviour
    {
        const float Units = 360f;
        TouchControls input; PlayerController player; MapData map;
        RectTransform frame, content, me, zoneRing, nextRing, shade; Image shadeImg;
        RectTransform bigShade; bool big;
        class Mark { public RectTransform rt; public Image img; public Vector2 web; public float until, life; public RectTransform line; }
        readonly List<Mark> shots = new List<Mark>(), hits = new List<Mark>();

        public static Minimap Create(TouchControls input, PlayerController player, MapData map)
        {
            var m = new GameObject("Minimap").AddComponent<Minimap>();
            m.input = input; m.player = player; m.map = map; m.Build();
            Fighter.OnShot += m.Shot; Fighter.OnHit += m.Hit;
            return m;
        }

        float U => frame.rect.width / Units;            // canvas units per drawing unit
        Vector2 ToMap(Vector2 web) => new Vector2((web.x + map.size / 2f) / map.size * Units, -(web.y + map.size / 2f) / map.size * Units); // drawing units, y down
        static Vector2 Web(Vector3 unity) => new Vector2(-unity.x, unity.z);

        void Build()
        {
            Transform c = input.Canvas.transform;
            bigShade = UiKit.Img(c, "MapShade", null, new Vector2(.5f, .5f), Vector2.zero, new Vector2(1400, 1400), new Color(0, 0, 0, .45f), true).rectTransform;
            bigShade.pivot = new Vector2(.5f, .5f);
            AddTap(bigShade.gameObject, () => SetBig(false));
            bigShade.gameObject.SetActive(false);

            // white border (2 px) around a rounded window
            var border = UiKit.Img(c, "Minimap", UiKit.Round, new Vector2(1, 1), new Vector2(-8, -6), new Vector2(132, 132), new Color(1, 1, 1, .55f), true);
            frame = border.rectTransform;
            AddTap(border.gameObject, () => { if (!input.Editing) SetBig(!big); });
            input.RegisterBlocker(frame);
            input.AddEditable("minimap", frame);
            var win = UiKit.Img(frame, "window", UiKit.Round, new Vector2(.5f, .5f), Vector2.zero, new Vector2(128, 128), Color.white);
            win.rectTransform.pivot = new Vector2(.5f, .5f);
            win.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            UiKit.Fill(win.rectTransform, 2);

            content = new GameObject("content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(win.rectTransform, false); UiKit.Fill(content);
            content.pivot = new Vector2(0, 1);

            // background: generated colours + aerial photo
            var photo = new GameObject("photo", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            photo.transform.SetParent(content, false); UiKit.Fill(photo.rectTransform); photo.raycastTarget = false;
            Texture2D tex = !string.IsNullOrEmpty(map.albedo) ? Resources.Load<Texture2D>("Maps/" + map.albedo) : null;
            photo.texture = tex != null ? tex : HeightTexture();

            shade = new GameObject("outside", typeof(RectTransform)).GetComponent<RectTransform>();
            shade.SetParent(content, false);
            shadeImg = shade.gameObject.AddComponent<Image>(); shadeImg.sprite = HoleSprite(); shadeImg.color = new Color(40 / 255f, 90 / 255f, 200 / 255f, .35f); shadeImg.raycastTarget = false;
            shade.anchorMin = shade.anchorMax = new Vector2(0, 1); shade.pivot = new Vector2(.5f, .5f);
            zoneRing = Ring("zone", ThinRing(3f / 64f, false), new Color(79 / 255f, 176 / 255f, 1f));
            nextRing = Ring("next", ThinRing(3f / 64f, true), Color.white);

            // houses: 8-unit squares, cream
            foreach (PropData p in map.props)
                if (p.p.StartsWith("house") || p.p == "farmhouse") Dot(null, new Vector2(p.x, p.z), 8, new Color(232 / 255f, 217 / 255f, 192 / 255f));
            // place names
            if (map.areas != null)
                foreach (AreaData a in map.areas)
                {
                    var t = UiKit.Txt(content, new Vector2(0, 1), Vector2.zero, new Vector2(120, 20), 1, TextAnchor.MiddleCenter, new Color(1f, .96f, .82f));
                    t.rectTransform.pivot = new Vector2(.5f, .5f); t.text = a.name;
                    var o = t.gameObject.AddComponent<Outline>(); o.effectColor = new Color(0, 0, 0, .65f);
                    names.Add((t, new Vector2(a.x, a.z)));
                }

            me = new GameObject("me", typeof(RectTransform)).GetComponent<RectTransform>();
            me.SetParent(content, false); me.anchorMin = me.anchorMax = new Vector2(0, 1); me.pivot = new Vector2(.5f, .5f);
            cone = UiKit.Img(me, "cone", UiKit.Get("map_cone"), new Vector2(.5f, .5f), Vector2.zero, Vector2.one, Color.white).rectTransform; cone.pivot = new Vector2(.5f, .5f);
            arrow = UiKit.Img(me, "arrow", UiKit.Get("map_arrow"), new Vector2(.5f, .5f), Vector2.zero, Vector2.one, Color.white).rectTransform; arrow.pivot = new Vector2(.5f, .5f);
        }
        RectTransform cone, arrow;
        readonly List<(Text t, Vector2 web)> names = new List<(Text, Vector2)>();
        readonly List<(RectTransform rt, Vector2 web, float size)> statics = new List<(RectTransform, Vector2, float)>();

        RectTransform Ring(string name, Sprite s, Color col)
        {
            var img = UiKit.Img(content, name, s, new Vector2(0, 1), Vector2.zero, Vector2.one, col);
            img.rectTransform.pivot = new Vector2(.5f, .5f);
            return img.rectTransform;
        }

        Image Dot(Sprite s, Vector2 web, float size, Color col)
        {
            var img = UiKit.Img(content, "dot", s, new Vector2(0, 1), Vector2.zero, Vector2.one, col);
            img.rectTransform.pivot = new Vector2(.5f, .5f);
            statics.Add((img.rectTransform, web, size));
            return img;
        }

        void AddTap(GameObject go, System.Action a)
        {
            var tr = go.AddComponent<EventTrigger>();
            var e = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
            e.callback.AddListener(_ => a());
            tr.triggers.Add(e);
        }

        void SetBig(bool on)
        {
            big = on;
            bigShade.gameObject.SetActive(on);
            if (on)
            {
                smallPos = frame.anchoredPosition; smallScale = frame.localScale; frame.localScale = Vector3.one;
                float s = Mathf.Min(1080f * .84f, 420 * UiKit.K);
                frame.anchorMin = frame.anchorMax = frame.pivot = new Vector2(.5f, .5f);
                frame.anchoredPosition = Vector2.zero; frame.sizeDelta = new Vector2(s, s);
                bigShade.SetAsLastSibling(); frame.SetAsLastSibling();
            }
            else
            {
                frame.anchorMin = frame.anchorMax = frame.pivot = new Vector2(1, 1);
                frame.anchoredPosition = smallPos; frame.localScale = smallScale; frame.sizeDelta = new Vector2(132, 132) * UiKit.K;
            }
        }
        Vector2 smallPos = new Vector2(-8, -6) * UiKit.K; Vector3 smallScale = Vector3.one;

        void Shot(Fighter s, Vector3 from, Vector3 to)
        {
            if (s == null || s == player.fighter || Vector3.Distance(from, player.transform.position) > 220f) return;
            Vector2 w = Web(from);
            foreach (Mark m in shots) if (Vector2.Distance(m.web, w) < 6f) { m.until = Time.time + 2.5f; return; }
            if (shots.Count >= 12) { Destroy(shots[0].rt.gameObject); shots.RemoveAt(0); }
            var img = UiKit.Img(content, "shot", UiKit.Disc, new Vector2(0, 1), Vector2.zero, Vector2.one, new Color(1, .16f, .16f));
            img.rectTransform.pivot = new Vector2(.5f, .5f);
            img.gameObject.AddComponent<Outline>().effectColor = new Color(1, 1, 1, .8f);
            shots.Add(new Mark { rt = img.rectTransform, img = img, web = w, until = Time.time + 2.5f, life = 1.2f });
        }

        void Hit(Fighter victim, Fighter attacker, float dmg, bool head, Vector3 point)
        {
            if (victim != player.fighter || attacker == null || attacker == victim) return;
            var line = UiKit.Img(content, "hitline", null, new Vector2(0, 1), Vector2.zero, Vector2.one, new Color(1, .12f, .12f)).rectTransform;
            line.pivot = new Vector2(0, .5f);
            var img = UiKit.Img(content, "hit", UiKit.Disc, new Vector2(0, 1), Vector2.zero, Vector2.one, new Color(1, .12f, .12f));
            img.rectTransform.pivot = new Vector2(.5f, .5f);
            img.gameObject.AddComponent<Outline>().effectColor = Color.white;
            hits.Add(new Mark { rt = img.rectTransform, img = img, web = Web(attacker.transform.position), until = Time.time + 3f, life = 1f, line = line });
        }

        void Update()
        {
            float u = U;
            void Place(RectTransform rt, Vector2 web, float size) { rt.anchoredPosition = ToMap(web) * u; rt.sizeDelta = Vector2.one * size * u; }
            foreach (var s in statics) Place(s.rt, s.web, s.size);
            foreach (var n in names) { n.t.rectTransform.anchoredPosition = ToMap(n.web) * u; n.t.fontSize = Mathf.Max(8, Mathf.RoundToInt((big ? 13 : 15) * u)); }

            Zone z = Zone.Instance;
            bool zoneOn = z != null && z.Active;
            zoneRing.gameObject.SetActive(zoneOn); nextRing.gameObject.SetActive(zoneOn); shade.gameObject.SetActive(zoneOn);
            if (zoneOn)
            {
                Vector2 c = new Vector2(-z.Centre.x, z.Centre.y), nc = new Vector2(-z.NextCentre.x, z.NextCentre.y);
                float k = Units / map.size;
                Place(zoneRing, c, z.Radius * 2 * k); Place(nextRing, nc, Mathf.Max(1, z.NextRadius * 2 * k));
                // outside tint: a big square with a round hole, the hole sized to the zone
                float hole = z.Radius * 2 * k;
                Place(shade, c, hole * 20f);
            }

            // me: arrow + view cone, turned with the camera
            Vector2 mw = Web(player.transform.position);
            me.anchoredPosition = ToMap(mw) * u; me.sizeDelta = Vector2.one;
            float heading = player.yaw;
            if (player.fighter != null && player.fighter.air == 1 && SkyDrop.Instance != null) { Vector3 pd = SkyDrop.Instance.Dir; heading = Mathf.Atan2(pd.x, pd.z) * Mathf.Rad2Deg; }
            me.localRotation = Quaternion.Euler(0, 0, 180f - heading);
            cone.sizeDelta = Vector2.one * 200 * u;
            arrow.sizeDelta = Vector2.one * 52 * u; arrow.anchoredPosition = new Vector2(0, 4f * u); // the svg point (0,0) is 4 units under the picture centre

            for (int i = shots.Count - 1; i >= 0; i--)
            {
                Mark m = shots[i]; float left = m.until - Time.time;
                if (left <= 0) { Destroy(m.rt.gameObject); shots.RemoveAt(i); continue; }
                Place(m.rt, m.web, 24); m.img.color = new Color(1, .16f, .16f, Mathf.Min(1, left / m.life));
            }
            for (int i = hits.Count - 1; i >= 0; i--)
            {
                Mark m = hits[i]; float left = m.until - Time.time;
                if (left <= 0) { Destroy(m.rt.gameObject); Destroy(m.line.gameObject); hits.RemoveAt(i); continue; }
                float a = Mathf.Min(1, left / m.life);
                Place(m.rt, m.web, 48); m.img.color = new Color(1, .12f, .12f, a);
                Vector2 from = ToMap(mw), to = ToMap(m.web), d = to - from; float L = Mathf.Max(1, d.magnitude);
                float len = Mathf.Min(L, 90) - 24;
                m.line.anchoredPosition = (from + d / L * 24) * u; m.line.sizeDelta = new Vector2(Mathf.Max(0, len) * u, 9 * u);
                m.line.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
                m.line.GetComponent<Image>().color = new Color(1, .12f, .12f, a);
            }
            me.SetAsLastSibling();
        }

        // -------- procedural pictures
        static Sprite ThinRing(float width, bool dashed)
        {
            const int S = 256; var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[S * S]; float r = S / 2f - 1, w = width * S / 2f;
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
                {
                    float dx = x + .5f - S / 2f, dy = y + .5f - S / 2f, d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(r - d) * Mathf.Clamp01(d - (r - w));
                    if (dashed && Mathf.Repeat(Mathf.Atan2(dy, dx) * r / 9f, 1f) > 0.55f) a = 0; // 5 on, 4 off
                    px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            tex.SetPixels32(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(.5f, .5f));
        }

        /// <summary>white square with a round hole whose diameter is 1/20 of the side (the zone sits in the hole)</summary>
        static Sprite HoleSprite()
        {
            const int S = 1024; var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[S * S]; float r = S / 40f;
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(S / 2f, S / 2f));
                    px[y * S + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(d - r) * 255));
                }
            tex.SetPixels32(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(.5f, .5f));
        }

        Texture2D HeightTexture()
        {
            int n = map.cells + 1; var tex = new Texture2D(map.cells, map.cells, TextureFormat.RGBA32, false);
            for (int iz = 0; iz < map.cells; iz++) for (int ix = 0; ix < map.cells; ix++)
                {
                    float h = map.heights[iz * n + ix]; float l = Mathf.Clamp(38 + h * 1.2f, 18, 80) / 100f;
                    tex.SetPixel(ix, map.cells - 1 - iz, h > map.snowHeight ? new Color(.87f, .9f, .92f) : Color.HSVToRGB(0.25f, 0.35f, l));
                }
            tex.Apply();
            return tex;
        }
    }
}
