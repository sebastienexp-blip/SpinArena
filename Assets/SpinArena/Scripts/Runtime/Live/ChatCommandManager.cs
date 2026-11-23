using SpinArena.Battle;
using SpinArena.Core.Commands;
using SpinArena.Core.Live;

namespace SpinArena.Live
{
    /// <summary>
    /// Chat du live : seule la commande !join est utilisée (entrée gratuite dans l'arène).
    /// Le reste du jeu se pilote par les tapotements et les cadeaux.
    /// </summary>
    public sealed class ChatCommandManager
    {
        readonly BattleRoyaleManager _br;
        readonly PlayerManager _players;
        readonly CommandRateLimiter _limiter;
        public int Accepted { get; private set; }
        public int Rejected { get; private set; }

        public ChatCommandManager(BattleRoyaleManager br, PlayerManager players, CommandLimits limits)
        {
            _br = br; _players = players;
            _limiter = new CommandRateLimiter(limits);
        }

        public void OnChat(LiveEvent e, double now)
        {
            _players.MarkActive(e.user.Key);
            if (ChatCommandParser.Parse(e.comment) != ChatCommand.Join) return;
            if (_limiter.Check(e.user.Key, ChatCommand.Join, now) != CommandVerdict.Accepted) { Rejected++; return; }
            Accepted++;
            if (Accepted % 500 == 0) _limiter.Prune(now);
            _br.Join(e.user);
        }
    }
}
