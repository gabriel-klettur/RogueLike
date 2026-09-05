using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.Combat;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Two fields centred on the caster, sharing one tick clock and one lifetime, and drawing
    /// nothing alike.
    ///
    /// <para><b>HEALING</b> keeps the historical rig: a gold-and-green sacred circle on the
    /// floor, an inner glow, a light pillar, rising sparkles and a pulse ring per tick. That is
    /// the right picture for a blessing standing on the ground, and it is unchanged.</para>
    ///
    /// <para><b>DAMAGING</b> gets <see cref="StaticDomeFX"/>: a charged shell the caster walks
    /// around inside, whose event layer is arcs crawling its surface. It used to take the
    /// healing rig, so <c>static_field</c> — a Lightning spell authoring <c>(0.95, 0.92, 0.50)</c>
    /// — drew as a HOLY HEALING CIRCLE in hardcoded gold and green, and the <c>_tint</c> the
    /// executor handed it was assigned and read by nobody. The rune also never stopped turning
    /// while a pulse ring fired every half second: measured ~170 % duty, so there was no frame
    /// without an "event" and the whole thing read as one steady texture.</para>
    ///
    /// <para>One controller with a branch rather than two classes, because everything ABOVE the
    /// rig — the sweep, the tick clock, the fade, the lifetime — is genuinely identical, and a
    /// second class would be a copy of all of it that drifts on the first fix.</para>
    ///
    /// <para>All healing-rig sprites are generated once and cached statically to avoid per-cast
    /// allocations (see <see cref="AuraSpriteFactory"/>). Light2D is wired via reflection so the
    /// assembly needs no hard dependency on URP.</para>
    /// </summary>
    public class AuraController : MonoBehaviour, ISpellEffectDissipates
    {
        // --- Tunables (palette + animation) ---
        // Holy gold + nature green: classic "sacred ground" look.
        private static readonly Color GoldCore   = new Color(1.00f, 0.92f, 0.55f, 1f);
        private static readonly Color GreenCore  = new Color(0.55f, 1.00f, 0.70f, 1f);
        private static readonly Color GoldSoft   = new Color(1.00f, 0.85f, 0.45f, 0.55f);
        private static readonly Color GreenSoft  = new Color(0.45f, 1.00f, 0.65f, 0.55f);

        private const float RuneRotSpeed       = 22f;     // deg/s
        private const float RuneCounterRotSpeed = -38f;   // deg/s for the inner star
        private const float TickPulseLifetime  = 0.85f;
        private const float SparkleEmitRate    = 28f;

        // --- Healing logic ---
        private float     _remaining;
        private float     _visualRadius;
        private int       _healPerTick;

        // ── Damaging variant ─────────────────────────────────────────────────
        // One controller with a branch rather than two classes: everything above the tick --
        // the rig, the pulse rings, the fade, the lifetime -- is identical, and a second
        // class would be a copy of all of it that drifts on the first fix.
        private bool      _damaging;
        private int       _damagePerTick;
        private float     _gameRadius = 1f;
        private LayerMask _targetLayers;
        private Valkur.Data.StatusApplication[] _statuses;
        private Color     _tint = Color.white;
        // Borrowed from PhysicsScratch, which owns the reset Domain-Reload-OFF demands.
        private float     _tickPeriod;
        private float     _tickTimer;
        private Transform _caster;
        private FloatingDamageSpawner _floating;

        // --- Visuals ---
        private Transform      _runeOuter;       // slow rotation
        private Transform      _runeInner;       // counter rotation (hexagram)
        private SpriteRenderer _runeOuterSr;
        private SpriteRenderer _runeInnerSr;
        private SpriteRenderer _innerGlowSr;
        private SpriteRenderer _pillarSr;
        private SpriteRenderer _casterHaloSr;
        private ParticleSystem _sparkles;

        /// <summary>
        /// The DAMAGING variant's rig, and the only one it has. A damaging field used to draw
        /// the healing rune — a hardcoded Gold/Green sacred circle lying flat on the floor — so
        /// <c>static_field</c>, a Lightning spell authoring <c>(0.95, 0.92, 0.50)</c>, rendered
        /// as a holy healing ground and its <c>_tint</c> was assigned and read by nobody.
        /// </summary>
        private StaticDomeFX   _dome;

        /// <summary>The caster's own body renderer, for the live sorting order the dome sorts
        /// its front and back hemispheres against. <c>YSortEntity</c> rewrites that order
        /// whenever the caster walks, so it has to be re-read, not captured.</summary>
        private SpriteRenderer _casterBody;

        /// <summary>Set once the registry has handed this field a compressed close. See
        /// <see cref="BeginDissipate"/>.</summary>
        private bool           _dissipating;

        private Component      _light2D;          // URP Light2D via reflection
        private static PropertyInfo _light2DIntensity;
        private static PropertyInfo _light2DColor;
        private static PropertyInfo _light2DOuterRadius;
        private static PropertyInfo _light2DInnerRadius;

        public void InitializeHealing(
            float duration,
            float gameRadius,
            float visualRadius,
            int healPerTick,
            float tickPeriod,
            Transform caster)
        {
            _ = gameRadius; // reserved for future "heal nearby allies" logic
            _remaining    = duration;
            _visualRadius = visualRadius;
            _healPerTick  = healPerTick;
            _tickPeriod   = tickPeriod;
            _tickTimer    = 0f;   // first tick fires immediately
            _caster       = caster;
            _floating     = caster != null ? caster.GetComponentInChildren<FloatingDamageSpawner>(true) : null;

            AuraSpriteFactory.EnsureSprites();
            BuildVisualRig();

            // Spawn-burst: an initial pulse + first heal tick.
            SpawnPulseRing(initial: true);
        }

        /// <summary>
        /// A field that hurts whatever stands in it, rather than mending whoever cast it.
        ///
        /// <para>Unlike <see cref="InitializeHealing"/> this one actually USES
        /// <paramref name="gameRadius"/>: the healing aura discarded it, which is how the
        /// executor got away with dividing it by 16 for the whole life of the project.</para>
        /// </summary>
        public void InitializeDamaging(
            float duration,
            float gameRadius,
            float visualRadius,
            int damagePerTick,
            float tickPeriod,
            Transform caster,
            LayerMask targetLayers,
            Valkur.Data.StatusApplication[] statuses,
            Color tint,
            Valkur.Data.SpellElement? element = null)
        {
            _remaining     = duration;
            _gameRadius    = Mathf.Max(0.1f, gameRadius);
            _visualRadius  = visualRadius;
            _damagePerTick = damagePerTick;
            _tickPeriod    = tickPeriod;
            _tickTimer     = 0f;
            _caster        = caster;
            _targetLayers  = targetLayers;
            _statuses      = statuses;
            _damaging      = true;
            _tint          = tint;

            // The dome is sized on the GAMEPLAY radius, not the visual one: this rig's whole
            // job is that the reach it draws is the reach it queries. The visual minimum exists
            // for the healing rune, which has no sweep to be honest about.
            var palette = ElementPalette.For(element ?? Valkur.Data.SpellElement.Lightning)
                                       .RecolouredTo(_tint);
            _dome = StaticDomeFX.Attach(transform, _gameRadius, palette);
            _casterBody = ResolveCasterBody(caster);

            // Deliberately NO spawn pulse ring. The dome's arcs ARE its event layer, and the
            // pulse rings this class fires per tick measured ~170 % duty against a 0.5 s
            // tickPeriod — overlapping events are not events, they are a steady texture.
        }

        /// <summary>
        /// The renderer whose sorting order the dome hangs its hemispheres off. Unity's
        /// overloaded null makes <c>??</c> unsafe on a Component, so both halves are explicit.
        /// </summary>
        private static SpriteRenderer ResolveCasterBody(Transform caster)
        {
            if (caster == null) return null;
            var own = caster.GetComponent<SpriteRenderer>();
            if (own != null) return own;
            return caster.GetComponentInChildren<SpriteRenderer>();
        }

        private void Update()
        {
            _remaining -= Time.deltaTime;
            float alpha = Mathf.Clamp01(_remaining / 0.6f); // last 0.6s fades out

            if (_remaining <= 0f)
            {
                Destroy(gameObject);
                return;
            }

            // FOLLOWS, DOES NOT PARENT. AuraExecutor used to SetParent this whole object onto
            // the caster, which inherits the entity's scale — and a scaled parent renders a
            // Light2D at `authored x lossyScale`, the failure that once put a spell light at an
            // effective 367 world units. The healing variant is still parented, because it is
            // literally an effect ON the caster and nothing under it carries a world radius.
            if (_damaging && _caster != null) transform.position = _caster.position;

            _tickTimer -= Time.deltaTime;
            if (_tickTimer <= 0f)
            {
                if (_damaging) DamageTick();
                else           HealTick();
                _tickTimer = _tickPeriod;
            }

            if (_damaging) AnimateDome(alpha);
            else           AnimateVisuals(alpha);
        }

        /// <summary>
        /// Drive the dome, re-reading the caster's live sorting order every frame.
        /// <c>YSortEntity</c> rewrites that order whenever the caster walks, so a base captured
        /// once at build time flips the far hemisphere in front of them the first time they
        /// take a step — which flattens the sphere back into the disc it exists not to be.
        /// </summary>
        private void AnimateDome(float alpha)
        {
            if (_dome == null) return;
            int order = _casterBody != null
                ? _casterBody.sortingOrder
                : SortingConfig.ComputeSortingOrder(SortingConfig.Z_ENTITY, transform.position.y);
            _dome.Tick(Time.deltaTime, alpha, order);
        }

        /// <summary>
        /// A persistent field has FIVE exit paths — its own timer, eviction by
        /// <c>maxInstances</c>, a zone change, its caster dying, and scene unload — and only the
        /// first runs any of this object's code before the GameObject is gone. Compressing
        /// <c>_remaining</c> rather than starting a second timeline means the close runs through
        /// exactly the same fade as a natural expiry, for both variants.
        /// </summary>
        public bool BeginDissipate(float seconds)
        {
            if (!isActiveAndEnabled) return false;
            if (_dissipating) return true;

            _dissipating = true;
            // A field on its way out must not still be hurting or mending: the caller has
            // already dropped the handle, so as far as maxInstances is concerned it is gone.
            _damagePerTick = 0;
            _healPerTick = 0;
            _remaining = Mathf.Min(_remaining, Mathf.Max(0.05f, seconds));
            return true;
        }

        private void OnDestroy()
        {
            _dome?.Destroy();
            _dome = null;
        }

        /// <summary>
        /// Hurt everything hostile inside the circle. The sweep is the same
        /// <c>OverlapCircleNonAlloc</c> every other area effect in the project uses, against
        /// the layers the CASTER was given -- so a monster that ever gets this spell hurts
        /// the player and not its own side, with no second code path.
        /// </summary>
        private void DamageTick()
        {
            if (_caster == null) return;

            bool prevHitTriggers = Physics2D.queriesHitTriggers;
            Physics2D.queriesHitTriggers = true;
            int count = Physics2D.OverlapCircleNonAlloc(
                transform.position, _gameRadius, Valkur.Gameplay.Combat.PhysicsScratch.AuraTargets, _targetLayers);
            Physics2D.queriesHitTriggers = prevHitTriggers;

            for (int i = 0; i < count; i++)
            {
                var col = Valkur.Gameplay.Combat.PhysicsScratch.AuraTargets[i];
                if (col == null) continue;
                if (col.transform == _caster || col.transform.IsChildOf(_caster)) continue;

                var health = col.GetComponent<Health>() ?? col.GetComponentInParent<Health>();
                if (health == null || health.IsDead) continue;

                // TakeDotDamage, not TakeDamage: the post-hit grace window exists to stop
                // several independent ATTACKERS stacking hits in one instant, and a field
                // ticking on its own clock is not a new attacker. Gating it would mean the
                // field stops working for a tenth of a second every time something else
                // lands a blow.
                health.TakeDotDamage(_damagePerTick, _caster.gameObject);
                Valkur.Gameplay.Combat.StatusApplicationFactory.ApplyAll(
                    _statuses, health.gameObject, _caster.gameObject);

                // The next arc terminates on this body. That is what turns a decorative layer
                // into a damage indicator at no extra cost — the player sees WHICH enemy the
                // field is reaching, not merely that it is running.
                _dome?.NoteTarget(health.transform.position);
            }

            // No pulse ring here. The old rig fired one every 0.5 s on top of a rune spinning at
            // a constant rate, which measured ~170 % duty: the rings overlapped, so there was
            // never a frame without one and the whole thing read as one steady texture. The
            // dome answers a hit through NoteTarget above instead, which is an event on the
            // ENEMY rather than a second ambient loop.
        }

        // --------------------------------------------------------------------
        // Logic
        // --------------------------------------------------------------------

        private void HealTick()
        {
            if (_caster == null) return;
            var health = _caster.GetComponent<Health>();
            if (health == null || health.IsDead) return;

            int before = health.CurrentHp;
            health.Heal(_healPerTick);
            int actual = health.CurrentHp - before;

            // Visual feedback per tick.
            SpawnPulseRing(initial: false);
            FlashCasterHalo();
            EmitSparkleBurst(12);
            if (actual > 0 && _floating != null) _floating.ShowHeal(actual);

        }

        // --------------------------------------------------------------------
        // Visual rig
        // --------------------------------------------------------------------

        private void BuildVisualRig()
        {
            float visScale = _visualRadius * 2f; // sprite is 1u radius -> diameter 2u

            // 1) Rune outer ring (slow rotation, on ground).
            _runeOuter = MakeChild("Rune_Outer");
            _runeOuter.localPosition = Vector3.zero;
            _runeOuter.localScale = Vector3.one * visScale;
            _runeOuterSr = AddSprite(_runeOuter, AuraSpriteFactory._runeOuterSprite, GoldCore,
                SortingConfig.LAYER_FLOOR_DECALS, 50);

            // 2) Inner rune: 2D projection of a regular dodecahedron (Schlegel diagram).
            _runeInner = MakeChild("Rune_Inner_Dodec");
            _runeInner.localPosition = Vector3.zero;
            _runeInner.localScale = Vector3.one * (visScale * 0.78f);
            _runeInnerSr = AddSprite(_runeInner, AuraSpriteFactory._runeInnerSprite, GreenCore,
                SortingConfig.LAYER_FLOOR_DECALS, 51);

            // 3) Inner soft glow disk (additive feel via additive-ish color).
            var glow = MakeChild("InnerGlow");
            glow.localPosition = Vector3.zero;
            glow.localScale = Vector3.one * (visScale * 0.95f);
            _innerGlowSr = AddSprite(glow, AuraSpriteFactory._innerGlowSprite, GreenSoft,
                SortingConfig.LAYER_FLOOR_DECALS, 49);

            // 4) Vertical light pillar behind the caster (FloorDecals so it never overlaps the player sprite).
            var pillar = MakeChild("LightPillar");
            pillar.localPosition = new Vector3(0f, _visualRadius * 0.55f, 0f);
            pillar.localScale = new Vector3(_visualRadius * 1.1f, _visualRadius * 4.5f, 1f);
            _pillarSr = AddSprite(pillar, AuraSpriteFactory._pillarSprite, GoldSoft,
                SortingConfig.LAYER_FLOOR_DECALS, 70);

            // 5) Caster halo flash on the ground under the player (also behind the sprite).
            var halo = MakeChild("CasterHalo");
            halo.localPosition = new Vector3(0f, 0.1f, 0f);
            halo.localScale = Vector3.one * 1.4f;
            _casterHaloSr = AddSprite(halo, AuraSpriteFactory._haloSprite, new Color(1f, 1f, 0.85f, 0f),
                SortingConfig.LAYER_FLOOR_DECALS, 75);

            // 6) Rising sparkle particles (rendered on FloorDecals so they always pass behind the player).
            _sparkles = BuildSparkles();

            // 7) Optional URP Light2D for global glow.
            TryAttachLight2D();
        }

        private Transform MakeChild(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            return go.transform;
        }

        private static SpriteRenderer AddSprite(Transform t, Sprite sprite, Color color, string layer, int order)
        {
            var sr = t.gameObject.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color  = color;
            // Set both ID and Name. Setting only Name occasionally fails on freshly
            // created renderers; ID is the authoritative value Unity uses for sorting.
            sr.sortingLayerID   = SortingLayer.NameToID(layer);
            sr.sortingLayerName = layer;
            sr.sortingOrder = order;
            return sr;
        }

        private ParticleSystem BuildSparkles()
        {
            var go = new GameObject("Sparkles");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;

            var ps = go.AddComponent<ParticleSystem>();
            // ParticleSystem auto-plays on AddComponent; stop it so we can configure
            // .main.duration / start* without Unity asserting.
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            // Detach the auto-created MeshRenderer; we want SpriteRenderer-style billboard via PS renderer.
            var psr = go.GetComponent<ParticleSystemRenderer>();
            psr.renderMode = ParticleSystemRenderMode.Billboard;
            psr.sharedMaterial = ElementalSprites.SharedUnlitMaterial;
            // Render BEHIND the player at all costs: use the lowest gameplay layer
            // (Ground) and set the ID directly (Unity sometimes ignores the Name
            // setter when the renderer was just created).
            int groundId = SortingLayer.NameToID(SortingConfig.LAYER_GROUND);
            psr.sortingLayerID = groundId;
            psr.sortingLayerName = SortingConfig.LAYER_GROUND;
            psr.sortingOrder = 100;
            psr.sortingFudge = 0.5f; // bias slightly forward within Ground but still BEHIND every Entities-layer sprite

            var main = ps.main;
            main.playOnAwake = false;
            main.duration = 5f;
            main.loop = true;
            main.startLifetime = 1.4f;
            main.startSpeed = 0.9f;
            main.startSize = 0.18f;
            main.startColor = new ParticleSystem.MinMaxGradient(GoldCore, GreenCore);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 256;
            main.gravityModifier = -0.05f; // gentle upward float

            var emission = ps.emission;
            emission.rateOverTime = SparkleEmitRate;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = _visualRadius * 0.95f;
            shape.radiusThickness = 0.6f;

            var color = ps.colorOverLifetime;
            color.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(GoldCore, 0f), new GradientColorKey(GreenCore, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f),
                        new GradientAlphaKey(0.9f, 0.7f), new GradientAlphaKey(0f, 1f) });
            color.color = new ParticleSystem.MinMaxGradient(grad);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            var sizeCurve = new AnimationCurve(
                new Keyframe(0f, 0.2f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0.4f));
            size.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            // Unity requires x/y/z of velocityOverLifetime to use the same MinMaxCurveMode.
            // Use TwoConstants for all three; only Y has a non-zero range (gentle upward drift).
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            psr.sharedMaterial.mainTexture = AuraSpriteFactory._sparkleSprite.texture;

            // Start the configured system.
            ps.Play(true);

            return ps;
        }

        private void EmitSparkleBurst(int count)
        {
            if (_sparkles == null) return;
            var emitParams = new ParticleSystem.EmitParams
            {
                applyShapeToPosition = true
            };
            _sparkles.Emit(emitParams, count);
        }

        // --------------------------------------------------------------------
        // Animation
        // --------------------------------------------------------------------

        private void AnimateVisuals(float alpha)
        {
            float t = Time.time;

            // Slow rotations.
            if (_runeOuter != null) _runeOuter.localRotation = Quaternion.Euler(0f, 0f, t * RuneRotSpeed);
            if (_runeInner != null) _runeInner.localRotation = Quaternion.Euler(0f, 0f, t * RuneCounterRotSpeed);

            // Rune subtle pulse.
            if (_runeOuterSr != null)
            {
                var c = GoldCore;
                c.a *= alpha * (0.85f + 0.15f * Mathf.Sin(t * 3.5f));
                _runeOuterSr.color = c;
            }
            if (_runeInnerSr != null)
            {
                var c = GreenCore;
                c.a *= alpha * (0.75f + 0.25f * Mathf.Sin(t * 2.3f + 0.7f));
                _runeInnerSr.color = c;
            }
            if (_innerGlowSr != null)
            {
                var c = GreenSoft;
                c.a *= alpha * (0.35f + 0.20f * Mathf.Sin(t * 2.0f));
                _innerGlowSr.color = c;
            }

            // Pillar gentle vertical wobble + flicker.
            if (_pillarSr != null)
            {
                var c = GoldSoft;
                c.a *= alpha * (0.45f + 0.25f * Mathf.PerlinNoise(t * 1.7f, 0f));
                _pillarSr.color = c;
                if (_pillarSr.transform.parent != null)
                {
                    var s = _pillarSr.transform.localScale;
                    s.x = _visualRadius * (1.0f + 0.06f * Mathf.Sin(t * 4.0f));
                    _pillarSr.transform.localScale = s;
                }
            }

            // Light2D follow.
            if (_light2D != null && _light2DIntensity != null)
            {
                float intensity = (0.9f + 0.25f * Mathf.Sin(t * 4f)) * alpha;
                try { _light2DIntensity.SetValue(_light2D, intensity); }
                catch (Exception) { /* ignore reflection errors */ }
            }

            // Sparkle emission scales with alpha (so it tapers off).
            if (_sparkles != null)
            {
                var em = _sparkles.emission;
                em.rateOverTime = SparkleEmitRate * alpha;
            }
        }

        // --------------------------------------------------------------------
        // Per-tick FX
        // --------------------------------------------------------------------

        private void SpawnPulseRing(bool initial)
        {
            var go = new GameObject(initial ? "PulseRing_Spawn" : "PulseRing");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = AuraSpriteFactory._pulseRingSprite;
            // Render on the floor so the ring slides underneath the player.
            sr.sortingLayerID   = SortingLayer.NameToID(SortingConfig.LAYER_FLOOR_DECALS);
            sr.sortingLayerName = SortingConfig.LAYER_FLOOR_DECALS;
            sr.sortingOrder = 80;
            sr.color = initial ? GoldCore : new Color(GreenCore.r, GreenCore.g, GreenCore.b, 0.95f);

            float startScale = _visualRadius * (initial ? 0.2f : 0.5f);
            float endScale   = _visualRadius * (initial ? 2.4f : 1.5f);
            float life       = initial ? 1.1f : TickPulseLifetime;

            StartCoroutine(AnimatePulseRing(go, sr, startScale, endScale, life));
        }

        private static IEnumerator AnimatePulseRing(GameObject go, SpriteRenderer sr, float s0, float s1, float life)
        {
            float t = 0f;
            Color baseCol = sr.color;
            while (t < life && go != null)
            {
                t += Time.deltaTime;
                float k = t / life;
                float ease = 1f - Mathf.Pow(1f - k, 3f); // ease-out cubic
                float s = Mathf.Lerp(s0, s1, ease);
                go.transform.localScale = new Vector3(s, s, 1f);
                if (sr != null)
                {
                    Color c = baseCol;
                    c.a = baseCol.a * (1f - k);
                    sr.color = c;
                }
                yield return null;
            }
            if (go != null) Destroy(go);
        }

        private void FlashCasterHalo()
        {
            if (_casterHaloSr == null) return;
            StartCoroutine(HaloFlashRoutine());
        }

        private IEnumerator HaloFlashRoutine()
        {
            float life = 0.45f;
            float t = 0f;
            while (t < life && _casterHaloSr != null)
            {
                t += Time.deltaTime;
                float k = t / life;
                float a = Mathf.Sin(k * Mathf.PI) * 0.65f; // 0->peak->0
                _casterHaloSr.color = new Color(1f, 1f, 0.85f, a);
                _casterHaloSr.transform.localScale = Vector3.one * Mathf.Lerp(1.1f, 2.1f, k);
                yield return null;
            }
            if (_casterHaloSr != null)
                _casterHaloSr.color = new Color(1f, 1f, 0.85f, 0f);
        }

        // --------------------------------------------------------------------
        // URP Light2D via reflection (no hard URP dependency)
        // --------------------------------------------------------------------

        private void TryAttachLight2D()
        {
            var t = Type.GetType(
                "UnityEngine.Rendering.Universal.Light2D, Unity.RenderPipelines.Universal.Runtime");
            if (t == null) return;

            try
            {
                _light2D = gameObject.AddComponent(t) as Component;
                if (_light2D == null) return;
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                _light2DIntensity   = _light2DIntensity   ?? t.GetProperty("intensity",              flags);
                _light2DColor       = _light2DColor       ?? t.GetProperty("color",                  flags);
                _light2DOuterRadius = _light2DOuterRadius ?? t.GetProperty("pointLightOuterRadius",  flags);
                _light2DInnerRadius = _light2DInnerRadius ?? t.GetProperty("pointLightInnerRadius",  flags);

                _light2DColor?.SetValue(_light2D, new Color(1f, 0.9f, 0.55f, 1f));
                _light2DIntensity?.SetValue(_light2D, 1.1f);
                _light2DOuterRadius?.SetValue(_light2D, _visualRadius * 2.4f);
                _light2DInnerRadius?.SetValue(_light2D, _visualRadius * 0.4f);
            }
            catch (Exception)
            {
                _light2D = null;
            }
        }
    }
}
