using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LastWar
{
    /// <summary>
    /// Mobile controls with the web game's look and layout: metal round buttons with white icons, red FIRE
    /// (right, plus a smaller one on the left), joystick on the left, look by dragging (FIRE also drags the camera).
    /// Keyboard + mouse in the editor / WebGL: WASD, right mouse look, left mouse fire, Space, Shift, R, Q, 1-3.
    /// </summary>
    public class TouchControls : MonoBehaviour
    {
        public Vector2 Move;        // x = right, y = forward (-1..1)
        public Vector2 LookDelta;   // pixels this frame
        public bool Sprint;         // auto-run toggle
        public bool JumpPressed;    // true for one frame
        public bool FireHeld;       // finger / mouse on FIRE
        public bool FirePressed;    // true for the frame FIRE went down
        public bool FireReleased;   // true for the frame FIRE went up
        public bool HolsterPressed; // the fist slot was tapped
        /// <summary>scoped: only FIRE, SCOPE, the joystick and the minimap stay on screen (web #hud.scoped-ui)</summary>
        public bool ScopedUi;
        /// <summary>lobby preview for the layout editor: every control shown, nothing reacts</summary>
        public bool Preview;
        int frostCount;
        /// <summary>ice wall: first tap shows where it would stand, FIRE places it, tapping again cancels</summary>
        public bool FrostAim;
        bool scopeAllowed = true; RectTransform gearRt; Vector2? jumpGround;
        public bool Scoped;         // scope toggle
        public int SlotPressed = -1;// weapon slot tapped this frame (-1 none)
        public bool ReloadPressed;  // true for one frame
        public Canvas Canvas => canvas;
        /// <summary>the safe-area part of the HUD canvas: put controls and HUD pieces here</summary>
        public RectTransform Root => root;
        RectTransform root;
        public bool Crouch, Prone;      // posture toggles
        public bool HealPressed, FrostPressed, BagPressed, GearPressed; // one frame each
        Btn crouchBtn, proneBtn, healBtn, frostBtn, jumpBtn;
        int airMode = -1;
        Text healBadge, frostBadge;

        class Btn { public RectTransform rt, icon; public Image bg; public Sprite normal; public bool pressed; }
        /// <summary>everything the layout editor can move / resize / fade (web HUD editor)</summary>
        public readonly List<(string name, RectTransform rt)> Editable = new List<(string, RectTransform)>();
        public bool Editing;
        /// <summary>settings / backpack: nothing outside them moves. (The big map only stops the camera turning: you keep running.)</summary>
        public static bool ModalOpen => SettingsPanel.IsOpen || BagPanel.IsOpen;
        readonly List<Btn> btns = new List<Btn>();
        readonly List<RectTransform> blockers = new List<RectTransform>();
        Btn fireBtn, fireLeft, scopeBtn, runBtn;
        RectTransform joyBase, joyKnob;
        int joyFinger = -1, lookFinger = -1, fireFinger = -1;
        Vector2 joyOrigin, lookLast;
        const float JoyRadius = 35f * UiKit.K;
        Canvas canvas;

        public static TouchControls Create()
        {
            var tc = new GameObject("Controls").AddComponent<TouchControls>();
            tc.BuildUi();
            return tc;
        }

        void BuildUi()
        {
            canvas = new GameObject("HUD").AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            UiKit.Scale(canvas.gameObject.AddComponent<CanvasScaler>());
            root = UiKit.SafeRoot(canvas); // everything clear of the front camera
            canvas.gameObject.AddComponent<GraphicRaycaster>();
            if (FindFirstObjectByType<EventSystem>() == null) { var es = new GameObject("EventSystem"); es.AddComponent<EventSystem>(); es.AddComponent<StandaloneInputModule>(); }

            // joystick: thin white ring on a faint dark disc, white knob (web .joy-base / .joy-knob)
            joyBase = UiKit.Img(root, "Joystick", UiKit.Disc, Vector2.zero, new Vector2(40, 50), new Vector2(120, 120), new Color(0, 0, 0, 0.18f)).rectTransform;
            var ring = UiKit.Img(joyBase, "ring", UiKit.Ring, new Vector2(.5f, .5f), Vector2.zero, new Vector2(120, 120), new Color(1, 1, 1, 0.5f));
            joyKnob = UiKit.Img(joyBase, "Knob", UiKit.Disc, new Vector2(.5f, .5f), Vector2.zero, new Vector2(50, 50), new Color(1, 1, 1, 0.75f)).rectTransform;
            Editable.Add(("joy", joyBase));

            Vector2 BR = new Vector2(1, 0), BL = Vector2.zero, TL = new Vector2(0, 1);
            // positions = the web defaults (style.css .btn-*): centre from the corner, in CSS px
            fireBtn = Button("fire", "btn_fire", 84, BR, new Vector2(-68, 150), null);
            fireLeft = Button("fire", "btn_fire", 60, TL, new Vector2(60, -139), null);
            jumpBtn = Button("jump", "btn", 64, BR, new Vector2(-56, 58), () => JumpPressed = true);
            Button("reload", "btn", 50, BR, new Vector2(-153, 125), () => ReloadPressed = true);
            scopeBtn = Button("scope", "btn", 52, BR, new Vector2(-148, 190), () => Scoped = !Scoped);
            runBtn = Button("sprint", "btn", 52, BL, new Vector2(194, 202), () => { Sprint = !Sprint; if (Sprint) Crouch = Prone = false; });
            crouchBtn = Button("crouch", "btn", 54, BR, new Vector2(-131, 49), () => { Crouch = !Crouch; if (Crouch) { Prone = false; Sprint = false; } });
            proneBtn = Button("prone", "btn", 50, BR, new Vector2(-201, 45), () => { Prone = !Prone; if (Prone) { Crouch = false; Sprint = false; } });
            healBtn = Button("heal", "btn", 50, BL, new Vector2(215, 95), () => HealPressed = true);
            frostBtn = Button("frost", "btn", 48, BR, new Vector2(-220, 188), () => FrostPressed = true);
            healBadge = Badge(healBtn); frostBadge = Badge(frostBtn);
            Button("bag", "btn", 42, TL, new Vector2(125, -27), () => BagPressed = true);
            // settings: small dark square, top-left (web .gear-btn)
            var gear = UiKit.Img(root, "gear", UiKit.Round, TL, new Vector2(56, -8), new Vector2(30, 26), new Color(0, 0, 0, .25f), true);
            gear.gameObject.AddComponent<Outline>().effectColor = new Color(1, 1, 1, .3f);
            var gi = UiKit.Img(gear.rectTransform, "icon", UiKit.Get("icon_settings"), new Vector2(.5f, .5f), Vector2.zero, new Vector2(18, 18), Color.white); gi.rectTransform.pivot = new Vector2(.5f, .5f);
            var gt = gear.gameObject.AddComponent<EventTrigger>(); var ge = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown }; ge.callback.AddListener(_ => { if (!Editing) GearPressed = true; }); gt.triggers.Add(ge);
            gearRt = gear.rectTransform;
            blockers.Add(gear.rectTransform);
            Editable.Add(("gear", gear.rectTransform));
            LayoutEditor.Apply(this);
        }

        Btn Button(string icon, string bg, float size, Vector2 corner, Vector2 centre, System.Action onDown)
        {
            // the exported button picture has 14% padding for its shadow: 100 px button = 128 px picture
            var img = UiKit.Img(root, icon, UiKit.Get(bg), corner, Vector2.zero, Vector2.one * size * 1.28f, Color.white, true);
            var rt = img.rectTransform; rt.pivot = new Vector2(.5f, .5f); rt.anchoredPosition = centre * UiKit.K;
            var ic = UiKit.Img(rt, "icon", UiKit.Get("icon_" + icon), new Vector2(.5f, .5f), Vector2.zero, Vector2.one * size * 0.58f, Color.white);
            ic.rectTransform.pivot = new Vector2(.5f, .5f);
            var b = new Btn { rt = rt, bg = img, normal = img.sprite, icon = ic.rectTransform };
            string key = icon; int dup = 2; while (Editable.Exists(e => e.name == key)) key = icon + dup++;
            Editable.Add((key, rt));
            var trigger = rt.gameObject.AddComponent<EventTrigger>();
            var down = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
            down.callback.AddListener(_ => { if (Editing) return; b.pressed = true; onDown?.Invoke(); });
            var up = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
            up.callback.AddListener(_ => b.pressed = false);
            trigger.triggers.Add(down); trigger.triggers.Add(up);
            btns.Add(b); blockers.Add(rt);
            return b;
        }

        Text Badge(Btn b)
        {
            // yellow count bubble on the top-right of the button (web .btn-heal b)
            var bub = UiKit.Img(b.rt, "badge", UiKit.Round, new Vector2(1, 1), new Vector2(-6, -6), new Vector2(18, 18), new Color(1f, .83f, .3f));
            bub.rectTransform.pivot = new Vector2(1, 1);
            var t = UiKit.Txt(bub.rectTransform, new Vector2(.5f, .5f), Vector2.zero, new Vector2(18, 18), 11, TextAnchor.MiddleCenter, new Color(.07f, .13f, .06f));
            t.rectTransform.pivot = new Vector2(.5f, .5f); Destroy(t.GetComponent<Shadow>()); t.text = "0";
            return t;
        }

        /// <summary>another HUD piece the layout editor can move (weapon box, minimap); the saved place is applied at once</summary>
        public void AddEditable(string name, RectTransform rt) { Editable.Add((name, rt)); LayoutEditor.ApplyOne(name, rt); }

        /// <summary>in the plane / sky-diving only JUMP shows (gold in the plane), like the web #hud.in-plane</summary>
        public void SetAir(int air)
        {
            if (air == airMode) return;
            // in the plane / falling JUMP sits in the middle of the right side (easy to reach); back on the ground it returns
            if (air > 0 && airMode <= 0) { jumpGround = jumpBtn.rt.anchoredPosition; jumpBtn.rt.anchoredPosition = new Vector2(-70, 540f / UiKit.K) * UiKit.K; }
            if (air == 0 && airMode > 0 && jumpGround.HasValue) jumpBtn.rt.anchoredPosition = jumpGround.Value;
            airMode = air;
            jumpBtn.bg.color = air == 1 ? new Color(1f, .85f, .35f) : Color.white;
            if (air != 0) { Sprint = false; Crouch = Prone = false; Scoped = false; }
        }

        bool Shown(Btn b)
        {
            if (Editing || Preview) return true;
            if (airMode > 0) return b == jumpBtn;
            if (b == frostBtn && frostCount <= 0 && !FrostAim) return false;
            if (b == scopeBtn && !scopeAllowed) return false;
            return !ScopedUi || b == fireBtn || b == fireLeft || b == scopeBtn;
        }

        /// <summary>counts on the heal / ice-wall buttons (0 = faded, like the web)</summary>
        public void SetCounts(int meds, int frost)
        {
            // no "0" bubbles: an empty medkit button is just its icon, the ice-wall button disappears until you carry one
            healBadge.text = meds.ToString(); frostBadge.text = frost.ToString();
            healBadge.transform.parent.gameObject.SetActive(meds > 0); frostBadge.transform.parent.gameObject.SetActive(frost > 0);
            healBtn.bg.color = new Color(1, 1, 1, meds > 0 ? 1f : .6f);
            frostCount = frost;
        }

        /// <summary>a HUD element that takes taps (weapon slots): touches on it don't turn the camera</summary>
        public void RegisterBlocker(RectTransform rt) { blockers.Add(rt); }

        public void ShowScope(bool show) { scopeAllowed = show; if (!show) Scoped = false; }

        public void SetVisible(bool v) { canvas.enabled = v; }

        bool Over(Btn b, Vector2 screen) => b != null && b.rt.gameObject.activeInHierarchy && !LayoutEditor.IsHidden(b.rt) && Hit(b.rt, screen);
        // the round part only (the picture is 1.28x the button)
        static bool Hit(RectTransform rt, Vector2 screen)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, screen, null, out Vector2 local)) return false;
            return local.magnitude <= rt.rect.width / 2.56f * 1.1f;
        }
        bool OverBlocker(Vector2 screen)
        {
            foreach (RectTransform r in blockers)
                if (r.gameObject.activeInHierarchy && !LayoutEditor.IsHidden(r) && (btns.Exists(b => b.rt == r) ? Hit(r, screen) : RectTransformUtility.RectangleContainsScreenPoint(r, screen))) return true;
            return false;
        }

        void Update()
        {
            LookDelta = Vector2.zero;
            foreach (Btn b in btns) { bool show = Shown(b); if (b.rt.gameObject.activeSelf != show) b.rt.gameObject.SetActive(show); }
            bool gearOn = Editing || Preview || (!ScopedUi && airMode <= 0); // no settings in the plane / during the drop if (gearRt.gameObject.activeSelf != gearOn) gearRt.gameObject.SetActive(gearOn);
            if (Editing || Preview) { Move = Vector2.zero; FireHeld = false; joyFinger = lookFinger = fireFinger = -1; return; }
            // a panel is open (settings, backpack, big map): its drags and taps stay inside it, nothing moves outside
            if (ModalOpen) { Move = Vector2.zero; FireHeld = false; joyFinger = lookFinger = fireFinger = -1; joyKnob.anchoredPosition = Vector2.zero; return; }
            bool wasHeld = FireHeld;
            if (Input.touchCount > 0) Touches();
            else
            {
                if (joyFinger >= 0) joyKnob.anchoredPosition = Vector2.zero;
                joyFinger = lookFinger = fireFinger = -1;
                Desktop();
            }
            if (wasHeld && !FireHeld) FireReleased = true;
            // button states like the web: pressed = gold ring, RUN / SCOPE on = green
            Sprite pressed = UiKit.Get("btn_pressed"), active = UiKit.Get("btn_active");
            foreach (Btn b in btns)
            {
                bool fire = b == fireBtn || b == fireLeft;
                bool down = fire ? FireHeld : b.pressed;
                bool on = (b == runBtn && Sprint) || (b == scopeBtn && Scoped) || (b == frostBtn && FrostAim) || (b == crouchBtn && Crouch) || (b == proneBtn && Prone);
                b.bg.sprite = on ? active : down && !fire ? pressed : b.normal;
                // (pressed: the icon dips; the button's own scale belongs to the layout editor)
                if (b.icon != null) b.icon.localScale = Vector3.one * (down ? 0.86f : 1f);
            }
        }

        void LateUpdate() { JumpPressed = false; FirePressed = false; FireReleased = false; HolsterPressed = false; ReloadPressed = false; SlotPressed = -1; HealPressed = FrostPressed = BagPressed = GearPressed = false; }

        void Touches()
        {
            Vector2 move = Move;
            for (int k = 0; k < Input.touchCount; k++)
            {
                Touch t = Input.GetTouch(k);
                if (t.phase == TouchPhase.Began)
                {
                    if ((Over(fireBtn, t.position) || Over(fireLeft, t.position)) && fireFinger < 0)
                    {
                        fireFinger = t.fingerId; FirePressed = true; Sprint = false;
                        if (lookFinger < 0) { lookFinger = t.fingerId; lookLast = t.position; }
                    }
                    else if (OverBlocker(t.position)) continue;
                    else if (t.position.x < Screen.width * 0.42f && joyFinger < 0) { joyFinger = t.fingerId; joyOrigin = t.position; Sprint = false; }
                    else if (lookFinger < 0) { lookFinger = t.fingerId; lookLast = t.position; }
                }
                if (t.fingerId == fireFinger && (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)) fireFinger = -1;
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
                    if (!Minimap.BigOpen) LookDelta += t.position - lookLast;
                    lookLast = t.position;
                    if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled) lookFinger = -1;
                }
            }
            Move = move;
            FireHeld = fireFinger >= 0;
        }

        void Desktop()
        {
            Vector2 m = new Vector2((Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0), (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0));
            Move = m.sqrMagnitude > 1 ? m.normalized : m;
            if (Input.GetKeyDown(KeyCode.Space)) JumpPressed = true;
            if (Input.GetKeyDown(KeyCode.LeftShift)) Sprint = !Sprint;
            if (Input.GetMouseButton(1)) LookDelta = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 12f;
            Vector2 mp = Input.mousePosition;
            bool onFire = Over(fireBtn, mp) || Over(fireLeft, mp);
            bool onUi = OverBlocker(mp) && !onFire;
            if (Input.GetMouseButtonDown(0) && !onUi) { FirePressed = true; Sprint = false; }
            if (Input.GetKeyDown(KeyCode.C)) { Crouch = !Crouch; Prone = false; }
            if (Input.GetKeyDown(KeyCode.Z)) { Prone = !Prone; Crouch = false; }
            if (Input.GetKeyDown(KeyCode.H)) HealPressed = true;
            if (Input.GetKeyDown(KeyCode.Tab)) BagPressed = true;
            FireHeld = Input.GetMouseButton(0) && !onUi;
            if (Input.GetKeyDown(KeyCode.R)) ReloadPressed = true;
            if (Input.GetKeyDown(KeyCode.Q) && scopeAllowed) Scoped = !Scoped;
            if (Input.GetKeyDown(KeyCode.X)) HolsterPressed = true;
            if (Input.GetKeyDown(KeyCode.F)) FrostPressed = true;
            for (int k = 0; k < 3; k++) if (Input.GetKeyDown(KeyCode.Alpha1 + k)) SlotPressed = k;
        }
    }
}
