using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SpinArena.Audio;
using SpinArena.CameraRig;
using SpinArena.Core.Battle;
using SpinArena.Core.Tops;
using SpinArena.Tops;
using SpinArena.UI;
using SpinArena.VFX;
using UnityEngine;

namespace SpinArena.Battle
{
    /// <summary>
    /// Mise en scène des manches : première élimination, doublés et séries, prime sur le joueur le plus redoutable,
    /// paliers de survivants, duel final, ralenti sur le coup de grâce, couronne du champion, podium, meilleur soutien,
    /// séries de victoires, montées de niveau et toupies débloquées, classement du live pendant les inscriptions,
    /// « dernière chance » et alerte avant la mort subite.
    /// </summary>
    public sealed class ShowManager : MonoBehaviour
    {
        BattleRoyaleManager _br;
        PlayerManager _players;
        HudManager _hud;
        GameConfig _cfg;

        readonly Dictionary<string, List<float>> _killTimes = new Dictionary<string, List<float>>();
        readonly Dictionary<string, int> _supportThisRound = new Dictionary<string, int>();
        readonly Dictionary<string, string> _supportNames = new Dictionary<string, string>();
        readonly Dictionary<string, int> _sessionWins = new Dictionary<string, int>();
        bool _firstBlood, _duelAnnounced, _top10, _top5, _lastChance;
        string _bountyId;
        public string ChampionId { get; private set; }
        int _lastWarnSecond = -1;
        float _boardTimer;

        public void Init(BattleRoyaleManager br, PlayerManager players, HudManager hud, GameConfig cfg)
        {
            _br = br; _players = players; _hud = hud; _cfg = cfg;
            var r = br.Round;
            r.PhaseChanged += OnPhase;
            r.ParticipantJoined += OnJoined;
            r.ParticipantEliminated += OnEliminated;
            r.WinnerDecided += OnWinner;
            br.WinnerExtra = WinnerExtra;
            br.Rewarded += OnRewarded;
        }

        ShowSettings S => _cfg.show;

        void OnPhase(RoundPhase phase)
        {
            if (phase == RoundPhase.Lobby)
            {
                _killTimes.Clear();
                _supportThisRound.Clear();
                _firstBlood = _duelAnnounced = _top10 = _top5 = _lastChance = false;
                _bountyId = null;
                _hud.TimerWarning = false;
                _boardTimer = 4f;
            }
            if (phase == RoundPhase.Fighting) _hud.TimerWarning = false;
        }

        void OnJoined(Participant part)
        {
            if (!S.championCrown || part.playerId != ChampionId) return;
            var t = _br.TopOf(part.playerId);
            if (t == null) return;
            t.SetCrown(true);
            _hud.Announce("Le champion " + t.DisplayName + " est de retour !", new Color(1f, 0.82f, 0.3f));
        }

        public void OnGiftActivated(string userId, string userName)
        {
            if (string.IsNullOrEmpty(userId)) return;
            _supportThisRound.TryGetValue(userId, out int n);
            _supportThisRound[userId] = n + 1;
            _supportNames[userId] = userName;
        }

        void OnEliminated(Participant part)
        {
            var round = _br.Round;
            var victim = _br.TopOf(part.playerId);
            string killer = part.eliminatedBy;
            var killerTop = _br.TopOf(killer);
            string kName = killerTop != null ? killerTop.DisplayName : null;

            if (S.announcements && !_firstBlood && kName != null)
            {
                _firstBlood = true;
                _hud.Announce("PREMIÈRE ÉLIMINATION : " + kName, new Color(1f, 0.45f, 0.4f));
            }

            // Prime : celui qui élimine le porteur la récupère.
            if (S.bounty && part.playerId == _bountyId)
            {
                _bountyId = null;
                if (killer != null && round.Get(killer) is Participant kp)
                {
                    kp.bounties++;
                    _hud.Announce("PRIME RÉCUPÉRÉE par " + (kName ?? "?") + " !", new Color(1f, 0.85f, 0.25f));
                    AudioManager.Play("gift", 1f, 0.2f, 0f);
                    if (killerTop != null) { VfxManager.Instance.JoinBeam(killerTop.transform.position, new Color(1f, 0.8f, 0.2f)); CinematicDirector.Instance?.Play(killerTop.transform, 0.3f, 1f, 0.85f, 0.7f); }
                }
            }

            // Doublés, triplés et séries.
            if (S.announcements && killer != null && kName != null)
            {
                if (!_killTimes.TryGetValue(killer, out var list)) _killTimes[killer] = list = new List<float>();
                float now = Time.time;
                list.Add(now);
                int recent = list.Count(x => now - x < 5f);
                int total = round.Get(killer)?.eliminations ?? list.Count;
                if (recent == 2) _hud.Announce("DOUBLÉ ! " + kName, new Color(1f, 0.7f, 0.2f));
                else if (recent >= 3) { _hud.Announce("TRIPLÉ !! " + kName, new Color(1f, 0.4f, 0.9f)); CinematicDirector.Instance?.Play(killerTop.transform, 0.3f, 1f, 0.85f, 0.8f, true); }
                else if (total == 3 || total == 5 || total == 8 || total == 12) _hud.Announce(kName + " : SÉRIE DE " + total + " !", new Color(0.5f, 0.9f, 1f));
            }

            UpdateBounty();

            int alive = round.AliveCount;
            if (round.Phase != RoundPhase.Fighting) return;
            if (S.announcements && !_top10 && alive == 10 && round.Participants.Count > 14) { _top10 = true; _hud.Announce("TOP 10 !", Color.white); }
            if (S.announcements && !_top5 && alive == 5 && round.Participants.Count > 7) { _top5 = true; _hud.Announce("TOP 5 !", new Color(1f, 0.85f, 0.4f)); }
            if (S.finalDuel && !_duelAnnounced && alive == 2)
            {
                _duelAnnounced = true;
                var two = _br.Fighting().Take(2).ToList();
                if (two.Count == 2)
                {
                    _hud.Banner("DUEL FINAL", two[0].DisplayName + "  contre  " + two[1].DisplayName, 2.5f, new Color(1f, 0.4f, 0.35f));
                    AudioManager.Play("go", 0.8f, 1f, 0f);
                    if (CinematicDirector.Instance == null || !CinematicDirector.Instance.Play(two[0].transform, 0.35f, 1.2f, 0.6f, 0.7f, true))
                        CameraDirector.Instance.Focus(two[0].transform, 0.45f);
                }
            }
        }

        /// <summary>La prime va au survivant qui a le plus d'éliminations (au moins 3).</summary>
        void UpdateBounty()
        {
            if (!S.bounty) return;
            var round = _br.Round;
            var leader = round.Participants.Where(p => p.alive && p.eliminations >= 3).OrderByDescending(p => p.eliminations).FirstOrDefault();
            string id = leader?.playerId;
            if (id == _bountyId) return;
            if (_bountyId != null) _br.TopOf(_bountyId)?.SetBounty(false);
            _bountyId = id;
            var t = _br.TopOf(id);
            if (t != null)
            {
                t.SetBounty(true);
                _hud.Announce("PRIME sur " + t.DisplayName + " (" + leader.eliminations + " éliminations)", new Color(1f, 0.3f, 0.3f));
            }
        }

        void OnWinner(string winnerId)
        {
            _hud.TimerWarning = false;
            if (winnerId != null)
            {
                _sessionWins.TryGetValue(winnerId, out int n);
                _sessionWins[winnerId] = n + 1;
            }
            if (ChampionId != null) _br.TopOf(ChampionId)?.SetCrown(false);
            ChampionId = winnerId;
            if (S.slowMotion)
            {
                var w = _br.TopOf(winnerId);
                CinematicDirector.Instance?.Play(w != null ? w.transform : null, 0.25f, 1.6f, 1f, 0.9f, true);
            }
        }

        IEnumerator SlowMotion()
        {
            Time.timeScale = 0.3f;
            yield return new WaitForSecondsRealtime(1.6f);
            if (!_br.Round.Paused) Time.timeScale = 1f;
        }

        /// <summary>Lignes ajoutées à la carte du vainqueur : série, podium, meilleur soutien.</summary>
        string WinnerExtra(string winnerId)
        {
            var sb = new StringBuilder();
            var p = _players.Registry.Get(winnerId);
            if (p != null && p.winStreak >= 2) sb.Append("\n<color=#FF8A65>" + p.winStreak + " victoires d'affilée !</color>");
            if (S.podium)
            {
                var round = _br.Round;
                string Name(int place) { var x = round.Participants.FirstOrDefault(q => q.placement == place); return x == null ? null : _players.Registry.Get(x.playerId)?.DisplayName; }
                string second = Name(2), third = Name(3);
                if (second != null) sb.Append("\n<color=#D6DEE8>2e : " + second + "</color>" + (third != null ? "   <color=#E39A5B>3e : " + third + "</color>" : ""));
            }
            if (S.topSupporter && _supportThisRound.Count > 0)
            {
                var best = _supportThisRound.OrderByDescending(kv => kv.Value).First();
                sb.Append("\n<color=#FF80AB>Meilleur soutien : " + _supportNames[best.Key] + "</color>");
            }
            return sb.ToString();
        }

        /// <summary>Montées de niveau et toupies débloquées, annoncées avec la photo du joueur.</summary>
        void OnRewarded(List<(Core.Players.PlayerProfile player, Core.Progression.RewardOutcome outcome, Participant part)> results)
        {
            if (!S.levelUps) return;
            foreach (var r in results.OrderByDescending(x => x.outcome.newLevel))
            {
                foreach (var id in r.outcome.unlockedTops)
                {
                    var def = _players.Catalog.Get(id);
                    if (def != null) _hud.GiftPopup(r.player.DisplayName, _hud.AvatarOf(r.player.id), "débloque " + def.name + " (" + TopDefinition.RarityName(def.rarity) + ")");
                }
                if (r.outcome.levelsGained > 0) _hud.GiftPopup(r.player.DisplayName, _hud.AvatarOf(r.player.id), "passe niveau " + r.outcome.newLevel + " !");
            }
        }

        void Update()
        {
            var round = _br.Round;
            if (round.Phase == RoundPhase.Lobby)
            {
                // Classement des champions du live, affiché régulièrement pendant les inscriptions.
                _boardTimer -= Time.unscaledDeltaTime;
                if (S.sessionBoard && _boardTimer <= 0f && _sessionWins.Count > 0)
                {
                    _boardTimer = 14f;
                    var sb = new StringBuilder();
                    int i = 0;
                    foreach (var kv in _sessionWins.OrderByDescending(x => x.Value).Take(6))
                    {
                        i++;
                        string name = _players.Registry.Get(kv.Key)?.DisplayName ?? "?";
                        string medal = i == 1 ? "<color=#FFD54A>1.</color>" : i == 2 ? "<color=#D6DEE8>2.</color>" : i == 3 ? "<color=#E39A5B>3.</color>" : i + ".";
                        sb.AppendLine(medal + " " + name + "  <color=#5CE1FF>" + kv.Value + " victoire" + (kv.Value > 1 ? "s" : "") + "</color>");
                    }
                    _hud.ShowBoard("CHAMPIONS DU LIVE", sb.ToString(), 7f);
                }
                if (S.announcements && !_lastChance && round.Participants.Count >= round.Settings.minParticipants && round.PhaseRemaining <= 6f && round.PhaseRemaining > 0f)
                {
                    _lastChance = true;
                    _hud.Announce("DERNIÈRE CHANCE : écris !join", new Color(1f, 0.85f, 0.3f));
                }
            }
            else if (round.Phase == RoundPhase.Fighting && !round.SuddenDeath)
            {
                int s = Mathf.CeilToInt(round.PhaseRemaining);
                bool warn = S.timerWarning && s <= 10;
                _hud.TimerWarning = warn;
                if (warn && s != _lastWarnSecond && s > 0) { _lastWarnSecond = s; AudioManager.Play("beep", 0.5f, 0.5f, 0f); }
            }
            else _hud.TimerWarning = false;
        }
    }
}
