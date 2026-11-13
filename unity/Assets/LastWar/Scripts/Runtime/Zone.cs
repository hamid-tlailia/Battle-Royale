using UnityEngine;

namespace LastWar
{
    /// <summary>
    /// The safe zone: a glowing wall that appears after the opening delay, then in each phase waits, shrinks
    /// toward a new random centre and hurts harder outside (ignores armour). Positions are Unity X/Z.
    /// </summary>
    public class Zone : MonoBehaviour
    {
        public static Zone Instance { get; private set; }
        public Vector2 Centre, NextCentre;
        public float Radius, NextRadius;
        public int Phase = -1;          // -1 = not yet visible
        public bool Shrinking;
        public float PhaseEnds;          // Time.time when the current wait / shrink ends
        public float Dps;
        public bool Active => Phase >= 0;

        Tuning tune;
        Vector2 fromCentre; float fromRadius;
        Transform wall;
        float tick;

        public static Zone Create(float playRadius)
        {
            var z = new GameObject("Zone").AddComponent<Zone>();
            Instance = z;
            z.tune = Tuning.Get();
            z.Radius = z.NextRadius = Mathf.Max(z.tune.zoneStartRadius, playRadius + 10f);
            z.Centre = z.NextCentre = Vector2.zero;
            z.PhaseEnds = Time.time + z.tune.zoneDelay;
            z.BuildWall();
            return z;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        public bool Outside(Vector3 p, float margin = 0f) => Vector2.Distance(new Vector2(p.x, p.z), Centre) > Radius - margin;
        public float SecondsLeft => Mathf.Max(0, PhaseEnds - Time.time);

        void Update()
        {
            if (Time.time >= PhaseEnds) Advance();
            if (Shrinking)
            {
                ZonePhase ph = tune.zonePhases[Phase];
                float t = 1f - SecondsLeft / Mathf.Max(0.01f, ph.shrink);
                Centre = Vector2.Lerp(fromCentre, NextCentre, t); Radius = Mathf.Lerp(fromRadius, NextRadius, t);
            }
            if (wall != null)
            {
                wall.gameObject.SetActive(Active);
                wall.position = new Vector3(Centre.x, -40f, Centre.y);
                wall.localScale = new Vector3(Mathf.Max(0.5f, Radius), 1f, Mathf.Max(0.5f, Radius));
            }
            // damage once a second to everyone outside
            if (Active && (tick += Time.deltaTime) >= 1f)
            {
                tick = 0f;
                if (GameBoot.Instance != null)
                    foreach (Fighter f in GameBoot.Instance.Fighters)
                        if (f.Alive && Outside(f.transform.position)) f.TakeDamage(Dps, null, "zone", false, f.transform.position);
            }
        }

        void Advance()
        {
            if (Phase < 0) { StartWait(0); return; }
            ZonePhase ph = tune.zonePhases[Phase];
            if (!Shrinking) { Shrinking = true; fromCentre = Centre; fromRadius = Radius; PhaseEnds = Time.time + ph.shrink; return; }
            Shrinking = false; Centre = NextCentre; Radius = NextRadius;
            if (Phase + 1 < tune.zonePhases.Length) StartWait(Phase + 1);
            else PhaseEnds = float.MaxValue;
        }

        void StartWait(int p)
        {
            Phase = p;
            ZonePhase ph = tune.zonePhases[p];
            Dps = ph.dps;
            // next circle lies fully inside the current one
            NextRadius = Mathf.Min(ph.radius, Radius);
            Vector2 c = Centre + Random.insideUnitCircle * Mathf.Max(0, Radius - NextRadius) * 0.8f;
            NextCentre = c;
            PhaseEnds = Time.time + ph.wait;
        }

        void BuildWall()
        {
            const int Seg = 128; const float H = 160f;
            var verts = new Vector3[(Seg + 1) * 2]; var uv = new Vector2[verts.Length]; var tris = new int[Seg * 12];
            for (int k = 0; k <= Seg; k++)
            {
                float a = k * Mathf.PI * 2f / Seg; float x = Mathf.Cos(a), z = Mathf.Sin(a);
                verts[k * 2] = new Vector3(x, 0, z); verts[k * 2 + 1] = new Vector3(x, H, z);
                uv[k * 2] = new Vector2(k / 4f, 0); uv[k * 2 + 1] = new Vector2(k / 4f, 1);
            }
            for (int k = 0, t = 0; k < Seg; k++)
            {
                int a = k * 2, b = a + 1, c = a + 2, d = a + 3;
                // both faces so it is visible from inside and outside
                tris[t++] = a; tris[t++] = b; tris[t++] = c; tris[t++] = c; tris[t++] = b; tris[t++] = d;
                tris[t++] = a; tris[t++] = c; tris[t++] = b; tris[t++] = c; tris[t++] = d; tris[t++] = b;
            }
            var mesh = new Mesh { name = "ZoneWall", vertices = verts, uv = uv, triangles = tris };
            mesh.bounds = new Bounds(Vector3.up * H / 2, new Vector3(4000, H, 4000));
            var go = new GameObject("ZoneWall");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Resources.Load<Material>("Materials/ZoneWall");
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            wall = go.transform; wall.SetParent(transform, false);
            go.SetActive(false);
        }
    }
}
