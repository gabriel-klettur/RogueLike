using UnityEngine;
using Valkur.Gameplay.Combat;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Per-frame driver: the two ramps every silhouette reads, the layers they share, and the
    /// dispatch to whichever shape is running.
    /// </summary>
    internal sealed partial class BuffAuraFX
    {
        private bool _expired;

        private void Update()
        {
            if (_expired || !_built) return;

            _age += Time.deltaTime;

            // FOLLOW, never parent. Parenting inherits the entity scale and would scale the
            // Light2D radius with it.
            if (_owner != null && _root != null)
                _root.position = _owner.position + _centerOffset;

            // 0 while the buff is comfortable, ramping to 1 as it runs out. Every silhouette
            // spends this in its own material, so the beat is one decision rather than five.
            float remaining = _duration - _age;
            float warn = remaining >= WARN_SECONDS
                ? 0f
                : 1f - Mathf.Clamp01(remaining / WARN_SECONDS);

            // How far the SHAPE has arrived. Not a fade-in: a shell assembling, bark climbing
            // and a column descending all read this, and each spends it differently.
            float onset = _profile.OnsetSeconds > 0f
                ? Mathf.Clamp01(_age / _profile.OnsetSeconds)
                : 1f;

            SyncSortingToCaster();

            switch (_profile.Silhouette)
            {
                case BuffSilhouette.Shell: TickShell(onset, warn); break;
                case BuffSilhouette.Growth: TickGrowth(onset, warn); break;
                case BuffSilhouette.Radiance: TickRadiance(onset, warn); break;
                case BuffSilhouette.Fervor: TickFervor(onset, warn); break;
                default: TickNeutralAura(onset, warn); break;
            }

            TickGroundRing(onset, warn);
            TickMotes(onset, warn);
            TickBodyTint(onset, warn);
            TickLight(onset, warn);

            if (_age >= _duration) Expire();
        }

        /// <summary>
        /// The buff ran out. The rig goes; the component STAYS, disabled and inert.
        ///
        /// <para>Two reasons, and the first is a real race. The component lives on the CASTER,
        /// so expiry could never be <c>Destroy(gameObject)</c> — that destroys the player — and
        /// <c>Destroy(this)</c> is deferred to end of frame, during which
        /// <c>GetComponent</c> still finds the dying component while <c>== null</c> is
        /// already true for it. A recast landing inside that window would either revive a
        /// doomed instance or trip <c>[DisallowMultipleComponent]</c>, which logs an error into
        /// a console this project requires to be clean. Parking the component removes the
        /// window entirely: the next cast finds it, re-enables it and rebuilds.</para>
        ///
        /// <para>The tint is cleared HERE as well as in <c>OnDestroy</c>. A persistent effect
        /// has five exit paths and only OnDestroy is on all of them, but this one no longer
        /// goes through OnDestroy at all — so without this the character would keep the buff's
        /// colour for the rest of the run.</para>
        /// </summary>
        private void Expire()
        {
            _expired = true;
            enabled = false;
            _built = false;
            TearDownRig();
            if (_bodyTint != null) _bodyTint.Clear(TintLayer.Buff);
        }

        /// <summary>
        /// The two silhouettes whose pieces enclose the body sort against the CASTER's live
        /// order, and it has to be re-read: <c>YSortEntity</c> rewrites that order whenever the
        /// character walks, so a value captured once at build time pops the far half in front
        /// the first time they take a step. <c>ShieldSphereFX</c> records the same measurement.
        /// </summary>
        private void SyncSortingToCaster()
        {
            if (_bodyRenderer == null) return;
            int order = _bodyRenderer.sortingOrder;
            if (order == _lastBodyOrder) return;
            _lastBodyOrder = order;

            RebaseShellOrders(order);
            RebaseGrowthOrders(order);
        }

        /// <summary>
        /// The held ground circle. Skipped entirely for a silhouette whose ring is a one-shot
        /// wave — <see cref="BuffSilhouette.Fervor"/> owns its shockwave in its own tick,
        /// because a shout's ring travels once and is gone rather than orbiting forever.
        /// </summary>
        private void TickGroundRing(float onset, float warn)
        {
            if (_groundRing == null || !_profile.GroundRingPersists) return;

            _spin += Mathf.Lerp(SPIN_CALM, SPIN_WARN, warn) * 360f * Time.deltaTime;
            _groundRing.transform.localRotation = Quaternion.Euler(0f, 0f, _spin);

            // The ring CONTRACTS as it warns. A ring that grew would read as the buff getting
            // stronger, which is the opposite of what is about to happen.
            SetRingRadius(_groundRing, _profile.GroundRingRadius * Mathf.Lerp(1f, 0.72f, warn));
            _groundRing.color = WithAlpha(_profile.Palette.core,
                0.55f * onset * Mathf.Lerp(1f, 0.55f, warn));
        }

        private void TickMotes(float onset, float warn)
        {
            if (_motes == null) return;

            _moteTimer -= Time.deltaTime;
            if (_moteTimer <= 0f && warn < 0.9f)
            {
                _moteTimer = Mathf.Lerp(_profile.MoteInterval, _profile.MoteInterval * 0.45f, warn);
                _moteAge[_nextMote] = 0f;
                SeedMote(_motes[_nextMote].transform, out _moteDrift[_nextMote]);
                _nextMote = (_nextMote + 1) % _motes.Length;
            }

            for (int i = 0; i < _motes.Length; i++)
            {
                if (_moteAge[i] >= _profile.MoteLife)
                {
                    _motes[i].color = WithAlpha(_moteColor, 0f);
                    continue;
                }

                _moteAge[i] += Time.deltaTime;
                float k = Mathf.Clamp01(_moteAge[i] / _profile.MoteLife);
                _motes[i].transform.localPosition += _moteDrift[i] * Time.deltaTime;

                // Rise, brighten, then fade. A linear fade reads as a dimmer switch; this
                // reads as something lifting off and going out.
                float a = Mathf.Sin(k * Mathf.PI);
                _motes[i].color = WithAlpha(_moteColor, 0.70f * a * onset);
                _motes[i].transform.localScale = Vector3.one * (_profile.MoteSize * (0.6f + 0.4f * a));
            }
        }

        /// <summary>
        /// The body's colour has exactly one owner and it is <c>SpriteTintStack</c>. This layer
        /// MULTIPLIES, which is why every silhouette but Growth holds it near white: driving it
        /// hard reads as the character being DIMMED rather than as power sitting on them. For
        /// Growth the dimming IS the effect, which is why it authors the only strong value.
        /// </summary>
        private void TickBodyTint(float onset, float warn)
        {
            if (_bodyTint == null) return;
            float k = _profile.BodyTint * _tintBoost * onset * Mathf.Lerp(1f, 0.35f, warn);
            _bodyTint.Set(TintLayer.Buff, Color.Lerp(Color.white, _profile.BodyTintTarget, k));
        }

        private void TickLight(float onset, float warn)
        {
            if (_light == null) return;
            float breath = 0.85f + 0.15f * Mathf.Sin(_age * 2.1f);
            ElementalProjectileVisual.GetLight2DIntensityProp()
                ?.SetValue(_light, _profile.LightIntensity * onset * breath * Mathf.Lerp(1f, 0.4f, warn));
        }

        /// <summary>The neutral fallback's own layer: a slow breath on the silhouette.</summary>
        private void TickNeutralAura(float onset, float warn)
        {
            if (_rim == null) return;
            float breath = 0.82f + 0.18f * Mathf.Sin(_age * 2.1f);
            _rim.color = WithAlpha(_profile.Palette.hotCore,
                0.30f * onset * breath * Mathf.Lerp(1f, 0.4f, warn));
        }

        // ── Mote configuration, per silhouette ────────────────────────────────

        private Sprite MoteSprite() => _profile.Silhouette switch
        {
            // Ice sheds flakes; bark sheds leaves; light and heat shed points of light.
            BuffSilhouette.Shell => ElementalSprites.Snowflake,
            BuffSilhouette.Growth => ElementalSprites.Wisp,
            BuffSilhouette.Fervor => ElementalSprites.Sparkle,
            _ => ElementalSprites.Sparkle,
        };

        private Color MoteColour() => _profile.Silhouette switch
        {
            // Growth's mote is its ONE additive layer, and it is the living tip of the plant —
            // the thing that makes the rig read as wood that is alive rather than as armour.
            BuffSilhouette.Growth => _profile.Bark.Leaf,
            _ => _profile.Palette.hotCore,
        };

        /// <summary>
        /// Every mote in this rig is additive, Growth's included: it is the single exception
        /// its own opaque tendrils are measured against. The method exists so the statement is
        /// written down where the next silhouette will read it.
        /// </summary>
        private bool MotesAreAdditive() => true;

        private void SeedMote(Transform t, out Vector3 drift)
        {
            float halfX = _size.x * 0.5f;
            switch (_profile.Silhouette)
            {
                // Flakes come off the SHOULDERS and fall. Rime shedding downward is the one
                // motion that says the armour is cold rather than that it is glowing.
                case BuffSilhouette.Shell:
                    t.localPosition = new Vector3(Random.Range(-halfX, halfX),
                                                  Random.Range(_size.y * 0.10f, _size.y * 0.32f), 0f);
                    drift = new Vector3(Random.Range(-0.10f, 0.10f), Random.Range(-0.55f, -0.28f), 0f);
                    break;

                // A leaf falls slowly and sideways, never straight up: it is matter coming off
                // a plant, not light leaving a body.
                case BuffSilhouette.Growth:
                    t.localPosition = new Vector3(Random.Range(-halfX, halfX),
                                                  Random.Range(_size.y * 0.05f, _size.y * 0.30f), 0f);
                    drift = new Vector3(Random.Range(-0.30f, 0.30f), Random.Range(-0.22f, -0.05f), 0f);
                    break;

                // Heat rises fast and close to the body. It is coming off skin, not out of a
                // spell, so it stays inside the silhouette rather than orbiting it.
                case BuffSilhouette.Fervor:
                    t.localPosition = new Vector3(Random.Range(-halfX * 0.7f, halfX * 0.7f),
                                                  Random.Range(-_size.y * 0.05f, _size.y * 0.25f), 0f);
                    drift = new Vector3(Random.Range(-0.08f, 0.08f), Random.Range(0.75f, 1.15f), 0f);
                    break;

                default:
                    t.localPosition = new Vector3(Random.Range(-halfX, halfX),
                                                  Random.Range(-_size.y * 0.15f, _size.y * 0.35f), 0f);
                    drift = new Vector3(Random.Range(-0.15f, 0.15f), Random.Range(0.5f, 0.9f), 0f);
                    break;
            }
        }
    }
}
