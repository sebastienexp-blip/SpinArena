using System.Collections.Generic;
using SpinArena.Core.Battle;
using UnityEngine;

namespace SpinArena.Audio
{
    /// <summary>
    /// Sons (créés avec ElevenLabs, dans Resources/Audio) : bruitages mutualisés et musique d'ambiance dynamique.
    /// La musique change selon la phase (inscriptions / combat) et monte en intensité quand il reste peu de survivants.
    /// </summary>
    public sealed class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        const string PrefMusic = "spin.music", PrefSfx = "spin.sfx";
        readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        readonly Dictionary<string, float> _lastPlay = new Dictionary<string, float>();
        readonly List<AudioSource> _sfx = new List<AudioSource>();
        AudioSource _musicA, _musicB, _spinLoop;
        AudioSource _current;
        string _currentTrack;
        float _tension, _musicVolume = 0.55f;
        int _next;

        public bool MusicOn { get; private set; } = true;
        public bool SfxOn { get; private set; } = true;
        public int ClipCount => _clips.Count;

        void Awake()
        {
            Instance = this;
            foreach (var c in Resources.LoadAll<AudioClip>("Audio")) _clips[c.name] = c;
            for (int i = 0; i < 14; i++)
            {
                var s = gameObject.AddComponent<AudioSource>();
                s.playOnAwake = false;
                _sfx.Add(s);
            }
            _musicA = MakeLoop(); _musicB = MakeLoop(); _spinLoop = MakeLoop();
            MusicOn = PlayerPrefs.GetInt(PrefMusic, 1) == 1;
            SfxOn = PlayerPrefs.GetInt(PrefSfx, 1) == 1;
            if (_clips.TryGetValue("spin_loop", out var loop)) { _spinLoop.clip = loop; _spinLoop.volume = 0f; _spinLoop.Play(); }
            if (_clips.Count == 0) Debug.LogWarning("[SpinArena] Aucun son trouvé dans Resources/Audio");
        }

        AudioSource MakeLoop()
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.loop = true; s.playOnAwake = false; s.volume = 0f;
            return s;
        }

        /// <summary>Joue un bruitage. minInterval évite les avalanches (ex. 50 chocs à la fois).</summary>
        public static void Play(string name, float volume = 1f, float minInterval = 0.05f, float pitchJitter = 0.06f)
        {
            var a = Instance;
            if (a == null || !a.SfxOn || !a._clips.TryGetValue(name, out var clip)) return;
            float now = Time.unscaledTime;
            if (a._lastPlay.TryGetValue(name, out var last) && now - last < minInterval) return;
            a._lastPlay[name] = now;
            var src = a._sfx[a._next];
            a._next = (a._next + 1) % a._sfx.Count;
            src.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
            src.PlayOneShot(clip, Mathf.Clamp01(volume));
        }

        /// <summary>Musique selon la phase de la manche ; tension 0..1 (peu de survivants, mort subite).</summary>
        public void SetPhase(RoundPhase phase, float tension)
        {
            _tension = Mathf.Clamp01(tension);
            string track = phase == RoundPhase.Fighting || phase == RoundPhase.Victory ? "music_battle" : "music_lobby";
            if (track != _currentTrack && _clips.TryGetValue(track, out var clip))
            {
                _currentTrack = track;
                var target = _current == _musicA ? _musicB : _musicA;
                target.clip = clip;
                target.time = 0f;
                target.Play();
                _current = target;
            }
        }

        public void SetMusic(bool on) { MusicOn = on; PlayerPrefs.SetInt(PrefMusic, on ? 1 : 0); PlayerPrefs.Save(); }
        public void SetSfx(bool on) { SfxOn = on; PlayerPrefs.SetInt(PrefSfx, on ? 1 : 0); PlayerPrefs.Save(); }

        /// <summary>Volume du ronronnement des toupies (0..1 selon le nombre de toupies en jeu).</summary>
        public void SetSpinCrowd(float amount) => _spinTarget = SfxOn ? Mathf.Clamp01(amount) * 0.35f : 0f;
        float _spinTarget;

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            float vol = MusicOn ? _musicVolume * (0.85f + 0.3f * _tension) : 0f;
            foreach (var s in new[] { _musicA, _musicB })
            {
                float goal = s == _current ? vol : 0f;
                s.volume = Mathf.MoveTowards(s.volume, goal, dt * 0.6f);
                if (s != _current && s.volume <= 0f && s.isPlaying) s.Stop();
            }
            // Plus la fin approche, plus la musique accélère légèrement.
            if (_current != null) _current.pitch = Mathf.MoveTowards(_current.pitch, 1f + 0.07f * _tension, dt * 0.05f);
            _spinLoop.volume = Mathf.MoveTowards(_spinLoop.volume, _spinTarget, dt * 0.5f);
        }
    }
}
