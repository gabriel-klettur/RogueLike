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

        private const float NODE_PX = 76f;
        private const float COL_SPACING = 138f;
        private const float ROW_SPACING = 122f;
        private const float BOARD_PADDING = 120f;

        private const float ZOOM_MIN = 0.45f, ZOOM_MAX = 2.2f, ZOOM_STEP = 0.12f;

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
        private bool _panning;
        private Vector2 _panStartMouse, _panStartBoard;

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

        /// <summary>Repoint the highlight without rebuilding, for a selection made elsewhere.</summary>
        public void SetSelected(string spellKey)
        {
            _selectedSpellKey = spellKey;
            RefreshBoard();
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
            var slabRt = slab.GetComponent<RectTransform>();
            slabRt.anchorMin = new Vector2(0.04f, 0.05f);
            slabRt.anchorMax = new Vector2(0.96f, 0.95f);
            slabRt.offsetMin = Vector2.zero;
            slabRt.offsetMax = Vector2.zero;
            slab.AddComponent<Image>().color = SLAB_BG;

            BuildHeader(slab.transform);
            BuildSchoolRail(slab.transform);
            BuildViewport(slab.transform);
            BuildFooter(slab.transform);

            RefreshBoard();
        }

        private void BuildHeader(Transform slab)
        {
            var bar = UIFactory.CreateUI("Header", slab);
            var rt = bar.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(0f, -38f);
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
        /// The school rail. Wrapped at four columns for the reason the outline's strip is:
        /// eleven tabs across one row leaves each of them unreadable.
        /// </summary>
        private void BuildSchoolRail(Transform slab)
        {
            var host = UIFactory.CreateUI("SchoolRailHost", slab);
            var rt = host.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(8f, -96f);
            rt.offsetMax = new Vector2(-8f, -40f);

            var vlg = host.AddComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandHeight = false;

            _schoolRail = TabStrip.CreateWrapped(host.transform, "SchoolRail",
                columns: 5, rowHeight: 24f);

            var trees = _catalog.spellTrees;
            for (int i = 0; trees != null && i < trees.Length; i++)
            {
                var tree = trees[i];
                if (tree == null) continue;
                _schoolRail.AddTab(SchoolKeyOf(tree), SchoolLabel(tree), null);
            }
            _schoolRail.TabChanged += (_, key) => { _schoolKey = key; _zoom = 1f; RefreshBoard(); };
            if (!string.IsNullOrEmpty(_schoolKey)) _schoolRail.SetActive(_schoolKey);
            else if (_schoolRail.Count > 0) _schoolRail.SetActive(0);
            _schoolKey = _schoolRail.ActiveKey;
        }

        private void BuildViewport(Transform slab)
        {
            var vp = UIFactory.CreateUI("Viewport", slab);
            _viewport = vp.GetComponent<RectTransform>();
            _viewport.anchorMin = new Vector2(0f, 0f);
            _viewport.anchorMax = new Vector2(1f, 1f);
            _viewport.offsetMin = new Vector2(6f, 30f);
            _viewport.offsetMax = new Vector2(-6f, -100f);
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
            rt.offsetMax = new Vector2(0f, 26f);
            bar.AddComponent<Image>().color = UITheme.BG_HEADER;

            _statusText = Label(bar.transform, "Drag to pan  ·  Wheel to zoom  ·  Click a node to select it",
                10.5f, FontStyles.Normal, UITheme.TEXT_MUTED,
                TextAlignmentOptions.MidlineLeft, left: 12f, right: -12f);
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
