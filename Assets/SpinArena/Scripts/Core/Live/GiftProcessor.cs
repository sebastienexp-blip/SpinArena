using System;
using System.Collections.Generic;
using System.Linq;

namespace SpinArena.Core.Live
{
    /// <summary>Niveaux d'effet d'un cadeau (configurables depuis l'administration).</summary>
    public enum GiftTier
    {
        Collective = 0,   // petit bonus collectif (ex. Rose)
        Power = 1,        // active un pouvoir de la toupie du donateur
        SpecialAttack = 2,// attaque spéciale
        Legendary = 3     // événement légendaire pour toute l'arène
    }

    [Serializable]
    public sealed class GiftRule
    {
        /// <summary>Nom du cadeau tel que TikTok l'envoie (ex. « Rose »), insensible à la casse. Vide = règle par valeur.</summary>
        public string giftName;
        /// <summary>Si giftName est vide : la règle s'applique aux cadeaux dont la valeur unitaire est ≥ minDiamonds.</summary>
        public int minDiamonds;
        public GiftTier tier;
        /// <summary>Identifiant de l'effet en jeu (voir GiftEffects dans le jeu).</summary>
        public string effect;
        /// <summary>Délai minimal entre deux activations de cette règle pour un même spectateur (s).</summary>
        public float perUserCooldown = 3f;
    }

    [Serializable]
    public sealed class GiftSettings
    {
        public List<GiftRule> rules = new List<GiftRule>();
        /// <summary>Nombre maximal d'activations exécutées par seconde (le reste attend en file).</summary>
        public float maxActivationsPerSecond = 3f;
        /// <summary>Activations maximales d'un même spectateur par minute (au-delà : mises en file, pas perdues).</summary>
        public int maxPerUserPerMinute = 20;
        /// <summary>Taille maximale de la file d'attente ; au-delà les petits effets collectifs sont fusionnés.</summary>
        public int maxQueue = 200;
        /// <summary>Un même cadeau répété N fois donne au plus ce nombre d'activations (le reste est cumulé en intensité).</summary>
        public int maxActivationsPerGift = 10;
        public int historySize = 300;

        public static GiftSettings CreateDefault() => new GiftSettings
        {
            rules = new List<GiftRule>
            {
                new GiftRule { giftName = "Rose", tier = GiftTier.Collective, effect = "spin_boost_all", perUserCooldown = 0f },
                new GiftRule { giftName = "TikTok", tier = GiftTier.Collective, effect = "spin_boost_all", perUserCooldown = 0f },
                new GiftRule { minDiamonds = 1000, tier = GiftTier.Legendary, effect = "meteor_storm", perUserCooldown = 60f },
                new GiftRule { minDiamonds = 100, tier = GiftTier.SpecialAttack, effect = "special_attack", perUserCooldown = 10f },
                new GiftRule { minDiamonds = 5, tier = GiftTier.Power, effect = "power_up", perUserCooldown = 4f },
                new GiftRule { minDiamonds = 1, tier = GiftTier.Collective, effect = "spin_boost_all", perUserCooldown = 0f },
            }
        };
    }

    /// <summary>Une activation prête à être exécutée par le jeu.</summary>
    public sealed class GiftActivation
    {
        public string userId;
        public string userName;
        public string giftName;
        public string giftImageUrl;
        public GiftTier tier;
        public string effect;
        /// <summary>Nombre de cadeaux couverts par cette activation (intensité, plafonnée par le jeu).</summary>
        public int count;
        public int diamonds;
        public double createdAt;
    }

    public sealed class GiftHistoryEntry
    {
        public double time;
        public string userId, userName, giftName, effect, outcome;
        public int count, diamonds;
    }

    /// <summary>
    /// Traitement des cadeaux : anti-doublons, séries (cadeaux enchaînés), correspondance avec les règles,
    /// file d'attente, limitation des activations abusives et historique. Ne dépend pas du moteur.
    /// </summary>
    public sealed class GiftProcessor
    {
        readonly GiftSettings _s;
        readonly HashSet<string> _seenIds = new HashSet<string>();
        readonly Queue<string> _seenOrder = new Queue<string>();
        readonly Dictionary<string, int> _streakCounts = new Dictionary<string, int>();
        readonly LinkedList<GiftActivation> _queue = new LinkedList<GiftActivation>();
        readonly Dictionary<string, double> _ruleCooldown = new Dictionary<string, double>();
        readonly Dictionary<string, Queue<double>> _userMinute = new Dictionary<string, Queue<double>>();
        readonly List<GiftHistoryEntry> _history = new List<GiftHistoryEntry>();
        double _tokens;
        double _lastRefill = double.NaN;

        public int Duplicates { get; private set; }
        public int Received { get; private set; }
        public IReadOnlyList<GiftHistoryEntry> History => _history;
        public int PendingCount => _queue.Count;

        public GiftProcessor(GiftSettings settings) { _s = settings ?? GiftSettings.CreateDefault(); }

        public GiftRule FindRule(string giftName, int diamonds)
        {
            if (!string.IsNullOrEmpty(giftName))
            {
                var named = _s.rules.FirstOrDefault(r => !string.IsNullOrEmpty(r.giftName)
                                                         && string.Equals(r.giftName, giftName, StringComparison.OrdinalIgnoreCase));
                if (named != null) return named;
            }
            return _s.rules.Where(r => string.IsNullOrEmpty(r.giftName) && diamonds >= r.minDiamonds)
                           .OrderByDescending(r => r.minDiamonds).FirstOrDefault();
        }

        /// <summary>
        /// Reçoit un événement cadeau brut. Pour les cadeaux enchaînables, seuls les nouveaux exemplaires
        /// (différence de compteur) sont comptés : une série 1,2,3…10 donne 10 cadeaux, pas 55.
        /// Retourne le nombre de cadeaux nouvellement pris en compte.
        /// </summary>
        public int Receive(LiveEvent e, double now)
        {
            if (e == null || e.type != LiveEventType.Gift || e.user == null) return 0;
            Received++;
            if (!string.IsNullOrEmpty(e.msgId))
            {
                string dedupKey = e.msgId + "|" + e.repeatCount + "|" + (e.repeatEnd ? 1 : 0);
                if (_seenIds.Contains(dedupKey)) { Duplicates++; return 0; }
                _seenIds.Add(dedupKey);
                _seenOrder.Enqueue(dedupKey);
                while (_seenOrder.Count > 2000) _seenIds.Remove(_seenOrder.Dequeue());
            }

            int newCount;
            string streakKey = e.user.Key + "|" + (e.giftId ?? e.giftName) + "|" + (e.groupId ?? "");
            int repeat = Math.Max(1, e.repeatCount);
            if (e.streakable)
            {
                _streakCounts.TryGetValue(streakKey, out int already);
                newCount = repeat > already ? repeat - already : 0;
                if (e.repeatEnd) _streakCounts.Remove(streakKey);
                else _streakCounts[streakKey] = Math.Max(already, repeat);
            }
            else
            {
                newCount = repeat;
            }
            if (newCount <= 0) { Duplicates++; return 0; }

            var rule = FindRule(e.giftName, e.diamondCount);
            if (rule == null)
            {
                Log(now, e.user.Key, e.user.DisplayName, e.giftName, "", newCount, e.diamondCount, "aucune règle");
                return newCount;
            }

            // Un gros paquet de cadeaux est découpé en quelques activations plus intenses, jamais en centaines d'effets.
            int activations = Math.Min(newCount, Math.Max(1, _s.maxActivationsPerGift));
            int perActivation = newCount / activations, remainder = newCount % activations;
            for (int i = 0; i < activations; i++)
            {
                Enqueue(new GiftActivation
                {
                    userId = e.user.Key,
                    userName = e.user.DisplayName,
                    giftName = e.giftName,
                    giftImageUrl = e.giftImageUrl,
                    tier = rule.tier,
                    effect = rule.effect,
                    count = perActivation + (i < remainder ? 1 : 0),
                    diamonds = e.diamondCount,
                    createdAt = now
                });
            }
            return newCount;
        }

        void Enqueue(GiftActivation a)
        {
            // File pleine : on fusionne avec une activation identique déjà en attente plutôt que de perdre le cadeau.
            if (_queue.Count >= _s.maxQueue)
            {
                for (var n = _queue.Last; n != null; n = n.Previous)
                {
                    if (n.Value.effect == a.effect && n.Value.userId == a.userId) { n.Value.count += a.count; return; }
                }
                for (var n = _queue.Last; n != null; n = n.Previous)
                {
                    if (n.Value.effect == a.effect && n.Value.tier == GiftTier.Collective) { n.Value.count += a.count; return; }
                }
            }
            _queue.AddLast(a);
        }

        /// <summary>
        /// Sort les activations autorisées à cet instant (débit global + limites par spectateur).
        /// Les activations bloquées restent en file ; elles ne sont jamais perdues.
        /// </summary>
        public List<GiftActivation> Dequeue(double now)
        {
            var output = new List<GiftActivation>();
            if (double.IsNaN(_lastRefill)) { _lastRefill = now; _tokens = Math.Max(1, _s.maxActivationsPerSecond); }
            _tokens = Math.Min(Math.Max(1, _s.maxActivationsPerSecond), _tokens + (now - _lastRefill) * _s.maxActivationsPerSecond);
            _lastRefill = now;

            var node = _queue.First;
            while (node != null && _tokens >= 1)
            {
                var next = node.Next;
                var a = node.Value;
                string rk = a.userId + "|" + a.effect;
                var rule = FindRuleByEffect(a.effect, a.tier);
                bool cool = rule != null && rule.perUserCooldown > 0 && _ruleCooldown.TryGetValue(rk, out var t) && now - t < rule.perUserCooldown;
                if (!_userMinute.TryGetValue(a.userId, out var q)) _userMinute[a.userId] = q = new Queue<double>();
                while (q.Count > 0 && now - q.Peek() > 60) q.Dequeue();
                bool capped = q.Count >= _s.maxPerUserPerMinute;
                if (!cool && !capped)
                {
                    _queue.Remove(node);
                    _tokens -= 1;
                    _ruleCooldown[rk] = now;
                    q.Enqueue(now);
                    output.Add(a);
                    Log(now, a.userId, a.userName, a.giftName, a.effect, a.count, a.diamonds, "exécuté");
                }
                node = next;
            }
            return output;
        }

        GiftRule FindRuleByEffect(string effect, GiftTier tier) => _s.rules.FirstOrDefault(r => r.effect == effect && r.tier == tier);

        void Log(double now, string uid, string name, string gift, string effect, int count, int diamonds, string outcome)
        {
            _history.Add(new GiftHistoryEntry { time = now, userId = uid, userName = name, giftName = gift, effect = effect, count = count, diamonds = diamonds, outcome = outcome });
            if (_history.Count > _s.historySize) _history.RemoveRange(0, _history.Count - _s.historySize);
        }
    }
}
