using System.Collections.Generic;
using SpinArena.Util;
using UnityEngine;
using UnityEngine.UI;

namespace SpinArena.VFX
{
    /// <summary>
    /// Grands effets spectaculaires : foudre tombant du ciel, feu, fumée, poussière, traces brûlées au sol,
    /// cercles d'alerte, éclats de lumière, bulles de bouclier, auras… Tout est mutualisé (pools), rien n'est
    /// créé en plein combat une fois les pools remplis.
    /// </summary>
    public sealed class SpectacleFx : MonoBehaviour
    {
        public static SpectacleFx Instance { get; private set; }

        ParticleSystem _fire, _smoke, _embers, _dust, _electric, _leaves;
        int _budget;
        const int MaxPerFrame = 320;

        static float K => Mathf.Clamp(GameConfig.Current.tuning.topScale / 1.55f, 1f, 2f);
        static float Quality => Mathf.Clamp(GameConfig.Current.display.particles, 0f, 2f);

        void Awake()
        {
            Instance = this;
            float k = K;
            _fire = Make("Fire", 900, true, 0.35f, 0.8f, 0.5f * k, 2.5f * k, 0.7f * k, 1.7f * k, -0.35f, FireGradient(), GrowShrink());
            _smoke = Make("Smoke", 500, false, 1.4f, 3f, 0.3f * k, 1.6f * k, 1.0f * k, 2.2f * k, -0.08f, SmokeGradient(), Grow(2.6f));
            _embers = Make("Embers", 700, true, 0.8f, 1.8f, 3f * k, 10f * k, 0.06f * k, 0.16f * k, 0.6f, EmberGradient(), null, ParticleSystemRenderMode.Stretch);
            _dust = Make("Dust", 500, false, 0.9f, 1.8f, 2f * k, 7f * k, 0.8f * k, 1.8f * k, 0.05f, DustGradient(), Grow(2.2f));
            _electric = Make("Electric", 600, true, 0.12f, 0.35f, 6f * k, 16f * k, 0.04f * k, 0.1f * k, 0.2f, ElectricGradient(), null, ParticleSystemRenderMode.Stretch);
            _leaves = Make("Leaves", 300, true, 0.8f, 1.6f, 2f * k, 6f * k, 0.12f * k, 0.25f * k, 0.25f, LeafGradient(), null);
            BuildOverlay();
        }

        // ------------------------------------------------------------------ fabrication

        ParticleSystem Make(string name, int max, bool additive, float lifeMin, float lifeMax, float speedMin, float speedMax,
                            float sizeMin, float sizeMax, float gravity, Gradient colour, AnimationCurve size,
                            ParticleSystemRenderMode mode = ParticleSystemRenderMode.Billboard)
        {
            var go = new GameObject("SFX_" + name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.maxParticles = max;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = gravity;
            var em = ps.emission; em.enabled = false;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.2f;
            var col = ps.colorOverLifetime; col.enabled = true; col.color = colour;
            if (size != null) { var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, size); }
            var rot = ps.rotationOverLifetime; rot.enabled = mode == ParticleSystemRenderMode.Billboard;
            rot.z = new ParticleSystem.MinMaxCurve(-1.2f, 1.2f);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = mode;
            if (mode == ParticleSystemRenderMode.Stretch) { r.velocityScale = 0.04f; r.lengthScale = 2f; }
            r.sharedMaterial = additive ? MaterialLibrary.Additive(Color.white, MaterialLibrary.SoftDot)
                                        : MaterialLibrary.ParticleAlpha(Color.white, CloudTex);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortingFudge = additive ? -10f : 10f; // la fumée derrière le feu
            ps.Play();
            return ps;
        }

        static Gradient G(params (Color c, float t)[] keys)
        {
            var g = new Gradient();
            var ck = new GradientColorKey[keys.Length]; var ak = new GradientAlphaKey[keys.Length];
            for (int i = 0; i < keys.Length; i++) { ck[i] = new GradientColorKey(keys[i].c, keys[i].t); ak[i] = new GradientAlphaKey(keys[i].c.a, keys[i].t); }
            g.SetKeys(ck, ak);
            return g;
        }
        static Gradient FireGradient() => G((new Color(1f, 1f, 0.85f, 1f), 0f), (new Color(1f, 0.8f, 0.3f, 1f), 0.2f), (new Color(1f, 0.4f, 0.08f, 0.85f), 0.5f), (new Color(0.6f, 0.08f, 0.02f, 0f), 1f));
        static Gradient SmokeGradient() => G((new Color(0.35f, 0.33f, 0.36f, 0f), 0f), (new Color(0.3f, 0.29f, 0.32f, 0.7f), 0.15f), (new Color(0.22f, 0.21f, 0.24f, 0.45f), 0.6f), (new Color(0.2f, 0.2f, 0.22f, 0f), 1f));
        static Gradient EmberGradient() => G((new Color(1f, 0.95f, 0.6f, 1f), 0f), (new Color(1f, 0.55f, 0.1f, 1f), 0.4f), (new Color(0.8f, 0.15f, 0.02f, 0f), 1f));
        static Gradient DustGradient() => G((new Color(0.62f, 0.56f, 0.5f, 0f), 0f), (new Color(0.6f, 0.55f, 0.5f, 0.55f), 0.12f), (new Color(0.5f, 0.46f, 0.42f, 0.3f), 0.6f), (new Color(0.5f, 0.46f, 0.42f, 0f), 1f));
        static Gradient ElectricGradient() => G((Color.white, 0f), (new Color(0.7f, 0.9f, 1f, 1f), 0.4f), (new Color(0.3f, 0.5f, 1f, 0f), 1f));
        static Gradient LeafGradient() => G((new Color(0.8f, 1f, 0.6f, 1f), 0f), (new Color(0.3f, 0.9f, 0.4f, 0.9f), 0.5f), (new Color(0.1f, 0.6f, 0.2f, 0f), 1f));
        static AnimationCurve GrowShrink() => new AnimationCurve(new Keyframe(0f, 0.4f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0.15f));
        static AnimationCurve Grow(float end) => new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, end));

        static Texture2D _cloud, _scorch, _ring;
        /// <summary>Tache de nuage bruitée (fumée, poussière).</summary>
        static Texture2D CloudTex
        {
            get
            {
                if (_cloud != null) return _cloud;
                const int n = 64;
                _cloud = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Cloud" };
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float noise = Mathf.PerlinNoise(x * 0.12f, y * 0.12f) * 0.6f + Mathf.PerlinNoise(x * 0.3f + 7f, y * 0.3f + 3f) * 0.4f;
                    float a = Mathf.Clamp01(1f - d) * (0.55f + noise * 0.7f);
                    a = Mathf.Clamp01(a * a * 1.6f);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
                _cloud.SetPixels32(px); _cloud.Apply(false, true);
                return _cloud;
            }
        }
        /// <summary>Trace brûlée : centre sombre, bord irrégulier.</summary>
        static Texture2D ScorchTex
        {
            get
            {
                if (_scorch != null) return _scorch;
                const int n = 128;
                _scorch = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Scorch" };
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float ang = Mathf.Atan2(dy, dx);
                    float edge = 0.75f + Mathf.PerlinNoise(Mathf.Cos(ang) * 2f + 5f, Mathf.Sin(ang) * 2f + 5f) * 0.3f;
                    float a = Mathf.Clamp01((edge - d) * 4f);
                    float streak = Mathf.PerlinNoise(ang * 3f, d * 6f) * 0.35f;
                    a *= 0.75f + streak;
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
                }
                _scorch.SetPixels32(px); _scorch.Apply(false, true);
                return _scorch;
            }
        }
        /// <summary>Anneau lumineux (cercles d'alerte).</summary>
        static Texture2D RingTex
        {
            get
            {
                if (_ring != null) return _ring;
                const int n = 128;
                _ring = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Ring" };
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float ring = Mathf.Clamp01(1f - Mathf.Abs(d - 0.86f) * 14f);
                    float fill = d < 0.86f ? 0.18f * d : 0f;
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(ring + fill) * 255f));
                }
                _ring.SetPixels32(px); _ring.Apply(false, true);
                return _ring;
            }
        }

        // ------------------------------------------------------------------ émission

        void LateUpdate()
        {
            _budget = 0;
            UpdateDecals();
            UpdateFlashes();
            UpdateFollowers();
            UpdateOverlay();
        }

        void Emit(ParticleSystem ps, Vector3 pos, int count, Color tint, Vector3? velocity = null, float size = -1f, float life = -1f)
        {
            count = Mathf.RoundToInt(count * Quality);
            count = Mathf.Min(count, MaxPerFrame - _budget);
            if (count <= 0) return;
            _budget += count;
            var p = new ParticleSystem.EmitParams { position = pos, startColor = tint, applyShapeToPosition = true };
            if (velocity.HasValue) p.velocity = velocity.Value;
            if (size > 0f) p.startSize = size;
            if (life > 0f) p.startLifetime = life;
            ps.Emit(p, count);
        }

        public void FireBurst(Vector3 pos, int count, Color tint) => Emit(_fire, pos, count, tint);
        /// <summary>Un nuage de poussière lancé avec une vitesse donnée (tornade).</summary>
        public void DustAt(Vector3 pos, Vector3 velocity, float size) => Emit(_dust, pos, 1, Color.white, velocity, size, Random.Range(0.6f, 1.2f));
        /// <summary>Un débris sombre (morceau arraché) lancé avec une vitesse donnée.</summary>
        public void Debris(Vector3 pos, Vector3 velocity, float size) => Emit(_smoke, pos, 1, new Color(0.25f, 0.22f, 0.2f, 1f), velocity, size, Random.Range(0.5f, 0.9f));
        public void SmokePuff(Vector3 pos, int count) => Emit(_smoke, pos, count, Color.white);
        public void Embers(Vector3 pos, int count) => Emit(_embers, pos, count, Color.white);
        public void ElectricSparks(Vector3 pos, int count, Color tint) => Emit(_electric, pos, count, tint);

        /// <summary>Anneau de poussière qui s'étale au sol depuis un point.</summary>
        public void DustRing(Vector3 pos, int count, float speed)
        {
            count = Mathf.RoundToInt(count * Quality);
            for (int i = 0; i < count && _budget < MaxPerFrame; i++)
            {
                float a = i / (float)Mathf.Max(1, count) * Mathf.PI * 2f + Random.value * 0.3f;
                var dir = new Vector3(Mathf.Cos(a), 0.08f, Mathf.Sin(a));
                _budget++;
                _dust.Emit(new ParticleSystem.EmitParams { position = pos + dir * 0.4f + Vector3.up * 0.2f, velocity = dir * speed * K * Random.Range(0.7f, 1.2f), startColor = Color.white, applyShapeToPosition = false }, 1);
            }
        }

        /// <summary>Spirale de particules qui monte autour d'un point (soin, régénération, pouvoir).</summary>
        public void Spiral(Vector3 pos, Color c, int count, float radius, bool leaves = false)
        {
            count = Mathf.RoundToInt(count * Quality);
            var ps = leaves ? _leaves : _electric;
            for (int i = 0; i < count && _budget < MaxPerFrame; i++)
            {
                float a = i / (float)Mathf.Max(1, count) * Mathf.PI * 4f;
                Vector3 off = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius * K;
                Vector3 tangent = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
                _budget++;
                ps.Emit(new ParticleSystem.EmitParams { position = pos + off + Vector3.up * (i / (float)count) * 1.2f * K, velocity = tangent * 3f * K + Vector3.up * 4f * K, startColor = c, startLifetime = 0.8f, applyShapeToPosition = false }, 1);
            }
        }

        // ------------------------------------------------------------------ traces au sol et cercles d'alerte

        sealed class Decal { public Transform t; public Material mat; public float born, life, size, grow; public Color color; public bool additive, pulse; }
        readonly List<Decal> _decals = new List<Decal>();
        static Mesh _quadFlat;
        static Mesh QuadFlat
        {
            get
            {
                if (_quadFlat != null) return _quadFlat;
                _quadFlat = new Mesh { name = "FlatQuad" };
                _quadFlat.vertices = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f) };
                _quadFlat.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
                _quadFlat.triangles = new[] { 0, 2, 1, 1, 2, 3 };
                _quadFlat.RecalculateNormals(); _quadFlat.RecalculateBounds();
                return _quadFlat;
            }
        }

        Decal GetDecal(bool additive, Texture tex)
        {
            foreach (var d in _decals) if (!d.t.gameObject.activeSelf && d.additive == additive && d.mat.mainTexture == tex) return d;
            if (_decals.Count >= 60) return null;
            var go = new GameObject(additive ? "Glow" : "Scorch", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(transform, false);
            go.GetComponent<MeshFilter>().sharedMesh = QuadFlat;
            var mr = go.GetComponent<MeshRenderer>();
            var mat = additive ? new Material(MaterialLibrary.Additive(Color.white, tex)) : new Material(MaterialLibrary.ParticleAlpha(Color.white, tex));
            mat.mainTexture = tex;
            MaterialLibrary.SetTexture(mat, tex);
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            var d2 = new Decal { t = go.transform, mat = mat, additive = additive };
            _decals.Add(d2);
            return d2;
        }

        /// <summary>Trace brûlée au sol qui s'estompe (météore, foudre).</summary>
        public void Scorch(Vector3 pos, float size, float seconds, Color tint)
        {
            var d = GetDecal(false, ScorchTex);
            if (d == null) return;
            d.t.gameObject.SetActive(true);
            d.t.position = pos + Vector3.up * 0.03f;
            d.t.rotation = Quaternion.Euler(0f, Random.value * 360f, 0f);
            d.size = size * K; d.grow = 0f; d.born = Time.time; d.life = seconds; d.color = tint; d.pulse = false;
            d.t.localScale = Vector3.one * d.size;
        }

        /// <summary>Lueur au sol (bord de lave, cercle d'alerte qui clignote).</summary>
        public void GroundGlow(Vector3 pos, float size, float seconds, Color c, bool pulse, float growTo = 0f)
        {
            var d = GetDecal(true, RingTex);
            if (d == null) return;
            d.t.gameObject.SetActive(true);
            d.t.position = pos + Vector3.up * 0.05f;
            d.t.rotation = Quaternion.identity;
            d.size = size * K; d.grow = growTo > 0f ? growTo * K : 0f; d.born = Time.time; d.life = seconds; d.color = c; d.pulse = pulse;
            d.t.localScale = Vector3.one * d.size;
        }

        void UpdateDecals()
        {
            float now = Time.time;
            foreach (var d in _decals)
            {
                if (!d.t.gameObject.activeSelf) continue;
                float t = (now - d.born) / Mathf.Max(0.01f, d.life);
                if (t >= 1f) { d.t.gameObject.SetActive(false); continue; }
                float s = d.grow > 0f ? Mathf.Lerp(d.size, d.grow, t) : d.size;
                d.t.localScale = Vector3.one * s;
                float a = d.pulse ? (0.55f + 0.45f * Mathf.Sin(now * 18f)) * Mathf.Clamp01((1f - t) * 4f) : Mathf.Clamp01((1f - t) * 2f);
                var c = d.color; c.a *= a;
                MaterialLibrary.SetColor(d.mat, d.additive ? c * 2f : c);
            }
        }

        // ------------------------------------------------------------------ éclats de lumière

        sealed class Flash { public Light light; public float born, life, peak; }
        readonly List<Flash> _flashes = new List<Flash>();

        public void LightFlash(Vector3 pos, Color c, float intensity, float range, float seconds)
        {
            Flash f = null;
            foreach (var x in _flashes) if (!x.light.enabled) { f = x; break; }
            if (f == null)
            {
                if (_flashes.Count >= 8) return;
                var go = new GameObject("Flash");
                go.transform.SetParent(transform, false);
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.shadows = LightShadows.None;
                f = new Flash { light = l };
                _flashes.Add(f);
            }
            f.light.enabled = true;
            f.light.transform.position = pos;
            f.light.color = c;
            f.light.range = range * K;
            f.peak = intensity; f.born = Time.time; f.life = seconds;
            f.light.intensity = intensity;
        }

        void UpdateFlashes()
        {
            foreach (var f in _flashes)
            {
                if (!f.light.enabled) continue;
                float t = (Time.time - f.born) / Mathf.Max(0.01f, f.life);
                if (t >= 1f) { f.light.enabled = false; continue; }
                f.light.intensity = f.peak * (1f - t) * (1f - t);
            }
        }

        // ------------------------------------------------------------------ voile plein écran (éclair blanc, ciel d'orage)

        Image _flashImg, _stormImg;
        float _flashA, _stormTarget, _stormA;

        void BuildOverlay()
        {
            var cgo = new GameObject("FxOverlay", typeof(RectTransform));
            cgo.transform.SetParent(transform, false);
            var canvas = cgo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5; // sous l'interface du jeu
            _stormImg = FullScreen("Storm", cgo.transform, new Color(0.02f, 0.03f, 0.12f, 0f));
            _flashImg = FullScreen("Flash", cgo.transform, new Color(0.9f, 0.95f, 1f, 0f));
        }

        static Image FullScreen(string name, Transform parent, Color c)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            var img = go.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>Éclair blanc sur tout l'écran (0..1).</summary>
        public void ScreenFlash(float strength)
        {
            if (!GameConfig.Current.display.cameraShake && strength > 0.4f) strength = 0.4f; // option « effets doux »
            _flashA = Mathf.Max(_flashA, Mathf.Clamp01(strength));
        }

        /// <summary>Assombrit le ciel (orage) ; 0 = normal.</summary>
        public void StormSky(float darkness) => _stormTarget = Mathf.Clamp01(darkness);

        void UpdateOverlay()
        {
            float dt = Time.unscaledDeltaTime;
            _flashA = Mathf.MoveTowards(_flashA, 0f, dt * 3.2f);
            _stormA = Mathf.MoveTowards(_stormA, _stormTarget, dt * 1.5f);
            var fc = _flashImg.color; fc.a = _flashA * 0.85f; _flashImg.color = fc;
            var sc = _stormImg.color; sc.a = _stormA * 0.55f; _stormImg.color = sc;
            _flashImg.enabled = _flashA > 0.005f;
            _stormImg.enabled = _stormA > 0.005f;
        }

        // ------------------------------------------------------------------ effets qui suivent une toupie

        enum FollowKind { Bubble, Afterburner, Aura, Frost }
        sealed class Follower { public Transform target, visual; public Material mat; public FollowKind kind; public float until, born; public Color color; }
        readonly List<Follower> _followers = new List<Follower>();
        static Mesh _sphere;

        Follower GetFollower(FollowKind kind)
        {
            foreach (var f in _followers) if (f.kind == kind && f.target == null) return f;
            if (_followers.Count >= 48) return null;
            var f2 = new Follower { kind = kind };
            if (kind == FollowKind.Bubble || kind == FollowKind.Aura)
            {
                if (_sphere == null)
                {
                    var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    _sphere = tmp.GetComponent<MeshFilter>().sharedMesh;
                    Destroy(tmp);
                }
                var go = new GameObject(kind.ToString(), typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(transform, false);
                go.GetComponent<MeshFilter>().sharedMesh = kind == FollowKind.Bubble ? _sphere : QuadFlat;
                var mr = go.GetComponent<MeshRenderer>();
                f2.mat = new Material(MaterialLibrary.Additive(Color.white, kind == FollowKind.Bubble ? BubbleTex : RingTex));
                if (f2.mat.HasProperty("_Cull")) f2.mat.SetFloat("_Cull", 0f);
                mr.sharedMaterial = f2.mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                f2.visual = go.transform;
                go.SetActive(false);
            }
            _followers.Add(f2);
            return f2;
        }

        static Texture2D _bubble;
        /// <summary>Texture de bulle : bord lumineux, centre presque transparent, reflets en bandes.</summary>
        static Texture2D BubbleTex
        {
            get
            {
                if (_bubble != null) return _bubble;
                const int n = 64;
                _bubble = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, name = "Bubble" };
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float v = y / (float)n;
                    float hex = Mathf.Abs(Mathf.Sin(x / (float)n * Mathf.PI * 12f)) * Mathf.Abs(Mathf.Sin(v * Mathf.PI * 8f));
                    float a = 0.12f + Mathf.Pow(1f - hex, 6f) * 0.5f;
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
                }
                _bubble.SetPixels32(px); _bubble.Apply(false, true);
                return _bubble;
            }
        }

        /// <summary>Bulle de bouclier autour de la toupie, pendant la durée du bouclier.</summary>
        public void ShieldBubble(Transform top, float seconds, Color c) => Follow(FollowKind.Bubble, top, seconds, c);
        /// <summary>Flammes de réacteur derrière la toupie (turbo).</summary>
        public void Afterburner(Transform top, float seconds, Color c) => Follow(FollowKind.Afterburner, top, seconds, c);
        /// <summary>Cercle d'énergie tournant au sol sous la toupie (surpuissance, royale).</summary>
        public void PowerAura(Transform top, float seconds, Color c) => Follow(FollowKind.Aura, top, seconds, c);
        /// <summary>Givre et cristaux sur une toupie gelée.</summary>
        public void Frost(Transform top, float seconds) => Follow(FollowKind.Frost, top, seconds, new Color(0.7f, 0.92f, 1f));

        void Follow(FollowKind kind, Transform top, float seconds, Color c)
        {
            if (top == null) return;
            foreach (var x in _followers) if (x.kind == kind && x.target == top) { x.until = Mathf.Max(x.until, Time.time + seconds); return; }
            var f = GetFollower(kind);
            if (f == null) return;
            f.target = top; f.until = Time.time + seconds; f.born = Time.time; f.color = c;
            if (f.visual != null) f.visual.gameObject.SetActive(true);
        }

        void UpdateFollowers()
        {
            float now = Time.time, k = K;
            foreach (var f in _followers)
            {
                if (f.target == null) continue;
                if (now >= f.until || !f.target.gameObject.activeInHierarchy)
                {
                    f.target = null;
                    if (f.visual != null) f.visual.gameObject.SetActive(false);
                    continue;
                }
                Vector3 p = f.target.position;
                float age = now - f.born, left = f.until - now;
                float fade = Mathf.Clamp01(age * 5f) * Mathf.Clamp01(left * 2f);
                switch (f.kind)
                {
                    case FollowKind.Bubble:
                        f.visual.position = p + Vector3.up * 0.45f * k;
                        f.visual.localScale = Vector3.one * (1.25f * k * (1f + 0.04f * Mathf.Sin(now * 9f)));
                        f.visual.rotation = Quaternion.Euler(0f, now * 40f, 0f);
                        var bc = f.color; bc.a = 0.55f * fade; MaterialLibrary.SetColor(f.mat, bc * 1.4f);
                        break;
                    case FollowKind.Aura:
                        f.visual.position = p + Vector3.up * 0.06f;
                        f.visual.localScale = Vector3.one * (2.2f * k * (1f + 0.08f * Mathf.Sin(now * 6f)));
                        f.visual.rotation = Quaternion.Euler(0f, now * 120f, 0f);
                        var ac = f.color; ac.a = 0.8f * fade; MaterialLibrary.SetColor(f.mat, ac * 2f);
                        if (Random.value < 0.5f) Emit(_embers, p + Vector3.up * 0.3f, 1, f.color, Vector3.up * 5f * k);
                        break;
                    case FollowKind.Afterburner:
                        var rb = f.target.GetComponent<Rigidbody>();
                        Vector3 v = rb != null ? Compat.GetVelocity(rb) : Vector3.zero;
                        v.y = 0f;
                        Vector3 back = v.sqrMagnitude > 0.5f ? -v.normalized : Random.insideUnitSphere.Flat().normalized;
                        Emit(_fire, p + back * 0.5f * k + Vector3.up * 0.3f, 2, Color.Lerp(Color.white, f.color, 0.4f), back * 4f * k + Vector3.up, 0.9f * k, 0.35f);
                        if (Random.value < 0.4f) Emit(_embers, p, 1, Color.white, back * 6f * k + Vector3.up * 2f);
                        break;
                    case FollowKind.Frost:
                        if (Random.value < 0.35f) Emit(_electric, p + Random.insideUnitSphere * 0.6f * k + Vector3.up * 0.4f * k, 1, f.color, Vector3.up * 0.5f);
                        if (Random.value < 0.1f) Emit(_smoke, p + Vector3.up * 0.3f, 1, new Color(0.85f, 0.95f, 1f, 0.5f), Vector3.up * 0.4f, 0.8f * k, 0.8f);
                        break;
                }
            }
        }

        // ------------------------------------------------------------------ explosions composées

        /// <summary>Gerbe de feu + fumée + braises + poussière + trace : impact de météore.</summary>
        public void FireImpact(Vector3 pos, float scale)
        {
            float k = K * scale;
            for (int i = 0; i < 18; i++)
            {
                var dir = Random.onUnitSphere; dir.y = Mathf.Abs(dir.y) * 0.8f + 0.2f;
                Emit(_fire, pos + Vector3.up * 0.3f, 1, Color.white, dir * Random.Range(2f, 7f) * k, Random.Range(1.2f, 2.4f) * k, Random.Range(0.4f, 0.8f));
            }
            Emit(_fire, pos + Vector3.up * 0.6f, 8, Color.white, Vector3.up * 5f * k, 2.4f * k, 0.6f);
            Emit(_embers, pos, Mathf.RoundToInt(70 * scale), Color.white);
            for (int i = 0; i < 10; i++) Emit(_smoke, pos + Vector3.up * (0.5f + i * 0.35f) * k, 1, Color.white, Vector3.up * Random.Range(1f, 3f) * k + Random.insideUnitSphere * k, Random.Range(1.6f, 2.8f) * k, Random.Range(2f, 3.4f));
            DustRing(pos, Mathf.RoundToInt(26 * scale), 7f * scale);
            Scorch(pos, 3.6f * scale, 9f, new Color(0.05f, 0.03f, 0.03f, 0.9f));
            GroundGlow(pos, 3.2f * scale, 3.5f, new Color(1f, 0.4f, 0.08f, 0.9f), false, 4.2f * scale);
            LightFlash(pos + Vector3.up * 1.5f, new Color(1f, 0.55f, 0.2f), 18f * scale, 14f, 0.7f);
        }

        /// <summary>Impact de foudre : gerbe électrique, arcs au sol, fumée, trace brûlée.</summary>
        public void LightningImpact(Vector3 pos, Color c)
        {
            float k = K;
            Emit(_electric, pos + Vector3.up * 0.3f, 90, Color.Lerp(c, Color.white, 0.5f));
            for (int i = 0; i < 6; i++)
            {
                var dir = Random.onUnitSphere; dir.y = Mathf.Abs(dir.y) * 0.5f;
                Emit(_fire, pos + Vector3.up * 0.3f, 1, new Color(0.75f, 0.9f, 1f), dir * 4f * k, 1.4f * k, 0.3f);
            }
            for (int i = 0; i < 6; i++) Emit(_smoke, pos + Vector3.up * (0.4f + i * 0.3f) * k, 1, Color.white, Vector3.up * 1.8f * k + Random.insideUnitSphere * 0.6f * k, 1.8f * k, 2.4f);
            DustRing(pos, 22, 6f);
            Scorch(pos, 3f, 8f, new Color(0.04f, 0.04f, 0.07f, 0.9f));
            GroundGlow(pos, 2.4f, 1.2f, new Color(c.r, c.g, c.b, 1f), false, 5f);
            LightFlash(pos + Vector3.up * 2f, Color.Lerp(c, Color.white, 0.5f), 45f, 26f, 0.5f);
        }
    }
}
