using UnityEditor;

namespace LastWar.EditorTools
{
    /// <summary>Map pictures (Resources/Maps): aerial photos and detail grain tiles (Android: ASTC 4x4, see TextureCompression).</summary>
    public class MapTextureImport : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            string p = assetPath.Replace('\\', '/');
            if (!p.Contains("/Resources/Maps/")) return;
            var ti = (TextureImporter)assetImporter;
            ti.mipmapEnabled = true;
            ti.anisoLevel = 8;
            ti.maxTextureSize = 2048;
            ti.isReadable = false;
            if (p.Contains("/detail_")) { ti.wrapMode = UnityEngine.TextureWrapMode.Repeat; ti.sRGBTexture = false; }
            else ti.wrapMode = UnityEngine.TextureWrapMode.Clamp;
                        TextureCompression.Apply(ti, TextureCompression.Kind.Terrain); // (photos and grain tiles: ASTC 4x4)
        }
    }
}
