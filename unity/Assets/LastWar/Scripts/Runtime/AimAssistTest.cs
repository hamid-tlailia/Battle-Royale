using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LastWar
{
    /// <summary>
    /// Aim assist tests in a real match (?play=1&nodrop=1&god=1&aatest=1 ; aatest=2: against the match's own bots).
    ///
    /// aatest=1 - on a flat platform high above the map (no trees / houses in the way), one dummy enemy (any outfit,
    /// cannot die) standing or strafing (3.5 m/s, side to side), at 5 / 15 / 30 / 60 m, from the hip (firing) and
    /// through the scope, with the assist off and on. Each run: the aim starts 10 degrees to the left of him, a finger
    /// swipe turns it at 30 deg/s for as long as it takes to reach him plus 2 degrees (a slight overshoot, as a hand
    /// does), then the finger lets go and the aim is watched for 1.5 s:
    ///   on body  = share of that time the crosshair is on his torso (within its half width)
    ///   mean/max = the angle from the crosshair to his torso (deg)
    ///   jitter   = how unsteady the aim's own turning is (RMS of its frame-to-frame change of turn rate, deg/s per frame)
    /// Then: two enemies crossing (one walks in front of the other) - the one held must stay held (switches = 0); one
    /// walking behind a wall - the assist must let go (time from hidden to let go) and not turn the view toward the
    /// wall while he is hidden.
    /// aatest=2 - the match's own bots, moving as they do: every few seconds the nearest one in sight within 45 m, the
    /// same swipe + hold, assist on and off in turn; the same numbers.
    /// Every result is a "[LastWar] aatest ..." line in the log.
    /// </summary>
    public static class AimAssistTest
    {
        public static IEnumerator Run(GameBoot gb, int mode)
        {
            yield return new WaitForSeconds(3f);
            Time.captureDeltaTime = 1f / 30f; // (every frame 1/30 s of game time, however slow the machine: the same swipe every run)
            PlayerController pc = gb.Player; Fighter me = pc.fighter;
            me.God = true;
            for (int k = 0; k < 3; k++) if (me.guns[k] != null && me.guns[k].def.id == "rifle") { me.SelectSlot(k); break; }
            if (me.holstered) me.ToggleHolster();
            if (mode == 2) { yield return RealBots(gb, pc, me); yield break; }

            // the bots are put away; a platform high above the spawn
            foreach (Fighter f in gb.Fighters) if (f != me) f.gameObject.SetActive(false);
            Vector3 o = pc.transform.position + Vector3.up * 160f;
            var plat = GameObject.CreatePrimitive(PrimitiveType.Cube); plat.name = "aaPlatform";
            plat.transform.position = o - Vector3.up * 0.5f; plat.transform.localScale = new Vector3(400f, 1f, 400f);
            plat.GetComponent<Renderer>().sharedMaterial = MapBuilder.NewLit(new Color(.42f, .46f, .4f));
            Teleport(pc, o + Vector3.up * 0.05f);
            yield return new WaitForSeconds(0.5f);
            Fighter a = Dummy(gb, o + new Vector3(0, 0, 30f), "TargetA", 3), b = Dummy(gb, o + new Vector3(300f, 0, 300f), "TargetB", 11);
            Debug.Log("[LastWar] aatest begin (platform)");
            var rows = new List<string>();
            foreach (float d in new[] { 5f, 15f, 30f, 60f })
                foreach (bool moving in new[] { false, true })
                    foreach (bool scoped in new[] { false, true })
                        foreach (bool on in new[] { false, true })
                        {
                            Res r = new Res();
                            yield return Swipe(pc, me, a, o, d, moving, scoped, on, r);
                            string line = $"d={d:0} {(moving ? "moving" : "still ")} {(scoped ? "scope" : "hip  ")} assist={(on ? "on " : "off")} on body {r.onBody * 100f:0}% first {(r.first < 0 ? "never" : (r.first * 1000f).ToString("0") + " ms")} error mean {r.mean:0.00} max {r.max:0.00} deg jitter {r.jitter:0.00}";
                            Debug.Log("[LastWar] aatest " + line); rows.Add(line);
                        }
            // two enemies crossing: A held at 15 m, B walks across in front of him at 11 m
            {
                PlayerController.AimAssistOn = true; TouchControls.ForceFire = true; pc.TestInput.Scoped = false;
                Place(a, o + new Vector3(0, 0, 15f)); Place(b, o + new Vector3(-4f, 0, 11f));
                Aim(pc, a); yield return new WaitForSeconds(0.6f);
                Fighter held = pc.Assist.Target; int switches = 0; float t = 0f;
                while (t < 2.6f)
                {
                    t += Time.deltaTime; Place(b, o + new Vector3(-4f + t * 3f, 0, 11f)); Run(b, true);
                    if (pc.Assist.Target != null && held != null && pc.Assist.Target != held) { switches++; held = pc.Assist.Target; }
                    if (held == null) held = pc.Assist.Target;
                    yield return null;
                }
                Run(b, false); TouchControls.ForceFire = false;
                Debug.Log($"[LastWar] aatest crossing: held {(held != null ? held.displayName : "nobody")}, switches {switches}");
                Place(b, o + new Vector3(300f, 0, 300f));
            }
            // behind a wall: A walks from the open behind a wall 12 m away; the assist must let go, the view not follow
            {
                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "aaWall";
                wall.transform.position = o + new Vector3(2.5f, 1.5f, 11f); wall.transform.localScale = new Vector3(4f, 3f, 0.4f);
                PlayerController.AimAssistOn = true; TouchControls.ForceFire = true;
                Place(a, o + new Vector3(-1.5f, 0, 14f)); Aim(pc, a); yield return new WaitForSeconds(0.6f);
                bool hadIt = pc.Assist.Target == a; float hiddenAt = -1f, letGo = -1f, yawHidden = 0f, t = 0f;
                Vector3 c0 = pc.TestCam.transform.position;
                while (t < 3f)
                {
                    t += Time.deltaTime; Place(a, o + new Vector3(-1.5f + t * 2f, 0, 14f)); Run(a, true);
                    bool vis = AimAssist.Visible(pc.TestCam.transform.position, a, me.transform);
                    if (!vis && hiddenAt < 0f) { hiddenAt = t; yawHidden = pc.TestYaw; }
                    if (hiddenAt >= 0f && letGo < 0f && pc.Assist.Target != a) letGo = t;
                    yield return null;
                }
                Run(a, false); TouchControls.ForceFire = false;
                float turnedWhileHidden = hiddenAt >= 0f ? Mathf.Abs(Mathf.DeltaAngle(yawHidden, pc.TestYaw)) : -1f;
                Debug.Log($"[LastWar] aatest wall: held before {hadIt}, hidden at {hiddenAt:0.00} s, let go {(letGo >= 0f ? (letGo - hiddenAt).ToString("0.00") + " s after" : "never")}, view turned {turnedWhileHidden:0.0} deg after he was hidden");
                Object.Destroy(wall);
            }
            Time.captureDeltaTime = 0f;
            Debug.Log("[LastWar] aatest done");
        }

        class Res { public float onBody, mean, max, jitter, first; }

        /// <summary>one run, as a player does it: the aim starts 8 deg left of him, the finger swipes toward him at 40 deg/s
        /// and stops 2.5 deg short (near him, not on him), then FIRE goes down (from the hip: held, firing) or the scope
        /// opens; watched for 1.2 s through the game's own crosshair ray - "on body" = the share of frames a shot would hit
        /// him, "first" = how long after the press the crosshair first lands on him</summary>
        static IEnumerator Swipe(PlayerController pc, Fighter me, Fighter a, Vector3 o, float d, bool moving, bool scoped, bool on, Res r)
        {
            PlayerController.AimAssistOn = on; pc.Assist.Release();
            pc.TestInput.Scoped = false; TouchControls.ForceFire = false;
            Vector3 home = o + new Vector3(0, 0, d);
            Place(a, home); Run(a, false); pc.TestLook = Vector2.zero;
            yield return new WaitForSeconds(0.35f);
            Vector3 c = pc.TestCam.transform.position;
            AimAssist.AnglesAt(a, c, out float ty, out float tp);
            pc.TestYaw = ty - 8f; pc.TestPitch = tp;
            float t = 0f, phase = 0f, mx = 0f, on1 = 0f, n = 0f, first = -1f, sum = 0f;
            float lastYaw = pc.TestYaw, lastRate = 0f, jit = 0f; int jn = 0; bool pressed = false;
            while (phase < 1.2f)
            {
                float dt = Time.deltaTime; t += dt;
                if (moving) { float x = Mathf.Sin(t * Mathf.PI * 2f / 2.4f) * 3.5f * 2.4f / (2f * Mathf.PI); Place(a, home + new Vector3(x, 0, 0)); Run(a, true); }
                if (!pressed)
                {
                    AimAssist.AnglesAt(a, pc.TestCam.transform.position, out float ny, out _);
                    float left = Mathf.DeltaAngle(pc.TestYaw, ny);
                    pc.TestLook = left > 2.5f ? new Vector2(40f, 0f) : Vector2.zero;
                    if (left <= 2.5f || t > 1.5f) { pressed = true; pc.TestLook = Vector2.zero; if (scoped) pc.TestInput.Scoped = true; else { TouchControls.ForcePress = true; TouchControls.ForceFire = true; } }
                }
                yield return null;
                if (!pressed) continue;
                phase += Time.deltaTime;
                bool hit = pc.CrosshairEnemy() == a;
                if (hit) { on1++; if (first < 0f) first = phase; }
                n++;
                float e = AimAssist.ErrorTo(a, pc.TestCam.transform.position, pc.TestYaw, pc.TestPitch, out _); sum += e; mx = Mathf.Max(mx, e);
                float rate = Mathf.DeltaAngle(lastYaw, pc.TestYaw) / Mathf.Max(1e-4f, Time.deltaTime); lastYaw = pc.TestYaw;
                if (phase > 0.25f) { jit += (rate - lastRate) * (rate - lastRate); jn++; }
                lastRate = rate;
            }
            pc.TestLook = Vector2.zero; TouchControls.ForceFire = false; pc.TestInput.Scoped = false; Run(a, false);
            r.onBody = n > 0 ? on1 / n : 0f; r.mean = n > 0 ? sum / n : 0f; r.max = mx; r.jitter = jn > 0 ? Mathf.Sqrt(jit / jn) : 0f; r.first = first;
            yield return new WaitForSeconds(0.3f);
        }

        /// <summary>the match's own bots: the nearest in sight within 45 m, the swipe + hold, assist on / off in turn</summary>
        static IEnumerator RealBots(GameBoot gb, PlayerController pc, Fighter me)
        {
            Debug.Log("[LastWar] aatest begin (real bots)");
            float until = Time.time + 150f; int run = 0;
            while (Time.time < until && run < 16)
            {
                Fighter best = null; float bd = 45f; Vector3 c = pc.TestCam.transform.position;
                foreach (Fighter f in gb.Fighters)
                {
                    if (f == me || !f.Alive || f.air != 0) continue;
                    float dd = Vector3.Distance(f.transform.position, me.transform.position);
                    if (dd < bd && dd > 3f && AimAssist.Visible(c, f, me.transform)) { bd = dd; best = f; }
                }
                if (best == null) { yield return new WaitForSeconds(0.5f); continue; }
                bool on = run % 2 == 1; PlayerController.AimAssistOn = on; pc.Assist.Release(); TouchControls.ForceFire = true;
                AimAssist.AnglesAt(best, c, out float ty, out float tp); pc.TestYaw = ty - 10f; pc.TestPitch = tp;
                float t = 0f, swipeFor = 12f / 30f, sum = 0f, mx = 0f, on1 = 0f, n = 0f;
                while (t < swipeFor + 1.5f && best.Alive)
                {
                    t += Time.deltaTime; pc.TestLook = t < swipeFor ? new Vector2(30f, 0f) : Vector2.zero;
                    yield return null;
                    if (t >= swipeFor)
                    {
                        float e = AimAssist.ErrorTo(best, pc.TestCam.transform.position, pc.TestYaw, pc.TestPitch, out float hw);
                        sum += e; mx = Mathf.Max(mx, e); if (e <= hw) on1++; n++;
                    }
                }
                pc.TestLook = Vector2.zero; TouchControls.ForceFire = false;
                Debug.Log($"[LastWar] aatest real {best.displayName} {bd:0} m assist={(on ? "on " : "off")} on body {(n > 0 ? on1 / n * 100f : 0f):0}% mean {(n > 0 ? sum / n : 0f):0.00} max {mx:0.00} deg");
                run++;
                yield return new WaitForSeconds(1.2f);
            }
            PlayerController.AimAssistOn = true;
            Debug.Log("[LastWar] aatest done");
        }

        static void Aim(PlayerController pc, Fighter a)
        {
            AimAssist.AnglesAt(a, pc.TestCam.transform.position, out float ty, out float tp); pc.TestYaw = ty; pc.TestPitch = tp;
        }

        static Fighter Dummy(GameBoot gb, Vector3 at, string name, int seed)
        {
            BotBrain bb = BotBrain.Spawn(at, name, seed);
            Fighter f = bb.fighter; SoldierBody body = bb.body;
            Object.Destroy(bb); // (stands where it is put)
            f.God = true; f.displayName = name;
            body.Play("idle_rifle");
            gb.Fighters.Add(f);
            return f;
        }

        static void Place(Fighter f, Vector3 at)
        {
            var cc = f.GetComponent<CharacterController>(); if (cc != null) cc.enabled = false;
            f.transform.position = at; f.transform.rotation = Quaternion.Euler(0, 180f, 0);
            if (cc != null) cc.enabled = true;
        }

        static void Run(Fighter f, bool run)
        {
            var b = f.GetComponent<SoldierBody>(); if (b == null) return;
            b.Speed = run ? 3.5f : 0f; b.Play(run ? "run" : "idle_rifle");
        }

        static void Teleport(PlayerController pc, Vector3 at)
        {
            var cc = pc.GetComponent<CharacterController>(); if (cc != null) cc.enabled = false;
            pc.transform.position = at; if (cc != null) cc.enabled = true;
        }
    }
}
