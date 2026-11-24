using System.Collections.Generic;
using SpinArena.Util;
using UnityEngine;

namespace SpinArena.Arena
{
    /// <summary>
    /// Ambiance générale : carte de reflets (les métaux reflètent des néons et un ciel nocturne au lieu d'une couleur
    /// unie), fond de ciel avec nébuleuse et étoiles, tribunes illuminées, projecteurs qui balaient l'arène.
    /// Tout est généré par code.
    /// </summary>
    public static class EnvironmentFactory
    {
        // ------------------------------------------------------------------ reflets

        /// <summary>Cubemap de reflets : dégradé nocturne, horizon lumineux et bandes de néons nettes.</summary>
        public static void ApplyReflections(Color glow, Color accent)
        {
            const int n = 128;
            var cube = new Cubemap(n, TextureFormat.RGBA32, true) { name = "SpinReflections" };
            var px = new Color[n * n];
            foreach (CubemapFace face in System.Enum.GetValues(typeof(CubemapFace)))
            {
                if (face == CubemapFace.Unknown) continue;
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    Vector3 d = Direction(face, (x + 0.5f) / n * 2f - 1f, (y + 0.5f) / n * 2f - 1f);
                    px[y * n + x] = Sky(d, glow, accent, true);
                }
                cube.SetPixels(px, face);
            }
            cube.Apply(true);
#if UNITY_2022_1_OR_NEWER
            RenderSettings.customReflectionTexture = cube;
#else
            RenderSettings.customReflection = cube;
#endif
            RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Custom;
            RenderSettings.reflectionIntensity = 1f;
        }

        static Vector3 Direction(CubemapFace f, float u, float v)
        {
            // Convention des cubemaps : v vers le bas.
            switch (f)
            {
                case CubemapFace.PositiveX: return new Vector3(1f, -v, -u).normalized;
                case CubemapFace.NegativeX: return new Vector3(-1f, -v, u).normalized;
                case CubemapFace.PositiveY: return new Vector3(u, 1f, v).normalized;
                case CubemapFace.NegativeY: return new Vector3(u, -1f, -v).normalized;
                case CubemapFace.PositiveZ: return new Vector3(u, -v, 1f).normalized;
                default: return new Vector3(-u, -v, -1f).normalized;
            }
        }

        /// <summary>Couleur du ciel dans une direction (utilisé pour les reflets et pour le fond).</summary>
        static Color Sky(Vector3 d, Color glow, Color accent, bool forReflection)
        {
            float h = d.y;
            Color deep = new Color(0.01f, 0.015f, 0.05f);
            Color mid = new Color(0.05f, 0.04f, 0.16f);
            Color horizon = Color.Lerp(accent, glow, 0.5f) * 0.55f;
            Color c = h > 0f ? Color.Lerp(horizon, Color.Lerp(mid, deep, h), Mathf.Pow(Mathf.Clamp01(h * 2.5f), 0.6f))
                             : Color.Lerp(horizon, new Color(0.01f, 0.01f, 0.03f), Mathf.Clamp01(-h * 4f));
            float az = Mathf.Atan2(d.z, d.x);
            // Nébuleuse colorée.
            float neb = Mathf.PerlinNoise(az * 1.6f + 10f, h * 3f + 4f) * Mathf.PerlinNoise(az * 3.1f, h * 5f);
            c += Color.Lerp(new Color(0.35f, 0.1f, 0.5f), glow * 0.6f, Mathf.PerlinNoise(az, h)) * Mathf.Clamp01(neb * 1.6f - 0.35f) * (h > -0.05f ? 1f : 0f);
            if (forReflection)
            {
                // Bandes de néons au-dessus de l'arène : donnent des reflets nets sur les métaux.
                float band = Mathf.Repeat(az / (Mathf.PI * 2f) * 8f, 1f);
                if (h > 0.25f && h < 0.42f && band > 0.1f && band < 0.42f) c += glow * 2.2f;
                if (h > 0.6f && h < 0.68f) c += accent * 1.4f;
                if (h > 0.92f) c += Color.white * 1.2f;
            }
            return c;
        }

        // ------------------------------------------------------------------ fond

        /// <summary>Grande sphère vue de l'intérieur : ciel nocturne avec nébuleuse et étoiles.</summary>
        public static void BuildBackdrop(Transform parent, Color glow, Color accent)
        {
            const int w = 1024, hgt = 512;
            var tex = new Texture2D(w, hgt, TextureFormat.RGBA32, true) { name = "Backdrop", wrapMode = TextureWrapMode.Repeat };
            var px = new Color32[w * hgt];
            var rng = new System.Random(7);
            for (int y = 0; y < hgt; y++)
            {
                float lat = (y + 0.5f) / hgt * Mathf.PI - Mathf.PI * 0.5f;
                for (int x = 0; x < w; x++)
                {
                    float lon = (x + 0.5f) / w * Mathf.PI * 2f;
                    var d = new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon));
                    px[y * w + x] = Sky(d, glow, accent, false);
                }
            }
            for (int i = 0; i < 1400; i++)
            {
                int x = rng.Next(w), y = hgt / 2 + rng.Next(hgt / 2);
                float b = (float)rng.NextDouble();
                var s = new Color(0.8f + b * 0.2f, 0.85f + b * 0.15f, 1f) * (0.4f + b * 0.6f);
                px[y * w + x] = s;
                if (b > 0.85f && x + 1 < w) px[y * w + x + 1] = s * 0.6f;
            }
            tex.SetPixels32(px);
            tex.Apply(true);

            var mesh = InvertedSphere(48, 24);
            var go = new GameObject("Backdrop");
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * 150f;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            var mat = MaterialLibrary.Unlit(Color.white);
            MaterialLibrary.SetTexture(mat, tex);
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        static Mesh InvertedSphere(int seg, int rings)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int i = 0; i <= rings; i++)
            {
                float lat = i / (float)rings * Mathf.PI - Mathf.PI * 0.5f;
                for (int j = 0; j <= seg; j++)
                {
                    float lon = j / (float)seg * Mathf.PI * 2f;
                    v.Add(new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon)));
                    uv.Add(new Vector2(j / (float)seg, i / (float)rings));
                }
            }
            for (int i = 0; i < rings; i++)
            for (int j = 0; j < seg; j++)
            {
                int a = i * (seg + 1) + j, b = a + 1, c = a + seg + 1, d = c + 1;
                // Faces tournées vers l'intérieur.
                t.Add(a); t.Add(b); t.Add(c); t.Add(b); t.Add(d); t.Add(c);
            }
            var m = new Mesh { name = "Sky" };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        // ------------------------------------------------------------------ tribunes et public

        /// <summary>Tribunes en gradins autour du stade, avec des milliers de petites lumières (téléphones du public).</summary>
        public static void BuildStands(Transform parent, float arenaRadius, Color glow)
        {
            var standMat = MaterialLibrary.Metal(new Color(0.06f, 0.07f, 0.1f), 0.6f, 0.45f, Color.black);
            var trimMat = MaterialLibrary.Metal(Color.black, 0f, 0.5f, glow * 1.6f);
            float r0 = arenaRadius + 13f;
            for (int tier = 0; tier < 5; tier++)
            {
                float ri = r0 + tier * 3.2f, ro = ri + 3.2f;
                float y0 = -4f, y1 = -1f + tier * 2.2f;
                var go = new GameObject("Stand" + tier);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = ArenaManager.ArcMesh(ri, ro, y0, y1, 0f, 360f, 96);
                go.AddComponent<MeshRenderer>().sharedMaterial = standMat;
                var trim = new GameObject("Trim" + tier);
                trim.transform.SetParent(parent, false);
                trim.AddComponent<MeshFilter>().sharedMesh = ArenaManager.ArcMesh(ri - 0.02f, ri + 0.12f, y1, y1 + 0.06f, 0f, 360f, 96);
                trim.AddComponent<MeshRenderer>().sharedMaterial = trimMat;
            }

            // Lumières du public : une seule maille de petits carrés colorés (un seul appel de dessin).
            var verts = new List<Vector3>(); var cols = new List<Color>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            var rng = new System.Random(3);
            Color[] palette = { Color.white, glow, new Color(1f, 0.5f, 0.8f), new Color(1f, 0.85f, 0.4f), new Color(0.6f, 0.8f, 1f) };
            for (int i = 0; i < 2200; i++)
            {
                int tier = rng.Next(5);
                float ri = r0 + tier * 3.2f + 0.4f + (float)rng.NextDouble() * 2.4f;
                float y = -1f + tier * 2.2f + 0.25f + (float)rng.NextDouble() * 0.6f;
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                var c = new Vector3(Mathf.Cos(a) * ri, y, Mathf.Sin(a) * ri);
                var right = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)) * 0.12f;
                var up = Vector3.up * 0.12f;
                int s = verts.Count;
                verts.Add(c - right - up); verts.Add(c + right - up); verts.Add(c + right + up); verts.Add(c - right + up);
                uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(1, 0)); uvs.Add(new Vector2(1, 1)); uvs.Add(new Vector2(0, 1));
                var col = palette[rng.Next(palette.Length)] * (0.5f + (float)rng.NextDouble() * 0.7f);
                col.a = 1f;
                for (int k = 0; k < 4; k++) cols.Add(col);
                tris.Add(s); tris.Add(s + 2); tris.Add(s + 1); tris.Add(s); tris.Add(s + 3); tris.Add(s + 2);
            }
            var m = new Mesh { name = "Crowd" };
            m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(verts); m.SetColors(cols); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            var crowd = new GameObject("CrowdLights");
            crowd.transform.SetParent(parent, false);
            crowd.AddComponent<MeshFilter>().sharedMesh = m;
            var cmat = new Material(MaterialLibrary.Additive(Color.white, MaterialLibrary.SoftDot));
            if (cmat.HasProperty("_Cull")) cmat.SetFloat("_Cull", 0f);
            var mr = crowd.AddComponent<MeshRenderer>();
            mr.sharedMaterial = cmat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            crowd.AddComponent<Twinkle>().Init(cmat);
        }

        /// <summary>Scintillement léger des lumières du public.</summary>
        public sealed class Twinkle : MonoBehaviour
        {
            Material _m;
            public void Init(Material m) => _m = m;
            void Update()
            {
                if (_m == null) return;
                float k = 0.8f + 0.2f * Mathf.Sin(Time.unscaledTime * 2.3f) * Mathf.Sin(Time.unscaledTime * 0.7f);
                MaterialLibrary.SetColor(_m, new Color(k, k, k, 1f));
            }
        }

        // ------------------------------------------------------------------ projecteurs

        /// <summary>Faisceaux lumineux (faux volumétrique) qui balaient l'arène depuis le haut des pylônes.</summary>
        public static List<Transform> BuildSearchlights(Transform parent, float arenaRadius, Color glow)
        {
            var list = new List<Transform>();
            var cone = BeamMesh();
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Beam" };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                float side = Mathf.Sin(u * Mathf.PI);              // plus dense au centre du faisceau
                float fade = Mathf.Pow(1f - v, 1.4f);              // s'estompe vers le bout
                px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(side * side * fade) * 255f));
            }
            tex.SetPixels32(px); tex.Apply();
            Color[] colors = { glow, new Color(0.9f, 0.5f, 1f), glow, new Color(1f, 0.8f, 0.5f) };
            for (int i = 0; i < 4; i++)
            {
                float a = (i + 0.5f) / 4f * Mathf.PI * 2f + 0.39f;
                var pos = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (arenaRadius + 9f) + Vector3.up * 10.5f;
                var go = new GameObject("Searchlight" + i);
                go.transform.SetParent(parent, false);
                go.transform.localPosition = pos;
                go.AddComponent<MeshFilter>().sharedMesh = cone;
                var mat = new Material(MaterialLibrary.Additive(colors[i] * 0.35f, tex));
                if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                list.Add(go.transform);
            }
            return list;
        }

        /// <summary>Cône ouvert de 30 m, pointe à l'origine, orienté vers +Z (UV : u = tour, v = longueur).</summary>
        static Mesh BeamMesh()
        {
            const int seg = 24, len = 8;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int i = 0; i <= len; i++)
            {
                float z = i / (float)len * 30f, r = 0.15f + z * 0.11f;
                for (int j = 0; j <= seg; j++)
                {
                    float a = j / (float)seg * Mathf.PI * 2f;
                    v.Add(new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, z));
                    uv.Add(new Vector2(j / (float)seg, i / (float)len));
                }
            }
            for (int i = 0; i < len; i++)
            for (int j = 0; j < seg; j++)
            {
                int a = i * (seg + 1) + j, b = a + 1, c = a + seg + 1, d = c + 1;
                t.Add(a); t.Add(c); t.Add(b); t.Add(b); t.Add(c); t.Add(d);
            }
            var m = new Mesh { name = "Beam" };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }
    }
}
