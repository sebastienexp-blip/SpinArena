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
        /// <summary>Classement des donateurs du live en cours (panneau en haut à droite).</summary>
        public DonorBoard Donors { get; } = new DonorBoard();
        /// <summary>Nombre reçu de chaque cadeau pendant ce live (clé : image du cadeau), pour le panneau de droite.</summary>
        public readonly System.Collections.Generic.Dictionary<string, int> ReceivedCounts = new System.Collections.Generic.Dictionary<string, int>();
        public int ReceivedVersion { get; private set; }
        public int Executed { get; private set; }
        /// <summary>Statistiques : action sur la toupie du donateur, sur une autre toupie, retour en jeu, énergie pour tous.</summary>
        public int Personal { get; private set; }
        public int ForOthers { get; private set; }
        public int Revives { get; private set; }
        public int Collective { get; private set; }

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
            GiftPrices.Learn(e.giftName, e.diamondCount);
            int added = _proc.Receive(e, now);
            if (added > 0)
            {
                Donors.Add(e.user.Key, e.user.DisplayName, e.giftName, added, Mathf.Max(1, e.diamondCount));
                var rule = _proc.FindRule(e.giftName, e.diamondCount);
                string key = GiftIcons.Slug(rule != null && !string.IsNullOrEmpty(rule.giftName) ? rule.giftName : e.giftName);
                if (key == "donut") key = "doughnut";
                ReceivedCounts.TryGetValue(key, out int c);
                ReceivedCounts[key] = c + added;
                ReceivedVersion++;
            }
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

        static readonly string[] WheelEffects = { "heal", "turbo", "shield", "grow", "kick", "freeze", "shockwave", "heavy" };

        void Execute(GiftActivation a, string effect)
        {
            var t = _br.TopOf(a.userId);
            var fx = GameConfig.Current.effects;
            int n = Mathf.Clamp(Mathf.RoundToInt(a.count * fx.giftIntensity), 1, 10);
            if (!fx.arenaEvents && (effect == "meteor_storm" || effect == "tornado")) effect = "super_heal";
            Texture icon = GiftIcons.Get(a.ruleName ?? a.giftName) ?? GiftIcons.Get(a.giftName);
            bool arenaEvent = effect == "meteor_storm" || effect == "tornado";
            string forWhom = null;
            if (!arenaEvent && !Ok(t))
            {
                // Toupie éliminée : un cadeau d'au moins quelques pièces la fait revenir dans l'arène (une fois par manche).
                if (fx.giftRevive && a.diamonds >= fx.reviveMinDiamonds && _br.Revive(a.userId))
                {
                    Revives++;
                    GameManager.Instance?.Show?.OnGiftActivated(a.userId, a.userName);
                    Audio.AudioManager.Play("join", 1f, 0.1f);
                    _hud.GiftPopup(a.userName, icon, "RETOUR DANS L'ARÈNE !");
                    return;
                }
                // Sinon, l'action du cadeau va à une toupie encore en lice, choisie au hasard.
                var alive = new List<TopController>(_br.Fighting());
                if (alive.Count > 0)
                {
                    t = alive[_rng.Next(alive.Count)];
                    forWhom = t.DisplayName;
                }
            }
            string label = Run(effect, t, n);
            if (label == null)
            {
                // Rien n'a pu s'appliquer (aucune toupie en lice) : énergie pour tout le monde.
                _ab.CollectiveBoost(n);
                label = "énergie pour toutes les toupies";
                Collective++;
            }
            else if (forWhom != null) { label += " pour " + forWhom; ForOthers++; }
            else Personal++;
            // Grosses actions : ralenti avec zoom.
            if (effect == "royal" || effect == "barrage" || effect == "tornado" || effect == "meteor_storm" || effect == "lightning" || effect == "giant" || (effect == "special_attack" && a.diamonds >= 50))
                CameraRig.CinematicDirector.Instance?.Play(arenaEvent ? null : t?.transform, 0.35f, 1f, 0.8f, 0.75f, true);
            GameManager.Instance?.Show?.OnGiftActivated(a.userId, a.userName);
            Audio.AudioManager.Play("gift", 0.8f, 0.15f);
            _hud.GiftPopup(a.userName, icon, (n > 1 ? "×" + n + "  " : "") + label);
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
                case "giant":
                    if (!_ab.GiantTop(t)) return null;
                    _hud.Banner("TOUPIE GÉANTE", t.DisplayName, 2.5f, new Color(0.75f, 0.5f, 1f));
                    return "toupie géante";
                case "lightning":
                    if (!_ab.LightningStorm(t, Mathf.Clamp(2 + n, 3, 8))) return null;
                    _hud.Banner("FOUDRE", null, 2f, new Color(0.65f, 0.85f, 1f));
                    return "foudre";
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
    /// <summary>Prix réels des cadeaux vus pendant le live (pièces), pour ranger la liste du moins cher au plus cher.</summary>
    public static class GiftPrices
    {
        static readonly Dictionary<string, int> Prices = new Dictionary<string, int>();
        public static int Version { get; private set; }
        static string Key(string name) { var k = GiftIcons.Slug(name); return k == "donut" ? "doughnut" : k; }
        public static void Learn(string name, int coins)
        {
            if (string.IsNullOrEmpty(name) || coins <= 0) return;
            string k = Key(name);
            if (Prices.TryGetValue(k, out int old) && old == coins) return;
            Prices[k] = coins;
            Version++;
        }
        public static int Of(string name) => !string.IsNullOrEmpty(name) && Prices.TryGetValue(Key(name), out int c) ? c : 0;
    }

    public static class GiftIcons
    {
        static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();
        static readonly Dictionary<string, string> Urls = new Dictionary<string, string>();
        static readonly HashSet<string> Loading = new HashSet<string>();

        /// <summary>Nom du fichier image : « Finger Heart » → finger_heart (Resources/Gifts/finger_heart.png).</summary>
        public static string Slug(string name) => (name ?? "").Trim().ToLowerInvariant().Replace(' ', '_');

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
