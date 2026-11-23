using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SpinArena.Core.Commands
{
    public enum ChatCommand
    {
        None,
        Join, Profile, Top, MyTop, Stats, Help,
        Attack, Shield, Turbo, Special
    }

    /// <summary>Reconnaît les commandes du chat (!join, !attaque…), insensible à la casse et aux accents.</summary>
    public static class ChatCommandParser
    {
        static readonly Dictionary<string, ChatCommand> Map = new Dictionary<string, ChatCommand>
        {
            { "join", ChatCommand.Join }, { "rejoindre", ChatCommand.Join }, { "jouer", ChatCommand.Join }, { "play", ChatCommand.Join },
            { "profil", ChatCommand.Profile }, { "profile", ChatCommand.Profile },
            { "top", ChatCommand.Top }, { "classement", ChatCommand.Top },
            { "toupie", ChatCommand.MyTop },
            { "stats", ChatCommand.Stats }, { "stat", ChatCommand.Stats },
            { "help", ChatCommand.Help }, { "aide", ChatCommand.Help },
            { "attaque", ChatCommand.Attack }, { "attack", ChatCommand.Attack },
            { "bouclier", ChatCommand.Shield }, { "shield", ChatCommand.Shield },
            { "turbo", ChatCommand.Turbo },
            { "special", ChatCommand.Special }, { "spe", ChatCommand.Special },
        };

        public static ChatCommand Parse(string comment)
        {
            if (string.IsNullOrWhiteSpace(comment)) return ChatCommand.None;
            string t = comment.Trim();
            // TikTok remplace parfois « ! » par sa variante pleine chasse.
            if (t[0] != '!' && t[0] != '！') return ChatCommand.None;
            t = t.Substring(1).TrimStart();
            int end = 0;
            while (end < t.Length && !char.IsWhiteSpace(t[end]) && t[end] != '!' && t[end] != '！') end++;
            string word = RemoveAccents(t.Substring(0, end)).ToLowerInvariant();
            return Map.TryGetValue(word, out var c) ? c : ChatCommand.None;
        }

        public static bool IsCombatCommand(ChatCommand c) =>
            c == ChatCommand.Attack || c == ChatCommand.Shield || c == ChatCommand.Turbo || c == ChatCommand.Special;

        public static string RemoveAccents(string s)
        {
            var norm = s.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(norm.Length);
            foreach (char ch in norm)
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        public const string HelpText =
            "!join rejoindre · !toupie ta toupie · !stats tes stats · !profil ton profil · !top classement · " +
            "en combat : !attaque !bouclier !turbo !special";
    }

    [Serializable]
    public sealed class CommandLimits
    {
        /// <summary>Délai minimal entre deux utilisations de la même commande par un même joueur (secondes).</summary>
        public float infoCooldown = 20f;     // !profil, !stats, !toupie, !top, !help
        public float combatCooldown = 8f;    // !attaque, !bouclier, !turbo
        public float specialCooldown = 25f;  // !special
        /// <summary>Nombre maximal de commandes acceptées par joueur sur une fenêtre glissante.</summary>
        public int burstMax = 6;
        public float burstWindow = 30f;
        /// <summary>Nombre maximal d'affichages d'informations (toutes personnes confondues) par minute.</summary>
        public int globalInfoPerMinute = 12;
    }

    public enum CommandVerdict { Accepted, Cooldown, Spam, GlobalLimit, Unknown }

    /// <summary>Anti-abus : temps de recharge par commande et par joueur, limite de rafale et limite globale.</summary>
    public sealed class CommandRateLimiter
    {
        readonly CommandLimits _l;
        readonly Dictionary<string, double> _last = new Dictionary<string, double>();
        readonly Dictionary<string, Queue<double>> _burst = new Dictionary<string, Queue<double>>();
        readonly Queue<double> _globalInfo = new Queue<double>();

        public CommandRateLimiter(CommandLimits limits) { _l = limits ?? new CommandLimits(); }

        public float CooldownOf(ChatCommand c)
        {
            switch (c)
            {
                case ChatCommand.Join: return 3f;
                case ChatCommand.Special: return _l.specialCooldown;
                case ChatCommand.Attack:
                case ChatCommand.Shield:
                case ChatCommand.Turbo: return _l.combatCooldown;
                default: return _l.infoCooldown;
            }
        }

        static bool IsInfo(ChatCommand c) =>
            c == ChatCommand.Profile || c == ChatCommand.Stats || c == ChatCommand.MyTop || c == ChatCommand.Top || c == ChatCommand.Help;

        /// <summary>now : horloge en secondes (temps du jeu non mis en pause, ou temps réel).</summary>
        public CommandVerdict Check(string userId, ChatCommand c, double now)
        {
            if (c == ChatCommand.None || string.IsNullOrEmpty(userId)) return CommandVerdict.Unknown;
            string key = userId + "|" + (int)c;
            if (_last.TryGetValue(key, out var t) && now - t < CooldownOf(c)) return CommandVerdict.Cooldown;

            if (!_burst.TryGetValue(userId, out var q)) _burst[userId] = q = new Queue<double>();
            while (q.Count > 0 && now - q.Peek() > _l.burstWindow) q.Dequeue();
            if (q.Count >= _l.burstMax) return CommandVerdict.Spam;

            if (IsInfo(c))
            {
                while (_globalInfo.Count > 0 && now - _globalInfo.Peek() > 60.0) _globalInfo.Dequeue();
                if (_globalInfo.Count >= _l.globalInfoPerMinute) return CommandVerdict.GlobalLimit;
                _globalInfo.Enqueue(now);
            }
            q.Enqueue(now);
            _last[key] = now;
            return CommandVerdict.Accepted;
        }

        /// <summary>Libère la mémoire des joueurs inactifs (à appeler de temps en temps).</summary>
        public void Prune(double now, double olderThan = 600)
        {
            var dead = new List<string>();
            foreach (var kv in _last) if (now - kv.Value > olderThan) dead.Add(kv.Key);
            foreach (var k in dead) _last.Remove(k);
            dead.Clear();
            foreach (var kv in _burst) if (kv.Value.Count == 0 || now - kv.Value.Peek() > olderThan) dead.Add(kv.Key);
            foreach (var k in dead) _burst.Remove(k);
        }
    }
}
