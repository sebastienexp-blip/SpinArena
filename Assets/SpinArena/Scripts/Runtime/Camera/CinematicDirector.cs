using System.Collections;
using SpinArena.Core.Battle;
using SpinArena.Rendering;
using UnityEngine;

namespace SpinArena.CameraRig
{
    /// <summary>
    /// Ralentis cinématiques sur les grosses actions : le temps ralentit, la caméra fonce sur l'action, l'image
    /// « claque » (zoom, aberration chromatique), puis tout repart. Un délai minimum évite d'en abuser.
    /// </summary>
    public sealed class CinematicDirector : MonoBehaviour
    {
        public static CinematicDirector Instance { get; private set; }
        float _cooldownUntil;
        bool _active;
        public int Played { get; private set; }

        void Awake() => Instance = this;

        /// <summary>Lance un ralenti. target null = ralenti sur toute l'arène (sans changer de plan).</summary>
        public bool Play(Transform target, float slow = 0.3f, float realSeconds = 0.8f, float zoom = 0.8f, float strength = 0.6f, bool force = false)
        {
            var gm = GameManager.Instance;
            var show = GameConfig.Current.show;
            if (gm == null || !show.cinematics || _active) return false;
            var round = gm.Battle.Round;
            if (round.Paused) return false;
            if (!force && (Time.unscaledTime < _cooldownUntil || round.Phase != RoundPhase.Fighting)) return false;
            _cooldownUntil = Time.unscaledTime + Mathf.Max(2f, show.cinematicCooldown);
            StartCoroutine(Run(target, Mathf.Clamp(Mathf.LerpUnclamped(1f, slow, show.slowMotionStrength), 0.1f, 1f), realSeconds, zoom, strength));
            return true;
        }

        IEnumerator Run(Transform target, float slow, float realSeconds, float zoom, float strength)
        {
            _active = true;
            Played++;
            var cam = CameraDirector.Instance;
            if (target != null) cam.FocusFast(target, zoom);
            cam.Punch(9f * strength);
            cam.Shake(0.15f * strength);
            RenderingSetup.Pulse(strength);
            Time.timeScale = slow;
            Time.fixedDeltaTime = 1f / 60f * slow; // physique fluide pendant le ralenti
            yield return new WaitForSecondsRealtime(realSeconds);
            var round = GameManager.Instance.Battle.Round;
            if (!round.Paused) Time.timeScale = 1f;
            Time.fixedDeltaTime = 1f / 60f;
            // Retour au plan large, sauf si l'écran du vainqueur a pris la main entre-temps.
            if (target != null && round.Phase != RoundPhase.Victory && cam.CurrentFocus == target) cam.Focus(null, 0f);
            _active = false;
        }
    }
}
