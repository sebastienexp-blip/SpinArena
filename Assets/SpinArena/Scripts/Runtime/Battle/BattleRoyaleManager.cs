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
            Round.ParticipantWithdrawn += OnWithdrawn;
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

        /// <summary>Range les toupies inscrites sur des cercles de lancement (autant de cercles que nécessaire).</summary>
        void LayoutStaged()
        {
            if (Round.Phase != RoundPhase.Fighting) UpdateCrowd();
            var staged = _tops.Values.Where(t => t.State == TopState.Staged || t.State == TopState.Idle).ToList();
            int n = staged.Count;
            if (n <= 28)
            {
                for (int i = 0; i < n; i++) staged[i].Stage(_arena.SpawnPoint(i, n, 0.74f));
                return;
            }
            // Cercles du bord vers le centre ; l'écart entre toupies se resserre si la place manque.
            float R = _arena.Radius;
            float spacing = 2.2f * 0.42f * TopController.TopScale;
            var rings = new List<(float f, int cap)>();
            for (int tries = 0; tries < 6; tries++)
            {
                rings.Clear();
                int total = 0;
                for (float f = 0.8f; f > 0.08f; f -= Mathf.Max(0.06f, spacing / R))
                {
                    int cap = Mathf.Max(1, Mathf.FloorToInt(2f * Mathf.PI * f * R / spacing));
                    rings.Add((f, cap)); total += cap;
                }
                if (total >= n) break;
                spacing *= 0.85f;
            }
            int placed = 0;
            for (int ri = 0; ri < rings.Count && placed < n; ri++)
            {
                int cnt = Mathf.Min(rings[ri].cap, n - placed);
                float r = rings[ri].f * R, offset = ri * 0.37f;
                for (int j = 0; j < cnt; j++)
                {
                    float a = j / (float)cnt * Mathf.PI * 2f + offset;
                    staged[placed++].Stage(new Vector3(Mathf.Cos(a) * r, _arena.SurfaceHeight(r) + 0.05f, Mathf.Sin(a) * r));
                }
            }
            // Filet de sécurité : s'il reste des toupies, elles se placent au hasard au centre.
            for (; placed < n; placed++)
            {
                float a = Random.value * Mathf.PI * 2f, r = Random.value * R * 0.1f;
                staged[placed].Stage(new Vector3(Mathf.Cos(a) * r, _arena.SurfaceHeight(r) + 0.05f, Mathf.Sin(a) * r));
            }
        }

        /// <summary>
        /// Adapte l'arène au nombre de toupies : jusqu'à ~45 rien ne change ; au-delà l'arène s'agrandit
        /// (jusqu'à 2,2×) puis les toupies rapetissent un peu, et les effets coûteux sont coupés.
        /// </summary>
        void UpdateCrowd()
        {
            int n = Mathf.Max(Round.Participants.Count, _tops.Count);
            // Place disponible : ~45 toupies à la taille 1,55 ; moins quand les toupies sont plus grosses.
            float baseCap = 45f * Mathf.Pow(1.55f / Mathf.Max(0.5f, _cfg.tuning.topScale), 2f);
            float k = Mathf.Clamp(Mathf.Sqrt(n / baseCap), 1f, 3f);
            // Petite marge pour ne pas changer la taille de l'arène à chaque arrivée.
            if (Mathf.Abs(k - _arena.Scale) > 0.06f || (k == 1f && _arena.Scale != 1f)) _arena.SetScale(k);
            float capacity = baseCap * _arena.Scale * _arena.Scale;
            float crowd = n > capacity ? Mathf.Clamp(Mathf.Sqrt(capacity / n), 0.6f, 1f) : 1f;
            bool low = n > Mathf.Max(20, _cfg.display.detailLimit);
            bool changed = Mathf.Abs(crowd - TopController.CrowdScale) > 0.01f;
            TopController.CrowdScale = crowd;
            if (changed) foreach (var t in _tops.Values) t.RefreshScale();
            if (low != TopController.LowDetail) { TopController.LowDetail = low; GameManager.Instance?.ApplyDisplay(); }
        }

        /// <summary>Hors de la plateforme : dehors, tombée sous le sol, ou au-delà du muret.</summary>
        bool OffPlatform(Vector3 p)
        {
            if (_arena.IsOut(p)) return true;
            float r = new Vector2(p.x, p.z).magnitude;
            return p.y < _arena.SurfaceHeight(Mathf.Min(r, _arena.Radius)) - 1.5f || r > _arena.Radius + 0.9f * _arena.Scale;
        }

        /// <summary>Remet une toupie sur la plateforme, vers l'intérieur, sans perte.</summary>
        void PutBackOnPlatform(TopController t)
        {
            Vector3 p = t.transform.position;
            Vector2 flat = new Vector2(p.x, p.z);
            Vector2 dir = flat.sqrMagnitude > 0.01f ? flat.normalized : Random.insideUnitCircle.normalized;
            float r = _arena.Radius * 0.6f;
            t.Body.position = new Vector3(dir.x * r, _arena.SurfaceHeight(r) + 0.3f, dir.y * r);
            SpinArena.Util.Compat.SetVelocity(t.Body, new Vector3(-dir.x, 0f, -dir.y) * 2f);
            VfxManager.Instance.JoinBeam(t.Body.position, t.Glow);
        }

        /// <summary>Contrôle de sécurité au lancement : chaque toupie inscrite est bien posée sur la plateforme.</summary>
        void EnsureStagedOnPlatform()
        {
            foreach (var t in _tops.Values)
            {
                if (t.State != TopState.Staged && t.State != TopState.Idle) continue;
                Vector3 p = t.transform.position;
                float r = new Vector2(p.x, p.z).magnitude;
                bool bad = r > _arena.Radius * 0.9f || Mathf.Abs(p.y - _arena.SurfaceHeight(Mathf.Min(r, _arena.Radius))) > 0.6f;
                if (bad) { LayoutStaged(); return; }
            }
        }

        /// <summary>Le joueur a quitté le live avant le combat : sa toupie disparaît.</summary>
        void OnWithdrawn(string playerId)
        {
            if (!_tops.TryGetValue(playerId, out var top)) return;
            VfxManager.Instance.JoinBeam(top.transform.position, top.Glow);
            top.Despawn();
            _pool.Push(top);
            _tops.Remove(playerId);
            RebuildActive();
            LayoutStaged();
        }

        // ------------------------------------------------------------------ phases

        RoundPhase _prevPhase = RoundPhase.Lobby;

        void OnPhase(RoundPhase phase)
        {
            _lastCountdownSecond = -1;
            var prev = _prevPhase;
            _prevPhase = phase;
            switch (phase)
            {
                case RoundPhase.Lobby when prev == RoundPhase.Countdown:
                    // Retour à l'attente (moins de 2 toupies) : on garde les toupies inscrites, bien rangées.
                    LayoutStaged();
                    _hud.Banner("EN ATTENTE", "Il faut au moins 2 toupies", 2f, new Color(0.6f, 0.95f, 1f));
                    break;
                case RoundPhase.Lobby:
                    _hud.HideWinner();
                    CameraDirector.Instance.Focus(null, 0f);
                    foreach (var t in _tops.Values) { t.Despawn(); _pool.Push(t); }
                    _tops.Clear();
                    RebuildActive();
                    _hud.Banner("MANCHE " + Round.RoundNumber, "Viens dans le live : ta toupie entre toute seule", 3f, new Color(0.6f, 0.95f, 1f));
                    break;
                case RoundPhase.Countdown:
                    _countdownAnnounced = false;
                    EnsureStagedOnPlatform();
                    break;
                case RoundPhase.Fighting:
                    EnsureStagedOnPlatform();
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
                    if (OffPlatform(t.transform.position))
                    {
                        // Arène fermée : une toupie qui sortirait (bug physique, projection extrême) revient sur la plateforme.
                        if (_cfg.arena.ringOuts && _arena.IsOut(t.transform.position)) Round.Eliminate(t.PlayerId, EliminationReason.RingOut, t.LastAttackerId);
                        else if (!_cfg.arena.ringOuts) PutBackOnPlatform(t);
                    }
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
            if (part.reason == EliminationReason.Disconnected)
            {
                // Parti du live en plein combat : la toupie s'éteint sans grosse explosion.
                top.Eliminate(false);
                VfxManager.Instance.JoinBeam(top.transform.position, top.Glow);
                return;
            }
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
