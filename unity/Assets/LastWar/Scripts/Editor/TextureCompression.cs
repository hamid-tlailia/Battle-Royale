using UnityEditor;
using UnityEngine;

namespace LastWar.EditorTools
{
    /// <summary>
    /// Per-platform texture compression, in one place (Phase 2 size work):
    ///   Android: ASTC 4x4 for the map photos and ground tiles (large even areas such as grass lost a little colour at
    ///            6x6), the normal maps and the HUD / lobby pictures; ASTC 5x5 for colour textures (characters, buildings,
    ///            props); ASTC 6x6 for other data textures (metallic-roughness, occlusion)
    ///   WebGL:   kept uncompressed (RGBA32) as before - the WebGL build is the test build, run on desktop browsers
    ///            and a software renderer, where ASTC is not available
    ///   other platforms: uncompressed, as before
    /// Reversible: set <see cref="Enabled"/> false and every texture goes back to the earlier settings on the next import
    /// (the source pictures are never changed).
    /// </summary>
    public static class TextureCompression
    {
        public static readonly bool Enabled = true;

        public enum Kind { Colour, Data, Normal, Ui, Terrain }

        public static void Apply(TextureImporter ti, Kind kind)
        {
            ti.textureCompression = TextureImporterCompression.Uncompressed; // (default platform: as before)
            var android = ti.GetPlatformTextureSettings("Android");
            var webgl = ti.GetPlatformTextureSettings("WebGL");
            if (!Enabled)
            {
                android.overridden = false; webgl.overridden = false;
                ti.SetPlatformTextureSettings(android); ti.SetPlatformTextureSettings(webgl);
                return;
            }
            android.overridden = true;
            android.maxTextureSize = ti.maxTextureSize;
            android.format = kind == Kind.Normal || kind == Kind.Ui || kind == Kind.Terrain ? TextureImporterFormat.ASTC_4x4
                : kind == Kind.Colour ? TextureImporterFormat.ASTC_5x5 : TextureImporterFormat.ASTC_6x6;
            android.compressionQuality = 100; // (best encoder effort: slower import, same size)
            ti.SetPlatformTextureSettings(android);
            webgl.overridden = true;
            webgl.maxTextureSize = ti.maxTextureSize;
            webgl.format = TextureImporterFormat.RGBA32;
            ti.SetPlatformTextureSettings(webgl);
        }
    }

    /// <summary>
    /// Model textures moved out of the .glb files (scripts/unity/extern_textures.py) into Assets/LastWar/Textures/&lt;model&gt;/:
    /// the file name's suffix says how the model uses it - _c colour (sRGB), _n normal map, _l other linear data.
    /// glTFast uses these assets directly (with these settings) instead of uncompressed copies inside the model.
    /// </summary>
    public class ModelTextureImport : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            string p = assetPath.Replace('\\', '/');
            if (!p.Contains("/LastWar/Textures/")) return;
            var ti = (TextureImporter)assetImporter;
            string file = System.IO.Path.GetFileNameWithoutExtension(p);
            char role = file.Length > 2 && file[file.Length - 2] == '_' ? file[file.Length - 1] : 'c';
            ti.mipmapEnabled = true;
            ti.maxTextureSize = 2048;
            ti.isReadable = false;
            ti.textureShape = TextureImporterShape.Texture2D;
            if (role == 'n') { ti.textureType = TextureImporterType.NormalMap; ti.sRGBTexture = false; }
            else { ti.textureType = TextureImporterType.Default; ti.sRGBTexture = role != 'l'; }
            ti.alphaSource = TextureImporterAlphaSource.FromInput;
            // buildings and houses (seen up close, walls fill the screen): their colours at ASTC 4x4 like the ground, not 5x5
            bool building = p.Contains("/Textures/bld_") || p.Contains("/Textures/house_") || p.Contains("/Textures/farmhouse") || p.Contains("/Textures/ind_") || p.Contains("/Textures/mkt_");
            TextureCompression.Apply(ti, role == 'n' ? TextureCompression.Kind.Normal : role == 'l' ? TextureCompression.Kind.Data : building ? TextureCompression.Kind.Terrain : TextureCompression.Kind.Colour);
        }
    }
}
