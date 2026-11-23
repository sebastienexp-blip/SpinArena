using System;
using SpinArena.Core.Elements;

namespace SpinArena.Core.Tops
{
    public enum Rarity
    {
        Common = 0,
        Uncommon = 1,
        Rare = 2,
        Epic = 3,
        Legendary = 4,
        Mythic = 5,
        Divine = 6
    }

    /// <summary>Style de combat déduit des statistiques : pilote l'IA de la toupie.</summary>
    public enum CombatClass
    {
        Attack,   // fonce sur les adversaires
        Defense,  // tient le centre, encaisse et renvoie
        Stamina,  // tourne longtemps, esquive
        Balance   // polyvalente
    }

    /// <summary>Définition d'une toupie de la collection (données pures, sans rendu).</summary>
    [Serializable]
    public sealed class TopDefinition
    {
        public string id;
        public string name;
        public Element element;
        public Rarity rarity;
        /// <summary>Statistiques de 1 à 100.</summary>
        public int power, speed, defense, stability;
        /// <summary>Identifiant de la capacité spéciale (voir AbilityIds).</summary>
        public string specialAbility;
        /// <summary>Paramètres de forme pour le modèle 3D (nombre de lames, profil).</summary>
        public int bladeCount;
        public int shapeVariant;
        /// <summary>Couleurs en hexadécimal (#RRGGBB).</summary>
        public string primaryColor, secondaryColor, glowColor;

        public int TotalStats => power + speed + defense + stability;

        public CombatClass Class
        {
            get
            {
                int max = Math.Max(Math.Max(power, speed), Math.Max(defense, stability));
                int min = Math.Min(Math.Min(power, speed), Math.Min(defense, stability));
                if (max - min <= 6) return CombatClass.Balance;
                if (max == power) return CombatClass.Attack;
                if (max == defense) return CombatClass.Defense;
                if (max == stability) return CombatClass.Stamina;
                return CombatClass.Attack; // vitesse dominante : style offensif
            }
        }

        /// <summary>Énergie de rotation maximale (la « vie » de la toupie).</summary>
        public float MaxSpin => 600f + stability * 6f + defense * 2f;

        /// <summary>Masse physique (kg) : les toupies défensives sont plus lourdes.</summary>
        public float Mass => 1f + defense / 100f * 1.2f + stability / 100f * 0.3f;

        public static string RarityName(Rarity r)
        {
            switch (r)
            {
                case Rarity.Common: return "Commun";
                case Rarity.Uncommon: return "Peu commun";
                case Rarity.Rare: return "Rare";
                case Rarity.Epic: return "Épique";
                case Rarity.Legendary: return "Légendaire";
                case Rarity.Mythic: return "Mythique";
                case Rarity.Divine: return "Divin";
                default: return r.ToString();
            }
        }

        public static string RarityColor(Rarity r)
        {
            switch (r)
            {
                case Rarity.Common: return "#B8C0CC";
                case Rarity.Uncommon: return "#5BD46B";
                case Rarity.Rare: return "#3FA9FF";
                case Rarity.Epic: return "#B45CFF";
                case Rarity.Legendary: return "#FFB627";
                case Rarity.Mythic: return "#FF4D6D";
                case Rarity.Divine: return "#FFF4B0";
                default: return "#FFFFFF";
            }
        }
    }

    public static class AbilityIds
    {
        public const string Overcharge = "overcharge";      // Électricité : accélération + décharge en chaîne
        public const string FireBurst = "fire_burst";       // Feu : explosion de zone + brûlure
        public const string FrostShield = "frost_shield";   // Glace : bouclier de glace + ralentit les assaillants
        public const string RootBloom = "root_bloom";       // Nature : régénération + racines autour
        public const string ShadowDrain = "shadow_drain";   // Ténèbres : téléportation courte + drain d'énergie
        public const string HolyBarrier = "holy_barrier";   // Lumière : barrière sacrée + rayon

        public static string ForElement(Element e)
        {
            switch (e)
            {
                case Element.Electricity: return Overcharge;
                case Element.Fire: return FireBurst;
                case Element.Ice: return FrostShield;
                case Element.Nature: return RootBloom;
                case Element.Darkness: return ShadowDrain;
                default: return HolyBarrier;
            }
        }
    }
}
