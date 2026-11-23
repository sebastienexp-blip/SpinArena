using System.Collections;
using System.Linq;
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
        public bool Turbo(TopController t) { if (!Ok(t)) return false; t.Turbo(_fx.turboImpulse, 3f); Vfx.Sparkle(t.transform.position, t.Glow); return true; }

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
            CameraDirector.Instance.Shake(0.1f);
        }

        System.Collections.Generic.IEnumerable<TopController> Nearest(TopController t, int count, float maxDist)
        {
            Vector3 p = t.transform.position;
            return _br.Fighting().Where(o => o != t && (o.transform.position - p).Flat().magnitude <= maxDist)
                      .OrderBy(o => (o.transform.position - p).sqrMagnitude).Take(count);
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
