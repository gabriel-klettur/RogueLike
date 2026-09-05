using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Data;
using Valkur.UIKit;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The grimoire drawn as a constellation: one school at a time, nodes in sockets, joined
    /// by the prerequisite chains that unlock them.
    ///
    /// <para>WHY A SLAB AND NOT A PANEL. The Spells panel is 312 px wide and the outline view
    /// lives there precisely because a graph does not: a school five deep with routed edges
    /// needs a canvas, which is the same conclusion the FSM editor and DungeonNodeGraph each
    /// reached. This takes the whole screen, pans and zooms, and is opened from the Tree tab
    /// rather than owning a hotkey — it is a second READING of the data the Spells editor
    /// already has, not a sixteenth editor.</para>
    ///
    /// <para>IT IS A SELECTOR, like the Grid, the Table and the outline. Clicking a node calls
    /// back into <c>SelectSpell</c>, so the Properties panel and the live preview follow. It
    /// does not rewire prerequisites; the trees are seeded by
    /// <c>Valkur &gt; Progression &gt; Seed Progression Content</c>.</para>
    ///
    /// <para>BUILT TO BE RE-SKINNED. Every node is the same four layers — halo, socket, plate,
    /// icon — and only the icon changes per spell. All four are generated luminance maps
    /// (<see cref="SpellGraphSprites"/>), so dropping authored art in means assigning a sprite
    /// and nothing else. The icon chain is <c>SpellNode.iconOverride</c> →
    /// <c>SpellDefinition.iconSprite</c> → a role glyph, and the header counts how many nodes
    /// are still on the glyph so the remaining work is visible from inside the tool.</para>
    /// </summary>
    internal sealed partial class SpellGraphView : MonoBehaviour
    {
        /// <summary>Above DungeonNodeGraph's 800, so opening this covers whatever is beneath.</summary>
        private const int CANVAS_ORDER = 810;

        // Every distance the board is drawn at lives in SpellGraphGeometry, so the frame that
        // fits the board and the code that draws it read the same numbers.
        private const float NODE_PX = SpellGraphGeometry.NODE_PX;

        /// <summary>
        /// Manual zoom bounds, as FACTORS per wheel notch rather than absolute steps.
        ///
        /// <para>The old step was a flat +-0.12 on a zoom that never left 1.0, so it read as a
        /// steady 12 %. Now that the fit opens a school near 1.8 the same constant would be
        /// 7 % a notch up there and 27 % a notch down at the floor — the same gesture doing
        /// four times as much work depending on where the author already is.</para>
        ///
        /// <para>The ceiling is well above <see cref="SpellGraphGeometry.FIT_MAX"/> on purpose:
        /// the fit is where a school OPENS, so leaving no room above it would mean the author
        /// could not lean in on a single node.</para>
        /// </summary>
        private const float ZOOM_MIN = 0.35f, ZOOM_MAX = 4f, ZOOM_STEP = 0.12f;

        private static readonly Color SLAB_BG = new Color(0.055f, 0.052f, 0.070f, 0.985f);
        private static readonly Color BACKDROP = new Color(0f, 0f, 0f, 0.72f);
        private static readonly Color LINK_COLOR = new Color(0.52f, 0.56f, 0.68f, 0.70f);
        private static readonly Color SOCKET_IDLE = new Color(0.62f, 0.64f, 0.74f, 1f);
        // The selected socket and the plate under an icon are the kit's existing "this one
        // is chosen" and "recessed surface", so they come from the theme and follow a retune.
        // The rest of the palette below is the constellation's own and has no token.
        private static readonly Color SOCKET_SELECTED = UITheme.ACCENT;
        private static readonly Color PLATE_COLOR = UITheme.BG_PANEL;
        private static readonly Color NEEDS_ART = new Color(0.86f, 0.45f, 0.30f, 1f);

        private Canvas _canvas;
        private RectTransform _board;        // pannable / zoomable content
        private RectTransform _viewport;
        private TextMeshProUGUI _headerText;
        private TextMeshProUGUI _statusText;
        private TabStrip _schoolRail;

        private ProgressionCatalog _catalog;
        private System.Action<string> _onSelect;
        private System.Action _onClosed;
        private string _schoolKey;
        private string _selectedSpellKey;

        private readonly List<GameObject> _boardItems = new List<GameObject>();
        private readonly Dictionary<SpellNode, RectTransform> _nodeRects =
            new Dictionary<SpellNode, RectTransform>();

        private float _zoom = 1f;

        /// <summary>
        /// The zoom the last fit chose. It is also the manual zoom FLOOR whenever it lands
        /// below <see cref="ZOOM_MIN"/>, so a school too big for the normal range can still be
        /// seen whole rather than being clamped back up into a crop.
        /// </summary>
        private float _fitZoom = 1f;

        /// <summary>
        /// Set when a fit was asked for and the viewport could not be measured yet. uGUI
        /// resolves nothing on the frame a canvas is created, so the first fit of a freshly
        /// opened view reads a zero rect; before the fit mattered that failed silently into a
        /// 1.0 zoom, which is now the difference between a framed school and a small one.
        /// </summary>
        private bool _fitPending;

        private bool _panning;
        private Vector2 _panStartMouse, _panStartBoard;

        private RectTransform _slab;
        private RectTransform _railHost;
        private int _railColumns = -1;

        /// <summary>Mutes the TabChanged storm a rail rebuild would otherwise raise.</summary>
        private bool _rebuildingRail;

        /// <summary>
        /// Cleared the moment the author pans or zooms by hand.
        ///
        /// <para>It is what lets a window resize RE-FRAME the school without stealing a
        /// framing the author chose — the same distinction that stopped a node click
        /// reframing. Auto-framed views follow the window; hand-framed ones are left alone.</para>
        /// </summary>
        private bool _autoFramed = true;

        private Vector2 _lastViewportSize = Vector2.zero;

        /// <summary>Open the constellation over everything, on the given school.</summary>
        public static SpellGraphView Open(ProgressionCatalog catalog, string schoolKey,
            string selectedSpellKey, System.Action<string> onSelect, System.Action onClosed = null)
        {
            if (catalog == null) return null;

            var go = new GameObject("SpellGraphView");
            var view = go.AddComponent<SpellGraphView>();
            view._catalog = catalog;
            view._onSelect = onSelect;
            view._onClosed = onClosed;
            view._selectedSpellKey = selectedSpellKey;
            view._schoolKey = schoolKey;
            view.Build();
            return view;
        }

        /// <summary>
        /// Dismiss the constellation.
        ///
        /// <para><paramref name="notify"/> separates the two ways this ends. Escape or the
        /// Close button is the view closing ITSELF, and the owner has to hear about it so the
        /// tab that opened it stops being lit. The owner closing it — F4 going away, or
        /// another view tab being picked — must NOT call back, or the callback re-selects a
        /// tab, that raises TabChanged, and it lands straight back here.</para>
        /// </summary>
        public void Close(bool notify = true)
        {
            var callback = _onClosed;
            _onClosed = null;

            if (_canvas != null) SafeDestroy.Of(_canvas.gameObject);
            SafeDestroy.Of(gameObject);

            if (notify) callback?.Invoke();
        }

        /// <summary>
        /// Repoint the highlight without rebuilding, for a selection made elsewhere.
        ///
        /// <para>It does NOT refit. A selection changes four colours and moves nothing, so
        /// reframing on it would throw away wherever the author had panned and zoomed to —
        /// which is what this view did on every single node click before the fit was worth
        /// anything, and would have become a hard snap the moment it was.</para>
        /// </summary>
        public void SetSelected(string spellKey)
        {
            _selectedSpellKey = spellKey;
            RefreshBoard(refit: false);
        }

        // ── chrome ───────────────────────────────────────────────────────────────────

        private void Build()
        {
            SpellGraphSprites.EnsureAll();

            var canvasGo = new GameObject("SpellGraphCanvas",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = CANVAS_ORDER;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

            var backdrop = UIFactory.CreateUI("Backdrop", canvasGo.transform);
            UIFactory.StretchFill(backdrop);
            backdrop.AddComponent<Image>().color = BACKDROP;

            var slab = UIFactory.CreateUI("Slab", canvasGo.transform);
            var slabRt = _slab = slab.GetComponent<RectTransform>();
            slabRt.anchorMin = new Vector2(0.04f, 0.05f);
            slabRt.anchorMax = new Vector2(0.96f, 0.95f);
            slabRt.offsetMin = Vector2.zero;
            slabRt.offsetMax = Vector2.zero;
            slab.AddComponent<Image>().color = SLAB_BG;

            BuildHeader(slab.transform);
            BuildSchoolRail(slab.transform);
            BuildViewport(slab.transform);
            BuildFooter(slab.transform);

            LayoutChrome();
            RefreshBoard(refit: true);
        }

        private void BuildHeader(Transform slab)
        {
            var bar = UIFactory.CreateUI("Header", slab);
            var rt = bar.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(0f, -SpellGraphGeometry.HEADER_H);
            rt.offsetMax = Vector2.zero;
            bar.AddComponent<Image>().color = UITheme.BG_HEADER;

            _headerText = Label(bar.transform, "", 15f, FontStyles.Bold, UITheme.TEXT_PRIMARY,
                TextAlignmentOptions.MidlineLeft, left: 16f, right: -180f);

            var close = UIFactory.CreateUI("Close", bar.transform);
            var crt = close.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(1f, 0f);
            crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(1f, 0.5f);
            crt.anchoredPosition = new Vector2(-8f, 0f);
            crt.sizeDelta = new Vector2(112f, -8f);
            var cimg = close.AddComponent<Image>();
            cimg.color = UITheme.BTN_NORMAL;
            var cbtn = close.AddComponent<Button>();
            cbtn.targetGraphic = cimg;
            cbtn.onClick.AddListener(() => Close());
            Label(close.transform, "Close  (Esc)", 11f, FontStyles.Normal, UITheme.TEXT_PRIMARY,
                TextAlignmentOptions.Midline, left: 0f, right: 0f);
        }

        /// <summary>
        /// The host the school rail lives in. Built once; the rail inside it is rebuilt
        /// whenever the window width changes the number of columns that stay readable.
        /// </summary>
        private void BuildSchoolRail(Transform slab)
        {
            var host = UIFactory.CreateUI("SchoolRailHost", slab);
            _railHost = host.GetComponent<RectTransform>();
            _railHost.anchorMin = new Vector2(0f, 1f);
            _railHost.anchorMax = new Vector2(1f, 1f);
            _railHost.pivot = new Vector2(0.5f, 1f);

            var vlg = host.AddComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandHeight = false;

            RebuildSchoolRail(SpellGraphGeometry.RailColumns(RailWidth(), SchoolCount()));
        }

        private int SchoolCount()
        {
            var trees = _catalog != null ? _catalog.spellTrees : null;
            int n = 0;
            for (int i = 0; trees != null && i < trees.Length; i++) if (trees[i] != null) n++;
            return n;
        }

        /// <summary>Rail width available inside the slab, once its own 8 px inset is taken.</summary>
        private float RailWidth()
            => _slab != null ? Mathf.Max(0f, _slab.rect.width - 16f) : 0f;

        /// <summary>
        /// Replace the rail with one wrapped at <paramref name="columns"/>.
        ///
        /// <para><see cref="TabStrip"/> fixes its column count at creation and offers no way
        /// to clear it, so a column change is a rebuild. Both <c>AddTab</c> (on the first tab)
        /// and <c>SetActive</c> raise <c>TabChanged</c> unconditionally, so the handler is
        /// muted for the duration — otherwise restoring the author's own school would read as
        /// them picking a new one, and refit a board that had not moved.</para>
        /// </summary>
        private void RebuildSchoolRail(int columns)
        {
            if (_railHost == null) return;

            string keep = _schoolRail != null ? _schoolRail.ActiveKey : _schoolKey;
            if (_schoolRail != null) SafeDestroy.Of(_schoolRail.gameObject);

            _rebuildingRail = true;
            _railColumns = Mathf.Max(1, columns);
            _schoolRail = TabStrip.CreateWrapped(_railHost, "SchoolRail",
                columns: _railColumns, rowHeight: SpellGraphGeometry.RAIL_ROW_H,
                rowSpacing: SpellGraphGeometry.RAIL_ROW_SPACING);

            var trees = _catalog.spellTrees;
            for (int i = 0; trees != null && i < trees.Length; i++)
            {
                var tree = trees[i];
                if (tree == null) continue;
                _schoolRail.AddTab(SchoolKeyOf(tree), SchoolLabel(tree), null);
            }
            _schoolRail.TabChanged += OnSchoolTabChanged;

            if (string.IsNullOrEmpty(keep) || !_schoolRail.SetActive(keep))
                if (_schoolRail.Count > 0) _schoolRail.SetActive(0);
            _schoolKey = _schoolRail.ActiveKey;
            _rebuildingRail = false;
        }

        /// <summary>A different school is a different board, so it may be framed afresh.</summary>
        private void OnSchoolTabChanged(int _, string key)
        {
            if (_rebuildingRail) return;
            _schoolKey = key;
            _autoFramed = true;
            RefreshBoard(refit: true);
        }

        private void BuildViewport(Transform slab)
        {
            var vp = UIFactory.CreateUI("Viewport", slab);
            _viewport = vp.GetComponent<RectTransform>();
            _viewport.anchorMin = new Vector2(0f, 0f);
            _viewport.anchorMax = new Vector2(1f, 1f);
            // The vertical insets are applied by LayoutChrome, which derives them from the
            // rail's real row count. They used to be -100 and 30, which cleared a two-row rail
            // and nothing else.
            _viewport.offsetMin = new Vector2(SpellGraphGeometry.VIEWPORT_INSET, 0f);
            _viewport.offsetMax = new Vector2(-SpellGraphGeometry.VIEWPORT_INSET, 0f);
            vp.AddComponent<RectMask2D>();

            // An Image that raycasts is what gives the empty background something to catch a
            // drag on; without it a pan can only start on a node, which is the one place a
            // drag must not pan.
            var catcher = vp.AddComponent<Image>();
            catcher.color = new Color(0f, 0f, 0f, 0.0035f);

            var board = UIFactory.CreateUI("Board", vp.transform);
            _board = board.GetComponent<RectTransform>();
            _board.anchorMin = _board.anchorMax = new Vector2(0.5f, 0.5f);
            _board.pivot = new Vector2(0.5f, 0.5f);
            _board.anchoredPosition = Vector2.zero;
        }

        private void BuildFooter(Transform slab)
        {
            var bar = UIFactory.CreateUI("Footer", slab);
            var rt = bar.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = new Vector2(0f, SpellGraphGeometry.FOOTER_H);
            bar.AddComponent<Image>().color = UITheme.BG_HEADER;

            _statusText = Label(bar.transform, "Drag to pan  ·  Wheel to zoom  ·  Click a node to select it",
                10.5f, FontStyles.Normal, UITheme.TEXT_MUTED,
                TextAlignmentOptions.MidlineLeft, left: 12f, right: -12f);
        }

        /// <summary>
        /// Re-derive every chrome inset from the rail's real row count, and rewrap the rail
        /// if the window can now carry a different number of readable columns.
        ///
        /// <para>Returns whether anything moved, so the caller can decide whether the board
        /// needs re-framing. Called on open and whenever the slab's size changes — the slab is
        /// anchored to fractions of the screen, so ANY window resize reaches here.</para>
        /// </summary>
        private bool LayoutChrome()
        {
            if (_slab == null || _railHost == null || _viewport == null) return false;

            int wanted = SpellGraphGeometry.RailColumns(RailWidth(), SchoolCount());
            bool changed = false;
            if (wanted != _railColumns) { RebuildSchoolRail(wanted); changed = true; }

            float railH = SpellGraphGeometry.RailHeight(
                SpellGraphGeometry.RailRows(SchoolCount(), _railColumns));
            float top = SpellGraphGeometry.ChromeTopInset(railH);

            // Every write below is guarded by a comparison. This runs once a frame from
            // TickResponsiveLayout, and assigning a RectTransform's offsets marks its layout
            // dirty whether or not the value changed — an unguarded version would rebuild the
            // rail's VerticalLayoutGroup sixty times a second for a window nobody resized.
            var railMin = new Vector2(8f, -(top - SpellGraphGeometry.RAIL_PAD));
            var railMax = new Vector2(-8f,
                -(SpellGraphGeometry.HEADER_H + SpellGraphGeometry.RAIL_PAD));
            if (_railHost.offsetMin != railMin || _railHost.offsetMax != railMax)
            {
                _railHost.offsetMin = railMin;
                _railHost.offsetMax = railMax;
                changed = true;
            }

            var min = new Vector2(SpellGraphGeometry.VIEWPORT_INSET,
                SpellGraphGeometry.FOOTER_H + SpellGraphGeometry.VIEWPORT_INSET * 0.5f);
            var max = new Vector2(-SpellGraphGeometry.VIEWPORT_INSET, -top);
            if (_viewport.offsetMin != min || _viewport.offsetMax != max)
            {
                _viewport.offsetMin = min;
                _viewport.offsetMax = max;
                changed = true;
            }

            return changed;
        }

        internal static string SchoolKeyOf(SpellTree tree)
            => string.IsNullOrEmpty(tree.schoolKey) ? tree.name : tree.schoolKey;

        private static string SchoolLabel(SpellTree tree)
        {
            string key = SchoolKeyOf(tree);
            if (string.IsNullOrEmpty(key)) return "?";
            return char.ToUpperInvariant(key[0]) + key.Substring(1);
        }

        private static TextMeshProUGUI Label(Transform parent, string text, float size,
            FontStyles style, Color color, TextAlignmentOptions align, float left, float right)
        {
            // Image + TMP on the same GameObject is a NullReferenceException in this project,
            // so every label is its own child and the parent owns the background.
            var go = UIFactory.CreateUI("Label", parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = new Vector2(left, 0f);
            rt.offsetMax = new Vector2(right, 0f);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.fontStyle = style;
            tmp.color = color;
            tmp.alignment = align;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.raycastTarget = false;
            return tmp;
        }
    }
}
