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
