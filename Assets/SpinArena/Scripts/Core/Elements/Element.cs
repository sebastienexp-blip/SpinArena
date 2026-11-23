using System;

namespace SpinArena.Core.Elements
{
    /// <summary>Les six familles de toupies.</summary>
    public enum Element
    {
        Electricity = 0,
        Fire = 1,
        Ice = 2,
        Nature = 3,
        Darkness = 4,
        Light = 5
    }

    /// <summary>
    /// Table des affinités. Chaque élément est fort contre deux éléments, faible contre deux, neutre contre un.
    /// Roue : Feu &gt; Glace, Nature · Glace &gt; Nature, Électricité · Nature &gt; Électricité, Ténèbres ·
    /// Électricité &gt; Ténèbres, Lumière · Ténèbres &gt; Lumière, Feu · Lumière &gt; Feu, Glace.
    /// Paires neutres : Feu–Électricité, Glace–Ténèbres, Nature–Lumière.
    /// </summary>
    public static class ElementChart
    {
        public const float StrongMultiplier = 1.25f;
        public const float WeakMultiplier = 0.8f;

        // Ordre de la roue : chaque élément bat les deux suivants.
        static readonly Element[] Wheel =
        {
            Element.Fire, Element.Ice, Element.Nature, Element.Electricity, Element.Darkness, Element.Light
        };

        static int WheelIndex(Element e)
        {
            for (int i = 0; i < Wheel.Length; i++) if (Wheel[i] == e) return i;
            throw new ArgumentOutOfRangeException(nameof(e));
        }

        /// <summary>+1 = attaquant avantagé, -1 = désavantagé, 0 = neutre.</summary>
        public static int Affinity(Element attacker, Element defender)
        {
            if (attacker == defender) return 0;
            int d = (WheelIndex(defender) - WheelIndex(attacker) + Wheel.Length) % Wheel.Length;
            if (d == 1 || d == 2) return 1;
            if (d == 4 || d == 5) return -1;
            return 0;
        }

        public static float DamageMultiplier(Element attacker, Element defender)
        {
            int a = Affinity(attacker, defender);
            return a > 0 ? StrongMultiplier : a < 0 ? WeakMultiplier : 1f;
        }

        public static string DisplayName(Element e)
        {
            switch (e)
            {
                case Element.Electricity: return "Électricité";
                case Element.Fire: return "Feu";
                case Element.Ice: return "Glace";
                case Element.Nature: return "Nature";
                case Element.Darkness: return "Ténèbres";
                case Element.Light: return "Lumière";
                default: return e.ToString();
            }
        }
    }
}
