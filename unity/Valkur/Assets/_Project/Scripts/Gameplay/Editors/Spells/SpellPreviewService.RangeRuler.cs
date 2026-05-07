using TMPro;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.Gameplay.Spells
{
    public sealed partial class SpellPreviewService
    {
        // ── Range ruler ───────────────────────────────────────────────────────────

        /// <summary>
        /// Computes the spell's primary impact distance — the value displayed by
        /// the red ruler under the caster. Picks the largest directional-reach field;
        /// for radial spells (Aura, Puddle, Vortex) falls back to radius.
        /// </summary>
        private static float ResolveSpellImpactDistance(SpellDefinition s)
        {
            if (s == null) return 0f;
            float dist = 0f;
            dist = Mathf.Max(dist, s.range);
            dist = Mathf.Max(dist, s.length);
            dist = Mathf.Max(dist, s.distance);
            dist = Mathf.Max(dist, s.coneLength);
            dist = Mathf.Max(dist, s.hitRadius);
            dist = Mathf.Max(dist, s.radius);
            dist = Mathf.Max(dist, s.meteorAreaRadius);
            dist = Mathf.Max(dist, s.meteorImpactRadius);
            dist = Mathf.Max(dist, s.explosionRadius);
            dist = Mathf.Max(dist, s.triggerRadius);
            return dist;
        }

        /// <summary>
        /// Builds the persistent ruler GO (LineRenderer + TextMeshPro label) under
        /// the stage root. Lives across spell switches; only endpoints + label text
        /// are rewritten each Tick by UpdateRangeRuler.
        /// </summary>
        private void BuildRangeRuler(int previewLayer)
        {
            _rangeRulerGo = new GameObject("SpellRangeRuler");
            _rangeRulerGo.transform.SetParent(_stageRoot.transform, false);
            _rangeRulerGo.layer = previewLayer;

            _rangeRulerLine = _rangeRulerGo.AddComponent<LineRenderer>();
            _rangeRulerLine.useWorldSpace = true;
            _rangeRulerLine.positionCount = 2;
            _rangeRulerLine.startWidth = RULER_LINE_WIDTH;
            _rangeRulerLine.endWidth   = RULER_LINE_WIDTH;
            _rangeRulerLine.startColor = RULER_COLOR;
            _rangeRulerLine.endColor   = RULER_COLOR;
            _rangeRulerLine.numCapVertices = 4;
            _rangeRulerLine.alignment = LineAlignment.View;
            _rangeRulerLine.sortingLayerName = SortingConfig.LAYER_VFX;
            _rangeRulerLine.sortingOrder = 80;

            var sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")
                  ?? Shader.Find("Sprites/Default");
            _rangeRulerMaterial = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            _rangeRulerLine.sharedMaterial = _rangeRulerMaterial;

            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(_rangeRulerGo.transform, false);
            labelGo.layer = previewLayer;
            _rangeRulerLabel = labelGo.AddComponent<TextMeshPro>();
            _rangeRulerLabel.fontSize = 4f;
            _rangeRulerLabel.color = RULER_COLOR;
            _rangeRulerLabel.alignment = TextAlignmentOptions.Center;
            _rangeRulerLabel.enableWordWrapping = false;
            _rangeRulerLabel.fontStyle = FontStyles.Bold;
            var labelMr = _rangeRulerLabel.GetComponent<MeshRenderer>();
            if (labelMr != null)
            {
                labelMr.sortingLayerName = SortingConfig.LAYER_VFX;
                labelMr.sortingOrder = 81;
            }
        }

        /// <summary>
        /// Each Tick: orient the ruler in the current cast direction, set endpoints
        /// relative to the caster, refresh the "n.n tiles" label.
        /// Hides the ruler when no spell is selected or the spell has zero reach.
        /// </summary>
        private void UpdateRangeRuler()
        {
            if (_rangeRulerGo == null || _casterTransform == null) return;

            float dist = ResolveSpellImpactDistance(_spell);
            if (_spell == null || dist <= 0.01f)
            {
                if (_rangeRulerGo.activeSelf) _rangeRulerGo.SetActive(false);
                return;
            }
            if (!_rangeRulerGo.activeSelf) _rangeRulerGo.SetActive(true);

            // Anchor below the caster in world -Y so the ruler sits at the bottom
            // of the preview regardless of cast direction.
            Vector3 anchor = _casterTransform.position + new Vector3(0f, -RULER_Y_OFFSET, 0f);
            Vector3 dir3   = new Vector3(_direction.x, _direction.y, 0f).normalized;
            Vector3 endPt  = anchor + dir3 * dist;

            _rangeRulerLine.SetPosition(0, anchor);
            _rangeRulerLine.SetPosition(1, endPt);

            if (_rangeRulerLabel != null)
            {
                Vector3 mid = (anchor + endPt) * 0.5f;
                _rangeRulerLabel.transform.position = mid + new Vector3(0f, -RULER_LABEL_Y_OFFSET, 0f);
                _rangeRulerLabel.text = $"{dist:F1} tiles";
            }
        }
    }
}
