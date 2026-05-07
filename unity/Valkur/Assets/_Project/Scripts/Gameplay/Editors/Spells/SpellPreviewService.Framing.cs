using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Spells
{
    public sealed partial class SpellPreviewService
    {
        // ── Camera framing ────────────────────────────────────────────────────────

        private void UpdateCameraFraming()
        {
            if (_camera == null || _casterTransform == null) return;

            if (!_lockedBoundsInitialized)
            {
                _lockedBounds = new Bounds(_casterTransform.position,
                                           Vector3.one * (ORTHO_SIZE_DEFAULT * 2f));
                _lockedBoundsInitialized = true;
            }

            // Encapsulate any runtime renderer that overshoots the seeded box.
            // Bounds only grow, never shrink, so the camera does not pulse with
            // the spell's expand/contract animation.
            EncapsulateRendererSubtree(_stageRoot != null ? _stageRoot.transform : null);
            for (int i = 0; i < _trackedWorldSpawns.Count; i++)
                EncapsulateRendererSubtree(_trackedWorldSpawns[i] != null ? _trackedWorldSpawns[i].transform : null);
            foreach (var go in _absorbedWorldVfx)
                EncapsulateRendererSubtree(go != null ? go.transform : null);

            if (float.IsNaN(_lockedBounds.center.x) || float.IsNaN(_lockedBounds.center.y))
                _lockedBounds = new Bounds(_casterTransform.position, Vector3.one * 4f);

            // Anchor framing on the caster so the player is the stable reference and
            // pick the larger side-distance so the spell extent on the far side is
            // always inside the view.
            var casterPos = _casterTransform.position;
            float halfX = Mathf.Max(
                Mathf.Abs(_lockedBounds.max.x - casterPos.x),
                Mathf.Abs(casterPos.x - _lockedBounds.min.x));
            float halfY = Mathf.Max(
                Mathf.Abs(_lockedBounds.max.y - casterPos.y),
                Mathf.Abs(casterPos.y - _lockedBounds.min.y));
            float orthoFitBoth = Mathf.Max(halfX, halfY) + BOUNDS_PADDING;
            orthoFitBoth = Mathf.Max(orthoFitBoth, ORTHO_SIZE_MIN);

            float zoomed = orthoFitBoth / Mathf.Max(_userZoom, 0.0001f);
            _camera.orthographicSize = Mathf.Max(ORTHO_SIZE_MIN * 0.25f, zoomed);
            _camera.transform.position = new Vector3(casterPos.x, casterPos.y, CAMERA_Z);
        }

        /// <summary>
        /// Walks root's renderer subtree and grows _lockedBounds to encapsulate each
        /// renderer whose centre is within BOUNDS_SANITY_RADIUS of the caster (so a
        /// pooled VFX reused at a different world position can't yank the framing).
        /// </summary>
        private void EncapsulateRendererSubtree(Transform root)
        {
            if (root == null) return;
            var casterPos = _casterTransform.position;
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                if (r == null) continue;
                var b = r.bounds;
                if (b.size.sqrMagnitude < 0.0001f) continue;
                if (Vector3.Distance(b.center, casterPos) > BOUNDS_SANITY_RADIUS) continue;
                _lockedBounds.Encapsulate(b);
            }
        }

        /// <summary>
        /// Pre-computes a stable framing box around the caster sized for the spell's
        /// nominal reach. Called from SetSelectedSpell so the camera ortho is fixed
        /// BEFORE the first cycle fires — no zoom-in/zoom-out wobble.
        /// The box is symmetric (covers reach in every direction) so the framing
        /// doesn't change when the user toggles N/S/E/W direction.
        /// </summary>
        private void SeedLockedBoundsForSpell(SpellDefinition s)
        {
            _lockedBoundsInitialized = false;
            if (_casterTransform == null) return;

            float reach = ComputeSpellReach(s);
            float side = Mathf.Max(ORTHO_SIZE_MIN * 2f, reach * 2f);
            _lockedBounds = new Bounds(_casterTransform.position,
                                       new Vector3(side, side, 0f));
            _lockedBoundsInitialized = true;
        }

        private static float ComputeSpellReach(SpellDefinition s)
        {
            if (s == null) return ORTHO_SIZE_DEFAULT;

            float reach = 0f;
            reach = Mathf.Max(reach, s.range);
            reach = Mathf.Max(reach, s.radius);
            reach = Mathf.Max(reach, s.hitRadius);
            reach = Mathf.Max(reach, s.length);
            reach = Mathf.Max(reach, s.distance);
            reach = Mathf.Max(reach, s.coneLength);
            reach = Mathf.Max(reach, s.explosionRadius);
            reach = Mathf.Max(reach, s.meteorAreaRadius);
            reach = Mathf.Max(reach, s.meteorImpactRadius);
            reach = Mathf.Max(reach, s.triggerRadius);
            reach = Mathf.Max(reach, s.wallWidth  * 0.5f);
            reach = Mathf.Max(reach, s.wallHeight * 0.5f);

            if (reach <= 0f) reach = ORTHO_SIZE_DEFAULT;

            // Add a forward offset for slashes/projectiles that spawn 1+ tile in front,
            // plus padding for trail/impact bloom not represented in the SpellDefinition.
            return reach + 2f;
        }
    }
}
