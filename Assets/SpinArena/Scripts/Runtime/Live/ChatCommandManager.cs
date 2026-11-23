using SpinArena.Battle;
using SpinArena.Core.Battle;
using SpinArena.Core.Commands;
using SpinArena.Core.Elements;
using SpinArena.Core.Live;
using SpinArena.Core.Tops;
using SpinArena.UI;
using UnityEngine;

namespace SpinArena.Live
{
    /// <summary>
    /// Commandes du chat : entrée dans l'arène, informations (profil, stats, toupie, classement, aide)
    /// et coups de pouce en combat. Participation gratuite ; anti-abus par joueur et global.
    /// </summary>
    public sealed class ChatCommandManager
    {
        readonly BattleRoyaleManager _br;
        readonly AbilityManager _abilities;
        readonly PlayerManager _players;
        readonly HudManager _hud;
        readonly CommandRateLimiter _limiter;
        public int Accepted { get; private set; }
        public int Rejected { get; private set; }

        public ChatCommandManager(BattleRoyaleManager br, AbilityManager abilities, PlayerManager players, HudManager hud, CommandLimits limits)
        {
            _br = br; _abilities = abilities; _players = players; _hud = hud;
            _limiter = new CommandRateLimiter(limits);
        }

        public void OnChat(LiveEvent e, double now)
        {
            _players.MarkActive(e.user.Key);
            var cmd = ChatCommandParser.Parse(e.comment);
            if (cmd == ChatCommand.None) return;
            var verdict = _limiter.Check(e.user.Key, cmd, now);
            if (verdict != CommandVerdict.Accepted) { Rejected++; return; }
            Accepted++;
            if (Accepted % 500 == 0) _limiter.Prune(now);

            var profile = _players.Touch(e.user);
            var top = _br.TopOf(profile.id);
            Texture avatar = _hud.AvatarOf(profile.id);
            switch (cmd)
            {
                case ChatCommand.Join:
                    var r = _br.Join(e.user);
                    if (r == BattleRound.JoinResult.Full) _hud.Feed(profile.DisplayName + " : file d'attente pleine, réessaie à la prochaine manche", avatar);
                    break;
                case ChatCommand.Help:
                    _hud.Feed(ChatCommandParser.HelpText, null, new Color(0.8f, 0.92f, 1f));
                    break;
                case ChatCommand.Top:
                    _hud.ShowLeaderboard(_players.Registry.Leaderboard(8));
                    break;
                case ChatCommand.Profile:
                    _hud.Feed(profile.DisplayName + " · <color=#FFD54A>Nv " + profile.level + "</color> · " + profile.wins + " victoire(s) · rang #" + _players.Registry.RankOf(profile.id) + " · " + profile.coins + " éclats", avatar);
                    break;
                case ChatCommand.Stats:
                    _hud.Feed(profile.DisplayName + " · " + profile.gamesPlayed + " manche(s) · " + profile.eliminations + " élimination(s)"
                              + (profile.bestPlacement > 0 ? " · meilleur #" + profile.bestPlacement : ""), avatar);
                    break;
                case ChatCommand.MyTop:
                    var def = _players.Catalog.Get(profile.selectedTopId);
                    if (def != null)
                        _hud.Feed(profile.DisplayName + " · <color=" + TopDefinition.RarityColor(def.rarity) + ">" + def.name + "</color> (" + ElementChart.DisplayName(def.element) + ", "
                                  + TopDefinition.RarityName(def.rarity) + ") PUI " + def.power + " VIT " + def.speed + " DÉF " + def.defense + " STA " + def.stability, avatar);
                    break;
                case ChatCommand.Attack:
                    if (_abilities.Attack(top)) _hud.Feed(profile.DisplayName + " : <color=#FF8A65>attaque renforcée</color>", avatar);
                    break;
                case ChatCommand.Shield:
                    if (_abilities.Shield(top)) _hud.Feed(profile.DisplayName + " : <color=#80D8FF>bouclier</color>", avatar);
                    break;
                case ChatCommand.Turbo:
                    if (_abilities.Turbo(top)) _hud.Feed(profile.DisplayName + " : <color=#FFD54A>turbo</color>", avatar);
                    break;
                case ChatCommand.Special:
                    if (_abilities.Special(top)) _hud.Feed(profile.DisplayName + " : <color=#E1A6FF>capacité spéciale</color> !", avatar);
                    break;
            }
        }
    }
}
