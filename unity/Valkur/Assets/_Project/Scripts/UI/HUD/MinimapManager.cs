using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// Minimap system: renders entity positions as coloured pixels on a Texture2D displayed in a RawImage.
    /// Mirrors Python MinimapController + MinimapView:
    ///   - Background tile layer (static, rate-limited)
    ///   - Entity layer (player=green dot, monsters=red dots) updated every frame
    ///   - Rate-limited tile redraw tied to player tile movement threshold
    ///
    /// Usage:
    ///   - Add MinimapManager to a Canvas child; wire rawImage and configure size.
    ///   - Entities register themselves via MinimapDot.
    /// </summary>
    public partial class MinimapManager : MonoBehaviour
    {
        [Header("Display")]
        [Tooltip("The RawImage UI element to which the minimap texture is assigned.")]
        [SerializeField] private UnityEngine.UI.RawImage rawImage;

        [Tooltip("Minimap pixel dimensions (Python: 200x200 approx).")]
        [SerializeField] private int texWidth  = 160;
        [SerializeField] private int texHeight = 160;

        [Header("World")]
        [Tooltip("World-space radius visible on the minimap (zoom control). Persisted between sessions via PlayerPrefs.")]
        [SerializeField] private float viewRadius = DEFAULT_VIEW_RADIUS;

        // Zoom range: too small (<6) makes a single tile fill the dial, too
        // large (>72) flattens the world. Geometric step keeps each wheel
        // detent feeling proportional regardless of current zoom.
        public  const float MIN_VIEW_RADIUS     = 8f;
        public  const float MAX_VIEW_RADIUS     = 64f;
        public  const float DEFAULT_VIEW_RADIUS = 24f;
        private const float ZOOM_STEP_FACTOR    = 1.18f;
        private const string ZOOM_PREF_KEY      = "valkur.minimap.viewRadius";

        [Header("Rate Limits")]
        [Tooltip("How often (seconds) the background tile layer redraws.")]
#pragma warning disable CS0414
        [SerializeField] private float tileRedrawInterval = 0.5f;
#pragma warning restore CS0414

        [Header("Dot sizes (pixels)")]
        [SerializeField] private int playerDotSize  = 3;
        [SerializeField] private int monsterDotSize = 2;
        [SerializeField] private int npcDotSize     = 2;

        [Header("Colors")]
        [SerializeField] private Color bgColor      = new Color(0.06f, 0.06f, 0.10f, 0.85f);
        [SerializeField] private Color playerColor  = new Color(0.2f,  0.95f, 0.3f,  1f);
        [SerializeField] private Color monsterColor = new Color(0.9f,  0.2f,  0.2f,  1f);
        [SerializeField] private Color npcColor     = new Color(0.9f,  0.85f, 0.3f,  1f);
        [SerializeField] private Color borderColor  = new Color(0.3f,  0.3f,  0.35f, 1f);
        [SerializeField] private Color fogColor     = new Color(0.03f, 0.03f, 0.05f, 1f);

        [Header("Fog of War")]
        [Tooltip("Enable exploration-based fog of war. Once a cell has been within reveal radius, it stays unfogged.")]
        [SerializeField] private bool fogOfWarEnabled = true;
        [Tooltip("World-space radius around the player that counts as explored each frame.")]
        [SerializeField] private float revealRadius = 14f;
        [Tooltip("World units covered by one fog cell; smaller = finer detail, more memory.")]
        [SerializeField] private float fogCellSize = 1f;

        // ── Static registry ───────────────────────────────────────────────
        private static readonly List<MinimapDot> _dots = new List<MinimapDot>();
        private static readonly List<MinimapMarker> _markers = new List<MinimapMarker>();

        public static void Register(MinimapDot dot)   { if (!_dots.Contains(dot)) _dots.Add(dot); }
        public static void Unregister(MinimapDot dot) { _dots.Remove(dot); }
        public static void RegisterMarker(MinimapMarker m)   { if (m != null && !_markers.Contains(m)) _markers.Add(m); }
        public static void UnregisterMarker(MinimapMarker m) { _markers.Remove(m); }

        /// <summary>
        /// Read-only view of every registered marker. Lets MinimapHUD project
        /// markers to disc-local UI coordinates without exposing the static
        /// list for mutation.
        /// </summary>
        public static IReadOnlyList<MinimapMarker> Markers => _markers;

        /// <summary>
        /// Wire the host RawImage at runtime. Lets MinimapHUD build the chrome
        /// programmatically and then plug the freshly-created RawImage into the
        /// already-Awake() manager — no scene-side serialization required.
        /// </summary>
        public void BindRawImage(UnityEngine.UI.RawImage img)
        {
            rawImage = img;
            if (img != null && _tex != null) img.texture = _tex;
        }

        /// <summary>Current visible world radius (zoom level). See <see cref="SetViewRadius"/>.</summary>
        public float ViewRadius => viewRadius;

        /// <summary>
        /// Apply a new zoom level. Clamped to [MIN_VIEW_RADIUS, MAX_VIEW_RADIUS]
        /// and persisted to PlayerPrefs so the player keeps their preferred zoom
        /// across sessions (same convention as MusicPlayerHUD's size persistence).
        /// </summary>
        public void SetViewRadius(float radius)
        {
            float clamped = Mathf.Clamp(radius, MIN_VIEW_RADIUS, MAX_VIEW_RADIUS);
            if (Mathf.Approximately(clamped, viewRadius)) return;

            viewRadius = clamped;
            PlayerPrefs.SetFloat(ZOOM_PREF_KEY, viewRadius);
            PlayerPrefs.Save();

            // Clear fog so the back-projected fog grid (which depends on
            // viewRadius via worldPerPx) snaps to the new pixel sampling cleanly
            // on the next redraw — without this, stale fog cells can briefly
            // show through at the new zoom.
            _exploredCells.Clear();
        }

        /// <summary>
        /// Adjust zoom by integer detents. Positive detents zoom *out* (larger
        /// view radius — see more world); negative detents zoom *in*. Step is
        /// geometric so each click feels equally weighted at any zoom level.
        /// </summary>
        public void AdjustZoom(int detents)
        {
            if (detents == 0) return;
            float factor = Mathf.Pow(ZOOM_STEP_FACTOR, detents);
            SetViewRadius(viewRadius * factor);
        }

        // ── Static instance for MinimapDot color lookups ──────────────────
        public static MinimapManager Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsOnPlayModeEnter()
        {
            Instance = null;
            _dots?.Clear();
            _markers?.Clear();
        }

        // ── Runtime state ─────────────────────────────────────────────────
        private Texture2D _tex;
        private Color[] _bgPixels;          // pre-filled background row
#pragma warning disable CS0414
        private float _lastTileRedraw = -99f;
#pragma warning restore CS0414
        private Transform _playerTransform;

        // Fog-of-war: coarse 2D grid of explored cells in world space.
        // Key = (cellX, cellY); value true once revealed.
        private readonly System.Collections.Generic.HashSet<long> _exploredCells
            = new System.Collections.Generic.HashSet<long>();

        // Throttle the (very expensive) full-texture redraw + GPU upload.
        // Texture2D.Apply() with 25k pixels every frame was the dominant CPU+GC cost
        // (~400 KB/s alloc, ~30 ms/frame stall on integrated GPUs).
        // 12 Hz is plenty smooth for a minimap and cuts the cost ~5x.
        private const float REDRAW_INTERVAL = 1f / 12f;
        private float _nextRedrawTime;

        // ── Lifecycle ─────────────────────────────────────────────────────

        private void Awake()
        {
            Instance = this;

            // Restore the player's last zoom preference. Out-of-range values
            // from older or corrupted prefs are clamped silently.
            if (PlayerPrefs.HasKey(ZOOM_PREF_KEY))
            {
                float saved = PlayerPrefs.GetFloat(ZOOM_PREF_KEY, DEFAULT_VIEW_RADIUS);
                viewRadius = Mathf.Clamp(saved, MIN_VIEW_RADIUS, MAX_VIEW_RADIUS);
            }

            _tex = new Texture2D(texWidth, texHeight, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode   = TextureWrapMode.Clamp,
            };

            // Pre-compute solid background pixel array
            _bgPixels = new Color[texWidth * texHeight];
            for (int i = 0; i < _bgPixels.Length; i++)
                _bgPixels[i] = bgColor;

            if (rawImage != null)
                rawImage.texture = _tex;
        }

        private void OnDestroy()
        {
            if (_tex != null)
                Destroy(_tex);
        }

        private void LateUpdate()
        {
            // Throttle the entire redraw + GPU upload pipeline.
            if (Time.unscaledTime < _nextRedrawTime) return;
            _nextRedrawTime = Time.unscaledTime + REDRAW_INTERVAL;

            // Resolve player transform lazily
            if (_playerTransform == null)
            {
                var player = EntityRegistry.Player;
                if (player != null) _playerTransform = player.transform;
            }

            if (_playerTransform == null) return;

            Vector2 center = _playerTransform.position;

            // Reveal cells around the player before rendering.
            if (fogOfWarEnabled) RevealAround(center, revealRadius);

            // Clear to background
            _tex.SetPixels(_bgPixels);

            // Fog-of-war pass: darken pixels that map to unexplored cells.
            if (fogOfWarEnabled) PaintFog(center);

            // Draw border
            DrawBorder();

            // Draw world markers (portals, vendors, quests).
            foreach (var m in _markers)
            {
                if (m == null || !m.isActiveAndEnabled) continue;
                if (!TryProject(m.WorldPosition, center, out int mx, out int my)) continue;
                DrawMarker(mx, my, m.pixelSize, m.shape, m.EffectiveColor);
            }

            // Draw entity dots (drawn after markers so player/enemies are on top)
            foreach (var dot in _dots)
            {
                if (dot == null || !dot.enabled) continue;
                Vector2 wPos = dot.transform.position;
                Vector2 rel  = wPos - center;
                int px = Mathf.RoundToInt((rel.x / viewRadius) * (texWidth  * 0.5f) + texWidth  * 0.5f);
                int py = Mathf.RoundToInt((rel.y / viewRadius) * (texHeight * 0.5f) + texHeight * 0.5f);

                // Clip to texture bounds
                int half = GetDotHalf(dot);
                px = Mathf.Clamp(px, half, texWidth  - half - 1);
                py = Mathf.Clamp(py, half, texHeight - half - 1);

                DrawDot(px, py, half, dot.DotColor);
            }

            _tex.Apply(false);
        }

    }
}