using UnityEngine;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Core.UI;
using Valkur.Data;

namespace Valkur.UI.HUD
{
    public partial class DebugHUD
    {
        // -- Widgets -----------------------------------------------------------------

        private Canvas _canvas;
        private CanvasScaler _scaler;
        private CanvasGroup _group;
        private RectTransform _column;
        private RectTransform _pixels;
        private RectTransform _panel;
        private RectTransform _chip;
        private HudArt _art;
        private DebugHudArt _dart;
        private Material _additive;
        private HudMoteLayer _motes;
        private readonly DebugHudCounters _counters = new DebugHudCounters();

        private DebugHudSection[] _sections;
        private DebugHudSection _perf, _player, _combat, _near;
        private DebugHudRow _bigNumbers, _bigLabels, _stats0, _stats1, _stats2, _hitchRow, _errorRow;
        private DebugHudRow[] _playerRows;
        private DebugHudRow[] _combatRows;
        private DebugHudRow[] _nearRows;
        private Image[] _nearGlyphs;
        private DebugFrameGraph _graph;
        private RectTransform _graphFrame;
        private RawImage _budgetGood, _budgetWarn;
        private Image _copyButton;
        private HudPixelText _copyLabel, _hint;
        private DebugHudRow _chipNumbers, _chipLabels;
        private DebugFrameGraph _chipGraph;
        private Image _panelFrame, _chipFrame;

        private int _pixelScale = -1;
        private int _screenW, _screenH;
        private float _rootScale = -1f;
        private int _panelHeight;
        private int _bandTexels = int.MaxValue;
        private int _priority = -1;          // the section the author opened last keeps its room
        private bool _showErrorRow, _showStatusRow;
        private int _nearShown = 1;

        // -- Geometry (texels) ---------------------------------------------------------

        private int Inset => 2 + _style.paddingTexels;
        private int Width => _style.widthTexels;
        private int ContentWidth => Width - 2 * Inset;
        private int RowH => _style.rowTexels;
        private int HeaderH => _style.headerTexels;
        private const int BigRowH = 10;
        private const int ChipGraphH = 12;

        /// <summary>Panel height in texels as last laid out. For the tests.</summary>
        public int PanelHeightTexels => _panelHeight;

        /// <summary>The whole-pixel scale currently applied.</summary>
        public int PixelScale => _pixelScale;

        public RectTransform PixelsRoot => _pixels;
        public RectTransform PanelRoot => _panel;
        public RectTransform ChipRoot => _chip;
        public Canvas OverlayCanvas => _canvas;
        public HudMoteLayer Motes => _motes;
        internal DebugHudSection[] Sections => _sections;

        // -- Build -----------------------------------------------------------------

        private void Build()
        {
            _art = HudArt.Get();
            _dart = DebugHudArt.Build(_style);
            _style.ResolveSurfaces(out var outline, out var panelC, out var headerC, out var recessC,
                                   out var textC, out var dimC);

            var canvasGo = new GameObject("DebugHUDCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = HudLayout.ToolSortingOrder;
            _canvas.referencePixelsPerUnit = HudArt.SpritePixelsPerUnit;
            _scaler = canvasGo.AddComponent<CanvasScaler>();
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _scaler.referenceResolution = new Vector2(HudLayout.ReferenceWidth, HudLayout.ReferenceHeight);
            _scaler.matchWidthOrHeight = HudLayout.Match;
            _scaler.referencePixelsPerUnit = HudArt.SpritePixelsPerUnit;
            // Needed for the headers and the copy button, and for nothing else (H5).
            canvasGo.AddComponent<GraphicRaycaster>();

            _column = HudRect.Make("Column", canvasGo.transform, 0, 0, Width, 10);
            _column.anchorMin = _column.anchorMax = _column.pivot = new Vector2(0f, 1f);
            _group = _column.gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;

            _pixels = HudRect.Make("Pixels", _column, 0, 0, Width, 10);

            var shader = PlayerHudStyle.Active.hudFxShader != null
                ? PlayerHudStyle.Active.hudFxShader : Shader.Find("Valkur/UI/HudFx");
            if (shader != null)
            {
                _additive = new Material(shader) { name = "DebugHudAdditive", hideFlags = HideFlags.DontSave };
                _additive.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                _additive.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            }

            BuildPanel(headerC, textC, dimC, recessC);
            BuildChip(recessC, dimC);
            _motes = HudMoteLayer.Create(_pixels, _art, _style.moteCapacity, _additive);

            UILayerHelper.SetUILayerRecursive(canvasGo);
            canvasGo.SetActive(false);
            Layout();
        }

        private void BuildPanel(Color headerC, Color textC, Color dimC, Color recessC)
        {
            _panel = HudRect.Make("Panel", _pixels, 0, 0, Width, 10);
            _panelFrame = HudRect.MakeImage("Frame", _panel, _dart.Frame, 0, 0, Width, 10, Image.Type.Sliced);
            HudRect.Fill((RectTransform)_panelFrame.transform);

            int cw = ContentWidth;
            _perf = new DebugHudSection(_panel, "Rendimiento", _art, DebugHudText.Performance, cw, HeaderH,
                                        headerC, _style.title, dimC);
            _player = new DebugHudSection(_panel, "Jugador", _art, DebugHudText.Player, cw, HeaderH,
                                          headerC, _style.title, dimC);
            _combat = new DebugHudSection(_panel, "Combate", _art, DebugHudText.Combat, cw, HeaderH,
                                          headerC, _style.title, dimC);
            _near = new DebugHudSection(_panel, "Cerca", _art, DebugHudText.Nearby, cw, HeaderH,
                                        headerC, _style.title, dimC);
            _sections = new[] { _perf, _player, _combat, _near };
            for (int i = 0; i < _sections.Length; i++)
            {
                int index = i;
                _sections[i].HeaderClicked += () => OnHeaderClicked(index);
            }

            // Rendimiento: the big numbers, the graph, three stat rows, the hitch, the error.
            var body = _perf.Body;
            _bigNumbers = new DebugHudRow(body, "BigNumbers", _art, 0, 0, cw, BigRowH, new[]
            {
                new DebugCell(0, 20, HudTextAlign.Right),
                new DebugCell(40, 26, HudTextAlign.Right),
            }, HudFontFace.Large);
            _bigLabels = new DebugHudRow(body, "BigLabels", _art, 0, 0, cw, BigRowH, new[]
            {
                new DebugCell(23, 16), new DebugCell(69, 12), new DebugCell(84, cw - 84, HudTextAlign.Right),
            });
            _bigLabels.Set(0, "FPS", dimC);
            _bigLabels.Set(1, "MS", dimC);

            _graphFrame = HudRect.MakeImage("GraphFrame", body, _art.BarFrame, 0, 0, cw, _style.graphTexels + 2,
                                             Image.Type.Sliced).rectTransform;
            _graph = new DebugFrameGraph(_graphFrame, "Graph", 1, 1, cw - 2, _style.graphTexels, recessC);
            _budgetGood = MakeBudgetLine(_graphFrame, "BudgetGood", cw - 2);
            _budgetWarn = MakeBudgetLine(_graphFrame, "BudgetWarn", cw - 2);

            _stats0 = StatRow(body, "Stats0");
            _stats1 = StatRow(body, "Stats1");
            _stats2 = StatRow(body, "Stats2");
            SetStatLabels(_stats0, dimC, "P50", "P95", "P99", "MAX");
            SetStatLabels(_stats1, dimC, "CPU", "GPU", "LOTE", "SETP");
            SetStatLabels(_stats2, dimC, "GC/M", "ASIG", "MEM", "ERR");
            _hitchRow = new DebugHudRow(body, "Hitch", _art, 0, 0, cw, RowH, new[]
            {
                new DebugCell(0, 30), new DebugCell(30, cw - 30),
            });
            _hitchRow.Set(0, "TIRON", dimC);
            _errorRow = new DebugHudRow(body, "Error", _art, 0, 0, cw, RowH, new[] { new DebugCell(0, cw) });

            // Jugador: zone and position, motion and posture, the two pools, the statuses.
            _playerRows = new[]
            {
                new DebugHudRow(_player.Body, "Zone", _art, 0, 0, cw, RowH, new[]
                {
                    new DebugCell(0, 18), new DebugCell(19, 52), new DebugCell(74, 14), new DebugCell(88, cw - 88, HudTextAlign.Right),
                }),
                // Speed and posture. The cast phase is NOT here: it is the Combate header's
                // summary, and one datum is drawn in one place (R11).
                new DebugHudRow(_player.Body, "Motion", _art, 0, 0, cw, RowH, new[]
                {
                    new DebugCell(0, 14), new DebugCell(15, 24), new DebugCell(42, 36), new DebugCell(80, cw - 80, HudTextAlign.Right),
                }),
                new DebugHudRow(_player.Body, "Pools", _art, 0, 0, cw, RowH, new[]
                {
                    new DebugCell(0, 18), new DebugCell(19, 40), new DebugCell(62, 18), new DebugCell(81, cw - 81),
                }),
                new DebugHudRow(_player.Body, "Status", _art, 0, 0, cw, RowH, new[] { new DebugCell(0, cw) }),
            };
            _playerRows[0].Set(0, "ZONA", dimC);
            _playerRows[0].Set(2, "POS", dimC);
            _playerRows[1].Set(0, "VEL", dimC);
            _playerRows[2].Set(0, "VIDA", dimC);
            _playerRows[2].Set(2, "MANA", dimC);

            // Combate: the three mouse buttons and the dash, from the seams the cast code uses.
            string[] combatLabels = { "IZQ", "DER", "CEN", "DASH" };
            _combatRows = new DebugHudRow[combatLabels.Length];
            for (int i = 0; i < combatLabels.Length; i++)
            {
                _combatRows[i] = new DebugHudRow(_combat.Body, "Combat" + i, _art, 0, 0, cw, RowH, new[]
                {
                    new DebugCell(0, 18), new DebugCell(19, 78), new DebugCell(98, cw - 98, HudTextAlign.Right),
                });
                _combatRows[i].Set(0, combatLabels[i], dimC);
            }

            // Cerca: a shape per side, the name, health, FSM state, distance.
            int rows = Mathf.Max(1, _style.nearbyRows);
            _nearRows = new DebugHudRow[rows];
            _nearGlyphs = new Image[rows];
            for (int i = 0; i < rows; i++)
            {
                _nearRows[i] = new DebugHudRow(_near.Body, "Near" + i, _art, 0, 0, cw, RowH, new[]
                {
                    new DebugCell(9, 50), new DebugCell(60, 30, HudTextAlign.Right),
                    new DebugCell(93, 28), new DebugCell(122, cw - 122, HudTextAlign.Right),
                });
                _nearGlyphs[i] = HudRect.MakeImage("Side", _nearRows[i].Root, _dart.Hostile, 0, 0, 7, 7);
            }

            // Footer: the copy button (a click target) and the level hint.
            var copyRt = HudRect.Make("Copy", _panel, 0, 0, 32, HeaderH);
            _copyButton = copyRt.gameObject.AddComponent<Image>();
            _copyButton.sprite = _art.White;
            _copyButton.color = headerC;
            _copyButton.raycastTarget = true;
            copyRt.gameObject.AddComponent<DebugHudClick>().Clicked = () => CopyReport();
            _copyLabel = HudPixelText.Create(copyRt, "Label", _art, HudFontFace.Small, HudTextAlign.Centre, 0, 0, 32, HeaderH);
            _copyLabel.SetText(DebugHudText.Copy);
            _copyLabel.SetColour(textC);
            _hint = HudPixelText.Create(_panel, "Hint", _art, HudFontFace.Small, HudTextAlign.Right, 0, 0, cw - 34, HeaderH);
            _hint.SetColour(dimC);
        }

        private void BuildChip(Color recessC, Color dimC)
        {
            int w = ChipWidth;
            _chip = HudRect.Make("Chip", _pixels, 0, 0, w, ChipHeight);
            _chipFrame = HudRect.MakeImage("Frame", _chip, _dart.Frame, 0, 0, w, ChipHeight, Image.Type.Sliced);
            int y = Inset;
            _chipNumbers = new DebugHudRow(_chip, "Numbers", _art, Inset, y, 70, ChipGraphH, new[]
            {
                new DebugCell(0, 20, HudTextAlign.Right), new DebugCell(36, 26, HudTextAlign.Right),
            }, HudFontFace.Large);
            _chipLabels = new DebugHudRow(_chip, "Labels", _art, Inset, y, 80, ChipGraphH, new[]
            {
                new DebugCell(23, 13), new DebugCell(65, 10),
            });
            _chipLabels.Set(0, "FPS", dimC);
            _chipLabels.Set(1, "MS", dimC);
            var frame = HudRect.MakeImage("GraphFrame", _chip, _art.BarFrame, w - Inset - _style.chipGraphWidthTexels - 2,
                                          y - 1, _style.chipGraphWidthTexels + 2, ChipGraphH + 2, Image.Type.Sliced);
            _chipGraph = new DebugFrameGraph(frame.rectTransform, "ChipGraph", 1, 1, _style.chipGraphWidthTexels,
                                             ChipGraphH, recessC);
        }

        private int ChipWidth => Inset * 2 + 78 + 4 + _style.chipGraphWidthTexels + 2;
        private int ChipHeight => Inset * 2 + ChipGraphH;

        private DebugHudRow StatRow(Transform parent, string name)
        {
            int col = ContentWidth / 4;
            var cells = new DebugCell[8];
            for (int k = 0; k < 4; k++)
            {
                // A four-letter label is 15 texels; the value starts 4 texels after it, or
                // "LOTE51" reads as one word (measured on the first live capture).
                cells[k * 2] = new DebugCell(k * col, 16);
                cells[k * 2 + 1] = new DebugCell(k * col + 19, col - 19);
            }
            return new DebugHudRow(parent, name, _art, 0, 0, ContentWidth, RowH, cells);
        }

        private static void SetStatLabels(DebugHudRow row, Color dim, string a, string b, string c, string d)
        {
            row.Set(0, a, dim);
            row.Set(2, b, dim);
            row.Set(4, c, dim);
            row.Set(6, d, dim);
        }

        private RawImage MakeBudgetLine(Transform parent, string name, int width)
        {
            var rt = HudRect.Make(name, parent, 1, 1, width, 1);
            var img = rt.gameObject.AddComponent<RawImage>();
            img.texture = _dart.DashTexture;
            img.uvRect = new Rect(0f, 0f, width / 4f, 1f);
            img.color = _style.budgetLine;
            img.raycastTarget = false;
            return img;
        }

        // -- Layout ------------------------------------------------------------------

        /// <summary>
        /// Places every piece on whole texels, folds sections that do not fit the band, and
        /// sizes the column to whichever face is showing.
        /// </summary>
        private void Layout()
        {
            if (_panel == null) return;
            FitToBand();
            _panelHeight = MeasurePanel();
            int h = _panelHeight;
            int cw = ContentWidth;

            HudRect.Place(_panel, 0, 0, Width, h);
            int top = h - Inset;
            for (int i = 0; i < _sections.Length; i++)
            {
                int used = _sections[i].Place(Inset, top, BodyHeight(i));
                top -= used + _style.sectionGapTexels;
            }
            LayoutPerfBody();
            LayoutRows(_playerRows, _showStatusRow ? 4 : 3);
            LayoutRows(_combatRows, _combatRows.Length);
            LayoutRows(_nearRows, _nearShown);

            HudRect.Place((RectTransform)_copyButton.transform, Inset, Inset, 32, HeaderH);
            HudRect.Place(_hint.rectTransform, Inset + 34, Inset, cw - 34, HeaderH);

            bool chip = _level == LevelChip;
            int w = chip ? ChipWidth : Width;
            int ch = chip ? ChipHeight : h;
            HudRect.Place(_pixels, 0, 0, w, ch);
            if (_motes != null) HudRect.Fill(_motes.rectTransform);
            Refit(force: true);
        }

        private int MeasurePanel()
        {
            int h = Inset * 2 + HeaderH + _style.sectionGapTexels;
            for (int i = 0; i < _sections.Length; i++)
            {
                h += _sections[i].HeaderHeight + (_sections[i].IsOpen ? BodyHeight(i) : 0);
                if (i < _sections.Length - 1) h += _style.sectionGapTexels;
            }
            return h;
        }

        private int BodyHeight(int i)
        {
            switch (i)
            {
                case 0: return BigRowH + _style.graphTexels + 2 + 1 + RowH * (_showErrorRow ? 5 : 4);
                case 1: return RowH * (_showStatusRow ? 4 : 3);
                case 2: return RowH * _combatRows.Length;
                default: return RowH * Mathf.Max(1, _nearShown);
            }
        }

        /// <summary>
        /// Folds sections, lowest first, until the panel fits the band this screen leaves between
        /// the top-left instruments and the player panel. The section the author opened last is
        /// never the one folded, so opening one on a small screen closes another instead of
        /// doing nothing.
        /// </summary>
        private void FitToBand()
        {
            for (int i = 0; i < _sections.Length; i++) _sections[i].AutoCollapsed = false;
            if (_bandTexels == int.MaxValue) return;
            for (int i = _sections.Length - 1; i >= 0 && MeasurePanel() > _bandTexels; i--)
            {
                if (i == _priority || !_sections[i].IsOpen) continue;
                _sections[i].AutoCollapsed = true;
            }
        }

        private void LayoutPerfBody()
        {
            int y = BodyHeight(0);
            y -= BigRowH;
            _bigNumbers.SetY(y);
            _bigLabels.SetY(y);
            y -= _style.graphTexels + 2;
            HudRect.Place(_graphFrame, 0, y, ContentWidth, _style.graphTexels + 2);
            PlaceBudget(_budgetGood, _style.goodMs);
            PlaceBudget(_budgetWarn, _style.warnMs);
            y -= 1;
            y -= RowH; _stats0.SetY(y);
            y -= RowH; _stats1.SetY(y);
            y -= RowH; _stats2.SetY(y);
            y -= RowH; _hitchRow.SetY(y);
            _errorRow.SetVisible(_showErrorRow);
            if (_showErrorRow) { y -= RowH; _errorRow.SetY(y); }
        }

        private void PlaceBudget(RawImage line, float ms)
        {
            int bar = DebugFrameGraph.BarHeight(ms, _style.graphCeilingMs, _style.graphTexels);
            var rt = line.rectTransform;
            HudRect.Place(rt, 1, 1 + bar, ContentWidth - 2, 1);
        }

        private void LayoutRows(DebugHudRow[] rows, int shown)
        {
            int y = RowH * shown;
            for (int i = 0; i < rows.Length; i++)
            {
                bool on = i < shown;
                rows[i].SetVisible(on);
                if (!on) continue;
                y -= RowH;
                rows[i].SetY(y);
            }
        }

        // -- Pixel grid and band ---------------------------------------------------------

        /// <summary>
        /// Re-derives the whole-pixel scale, the band height and the column position when the
        /// screen or the canvas scale changed. The column's top-left corner lands on a whole
        /// screen pixel, so every texel edge inside lands on one too (R1).
        /// </summary>
        public void Refit(bool force = false)
        {
            if (_column == null) return;
            int sw = Screen.width, sh = Screen.height;
            float root = _canvas != null && _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;
            if (!force && sw == _screenW && sh == _screenH && Mathf.Approximately(root, _rootScale)) return;
            _screenW = sw;
            _screenH = sh;
            _rootScale = root;

            var playerStyle = PlayerHudStyle.Active;
            _pixelScale = Mathf.Max(1, playerStyle.HudPixelScaleFor(sw, sh));
            int band = BandTexels(sh, root, _pixelScale, playerStyle);
            if (band != _bandTexels)
            {
                _bandTexels = band;
                Layout();                                     // re-fits and re-enters Refit(force)
                return;
            }

            float perTexel = _pixelScale / root;
            _pixels.localScale = new Vector3(perTexel, perTexel, 1f);
            var size = _pixels.sizeDelta;
            _column.sizeDelta = new Vector2(size.x * perTexel, size.y * perTexel);
            float left = Mathf.Round(HudLayout.ToolColumnLeft * root) / root;
            float top = Mathf.Round(HudLayout.ToolColumnTop * root) / root;
            _column.anchoredPosition = new Vector2(left, -top);
        }

        /// <summary>
        /// Texels available to the panel on a screen of <paramref name="screenHeight"/> pixels:
        /// from the tool column's top down to the combo badge that stacks on the player panel.
        /// Pure, so a test can check the band at any resolution.
        /// </summary>
        public static int BandTexels(int screenHeight, float rootScale, int pixelScale, PlayerHudStyle playerStyle)
        {
            if (pixelScale < 1 || playerStyle == null) return int.MaxValue;
            float topPx = Mathf.Round(HudLayout.ToolColumnTop * rootScale);
            float playerPx = (playerStyle.marginTexels + playerStyle.PanelHeightTexels) * pixelScale;
            float comboPx = (ComboHUD.PreferredHeight + HudLayout.StackGap) * rootScale;
            float bandPx = screenHeight - topPx - playerPx - comboPx;
            return Mathf.Max(0, Mathf.FloorToInt(bandPx / pixelScale));
        }

        // -- Folds -------------------------------------------------------------------------

        private void OnHeaderClicked(int index)
        {
            var s = _sections[index];
            if (s.AutoCollapsed)
            {
                // Folded for room, not by choice: opening it makes it the priority, and the
                // fit folds something else.
                s.Collapsed = false;
                _priority = index;
            }
            else
            {
                s.Collapsed = !s.Collapsed;
                if (!s.Collapsed) _priority = index;
                else if (_priority == index) _priority = -1;
            }
            SaveCollapsed();
            Layout();
            RefreshReadouts();
        }

        private int CollapsedMask()
        {
            int mask = 0;
            for (int i = 0; i < _sections.Length; i++)
                if (_sections[i].Collapsed) mask |= 1 << i;
            return mask;
        }

        private void LoadCollapsed(int mask)
        {
            for (int i = 0; i < _sections.Length; i++) _sections[i].Collapsed = (mask & (1 << i)) != 0;
            Layout();
        }

        // -- Teardown -------------------------------------------------------------------

        private void Teardown()
        {
            _counters.Stop();
            _graph?.Destroy();
            _chipGraph?.Destroy();
            _dart?.Dispose();
            if (_additive != null)
            {
                if (Application.isPlaying) Destroy(_additive);
                else DestroyImmediate(_additive);
                _additive = null;
            }
        }
    }
}
