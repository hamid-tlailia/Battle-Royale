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
                SetGunShown(posed);
                if (!posed) { leftW = 0f; FrameLog(false, null, null, 0, 0, null, null, null, Vector3.zero); return; }
                look = b.gunId;
                var p = WeaponGrips.Get(look);
                if (!cal.TryGetValue(look, out Cal c)) c = Calibrate(look, p);

                // 1. upper body aiming
                Quaternion gr = handR.rotation * c.gunInHand;
                if (b.ShoulderGrip && Aiming.Contains(b.playing))
                {
                    Vector3 barrel = gr * Vector3.back;
                    Quaternion q = Quaternion.FromToRotation(barrel, aim);
                    q.ToAngleAxis(out float ang, out Vector3 ax); if (ang > 180f) ang -= 360f;
                    if (Mathf.Abs(ang) > 0.5f && ax.sqrMagnitude > 0.5f)
                    {
                        Quaternion part = Quaternion.AngleAxis(Mathf.Clamp(ang, -70f, 70f) / 3f, ax);
                        foreach (string s in new[] { "Spine", "Spine1", "Spine2" }) { Transform t = b.PoseBone(s); if (t != null) t.rotation = part * t.rotation; }
                        gr = handR.rotation * c.gunInHand;
                    }
                }
                // 2-3. the gun from the right hand (the holder only)
                gun.rotation = gr;
                gun.position = handR.position + handR.rotation * c.gunAtHand;

                // 4-5. the left hand onto the LeftHandGrip
                bool want = b.playing != "mx_reload" && !b.current.StartsWith("death");
                leftW = Mathf.MoveTowards(leftW, want ? 1f : 0f, Time.deltaTime * 5f);
                float w = leftW * leftW * (3f - 2f * leftW); // smoothstep: eases in and out
                Transform la = b.PoseBone("LeftArm"), lf = b.PoseBone("LeftForeArm"), hl = b.PoseBone("LeftHand");
                if (w <= 0.001f || la == null || lf == null || hl == null) { Log(p, c, 0, 0, la, lf, hl, Vector3.zero); FrameLog(true, p, c, w, 0, la, lf, hl, Vector3.zero); return; }
                Vector3 target = gun.TransformPoint(p.leftHandGrip);
                Vector3 back = gun.TransformDirection(Vector3.forward); // toward the stock
                float slide = 0f;
                if (c.armLen > 0.1f)
                {
                    float over = (target - la.position).magnitude - ReachLimit * c.armLen;
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
                Log(p, c, w, slide, la, lf, hl, target);
                FrameLog(true, p, c, w, slide, la, lf, hl, target);
            }

            /// <summary>?wikframes=N : every frame of the first N after the body was built, one line each</summary>
            public static int FramesToLog;
            void FrameLog(bool posed, WeaponGrips.Profile p, Cal c, float w, float slide, Transform la, Transform lf, Transform hl, Vector3 target)
            {
                int f = Time.frameCount - b.builtFrame;
                if (FramesToLog <= 0 || f > FramesToLog) return;
                if (!posed) { Debug.Log($"[LastWar] wikf {b.name} f{f}: animation posed NO -> IK off, gun hidden ({(b.gun != null ? "drawn " + b.gun.GetComponentInChildren<Renderer>()?.enabled : "no gun")}), left weight {leftW:0.00}"); return; }
                Transform gun = b.gun;
                float rErr = (gun.TransformPoint(p.rightHandGrip) - b.Palm("Right")).magnitude;
                float lErr = w > 0.001f ? (b.Palm("Left") - target).magnitude : -1f;
                float elbow = la != null && lf != null && hl != null ? Vector3.Angle(la.position - lf.position, hl.position - lf.position) : 0f;
                float wrist = lf != null && hl != null ? Vector3.Angle(hl.position - lf.position, b.HandDir(hl, "Left")) : 0f;
                float ext = c != null && c.armLen > 0 && la != null ? (b.Palm("Left") - la.position).magnitude / c.armLen : 0f;
                Debug.Log($"[LastWar] wikf {b.name} f{f}: posed yes ({b.PosedBy}) clip {b.playing} | IK {(w > 0.001f ? "on" : "fading in")} weight {w:0.00} | right {rErr * 100f:0.0} cm left {(lErr < 0 ? "-" : (lErr * 100f).ToString("0.0") + " cm")} slide {slide * 100f:0} | elbow {elbow:0} ext {ext:0.00} wrist {wrist:0} | gun {gun.position:F3} drawn {gun.GetComponentInChildren<Renderer>()?.enabled}");
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
                Debug.Log($"[LastWar] wik {b.name} {look} {b.playing}: right palm to RightHandGrip {rErr * 100f:0.0} cm | left weight {w:0.00} palm to target {(lErr < 0 ? "-" : (lErr * 100f).ToString("0.0") + " cm")} slide {slide * 100f:0} cm | elbow {elbow:0} deg wrist bend {wrist:0} deg arm extension {ext:0.00} | barrel pitch {Mathf.Asin(Mathf.Clamp((gun.rotation * Vector3.back).y, -1, 1)) * Mathf.Rad2Deg:0}");
            }
        }
    }
}
