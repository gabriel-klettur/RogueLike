using UnityEngine;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// World-space grid cursor that highlights the cell under the mouse.
    /// Draws a colored outline rectangle matching the brush size.
    /// Maps to Python's TileEditorView brush preview rectangle (OUTLINE_HOVER).
    /// </summary>
    public class TileEditorGridCursor : MonoBehaviour
    {
        private static readonly Color CursorColor = new Color(0.85f, 0.75f, 0.45f, 0.8f);
        private static readonly Color EraserColor = new Color(1f, 0.3f, 0.3f, 0.7f);
        private static readonly Color FillColor = new Color(0.3f, 0.8f, 1f, 0.7f);
        private static readonly Color EyedropperColor = new Color(0.3f, 1f, 0.5f, 0.7f);

        private LineRenderer _lineRenderer;
        private SpriteRenderer _fillRenderer;

        public void Initialize()
        {
            // Line renderer for the outline
            _lineRenderer = gameObject.AddComponent<LineRenderer>();
            _lineRenderer.useWorldSpace = true;
            _lineRenderer.loop = true;
            _lineRenderer.positionCount = 4;
            _lineRenderer.startWidth = 0.04f;
            _lineRenderer.endWidth = 0.04f;
            _lineRenderer.sortingOrder = 998;
            _lineRenderer.material = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")
                ?? Shader.Find("Sprites/Default"));
            _lineRenderer.startColor = CursorColor;
            _lineRenderer.endColor = CursorColor;

            // Semi-transparent fill quad
            var fillGo = new GameObject("CursorFill");
            fillGo.transform.SetParent(transform, false);
            _fillRenderer = fillGo.AddComponent<SpriteRenderer>();
            _fillRenderer.sortingOrder = 997;
            _fillRenderer.color = new Color(CursorColor.r, CursorColor.g, CursorColor.b, 0.15f);
            _fillRenderer.sprite = CreateWhiteSprite();
        }

        /// <summary>
        /// Update the cursor position and size. Call from TileEditorManager.Update().
        /// </summary>
        public void UpdateCursor(Vector3 worldCenter, int brushSize, TileEditorState.Tool tool)
        {
            Color color = GetToolColor(tool);
            float half = brushSize * 0.5f;

            // Outline corners
            Vector3 bl = worldCenter + new Vector3(-half, -half, 0f);
            Vector3 br = worldCenter + new Vector3(half, -half, 0f);
            Vector3 tr = worldCenter + new Vector3(half, half, 0f);
            Vector3 tl = worldCenter + new Vector3(-half, half, 0f);

            _lineRenderer.SetPosition(0, bl);
            _lineRenderer.SetPosition(1, br);
            _lineRenderer.SetPosition(2, tr);
            _lineRenderer.SetPosition(3, tl);
            _lineRenderer.startColor = color;
            _lineRenderer.endColor = color;

            // Fill quad
            if (_fillRenderer != null)
            {
                _fillRenderer.transform.position = worldCenter;
                _fillRenderer.transform.localScale = new Vector3(brushSize, brushSize, 1f);
                _fillRenderer.color = new Color(color.r, color.g, color.b, 0.15f);
            }
        }

        private Color GetToolColor(TileEditorState.Tool tool)
        {
            switch (tool)
            {
                case TileEditorState.Tool.Eraser: return EraserColor;
                case TileEditorState.Tool.Fill: return FillColor;
                case TileEditorState.Tool.Eyedropper: return EyedropperColor;
                default: return CursorColor;
            }
        }

        private static Sprite CreateWhiteSprite()
        {
            var tex = new Texture2D(4, 4);
            var pixels = new Color[16];
            for (int i = 0; i < 16; i++) pixels[i] = Color.white;
            tex.SetPixels(pixels);
            tex.Apply();
            tex.filterMode = FilterMode.Point;
            return Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
        }
    }
}
