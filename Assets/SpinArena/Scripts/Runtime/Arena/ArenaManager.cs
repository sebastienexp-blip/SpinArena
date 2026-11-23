using System.Collections.Generic;
using SpinArena.Util;
using UnityEngine;

namespace SpinArena.Arena
{
    /// <summary>
    /// Construit l'arène (stade en cuvette) : sol incurvé qui ramène les toupies vers le centre, rebord,
    /// murets avec ouvertures (éjections), éclairage et décor. Fournit les règles géométriques (sortie d'arène).
    /// </summary>
    public sealed class ArenaManager : MonoBehaviour
    {
        public ArenaSettings Settings { get; private set; }
        public float Radius => Settings.radius;
        public Color ThemeGlow { get; private set; }

        float _ledgeOuter;
        readonly List<Vector2> _pockets = new List<Vector2>(); // angles (degrés) début / fin des ouvertures
        Material _glowMat;
        Transform _ringLights;

        public void Build(ArenaSettings s)
        {
            Settings = s;
            ThemeGlow = new Color(0.25f, 0.85f, 1f);
            Color steel = new Color(0.10f, 0.12f, 0.16f);
            Color accent = new Color(0.20f, 0.35f, 1f);
            _ledgeOuter = s.radius + 1.1f;

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.20f, 0.26f, 0.42f);
            RenderSettings.ambientEquatorColor = new Color(0.10f, 0.12f, 0.20f);
            RenderSettings.ambientGroundColor = new Color(0.03f, 0.03f, 0.06f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.02f, 0.03f, 0.07f);
            RenderSettings.fogStartDistance = 45f;
            RenderSettings.fogEndDistance = 110f;

            BuildFloor(s, steel);
            BuildWalls(s, accent);
            BuildDecor(s, accent);
            BuildLights();
        }

        // ------------------------------------------------------------------ sol

        public float SurfaceHeight(float r)
        {
            float R = Settings.radius;
            if (r >= R) return Settings.rimHeight;
            float t = r / R;
            return Settings.rimHeight * t * t;
        }

        void BuildFloor(ArenaSettings s, Color steel)
        {
            const int seg = 120, rings = 34;
            float R = s.radius;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            verts.Add(new Vector3(0, 0, 0)); uvs.Add(new Vector2(0.5f, 0.5f));
            for (int i = 1; i <= rings; i++)
            {
                // Anneaux plus serrés près du bord pour une courbe lisse ; le dernier anneau forme le rebord plat.
                float r = i < rings ? R * Mathf.Pow(i / (float)(rings - 1), 0.85f) : _ledgeOuter;
                if (i == rings - 1) r = R;
                for (int j = 0; j < seg; j++)
                {
                    float a = j / (float)seg * Mathf.PI * 2f;
                    var p = new Vector3(Mathf.Cos(a) * r, SurfaceHeight(r), Mathf.Sin(a) * r);
                    verts.Add(p);
                    uvs.Add(new Vector2(p.x / (_ledgeOuter * 2f) + 0.5f, p.z / (_ledgeOuter * 2f) + 0.5f));
                }
            }
            for (int j = 0; j < seg; j++) { tris.Add(0); tris.Add(1 + (j + 1) % seg); tris.Add(1 + j); }
            for (int i = 0; i < rings - 1; i++)
            for (int j = 0; j < seg; j++)
            {
                int a = 1 + i * seg + j, b = 1 + i * seg + (j + 1) % seg;
                int c = a + seg, d = b + seg;
                tris.Add(a); tris.Add(b); tris.Add(d);
                tris.Add(a); tris.Add(d); tris.Add(c);
            }
            var mesh = new Mesh { name = "ArenaFloor" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject("Floor");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            var mat = MaterialLibrary.Metal(Color.white, 0.85f, 0.72f, ThemeGlow * 1.8f);
            Texture2D albedo, emission;
            MakeFloorTextures(steel, out albedo, out emission);
            MaterialLibrary.SetTexture(mat, albedo);
            if (mat.HasProperty("_EmissionMap")) mat.SetTexture("_EmissionMap", emission);
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var col = go.AddComponent<MeshCollider>();
            col.sharedMesh = mesh;
            col.SetPhysics("Floor", 0.04f, 0.05f, true, false);
        }

        void MakeFloorTextures(Color steel, out Texture2D albedo, out Texture2D emission)
        {
            const int n = 1024;
            albedo = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "FloorAlbedo", wrapMode = TextureWrapMode.Clamp, anisoLevel = 4 };
            emission = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "FloorEmission", wrapMode = TextureWrapMode.Clamp };
            var a = new Color32[n * n];
            var e = new Color32[n * n];
            float ledgeR = Settings.radius / _ledgeOuter; // rayon (0..1) du bord de la cuvette dans la texture
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);
                float ang = Mathf.Atan2(v, u);
                // Plaques métalliques : variation par secteur et par anneau, micro-bruit brossé.
                float sector = Mathf.Floor((ang + Mathf.PI) / (Mathf.PI * 2f) * 24f);
                float band = Mathf.Floor(r * 9f);
                float plate = Mathf.Repeat(Mathf.Sin(sector * 12.9898f + band * 78.233f) * 43758.5453f, 1f);
                float brushed = Mathf.Repeat(Mathf.Sin((r * 900f) + plate * 10f) * 1000f, 1f) * 0.04f;
                Color c = steel * (0.85f + plate * 0.3f) + new Color(brushed, brushed, brushed);
                // Rainures sombres entre les plaques.
                float seamR = Mathf.Abs(Mathf.Repeat(r * 9f, 1f) - 0.5f);
                float seamA = Mathf.Abs(Mathf.Repeat((ang + Mathf.PI) / (Mathf.PI * 2f) * 24f, 1f) - 0.5f);
                if (seamR > 0.48f || seamA > 0.485f) c *= 0.45f;
                // Lignes lumineuses : cercles de visée, rayons, anneau du bord, logo central.
                float glow = 0f;
                glow = Mathf.Max(glow, Line(r, 0.12f, 0.004f));
                glow = Mathf.Max(glow, Line(r, ledgeR * 0.5f, 0.003f) * 0.7f);
                glow = Mathf.Max(glow, Line(r, ledgeR * 0.82f, 0.003f) * 0.8f);
                glow = Mathf.Max(glow, Line(r, ledgeR - 0.008f, 0.006f));
                float spoke = Mathf.Abs(Mathf.Repeat((ang + Mathf.PI) / (Mathf.PI * 2f) * 6f, 1f) - 0.5f);
                if (r > 0.14f && r < ledgeR - 0.02f) glow = Mathf.Max(glow, Mathf.Clamp01(1f - Mathf.Abs(spoke - 0.5f) / 0.006f) * 0.6f);
                // Étoile à 6 branches au centre (identité visuelle SPIN ARENA).
                float star = 0.09f * (0.65f + 0.35f * Mathf.Cos(ang * 6f));
                if (r < star) glow = Mathf.Max(glow, 0.9f);
                if (r > ledgeR) { c = steel * 0.6f; }
                a[y * n + x] = c;
                byte g = (byte)(Mathf.Clamp01(glow) * 255f);
                e[y * n + x] = new Color32(g, g, g, 255);
            }
            albedo.SetPixels32(a); albedo.Apply(true, true);
            emission.SetPixels32(e); emission.Apply(true, true);
        }

        static float Line(float r, float center, float width) => Mathf.Clamp01(1f - Mathf.Abs(r - center) / width);

        // ------------------------------------------------------------------ murets et ouvertures

        void BuildWalls(ArenaSettings s, Color accent)
        {
            _pockets.Clear();
            float step = 360f / Mathf.Max(1, s.pocketCount);
            for (int i = 0; i < s.pocketCount; i++)
            {
                float c = 45f + i * step;
                _pockets.Add(new Vector2(c - s.pocketDegrees * 0.5f, c + s.pocketDegrees * 0.5f));
            }
            var wallMat = MaterialLibrary.Metal(new Color(0.18f, 0.2f, 0.26f), 0.9f, 0.65f, Color.black);
            _glowMat = MaterialLibrary.Metal(Color.black, 0f, 0.5f, ThemeGlow * 3f);
            float ri = s.radius + 0.75f, ro = s.radius + 1.1f, y0 = s.rimHeight;
            for (int i = 0; i < _pockets.Count; i++)
            {
                float a0 = _pockets[i].y, a1 = _pockets[(i + 1) % _pockets.Count].x + (i + 1 == _pockets.Count ? 360f : 0f);
                var wall = ArcMesh(ri, ro, y0 - 0.2f, y0 + s.wallHeight, a0, a1, 24);
                var go = new GameObject("Wall" + i);
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = wall;
                go.AddComponent<MeshRenderer>().sharedMaterial = wallMat;
                // Bande lumineuse sur le dessus.
                var strip = new GameObject("Glow" + i);
                strip.transform.SetParent(go.transform, false);
                strip.AddComponent<MeshFilter>().sharedMesh = ArcMesh(ri - 0.01f, ri + 0.08f, y0 + s.wallHeight, y0 + s.wallHeight + 0.04f, a0, a1, 24);
                strip.AddComponent<MeshRenderer>().sharedMaterial = _glowMat;
                // Collisions : boîtes le long de l'arc.
                int boxes = Mathf.CeilToInt((a1 - a0) / 5f);
                for (int b = 0; b < boxes; b++)
                {
                    float am = Mathf.Lerp(a0, a1, (b + 0.5f) / boxes) * Mathf.Deg2Rad;
                    float len = (a1 - a0) / boxes * Mathf.Deg2Rad * ((ri + ro) * 0.5f) + 0.05f;
                    var box = new GameObject("C");
                    box.transform.SetParent(go.transform, false);
                    box.transform.localPosition = new Vector3(Mathf.Cos(am) * (ri + ro) * 0.5f, y0 + s.wallHeight * 0.5f - 0.1f, Mathf.Sin(am) * (ri + ro) * 0.5f);
                    box.transform.localRotation = Quaternion.Euler(0f, -am * Mathf.Rad2Deg + 90f, 0f);
                    var bc = box.AddComponent<BoxCollider>();
                    bc.size = new Vector3(len, s.wallHeight + 0.2f, ro - ri);
                    bc.SetPhysics("Wall", 0.05f, 0.55f, true, true);
                }
            }
            // Repères lumineux rouges aux ouvertures (zones d'éjection).
            var danger = MaterialLibrary.Metal(Color.black, 0f, 0.5f, new Color(1f, 0.15f, 0.1f) * 3f);
            foreach (var p in _pockets)
            {
                var g = new GameObject("PocketMark");
                g.transform.SetParent(transform, false);
                g.AddComponent<MeshFilter>().sharedMesh = ArcMesh(s.radius + 0.1f, s.radius + 0.35f, s.rimHeight + 0.01f, s.rimHeight + 0.03f, p.x, p.y, 8);
                g.AddComponent<MeshRenderer>().sharedMaterial = danger;
            }
        }

        public static Mesh ArcMesh(float ri, float ro, float y0, float y1, float a0Deg, float a1Deg, int seg)
        {
            var v = new List<Vector3>();
            var t = new List<int>();
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(c); v.Add(d);
                t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3);
            }
            Vector3 P(float r, float y, float a) => new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
            for (int i = 0; i < seg; i++)
            {
                float a = Mathf.Lerp(a0Deg, a1Deg, i / (float)seg) * Mathf.Deg2Rad;
                float b = Mathf.Lerp(a0Deg, a1Deg, (i + 1) / (float)seg) * Mathf.Deg2Rad;
                Quad(P(ri, y1, a), P(ri, y1, b), P(ro, y1, b), P(ro, y1, a));     // dessus
                Quad(P(ri, y0, a), P(ri, y0, b), P(ri, y1, b), P(ri, y1, a));     // face intérieure
                Quad(P(ro, y1, a), P(ro, y1, b), P(ro, y0, b), P(ro, y0, a));     // face extérieure
            }
            float s0 = a0Deg * Mathf.Deg2Rad, s1 = a1Deg * Mathf.Deg2Rad;
            Quad(P(ro, y0, s0), P(ri, y0, s0), P(ri, y1, s0), P(ro, y1, s0));
            Quad(P(ri, y0, s1), P(ro, y0, s1), P(ro, y1, s1), P(ri, y1, s1));
            var m = new Mesh { name = "Arc" };
            m.SetVertices(v);
            m.SetTriangles(t, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        // ------------------------------------------------------------------ décor

        void BuildDecor(ArenaSettings s, Color accent)
        {
            // Socle extérieur de l'arène.
            var baseMat = MaterialLibrary.Metal(new Color(0.07f, 0.08f, 0.11f), 0.8f, 0.5f, Color.black);
            var skirt = new GameObject("Skirt");
            skirt.transform.SetParent(transform, false);
            skirt.AddComponent<MeshFilter>().sharedMesh = ArcMesh(_ledgeOuter - 0.05f, _ledgeOuter + 0.4f, -6f, s.rimHeight - 0.01f, 0f, 360f, 96);
            skirt.AddComponent<MeshRenderer>().sharedMaterial = baseMat;

            // Anneau lumineux flottant au-dessus de l'arène.
            _ringLights = new GameObject("HaloRing").transform;
            _ringLights.SetParent(transform, false);
            _ringLights.localPosition = new Vector3(0, 9f, 0);
            var haloMat = MaterialLibrary.Metal(Color.black, 0f, 0.5f, accent * 2.5f);
            for (int i = 0; i < 3; i++)
            {
                var ring = new GameObject("Ring" + i);
                ring.transform.SetParent(_ringLights, false);
                float r = s.radius + 4f + i * 1.2f;
                ring.AddComponent<MeshFilter>().sharedMesh = ArcMesh(r, r + 0.12f, i * 0.6f, i * 0.6f + 0.12f, 0f, 360f, 128);
                ring.AddComponent<MeshRenderer>().sharedMaterial = i == 1 ? _glowMat : haloMat;
            }

            // Pylônes électriques autour du stade.
            var pylonMat = MaterialLibrary.Metal(new Color(0.12f, 0.13f, 0.17f), 0.9f, 0.6f, Color.black);
            for (int i = 0; i < 8; i++)
            {
                float a = (i + 0.5f) / 8f * Mathf.PI * 2f;
                var pos = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (s.radius + 9f);
                var pylon = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(pylon.GetComponent<Collider>());
                pylon.name = "Pylon" + i;
                pylon.transform.SetParent(transform, false);
                pylon.transform.localPosition = pos + Vector3.up * 2f;
                pylon.transform.localScale = new Vector3(1.1f, 8f, 1.1f);
                pylon.GetComponent<MeshRenderer>().sharedMaterial = pylonMat;
                var core = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(core.GetComponent<Collider>());
                core.transform.SetParent(transform, false);
                core.transform.localPosition = pos + Vector3.up * 2.5f;
                core.transform.localScale = new Vector3(0.35f, 8.2f, 0.35f);
                core.GetComponent<MeshRenderer>().sharedMaterial = _glowMat;
            }

            // Abîme lumineux sous l'arène (ce qui est éjecté tombe dedans).
            var abyss = new GameObject("Abyss");
            abyss.transform.SetParent(transform, false);
            abyss.transform.localPosition = new Vector3(0, -14f, 0);
            abyss.AddComponent<MeshFilter>().sharedMesh = ArcMesh(0.01f, 90f, 0f, 0.01f, 0f, 360f, 64);
            abyss.AddComponent<MeshRenderer>().sharedMaterial = MaterialLibrary.Metal(new Color(0.01f, 0.02f, 0.05f), 0f, 0.2f, accent * 0.15f);
        }

        void BuildLights()
        {
            var sun = new GameObject("KeyLight").AddComponent<Light>();
            sun.transform.SetParent(transform, false);
            sun.type = LightType.Directional;
            sun.color = new Color(0.85f, 0.9f, 1f);
            sun.intensity = 1.3f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.7f;
            sun.transform.rotation = Quaternion.Euler(58f, -30f, 0f);
            var rim = new GameObject("RimLight").AddComponent<Light>();
            rim.transform.SetParent(transform, false);
            rim.type = LightType.Directional;
            rim.color = ThemeGlow;
            rim.intensity = 0.45f;
            rim.shadows = LightShadows.None;
            rim.transform.rotation = Quaternion.Euler(25f, 150f, 0f);
        }

        void Update()
        {
            if (_ringLights != null) _ringLights.Rotate(0f, 6f * Time.deltaTime, 0f, Space.World);
        }

        // ------------------------------------------------------------------ règles

        /// <summary>Vrai si la position est hors de l'arène (tombée ou projetée au-delà du rebord).</summary>
        public bool IsOut(Vector3 p)
        {
            float r = new Vector2(p.x, p.z).magnitude;
            return p.y < Settings.rimHeight - 2.5f && r > Settings.radius + 0.5f || r > _ledgeOuter + 2.5f || p.y < -3f;
        }

        public bool IsInPocket(float angleDeg)
        {
            angleDeg = Mathf.Repeat(angleDeg, 360f);
            foreach (var p in _pockets)
                if (Mathf.Repeat(angleDeg - p.x, 360f) <= p.y - p.x) return true;
            return false;
        }

        /// <summary>Position de départ n°i sur un cercle (lancement).</summary>
        public Vector3 SpawnPoint(int i, int count, float radiusFactor)
        {
            float a = i / (float)Mathf.Max(1, count) * Mathf.PI * 2f + 0.3f;
            float r = Settings.radius * radiusFactor;
            return new Vector3(Mathf.Cos(a) * r, SurfaceHeight(r) + 0.05f, Mathf.Sin(a) * r);
        }
    }
}
