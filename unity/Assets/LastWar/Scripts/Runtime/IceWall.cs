using UnityEngine;

namespace LastWar
{
    /// <summary>
    /// Ice wall (web frost wall): 3.2 x 2.3 x 0.5 m slab of ice in front of you, stops bullets until it has taken
    /// 260 damage (~13 rifle bullets), melts after 25 s.
    /// </summary>
    public class IceWall : MonoBehaviour
    {
        public const float Width = 3.2f, Height = 2.3f, Thick = 0.5f, Distance = 2.4f, Hp = 260f, Life = 25f;
        float hp = Hp, until;
        Renderer look;
        static Material ice, ghostMat;

        static Material Mat(float a)
        {
            Shader sh = Shader.Find("UI/Default");
            var m = new Material(sh) { color = new Color(.68f, .92f, 1f, a), renderQueue = 3000 };
            return m;
        }

        /// <summary>where a wall would stand for this fighter (on the ground, facing them); false if there is no room</summary>
        public static bool Spot(Transform who, out Vector3 pos, out Quaternion rot)
        {
            int mask = ~(1 << MapBuilder.PlayerLayer);
            Vector3 fwd = who.forward; fwd.y = 0; fwd.Normalize();
            rot = Quaternion.LookRotation(fwd);
            foreach (float dist in new[] { Distance, Distance + 0.8f, Distance - 0.6f })
            {
                Vector3 p = who.position + fwd * dist + Vector3.up * 2f;
                if (!Physics.Raycast(p, Vector3.down, out RaycastHit h, 6f, mask, QueryTriggerInteraction.Ignore)) continue;
                pos = h.point + Vector3.up * (Height / 2f - 0.15f);
                if (Physics.CheckBox(pos + Vector3.up * 0.1f, new Vector3(Width / 2f - 0.1f, Height / 2f - 0.2f, Thick / 2f - 0.05f), rot, mask, QueryTriggerInteraction.Ignore)) continue;
                return true;
            }
            pos = who.position; return false;
        }

        public static IceWall Place(Vector3 pos, Quaternion rot)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "IceWall";
            go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = new Vector3(Width, Height, Thick);
            if (ice == null) ice = Mat(0.72f);
            var w = go.AddComponent<IceWall>();
            w.look = go.GetComponent<Renderer>(); w.look.sharedMaterial = ice;
            w.until = Time.time + Life;
            Sfx.Play("reload", pos, 0.6f, 1.6f);
            return w;
        }

        /// <summary>see-through preview while aiming the wall</summary>
        public static Transform Ghost()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "IceGhost"; Destroy(go.GetComponent<Collider>());
            go.transform.localScale = new Vector3(Width, Height, Thick);
            if (ghostMat == null) ghostMat = Mat(0.3f);
            go.GetComponent<Renderer>().sharedMaterial = ghostMat;
            return go.transform;
        }

        public void Damage(float d)
        {
            hp -= d;
            if (hp <= 0) Destroy(gameObject);
        }

        void Update()
        {
            if (Time.time > until) Destroy(gameObject);
            // the last seconds it thins out
            float left = until - Time.time;
            if (left < 2f) transform.localScale = new Vector3(Width, Height * Mathf.Max(0.05f, left / 2f), Thick);
        }
    }
}
