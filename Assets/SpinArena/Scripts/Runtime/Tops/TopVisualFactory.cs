using System.Collections.Generic;
using SpinArena.Core.Tops;
using SpinArena.Util;
using UnityEngine;

namespace SpinArena.Tops
{
    /// <summary>
    /// Génère le modèle 3D d'une toupie à partir de sa définition : pointe, disque de performance, lames d'attaque,
    /// anneau d'énergie lumineux, couronne et emblème. Les maillages sont partagés entre toupies identiques.
    /// (Phase 2 : sert de base ; les modèles sculptés FBX pourront remplacer ce générateur toupie par toupie.)
    /// </summary>
    public static class TopVisualFactory
    {
        sealed class TopMeshes
        {
            public Mesh tip, body, blades, ring, crown, emblem;
            public Material primary, secondary, glow, tipMat;
        }

        static readonly Dictionary<string, TopMeshes> Cache = new Dictionary<string, TopMeshes>();

        public const float Radius = 0.48f;   // rayon des lames (m)

        /// <summary>Crée la hiérarchie visuelle sous parent. Renvoie le transform qui tourne (axe Y).</summary>
        public static Transform Build(TopDefinition def, Transform parent)
        {
            var m = Get(def);
            var spin = new GameObject("Spin").transform;
            spin.SetParent(parent, false);
            Add(spin, "Tip", m.tip, m.tipMat);
            Add(spin, "Body", m.body, m.primary);
            Add(spin, "Blades", m.blades, m.secondary);
            Add(spin, "Ring", m.ring, m.glow);
            Add(spin, "Crown", m.crown, m.primary);
            Add(spin, "Emblem", m.emblem, m.glow);
            return spin;
        }

        static void Add(Transform parent, string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = name == "Body" || name == "Blades" ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        static TopMeshes Get(TopDefinition def)
        {
            if (Cache.TryGetValue(def.id, out var m)) return m;
            int v = def.shapeVariant;
            int rar = (int)def.rarity;
            Color primary = Compat.Hex(def.primaryColor, Color.gray);
            Color secondary = Compat.Hex(def.secondaryColor, Color.white);
            Color glow = Compat.Hex(def.glowColor, Color.cyan);
            Color gold = new Color(1f, 0.78f, 0.3f);
            if (def.rarity >= Rarity.Legendary) secondary = Color.Lerp(secondary, gold, 0.65f);

            m = new TopMeshes
            {
                tip = Lathe(new[] { V(0.0f, 0.0f), V(0.035f, 0.01f), V(0.07f, 0.06f), V(0.11f, 0.12f), V(0.0f, 0.12f) }, 24),
                body = Lathe(BodyProfile(v), 48),
                blades = Blades(def.bladeCount, v, rar),
                ring = Lathe(new[] { V(0.30f, 0.205f), V(0.335f, 0.215f), V(0.335f, 0.235f), V(0.30f, 0.245f) }, 64),
                crown = Lathe(CrownProfile(v), 40),
                emblem = Star(6 - (v % 2), 0.09f, 0.045f, 0.36f + (v == 3 ? 0.04f : 0f)),
                primary = MaterialLibrary.Metal(primary, 0.85f, 0.7f + rar * 0.03f, Color.black),
                secondary = MaterialLibrary.Metal(secondary, 1f, 0.82f, glow * (0.05f * rar)),
                glow = MaterialLibrary.Metal(Color.black, 0f, 0.6f, glow * (2.2f + rar * 0.45f)),
                tipMat = MaterialLibrary.Metal(new Color(0.75f, 0.77f, 0.8f), 1f, 0.9f, Color.black)
            };
            Cache[def.id] = m;
            return m;
        }

        static Vector2 V(float r, float y) => new Vector2(r, y);

        static Vector2[] BodyProfile(int v)
        {
            switch (v)
            {
                case 0: return new[] { V(0.0f, 0.10f), V(0.16f, 0.11f), V(0.30f, 0.16f), V(0.36f, 0.20f), V(0.34f, 0.25f), V(0.20f, 0.27f), V(0.0f, 0.27f) };
                case 1: return new[] { V(0.0f, 0.10f), V(0.22f, 0.12f), V(0.40f, 0.17f), V(0.41f, 0.21f), V(0.30f, 0.26f), V(0.0f, 0.27f) };
                case 2: return new[] { V(0.0f, 0.10f), V(0.12f, 0.10f), V(0.26f, 0.18f), V(0.33f, 0.22f), V(0.31f, 0.28f), V(0.12f, 0.30f), V(0.0f, 0.30f) };
                default: return new[] { V(0.0f, 0.10f), V(0.25f, 0.13f), V(0.38f, 0.19f), V(0.37f, 0.24f), V(0.25f, 0.25f), V(0.0f, 0.26f) };
            }
        }

        static Vector2[] CrownProfile(int v)
        {
            float h = 0.27f;
            switch (v)
            {
                case 0: return new[] { V(0.24f, h), V(0.25f, h + 0.03f), V(0.18f, h + 0.07f), V(0.10f, h + 0.09f), V(0.0f, h + 0.09f) };
                case 1: return new[] { V(0.28f, h), V(0.26f, h + 0.05f), V(0.12f, h + 0.06f), V(0.0f, h + 0.06f) };
                case 2: return new[] { V(0.20f, h + 0.02f), V(0.21f, h + 0.06f), V(0.14f, h + 0.11f), V(0.05f, h + 0.13f), V(0.0f, h + 0.13f) };
                default: return new[] { V(0.26f, h), V(0.22f, h + 0.08f), V(0.08f, h + 0.10f), V(0.0f, h + 0.10f) };
            }
        }

        /// <summary>
        /// Révolution d'un profil (r, y) autour de l'axe Y. Le profil est décrit du centre bas vers le centre haut :
        /// la normale extérieure d'un segment P→Q est alors (Δy, −Δr) dans le plan (r, y).
        /// </summary>
        public static Mesh Lathe(Vector2[] profile, int seg)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            int n = profile.Length;
            for (int k = 0; k < n - 1; k++)
            {
                Vector2 P = profile[k], Q = profile[k + 1];
                Vector2 n2 = new Vector2(Q.y - P.y, -(Q.x - P.x));
                int start = verts.Count;
                for (int j = 0; j <= seg; j++)
                {
                    float a = j / (float)seg * Mathf.PI * 2f;
                    float c = Mathf.Cos(a), s = Mathf.Sin(a);
                    verts.Add(new Vector3(P.x * c, P.y, P.x * s));
                    verts.Add(new Vector3(Q.x * c, Q.y, Q.x * s));
                }
                // Sens des triangles choisi d'après le premier quad (arêtes vives : sommets propres au segment).
                Vector3 a0 = verts[start], b0 = verts[start + 1], c0 = verts[start + 3];
                Vector3 mid = (a0 + b0 + c0) / 3f;
                Vector3 radial = new Vector3(mid.x, 0f, mid.z).normalized;
                Vector3 wanted = radial * n2.x + Vector3.up * n2.y;
                bool flip = Vector3.Dot(Vector3.Cross(b0 - a0, c0 - a0), wanted) < 0f;
                for (int j = 0; j < seg; j++)
                {
                    int i0 = start + j * 2, i1 = i0 + 1, i2 = i0 + 2, i3 = i0 + 3;
                    if (!flip) { tris.Add(i0); tris.Add(i1); tris.Add(i3); tris.Add(i0); tris.Add(i3); tris.Add(i2); }
                    else { tris.Add(i0); tris.Add(i3); tris.Add(i1); tris.Add(i0); tris.Add(i2); tris.Add(i3); }
                }
            }
            var m = new Mesh { name = "Lathe" };
            m.SetVertices(verts);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Lames d'attaque inclinées, réparties autour du disque.</summary>
        static Mesh Blades(int count, int variant, int rarity)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            float y0 = 0.17f, y1 = 0.23f + 0.01f * (variant % 2);
            float sweep = 0.55f + 0.1f * variant;           // courbure vers l'arrière
            float width = Mathf.PI * 2f / count * (0.42f + 0.04f * rarity);
            for (int k = 0; k < count; k++)
            {
                float a = k / (float)count * Mathf.PI * 2f;
                // Polygone de la lame vu de dessus (étoilé depuis son centre).
                var poly = new List<Vector2>();
                const int steps = 6;
                for (int i = 0; i <= steps; i++)
                {
                    float t = i / (float)steps;
                    float ang = a + t * width;
                    poly.Add(new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * 0.28f);
                }
                for (int i = 0; i <= steps; i++)
                {
                    float t = 1f - i / (float)steps;
                    float r = Mathf.Lerp(Radius * 0.82f, Radius, Mathf.Sin(t * Mathf.PI * 0.5f));
                    float ang = a + t * width + sweep * (1f - t) * 0.35f;
                    poly.Add(new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r);
                }
                Extrude(poly, y0, y1, verts, tris);
            }
            var m = new Mesh { name = "Blades" };
            m.SetVertices(verts);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        static void Extrude(List<Vector2> poly, float y0, float y1, List<Vector3> verts, List<int> tris)
        {
            Vector2 c = Vector2.zero;
            foreach (var p in poly) c += p;
            c /= poly.Count;
            int n = poly.Count;
            // Dessus et dessous en éventail depuis le centre.
            int top = verts.Count;
            verts.Add(new Vector3(c.x, y1, c.y));
            foreach (var p in poly) verts.Add(new Vector3(p.x, y1, p.y));
            int bot = verts.Count;
            verts.Add(new Vector3(c.x, y0, c.y));
            foreach (var p in poly) verts.Add(new Vector3(p.x, y0, p.y));
            bool ccw = SignedArea(poly) > 0f;
            for (int i = 0; i < n; i++)
            {
                int a = i + 1, b = (i + 1) % n + 1;
                if (ccw) { tris.Add(top); tris.Add(top + b); tris.Add(top + a); tris.Add(bot); tris.Add(bot + a); tris.Add(bot + b); }
                else { tris.Add(top); tris.Add(top + a); tris.Add(top + b); tris.Add(bot); tris.Add(bot + b); tris.Add(bot + a); }
            }
            // Côtés.
            for (int i = 0; i < n; i++)
            {
                var p = poly[i]; var q = poly[(i + 1) % n];
                int s = verts.Count;
                verts.Add(new Vector3(p.x, y0, p.y)); verts.Add(new Vector3(q.x, y0, q.y));
                verts.Add(new Vector3(q.x, y1, q.y)); verts.Add(new Vector3(p.x, y1, p.y));
                if (ccw) { tris.Add(s); tris.Add(s + 2); tris.Add(s + 1); tris.Add(s); tris.Add(s + 3); tris.Add(s + 2); }
                else { tris.Add(s); tris.Add(s + 1); tris.Add(s + 2); tris.Add(s); tris.Add(s + 2); tris.Add(s + 3); }
            }
        }

        static float SignedArea(List<Vector2> p)
        {
            float a = 0f;
            for (int i = 0; i < p.Count; i++) { var u = p[i]; var w = p[(i + 1) % p.Count]; a += u.x * w.y - w.x * u.y; }
            return a * 0.5f;
        }

        /// <summary>Emblème en étoile, légèrement en relief, posé sur la couronne.</summary>
        static Mesh Star(int points, float rOuter, float rInner, float y)
        {
            var poly = new List<Vector2>();
            for (int i = 0; i < points * 2; i++)
            {
                float a = i / (float)(points * 2) * Mathf.PI * 2f;
                float r = i % 2 == 0 ? rOuter : rInner;
                poly.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
            }
            var verts = new List<Vector3>();
            var tris = new List<int>();
            Extrude(poly, y, y + 0.015f, verts, tris);
            var m = new Mesh { name = "Emblem" };
            m.SetVertices(verts);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}
