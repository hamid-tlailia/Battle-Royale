using UnityEngine;

namespace LastWar
{
    /// <summary>
    /// Aim assist (the feel of a mobile shooter's: the crosshair is helped onto a near enemy's body, it never snaps).
    /// It only ever adds small turns to the aim angles (yaw / pitch) the PlayerController already has; the camera follows
    /// the aim as before, the gun and its IK are not touched.
    ///
    /// The target: a living enemy in sight (the line to his chest or hips is clear: walls, the ice wall block it) whose
    /// torso - hips to neck, read from his skeleton, so crouching and lying down count - passes within a catch circle round
    /// the crosshair. The circle is the angle of `ConeRadius` metres at his distance (wide near, narrow far) between
    /// ConeMin and ConeMax degrees. One target at a time: kept while he stays inside a wider circle (x Hold) and in sight;
    /// let go `LoseTime` s after he is out of it / hidden / dead; never handed over to another enemy while it holds, and
    /// never while firing.
    ///
    /// Three forces, each x Strength (settings) x a fall-off with distance (full up to FullDist m, none at the range):
    ///   friction - the view turns slower while the crosshair is over / near the body (down to x Friction on it);
    ///   tracking - the aim moves with the target as seen from the camera (his strafing, your own moving), so it stays on;
    ///   pull     - only through the scope or while firing: a gradual turn toward the nearest point of the torso,
    ///              at most PullMax deg/s, a fifth of it while your finger moves the view away from him; none once the
    ///              crosshair is on the body (where on the body you aim is yours: the head too).
    /// Through a sniper scope: friction and tracking only (SniperPull = 0).
    /// Every value can be set from the WebGL address for tests: ?aa_str= ?aa_range= ?aa_cone= ?aa_fric= ?aa_pull=
    /// ?aa_pullmax= ?aa_track= ?aa_full= ; ?aadbg=1 logs the target, distance, error and forces twice a second.
    /// </summary>
    public class AimAssist
    {
        // ---- settings (the settings page: on / off, strength, range)
        public static float Strength = 0.7f;   // 0..1
        public static float Range = 70f;       // m (from the hip; through a scope x ScopeRangeMul, never past the gun's range)
        // ---- tuning
        // (a generous catch, as mobile shooters have: 2.2 m round his torso, never under 3 degrees, x1.5 through a scope)
        public static float ConeRadius = 2.2f, ConeMin = 3f, ConeMax = 18f, ScopeConeMul = 1.5f, Hold = 1.6f;
        public static float FullDist = 10f, ScopeRangeMul = 1.8f;
        public static float Friction = 0.42f;
        public static float PullRate = 6f, PullMax = 50f, SniperPull = 0f;
        public static float Track = 0.92f;
        public static float LoseTime = 0.3f;
        public static bool Debug;

        public Fighter Target { get; private set; }
        /// <summary>the look sensitivity factor for the next frame (friction): 1 = none</summary>
        public float SensFactor { get; private set; } = 1f;
        public float LastError { get; private set; } = -1f;
        public float LastHalfWidth { get; private set; }

        float lostFor, prevYaw, prevPitch, nextLog, awayFor; bool havePrev, locked; Vector3 prevRoot, vel;
        /// <summary>held fast: caught the moment the scope opened / FIRE went down; glued while the scope / FIRE stay</summary>
        public bool Locked => locked && Target != null;

        public static void ReadUrl(string u)
        {
            float F(string key, float d)
            {
                int i = u.IndexOf(key + "="); if (i < 0) return d;
                int j = i + key.Length + 1, k = j;
                while (k < u.Length && (char.IsDigit(u[k]) || u[k] == '.' || u[k] == '-')) k++;
                return float.TryParse(u.Substring(j, k - j), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : d;
            }
            Strength = F("aa_str", Strength); Range = F("aa_range", Range); ConeRadius = F("aa_cone", ConeRadius);
            Friction = F("aa_fric", Friction); PullRate = F("aa_pull", PullRate); PullMax = F("aa_pullmax", PullMax);
            Track = F("aa_track", Track); FullDist = F("aa_full", FullDist);
            Debug = u.Contains("aadbg=1");
        }

        public void Release() { Target = null; havePrev = false; lostFor = 0f; SensFactor = 1f; LastError = -1f; locked = false; awayFor = 0f; }

        static Vector3 Dir(float yaw, float pitchDeg)
        {
            float y = yaw * Mathf.Deg2Rad, p = pitchDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(y) * Mathf.Cos(p), Mathf.Sin(p), Mathf.Cos(y) * Mathf.Cos(p));
        }
        static void AnglesOf(Vector3 d, out float yaw, out float pitchDeg)
        {
            yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; pitchDeg = Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
        }

        /// <summary>the enemy's torso line, hips to the base of the neck (his skeleton as posed now)</summary>
        static void Torso(Fighter q, out Vector3 lo, out Vector3 hi)
        {
            var b = q.GetComponent<SoldierBody>();
            if (b != null && b.TorsoLine(out lo, out hi)) return;
            float h = Mathf.Clamp(q.HeadHeight, 0.6f, 1.8f) / 1.8f;
            lo = q.transform.position + Vector3.up * 0.95f * h; hi = q.transform.position + Vector3.up * 1.45f * h;
        }

        struct Seen { public float err, allowed, halfW, dist, pYaw, pPitch, cYaw, cPitch; }

        /// <summary>where the torso is as seen from the camera: the angle from the aim line to its nearest point, the catch
        /// circle, the body's half width (deg) and the angles of that point and of the torso's middle</summary>
        static Seen Look(Fighter q, Vector3 c, Vector3 fw, bool scoped)
        {
            Torso(q, out Vector3 lo, out Vector3 hi);
            Vector3 best = lo; float bm = float.MaxValue;
            for (int i = 0; i <= 10; i++)
            {
                Vector3 p = Vector3.Lerp(lo, hi, i / 10f), v = p - c; float along = Vector3.Dot(v, fw);
                float m = along > 0f ? (v - fw * along).magnitude / along : float.MaxValue;
                if (m < bm) { bm = m; best = p; }
            }
            var s = new Seen();
            Vector3 mid = (lo + hi) * 0.5f;
            s.dist = Vector3.Distance(c, mid);
            s.err = Vector3.Angle(fw, best - c);
            s.allowed = Mathf.Clamp(Mathf.Atan2(ConeRadius, s.dist) * Mathf.Rad2Deg, ConeMin, ConeMax) * (scoped ? ScopeConeMul : 1f);
            s.halfW = Mathf.Atan2(0.2f, s.dist) * Mathf.Rad2Deg;
            AnglesOf(best - c, out s.pYaw, out s.pPitch); AnglesOf(mid - c, out s.cYaw, out s.cPitch);
            return s;
        }

        /// <summary>tests: the angle (deg) from the aim at (yaw, pitch radians) from `c` to the nearest point of `q`'s torso,
        /// and the body's half width (deg) there</summary>
        public static float ErrorTo(Fighter q, Vector3 c, float yaw, float pitch, out float halfW)
        {
            Seen s = Look(q, c, Dir(yaw, pitch * Mathf.Rad2Deg), false); halfW = s.halfW; return s.err;
        }
        /// <summary>tests: the aim angles (yaw deg, pitch radians) at the middle of `q`'s torso from `c`</summary>
        public static void AnglesAt(Fighter q, Vector3 c, out float yaw, out float pitch)
        {
            Torso(q, out Vector3 lo, out Vector3 hi); AnglesOf((lo + hi) * 0.5f - c, out yaw, out float pd); pitch = pd * Mathf.Deg2Rad;
        }
        public static bool Visible(Vector3 c, Fighter q, Transform me) => InSight(c, q, me);

        static bool InSight(Vector3 c, Fighter q, Transform me)
        {
            Torso(q, out Vector3 lo, out Vector3 hi);
            return Clear(c, (lo + hi) * 0.5f, q, me) || Clear(c, hi, q, me) || Clear(c, lo, q, me);
        }
        static bool Clear(Vector3 from, Vector3 to, Fighter q, Transform me)
        {
            Vector3 d = to - from;
            foreach (RaycastHit h in Physics.RaycastAll(from, d.normalized, d.magnitude, ~0, QueryTriggerInteraction.Ignore))
                if (!h.collider.transform.IsChildOf(me) && !h.collider.transform.IsChildOf(q.transform)) return false;
            return true;
        }

        /// <summary>one frame: turns yaw (deg) / pitch (radians) a little; `look` = the finger's look input this frame</summary>
        /// <param name="engage">this frame the scope opened or FIRE went down: an enemy inside the catch circle is caught at once</param>
        /// <param name="origin">where the view is seen from this frame (the eye through the scope - the camera was still behind
        /// the shoulder the frame the scope opened, and the catch missed by degrees)</param>
        public void Step(Fighter me, Camera cam, Vector3 origin, ref float yaw, ref float pitch, Vector2 look, bool scoped, bool firing, float dt, bool engage = false)
        {
            if (!PlayerController.AimAssistOn || Strength <= 0f || cam == null || me == null || me.holstered || me.Gun == null || GameBoot.Instance == null || dt <= 0f)
            { Release(); return; }
            bool sniper = scoped && me.Gun.def.id.StartsWith("sniper");
            Vector3 c = origin;
            float pitchDeg = pitch * Mathf.Rad2Deg;
            Vector3 fw = Dir(yaw, pitchDeg);
            float range = Mathf.Min(Range * (scoped ? ScopeRangeMul : 1f), me.Gun.def.range > 0f ? me.Gun.def.range : 250f);

            // keep the one held while he stays near the crosshair and in sight; let go a moment after he is not
            if (Target != null)
            {
                bool ok = Target.Alive && Target.air == 0;
                if (ok)
                {
                    Seen k = Look(Target, c, fw, scoped);
                    ok = k.dist <= range * 1.1f && k.err <= k.allowed * Hold && InSight(c, Target, me.transform);
                }
                lostFor = ok ? 0f : lostFor + dt;
                if (!Target.Alive || lostFor > LoseTime) Release();
            }
            // nobody held: the enemy whose torso is nearest the crosshair
            if (engage && Target != null && !locked) Target = null; // (a fresh catch: the one nearest the crosshair now)
            // (nobody held: picked even while FIRE is held - firing first and then sweeping onto him caught nothing; while one
            // is held, another never takes over)
            if (Target == null)
            {
                Fighter best = null; float bs = float.MaxValue;
                foreach (Fighter q in GameBoot.Instance.Fighters)
                {
                    if (q == me || !q.Alive || q.air != 0) continue;
                    if ((q.transform.position - c).sqrMagnitude > range * range) continue;
                    if (Vector3.Dot(q.transform.position - c, fw) <= 0f) continue;
                    Seen k = Look(q, c, fw, scoped);
                    if (k.dist > range || k.err > k.allowed) continue;
                    float score = k.err / k.allowed + 0.3f * k.dist / range;
                    if (score >= bs || !InSight(c, q, me.transform)) continue;
                    bs = score; best = q;
                }
                if (best != null) { Target = best; havePrev = false; lostFor = 0f; }
            }
            if (Target == null) { SensFactor = 1f; LastError = -1f; locked = false; return; }
            if (!scoped && !firing) locked = false;
            // the instant catch: the scope just opened / FIRE just went down with him inside the circle - the aim goes onto
            // his chest at once (a sniper scope: no, the shot is the player's own)
            if (engage && !sniper && lostFor <= 0f)
            {
                Seen e0 = Look(Target, c, fw, scoped);
                if (e0.err <= e0.allowed)
                {
                    Torso(Target, out Vector3 lo0, out Vector3 hi0); AnglesOf(Vector3.Lerp(lo0, hi0, 0.62f) - c, out float sy, out float sp);
                    yaw = sy; pitchDeg = sp; fw = Dir(yaw, pitchDeg); locked = true; awayFor = 0f; havePrev = false;
                }
            }
            if (lostFor > 0f) { SensFactor = 1f; havePrev = false; return; } // (hidden this moment: nothing pulls toward a wall)

            Seen s = Look(Target, c, fw, scoped);
            float fall = s.dist <= FullDist ? 1f : 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FullDist, range, s.dist));
            float st = Strength * fall;
            // how near the crosshair is to the body: 1 on it, 0 at the catch circle's edge
            float close = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(s.halfW, s.allowed, s.err));
            LastError = s.err; LastHalfWidth = s.halfW;

            // friction
            SensFactor = Mathf.Lerp(1f, Friction, locked ? Mathf.Max(st * close, 0.6f) : st * close);
            // scoped / firing and he comes into the circle: held fast too (pulled in quickly below, no snap)
            if (!locked && (scoped || firing) && !sniper && s.err <= s.allowed) { locked = true; awayFor = 0f; }
            // caught but the finger drags firmly away from him (half a second): let go - the hand always wins
            if (locked) { awayFor = s.err > s.allowed && look.sqrMagnitude > 1e-6f ? awayFor + dt : 0f; if (awayFor > 0.5f) { Release(); return; } }

            // tracking: the torso's middle moved (as seen from here) since the last frame - the aim follows a share of it
            // (from his root, not his bones: the torso bobs with the run cycle and following that shook the aim; his speed
            // smoothed over ~0.15 s, the aim turned by how much that moves him as seen from here this frame)
            float trackYaw = 0f, trackPitch = 0f;
            Vector3 root = Target.transform.position;
            if (havePrev)
            {
                Vector3 v = (root - prevRoot) / dt;
                if (v.magnitude > 20f) v = Vector3.zero; // (teleported / respawned: not followed)
                vel = Vector3.Lerp(vel, v, 1f - Mathf.Exp(-dt / 0.15f));
                Vector3 mid = root + Vector3.up * 1.15f, ahead = mid + vel * dt;
                AnglesOf(mid - c, out float y0, out float p0); AnglesOf(ahead - c, out float y1, out float p1);
                float k = locked ? 1f : Track * st * close; // (caught: follows him fully)
                trackYaw = Mathf.DeltaAngle(y0, y1) * k; trackPitch = (p1 - p0) * k;
            }
            else vel = Vector3.zero;
            prevRoot = root; prevYaw = s.cYaw; prevPitch = s.cPitch; havePrev = true;
            yaw += trackYaw; pitchDeg += trackPitch;

            // pull: through the scope or while firing, onto the body (not once on it)
            float pull = 0f;
            if ((scoped || firing) && s.err > s.halfW * 0.6f)
            {
                float rate = (locked ? 16f : PullRate * st) * (sniper ? SniperPull : 1f); // (caught: onto him within a few frames)
                pull = Mathf.Min((s.err - s.halfW * 0.5f) * (1f - Mathf.Exp(-rate * dt)), (locked ? 240f : PullMax * st) * dt); // (to the body's edge, not past its middle)
                if (pull < 0f) pull = 0f;
                Vector2 e = new Vector2(Mathf.DeltaAngle(yaw, s.pYaw), s.pPitch - pitchDeg);
                if (e.sqrMagnitude > 1e-8f)
                {
                    e.Normalize();
                    if (look.sqrMagnitude > 1e-6f && Vector2.Dot(look.normalized, e) < -0.2f) pull *= 0.2f; // (your finger leads)
                    yaw += e.x * pull; pitchDeg += e.y * pull;
                }
            }
            pitch = Mathf.Clamp(pitchDeg * Mathf.Deg2Rad, -1.2f, 1.2f);

            if (Debug && Time.time >= nextLog)
            {
                nextLog = Time.time + 0.5f;
                UnityEngine.Debug.Log($"[LastWar] aa {Target.displayName} {s.dist:0.0} m err {s.err:0.00} deg (body {s.halfW:0.00} circle {s.allowed:0.00}) fall {fall:0.00} sens x{SensFactor:0.00} track {trackYaw:0.000}/{trackPitch:0.000} pull {pull:0.000} {(scoped ? "scope" : "hip")}{(firing ? " fire" : "")}");
            }
        }
    }
}
