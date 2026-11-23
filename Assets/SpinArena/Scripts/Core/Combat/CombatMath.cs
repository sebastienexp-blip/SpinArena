using System;
using SpinArena.Core.Elements;
using SpinArena.Core.Tops;

namespace SpinArena.Core.Combat
{
    /// <summary>Statistiques effectives d'une toupie en combat (base + niveau + bonus temporaires).</summary>
    public struct CombatStats
    {
        public Element element;
        public float power, speed, defense, stability; // 1..~130 après bonus
        public float damageMultiplier;   // bonus temporaires (!attaque, cadeaux)
        public float damageReduction;    // 0..0.6 (!bouclier, capacités)

        public static CombatStats From(TopDefinition d, int playerLevel)
        {
            // Le niveau donne au plus +15 % : la progression compte, sans écraser les nouveaux joueurs.
            float lv = 1f + Math.Min(Math.Max(playerLevel, 1), 200) / 200f * 0.15f;
            return new CombatStats
            {
                element = d.element,
                power = d.power * lv,
                speed = d.speed * lv,
                defense = d.defense * lv,
                stability = d.stability * lv,
                damageMultiplier = 1f,
                damageReduction = 0f
            };
        }
    }

    public static class CombatMath
    {
        /// <summary>Plafond de réduction des dégâts : aucune toupie n'est jamais invulnérable.</summary>
        public const float MaxReduction = 0.6f;
        /// <summary>Vitesse relative minimale (m/s) pour qu'un contact compte comme un impact.</summary>
        public const float MinImpactSpeed = 1.2f;

        /// <summary>
        /// Perte d'énergie de rotation infligée au défenseur lors d'un choc.
        /// relativeSpeed : vitesse relative le long de la normale de contact (m/s).
        /// </summary>
        public static float ImpactDamage(in CombatStats attacker, in CombatStats defender, float relativeSpeed)
        {
            if (relativeSpeed < MinImpactSpeed) return 0f;
            float v = Math.Min(relativeSpeed, 30f);
            float atk = 0.6f + attacker.power / 100f;          // 0.61 .. ~1.9
            float def = 1f + defender.defense / 120f;          // 1.0 .. ~2.1
            float elem = ElementChart.DamageMultiplier(attacker.element, defender.element);
            float red = Math.Min(Math.Max(defender.damageReduction, 0f), MaxReduction);
            float mult = Math.Max(attacker.damageMultiplier, 0f);
            return v * 9f * atk / def * elem * mult * (1f - red);
        }

        /// <summary>Force de projection appliquée au défenseur (impulsion, N·s) : la stabilité résiste.</summary>
        public static float Knockback(in CombatStats attacker, in CombatStats defender, float relativeSpeed)
        {
            if (relativeSpeed < MinImpactSpeed) return 0f;
            float v = Math.Min(relativeSpeed, 30f);
            float push = 0.5f + attacker.power / 100f;
            float hold = 1f + defender.stability / 90f;
            return v * 0.9f * push / hold;
        }

        /// <summary>Perte naturelle d'énergie par seconde (frottements) ; la stabilité la ralentit.</summary>
        public static float SpinDecayPerSecond(in CombatStats s, float suddenDeathFactor)
        {
            float baseDecay = 9f - s.stability / 100f * 5f; // 9 → 4 /s
            return Math.Max(baseDecay, 2f) * Math.Max(suddenDeathFactor, 1f);
        }

        /// <summary>Vitesse de déplacement maximale (m/s).</summary>
        public static float MaxMoveSpeed(in CombatStats s) => 5f + s.speed / 100f * 6f;

        /// <summary>Force d'accélération appliquée par l'IA (N par kg).</summary>
        public static float Acceleration(in CombatStats s) => 10f + s.speed / 100f * 14f;
    }
}
