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

        static readonly (string name, int value)[] Small = { ("Rose", 1), ("TikTok", 1), ("Weights", 1), ("Ice Cream Cone", 1), ("GG", 1), ("Mini Speaker", 1), ("Football", 1), ("Heart Me", 1) };
        static readonly (string name, int value)[] Medium = { ("Finger Heart", 5), ("Panda", 5), ("Mic", 5), ("Hand Wave", 9), ("Perfume", 20), ("Doughnut", 30), ("Cap", 99) };
        static readonly (string name, int value)[] Big = { ("Hand Hearts", 100), ("Sunglasses", 199), ("TikTok Crown", 299), ("Money Gun", 500) };
        static readonly (string name, int value)[] Huge = { ("Galaxy", 1000), ("Sunset Speedway", 10000) };

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
                    if (_rng.Next(5) == 0) Chat(u, "!join", now);
                    // Tapotements simulés.
                    _pending.Enqueue(new LiveEvent { type = LiveEventType.Like, user = Pick(), likeCount = 5 + _rng.Next(11), receivedAt = now });
                }
                _nextChat = now + (phase == RoundPhase.Lobby ? 0.25 : 0.6) + _rng.NextDouble() * 0.8;
            }
            if (now >= _nextGift && phase == RoundPhase.Fighting)
            {
                var u = Pick();
                int r = _rng.Next(100);
                var g = r < 75 ? Small[_rng.Next(Small.Length)] : r < 93 ? Medium[_rng.Next(Medium.Length)] : r < 99 ? Big[_rng.Next(Big.Length)] : Huge[_rng.Next(Huge.Length)];
                string gift = g.name; int diamonds = g.value;
                bool streak = diamonds < 100; int repeat = streak ? 1 + _rng.Next(4) : 1;
                string id = "demo_gift" + (++_msg);
                for (int i = 1; i <= repeat; i++)
                    _pending.Enqueue(new LiveEvent { type = LiveEventType.Gift, msgId = id, user = u, giftId = gift, giftName = gift, diamondCount = diamonds, repeatCount = i, repeatEnd = i == repeat, streakable = streak, receivedAt = now });
                _nextGift = now + 1.5 + _rng.NextDouble() * 3;
            }
            while (_pending.Count > 0) EventReceived?.Invoke(_pending.Dequeue());
        }
    }
}
