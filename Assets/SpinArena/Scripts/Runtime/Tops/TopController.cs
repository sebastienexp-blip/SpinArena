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
        /// <summary>Point au-dessus de la toupie où s'affiche son pseudo.</summary>
        public Vector3 NameAnchor { get; private set; }
        public string ShortName => DisplayName == null ? "" : DisplayName.Length > 14 ? DisplayName.Substring(0, 13) + "…" : DisplayName;

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
        public static float TopScale => GameConfig.Current.tuning.topScale * CrowdScale;
        /// <summary>Réduction des toupies quand l'arène est bondée (1 = taille normale).</summary>
        public static float CrowdScale = 1f;
        /// <summary>Beaucoup de toupies : traînées et flou coupés, collisions moins coûteuses.</summary>
        public static bool LowDetail;

        /// <summary>Réapplique la taille après un changement de CrowdScale.</summary>
        public void RefreshScale() { if (_visualRoot != null) ApplySize(); }
        static float BodyRadius => 0.42f * TopScale;

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
                BuildSpinBlur();
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
            SetCrown(false);
            HasBounty = false;
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
                // Médaillon rond posé sur le dessus : photo de profil découpée en cercle + anneau lumineux.
                var q = new GameObject("Photo", typeof(MeshFilter), typeof(MeshRenderer));
                q.GetComponent<MeshFilter>().sharedMesh = DiscMesh;
                q.transform.SetParent(transform, false);
                _photo = q.GetComponent<MeshRenderer>();
                _photo.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _photo.receiveShadows = false;
                var ring = new GameObject("Ring", typeof(MeshFilter), typeof(MeshRenderer));
                ring.GetComponent<MeshFilter>().sharedMesh = RingMesh;
                ring.transform.SetParent(q.transform, false);
                ring.transform.localPosition = new Vector3(0f, 0f, 0.002f);
                _ring = ring.GetComponent<MeshRenderer>();
                _ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _ring.receiveShadows = false;
                // Sans photo : initiale du pseudo sur un disque de couleur.
                var letter = new GameObject("Initial");
                letter.transform.SetParent(q.transform, false);
                letter.transform.localPosition = new Vector3(0f, 0f, -0.002f);
                _initial = letter.AddComponent<TextMesh>();
                _initial.font = Compat.UiFont;
                _initial.GetComponent<MeshRenderer>().sharedMaterial = Compat.UiFont.material;
                _initial.fontSize = 96;
                _initial.characterSize = 0.075f;
                _initial.anchor = TextAnchor.MiddleCenter;
                _initial.alignment = TextAlignment.Center;
                _initial.fontStyle = FontStyle.Bold;
                _initial.color = Color.white;
            }
        }

        MeshRenderer _ring;
        TextMesh _initial;
        Material _ringMat, _plainMat;

        static Mesh _disc, _ringMesh;
        /// <summary>Disque de rayon 0,5 (même face visible qu'un Quad), UV en cercle : l'image est découpée en rond.</summary>
        static Mesh DiscMesh
        {
            get
            {
                if (_disc != null) return _disc;
                const int n = 48;
                var v = new Vector3[n + 1]; var uv = new Vector2[n + 1]; var t = new int[n * 3];
                v[0] = Vector3.zero; uv[0] = new Vector2(0.5f, 0.5f);
                for (int i = 0; i < n; i++)
                {
                    float a = i / (float)n * Mathf.PI * 2f, c = Mathf.Cos(a), sn = Mathf.Sin(a);
                    v[i + 1] = new Vector3(c * 0.5f, sn * 0.5f, 0f);
                    uv[i + 1] = new Vector2(0.5f + c * 0.5f, 0.5f + sn * 0.5f);
                    t[i * 3] = 0; t[i * 3 + 1] = 1 + (i + 1) % n; t[i * 3 + 2] = 1 + i;
                }
                _disc = new Mesh { name = "PhotoDisc", vertices = v, uv = uv, triangles = t };
                _disc.RecalculateNormals(); _disc.RecalculateBounds();
                return _disc;
            }
        }
        /// <summary>Anneau autour du médaillon (rayons 0,47 à 0,56).</summary>
        static Mesh RingMesh
        {
            get
            {
                if (_ringMesh != null) return _ringMesh;
                const int n = 48;
                var v = new Vector3[n * 2]; var uv = new Vector2[n * 2]; var t = new int[n * 6];
                for (int i = 0; i < n; i++)
                {
                    float a = i / (float)n * Mathf.PI * 2f, c = Mathf.Cos(a), sn = Mathf.Sin(a);
                    v[i * 2] = new Vector3(c * 0.47f, sn * 0.47f, 0f); v[i * 2 + 1] = new Vector3(c * 0.56f, sn * 0.56f, 0f);
                    uv[i * 2] = new Vector2(0.5f, 0.5f); uv[i * 2 + 1] = new Vector2(0.5f, 0.5f);
                    int a0 = i * 2, a1 = i * 2 + 1, b0 = ((i + 1) % n) * 2, b1 = b0 + 1;
                    t[i * 6] = a0; t[i * 6 + 1] = b1; t[i * 6 + 2] = a1;
                    t[i * 6 + 3] = a0; t[i * 6 + 4] = b0; t[i * 6 + 5] = b1;
                }
                _ringMesh = new Mesh { name = "PhotoRing", vertices = v, uv = uv, triangles = t };
                _ringMesh.RecalculateNormals(); _ringMesh.RecalculateBounds();
                return _ringMesh;
            }
        }

        /// <summary>Photo de profil TikTok posée sur la toupie ; null = pas de photo (rien n'est affiché).</summary>
        public void SetPhoto(Texture tex)
        {
            if (_photo == null) return;
            _hasPhoto = tex != null;
            // Anneau aux couleurs de la toupie.
            if (_ringMat == null) { _ringMat = MaterialLibrary.Unlit(Glow); _ring.sharedMaterial = _ringMat; }
            else MaterialLibrary.SetColor(_ringMat, Glow);
            if (!_hasPhoto)
            {
                // Pas (encore) de photo : disque de couleur avec l'initiale du pseudo.
                if (_plainMat == null) _plainMat = MaterialLibrary.Unlit(Color.Lerp(Glow, Color.black, 0.45f));
                else MaterialLibrary.SetColor(_plainMat, Color.Lerp(Glow, Color.black, 0.45f));
                _photo.sharedMaterial = _plainMat;
                string n = DisplayName ?? "";
                _initial.text = n.Length > 0 ? char.ToUpperInvariant(n[0]).ToString() : "?";
                _initial.gameObject.SetActive(true);
                return;
            }
            _initial.gameObject.SetActive(false);
            if (_photoMat == null) _photoMat = MaterialLibrary.Transparent(tex, Color.white);
            else MaterialLibrary.SetTexture(_photoMat, tex);
            _photo.sharedMaterial = _photoMat;
        }
        bool _hasPhoto;

        // ------------------------------------------------------------------ couronne, prime, halo, fumée

        Material _blurMat;
        Transform _blur;

        /// <summary>Disque de flou de rotation : plus la toupie tourne vite, plus le flou lumineux est visible.</summary>
        void BuildSpinBlur()
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            DestroyImmediate(q.GetComponent<Collider>()); // immédiat : jamais de collider non convexe sur un corps physique
            q.name = "SpinBlur";
            q.transform.SetParent(_spin, false);
            q.transform.localPosition = new Vector3(0f, 0.215f, 0f);
            q.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            q.transform.localScale = Vector3.one * 1.08f;
            _blurMat = new Material(MaterialLibrary.Additive(Color.white, TopVisualFactory.SpinBlurTexture));
            if (_blurMat.HasProperty("_Cull")) _blurMat.SetFloat("_Cull", 0f);
            var r = q.GetComponent<MeshRenderer>();
            r.sharedMaterial = _blurMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _blur = q.transform;
        }

        public bool Crowned { get; private set; }
        public bool HasBounty { get; private set; }
        Transform _crown, _aura;
        Material _auraMat;
        float _nextSmoke;

        public void SetBounty(bool on) => HasBounty = on;

        /// <summary>Couronne dorée au-dessus de la toupie du dernier vainqueur.</summary>
        public void SetCrown(bool on)
        {
            Crowned = on;
            if (on && _crown == null) _crown = BuildCrown();
            if (_crown != null) _crown.gameObject.SetActive(on);
        }

        Transform BuildCrown()
        {
            var root = new GameObject("Crown").transform;
            root.SetParent(transform, false);
            var gold = MaterialLibrary.Metal(new Color(1f, 0.78f, 0.25f), 1f, 0.85f, new Color(1f, 0.6f, 0.1f) * 1.2f);
            var band = new GameObject("Band");
            band.transform.SetParent(root, false);
            band.AddComponent<MeshFilter>().sharedMesh = TopVisualFactory.Lathe(new[] { new Vector2(0.20f, 0f), new Vector2(0.24f, 0f), new Vector2(0.24f, 0.07f), new Vector2(0.20f, 0.07f) }, 32);
            band.AddComponent<MeshRenderer>().sharedMaterial = gold;
            var spikeMesh = TopVisualFactory.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.045f, 0f), new Vector2(0f, 0.13f) }, 12);
            for (int i = 0; i < 5; i++)
            {
                float a = i / 5f * Mathf.PI * 2f;
                var sp = new GameObject("Spike");
                sp.transform.SetParent(root, false);
                sp.transform.localPosition = new Vector3(Mathf.Cos(a) * 0.22f, 0.07f, Mathf.Sin(a) * 0.22f);
                sp.AddComponent<MeshFilter>().sharedMesh = spikeMesh;
                sp.AddComponent<MeshRenderer>().sharedMaterial = gold;
            }
            return root;
        }

        Transform BuildAura()
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            DestroyImmediate(q.GetComponent<Collider>()); // immédiat : jamais de collider non convexe sur un corps physique
            q.name = "Aura";
            q.transform.SetParent(transform, false);
            q.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            _auraMat = new Material(MaterialLibrary.Additive(Color.white, MaterialLibrary.SoftDot));
            var r = q.GetComponent<MeshRenderer>();
            r.sharedMaterial = _auraMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return q.transform;
        }

        void UpdateExtras()
        {
            var show = GameConfig.Current.show;
            float k = TopScale * _sizeVisual;
            if (_crown != null && _crown.gameObject.activeSelf)
            {
                _crown.localPosition = new Vector3(0f, 0.5f * k, 0f);
                _crown.localScale = Vector3.one * k;
                _crown.Rotate(0f, 60f * Time.deltaTime, 0f, Space.Self);
            }
            bool aura = show.rarityAuras && Def != null && Def.rarity >= Rarity.Epic && Alive;
            if (aura && _aura == null) _aura = BuildAura();
            if (_aura != null)
            {
                _aura.gameObject.SetActive(aura);
                if (aura)
                {
                    float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * 4f);
                    _aura.localPosition = new Vector3(0f, 0.03f, 0f);
                    _aura.localScale = Vector3.one * (1.9f + 0.25f * (int)Def.rarity) * k * pulse;
                    var c = Compat.Hex(TopDefinition.RarityColor(Def.rarity));
                    c.a = 0.55f;
                    MaterialLibrary.SetColor(_auraMat, c);
                }
            }
            if (show.lowEnergySmoke && State == TopState.Fighting && SpinFraction < 0.2f && Time.time > _nextSmoke)
            {
                _nextSmoke = Time.time + 0.18f;
                VFX.VfxManager.Instance?.Smoke(transform.position + Vector3.up * 0.4f * k);
            }
        }

        // ------------------------------------------------------------------ taille (tapotements, cadeaux)

        float _size = 1f, _sizeVisual = 1f, _heavyUntil, _heavyMult = 1f, _slowUntil;
        public static float MaxSize => Mathf.Max(1f, GameConfig.Current.tuning.maxSize);
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
            _trail.emitting = GameConfig.Current.display.trails && !LowDetail;
            // Des centaines de toupies : détection de collision plus légère (le téléphone suit).
            _rb.collisionDetectionMode = LowDetail ? CollisionDetectionMode.ContinuousSpeculative : CollisionDetectionMode.ContinuousDynamic;
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

        public void SetSpinFraction(float f) => Spin = MaxSpin * Mathf.Clamp01(f);

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
            var tune = GameConfig.Current.tuning;
            float dmg = CombatMath.ImpactDamage(atk, def, rel) * tune.damage;
            if (dmg <= 0f) return;
            TakeDamage(dmg, other.PlayerId);
            other.DamageDealt += dmg;
            float knock = CombatMath.Knockback(atk, def, rel) * tune.knockback;
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
                var tune = GameConfig.Current.tuning;
                Spin -= CombatMath.SpinDecayPerSecond(s, _decayFactor) * tune.spinDecay * Time.fixedDeltaTime;
                if (Spin < 0f) Spin = 0f;
                float vigor = Mathf.Lerp(0.35f, 1f, SpinFraction);
                Vector3 v = _rb.GetVelocity();
                Vector3 flat = v.Flat();
                float max = CombatMath.MaxMoveSpeed(s) * vigor * tune.speed;
                if (Slowed) max *= 0.3f;
                if (flat.magnitude < max)
                    _rb.AddForce(_desired * CombatMath.Acceleration(s) * vigor * tune.speed * (Slowed ? 0.3f : 1f), ForceMode.Acceleration);
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
            UpdateExtras();
            if (_blurMat != null)
            {
                float f = State == TopState.Eliminated ? 0f : SpinFraction;
                var c = Color.Lerp(Color.white, Glow, 0.6f) * (0.15f + 0.45f * f * f);
                c.a = 0.25f + 0.55f * f;
                MaterialLibrary.SetColor(_blurMat, c);
            }
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
                var disp = GameConfig.Current.display;
                // Les pseudos sont affichés par l'interface (NameTags) : plus nets et lisibles qu'un texte 3D.
                _label.gameObject.SetActive(false);
                _photo.gameObject.SetActive(disp.showPhotos && State != TopState.Eliminated);
                if ((!disp.trails || LowDetail) && _trail.emitting) _trail.emitting = false;
                if (_blur != null && _blur.gameObject.activeSelf == LowDetail) _blur.gameObject.SetActive(!LowDetail);
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
            // Médaillon presque à plat, comme une vraie toupie vue d'en haut, et assez grand pour couvrir le dessus.
            Vector3 normal = Vector3.Slerp(Vector3.up, toCam, 0.3f);
            _photo.transform.position = transform.position + Vector3.up * (0.45f * k) + normal * 0.02f;
            _photo.transform.rotation = Quaternion.LookRotation(-normal, cam.transform.up);
            _photo.transform.localScale = Vector3.one * (0.8f * k);
            _label.transform.position = transform.position + up * (1.25f * k) + Vector3.up * 0.4f;
            NameAnchor = transform.position + Vector3.up * (0.75f * k);
            _label.transform.rotation = face;
        }

        void OnDisable() { if (_trail != null) _trail.Clear(); }
    }
}
