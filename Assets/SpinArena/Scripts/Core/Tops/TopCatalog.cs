using System;
using System.Collections.Generic;
using System.Linq;
using SpinArena.Core.Elements;

namespace SpinArena.Core.Tops
{
    /// <summary>
    /// Collection des toupies. Le catalogue par défaut (42 toupies originales : 6 éléments × 7 raretés)
    /// est généré de façon déterministe ; on peut en ajouter d'autres avec <see cref="Add"/>
    /// (par exemple depuis un fichier de configuration JSON).
    /// </summary>
    public sealed class TopCatalog
    {
        readonly Dictionary<string, TopDefinition> _byId = new Dictionary<string, TopDefinition>();
        readonly List<TopDefinition> _all = new List<TopDefinition>();

        public IReadOnlyList<TopDefinition> All => _all;
        public int Count => _all.Count;

        public void Add(TopDefinition def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (string.IsNullOrEmpty(def.id)) throw new ArgumentException("Toupie sans identifiant");
            if (_byId.ContainsKey(def.id)) throw new ArgumentException("Identifiant de toupie en double : " + def.id);
            ClampStats(def);
            _byId[def.id] = def;
            _all.Add(def);
        }

        public TopDefinition Get(string id) => id != null && _byId.TryGetValue(id, out var d) ? d : null;
        public bool Contains(string id) => id != null && _byId.ContainsKey(id);

        public IEnumerable<TopDefinition> ByRarity(Rarity r) => _all.Where(t => t.rarity == r);
        public IEnumerable<TopDefinition> ByElement(Element e) => _all.Where(t => t.element == e);

        /// <summary>Toupie de départ attribuée à un nouveau joueur : commune, élément stable dérivé de son identifiant.</summary>
        public TopDefinition StarterFor(string userId)
        {
            var commons = ByRarity(Rarity.Common).ToList();
            if (commons.Count == 0) return _all.FirstOrDefault();
            return commons[(int)(StableHash(userId) % (uint)commons.Count)];
        }

        /// <summary>Hachage FNV-1a stable d'une plateforme à l'autre (contrairement à string.GetHashCode).</summary>
        public static uint StableHash(string s)
        {
            unchecked
            {
                uint h = 2166136261;
                if (s != null) foreach (char c in s) { h ^= c; h *= 16777619; }
                return h;
            }
        }

        static void ClampStats(TopDefinition d)
        {
            d.power = Clamp(d.power); d.speed = Clamp(d.speed); d.defense = Clamp(d.defense); d.stability = Clamp(d.stability);
            if (d.bladeCount < 2) d.bladeCount = 2;
            if (d.bladeCount > 8) d.bladeCount = 8;
        }

        static int Clamp(int v) => v < 1 ? 1 : v > 100 ? 100 : v;

        // ---------------------------------------------------------------- catalogue par défaut

        static readonly Dictionary<Element, string[]> Names = new Dictionary<Element, string[]>
        {
            { Element.Electricity, new[] { "Étincelle", "Survolteur", "Arc Fulgurant", "Tempestor", "Raijin Prime", "Orage Éternel", "Voltarion Divin" } },
            { Element.Fire,        new[] { "Braise", "Fournaise", "Pyroclaste", "Inferno Rex", "Phénix Écarlate", "Cœur de Magma", "Ignis Divin" } },
            { Element.Ice,         new[] { "Givre", "Stalactor", "Blizzard", "Cryo Lame", "Glacius Rex", "Zéro Absolu", "Borée Divine" } },
            { Element.Nature,      new[] { "Pousse", "Ronceterre", "Sylvestre", "Épine Venin", "Gaïa Gardien", "Racine-Monde", "Sylva Divine" } },
            { Element.Darkness,    new[] { "Ombre", "Spectre", "Néant Rôdeur", "Faucheuse", "Abysse Roi", "Éclipse Noire", "Nyx Divine" } },
            { Element.Light,       new[] { "Lueur", "Prisme", "Aurore", "Paladin Solaire", "Séraphin", "Halo Éternel", "Lumen Divin" } },
        };

        // Couleurs : principale, secondaire, lueur.
        static readonly Dictionary<Element, string[]> Palette = new Dictionary<Element, string[]>
        {
            { Element.Electricity, new[] { "#2A3BFF", "#C9D2E3", "#5CE1FF" } },
            { Element.Fire,        new[] { "#C21E12", "#2B2B2E", "#FF8A1F" } },
            { Element.Ice,         new[] { "#7FD8FF", "#EAF6FF", "#B9F3FF" } },
            { Element.Nature,      new[] { "#1F8A3A", "#6B4A2B", "#8CFF6A" } },
            { Element.Darkness,    new[] { "#3B0E5C", "#121016", "#B04CFF" } },
            { Element.Light,       new[] { "#F2D27A", "#FFFFFF", "#FFF6C8" } },
        };

        // Profil de base de chaque élément : puissance, vitesse, défense, stabilité.
        static readonly Dictionary<Element, int[]> Bias = new Dictionary<Element, int[]>
        {
            { Element.Electricity, new[] { 26, 34, 18, 22 } },
            { Element.Fire,        new[] { 34, 26, 18, 22 } },
            { Element.Ice,         new[] { 22, 18, 32, 28 } },
            { Element.Nature,      new[] { 18, 22, 26, 34 } },
            { Element.Darkness,    new[] { 31, 30, 17, 22 } },
            { Element.Light,       new[] { 24, 22, 30, 24 } },
        };

        public static TopCatalog CreateDefault()
        {
            var cat = new TopCatalog();
            foreach (Element e in Enum.GetValues(typeof(Element)))
            {
                var names = Names[e];
                for (int r = 0; r <= (int)Rarity.Divine; r++)
                {
                    var rarity = (Rarity)r;
                    int total = 160 + r * 25;              // 160 (commun) → 310 (divin)
                    var b = Bias[e];
                    // Petite variation de profil selon le rang : deux toupies du même élément n'ont pas le même style.
                    int shift = (r % 4);
                    int[] w = new int[4];
                    for (int i = 0; i < 4; i++) w[i] = b[i] + (i == shift ? 6 : 0) - (i == (shift + 2) % 4 ? 4 : 0);
                    int sumW = w.Sum();
                    int[] s = w.Select(x => (int)Math.Round(total * (double)x / sumW)).ToArray();
                    var pal = Palette[e];
                    cat.Add(new TopDefinition
                    {
                        id = e.ToString().ToLowerInvariant() + "_" + r,
                        name = names[r],
                        element = e,
                        rarity = rarity,
                        power = s[0], speed = s[1], defense = s[2], stability = s[3],
                        specialAbility = AbilityIds.ForElement(e),
                        bladeCount = 3 + (r + (int)e) % 4,
                        shapeVariant = (r + 2 * (int)e) % 4,
                        primaryColor = pal[0], secondaryColor = pal[1], glowColor = pal[2]
                    });
                }
            }
            return cat;
        }
    }
}
