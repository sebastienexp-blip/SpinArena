using System;

namespace SpinArena.Core.Live
{
    /// <summary>Liste des actions de cadeaux : identifiant technique et nom affiché (options, légende à l'écran).</summary>
    public static class GiftEffects
    {
        /// <summary>Actions possibles (identifiant, nom affiché dans les options).</summary>
        public static readonly string[] EffectIds =
        {
            "none", "heal_small", "heal", "super_heal", "regen", "turbo", "rafale", "heavy", "shield", "grow", "royal",
            "freeze", "kick", "sound_wave", "shockwave", "blast", "power_up", "special_attack", "barrage", "wheel", "meteor_storm", "tornado", "lightning", "giant"
        };
        public static readonly string[] EffectNames =
        {
            "Aucune action", "Énergie +", "Soin", "Super soin + bouclier", "Régénération", "Turbo", "Rafale", "Toupie blindée", "Bouclier", "Grossit", "Toupie royale",
            "Gèle un adversaire", "Tir puissant", "Onde sonore", "Onde de choc", "Souffle géant", "Surpuissance", "Capacité spéciale", "Tir de barrage", "Roue au hasard", "Pluie de météores", "Tornade", "Foudre", "Toupie géante"
        };


        public static bool IsKnown(string id) => Array.IndexOf(EffectIds, id) >= 0;

        public static string NameOf(string id)
        {
            int i = Array.IndexOf(EffectIds, id);
            return i >= 0 ? EffectNames[i] : id;
        }
    }
}
