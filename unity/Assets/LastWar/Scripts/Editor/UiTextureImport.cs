using UnityEditor;
using UnityEngine;

namespace LastWar.EditorTools
{
    /// <summary>HUD pictures (Resources/UI) import as crisp, uncompressed sprites.</summary>
    public class UiTextureImport : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.Replace('\\', '/').Contains("/Resources/UI/")) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.isReadable = true;
        }

        /// <summary>see-through pixels re-weighted for linear-space blending, so the buttons look as dark as in the web</summary>
        void OnPostprocessTexture(Texture2D tex)
        {
            if (!assetPath.Replace('\\', '/').Contains("/Resources/UI/")) return;
            Color[] px = tex.GetPixels();
            bool any = false;
            for (int i = 0; i < px.Length; i++)
            {
                float a = px[i].a;
                if (a <= 0.001f || a >= 0.999f) continue;
                px[i].a = LastWar.GammaAlpha.Fix(px[i]); any = true;
            }
            if (any) { tex.SetPixels(px); tex.Apply(false); }
        }
    }
}
