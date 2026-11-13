using System;
using UnityEngine;

namespace LastWar
{
    /// <summary>One map exactly as the web game generates it (exported by scripts/unity/export-maps.ts).</summary>
    [Serializable]
    public class MapData
    {
        public string id;
        public string name;
        public float size;
        public int cells;
        public float snowHeight;
        public float playRadius;
        public float[] heights;
        public int[] mask;
        public string albedo;
        public PropData[] props;
        public BoxData[] boxes;
        public Vec2[] spawns;
        public AreaData[] areas;

        public static MapData Load(string id)
        {
            TextAsset t = Resources.Load<TextAsset>("Maps/" + id);
            if (t == null) throw new Exception("Map not found: " + id);
            return JsonUtility.FromJson<MapData>(t.text);
        }

        /// <summary>Terrain height at web-game coordinates (bilinear on the grid).</summary>
        public float HeightAt(float x, float z)
        {
            float cell = size / cells, half = size / 2f;
            float fx = Mathf.Clamp((x + half) / cell, 0, cells - 0.0001f), fz = Mathf.Clamp((z + half) / cell, 0, cells - 0.0001f);
            int ix = (int)fx, iz = (int)fz; float u = fx - ix, v = fz - iz; int n = cells + 1;
            float h00 = heights[iz * n + ix], h10 = heights[iz * n + ix + 1], h01 = heights[(iz + 1) * n + ix], h11 = heights[(iz + 1) * n + ix + 1];
            return Mathf.Lerp(Mathf.Lerp(h00, h10, u), Mathf.Lerp(h01, h11, u), v);
        }
    }

    [Serializable] public class PropData { public string p; public float x, y, z, rot, s; }
    [Serializable] public class BoxData { public float x, y, z, hx, hy, hz, rot; public int color; }
    [Serializable] public class Vec2 { public float x, z; }
    [Serializable] public class AreaData { public string name; public float x, z; }

    /// <summary>
    /// The web game is right-handed (three.js); Unity is left-handed. glTFast imports models by mirroring X,
    /// so the whole world uses the same rule: unity = (-x, y, z), yaw = -angle.
    /// </summary>
    public static class Coords
    {
        public static Vector3 ToUnity(float x, float y, float z) => new Vector3(-x, y, z);
        public static Quaternion Yaw(float rad) => Quaternion.Euler(0f, -rad * Mathf.Rad2Deg, 0f);
    }
}
