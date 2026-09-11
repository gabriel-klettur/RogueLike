using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// The minimap's model: which entities and landmarks exist, what the player has explored,
    /// and how far the dial is zoomed. It draws nothing — <see cref="MinimapHUD"/> and
    /// <see cref="WorldMapPanel"/> draw from it through <see cref="MinimapView"/>.
    ///
    /// <para><b>What it used to do, and why that is gone.</b> This class painted a 160x160
    /// <c>Texture2D</c> pixel by pixel twelve times a second: 25,600 <c>SetPixel</c> calls for
    /// the background, another 25,600 hash lookups for the fog, then flat squares for dots.
    /// Measured at 4.26 ms a redraw — a spike every fifth frame — for a map with no terrain on
    /// it at all. The terrain is now a baked atlas the GPU samples, the fog a byte grid
    /// uploaded only when it changes, and the glyphs one mesh; none of it costs a
    /// <c>SetPixel</c>.</para>
    ///
    /// <para>Its public surface is kept: <see cref="MinimapDot"/> and <see cref="MinimapMarker"/>
    /// register here, gameplay reaches both through reflection (<c>EntitySetup</c>), and the
    /// fog and zoom API is what the tests drive.</para>
    /// </summary>
    public partial class MinimapManager : MonoBehaviour
    {
        // Zoom range: too small (<8) makes a single tile fill the dial, too large (>64)
        // flattens the world. Geometric step keeps each wheel detent feeling proportional.
        public  const float MIN_VIEW_RADIUS     = 8f;
        public  const float MAX_VIEW_RADIUS     = 64f;
        public  const float DEFAULT_VIEW_RADIUS = 24f;
        private const float ZOOM_STEP_FACTOR    = 1.18f;
        private const string ZOOM_PREF_KEY      = "valkur.minimap.viewRadius";

        [Header("World")]
        [Tooltip("World-space radius visible on the minimap (zoom). Persisted between sessions via PlayerPrefs.")]
        [SerializeField] private float viewRadius = DEFAULT_VIEW_RADIUS;

        [Header("Fog of War")]
        [Tooltip("Enable exploration-based fog of war. Once a cell has been within reveal radius, it stays explored.")]
        [SerializeField] private bool fogOfWarEnabled = true;

        // ── Static registry ───────────────────────────────────────────────
        private static readonly List<MinimapDot> _dots = new List<MinimapDot>();
        private static readonly List<MinimapMarker> _markers = new List<MinimapMarker>();

        public static void Register(MinimapDot dot)   { if (dot != null && !_dots.Contains(dot)) _dots.Add(dot); }
        public static void Unregister(MinimapDot dot) { _dots.Remove(dot); }
        public static void RegisterMarker(MinimapMarker m)   { if (m != null && !_markers.Contains(m)) _markers.Add(m); }
        public static void UnregisterMarker(MinimapMarker m) { _markers.Remove(m); }

        /// <summary>Every registered entity dot. Read-only view.</summary>
        public static IReadOnlyList<MinimapDot> Dots => _dots;

        /// <summary>Every registered landmark marker. Read-only view.</summary>
        public static IReadOnlyList<MinimapMarker> Markers => _markers;

        // ── Static instance ───────────────────────────────────────────────
        public static MinimapManager Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsOnPlayModeEnter()
        {
            Instance = null;
            _dots?.Clear();
            _markers?.Clear();
        }

        private MinimapFogMap _fog;
        private bool _prefsRead;

        /// <summary>The explored mask. Created on first use, so EditMode tests need no Awake.</summary>
        public MinimapFogMap Fog => _fog ??= new MinimapFogMap();

        /// <summary>True when fog of war is on.</summary>
        public bool FogOfWarEnabled => fogOfWarEnabled;

        private void Awake()
        {
            Instance = this;
            ReadZoomPreference();
        }

        private void OnDestroy()
        {
            SaveFogNow();
            _fog?.Dispose();
            if (Instance == this) Instance = null;
        }

        private void OnApplicationQuit() => SaveFogNow();

        private void ReadZoomPreference()
        {
            if (_prefsRead) return;
            _prefsRead = true;
            if (PlayerPrefs.HasKey(ZOOM_PREF_KEY))
                viewRadius = Mathf.Clamp(PlayerPrefs.GetFloat(ZOOM_PREF_KEY, DEFAULT_VIEW_RADIUS), MIN_VIEW_RADIUS, MAX_VIEW_RADIUS);
        }

        // ── Zoom ──────────────────────────────────────────────────────────

        /// <summary>Current visible world radius (the zoom TARGET; the dial eases toward it).</summary>
        public float ViewRadius => viewRadius;

        /// <summary>Apply a new zoom level, clamped and persisted.</summary>
        public void SetViewRadius(float radius)
        {
            float clamped = Mathf.Clamp(radius, MIN_VIEW_RADIUS, MAX_VIEW_RADIUS);
            if (Mathf.Approximately(clamped, viewRadius)) return;
            viewRadius = clamped;
            PlayerPrefs.SetFloat(ZOOM_PREF_KEY, viewRadius);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Adjust zoom by detents. Positive zooms OUT (see more world). Geometric, so each
        /// click weighs the same at any zoom.
        /// </summary>
        public void AdjustZoom(int detents)
        {
            if (detents == 0) return;
            SetViewRadius(viewRadius * Mathf.Pow(ZOOM_STEP_FACTOR, detents));
        }

        // ── Fog facade (kept for callers and tests) ───────────────────────

        /// <summary>Mark everything within <paramref name="radius"/> of a world position explored.</summary>
        public void RevealAround(Vector2 worldCenter, float radius) => Fog.Reveal(worldCenter, radius);

        /// <summary>Forget the live world's explored cells.</summary>
        public void ClearFog() => Fog.ClearActive();

        /// <summary>True when a world position is explored (always, with fog of war off).</summary>
        public bool IsExplored(Vector2 worldPos) => !fogOfWarEnabled || Fog.IsExplored(worldPos);

        // ── Colours ───────────────────────────────────────────────────────

        /// <summary>Default dot colour for a dot type, from the shipped style.</summary>
        public Color GetDefaultColor(MinimapDotType type)
        {
            var s = MinimapStyle.Active;
            switch (type)
            {
                case MinimapDotType.Player:  return s.playerColor;
                case MinimapDotType.Monster: return s.enemyColor;
                case MinimapDotType.Ally:    return s.allyColor;
                default:                     return s.neutralColor;
            }
        }
    }

    /// <summary>
    /// APPEND ONLY, never renumber: the value is serialized on every <see cref="MinimapDot"/>
    /// and is also reached BY NAME through reflection from Valkur.Gameplay
    /// (<c>EntitySetup.ConfigureMinimapDot</c>), which the compiler cannot check —
    /// <c>MinimapDotNameContractTests</c> asks that question instead.
    /// </summary>
    public enum MinimapDotType { Player, Monster, NPC, Ally }
}
