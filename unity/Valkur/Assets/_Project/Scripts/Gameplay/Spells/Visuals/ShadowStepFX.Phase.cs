using UnityEngine;
using Valkur.Gameplay.Combat;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The half of Shadow Step that is a MECHANIC rather than a picture: the half-second of
    /// being unhittable that the spell's slower cooldown pays for, and the animation of the
    /// ribbons that carries it.
    ///
    /// <para>THE WINDOW MUST BE VISIBLE, AND IT MUST BE SEEN TO CLOSE. A period of
    /// invulnerability the player cannot see is one they cannot use — they either waste it or
    /// never learn it exists. So the body is held at 55 % through <see cref="SpriteTintStack"/>
    /// with a slow violet shimmer, and it ENDS on a hard snap back to full opacity rather than
    /// a fade: a ramp has no moment, and the whole value of the information is knowing exactly
    /// when it stopped being true.</para>
    ///
    /// <para><c>Health.SetInvincible</c> IS SAVED AND RESTORED, NEVER CLEARED. That one bool
    /// has three independent owners — the dev console's god mode, the F4 editor's test
    /// invulnerability and the magic shield — and writing <c>false</c> at the end of the window
    /// would switch off whichever of the others was holding it. The defect has shipped twice in
    /// this project already; <c>ShieldController</c> records the same save/restore.</para>
    /// </summary>
    internal sealed partial class ShadowStepFX
    {
        /// <summary>How long the snap back to full opacity takes. Short enough to read as a
        /// snap, long enough not to be lost between two frames.</summary>
        private const float PHASE_SNAP_SECONDS = 0.08f;

        /// <summary>Body alpha while phased.</summary>
        private const float PHASE_ALPHA = 0.55f;

        /// <summary>How far the ribbons are pulled below the silhouette, as a fraction of it.</summary>
        private const float RIBBON_SINK = 1.1f;

        private SpriteTintStack _bodyTint;
        private Health _health;
        private bool _hadInvincibility;
        private bool _invincibilityTaken;
        private float _phaseSeconds;

        /// <summary>
        /// Take the arriving character's alpha, and — if the spell authors a window — their
        /// invincibility. The stack is resolved from the ENTITY, not from the renderer: status
        /// effects and the hit flash attach theirs at the entity root, and two stacks on one
        /// character would each hold a different idea of the resting colour.
        /// </summary>
        private void TakeBody(Transform owner, float phaseSeconds)
        {
            if (owner == null) return;

            _phaseSeconds = Mathf.Max(0f, phaseSeconds);
            _bodyTint = SpriteTintStack.Attach(owner.gameObject);
            _bodyTint?.Set(TintLayer.Teleport, new Color(1f, 1f, 1f, 0f));

            if (_phaseSeconds <= 0f) return;

            _health = owner.GetComponent<Health>();
            if (_health == null) return;

            _hadInvincibility = _health.IsInvincible;
            _invincibilityTaken = true;
            _health.SetInvincible(true);
        }

        private void Update()
        {
            _age += Time.deltaTime;

            UpdateRibbons();
            UpdatePathMotes();
            UpdateBody();

            if (_age >= _life) Destroy(gameObject);
        }

        private void UpdateRibbons()
        {
            if (_ribbons == null) return;

            for (int i = 0; i < _ribbons.Length; i++)
            {
                float own = _age - _delay - _ribbonBirth[i];
                float k = Mathf.Clamp01(own / RIBBON_SECONDS);

                if (own < 0f)
                {
                    // Not born yet. The knit's ribbons wait below the floor; the peel's wait
                    // in place, still whole.
                    _ribbons[i].color = WithAlpha(_palette.glow, _mode == Mode.Peel ? RIBBON_ALPHA : 0f);
                    continue;
                }

                float sink, height, alpha;
                if (_mode == Mode.Peel)
                {
                    // DRAWN DOWN, accelerating: something pulling on it from below, not
                    // something falling. The strip shortens as it goes, so it reads as being
                    // taken INTO the floor rather than sliding behind it.
                    sink = _silhouette.y * RIBBON_SINK * k * k;
                    height = 1f - k;
                    alpha = RIBBON_ALPHA * (1f - Mathf.SmoothStep(0.6f, 1f, k));
                }
                else
                {
                    // Rising and knitting shut, decelerating into place.
                    float ease = 1f - (1f - k) * (1f - k);
                    sink = _silhouette.y * RIBBON_SINK * (1f - ease);
                    height = ease;
                    alpha = RIBBON_ALPHA * Mathf.Clamp01(k / 0.15f) * (1f - Mathf.SmoothStep(0.75f, 1f, k));
                }

                var t = _ribbons[i].transform;
                // The strips also spread very slightly apart on the way down and close on the
                // way up: what makes it read as PEELING rather than as one sprite sliding.
                float spread = _mode == Mode.Peel ? 1f + 0.30f * k : 1f + 0.30f * (1f - k);
                t.localPosition = new Vector3(_ribbonSlot[i].x * spread, _ribbonSlot[i].y - sink, 0f);
                t.localScale = new Vector3(_ribbonRestScale.x,
                                           _ribbonRestScale.y * Mathf.Max(0.001f, height), 1f);
                _ribbons[i].color = WithAlpha(_palette.glow, alpha);
            }
        }

        private void UpdatePathMotes()
        {
            if (_motes == null) return;

            // Lit in ORDER, over the same window the ribbons knit in, so the trail arrives
            // rather than appearing all at once.
            float window = Mathf.Max(0.01f, _delay + RIBBON_SECONDS);
            for (int i = 0; i < _motes.Length; i++)
            {
                float along = (i + 0.5f) / _motes.Length;
                float own = _age - along * window;
                float alpha = own <= 0f
                    ? 0f
                    : Mathf.Clamp01(own / 0.04f) * (1f - Mathf.Clamp01(own / 0.30f));
                _motes[i].color = WithAlpha(_palette.hotCore, alpha * 0.7f);
            }
        }

        private void UpdateBody()
        {
            if (_mode != Mode.Knit || _bodyTint == null) return;

            float knitEnd = _delay + RIBBON_SECONDS;

            if (_age < _delay)
            {
                _bodyTint.Set(TintLayer.Teleport, new Color(1f, 1f, 1f, 0f));
                return;
            }

            if (_age < knitEnd)
            {
                // Coming back into existence behind the ribbons.
                float k = Mathf.Clamp01((_age - _delay) / RIBBON_SECONDS);
                float presence = Mathf.Lerp(0f, _phaseSeconds > 0f ? PHASE_ALPHA : 1f, k * k);
                _bodyTint.Set(TintLayer.Teleport, new Color(1f, 1f, 1f, presence));
                return;
            }

            if (_phaseSeconds <= 0f) { ReleaseBody(); return; }

            float phaseEnd = knitEnd + _phaseSeconds;
            if (_age < phaseEnd)
            {
                // Held at 55 % with a slow shimmer — slow because a fast flicker reads as a
                // rendering fault, and this has to read as a state the character is in.
                float shimmer = 0.92f + 0.08f * Mathf.Sin(_age * 9f);
                _bodyTint.Set(TintLayer.Teleport,
                              new Color(1f, 1f, 1f, PHASE_ALPHA * shimmer));
                return;
            }

            // The snap. It is the only part of the window the player can act on, so it is a
            // fast ramp to full and then the layer is gone.
            float snap = Mathf.Clamp01((_age - phaseEnd) / PHASE_SNAP_SECONDS);
            if (snap < 1f)
            {
                _bodyTint.Set(TintLayer.Teleport,
                              new Color(1f, 1f, 1f, Mathf.Lerp(PHASE_ALPHA, 1f, snap)));
                return;
            }

            ReleaseBody();
        }

        /// <summary>
        /// Hand the character back everything this rig borrowed. Idempotent, because it is
        /// reached both from the normal end of the window and from <see cref="OnDestroy"/>.
        /// </summary>
        private void ReleaseBody()
        {
            _bodyTint?.Clear(TintLayer.Teleport);

            if (!_invincibilityTaken || _health == null) return;
            _invincibilityTaken = false;
            // RESTORED, not cleared: whichever of the other three owners was holding this
            // before the blink keeps holding it afterwards.
            _health.SetInvincible(_hadInvincibility);
        }

        /// <summary>
        /// Whatever happened — window finished, scene torn down, the caster killed mid-blink —
        /// the character must not be left translucent or permanently invincible.
        /// </summary>
        private void OnDestroy()
        {
            ReleaseBody();

            // The ribbon sprites are created per cast out of the body's texture, so they are
            // this object's to destroy. The TEXTURE is not: it belongs to the atlas.
            if (_slices == null) return;
            for (int i = 0; i < _slices.Length; i++)
                if (_slices[i] != null) Destroy(_slices[i]);
            _slices = null;
        }
    }
}
