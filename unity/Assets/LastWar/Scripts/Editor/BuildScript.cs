using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace LastWar.EditorTools
{
    /// <summary>
    /// CI / menu entry points (see .github/workflows/unity-build.yml):
    ///   LastWar.EditorTools.BuildScript.BuildAndroid  -> build/Android/LastWar.apk
    ///   LastWar.EditorTools.BuildScript.BuildWebGL    -> build/WebGL/ (test build, opened in a browser)
    /// Everything (render pipeline, materials, the scene) is created here, so the repository holds only
    /// scripts, models and data.
    /// </summary>
    public static class BuildScript
    {
        const string Root = "Assets/LastWar";
        const string ScenePath = Root + "/Scenes/Main.unity";
        const string Keystore = "Signing/lastwar-unity-dev.keystore";

        [MenuItem("Last War/Build Scene", priority = 1)]
        public static void BuildScene()
        {
            EnsureUrp();
            EnsureMaterials();
            AssetDatabase.ImportAsset(Root + "/Resources/Models", ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
            ModelTextureCheck();
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Game").AddComponent<GameBoot>();
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Debug.Log("[LastWar] Scene built: " + ScenePath);
        }

        /// <summary>after the models are re-imported: how many of their textures are the separate texture assets
        /// (Assets/LastWar/Textures, compressed per platform) and how many are still uncompressed copies inside a model</summary>
        static void ModelTextureCheck()
        {
            int models = 0, external = 0, embedded = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:GameObject", new[] { Root + "/Resources/Models" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".glb")) continue;
                models++;
                foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path)) if (o is Texture2D) embedded++;
                foreach (string dep in AssetDatabase.GetDependencies(path, false)) if (dep.Contains("/LastWar/Textures/")) external++;
            }
            Debug.Log($"[LastWar] model textures: {models} models, {external} separate texture assets, {embedded} still embedded (uncompressed)");
        }

        static void CommonPlayerSettings()
        {
            PlayerSettings.companyName = "HamidTlailia";
            PlayerSettings.productName = "Last War";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            // our own logo only: no "Made with Unity" screen (optional since Unity 6 Personal)
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.enableFrameTimingStats = true; // (the benchmark reads CPU / GPU frame times where the device reports them)
            PlayerSettings.SplashScreen.showUnityLogo = false;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Art/AppIcon.png");
            if (icon != null) PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
        }

        [MenuItem("Last War/Build Android APK", priority = 20)]
        public static void BuildAndroid()
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            EditorUserBuildSettings.buildAppBundle = false;
            CommonPlayerSettings();
            int versionCode = (int)((System.DateTime.UtcNow - new System.DateTime(2026, 1, 1)).TotalMinutes);
            PlayerSettings.Android.bundleVersionCode = versionCode;
            PlayerSettings.bundleVersion = "0.1." + versionCode;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.hamidtlailia.lastwar");
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            if (File.Exists(Keystore))
            {
                PlayerSettings.Android.useCustomKeystore = true;
                PlayerSettings.Android.keystoreName = Path.GetFullPath(Keystore);
                PlayerSettings.Android.keystorePass = "lastwardev123";
                PlayerSettings.Android.keyaliasName = "lastwardev";
                PlayerSettings.Android.keyaliasPass = "lastwardev123";
            }
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLES3 });
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC; // (textures without their own setting)
            BuildScene();
            Build(BuildTarget.Android, BuildTargetGroup.Android, "build/Android/LastWar.apk");
        }

        [MenuItem("Last War/Build WebGL test", priority = 21)]
        public static void BuildWebGL()
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL);
            CommonPlayerSettings();
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled; // runs from any static server
            PlayerSettings.WebGL.decompressionFallback = false;
            PlayerSettings.WebGL.dataCaching = false;
            BuildScene();
            Build(BuildTarget.WebGL, BuildTargetGroup.WebGL, "build/WebGL");
        }

        static void Build(BuildTarget target, BuildTargetGroup group, string path)
        {
            Directory.CreateDirectory(target == BuildTarget.WebGL ? path : Path.GetDirectoryName(path));
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath }, locationPathName = path, target = target, targetGroup = group, options = BuildOptions.None,
            });
            Debug.Log($"[LastWar] {target} build: {report.summary.result}, {report.summary.totalSize / (1024 * 1024)} MB, errors {report.summary.totalErrors}");
            SizeReport(report);
            if (Application.isBatchMode) EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        /// <summary>build-size audit in the build log: the largest assets packed into the build and the totals by type</summary>
        static void SizeReport(BuildReport report)
        {
            var bySource = new System.Collections.Generic.Dictionary<string, ulong>();
            var byType = new System.Collections.Generic.Dictionary<string, ulong>();
            foreach (PackedAssets pa in report.packedAssets)
                foreach (PackedAssetInfo c in pa.contents)
                {
                    string src = string.IsNullOrEmpty(c.sourceAssetPath) ? "(built-in / generated)" : c.sourceAssetPath;
                    bySource.TryGetValue(src, out ulong a); bySource[src] = a + c.packedSize;
                    string ty = c.type != null ? c.type.Name : "?";
                    byType.TryGetValue(ty, out ulong t); byType[ty] = t + c.packedSize;
                }
            var top = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, ulong>>(bySource);
            top.Sort((x, y) => y.Value.CompareTo(x.Value));
            var sb = new System.Text.StringBuilder("[LastWar] size by type:");
            var types = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, ulong>>(byType);
            types.Sort((x, y) => y.Value.CompareTo(x.Value));
            foreach (var kv in types) sb.Append($" {kv.Key} {kv.Value / 1048576.0:0.0}MB;");
            Debug.Log(sb.ToString());
            for (int i = 0; i < top.Count && i < 60; i++) Debug.Log($"[LastWar] size #{i + 1} {top[i].Value / 1048576.0:0.00} MB  {top[i].Key}");
        }

        /// <summary>Mobile URP asset (created once, assigned to every quality level).</summary>
        static void EnsureUrp()
        {
            if (GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset) return;
            string dir = Root + "/Settings";
            Directory.CreateDirectory(dir);
            var rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(rendererData, dir + "/LastWar_Renderer.asset");
            var post = AssetDatabase.LoadAssetAtPath<ScriptableObject>("Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
            var so = new SerializedObject(rendererData);
            SerializedProperty pp = so.FindProperty("postProcessData") ?? so.FindProperty("m_PostProcessData");
            if (pp != null && post != null) { pp.objectReferenceValue = post; so.ApplyModifiedPropertiesWithoutUndo(); }
            UniversalRenderPipelineAsset urp = UniversalRenderPipelineAsset.Create(rendererData);
            urp.shadowDistance = 90f; urp.shadowCascadeCount = 2; urp.msaaSampleCount = 4; urp.renderScale = 1f;
            urp.mainLightShadowmapResolution = 2048;
            AssetDatabase.CreateAsset(urp, dir + "/LastWar_URP.asset");
            GraphicsSettings.defaultRenderPipeline = urp;
            int current = QualitySettings.GetQualityLevel();
            // every quality level (phones default to a low one): full-size textures, anisotropic filtering, far LODs
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false); QualitySettings.renderPipeline = urp;
                QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable; QualitySettings.globalTextureMipmapLimit = 0; QualitySettings.lodBias = 2f;
            }
            QualitySettings.SetQualityLevel(current, false);
            AssetDatabase.SaveAssets();
            Debug.Log("[LastWar] URP asset created");
        }

        /// <summary>Base lit material in Resources (keeps the URP Lit shader in every build).</summary>
        static void EnsureMaterials()
        {
            string dir = Root + "/Resources/Materials";
            Directory.CreateDirectory(dir);
            string path = dir + "/Lit.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(path) == null)
                AssetDatabase.CreateAsset(new Material(Shader.Find("Universal Render Pipeline/Lit")) { enableInstancing = true }, path);
            string cut = dir + "/LitCutout.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(cut) == null)
            {
                // leaves, fences with gaps, window frames: alpha-tested (keeps the shader variant in the build)
                var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { enableInstancing = true };
                m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", 0.5f); m.EnableKeyword("_ALPHATEST_ON");
                m.SetFloat("_Cull", 0f); m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
                AssetDatabase.CreateAsset(m, cut);
            }
            // terrain: Lit + detail map (fine grass grain over the aerial photo, like the web game's ground shader)
            string terr = dir + "/LitTerrain.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(terr) == null)
            {
                var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { enableInstancing = true };
                m.EnableKeyword("_DETAIL_MULX2");
                var detail = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Resources/Maps/detail_grass.png");
                m.SetTexture("_DetailAlbedoMap", detail); m.SetFloat("_DetailAlbedoMapScale", 1f);
                m.SetTextureScale("_DetailAlbedoMap", new Vector2(275f, 275f)); // ~1.6 m per tile on a 440 m map
                m.SetFloat("_Smoothness", 0.05f);
                AssetDatabase.CreateAsset(m, terr);
            }
            // vertex-coloured models (guns): URP Lit ignores vertex colours, the particle Lit shader multiplies them in
            string vc = dir + "/VertexLit.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(vc) == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Particles/Lit");
                if (sh != null) { var m = new Material(sh) { enableInstancing = true }; m.SetFloat("_Smoothness", 0.25f); AssetDatabase.CreateAsset(m, vc); }
            }
            // ice wall once it is hit: the same vertex-coloured particle Lit, see-through (alpha set at run time as it weakens)
            string iceFade = dir + "/IceFade.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(iceFade) == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Particles/Lit");
                if (sh != null)
                {
                    var m = new Material(sh); m.SetFloat("_Smoothness", 0.92f);
                    m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f); m.SetFloat("_ZWrite", 0f); m.SetFloat("_Cull", 0f);
                    m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.SetOverrideTag("RenderType", "Transparent");
                    m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", new Color(.06f, .16f, .28f));
                    m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                    AssetDatabase.CreateAsset(m, iceFade);
                }
            }
            // the sea: URP Lit, see-through (the sand shows under shallow water), glossy, two layers of ripples (the normal
            // map and the detail normal map, scrolled apart at run time by MapBuilder's Sea)
            string water = dir + "/Water.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(water) == null)
            {
                var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f); m.SetFloat("_ZWrite", 0f);
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_SrcBlendAlpha", 1f); m.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.SetOverrideTag("RenderType", "Transparent");
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                var ripple = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Resources/Maps/water_normal.png");
                m.SetTexture("_BumpMap", ripple); m.SetFloat("_BumpScale", 0.45f); m.EnableKeyword("_NORMALMAP");
                m.SetTexture("_DetailNormalMap", ripple); m.SetFloat("_DetailNormalMapScale", 0.35f); m.EnableKeyword("_DETAIL_MULX2");
                m.SetColor("_BaseColor", new Color(0.06f, 0.33f, 0.46f, 0.84f)); m.SetFloat("_Smoothness", 0.93f); m.SetFloat("_Metallic", 0f);
                AssetDatabase.CreateAsset(m, water);
            }
            // zone wall: see-through blue, both sides; tracer: bright additive streak
            Transparent(dir + "/ZoneWall.mat", new Color(0.25f, 0.55f, 1f, 0.28f), false);
            Transparent(dir + "/Tracer.mat", new Color(1f, 0.85f, 0.5f, 0.9f), true);
            AssetDatabase.SaveAssets();
        }

        static void Transparent(string path, Color color, bool additive)
        {
            var old = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (old != null) return;
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", additive ? 2f : 0f);
            m.SetFloat("_Cull", 0f);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
            m.SetFloat("_SrcBlendAlpha", 1f);
            m.SetFloat("_DstBlendAlpha", (float)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.SetColor("_BaseColor", color);
            AssetDatabase.CreateAsset(m, path);
        }
    }
}
