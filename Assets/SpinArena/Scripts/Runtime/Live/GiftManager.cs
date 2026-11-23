using SpinArena.Battle;
using SpinArena.Core.Battle;
using SpinArena.Core.Live;
using SpinArena.UI;
using UnityEngine;

namespace SpinArena.Live
{
    /// <summary>
    /// Cadeaux du live → effets en jeu, via le GiftProcessor du noyau (anti-doublons, séries, file, limites, historique).
    /// Les cadeaux enrichissent le spectacle : aucun effet n'élimine directement une toupie ni ne garantit la victoire.
    /// </summary>
    public sealed class GiftManager
    {
        readonly GiftProcessor _proc;
        readonly BattleRoyaleManager _br;
        readonly AbilityManager _abilities;
        readonly PlayerManager _players;
        readonly HudManager _hud;
        public GiftProcessor Processor => _proc;
        public int Executed { get; private set; }

        public GiftManager(GiftSettings settings, BattleRoyaleManager br, AbilityManager abilities, PlayerManager players, HudManager hud)
        {
            _proc = new GiftProcessor(settings);
            _br = br; _abilities = abilities; _players = players; _hud = hud;
        }

        public void OnGift(LiveEvent e, double now)
        {
            _players.Touch(e.user);
            _proc.Receive(e, now);
        }

        /// <summary>Exécute les activations autorisées. Hors combat, elles patientent (rien n'est perdu).</summary>
        public void Tick(double now)
        {
            if (_br.Round.Phase != RoundPhase.Fighting || _br.Round.Paused) return;
            foreach (var a in _proc.Dequeue(now))
            {
                Executed++;
                Execute(a);
            }
        }

        void Execute(GiftActivation a)
        {
            var top = _br.TopOf(a.userId);
            Texture avatar = _hud.AvatarOf(a.userId);
            string qty = a.count > 1 ? a.count + "× " : "";
            switch (a.effect)
            {
                case "power_up":
                    if (_abilities.PowerUp(top, a.count)) _hud.Feed(a.userName + " (" + qty + a.giftName + ") : <color=#FFB74D>toupie surpuissante</color>", avatar);
                    else { _abilities.CollectiveBoost(a.count); _hud.Feed(a.userName + " (" + qty + a.giftName + ") : énergie pour tous", avatar); }
                    break;
                case "special_attack":
                    if (_abilities.Special(top, 1.4f)) _hud.Feed(a.userName + " (" + a.giftName + ") : <color=#E1A6FF>ATTAQUE SPÉCIALE</color>", avatar);
                    else { _abilities.SingleMeteor(); _hud.Feed(a.userName + " (" + a.giftName + ") : <color=#FF9E40>météore</color> !", avatar); }
                    break;
                case "meteor_storm":
                    _abilities.MeteorStorm(a.count);
                    _hud.Banner("PLUIE DE MÉTÉORES", "offerte par " + a.userName, 3f, new Color(1f, 0.6f, 0.2f));
                    _hud.Feed(a.userName + " (" + a.giftName + ") : <color=#FF9E40>PLUIE DE MÉTÉORES</color>", avatar);
                    break;
                default: // spin_boost_all
                    _abilities.CollectiveBoost(a.count);
                    _hud.Feed(a.userName + " (" + qty + a.giftName + ") : <color=#FF80AB>énergie pour toutes les toupies</color>", avatar);
                    break;
            }
        }
    }
}
