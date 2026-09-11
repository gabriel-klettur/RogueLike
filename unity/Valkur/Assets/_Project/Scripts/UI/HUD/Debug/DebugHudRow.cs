using UnityEngine;

namespace Valkur.UI.HUD
{
    /// <summary>Where one cell of a row sits, in texels from the row's left edge.</summary>
    public readonly struct DebugCell
    {
        public readonly int X;
        public readonly int Width;
        public readonly HudTextAlign Align;

        public DebugCell(int x, int width, HudTextAlign align = HudTextAlign.Left)
        {
            X = x;
            Width = width;
            Align = align;
        }
    }

    /// <summary>
    /// One row of the debug HUD: pixel labels at FIXED positions.
    ///
    /// <para><b>Columns are positions, never padding.</b> The old panel aligned its columns with
    /// <c>{name,-14}</c> in a proportional font, so "Bola de Fuego RDY" and "- RDY" put their
    /// "RDY" at different x. Here every cell has its own rect; what is in the cell next to it
    /// cannot move it.</para>
    /// </summary>
    public sealed class DebugHudRow
    {
        private readonly HudPixelText[] _cells;

        public RectTransform Root { get; }
        public int CellCount => _cells.Length;

        public DebugHudRow(Transform parent, string name, HudArt art, int x, int y, int width, int height,
                           DebugCell[] cells, HudFontFace face = HudFontFace.Small)
        {
            Root = HudRect.Make(name, parent, x, y, width, height);
            _cells = new HudPixelText[cells.Length];
            for (int i = 0; i < cells.Length; i++)
            {
                var c = cells[i];
                _cells[i] = HudPixelText.Create(Root, "C" + i, art, face, c.Align, c.X, 0, c.Width, height);
            }
        }

        public HudPixelText Cell(int i) => i >= 0 && i < _cells.Length ? _cells[i] : null;

        /// <summary>Sets a cell's text and colour; both writes are no-ops when nothing changed.</summary>
        public void Set(int i, string text, Color colour)
        {
            var c = Cell(i);
            if (c == null) return;
            c.SetText(text);
            c.SetColour(colour);
        }

        /// <summary>Blanks every cell from <paramref name="from"/> on.</summary>
        public void ClearFrom(int from)
        {
            for (int i = from; i < _cells.Length; i++) _cells[i].SetText(string.Empty);
        }

        public void SetY(int y)
        {
            var p = Root.anchoredPosition;
            if ((int)p.y == y) return;
            Root.anchoredPosition = new Vector2(p.x, y);
        }

        public void SetVisible(bool visible)
        {
            if (Root.gameObject.activeSelf != visible) Root.gameObject.SetActive(visible);
        }
    }
}
