using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The per-frame life of the sphere: it assembles, it holds, it takes hits, and it comes
    /// apart.
    /// </summary>
    internal sealed partial class ShieldSphereFX
    {
        /// <summary>How long a ripple takes to cross the shell from pole to antipode.</summary>
        private const float RippleSeconds = 0.62f;

        /// <summary>Angular half-width of the ripple band, radians.</summary>
        private const float RippleWidth = 0.55f;

        /// <summary>How long the bright spot at the point of contact lasts.</summary>
        private const float FlashSeconds = 0.28f;

        private bool _breaking;
        private float _breakDuration;
        private float _breakTime;

        private float _impact01;
        private float _flashAge = 999f;
        private Vector3 _flashContact = Vector3.forward;

        /// <summary>True once the shell has finished coming apart and can be destroyed.</summary>
        public bool FadeComplete => _breaking && _breakTime >= _breakDuration;

        public void Tick(float deltaTime)
        {
            _age += deltaTime;
            if (_breaking) _breakTime += deltaTime;

            float breakTime = _breaking
                ? Mathf.Clamp01(_breakTime / Mathf.Max(0.01f, _breakDuration))
                : 0f;

            float assemble01 = Mathf.Clamp01(_age / AssembleSeconds);

            // The shell GROWS outward from the body with an overshoot while the motes converge
            // INWARD from outside it. They meet on the surface, which is what makes the spawn
            // read as something being assembled rather than as something being switched on.
            float shellAssemble = EaseOutBack(assemble01);
            float moteRadiusFactor = Mathf.Lerp(2.4f, 1f, EaseOutCubic(assemble01));

            // A short white-hot flare on the frame it snaps closed.
            float ignition = Mathf.Exp(-Mathf.Pow(_age / 0.20f, 2f));

            _impact01 = Mathf.Lerp(_impact01, 0f, Mathf.Clamp01(deltaTime * 6f));
            _flashAge += deltaTime;

            float envelope = Mathf.Clamp01(_age / (AssembleSeconds * 0.55f));

            AgeRipples(deltaTime);

            UpdateFacets(envelope, shellAssemble, breakTime);
            UpdateCracks(envelope, shellAssemble, breakTime, deltaTime);
            UpdateMotes(envelope, moteRadiusFactor, deltaTime, breakTime);
            UpdateRim(envelope, shellAssemble, breakTime, _impact01 + ignition);
            UpdateSheen(envelope, shellAssemble);
            UpdateFlash(envelope);
            UpdateLight(envelope, breakTime, ignition);
        }

        /// <summary>
        /// A blow was turned away. <paramref name="worldDirection"/> points from the caster
        /// toward whatever struck them; pass <c>Vector2.zero</c> when that is unknown.
        ///
        /// <para>THIS IS THE POINT OF THE WHOLE EFFECT. A shield that never reacts to being hit
        /// is an aura — and until this existed the game had no way to know, because
        /// <c>Health.ApplyDamage</c> returned in silence on the invincibility check, so the one
        /// moment a shield exists for produced no pixel at all.</para>
        /// </summary>
        public void Impact(Vector2 worldDirection, float strength01)
        {
            if (_breaking) return;

            Vector3 contact;
            if (worldDirection.sqrMagnitude > 1e-4f)
            {
                // Bias toward the camera so the struck cells are on the near hemisphere and
                // the player can actually see the hit they just survived. A hit resolved onto
                // the far side is correct and useless.
                contact = new Vector3(worldDirection.x, worldDirection.y, 0.55f).normalized;
            }
            else
            {
                contact = RandomUnitVector();
                contact.z = Mathf.Abs(contact.z);
            }

            float strength = Mathf.Clamp01(strength01);

            _ripples[_nextRipple] = new Ripple
            {
                Contact = contact,
                Age = 0f,
                Strength = Mathf.Lerp(0.55f, 1.15f, strength),
                Active = true,
            };
            _nextRipple = (_nextRipple + 1) % RIPPLE_POOL;

            PushMotesFrom(contact, Mathf.Lerp(0.10f, 0.30f, strength));

            _impact01 = Mathf.Max(_impact01, Mathf.Lerp(0.45f, 1f, strength));
            _flashAge = 0f;
            _flashContact = contact;

            LastContactPoint = _root.position + _config.BodyOffset +
                               new Vector3(contact.x, contact.y, 0f) * _config.Radius;
        }

        /// <summary>
        /// Wind the shell down over <paramref name="seconds"/>. It does not fade: the facets
        /// fly outward and spin, the motes scatter, and the rim brightens before it goes. A
        /// barrier that simply becomes transparent never looks like it stopped protecting
        /// anything — it looks like a render bug.
        /// </summary>
        public void BeginFade(float seconds)
        {
            if (_breaking) return;
            _breaking = true;
            _breakDuration = Mathf.Max(0.05f, seconds);
            _breakTime = 0f;
        }

        public void Destroy()
        {
            if (_lightGo != null) Object.Destroy(_lightGo);
        }

        // ── ripples ──────────────────────────────────────────────────────────────────

        private void AgeRipples(float deltaTime)
        {
            for (int i = 0; i < _ripples.Length; i++)
            {
                if (!_ripples[i].Active) continue;
                _ripples[i].Age += deltaTime;
                if (_ripples[i].Age >= RippleSeconds) _ripples[i].Active = false;
            }
        }

        /// <summary>
        /// How brightly the cell at <paramref name="direction"/> is lit by the live ripples.
        ///
        /// <para>The band is placed by GEODESIC distance — the angle between the cell and the
        /// point of contact — so it expands as a circle drawn ON the sphere, wrapping around
        /// the far side and converging again at the antipode. Measuring straight-line distance
        /// in screen space instead would draw an expanding disc that stops at the silhouette,
        /// which is a ripple on a plate, not on a ball.</para>
        /// </summary>
        private float RippleAt(Vector3 direction)
        {
            float total = 0f;

            for (int i = 0; i < _ripples.Length; i++)
            {
                if (!_ripples[i].Active) continue;

                float t = _ripples[i].Age / RippleSeconds;
                float front = t * Mathf.PI;
                float geodesic = Mathf.Acos(Mathf.Clamp(
                    Vector3.Dot(direction, _ripples[i].Contact), -1f, 1f));

                float band = Mathf.Exp(-Mathf.Pow((geodesic - front) / RippleWidth, 2f));
                total += band * _ripples[i].Strength * (1f - t);
            }

            return total;
        }

        private void UpdateFlash(float envelope)
        {
            if (_flash == null) return;

            float t = _flashAge / FlashSeconds;
            if (t >= 1f) { SetAlpha(_flash, 0f); return; }

            float radius = _config.Radius * Mathf.Lerp(0.45f, 1.15f, EaseOutCubic(t));
            _flashRoot.localPosition = _config.BodyOffset +
                                       new Vector3(_flashContact.x, _flashContact.y, 0f) * _config.Radius;
            _flashRoot.localScale = Vector3.one * radius * 2f;
            SetAlpha(_flash, Mathf.Pow(1f - t, 2f) * 0.85f * envelope);
        }

        private void UpdateLight(float envelope, float breakTime, float ignition)
        {
            if (_light == null) return;
            var property = ElementalProjectileVisual.GetLight2DIntensityProp();
            if (property == null) return;

            float pulse = 0.90f + 0.10f * Mathf.Sin(_age * 4.1f);
            float intensity = (1.35f * pulse + 2.4f * ignition + 2.0f * _impact01)
                              * envelope * (1f - breakTime);
            try { property.SetValue(_light, intensity); }
            catch { }
        }

        // ── easing ───────────────────────────────────────────────────────────────────

        private static float EaseOutCubic(float x)
        {
            float t = 1f - Mathf.Clamp01(x);
            return 1f - t * t * t;
        }

        /// <summary>Overshoots past 1 before settling — the shell snapping into place.</summary>
        private static float EaseOutBack(float x)
        {
            const float c1 = 1.42f;
            const float c3 = c1 + 1f;
            float t = Mathf.Clamp01(x) - 1f;
            return 1f + c3 * t * t * t + c1 * t * t;
        }
    }
}
