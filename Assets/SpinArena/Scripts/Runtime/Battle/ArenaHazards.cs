using System.Collections.Generic;
using SpinArena.Arena;
using SpinArena.Audio;
using SpinArena.CameraRig;
using SpinArena.Core.Battle;
using SpinArena.Tops;
using SpinArena.Util;
using SpinArena.VFX;
using UnityEngine;

namespace SpinArena.Battle
{
    /// <summary>
    /// Événements d'arène automatiques : zone qui se resserre pendant la mort subite, éclairs annoncés par une cible
    /// au sol, accélérateurs qui propulsent les toupies. Chacun est activable dans les options.
    /// </summary>
    public sealed class ArenaHazards : MonoBehaviour
    {
        BattleRoyaleManager _br;
        ArenaManager _arena;
        GameConfig _cfg;
        Transform _zoneRing, _warn;
        Material _warnMat;
        readonly List<Transform> _pads = new List<Transform>();
        readonly Dictionary<TopController, float> _padCooldown = new Dictionary<TopController, float>();
        float _zoneRadius, _nextStrike, _strikeAt;
        Vector3 _strikePos;
        bool _striking;

        public float ZoneRadius => _zoneRadius;

        public void Init(BattleRoyaleManager br, ArenaManager arena, AbilityManager abilities, GameConfig cfg)
        {
            _br = br; _arena = arena; _cfg = cfg;
            _zoneRadius = arena.Radius + 5f;
            BuildZoneRing();
            BuildWarning();
            BuildPads();
            br.Round.PhaseChanged += p =>
            {
                if (p == RoundPhase.Fighting) { _nextStrike = Time.time + 12f + Random.value * 6f; _zoneRadius = _arena.Radius + 5f; }
                if (p != RoundPhase.Fighting) { _striking = false; _warn.gameObject.SetActive(false); _zoneRing.gameObject.SetActive(false); }
            };
            br.Round.SuddenDeathStarted += () =>
            {
                if (_cfg.show.shrinkingZone) GameManager.Instance.Hud.Announce("LA ZONE SE RESSERRE !", new Color(1f, 0.35f, 0.3f));
            };
        }

        // ------------------------------------------------------------------ construction

        void BuildZoneRing()
        {
            var go = new GameObject("ShrinkZone");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = ArenaManager.ArcMesh(0.985f, 1f, 0f, 0.6f, 0f, 360f, 128);
            var mat = MaterialLibrary.Metal(Color.black, 0f, 0.5f, new Color(1f, 0.15f, 0.1f) * 4f);
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            _zoneRing = go.transform;
            go.SetActive(false);
        }

        void BuildWarning()
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(q.GetComponent<Collider>());
            q.name = "StrikeWarning";
            q.transform.SetParent(transform, false);
            q.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            _warnMat = new Material(MaterialLibrary.Additive(Color.white, RingTexture()));
            q.GetComponent<MeshRenderer>().sharedMaterial = _warnMat;
            _warn = q.transform;
            q.SetActive(false);
        }

        static Texture2D RingTexture()
        {
            const int n = 128;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Ring" };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float ring = Mathf.Clamp01(1f - Mathf.Abs(d - 0.85f) / 0.08f);
                float inner = d < 0.85f ? 0.18f : 0f;
                float cross = (Mathf.Abs(dx) < 0.04f || Mathf.Abs(dy) < 0.04f) && d < 0.6f ? 0.8f : 0f;
                px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(Mathf.Max(ring, Mathf.Max(inner, cross))) * 255f));
            }
            t.SetPixels32(px); t.Apply();
            return t;
        }

        void BuildPads()
        {
            // Chevron lumineux (flèche) pour indiquer le sens de la propulsion.
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Chevron" };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                float a = 0f;
                for (int k = 0; k < 3; k++)
                {
                    float center = 0.25f + k * 0.25f;
                    float dist = Mathf.Abs((v - center) - Mathf.Abs(u - 0.5f) * 0.6f);
                    if (Mathf.Abs(u - 0.5f) < 0.4f) a = Mathf.Max(a, Mathf.Clamp01(1f - dist / 0.05f));
                }
                px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            tex.SetPixels32(px); tex.Apply();
            var mat = MaterialLibrary.Additive(new Color(0.3f, 1f, 0.6f), tex);
            for (int i = 0; i < 3; i++)
            {
                float ang = (i / 3f) * Mathf.PI * 2f + 0.5f;
                float r = _arena.Radius * 0.55f;
                var pos = new Vector3(Mathf.Cos(ang) * r, _arena.SurfaceHeight(r) + 0.04f, Mathf.Sin(ang) * r);
                Vector3 tangent = new Vector3(-Mathf.Sin(ang), 0f, Mathf.Cos(ang));
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(q.GetComponent<Collider>());
                q.name = "SpeedPad";
                q.transform.SetParent(transform, false);
                q.transform.position = pos;
                // Le quad est couché au sol et la flèche pointe dans le sens de la rotation de l'arène.
                q.transform.rotation = Quaternion.LookRotation(Vector3.down, tangent);
                q.transform.localScale = new Vector3(2.2f, 2.2f, 1f);
                var mr = q.GetComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _pads.Add(q.transform);
            }
        }

        // ------------------------------------------------------------------ boucle

        void Update()
        {
            var round = _br.Round;
            var show = _cfg.show;
            foreach (var p in _pads) p.gameObject.SetActive(show.speedPads);
            if (round.Phase != RoundPhase.Fighting || round.Paused) return;

            if (show.speedPads) UpdatePads();
            UpdateZone(round, show);
            if (show.lightning) UpdateLightning();
        }

        void UpdatePads()
        {
            foreach (var t in _br.Fighting())
            {
                foreach (var pad in _pads)
                {
                    if ((t.transform.position - pad.position).Flat().sqrMagnitude > 1.3f * 1.3f) continue;
                    if (_padCooldown.TryGetValue(t, out var cd) && Time.time < cd) continue;
                    _padCooldown[t] = Time.time + 2.5f;
                    Vector3 dir = -pad.up.Flat().normalized; // sens indiqué par les chevrons
                    t.Push(dir * 7f * t.Body.mass);
                    t.SpeedBuff(1.5f);
                    VfxManager.Instance.Sparkle(t.transform.position, new Color(0.3f, 1f, 0.6f));
                    AudioManager.Play("turbo", 0.5f, 0.15f);
                }
            }
        }

        void UpdateZone(BattleRound round, ShowSettings show)
        {
            bool active = show.shrinkingZone && round.SuddenDeath;
            _zoneRing.gameObject.SetActive(active);
            if (!active) return;
            float min = _arena.Radius * 0.3f;
            _zoneRadius = Mathf.MoveTowards(Mathf.Min(_zoneRadius, _arena.Radius), min, Time.deltaTime * (_arena.Radius - min) / 45f);
            _zoneRing.position = new Vector3(0f, _arena.SurfaceHeight(_zoneRadius) - 0.1f, 0f);
            _zoneRing.localScale = new Vector3(_zoneRadius, 1f, _zoneRadius);
            foreach (var t in _br.Fighting())
            {
                Vector3 p = t.transform.position.Flat();
                float r = p.magnitude;
                if (r <= _zoneRadius) continue;
                t.TakeDamage(t.MaxSpin * 0.06f * Time.deltaTime, null);
                t.Body.AddForce(-p.normalized * 8f, ForceMode.Acceleration);
            }
        }

        void UpdateLightning()
        {
            if (!_striking && Time.time >= _nextStrike)
            {
                // Cible : souvent près d'une toupie, pour que l'éclair menace vraiment quelqu'un.
                Vector3 target;
                var list = new List<TopController>(_br.Fighting());
                if (list.Count > 0 && Random.value < 0.7f) target = list[Random.Range(0, list.Count)].transform.position + Random.insideUnitSphere.Flat() * 1.5f;
                else { float a = Random.value * Mathf.PI * 2f, rr = Mathf.Sqrt(Random.value) * _arena.Radius * 0.8f; target = new Vector3(Mathf.Cos(a) * rr, 0f, Mathf.Sin(a) * rr); }
                float r = target.Flat().magnitude;
                target.y = _arena.SurfaceHeight(Mathf.Min(r, _arena.Radius)) + 0.05f;
                _strikePos = target;
                _strikeAt = Time.time + 1.6f;
                _striking = true;
                _warn.gameObject.SetActive(true);
                _warn.position = target;
            }
            if (_striking)
            {
                float left = _strikeAt - Time.time;
                float pulse = 0.6f + 0.4f * Mathf.Sin(Time.time * 20f);
                _warn.localScale = Vector3.one * (5f + left * 1.5f);
                MaterialLibrary.SetColor(_warnMat, new Color(1f, 0.85f, 0.2f, pulse));
                if (left <= 0f) Strike();
            }
        }

        void Strike()
        {
            _striking = false;
            _warn.gameObject.SetActive(false);
            float interval = Mathf.Max(6f, _cfg.show.lightningInterval);
            _nextStrike = Time.time + interval * (0.7f + Random.value * 0.6f);
            var vfx = VfxManager.Instance;
            for (int i = 0; i < 8; i++) vfx.Sparkle(_strikePos + Vector3.up * (i * 1.2f), new Color(0.7f, 0.9f, 1f));
            vfx.Explosion(_strikePos + Vector3.up * 0.2f, new Color(0.6f, 0.85f, 1f), 1.1f);
            CameraDirector.Instance.Shake(0.25f);
            AudioManager.Play("shockwave", 1f, 0.1f, 0f);
            AudioManager.Play("elim", 0.6f, 0.1f);
            foreach (var t in new List<TopController>(_br.Fighting()))
            {
                Vector3 d = (t.transform.position - _strikePos).Flat();
                float dist = d.magnitude;
                if (dist > 2.6f) continue;
                float k = 1f - dist / 2.6f;
                t.TakeDamage(t.MaxSpin * 0.12f * k, null);
                t.Push((d.normalized + Vector3.up * 0.3f) * 6f * k * t.Body.mass);
            }
        }
    }
}
