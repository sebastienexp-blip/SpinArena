using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SpinArena.Core.Battle;
using UnityEngine;

namespace SpinArena
{
    /// <summary>
    /// Banc d'essai automatique (lancé avec « -spinlab ») : mode démo, plusieurs manches complètes,
    /// mesures (images/s, durées, éliminations, erreurs) et captures d'écran, puis fermeture du jeu.
    /// Sert à vérifier le jeu sur un serveur de test (GitHub Actions) sans téléphone.
    /// </summary>
    public sealed class LabRunner : MonoBehaviour
    {
        string _out;
        int _targetRounds = 3;
        float _maxSeconds = 900f;
        float _start, _nextShot, _fpsMin = 999f, _fpsSum;
        int _fpsSamples, _shots, _errors, _roundsDone, _ringOuts, _spinOuts, _joins;
        readonly List<string> _lines = new List<string>();
        readonly List<float> _fightDurations = new List<float>();
        float _fightStart;
        bool _done, _tornadoDone, _tornadoShot, _reviveDone, _reviveShot, _closeupDone, _closeupShot;
        Tops.TopController _closeupTarget;

        public static bool Requested
        {
            get
            {
                foreach (var a in Environment.GetCommandLineArgs()) if (a == "-spinlab") return true;
                return false;
            }
        }

        static string Arg(string name, string fallback)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return fallback;
        }

        int _labPlayers, _labJoined;
        bool _leaveTested;

        public void Begin(GameManager gm)
        {
            _out = Arg("-labout", Path.Combine(Application.persistentDataPath, "lab"));
            Directory.CreateDirectory(_out);
            int.TryParse(Arg("-labrounds", "3"), out _targetRounds);
            float.TryParse(Arg("-labseconds", "900"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _maxSeconds);
            Application.logMessageReceived += (m, s, t) => { if (t == LogType.Exception || t == LogType.Error) { _errors++; if (_errors <= 30) _lines.Add("ERREUR : " + m); } };
            gm.Config.round.lobbySeconds = 10f;
            gm.Config.round.victorySeconds = 5f;
            gm.Config.round.maxFightSeconds = 120f;
            gm.Config.live.demoSpectators = Mathf.Max(gm.Config.live.demoSpectators, int.Parse(Arg("-labplayers", "50")));
            gm.SetConnector("demo");
            int.TryParse(Arg("-labplayers", "50"), out _labPlayers);
            var r = gm.Battle.Round;
            r.ParticipantJoined += _ => _joins++;
            r.PhaseChanged += ph =>
            {
                if (ph == RoundPhase.Fighting) { _fightStart = Time.time; Log("Combat : " + r.Participants.Count + " toupies · arène ×" + gm.Arena.Scale.ToString("0.00") + " · toupies ×" + Tops.TopController.CrowdScale.ToString("0.00") + (Tops.TopController.LowDetail ? " · effets allégés" : "")); Shot("lancement"); }
            };
            r.ParticipantEliminated += p => { if (p.reason == EliminationReason.RingOut) _ringOuts++; else _spinOuts++; };
            r.WinnerDecided += w =>
            {
                float d = Time.time - _fightStart;
                _fightDurations.Add(d);
                _roundsDone++;
                var p = gm.Players.Registry.Get(w);
                Log("Manche " + r.RoundNumber + " gagnée par " + (p != null ? p.DisplayName : "personne") + " en " + d.ToString("0") + " s");
                Shot("victoire" + _roundsDone);
            };
            _start = Time.realtimeSinceStartup;
            _nextShot = 20f;
            Log("Banc d'essai démarré (" + _targetRounds + " manches, " + SystemInfo.graphicsDeviceName + ", " + SystemInfo.processorType + ")");
        }

        void Log(string s) { _lines.Add("[" + (Time.realtimeSinceStartup - _start).ToString("0") + " s] " + s); }

        void Shot(string name)
        {
            if (_shots >= 24) return;
            _shots++;
            ScreenCapture.CaptureScreenshot(Path.Combine(_out, _shots.ToString("00") + "_" + name + ".png"));
        }

        void Update()
        {
            if (_done) return;
            float t = Time.realtimeSinceStartup - _start;
            if (t > 3f)
            {
                float fps = 1f / Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
                _fpsMin = Mathf.Min(_fpsMin, fps);
                _fpsSum += fps; _fpsSamples++;
            }
            if (t > _nextShot) { _nextShot += 25f; Shot("vue"); }
            // Essai de la tornade à chaque manche : 20 s après le lancement, capture 2 s plus tard.
            var round = GameManager.Instance.Battle.Round;
            // Beaucoup de joueurs : on les inscrit directement (20 par image) pendant l'attente.
            if (round.Phase == RoundPhase.Lobby && _labPlayers > 60)
            {
                for (int k = 0; k < 20 && _labJoined < _labPlayers * (_roundsDone + 1); k++)
                {
                    int id = _labJoined++ % _labPlayers;
                    GameManager.Instance.Battle.Join(new Core.Live.LiveUser { userId = "lab_" + id, uniqueId = "lab" + id, nickname = "Joueur" + id });
                }
                // Essai du départ du live : un joueur inscrit s'en va avant le combat.
                if (!_leaveTested && round.Participants.Count > 10)
                {
                    _leaveTested = true;
                    string who = round.Participants[3].playerId;
                    bool ok = round.Withdraw(who);
                    bool topGone = GameManager.Instance.Battle.TopOf(who) == null;
                    Log("Départ du live testé : " + (ok && topGone ? "toupie retirée" : "ÉCHEC (retiré=" + ok + ", toupie disparue=" + topGone + ")"));
                }
            }
            if (round.Phase == RoundPhase.Fighting)
            {
                float ft = Time.time - _fightStart;
                if (!_tornadoDone && ft > 20f) { _tornadoDone = true; GameManager.Instance.Abilities.Tornado(); Log("Tornade déclenchée"); }
                if (_tornadoDone && !_tornadoShot && ft > 22.5f) { _tornadoShot = true; Shot("tornade"); }
                // Gros plan sur une toupie en combat (contrôle du rendu des toupies).
                if (!_closeupDone && ft > 8f)
                {
                    _closeupTarget = GameManager.Instance.Battle.Fighting().FirstOrDefault();
                    if (_closeupTarget != null) { _closeupDone = true; CameraRig.CameraDirector.Instance.FocusFast(_closeupTarget.transform, 1f); }
                }
                if (_closeupDone && !_closeupShot && ft > 11f)
                {
                    _closeupShot = true;
                    Shot("gros_plan");
                    CameraRig.CameraDirector.Instance.Focus(null, 0f);
                }
                // Essai du retour en jeu : un joueur éliminé envoie un Doughnut.
                if (!_reviveDone && ft > 30f)
                {
                    var out_ = round.Participants.FirstOrDefault(p => !p.alive);
                    if (out_ != null)
                    {
                        _reviveDone = true;
                        var prof = GameManager.Instance.Players.Registry.Get(out_.playerId);
                        GameManager.Instance.Gifts.OnGift(new Core.Live.LiveEvent { type = Core.Live.LiveEventType.Gift, msgId = "lab-revive" + round.RoundNumber, user = new Core.Live.LiveUser { userId = out_.playerId, nickname = prof?.nickname }, giftName = "Doughnut", diamondCount = 30, repeatCount = 1, repeatEnd = true }, Time.realtimeSinceStartupAsDouble);
                        Log("Cadeau d'un joueur éliminé (" + prof?.nickname + ")");
                    }
                }
                if (_reviveDone && !_reviveShot && ft > 33f) { _reviveShot = true; Shot("retour"); Log("Joueur revenu en jeu : " + (round.Participants.Any(p => p.revives > 0 && p.alive) ? "oui" : "non")); }
            }
            else if (round.Phase == RoundPhase.Lobby)
            {
                _tornadoDone = false; _tornadoShot = false; _reviveDone = false; _reviveShot = false; _closeupDone = false; _closeupShot = false;
            }
            if (_roundsDone >= _targetRounds || t > _maxSeconds) Finish();
        }

        void Finish()
        {
            _done = true;
            var gm = GameManager.Instance;
            var sb = new StringBuilder();
            sb.AppendLine("# Banc d'essai SPIN ARENA");
            sb.AppendLine();
            sb.AppendLine("- Manches terminées : " + _roundsDone + " / " + _targetRounds);
            sb.AppendLine("- Inscriptions : " + _joins + " · éjections : " + _ringOuts + " · à court d'énergie : " + _spinOuts);
            if (_fightDurations.Count > 0)
            {
                float sum = 0; foreach (var d in _fightDurations) sum += d;
                sb.AppendLine("- Durée moyenne d'un combat : " + (sum / _fightDurations.Count).ToString("0") + " s");
            }
            sb.AppendLine("- Images/s : moyenne " + (_fpsSamples > 0 ? (_fpsSum / _fpsSamples).ToString("0") : "?") + ", minimum " + _fpsMin.ToString("0"));
            sb.AppendLine("- Commandes acceptées : " + gm.Commands.Accepted + " · refusées : " + gm.Commands.Rejected);
            sb.AppendLine("- Dons simulés reçus : " + gm.Gifts.Processor.Received + " · exécutés : " + gm.Gifts.Executed + " · doublons : " + gm.Gifts.Processor.Duplicates);
            sb.AppendLine("- Actions : sur la toupie du donateur " + gm.Gifts.Personal + " · sur une autre toupie " + gm.Gifts.ForOthers + " · retours en jeu " + gm.Gifts.Revives + " · énergie pour tous " + gm.Gifts.Collective);
            sb.AppendLine("- Ralentis cinématiques : " + (CameraRig.CinematicDirector.Instance != null ? CameraRig.CinematicDirector.Instance.Played : 0));
            sb.AppendLine("- Erreurs : " + _errors);
            sb.AppendLine();
            foreach (var l in _lines) sb.AppendLine("    " + l);
            File.WriteAllText(Path.Combine(_out, "journal.md"), sb.ToString());
            Shot("fin");
            Invoke(nameof(Quit), 1.5f);
        }

        void Quit() => Application.Quit(_roundsDone >= _targetRounds && _errors == 0 ? 0 : 2);
    }
}
