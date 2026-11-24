using System.Collections.Generic;
using System.Linq;
using SpinArena.Arena;
using SpinArena.Audio;
using SpinArena.CameraRig;
using SpinArena.Core.Battle;
using SpinArena.Core.Live;
using SpinArena.Core.Players;
using SpinArena.Core.Tops;
using SpinArena.Tops;
using SpinArena.UI;
using SpinArena.VFX;
using UnityEngine;

namespace SpinArena.Battle
{
    /// <summary>
    /// Pilote les manches : inscriptions, placement et lancement des toupies, détection des éliminations
    /// (énergie à zéro, éjection), mort subite, vainqueur, récompenses. Gère aussi le pool de toupies (TopManager).
    /// </summary>
    public sealed class BattleRoyaleManager : MonoBehaviour
    {
        public BattleRound Round { get; private set; }
        /// <summary>Lignes supplémentaires pour la carte du vainqueur (fournies par la mise en scène).</summary>
        public System.Func<string, string> WinnerExtra;
        public event System.Action<List<(Core.Players.PlayerProfile player, Core.Progression.RewardOutcome outcome, Participant part)>> Rewarded;
        public IReadOnlyDictionary<string, TopController> Tops => _tops;

        ArenaManager _arena;
        PlayerManager _players;
        GameConfig _cfg;
        HudManager _hud;
        readonly Dictionary<string, TopController> _tops = new Dictionary<string, TopController>();
        readonly Stack<TopController> _pool = new Stack<TopController>();
        TopController[] _active = new TopController[128];
        int _activeCount;
        int _thinkCursor;
        float _hudTimer;
        readonly List<TopController> _sorted = new List<TopController>();
        bool _countdownAnnounced;
        int _lastCountdownSecond = -1;

        public void Init(ArenaManager arena, PlayerManager players, GameConfig cfg, HudManager hud)
        {
            _arena = arena; _players = players; _cfg = cfg; _hud = hud;
            Round = new BattleRound(cfg.round);
            Round.PhaseChanged += OnPhase;
            Round.ParticipantJoined += OnJoined;
            Round.ParticipantEliminated += OnEliminated;
            Round.WinnerDecided += OnWinner;
            Round.ParticipantRevived += OnRevived;
            Round.SuddenDeathStarted += () => { _hud.Banner("MORT SUBITE", "Les toupies s'épuisent 4× plus vite !", 3f, new Color(1f, 0.35f, 0.3f)); CameraDirector.Instance.Shake(0.3f); };
            Round.StandingProvider = () => _tops.Values.Where(t => t.Alive).OrderByDescending(t => t.SpinFraction).ThenByDescending(t => t.DamageDealt).Select(t => t.PlayerId);
            Round.KeepForNextRound = id => _players.IsActive(id, cfg.live.inactivityMinutes);
            // Prépare quelques toupies d'avance pour éviter des pics de création pendant le live.
            for (int i = 0; i < 16; i++) _pool.Push(CreateTop());
        }

        // ------------------------------------------------------------------ inscriptions

        public BattleRound.JoinResult Join(LiveUser user)
        {
            var p = _players.Touch(user);
            return Round.RequestJoin(p.id);
        }

        void OnJoined(Participant part)
        {
            var p = _players.Registry.Get(part.playerId);
            var def = _players.Catalog.Get(p?.selectedTopId) ?? _players.Catalog.StarterFor(part.playerId);
            var top = _pool.Count > 0 ? _pool.Pop() : CreateTop();
            Texture avatar = _hud.AvatarOf(part.playerId);
            top.Setup(_arena, part.playerId, p?.DisplayName ?? part.playerId, def, p?.level ?? 1, null);
            if (p != null && !string.IsNullOrEmpty(p.avatarUrl))
            {
                var tex = AvatarCache.Instance.Get(p.id, p.avatarUrl, photo => { _hud.SetAvatar(part.playerId, photo); if (_tops.TryGetValue(part.playerId, out var tc)) tc.SetPhoto(photo); });
                if (tex != null && tex != AvatarCache.Instance.Generated(p.id)) { _hud.SetAvatar(p.id, tex); top.SetPhoto(tex); avatar = tex; }
            }
            _tops[part.playerId] = top;
            RebuildActive();

            if (Round.Phase == RoundPhase.Fighting)
            {
                // Arrivée en cours de combat : la toupie tombe du ciel au bord de l'arène.
                float a = Random.value * 360f;
                Vector3 pos = new Vector3(Mathf.Cos(a * Mathf.Deg2Rad), 0, Mathf.Sin(a * Mathf.Deg2Rad)) * _arena.Radius * 0.55f;
                pos.y = _arena.SurfaceHeight(_arena.Radius * 0.55f) + 0.4f;
                top.Stage(pos);
                top.Launch(6f);
                top.SetSpinFraction(_cfg.round.lateJoinEnergy);
            }
            else LayoutStaged();
            VfxManager.Instance.JoinBeam(top.transform.position, top.Glow);
            AudioManager.Play("join", 0.7f, 0.15f);
            _hud.JoinCard(top.DisplayName, avatar, def);
        }

        TopController CreateTop()
        {
            var go = new GameObject("Top");
            go.transform.SetParent(transform, false);
            go.AddComponent<Rigidbody>();
            var t = go.AddComponent<TopController>();
            t.Impact = OnImpact;
            t.WallHit = (top, p) => { VfxManager.Instance.WallSparks(p); AudioManager.Play("wall", 0.45f, 0.1f); };
            go.SetActive(false);
            return t;
        }

        void RebuildActive()
        {
            _activeCount = 0;
            foreach (var t in _tops.Values)
            {
                if (_activeCount >= _active.Length) System.Array.Resize(ref _active, _active.Length * 2);
                _active[_activeCount++] = t;
            }
        }

        /// <summary>Range les toupies inscrites sur un ou deux cercles de lancement.</summary>
        void LayoutStaged()
        {
            var staged = _tops.Values.Where(t => t.State == TopState.Staged || t.State == TopState.Idle).ToList();
            int n = staged.Count;
            int outer = n <= 28 ? n : Mathf.CeilToInt(n * 0.6f);
            for (int i = 0; i < n; i++)
            {
                bool isOuter = i < outer;
                int idx = isOuter ? i : i - outer;
                int cnt = isOuter ? outer : n - outer;
                staged[i].Stage(_arena.SpawnPoint(idx, cnt, isOuter ? 0.74f : 0.46f));
            }
        }

        // ------------------------------------------------------------------ phases

        void OnPhase(RoundPhase phase)
        {
            _lastCountdownSecond = -1;
            switch (phase)
            {
                case RoundPhase.Lobby:
                    _hud.HideWinner();
                    CameraDirector.Instance.Focus(null, 0f);
                    foreach (var t in _tops.Values) { t.Despawn(); _pool.Push(t); }
                    _tops.Clear();
                    RebuildActive();
                    _hud.Banner("MANCHE " + Round.RoundNumber, "Écris !join pour participer", 3f, new Color(0.6f, 0.95f, 1f));
                    break;
                case RoundPhase.Countdown:
                    _countdownAnnounced = false;
                    break;
                case RoundPhase.Fighting:
                    foreach (var t in _tops.Values) t.Launch(_cfg.tuning.launchPower);
                    AudioManager.Play("go", 1f, 0.5f, 0f);
                    AudioManager.Play("launch", 0.8f, 0.5f, 0f);
                    _hud.Banner("LANCEZ !", Round.Participants.Count + " toupies dans l'arène", 1.6f, new Color(1f, 0.85f, 0.3f));
                    CameraDirector.Instance.Shake(0.35f);
                    break;
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            Round.Tick(dt);
            var phase = Round.Phase;

            if (phase == RoundPhase.Countdown)
            {
                int s = Mathf.CeilToInt(Round.PhaseRemaining);
                if (s != _lastCountdownSecond && s > 0 && s <= 3)
                {
                    _lastCountdownSecond = s;
                    _hud.Banner(s.ToString(), _countdownAnnounced ? null : "Que le meilleur gagne !", 0.8f);
                    AudioManager.Play("beep", 0.9f, 0.3f, 0f);
                    _countdownAnnounced = true;
                }
            }

            if (phase == RoundPhase.Fighting)
            {
                float decay = Round.DecayFactor;
                // IA : chaque toupie réfléchit environ 8 fois par seconde, réparties sur les images.
                int per = Mathf.Max(1, Mathf.CeilToInt(_activeCount / 8f));
                for (int k = 0; k < per && _activeCount > 0; k++)
                {
                    _thinkCursor = (_thinkCursor + 1) % _activeCount;
                    _active[_thinkCursor].Think(_active, _activeCount);
                }
                for (int i = 0; i < _activeCount; i++)
                {
                    var t = _active[i];
                    if (t.State != TopState.Fighting) continue;
                    t.SetDecayFactor(decay);
                    if (_arena.IsOut(t.transform.position)) Round.Eliminate(t.PlayerId, EliminationReason.RingOut, t.LastAttackerId);
                    else if (t.Spin <= 0f) Round.Eliminate(t.PlayerId, EliminationReason.OutOfSpin, t.LastAttackerId);
                }
                int alive = Round.AliveCount;
                CameraDirector.Instance.SetTension(alive <= 3 ? 1f : alive <= 8 ? 0.5f : 0f);
            }

            _hudTimer -= dt;
            if (_hudTimer <= 0f)
            {
                _hudTimer = 0.2f;
                _sorted.Clear();
                foreach (var t in _tops.Values) if (t.Alive) _sorted.Add(t);
                _sorted.Sort((a, b) => b.SpinFraction.CompareTo(a.SpinFraction));
                _hud.SetPlayers(_sorted, _players.LevelOf);
                _hud.SetPhase(phase, Round.PhaseRemaining, Round.RoundNumber, Round.AliveCount, Round.Participants.Count, Round.NextRoundQueue.Count);
                int aliveNow = Round.AliveCount;
                float tension = phase != RoundPhase.Fighting ? 0f : Round.SuddenDeath || aliveNow <= 3 ? 1f : aliveNow <= 8 ? 0.6f : aliveNow <= 20 ? 0.3f : 0f;
                if (AudioManager.Instance != null)
                {
                    AudioManager.Instance.SetPhase(phase, tension);
                    AudioManager.Instance.SetSpinCrowd(phase == RoundPhase.Fighting ? aliveNow / 25f : 0f);
                }
            }
        }

        // ------------------------------------------------------------------ combats

        void OnImpact(TopController victim, TopController attacker, float damage, Vector3 point)
        {
            VfxManager.Instance.Impact(point, victim.Glow, attacker.Glow, damage);
            AudioManager.Play("impact", Mathf.Clamp(damage / 150f, 0.25f, 1f), 0.06f, 0.12f);
            if (damage > 90f) CameraDirector.Instance.Shake(Mathf.Min(0.25f, damage / 900f));
            // Choc énorme : ralenti avec zoom (le délai minimum évite d'en abuser).
            if (damage > 150f && Round.AliveCount > 2) { VfxManager.Instance.Explosion(point, Color.Lerp(victim.Glow, attacker.Glow, 0.5f), 0.5f); CinematicDirector.Instance?.Play(victim.transform, 0.3f, 0.7f, 0.75f, 0.55f); }
        }

        void OnEliminated(Participant part)
        {
            if (!_tops.TryGetValue(part.playerId, out var top)) return;
            bool ringOut = part.reason == EliminationReason.RingOut;
            top.Eliminate(ringOut);
            VfxManager.Instance.Explosion(top.transform.position + Vector3.up * 0.3f, top.Glow, ringOut ? 0.7f : 1f);
            CameraDirector.Instance.Shake(0.12f);
            AudioManager.Play(ringOut ? "ringout" : "elim", 0.85f, 0.08f);
            if (ringOut && Round.AliveCount <= 12 && Round.AliveCount > 1) CinematicDirector.Instance?.Play(top.transform, 0.3f, 0.8f, 0.7f, 0.6f);
        }

        void OnWinner(string winnerId)
        {
            var results = _players.ApplyRound(Round);
            AudioManager.Play("victory", 1f, 1f, 0f);
            if (winnerId != null && _tops.TryGetValue(winnerId, out var w))
            {
                CameraDirector.Instance.Focus(w.transform, 1f);
                VfxManager.Instance.Confetti(w.transform.position);
                var res = results.FirstOrDefault(r => r.player.id == winnerId);
                string details = res.part != null
                    ? res.part.eliminations + " élimination(s) · +" + res.outcome.xpGained + " XP · +" + res.outcome.coinsGained + " éclats"
                      + (res.outcome.levelsGained > 0 ? "\n<color=#FFD54A>Niveau " + res.outcome.newLevel + " !</color>" : "")
                    : "";
                _hud.ShowWinner(w.DisplayName, _hud.AvatarOf(winnerId), w.Def.name + " · " + details + (WinnerExtra?.Invoke(winnerId) ?? ""));
            }
            else _hud.Banner("ÉGALITÉ", "Aucun survivant !", 3f);
            Rewarded?.Invoke(results);
        }

        public bool Revive(string playerId) => Round.Revive(playerId);

        /// <summary>Retour en jeu : la toupie retombe au bord de l'arène avec une partie de son énergie.</summary>
        void OnRevived(Participant part)
        {
            var p = _players.Registry.Get(part.playerId);
            var def = _players.Catalog.Get(p?.selectedTopId) ?? _players.Catalog.StarterFor(part.playerId);
            if (!_tops.TryGetValue(part.playerId, out var top)) { top = _pool.Count > 0 ? _pool.Pop() : CreateTop(); _tops[part.playerId] = top; RebuildActive(); }
            top.Setup(_arena, part.playerId, p?.DisplayName ?? part.playerId, def, p?.level ?? 1, null);
            Texture photo = _hud.AvatarOf(part.playerId);
            if (photo != AvatarCache.Instance.Generated(part.playerId)) top.SetPhoto(photo);
            float a = Random.value * 360f, r = _arena.Radius * 0.5f;
            var pos = new Vector3(Mathf.Cos(a * Mathf.Deg2Rad) * r, _arena.SurfaceHeight(r) + 0.4f, Mathf.Sin(a * Mathf.Deg2Rad) * r);
            top.Stage(pos);
            top.Launch(6f);
            top.SetSpinFraction(_cfg.effects.reviveEnergy);
            VfxManager.Instance.JoinBeam(pos, top.Glow);
            _hud.Announce("RETOUR DE " + top.DisplayName + " !", new Color(0.5f, 1f, 0.6f));
        }

        public TopController TopOf(string playerId) => playerId != null && _tops.TryGetValue(playerId, out var t) ? t : null;

        public IEnumerable<TopController> Fighting() => _tops.Values.Where(t => t.State == TopState.Fighting);
    }
}
