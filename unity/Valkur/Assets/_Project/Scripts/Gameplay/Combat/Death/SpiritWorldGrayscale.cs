using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Core;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.Combat.Death
{
    /// <summary>
    /// While the player is in spirit form, swaps the material on every world
    /// SpriteRenderer / TilemapRenderer to <c>Valkur/SpriteDesaturate</c> so
    /// the screen drains to grayscale. The renderers under the resurrection
    /// altar (BuildingObject with the configured templateId) and under the
    /// spirit's path-marker root keep their original materials, leaving them
    /// the only color in the scene.
    ///
    /// Why a material swap instead of <c>SpriteRenderer.color</c> /
    /// <c>Tilemap.color</c>: those properties are MULTIPLICATIVE — they tint
    /// pixels but cannot desaturate. Multiplying a vivid red sprite by gray
    /// (0.5,0.5,0.5) yields dark red, not gray. The desat shader outputs
    /// pure Rec.601 luminance regardless of source hue, which actually drains
    /// the saturation we want.
    ///
    /// Driven by <c>GameEvents.OnPlayerDied</c> / <c>OnPlayerRevived</c> /
    /// <c>OnPlayerResurrected</c>, so it doesn't poll <c>EntityRegistry.Player</c>
    /// every frame and won't desync if the registry slot is briefly null
    /// (scene transitions, between PurgeDestroyed and RegisterPlayer, etc.).
    /// </summary>
    public class SpiritWorldGrayscale : MonoBehaviour
    {
        [SerializeField, Tooltip("Template id used to identify resurrection altar buildings. " +
                                 "Mirrors ResurrectionZoneAutoBinder.targetTemplateId — change " +
                                 "both at once if the altar template ever moves.")]
        private int altarTemplateId = 249;

        private struct Captured
        {
            public Renderer renderer;
            public Material originalSharedMaterial;
        }

        private readonly List<Captured> _captured = new List<Captured>(512);
        private Material _desatMaterial;
        private bool _grayscaleActive;

        public bool IsGrayscaleActive => _grayscaleActive;

        private bool _initialized;

        private void Awake()
        {
            Initialize();
        }

        /// <summary>
        /// Idempotent setup: ServiceLocator registration + GameEvents subscription.
        /// Public so EditMode tests can drive it explicitly when Unity's
        /// AddComponent doesn't fire Awake deterministically.
        /// </summary>
        public void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            ServiceLocator.Register<SpiritWorldGrayscale>(this);
            GameEvents.OnPlayerDied        += HandlePlayerDied;
            GameEvents.OnPlayerRevived     += HandlePlayerAlive;
            GameEvents.OnPlayerResurrected += HandlePlayerAlive;
        }

        public void Shutdown()
        {
            if (!_initialized) return;
            _initialized = false;
            GameEvents.OnPlayerDied        -= HandlePlayerDied;
            GameEvents.OnPlayerRevived     -= HandlePlayerAlive;
            GameEvents.OnPlayerResurrected -= HandlePlayerAlive;

            if (_grayscaleActive) RestoreMaterials();
            if (ServiceLocator.Get<SpiritWorldGrayscale>() == this)
                ServiceLocator.Unregister<SpiritWorldGrayscale>();
            if (_desatMaterial != null)
            {
                if (Application.isPlaying) Destroy(_desatMaterial);
                else DestroyImmediate(_desatMaterial);
                _desatMaterial = null;
            }
        }

        private void OnDestroy()
        {
            Shutdown();
        }

        // ── Event handlers ──────────────────────────────────────────────────────

        private void HandlePlayerDied()
        {
            if (_grayscaleActive) return;
            _grayscaleActive = true;
            ApplyDesaturation();
        }

        private void HandlePlayerAlive()
        {
            if (!_grayscaleActive) return;
            _grayscaleActive = false;
            RestoreMaterials();
        }

        // ── Public API (used by the test suite + the dev console) ───────────────

        public void ForceApply() => HandlePlayerDied();
        public void ForceRestore() => HandlePlayerAlive();

        public int CapturedRendererCount => _captured.Count;

        // ── Core ────────────────────────────────────────────────────────────────

        private void ApplyDesaturation()
        {
            _captured.Clear();
            if (_desatMaterial == null) _desatMaterial = CreateDesatMaterial();
            if (_desatMaterial == null) return; // shader missing → bail silently

            var exempt = BuildExemptSet();
            CaptureAndSwap<SpriteRenderer>(exempt);
            CaptureAndSwap<TilemapRenderer>(exempt);
        }

        private void CaptureAndSwap<T>(HashSet<Transform> exempt) where T : Renderer
        {
            var renderers = FindObjectsOfType<T>(includeInactive: false);
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null || !r.enabled) continue;
                if (IsExempt(r.transform, exempt)) continue;

                _captured.Add(new Captured
                {
                    renderer = r,
                    originalSharedMaterial = r.sharedMaterial,
                });
                r.sharedMaterial = _desatMaterial;
            }
        }

        private void RestoreMaterials()
        {
            for (int i = 0; i < _captured.Count; i++)
            {
                var entry = _captured[i];
                if (entry.renderer == null) continue;
                entry.renderer.sharedMaterial = entry.originalSharedMaterial;
            }
            _captured.Clear();
        }

        // Build the set of Transform roots whose entire subtree must keep its
        // color: every altar building (template id 249 by default) plus the
        // path-marker pool root. Looking up by Transform lets the per-renderer
        // check stay O(depth) instead of O(N).
        private HashSet<Transform> BuildExemptSet()
        {
            var set = new HashSet<Transform>();

            var buildings = FindObjectsOfType<BuildingObject>(includeInactive: false);
            for (int i = 0; i < buildings.Length; i++)
            {
                var b = buildings[i];
                if (b == null || b.Template == null) continue;
                if (b.Template.templateId == altarTemplateId)
                    set.Add(b.transform);
            }

            var pathHighlighter = ServiceLocator.Get<SpiritAltarPathHighlighter>();
            if (pathHighlighter != null && pathHighlighter.MarkerRoot != null)
                set.Add(pathHighlighter.MarkerRoot);

            return set;
        }

        private static bool IsExempt(Transform t, HashSet<Transform> exempt)
        {
            while (t != null)
            {
                if (exempt.Contains(t)) return true;
                t = t.parent;
            }
            return false;
        }

        private static Material CreateDesatMaterial()
        {
            var shader = Shader.Find("Valkur/SpriteDesaturate");
            if (shader == null)
            {
                Debug.LogWarning("[SpiritWorldGrayscale] Shader 'Valkur/SpriteDesaturate' not found — grayscale will be a no-op. Verify the shader is included in builds (Project Settings → Graphics → Always Included Shaders).");
                return null;
            }
            return new Material(shader)
            {
                name = "SpiritDesatMaterial",
                hideFlags = HideFlags.DontSave,
            };
        }
    }
}
