using System.Reflection;
using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Drives a 24-hour day/night cycle by animating the scene's URP 2D Global Light 2D.
    /// Mirrors Python's ambient lighting approach (no day/night in Python, this is a Unity enhancement).
    ///
    /// Uses reflection to write Light2D.color and Light2D.intensity so that
    /// Valkur.Gameplay.asmdef does NOT need a direct reference to the URP runtime assembly
    /// (consistent with GameplaySceneSetup.EnsureGlobalLight2D approach).
    ///
    /// Phases (normalized day time 0..1):
    ///   Dawn:  0.20 - 0.30  (warm pinkish light, intensity rising)
    ///   Day:   0.30 - 0.70  (full white light, max intensity)
    ///   Dusk:  0.70 - 0.80  (orange tint, intensity falling)
    ///   Night: 0.80 - 0.20  (deep blue, low intensity)
    ///
    /// Fires OnPhaseChanged event for AudioManager ambient updates.
    /// </summary>
    public class DayNightCycle : SingletonMonoBehaviour<DayNightCycle>
    {
        [Header("Timing")]
        [Tooltip("Real-world seconds per full in-game day (Python: no cycle — set to 0 to pause).")]
        [SerializeField] private float realSecondsPerDay = 300f;

        [Tooltip("Starting time of day (0=midnight, 0.25=dawn, 0.5=noon, 0.75=dusk).")]
        [SerializeField, Range(0f, 1f)] private float startTimeNormalized = 0.35f;

        [Tooltip("Pause the cycle (useful in dungeons, etc.).")]
        [SerializeField] private bool paused;

        [Header("Day Phase Colors & Intensities")]
        [SerializeField] private Color dayColor    = new Color(1.00f, 0.98f, 0.95f, 1f);
        [SerializeField] private Color dawnColor   = new Color(1.00f, 0.72f, 0.55f, 1f);
        [SerializeField] private Color duskColor   = new Color(1.00f, 0.60f, 0.40f, 1f);
        [SerializeField] private Color nightColor  = new Color(0.12f, 0.14f, 0.30f, 1f);

        [SerializeField] private float dayIntensity   = 1.0f;
        [SerializeField] private float dawnIntensity  = 0.6f;
        [SerializeField] private float duskIntensity  = 0.55f;
        [SerializeField] private float nightIntensity = 0.15f;

        // ── Public state ──────────────────────────────────────────────

        public enum DayPhase { Day, Dawn, Dusk, Night }

        /// <summary>Current normalized day time [0, 1).</summary>
        public float TimeNormalized { get; private set; }

        /// <summary>Current phase.</summary>
        public DayPhase CurrentPhase { get; private set; } = DayPhase.Day;

        /// <summary>Approximate in-game hour 0-23.</summary>
        public int HourOfDay => Mathf.FloorToInt(TimeNormalized * 24f);

        // ── Events ────────────────────────────────────────────────────
        public static System.Action<DayPhase> OnPhaseChanged;

        protected override bool Persist => false;

        // ── Reflection cache ──────────────────────────────────────────

        private Component    _globalLight;
        private PropertyInfo _colorProp;
        private PropertyInfo _intensityProp;
        private bool         _lightResolved;

        // ── Lifecycle ─────────────────────────────────────────────────

        protected override void OnSingletonAwake()
        {
            TimeNormalized = startTimeNormalized;
            // NOTE: Do NOT call ResolveGlobalLight() here — the Global Light 2D may not
            // exist yet at Awake time (GameplaySceneSetup creates it in Start).
            // Resolved lazily on first Update frame instead.
        }

        private void Update()
        {
            // Lazy-resolve the Global Light 2D — by first Update() all Start() calls have run.
            if (!_lightResolved)
            {
                ResolveGlobalLight();
                _lightResolved = true;
            }

            if (paused || realSecondsPerDay <= 0f) return;

            TimeNormalized = (TimeNormalized + Time.deltaTime / realSecondsPerDay) % 1f;
            UpdateLighting();
        }

        // ── Public API ────────────────────────────────────────────────

        /// <summary>Set the time of day directly. 0=midnight, 0.5=noon.</summary>
        public void SetTimeNormalized(float t)
        {
            TimeNormalized = t % 1f;
            UpdateLighting();
        }

        // ── Internal ──────────────────────────────────────────────────

        private void ResolveGlobalLight()
        {
            var light2DType = System.Type.GetType(
                "UnityEngine.Rendering.Universal.Light2D, Unity.RenderPipelines.Universal.Runtime");
            if (light2DType == null)
            {
                Debug.LogWarning("[DayNightCycle] Light2D type not found — URP 2D Renderer missing.");
                return;
            }

            // Find all Light2D components; pick the first with lightType == Global (1)
            var all = FindObjectsOfType(light2DType);
            var ltProp = light2DType.GetProperty("lightType",
                BindingFlags.Public | BindingFlags.Instance);

            foreach (Component c in all)
            {
                if (ltProp == null) { _globalLight = c; break; }
                try
                {
                    int val = System.Convert.ToInt32(ltProp.GetValue(c));
                    if (val == 1) { _globalLight = c; break; } // 1 = Global
                }
                catch { }
            }

            if (_globalLight == null && all.Length > 0)
                _globalLight = all[0] as Component;

            if (_globalLight == null)
            {
                Debug.LogWarning("[DayNightCycle] No Light2D found in scene.");
                return;
            }

            _colorProp     = light2DType.GetProperty("color",     BindingFlags.Public | BindingFlags.Instance);
            _intensityProp = light2DType.GetProperty("intensity", BindingFlags.Public | BindingFlags.Instance);
        }

        private void UpdateLighting()
        {
            if (_globalLight == null) return;

            float t = TimeNormalized;
            Color targetColor;
            float targetIntensity;
            DayPhase newPhase;

            if (t >= 0.20f && t < 0.30f)
            {
                float blend     = (t - 0.20f) / 0.10f;
                targetColor     = Color.Lerp(dawnColor, dayColor, blend);
                targetIntensity = Mathf.Lerp(dawnIntensity, dayIntensity, blend);
                newPhase = blend < 0.5f ? DayPhase.Dawn : DayPhase.Day;
            }
            else if (t >= 0.30f && t < 0.70f)
            {
                targetColor     = dayColor;
                targetIntensity = dayIntensity;
                newPhase        = DayPhase.Day;
            }
            else if (t >= 0.70f && t < 0.80f)
            {
                float blend     = (t - 0.70f) / 0.10f;
                targetColor     = Color.Lerp(duskColor, nightColor, blend);
                targetIntensity = Mathf.Lerp(duskIntensity, nightIntensity, blend);
                newPhase = blend < 0.5f ? DayPhase.Dusk : DayPhase.Night;
            }
            else
            {
                targetColor     = nightColor;
                targetIntensity = nightIntensity;
                newPhase        = DayPhase.Night;
            }

            try
            {
                _colorProp?.SetValue(_globalLight, targetColor);
                _intensityProp?.SetValue(_globalLight, targetIntensity);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[DayNightCycle] Failed to set Light2D properties: {ex.Message}");
            }

            if (newPhase != CurrentPhase)
            {
                CurrentPhase = newPhase;
                OnPhaseChanged?.Invoke(newPhase);
            }
        }
    }
}

