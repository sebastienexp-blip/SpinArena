using System;
using System.Collections.Generic;
using System.Linq;
using SpinArena.Core.Battle;
using SpinArena.Core.Live;
using SpinArena.Live;
using SpinArena.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SpinArena
{
    /// <summary>
    /// Menu des options (3 tapotements rapides dans le coin en haut à gauche). Onglets : Direct, Partie, Combat,
    /// Tapotements, Cadeaux, Affichage, Son, Live, Récompenses, Système. Chaque réglage s'applique tout de suite
    /// et est enregistré sur le téléphone.
    /// </summary>
    public sealed class OptionsMenu : MonoBehaviour
    {
        GameManager _gm;
        GameConfig C => _gm.Config;
        GameObject _panel;
        RectTransform _content;
        ScrollRect _scroll;
        Text _title;
        readonly List<(Button button, string tab)> _tabButtons = new List<(Button, string)>();
        readonly List<Action> _refreshers = new List<Action>();
        string _tab = "Direct";
        int _taps;
        float _firstTap, _refreshTimer;
        readonly Dictionary<string, float> _confirm = new Dictionary<string, float>();

        static readonly string[] Tabs = { "Direct", "Partie", "Combat", "Tapotements", "Cadeaux", "Affichage", "Son", "Live", "Récompenses", "Système" };

        public void Init(GameManager gm)
        {
            _gm = gm;
            var root = gm.Hud.Canvas.transform;

            var hot = UiFactory.Rect("OptionsHotspot", root, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(140, 140));
            var img = hot.gameObject.AddComponent<Image>();
            img.color = new Color(0, 0, 0, 0);
            hot.gameObject.AddComponent<OptionsHotspot>().Owner = this;

            var p = UiFactory.Panel("Options", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1760, 1000), new Color(0.02f, 0.03f, 0.08f, 0.97f));
            p.raycastTarget = true;
            _panel = p.gameObject;
            Vector2 tl = new Vector2(0f, 1f), tr = new Vector2(1f, 1f);
            _title = UiFactory.Label("Title", p.transform, "OPTIONS", 44, TextAnchor.MiddleLeft, UiFactory.Gold, tl, tl, new Vector2(30, -14), new Vector2(900, 60));
            UiFactory.Button("Close", p.transform, "Fermer", tr, new Vector2(-120, -46), new Vector2(200, 70), () => _panel.SetActive(false));

            // Onglets à gauche.
            for (int i = 0; i < Tabs.Length; i++)
            {
                string tab = Tabs[i];
                var b = UiFactory.Button("Tab" + tab, p.transform, tab, tl, new Vector2(150, -130 - i * 84), new Vector2(270, 74), () => Show(tab));
                _tabButtons.Add((b, tab));
            }

            // Zone de réglages qui défile.
            var view = UiFactory.Rect("View", p.transform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            view.offsetMin = new Vector2(310, 24);
            view.offsetMax = new Vector2(-24, -100);
            var vimg = view.gameObject.AddComponent<Image>();
            vimg.color = new Color(1f, 1f, 1f, 0.03f);
            view.gameObject.AddComponent<RectMask2D>();
            _content = UiFactory.Rect("Content", view, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0, 0));
            var layout = _content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8;
            layout.padding = new RectOffset(16, 16, 12, 24);
            layout.childForceExpandHeight = false;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            var fit = _content.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _scroll = view.gameObject.AddComponent<ScrollRect>();
            _scroll.content = _content;
            _scroll.viewport = view;
            _scroll.horizontal = false;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 30;

            _panel.SetActive(false);
        }

        internal void Tap()
        {
            float t = Time.unscaledTime;
            if (t - _firstTap > 1.5f) { _taps = 0; _firstTap = t; }
            if (++_taps >= 3)
            {
                _taps = 0;
                _panel.SetActive(!_panel.activeSelf);
                if (_panel.activeSelf) Show(_tab);
            }
        }

        void Update()
        {
            if (_panel == null || !_panel.activeSelf) return;
            _refreshTimer -= Time.unscaledDeltaTime;
            if (_refreshTimer <= 0f)
            {
                _refreshTimer = 0.5f;
                foreach (var r in _refreshers) r();
            }
        }

        // ------------------------------------------------------------------ construction des onglets

        void Show(string tab)
        {
            _tab = tab;
            _title.text = "OPTIONS · " + tab.ToUpperInvariant();
            foreach (var (b, t) in _tabButtons)
                ((Image)b.targetGraphic).color = t == tab ? new Color(0.25f, 0.45f, 0.9f, 1f) : new Color(0.12f, 0.2f, 0.4f, 0.95f);
            for (int i = _content.childCount - 1; i >= 0; i--) Destroy(_content.GetChild(i).gameObject);
            _refreshers.Clear();
            _confirm.Clear();
            switch (tab)
            {
                case "Direct": BuildDirect(); break;
                case "Partie": BuildRound(); break;
                case "Combat": BuildCombat(); break;
                case "Tapotements": BuildTaps(); break;
                case "Cadeaux": BuildGifts(); break;
                case "Affichage": BuildDisplay(); break;
                case "Son": BuildSound(); break;
                case "Live": BuildLive(); break;
                case "Récompenses": BuildRewards(); break;
                default: BuildSystem(); break;
            }
            _scroll.verticalNormalizedPosition = 1f;
            foreach (var r in _refreshers) r();
        }

        void BuildDirect()
        {
            var round = _gm.Battle.Round;
            Info(() =>
                "Manche " + round.RoundNumber + " · " + PhaseName(round.Phase) + (round.Paused ? " (PAUSE)" : "") +
                " · " + round.AliveCount + " en lice / " + round.Participants.Count + " inscrits · file : " + round.NextRoundQueue.Count + "\n" +
                "Connecteur : " + _gm.Live.Name + " — " + _gm.Live.StatusText + "\n" +
                "Images/s : " + Mathf.RoundToInt(_gm.Fps) + " · joueurs enregistrés : " + _gm.Players.Registry.Count + "\n" +
                "!join acceptés : " + _gm.Commands.Accepted + " · refusés (anti-abus) : " + _gm.Commands.Rejected + "\n" +
                "Dons reçus : " + _gm.Gifts.Processor.Received + " · exécutés : " + _gm.Gifts.Executed + " · en attente : " + _gm.Gifts.Processor.PendingCount + " · doublons : " + _gm.Gifts.Processor.Duplicates + "\n" +
                "Tapotements : " + _gm.Likes + " · paliers donnés : " + _gm.Taps.Boosts + " · photos de profil : " + AvatarCache.Instance.Downloaded + " (échecs " + AvatarCache.Instance.Failed + ")" +
                (_gm.Players.LastSaveError != null ? "\nSauvegarde : " + _gm.Players.LastSaveError : ""), 330);
            Header("Commandes de la partie");
            ActionRow("Passer à la phase suivante", "Suivante", () => round.ForceNextPhase());
            ActionRow("Mettre en pause / reprendre", "Pause", () => _gm.SetPaused(!round.Paused));
            Header("Tester les événements (pendant un combat)");
            ActionRow("Pluie de météores", "Lancer", () => _gm.Abilities.MeteorStorm(1));
            ActionRow("Tornade", "Lancer", () => _gm.Abilities.Tornado());
            ActionRow("Énergie pour toutes les toupies", "Lancer", () => _gm.Abilities.CollectiveBoost(3));
        }

        void BuildRound()
        {
            var r = C.round;
            Header("Joueurs");
            Stepper("Joueurs maximum par manche", () => r.maxParticipants, v => r.maxParticipants = (int)v, 5, 100, 5, F0);
            Stepper("Joueurs minimum pour lancer", () => r.minParticipants, v => r.minParticipants = (int)v, 2, 20, 1, F0);
            Toggle("Réinscrire automatiquement les joueurs actifs", () => r.autoRejoin, v => r.autoRejoin = v);
            Stepper("Joueur inactif après", () => C.live.inactivityMinutes, v => C.live.inactivityMinutes = v, 1, 60, 1, v => v.ToString("0") + " min");
            Header("Durées");
            Stepper("Inscriptions", () => r.lobbySeconds, v => r.lobbySeconds = v, 10, 120, 5, Sec);
            Stepper("Compte à rebours", () => r.countdownSeconds, v => r.countdownSeconds = v, 3, 15, 1, Sec);
            Stepper("Durée du combat avant la mort subite", () => r.maxFightSeconds, v => r.maxFightSeconds = v, 60, 600, 15, Sec);
            Stepper("Écran du vainqueur", () => r.victorySeconds, v => r.victorySeconds = v, 4, 30, 1, Sec);
            Stepper("Entrée possible après le lancement", () => r.lateJoinSeconds, v => r.lateJoinSeconds = v, 0, 60, 5, Sec);
            Stepper("Mort subite : perte d'énergie", () => r.suddenDeathDecay, v => r.suddenDeathDecay = v, 1, 10, 0.5f, v => "×" + v.ToString("0.0"));
        }

        void BuildCombat()
        {
            var t = C.tuning; var fx = C.effects; var a = C.arena;
            Header("Physique");
            Stepper("Dégâts des chocs", () => t.damage, v => t.damage = v, 0.25f, 3f, 0.05f, Pct);
            Stepper("Force des projections", () => t.knockback, v => t.knockback = v, 0.25f, 3f, 0.05f, Pct);
            Stepper("Perte d'énergie naturelle", () => t.spinDecay, v => t.spinDecay = v, 0.25f, 3f, 0.05f, Pct);
            Stepper("Vitesse des toupies", () => t.speed, v => t.speed = v, 0.5f, 2f, 0.05f, Pct);
            Stepper("Élan au lancement", () => t.launchPower, v => t.launchPower = v, 3f, 15f, 0.5f, F1);
            Stepper("Taille des toupies", () => t.topScale, v => t.topScale = v, 0.8f, 2.2f, 0.05f, v => (v / 1.35f * 100f).ToString("0") + " %");
            Header("Pouvoirs et événements");
            Stepper("Rayon des ondes de choc", () => fx.shockwaveRadius, v => fx.shockwaveRadius = v, 2f, 8f, 0.5f, v => v.ToString("0.0") + " m");
            Stepper("Dégâts des ondes de choc", () => fx.shockwaveDamage, v => fx.shockwaveDamage = v, 0f, 0.2f, 0.01f, Pct);
            Stepper("Nombre de météores", () => fx.meteorCount, v => fx.meteorCount = (int)v, 2, 20, 1, F0);
            Stepper("Dégâts des météores", () => fx.meteorDamage, v => fx.meteorDamage = v, 0f, 0.3f, 0.01f, Pct);
            Header("Arène (au prochain lancement du jeu)");
            Stepper("Rayon de l'arène", () => a.radius, v => a.radius = v, 12f, 24f, 1f, v => v.ToString("0") + " m");
            Stepper("Nombre d'ouvertures (éjections)", () => a.pocketCount, v => a.pocketCount = (int)v, 0, 8, 1, F0);
            Stepper("Largeur des ouvertures", () => a.pocketDegrees, v => a.pocketDegrees = v, 10f, 40f, 2f, v => v.ToString("0") + "°");
        }

        void BuildTaps()
        {
            var fx = C.effects;
            Toggle("Tapotements activés", () => fx.tapsEnabled, v => fx.tapsEnabled = v);
            Stepper("Tapotements pour un palier", () => fx.tapsPerBoost, v => fx.tapsPerBoost = (int)v, 1, 100, 1, F0);
            Stepper("Croissance par palier", () => fx.tapGrowth, v => fx.tapGrowth = v, 0f, 0.05f, 0.005f, v => "+" + (v * 100f).ToString("0.0") + " %");
            Stepper("Énergie par palier", () => fx.tapSpinBoost, v => fx.tapSpinBoost = v, 0f, 0.1f, 0.005f, v => "+" + (v * 100f).ToString("0.0") + " %");
            Stepper("Coup de vitesse", () => fx.tapSpeedSeconds, v => fx.tapSpeedSeconds = v, 0f, 6f, 0.5f, Sec);
            Stepper("Taille maximale", () => C.tuning.maxSize, v => C.tuning.maxSize = v, 1f, 3f, 0.1f, v => "×" + v.ToString("0.0"));
        }

        void BuildGifts()
        {
            var fx = C.effects; var g = C.gifts;
            Header("Général");
            Toggle("Cadeaux activés", () => fx.giftsEnabled, v => fx.giftsEnabled = v);
            Toggle("Un cadeau fait entrer le spectateur dans l'arène", () => fx.giftJoins, v => fx.giftJoins = v);
            Toggle("Événements d'arène (météores, tornade)", () => fx.arenaEvents, v => fx.arenaEvents = v);
            Stepper("Intensité des actions", () => fx.giftIntensity, v => fx.giftIntensity = v, 0.5f, 3f, 0.25f, Pct);
            Header("Limites anti-abus");
            Stepper("Actions maximum par seconde", () => g.maxActivationsPerSecond, v => g.maxActivationsPerSecond = v, 1, 10, 1, F0);
            Stepper("Actions maximum par spectateur et par minute", () => g.maxPerUserPerMinute, v => g.maxPerUserPerMinute = (int)v, 5, 100, 5, F0);
            Stepper("Actions maximum pour une série", () => g.maxActivationsPerGift, v => g.maxActivationsPerGift = (int)v, 1, 20, 1, F0);
            Header("Énergie pour tous et surpuissance");
            Stepper("Énergie par cadeau (pour tous)", () => fx.collectiveSpinPerGift, v => fx.collectiveSpinPerGift = v, 0f, 0.05f, 0.005f, Pct);
            Stepper("Plafond d'énergie pour tous", () => fx.collectiveSpinCap, v => fx.collectiveSpinCap = v, 0f, 0.3f, 0.01f, Pct);
            Stepper("Durée de la surpuissance", () => fx.powerUpSeconds, v => fx.powerUpSeconds = v, 2, 20, 1, Sec);
            Stepper("Force de la surpuissance", () => fx.powerUpDamage, v => fx.powerUpDamage = v, 1f, 2f, 0.05f, Pct);
            Header("Action de chaque cadeau");
            foreach (var rule in g.rules.Where(x => !string.IsNullOrEmpty(x.giftName)))
            {
                var rr = rule;
                Choice(rr.giftName, GiftEffects.EffectNames,
                    () => Math.Max(0, Array.IndexOf(GiftEffects.EffectIds, rr.effect)),
                    i => rr.effect = GiftEffects.EffectIds[i], GiftIcons.Get(rr.giftName));
            }
            Header("Cadeaux inconnus (selon leur valeur)");
            foreach (var rule in g.rules.Where(x => string.IsNullOrEmpty(x.giftName)).OrderByDescending(x => x.minDiamonds))
            {
                var rr = rule;
                Choice("À partir de " + rr.minDiamonds + " pièce(s)", GiftEffects.EffectNames,
                    () => Math.Max(0, Array.IndexOf(GiftEffects.EffectIds, rr.effect)),
                    i => rr.effect = GiftEffects.EffectIds[i]);
            }
        }

        void BuildDisplay()
        {
            var d = C.display;
            Header("À l'écran");
            Toggle("Pseudos au-dessus des toupies", () => d.showNames, v => d.showNames = v);
            Stepper("Taille des pseudos", () => d.nameSize, v => d.nameSize = v, 0.6f, 1.6f, 0.1f, Pct);
            Toggle("Liste des cadeaux et interactions (droite)", () => d.showGiftLegend, v => d.showGiftLegend = v);
            Toggle("Photos de profil sur les toupies", () => d.showPhotos, v => d.showPhotos = v);
            Toggle("Liste des joueurs (gauche)", () => d.showPlayerList, v => d.showPlayerList = v);
            Toggle("Annonces des cadeaux", () => d.showGiftPopups, v => d.showGiftPopups = v);
            Toggle("Cartes d'arrivée des joueurs", () => d.showJoinCards, v => d.showJoinCards = v);
            Toggle("Grandes annonces (manche, compte à rebours…)", () => d.showBanners, v => d.showBanners = v);
            Toggle("Bandeau d'aide en bas", () => d.showHint, v => d.showHint = v);
            Toggle("Ligne d'état (connexion, images/s)", () => d.showStatus, v => d.showStatus = v);
            Header("Caméra");
            Toggle("Secousses", () => d.cameraShake, v => d.cameraShake = v);
            Stepper("Force des secousses", () => d.shakeStrength, v => d.shakeStrength = v, 0.25f, 2f, 0.25f, Pct);
            Stepper("Rotation de la caméra", () => d.cameraOrbit, v => d.cameraOrbit = v, 0f, 3f, 0.25f, v => v <= 0f ? "fixe" : Pct(v));
            Stepper("Distance de la caméra", () => d.cameraDistance, v => d.cameraDistance = v, 0.8f, 1.3f, 0.05f, Pct);
            Header("Effets et qualité");
            Toggle("Traînées lumineuses", () => d.trails, v => d.trails = v);
            Stepper("Quantité de particules", () => d.particles, v => d.particles = v, 0.25f, 1.5f, 0.25f, Pct);
            Toggle("Lueurs (bloom)", () => d.bloom, v => d.bloom = v);
            Toggle("Ombres", () => d.shadows, v => d.shadows = v);
            Stepper("Résolution de rendu", () => d.renderScale, v => d.renderScale = v, 0.6f, 1f, 0.05f, Pct);
            Choice("Images par seconde", new[] { "30", "60" }, () => d.fps <= 30 ? 0 : 1, i => d.fps = i == 0 ? 30 : 60);
        }

        void BuildSound()
        {
            var a = C.audio;
            Toggle("Musique", () => a.music, v => a.music = v);
            Stepper("Volume de la musique", () => a.musicVolume, v => a.musicVolume = v, 0f, 1f, 0.05f, Pct);
            Toggle("Bruitages", () => a.sfx, v => a.sfx = v);
            Stepper("Volume des bruitages", () => a.sfxVolume, v => a.sfxVolume = v, 0f, 1f, 0.05f, Pct);
            Stepper("Bruit des toupies qui tournent", () => a.crowdVolume, v => a.crowdVolume = v, 0f, 1f, 0.05f, Pct);
            ActionRow("Écouter un bruitage", "Tester", () => Audio.AudioManager.Play("gift", 1f, 0f, 0f));
        }

        void BuildLive()
        {
            var l = C.live;
            Info(() => "État : " + _gm.Live.StatusText + "\nAdresse du pont : " + l.bridgeUrl, 110);
            ActionRow("Live TikTok (pont Termux)", "Activer", () => _gm.SetConnector("bridge"));
            ActionRow("Mode démo (spectateurs simulés)", "Activer", () => _gm.SetConnector("demo"));
            Stepper("Spectateurs simulés en démo", () => l.demoSpectators, v => l.demoSpectators = (int)v, 5, 100, 5, F0);
            ActionRow("Relancer la connexion actuelle", "Relancer", () => _gm.SetConnector(_gm.Live.IsDemo ? "demo" : "bridge"));
        }

        void BuildRewards()
        {
            var r = C.rewards;
            Header("Expérience");
            Stepper("Participation", () => r.xpParticipation, v => r.xpParticipation = (int)v, 0, 200, 5, Xp);
            Stepper("Par élimination", () => r.xpPerElimination, v => r.xpPerElimination = (int)v, 0, 200, 5, Xp);
            Stepper("Par seconde de survie", () => r.xpPerSurvivedSecond, v => r.xpPerSurvivedSecond = (int)v, 0, 5, 1, Xp);
            Stepper("Victoire", () => r.xpWin, v => r.xpWin = (int)v, 0, 1000, 25, Xp);
            Stepper("Podium (2e et 3e)", () => r.xpTop3, v => r.xpTop3 = (int)v, 0, 500, 10, Xp);
            Header("Éclats");
            Stepper("Participation", () => r.coinsParticipation, v => r.coinsParticipation = (int)v, 0, 100, 5, F0);
            Stepper("Par élimination", () => r.coinsPerElimination, v => r.coinsPerElimination = (int)v, 0, 50, 1, F0);
            Stepper("Victoire", () => r.coinsWin, v => r.coinsWin = (int)v, 0, 500, 10, F0);
        }

        void BuildSystem()
        {
            Info(() => "Spin Arena " + Application.version + " · " + SystemInfo.deviceModel + " · " + SystemInfo.graphicsDeviceName +
                       "\nOptions enregistrées dans : " + GameConfig.OverridePath, 110);
            ActionRow("Enregistrer maintenant (joueurs et options)", "Enregistrer", () => { _gm.Players.Save(); C.SaveOverride(); });
            ActionRow("Remettre toutes les options d'origine", "Réinitialiser", () => { C.ResetToDefaults(); _gm.ApplyDisplay(); Show("Système"); }, true);
            ActionRow("Effacer tous les joueurs (niveaux, victoires)", "Effacer", () => _gm.Players.ClearAll(), true);
            ActionRow("Quitter le jeu", "Quitter", Application.Quit, true);
        }

        // ------------------------------------------------------------------ briques

        static string F0(float v) => v.ToString("0");
        static string F1(float v) => v.ToString("0.0");
        static string Sec(float v) => v.ToString("0.#") + " s";
        static string Pct(float v) => (v * 100f).ToString("0") + " %";
        static string Xp(float v) => v.ToString("0") + " XP";

        static string PhaseName(RoundPhase p) =>
            p == RoundPhase.Lobby ? "inscriptions" : p == RoundPhase.Countdown ? "compte à rebours" : p == RoundPhase.Fighting ? "combat" : "victoire";

        void Changed()
        {
            C.SaveOverride();
            _gm.ApplyDisplay();
            _gm.Hud.RefreshLegend();
        }

        RectTransform Row(float height = 84)
        {
            var rt = UiFactory.Rect("Row", _content, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0, height));
            var bg = rt.gameObject.AddComponent<Image>();
            bg.sprite = UiFactory.Rounded;
            bg.type = Image.Type.Sliced;
            bg.color = new Color(1f, 1f, 1f, 0.05f);
            bg.raycastTarget = false;
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;
            return rt;
        }

        Text RowLabel(RectTransform row, string text, Texture icon = null)
        {
            float x = 20;
            if (icon != null)
            {
                var pic = UiFactory.Picture("Icon", row, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(14, 0), 64);
                pic.texture = icon;
                x = 90;
            }
            return UiFactory.Label("Label", row, text, 28, TextAnchor.MiddleLeft, Color.white, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(x, 0), new Vector2(760, 80), false);
        }

        void Header(string text)
        {
            var row = UiFactory.Rect("Header", _content, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0, 64));
            var le = row.gameObject.AddComponent<LayoutElement>();
            le.minHeight = 64; le.preferredHeight = 64;
            UiFactory.Label("Text", row, text.ToUpperInvariant(), 26, TextAnchor.LowerLeft, UiFactory.Accent, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(8, 6), new Vector2(1200, 50), false);
        }

        void Info(Func<string> text, float height)
        {
            var row = Row(height);
            var t = UiFactory.Label("Info", row, "", 24, TextAnchor.UpperLeft, new Color(0.85f, 0.92f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20, -12), new Vector2(1360, height - 20), false);
            _refreshers.Add(() => t.text = text());
        }

        void Stepper(string label, Func<float> get, Action<float> set, float min, float max, float step, Func<float, string> fmt)
        {
            var row = Row();
            RowLabel(row, label);
            var value = UiFactory.Label("Value", row, "", 30, TextAnchor.MiddleCenter, UiFactory.Gold, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-130, 0), new Vector2(200, 70), false);
            Action refresh = () => value.text = fmt(get());
            void Apply(float d)
            {
                float v = Mathf.Clamp(Mathf.Round((get() + d) / step) * step, min, max);
                set(v);
                refresh();
                Changed();
            }
            UiFactory.Button("Minus", row, "-", new Vector2(1f, 0.5f), new Vector2(-280, 0), new Vector2(90, 66), () => Apply(-step));
            UiFactory.Button("Plus", row, "+", new Vector2(1f, 0.5f), new Vector2(-55, 0), new Vector2(90, 66), () => Apply(step));
            _refreshers.Add(refresh);
        }

        void Toggle(string label, Func<bool> get, Action<bool> set)
        {
            var row = Row();
            RowLabel(row, label);
            Button b = null;
            Text txt = null;
            Action refresh = () =>
            {
                bool on = get();
                txt.text = on ? "OUI" : "NON";
                ((Image)b.targetGraphic).color = on ? new Color(0.15f, 0.6f, 0.35f, 1f) : new Color(0.45f, 0.15f, 0.18f, 1f);
            };
            b = UiFactory.Button("Toggle", row, "", new Vector2(1f, 0.5f), new Vector2(-130, 0), new Vector2(220, 66), () => { set(!get()); refresh(); Changed(); });
            txt = b.GetComponentInChildren<Text>();
            _refreshers.Add(refresh);
        }

        void Choice(string label, string[] options, Func<int> get, Action<int> set, Texture icon = null)
        {
            var row = Row();
            RowLabel(row, label, icon);
            var value = UiFactory.Label("Value", row, "", 26, TextAnchor.MiddleCenter, UiFactory.Gold, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-200, 0), new Vector2(300, 70), false);
            Action refresh = () => value.text = options[Mathf.Clamp(get(), 0, options.Length - 1)];
            UiFactory.Button("Prev", row, "<", new Vector2(1f, 0.5f), new Vector2(-400, 0), new Vector2(80, 66), () => { set((get() - 1 + options.Length) % options.Length); refresh(); Changed(); });
            UiFactory.Button("Next", row, ">", new Vector2(1f, 0.5f), new Vector2(-50, 0), new Vector2(80, 66), () => { set((get() + 1) % options.Length); refresh(); Changed(); });
            _refreshers.Add(refresh);
        }

        void ActionRow(string label, string button, Action action, bool needsConfirm = false)
        {
            var row = Row();
            RowLabel(row, label);
            Text txt = null;
            var b = UiFactory.Button("Action", row, button, new Vector2(1f, 0.5f), new Vector2(-150, 0), new Vector2(260, 66), () =>
            {
                if (needsConfirm)
                {
                    float now = Time.unscaledTime;
                    if (!_confirm.TryGetValue(label, out var t0) || now - t0 > 3f)
                    {
                        _confirm[label] = now;
                        txt.text = "Confirmer ?";
                        return;
                    }
                    _confirm.Remove(label);
                    txt.text = button;
                }
                action();
            });
            txt = b.GetComponentInChildren<Text>();
            if (needsConfirm) ((Image)b.targetGraphic).color = new Color(0.55f, 0.18f, 0.2f, 1f);
        }
    }

    /// <summary>Zone tactile invisible qui ouvre le menu des options (3 tapotements rapides).</summary>
    public sealed class OptionsHotspot : MonoBehaviour, IPointerClickHandler
    {
        public OptionsMenu Owner;
        public void OnPointerClick(PointerEventData eventData) => Owner?.Tap();
    }
}
