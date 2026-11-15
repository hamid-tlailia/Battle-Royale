using System.Collections.Generic;
using UnityEngine;

namespace LastWar
{
    /// <summary>
    /// Ice wall (web frost wall, made quicker): the ice button shows a see-through half-round wall in front of you,
    /// FIRE raises it in a blink - also in the middle of a jump (it stands on the ground under the spot). Each block
    /// stands where it lands. Stops bullets until it has taken 260 damage (~13 rifle bullets), melts after 25 s.
    /// </summary>
    public class IceWall : MonoBehaviour
    {
        public const float Height = 1.7f, Thick = 0.45f, Radius = 2.3f, Arc = 170f, Hp = 260f, Life = 25f, Rise = 0.12f;
        const int Blocks = 9;
        public struct Block { public Vector3 pos; public Quaternion rot; public float w, ground; }
        static readonly List<Block> spot = new List<Block>();
        float hp = Hp, until, born;
        readonly List<(Transform t, float w, float baseY)> parts = new List<(Transform, float, float)>();
        static Material ice, ghostMat;

        static Material Mat(float a)
        {
            Shader sh = Shader.Find("UI/Default");
            return new Material(sh) { color = new Color(.68f, .92f, 1f, a), renderQueue = 3000 };
        }

        static float BlockW => 2f * Radius * Mathf.Sin(Arc / (Blocks - 1) * 0.5f * Mathf.Deg2Rad) + 0.12f; // blocks overlap: no gaps

        /// <summary>where the blocks would stand around the front of `who` (open behind); empty when there is no room.
        /// Works in the air: the ground is searched up to 8 m below.</summary>
        public static List<Block> Spot(Transform who)
        {
            spot.Clear();
            int mask = ~(1 << MapBuilder.PlayerLayer);
            Vector3 fwd = who.forward; fwd.y = 0; if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward; fwd.Normalize();
            float baseYaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg, step = Arc / (Blocks - 1), w = BlockW;
            for (int i = 0; i < Blocks; i++)
            {
                float a = baseYaw - Arc / 2f + i * step;
                Vector3 dir = Quaternion.Euler(0, a, 0) * Vector3.forward;
                Vector3 p = who.position + dir * Radius + Vector3.up * 1.5f;
                if (!Physics.Raycast(p, Vector3.down, out RaycastHit h, 9.5f, mask, QueryTriggerInteraction.Ignore)) continue;
                if (h.collider.GetComponentInParent<IceWall>() != null) continue;
                Vector3 c = h.point + Vector3.up * (Height / 2f - 0.15f);
                Quaternion rot = Quaternion.LookRotation(dir);
                // a block that would stand inside a house wall / rock is left out (the rest still stands)
                if (Physics.CheckBox(c + Vector3.up * 0.15f, new Vector3(w / 2f - 0.15f, Height / 2f - 0.3f, Thick / 2f - 0.08f), rot, mask, QueryTriggerInteraction.Ignore)) continue;
                spot.Add(new Block { pos = c, rot = rot, w = w, ground = h.point.y });
            }
            return spot;
        }

        public static void Place(List<Block> blocks, Vector3 soundAt)
        {
            if (blocks.Count == 0) return;
            var go = new GameObject("IceWall"); go.transform.position = blocks[blocks.Count / 2].pos;
            var wall = go.AddComponent<IceWall>();
            if (ice == null) ice = Mat(0.72f);
            foreach (Block b in blocks)
            {
                var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
                c.name = "ice"; c.transform.SetParent(go.transform, true);
                c.transform.SetPositionAndRotation(b.pos, b.rot);
                c.transform.localScale = new Vector3(b.w, 0.05f, Thick);
                c.GetComponent<Renderer>().sharedMaterial = ice;
                wall.parts.Add((c.transform, b.w, b.ground));
            }
            wall.born = Time.time; wall.until = Time.time + Life;
            Sfx.Play("reload", soundAt, 0.6f, 1.6f);
        }

        /// <summary>the see-through preview (one block per spot, hidden when unused)</summary>
        public static Transform Ghost()
        {
            var root = new GameObject("IceGhost").transform;
            if (ghostMat == null) ghostMat = Mat(0.3f);
            for (int i = 0; i < Blocks; i++)
            {
                var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(c.GetComponent<Collider>());
                c.transform.SetParent(root, false);
                c.GetComponent<Renderer>().sharedMaterial = ghostMat;
            }
            return root;
        }

        public static void ShowGhost(Transform ghost, List<Block> blocks)
        {
            for (int i = 0; i < ghost.childCount; i++)
            {
                Transform c = ghost.GetChild(i);
                bool on = i < blocks.Count;
                if (c.gameObject.activeSelf != on) c.gameObject.SetActive(on);
                if (!on) continue;
                c.SetPositionAndRotation(blocks[i].pos, blocks[i].rot);
                c.localScale = new Vector3(blocks[i].w, Height, Thick);
            }
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
