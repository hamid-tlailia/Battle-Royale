using UnityEngine;

namespace LastWar
{
    /// <summary>
    /// Third-person soldier: walk / auto-run / jump with the web game's numbers, camera over the shoulder that
    /// avoids walls, shooting through the crosshair (the dot in the middle of the screen), scope zoom.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        public const float WalkSpeed = 4.6f, SprintSpeed = 6.6f, JumpVelocity = 7.2f, Gravity = -22f, GroundAccel = 40f, AirAccel = 10f;
        public TouchControls input;
        public Camera cam;
        public float yaw, pitch = -0.15f;
        public float lookSensitivity = 0.2f; // degrees per screen pixel
        /// <summary>the joystick also turns the view (degrees a second at full push to the side; ?joyturn=0 turns it off)</summary>
        public static bool JoyTurn = false; public static float JoyTurnSpeed = 95f; float joyHold; // (off: the stick only moves him; ?joyturn=1 to try it)
        public Fighter fighter;
        public SoldierBody body;

        CharacterController cc;
        Vector3 velocity;
        float camDist = 3.6f;
        /// <summary>looking through the optic (first person): long-range gun in the hands, on the ground</summary>
        public bool Scoped => input.Scoped && fighter.Alive && fighter.air == 0 && !fighter.holstered && fighter.Gun != null && (fighter.Gun.def.id == "rifle" || fighter.Gun.def.id == "sniper");
        /// <summary>the web game's optics: red dot on the rifle (52 deg), x8 on the sniper (10 deg)</summary>
        /// <summary>zoom of the optic on the gun in your hands: the scope you attached, else the rifle's x2 / the sniper's x8</summary>
        /// <summary>1 = the rifle's own red-dot sight (no scope attached); 2 / 4 / 8 = a scope; the sniper's own is x8</summary>
        public int ScopeZoom => fighter.scope > 0 ? fighter.scope : fighter.Gun != null && fighter.Gun.def.id == "sniper" ? 8 : 1;
        float ScopeFov => ScopeZoom >= 8 ? 10f : ScopeZoom >= 4 ? 18f : ScopeZoom >= 2 ? 34f : 52f; // web SCOPE_FOV
        bool sniperHold, sniperAuto, wasScoped, wasGrounded = true; float unscopeAt, stepAcc, airTime, turn;
        /// <summary>the crosshair is on a living enemy (no wall in between)</summary>
        public Fighter OnEnemy { get; private set; }
        public static bool AimAssistOn = true; // false = full control (settings, phase 3)
        Fighter lockTarget; // (the enemy the aim assist holds now: no joystick turning meanwhile)
        public readonly AimAssist Assist = new AimAssist();
        /// <summary>tests (?aatest=1): a look turn in degrees a second, fed in like the finger's (friction applies)</summary>
        public Vector2 TestLook;
        /// <summary>tests: the aim angles (yaw degrees, pitch radians) and the controls</summary>
        public float TestYaw { get => yaw; set => yaw = value; }
        public float TestPitch { get => pitch; set => pitch = Mathf.Clamp(value, -1.2f, 1.2f); }
        public TouchControls TestInput => input;
        public Camera TestCam => cam;
        /// <summary>tests: the living enemy the crosshair ray is on right now (what the shot would hit), or null</summary>
        public Fighter CrosshairEnemy()
        {
            Ray r = ViewRay(); float best = float.MaxValue; Fighter on = null;
            foreach (RaycastHit h in Physics.RaycastAll(r, 320f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.collider.transform.IsChildOf(transform) || h.distance >= best) continue;
                best = h.distance; Fighter q = h.collider.GetComponentInParent<Fighter>(); on = q != null && q != fighter && q.Alive ? q : null;
            }
            return on;
        }

        public static PlayerController Spawn(Vector3 pos, TouchControls input, Camera cam)
        {
            var go = new GameObject("Player");
            go.layer = MapBuilder.PlayerLayer;
            go.transform.position = pos;
            var cc = go.AddComponent<CharacterController>();
            cc.height = 1.8f; cc.radius = 0.4f; cc.center = new Vector3(0, 0.9f, 0);
            cc.slopeLimit = 60f; cc.stepOffset = 0.45f; cc.skinWidth = 0.04f;
            var pc = go.AddComponent<PlayerController>();
            pc.input = input; pc.cam = cam;
            pc.fighter = go.AddComponent<Fighter>();
            pc.fighter.isPlayer = true; pc.fighter.displayName = "You";
            // the web game: you land empty-handed and loot (?armed=1 in the WebGL URL: start with guns, for tests)
            string url = Application.absoluteURL ?? "";
            if (url.Contains("armed=1") || url.Contains("sniper=1") || url.Contains("nodrop=1")) pc.fighter.GiveStartingGuns("rifle", "shotgun");
            else pc.fighter.holstered = true;
            pc.body = SoldierBody.Attach(go, Profile.Skin, Profile.Parts); pc.body.LiveBack = true; // (your own soldier: the pack stays against the back in every pose)
            if (url.Contains("gunshow=1")) pc.StartCoroutine(pc.GunShow());
            return pc;
        }

        void Awake() { cc = GetComponent<CharacterController>(); }

        /// <summary>?gunshow=1 (tests): every gun model laid on the ground in a row in front of you</summary>
        System.Collections.IEnumerator GunShow()
        {
            yield return new WaitForSeconds(2f);
            while (fighter.air != 0) yield return null;
            Vector3 f = Quaternion.Euler(0, yaw, 0) * Vector3.forward, r = Quaternion.Euler(0, yaw, 0) * Vector3.right;
            for (int i = 0; i < GunFrame.Looks.Length; i++)
            {
                Vector3 at = transform.position + f * (2.2f + (i / 5) * 1.3f) + r * ((i % 5) - 2) * 1.2f;
                if (Physics.Raycast(at + Vector3.up * 3f, Vector3.down, out RaycastHit h, 8f, ~(1 << MapBuilder.PlayerLayer), QueryTriggerInteraction.Ignore)) at = h.point;
                Loot.Spawn(Loot.Kind.Weapon, GunFrame.Looks[i], 1, at);
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            input.SniperAiming = false; // (set again below while a sniper shot is held)
            if (vaultT >= 0f) { VaultStep(dt); return; }
            if (!fighter.Alive) { if (frostGhost != null) frostGhost.gameObject.SetActive(false); body.Play("death"); body.SetGun(null); body.Tilt = 0; body.Pose = null; body.SetChute(false); if (cc.enabled) cc.enabled = false; return; }
            if (GameBoot.Instance != null && GameBoot.Instance.Result != 0 && (GameBoot.Instance.Result < 0 || Hud.Celebrating)) { body.Play("idle_rifle"); return; } // (won: still moving until the victory screen)
            // (web: radians per pixel x sensitivity x zoom x scope sensitivity x 0.8 while the dot is red)
            // (x the aim assist's friction: the view turns slower over / near the enemy it holds)
            float sens = lookSensitivity * Profile.Sensitivity * (Scoped ? cam.fieldOfView / 70f * Profile.ScopeSensitivity : 1f) * Assist.SensFactor;
            yaw += input.LookDelta.x * sens + TestLook.x * dt * Assist.SensFactor;
            pitch = Mathf.Clamp(pitch + input.LookDelta.y * sens * Mathf.Deg2Rad + TestLook.y * dt * Assist.SensFactor * Mathf.Deg2Rad, -1.2f, 1.2f); // Unity screen Y points up: drag down = look down
            lookThisFrame = input.LookDelta * sens + TestLook * dt;
            if (fighter.air != 0) { sniperHold = false; AirUpdate(dt); return; }
            body.Tilt = 0; body.Pose = null; body.SetChute(false);
            // the joystick turns the soldier too: pushed to a side the view swings that way (faster the further round),
            // so sweeping it round in a circle turns him all the way round. Straight ahead / back: no turn; not while
            // shooting, scoped, aiming the ice wall or held on a target
            {
                Vector2 jm = input.Move; float jl = jm.magnitude;
                // (never against the hand turning the view: while a finger drags the view, and half a second after)
                if (input.Looking) joyHold = Time.time + 0.5f;
                if (JoyTurn && jl > 0.5f && !Scoped && !input.FireHeld && !input.FrostAim && lockTarget == null && !input.FreeLook && Time.time >= joyHold)
                {
                    float ja = Mathf.Atan2(jm.x, jm.y) * Mathf.Rad2Deg, aa = Mathf.Abs(ja);
                    if (aa > 15f && aa < 165f) yaw += Mathf.Sign(ja) * Mathf.Sin(Mathf.Clamp(aa, 15f, 165f) * Mathf.Deg2Rad) * JoyTurnSpeed * Mathf.InverseLerp(0.5f, 1f, jl) * dt;
                }
            }

            // weapons
            // holding the snowball (ice wall aimed): a tap on the weapon box only puts it away - back to the gun / fists
            if (input.FrostAim && (input.HolsterPressed || input.SlotPressed >= 0 || input.ReloadPressed)) { input.FrostAim = false; input.HolsterPressed = false; input.SlotPressed = -1; input.ReloadPressed = false; }
            // a tap on the weapon box (switch / reload / fists) closes the scope first
            if (input.HolsterPressed || input.SlotPressed >= 0 || input.ReloadPressed) { input.Scoped = false; sniperHold = false; unscopeAt = 0; }
            if (input.HolsterPressed) fighter.ToggleHolster();
            if (input.SlotPressed >= 0) { if (input.SlotPressed == fighter.slot && !fighter.holstered) fighter.Reload(); else fighter.SelectSlot(input.SlotPressed); } // tap the equipped gun = reload
            if (input.ReloadPressed) fighter.Reload();
            if (input.HealPressed) fighter.Heal();
            if (input.BagPressed) BagPanel.Toggle(input, fighter);
            if (input.GearPressed) SettingsPanel.Open(input.Canvas, true);
            bool armed = !fighter.holstered && fighter.Gun != null;

            // posture: stand 1.8 m, crouch 1.2 m, prone 0.7 m (jump = stand up)
            if (input.JumpPressed && (input.Crouch || input.Prone)) { input.Crouch = input.Prone = false; input.JumpPressed = false; }
            float targetH = input.Prone ? 0.7f : input.Crouch ? 1.2f : 1.8f;
            if (!Mathf.Approximately(cc.height, targetH))
            {
                float h = Mathf.MoveTowards(cc.height, targetH, dt * 6f);
                // standing up under something low: stay down
                if (h > cc.height && Physics.SphereCast(transform.position + Vector3.up * 0.3f, 0.3f, Vector3.up, out _, h, ~(1 << MapBuilder.PlayerLayer), QueryTriggerInteraction.Ignore)) h = cc.height;
                cc.height = h; cc.radius = Mathf.Min(0.4f, h / 2f); cc.center = new Vector3(0, h / 2f, 0);
                fighter.HeadHeight = h;
            }

            // movement relative to the camera's heading
            Vector2 mv = input.Move;
            if (input.Sprint && mv.y > -0.3f) mv = new Vector2(mv.x * 0.5f, 1f); // locked run keeps going through a quick scope / reload / jump
            bool sprinting = input.Sprint && mv.y > 0.5f && !Scoped; // (FIRE on the ground ends the run in the controls; from a jump it goes on)
            // crawling is slow and goes in pulls (the body moves with the arms, not as a slide); firing while lying down
            // stops the crawl - the soldier holds still in the prone aiming pose while the shots go, and crawls on after
            bool proneFire = input.Prone && armed && fighter.Gun != null && (input.FireHeld || Time.time - fighter.lastShotAt < 0.35f);
            float speed = (sprinting ? SprintSpeed : WalkSpeed) * (input.Prone ? (proneFire ? 0f : 0.2f * (body.Pose == "crawl" ? body.CrawlPull : 1f)) : input.Crouch ? 0.55f : 1f);
            Quaternion heading = Quaternion.Euler(0, yaw, 0);
            Vector3 want = heading * new Vector3(mv.x, 0, mv.y) * speed;
            bool grounded = cc.isGrounded;
            Vector3 flat = new Vector3(velocity.x, 0, velocity.z);
            flat = Vector3.MoveTowards(flat, want, (grounded ? GroundAccel : AirAccel) * dt);
            velocity.x = flat.x; velocity.z = flat.z;
            if (grounded && velocity.y < 0) velocity.y = -2f;
            // JUMP in front of a window, low wall or fence: climb through / over it (web vault)
            if (input.JumpPressed && (grounded || NearGround()) && TryVault()) { input.JumpPressed = false; return; }
            if (input.JumpPressed && (grounded || NearGround())) velocity.y = JumpVelocity;
            velocity.y += Gravity * dt;
            cc.Move(velocity * dt);
            AirMove.KeepOnMap(transform, cc); // (never off the map's edge)

            Separate(transform, cc, dt);
            RunAimAssist(dt);
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            Vector3 aim = AimDirection(out Vector3 eye);
            body.AimDir = aim;
            if (DebugAim && Time.time > nextAimLog)
            {
                nextAimLog = Time.time + 2f;
                Vector3 cf = cam.transform.forward; cf.y = 0; Vector3 af = aim; af.y = 0;
                Debug.Log($"[LastWar] aim: yaw(aim vs camera) {Vector3.SignedAngle(cf, af, Vector3.up):0.0} pitch {Mathf.Asin(Mathf.Clamp(aim.y, -1, 1)) * Mathf.Rad2Deg:0.0} body vs camera {Vector3.SignedAngle(cf, transform.forward, Vector3.up):0.0}");
            }
            body.SetGun(armed && !input.FrostAim ? fighter.Gun.def.Look : null);
            body.Reloading = fighter.reloadEnd > 0f; if (fighter.Gun != null) body.ReloadTime = fighter.Gun.def.reloadTime;
            body.Snowball = input.FrostAim;
            // long guns that are not in the hands hang across the back (the pistol goes in the pocket)
            backList.Clear();
            for (int k = 0; k < 3; k++) { GunState bg = fighter.guns[k]; if (bg != null && bg.def.id != "pistol" && (fighter.holstered || k != fighter.slot)) backList.Add(bg.def.Look); }
            body.SetBack(backList);
            body.SetBag(fighter.bagLv);

            bool onGround = cc.isGrounded || NearGround();
            // standing still (for the controls: a screen drag then circles the camera round him instead of turning him)
            input.Standing = onGround && input.Move.sqrMagnitude < 0.0025f && !input.Sprint && !Scoped && !input.FireHeld && !input.FrostAim && !input.Prone;
            input.InAir = !onGround;
            bool moving = flat.sqrMagnitude > 1f;
            bool fired = false;
            GunState gun = armed ? fighter.Gun : null;
            // FIRE with the guns put away takes the last one back out (web)
            if (fighter.holstered && fighter.HasGun && input.FirePressed && !input.FrostAim) { fighter.ToggleHolster(); input.FirePressed = false; }

            // ice wall: the button shows the half-round wall, FIRE raises it (also mid-jump); the button again cancels
            // a second tap on the ice button raises the wall at once where the preview stands (a quick double tap = a wall
            // straight away); with nowhere to put it the second tap puts the snowball away
            bool frostSecond = input.FrostPressed && input.FrostAim;
            if (input.FrostPressed && !frostSecond) input.FrostAim = fighter.frost > 0;
            if (input.FrostAim && fighter.frost <= 0) input.FrostAim = false;
            if (frostGhost == null) frostGhost = IceWall.Ghost();
            var wallSpot = input.FrostAim ? IceWall.Spot(transform, eye, aim) : null; // (where the crosshair points, up to 3 m farther)
            frostGhost.gameObject.SetActive(input.FrostAim);
            if (input.FrostAim) IceWall.ShowGhost(frostGhost, wallSpot);
            if (frostSecond)
            {
                if (wallSpot != null && wallSpot.Count > 0) { IceWall.Place(wallSpot, transform.position); fighter.frost--; }
                input.FrostAim = false; frostGhost.gameObject.SetActive(false);
            }
            if (input.FrostAim && input.FirePressed)
            {
                if (wallSpot.Count > 0) { IceWall.Place(wallSpot, transform.position); fighter.frost--; input.FrostAim = false; }
                input.FirePressed = false; blockFire = true; // this press built the wall, no shot
            }
            if (blockFire) { if (!input.FireHeld) blockFire = false; else gun = null; }

            // loot: walking over takes it (a gun only into an empty slot); the rest within reach is listed with TAKE
            Nearby.Clear();
            for (int i = Loot.All.Count - 1; i >= 0; i--)
            {
                Loot it = Loot.All[i];
                Vector3 d = it.transform.position - transform.position;
                if (d.y < -0.6f || d.y > 1.8f) continue;
                float h2 = d.x * d.x + d.z * d.z;
                if (h2 < 1.4f * 1.4f && Profile.AutoPickup && fighter.TryPickup(it, false)) { Destroy(it.gameObject); continue; }
                if (h2 < 2.6f * 2.6f) Nearby.Add(it);
            }
            Nearby.Sort((a, b) => (a.kind == Loot.Kind.Weapon ? 0 : 1) - (b.kind == Loot.Kind.Weapon ? 0 : 1));

            if (gun != null && gun.def.id == "sniper")
            {
                // sniper: FIRE down opens the scope, letting go takes the shot (the scope closes again if FIRE opened it)
                if (input.FirePressed) { sniperAuto = !input.Scoped; input.Scoped = true; sniperHold = true; unscopeAt = 0; }
                // X: the shot is called off (the scope closes if FIRE opened it)
                if (sniperHold && input.CancelShot) { sniperHold = false; if (sniperAuto) input.Scoped = false; }
                if (sniperHold && input.FireReleased)
                {
                    sniperHold = false;
                    fired = fighter.Fire(eye, aim, true, moving, input.Crouch || input.Prone, onGround);
                    if (sniperAuto) unscopeAt = Time.time + 0.35f;
                }
            }
            else
            {
                sniperHold = false;
                // fire through the crosshair while held; the shotgun's second blast comes by itself
                bool wantFire = gun != null && (input.FireHeld || input.FirePressed || fighter.BurstPending);
                fired = wantFire && fighter.Fire(eye, aim, Scoped, moving, input.Crouch || input.Prone, onGround);
            }
            input.SniperAiming = sniperHold;
            if (unscopeAt > 0 && Time.time >= unscopeAt) { unscopeAt = 0; input.Scoped = false; }
            bool firing = Time.time - fighter.lastShotAt < 0.35f || (input.FireHeld && gun != null && gun.def.id != "sniper");

            // sounds: optic up / down, footsteps (softer crouched, stone on floors), landing
            if (Scoped != wasScoped) { wasScoped = Scoped; Sfx.Play2D(Scoped ? "scope_on" : "scope_off", 1f); }
            float hs = new Vector3(cc.velocity.x, 0, cc.velocity.z).magnitude;
            if (cc.isGrounded && hs > 1.2f && !input.Prone)
            {
                stepAcc += hs * dt;
                if (stepAcc > (input.Crouch ? 1.4f : 2.0f)) { stepAcc = 0; Sfx.Step(transform.position, true, input.Crouch, OnFloor(transform.position)); }
            }
            if (!onGround) airTime += dt;
            else
            {
                if (!wasGrounded && airTime > 0.25f) Sfx.Play2D("land", 0.5f);
                // landing from a jump with FIRE still held: the run ends (as a shot on the ground does); let go in the air = the run goes on
                if (!wasGrounded && airTime > 0.15f && input.Sprint && input.FireHeld && gun != null) input.Sprint = false;
                airTime = 0;
            }
            wasGrounded = onGround;

            // animation: the web game's clip rules (empty hands swing free, armed walks in the firing stance)
            string clip;
            if (input.Prone) clip = "prone";
            else if (airTime > 0.15f) clip = "jump";
            else if (input.Crouch) clip = firing ? "crouch_fire" : hs > 0.4f ? "crouch_walk" : "crouch_idle";
            else if (hs > 5.2f || (input.Sprint && hs > 0.5f && !Scoped)) clip = firing && armed ? "walk_fire" : "run"; // shooting on the run: the gun up, legs keep running
            // empty hands: a walk when slow, Mixamo's Jog Forward at a full push of the stick (recorded at 2.5 m/s - the
            // walk clip had been played at twice its speed up to 5 m/s), the run when sprinting
            else if (!armed) clip = hs > 2.4f && SoldierBody.HasJog ? "jog" : hs > 0.4f ? "walk" : "idle";
            else if (hs > 0.4f) clip = "walk_fire";
            else clip = firing ? "fire" : "idle_rifle";
            body.Speed = proneFire ? 0f : hs; // (before Play: lying down, it picks the still prone pose or the crawl by it)
            body.Play(clip);
            body.Armed = armed;
            body.AirAim = clip == "jump" && armed && firing; // firing in the air: the upper body aims (the legs keep the jump)
            // the web game's extra poses: kneeling when crouched and still, arms free when running empty-handed, crawling
            body.Pose = input.Prone ? (hs > 0.25f && !proneFire ? "crawl" : null)
                : input.Crouch && hs <= 0.4f && onGround ? "kneel"
                : !armed && clip == "run" ? "runfree" : null;
            Locomotion(body, cc.velocity, yaw, hs, clip, armed, input.Sprint, ref turn, dt);
        }

        readonly System.Collections.Generic.List<string> backList = new System.Collections.Generic.List<string>();

        /// <summary>
        /// Directional locomotion (web CharacterView): the legs / body turn toward where the soldier moves (no sideways
        /// sliding), the walk plays backwards when retreating, and the clip speed follows the real speed.
        /// </summary>
        public static void Locomotion(SoldierBody body, Vector3 vel, float yawDeg, float hs, string clip, bool armed, bool running, ref float turn, float dt)
        {
            float target = 0; bool reverse = false;
            if (hs > 0.4f && clip != "jump")
            {
                float rel = Mathf.DeltaAngle(yawDeg, Mathf.Atan2(vel.x, vel.z) * Mathf.Rad2Deg);
                // armed: the body turns less, so the gun stays in the hands and in view while strafing
                float lim = armed ? 31.5f : 66f;
                if (Mathf.Abs(rel) <= 112f) target = Mathf.Clamp(rel, -lim, lim);
                else { reverse = true; target = Mathf.Clamp(Mathf.DeltaAngle(0, rel - Mathf.Sign(rel) * 180f), -lim, lim); }
            }
            turn += Mathf.DeltaAngle(turn, target) * Mathf.Min(1f, dt * 13f);
            body.Turn = turn;
            float reference = clip == "walk" ? 2.0f : clip == "walk_fire" ? 2.6f : clip == "run" ? 6.0f : clip == "crouch_walk" ? 1.6f : clip == "jog" ? 2.46f : 0f;
            float rate = reference > 0 ? Mathf.Clamp((running && clip == "run" ? Mathf.Max(hs, reference) : hs) / reference, 0.6f, clip == "walk" || clip == "walk_fire" ? 2.6f : 1.6f) : 1f;
            if (clip == "jog") rate = Mathf.Clamp(hs / reference, 0.8f, 2.0f);
            body.Rate = rate * (reverse ? -1f : 1f);
        }

        /// <summary>two soldiers never overlap: each eases away from the other (instead of capsules pushing and jittering)</summary>
        public static void Separate(Transform t, CharacterController cc, float dt)
        {
            if (GameBoot.Instance == null || !cc.enabled) return;
            Vector3 push = Vector3.zero;
            foreach (Fighter f in GameBoot.Instance.Fighters)
            {
                if (f == null || f.transform == t || !f.Alive || f.air != 0) continue;
                Vector3 d = t.position - f.transform.position; d.y = 0;
                if (Mathf.Abs(t.position.y - f.transform.position.y) > 1.6f) continue;
                float m = d.magnitude;
                if (m < 0.8f) push += (m > 0.01f ? d / m : t.right) * (0.8f - m);
            }
            if (push.sqrMagnitude > 1e-6f) cc.Move(Vector3.ClampMagnitude(push, 0.5f) * Mathf.Min(1f, dt * 8f));
        }

        /// <summary>standing on a house floor / roof rather than the ground (stone footsteps)</summary>
        public static bool OnFloor(Vector3 p)
        {
            MapData m = GameBoot.Instance != null ? GameBoot.Instance.Map : null;
            return m != null && p.y > m.HeightAt(-p.x, p.z) + 0.3f;
        }

        /// <summary>direction from the soldier's eye to whatever the crosshair (screen centre) is on</summary>
        public static bool DebugAim; float nextAimLog;
        public Vector3 AimDirection(out Vector3 eye)
        {
            eye = transform.position + Vector3.up * (cc.height - 0.25f);
            Ray r = ViewRay();
            float best = 300f;
            foreach (RaycastHit h in Physics.RaycastAll(r, 300f, ~0, QueryTriggerInteraction.Ignore))
                if (!h.collider.transform.IsChildOf(transform) && h.distance < best) best = h.distance;
            Vector3 target = r.origin + r.direction * best;
            Vector3 d = target - eye;
            // something right in front of the muzzle: shoot straight ahead rather than sideways into it
            if (new Vector2(d.x, d.z).magnitude < 1.2f) return r.direction;
            return d.normalized;
        }

        /// <summary>
        /// The crosshair ray, started level with the soldier instead of at the camera: a trunk, rock or slope between
        /// the camera and your own back must not become the target (it sent hip-fire bullets off to the side).
        /// </summary>
        Ray ViewRay()
        {
            Ray r = freeLooking ? new Ray(aimViewPos, aimViewRot * Vector3.forward) : cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0)); // (free look: the view without it)
            Vector3 head = transform.position + Vector3.up * (cc.height - 0.25f);
            float skip = Mathf.Max(0f, Vector3.Dot(head - r.origin, r.direction) - 0.3f);
            return new Ray(r.origin + r.direction * skip, r.direction);
        }

        // ------------------------------------------------------------ aim assist (same rules as the web game)
        void Angles(Fighter q, out float qYaw, out float qPitch, out float dist, out Vector3 t)
        {
            Vector3 c = cam.transform.position; t = q.transform.position + Vector3.up * 1.05f;
            Vector3 d = t - c; dist = d.magnitude;
            qYaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; qPitch = Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude);
        }

        Vector2 lookThisFrame; bool assistScoped;



        /// <summary>how far (m) the aim line from `c` along `fw` passes from the segment lo..hi, and the segment's nearest point</summary>
        static float AimMiss(Vector3 c, Vector3 fw, Vector3 lo, Vector3 hi, out Vector3 nearest)
        {
            float best = float.MaxValue; nearest = lo;
            for (int i = 0; i <= 12; i++)
            {
                Vector3 p = Vector3.Lerp(lo, hi, i / 12f); Vector3 v = p - c; float along = Vector3.Dot(v, fw);
                if (along <= 0f) continue;
                float m = (v - fw * along).magnitude;
                if (m < best) { best = m; nearest = p; }
            }
            return best;
        }

        bool Los(Vector3 from, Vector3 to, Fighter q)
        {
            Vector3 d = to - from;
            foreach (RaycastHit h in Physics.RaycastAll(from, d.normalized, d.magnitude, ~0, QueryTriggerInteraction.Ignore))
                if (!h.collider.transform.IsChildOf(transform) && !h.collider.transform.IsChildOf(q.transform)) return false;
            return true;
        }

        void RunAimAssist(float dt)
        {
            OnEnemy = null;
            if (cam == null) return;
            // 1) is the crosshair on an enemy right now?
            Ray r = ViewRay();
            float best = float.MaxValue;
            foreach (RaycastHit h in Physics.RaycastAll(r, Scoped ? 320f : 250f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.collider.transform.IsChildOf(transform) || h.distance >= best) continue;
                best = h.distance;
                Fighter q = h.collider.GetComponentInParent<Fighter>();
                OnEnemy = q != null && q != fighter && q.Alive ? q : null;
            }
            // 2) the aim assist (AimAssist.cs): friction, tracking and - scoped / firing - a gentle pull onto the torso
            //    of one enemy held near the crosshair; nothing else turns the soldier
            if (input.FrostAim) { Assist.Release(); lockTarget = null; return; }
            bool scopeNow = Scoped; bool engage = (scopeNow && !assistScoped) || input.FirePressed; assistScoped = scopeNow;
            Vector3 origin = scopeNow ? transform.position + Vector3.up * (cc.height - 0.25f) + Quaternion.Euler(-pitch * Mathf.Rad2Deg, yaw, 0) * Vector3.forward * 0.25f : cam.transform.position;
            Assist.Step(fighter, cam, origin, ref yaw, ref pitch, lookThisFrame, scopeNow, input.FireHeld, dt, engage);
            // the camera turned at once to where the aim now is (it is placed in LateUpdate: this frame's shot went out along
            // last frame's view - the first shot after a catch missed)
            if (!freeLooking) { cam.transform.rotation = Quaternion.Euler(-pitch * Mathf.Rad2Deg, yaw, 0); if (scopeNow) cam.transform.position = origin; }
            lockTarget = Assist.Target;
        }

        bool OnTheMove => cc != null && (!cc.isGrounded || new Vector2(cc.velocity.x, cc.velocity.z).magnitude > 1.2f);
        bool dropStraight, planeViewSet;
        /// <summary>in the plane / falling / under the parachute</summary>
        void AirUpdate(float dt)
        {
            var sky = SkyDrop.Instance;
            if (sky == null) { fighter.air = 0; cc.enabled = true; return; }
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            body.SetGun(null);
            if (fighter.air == 1)
            {
                // first frame: look the way the plane flies, a little down
                if (!planeViewSet) { planeViewSet = true; Vector3 d = sky.Dir; yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; pitch = -0.22f; }
                AirMove.Ride(fighter, cc); body.SetVisible(false);
                input.JumpReady = sky.OverMap && sky.T > 0.5f;
                if ((input.JumpPressed && input.JumpReady) || sky.MustJump) { AirMove.Jump(fighter); body.SetVisible(true); dropStraight = false; }
                return;
            }
            // JUMP while falling: drop straight down (tap again to glide)
            if (input.JumpPressed) dropStraight = !dropStraight;
            Vector2 mv = input.Move;
            Quaternion heading = Quaternion.Euler(0, yaw, 0);
            // stick untouched: glide toward where you look
            Vector3 wish = mv.sqrMagnitude > 0.01f ? heading * new Vector3(mv.x, 0, mv.y) : heading * Vector3.forward;
            AirMove.Fall(fighter, cc, Vector3.ClampMagnitude(wish, 1f), dropStraight, Mathf.Max(0, -pitch), GameBoot.Instance.Map.playRadius, dt);
            body.Tilt = fighter.air == 2 ? 83f : 0f; body.Pose = fighter.air == 2 ? "sky" : fighter.air == 3 ? "chute" : null;
            body.SetChute(fighter.air == 3);
            body.Play("idle");
            body.AimDir = transform.forward;
            if (fighter.air == 0) { velocity = Vector3.zero; dropStraight = false; }
        }

        bool NearGround() => Physics.Raycast(transform.position + Vector3.up * 0.2f, Vector3.down, 0.5f, ~(1 << MapBuilder.PlayerLayer), QueryTriggerInteraction.Ignore);

        /// <summary>things within reach that walking over did not take (shown in the HUD list with TAKE)</summary>
        public readonly System.Collections.Generic.List<Loot> Nearby = new System.Collections.Generic.List<Loot>();
        public void Take(Loot it) { if (it != null && fighter.TryPickup(it, true)) Destroy(it.gameObject); }
        Transform frostGhost; bool blockFire; float camY = float.NaN;

        // ------------------------------------------------------------ vault (through a window / over a low wall)
        float vaultT = -1f; Vector3 vaultFrom, vaultApex, vaultTo;
        bool TryVault()
        {
            int mask = ~(1 << MapBuilder.PlayerLayer);
            Vector3 f = Quaternion.Euler(0, yaw, 0) * Vector3.forward, p = transform.position;
            // something right in front at waist height?
            if (!Physics.Raycast(p + Vector3.up * 0.55f, f, out RaycastHit wall, 1.1f, mask, QueryTriggerInteraction.Ignore)
                && !Physics.Raycast(p + Vector3.up * 1.0f, f, out wall, 1.1f, mask, QueryTriggerInteraction.Ignore)) return false;
            if (wall.collider.GetComponentInParent<Fighter>() != null) return false;
            // the lowest gap at least 0.55 m tall between 0.5 and 1.6 m: a window sill / the top of a fence
            for (float h = 0.5f; h <= 1.6f; h += 0.1f)
            {
                Vector3 o = p + Vector3.up * h;
                if (Physics.Raycast(o, f, wall.distance + 0.9f, mask, QueryTriggerInteraction.Ignore)) continue;
                if (Physics.Raycast(o + Vector3.up * 0.5f, f, wall.distance + 0.9f, mask, QueryTriggerInteraction.Ignore)) continue;
                // somewhere to land on the far side
                Vector3 beyond = p + f * (wall.distance + 1.0f) + Vector3.up * (h + 0.3f);
                if (!Physics.Raycast(beyond, Vector3.down, out RaycastHit floor, h + 3.5f, mask, QueryTriggerInteraction.Ignore)) continue;
                Vector3 land = floor.point + Vector3.up * 0.05f;
                if (Physics.CheckCapsule(land + Vector3.up * 0.45f, land + Vector3.up * 1.4f, 0.3f, mask, QueryTriggerInteraction.Ignore)) continue;
                vaultFrom = p; vaultTo = land;
                vaultApex = p + f * wall.distance + Vector3.up * (h + 0.2f);
                vaultT = 0f; cc.enabled = false; velocity = Vector3.zero;
                body.Play("jump");
                Sfx.Play2D("land", 0.35f, 1.3f);
                return true;
            }
            return false;
        }

        void VaultStep(float dt)
        {
            vaultT = Mathf.Min(1f, vaultT + dt / 0.5f);
            float t = vaultT, u = 1f - t;
            transform.position = u * u * vaultFrom + 2f * u * t * vaultApex + t * t * vaultTo; // a curve over the sill
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            if (vaultT >= 1f) { vaultT = -1f; cc.enabled = true; }
        }

        // ------------------------------------------------------------ spectating after you are out
        public Fighter SpectateTarget;
        public void StartSpectate(Fighter first)
        {
            SpectateTarget = first != null && first.Alive ? first : NextAlive();
        }
        Fighter NextAlive()
        {
            if (GameBoot.Instance == null) return null;
            Fighter best = null; float bd = float.MaxValue;
            foreach (Fighter f in GameBoot.Instance.Fighters)
            {
                if (f == fighter || !f.Alive) continue;
                float d = (f.transform.position - transform.position).sqrMagnitude;
                if (d < bd) { bd = d; best = f; }
            }
            return best;
        }

        void LateUpdate()
        {
            if (cam == null) return;
            freeLooking = false; // (set again below on foot)
            if (!fighter.Alive && SpectateTarget != null)
            {
                if (!SpectateTarget.Alive) SpectateTarget = NextAlive();
                if (SpectateTarget == null) return;
                // over the shoulder of the one you watch, turning with them
                Transform t = SpectateTarget.transform;
                Quaternion sl = Quaternion.Euler(12f, t.eulerAngles.y, 0);
                Vector3 piv = t.position + Vector3.up * 1.5f;
                Vector3 want = piv + sl * new Vector3(0.8f, 0.4f, -3.8f);
                cam.transform.position = Vector3.Lerp(cam.transform.position, want, Time.deltaTime * 6f);
                cam.transform.rotation = Quaternion.Slerp(cam.transform.rotation, sl, Time.deltaTime * 6f);
                cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, 70f, Time.deltaTime * 8f);
                return;
            }
            if (fighter.air == 1 && SkyDrop.Instance != null)
            {
                // chase camera around the plane
                Vector3 pp = SkyDrop.Instance.PlanePos;
                Quaternion lk = Quaternion.Euler(-pitch * Mathf.Rad2Deg, yaw, 0);
                cam.transform.position = pp - lk * Vector3.forward * 30f + Vector3.up * 7f;
                cam.transform.LookAt(pp);
                cam.fieldOfView = 70f;
                return;
            }
            Vector3 pivot = transform.position + Vector3.up * Mathf.Max(1.0f, cc.height - 0.3f); // (prone: the camera stays above the body, not in the ground)
            // smooth the camera over bumps and steps (web camY); big jumps / falls snap
            if (float.IsNaN(camY) || Mathf.Abs(pivot.y - camY) > 1.5f || fighter.air != 0) camY = pivot.y;
            else camY = Mathf.Lerp(camY, pivot.y, Mathf.Min(1f, Time.deltaTime * 14f));
            pivot.y = camY;
            Quaternion look = Quaternion.Euler(-pitch * Mathf.Rad2Deg, yaw, 0);
            // free look (eye button): the camera circles the soldier by its own offset; the aim keeps using `aimView`
            // (where the camera would be without it), so the crosshair's shots still go where the soldier looks
            float fs = lookSensitivity * Profile.Sensitivity;
            if (input.FreeLook && !Scoped) { freeYaw = Mathf.Repeat(freeYaw + input.FreeLookDelta.x * fs + 180f, 360f) - 180f; freePitch = Mathf.Clamp(freePitch + input.FreeLookDelta.y * fs, -55f, 60f); if (input.StandOrbit) standOrbited = true; }
            // circled round while standing: the camera stays where it was left; the moment he moves, shoots or aims he turns
            // to face where it looks (it does not swing back behind him)
            else if (standOrbited && input.Standing && !Scoped) { }
            else if (standOrbited)
            {
                yaw += freeYaw; pitch = Mathf.Clamp(pitch + freePitch * Mathf.Deg2Rad, -1.2f, 1.2f);
                freeYaw = freePitch = 0f; standOrbited = false;
            }
            else { freeYaw = Mathf.LerpAngle(freeYaw, 0f, Time.deltaTime * 9f); freePitch = Mathf.Lerp(freePitch, 0f, Time.deltaTime * 9f); if (Mathf.Abs(freeYaw) < 0.2f && Mathf.Abs(freePitch) < 0.2f) freeYaw = freePitch = 0f; }
            if (Scoped) freeYaw = freePitch = 0f; // (through the optic the view is the aim)
            bool free = freeYaw != 0f || freePitch != 0f;
            freeLooking = free;
            Quaternion aimLook = look;
            if (free) look = Quaternion.Euler(Mathf.Clamp(-pitch * Mathf.Rad2Deg - freePitch, -75f, 80f), yaw + freeYaw, 0);
            body.SetVisible(!Scoped && fighter.air != 1);
            if (Scoped)
            {
                // first person through the optic: the eye, a little forward; your own body is hidden
                camDist = 0f;
                cam.transform.position = transform.position + Vector3.up * (cc.height - 0.25f) + look * Vector3.forward * 0.25f;
                cam.transform.rotation = look;
                cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, ScopeFov, Time.deltaTime * 20f);
                return;
            }
            float targetDist = fighter.air != 0 ? 5.6f : 2.85f; // closer: the soldier fills more of the screen
            camDist = Mathf.Lerp(camDist, targetDist, Time.deltaTime * 12f);
            Vector3 back = look * new Vector3(fighter.air != 0 ? 0.6f : 0.9f, 0.4f, -camDist);
            float len = back.magnitude;
            if (Physics.SphereCast(pivot, 0.2f, back / len, out RaycastHit hit, len, ~(1 << MapBuilder.PlayerLayer), QueryTriggerInteraction.Ignore)) len = Mathf.Max(0.4f, hit.distance - 0.1f);
            cam.transform.position = pivot + back.normalized * len;
            // under the canopy: keep the camera below it so turning never hides the soldier
            if (fighter.air == 3 && cam.transform.position.y > pivot.y + 1.6f) cam.transform.position = new Vector3(cam.transform.position.x, pivot.y + 1.6f, cam.transform.position.z);
            cam.transform.rotation = look;
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, 70f, Time.deltaTime * 14f);
            // the view the aim uses: the camera as it would be without free look
            if (free)
            {
                Vector3 ab = aimLook * new Vector3(fighter.air != 0 ? 0.6f : 0.9f, 0.4f, -camDist);
                aimViewPos = pivot + ab; aimViewRot = aimLook;
            }
        }
        float freeYaw, freePitch; bool freeLooking, standOrbited; Vector3 aimViewPos; Quaternion aimViewRot;
        public bool FreeLooking => freeLooking;
    }
}
