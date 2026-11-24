using System;
using System.IO;
using SpinArena.Core.Battle;
using SpinArena.Core.Commands;
using SpinArena.Core.Live;
using SpinArena.Core.Progression;
using UnityEngine;

namespace SpinArena
{
    [Serializable]
    public sealed class ArenaSettings
    {
        public float radius = 16f;
        public float rimHeight = 2.4f;
        public float wallHeight = 0.7f;
        public int pocketCount = 4;
        public float pocketDegrees = 22f;
        public string theme = "electric";
    }

    [Serializable]
    public sealed class LiveSettings
    {
        /// <summary>« bridge » = pont Termux (vrai live) ; « demo » = spectateurs simulés, signalés à l'écran.</summary>
        public string connector = "bridge";
        public string bridgeUrl = "ws://127.0.0.1:8787/";
        /// <summary>Nombre de spectateurs simulés en mode démo.</summary>
        public int demoSpectators = 30;
        /// <summary>Un joueur inactif depuis N minutes n'est plus réinscrit automatiquement.</summary>
        public float inactivityMinutes = 15f;
        /// <summary>Un spectateur qui s'abonne pendant le live entre dans l'arène (gratuit).</summary>
        public bool followJoins = true;
    }

    [Serializable]
    public sealed class EffectSettings
    {
        /// <summary>Bonus d'énergie collectif par rose (fraction de l'énergie max), plafonné par activation.</summary>
        public float collectiveSpinPerGift = 0.015f;
        public float collectiveSpinCap = 0.08f;
        public float powerUpSeconds = 6f;
        public float powerUpDamage = 1.25f;
        public float attackBuff = 1.15f;
        public float attackSeconds = 5f;
        public float shieldReduction = 0.25f;
        public float shieldSeconds = 4f;
        public float turboImpulse = 9f;
        public float shockwaveRadius = 4f;
        public float shockwaveDamage = 0.06f;   // fraction de l'énergie max des cibles
        public float meteorDamage = 0.10f;
        public int meteorCount = 8;
        /// <summary>Cadeaux : activés, font entrer le donateur, intensité globale, événements d'arène autorisés.</summary>
        public bool giftsEnabled = true;
        public bool giftJoins = true;
        public float giftIntensity = 1f;
        public bool arenaEvents = true;
        public bool tapsEnabled = true;
        /// <summary>Un joueur éliminé revient dans l'arène s'il envoie un cadeau d'au moins N pièces (1 fois par manche).</summary>
        public bool giftRevive = true;
        public int reviveMinDiamonds = 5;
        public float reviveEnergy = 0.5f;
        /// <summary>Tapotements : un palier tous les N tapotements d'un même spectateur.</summary>
        public int tapsPerBoost = 15;
        public float tapGrowth = 0.01f;       // +1 % de taille par palier (plafond : tuning.maxSize)
        public float tapSpinBoost = 0.02f;    // +2 % d'énergie par palier
        public float tapSpeedSeconds = 2f;    // coup de vitesse
    }

    /// <summary>Réglages de la physique et du combat (multiplicateurs, 1 = normal).</summary>
    [Serializable]
    public sealed class TuningSettings
    {
        public float damage = 1f;          // dégâts des chocs
        public float knockback = 1f;       // force des projections
        public float spinDecay = 1f;       // perte d'énergie naturelle
        public float speed = 1f;           // vitesse de déplacement
        public float topScale = 1.35f;     // taille de base des toupies
        public float maxSize = 1.8f;       // taille maximale (tapotements, cadeaux)
        public float launchPower = 7.5f;   // élan au lancement
    }

    /// <summary>Options d'affichage et de qualité graphique.</summary>
    [Serializable]
    public sealed class DisplaySettings
    {
        public bool showNames = true;
        public bool showPhotos = true;
        public bool showPlayerList = true;
        public bool showGiftPopups = true;
        public bool showGiftLegend = true;   // liste des cadeaux et de leurs actions (droite)
        public float nameSize = 1f;          // taille des pseudos (0.6 .. 1.6)
        public bool showJoinCards = true;
        public bool showHint = true;
        public bool showStatus = true;
        public bool showBanners = true;
        public bool trails = true;
        public float particles = 1f;       // 0.25 .. 1.5
        public bool cameraShake = true;
        public float shakeStrength = 1f;
        public float cameraOrbit = 1f;     // vitesse de rotation de la caméra (0 = fixe)
        public float cameraDistance = 1f;  // 0.8 .. 1.3
        public bool bloom = true;
        public bool shadows = true;
        public float renderScale = 1f;     // 0.6 .. 1
        public int fps = 60;               // 30 ou 60
    }

    [Serializable]
    public sealed class SoundSettings
    {
        public bool music = true;
        public float musicVolume = 0.55f;
        public bool sfx = true;
        public float sfxVolume = 1f;
        public float crowdVolume = 0.35f;  // ronronnement des toupies
    }

    /// <summary>Mise en scène et événements d'arène (activables dans les options).</summary>
    [Serializable]
    public sealed class ShowSettings
    {
        public bool announcements = true;   // première élimination, doublés, séries, top 10/5, dernière chance
        public bool bounty = true;          // prime sur le joueur le plus redoutable
        public bool finalDuel = true;
        public bool slowMotion = true;      // ralenti sur le coup de grâce
        public bool cinematics = true;      // ralentis avec zoom sur les grosses actions
        public float cinematicCooldown = 6f;
        public float slowMotionStrength = 1f; // 0 = pas de ralenti, 1 = normal, 1.3 = très lent
        public bool championCrown = true;   // le dernier vainqueur porte une couronne
        public bool podium = true;
        public bool topSupporter = true;
        public bool levelUps = true;        // montées de niveau et toupies débloquées
        public bool sessionBoard = true;    // champions du live pendant les inscriptions
        public bool timerWarning = true;    // alerte 10 s avant la mort subite
        public bool shrinkingZone = true;   // la zone se resserre pendant la mort subite
        public bool lightning = true;       // éclairs sur l'arène pendant les combats
        public float lightningInterval = 18f;
        public bool speedPads = true;       // accélérateurs au sol
        public bool rarityAuras = true;     // halo sous les toupies Épique et plus
        public bool lowEnergySmoke = true;  // fumée des toupies presque à court d'énergie
    }

    /// <summary>Configuration complète, modifiable sans recompiler (Resources/Config/game_config.json).</summary>
    [Serializable]
    public sealed class GameConfig
    {
        public RoundSettings round = new RoundSettings();
        public CommandLimits commands = new CommandLimits();
        public GiftSettings gifts = GiftSettings.CreateDefault();
        public RewardSettings rewards = new RewardSettings();
        public ArenaSettings arena = new ArenaSettings();
        public LiveSettings live = new LiveSettings();
        public EffectSettings effects = new EffectSettings();
        public TuningSettings tuning = new TuningSettings();
        public DisplaySettings display = new DisplaySettings();
        public SoundSettings audio = new SoundSettings();
        public ShowSettings show = new ShowSettings();

        public static GameConfig Current => GameManager.Instance != null ? GameManager.Instance.Config : _fallback ?? (_fallback = new GameConfig());
        static GameConfig _fallback;

        /// <summary>Remet toutes les options aux valeurs d'origine, sans remplacer les objets (les systèmes gardent leurs références).</summary>
        public void ResetToDefaults()
        {
            var d = new GameConfig();
            var asset = Resources.Load<TextAsset>("Config/game_config");
            if (asset != null) JsonUtility.FromJsonOverwrite(asset.text, d);
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(d.round), round);
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(d.commands), commands);
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(d.gifts), gifts);
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(d.rewards), rewards);
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(d.effects), effects);
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(d.tuning), tuning);
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(d.display), display);
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(d.audio), audio);
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(d.show), show);
            try { if (File.Exists(OverridePath)) File.Delete(OverridePath); } catch (Exception) { }
        }

        public static string OverridePath => Path.Combine(Application.persistentDataPath, "game_config.json");

        /// <summary>Charge la configuration : fichier modifié par l'administration s'il existe, sinon celle du jeu.</summary>
        public static GameConfig Load()
        {
            var cfg = new GameConfig();
            try
            {
                var asset = Resources.Load<TextAsset>("Config/game_config");
                if (asset != null) JsonUtility.FromJsonOverwrite(asset.text, cfg);
                if (File.Exists(OverridePath)) JsonUtility.FromJsonOverwrite(File.ReadAllText(OverridePath), cfg);
            }
            catch (Exception e)
            {
                Debug.LogError("[SpinArena] Configuration illisible, valeurs par défaut utilisées : " + e.Message);
            }
            if (cfg.gifts == null || cfg.gifts.rules == null || cfg.gifts.rules.Count == 0) cfg.gifts = GiftSettings.CreateDefault();
            cfg.round.maxParticipants = Mathf.Clamp(cfg.round.maxParticipants, 2, 100);
            return cfg;
        }

        public void SaveOverride()
        {
            try { File.WriteAllText(OverridePath, JsonUtility.ToJson(this, true)); }
            catch (Exception e) { Debug.LogError("[SpinArena] Sauvegarde de la configuration impossible : " + e.Message); }
        }
    }
}
