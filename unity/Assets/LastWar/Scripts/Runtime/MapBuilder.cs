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
            var root = new GameObject("Map " + map.id);
            BuildTerrain(map, root.transform);
            LoadShapes();
            var props = new GameObject("Props").transform; props.SetParent(root.transform);
            foreach (PropData p in map.props) PlaceProp(p, props);
            var boxes = new GameObject("Boxes").transform; boxes.SetParent(root.transform);
            foreach (BoxData b in map.boxes) PlaceBox(b, boxes);
            Physics.IgnoreLayerCollision(PlayerLayer, CoverLayer, true);
            return root;
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
                var detail = Resources.Load<Texture2D>(map.id == "desert" ? "Maps/detail_sand" : "Maps/detail_grass");
                if (detail != null) { detail.wrapMode = TextureWrapMode.Repeat; detail.anisoLevel = 8; terrainMat.SetTexture("_DetailAlbedoMap", detail); }
                terrainMat.SetTextureScale("_DetailAlbedoMap", Vector2.one * (map.size / 1.6f));
            }
            else terrainMat = NewLit(Color.white, tex);
            go.AddComponent<MeshRenderer>().sharedMaterial = terrainMat;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
        }

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

        static void PlaceProp(PropData p, Transform parent)
        {
            var root = new GameObject(p.p);
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(Coords.ToUnity(p.x, p.y, p.z), Coords.Yaw(p.rot));
            root.transform.localScale = Vector3.one * p.s;
            bool cover = Cover.Contains(p.p);
            if (cover) root.layer = CoverLayer;
            GameObject model = Model(p.p);
            if (model != null)
            {
                GameObject inst = UnityEngine.Object.Instantiate(model, root.transform, false);
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
