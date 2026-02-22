using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// Visual indicator showing the player's facing direction (toward mouse cursor).
    /// Renders a small arrow/chevron sprite that orbits the player at a fixed distance,
    /// always pointing toward the mouse position.
    /// </summary>
    public class FacingIndicator : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private float orbitRadius = 0.7f;
        [SerializeField] private float arrowScale = 0.3f;
        [SerializeField] private Color arrowColor = new Color(1f, 1f, 1f, 0.6f);

        private GameObject _arrowGo;
        private SpriteRenderer _arrowSr;
        private PlayerController _player;

        private void Start()
        {
            _player = GetComponent<PlayerController>();
            CreateArrowVisual();
        }

        private void LateUpdate()
        {
            if (_player == null || _arrowGo == null) return;

            Vector2 dir = _player.FacingDirection;
            if (dir.sqrMagnitude < 0.001f) return;

            // Position: orbit around player
            _arrowGo.transform.position = transform.position + (Vector3)(dir.normalized * orbitRadius);

            // Rotation: point in facing direction
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            _arrowGo.transform.rotation = Quaternion.Euler(0, 0, angle - 90f);

            // Pulse alpha slightly for polish
            float pulse = 0.5f + 0.15f * Mathf.Sin(Time.time * 3f);
            _arrowSr.color = new Color(arrowColor.r, arrowColor.g, arrowColor.b, pulse);
        }

        private void CreateArrowVisual()
        {
            _arrowGo = new GameObject("FacingArrow");
            _arrowGo.transform.SetParent(null); // world space, not child (avoids flip issues)

            _arrowSr = _arrowGo.AddComponent<SpriteRenderer>();
            _arrowSr.sprite = CreateArrowSprite();
            _arrowSr.color = arrowColor;
            _arrowSr.sortingLayerName = SortingConfig.LAYER_ENTITIES;
            _arrowSr.sortingOrder = SortingConfig.Z_SKY + 10;
            _arrowSr.material = new Material(Shader.Find("Sprites/Default"));

            _arrowGo.transform.localScale = Vector3.one * arrowScale;
        }

        /// <summary>
        /// Generate a minimalist chevron/arrow sprite procedurally.
        /// Points upward (rotated at runtime to face direction).
        /// </summary>
        private static Sprite CreateArrowSprite()
        {
            int size = 32;
            var tex = new Texture2D(size, size);
            tex.filterMode = FilterMode.Bilinear;
            var pixels = new Color[size * size];

            // Clear
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = Color.clear;

            // Draw a chevron (V shape pointing up)
            // Center at (16, 16), pointing up
            float cx = size / 2f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = (x - cx) / cx; // -1 to 1
                    float ny = (y - cx) / cx;

                    // Chevron: two diagonal lines forming a V pointing up
                    // Left arm: from (-0.6, -0.4) to (0, 0.6)
                    // Right arm: from (0.6, -0.4) to (0, 0.6)
                    float leftDist = DistToSegment(nx, ny, -0.6f, -0.5f, 0f, 0.7f);
                    float rightDist = DistToSegment(nx, ny, 0.6f, -0.5f, 0f, 0.7f);
                    float minDist = Mathf.Min(leftDist, rightDist);

                    float thickness = 0.18f;
                    if (minDist < thickness)
                    {
                        float alpha = 1f - (minDist / thickness);
                        alpha = alpha * alpha; // smooth falloff
                        pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                    }
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 32f);
        }

        private static float DistToSegment(float px, float py, float ax, float ay, float bx, float by)
        {
            float dx = bx - ax, dy = by - ay;
            float lenSq = dx * dx + dy * dy;
            if (lenSq < 0.0001f) return Mathf.Sqrt((px - ax) * (px - ax) + (py - ay) * (py - ay));

            float t = Mathf.Clamp01(((px - ax) * dx + (py - ay) * dy) / lenSq);
            float projX = ax + t * dx;
            float projY = ay + t * dy;
            return Mathf.Sqrt((px - projX) * (px - projX) + (py - projY) * (py - projY));
        }

        private void OnDestroy()
        {
            if (_arrowGo != null)
                Destroy(_arrowGo);
        }
    }
}
