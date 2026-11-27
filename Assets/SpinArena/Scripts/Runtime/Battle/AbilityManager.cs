using System.Collections;
using System.Linq;
using SpinArena.Audio;
using SpinArena.CameraRig;
using SpinArena.Core.Elements;
using SpinArena.Core.Tops;
using SpinArena.Tops;
using SpinArena.Rendering;
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
        static SpectacleFx Sfx => SpectacleFx.Instance;

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
                    // Éclair en chaîne : il saute d'adversaire en adversaire (jusqu'à 5).
                    ChainLightning(t, 5, 9f, 0.05f * power, c);
                    break;
                case Element.Fire: // Explosion de zone.
                    Shockwave(t, _fx.shockwaveRadius * 1.15f, _fx.shockwaveDamage * 1.2f * power, 7f);
                    Vfx.Explosion(p + Vector3.up * 0.3f, c, 0.8f);
                    Sfx?.FireImpact(p, 0.8f);
                    break;
                case Element.Ice: // Bouclier de glace + gel des adversaires proches.
                    t.BuffShield(0.45f, 5f);
                    foreach (var o in Nearest(t, 6, 4.5f)) { o.Body.SetVelocity(o.Body.GetVelocity() * 0.3f); Sfx?.Frost(o.transform, 2f); }
                    Vfx.Shockwave(p, c);
                    Sfx?.Scorch(p, 6f, 5f, new Color(0.75f, 0.92f, 1f, 0.5f));
                    Sfx?.ElectricSparks(p + Vector3.up * 0.4f, 60, new Color(0.8f, 0.95f, 1f));
                    break;
                case Element.Nature: // Régénération + racines (immobilise un instant les voisins).
                    t.AddSpin(t.MaxSpin * 0.12f * power);
                    foreach (var o in Nearest(t, 6, 4f)) { o.Body.SetVelocity(Vector3.zero); o.TakeDamage(o.MaxSpin * 0.02f, t.PlayerId); }
                    Vfx.Shockwave(p, c);
                    Vfx.Sparkle(p, c);
                    Sfx?.Spiral(p, new Color(0.5f, 1f, 0.5f), 40, 1.6f, true);
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
                            Sfx?.SmokePuff(p + Vector3.up * 0.5f, 8);
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

        /// <summary>Éclair qui part de la toupie et rebondit sur les adversaires les plus proches, un par un.</summary>
        public void ChainLightning(TopController src, int hops, float range, float dmgFraction, Color c)
        {
            if (!Ok(src)) return;
            var hit = new System.Collections.Generic.HashSet<TopController> { src };
            TopController from = src;
            for (int i = 0; i < hops; i++)
            {
                Vector3 fp = from.transform.position;
                var next = _br.Fighting().Where(o => !hit.Contains(o) && (o.transform.position - fp).Flat().magnitude <= range)
                                         .OrderBy(o => (o.transform.position - fp).sqrMagnitude).FirstOrDefault();
                if (next == null) break;
                hit.Add(next);
                Vfx.Lightning(fp + Vector3.up * 0.6f, next.transform.position + Vector3.up * 0.5f, c, 0.4f + i * 0.05f);
                next.TakeDamage(next.MaxSpin * dmgFraction * (1f - i * 0.12f), src.PlayerId);
                next.Push((next.transform.position - fp).Flat().normalized * 3f * next.Body.mass);
                from = next;
            }
            AudioManager.Play("shockwave", 0.7f, 0.15f);
            CameraDirector.Instance.Shake(0.12f);
        }

        void Shockwave(TopController src, float radius, float dmgFraction, float impulse)
        {
            Vector3 p = src.transform.position;
            int arcs = 0;
            foreach (var o in _br.Fighting().ToList())
            {
                if (o == src) continue;
                Vector3 d = (o.transform.position - p).Flat();
                float dist = d.magnitude;
                if (dist > radius) continue;
                float k = 1f - dist / radius;
                o.TakeDamage(o.MaxSpin * dmgFraction * k, src.PlayerId);
                o.Push((d.normalized + Vector3.up * 0.15f) * impulse * k * o.Body.mass);
                // Arcs électriques vers les toupies touchées (spectaculaire, sans changer les dégâts).
                if (arcs++ < 8) Vfx.Lightning(p + Vector3.up * 0.6f, o.transform.position + Vector3.up * 0.5f, src.Glow, 0.3f);
            }
            Vfx.Shockwave(p, src.Glow);
            Sfx?.DustRing(p, 30, radius * 1.4f);
            Sfx?.GroundGlow(p, 1.5f, 0.6f, src.Glow, false, radius * 2f / Mathf.Max(1f, GameConfig.Current.tuning.topScale / 1.55f));
            Sfx?.LightFlash(p + Vector3.up, src.Glow, 12f, radius * 2f, 0.35f);
            AudioManager.Play("shockwave", 0.9f, 0.1f);
            CameraDirector.Instance.Shake(0.1f);
        }

        // ------------------------------------------------------------------ toupie géante (Galaxy)

        /// <summary>
        /// La toupie du donateur grossit jusqu'à occuper presque toute l'arène pendant 9 secondes : elle se place
        /// au centre, devient invincible et repousse toutes les autres vers le bord (choc, éclairs, petits dégâts).
        /// </summary>
        public bool GiantTop(TopController t)
        {
            if (!Ok(t) || t.IsGiant) return false;
            StartCoroutine(GiantRoutine(t));
            return true;
        }

        IEnumerator GiantRoutine(TopController t)
        {
            var arena = FindArena();
            float R = arena != null ? arena.Radius : 16f;
            const float duration = 9f;
            float baseR = Mathf.Max(0.1f, t.VisualRadius);
            t.Giant(R * 0.7f / baseR, duration);
            var color = new Color(0.75f, 0.5f, 1f);
            AudioManager.Play("grow", 1f, 0.2f, 0f);
            AudioManager.Play("thunder", 0.8f, 0.2f, 0f);
            Sfx?.StormSky(0.5f);
            Sfx?.ScreenFlash(0.5f);
            Sfx?.PowerAura(t.transform, duration, color);
            RenderingSetup.Pulse(0.8f);
            CameraDirector.Instance.Shake(0.4f);
            float start = Time.time, nextArc = 0f, nextDust = 0f;
            while (Time.time - start < duration + 1.5f && t != null && t.State == TopState.Fighting)
            {
                float gr = t.VisualRadius;
                bool active = Time.time - start < duration;
                if (active)
                {
                    // La géante glisse au centre et y reste.
                    Vector3 p = t.Body.position;
                    Vector3 center = new Vector3(0f, arena != null ? arena.SurfaceHeight(0f) : p.y, 0f);
                    t.Body.position = Vector3.Lerp(p, new Vector3(center.x, p.y, center.z), Time.deltaTime * 2f);
                    t.Body.SetVelocity(Vector3.zero);
                    // Les autres toupies sont repoussées jusqu'à son bord.
                    foreach (var o in _br.Fighting().ToList())
                    {
                        if (o == t) continue;
                        Vector3 d = (o.transform.position - t.transform.position).Flat();
                        float dist = d.magnitude, edge = gr + o.VisualRadius;
                        if (dist >= edge) continue;
                        Vector3 dir = dist > 0.01f ? d / dist : Random.insideUnitSphere.Flat().normalized;
                        float r = Mathf.Min((t.transform.position.Flat() + dir * edge).magnitude, R * 0.95f);
                        Vector3 np = t.transform.position.Flat() + dir * edge;
                        np = np.normalized * r;
                        np.y = (arena != null ? arena.SurfaceHeight(r) : o.transform.position.y) + 0.05f;
                        o.Body.position = np;
                        o.Body.SetVelocity(Vector3.Cross(Vector3.up, dir) * 6f + dir * 3f);
                        o.TakeDamage(o.MaxSpin * 0.03f * Time.deltaTime, t.PlayerId);
                        if (Time.time > nextDust) Sfx?.DustRing(np, 4, 3f);
                    }
                    if (Time.time > nextDust) nextDust = Time.time + 0.25f;
                    // Éclairs qui jaillissent du bord de la géante vers les toupies proches.
                    if (Time.time > nextArc)
                    {
                        nextArc = Time.time + 0.35f;
                        var near = _br.Fighting().Where(o => o != t).OrderBy(o => (o.transform.position - t.transform.position).sqrMagnitude).FirstOrDefault();
                        if (near != null)
                        {
                            Vector3 dir = (near.transform.position - t.transform.position).Flat().normalized;
                            Vfx.Lightning(t.transform.position + dir * gr * 0.9f + Vector3.up * gr * 0.4f, near.transform.position + Vector3.up * 0.5f, color, 0.3f, 0.3f);
                        }
                        Sfx?.Embers(t.transform.position + Random.insideUnitSphere.Flat() * gr + Vector3.up * gr * 0.5f, 6);
                    }
                }
                yield return null;
            }
            // Retour à la taille normale : grosse onde de choc.
            if (t != null && t.State == TopState.Fighting)
            {
                Vfx.Shockwave(t.transform.position, color);
                Sfx?.DustRing(t.transform.position, 30, 9f);
                CameraDirector.Instance.Shake(0.3f);
            }
            Sfx?.StormSky(0f);
        }

        // ------------------------------------------------------------------ foudre

        /// <summary>
        /// Orage : le ciel s'assombrit, puis la foudre s'abat sur plusieurs adversaires l'un après l'autre
        /// (cercle d'alerte, éclair géant, onde, toupie sonnée). La toupie du donateur n'est jamais touchée.
        /// </summary>
        public bool LightningStorm(TopController src, int strikes)
        {
            if (_br.Round.Phase != Core.Battle.RoundPhase.Fighting) return false;
            var targets = _br.Fighting().Where(o => o != src).OrderBy(_ => Random.value).Take(Mathf.Max(1, strikes)).ToList();
            if (targets.Count == 0) return false;
            StartCoroutine(StormRoutine(src, targets));
            return true;
        }

        IEnumerator StormRoutine(TopController src, System.Collections.Generic.List<TopController> targets)
        {
            Sfx?.StormSky(0.8f);
            CameraDirector.Instance.LowAngle(0.8f + targets.Count * 0.85f + 0.8f);
            AudioManager.Play("thunder", 0.6f, 0.2f, 0.1f);
            yield return new WaitForSeconds(0.6f);
            var color = new Color(0.65f, 0.8f, 1f);
            foreach (var o in targets)
            {
                if (o == null || o.State != TopState.Fighting) continue;
                // Alerte : cercle qui clignote sous la cible.
                Sfx?.GroundGlow(o.transform.position, 3.5f, 0.55f, new Color(0.6f, 0.85f, 1f, 1f), true, 1.4f);
                Sfx?.ElectricSparks(o.transform.position + Vector3.up * 0.5f, 12, color);
                yield return new WaitForSeconds(0.5f);
                if (o == null || o.State != TopState.Fighting) continue;
                Vector3 hit = o.transform.position;
                Vfx.SkyBolt(hit, color);
                Sfx?.LightningImpact(hit, color);
                Sfx?.ScreenFlash(0.7f);
                RenderingSetup.Pulse(0.9f);
                CameraDirector.Instance.Shake(0.45f);
                CameraDirector.Instance.Punch(4f);
                AudioManager.Play("thunder", 1f, 0.05f, 0.12f);
                // Arcs qui rampent au sol autour de l'impact.
                for (int i = 0; i < 5; i++)
                {
                    float a = Random.value * Mathf.PI * 2f, d = Random.Range(1.5f, 3.5f) * Mathf.Max(1f, GameConfig.Current.tuning.topScale / 1.55f);
                    Vfx.Lightning(hit + Vector3.up * 0.2f, hit + new Vector3(Mathf.Cos(a) * d, 0.1f, Mathf.Sin(a) * d), color, 0.3f, 0.12f);
                }
                o.TakeDamage(o.MaxSpin * _fx.lightningDamage, src != null ? src.PlayerId : null);
                o.Slow(1.2f);
                o.Push(Vector3.up * 2.5f * o.Body.mass);
                // Les voisins proches prennent un choc électrique.
                foreach (var n in Nearest(o, 3, 3.5f * Mathf.Max(1f, GameConfig.Current.tuning.topScale / 1.55f)))
                {
                    if (n == src) continue;
                    Vfx.Lightning(hit + Vector3.up * 0.5f, n.transform.position + Vector3.up * 0.5f, color, 0.3f, 0.2f);
                    n.TakeDamage(n.MaxSpin * _fx.lightningDamage * 0.3f, src != null ? src.PlayerId : null);
                    n.Push((n.transform.position - hit).Flat().normalized * 4f * n.Body.mass);
                }
                yield return new WaitForSeconds(0.35f);
            }
            yield return new WaitForSeconds(0.4f);
            Sfx?.StormSky(0f);
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
            // Spirale verte qui monte autour de la toupie + lueur au sol.
            Sfx?.Spiral(t.transform.position, new Color(0.5f, 1f, 0.6f), 26, 0.9f);
            Sfx?.GroundGlow(t.transform.position, 2f, 0.8f, new Color(0.4f, 1f, 0.5f, 0.8f), false, 3.5f);
            return true;
        }

        public bool Heavy(TopController t, float seconds)
        {
            if (!Ok(t)) return false;
            t.BuffHeavy(1.6f, seconds);
            Vfx.Shockwave(t.transform.position, new Color(0.8f, 0.8f, 0.85f));
            // La toupie s'alourdit : le sol tremble, poussière, halo d'acier.
            Sfx?.DustRing(t.transform.position, 18, 5f);
            Sfx?.PowerAura(t.transform, seconds, new Color(0.75f, 0.8f, 0.9f));
            CameraDirector.Instance.Shake(0.12f);
            return true;
        }

        public bool GrowTop(TopController t, float amount)
        {
            if (t == null || !t.Alive) return false;
            t.Grow(amount);
            Vfx.JoinBeam(t.transform.position, t.Glow);
            Sfx?.Spiral(t.transform.position, new Color(1f, 0.85f, 0.3f), 30, 1.1f);
            Sfx?.GroundGlow(t.transform.position, 2.5f, 0.9f, new Color(1f, 0.85f, 0.3f, 0.9f), false, 4.5f);
            AudioManager.Play("grow", 0.8f, 0.2f);
            return true;
        }

        /// <summary>Gèle l'adversaire le plus proche (ralenti, plus de déplacement).</summary>
        public bool FreezeNearest(TopController t, float seconds)
        {
            if (!Ok(t)) return false;
            var o = Nearest(t, 1, 60f).FirstOrDefault();
            if (o == null) return false;
            o.Body.SetVelocity(Vector3.zero);
            o.Slow(seconds);
            AudioManager.Play("freeze", 0.8f, 0.2f);
            Vfx.Shockwave(o.transform.position, new Color(0.6f, 0.9f, 1f));
            // Rayon de givre de la toupie vers sa cible, éclats de glace, sol gelé.
            Vfx.Lightning(t.transform.position + Vector3.up * 0.6f, o.transform.position + Vector3.up * 0.5f, new Color(0.7f, 0.95f, 1f), 0.35f, 0.3f);
            Sfx?.ElectricSparks(o.transform.position + Vector3.up * 0.4f, 50, new Color(0.8f, 0.95f, 1f));
            Sfx?.Scorch(o.transform.position, 2.6f, seconds + 1f, new Color(0.75f, 0.92f, 1f, 0.55f));
            return true;
        }

        /// <summary>Tir : l'adversaire le plus proche est projeté loin de la toupie.</summary>
        public bool Kick(TopController t, float power)
        {
            if (!Ok(t)) return false;
            var o = Nearest(t, 1, 60f).FirstOrDefault();
            if (o == null) return false;
            Vector3 d = (o.transform.position - t.transform.position).Flat().normalized;
            o.Push((d + Vector3.up * 0.2f) * 7f * power * o.Body.mass);
            o.TakeDamage(o.MaxSpin * 0.03f * power, t.PlayerId);
            Vfx.Impact(o.transform.position + Vector3.up * 0.4f, t.Glow, Color.white, 90f);
            AudioManager.Play("impact", 1f, 0.05f);
            // Frappe : éclair de lumière, onde, poussière, petite gerbe de feu.
            Vfx.Shockwave(o.transform.position, t.Glow);
            Sfx?.DustRing(o.transform.position, 14, 6f);
            Sfx?.FireBurst(o.transform.position + Vector3.up * 0.4f, 8, Color.white);
            Sfx?.LightFlash(o.transform.position + Vector3.up, t.Glow, 10f, 10f, 0.3f);
            CameraDirector.Instance.Shake(0.15f);
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
                Sfx?.Spiral(t.transform.position, new Color(0.6f, 1f, 0.6f), 10, 0.8f, true);
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
            // Sacre : colonne dorée, aura royale, éclair de lumière.
            Sfx?.PowerAura(t.transform, 10f, new Color(1f, 0.85f, 0.3f));
            Sfx?.Spiral(t.transform.position, new Color(1f, 0.9f, 0.4f), 40, 1.3f);
            Sfx?.LightFlash(t.transform.position + Vector3.up * 2f, new Color(1f, 0.85f, 0.4f), 20f, 16f, 0.8f);
            Sfx?.ScreenFlash(0.25f);
            return true;
        }

        /// <summary>Rafale de projectiles sur les adversaires les plus proches.</summary>
        public bool Barrage(TopController t, int shots)
        {
            if (!Ok(t)) return false;
            var targets = Nearest(t, shots, 60f).ToList();
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
                    SpectacleFx.Instance?.FireImpact(m.transform.position, 0.45f);
                }, false);
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
            const float duration = 7f;
            float reach = 6.5f * (arena != null ? arena.Scale : 1f) * Mathf.Sqrt(Mathf.Max(1f, GameConfig.Current.tuning.topScale / 1.55f));
            float start = Time.time, angle = Random.value * Mathf.PI * 2f, spin = 0f;
            CameraDirector.Instance.Shake(0.35f);
            Sfx?.StormSky(0.55f);
            CameraDirector.Instance.LowAngle(duration);
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
                    Vector3 dir = dist > 0.01f ? d / dist : Vector3.right;
                    // Aspirée vers le cœur, elle tourne autour de la colonne…
                    Vector3 swirl = Vector3.Cross(Vector3.up, dir) * 26f - dir * (12f + 10f * k);
                    o.Body.AddForce(swirl * k, ForceMode.Acceleration);
                    // … et elle est soulevée dans les airs, plus haut près du centre, avec un mouvement de vague.
                    float ground = arena != null ? arena.SurfaceHeight(o.transform.position.Flat().magnitude) : 0f;
                    float targetH = Mathf.Lerp(1.2f, 7f, k) * (1f + 0.25f * Mathf.Sin(Time.time * 3f + o.GetInstanceID()));
                    float h = o.transform.position.y - ground;
                    float lift = -Physics.gravity.y * (h < targetH ? 1.6f : 0.6f) * Mathf.Clamp01(k * 2.5f);
                    o.Body.AddForce(Vector3.up * lift, ForceMode.Acceleration);
                    o.TakeDamage(o.MaxSpin * 0.03f * k * Time.deltaTime, null);
                    if (h > 1f && Random.value < 0.15f) Sfx?.DustAt(o.transform.position, Vector3.down * 2f, 0.7f);
                }
                if (Random.value < 0.05f) CameraDirector.Instance.Shake(0.08f);
                yield return null;
            }
            Vfx.HideTornado();
            Sfx?.StormSky(0f);
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
            Sfx?.FireImpact(p, 1f);
            Sfx?.ScreenFlash(0.18f);
            RenderingSetup.Pulse(0.5f);
            AudioManager.Play("meteor", 0.9f, 0.12f);
            CameraDirector.Instance.Shake(0.32f);
            float radius = 3.2f * Mathf.Max(1f, GameConfig.Current.tuning.topScale / 1.55f);
            foreach (var o in _br.Fighting().ToList())
            {
                Vector3 d = (o.transform.position - p).Flat();
                float dist = d.magnitude;
                if (dist > radius) continue;
                float k = 1f - dist / radius;
                o.TakeDamage(o.MaxSpin * _fx.meteorDamage * k, null);
                o.Push((d.normalized + Vector3.up * 0.3f) * 6f * k * o.Body.mass);
            }
        }
    }
}
