#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SpinArena.EditorTools
{
    /// <summary>
    /// Configuration automatique du projet et construction de l'APK (appelé par GitHub Actions, game-ci/unity-builder).
    /// Crée : le pipeline URP, les modèles de matériaux (Resources/Materials), la scène de démarrage, les réglages Android.
    /// </summary>
    public static class Builder
    {
        const string Root = "Assets/SpinArena";
        const string SettingsDir = Root + "/Settings";
        const string MaterialsDir = Root + "/Resources/Materials";
        const string ScenePath = Root + "/Scenes/Main.unity";

        public static void BuildAndroid() => Build(BuildTarget.Android, "build/Android/SpinArena.apk", "android");
        /// <summary>Version Windows : uniquement pour le banc d'essai automatique (captures et mesures sur serveur).</summary>
        public static void BuildWindows() => Build(BuildTarget.StandaloneWindows64, "build/Windows/SpinArena.exe", "windows");

        [MenuItem("SpinArena/Configurer le projet")]
        public static void Setup()
        {
            Folder(SettingsDir); Folder(MaterialsDir); Folder(Root + "/Scenes");
            var pipe = EnsurePipeline();
            GraphicsSettings.defaultRenderPipeline = pipe;
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipe;
            }
            PlayerSettings.colorSpace = ColorSpace.Linear;
            EnsureMaterials();
            UseLegacyInput();
            EnsureScene();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        static UniversalRenderPipelineAsset EnsurePipeline()
        {
            string rdPath = SettingsDir + "/SpinRenderer.asset";
            var rd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rdPath);
            if (rd == null)
            {
                rd = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(rd, rdPath);
            }
            // Données de post-traitement (bloom…) : indispensables pour les lueurs.
            if (rd.postProcessData == null)
                rd.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>("Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
            EditorUtility.SetDirty(rd);

            string pPath = SettingsDir + "/SpinURP.asset";
            var pipe = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pPath);
            if (pipe == null)
            {
                pipe = UniversalRenderPipelineAsset.Create(rd);
                AssetDatabase.CreateAsset(pipe, pPath);
            }
            // Profil « téléphone haut de gamme en live » : HDR pour le bloom, MSAA 2×, ombres de la lumière principale.
            pipe.supportsHDR = true;
            pipe.msaaSampleCount = 2;
            pipe.renderScale = 1f;
            pipe.shadowDistance = 45f;
            pipe.shadowCascadeCount = 2;
            pipe.supportsCameraDepthTexture = false;
            pipe.supportsCameraOpaqueTexture = false;
            EditorUtility.SetDirty(pipe);
            return pipe;
        }

        static void EnsureMaterials()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            var particles = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (lit == null || unlit == null || particles == null) throw new Exception("Shaders URP introuvables : le paquet URP est-il installé ?");

            var mLit = Mat("SA_Lit", lit);
            mLit.SetFloat("_Metallic", 0.8f);
            mLit.SetFloat("_Smoothness", 0.7f);
            mLit.SetColor("_EmissionColor", Color.black);
            mLit.EnableKeyword("_EMISSION");
            mLit.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;

            var mLitN = Mat("SA_LitNormal", lit);
            mLitN.SetFloat("_Metallic", 0.9f);
            mLitN.SetFloat("_Smoothness", 0.8f);
            mLitN.SetColor("_EmissionColor", Color.black);
            mLitN.SetTexture("_BumpMap", Texture2D.normalTexture);
            mLitN.EnableKeyword("_EMISSION");
            mLitN.EnableKeyword("_NORMALMAP");
            mLitN.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;

            Mat("SA_Unlit", unlit);

            var add = Mat("SA_Additive", particles);
            Transparent(add, additive: true);
            add.SetTexture("_BaseMap", Texture2D.whiteTexture);

            var tr = Mat("SA_Transparent", unlit);
            Transparent(tr, additive: false);

            foreach (var n in new[] { "SA_Lit", "SA_LitNormal", "SA_Unlit", "SA_Additive", "SA_Transparent" })
                EditorUtility.SetDirty(AssetDatabase.LoadAssetAtPath<Material>(MaterialsDir + "/" + n + ".mat"));
        }

        static Material Mat(string name, Shader shader)
        {
            string path = MaterialsDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            else m.shader = shader;
            return m;
        }

        static void Transparent(Material m, bool additive)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", additive ? 2f : 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_SrcBlendAlpha")) m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            if (m.HasProperty("_DstBlendAlpha")) m.SetFloat("_DstBlendAlpha", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = (int)RenderQueue.Transparent;
        }

        static void AlwaysInclude(string shaderName)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null) return;
            var gs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset").FirstOrDefault();
            if (gs == null) return;
            var so = new SerializedObject(gs);
            var arr = so.FindProperty("m_AlwaysIncludedShaders");
            if (arr == null) return;
            for (int i = 0; i < arr.arraySize; i++) if (arr.GetArrayElementAtIndex(i).objectReferenceValue == shader) return;
            arr.InsertArrayElementAtIndex(arr.arraySize);
            arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = shader;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Le jeu utilise l'ancien système d'entrée (interface tactile UGUI) : activeInputHandler = 0 ou 2.</summary>
        static void UseLegacyInput()
        {
            var ps = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset").FirstOrDefault();
            if (ps == null) return;
            var so = new SerializedObject(ps);
            var p = so.FindProperty("activeInputHandler");
            if (p != null && p.intValue == 1) { p.intValue = 2; so.ApplyModifiedPropertiesWithoutUndo(); }
        }

        static void EnsureScene()
        {
            // Scène vide : le GameManager se crée tout seul au chargement et construit le reste par code.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("SpinArena Boot");
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        static void Folder(string path)
        {
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            AssetDatabase.Refresh();
        }

        static void Build(BuildTarget target, string output, string tag)
        {
            var rep = new StringBuilder("# Construction " + tag + "\n\n");
            int code = 1;
            try
            {
                Directory.CreateDirectory("ci-out/" + tag);
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                Setup();

                PlayerSettings.companyName = "Sebrakon";
                PlayerSettings.productName = "Spin Arena";
                string run = Environment.GetEnvironmentVariable("GITHUB_RUN_NUMBER");
                if (string.IsNullOrEmpty(run) && File.Exists("ci-build-number.txt")) run = File.ReadAllText("ci-build-number.txt").Trim();
                int build = int.TryParse(run, out int n) ? n : 1;
                PlayerSettings.bundleVersion = "0.1." + build;
#if UNITY_2022_1_OR_NEWER
                // Les photos de profil passent par le pont local (http://127.0.0.1) qui les convertit en PNG.
                PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
#endif

                if (target == BuildTarget.Android) ConfigureAndroid(build);
                else
                {
                    PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
                    PlayerSettings.defaultScreenWidth = 1920;
                    PlayerSettings.defaultScreenHeight = 1080;
                    PlayerSettings.runInBackground = true;
                    PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
                }

                var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
                rep.AppendLine("- Scènes : " + string.Join(", ", scenes));
                var r = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = scenes, locationPathName = output, target = target, options = BuildOptions.None });
                var s2 = r.summary;
                rep.AppendLine("- Résultat : **" + s2.result + "** en " + s2.totalTime.TotalMinutes.ToString("0.0") + " min, " + s2.totalErrors + " erreur(s), " + s2.totalWarnings + " avertissement(s)");
                rep.AppendLine("- Taille : " + (s2.totalSize / 1048576f).ToString("0.0") + " Mo");
                foreach (var step in r.steps)
                    foreach (var m in step.messages.Where(m => m.type == LogType.Error || m.type == LogType.Exception).Take(20))
                        rep.AppendLine("- ❌ " + step.name + " : " + m.content.Split('\n')[0]);
                code = s2.result == BuildResult.Succeeded ? 0 : 1;
            }
            catch (Exception e)
            {
                rep.AppendLine("- ❌ Exception : " + e.Message);
                Debug.LogException(e);
            }
            File.WriteAllText("ci-out/" + tag + "/build.md", rep.ToString());
            EditorApplication.Exit(code);
        }

        static void ConfigureAndroid(int build)
        {
            var t = NamedBuildTarget.Android;
            PlayerSettings.SetApplicationIdentifier(t, "com.sebrakon.spinarena");
            PlayerSettings.SetScriptingBackend(t, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetManagedStrippingLevel(t, ManagedStrippingLevel.Low);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.Android.bundleVersionCode = build;
            PlayerSettings.Android.forceInternetPermission = true;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = true; // mode portrait (option « Sens de l'écran »)
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.runInBackground = true;
            PlayerSettings.Android.startInFullscreen = true;
            PlayerSettings.Android.renderOutsideSafeArea = true;
            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.androidCreateSymbols = AndroidCreateSymbols.Disabled;
        }
    }
}
#endif
