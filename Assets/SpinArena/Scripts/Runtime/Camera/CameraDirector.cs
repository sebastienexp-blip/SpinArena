using UnityEngine;

namespace SpinArena.CameraRig
{
    /// <summary>
    /// Caméra de réalisation : vue d'ensemble qui tourne lentement, rapprochement quand il reste peu de survivants,
    /// plan sur le vainqueur, secousses sur les gros impacts.
    /// </summary>
    public sealed class CameraDirector : MonoBehaviour
    {
        public static CameraDirector Instance { get; private set; }
        public Camera Cam { get; private set; }

        float _arenaRadius = 16f;
        float _orbit;
        float _shake;
        Transform _focus;
        Vector3 _focusPoint;
        float _closeness;       // 0 = vue d'ensemble, 1 = plan rapproché
        float _targetCloseness;
        Vector3 _smoothedFocus;

        void Awake()
        {
            Instance = this;
            Cam = gameObject.AddComponent<Camera>();
            Cam.clearFlags = CameraClearFlags.SolidColor;
            Cam.backgroundColor = new Color(0.02f, 0.03f, 0.07f);
            Cam.fieldOfView = 45f;
            Cam.nearClipPlane = 0.3f;
            Cam.farClipPlane = 200f;
            Cam.allowHDR = true;
            gameObject.tag = "MainCamera";
            gameObject.AddComponent<AudioListener>();
        }

        /// <summary>Recadre la vue d'ensemble sur l'arène (elle s'agrandit quand il y a beaucoup de toupies).</summary>
        public void Configure(float arenaRadius)
        {
            _arenaRadius = arenaRadius;
            // Le fond étoilé grandit avec l'arène : la caméra doit voir assez loin pour l'atteindre.
            if (Cam != null) Cam.farClipPlane = Mathf.Max(200f, arenaRadius * 16f);
        }

        /// <summary>Plan sur une toupie (null = retour à la vue d'ensemble).</summary>
        float _focusSpeed = 0.6f, _fovPunch;

        /// <summary>Plan rapide pour un ralenti : la caméra fonce sur la cible.</summary>
        public void FocusFast(Transform t, float closeness)
        {
            Focus(t, closeness);
            _focusSpeed = 3.5f;
        }

        /// <summary>Petit « coup de zoom » (l'angle de vue se resserre puis revient).</summary>
        public void Punch(float amount) => _fovPunch = Mathf.Max(_fovPunch, amount);

        public Transform CurrentFocus => _focus;

        public void Focus(Transform t, float closeness)
        {
            _focus = t;
            _targetCloseness = t != null ? closeness : 0f;
        }

        /// <summary>Resserre légèrement le cadre selon le nombre de survivants.</summary>
        public void SetTension(float tension01)
        {
            if (_focus == null) _targetCloseness = Mathf.Clamp01(tension01) * 0.35f;
        }

        public void Shake(float amount)
        {
            var d = GameConfig.Current.display;
            if (!d.cameraShake) return;
            _shake = Mathf.Min(_shake + amount * d.shakeStrength, 0.6f * Mathf.Max(d.shakeStrength, 0.1f));
        }

        /// <summary>
        /// Angle de vue : 45° en paysage. En portrait (écran plus haut que large), l'angle vertical s'ouvre
        /// pour que l'arène occupe toute la largeur de l'écran.
        /// </summary>
        /// <summary>Gros plans plus éloignés quand les toupies sont plus grosses.</summary>
        static float TopScaleFactor => Mathf.Max(1f, GameConfig.Current.tuning.topScale / 1.55f);

        public static float BaseFov(float aspect)
        {
            if (aspect >= 1f) return 45f;
            float v = 2f * Mathf.Atan(Mathf.Tan(23f * Mathf.Deg2Rad) / Mathf.Max(0.3f, aspect)) * Mathf.Rad2Deg;
            return Mathf.Clamp(v, 45f, 95f);
        }

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            var disp = GameConfig.Current.display;
            _orbit += dt * (_focus != null ? 10f : 3.5f * disp.cameraOrbit);
            _closeness = Mathf.MoveTowards(_closeness, _targetCloseness, dt * _focusSpeed);
            if (Mathf.Abs(_closeness - _targetCloseness) < 0.001f) _focusSpeed = 0.6f;
            _fovPunch = Mathf.MoveTowards(_fovPunch, 0f, dt * 18f);
            Cam.fieldOfView = BaseFov(Cam.aspect) - _fovPunch;
            _focusPoint = _focus != null && _focus.gameObject.activeInHierarchy ? _focus.position : Vector3.zero;
            _smoothedFocus = Vector3.Lerp(_smoothedFocus, _focusPoint, 1f - Mathf.Exp(-dt * 3f));

            // Vue d'ensemble : toute l'arène tient dans un écran 16:9 (caméra en hauteur, inclinée).
            // Portrait : caméra plus plongeante et plus proche, l'arène remplit tout l'écran (le bord déborde un peu).
            bool portrait = Cam.aspect < 1f;
            float dist = Mathf.Lerp(_arenaRadius * (portrait ? 2.2f : 2.45f) * Mathf.Clamp(disp.cameraDistance, 0.6f, 1.5f), 7f * TopScaleFactor, _closeness);
            float pitch = Mathf.Lerp(portrait ? 60f : 52f, 30f, _closeness);
            Quaternion rot = Quaternion.Euler(pitch, _orbit, 0f);
            Vector3 look = Vector3.Lerp(Vector3.zero, _smoothedFocus + Vector3.up * 0.6f, Mathf.Clamp01(_closeness * 2f));
            Vector3 pos = look - rot * Vector3.forward * dist;
            if (_shake > 0f)
            {
                pos += Random.insideUnitSphere * _shake;
                _shake = Mathf.MoveTowards(_shake, 0f, dt * 1.8f);
            }
            transform.SetPositionAndRotation(pos, rot);
        }
    }
}
