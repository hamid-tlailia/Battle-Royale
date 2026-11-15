using System.Collections.Generic;
using UnityEngine;

namespace LastWar
{
    /// <summary>
    /// Ice wall (web frost wall, made quicker): one tap raises a half-round wall of ice in front of you in a blink,
    /// each block standing on the ground where it lands. Stops bullets until it has taken 260 damage (~13 rifle
    /// bullets), melts after 25 s.
    /// </summary>
    public class IceWall : MonoBehaviour
    {
        public const float Height = 2.3f, Thick = 0.45f, Radius = 2.3f, Arc = 170f, Hp = 260f, Life = 25f, Rise = 0.14f;
        const int Blocks = 9;
        float hp = Hp, until, born;
        readonly List<(Transform t, float w, float baseY)> parts = new List<(Transform, float, float)>();
        static Material ice;

        static Material Mat(float a)
        {
            Shader sh = Shader.Find("UI/Default");
            return new Material(sh) { color = new Color(.68f, .92f, 1f, a), renderQueue = 3000 };
        }

        /// <summary>raise the wall around the front of `who` (open behind them); false when there is no room at all</summary>
        public static bool Place(Transform who)
        {
            int mask = ~(1 << MapBuilder.PlayerLayer);
            Vector3 fwd = who.forward; fwd.y = 0; fwd.Normalize();
            float baseYaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg, step = Arc / (Blocks - 1);
            float w = 2f * Radius * Mathf.Sin(step * 0.5f * Mathf.Deg2Rad) + 0.12f; // blocks overlap a little: no gaps
            var go = new GameObject("IceWall"); go.transform.position = who.position;
            var wall = go.AddComponent<IceWall>();
            if (ice == null) ice = Mat(0.72f);
            for (int i = 0; i < Blocks; i++)
            {
                float a = baseYaw - Arc / 2f + i * step;
                Vector3 dir = Quaternion.Euler(0, a, 0) * Vector3.forward;
                Vector3 p = who.position + dir * Radius + Vector3.up * 2f;
                if (!Physics.Raycast(p, Vector3.down, out RaycastHit h, 6f, mask, QueryTriggerInteraction.Ignore)) continue;
                Vector3 c = h.point + Vector3.up * (Height / 2f - 0.15f);
                Quaternion rot = Quaternion.LookRotation(dir);
                // a block that would stand inside a house wall / rock is left out (the rest still stands)
                if (Physics.CheckBox(c + Vector3.up * 0.15f, new Vector3(w / 2f - 0.15f, Height / 2f - 0.3f, Thick / 2f - 0.08f), rot, mask, QueryTriggerInteraction.Ignore)) continue;
                var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
                b.name = "ice"; b.transform.SetParent(go.transform, true);
                b.transform.SetPositionAndRotation(c, rot);
                b.transform.localScale = new Vector3(w, 0.05f, Thick);
                b.GetComponent<Renderer>().sharedMaterial = ice;
                wall.parts.Add((b.transform, w, h.point.y));
            }
            if (wall.parts.Count == 0) { Destroy(go); return false; }
            wall.born = Time.time; wall.until = Time.time + Life;
            Sfx.Play("reload", who.position + fwd * Radius, 0.6f, 1.6f);
            return true;
        }

        public void Damage(float d)
        {
            hp -= d;
            if (hp <= 0) Destroy(gameObject);
        }

        void Update()
        {
            if (Time.time > until) { Destroy(gameObject); return; }
            // shoots up out of the ground, and the last seconds it melts down
            float h = Height * Mathf.Min(Mathf.Clamp01((Time.time - born) / Rise), Mathf.Max(0.05f, (until - Time.time) / 2f));
            foreach (var (t, w, y) in parts)
            {
                if (t == null) continue;
                t.localScale = new Vector3(w, h, Thick);
                Vector3 p = t.position; p.y = y - 0.15f + h / 2f; t.position = p;
            }
        }
    }
}
