using UnityEditor;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.VFX;

namespace Valkur.Editor
{
    /// <summary>
    /// Live preview for a <see cref="ParticlePresetDefinition"/> in the Inspector.
    ///
    /// Click a <c>PP_*.asset</c> in the Project window, change a field, watch it move —
    /// with no Play Mode, no scene, and no editor window. This is the cheapest possible
    /// iteration loop for tuning preset visuals: every other route costs a Play-mode
    /// entry plus the walk back to wherever you were testing.
    ///
    /// It runs the real <see cref="ParticleEmitter.ApplyPreset"/>, so what you see is
    /// what the game builds — no preview-only approximation to drift out of sync. The
    /// system is advanced with <c>ParticleSystem.Simulate</c> rather than by the engine
    /// clock, because nothing ticks outside Play Mode.
    ///
    /// The preview object lives in <see cref="PreviewRenderUtility"/>'s own scene, so it
    /// never touches, dirties, or gets saved into whatever scene is open.
    /// </summary>
    [CustomEditor(typeof(ParticlePresetDefinition))]
    [CanEditMultipleObjects]
    public class ParticlePresetDefinitionEditor : UnityEditor.Editor
    {
        private const float SIMULATION_STEP = 1f / 60f;

        private PreviewRenderUtility _preview;
        private GameObject _emitterGo;
        private ParticleEmitter _emitter;
        private ParticleSystem _ps;
        private string _appliedSignature;
        private bool _renderFailed;

        private ParticlePresetDefinition Preset => target as ParticlePresetDefinition;

        // ── Lifecycle ────────────────────────────────────────────────────────────

        private void OnDisable() => Teardown();
        private void OnDestroy() => Teardown();

        private void Teardown()
        {
            if (_emitterGo != null) DestroyImmediate(_emitterGo);
            _emitterGo = null;
            _emitter = null;
            _ps = null;

            _preview?.Cleanup();
            _preview = null;
            _appliedSignature = null;
            _renderFailed = false;
        }

        // ── Preview ──────────────────────────────────────────────────────────────

        public override bool HasPreviewGUI() => true;

        /// <summary>Particles only move if the inspector keeps asking to be redrawn.</summary>
        public override bool RequiresConstantRepaint()
            => !_renderFailed && ParticlePresetPreviewSupport.IsPreviewable(Preset);

        public override GUIContent GetPreviewTitle()
        {
            var p = Preset;
            return new GUIContent(p == null ? "Preview" : $"Preview — {p.displayName ?? p.id}");
        }

        public override void OnPreviewGUI(Rect r, GUIStyle background)
        {
            if (Event.current.type != EventType.Repaint) return;

            string blocked = ParticlePresetPreviewSupport.UnsupportedReason(Preset);
            if (blocked != null) { DrawMessage(r, blocked); return; }
            if (_renderFailed) { DrawMessage(r, "Preview unavailable in this render pipeline configuration."); return; }

            EnsurePreview();
            if (_preview == null || _ps == null) { DrawMessage(r, "Building preview…"); return; }

            ReapplyIfPresetChanged();
            Advance();

            try
            {
                _preview.BeginPreview(r, background);
                // allowScriptableRenderPipeline: without it URP is bypassed and the
                // additive particle materials render as flat quads.
                _preview.Render(allowScriptableRenderPipeline: true);
                var tex = _preview.EndPreview();
                GUI.DrawTexture(r, tex, ScaleMode.StretchToFill, false);
            }
            catch (System.Exception e)
            {
                // A preview must never take the Inspector down with it.
                Debug.LogWarning($"[ParticlePresetPreview] Disabled after a render error: {e.Message}");
                _renderFailed = true;
                Teardown();
            }
        }

        private static void DrawMessage(Rect r, string message)
        {
            var style = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { wordWrap = true };
            EditorGUI.DrawRect(r, new Color(0.08f, 0.08f, 0.10f, 1f));
            GUI.Label(r, message, style);
        }

        // ── Preview scene ────────────────────────────────────────────────────────

        private void EnsurePreview()
        {
            if (_preview == null)
            {
                _preview = new PreviewRenderUtility();
                var cam = _preview.camera;
                cam.orthographic = true;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.08f, 0.08f, 0.10f, 1f);
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 200f;
                cam.transform.position = new Vector3(0f, 0f, -50f);
                cam.transform.rotation = Quaternion.identity;
            }

            if (_emitterGo == null)
            {
                _emitterGo = new GameObject("PP_InspectorPreview")
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                _emitter = _emitterGo.AddComponent<ParticleEmitter>();
                _preview.AddSingleGO(_emitterGo);
                _appliedSignature = null;
            }
        }

        /// <summary>
        /// Rebuilds the emitter whenever any inspector field changed. Cheap enough to do
        /// on a signature comparison, and it is what makes editing feel immediate.
        /// </summary>
        private void ReapplyIfPresetChanged()
        {
            var preset = Preset;
            if (preset == null) return;

            string signature = BuildSignature(preset);
            if (signature == _appliedSignature && _ps != null) return;

            _emitter.ApplyPreset(preset, 1f);
            _ps = _emitterGo.GetComponentInChildren<ParticleSystem>(true);
            if (_ps != null)
            {
                _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                _ps.Play();
            }

            _preview.camera.orthographicSize = ParticlePresetPreviewSupport.InitialOrthoSize(preset);
            _appliedSignature = signature;
        }

        /// <summary>
        /// Serialized form of the asset — the cheapest reliable "did anything change".
        /// Comparing individual fields would miss whichever one gets added next.
        /// </summary>
        private static string BuildSignature(ParticlePresetDefinition preset)
            => EditorJsonUtility.ToJson(preset);

        private void Advance()
        {
            if (_ps == null) return;

            // Burst presets disable their own child when they finish; wake and replay so
            // the preview loops instead of going black after one shot.
            if (!_ps.main.loop && !_ps.IsAlive(true))
            {
                if (!_ps.gameObject.activeSelf) _ps.gameObject.SetActive(true);
                _ps.Clear(true);
                _ps.Play();
            }

            _ps.Simulate(SIMULATION_STEP, withChildren: true, restart: false, fixedTimeStep: true);
        }
    }
}
