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
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Game").AddComponent<GameBoot>();
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Debug.Log("[LastWar] Scene built: " + ScenePath);
        }

        static void CommonPlayerSettings()
        {
            PlayerSettings.companyName = "HamidTlailia";
            PlayerSettings.productName = "Last War";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            // our own logo only: no "Made with Unity" screen (optional since Unity 6 Personal)
            PlayerSettings.SplashScreen.show = false;
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
            if (Application.isBatchMode) EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
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
            urp.shadowDistance = 90f; urp.shadowCascadeCount = 2; urp.msaaSampleCount = 2; urp.renderScale = 1f;
            AssetDatabase.CreateAsset(urp, dir + "/LastWar_URP.asset");
            GraphicsSettings.defaultRenderPipeline = urp;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++) { QualitySettings.SetQualityLevel(i, false); QualitySettings.renderPipeline = urp; }
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
