using System.Collections.Generic;
using UnityEngine;

namespace SpinArena.Util
{
    /// <summary>
    /// Matériaux du jeu. Les modèles de matériaux (Resources/Materials) sont créés par l'outil de construction :
    /// ils garantissent que les shaders URP et leurs variantes (émission, transparence) sont inclus dans l'APK.
    /// </summary>
    public static class MaterialLibrary
    {
        public const string LitName = "SA_Lit";
        public const string UnlitName = "SA_Unlit";
        public const string AdditiveName = "SA_Additive";
        public const string TransparentName = "SA_Transparent";

        static readonly Dictionary<string, Material> Templates = new Dictionary<string, Material>();
        static readonly Dictionary<string, Material> Shared = new Dictionary<string, Material>();

        static Material Template(string name)
        {
            if (Templates.TryGetValue(name, out var m) && m != null) return m;
            m = Resources.Load<Material>("Materials/" + name);
            if (m == null)
            {
                // Secours (éditeur sans configuration) : shader trouvé par son nom.
                string shader = name == LitName ? "Universal Render Pipeline/Lit"
                              : name == UnlitName ? "Universal Render Pipeline/Unlit"
                              : "Universal Render Pipeline/Particles/Unlit";
                var s = Shader.Find(shader) ?? Shader.Find("Sprites/Default");
                m = new Material(s);
                Debug.LogWarning("[SpinArena] Modèle de matériau absent : " + name + " (secours " + s.name + ")");
            }
            Templates[name] = m;
            return m;
        }

        /// <summary>Métal PBR avec émission optionnelle.</summary>
        public static Material Metal(Color baseColor, float metallic, float smoothness, Color emission)
        {
            var m = new Material(Template(LitName));
            SetColor(m, baseColor);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            SetEmission(m, emission);
            return m;
        }

        public static Material Unlit(Color c)
        {
            var m = new Material(Template(UnlitName));
            SetColor(m, c);
            return m;
        }

        /// <summary>Matériau additif partagé (particules, traînées, halos) : une instance par couleur.</summary>
        public static Material Additive(Color c, Texture tex = null)
        {
            string key = "add" + ColorUtility.ToHtmlStringRGBA(c) + (tex != null ? tex.GetInstanceID().ToString() : "");
            if (Shared.TryGetValue(key, out var s) && s != null) return s;
            var m = new Material(Template(AdditiveName));
            SetColor(m, c);
            if (tex != null) SetTexture(m, tex);
            Shared[key] = m;
            return m;
        }

        /// <summary>Matériau transparent (alpha) avec texture : photos de profil, panneaux.</summary>
        public static Material Transparent(Texture tex, Color tint)
        {
            var m = new Material(Template(TransparentName));
            SetColor(m, tint);
            SetTexture(m, tex);
            return m;
        }

        public static void SetColor(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        public static void SetTexture(Material m, Texture t)
        {
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", t);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", t);
        }

        public static void SetEmission(Material m, Color e)
        {
            if (!m.HasProperty("_EmissionColor")) return;
            // Le mot-clé reste toujours actif : une seule variante de shader à inclure dans l'APK (émission noire = aucun effet).
            m.SetColor("_EmissionColor", e);
            m.EnableKeyword("_EMISSION");
        }

        static Texture2D _soft;
        /// <summary>Tache lumineuse douce (particules) générée une fois.</summary>
        public static Texture2D SoftDot
        {
            get
            {
                if (_soft != null) return _soft;
                const int n = 64;
                _soft = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "SoftDot" };
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - d);
                    a = a * a * (3f - 2f * a);
                    byte b = (byte)(a * 255f);
                    px[y * n + x] = new Color32(255, 255, 255, b);
                }
                _soft.SetPixels32(px);
                _soft.Apply(false, true);
                return _soft;
            }
        }
    }
}
