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
    public class MinimapManager : MonoBehaviour
    {
        [Header("Display")]
        [Tooltip("The RawImage UI element to which the minimap texture is assigned.")]
        [SerializeField] private UnityEngine.UI.RawImage rawImage;

        [Tooltip("Minimap pixel dimensions (Python: 200x200 approx).")]
        [SerializeField] private int texWidth  = 160;
        [SerializeField] private int texHeight = 160;

        [Header("World")]
        [Tooltip("World-space radius visible on the minimap (zoom control).")]
        [SerializeField] private float viewRadius = 24f;

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

        // ── Static instance for MinimapDot color lookups ──────────────────
        public static MinimapManager Instance { get; private set; }

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

        // ── Lifecycle ─────────────────────────────────────────────────────

        private void Awake()
        {
            Instance = this;

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

        // ── Drawing helpers ───────────────────────────────────────────────

        private void DrawDot(int cx, int cy, int half, Color col)
        {
            for (int dx = -half; dx <= half; dx++)
            for (int dy = -half; dy <= half; dy++)
            {
                int x = cx + dx;
                int y = cy + dy;
                if (x >= 0 && x < texWidth && y >= 0 && y < texHeight)
                    _tex.SetPixel(x, y, col);
            }
        }

        private void DrawBorder()
        {
            for (int x = 0; x < texWidth;  x++) { _tex.SetPixel(x, 0, borderColor); _tex.SetPixel(x, texHeight - 1, borderColor); }
            for (int y = 0; y < texHeight; y++) { _tex.SetPixel(0, y, borderColor); _tex.SetPixel(texWidth - 1, y, borderColor); }
        }

        private int GetDotHalf(MinimapDot dot)
        {
            switch (dot.DotType)
            {
                case MinimapDotType.Player:  return playerDotSize  / 2;
                case MinimapDotType.Monster: return monsterDotSize / 2;
                default:                     return npcDotSize     / 2;
            }
        }

        // ── Color helpers ─────────────────────────────────────────────────

        public Color GetDefaultColor(MinimapDotType type)
        {
            switch (type)
            {
                case MinimapDotType.Player:  return playerColor;
                case MinimapDotType.Monster: return monsterColor;
                default:                     return npcColor;
            }
        }

        // ── Fog-of-war helpers ────────────────────────────────────────────

        /// <summary>Marks all fog cells within radius of the given world-space position as explored.</summary>
        public void RevealAround(Vector2 worldCenter, float radius)
        {
            if (fogCellSize <= 0f) return;
            int r = Mathf.CeilToInt(radius / fogCellSize);
            int cx = Mathf.FloorToInt(worldCenter.x / fogCellSize);
            int cy = Mathf.FloorToInt(worldCenter.y / fogCellSize);
            float rSqr = radius * radius;
            for (int dy = -r; dy <= r; dy++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    float wx = (cx + dx) * fogCellSize + fogCellSize * 0.5f;
                    float wy = (cy + dy) * fogCellSize + fogCellSize * 0.5f;
                    float dX = wx - worldCenter.x;
                    float dY = wy - worldCenter.y;
                    if (dX * dX + dY * dY <= rSqr)
                        _exploredCells.Add(CellKey(cx + dx, cy + dy));
                }
            }
        }

        /// <summary>Forgets all explored fog cells (e.g. on new game / map change).</summary>
        public void ClearFog() => _exploredCells.Clear();

        /// <summary>Returns true if the given world-space position is currently considered explored.</summary>
        public bool IsExplored(Vector2 worldPos)
        {
            if (!fogOfWarEnabled) return true;
            int cx = Mathf.FloorToInt(worldPos.x / fogCellSize);
            int cy = Mathf.FloorToInt(worldPos.y / fogCellSize);
            return _exploredCells.Contains(CellKey(cx, cy));
        }

        private static long CellKey(int x, int y)
            => ((long)(uint)x << 32) | (uint)y;

        private void PaintFog(Vector2 center)
        {
            // For each minimap pixel, back-project to world and check exploration.
            float worldPerPxX = (viewRadius * 2f) / texWidth;
            float worldPerPxY = (viewRadius * 2f) / texHeight;
            for (int py = 0; py < texHeight; py++)
            {
                float wy = center.y + (py - texHeight * 0.5f) * worldPerPxY;
                int cy = Mathf.FloorToInt(wy / fogCellSize);
                for (int px = 0; px < texWidth; px++)
                {
                    float wx = center.x + (px - texWidth * 0.5f) * worldPerPxX;
                    int cx = Mathf.FloorToInt(wx / fogCellSize);
                    if (!_exploredCells.Contains(CellKey(cx, cy)))
                        _tex.SetPixel(px, py, fogColor);
                }
            }
        }

        // ── Projection & marker drawing ──────────────────────────────────

        private bool TryProject(Vector2 worldPos, Vector2 center, out int px, out int py)
        {
            Vector2 rel = worldPos - center;
            px = Mathf.RoundToInt((rel.x / viewRadius) * (texWidth  * 0.5f) + texWidth  * 0.5f);
            py = Mathf.RoundToInt((rel.y / viewRadius) * (texHeight * 0.5f) + texHeight * 0.5f);
            return px >= 0 && px < texWidth && py >= 0 && py < texHeight;
        }

        private void DrawMarker(int cx, int cy, int pixelSize, MinimapMarker.MarkerShape shape, Color color)
        {
            int half = Mathf.Max(1, pixelSize / 2);
            switch (shape)
            {
                case MinimapMarker.MarkerShape.Square:
                    DrawDot(cx, cy, half, color);
                    break;
                case MinimapMarker.MarkerShape.Diamond:
                    for (int dy = -half; dy <= half; dy++)
                    {
                        int span = half - Mathf.Abs(dy);
                        for (int dx = -span; dx <= span; dx++)
                            SetPixelSafe(cx + dx, cy + dy, color);
                    }
                    break;
                case MinimapMarker.MarkerShape.Plus:
                    for (int d = -half; d <= half; d++)
                    {
                        SetPixelSafe(cx + d, cy, color);
                        SetPixelSafe(cx, cy + d, color);
                    }
                    break;
            }
        }

        private void SetPixelSafe(int x, int y, Color c)
        {
            if (x >= 0 && x < texWidth && y >= 0 && y < texHeight)
                _tex.SetPixel(x, y, c);
        }
    }

    // ── Companion enum ────────────────────────────────────────────────────
    public enum MinimapDotType { Player, Monster, NPC }
}
