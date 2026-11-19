using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LastWar
{
    [Serializable] public class ShapeData { public string name; public string type; public float[] v; public int[] i; public float radius; public float height; }
    [Serializable] public class ShapeList { public ShapeData[] shapes; }

    /// <summary>Builds a map (terrain, houses, trees, rocks, crates) from the exported web-game data.</summary>
    public static class MapBuilder
    {
        /// <summary>props players walk through (bullets still stop): same list as the web game</summary>
        static readonly HashSet<string> Cover = new HashSet<string>(); // nothing is walked through (same as the web game)
        /// <summary>models not placed in Unity (simple far versions / scan rock mesh handled separately)</summary>
        public const int CoverLayer = 8, PlayerLayer = 9;

        static Dictionary<string, ShapeData> shapes;
        static readonly Dictionary<string, Mesh> colliderMeshes = new Dictionary<string, Mesh>();

        public static Material NewLit(Color color, Texture tex = null, bool cutout = false)
        {
            Material baseMat = Resources.Load<Material>(cutout ? "Materials/LitCutout" : "Materials/Lit");
            var m = baseMat != null ? new Material(baseMat) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", color);
            if (tex != null) m.SetTexture("_BaseMap", tex);
            m.SetFloat("_Smoothness", 0.1f);
            return m;
        }

        public static GameObject Build(MapData map)
        {
            currentMap = map; propBounds.Clear();
            var root = new GameObject("Map " + map.id);
            BuildTerrain(map, root.transform);
            BuildSkirt(map, root.transform);
            LoadShapes();
            var props = new GameObject("Props").transform; props.SetParent(root.transform);
            foreach (PropData p in map.props) PlaceProp(p, props);
            var boxes = new GameObject("Boxes").transform; boxes.SetParent(root.transform);
            foreach (BoxData b in map.boxes) PlaceBox(b, boxes);
            Physics.IgnoreLayerCollision(PlayerLayer, CoverLayer, true);
            // soldiers don't stand on / shove each other's capsules (that shook the camera); a soft push keeps them apart
            Physics.IgnoreLayerCollision(PlayerLayer, PlayerLayer, true);
            return root;
        }

        /// <summary>
        /// Land beyond the map: the outer slopes of the rim running down and away into the haze, so that from the
        /// plane the valley does not end in a cut edge over empty sky. No collider - nobody gets there.
        /// </summary>
        static void BuildSkirt(MapData map, Transform parent)
        {
            // land beyond the valley: the rim's hills keep rolling outward (noise), grass-coloured, then fade into the haze;
            // seen from the plane the map sits in a landscape instead of a crater over an empty plain
            const int seg = 160, rings = 26; float half = map.size / 2f, r0 = half * 0.975f, rMax = Mathf.Max(1700f, map.size * 1.6f);
            float low = float.MaxValue; foreach (float h in map.heights) low = Mathf.Min(low, h);
            bool desert = map.id == "desert", dvalley = map.id == "valley";
            Color grassA = dvalley ? new Color(.5f, .43f, .33f) : desert ? new Color(.74f, .5f, .35f) : new Color(.33f, .5f, .22f), grassB = dvalley ? new Color(.42f, .36f, .28f) : desert ? new Color(.62f, .38f, .26f) : new Color(.26f, .41f, .18f);
            Color rock = dvalley ? new Color(.38f, .33f, .27f) : desert ? new Color(.55f, .32f, .22f) : new Color(.45f, .45f, .4f), snow = new Color(.9f, .92f, .95f);
            var verts = new Vector3[seg * rings]; var cols = new Color[seg * rings]; var uvs = new Vector2[seg * rings];
            float seed = map.id.GetHashCode() % 1000;
            for (int r = 0; r < rings; r++)
            {
                float t = r / (float)(rings - 1), rad = Mathf.Lerp(r0, rMax, t * t);
                for (int i = 0; i < seg; i++)
                {
                    float a = i / (float)seg * Mathf.PI * 2f, cx = Mathf.Cos(a), sz = Mathf.Sin(a);
                    float edge = map.HeightAt(cx * r0, sz * r0);
                    float x = cx * rad, z = sz * rad;
                    float n1 = Mathf.PerlinNoise(seed + x * 0.006f, z * 0.006f), n2 = Mathf.PerlinNoise(seed + 50 + x * 0.02f, z * 0.02f);
                    float hills = low + 6f + n1 * 55f + n2 * 10f;                     // rolling country
                    float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((rad - r0) / 140f)); // from the rim's own height into the hills
                    float y = Mathf.Lerp(edge - 0.8f, hills, k) - Mathf.Clamp01((rad - 900f) / 800f) * 40f; // far away it sinks under the haze
                    verts[r * seg + i] = Coords.ToUnity(x, y, z);
                    uvs[r * seg + i] = new Vector2(x, z) / 6f;
                    Color c = Color.Lerp(grassA, grassB, n2);
                    // snow only on the highest tops (and on the rim itself where the map is snowy), the rest green
                    float snowLine = Mathf.Max(map.snowHeight, low + 45f);
                    if (!desert && y > snowLine) c = Color.Lerp(c, snow, Mathf.Clamp01((y - snowLine) / 10f) * (1f - k * 0.3f));
                    else if (y > low + 50f) c = Color.Lerp(c, rock, 0.4f);
                    cols[r * seg + i] = c;
                }
            }
            var tris = new List<int>();
            for (int r = 0; r < rings - 1; r++)
                for (int i = 0; i < seg; i++)
                {
                    int a0 = r * seg + i, a1 = r * seg + (i + 1) % seg, b0 = a0 + seg, b1 = a1 + seg;
                    tris.AddRange(new[] { a0, b0, a1, a1, b0, b1 }); // facing up after the X mirror (one side: no dark doubles)
                }
            var mesh = new Mesh { name = "skirt", indexFormat = IndexFormat.UInt32 };
            mesh.vertices = verts; mesh.colors = cols; mesh.uv = uvs; mesh.triangles = tris.ToArray();
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var go = new GameObject("Skirt", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var vbase = Resources.Load<Material>("Materials/VertexLit");
            Material mat;
            if (vbase != null)
            {
                mat = new Material(vbase);
                var grain = Resources.Load<Texture2D>("Maps/" + (desert ? "detail_sand" : "detail_grass"));
                if (grain != null) { grain.wrapMode = TextureWrapMode.Repeat; mat.SetTexture("_BaseMap", grain); mat.SetColor("_BaseColor", new Color(1.6f, 1.6f, 1.6f)); }
            }
            else mat = NewLit(grassB);
            var mr = go.GetComponent<MeshRenderer>(); mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
        }

        // ------------------------------------------------------------------ terrain
        static void BuildTerrain(MapData map, Transform parent)
        {
            int n = map.cells + 1; float cell = map.size / map.cells, half = map.size / 2f;
            var verts = new Vector3[n * n]; var uvs = new Vector2[n * n];
            for (int iz = 0; iz < n; iz++)
                for (int ix = 0; ix < n; ix++)
                {
                    float x = -half + ix * cell, z = -half + iz * cell;
                    verts[iz * n + ix] = Coords.ToUnity(x, map.heights[iz * n + ix], z);
                    uvs[iz * n + ix] = new Vector2((x + half) / map.size, 1f - (z + half) / map.size);
                }
            var tris = new int[map.cells * map.cells * 6]; int t = 0;
            for (int iz = 0; iz < map.cells; iz++)
                for (int ix = 0; ix < map.cells; ix++)
                {
                    int a = iz * n + ix, b = a + 1, c = a + n, d = c + 1;
                    // clockwise from above after the X mirror (Unity front faces)
                    tris[t++] = a; tris[t++] = b; tris[t++] = c;
                    tris[t++] = b; tris[t++] = d; tris[t++] = c;
                }
            var mesh = new Mesh { name = "terrain", indexFormat = IndexFormat.UInt32 };
            mesh.vertices = verts; mesh.uv = uvs; mesh.triangles = tris;
            mesh.RecalculateNormals(); mesh.RecalculateBounds();

            Texture2D tex = null;
            if (!string.IsNullOrEmpty(map.albedo)) tex = Resources.Load<Texture2D>("Maps/" + map.albedo);
            if (tex == null) tex = HeightColours(map);
            tex.wrapMode = TextureWrapMode.Clamp; tex.anisoLevel = 8;

            var go = new GameObject("Terrain");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            Material terrainMat;
            var baseTerrain = Resources.Load<Material>("Materials/LitTerrain");
            if (baseTerrain != null)
            {
                terrainMat = new Material(baseTerrain);
                terrainMat.SetTexture("_BaseMap", tex);
                // desert: sand grain instead of grass
                var detail = Resources.Load<Texture2D>(map.id == "desert" || map.id == "valley" ? "Maps/detail_sand" : "Maps/detail_grass");
                if (detail != null) { detail.wrapMode = TextureWrapMode.Repeat; detail.anisoLevel = 8; terrainMat.SetTexture("_DetailAlbedoMap", detail); }
                terrainMat.SetTextureScale("_DetailAlbedoMap", Vector2.one * (map.size / 1.6f));
            }
            else terrainMat = NewLit(Color.white, tex);
            go.AddComponent<MeshRenderer>().sharedMaterial = terrainMat;
            Ground = go.AddComponent<MeshCollider>(); Ground.sharedMesh = mesh;
        }
        /// <summary>the map's ground (the terrain's collider): checks for room above the ground leave it out</summary>
        public static Collider Ground;

        /// <summary>generated map (no aerial photo): grass / rock / snow by height</summary>
        static Texture2D HeightColours(MapData map)
        {
            int n = map.cells + 1; var tex = new Texture2D(n, n, TextureFormat.RGB24, true);
            var px = new Color[n * n];
            for (int iz = 0; iz < n; iz++)
                for (int ix = 0; ix < n; ix++)
                {
                    float h = map.heights[iz * n + ix];
                    Color c = h > map.snowHeight ? new Color(0.9f, 0.92f, 0.94f) : h > 18 ? Color.Lerp(new Color(0.45f, 0.47f, 0.42f), new Color(0.6f, 0.6f, 0.56f), Mathf.InverseLerp(18, 40, h)) : Color.Lerp(new Color(0.24f, 0.42f, 0.18f), new Color(0.42f, 0.5f, 0.26f), Mathf.InverseLerp(-5, 18, h));
                    px[(n - 1 - iz) * n + ix] = c; // texture row 0 = bottom = +z edge (uv.y = 1 - z)
                }
            tex.SetPixels(px); tex.Apply(true);
            return tex;
        }

        // ------------------------------------------------------------------ props
        static MapData currentMap;
        static readonly Dictionary<PropData, Bounds> propBounds = new Dictionary<PropData, Bounds>();
        /// <summary>world box of a placed house (for putting loot on its floors)</summary>
        public static Bounds PropBounds(PropData p) => propBounds.TryGetValue(p, out Bounds b) ? b : new Bounds(Coords.ToUnity(p.x, p.y, p.z), new Vector3(8, 6, 8));
        static void LoadShapes()
        {
            if (shapes != null) return;
            shapes = new Dictionary<string, ShapeData>();
            var list = JsonUtility.FromJson<ShapeList>(Resources.Load<TextAsset>("Data/prefabs").text);
            foreach (ShapeData s in list.shapes) shapes[s.name] = s;
        }

        static readonly Dictionary<string, GameObject> models = new Dictionary<string, GameObject>();
        public static GameObject Model(string name)
        {
            if (!models.TryGetValue(name, out GameObject m)) { m = Resources.Load<GameObject>("Models/" + name); models[name] = m; }
            return m;
        }

        /// <summary>loads models in the background (the lobby's outfits: switching outfit is then instant)</summary>
        public static System.Collections.IEnumerator Preload(string[] names)
        {
            foreach (string n in names)
            {
                if (models.ContainsKey(n)) continue;
                var req = Resources.LoadAsync<GameObject>("Models/" + n);
                yield return req;
                if (!models.ContainsKey(n)) models[n] = req.asset as GameObject;
            }
        }

        static void BedRock(Transform root, float embed)
        {
            Bounds lb = GunFrame.LocalBounds(root);
            if (lb.size.sqrMagnitude < 1e-6f) return;
            float low = float.MaxValue;
            for (int i = 0; i < 3; i++)
                for (int k = 0; k < 3; k++)
                {
                    Vector3 lp = new Vector3(Mathf.Lerp(lb.min.x, lb.max.x, 0.1f + 0.4f * i), lb.min.y, Mathf.Lerp(lb.min.z, lb.max.z, 0.1f + 0.4f * k));
                    Vector3 w = root.TransformPoint(lp);
                    Vector3 web = new Vector3(-w.x, w.y, w.z); // (map heights are in the web game's coordinates)
                    low = Mathf.Min(low, currentMap.HeightAt(web.x, web.z));
                }
            float bottom = root.TransformPoint(new Vector3(lb.center.x, lb.min.y, lb.center.z)).y;
            float drop = bottom - (low - embed);
            if (drop > 0f) root.position -= Vector3.up * drop;
        }

        static void PlaceProp(PropData p, Transform parent)
        {
            var root = new GameObject(p.p);
            root.transform.SetParent(parent, false);
            float py = p.y;
            if (p.p.StartsWith("tpine") || p.p.StartsWith("toak") || p.p == "stump")
            {
                // trees: bedded below the lowest ground the roots reach (slopes left root flares hanging in the air)
                float r = 2.2f * p.s, low = currentMap.HeightAt(p.x, p.z);
                for (int k = 0; k < 12; k++) { float a = k / 12f * Mathf.PI * 2f; low = Mathf.Min(low, currentMap.HeightAt(p.x + Mathf.Cos(a) * r, p.z + Mathf.Sin(a) * r)); }
                py = Mathf.Min(py, low - 0.45f * p.s);
            }
            root.transform.SetPositionAndRotation(Coords.ToUnity(p.x, py, p.z), Coords.Yaw(p.rot));
            root.transform.localScale = Vector3.one * p.s;
            bool cover = Cover.Contains(p.p);
            if (cover) root.layer = CoverLayer;
            GameObject model = Model(p.p);
            if (model != null)
            {
                GameObject inst = UnityEngine.Object.Instantiate(model, root.transform, false);
                // boulders: bedded below the lowest ground under them (on slopes a rock placed at one height floated over
                // the low side - Death Valley up to a metre); only ever lowered
                if (p.p.StartsWith("rock")) BedRock(root.transform, 0.15f * p.s);
                if (p.p.StartsWith("house") || p.p == "farmhouse" || p.p.StartsWith("bld_"))
                {
                    Bounds b = new Bounds(root.transform.position, Vector3.zero);
                    foreach (Renderer r in inst.GetComponentsInChildren<Renderer>(true)) b.Encapsulate(r.bounds);
                    propBounds[p] = b;
                }
                // glTFast's own shaders draw grey / nothing in player builds: use URP Lit with the same colour + texture
                foreach (Renderer r in inst.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = r.sharedMaterials;
                    for (int k = 0; k < mats.Length; k++) mats[k] = LitFrom(mats[k]);
                    r.sharedMaterials = mats;
                }
            }
            if (!shapes.TryGetValue(p.p, out ShapeData shape)) return;
            switch (shape.type)
            {
                case "cylinder":
                    var cap = root.AddComponent<CapsuleCollider>();
                    cap.radius = shape.radius; cap.height = Mathf.Max(shape.height, shape.radius * 2); cap.center = new Vector3(0, shape.height / 2, 0);
                    break;
                case "trimesh":
                case "hull":
                    var mc = root.AddComponent<MeshCollider>();
                    mc.sharedMesh = ColliderMesh(shape);
                    mc.convex = shape.type == "hull";
                    break;
            }
        }

        static readonly Dictionary<Material, Material> litCache = new Dictionary<Material, Material>();
        /// <summary>glTF material -> URP Lit (opaque or alpha-cutout), shared per source material</summary>
        public static Material LitFrom(Material src)
        {
            if (src == null) return NewLit(Color.white);
            if (litCache.TryGetValue(src, out Material m)) return m;
            if (src.name.StartsWith("vc"))
            {
                // the guns' colours live in the vertices: a shader that reads them (else they come out white)
                var vbase = Resources.Load<Material>("Materials/VertexLit");
                if (vbase != null) { m = new Material(vbase); litCache[src] = m; return m; }
            }
            if (src.shader != null && src.shader.name.ToLower().Contains("unlit")) { litCache[src] = src; return src; } // scanned rock: keep
            Texture tex = null; Color col = Color.white;
            foreach (string prop in new[] { "baseColorTexture", "_BaseMap", "_MainTex" }) if (src.HasProperty(prop) && src.GetTexture(prop) != null) { tex = src.GetTexture(prop); break; }
            foreach (string prop in new[] { "baseColorFactor", "_BaseColor", "_Color" }) if (src.HasProperty(prop)) { col = src.GetColor(prop); break; }
            bool cutout = src.renderQueue >= 2450 || (src.HasProperty("alphaCutoff") && src.GetFloat("alphaCutoff") > 0f && src.IsKeywordEnabled("_ALPHATEST_ON"));
            m = NewLit(new Color(col.r, col.g, col.b, 1f), tex, cutout);
            litCache[src] = m;
            return m;
        }

        static Mesh ColliderMesh(ShapeData s)
        {
            if (colliderMeshes.TryGetValue(s.name, out Mesh m)) return m;
            var v = new Vector3[s.v.Length / 3];
            for (int k = 0; k < v.Length; k++) v[k] = new Vector3(-s.v[k * 3], s.v[k * 3 + 1], s.v[k * 3 + 2]);
            m = new Mesh { name = "col_" + s.name, indexFormat = IndexFormat.UInt32 };
            m.vertices = v;
            if (s.i.Length > 0)
            {
                var tri = new int[s.i.Length];
                for (int k = 0; k < tri.Length; k += 3) { tri[k] = s.i[k]; tri[k + 1] = s.i[k + 2]; tri[k + 2] = s.i[k + 1]; } // mirrored: flip winding
                m.triangles = tri;
            }
            else
            {
                // hull given as points only: Unity builds the convex hull itself from any triangles over the points
                var tri = new List<int>();
                for (int k = 2; k < v.Length; k++) { tri.Add(0); tri.Add(k - 1); tri.Add(k); }
                m.triangles = tri.ToArray();
            }
            m.RecalculateBounds();
            colliderMeshes[s.name] = m;
            return m;
        }

        static Mesh cube;
        static Mesh CubeMesh()
        {
            if (cube != null) return cube;
            cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            return cube;
        }
        static readonly Dictionary<int, Material> boxMats = new Dictionary<int, Material>();

        static void PlaceBox(BoxData b, Transform parent)
        {
            var go = new GameObject("box");
            go.AddComponent<MeshFilter>().sharedMesh = CubeMesh();
            go.AddComponent<MeshRenderer>();
            go.AddComponent<BoxCollider>(); // unit cube, scaled below
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(Coords.ToUnity(b.x, b.y, b.z), Coords.Yaw(b.rot));
            go.transform.localScale = new Vector3(b.hx * 2, b.hy * 2, b.hz * 2);
            Color c = new Color(((b.color >> 16) & 255) / 255f, ((b.color >> 8) & 255) / 255f, (b.color & 255) / 255f);
            if (!boxMats.TryGetValue(b.color, out Material mat)) { mat = NewLit(c); boxMats[b.color] = mat; }
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }
    }
}
