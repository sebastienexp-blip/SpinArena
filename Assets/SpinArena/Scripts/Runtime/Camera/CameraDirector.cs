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

        float _lowUntil, _low;
        /// <summary>Caméra plus basse (vue de côté) pendant quelques secondes : tornade, orage — on voit la hauteur des effets.</summary>
        public void LowAngle(float seconds) => _lowUntil = Mathf.Max(_lowUntil, Time.time + seconds);

        public Transform CurrentFocus => _focus;

        public void Focus(Transform t, float closeness)
        {
            _focus = t;
            _targetCloseness = t != null ? closeness : 0f;
        }

        /// <summary>Resserre légèrement le cadre selon le nombre de survivants.</summary>
        public void SetTension(float tension01)
        {
            // Le cadrage automatique sur les toupies remplace le resserrement : rien à faire ici.
            if (_focus == null) _targetCloseness = 0f;
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

        float _fitDist;
        Vector3 _fitCenter;
        readonly System.Collections.Generic.List<(Vector3 p, float r)> _field = new System.Collections.Generic.List<(Vector3, float)>();

        void CollectField()
        {
            _field.Clear();
            var br = GameManager.Instance != null ? GameManager.Instance.Battle : null;
            if (br == null) return;
            foreach (var t in br.OnField()) _field.Add((t.transform.position + Vector3.up * 0.4f, t.VisualRadius + 0.6f));
        }

        /// <summary>Centre des toupies (à plat), ramené doucement vers le centre de l'arène.</summary>
        Vector3 FieldCenter()
        {
            CollectField();
            if (_field.Count == 0) return Vector3.zero;
            Vector3 min = _field[0].p, max = _field[0].p;
            foreach (var f in _field) { min = Vector3.Min(min, f.p); max = Vector3.Max(max, f.p); }
            Vector3 c = (min + max) * 0.5f; c.y = 0f;
            return c * 0.7f;
        }

        /// <summary>Plus petite distance de caméra pour laquelle toutes les toupies tiennent dans le cadre utile.</summary>
        float FitDistance(Quaternion rot, Vector3 look, float baseDist)
        {
            if (_field.Count == 0) return baseDist;
            float tanV = Mathf.Tan(Cam.fieldOfView * 0.5f * Mathf.Deg2Rad), tanH = tanV * Cam.aspect;
            bool portrait = Cam.aspect < 1f;
            // Zones libres : en portrait le haut porte le chrono et le bas est couvert par le chat TikTok.
            float top = portrait ? 0.62f : 0.72f, bottom = portrait ? -0.55f : -0.85f, side = 0.86f;
            var inv = Quaternion.Inverse(rot);
            Vector3 fwd = rot * Vector3.forward;
            float lo = baseDist * 0.55f, hi = baseDist * 2.6f;
            for (int it = 0; it < 14; it++)
            {
                float d = (lo + hi) * 0.5f;
                Vector3 pos = look - fwd * d;
                bool ok = true;
                foreach (var f in _field)
                {
                    Vector3 v = inv * (f.p - pos);
                    if (v.z < 0.5f) { ok = false; break; }
                    float rx = f.r / v.z;
                    float x = v.x / (v.z * tanH), y = v.y / (v.z * tanV);
                    float ry = rx / tanV, rxh = rx / tanH;
                    if (Mathf.Abs(x) + rxh > side || y + ry > top || y - ry < bottom) { ok = false; break; }
                }
                if (ok) hi = d; else lo = d;
            }
            return hi;
        }

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
            float baseDist = _arenaRadius * (portrait ? 2.0f : 2.45f) * Mathf.Clamp(disp.cameraDistance, 0.6f, 1.5f);
            _low = Mathf.MoveTowards(_low, Time.time < _lowUntil ? 1f : 0f, dt * 0.8f);
            float lowEase = _low * _low * (3f - 2f * _low);
            float pitch = Mathf.Lerp(Mathf.Lerp(portrait ? 64f : 52f, portrait ? 38f : 34f, lowEase), 30f, _closeness);
            Quaternion rot = Quaternion.Euler(pitch, _orbit, 0f);
            // Vue d'ensemble cadrée sur les toupies : toutes restent visibles (hors zones de l'interface et du chat).
            Vector3 center = FieldCenter();
            _fitCenter = Vector3.Lerp(_fitCenter, center, 1f - Mathf.Exp(-dt * 1.5f));
            float fit = FitDistance(rot, _fitCenter, baseDist);
            _fitDist = _fitDist <= 0f ? fit : Mathf.Lerp(_fitDist, fit, 1f - Mathf.Exp(-dt * (fit > _fitDist ? 4f : 1.2f)));
            float dist = Mathf.Lerp(_fitDist, 7f * TopScaleFactor, _closeness);
            Vector3 look = Vector3.Lerp(_fitCenter, _smoothedFocus + Vector3.up * 0.6f, Mathf.Clamp01(_closeness * 2f));
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
