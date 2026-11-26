using System.Linq;
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
            Look = LookOf(map);
            currentMap = map; propBounds.Clear();
            var root = new GameObject("Map " + map.id);
            MakeIsland(map);
            BuildTerrain(map, root.transform);
            BuildSkirt(map, root.transform);
            BuildSea(map, root.transform);
            BuildShore(map, root.transform);
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

        // ---- every map an island: the ground inside the play circle is left as it is; from its edge the land runs down
        // into the sea - the rim's hills and walls that ringed the maps (a crater seen from
        // the plane) go under water, a sand beach where the land meets it, open sea to the horizon.
        /// <summary>the sea's surface (world height), below the lowest ground of the play area</summary>
        public static float SeaLevel { get; private set; } = -1000f;
        static float coastR, seaFloor;
        static float[] landHeights; static MapData landFor;
        /// <summary>the island's coast: from coastR out over W metres (steeper ground: a longer slope, 40-150 m) the
        /// ground eases down from its height at coastR to the sea floor</summary>
        static float CoastW(float edge) => Mathf.Clamp((edge - seaFloor) * 1.6f, 40f, 150f);
        static float CoastY(float edge, float r) { float t = Mathf.Clamp01((r - coastR) / CoastW(edge)); t = t * t * (3f - 2f * t); return Mathf.Lerp(edge, seaFloor, t); }
        static float Bilinear(float[] h, MapData map, float x, float z)
        {
            float cell = map.size / map.cells, half = map.size / 2f;
            float fx = Mathf.Clamp((x + half) / cell, 0, map.cells - 0.0001f), fz = Mathf.Clamp((z + half) / cell, 0, map.cells - 0.0001f);
            int ix = (int)fx, iz = (int)fz; float u = fx - ix, v = fz - iz; int n = map.cells + 1;
            return Mathf.Lerp(Mathf.Lerp(h[iz * n + ix], h[iz * n + ix + 1], u), Mathf.Lerp(h[(iz + 1) * n + ix], h[(iz + 1) * n + ix + 1], u), v);
        }
        /// <summary>the ground's height before the coast was cut (web coordinates)</summary>
        static float LandAt(MapData map, float x, float z) => landHeights != null && landFor == map ? Bilinear(landHeights, map, x, z) : map.HeightAt(x, z);
        public static bool UseIsland = true;
        static void MakeIsland(MapData map)
        {
            if (!UseIsland) { SeaLevel = -1000f; return; }
            if (landFor != map) { landHeights = (float[])map.heights.Clone(); landFor = map; }
            int n = map.cells + 1; float cell = map.size / map.cells, half = map.size / 2f;
            // the coast starts just inside the play circle's edge: the ring of hills / walls that rose there (from about
            // 180 m on the 440 m maps) is cut off - from there the ground only ever runs down, to a shore the soldiers can
            // walk onto (they are kept within the play circle + 12 m)
            coastR = Mathf.Min(map.playRadius - 6f, half - 4f);
            float low = float.MaxValue;
            for (int iz = 0; iz < n; iz++) for (int ix = 0; ix < n; ix++)
            {
                float x = -half + ix * cell, z = -half + iz * cell;
                if (x * x + z * z <= coastR * coastR) low = Mathf.Min(low, landHeights[iz * n + ix]);
            }
            SeaLevel = low - 1.2f; seaFloor = SeaLevel - 7f;
            for (int iz = 0; iz < n; iz++) for (int ix = 0; ix < n; ix++)
            {
                float x = -half + ix * cell, z = -half + iz * cell, r = Mathf.Sqrt(x * x + z * z);
                if (r <= coastR) { map.heights[iz * n + ix] = landHeights[iz * n + ix]; continue; }
                float edge = Bilinear(landHeights, map, x / r * coastR, z / r * coastR);
                map.heights[iz * n + ix] = Mathf.Min(landHeights[iz * n + ix], CoastY(edge, r));
            }
            Debug.Log($"[LastWar] island {map.id}: coast from {coastR:0} m, sea level {SeaLevel:0.0}, floor {seaFloor:0.0}");
        }

        /// <summary>
        /// Beyond the map's square: the coast keeps running down to the sea floor, then the floor stays under the water out
        /// to the haze (no collider - nobody gets there). Sand where the slope meets the water, darker under it.
        /// (?island=0: the earlier rolling hills around the map)
        /// </summary>
        static void BuildSkirt(MapData map, Transform parent)
        {
            const int seg = 160, rings = 26; float half = map.size / 2f, r0 = half * 0.975f, rMax = Mathf.Max(1700f, map.size * 1.6f);
            float low = float.MaxValue; foreach (float h in map.heights) low = Mathf.Min(low, h);
            bool desert = map.id == "desert", dvalley = map.id == "valley", island = UseIsland && SeaLevel > -999f;
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
                    float x = cx * rad, z = sz * rad, y; Color c;
                    float n1 = Mathf.PerlinNoise(seed + x * 0.006f, z * 0.006f), n2 = Mathf.PerlinNoise(seed + 50 + x * 0.02f, z * 0.02f);
                    if (island)
                    {
                        // the coast: the same slope as on the map's own ground, from the land's height at the coast line
                        float edgeLand = LandAt(map, cx * coastR, sz * coastR), edgeNow = map.HeightAt(cx * r0, sz * r0);
                        // (0.4 m under the map's own ground where both lie: no flicker at the square's corners)
                        y = Mathf.Min(edgeNow - 0.3f, CoastY(edgeLand, rad) - 0.4f) - Mathf.Clamp01((rad - r0) / 300f) * n2 * 1.5f;
                        // (the beach photo carries the sand's colour: white = the photo as it is; grass tinted over it)
                        float above = y - SeaLevel;
                        Color land = Color.Lerp(grassA, grassB, n2) * 1.25f; land.a = 1f;
                        c = above > 7.5f ? land : above > 3.5f ? Color.Lerp(Color.white, land, (above - 3.5f) / 4f) : above > 0f ? Color.white : Color.Lerp(new Color(.72f, .72f, .74f), new Color(.08f, .17f, .22f), Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((-above - 1f) / 7f))); // (deeper: dark blue-green - the sea looks deep, not a grey lagoon)
                    }
                    else
                    {
                        // (the earlier land beyond the valley: the rim's hills keep rolling outward, then fade into the haze)
                        float edge = map.HeightAt(cx * r0, sz * r0);
                        float hills = low + 6f + n1 * 55f + n2 * 10f;
                        float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((rad - r0) / 140f));
                        y = Mathf.Lerp(edge - 0.8f, hills, k) - Mathf.Clamp01((rad - 900f) / 800f) * 40f;
                        c = Color.Lerp(grassA, grassB, n2);
                        float snowLine = Mathf.Max(map.snowHeight, low + 45f);
                        if (!desert && y > snowLine) c = Color.Lerp(c, snow, Mathf.Clamp01((y - snowLine) / 10f) * (1f - k * 0.3f));
                        else if (y > low + 50f) c = Color.Lerp(c, rock, 0.4f);
                    }
                    verts[r * seg + i] = Coords.ToUnity(x, y, z);
                    uvs[r * seg + i] = new Vector2(x, z) / 6f;
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
                var beach = island ? Resources.Load<Texture2D>("Maps/beach_sand") : null;
                var grain = beach != null ? beach : Resources.Load<Texture2D>("Maps/" + (desert ? "detail_sand" : "detail_grass"));
                if (grain != null) { grain.wrapMode = TextureWrapMode.Repeat; mat.SetTexture("_BaseMap", grain); mat.SetColor("_BaseColor", beach != null ? Color.white : new Color(1.6f, 1.6f, 1.6f)); }
                if (beach != null) mat.SetTextureScale("_BaseMap", Vector2.one * (6f / 30f)); // (the photo is 30 m a tile; the skirt's uv runs 6 m a unit)
            }
            else mat = NewLit(grassB);
            var mr = go.GetComponent<MeshRenderer>(); mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
        }

        /// <summary>the sea: one flat sheet of water at SeaLevel out past the haze, a little see-through near the shore
        /// (the sand shows under it), glossy so the sun glints on it; gently moving (Sea component)</summary>
        /// <summary>the sea's colour (alpha: how little of the sand shows through) and its wave size (m); ?seacol=r,g,b,a ?seawave=</summary>
        public static Color SeaColour = new Color(0.05f, 0.42f, 0.58f, 0.93f); /* (the teal of a clear sea) */ public static float SeaWave = 22f;
        static void BuildSea(MapData map, Transform parent)
        {
            if (!UseIsland || SeaLevel < -999f) return;
            float R = Mathf.Max(1700f, map.size * 1.6f) * 1.25f;
            const int seg = 96, rings = 12;
            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            verts.Add(new Vector3(0, SeaLevel, 0)); uvs.Add(new Vector2(.5f, .5f));
            for (int r = 1; r <= rings; r++)
            {
                float rad = R * (r / (float)rings) * (r / (float)rings);
                for (int i = 0; i < seg; i++) { float a = i / (float)seg * Mathf.PI * 2f; var v = new Vector3(Mathf.Cos(a) * rad, SeaLevel, Mathf.Sin(a) * rad); verts.Add(v); uvs.Add(new Vector2(v.x, v.z) / (2f * R) + new Vector2(.5f, .5f)); }
            }
            for (int i = 0; i < seg; i++) tris.AddRange(new[] { 0, 1 + (i + 1) % seg, 1 + i });
            for (int r = 1; r < rings; r++)
                for (int i = 0; i < seg; i++)
                {
                    int a0 = 1 + (r - 1) * seg + i, a1 = 1 + (r - 1) * seg + (i + 1) % seg, b0 = a0 + seg, b1 = a1 + seg;
                    tris.AddRange(new[] { a0, a1, b0, a1, b1, b0 });
                }
            var mesh = new Mesh { name = "sea", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            if (mesh.normals.Length > 0 && mesh.normals[0].y < 0) { var tr = mesh.triangles; for (int k = 0; k < tr.Length; k += 3) { int x = tr[k + 1]; tr[k + 1] = tr[k + 2]; tr[k + 2] = x; } mesh.triangles = tr; mesh.RecalculateNormals(); }
            var go = new GameObject("Sea", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            // (Materials/Water: see-through glossy water with two layers of ripples, made by the build; without it a plain
            // glossy sheet)
            var wbase = Resources.Load<Material>("Materials/Water");
            Material mat;
            if (wbase != null)
            {
                mat = new Material(wbase);
                mat.SetTextureScale("_BumpMap", Vector2.one * (2f * R / SeaWave));          // waves ~SeaWave m across
                mat.SetTextureScale("_DetailNormalMap", Vector2.one * (2f * R / (SeaWave * 0.27f))); // and wavelets on them
                // a natural sea: blue-green, the waves plain to see; no sky mirrored in it (the reflection of the pale sky made
                // grey-white sheets - it read as ice / metal), the sun's glint kept
                mat.SetColor("_BaseColor", SeaColour); mat.SetFloat("_Smoothness", 0.74f);
                mat.SetFloat("_BumpScale", 0.7f); mat.SetFloat("_DetailNormalMapScale", 0.6f);
                mat.SetFloat("_EnvironmentReflections", 0f); mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            }
            else { mat = NewLit(new Color(0.11f, 0.38f, 0.52f)); mat.SetFloat("_Smoothness", 0.88f); mat.SetFloat("_Metallic", 0f); }
            mat.renderQueue = 2990; // (before every other see-through thing: the area names' world canvases sank behind the sea far off)
            var mr = go.GetComponent<MeshRenderer>(); mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = true;
            go.AddComponent<Sea>().mat = wbase != null ? mat : null;
        }

        /// <summary>the sea at the island's edge, as a real coast looks: light turquoise shallow water along the beach
        /// fading into the deep blue, and a band of white foam where the waves break on the sand, rolling in and out
        /// (two see-through rings that follow the real shore line, found by walking out from the land at 256 angles)</summary>
        static void BuildShore(MapData map, Transform parent)
        {
            if (!UseIsland || SeaLevel < -999f) return;
            var baseMat = Resources.Load<Material>("Materials/IceFade"); // (URP Particles/Lit: see-through, vertex colours)
            if (baseMat == null) return;
            const int seg = 256;
            float[] rs = new float[seg];
            for (int i = 0; i < seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f, cx = Mathf.Cos(a), sz = Mathf.Sin(a), r = Mathf.Max(20f, coastR - 40f);
                while (r < coastR + 260f && map.HeightAt(cx * r, sz * r) >= SeaLevel) r += 1f;
                rs[i] = r;
            }
            for (int i = 0; i < seg; i++) rs[i] = (rs[(i + seg - 1) % seg] + 2f * rs[i] + rs[(i + 1) % seg]) / 4f; // (smoothed)
            Mesh Ring(float[] offs, Color[] cols, float y, float vScale)
            {
                var v = new List<Vector3>(); var c = new List<Color>(); var uv = new List<Vector2>(); var t = new List<int>();
                int k = offs.Length;
                for (int i = 0; i <= seg; i++)
                {
                    int ii = i % seg; float a = ii / (float)seg * Mathf.PI * 2f, cx = Mathf.Cos(a), sz = Mathf.Sin(a);
                    for (int j = 0; j < k; j++)
                    {
                        float r = rs[ii] + offs[j];
                        v.Add(Coords.ToUnity(cx * r, SeaLevel + y, sz * r)); c.Add(cols[j]);
                        uv.Add(new Vector2(i / (float)seg * rs[ii] * 2f * Mathf.PI / 14f, offs[j] / vScale));
                    }
                }
                for (int i = 0; i < seg; i++)
                    for (int j = 0; j < k - 1; j++)
                    {
                        int a0 = i * k + j, a1 = a0 + 1, b0 = a0 + k, b1 = b0 + 1;
                        t.AddRange(new[] { a0, b0, a1, a1, b0, b1 });
                    }
                var mesh = new Mesh { name = "shore", indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(v); mesh.SetColors(c); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
                return mesh;
            }
            GameObject Make(string name, Mesh mesh, Texture2D tex, int queue)
            {
                var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(parent, false); go.GetComponent<MeshFilter>().sharedMesh = mesh;
                var mat = new Material(baseMat) { name = name };
                mat.DisableKeyword("_EMISSION"); if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", Color.black);
                mat.SetColor("_BaseColor", Color.white); if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.6f);
                if (tex != null) mat.SetTexture("_BaseMap", tex);
                mat.renderQueue = queue;
                var mr = go.GetComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
                return go;
            }
            // the shallows: turquoise over the sand, fading out into the deep water
            Color tq = ShallowColour;
            var shallow = Ring(new[] { -6f, 0f, 14f, 40f, 85f }, new[] { new Color(tq.r, tq.g, tq.b, 0f), new Color(tq.r, tq.g, tq.b, .75f), new Color(tq.r, tq.g, tq.b, .55f), new Color(tq.r, tq.g, tq.b, .22f), new Color(tq.r, tq.g, tq.b, 0f) }, 0.04f, 40f);
            Make("Shallows", shallow, null, 2991);
            // the foam: white, broken, on the shore line and a little out
            var foam = Ring(new[] { -3f, 0f, 3f, 8f, 14f }, new[] { new Color(1, 1, 1, 0f), new Color(1, 1, 1, .95f), new Color(1, 1, 1, .8f), new Color(1, 1, 1, .35f), new Color(1, 1, 1, 0f) }, 0.08f, 12f);
            var fgo = Make("Foam", foam, FoamTexture(), 2992);
            fgo.AddComponent<Shore>().mat = fgo.GetComponent<MeshRenderer>().sharedMaterial;
        }
        /// <summary>the shallows' colour near the beach (?shallow=r,g,b)</summary>
        public static Color ShallowColour = new Color(0.32f, 0.82f, 0.84f);

        static Texture2D foamTex;
        /// <summary>broken white foam: soft blobs and streaks, see-through between them (tiles both ways)</summary>
        static Texture2D FoamTexture()
        {
            if (foamTex != null) return foamTex;
            const int n = 256; var px = new Color32[n * n]; var rnd = new System.Random(7);
            float[] f = new float[n * n];
            for (int o = 0; o < 4; o++)
            {
                int g = 4 << o; float amp = 1f / (1 << o); var grid = new float[(g + 1) * (g + 1)];
                for (int i = 0; i < grid.Length; i++) grid[i] = (float)rnd.NextDouble();
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float gx = x / (float)n * g, gy = y / (float)n * g; int x0 = (int)gx, y0 = (int)gy; float tx = gx - x0, ty = gy - y0;
                        tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty);
                        int x1 = (x0 + 1) % g, y1 = (y0 + 1) % g; x0 %= g; y0 %= g;
                        float v = Mathf.Lerp(Mathf.Lerp(grid[y0 * (g + 1) + x0], grid[y0 * (g + 1) + x1], tx), Mathf.Lerp(grid[y1 * (g + 1) + x0], grid[y1 * (g + 1) + x1], tx), ty);
                        f[y * n + x] += v * amp;
                    }
            }
            for (int i = 0; i < f.Length; i++)
            {
                float v = Mathf.Clamp01((f[i] / 1.875f - 0.42f) * 3.2f);
                px[i] = new Color32(255, 255, 255, (byte)(v * 255));
            }
            foamTex = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, name = "foam" };
            foamTex.SetPixels32(px); foamTex.Apply(true);
            return foamTex;
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
            tex = Albedo(map);
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
                var detail = Resources.Load<Texture2D>(map.id == "desert" || map.id == "valley" ? "Maps/detail_sand" : Look == "snow" ? "Maps/detail_snow" : Look == "autumn" ? "Maps/detail_grass_autumn" : "Maps/detail_grass");
                if (detail != null) { detail.wrapMode = TextureWrapMode.Repeat; detail.anisoLevel = 8; terrainMat.SetTexture("_DetailAlbedoMap", detail); }
                terrainMat.SetTextureScale("_DetailAlbedoMap", Vector2.one * (map.size / 1.6f));
            }
            else terrainMat = NewLit(Color.white, tex);
            go.AddComponent<MeshRenderer>().sharedMaterial = terrainMat;
            var groundCol = go.AddComponent<MeshCollider>(); groundCol.sharedMesh = mesh; Ground = groundCol;
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
            // the ground as drawn: the terrain's own triangles (its collider), not the height grid's smooth blend - on a
            // steep bank by a house the two parted by up to half a metre and a rock was left hanging over the dip
            for (int i = 0; i < 5; i++)
                for (int k = 0; k < 5; k++)
                {
                    Vector3 lp = new Vector3(Mathf.Lerp(lb.min.x, lb.max.x, 0.05f + 0.225f * i), lb.min.y, Mathf.Lerp(lb.min.z, lb.max.z, 0.05f + 0.225f * k));
                    Vector3 w = root.TransformPoint(lp);
                    low = Mathf.Min(low, GroundAt(w));
                }
            float bottom = root.TransformPoint(new Vector3(lb.center.x, lb.min.y, lb.center.z)).y;
            float drop = bottom - (low - embed);
            if (drop > 0f) root.position -= Vector3.up * drop;
        }

        /// <summary>the terrain's surface under a world point (its collider; the height grid where it misses)</summary>
        static float GroundAt(Vector3 w)
        {
            if (Ground != null && Ground.Raycast(new Ray(new Vector3(w.x, 2000f, w.z), Vector3.down), out RaycastHit h, 4000f)) return h.point.y;
            return currentMap.HeightAt(-w.x, w.z); // (map heights are in the web game's coordinates)
        }

        static void PlaceProp(PropData p, Transform parent)
        {
            float py = p.y;
            // past the coast the ground was lowered: a prop there goes down with it, or is left out under the sea
            if (UseIsland && landFor == currentMap && SeaLevel > -999f && p.x * p.x + p.z * p.z > coastR * coastR)
            {
                float now = currentMap.HeightAt(p.x, p.z), dh = now - LandAt(currentMap, p.x, p.z);
                if (dh < -0.05f) { if (now < SeaLevel + 0.4f) return; py += dh; }
            }
            var root = new GameObject(p.p);
            root.transform.SetParent(parent, false);
            if (p.p.StartsWith("tpine") || p.p.StartsWith("toak") || p.p == "stump")
            {
                // trees: bedded below the lowest ground the roots reach (slopes left root flares hanging in the air)
                float r = 2.2f * p.s, low = GroundAt(Coords.ToUnity(p.x, 0, p.z));
                for (int k = 0; k < 12; k++) { float a = k / 12f * Mathf.PI * 2f; low = Mathf.Min(low, GroundAt(Coords.ToUnity(p.x + Mathf.Cos(a) * r, 0, p.z + Mathf.Sin(a) * r))); }
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
                    for (int k = 0; k < mats.Length; k++) mats[k] = LookMat(LitFrom(mats[k]), p.p);
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
        /// <summary>each green map its own season (they looked alike): Highlands under snow, Oakwood in autumn, Valley
        /// green - only in a match (the lobby stands on Highlands as it was). The ground and the trees / bushes take the
        /// look's own textures (Resources/Looks/&lt;look&gt;/; scripts/unity/map-looks.py); ?look=none|snow|autumn to try</summary>
        public static string Look = "";
        public static string LookOf(MapData map)
        {
            string u = Application.absoluteURL ?? ""; int li = u.IndexOf("look=");
            if (li >= 0) { string v = new string(u.Substring(li + 5).TakeWhile(char.IsLetter).ToArray()); return v == "none" ? "" : v; }
            if (!GameBoot.InMatch) return "";
            return map.id == "green" ? "snow" : map.id == "forest" ? "autumn" : "";
        }
        static readonly Dictionary<(Material, string), Material> lookMats = new Dictionary<(Material, string), Material>();
        /// <summary>a prop's material in the map's look: its colour texture swapped for Looks/&lt;look&gt;/&lt;model&gt;_&lt;texture&gt;</summary>
        static Material LookMat(Material m, string model)
        {
            if (string.IsNullOrEmpty(Look) || m == null || !m.HasProperty("_BaseMap")) return m;
            Texture t = m.GetTexture("_BaseMap"); if (t == null) return m;
            string key = model + "_" + t.name;
            if (lookMats.TryGetValue((m, Look + key), out Material lm)) return lm;
            var v = Resources.Load<Texture2D>($"Looks/{Look}/{key}");
            lm = m;
            if (v != null) { lm = new Material(m) { name = m.name + " " + Look }; lm.SetTexture("_BaseMap", v); if (lm.HasProperty("_MainTex")) lm.SetTexture("_MainTex", v); }
            lookMats[(m, Look + key)] = lm;
            return lm;
        }
        /// <summary>the map's ground texture in its look (Maps/&lt;albedo&gt;_&lt;look&gt;), else its own</summary>
        public static Texture2D Albedo(MapData map)
        {
            if (string.IsNullOrEmpty(map.albedo)) return null;
            string look = LookOf(map);
            Texture2D t = !string.IsNullOrEmpty(look) ? Resources.Load<Texture2D>($"Maps/{map.albedo}_{look}") : null;
            return t != null ? t : Resources.Load<Texture2D>("Maps/" + map.albedo);
        }

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

    /// <summary>the sea sheet rises and falls a few centimetres, slowly (a still sheet looked like glass)</summary>
    /// <summary>the foam rolling up the beach and back, breaking and re-forming</summary>
    public class Shore : MonoBehaviour
    {
        public Material mat;
        void Update()
        {
            if (mat == null) return;
            float t = Time.time;
            mat.SetTextureOffset("_BaseMap", new Vector2(t * 0.012f, Mathf.Sin(t * 0.55f) * 0.22f));
            var c = mat.GetColor("_BaseColor"); c.a = 0.75f + 0.25f * Mathf.Sin(t * 0.55f + 1.2f); mat.SetColor("_BaseColor", c);
        }
    }

    public class Sea : MonoBehaviour
    {
        public Material mat;
        float y0; Mesh mesh; Vector3[] rest, now; float nextWave;
        void Start()
        {
            y0 = transform.localPosition.y;
            var mf = GetComponent<MeshFilter>(); mesh = mf != null ? mf.mesh : null;
            if (mesh != null) { rest = mesh.vertices; now = (Vector3[])rest.Clone(); mesh.MarkDynamic(); }
        }
        void Update()
        {
            float t = Time.time;
            var p = transform.localPosition; p.y = y0 + Mathf.Sin(t * 0.7f) * 0.05f; transform.localPosition = p;
            // the two layers of ripples drift different ways (the sea moves; not one sheet sliding) - a little faster than
            // before: wind on the water
            if (mat != null) { mat.SetTextureOffset("_BumpMap", new Vector2(t * 0.011f, t * 0.007f)); mat.SetTextureOffset("_DetailNormalMap", new Vector2(-t * 0.024f, t * 0.017f)); } // (waves rolling in, the wavelets across them)
            // a slow swell running over it (three long waves, ~0.35 m), ten times a second (the mesh is a few hundred points)
            if (mesh == null || Time.time < nextWave) return;
            nextWave = Time.time + 0.1f;
            for (int i = 0; i < rest.Length; i++)
            {
                Vector3 v = rest[i];
                float h = Mathf.Sin(v.x * 0.045f + t * 0.9f) * 0.16f + Mathf.Sin(v.z * 0.038f - t * 0.75f) * 0.13f + Mathf.Sin((v.x + v.z) * 0.021f + t * 0.5f) * 0.09f;
                now[i] = new Vector3(v.x, v.y + h, v.z);
            }
            mesh.vertices = now; mesh.RecalculateNormals();
        }
    }
}
