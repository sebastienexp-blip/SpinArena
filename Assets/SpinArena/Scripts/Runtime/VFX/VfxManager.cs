using System.Collections.Generic;
using SpinArena.Util;
using UnityEngine;

namespace SpinArena.VFX
{
    /// <summary>
    /// Effets visuels mutualisés : quelques systèmes de particules partagés qui émettent à la demande
    /// (aucune création d'objet pendant le combat), plus un petit pool pour les météores.
    /// </summary>
    public sealed class VfxManager : MonoBehaviour
    {
        public static VfxManager Instance { get; private set; }

        ParticleSystem _sparks, _glow, _debris, _beam, _confetti, _ring;
        readonly Queue<Meteor> _meteorPool = new Queue<Meteor>();
        int _budgetThisFrame;
        const int MaxParticlesPerFrame = 260;

        void Awake()
        {
            Instance = this;
            _sparks = Make("Sparks", 1200, true, 0.25f, 0.55f, 6f, 16f, 0.05f, 0.12f, ParticleSystemRenderMode.Stretch);
            _glow = Make("Glow", 200, false, 0.25f, 0.45f, 0f, 0.5f, 1.2f, 2.4f, ParticleSystemRenderMode.Billboard);
            _debris = Make("Debris", 600, true, 0.6f, 1.2f, 3f, 9f, 0.08f, 0.2f, ParticleSystemRenderMode.Billboard);
            _beam = Make("Beam", 400, false, 0.7f, 1.1f, 6f, 12f, 0.25f, 0.6f, ParticleSystemRenderMode.Stretch);
            _confetti = Make("Confetti", 800, true, 2.5f, 4f, 4f, 11f, 0.12f, 0.25f, ParticleSystemRenderMode.Billboard);
            _ring = Make("Shock", 60, false, 0.35f, 0.4f, 0f, 0f, 1f, 1f, ParticleSystemRenderMode.HorizontalBillboard);

            var beamShape = _beam.shape; beamShape.shapeType = ParticleSystemShapeType.Circle; beamShape.radius = 0.5f;
            beamShape.rotation = new Vector3(-90f, 0f, 0f);
            var confMain = _confetti.main; confMain.gravityModifier = 0.6f;
            var debrisMain = _debris.main; debrisMain.gravityModifier = 1.2f;
            var sparkMain = _sparks.main; sparkMain.gravityModifier = 0.8f;
            var ringSize = _ring.sizeOverLifetime; ringSize.enabled = true;
            ringSize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.2f), new Keyframe(1f, 9f)));
        }

        ParticleSystem Make(string name, int max, bool gravity, float lifeMin, float lifeMax, float speedMin, float speedMax,
                            float sizeMin, float sizeMax, ParticleSystemRenderMode mode)
        {
            var go = new GameObject("FX_" + name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true; // reste « en lecture » pour simuler les particules émises à la demande
            main.maxParticles = max;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.gravityModifier = gravity ? 0.5f : 0f;
            var em = ps.emission; em.enabled = false;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.1f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = mode;
            if (mode == ParticleSystemRenderMode.Stretch) { r.velocityScale = 0.05f; r.lengthScale = 1.5f; }
            r.sharedMaterial = MaterialLibrary.Additive(Color.white, MaterialLibrary.SoftDot);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        void LateUpdate() => _budgetThisFrame = 0;

        void Emit(ParticleSystem ps, Vector3 pos, int count, Color color)
        {
            count = Mathf.RoundToInt(count * Mathf.Clamp(GameConfig.Current.display.particles, 0f, 2f));
            count = Mathf.Min(count, MaxParticlesPerFrame - _budgetThisFrame);
            if (count <= 0) return;
            _budgetThisFrame += count;
            var p = new ParticleSystem.EmitParams { position = pos, startColor = color, applyShapeToPosition = true };
            ps.Emit(p, count);
        }

        /// <summary>Étincelles d'impact entre deux toupies ; intensité selon les dégâts.</summary>
        public void Impact(Vector3 pos, Color a, Color b, float damage)
        {
            int n = Mathf.Clamp(Mathf.RoundToInt(damage * 0.35f), 6, 60);
            Emit(_sparks, pos, n / 2, Color.Lerp(a, Color.white, 0.4f) * 1.5f);
            Emit(_sparks, pos, n / 2, Color.Lerp(b, Color.white, 0.4f) * 1.5f);
            if (damage > 60f) Emit(_glow, pos, 1, Color.Lerp(a, b, 0.5f));
        }

        public void WallSparks(Vector3 pos) => Emit(_sparks, pos, 10, new Color(1f, 0.85f, 0.5f) * 1.4f);

        /// <summary>Explosion d'élimination.</summary>
        public void Explosion(Vector3 pos, Color color, float scale = 1f)
        {
            Emit(_glow, pos, 2, color * 2f);
            Emit(_debris, pos, Mathf.RoundToInt(40 * scale), Color.Lerp(color, Color.white, 0.3f));
            Emit(_sparks, pos, Mathf.RoundToInt(50 * scale), color * 1.8f);
            Emit(_ring, pos + Vector3.up * 0.1f, 1, color);
        }

        /// <summary>Colonne de lumière à l'arrivée d'un joueur.</summary>
        public void JoinBeam(Vector3 pos, Color color)
        {
            Emit(_beam, pos, 40, color * 1.6f);
            Emit(_ring, pos + Vector3.up * 0.05f, 1, color);
        }

        public void Shockwave(Vector3 pos, Color color)
        {
            Emit(_ring, pos + Vector3.up * 0.15f, 1, color * 2f);
            Emit(_sparks, pos, 30, color * 1.6f);
        }

        public void Confetti(Vector3 pos)
        {
            Color[] palette = { new Color(1f, 0.8f, 0.2f), new Color(0.3f, 0.9f, 1f), new Color(1f, 0.3f, 0.6f), new Color(0.5f, 1f, 0.4f) };
            foreach (var c in palette) Emit(_confetti, pos + Vector3.up * 2f, 40, c * 1.3f);
        }

        public void Sparkle(Vector3 pos, Color c) => Emit(_beam, pos, 6, c * 1.4f);

        /// <summary>Petite fumée grise (toupie presque à court d'énergie).</summary>
        public void Smoke(Vector3 pos) => Emit(_glow, pos, 1, new Color(0.45f, 0.45f, 0.5f, 0.6f));

        // ------------------------------------------------------------------ tornade (entonnoir visible)

        Transform _funnel;
        Material _funnelMat;

        /// <summary>Affiche l'entonnoir de la tornade (cône tournant, rayé et lumineux) à cet endroit.</summary>
        public void ShowTornado(Vector3 center, float radius, float alpha, float phase)
        {
            if (_funnel == null) _funnel = BuildFunnel();
            _funnel.gameObject.SetActive(true);
            _funnel.position = center;
            _funnel.localScale = new Vector3(radius, 1f, radius);
            _funnel.rotation = Quaternion.Euler(0f, -phase * Mathf.Rad2Deg * 1.6f, 0f);
            MaterialLibrary.SetColor(_funnelMat, new Color(0.8f, 0.72f, 1f, Mathf.Clamp01(alpha)));
            _funnelMat.SetTextureOffset("_BaseMap", new Vector2(phase * 0.35f, -phase * 0.5f));
        }

        public void HideTornado() { if (_funnel != null) _funnel.gameObject.SetActive(false); }

        Transform BuildFunnel()
        {
            // Cône ouvert : étroit en bas, large en haut, 8 m de haut. UV : u = tour, v = hauteur.
            const int seg = 40, rings = 12;
            var v = new System.Collections.Generic.List<Vector3>();
            var uv = new System.Collections.Generic.List<Vector2>();
            var tri = new System.Collections.Generic.List<int>();
            for (int i = 0; i <= rings; i++)
            {
                float h = i / (float)rings;
                float r = Mathf.Lerp(0.18f, 1f, h * h * 0.6f + h * 0.4f);
                for (int j = 0; j <= seg; j++)
                {
                    float a = j / (float)seg * Mathf.PI * 2f;
                    v.Add(new Vector3(Mathf.Cos(a) * r, h * 8f, Mathf.Sin(a) * r));
                    uv.Add(new Vector2(j / (float)seg * 3f, h * 2f));
                }
            }
            for (int i = 0; i < rings; i++)
            for (int j = 0; j < seg; j++)
            {
                int a = i * (seg + 1) + j, b = a + 1, c = a + seg + 1, d = c + 1;
                tri.Add(a); tri.Add(c); tri.Add(b); tri.Add(b); tri.Add(c); tri.Add(d);
            }
            var mesh = new Mesh { name = "Funnel" };
            mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(tri, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();

            // Texture rayée en diagonale (vent), transparente entre les bandes, plus légère en haut.
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, name = "Wind" };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float s = Mathf.Repeat((x + y * 0.6f) / n * 4f, 1f);
                float band = Mathf.Clamp01(1f - Mathf.Abs(s - 0.5f) * 3.2f);
                float noise = Mathf.Repeat(Mathf.Sin(x * 12.9898f + y * 78.233f) * 43758.5f, 1f) * 0.25f;
                px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(band * 0.85f + noise * band) * 255f));
            }
            tex.SetPixels32(px); tex.Apply();

            var go = new GameObject("TornadoFunnel");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            _funnelMat = new Material(MaterialLibrary.Additive(Color.white, tex));
            if (_funnelMat.HasProperty("_Cull")) _funnelMat.SetFloat("_Cull", 0f); // visible des deux côtés
            mr.sharedMaterial = _funnelMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.SetActive(false);
            return go.transform;
        }

        /// <summary>Entonnoir de tornade : particules en hélice qui tournent et montent (à appeler à chaque image).</summary>
        public void Vortex(Vector3 center, float radius, float phase, Color color)
        {
            // Nombre de particules selon le temps écoulé : même rendu quelle que soit la fluidité.
            int n = Mathf.RoundToInt(500f * Time.deltaTime * Mathf.Clamp(GameConfig.Current.display.particles, 0f, 2f));
            n = Mathf.Clamp(n, 1, 60);
            n = Mathf.Min(n, MaxParticlesPerFrame - _budgetThisFrame);
            if (n <= 0) return;
            _budgetThisFrame += n;
            for (int i = 0; i < n; i++)
            {
                float h = Random.value * 7f;                 // hauteur dans l'entonnoir
                float rr = Mathf.Lerp(radius * 0.25f, radius, h / 7f);
                float a = phase + Random.value * Mathf.PI * 2f;
                Vector3 p = center + new Vector3(Mathf.Cos(a) * rr, h, Mathf.Sin(a) * rr);
                Vector3 tangent = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
                var ep = new ParticleSystem.EmitParams
                {
                    position = p,
                    velocity = tangent * (6f + rr * 2f) + Vector3.up * 2.5f,
                    startColor = Color.Lerp(color, Color.white, Random.value * 0.5f),
                    startSize = Random.Range(0.25f, 0.6f),
                    startLifetime = Random.Range(0.4f, 0.8f),
                    applyShapeToPosition = false
                };
                _glow.Emit(ep, 1);
            }
            if (Random.value < 0.4f) Emit(_debris, center + Vector3.up * 0.3f, 2, new Color(0.7f, 0.65f, 0.8f));
        }

        // ------------------------------------------------------------------ météores (événement légendaire)

        public sealed class Meteor : MonoBehaviour
        {
            public Vector3 target;
            public float speed = 26f;
            public System.Action<Meteor> Landed;
            public Color color;
            TrailRenderer _trail;

            public void Init()
            {
                if (_trail != null) return;
                var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(body.GetComponent<Collider>());
                body.transform.SetParent(transform, false);
                body.transform.localScale = Vector3.one * 0.9f;
                body.GetComponent<MeshRenderer>().sharedMaterial = MaterialLibrary.Metal(Color.black, 0f, 0.3f, new Color(1f, 0.45f, 0.1f) * 4f);
                _trail = gameObject.AddComponent<TrailRenderer>();
                _trail.time = 0.6f;
                _trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1.2f), new Keyframe(1f, 0f));
                _trail.sharedMaterial = MaterialLibrary.Additive(new Color(1f, 0.55f, 0.15f), MaterialLibrary.SoftDot);
            }

            public void Fire(Vector3 from, Vector3 to)
            {
                transform.position = from;
                target = to;
                gameObject.SetActive(true);
                _trail.Clear();
            }

            void Update()
            {
                transform.position = Vector3.MoveTowards(transform.position, target, speed * Time.deltaTime);
                if ((transform.position - target).sqrMagnitude < 0.01f) { gameObject.SetActive(false); Landed?.Invoke(this); }
            }
        }

        public Meteor SpawnMeteor(Vector3 target, System.Action<Meteor> landed)
        {
            Meteor m = _meteorPool.Count > 0 ? _meteorPool.Dequeue() : null;
            if (m == null)
            {
                var go = new GameObject("Meteor");
                go.transform.SetParent(transform, false);
                m = go.AddComponent<Meteor>();
                m.Init();
            }
            m.Landed = x => { landed?.Invoke(x); _meteorPool.Enqueue(x); };
            Vector3 from = target + new Vector3(Random.Range(-8f, 8f), 28f, Random.Range(-8f, 8f));
            m.Fire(from, target);
            return m;
        }
    }
}
