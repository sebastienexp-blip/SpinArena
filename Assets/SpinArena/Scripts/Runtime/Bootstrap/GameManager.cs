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
        public TapManager Taps { get; private set; }
        public ShowManager Show { get; private set; }
        public ArenaHazards Hazards { get; private set; }
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
            Application.runInBackground = true;
            Time.fixedDeltaTime = 1f / 60f;
            Physics.defaultSolverIterations = 8;
            Physics.bounceThreshold = 1.5f;
            Application.logMessageReceived += OnLog;

            Config = GameConfig.Load();
            ApplyOrientation();
            Catalog = TopCatalog.CreateDefault();

            Arena = Child<ArenaManager>("Arena");
            Arena.Build(Config.arena);
            var cam = Child<CameraDirector>("Camera");
            Child<CinematicDirector>("Cinematics");
            cam.Configure(Arena.Radius);
            Arena.ScaleChanged += k => cam.Configure(Arena.Radius);
            RenderingSetup.Apply(cam.Cam);
            Child<VfxManager>("VFX");
            Child<SpectacleFx>("SFX");
            Child<AvatarCache>("Avatars");
            Child<Audio.AudioManager>("Audio");
            Hud = Child<HudManager>("UI");
            Players = Child<PlayerManager>("Players");
            Players.Init(Catalog, Config.rewards);
            Battle = Child<BattleRoyaleManager>("Battle");
            Battle.Init(Arena, Players, Config, Hud);
            Abilities = Child<AbilityManager>("Abilities");
            Abilities.Init(Battle, Config.effects);
            Show = Child<ShowManager>("Show");
            Show.Init(Battle, Players, Hud, Config);
            Hazards = Child<ArenaHazards>("Hazards");
            Hazards.Init(Battle, Arena, Abilities, Config);
            Commands = new ChatCommandManager(Battle, Players, Config.commands);
            Taps = new TapManager(Battle, Abilities, Players, Config.effects);
            Gifts = new GiftManager(Config.gifts, Battle, Abilities, Players, Hud);
            Child<OptionsMenu>("Options").Init(this);

            ApplyDisplay();
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
            if (Live is DemoLiveConnector demo) demo.AlivePicker = () =>
            {
                var alive = new System.Collections.Generic.List<Tops.TopController>(Battle.Fighting());
                if (alive.Count == 0) return null;
                var t = alive[UnityEngine.Random.Range(0, alive.Count)];
                var p = Players.Registry.Get(t.PlayerId);
                return new LiveUser { userId = t.PlayerId, uniqueId = p?.uniqueId, nickname = t.DisplayName };
            };
            Live.EventReceived += OnLiveEvent;
            Live.Connect();
            PlayerPrefs.SetString(PrefConnector, kind == "demo" ? "demo" : "bridge");
            PlayerPrefs.Save();
        }

        static double Now => Time.realtimeSinceStartupAsDouble;

        void OnLiveEvent(LiveEvent e)
        {
            // Tout signe de vie (arrivée, commentaire, tapotement, don, partage, abonnement) repousse le départ.
            if (e.user != null && !string.IsNullOrEmpty(e.user.Key)) Players.MarkActive(e.user.Key);
            AutoJoin(e);
            switch (e.type)
            {
                case LiveEventType.Chat: Commands.OnChat(e, Now); break;
                case LiveEventType.Gift: Gifts.OnGift(e, Now); break;
                case LiveEventType.Follow:
                    if (Config.live.followJoins && Battle.TopOf(e.user.Key) == null) Battle.Join(e.user);
                    break;
                case LiveEventType.Like:
                    int n = Mathf.Max(1, e.likeCount);
                    _likes += n;
                    Taps.OnTaps(e.user, n);
                    break;
            }
        }

        float _departTimer = 5f;
        readonly System.Collections.Generic.List<string> _leaving = new System.Collections.Generic.List<string>();

        /// <summary>
        /// Départs du live. TikTok n'envoie aucun signal quand un spectateur s'en va : un joueur sans aucun signe
        /// de vie depuis « leaveAfterMinutes » est considéré comme parti et perd sa toupie (il la retrouve en revenant).
        /// </summary>
        void CheckDepartures()
        {
            float minutes = Config.live.leaveAfterMinutes;
            if (minutes <= 0f || Live == null || Live.IsDemo || Battle == null) return;
            _leaving.Clear();
            foreach (var part in Battle.Round.Participants)
                if (part.alive && !BattleRoyaleManager.IsBot(part.playerId) && !Players.IsActive(part.playerId, minutes)) _leaving.Add(part.playerId);
            foreach (var q in Battle.Round.NextRoundQueue)
                if (!BattleRoyaleManager.IsBot(q) && !Players.IsActive(q, minutes)) _leaving.Add(q);
            foreach (var id in _leaving)
                if (Battle.Round.Withdraw(id)) Debug.Log("[SpinArena] " + id + " a quitté le live : toupie retirée");
        }

        /// <summary>Entrée automatique : arrivée dans le live, ou premier commentaire / tapotement.</summary>
        void AutoJoin(LiveEvent e)
        {
            if (e.user == null || string.IsNullOrEmpty(e.user.Key) || Live.IsDemo) return;
            var l = Config.live;
            bool want = (e.type == LiveEventType.Member && l.viewerJoins)
                        || ((e.type == LiveEventType.Chat || e.type == LiveEventType.Like) && l.interactionJoins);
            if (!want || Battle.TopOf(e.user.Key) != null) return;
            if (Battle.Round.Get(e.user.Key) != null || System.Linq.Enumerable.Contains(Battle.Round.NextRoundQueue, e.user.Key)) return;
            Battle.Join(e.user);
        }

        void Update()
        {
            double now = Now;
            try { Live?.Pump(now); }
            catch (Exception ex) { Debug.LogException(ex); }
            Gifts.Tick(now);
            _departTimer -= Time.unscaledDeltaTime;
            if (_departTimer <= 0f) { _departTimer = 5f; CheckDepartures(); }

            _fpsAccum += Time.unscaledDeltaTime; _fpsFrames++;
            if (_fpsAccum >= 1f) { Fps = _fpsFrames / _fpsAccum; _fpsAccum = 0f; _fpsFrames = 0; }

            _statusTimer -= Time.unscaledDeltaTime;
            if (_statusTimer <= 0f)
            {
                _statusTimer = 1f;
                Hud.SetTaps(_likes);
                Hud.SetStatus(Live.StatusText + "  ·  " + Mathf.RoundToInt(Fps) + " i/s  ·  " + Players.Registry.Count + " joueurs", Live.IsDemo);
            }
        }

        /// <summary>Applique les options d'affichage (qualité, ombres, images/s).</summary>
        public void ApplyDisplay()
        {
            var d = Config.display;
            Application.targetFrameRate = d.fps <= 30 ? 30 : 60;
            // Ombres coupées quand il y a des centaines de toupies (le téléphone garde des images fluides).
            if (Arena.KeyLight != null) Arena.KeyLight.shadows = d.shadows && !Tops.TopController.LowDetail ? LightShadows.Soft : LightShadows.None;
            RenderingSetup.ApplyQuality(d.bloom, d.renderScale);
        }

        /// <summary>Tourne l'écran selon l'option (portrait ou paysage). L'interface et la caméra suivent d'elles-mêmes.</summary>
        public void ApplyOrientation()
        {
            Screen.orientation = Config.display.orientation == 0 ? ScreenOrientation.LandscapeLeft : ScreenOrientation.Portrait;
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
