using System;
using System.Collections.Generic;
using System.IO;
using SpinArena.Core.Battle;
using SpinArena.Core.Live;
using SpinArena.Core.Players;
using SpinArena.Core.Progression;
using SpinArena.Core.Tops;
using UnityEngine;

namespace SpinArena
{
    /// <summary>
    /// Joueurs : profils permanents, activité dans le chat, récompenses de fin de manche et sauvegarde locale
    /// (écriture atomique). La sauvegarde serveur avec validation arrivera en phase 9 ; ce fichier restera la copie locale.
    /// </summary>
    public sealed class PlayerManager : MonoBehaviour
    {
        [Serializable] sealed class SaveFile { public int version = 1; public List<PlayerProfile> players = new List<PlayerProfile>(); }

        public PlayerRegistry Registry { get; private set; }
        public TopCatalog Catalog { get; private set; }
        RewardSettings _rewards;
        readonly Dictionary<string, float> _lastActive = new Dictionary<string, float>();
        bool _dirty;
        float _nextSave;
        string _path;

        public string LastSaveError { get; private set; }

        public void Init(TopCatalog catalog, RewardSettings rewards)
        {
            Catalog = catalog;
            _rewards = rewards;
            Registry = new PlayerRegistry(catalog);
            _path = Path.Combine(Application.persistentDataPath, "players.json");
            Load();
        }

        static long NowUnix => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        public PlayerProfile Touch(LiveUser user)
        {
            var p = Registry.GetOrCreate(user, NowUnix, out bool created);
            _lastActive[p.id] = Time.realtimeSinceStartup;
            _dirty |= created;
            return p;
        }

        public void MarkActive(string id) { if (id != null) _lastActive[id] = Time.realtimeSinceStartup; }

        public bool IsActive(string id, float minutes) =>
            id != null && _lastActive.TryGetValue(id, out var t) && Time.realtimeSinceStartup - t < minutes * 60f;

        public int LevelOf(string id) => Registry.Get(id)?.level ?? 1;

        /// <summary>Distribue XP et pièces à tous les participants d'une manche terminée.</summary>
        public List<(PlayerProfile player, RewardOutcome outcome, Participant part)> ApplyRound(BattleRound round)
        {
            var list = new List<(PlayerProfile, RewardOutcome, Participant)>();
            int n = round.Participants.Count;
            foreach (var part in round.Participants)
            {
                var p = Registry.Get(part.playerId);
                if (p == null) continue;
                var o = ProgressionRules.Apply(p, new RoundResult
                {
                    playerId = part.playerId, placement = part.placement, eliminations = part.eliminations,
                    survivedSeconds = part.survivedSeconds, participants = n
                }, _rewards);
                list.Add((p, o, part));
            }
            _dirty = true;
            Save();
            return list;
        }

        void Update()
        {
            if (_dirty && Time.realtimeSinceStartup > _nextSave) Save();
        }

        void OnApplicationPause(bool paused) { if (paused) Save(); }
        void OnApplicationQuit() => Save();

        public void Save()
        {
            _nextSave = Time.realtimeSinceStartup + 30f;
            if (Registry == null) return;
            try
            {
                var file = new SaveFile();
                file.players.AddRange(Registry.All);
                string tmp = _path + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(file));
                // Copie de secours puis remplacement : si l'écriture est interrompue, la copie .bak est relue au démarrage.
                if (File.Exists(_path)) { File.Copy(_path, _path + ".bak", true); File.Delete(_path); }
                File.Move(tmp, _path);
                _dirty = false;
                LastSaveError = null;
            }
            catch (Exception e)
            {
                LastSaveError = e.Message;
                Debug.LogError("[SpinArena] Sauvegarde des joueurs impossible : " + e.Message);
            }
        }

        void Load()
        {
            foreach (var path in new[] { _path, _path + ".bak" })
            {
                try
                {
                    if (!File.Exists(path)) continue;
                    var file = JsonUtility.FromJson<SaveFile>(File.ReadAllText(path));
                    if (file?.players == null) continue;
                    Registry.Load(file.players);
                    Debug.Log("[SpinArena] " + Registry.Count + " joueurs chargés depuis " + Path.GetFileName(path));
                    return;
                }
                catch (Exception e) { Debug.LogWarning("[SpinArena] Sauvegarde illisible (" + path + ") : " + e.Message); }
            }
        }
    }
}
