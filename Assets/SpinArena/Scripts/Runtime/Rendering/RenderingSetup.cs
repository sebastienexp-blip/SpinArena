using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SpinArena.Rendering
{
    /// <summary>Post-traitement URP : bloom (lueurs), tonemapping ACES, vignette légère, couleurs plus vives.</summary>
    public static class RenderingSetup
    {
        public static void Apply(Camera cam)
        {
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            data.renderShadows = true;

            var go = new GameObject("PostFX");
            go.transform.SetParent(cam.transform, false);
            var vol = go.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 1f;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(0.95f);
            bloom.intensity.Override(1.1f);
            bloom.scatter.Override(0.65f);
            bloom.highQualityFiltering.Override(false);
            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);
            var vig = profile.Add<Vignette>(true);
            vig.intensity.Override(0.22f);
            vig.smoothness.Override(0.5f);
            var color = profile.Add<ColorAdjustments>(true);
            color.saturation.Override(12f);
            color.contrast.Override(8f);
            color.postExposure.Override(0.15f);
            vol.sharedProfile = profile;
            _volume = vol;
        }

        static Volume _volume;

        /// <summary>Qualité graphique réglable dans les options : lueurs, échelle de rendu.</summary>
        public static void ApplyQuality(bool bloom, float renderScale)
        {
            if (_volume != null) _volume.enabled = bloom;
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp) urp.renderScale = Mathf.Clamp(renderScale, 0.5f, 1f);
        }
    }
}
