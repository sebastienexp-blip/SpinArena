using SpinArena.Arena;
using SpinArena.Core.Combat;
using SpinArena.Core.Tops;
using SpinArena.Util;
using UnityEngine;

namespace SpinArena.Tops
{
    public enum TopState { Idle, Staged, Fighting, Eliminated }

    /// <summary>
    /// Toupie physique : rigidbody sur le sol incurvé, énergie de rotation, chocs, projections, IA autonome
    /// selon la classe de combat, bonus temporaires et animation d'élimination.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class TopController : MonoBehaviour
    {
        public string PlayerId { get; private set; }
        public string DisplayName { get; private set; }
        public TopDefinition Def { get; private set; }
        public int PlayerLevel { get; private set; }
        public TopState State { get; private set; }
        public float Spin { get; private set; }
        public float MaxSpin { get; private set; }
        public float SpinFraction => MaxSpin > 0 ? Mathf.Clamp01(Spin / MaxSpin) : 0f;
        public bool Alive => State == TopState.Fighting || State == TopState.Staged;
        public string LastAttackerId => Time.time - _lastHitTime < 6f ? _lastAttacker : null;
        public float DamageDealt { get; private set; }
        public Rigidbody Body => _rb;
        public Color Glow { get; private set; }

        public System.Action<TopController, TopController, float, Vector3> Impact; // (victime, attaquant, dégâts, point)
        public System.Action<TopController, Vector3> WallHit;

        Rigidbody _rb;
        SphereCollider _col;
        Transform _visualRoot, _spin;
        TrailRenderer _trail;
        TextMesh _label;
        MeshRenderer _photo;          // photo de profil posée sur la toupie (masquée s'il n'y en a pas)
        Material _photoMat;
        ArenaManager _arena;
        CombatStats _base;

        // IA
        Vector3 _desired;
        TopController _target;
        float _orbitSign = 1f;

        // Bonus temporaires
        float _atkUntil, _atkMult = 1f, _shieldUntil, _shieldRed, _turboUntil, _powerUntil, _powerMult = 1f;
        string _lastAttacker;
        float _lastHitTime = -100f;
        float _elimTime;
        float _spinAngle, _precession;
        float _decayFactor = 1f;

        /// <summary>Échelle des toupies : assez grandes pour rester lisibles sur un téléphone avec 50 toupies.</summary>
        public const float TopScale = 1.35f;
        const float BodyRadius = 0.42f * TopScale;

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _rb.constraints = RigidbodyConstraints.FreezeRotation;
            _rb.SetLinearDamping(0.15f);
            _rb.SetAngularDamping(0.5f);
            _col = gameObject.AddComponent<SphereCollider>();
            _col.radius = BodyRadius;
            _col.center = new Vector3(0f, BodyRadius, 0f);
            _col.SetPhysics("Top", 0.02f, 0.75f, true, true);
            gameObject.layer = 0;
        }

        public void Setup(ArenaManager arena, string playerId, string displayName, TopDefinition def, int level, Texture avatar)
        {
            _arena = arena;
            PlayerId = playerId;
            DisplayName = displayName;
            PlayerLevel = level;
            if (Def != def)
            {
                if (_visualRoot != null) Destroy(_visualRoot.gameObject);
                _visualRoot = new GameObject("Visual").transform;
                _visualRoot.SetParent(transform, false);
                _spin = TopVisualFactory.Build(def, _visualRoot);
                _visualRoot.localScale = Vector3.one * TopScale;
                Def = def;
                Glow = Compat.Hex(def.glowColor, Color.cyan);
                BuildExtras();
            }
            _base = CombatStats.From(def, level);
            MaxSpin = def.MaxSpin;
            Spin = MaxSpin;
            _rb.mass = def.Mass;
            _label.text = displayName.Length > 14 ? displayName.Substring(0, 13) + "…" : displayName;
            SetPhoto(avatar);
            _size = _sizeVisual = 1f;
            _heavyUntil = _slowUntil = 0f;
            ApplySize();
            _atkUntil = _shieldUntil = _turboUntil = _powerUntil = 0f;
            _lastAttacker = null; _lastHitTime = -100f;
            DamageDealt = 0f;
            _orbitSign = (TopCatalog.StableHash(playerId) & 1) == 0 ? 1f : -1f;
            _visualRoot.localRotation = Quaternion.identity;
            _visualRoot.localPosition = Vector3.zero;
            _visualRoot.localScale = Vector3.one * TopScale;
            State = TopState.Idle;
            gameObject.SetActive(true);
        }

        void BuildExtras()
        {
            if (_trail == null)
            {
                var tgo = new GameObject("Trail");
                tgo.transform.SetParent(transform, false);
                tgo.transform.localPosition = new Vector3(0f, 0.25f, 0f);
                _trail = tgo.AddComponent<TrailRenderer>();
                _trail.time = 0.35f;
                _trail.minVertexDistance = 0.12f;
                _trail.widthCurve = new AnimationCurve(new Keyframe(0f, 0.7f), new Keyframe(1f, 0f));
                _trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _trail.receiveShadows = false;
                _trail.numCapVertices = 2;
            }
            _trail.sharedMaterial = MaterialLibrary.Additive(Color.white, MaterialLibrary.SoftDot);
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Glow, 0f), new GradientColorKey(Glow * 0.6f, 1f) },
                      new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
            _trail.colorGradient = g;

            if (_label == null)
            {
                var lgo = new GameObject("Label");
                lgo.transform.SetParent(transform, false);
                _label = lgo.AddComponent<TextMesh>();
                _label.font = Compat.UiFont;
                _label.GetComponent<MeshRenderer>().sharedMaterial = Compat.UiFont.material;
                _label.fontSize = 48;
                _label.characterSize = 0.1f;
                _label.anchor = TextAnchor.LowerCenter;
                _label.alignment = TextAlignment.Center;
            }
            _label.color = Color.white;

            if (_photo == null)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                q.name = "Photo";
                Destroy(q.GetComponent<Collider>());
                q.transform.SetParent(transform, false);
                _photo = q.GetComponent<MeshRenderer>();
                _photo.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _photo.receiveShadows = false;
            }
        }

        /// <summary>Photo de profil TikTok posée sur la toupie ; null = pas de photo (rien n'est affiché).</summary>
        public void SetPhoto(Texture tex)
        {
            if (_photo == null) return;
            _hasPhoto = tex != null;
            if (!_hasPhoto) return;
            if (_photoMat == null) { _photoMat = MaterialLibrary.Transparent(tex, Color.white); _photo.sharedMaterial = _photoMat; }
            else MaterialLibrary.SetTexture(_photoMat, tex);
        }
        bool _hasPhoto;

        // ------------------------------------------------------------------ taille (tapotements, cadeaux)

        float _size = 1f, _sizeVisual = 1f, _heavyUntil, _heavyMult = 1f, _slowUntil;
        public const float MaxSize = 1.8f;
        public float Size => _size;

        /// <summary>Grossit la toupie pour la manche en cours (plafonné). Plus grosse = plus lourde, plus dure à éjecter.</summary>
        public void Grow(float amount) { _size = Mathf.Clamp(_size + amount, 1f, MaxSize); }

        public void BuffHeavy(float mult, float seconds) { _heavyMult = Mathf.Clamp(mult, 1f, 2f); _heavyUntil = Time.time + seconds; }
        public void Slow(float seconds) { _slowUntil = Time.time + seconds; }
        public bool Slowed => Time.time < _slowUntil;

        void ApplySize()
        {
            if (_visualRoot != null) _visualRoot.localScale = Vector3.one * TopScale * _sizeVisual;
            _col.radius = BodyRadius * _sizeVisual;
            _col.center = new Vector3(0f, BodyRadius * _sizeVisual, 0f);
            if (Def != null) _rb.mass = Def.Mass * _sizeVisual * _sizeVisual * (Time.time < _heavyUntil ? _heavyMult : 1f);
        }

        // ------------------------------------------------------------------ cycle de vie

        /// <summary>Place la toupie sur sa position de lancement (pendant le lobby et le compte à rebours).</summary>
        public void Stage(Vector3 position)
        {
            State = TopState.Staged;
            _rb.isKinematic = true;
            _rb.constraints = RigidbodyConstraints.FreezeRotation;
            transform.SetPositionAndRotation(position, Quaternion.identity);
            _trail.Clear();
            _trail.emitting = false;
        }

        /// <summary>Lancement : la toupie part en tournant autour du centre.</summary>
        public void Launch(float strength)
        {
            State = TopState.Fighting;
            _rb.isKinematic = false;
            Vector3 p = transform.position.Flat();
            Vector3 tangent = Vector3.Cross(Vector3.up, p.normalized) * _orbitSign;
            _rb.SetVelocity((tangent * 0.8f - p.normalized * 0.45f) * strength * (0.85f + Def.speed / 300f));
            _trail.Clear();
            _trail.emitting = true;
        }

        public void Eliminate(bool ringOut)
        {
            if (State == TopState.Eliminated) return;
            State = TopState.Eliminated;
            _elimTime = Time.time;
            _trail.emitting = false;
            if (!ringOut)
            {
                // La toupie bascule et s'immobilise.
                _rb.SetVelocity(_rb.GetVelocity() * 0.3f);
            }
        }

        public void Despawn()
        {
            State = TopState.Idle;
            _rb.isKinematic = true;
            gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ combat

        public CombatStats Effective
        {
            get
            {
                var s = _base;
                float t = Time.time;
                if (t < _atkUntil) s.damageMultiplier *= _atkMult;
                if (t < _powerUntil) s.damageMultiplier *= _powerMult;
                if (t < _shieldUntil) s.damageReduction += _shieldRed;
                if (t < _turboUntil) s.speed *= 1.35f;
                return s;
            }
        }

        public bool ShieldActive => Time.time < _shieldUntil;
        public bool PoweredUp => Time.time < _powerUntil || Time.time < _atkUntil;

        public void TakeDamage(float amount, string attackerId)
        {
            if (State != TopState.Fighting || amount <= 0f) return;
            Spin = Mathf.Max(0f, Spin - amount);
            if (!string.IsNullOrEmpty(attackerId)) { _lastAttacker = attackerId; _lastHitTime = Time.time; }
        }

        public void AddSpin(float amount)
        {
            if (!Alive) return;
            Spin = Mathf.Min(MaxSpin, Spin + amount);
        }

        public void BuffAttack(float mult, float seconds) { _atkMult = Mathf.Max(mult, 1f); _atkUntil = Time.time + seconds; }
        public void BuffShield(float reduction, float seconds) { _shieldRed = Mathf.Clamp(reduction, 0f, CombatMath.MaxReduction); _shieldUntil = Time.time + seconds; }
        /// <summary>Bonus cumulable en durée, plafonné au double de la durée de base.</summary>
        public void BuffPower(float mult, float seconds)
        {
            float now = Time.time;
            _powerMult = Mathf.Max(mult, 1f);
            _powerUntil = Mathf.Min(Mathf.Max(_powerUntil, now) + seconds, now + seconds * 2f);
        }

        public void Turbo(float impulse, float seconds)
        {
            if (State != TopState.Fighting) return;
            _turboUntil = Time.time + seconds;
            Vector3 dir = _target != null && _target.Alive ? (_target.transform.position - transform.position).Flat().normalized
                                                            : _rb.GetVelocity().Flat().normalized;
            if (dir.sqrMagnitude < 0.01f) dir = -transform.position.Flat().normalized;
            _rb.AddForce(dir * impulse * _rb.mass, ForceMode.Impulse);
        }

        public void SpeedBuff(float seconds) { _turboUntil = Mathf.Max(_turboUntil, Time.time + seconds); }

        public void Push(Vector3 impulse) { if (State == TopState.Fighting) _rb.AddForce(impulse, ForceMode.Impulse); }

        public void SetDecayFactor(float f) => _decayFactor = f;

        void OnCollisionEnter(Collision c)
        {
            if (State != TopState.Fighting) return;
            var other = c.rigidbody != null ? c.rigidbody.GetComponent<TopController>() : null;
            Vector3 point = c.contactCount > 0 ? c.GetContact(0).point : transform.position;
            if (other == null)
            {
                if (c.relativeVelocity.sqrMagnitude > 16f) WallHit?.Invoke(this, point);
                return;
            }
            if (other.State != TopState.Fighting) return;
            float rel = c.relativeVelocity.Flat().magnitude;
            var atk = other.Effective;
            var def = Effective;
            float dmg = CombatMath.ImpactDamage(atk, def, rel);
            if (dmg <= 0f) return;
            TakeDamage(dmg, other.PlayerId);
            other.DamageDealt += dmg;
            float knock = CombatMath.Knockback(atk, def, rel);
            Vector3 dir = (transform.position - other.transform.position).Flat().normalized;
            _rb.AddForce((dir + Vector3.up * 0.12f) * knock, ForceMode.Impulse);
            Impact?.Invoke(this, other, dmg, point);
        }

        // ------------------------------------------------------------------ IA (appelée par le gestionnaire, en décalé)

        public void Think(TopController[] all, int count)
        {
            if (State != TopState.Fighting) return;
            Vector3 me = transform.position;
            float R = _arena.Radius;
            float rMe = me.Flat().magnitude;
            TopController best = null;
            float bestScore = float.MaxValue;
            Vector3 avoid = Vector3.zero;
            for (int i = 0; i < count; i++)
            {
                var o = all[i];
                if (o == this || o == null || o.State != TopState.Fighting) continue;
                Vector3 d = (o.transform.position - me).Flat();
                float dist = d.magnitude;
                if (dist < 3f) avoid -= d / Mathf.Max(dist * dist, 0.25f);
                // Préférence : proche, affaibli ; un peu d'aléatoire stable pour éviter que tout le monde vise le même.
                float score = dist * (0.6f + o.SpinFraction) + ((TopCatalog.StableHash(PlayerId + o.PlayerId) % 7) * 0.4f);
                if (score < bestScore) { bestScore = score; best = o; }
            }
            _target = best;
            Vector3 toCenter = (-me).Flat();
            Vector3 tangent = Vector3.Cross(Vector3.up, me.Flat().normalized) * _orbitSign;
            Vector3 toTarget = best != null ? (best.transform.position - me).Flat() : toCenter;
            float distT = toTarget.magnitude;
            Vector3 desired;
            switch (Def.Class)
            {
                case CombatClass.Attack:
                    desired = toTarget.normalized * 1.0f + tangent * 0.25f;
                    break;
                case CombatClass.Defense:
                    desired = rMe > R * 0.25f ? toCenter.normalized + tangent * 0.2f
                            : (distT < 4f ? toTarget.normalized * 0.8f : tangent * 0.5f);
                    break;
                case CombatClass.Stamina:
                    float ideal = R * 0.45f;
                    desired = tangent * 0.9f + toCenter.normalized * Mathf.Clamp((rMe - ideal) / ideal, -0.6f, 0.6f) + avoid.normalized * 0.8f;
                    break;
                default:
                    desired = distT < 7f ? toTarget.normalized * 0.9f + tangent * 0.3f : tangent * 0.7f + toCenter.normalized * 0.3f;
                    break;
            }
            // Instinct de survie : loin du bord, surtout en face d'une ouverture.
            if (rMe > R * 0.72f)
            {
                float ang = Mathf.Atan2(me.z, me.x) * Mathf.Rad2Deg;
                float danger = _arena.IsInPocket(ang) ? 2.2f : 1.2f;
                desired += toCenter.normalized * danger * Mathf.InverseLerp(R * 0.72f, R, rMe);
            }
            // Toupie faible : comportement plus prudent.
            if (SpinFraction < 0.25f) desired = Vector3.Lerp(desired, tangent + avoid.normalized, 0.4f);
            _desired = desired.sqrMagnitude > 0.0001f ? desired.normalized : Vector3.zero;
        }

        // ------------------------------------------------------------------ boucles

        void FixedUpdate()
        {
            if (State == TopState.Fighting)
            {
                var s = Effective;
                Spin -= CombatMath.SpinDecayPerSecond(s, _decayFactor) * Time.fixedDeltaTime;
                if (Spin < 0f) Spin = 0f;
                float vigor = Mathf.Lerp(0.35f, 1f, SpinFraction);
                Vector3 v = _rb.GetVelocity();
                Vector3 flat = v.Flat();
                float max = CombatMath.MaxMoveSpeed(s) * vigor;
                if (Slowed) max *= 0.3f;
                if (flat.magnitude < max)
                    _rb.AddForce(_desired * CombatMath.Acceleration(s) * vigor * (Slowed ? 0.3f : 1f), ForceMode.Acceleration);
                // Effet gyroscopique : une toupie qui tourne vite « glisse » en orbite plutôt qu'en ligne droite.
                Vector3 orbit = Vector3.Cross(Vector3.up, flat) * (0.06f * vigor * _orbitSign);
                _rb.AddForce(orbit, ForceMode.Acceleration);
                if (flat.magnitude > max * 1.6f) _rb.AddForce(-flat.normalized * (flat.magnitude - max * 1.6f) * 2f, ForceMode.Acceleration);
            }
        }

        void Update()
        {
            if (_spin == null) return;
            float dt = Time.deltaTime;
            if (Mathf.Abs(_sizeVisual - _size) > 0.001f || Time.time < _heavyUntil + 0.1f)
            {
                _sizeVisual = Mathf.MoveTowards(_sizeVisual, _size, dt * 0.8f);
                ApplySize();
            }
            float frac = State == TopState.Eliminated ? 0f : SpinFraction;
            float rate = State == TopState.Eliminated ? Mathf.Max(0f, 1f - (Time.time - _elimTime) * 1.5f) * 900f : 600f + frac * 2400f;
            _spinAngle += rate * dt * _orbitSign;
            _spin.localRotation = Quaternion.Euler(0f, _spinAngle, 0f);

            // Oscillation : plus la toupie faiblit, plus elle penche et tourne autour de son axe.
            float tilt;
            if (State == TopState.Eliminated)
            {
                tilt = Mathf.Lerp(18f, 82f, Mathf.Clamp01((Time.time - _elimTime) * 2.2f));
                _precession += 120f * dt;
            }
            else
            {
                tilt = Mathf.Pow(1f - frac, 3f) * 24f;
                _precession += (200f + (1f - frac) * 400f) * dt;
            }
            _visualRoot.localRotation = Quaternion.Euler(0f, _precession, 0f) * Quaternion.Euler(tilt, 0f, 0f) * Quaternion.Euler(0f, -_precession, 0f);

            if (State == TopState.Eliminated && Time.time - _elimTime > 2.6f) Despawn();

            if (_label != null)
            {
                _label.color = frac < 0.25f && State == TopState.Fighting ? new Color(1f, 0.45f, 0.4f) : Color.white;
                _label.gameObject.SetActive(State != TopState.Eliminated);
                _photo.gameObject.SetActive(_hasPhoto && State != TopState.Eliminated);
            }
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null || _label == null) return;
            Quaternion face = Quaternion.LookRotation(transform.position - cam.transform.position);
            Vector3 up = cam.transform.up;
            // Photo posée sur le dessus de la toupie, inclinée vers la caméra pour rester lisible ; elle ne tourne pas.
            float k = TopScale * _sizeVisual;
            Vector3 toCam = (cam.transform.position - transform.position).normalized;
            Vector3 normal = Vector3.Slerp(Vector3.up, toCam, 0.45f);
            _photo.transform.position = transform.position + Vector3.up * (0.43f * k) + normal * 0.02f;
            _photo.transform.rotation = Quaternion.LookRotation(-normal, cam.transform.up);
            _photo.transform.localScale = Vector3.one * (0.62f * k);
            _label.transform.position = transform.position + up * (1.25f * k) + Vector3.up * 0.4f;
            _label.transform.rotation = face;
        }

        void OnDisable() { if (_trail != null) _trail.Clear(); }
    }
}
