using System.Collections.Generic;
using UnityEngine;

namespace LastWar
{
    /// <summary>Short-lived bullet streaks and a small impact puff for every shot (pooled line renderers).</summary>
    public class Tracers : MonoBehaviour
    {
        class Streak { public LineRenderer lr; public float t; public Vector3 a, b; }
        readonly List<Streak> pool = new List<Streak>();
        Material mat;

        public static Tracers Create()
        {
            var t = new GameObject("Tracers").AddComponent<Tracers>();
            t.mat = Resources.Load<Material>("Materials/Tracer");
            Fighter.OnShot += t.Shot;
            return t;
        }

        void Shot(Fighter shooter, Vector3 from, Vector3 to)
        {
            Vector3 dir = (to - from); float len = dir.magnitude; if (len < 1.5f) return;
            dir /= len;
            // start at the gun, not the eye
            SoldierBody body = shooter != null ? shooter.GetComponent<SoldierBody>() : null;
            Vector3 start = body != null && body.Muzzle.HasValue ? body.Muzzle.Value : from + dir * 0.8f;
            Streak s = null;
            foreach (Streak p in pool) if (p.t <= 0) { s = p; break; }
            if (s == null)
            {
                var go = new GameObject("tracer"); go.transform.SetParent(transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.sharedMaterial = mat; lr.positionCount = 2; lr.useWorldSpace = true;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; lr.receiveShadows = false;
                lr.startWidth = 0.035f; lr.endWidth = 0.02f;
                lr.startColor = new Color(1f, 0.9f, 0.55f, 0.9f); lr.endColor = new Color(1f, 0.75f, 0.35f, 0.5f);
                s = new Streak { lr = lr }; pool.Add(s);
            }
            s.a = start; s.b = to; s.t = 0.09f; s.lr.enabled = true;
            s.lr.SetPosition(0, start); s.lr.SetPosition(1, to);
        }

        void Update()
        {
            foreach (Streak s in pool)
            {
                if (s.t <= 0) continue;
                s.t -= Time.deltaTime;
                if (s.t <= 0) { s.lr.enabled = false; continue; }
                // the streak travels: tail catches up with the head
                float k = 1f - s.t / 0.09f;
                s.lr.SetPosition(0, Vector3.Lerp(s.a, s.b, k * 0.85f));
            }
        }
    }
}
