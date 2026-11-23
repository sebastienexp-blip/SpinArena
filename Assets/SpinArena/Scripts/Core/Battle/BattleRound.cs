using System;
using System.Collections.Generic;
using System.Linq;

namespace SpinArena.Core.Battle
{
    public enum RoundPhase
    {
        Lobby,       // inscriptions (!join) ouvertes
        Countdown,   // compte à rebours avant le lancer
        Fighting,    // combat en cours
        Victory      // annonce du gagnant
    }

    public enum EliminationReason
    {
        OutOfSpin,   // énergie de rotation à zéro
        RingOut,     // éjecté de l'arène
        Disconnected // retiré par l'administrateur
    }

    [Serializable]
    public sealed class RoundSettings
    {
        public int maxParticipants = 50;
        public int minParticipants = 2;
        public float lobbySeconds = 25f;
        public float countdownSeconds = 5f;
        public float maxFightSeconds = 150f;
        public float victorySeconds = 9f;
        /// <summary>Pendant les N premières secondes du combat, un nouveau joueur entre encore directement.</summary>
        public float lateJoinSeconds = 10f;
        /// <summary>Multiplicateur de perte d'énergie après le temps réglementaire (mort subite).</summary>
        public float suddenDeathDecay = 4f;
        /// <summary>Les participants d'une manche sont réinscrits automatiquement à la suivante.</summary>
        public bool autoRejoin = true;
    }

    public sealed class Participant
    {
        public string playerId;
        public bool alive = true;
        public int eliminations;          // adversaires éliminés par ce joueur
        public float survivedSeconds;
        public int placement;             // 1 = vainqueur, attribué à l'élimination
        public EliminationReason? reason;
        public string eliminatedBy;
    }

    /// <summary>
    /// Machine à états d'une manche de battle royale, sans aucune dépendance au moteur.
    /// Le moteur (Unity) appelle <see cref="Tick"/> et signale les éliminations ; la manche décide
    /// des transitions, du classement final et du gagnant.
    /// </summary>
    public sealed class BattleRound
    {
        public RoundSettings Settings { get; }
        public RoundPhase Phase { get; private set; } = RoundPhase.Lobby;
        public float PhaseTime { get; private set; }
        public int RoundNumber { get; private set; } = 1;
        public bool SuddenDeath { get; private set; }
        public bool Paused { get; set; }
        public string WinnerId { get; private set; }

        readonly List<Participant> _participants = new List<Participant>();
        readonly Dictionary<string, Participant> _byId = new Dictionary<string, Participant>();
        readonly List<string> _nextRoundQueue = new List<string>();

        public event Action<RoundPhase> PhaseChanged;
        public event Action<Participant> ParticipantJoined;
        public event Action<string> QueuedForNextRound;
        public event Action<Participant> ParticipantEliminated;
        public event Action<string> WinnerDecided;   // null = égalité (tous éliminés en même temps)
        public event Action SuddenDeathStarted;

        public BattleRound(RoundSettings settings) { Settings = settings ?? new RoundSettings(); }

        public IReadOnlyList<Participant> Participants => _participants;
        public IReadOnlyList<string> NextRoundQueue => _nextRoundQueue;
        public int AliveCount => _participants.Count(p => p.alive);
        public Participant Get(string id) => id != null && _byId.TryGetValue(id, out var p) ? p : null;

        public float PhaseRemaining
        {
            get
            {
                switch (Phase)
                {
                    case RoundPhase.Lobby: return Math.Max(0f, Settings.lobbySeconds - PhaseTime);
                    case RoundPhase.Countdown: return Math.Max(0f, Settings.countdownSeconds - PhaseTime);
                    case RoundPhase.Fighting: return Math.Max(0f, Settings.maxFightSeconds - PhaseTime);
                    default: return Math.Max(0f, Settings.victorySeconds - PhaseTime);
                }
            }
        }

        public enum JoinResult { Joined, AlreadyIn, Queued, AlreadyQueued, Full }

        /// <summary>Demande d'entrée d'un joueur (commande !join).</summary>
        public JoinResult RequestJoin(string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) throw new ArgumentException(nameof(playerId));
            if (_byId.ContainsKey(playerId)) return JoinResult.AlreadyIn;

            bool canEnterNow = Phase == RoundPhase.Lobby || Phase == RoundPhase.Countdown
                               || (Phase == RoundPhase.Fighting && PhaseTime <= Settings.lateJoinSeconds && !SuddenDeath);
            if (canEnterNow && _participants.Count < Settings.maxParticipants)
            {
                var p = new Participant { playerId = playerId };
                _participants.Add(p);
                _byId[playerId] = p;
                ParticipantJoined?.Invoke(p);
                return JoinResult.Joined;
            }
            if (_nextRoundQueue.Contains(playerId)) return JoinResult.AlreadyQueued;
            if (_nextRoundQueue.Count >= Settings.maxParticipants * 2) return JoinResult.Full;
            _nextRoundQueue.Add(playerId);
            QueuedForNextRound?.Invoke(playerId);
            return JoinResult.Queued;
        }

        /// <summary>Signale une élimination détectée par la physique. Ignore les doublons.</summary>
        public bool Eliminate(string playerId, EliminationReason reason, string byPlayerId = null)
        {
            if (Phase != RoundPhase.Fighting) return false;
            var p = Get(playerId);
            if (p == null || !p.alive) return false;
            p.alive = false;
            p.reason = reason;
            p.survivedSeconds = PhaseTime;
            p.placement = AliveCount + 1;
            if (!string.IsNullOrEmpty(byPlayerId) && byPlayerId != playerId)
            {
                p.eliminatedBy = byPlayerId;
                var killer = Get(byPlayerId);
                if (killer != null) killer.eliminations++;
            }
            ParticipantEliminated?.Invoke(p);
            CheckForWinner();
            return true;
        }

        /// <summary>Forcer le passage à la phase suivante (interface administrateur).</summary>
        public void ForceNextPhase()
        {
            switch (Phase)
            {
                case RoundPhase.Lobby: if (_participants.Count >= 1) SetPhase(RoundPhase.Countdown); break;
                case RoundPhase.Countdown: SetPhase(RoundPhase.Fighting); break;
                case RoundPhase.Fighting: DecideByStanding(); break;
                case RoundPhase.Victory: StartNewRound(); break;
            }
        }

        /// <summary>Le moteur fournit, au moment voulu, un classement (meilleur en premier) des survivants pour départager.</summary>
        public Func<IEnumerable<string>> StandingProvider;

        public void Tick(float dt)
        {
            if (Paused || dt <= 0f) return;
            PhaseTime += dt;
            switch (Phase)
            {
                case RoundPhase.Lobby:
                    if (PhaseTime >= Settings.lobbySeconds)
                    {
                        if (_participants.Count >= Settings.minParticipants) SetPhase(RoundPhase.Countdown);
                        else PhaseTime = Settings.lobbySeconds; // on attend des joueurs, le chrono reste à 0
                    }
                    break;
                case RoundPhase.Countdown:
                    if (PhaseTime >= Settings.countdownSeconds) SetPhase(RoundPhase.Fighting);
                    break;
                case RoundPhase.Fighting:
                    foreach (var p in _participants) if (p.alive) p.survivedSeconds = PhaseTime;
                    if (!SuddenDeath && PhaseTime >= Settings.maxFightSeconds)
                    {
                        SuddenDeath = true;
                        SuddenDeathStarted?.Invoke();
                    }
                    // Garde-fou : la mort subite ne peut pas durer indéfiniment.
                    if (SuddenDeath && PhaseTime >= Settings.maxFightSeconds + 60f) DecideByStanding();
                    break;
                case RoundPhase.Victory:
                    if (PhaseTime >= Settings.victorySeconds) StartNewRound();
                    break;
            }
        }

        /// <summary>Multiplicateur de perte d'énergie à appliquer par le moteur.</summary>
        public float DecayFactor => SuddenDeath ? Settings.suddenDeathDecay : 1f;

        void CheckForWinner()
        {
            int alive = AliveCount;
            if (alive > 1) return;
            var last = _participants.FirstOrDefault(p => p.alive);
            if (last != null) last.placement = 1;
            DeclareWinner(last?.playerId);
        }

        void DecideByStanding()
        {
            var order = StandingProvider?.Invoke()?.Where(id => Get(id)?.alive == true).ToList()
                        ?? _participants.Where(p => p.alive).Select(p => p.playerId).ToList();
            for (int i = order.Count - 1; i >= 1; i--)
            {
                var p = Get(order[i]);
                p.alive = false; p.reason = EliminationReason.OutOfSpin; p.placement = i + 1; p.survivedSeconds = PhaseTime;
                ParticipantEliminated?.Invoke(p);
            }
            foreach (var p in _participants.Where(p => p.alive && (order.Count == 0 || p.playerId != order[0])).ToList())
            {
                p.alive = false; p.reason = EliminationReason.OutOfSpin; p.placement = order.Count + 1;
                ParticipantEliminated?.Invoke(p);
            }
            var w = order.Count > 0 ? Get(order[0]) : null;
            if (w != null) w.placement = 1;
            DeclareWinner(w?.playerId);
        }

        void DeclareWinner(string id)
        {
            if (Phase != RoundPhase.Fighting) return;
            WinnerId = id;
            SetPhase(RoundPhase.Victory);
            WinnerDecided?.Invoke(id);
        }

        /// <summary>Filtre de réinscription automatique (ex. : joueur encore actif dans le chat). Null = tout le monde.</summary>
        public Func<string, bool> KeepForNextRound;

        void StartNewRound()
        {
            RoundNumber++;
            var previous = Settings.autoRejoin
                ? _participants.Select(p => p.playerId).Where(id => KeepForNextRound == null || KeepForNextRound(id)).ToList()
                : new List<string>();
            _participants.Clear();
            _byId.Clear();
            WinnerId = null;
            SuddenDeath = false;
            SetPhase(RoundPhase.Lobby);
            var queued = _nextRoundQueue.ToList();
            _nextRoundQueue.Clear();
            // Les joueurs qui attendaient passent en premier : personne ne reste bloqué en file si l'arène est pleine.
            foreach (var id in queued) RequestJoin(id);
            foreach (var id in previous) RequestJoin(id);
        }

        void SetPhase(RoundPhase phase)
        {
            Phase = phase;
            PhaseTime = 0f;
            PhaseChanged?.Invoke(phase);
        }
    }
}
