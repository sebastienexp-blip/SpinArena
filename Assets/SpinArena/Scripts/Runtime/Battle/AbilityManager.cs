using System.Collections;
using System.Linq;
using SpinArena.Audio;
using SpinArena.CameraRig;
using SpinArena.Core.Elements;
using SpinArena.Core.Tops;
using SpinArena.Tops;
using SpinArena.Util;
using SpinArena.VFX;
using UnityEngine;

namespace SpinArena.Battle
{
    /// <summary>
    /// Pouvoirs : commandes de combat (!attaque, !bouclier, !turbo, !special), capacités spéciales des six éléments
    /// et effets des cadeaux. Tous les effets sont plafonnés : ils pèsent sur le combat sans jamais le décider seuls.
    /// </summary>
    public sealed class AbilityManager : MonoBehaviour
    {
        BattleRoyaleManager _br;
        EffectSettings _fx;

        public void Init(BattleRoyaleManager br, EffectSettings fx) { _br = br; _fx = fx; }

        static VfxManager Vfx => VfxManager.Instance;

        public bool Attack(TopController t) { if (!Ok(t)) return false; t.BuffAttack(_fx.attackBuff, _fx.attackSeconds); Vfx.Sparkle(t.transform.position, new Color(1f, 0.4f, 0.2f)); return true; }
        public bool Shield(TopController t) { if (!Ok(t)) return false; t.BuffShield(_fx.shieldReduction, _fx.shieldSeconds); Vfx.Shockwave(t.transform.position, new Color(0.5f, 0.8f, 1f)); return true; }
        public bool Turbo(TopController t) { if (!Ok(t)) return false; AudioManager.Play("turbo", 0.8f, 0.1f); t.Turbo(_fx.turboImpulse, 3f); Vfx.Sparkle(t.transform.position, t.Glow); return true; }

        static bool Ok(TopController t) => t != null && t.State == TopState.Fighting;

        /// <summary>Capacité spéciale de l'élément de la toupie.</summary>
        public bool Special(TopController t, float power = 1f)
        {
            if (!Ok(t)) return false;
            Vector3 p = t.transform.position;
            Color c = t.Glow;
            switch (t.Def.element)
            {
                case Element.Electricity: // Surcharge : accélération + décharge sur les deux adversaires les plus proches.
                    t.Turbo(_fx.turboImpulse * 1.2f, 3f);
                    t.BuffAttack(1.3f, 4f);
                    foreach (var o in Nearest(t, 2, 7f))
                    {
                        o.TakeDamage(o.MaxSpin * 0.05f * power, t.PlayerId);
                        Vfx.Impact(Vector3.Lerp(p, o.transform.position, 0.5f) + Vector3.up * 0.4f, c, Color.white, 80f);
                        Vfx.Impact(o.transform.position + Vector3.up * 0.4f, c, Color.white, 60f);
                    }
                    break;
                case Element.Fire: // Explosion de zone.
                    Shockwave(t, _fx.shockwaveRadius * 1.15f, _fx.shockwaveDamage * 1.2f * power, 7f);
                    Vfx.Explosion(p + Vector3.up * 0.3f, c, 0.8f);
                    break;
                case Element.Ice: // Bouclier de glace + gel des adversaires proches.
                    t.BuffShield(0.45f, 5f);
                    foreach (var o in Nearest(t, 6, 4.5f)) o.Body.SetVelocity(o.Body.GetVelocity() * 0.3f);
                    Vfx.Shockwave(p, c);
                    break;
                case Element.Nature: // Régénération + racines (immobilise un instant les voisins).
                    t.AddSpin(t.MaxSpin * 0.12f * power);
                    foreach (var o in Nearest(t, 6, 4f)) { o.Body.SetVelocity(Vector3.zero); o.TakeDamage(o.MaxSpin * 0.02f, t.PlayerId); }
                    Vfx.Shockwave(p, c);
                    Vfx.Sparkle(p, c);
                    break;
                case Element.Darkness: // Téléportation derrière la cible + drain d'énergie.
                    var target = Nearest(t, 1, 12f).FirstOrDefault();
                    if (target != null)
                    {
                        Vector3 behind = target.transform.position + (target.transform.position - p).Flat().normalized * 1.1f;
                        float limit = (FindArena() != null ? FindArena().Radius : 16f) * 0.85f;
                        if (behind.Flat().magnitude < limit)
                        {
                            Vfx.Explosion(p + Vector3.up * 0.3f, c, 0.4f);
                            t.Body.position = new Vector3(behind.x, Mathf.Max(target.transform.position.y, 0.3f), behind.z);
                        }
                        float drain = target.MaxSpin * 0.08f * power;
                        target.TakeDamage(drain, t.PlayerId);
                        t.AddSpin(drain * 0.8f);
                        Vfx.Impact(target.transform.position + Vector3.up * 0.4f, c, Color.black, 90f);
                    }
                    break;
                default: // Lumière : barrière sacrée + soin + onde qui repousse.
                    t.BuffShield(0.35f, 6f);
                    t.AddSpin(t.MaxSpin * 0.06f * power);
                    Shockwave(t, _fx.shockwaveRadius, _fx.shockwaveDamage * 0.5f * power, 5f);
                    break;
            }
            return true;
        }

        void Shockwave(TopController src, float radius, float dmgFraction, float impulse)
        {
            Vector3 p = src.transform.position;
            foreach (var o in _br.Fighting().ToList())
            {
                if (o == src) continue;
                Vector3 d = (o.transform.position - p).Flat();
                float dist = d.magnitude;
                if (dist > radius) continue;
                float k = 1f - dist / radius;
                o.TakeDamage(o.MaxSpin * dmgFraction * k, src.PlayerId);
                o.Push((d.normalized + Vector3.up * 0.15f) * impulse * k * o.Body.mass);
            }
            Vfx.Shockwave(p, src.Glow);
            AudioManager.Play("shockwave", 0.9f, 0.1f);
            CameraDirector.Instance.Shake(0.1f);
        }

        System.Collections.Generic.IEnumerable<TopController> Nearest(TopController t, int count, float maxDist)
        {
            Vector3 p = t.transform.position;
            return _br.Fighting().Where(o => o != t && (o.transform.position - p).Flat().magnitude <= maxDist)
                      .OrderBy(o => (o.transform.position - p).sqrMagnitude).Take(count);
        }

        // ------------------------------------------------------------------ effets des cadeaux (toupie du donateur)

        public void SpeedBoost(TopController t, float seconds) { if (Ok(t)) t.SpeedBuff(seconds); }

        public bool Heal(TopController t, float fraction)
        {
            if (!Ok(t)) return false;
            t.AddSpin(t.MaxSpin * Mathf.Min(fraction, 0.3f));
            AudioManager.Play("heal", 0.6f, 0.15f);
            Vfx.Sparkle(t.transform.position, new Color(0.5f, 1f, 0.6f));
            return true;
        }

        public bool Heavy(TopController t, float seconds)
        {
            if (!Ok(t)) return false;
            t.BuffHeavy(1.6f, seconds);
            Vfx.Shockwave(t.transform.position, new Color(0.8f, 0.8f, 0.85f));
            return true;
        }

        public bool GrowTop(TopController t, float amount)
        {
            if (t == null || !t.Alive) return false;
            t.Grow(amount);
            Vfx.JoinBeam(t.transform.position, t.Glow);
            AudioManager.Play("grow", 0.8f, 0.2f);
            return true;
        }

        /// <summary>Gèle l'adversaire le plus proche (ralenti, plus de déplacement).</summary>
        public bool FreezeNearest(TopController t, float seconds)
        {
            if (!Ok(t)) return false;
            var o = Nearest(t, 1, 14f).FirstOrDefault();
            if (o == null) return false;
            o.Body.SetVelocity(Vector3.zero);
            o.Slow(seconds);
            AudioManager.Play("freeze", 0.8f, 0.2f);
            Vfx.Shockwave(o.transform.position, new Color(0.6f, 0.9f, 1f));
            return true;
        }

        /// <summary>Tir : l'adversaire le plus proche est projeté loin de la toupie.</summary>
        public bool Kick(TopController t, float power)
        {
            if (!Ok(t)) return false;
            var o = Nearest(t, 1, 10f).FirstOrDefault();
            if (o == null) return false;
            Vector3 d = (o.transform.position - t.transform.position).Flat().normalized;
            o.Push((d + Vector3.up * 0.2f) * 7f * power * o.Body.mass);
            o.TakeDamage(o.MaxSpin * 0.03f * power, t.PlayerId);
            Vfx.Impact(o.transform.position + Vector3.up * 0.4f, t.Glow, Color.white, 90f);
            AudioManager.Play("impact", 1f, 0.05f);
            return true;
        }

        /// <summary>Onde de choc autour de la toupie (rayon et force réglables).</summary>
        public bool Wave(TopController t, float radiusMult, float damageMult, float impulse)
        {
            if (!Ok(t)) return false;
            Shockwave(t, _fx.shockwaveRadius * radiusMult, _fx.shockwaveDamage * damageMult, impulse);
            return true;
        }

        public bool Regen(TopController t, float total, float seconds)
        {
            if (!Ok(t)) return false;
            StartCoroutine(RegenRoutine(t, Mathf.Min(total, 0.3f), seconds));
            return true;
        }

        IEnumerator RegenRoutine(TopController t, float total, float seconds)
        {
            int ticks = Mathf.Max(1, Mathf.RoundToInt(seconds * 2f));
            for (int i = 0; i < ticks && Ok(t); i++)
            {
                t.AddSpin(t.MaxSpin * total / ticks);
                Vfx.Sparkle(t.transform.position, new Color(1f, 0.6f, 0.85f));
                yield return new WaitForSeconds(0.5f);
            }
        }

        /// <summary>Rafale : trois élans successifs vers les adversaires, dégâts renforcés.</summary>
        public bool Rafale(TopController t)
        {
            if (!Ok(t)) return false;
            t.BuffAttack(1.3f, 4f);
            StartCoroutine(RafaleRoutine(t));
            return true;
        }

        IEnumerator RafaleRoutine(TopController t)
        {
            for (int i = 0; i < 3 && Ok(t); i++)
            {
                t.Turbo(_fx.turboImpulse, 1.2f);
                AudioManager.Play("turbo", 0.8f, 0.1f);
                Vfx.Sparkle(t.transform.position, t.Glow);
                yield return new WaitForSeconds(0.45f);
            }
        }

        /// <summary>Couronne : la toupie devient royale (plus grosse, surpuissante, protégée).</summary>
        public bool Royal(TopController t)
        {
            if (!Ok(t)) return false;
            t.Grow(0.3f);
            AudioManager.Play("grow", 1f, 0.2f, 0f);
            t.BuffPower(1.3f, 10f);
            t.BuffShield(0.35f, 10f);
            Vfx.JoinBeam(t.transform.position, new Color(1f, 0.85f, 0.3f));
            Vfx.Confetti(t.transform.position);
            return true;
        }

        /// <summary>Rafale de projectiles sur les adversaires les plus proches.</summary>
        public bool Barrage(TopController t, int shots)
        {
            if (!Ok(t)) return false;
            var targets = Nearest(t, shots, 16f).ToList();
            if (targets.Count == 0) return false;
            foreach (var o in targets)
            {
                var victim = o;
                Vfx.SpawnMeteor(o.transform.position + Vector3.up * 0.2f, m =>
                {
                    Vfx.Explosion(m.transform.position, t.Glow, 0.6f);
                    if (victim.State == TopState.Fighting)
                    {
                        victim.TakeDamage(victim.MaxSpin * 0.07f, t.PlayerId);
                        victim.Push(((victim.transform.position - t.transform.position).Flat().normalized + Vector3.up * 0.2f) * 5f * victim.Body.mass);
                    }
                });
            }
            return true;
        }

        /// <summary>
        /// Tornade : un tourbillon se déplace dans l'arène pendant 7 secondes. Les toupies qu'il touche sont aspirées,
        /// tournent autour de lui et perdent un peu d'énergie. Aucun météore : c'est un événement différent.
        /// </summary>
        public void Tornado()
        {
            StartCoroutine(TornadoRoutine());
            AudioManager.Play("tornado", 1f, 1f, 0f);
        }

        IEnumerator TornadoRoutine()
        {
            var arena = FindArena();
            float R = arena != null ? arena.Radius : 16f;
            const float duration = 7f, reach = 6.5f;
            float start = Time.time, angle = Random.value * Mathf.PI * 2f, spin = 0f;
            CameraDirector.Instance.Shake(0.35f);
            var color = new Color(0.85f, 0.75f, 1f);
            while (Time.time - start < duration)
            {
                float t = (Time.time - start) / duration;
                // Le tourbillon dessine une boucle à travers l'arène.
                angle += Time.deltaTime * 0.9f;
                float r = R * 0.45f * Mathf.Sin(t * Mathf.PI);
                Vector3 c = new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r);
                c.y = arena != null ? arena.SurfaceHeight(r) : 0f;
                spin += Time.deltaTime * 9f;
                Vfx.Vortex(c, reach * 0.55f, spin, color);
                float fade = Mathf.Min(1f, Mathf.Min(t, 1f - t) * 6f);
                Vfx.ShowTornado(c, reach * 0.6f, 0.85f * fade, spin);
                foreach (var o in _br.Fighting())
                {
                    Vector3 d = (o.transform.position - c).Flat();
                    float dist = d.magnitude;
                    if (dist > reach) continue;
                    float k = 1f - dist / reach;
                    Vector3 swirl = Vector3.Cross(Vector3.up, d.normalized) * 22f - d.normalized * 9f;
                    o.Body.AddForce(swirl * k, ForceMode.Acceleration);
                    o.TakeDamage(o.MaxSpin * 0.025f * k * Time.deltaTime, null);
                }
                yield return null;
            }
            Vfx.HideTornado();
        }

        // ------------------------------------------------------------------ effets des cadeaux

        /// <summary>Bonus collectif : toutes les toupies regagnent un peu d'énergie (aucune n'est favorisée).</summary>
        public void CollectiveBoost(int count)
        {
            float frac = Mathf.Min(_fx.collectiveSpinCap, _fx.collectiveSpinPerGift * Mathf.Max(1, count));
            foreach (var t in _br.Fighting())
            {
                t.AddSpin(t.MaxSpin * frac);
                Vfx.Sparkle(t.transform.position, new Color(1f, 0.5f, 0.7f));
            }
        }

        public bool PowerUp(TopController t, int count)
        {
            if (!Ok(t)) return false;
            t.BuffPower(_fx.powerUpDamage, _fx.powerUpSeconds * Mathf.Min(2, Mathf.Max(1, count)));
            Vfx.JoinBeam(t.transform.position, t.Glow);
            return true;
        }

        /// <summary>Pluie de météores : frappe des points au hasard, touche tout le monde de la même façon.</summary>
        public void MeteorStorm(int count)
        {
            StartCoroutine(Meteors(Mathf.Clamp(_fx.meteorCount + (count - 1) * 2, 4, 16)));
        }

        public void SingleMeteor() => StartCoroutine(Meteors(1));

        IEnumerator Meteors(int n)
        {
            float R = 16f;
            var arena = FindArena();
            if (arena != null) R = arena.Radius;
            for (int i = 0; i < n; i++)
            {
                float a = Random.value * Mathf.PI * 2f, r = Mathf.Sqrt(Random.value) * R * 0.8f;
                float y = arena != null ? arena.SurfaceHeight(r) : 0f;
                var target = new Vector3(Mathf.Cos(a) * r, y + 0.2f, Mathf.Sin(a) * r);
                Vfx.SpawnMeteor(target, m => Land(m.transform.position));
                yield return new WaitForSeconds(0.55f);
            }
        }

        Arena.ArenaManager _arenaCache;
        Arena.ArenaManager FindArena()
        {
            if (_arenaCache == null) _arenaCache = GameManager.Instance != null ? GameManager.Instance.Arena : null;
            return _arenaCache;
        }

        void Land(Vector3 p)
        {
            Vfx.Explosion(p, new Color(1f, 0.5f, 0.15f), 1.3f);
            AudioManager.Play("meteor", 0.9f, 0.12f);
            CameraDirector.Instance.Shake(0.22f);
            foreach (var o in _br.Fighting().ToList())
            {
                Vector3 d = (o.transform.position - p).Flat();
                float dist = d.magnitude;
                if (dist > 3.2f) continue;
                float k = 1f - dist / 3.2f;
                o.TakeDamage(o.MaxSpin * _fx.meteorDamage * k, null);
                o.Push((d.normalized + Vector3.up * 0.3f) * 6f * k * o.Body.mass);
            }
        }
    }
}
