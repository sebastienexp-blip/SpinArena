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
            // Effets à l'échelle des toupies : plus elles sont grosses, plus les gerbes et explosions sont grandes.
            float k = FxScale;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin * k, speedMax * k);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin * k, sizeMax * k);
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

        static float FxScale => Mathf.Clamp(GameConfig.Current.tuning.topScale / 1.55f, 1f, 2f);

        void LateUpdate()
        {
            _budgetThisFrame = 0;
            UpdateBolts();
        }

        // ------------------------------------------------------------------ éclairs

        sealed class Bolt
        {
            public LineRenderer line, glow;
            public Vector3 from, to;
            public float until, nextJitter, width, born, amp = 0.12f;
            public Color color;
            public bool sky;      // foudre du ciel : forme stable, clignotements, halo
            public int points;
        }
        readonly List<Bolt> _bolts = new List<Bolt>();
        const int BoltPoints = 14;

        /// <summary>Éclair crépitant entre deux points (quelques dixièmes de seconde), avec une gerbe à l'arrivée.</summary>
        Bolt GetBolt(int points)
        {
            Bolt b = null;
            foreach (var x in _bolts) if (!x.line.gameObject.activeSelf) { b = x; break; }
            if (b == null)
            {
                if (_bolts.Count >= 60) return null;
                var go = new GameObject("Bolt");
                go.transform.SetParent(transform, false);
                b = new Bolt { line = NewLine(go), glow = NewLine(new GameObject("Glow")) };
                b.glow.transform.SetParent(go.transform, false);
                _bolts.Add(b);
            }
            b.points = points;
            b.line.positionCount = points;
            b.glow.positionCount = points;
            return b;
        }

        static LineRenderer NewLine(GameObject go)
        {
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.numCapVertices = 2;
            lr.numCornerVertices = 1;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.sharedMaterial = MaterialLibrary.Additive(Color.white, MaterialLibrary.SoftDot);
            return lr;
        }

        public void Lightning(Vector3 from, Vector3 to, Color color, float seconds = 0.35f, float width = 0.18f)
        {
            if (GameConfig.Current.display.particles <= 0.05f) return;
            var b = GetBolt(BoltPoints);
            if (b == null) return;
            b.from = from; b.to = to; b.color = color; b.sky = false; b.amp = 0.12f;
            b.born = Time.time; b.until = Time.time + seconds; b.nextJitter = 0f; b.width = width * FxScale;
            b.line.gameObject.SetActive(true);
            Jitter(b);
            Emit(_sparks, to, 14, Color.Lerp(color, Color.white, 0.5f) * 2f);
            Emit(_glow, to, 1, color * 1.6f);
        }

        /// <summary>
        /// Foudre qui tombe du ciel sur un point : éclair épais avec halo, ramifications, clignotements
        /// comme un vrai coup de tonnerre. Les dégâts et l'impact au sol sont gérés par l'appelant.
        /// </summary>
        public void SkyBolt(Vector3 target, Color color)
        {
            float k = FxScale;
            Vector3 top = target + new Vector3(Random.Range(-4f, 4f), 36f * Mathf.Max(1f, k * 0.8f), Random.Range(-4f, 4f));
            var b = GetBolt(30);
            if (b == null) return;
            b.from = top; b.to = target; b.color = color; b.sky = true; b.amp = 0.07f;
            b.born = Time.time; b.until = Time.time + 0.55f; b.nextJitter = float.MaxValue; b.width = 0.55f * k;
            b.line.gameObject.SetActive(true);
            Jitter(b);
            // Ramifications : partent du tronc et descendent en biais.
            int branches = Random.Range(3, 6);
            for (int i = 0; i < branches; i++)
            {
                int idx = Random.Range(4, 20);
                Vector3 start = b.line.GetPosition(idx);
                float len = (top - target).magnitude * Random.Range(0.12f, 0.28f);
                Vector3 dir = (Vector3.down + Random.insideUnitSphere.Flat() * 1.3f).normalized;
                var br = GetBolt(12);
                if (br == null) break;
                br.from = start; br.to = start + dir * len; br.color = color; br.sky = true; br.amp = 0.16f;
                br.born = Time.time; br.until = Time.time + Random.Range(0.18f, 0.35f); br.nextJitter = float.MaxValue; br.width = 0.22f * k;
                br.line.gameObject.SetActive(true);
                Jitter(br);
            }
        }

        void Jitter(Bolt b)
        {
            Vector3 d = b.to - b.from;
            float len = d.magnitude;
            Vector3 side = Vector3.Cross(d.normalized, Vector3.up);
            if (side.sqrMagnitude < 0.01f) side = Vector3.right;
            side.Normalize();
            Vector3 side2 = Vector3.Cross(d.normalized, side).normalized;
            // Déviation qui s'accumule (zigzag naturel) plutôt que du bruit pur.
            Vector3 drift = Vector3.zero;
            for (int i = 0; i < b.points; i++)
            {
                float t = i / (float)(b.points - 1);
                float env = Mathf.Sin(t * Mathf.PI);
                drift += (side * Random.Range(-1f, 1f) + side2 * Random.Range(-1f, 1f)) * len * b.amp * 0.35f;
                drift *= 0.8f;
                Vector3 p = Vector3.Lerp(b.from, b.to, t) + drift * env * 2.2f;
                if (i == 0) p = b.from;
                if (i == b.points - 1) p = b.to;
                b.line.SetPosition(i, p);
                b.glow.SetPosition(i, p);
            }
            float w = b.width * Random.Range(0.8f, 1.2f);
            b.line.widthCurve = new AnimationCurve(new Keyframe(0f, w * (b.sky ? 0.5f : 1f)), new Keyframe(0.5f, w * 1.2f), new Keyframe(1f, w * (b.sky ? 1.4f : 0.6f)));
            b.glow.widthCurve = new AnimationCurve(new Keyframe(0f, w * 3f), new Keyframe(1f, w * 4.5f));
            var c = Color.Lerp(b.color, Color.white, b.sky ? 0.6f : 0.35f) * (b.sky ? 3f : 2.2f);
            b.line.startColor = c; b.line.endColor = c;
            var g = b.color; g.a = b.sky ? 0.35f : 0.18f;
            b.glow.startColor = g; b.glow.endColor = g;
            if (!b.sky) b.nextJitter = Time.time + 0.045f;
        }

        void UpdateBolts()
        {
            float now = Time.time;
            for (int i = 0; i < _bolts.Count; i++)
            {
                var b = _bolts[i];
                if (!b.line.gameObject.activeSelf) continue;
                if (now >= b.until) { b.line.gameObject.SetActive(false); continue; }
                if (b.sky)
                {
                    // Clignotements d'un vrai éclair : allumé, éteint, rallumé (nouvelle forme), puis s'éteint en douceur.
                    float t = now - b.born;
                    bool on = t < 0.07f || (t > 0.11f && t < 0.19f) || t > 0.23f;
                    bool wasOn = b.line.enabled;
                    b.line.enabled = on; b.glow.enabled = on;
                    if (on && !wasOn && t > 0.1f) Jitter(b);
                    if (t > 0.23f)
                    {
                        float fade = Mathf.Clamp01(1f - (t - 0.23f) / Mathf.Max(0.05f, b.until - b.born - 0.23f));
                        b.line.widthMultiplier = fade; b.glow.widthMultiplier = fade;
                    }
                    else { b.line.widthMultiplier = 1f; b.glow.widthMultiplier = 1f; }
                }
                else
                {
                    b.line.enabled = true; b.glow.enabled = true;
                    b.line.widthMultiplier = 1f; b.glow.widthMultiplier = 1f;
                    if (now >= b.nextJitter) Jitter(b);
                }
            }
        }

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
            // Grosse explosion : éclair blanc, débris, gerbe d'étincelles et double onde au sol.
            Emit(_glow, pos, 3, color * 2.4f);
            Emit(_glow, pos + Vector3.up * 0.3f, 1, Color.white * 1.6f);
            Emit(_debris, pos, Mathf.RoundToInt(60 * scale), Color.Lerp(color, Color.white, 0.3f));
            Emit(_sparks, pos, Mathf.RoundToInt(80 * scale), color * 2f);
            Emit(_sparks, pos, Mathf.RoundToInt(30 * scale), new Color(1f, 0.75f, 0.35f) * 2f);
            Emit(_ring, pos + Vector3.up * 0.1f, 1, color * 1.5f);
            Emit(_ring, pos + Vector3.up * 0.12f, 1, Color.white);
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

        // Tornade réaliste : trois couches d'entonnoir (poussière opaque dehors, vent clair dedans) qui tournent à
        // des vitesses différentes, sommet qui oscille, nuage d'orage au-dessus, poussière et débris à la base.
        Transform _funnel;
        readonly Transform[] _layers = new Transform[3];
        readonly Material[] _layerMats = new Material[3];
        float _cloudBolt;

        public void ShowTornado(Vector3 center, float radius, float alpha, float phase)
        {
            if (_funnel == null) _funnel = BuildFunnel();
            _funnel.gameObject.SetActive(true);
            _funnel.position = center;
            float k = FxScale;
            // Le sommet décrit un petit cercle : la colonne se tord et ondule.
            float sway = 9f * Mathf.Sin(phase * 0.35f);
            _funnel.rotation = Quaternion.Euler(sway, phase * 12f, 0f);
            _funnel.localScale = new Vector3(radius, k, radius);
            float[] speeds = { -95f, -150f, -210f };
            float[] alphas = { 0.75f, 0.55f, 0.35f };
            for (int i = 0; i < 3; i++)
            {
                _layers[i].localRotation = Quaternion.Euler(0f, phase * speeds[i], 0f);
                var c = i < 2 ? new Color(0.58f, 0.54f, 0.5f, alphas[i] * alpha) : new Color(0.75f, 0.85f, 1f, alphas[i] * alpha);
                MaterialLibrary.SetColor(_layerMats[i], c);
                _layerMats[i].SetTextureOffset("_BaseMap", new Vector2(phase * (0.15f + i * 0.1f), -phase * (0.45f + i * 0.2f)));
            }
            var sfx = SpectacleFx.Instance;
            if (sfx == null || alpha < 0.2f) return;
            // Base : poussière qui tourbillonne et s'échappe, débris arrachés du sol.
            if (Random.value < 0.7f) sfx.DustRing(center, 3, 4f);
            float top = 10f * k;
            // Nuage d'orage tournant au sommet.
            if (Random.value < 0.6f)
            {
                float a = Random.value * Mathf.PI * 2f, rr = radius * Random.Range(1.2f, 2.6f);
                sfx.SmokePuff(center + new Vector3(Mathf.Cos(a) * rr, top + Random.Range(-0.5f, 1f), Mathf.Sin(a) * rr), 1);
            }
            // Éclairs dans le nuage, de temps en temps.
            _cloudBolt -= Time.deltaTime;
            if (_cloudBolt <= 0f)
            {
                _cloudBolt = Random.Range(0.5f, 1.3f);
                float a = Random.value * Mathf.PI * 2f, rr = radius * Random.Range(0.8f, 2f);
                Vector3 from = center + new Vector3(Mathf.Cos(a) * rr, top, Mathf.Sin(a) * rr);
                Vector3 to = from + new Vector3(Random.Range(-2f, 2f), -Random.Range(2f, 4f) * k, Random.Range(-2f, 2f));
                Lightning(from, to, new Color(0.7f, 0.85f, 1f), 0.25f, 0.25f);
                sfx.LightFlash(from, new Color(0.75f, 0.85f, 1f), 10f, 18f, 0.25f);
            }
        }

        public void HideTornado() { if (_funnel != null) _funnel.gameObject.SetActive(false); }

        Transform BuildFunnel()
        {
            var root = new GameObject("Tornado").transform;
            root.SetParent(transform, false);
            var tex = WindTex();
            float[] scale = { 1f, 0.78f, 0.55f };
            for (int i = 0; i < 3; i++)
            {
                var go = new GameObject("Layer" + i);
                go.transform.SetParent(root, false);
                go.transform.localScale = new Vector3(scale[i], 1f, scale[i]);
                go.AddComponent<MeshFilter>().sharedMesh = FunnelMesh(i * 1.7f);
                var mr = go.AddComponent<MeshRenderer>();
                // Couches extérieures : poussière opaque (fondu normal) ; couche intérieure : vent clair (additif).
                var mat = i < 2 ? new Material(MaterialLibrary.ParticleAlpha(Color.white, tex)) : new Material(MaterialLibrary.Additive(Color.white, tex));
                MaterialLibrary.SetTexture(mat, tex);
                if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                _layers[i] = go.transform;
                _layerMats[i] = mat;
            }
            root.gameObject.SetActive(false);
            return root;
        }

        /// <summary>Entonnoir de 10 m : très étroit au sol, large en haut, légèrement courbé et irrégulier.</summary>
        static Mesh FunnelMesh(float seed)
        {
            const int seg = 48, rings = 24;
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var tri = new List<int>();
            for (int i = 0; i <= rings; i++)
            {
                float h = i / (float)rings;
                float r = Mathf.Lerp(0.12f, 1.25f, Mathf.Pow(h, 1.7f));
                Vector3 bend = new Vector3(Mathf.Sin(h * 2.2f + seed) * 0.35f, 0f, Mathf.Cos(h * 1.7f + seed) * 0.25f) * h;
                for (int j = 0; j <= seg; j++)
                {
                    float a = j / (float)seg * Mathf.PI * 2f;
                    float wob = 1f + 0.12f * Mathf.Sin(a * 3f + h * 9f + seed) + 0.06f * Mathf.Sin(a * 7f - h * 13f);
                    v.Add(new Vector3(Mathf.Cos(a) * r * wob, h * 10f, Mathf.Sin(a) * r * wob) + bend);
                    uv.Add(new Vector2(j / (float)seg * 3f, h * 2.5f));
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
            // Transparence plus forte en bas et en haut (bords fondus).
            var cols = new Color[v.Count];
            for (int i = 0; i <= rings; i++)
            {
                float h = i / (float)rings;
                float a = Mathf.Clamp01(h * 6f) * Mathf.Clamp01((1f - h) * 3f);
                for (int j = 0; j <= seg; j++) cols[i * (seg + 1) + j] = new Color(1f, 1f, 1f, a);
            }
            mesh.colors = cols;
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        static Texture2D WindTex()
        {
            // Traînées de vent en spirale, bruitées : aspect de poussière qui tourbillonne.
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, name = "Wind" };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = x / (float)n, w = y / (float)n;
                float s = Mathf.Repeat(u * 5f + w * 2.2f + Mathf.PerlinNoise(u * 6f, w * 6f) * 0.8f, 1f);
                float band = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(s - 0.5f) * 2.4f), 1.6f);
                float noise = Mathf.PerlinNoise(u * 18f + 3f, w * 9f) * 0.5f + Mathf.PerlinNoise(u * 40f, w * 30f) * 0.3f;
                float a = Mathf.Clamp01(band * 0.75f + noise * 0.45f - 0.15f);
                byte lum = (byte)(200 + noise * 55f);
                px[y * n + x] = new Color32(lum, lum, lum, (byte)(a * 255f));
            }
            tex.SetPixels32(px); tex.Apply(true, true);
            return tex;
        }

        /// <summary>Tourbillon de poussière et de débris le long de l'entonnoir (à appeler à chaque image).</summary>
        public void Vortex(Vector3 center, float radius, float phase, Color color)
        {
            int n = Mathf.RoundToInt(260f * Time.deltaTime * Mathf.Clamp(GameConfig.Current.display.particles, 0f, 2f));
            n = Mathf.Clamp(n, 1, 30);
            n = Mathf.Min(n, MaxParticlesPerFrame - _budgetThisFrame);
            if (n <= 0) return;
            _budgetThisFrame += n;
            float k = FxScale;
            for (int i = 0; i < n; i++)
            {
                float h = Random.value * 9f * k;
                float rr = Mathf.Lerp(radius * 0.3f, radius * 1.3f, Mathf.Pow(h / (9f * k), 1.5f));
                float a = phase + Random.value * Mathf.PI * 2f;
                Vector3 p = center + new Vector3(Mathf.Cos(a) * rr, h, Mathf.Sin(a) * rr);
                Vector3 tangent = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
                // Débris (morceaux sombres) projetés en spirale.
                Vector3 vel = tangent * (9f + rr * 2.5f) + Vector3.up * 4f * k;
                if (i % 3 == 0) SpectacleFx.Instance?.Debris(p, vel, Random.Range(0.15f, 0.4f) * k);
                else SpectacleFx.Instance?.DustAt(p, vel, Random.Range(0.8f, 1.8f) * k);
            }
            if (Random.value < 0.25f) Emit(_sparks, center + Vector3.up * 0.3f, 2, new Color(0.8f, 0.9f, 1f));
        }

        // ------------------------------------------------------------------ météores (événement légendaire)

        public sealed class Meteor : MonoBehaviour
        {
            public Vector3 target;
            public float speed = 26f;
            public System.Action<Meteor> Landed;
            public Color color;
            public bool big;
            TrailRenderer _trail;
            Transform _rock;
            Light _light;
            Vector3 _spinAxis;

            public void Init()
            {
                if (_trail != null) return;
                // Rocher irrégulier : sphère déformée, croûte sombre veinée de lave.
                var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(body.GetComponent<Collider>());
                body.transform.SetParent(transform, false);
                var mf = body.GetComponent<MeshFilter>();
                mf.sharedMesh = RockMesh;
                body.GetComponent<MeshRenderer>().sharedMaterial = MaterialLibrary.Metal(new Color(0.12f, 0.08f, 0.07f), 0f, 0.25f, new Color(1f, 0.35f, 0.05f) * 2.2f);
                _rock = body.transform;
                _trail = gameObject.AddComponent<TrailRenderer>();
                _trail.time = 0.8f;
                _trail.minVertexDistance = 0.2f;
                _trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _trail.sharedMaterial = MaterialLibrary.Additive(Color.white, MaterialLibrary.SoftDot);
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.95f, 0.7f), 0f), new GradientColorKey(new Color(1f, 0.5f, 0.1f), 0.3f), new GradientColorKey(new Color(0.6f, 0.1f, 0.02f), 1f) },
                          new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.7f, 0.4f), new GradientAlphaKey(0f, 1f) });
                _trail.colorGradient = g;
                _light = gameObject.AddComponent<Light>();
                _light.type = LightType.Point;
                _light.color = new Color(1f, 0.55f, 0.2f);
                _light.shadows = LightShadows.None;
            }

            static Mesh _rockMesh;
            static Mesh RockMesh
            {
                get
                {
                    if (_rockMesh != null) return _rockMesh;
                    var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    var src = tmp.GetComponent<MeshFilter>().sharedMesh;
                    Destroy(tmp);
                    var m = Instantiate(src);
                    var v = m.vertices;
                    for (int i = 0; i < v.Length; i++)
                    {
                        Vector3 n = v[i].normalized;
                        float bump = 0.8f + Mathf.PerlinNoise(n.x * 2.3f + 3f, n.y * 2.3f + n.z * 1.7f) * 0.45f;
                        v[i] = n * 0.5f * bump;
                    }
                    m.vertices = v; m.RecalculateNormals(); m.RecalculateBounds();
                    m.name = "Rock";
                    _rockMesh = m;
                    return m;
                }
            }

            public void Fire(Vector3 from, Vector3 to, bool isBig)
            {
                big = isBig;
                float k = FxScale;
                transform.position = from;
                target = to;
                speed = big ? 24f : 30f;
                _rock.localScale = Vector3.one * (big ? 1.5f : 0.7f) * k;
                _trail.widthCurve = new AnimationCurve(new Keyframe(0f, (big ? 1.8f : 0.8f) * k), new Keyframe(1f, 0f));
                _light.intensity = big ? 6f : 2.5f;
                _light.range = (big ? 12f : 6f) * k;
                _spinAxis = Random.onUnitSphere;
                gameObject.SetActive(true);
                _trail.Clear();
            }

            void Update()
            {
                transform.position = Vector3.MoveTowards(transform.position, target, speed * Time.deltaTime);
                _rock.Rotate(_spinAxis, 360f * Time.deltaTime, Space.World);
                var sfx = SpectacleFx.Instance;
                if (sfx != null)
                {
                    // Flammes et fumée qui s'arrachent du rocher.
                    int n = Mathf.Clamp(Mathf.RoundToInt((big ? 90f : 35f) * Time.deltaTime), 1, 4);
                    sfx.FireBurst(transform.position, n, Color.white);
                    if (big && Random.value < 0.6f) sfx.SmokePuff(transform.position, 1);
                    if (Random.value < 0.5f) sfx.Embers(transform.position, 1);
                }
                _light.intensity *= 1f + 0.08f * Mathf.Sin(Time.time * 40f);
                if ((transform.position - target).sqrMagnitude < 0.01f) { gameObject.SetActive(false); Landed?.Invoke(this); }
            }
        }

        public Meteor SpawnMeteor(Vector3 target, System.Action<Meteor> landed, bool big = true)
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
            float k = FxScale;
            Vector3 from = target + new Vector3(Random.Range(-10f, 10f), 30f * Mathf.Max(1f, k * 0.8f), Random.Range(-10f, 10f));
            m.Fire(from, target, big);
            // Cercle d'alerte rouge qui se resserre jusqu'à l'impact.
            if (big && SpectacleFx.Instance != null)
            {
                float eta = (from - target).magnitude / m.speed;
                SpectacleFx.Instance.GroundGlow(target, 4.5f, eta, new Color(1f, 0.25f, 0.1f, 0.9f), true, 1.6f);
            }
            return m;
        }
    }
}
