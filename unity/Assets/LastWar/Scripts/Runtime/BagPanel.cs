using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LastWar
{
    /// <summary>
    /// Backpack (web .bag-panel): weapons and armour on the left, supplies on the right, sliding in from the left.
    /// </summary>
    public class BagPanel : MonoBehaviour
    {
        static BagPanel open;
        TouchControls input; Fighter f; RectTransform panel; Text left, right; float slide;

        public static void Toggle(TouchControls input, Fighter f)
        {
            if (open != null) { Destroy(open.gameObject); open = null; return; }
            var shade = UiKit.Img(input.Canvas.transform, "BagShade", null, new Vector2(.5f, .5f), Vector2.zero, new Vector2(1400, 1400), new Color(0, 0, 0, .25f), true);
            shade.rectTransform.pivot = new Vector2(.5f, .5f);
            open = shade.gameObject.AddComponent<BagPanel>();
            open.input = input; open.f = f;
            var tr = shade.gameObject.AddComponent<EventTrigger>(); var e = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            e.callback.AddListener(_ => { Destroy(open.gameObject); open = null; }); tr.triggers.Add(e);
            open.Build();
        }

        void Build()
        {
            panel = UiKit.Img(transform, "bag", UiKit.Get("Lobby/sheet"), new Vector2(0, .5f), Vector2.zero, new Vector2(420, 300), new Color(1, 1, 1, .92f), true).rectTransform;
            panel.pivot = new Vector2(0, .5f);
            var title = UiKit.Txt(panel, new Vector2(0, 1), new Vector2(16, -12), new Vector2(300, 22), 15, TextAnchor.UpperLeft, new Color(1f, .83f, .3f)); title.text = "BACKPACK";
            var h1 = UiKit.Txt(panel, new Vector2(0, 1), new Vector2(16, -42), new Vector2(190, 16), 10, TextAnchor.UpperLeft, new Color(1, 1, 1, .6f)); h1.text = "WEAPONS & ARMOR";
            var h2 = UiKit.Txt(panel, new Vector2(0, 1), new Vector2(220, -42), new Vector2(190, 16), 10, TextAnchor.UpperLeft, new Color(1, 1, 1, .6f)); h2.text = "SUPPLIES";
            left = UiKit.Txt(panel, new Vector2(0, 1), new Vector2(16, -62), new Vector2(190, 220), 12, TextAnchor.UpperLeft, Color.white); left.horizontalOverflow = HorizontalWrapMode.Wrap;
            right = UiKit.Txt(panel, new Vector2(0, 1), new Vector2(220, -62), new Vector2(190, 220), 12, TextAnchor.UpperLeft, Color.white); right.horizontalOverflow = HorizontalWrapMode.Wrap;
            left.lineSpacing = right.lineSpacing = 1.3f;
        }

        void Update()
        {
            // slide in from the left
            slide = Mathf.MoveTowards(slide, 1, Time.deltaTime * 6f);
            panel.anchoredPosition = new Vector2(Mathf.Lerp(-440, 12, 1 - (1 - slide) * (1 - slide)) * UiKit.K, 0);
            var sbL = new System.Text.StringBuilder();
            for (int k = 0; k < 3; k++)
            {
                GunState g = f.guns[k];
                sbL.AppendLine(g == null ? $"<color=#ffffff80>{Tuning.SlotNames[k]} — empty</color>" : $"<b>{g.def.name}</b>\n<size={Mathf.RoundToInt(10 * UiKit.K)}><color=#ffffffb0>{Tuning.SlotNames[k]} · {g.mag} in magazine</color></size>");
            }
            sbL.AppendLine($"<b>Armor</b>  {Mathf.RoundToInt(f.armor)} / 100");
            left.text = sbL.ToString();
            var sbR = new System.Text.StringBuilder();
            for (int k = 0; k < 3; k++) { GunState g = f.guns[k]; if (g != null && g.reserve > 0) sbR.AppendLine($"<b>{g.def.name} ammo</b>  {g.reserve}"); }
            if (f.meds > 0) sbR.AppendLine($"<b>Medkit</b>  × {f.meds}  <color=#ffffffb0>(+50 HP)</color>");
            if (f.frost > 0) sbR.AppendLine($"<b>Ice wall</b>  × {f.frost}");
            right.text = sbR.Length > 0 ? sbR.ToString() : "<color=#ffffff80>No supplies yet</color>";
        }

        void OnDestroy() { if (open == this) open = null; }
    }
}
