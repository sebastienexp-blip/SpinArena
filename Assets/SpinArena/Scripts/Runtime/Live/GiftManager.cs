using System.Collections.Generic;
using SpinArena.Battle;
using SpinArena.Core.Battle;
using SpinArena.Core.Live;
using SpinArena.Tops;
using SpinArena.UI;
using UnityEngine;

namespace SpinArena.Live
{
    /// <summary>
    /// Cadeaux du live → actions en jeu, via le GiftProcessor du noyau (anti-doublons, séries, file, limites, historique).
    /// Chaque cadeau a sa propre action sur la toupie du donateur ; plus il vaut cher, plus l'action est forte.
    /// Les gros cadeaux déclenchent des événements d'arène qui touchent tout le monde. Rien ne fait gagner à coup sûr.
    /// À l'écran : l'image du cadeau, le pseudo et l'action (jamais de montant).
    /// </summary>
    public sealed class GiftManager
    {
        readonly GiftProcessor _proc;
        readonly BattleRoyaleManager _br;
        readonly AbilityManager _ab;
        readonly PlayerManager _players;
        readonly HudManager _hud;
        readonly System.Random _rng = new System.Random();
        public GiftProcessor Processor => _proc;
        public int Executed { get; private set; }

        public GiftManager(GiftSettings settings, BattleRoyaleManager br, AbilityManager abilities, PlayerManager players, HudManager hud)
        {
            _proc = new GiftProcessor(settings);
            _br = br; _ab = abilities; _players = players; _hud = hud;
        }

        public void OnGift(LiveEvent e, double now)
        {
            if (!GameConfig.Current.effects.giftsEnabled) return;
            _players.Touch(e.user);
            // Un spectateur qui envoie un cadeau entre dans l'arène s'il n'y est pas encore (comme avec !join).
            if (GameConfig.Current.effects.giftJoins && _br.TopOf(e.user.Key) == null) _br.Join(e.user);
            GiftIcons.Remember(e.giftName, e.giftImageUrl);
            _proc.Receive(e, now);
        }

        /// <summary>Exécute les actions autorisées. Hors combat, elles patientent (rien n'est perdu).</summary>
        public void Tick(double now)
        {
            if (_br.Round.Phase != RoundPhase.Fighting || _br.Round.Paused) return;
            foreach (var a in _proc.Dequeue(now))
            {
                Executed++;
                Execute(a, a.effect);
            }
        }

        /// <summary>Actions possibles (identifiant, nom affiché dans les options).</summary>
        public static readonly string[] EffectIds =
        {
            "heal_small", "heal", "super_heal", "regen", "turbo", "rafale", "heavy", "shield", "grow", "royal",
            "freeze", "kick", "sound_wave", "shockwave", "blast", "power_up", "special_attack", "barrage", "wheel", "meteor_storm", "tornado"
        };
        public static readonly string[] EffectNames =
        {
            "Énergie +", "Soin", "Super soin + bouclier", "Régénération", "Turbo", "Rafale", "Toupie blindée", "Bouclier", "Grossit", "Toupie royale",
            "Gèle un adversaire", "Tir puissant", "Onde sonore", "Onde de choc", "Souffle géant", "Surpuissance", "Capacité spéciale", "Tir de barrage", "Roue au hasard", "Pluie de météores", "Tornade"
        };

        static readonly string[] WheelEffects = { "heal", "turbo", "shield", "grow", "kick", "freeze", "shockwave", "heavy" };

        void Execute(GiftActivation a, string effect)
        {
            var t = _br.TopOf(a.userId);
            var fx = GameConfig.Current.effects;
            int n = Mathf.Clamp(Mathf.RoundToInt(a.count * fx.giftIntensity), 1, 10);
            if (!fx.arenaEvents && (effect == "meteor_storm" || effect == "tornado")) effect = "super_heal";
            string label = Run(effect, t, n);
            if (label == null)
            {
                // Toupie absente ou déjà éliminée : l'énergie profite à toutes les toupies en lice.
                _ab.CollectiveBoost(n);
                label = "énergie pour toutes les toupies";
            }
            Audio.AudioManager.Play("gift", 0.8f, 0.15f);
            _hud.GiftPopup(a.userName, GiftIcons.Get(a.giftName), (n > 1 ? "×" + n + "  " : "") + label);
        }

        /// <summary>Lance l'action ; renvoie le texte affiché, ou null si elle n'a pas pu s'appliquer.</summary>
        string Run(string effect, TopController t, int n)
        {
            switch (effect)
            {
                case "heal_small": return _ab.Heal(t, 0.04f * n) ? "énergie +" : null;
                case "turbo": return _ab.Turbo(t) ? "turbo !" : null;
                case "heavy": return _ab.Heavy(t, 8f + 2f * n) ? "toupie blindée" : null;
                case "freeze": return _ab.FreezeNearest(t, 2.5f + 0.5f * n) ? "gèle un adversaire" : null;
                case "grow": return _ab.GrowTop(t, 0.06f * n) ? "grossit !" : null;
                case "kick": return _ab.Kick(t, Mathf.Min(1f + 0.25f * (n - 1), 2f)) ? "tir puissant" : null;
                case "sound_wave": return _ab.Wave(t, 0.8f, 0.4f, 4f) ? "onde sonore" : null;
                case "heal": return _ab.Heal(t, 0.12f * Mathf.Min(n, 2)) ? "soin" : null;
                case "shield": if (!Ok(t)) return null; Shield(t, 0.4f + 0.05f * Mathf.Min(n, 4), 6f); return "bouclier";
                case "shockwave": return _ab.Wave(t, 1f, 1f, 6f) ? "onde de choc" : null;
                case "blast": return _ab.Wave(t, 1.4f, 0.6f, 10f) ? "souffle géant" : null;
                case "regen": return _ab.Regen(t, 0.2f, 8f) ? "régénération" : null;
                case "power_up": return _ab.PowerUp(t, n) ? "surpuissance" : null;
                case "special_attack": return _ab.Special(t, 1.4f) ? "capacité spéciale" : null;
                case "super_heal": if (!_ab.Heal(t, 0.3f)) return null; Shield(t, 0.45f, 6f); return "super soin + bouclier";
                case "rafale": return _ab.Rafale(t) ? "rafale" : null;
                case "royal": return _ab.Royal(t) ? "toupie royale" : null;
                case "barrage": return _ab.Barrage(t, 5) ? "tir de barrage" : null;
                case "meteor_storm":
                    _ab.MeteorStorm(n);
                    _hud.Banner("PLUIE DE MÉTÉORES", null, 2.5f, new Color(1f, 0.6f, 0.2f));
                    return "pluie de météores";
                case "tornado":
                    _ab.Tornado();
                    _hud.Banner("TORNADE", null, 2.5f, new Color(1f, 0.5f, 0.8f));
                    return "tornade sur l'arène";
                case "wheel":
                    string pick = WheelEffects[_rng.Next(WheelEffects.Length)];
                    string r = Run(pick, t, 1);
                    return r != null ? "roue : " + r : null;
                default:
                    return _ab.Heal(t, 0.04f * n) ? "énergie +" : null;
            }
        }

        static bool Ok(TopController t) => t != null && t.State == TopState.Fighting;
        bool Shield(TopController t, float red, float sec) { t.BuffShield(red, sec); VFX.VfxManager.Instance.Shockwave(t.transform.position, new Color(0.5f, 0.8f, 1f)); return true; }
    }

    /// <summary>Images des cadeaux : celles fournies avec le jeu, sinon l'image envoyée par TikTok pendant le live.</summary>
    public static class GiftIcons
    {
        static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();
        static readonly Dictionary<string, string> Urls = new Dictionary<string, string>();
        static readonly HashSet<string> Loading = new HashSet<string>();

        static string Slug(string name) => (name ?? "").Trim().ToLowerInvariant().Replace(' ', '_');

        public static void Remember(string name, string url)
        {
            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(url)) Urls[Slug(name)] = url;
        }

        public static Texture2D Get(string name)
        {
            string s = Slug(name);
            if (s == "donut") s = "doughnut";
            if (Cache.TryGetValue(s, out var t)) return t;
            t = Resources.Load<Texture2D>("Gifts/" + s);
            if (t != null) { Cache[s] = t; return t; }
            if (Urls.TryGetValue(s, out var url) && !Loading.Contains(s) && GameManager.Instance != null)
            {
                Loading.Add(s);
                GameManager.Instance.StartCoroutine(Download(s, url));
            }
            return null;
        }

        static System.Collections.IEnumerator Download(string slug, string url)
        {
            using (var req = UnityEngine.Networking.UnityWebRequestTexture.GetTexture(url))
            {
                req.timeout = 10;
                yield return req.SendWebRequest();
                if (req.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                    Cache[slug] = UnityEngine.Networking.DownloadHandlerTexture.GetContent(req);
            }
        }
    }
}
