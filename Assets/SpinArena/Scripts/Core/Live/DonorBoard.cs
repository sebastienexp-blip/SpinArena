using System;
using System.Collections.Generic;

namespace SpinArena.Core.Live
{
    /// <summary>Une ligne du classement des donateurs.</summary>
    public sealed class DonorEntry
    {
        public string userId;
        public string userName;
        /// <summary>Valeur totale envoyée pendant le live (sert au tri, jamais affichée).</summary>
        public long totalValue;
        /// <summary>Cadeau principal du donateur (celui qui pèse le plus) et nombre envoyé.</summary>
        public string mainGift;
        public int mainGiftCount;
        internal readonly Dictionary<string, (int count, long value)> gifts = new Dictionary<string, (int, long)>();
    }

    /// <summary>
    /// Classement des donateurs du live en cours : qui a le plus soutenu, avec son cadeau principal et combien.
    /// Pur C# (testé hors Unity). Remis à zéro au début de chaque live.
    /// </summary>
    public sealed class DonorBoard
    {
        readonly Dictionary<string, DonorEntry> _byId = new Dictionary<string, DonorEntry>();
        public int Version { get; private set; }
        public int Count => _byId.Count;

        public void Clear() { _byId.Clear(); Version++; }

        /// <summary>Ajoute <paramref name="count"/> exemplaires d'un cadeau valant <paramref name="valueEach"/> chacun.</summary>
        public void Add(string userId, string userName, string giftName, int count, int valueEach)
        {
            if (string.IsNullOrEmpty(userId) || count <= 0) return;
            if (!_byId.TryGetValue(userId, out var d)) { d = new DonorEntry { userId = userId }; _byId[userId] = d; }
            if (!string.IsNullOrEmpty(userName)) d.userName = userName;
            string g = string.IsNullOrEmpty(giftName) ? "?" : giftName;
            long v = (long)count * Math.Max(1, valueEach);
            d.gifts.TryGetValue(g, out var cur);
            d.gifts[g] = (cur.count + count, cur.value + v);
            d.totalValue += v;
            // Cadeau principal : celui qui pèse le plus ; à égalité, le plus nombreux.
            string best = null; (int count, long value) bv = default;
            foreach (var kv in d.gifts)
                if (best == null || kv.Value.value > bv.value || (kv.Value.value == bv.value && kv.Value.count > bv.count)) { best = kv.Key; bv = kv.Value; }
            d.mainGift = best; d.mainGiftCount = bv.count;
            Version++;
        }

        /// <summary>Les <paramref name="n"/> premiers donateurs, du plus généreux au moins généreux.</summary>
        public List<DonorEntry> Top(int n)
        {
            var list = new List<DonorEntry>(_byId.Values);
            list.Sort((a, b) => b.totalValue != a.totalValue ? b.totalValue.CompareTo(a.totalValue) : string.CompareOrdinal(a.userId, b.userId));
            if (list.Count > n) list.RemoveRange(n, list.Count - n);
            return list;
        }

        public int RankOf(string userId)
        {
            var top = Top(int.MaxValue);
            for (int i = 0; i < top.Count; i++) if (top[i].userId == userId) return i + 1;
            return 0;
        }
    }
}
