using System;
using System.Collections.Generic;
using System.Linq;
using SpinArena.Core.Live;
using SpinArena.Core.Tops;

namespace SpinArena.Core.Players
{
    /// <summary>Profil permanent d'un spectateur (sauvegardé entre les lives).</summary>
    [Serializable]
    public sealed class PlayerProfile
    {
        public string id;            // clé stable (identifiant TikTok)
        public string uniqueId;      // @pseudo
        public string nickname;
        public string avatarUrl;
        public int level = 1;
        public long xp;              // XP dans le niveau courant
        public long totalXp;
        public long coins;
        public int wins;
        public int gamesPlayed;
        public int eliminations;
        public int bestPlacement;    // 0 = jamais classé
        public int seasonPoints;
        public int winStreak;        // victoires d'affilée en cours
        public int bestWinStreak;
        public int bountiesClaimed;  // primes remportées (élimination du joueur le plus redoutable)
        public string selectedTopId;
        public List<string> ownedTops = new List<string>();
        public long firstSeenUnix;
        public long lastSeenUnix;

        public string DisplayName => !string.IsNullOrEmpty(nickname) ? nickname : !string.IsNullOrEmpty(uniqueId) ? uniqueId : "Joueur";
    }

    /// <summary>Registre des joueurs connus. La persistance (fichier, serveur) est faite à l'extérieur.</summary>
    public sealed class PlayerRegistry
    {
        readonly Dictionary<string, PlayerProfile> _players = new Dictionary<string, PlayerProfile>();
        readonly TopCatalog _catalog;

        public PlayerRegistry(TopCatalog catalog) { _catalog = catalog; }

        public int Count => _players.Count;
        public IEnumerable<PlayerProfile> All => _players.Values;
        public PlayerProfile Get(string id) => id != null && _players.TryGetValue(id, out var p) ? p : null;

        /// <summary>Retrouve ou crée le profil d'un spectateur et met à jour pseudo et photo (jamais inventés).</summary>
        public PlayerProfile GetOrCreate(LiveUser user, long nowUnix, out bool created)
        {
            if (user == null || string.IsNullOrEmpty(user.Key)) throw new ArgumentException("Spectateur sans identifiant");
            created = false;
            if (!_players.TryGetValue(user.Key, out var p))
            {
                created = true;
                var starter = _catalog.StarterFor(user.Key);
                p = new PlayerProfile
                {
                    id = user.Key,
                    firstSeenUnix = nowUnix,
                    selectedTopId = starter?.id,
                };
                if (starter != null) p.ownedTops.Add(starter.id);
                _players[user.Key] = p;
            }
            if (!string.IsNullOrEmpty(user.uniqueId)) p.uniqueId = user.uniqueId;
            if (!string.IsNullOrEmpty(user.nickname)) p.nickname = user.nickname;
            if (!string.IsNullOrEmpty(user.avatarUrl)) p.avatarUrl = user.avatarUrl;
            p.lastSeenUnix = nowUnix;
            EnsureValidTop(p);
            return p;
        }

        public void Load(IEnumerable<PlayerProfile> profiles)
        {
            _players.Clear();
            foreach (var p in profiles)
            {
                if (p == null || string.IsNullOrEmpty(p.id)) continue;
                if (p.ownedTops == null) p.ownedTops = new List<string>();
                EnsureValidTop(p);
                _players[p.id] = p;
            }
        }

        void EnsureValidTop(PlayerProfile p)
        {
            p.ownedTops.RemoveAll(t => !_catalog.Contains(t));
            if (p.ownedTops.Count == 0)
            {
                var s = _catalog.StarterFor(p.id);
                if (s != null) p.ownedTops.Add(s.id);
            }
            if (!p.ownedTops.Contains(p.selectedTopId)) p.selectedTopId = p.ownedTops.FirstOrDefault();
        }

        /// <summary>Classement : victoires, puis éliminations, puis XP totale.</summary>
        public List<PlayerProfile> Leaderboard(int count) =>
            _players.Values.OrderByDescending(p => p.wins).ThenByDescending(p => p.eliminations).ThenByDescending(p => p.totalXp)
                    .Take(count).ToList();

        public int RankOf(string id)
        {
            int i = 1;
            foreach (var p in _players.Values.OrderByDescending(p => p.wins).ThenByDescending(p => p.eliminations).ThenByDescending(p => p.totalXp))
            {
                if (p.id == id) return i;
                i++;
            }
            return 0;
        }
    }
}
