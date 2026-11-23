using System.Collections.Generic;
using SpinArena.Battle;
using SpinArena.Core.Battle;
using SpinArena.Core.Live;
using SpinArena.VFX;
using UnityEngine;

namespace SpinArena.Live
{
    /// <summary>
    /// Tapotements (likes) : chaque palier de tapotements d'un spectateur donne à SA toupie un peu de rotation,
    /// un petit coup de vitesse et la fait grossir. Petit bonus régulier, plafonné : il ne remplace pas les cadeaux.
    /// </summary>
    public sealed class TapManager
    {
        readonly BattleRoyaleManager _br;
        readonly AbilityManager _abilities;
        readonly PlayerManager _players;
        readonly EffectSettings _fx;
        readonly Dictionary<string, int> _pending = new Dictionary<string, int>();
        int _round = -1;

        public int Boosts { get; private set; }

        public TapManager(BattleRoyaleManager br, AbilityManager abilities, PlayerManager players, EffectSettings fx)
        {
            _br = br; _abilities = abilities; _players = players; _fx = fx;
        }

        public void OnTaps(LiveUser user, int count)
        {
            if (user == null || string.IsNullOrEmpty(user.Key)) return;
            _players.MarkActive(user.Key);
            if (_round != _br.Round.RoundNumber) { _pending.Clear(); _round = _br.Round.RoundNumber; }
            var top = _br.TopOf(user.Key);
            if (top == null || !top.Alive) return;
            _pending.TryGetValue(user.Key, out int acc);
            acc += count;
            int step = Mathf.Max(1, _fx.tapsPerBoost);
            int boosts = acc / step;
            _pending[user.Key] = acc % step;
            if (boosts <= 0) return;
            boosts = Mathf.Min(boosts, 3); // une avalanche de tapotements ne donne pas plus de 3 paliers d'un coup
            Boosts += boosts;
            top.Grow(_fx.tapGrowth * boosts);
            if (_br.Round.Phase == RoundPhase.Fighting)
            {
                top.AddSpin(top.MaxSpin * _fx.tapSpinBoost * boosts);
                _abilities.SpeedBoost(top, _fx.tapSpeedSeconds);
            }
            VfxManager.Instance.Sparkle(top.transform.position, new Color(1f, 0.45f, 0.6f));
            Audio.AudioManager.Play("grow", 0.3f, 0.35f);
        }
    }
}
