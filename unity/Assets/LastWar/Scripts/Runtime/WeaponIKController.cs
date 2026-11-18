using System.Collections.Generic;
using UnityEngine;

namespace LastWar
{
    public partial class SoldierBody
    {
        /// <summary>
        /// Phase 2 step C: a long gun in the hands. Runs in SoldierBody.LateUpdate after the animation has been evaluated
        /// and copied onto the outfit's skeleton, in this order:
        ///   1. upper body aims (chest turned so the barrel meets the crosshair; aiming clips only)
        ///   2. the gun is placed from the right hand: its RightHandGrip point (weapon_grips.json) sits in the right palm,
        ///      turned the way the rifle clips hold a rifle - measured once per gun on the aiming clip, so the GunFrame holder
        ///      gets one rotation and the model's own root is never touched
        ///   3. the right hand keeps the clip's pose (the Mixamo clips close it on the grip)
        ///   4. the left hand is brought by two-bone IK to the gun's LeftHandGrip, faded in / out (no snapping), the elbow
        ///      bending in the plane the clip already gives it; a short arm (Shadow, Knight) reaches a point slid back along
        ///      the handguard instead of being stretched
        ///   5. the left palm turns onto the grip and the fingers close.
        /// Pistols do not come here (one-handed: their own path).
        /// </summary>
        class WeaponIKController
        {
            readonly SoldierBody b;
            class Cal { public Quaternion gunInHand; public Vector3 gunAtHand; public float armLen; }
            readonly Dictionary<string, Cal> cal = new Dictionary<string, Cal>();
            float leftW; string look;
            public const float FadeSeconds = 0.15f, FadeFrames = 8f;
            public const float ShoulderMax = 20f, ShoulderRate = 90f; // deg, deg/s
            float shoulderNow, lastShoulder, lastHoldBack;
            public float SlideMax = 0.12f;      // how far back along the handguard a short arm may take the left hand (m)
            public float ReachLimit = 0.96f;    // of the arm's full length: beyond it the target slides back
            float nextLog;

            public WeaponIKController(SoldierBody body) { b = body; }
            bool gunShown = true;
            void SetGunShown(bool on)
            {
                if (on == gunShown || b.gun == null) return;
                gunShown = on;
                foreach (Renderer r in b.gun.GetComponentsInChildren<Renderer>(true)) r.enabled = on && b.visible;
            }
            public void Release() { leftW = 0f; }

            Cal Calibrate(string key, WeaponGrips.Profile p)
            {
                var c = new Cal { gunInHand = Quaternion.identity, gunAtHand = Vector3.zero };
                cal[key] = c;
                if (!b.clipIndex.TryGetValue("mx_aim", out int ai) || b.animRoot == null) return c;
                Transform sh = b.SrcBone("RightHand"), sl = b.SrcBone("LeftHand"), smr = b.SrcBone("RightHandMiddle1"), sml = b.SrcBone("LeftHandMiddle1");
                if (sh == null || sl == null) return c;
                int hi = b.srcBones.IndexOf(sh); if (hi < 0 || hi >= b.boneMap.Count) return c;
                clips[ai].SampleAnimation(b.animRoot, 0.4f);
                Vector3 rp = smr != null ? Vector3.Lerp(sh.position, smr.position, 0.8f) : sh.position;
                Vector3 lp = sml != null ? Vector3.Lerp(sl.position, sml.position, 0.8f) : sl.position;
                // the gun turned so its right-hand -> left-hand grip line lies along the clip's palm line, up kept up
                Vector3 v = p.leftHandGrip - p.rightHandGrip, d = lp - rp;
                if (v.sqrMagnitude < 1e-6f || d.sqrMagnitude < 1e-6f) return c;
                Quaternion R = Quaternion.LookRotation(d.normalized, b.transform.up) * Quaternion.Inverse(Quaternion.LookRotation(v.normalized, Vector3.up));
                Vector3 origin = rp - R * p.rightHandGrip;          // RightHandGrip in the right palm (not the model origin)
                Quaternion hand = sh.rotation * b.boneMap[hi];      // the outfit's right hand in that pose
                c.gunInHand = Quaternion.Inverse(hand) * R;
                c.gunAtHand = Quaternion.Inverse(hand) * (origin - sh.position);
                // the outfit's own left arm length (upper arm + forearm + to the palm)
                Transform la = b.PoseBone("LeftArm"), lf = b.PoseBone("LeftForeArm"), hl = b.PoseBone("LeftHand"), lm = b.PoseBone("LeftHandMiddle1");
                if (la != null && lf != null && hl != null)
                    c.armLen = (lf.position - la.position).magnitude + (hl.position - lf.position).magnitude + (lm != null ? (lm.position - hl.position).magnitude * 0.8f : 0.07f);
                return c;
            }

            static readonly HashSet<string> Aiming = new HashSet<string> { "mx_aim", "mx_fire", "mx_crouch_aim", "mx_crouch_fire", "mx_walk" };

            public void Apply(Vector3 aim)
            {
                Transform handR = b.handR, gun = b.gun;
                if (gun == null || handR == null) return;
                // until the animation has posed the body (the skeleton stands in its rest pose, arms out, before the first
                // evaluation - and later than one frame if loading is slow) nothing is solved and the gun is not drawn, so it
                // never shows detached; posed once = latched, a clip change does not reset it
                bool posed = b.CheckPosed();
                if (!posed) { SetGunShown(false); leftW = 0f; FrameLog(false, null, null, 0, 0, null, null, null, Vector3.zero); return; }
                look = b.gunId;
                var p = WeaponGrips.Get(look);
                if (!cal.TryGetValue(look, out Cal c)) c = Calibrate(look, p);

                // 1. upper body aiming
                Quaternion gr = handR.rotation * c.gunInHand;
                // (readout: the reach before the chest is turned, to tell the clip's own reach from the turn's effect)
                Transform laPre = b.PoseBone("LeftArm"), chestPre = b.PoseBone("Spine2");
                Vector3 preTarget = handR.position + handR.rotation * c.gunAtHand + gr * p.leftHandGrip;
                Vector3 preShoulder = laPre != null ? laPre.position : Vector3.zero;
                Quaternion preChest = chestPre != null ? chestPre.rotation : Quaternion.identity;
                float turnApplied = 0f;
                if (b.ShoulderGrip && Aiming.Contains(b.playing))
                {
                    Vector3 barrel = gr * Vector3.back;
                    Quaternion q = Quaternion.FromToRotation(barrel, aim);
                    q.ToAngleAxis(out float ang, out Vector3 ax); if (ang > 180f) ang -= 360f;
                    if (Mathf.Abs(ang) > 0.5f && ax.sqrMagnitude > 0.5f)
                    {
                        Quaternion part = Quaternion.AngleAxis(Mathf.Clamp(ang, -70f, 70f) / 3f, ax);
                        turnApplied = Mathf.Clamp(ang, -70f, 70f);
                        foreach (string s in new[] { "Spine", "Spine1", "Spine2" }) { Transform t = b.PoseBone(s); if (t != null) t.rotation = part * t.rotation; }
                        gr = handR.rotation * c.gunInHand;
                    }
                }
                // 2-3. the gun from the right hand (the holder only)
                gun.rotation = gr;
                gun.position = handR.position + handR.rotation * c.gunAtHand;

                // 4-5. the left hand onto the LeftHandGrip
                bool want = b.playing != "mx_reload" && !b.current.StartsWith("death");
                leftW = Mathf.MoveTowards(leftW, want ? 1f : 0f, Mathf.Min(Time.deltaTime / FadeSeconds, 1f / FadeFrames)); // at least 8 frames AND 0.15 s
                float w = leftW * leftW * (3f - 2f * leftW); // smoothstep: eases in and out
                Transform la = b.PoseBone("LeftArm"), lf = b.PoseBone("LeftForeArm"), hl = b.PoseBone("LeftHand");
                if (w <= 0.001f || la == null || lf == null || hl == null) { SetGunShown(true); Log(p, c, 0, 0, la, lf, hl, Vector3.zero); FrameLog(true, p, c, w, 0, la, lf, hl, Vector3.zero); return; }
                Vector3 back = gun.TransformDirection(Vector3.forward); // toward the stock
                // B. holdBack (weapon_grips.json, long-span guns only): the gun drawn back along its barrel by bending the right
                //    arm; the right hand keeps its rotation, so the gun stays on it (right palm on RightHandGrip)
                float hb = UseHoldBack ? p.holdBack * w : 0f;
                if (hb > 0.0005f)
                {
                    Transform ra = b.PoseBone("RightArm"), rf = b.PoseBone("RightForeArm");
                    if (ra != null && rf != null)
                    {
                        Quaternion keep = handR.rotation;
                        b.TwoBone(ra, rf, handR, handR.position + back * hb);
                        handR.rotation = keep;
                        gun.rotation = handR.rotation * c.gunInHand;
                        gun.position = handR.position + handR.rotation * c.gunAtHand;
                        back = gun.TransformDirection(Vector3.forward);
                    }
                }
                Vector3 target = gun.TransformPoint(p.leftHandGrip);
                float slide = 0f, shoulderWant = 0f;
                Transform clav = b.PoseBone("LeftShoulder");
                if (c.armLen > 0.1f)
                {
                    float over = (target - la.position).magnitude - ReachLimit * c.armLen;
                    // A. shoulder reach: beyond the arm's reach the support collarbone turns toward the target (as a person
                    //    reaching forward does), at most 20 deg, eased over time; generic for every character and gun
                    if (UseShoulderReach && over > 0f && clav != null)
                    {
                        Vector3 cl = la.position - clav.position, ct = target - clav.position;
                        float full = Vector3.Angle(cl, ct), clavLen = cl.magnitude;
                        // the turn that brings the shoulder joint 'over' metres nearer (small-angle: arc = length x angle)
                        shoulderWant = Mathf.Min(ShoulderMax, full, clavLen > 0.01f ? over / clavLen * Mathf.Rad2Deg : 0f);
                    }
                    shoulderNow = Mathf.MoveTowards(shoulderNow, shoulderWant, ShoulderRate * Time.deltaTime);
                    float sdeg = shoulderNow * w;
                    if (sdeg > 0.05f && clav != null)
                    {
                        Vector3 ax = Vector3.Cross(la.position - clav.position, target - clav.position);
                        if (ax.sqrMagnitude > 1e-8f) clav.rotation = Quaternion.AngleAxis(sdeg, ax.normalized) * clav.rotation;
                    }
                    lastShoulder = sdeg; lastHoldBack = hb;
                    over = (target - la.position).magnitude - ReachLimit * c.armLen;
                    if (over > 0f) { slide = Mathf.Min(SlideMax, over * 1.15f); target += back * slide; }
                }
                // the palm turned onto the grip: under the handguard palm up (fingers round its right side); a vertical
                // fore-grip (smg_hornet) held from behind
                Vector3 up = gun.up, barrelDir = -back, side = Vector3.Cross(up, barrelDir).normalized; // side: the shooter's right
                Vector3 k, n;
                if (p.leftHandStyle == "vertical") { k = (side * 0.8f - up * 0.6f).normalized; n = barrelDir; }
                else { k = (side * 0.75f + barrelDir * 0.66f).normalized; n = up; }
                Quaternion animUpper = la.rotation, animFore = lf.rotation, animHand = hl.rotation;
                var fingers = SaveFingers("Left");
                for (int it = 0; it < 2; it++)
                {
                    b.OrientHand(hl, lf, "Left", k, n);
                    Vector3 palmOff = b.Palm("Left") - hl.position;
                    b.TwoBone(la, lf, hl, target - palmOff);
                }
                b.OrientHand(hl, lf, "Left", k, n);
                b.Curl("Left", 48f, 55f, 32f, 0f, barrelDir);
                if (w < 0.999f)
                {
                    la.rotation = Quaternion.Slerp(animUpper, la.rotation, w);
                    lf.rotation = Quaternion.Slerp(animFore, lf.rotation, w);
                    hl.rotation = Quaternion.Slerp(animHand, hl.rotation, w);
                    BlendFingers(fingers, w);
                }
                SetGunShown(true); // (placed and solved: drawn from this frame)
                FailLog(p, c, w, slide, la, lf, hl, target, preTarget, preShoulder, preChest, turnApplied);
                Log(p, c, w, slide, la, lf, hl, target);
                FrameLog(true, p, c, w, slide, la, lf, hl, target);
            }

            float nextFail;
            /// <summary>with ?gundbg=1: whenever the left palm ends more than 1.5 cm off its target with the IK fully on, the
            /// whole situation - which character / gun / clip, both shoulders, the target and palm, the chest before and after
            /// the aiming turn, and the reach before and after that turn</summary>
            void FailLog(WeaponGrips.Profile p, Cal c, float w, float slide, Transform la, Transform lf, Transform hl, Vector3 target,
                Vector3 preTarget, Vector3 preShoulder, Quaternion preChest, float turn)
            {
                if (!GunDebug || w < 0.99f || Time.time < nextFail) return;
                Vector3 lp = b.Palm("Left"); float err = (lp - target).magnitude;
                if (err < 0.015f) return;
                nextFail = Time.time + 1.5f;
                Transform ra = b.PoseBone("RightArm"), chest = b.PoseBone("Spine2");
                float elbow = Vector3.Angle(la.position - lf.position, hl.position - lf.position);
                float reachPre = c.armLen > 0 ? (preTarget - preShoulder).magnitude / c.armLen : 0f;
                float reachPost = c.armLen > 0 ? (target + b.gun.TransformDirection(Vector3.forward) * -slide - la.position).magnitude / c.armLen : 0f; // (before the slide)
                string who = b.outfitRoot != null ? b.outfitRoot.name.Replace("(Clone)", "") : "?";
                Debug.Log($"[LastWar] wikfail {b.name} {who} {look} {b.playing}: palm off {err * 100f:0.0} cm | L shoulder {la.position:F3} R shoulder {(ra != null ? ra.position : Vector3.zero):F3} | LeftHandGrip {target:F3} (slid {slide * 100f:0} cm) palm {lp:F3} | chest turn applied {turn:0} deg, chest rotation changed {(chest != null ? Quaternion.Angle(preChest, chest.rotation) : 0):0} deg | reach (target-shoulder / arm) BEFORE turn {reachPre:0.00} AFTER turn {reachPost:0.00} | elbow {elbow:0} ext {(lp - la.position).magnitude / c.armLen:0.00} | arm {c.armLen:0.00} m | shoulder {lastShoulder:0.0} deg holdBack {lastHoldBack * 100f:0.0} cm");
            }

            /// <summary>?wikframes=N : every frame of the first N after the body was built, one line each</summary>
            public static int FramesToLog;
            void FrameLog(bool posed, WeaponGrips.Profile p, Cal c, float w, float slide, Transform la, Transform lf, Transform hl, Vector3 target)
            {
                int f = Time.frameCount - b.builtFrame;
                if (FramesToLog <= 0 || f > FramesToLog) return;
                if (!posed) { Debug.Log($"[LastWar] wikf {b.name} f{f}: source skeleton max {b.SrcMaxAngle:0.000} deg from bind, clip ticks {b.ClipAdvances} -> posed NO -> IK off, gun hidden ({(b.gun != null ? "drawn " + b.gun.GetComponentInChildren<Renderer>()?.enabled : "no gun")}), left weight {leftW:0.00}"); return; }
                Transform gun = b.gun;
                float rErr = (gun.TransformPoint(p.rightHandGrip) - b.Palm("Right")).magnitude;
                float lErr = w > 0.001f ? (b.Palm("Left") - target).magnitude : -1f;
                float elbow = la != null && lf != null && hl != null ? Vector3.Angle(la.position - lf.position, hl.position - lf.position) : 0f;
                float wrist = lf != null && hl != null ? Vector3.Angle(hl.position - lf.position, b.HandDir(hl, "Left")) : 0f;
                float ext = c != null && c.armLen > 0 && la != null ? (b.Palm("Left") - la.position).magnitude / c.armLen : 0f;
                Debug.Log($"[LastWar] wikf {b.name} f{f} dt {Time.deltaTime * 1000f:0}ms: posed yes ({b.PosedBy}) clip {b.playing} | IK {(w > 0.001f ? "on" : "fading in")} weight {w:0.00} | right {rErr * 100f:0.0} cm left {(lErr < 0 ? "-" : (lErr * 100f).ToString("0.0") + " cm")} slide {slide * 100f:0} | elbow {elbow:0} ext {ext:0.00} wrist {wrist:0} | gun {gun.position:F3} drawn {gun.GetComponentInChildren<Renderer>()?.enabled}");
            }

            static string VisibleSkin(Transform bone)
            {
                // which SkinnedMeshRenderer uses this bone, and is it drawn
                foreach (var r in bone.root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (System.Array.IndexOf(r.bones, bone) >= 0) return $"skinned by '{r.name}' enabled {r.enabled}";
                return "skinned by nothing";
            }
            struct Saved { public Transform t; public Quaternion r; }
            readonly List<Saved> saved = new List<Saved>();
            List<Saved> SaveFingers(string sd)
            {
                saved.Clear();
                if (b.fingerRest != null) foreach (var kv in b.fingerRest) if (kv.Key.name.Contains(sd + "Hand")) saved.Add(new Saved { t = kv.Key, r = kv.Key.localRotation });
                return saved;
            }
            static void BlendFingers(List<Saved> before, float w)
            {
                foreach (Saved s in before) s.t.localRotation = Quaternion.Slerp(s.r, s.t.localRotation, w);
            }

            void Log(WeaponGrips.Profile p, Cal c, float w, float slide, Transform la, Transform lf, Transform hl, Vector3 target)
            {
                if (!GunDebug || Time.time < nextLog) return;
                nextLog = Time.time + 1.5f;
                Transform gun = b.gun;
                Vector3 rp = b.Palm("Right"), lp = b.Palm("Left");
                float rErr = (gun.TransformPoint(p.rightHandGrip) - rp).magnitude;
                float lErr = w > 0 ? (lp - target).magnitude : -1f;
                float elbow = la != null && lf != null && hl != null ? Vector3.Angle(la.position - lf.position, hl.position - lf.position) : 0f;
                float wrist = lf != null && hl != null ? Vector3.Angle(hl.position - lf.position, b.HandDir(hl, "Left")) : 0f;
                float ext = c.armLen > 0 && la != null ? (lp - la.position).magnitude / c.armLen : 0f;
                if (la != null && w > 0)
                {
                    string Path(Transform t) { string r = t.name; for (Transform q = t.parent; q != null && q != b.transform.parent; q = q.parent) r = q.name + "/" + r; return r; }
                    Transform root = b.transform;
                    Debug.Log($"[LastWar] wik3 {b.name} {look}: LHG world {target:F3} | LHG in character space {root.InverseTransformPoint(target):F3} | shoulder {la.position:F3} elbow {lf.position:F3} wrist {hl.position:F3} | upper arm {(lf.position - la.position).magnitude:0.000} forearm {(hl.position - lf.position).magnitude:0.000} | gun origin {gun.position:F3} right palm {b.Palm("Right"):F3}");
                    Debug.Log($"[LastWar] wik4 {b.name}: LeftArm path {Path(la)} | LeftForeArm path {Path(lf)} | LeftHand path {Path(hl)} | RightHand path {Path(b.handR)} | gun path {Path(gun)}");
                    Debug.Log($"[LastWar] wik5 {b.name}: lossyScale root {root.lossyScale.x:0.000} gun {gun.lossyScale.x:0.000} LeftArm {la.lossyScale.x:0.000} LeftHand {hl.lossyScale.x:0.000} RightHand {b.handR.lossyScale.x:0.000} outfit {(b.outfitRoot != null ? b.outfitRoot.lossyScale.x : 0):0.000} base {(b.model != null ? b.model.lossyScale.x : 0):0.000} | renderers on LeftArm's skeleton visible {(la.GetComponentInParent<Animator>() == null ? "(no animator)" : "")} {VisibleSkin(la)}");
                }
                if (la != null && w > 0) Debug.Log($"[LastWar] wik2 {b.name} {look}: target-shoulder {(target - la.position).magnitude:0.00} m armLen {c.armLen:0.00} | target-gun origin {(target - gun.position).magnitude:0.00} | LHG {p.leftHandGrip:F2} | left arm in outfit {(b.outfitRoot != null && la.IsChildOf(b.outfitRoot))} | hand in outfit {(b.outfitRoot != null && b.handR.IsChildOf(b.outfitRoot))} | gun lossyScale {gun.lossyScale.x:0.000} | palm-hand {(b.Palm("Left") - hl.position).magnitude:0.00}");
                Debug.Log($"[LastWar] wik {b.name} {look} {b.playing}: right palm to RightHandGrip {rErr * 100f:0.0} cm | left weight {w:0.00} palm to target {(lErr < 0 ? "-" : (lErr * 100f).ToString("0.0") + " cm")} slide {slide * 100f:0} cm shoulder {lastShoulder:0.0} deg holdBack {lastHoldBack * 100f:0.0} cm | elbow {elbow:0} deg wrist bend {wrist:0} deg arm extension {ext:0.00} | barrel pitch {Mathf.Asin(Mathf.Clamp((gun.rotation * Vector3.back).y, -1, 1)) * Mathf.Rad2Deg:0}");
            }
        }
    }
}
