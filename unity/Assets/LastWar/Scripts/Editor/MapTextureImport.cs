using UnityEditor;

namespace LastWar.EditorTools
{
    /// <summary>Map pictures (Resources/Maps): aerial photos kept sharp (no lossy phone compression), detail grain tiles.</summary>
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
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.isReadable = false;
            if (p.Contains("/detail_")) { ti.wrapMode = UnityEngine.TextureWrapMode.Repeat; ti.sRGBTexture = false; }
            else ti.wrapMode = UnityEngine.TextureWrapMode.Clamp;
        }
    }
}
