using System;
using System.Collections.Generic;
using SpinArena.Core.Battle;
using SpinArena.Core.Live;

namespace SpinArena.Live
{
    /// <summary>
    /// Connecteur de démonstration : spectateurs simulés pour tester le jeu sans live.
    /// Toujours signalé « MODE DÉMO » à l'écran ; à ne pas utiliser pendant un vrai live.
    /// </summary>
    public sealed class DemoLiveConnector : ILiveConnector
    {
        static readonly string[] Names =
        {
            "Lina", "Max", "Sofia", "Nathan", "Inès", "Léo", "Jade", "Hugo", "Chloé", "Yanis", "Manon", "Adam",
            "Camille", "Noah", "Sarah", "Lucas", "Emma", "Rayan", "Louise", "Tom", "Alice", "Ethan", "Zoé", "Enzo",
            "Lola", "Gabriel", "Nina", "Liam", "Rose", "Sacha", "Mila", "Théo", "Anna", "Ilyes", "Clara", "Jules",
            "Maëlys", "Malo", "Léa", "Kylian", "Eva", "Axel", "Julia", "Nolan", "Ambre", "Timéo", "Lou", "Mathis"
        };
        static readonly string[] Suffixes = { "_spin", "77", "_pro", "TV", "_fr", "x", "_off", "59", "_games", "13" };

        readonly int _count;
        readonly Func<RoundPhase> _phase;
        readonly Random _rng = new Random(1234);
        readonly List<LiveUser> _users = new List<LiveUser>();
        readonly Queue<LiveEvent> _pending = new Queue<LiveEvent>();
        double _nextChat, _nextGift, _lastNow;
        int _msg;
        bool _running;

        public DemoLiveConnector(int spectators, Func<RoundPhase> phase)
        {
            _count = Math.Max(2, Math.Min(spectators, 120));
            _phase = phase;
            for (int i = 0; i < _count; i++)
            {
                string n = Names[i % Names.Length] + (i >= Names.Length ? (i / Names.Length + 1).ToString() : "");
                _users.Add(new LiveUser { userId = "demo_" + i, uniqueId = (n + Suffixes[i % Suffixes.Length]).ToLowerInvariant(), nickname = n, avatarUrl = "" });
            }
        }

        public string Name => "Démonstration";
        public bool IsDemo => true;
        public bool IsConnected => _running;
        public string StatusText => _running ? "MODE DÉMO : " + _count + " spectateurs simulés" : "démo arrêtée";
        public event Action<LiveEvent> EventReceived;

        public void Connect() { _running = true; _nextChat = 0; _nextGift = 0; }
        public void Disconnect() { _running = false; _pending.Clear(); }

        LiveUser Pick() => _users[_rng.Next(_users.Count)];

        void Chat(LiveUser u, string text, double now) =>
            _pending.Enqueue(new LiveEvent { type = LiveEventType.Chat, msgId = "demo" + (++_msg), user = u, comment = text, receivedAt = now });

        public void Pump(double now)
        {
            if (!_running) return;
            if (now < _lastNow) { _nextChat = now; _nextGift = now; }
            _lastNow = now;
            var phase = _phase();
            if (now >= _nextChat)
            {
                var u = Pick();
                if (phase == RoundPhase.Lobby || phase == RoundPhase.Countdown) Chat(u, "!join", now);
                else if (phase == RoundPhase.Fighting)
                {
                    int r = _rng.Next(100);
                    string cmd = r < 35 ? "!attaque" : r < 55 ? "!bouclier" : r < 75 ? "!turbo" : r < 85 ? "!special" : r < 92 ? "!join" : r < 96 ? "!stats" : "!toupie";
                    Chat(u, cmd, now);
                }
                else if (_rng.Next(4) == 0) Chat(u, _rng.Next(2) == 0 ? "!top" : "!profil", now);
                _nextChat = now + (phase == RoundPhase.Lobby ? 0.25 : 0.6) + _rng.NextDouble() * 0.8;
            }
            if (now >= _nextGift && phase == RoundPhase.Fighting)
            {
                var u = Pick();
                int r = _rng.Next(100);
                string gift; int diamonds; bool streak = true; int repeat = 1 + _rng.Next(5);
                if (r < 70) { gift = "Rose"; diamonds = 1; }
                else if (r < 90) { gift = "Donut"; diamonds = 30; }
                else if (r < 98) { gift = "Couronne"; diamonds = 199; streak = false; repeat = 1; }
                else { gift = "Lion"; diamonds = 29999; streak = false; repeat = 1; }
                string id = "demo_gift" + (++_msg);
                for (int i = 1; i <= repeat; i++)
                    _pending.Enqueue(new LiveEvent { type = LiveEventType.Gift, msgId = id, user = u, giftId = gift, giftName = gift, diamondCount = diamonds, repeatCount = i, repeatEnd = i == repeat, streakable = streak, receivedAt = now });
                _nextGift = now + 3 + _rng.NextDouble() * 6;
            }
            while (_pending.Count > 0) EventReceived?.Invoke(_pending.Dequeue());
        }
    }
}
