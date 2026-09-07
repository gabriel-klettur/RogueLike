using UnityEngine;
using Valkur.Core;
using Valkur.Core.Input;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// The per-frame half: what the rig is told each frame and how it gets there.
    ///
    /// <para>There is exactly one moving quantity: the drawn heading, eased so the rig does not
    /// jitter with the mouse. Everything else about the look is constant. That is the whole
    /// design — the indicator answers WHERE YOU POINT and refuses every other job — and it is
    /// smaller than it was on purpose: readiness, cast phase and posture each lived here for a
    /// while and each was a second meaning loaded onto the one shape under the character.
    /// The old chevron's <c>sin(t*3)</c> alpha pulse is the other failure at the same spot —
    /// a lamp with a flicker, on forever, saying nothing.</para>
    ///
    /// <para>Both layers are additive, so COLOUR is brightness and ALPHA is coverage: every
    /// gain here multiplies RGB and leaves the authored alphas alone. Reaching for the alpha to
    /// dim the aura would make it NARROWER, not fainter.</para>
    /// </summary>
    public partial class FacingIndicator
    {
        private float _drawnAngleDeg;

        /// <summary>Seconds since the last <see cref="Pulse"/>. Starts past the end of the
        /// envelope so a freshly built rig is at rest rather than mid-flash.</summary>
        private float _pulseAge = float.PositiveInfinity;

        private int _bodyOrder;
        private bool _aimBehindBody;

        /// <summary>Test seam: drives the heading in place of <see cref="PlayerController.FacingDirection"/>
        /// when set, so an EditMode test can aim the rig without a PlayerController. Production
        /// never sets it.</summary>
        internal Vector2? FacingOverride;

        // ── Read by tests and the DevConsole ─────────────────────────────────────────

        /// <summary>
        /// Acknowledge an action the player just took along the aim line — a cast, a chop, a
        /// pick strike. PUSHED IN by whoever acted, never pulled: the rig deliberately has no
        /// idea what a spell or a tree is, and the source guard in the tests keeps it that way.
        ///
        /// <para>This is the one thing besides the heading that moves the rig, and the line it
        /// sits on is the whole design. A STATE would have to be read — a sweep that dimmed
        /// while the primary recovered was a second readout competing with the first. An EVENT
        /// tells the player nothing they did not just do; it is confirmation at the place they
        /// are already looking. Re-pulsing mid-pulse restarts the envelope rather than stacking,
        /// so a fast harvest rhythm reads as separate beats and never as a rig that is
        /// permanently lit.</para>
        /// </summary>
        internal void Pulse() => _pulseAge = 0f;

        internal Transform RootTransform => _root;
        internal Transform AimTransform => _aim;
        internal bool IsShowing => _tipSr != null && _tipSr.enabled;
        internal float DrawnHeadingDeg => _drawnAngleDeg;
        /// <summary>The body's Y-sort order the rig was rebased on this frame.</summary>
        internal int BodyOrder => _bodyOrder;
        /// <summary>True while the aim pieces sort behind the body (aim points north).</summary>
        internal bool AimIsBehindBody => _aimBehindBody;
        internal Sprite TipSprite => _tipSr != null ? _tipSr.sprite : null;
        internal float TipLocalX => _tipSr != null ? _tipSr.transform.localPosition.x : 0f;
        internal Color TipColor => _tipSr != null ? _tipSr.color : Color.clear;
        internal Color AuraColor => _auraSr != null ? _auraSr.color : Color.clear;
        internal SpriteRenderer TipRenderer => _tipSr;
        internal SpriteRenderer AuraRenderer => _auraSr;

        private void LateUpdate()
        {
            if (_root == null) return;
            ApplyState(Time.deltaTime, snapHeading: false);
        }

        /// <summary>
        /// Resolve everything the rig shows this frame and write it. <paramref name="dt"/> is a
        /// parameter rather than <c>Time.deltaTime</c> for the reason the vortex records: a rig
        /// that reads the clock itself cannot be measured from a test or a probe.
        /// <paramref name="snapHeading"/> skips every smoothing filter — used on build so the
        /// first frame does not swing in from zero degrees.
        /// </summary>
        internal void ApplyState(float dt, bool snapHeading)
        {
            if (_root == null) return;

            _root.position = transform.position;

            bool visible = ResolveVisible();
            SetVisible(visible);
            if (!visible) return;

            // ── Heading ──
            Vector2 facing = FacingOverride ?? (_player != null ? _player.FacingDirection : Vector2.zero);
            if (facing.sqrMagnitude > 0.0001f)
            {
                float target = Mathf.Atan2(facing.y, facing.x) * Mathf.Rad2Deg;
                _drawnAngleDeg = snapHeading
                    ? target
                    : Mathf.LerpAngle(_drawnAngleDeg, target, Response(FacingIndicatorStyle.TurnResponse, dt));
            }
            _aim.localRotation = Quaternion.Euler(0f, 0f, _drawnAngleDeg);

            ApplyDepth();
            TickPulse(dt);
        }

        /// <summary>
        /// Advance the pulse, and write NOTHING once it is over.
        ///
        /// <para>At rest the two colours and the tip offset are exactly what the build painted,
        /// so this early-returns and no <c>SpriteRenderer.color</c> is touched — which matters
        /// because a colour set dirties URP's batcher, and at rest that would be sixty writes a
        /// second to set values that cannot have changed. The frame the envelope reaches zero
        /// still paints, and that is the one that settles the rig back onto its base values.</para>
        /// </summary>
        private void TickPulse(float dt)
        {
            if (_pulseAge >= FacingIndicatorStyle.PulseSeconds) return;
            _pulseAge += dt;
            PaintLayers(PulseEnvelope());
        }

        /// <summary>Hard onset, quadratic decay. Zero outside the window.</summary>
        private float PulseEnvelope()
        {
            if (_pulseAge < 0f || _pulseAge >= FacingIndicatorStyle.PulseSeconds) return 0f;
            float t = 1f - _pulseAge / FacingIndicatorStyle.PulseSeconds;
            return t * t;
        }

        /// <summary>
        /// Paint both layers at a given pulse strength (0 = at rest). Called once from the
        /// build and then only while a pulse is live.
        ///
        /// <para>Both layers move together on every axis — colour, offset and scale. They are
        /// one shape, and a glow that lagged the chevron it frames would read as two objects
        /// that merely overlap.</para>
        /// </summary>
        private void PaintLayers(float pulse)
        {
            float gain = 1f + (FacingIndicatorStyle.PulseGain - 1f) * pulse;

            _auraSr.color = Scaled(FacingIndicatorStyle.AuraTone,
                FacingIndicatorStyle.AuraGain * gain, FacingIndicatorStyle.AuraAlpha);
            _tipSr.color = Scaled(FacingIndicatorStyle.TipTone,
                FacingIndicatorStyle.TipGain * gain, FacingIndicatorStyle.TipAlpha);

            var tipPos = new Vector3(
                FacingIndicatorStyle.TipRadius + FacingIndicatorStyle.PulseKick * pulse, 0f, 0f);
            _tipSr.transform.localPosition = tipPos;
            _auraSr.transform.localPosition = tipPos;

            // The scale is what the player actually SEES of a pulse — the brightening above
            // clips to the same white the tip already is. The aura grows with it, or the glow
            // stops belonging to the shape halfway through every beat.
            float grow = 1f + FacingIndicatorStyle.PulseTipScale * pulse;
            _tipSr.transform.localScale = Vector3.one * (FacingIndicatorStyle.TipSize * grow);
            _auraSr.transform.localScale = Vector3.one
                * (FacingIndicatorStyle.TipSize * FacingIndicatorStyle.AuraGrow * grow);
        }

        // ── Depth ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Put every piece on the body's layer, behind or in front of the body by the sign of
        /// the aim.
        ///
        /// <para>The body's order is COMPUTED from the feet with the same formula
        /// <c>YSortEntity</c> uses, not read back from the renderer: both run in LateUpdate in
        /// an undefined order, so a read would trail the body by a frame whenever it moved and
        /// the rig would flicker across it while running. The LAYER is read from the renderer,
        /// because it changes rarely and for reasons the formula cannot know (the overhead
        /// visual layer).</para>
        /// </summary>
        private void ApplyDepth()
        {
            _bodyOrder = SortingConfig.ComputeSortingOrder(SortingConfig.Z_ENTITY, transform.position.y);
            _aimBehindBody = Mathf.Sin(_drawnAngleDeg * Mathf.Deg2Rad) > FacingIndicatorStyle.BehindAimSine;

            int layerId = _bodySr != null
                ? _bodySr.sortingLayerID
                : SortingLayer.NameToID(SortingConfig.LAYER_ENTITIES);
            int aimBase = _bodyOrder
                + (_aimBehindBody ? -FacingIndicatorStyle.DepthGap : FacingIndicatorStyle.DepthGap);

            SetDepth(_auraSr, layerId, aimBase + FacingIndicatorStyle.SubOrderAura);
            SetDepth(_tipSr, layerId, aimBase + FacingIndicatorStyle.SubOrderTip);
        }

        /// <summary>Write only on change: a sortingOrder set dirties URP's batcher.</summary>
        private static void SetDepth(SpriteRenderer sr, int layerId, int order)
        {
            if (sr.sortingLayerID != layerId) sr.sortingLayerID = layerId;
            if (sr.sortingOrder != order) sr.sortingOrder = order;
        }

        // ── Resolution ────────────────────────────────────────────────────────────────

        private bool ResolveVisible()
        {
            if (_health != null && _health.IsDead) return false;
            // Chat, the console and every runtime editor: the player is not playing, so an aim
            // that goes on tracking the cursor under a panel is a pointer with nothing to point.
            if (InputBlocker.IsGameplayBlocked) return false;
            if (GameEditorManager.HasInstance && GameEditorManager.Instance.AnyEditorActive) return false;
            return true;
        }

        private void SetVisible(bool visible)
        {
            if (_tipSr.enabled == visible) return;
            _auraSr.enabled = visible;
            _tipSr.enabled = visible;
        }

        // ── Maths ─────────────────────────────────────────────────────────────────────

        private static float Response(float rate, float dt)
            => 1f - Mathf.Exp(-rate * Mathf.Max(0f, dt));

        private static Color Scaled(Color c, float gain, float alpha)
            => new Color(c.r * gain, c.g * gain, c.b * gain, alpha);

    }
}
