using System.Collections.Generic;
using System.Linq;
using SpinArena.Core.Players;
using SpinArena.Core.Tops;

namespace SpinArena.Core.Progression
{
    /// <summary>
    /// Déblocage des toupies avec le niveau : chaque joueur garde l'élément de sa première toupie et obtient
    /// les raretés supérieures de cet élément en montant de niveau (gratuitement, sans cadeau).
    /// </summary>
    public static class TopUnlocks
    {
        /// <summary>Niveau requis pour chaque rareté (Commun → Divin).</summary>
        public static readonly int[] LevelForRarity = { 1, 5, 15, 30, 50, 80, 120 };

        public static Rarity MaxRarityFor(int level)
        {
            var r = Rarity.Common;
            for (int i = 0; i < LevelForRarity.Length; i++) if (level >= LevelForRarity[i]) r = (Rarity)i;
            return r;
        }

        /// <summary>Ajoute les toupies débloquées, équipe automatiquement la meilleure, renvoie les nouvelles.</summary>
        public static List<TopDefinition> Apply(PlayerProfile p, TopCatalog catalog)
        {
            var added = new List<TopDefinition>();
            var first = catalog.Get(p.ownedTops.FirstOrDefault()) ?? catalog.StarterFor(p.id);
            if (first == null) return added;
            var max = MaxRarityFor(p.level);
            foreach (var t in catalog.ByElement(first.element).Where(t => t.rarity <= max).OrderBy(t => t.rarity))
            {
                if (p.ownedTops.Contains(t.id)) continue;
                p.ownedTops.Add(t.id);
                added.Add(t);
            }
            if (added.Count > 0)
            {
                var best = p.ownedTops.Select(catalog.Get).Where(t => t != null).OrderByDescending(t => t.rarity).First();
                p.selectedTopId = best.id;
            }
            return added;
        }
    }
}
