using System;
using SpinArena.Arena;
using SpinArena.Battle;
using SpinArena.CameraRig;
using SpinArena.Core.Battle;
using SpinArena.Core.Live;
using SpinArena.Core.Tops;
using SpinArena.Live;
using SpinArena.Rendering;
using SpinArena.UI;
using SpinArena.VFX;
using UnityEngine;

namespace SpinArena
{
    /// <summary>
    /// Point d'entrée : crée et relie tous les systèmes (arène, caméra, effets, interface, joueurs, battle royale,
    /// pouvoirs, connecteur du live, commandes, cadeaux, administration). Une seule instance, créée au démarrage.
    /// </summary>
    public sealed class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        public GameConfig Config { get; private set; }
        public TopCatalog Catalog { get; private set; }
        public ArenaManager Arena { get; private set; }
        public BattleRoyaleManager Battle { get; private set; }
        public AbilityManager Abilities { get; private set; }
        public PlayerManager Players { get; private set; }
        public HudManager Hud { get; private set; }
        public ILiveConnector Live { get; private set; }
        public ChatCommandManager Commands { get; private set; }
        public GiftManager Gifts { get; private set; }
        public float Fps { get; private set; }

        const string PrefConnector = "spin.connector";
        float _fpsAccum; int _fpsFrames;
        float _statusTimer;
        int _likes;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoStart()
        {
            if (Instance != null) return;
            new GameObject("SpinArena").AddComponent<GameManager>();
        }

        void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Screen.orientation = ScreenOrientation.LandscapeLeft;
            Application.runInBackground = true;
            Time.fixedDeltaTime = 1f / 60f;
            Physics.defaultSolverIterations = 8;
            Physics.bounceThreshold = 1.5f;
            Application.logMessageReceived += OnLog;

            Config = GameConfig.Load();
            Catalog = TopCatalog.CreateDefault();

            Arena = Child<ArenaManager>("Arena");
            Arena.Build(Config.arena);
            var cam = Child<CameraDirector>("Camera");
            cam.Configure(Config.arena.radius);
            RenderingSetup.Apply(cam.Cam);
            Child<VfxManager>("VFX");
            Child<AvatarCache>("Avatars");
            Hud = Child<HudManager>("UI");
            Players = Child<PlayerManager>("Players");
            Players.Init(Catalog, Config.rewards);
            Battle = Child<BattleRoyaleManager>("Battle");
            Battle.Init(Arena, Players, Config, Hud);
            Abilities = Child<AbilityManager>("Abilities");
            Abilities.Init(Battle, Config.effects);
            Commands = new ChatCommandManager(Battle, Abilities, Players, Hud, Config.commands);
            Gifts = new GiftManager(Config.gifts, Battle, Abilities, Players, Hud);
            Child<AdminPanel>("Admin").Init(this);

            if (LabRunner.Requested) gameObject.AddComponent<LabRunner>().Begin(this);
            else SetConnector(PlayerPrefs.GetString(PrefConnector, Config.live.connector));
        }

        T Child<T>(string name) where T : Component
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            return go.AddComponent<T>();
        }

        /// <summary>Change de connecteur (« bridge » ou « demo ») ; le choix est mémorisé.</summary>
        public void SetConnector(string kind)
        {
            if (Live != null) { Live.EventReceived -= OnLiveEvent; Live.Disconnect(); }
            Live = kind == "demo"
                ? new DemoLiveConnector(Config.live.demoSpectators, () => Battle.Round.Phase)
                : (ILiveConnector)new BridgeLiveConnector(Config.live.bridgeUrl);
            Live.EventReceived += OnLiveEvent;
            Live.Connect();
            PlayerPrefs.SetString(PrefConnector, kind == "demo" ? "demo" : "bridge");
            PlayerPrefs.Save();
            Hud.Feed(Live.IsDemo ? "Mode démo activé (spectateurs simulés)" : "Connexion au live TikTok via le pont Termux…", null, new Color(0.7f, 0.85f, 1f));
        }

        static double Now => Time.realtimeSinceStartupAsDouble;

        void OnLiveEvent(LiveEvent e)
        {
            switch (e.type)
            {
                case LiveEventType.Chat: Commands.OnChat(e, Now); break;
                case LiveEventType.Gift: Gifts.OnGift(e, Now); break;
                case LiveEventType.Like: _likes += Mathf.Max(1, e.likeCount); break;
                case LiveEventType.Follow: Hud.Feed(e.user.DisplayName + " <color=#80D8FF>s'abonne</color>, merci !", Hud.AvatarOf(e.user.Key)); break;
                case LiveEventType.Status:
                    if (e.status == "connected") Hud.Feed("<color=#69F0AE>Live TikTok connecté</color>", null);
                    else if (e.status == "disconnected") Hud.Feed("<color=#FF8A80>Live TikTok déconnecté, reconnexion…</color>", null);
                    break;
            }
        }

        void Update()
        {
            double now = Now;
            try { Live?.Pump(now); }
            catch (Exception ex) { Debug.LogException(ex); }
            Gifts.Tick(now);

            _fpsAccum += Time.unscaledDeltaTime; _fpsFrames++;
            if (_fpsAccum >= 1f) { Fps = _fpsFrames / _fpsAccum; _fpsAccum = 0f; _fpsFrames = 0; }

            _statusTimer -= Time.unscaledDeltaTime;
            if (_statusTimer <= 0f)
            {
                _statusTimer = 1f;
                Hud.SetStatus(Live.StatusText + "  ·  " + Mathf.RoundToInt(Fps) + " i/s  ·  " + Players.Registry.Count + " joueurs", Live.IsDemo);
            }
        }

        public void SetPaused(bool paused)
        {
            Battle.Round.Paused = paused;
            Time.timeScale = paused ? 0f : 1f;
            Hud.Banner(paused ? "PAUSE" : "REPRISE", null, paused ? 9999f : 1f);
        }

        public int Likes => _likes;

        // Journal des erreurs (fichier sur le téléphone) : utile pour comprendre un problème après un live.
        System.IO.StreamWriter _log;
        void OnLog(string msg, string stack, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Warning) return;
            try
            {
                if (_log == null)
                {
                    string p = System.IO.Path.Combine(Application.persistentDataPath, "spinarena-log.txt");
                    if (System.IO.File.Exists(p) && new System.IO.FileInfo(p).Length > 2_000_000) System.IO.File.Delete(p);
                    _log = new System.IO.StreamWriter(p, true) { AutoFlush = true };
                }
                _log.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + type + "] " + msg);
                if (type == LogType.Exception) _log.WriteLine(stack);
            }
            catch (Exception) { /* le journal ne doit jamais faire planter le jeu */ }
        }

        void OnDestroy()
        {
            Live?.Disconnect();
            Application.logMessageReceived -= OnLog;
            _log?.Dispose();
        }
    }
}
