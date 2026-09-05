using UnityEngine;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Data;
using Valkur.Gameplay.Editors;

namespace Valkur.Gameplay.VFX
{
    /// <summary>
    /// Resizing a placed emitter by dragging the edges of its two authored boxes.
    ///
    /// The interaction, in the order the hand does it: hover an emitter, click to select it,
    /// hover one of its two boxes to arm it, click to take it, hover one of that box's four
    /// edges, drag. Each step is reversible by the step before — clicking off the boxes drops
    /// back to a plain selection, Escape drops the box, Escape during a drag puts the size
    /// back where it started.
    ///
    /// WHAT A DRAG WRITES. The two boxes are backed by different things, and the difference is
    /// visible in how they behave:
    ///
    ///  • The EMISSION box is spawn width and height (or the emission radius, for the circular
    ///    kinds). Dragging one side pins the opposite one — the emitter itself slides half the
    ///    growth, exactly as resizing a rectangle does everywhere else — and Shift resizes
    ///    around the centre instead.
    ///  • The REACH box is the whole family of motion terms at once. There is no single field
    ///    behind it, so its edges are solved for a reach ratio and both sides move together;
    ///    the emitter never slides.
    ///
    /// WHERE IT IS WRITTEN. Particle parameters live on the preset, which every placement
    /// shares, so a resize cannot touch them: it writes ratios onto the INSTANCE
    /// (<see cref="ParticleInstanceOverrides"/>), which the emitter folds over the preset when
    /// it builds its systems and the serializer stores per record. Resizing one leaf field
    /// leaves the other 83 alone.
    ///
    /// The geometry is in <see cref="ParticleBoundsHandles"/>, deliberately outside this file:
    /// a state machine driven by a live mouse cannot be tested, and the arithmetic under it is
    /// where the mistakes are.
    /// </summary>
    public partial class ParticlesRuntimeEditor
    {
        /// <summary>
        /// Grab distance for an edge, in SCREEN pixels, converted to world units against the
        /// live camera. Fixed world units would make handles unusable at either end of the
        /// zoom range — a tenth of a unit is half the screen zoomed in and invisible zoomed out.
        /// </summary>
        private const float BOUNDS_GRAB_PIXELS = 9f;

        /// <summary>
        /// Authoring grid for a dragged edge: one art texel at 16 PPU. Held Alt drags free.
        /// Anything finer than a texel is a size the pixel-art world cannot show.
        /// </summary>
        private const float BOUNDS_SNAP = 1f / 16f;

        private static readonly Color BOUNDS_HIGHLIGHT = new Color(1f, 1f, 1f, 0.95f);

        // ── State ────────────────────────────────────────────────────────────────

        /// <summary>Box the author has taken hold of, or None while only the emitter is selected.</summary>
        private ParticleBoundsBox _boundsBox = ParticleBoundsBox.None;

        /// <summary>Box under the cursor this frame (armed but not taken).</summary>
        private ParticleBoundsBox _boundsHoverBox = ParticleBoundsBox.None;

        /// <summary>Edge under the cursor this frame, within the taken box.</summary>
        private ParticleBoundsEdge _boundsHoverEdge = ParticleBoundsEdge.None;

        private bool _boundsDragging;
        private ParticleBoundsEdge _boundsDragEdge = ParticleBoundsEdge.None;
        private ParticleInstanceOverrides _boundsDragStartOverrides = ParticleInstanceOverrides.None;
        private Vector3 _boundsDragStartPosition;

        // ── Public read-only state (status line, tests) ──────────────────────────

        /// <summary>Box currently taken for resizing.</summary>
        public ParticleBoundsBox ActiveBoundsBox => _boundsBox;

        /// <summary>Edge currently under the cursor or being dragged.</summary>
        public ParticleBoundsEdge ActiveBoundsEdge =>
            _boundsDragging ? _boundsDragEdge : _boundsHoverEdge;

        // ── Frame entry point ────────────────────────────────────────────────────

        /// <summary>
        /// Runs before the rest of the map interaction. Returns true when it has consumed the
        /// input — a drag in progress, an edge grabbed, a box taken — so a click on a handle
        /// cannot also re-select an emitter or place a new one underneath.
        /// </summary>
        private bool HandleBoundsEditing(Vector3 worldPos)
        {
            if (_mode != EditorMode.Select || _activeInstance == null)
            {
                ClearBoundsState();
                return false;
            }

            var emitter = _activeInstance.GetComponentInParent<ParticleEmitter>();
            var identity = _activeInstance.GetComponentInParent<PersistedParticleInstance>();
            if (emitter == null || identity == null || emitter.Preset == null)
            {
                ClearBoundsState();
                return false;
            }

            if (_boundsDragging) return UpdateBoundsDrag(emitter, identity, worldPos);

            UpdateBoundsHover(emitter, worldPos);

            // Escape gives the box back before it gives the emitter back.
            if (_boundsBox != ParticleBoundsBox.None && EditorInput.ClosePressed())
            {
                _boundsBox = ParticleBoundsBox.None;
                SetStatus("Bounds released.");
                return true;
            }

            if (!MouseInputManager.WasLeftMouseButtonPressedThisFrame()) return false;

            // Grabbing an edge of the taken box starts a drag.
            if (_boundsBox != ParticleBoundsBox.None && _boundsHoverEdge != ParticleBoundsEdge.None)
            {
                BeginBoundsDrag(emitter, identity);
                return true;
            }

            // Otherwise a click on either box's border takes that box.
            if (_boundsHoverBox != ParticleBoundsBox.None)
            {
                _boundsBox = _boundsHoverBox;
                SetStatus(_boundsBox == ParticleBoundsBox.Emission
                    ? "Emission area taken — drag a side to resize. Shift = from centre, Alt = no snap."
                    : "Reach taken — drag a side to scale how far the particles travel.");
                return true;
            }

            // A click anywhere else drops the box and falls through to normal selection.
            _boundsBox = ParticleBoundsBox.None;
            return false;
        }

        // ── Hover ────────────────────────────────────────────────────────────────

        private void UpdateBoundsHover(ParticleEmitter emitter, Vector3 worldPos)
        {
            float tolerance = BoundsGrabWorld();
            Vector2 origin = emitter.transform.position;

            var emission = EmissionBoxOf(emitter);
            var reach = ReachBoxOf(emitter);

            _boundsHoverBox = ParticleBoundsHandles.PickBox(emission, reach, origin, worldPos, tolerance);

            // Edges are only offered for the box that has been taken: four handles on two
            // nested boxes under one cursor is a coin toss, and the wrong one silently edits a
            // different field.
            _boundsHoverEdge = _boundsBox == ParticleBoundsBox.None
                ? ParticleBoundsEdge.None
                : ParticleBoundsHandles.PickEdge(
                    _boundsBox == ParticleBoundsBox.Emission ? emission : reach,
                    origin, worldPos, tolerance);
        }

        // ── Drag ─────────────────────────────────────────────────────────────────

        private void BeginBoundsDrag(ParticleEmitter emitter, PersistedParticleInstance identity)
        {
            _boundsDragging = true;
            _boundsDragEdge = _boundsHoverEdge;
            _boundsDragStartOverrides = identity.Overrides;
            _boundsDragStartPosition = emitter.transform.position;
        }

        private bool UpdateBoundsDrag(ParticleEmitter emitter, PersistedParticleInstance identity,
                                      Vector3 worldPos)
        {
            // Escape mid-drag restores the size the drag started from. Without it the only way
            // out of a bad drag is to finish it and undo, and the emitter has already been
            // rebuilt at every intermediate size on the way.
            if (EditorInput.ClosePressed())
            {
                ApplyBounds(emitter, identity, _boundsDragStartOverrides, _boundsDragStartPosition);
                EndBoundsDrag();
                SetStatus("Resize cancelled.");
                return true;
            }

            bool symmetric = KeyboardInputManager.IsShiftHeld();
            float snap = KeyboardInputManager.IsAltHeld() ? 0f : BOUNDS_SNAP;
            Vector2 origin = emitter.transform.position;

            // The subject is the emitter's OWN blocks once it has them: a placement and the
            // preset it was born from are free to diverge, and taking the drag's base from the
            // asset would jump the box on the first pixel of any instance that had.
            var subject = ParticleBoundsSubject.Of(emitter);

            var drag = _boundsBox == ParticleBoundsBox.Emission
                ? ParticleBoundsHandles.DragEmissionEdge(
                    subject, identity.Overrides, _boundsDragEdge, origin, worldPos, symmetric, snap)
                : ParticleBoundsHandles.DragReachEdge(
                    subject, identity.Overrides, _boundsDragEdge, origin, worldPos, snap);

            if (drag.Changed)
            {
                Vector3 position = emitter.transform.position + (Vector3)drag.OriginDelta;
                ApplyBounds(emitter, identity, drag.Overrides, position);

                SetStatus(drag.StoppedAtMotionFloor
                    ? DescribeBounds(emitter, drag.Overrides) +
                      "  —  reach floor: any smaller and the particles stop moving. " +
                      "Shrink the emission area, or the instance scale, instead."
                    : DescribeBounds(emitter, drag.Overrides));
            }
            else if (_boundsBox == ParticleBoundsBox.Reach)
            {
                SetStatus("This preset's particles do not travel — there is no reach to scale.");
            }

            if (MouseInputManager.WasLeftMouseButtonReleasedThisFrame())
                CommitBoundsDrag(emitter, identity);

            return true;
        }

        /// <summary>
        /// Pushes one intermediate size onto the live emitter. Deliberately NOT an undo entry:
        /// a drag crosses dozens of sizes and every one of them would be a step the author has
        /// to press Undo through. The whole gesture becomes one entry on release.
        /// </summary>
        private void ApplyBounds(ParticleEmitter emitter, PersistedParticleInstance identity,
                                 ParticleInstanceOverrides overrides, Vector3 position)
        {
            if (emitter == null || identity == null) return;

            identity.SetOverrides(overrides);
            emitter.transform.position = position;
            emitter.SetOverrides(overrides);
        }

        private void CommitBoundsDrag(ParticleEmitter emitter, PersistedParticleInstance identity)
        {
            var before = _boundsDragStartOverrides;
            var after = identity.Overrides;

            // The ratios were the gesture; the configuration is the record. Baking leaves one
            // answer in the file, in world units, instead of a size that is only correct once
            // multiplied by something stored beside it.
            var bakedBefore = emitter.HasOwnConfig ? emitter.Config.Clone() : null;
            Vector3 beforePos = _boundsDragStartPosition;
            Vector3 afterPos = emitter.transform.position;

            EndBoundsDrag();

            bool sizeChanged =
                Mathf.Abs(before.spawnScaleX - after.spawnScaleX) > 1e-4f ||
                Mathf.Abs(before.spawnScaleY - after.spawnScaleY) > 1e-4f ||
                Mathf.Abs(before.reachScale - after.reachScale) > 1e-4f;
            bool moved = Vector3.Distance(beforePos, afterPos) > 1e-4f;
            if (!sizeChanged && !moved) return;

            var target = emitter;
            var targetIdentity = identity;

            if (emitter.HasOwnConfig)
            {
                emitter.BakeOverrides();
                identity.SetConfig(emitter.Config);

                var bakedAfter = emitter.Config.Clone();
                var restoreBefore = bakedBefore;

                ExecutePersistedEdit("Resize particle area",
                    () =>
                    {
                        if (target == null) return;
                        targetIdentity?.SetConfig(bakedAfter.Clone());
                        target.ApplyConfig(target.Preset, bakedAfter.Clone(), target.ScaleMultiplier);
                        target.transform.position = afterPos;
                    },
                    () =>
                    {
                        if (target == null) return;
                        targetIdentity?.SetConfig(restoreBefore?.Clone());
                        target.ApplyConfig(target.Preset, restoreBefore?.Clone(), target.ScaleMultiplier);
                        target.transform.position = beforePos;
                    });

                SetStatus(DescribeBounds(emitter, ParticleInstanceOverrides.None) + " — saved.");
                return;
            }

            ExecutePersistedEdit("Resize particle area",
                () => ApplyBounds(target, targetIdentity, after, afterPos),
                () => ApplyBounds(target, targetIdentity, before, beforePos));

            SetStatus(DescribeBounds(emitter, after) + " — saved.");
        }

        private void EndBoundsDrag()
        {
            _boundsDragging = false;
            _boundsDragEdge = ParticleBoundsEdge.None;
        }

        private void ClearBoundsState()
        {
            _boundsBox = ParticleBoundsBox.None;
            _boundsHoverBox = ParticleBoundsBox.None;
            _boundsHoverEdge = ParticleBoundsEdge.None;
            EndBoundsDrag();
        }

        // ── Boxes ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Where this emitter's particles are born, precalculated from its preset and its own
        /// overrides. No accumulation, no warm-up: it is the same number the moment the
        /// emitter is selected as it is a minute later, because it is what the emitter was
        /// configured with rather than a record of what it has done.
        /// </summary>
        private static ParticleFootprint EmissionBoxOf(ParticleEmitter emitter)
            => ParticleBoundsSubject.Of(emitter).Emission(
                   emitter.HasOwnConfig ? ParticleInstanceOverrides.None : emitter.Overrides);

        /// <summary>How far they can get, from the same source.</summary>
        private static ParticleFootprint ReachBoxOf(ParticleEmitter emitter)
            => ParticleBoundsSubject.Of(emitter).Reach(
                   emitter.HasOwnConfig ? ParticleInstanceOverrides.None : emitter.Overrides);

        /// <summary>
        /// Feeds the selection outline its two boxes and the current highlight. Called from the
        /// outline pass every frame, so the handles follow a preset edit or an undo without
        /// the author having to reselect anything.
        /// </summary>
        private void PushBoundsToOutline(ParticleEmitterOutlineRenderer fx, GameObject instance)
        {
            if (fx == null || instance == null) return;

            var emitter = instance.GetComponentInParent<ParticleEmitter>();
            if (emitter == null || emitter.Preset == null) return;

            fx.SetBoxes(EmissionBoxOf(emitter), ReachBoxOf(emitter));

            // Handles belong to the SELECTED emitter only. The hover outline shows another
            // emitter's boxes so the author can see what they are about to pick up, but
            // painting the selection's hovered edge onto it would put a grab handle on an
            // emitter that cannot be dragged.
            bool isActive = instance == _activeInstance;
            fx.SetHighlight(
                isActive ? (_boundsBox != ParticleBoundsBox.None ? _boundsBox : _boundsHoverBox)
                         : ParticleBoundsBox.None,
                isActive ? (_boundsDragging ? _boundsDragEdge : _boundsHoverEdge)
                         : ParticleBoundsEdge.None);
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        /// <summary>The grab distance in world units at the current zoom.</summary>
        private static float BoundsGrabWorld()
        {
            var cam = Camera.main;
            if (cam == null || Screen.height <= 0) return 0.2f;

            float worldPerPixel = (cam.orthographicSize * 2f) / Screen.height;
            return BOUNDS_GRAB_PIXELS * worldPerPixel;
        }

        /// <summary>Status text for a size: world units first, ratio second — the author is
        /// dragging a box on screen, not typing a multiplier.</summary>
        private static string DescribeBounds(ParticleEmitter emitter, ParticleInstanceOverrides o)
        {
            var subject = ParticleBoundsSubject.Of(emitter);
            var emission = subject.Emission(o);
            var reach = subject.Reach(o);

            return $"Emission {emission.HalfWidth * 2f:0.00} x {emission.HalfHeight * 2f:0.00} u " +
                   $"(x{o.spawnScaleX:0.##}/{o.spawnScaleY:0.##})  ·  " +
                   $"Reach {reach.HalfWidth * 2f:0.00} x {reach.HalfHeight * 2f:0.00} u (x{o.reachScale:0.##})";
        }
    }
}
