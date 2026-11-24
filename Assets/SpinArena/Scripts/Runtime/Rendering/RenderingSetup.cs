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
            _chroma = profile.Add<ChromaticAberration>(true);
            _chroma.intensity.Override(0f);
            _lens = profile.Add<LensDistortion>(true);
            _lens.intensity.Override(0f);
            vol.sharedProfile = profile;
            _volume = vol;
            go.AddComponent<PulseDriver>();
        }

        static Volume _volume;
        static ChromaticAberration _chroma;
        static LensDistortion _lens;
        static float _pulse;

        /// <summary>Impulsion d'image pour les gros coups : aberration chromatique et distorsion qui s'estompent.</summary>
        public static void Pulse(float strength) => _pulse = Mathf.Max(_pulse, Mathf.Clamp01(strength));

        sealed class PulseDriver : MonoBehaviour
        {
            void Update()
            {
                _pulse = Mathf.MoveTowards(_pulse, 0f, Time.unscaledDeltaTime * 1.6f);
                if (_chroma != null) _chroma.intensity.value = _pulse * 0.9f;
                if (_lens != null) _lens.intensity.value = -_pulse * 0.35f;
            }
        }

        /// <summary>Qualité graphique réglable dans les options : lueurs, échelle de rendu.</summary>
        public static void ApplyQuality(bool bloom, float renderScale)
        {
            if (_volume != null) _volume.enabled = bloom;
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp) urp.renderScale = Mathf.Clamp(renderScale, 0.5f, 1f);
        }
    }
}
