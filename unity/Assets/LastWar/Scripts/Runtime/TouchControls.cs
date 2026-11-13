using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LastWar
{
    /// <summary>
    /// Mobile controls built in code: joystick (left half), look by dragging (right half), round action buttons.
    /// Also reads keyboard + mouse in the editor / WebGL (WASD, mouse look while the right button is held, Space, Shift).
    /// </summary>
    public class TouchControls : MonoBehaviour
    {
        public Vector2 Move;        // x = right, y = forward (-1..1)
        public Vector2 LookDelta;   // pixels this frame
        public bool Sprint;         // auto-run toggle
        public bool JumpPressed;    // true for one frame
        public bool FireHeld;

        RectTransform joyBase, joyKnob;
        int joyFinger = -1, lookFinger = -1;
        Vector2 joyOrigin, lookLast;
        const float JoyRadius = 90f;
        Canvas canvas;
        readonly System.Collections.Generic.List<RectTransform> buttons = new System.Collections.Generic.List<RectTransform>();

        public static TouchControls Create()
        {
            var go = new GameObject("Controls");
            var tc = go.AddComponent<TouchControls>();
            tc.BuildUi();
            return tc;
        }

        void BuildUi()
        {
            canvas = new GameObject("HUD").AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvas.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 1f;
            canvas.gameObject.AddComponent<GraphicRaycaster>();
            if (FindFirstObjectByType<EventSystem>() == null) { var es = new GameObject("EventSystem"); es.AddComponent<EventSystem>(); es.AddComponent<StandaloneInputModule>(); }

            joyBase = Circle("Joystick", new Vector2(260, 230), 220, new Color(0, 0, 0, 0.25f), new Color(1, 1, 1, 0.45f));
            joyKnob = Circle("Knob", Vector2.zero, 100, new Color(1, 1, 1, 0.55f), Color.clear);
            joyKnob.SetParent(joyBase, false); joyKnob.anchoredPosition = Vector2.zero;

            AddButton("JUMP", new Vector2(-150, 260), 150, () => JumpPressed = true);
            AddButton("RUN", new Vector2(-150, 480), 120, () => Sprint = !Sprint);
        }

        RectTransform Circle(string name, Vector2 pos, float size, Color fill, Color ring)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform; rt.SetParent(canvas.transform, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0, 0); rt.sizeDelta = new Vector2(size, size); rt.anchoredPosition = pos;
            var img = go.GetComponent<Image>(); img.sprite = CircleSprite(); img.color = fill;
            if (ring.a > 0) { var o = go.AddComponent<Outline>(); o.effectColor = ring; o.effectDistance = new Vector2(3, 3); }
            return rt;
        }

        void AddButton(string label, Vector2 posFromRight, float size, System.Action onDown)
        {
            var rt = Circle(label, Vector2.zero, size, new Color(0.08f, 0.1f, 0.12f, 0.65f), new Color(0.85f, 0.88f, 0.9f, 0.6f));
            rt.anchorMin = rt.anchorMax = new Vector2(1, 0); rt.anchoredPosition = posFromRight;
            var txt = new GameObject("t", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            txt.transform.SetParent(rt, false); ((RectTransform)txt.transform).sizeDelta = rt.sizeDelta;
            txt.text = label; txt.alignment = TextAnchor.MiddleCenter; txt.fontSize = 30; txt.fontStyle = FontStyle.Bold; txt.color = Color.white;
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var trigger = rt.gameObject.AddComponent<EventTrigger>();
            var e = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
            e.callback.AddListener(_ => onDown());
            trigger.triggers.Add(e);
            buttons.Add(rt);
        }

        static Sprite circle;
        static Sprite CircleSprite()
        {
            if (circle != null) return circle;
            const int S = 128; var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(S / 2f, S / 2f)) / (S / 2f);
                    tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01((1f - d) * 40f)));
                }
            tex.Apply();
            circle = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f));
            return circle;
        }

        bool OverButton(Vector2 screen)
        {
            foreach (RectTransform b in buttons) if (RectTransformUtility.RectangleContainsScreenPoint(b, screen)) return true;
            return false;
        }

        void Update()
        {
            LookDelta = Vector2.zero;
            if (Input.touchCount > 0) Touches();
            else Desktop();
        }

        void LateUpdate() { JumpPressed = false; }

        void Touches()
        {
            Vector2 move = Move;
            for (int k = 0; k < Input.touchCount; k++)
            {
                Touch t = Input.GetTouch(k);
                if (t.phase == TouchPhase.Began)
                {
                    if (OverButton(t.position)) continue;
                    if (t.position.x < Screen.width * 0.42f && joyFinger < 0) { joyFinger = t.fingerId; joyOrigin = t.position; }
                    else if (lookFinger < 0) { lookFinger = t.fingerId; lookLast = t.position; }
                }
                if (t.fingerId == joyFinger)
                {
                    Vector2 d = (t.position - joyOrigin) / canvas.scaleFactor;
                    float len = d.magnitude;
                    Vector2 c = len > JoyRadius ? d / len * JoyRadius : d;
                    joyKnob.anchoredPosition = c;
                    move = len < 10 ? Vector2.zero : c / JoyRadius;
                    // pushing far past the top locks running
                    if (d.y > JoyRadius * 1.4f && Mathf.Abs(d.x) < d.y * 0.7f) Sprint = true;
                    if (d.y < -JoyRadius * 0.4f) Sprint = false;
                    if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled) { joyFinger = -1; move = Vector2.zero; joyKnob.anchoredPosition = Vector2.zero; }
                }
                else if (t.fingerId == lookFinger)
                {
                    LookDelta += t.position - lookLast; lookLast = t.position;
                    if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled) lookFinger = -1;
                }
            }
            Move = move;
        }

        void Desktop()
        {
            Vector2 m = new Vector2((Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0), (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0));
            Move = m.sqrMagnitude > 1 ? m.normalized : m;
            if (Input.GetKeyDown(KeyCode.Space)) JumpPressed = true;
            if (Input.GetKeyDown(KeyCode.LeftShift)) Sprint = !Sprint;
            if (Input.GetMouseButton(1)) LookDelta = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 12f;
            FireHeld = Input.GetMouseButton(0);
        }
    }
}
