using UnityEngine;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// World-space grid cursor that highlights the cell under the mouse (CYAN hover)
    /// and the last-interacted cell (GREEN selection).
    /// Maps to Python's TileOutlineView: OUTLINE_HOVER=(0,220,255) + OUTLINE_SEL=(0,255,0).
    /// </summary>
    public class TileEditorGridCursor : MonoBehaviour
    {
        // Python: OUTLINE_HOVER = (0, 220, 255), HOVER_ALPHA = 60
        private static readonly Color HoverColor = new Color(0f, 0.863f, 1f, 0.85f);
        private const float HoverFillAlpha = 0.235f; // 60/255

        // Python: OUTLINE_SEL = (0, 255, 0)
        private static readonly Color SelectionColor = new Color(0f, 1f, 0f, 0.9f);
        private const float SelectionFillAlpha = 0.12f;

        // Python: OUTLINE_WIDTH = 3 px → 3/16 PPU ≈ 0.1875 world units
        private const float OutlineWidth = 0.06f;

        // ── Hover cursor ──
        private LineRenderer _hoverLine;
        private SpriteRenderer _hoverFill;

        // ── Selection cursor ──
        private GameObject _selGo;
        private LineRenderer _selLine;
        private SpriteRenderer _selFill;

        private Material _lineMaterial;
        private Sprite _whiteSprite;

        public void Initialize()
        {
            _lineMaterial = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")
                ?? Shader.Find("Sprites/Default"));
            _lineMaterial.hideFlags = HideFlags.HideAndDontSave;
            _whiteSprite = CreateWhiteSprite();

            // ── Hover outline + fill ──
            _hoverLine = CreateOutline(gameObject, HoverColor, 998);

            var hoverFillGo = new GameObject("HoverFill");
            hoverFillGo.transform.SetParent(transform, false);
            _hoverFill = hoverFillGo.AddComponent<SpriteRenderer>();
            _hoverFill.sortingOrder = 997;
            _hoverFill.color = new Color(HoverColor.r, HoverColor.g, HoverColor.b, HoverFillAlpha);
            _hoverFill.sprite = _whiteSprite;

            // ── Selection outline + fill ──
            _selGo = new GameObject("SelectionCursor");
            _selGo.transform.SetParent(transform, false);

            _selLine = CreateOutline(_selGo, SelectionColor, 996);

            var selFillGo = new GameObject("SelectionFill");
            selFillGo.transform.SetParent(_selGo.transform, false);
            _selFill = selFillGo.AddComponent<SpriteRenderer>();
            _selFill.sortingOrder = 995;
            _selFill.color = new Color(SelectionColor.r, SelectionColor.g, SelectionColor.b, SelectionFillAlpha);
            _selFill.sprite = _whiteSprite;

            _selGo.SetActive(false);
        }

        /// <summary>
        /// Update hover cursor position/size. Called every frame from TileEditorManager.
        /// </summary>
        public void UpdateCursor(Vector3 worldCenter, int brushSize, TileEditorState.Tool tool)
        {
            float half = brushSize * 0.5f;
            SetRect(_hoverLine, worldCenter, half);
            _hoverLine.startColor = HoverColor;
            _hoverLine.endColor = HoverColor;

            if (_hoverFill != null)
            {
                _hoverFill.transform.position = worldCenter;
                _hoverFill.transform.localScale = new Vector3(brushSize, brushSize, 1f);
                _hoverFill.color = new Color(HoverColor.r, HoverColor.g, HoverColor.b, HoverFillAlpha);
            }
        }

        /// <summary>
        /// Show/update the GREEN selection indicator at a world position.
        /// </summary>
        public void SetSelection(Vector3 worldCenter, int brushSize)
        {
            if (_selGo == null) return;
            _selGo.SetActive(true);
            float half = brushSize * 0.5f;
            SetRect(_selLine, worldCenter, half);
            if (_selFill != null)
            {
                _selFill.transform.position = worldCenter;
                _selFill.transform.localScale = new Vector3(brushSize, brushSize, 1f);
            }
        }

        /// <summary>
        /// Hide the GREEN selection indicator.
        /// </summary>
        public void ClearSelection()
        {
            if (_selGo != null) _selGo.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_lineMaterial != null)
                Destroy(_lineMaterial);
        }

        // ── Helpers ──

        private LineRenderer CreateOutline(GameObject go, Color color, int sortOrder)
        {
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.loop = true;
            lr.positionCount = 4;
            lr.startWidth = OutlineWidth;
            lr.endWidth = OutlineWidth;
            lr.sortingOrder = sortOrder;
            lr.sharedMaterial = _lineMaterial;
            lr.startColor = color;
            lr.endColor = color;
            return lr;
        }

        private static void SetRect(LineRenderer lr, Vector3 center, float half)
        {
            Vector3 bl = center + new Vector3(-half, -half, 0f);
            Vector3 br = center + new Vector3(half, -half, 0f);
            Vector3 tr = center + new Vector3(half, half, 0f);
            Vector3 tl = center + new Vector3(-half, half, 0f);
            lr.SetPosition(0, bl);
            lr.SetPosition(1, br);
            lr.SetPosition(2, tr);
            lr.SetPosition(3, tl);
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
