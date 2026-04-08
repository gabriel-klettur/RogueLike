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
        [SerializeField] private float tileRedrawInterval = 0.5f;

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

        // ── Static registry ───────────────────────────────────────────────
        private static readonly List<MinimapDot> _dots = new List<MinimapDot>();

        public static void Register(MinimapDot dot)   { if (!_dots.Contains(dot)) _dots.Add(dot); }
        public static void Unregister(MinimapDot dot) { _dots.Remove(dot); }

        // ── Runtime state ─────────────────────────────────────────────────
        private Texture2D _tex;
        private Color[] _bgPixels;          // pre-filled background row
        private float _lastTileRedraw = -99f;
        private Transform _playerTransform;

        // ── Lifecycle ─────────────────────────────────────────────────────

        private void Awake()
        {
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

            // Clear to background
            _tex.SetPixels(_bgPixels);

            // Draw border
            DrawBorder();

            // Draw entity dots
            Vector2 center = _playerTransform.position;
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
    }

    // ── Companion enum ────────────────────────────────────────────────────
    public enum MinimapDotType { Player, Monster, NPC }
}
