using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Core.UI;
using Valkur.Data;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.Spells;
using Valkur.UI.HUD;

namespace Valkur.Gameplay.HUD
{
    /// <summary>
    /// The window itself: canvas, opaque stone, the texel grid inside it, and the three
    /// columns — school rail, constellation, detail card.
    ///
    /// <para><b>The outer rect is in screen fractions and everything inside it is in texels.</b>
    /// That split is deliberate and temporary: the character sheet's tab strip is anchored at
    /// the same fractions and is owned by <c>Valkur.UI</c>, so until the sheet's chrome is
    /// rebuilt, matching it is what keeps the strip sitting on this panel instead of beside
    /// it. Every rect DRAWN here lands on a whole texel (R1), which is the half that shows.</para>
    /// </summary>
    public sealed partial class SpellTreeHUD
    {
        private GrimoireStyle _style;
        private HudTheme _theme;
        private HudArt _art;
        private GrimoireArt _ink;
        private Material _additive;
        private Font _font;

        private Canvas _canvas;
        private GameObject _root;
        private RectTransform _panel;
        private RectTransform _pixels;
        private RectTransform _railRoot;
        private RectTransform _boardViewport;
        private RectTransform _boardRoot;
        private RectTransform _cardRoot;
        private HudMoteLayer _motes;

        private HudPixelText _title;
        private HudPixelText _purse;
        private Text _flavour;
        private Image _stone;
        private RectTransform _filterRoot;
        private float _spiritT;

        private GrimoireGeometry.Frame _frame;
        private Vector2Int _panelTexels;
        private int _pixelScale = 2;

        private float _openLeft;
        private bool _opening;

        private readonly List<RailRow> _railRows = new List<RailRow>();

        /// <summary>One school in the vertical rail.</summary>
        private sealed class RailRow
        {
            public Image Background;
            public Image Filet;
            public Image Sigil;
            public HudPixelText NameInk;
            public HudPixelText Count;
        }

        public void EnsureBuilt()
        {
            if (_canvas != null) return;

            _style = GrimoireStyle.Active;
            _theme = HudTheme.Active;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            BuildCanvas();
            BuildPanel();
            BuildTitle();
            BuildRail();
            BuildBoard();
            BuildFilterRow();
            BuildCard();
            BuildTitleRule();
            BuildMotes();

            _root.SetActive(false);
        }

        private void BuildCanvas()
        {
            _root = new GameObject("SpellTreeHUD_Root");
            _root.transform.SetParent(transform, false);

            _canvas = _root.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // The band, not a number written here. At 60 the panel drew UNDER the minimap
            // (105) and the music plaque (140), both of which stayed on top of an open
            // grimoire.
            _canvas.sortingOrder = HudLayout.CharacterSheetSortingOrder;

            // HudLayout.ApplyScaler, not three lines copied here. It carries the shared
            // reference and match (R9) AND the player's own interface-size preference
            // (GameSettings.uiScale x TextScale). This panel was built before that helper
            // existed and set the two constants by hand, so it was the one surface where the
            // size setting did nothing — a preference the player watches work on one screen
            // and not the next is the exact shape this project keeps removing.
            HudLayout.ApplyScaler(_root.AddComponent<CanvasScaler>());

            _root.AddComponent<GraphicRaycaster>();
        }

        private void BuildPanel()
        {
            var panelGo = new GameObject("Panel", typeof(RectTransform));
            panelGo.transform.SetParent(_root.transform, false);
            _panel = (RectTransform)panelGo.transform;
            _panel.anchorMin = new Vector2(GrimoireGeometry.PanelLeft, GrimoireGeometry.PanelBottom);
            _panel.anchorMax = new Vector2(GrimoireGeometry.PanelRight, GrimoireGeometry.PanelTop);
            _panel.offsetMin = _panel.offsetMax = Vector2.zero;

            // OPAQUE, and an OBJECT. At 0.85 alpha the same row measured luminance 0.0092
            // over dark stone and 0.0177 over pale sand — 93 % brighter — so a node's colour
            // changed with whatever the player stood on (R3). And flat, it was worse than
            // translucent: the panel against the hole of its own columns measured 1.06 : 1, so
            // the three columns did not exist. The bevelled frame is what makes an edge visible
            // between two dark tones; raising the tone alone cannot.
            _ink = GrimoireArt.Get();
            _stone = panelGo.AddComponent<Image>();
            _stone.sprite = _ink.Frame;
            _stone.type = Image.Type.Sliced;
            _stone.color = _theme.stoneLight;

            // The scale the rest of the HUD would pick, CAPPED at what this window can spell.
            //
            // Measured before the cap: at 1080p the shipped rule answers 3, which leaves the
            // board 110 texels for a chip row that inks 256 and squeezes every label to its
            // floor — so the window was at its best on the smallest screen. The cap is derived
            // from the row's own ink rather than declared, because a constant beside a
            // measurement is the half that goes stale on the next translation.
            int wanted = Mathf.Max(1, PlayerHudStyle.Active.HudPixelScaleFor(
                Mathf.Max(1, Screen.width), Mathf.Max(1, Screen.height)));
            _pixelScale = GrimoireGeometry.PixelScaleFor(
                wanted, _style, FilterRowIdealWidth(), BandIdealHeight());
            _panelTexels = GrimoireGeometry.PanelTexels(_pixelScale);
            _frame = GrimoireGeometry.Split(_panelTexels, _style);

            // The texel space: one child scaled so that one unit inside it is one texel, and
            // the panel's own canvas units stay outside. Same shape as the player panel's
            // Pixels child.
            var pixelsGo = new GameObject("Pixels", typeof(RectTransform));
            pixelsGo.transform.SetParent(_panel, false);
            _pixels = (RectTransform)pixelsGo.transform;
            _pixels.anchorMin = _pixels.anchorMax = new Vector2(0f, 0f);
            _pixels.pivot = new Vector2(0f, 0f);
            _pixels.anchoredPosition = Vector2.zero;
            _pixels.sizeDelta = new Vector2(_panelTexels.x, _panelTexels.y);
            _pixels.localScale = new Vector3(_pixelScale, _pixelScale, 1f);
        }

        /// <summary>
        /// The band height the window needs: the TALLEST constellation it may be asked to
        /// draw, and the rail beside it, which does not scroll.
        ///
        /// <para>Measured from the live trees rather than declared, for the reason the filter
        /// row's width is: the answer is a fact about the shipped data. Measured today it is
        /// two values and not nine — 117 texels for three schools and 173 for six — because
        /// every school is four depth steps across and differs only in how many siblings share
        /// a step.</para>
        /// </summary>
        private int BandIdealHeight()
        {
            var trees = grimoire != null ? grimoire.Trees : null;
            int rows = trees != null ? trees.Count : 0;

            float tallest = 0f;
            for (int i = 0; i < rows; i++)
            {
                var tree = trees[i];
                if (tree == null) continue;
                var measured = GrimoireGeometry.Measure(
                    SpellGraphLayout.Resolve(tree.Nodes), _style);
                if (measured.Size.y > tallest) tallest = measured.Size.y;
            }

            return GrimoireGeometry.BandMinTexels(
                _style, Mathf.CeilToInt(tallest), rows);
        }

        private void BuildTitle()
        {
            _art = HudArt.Get();

            var t = _frame.Title;

            // The two texels at the FOOT of the band belong to the rule, so the words sit
            // above it rather than on it.
            int textY = t.y + RuleBandTexels;
            int textH = Mathf.Max(1, t.height - RuleBandTexels);

            _title = HudPixelText.Create(_pixels, "Title", _art, HudFontFace.Small,
                                         HudTextAlign.Left, t.x, textY, t.width, textH);
            _title.SetColour(_theme.gold);

            _purse = HudPixelText.Create(_pixels, "Purse", _art, HudFontFace.Small,
                                         HudTextAlign.Right, t.x, textY, t.width, textH);
            _purse.SetColour(_theme.text);

            // The school's own line, the one piece of prose the grimoire has: authored on all
            // nine schools and read by nothing for the life of the project. TMP-free legacy
            // Text rather than the pixel face because the bitmap face is CAPITALS ONLY and a
            // sentence shouted is a sentence nobody reads (H2).
            var f = _frame.Footer;
            _flavour = MakeText(_pixels, "Flavour", f.x, f.y, f.width, f.height, 9,
                                TextAnchor.MiddleCenter);
            _flavour.color = _theme.textDim;
            _flavour.fontStyle = FontStyle.Italic;
        }

        /// <summary>Texels at the foot of the title band reserved for the rule.</summary>
        private const int RuleBandTexels = 2;

        /// <summary>
        /// One gold rule under the title, with a diamond pinned on it. The gold is the theme's
        /// "importance" token and this is the one place in the window that claims it — a window
        /// where everything is gold is one where the gold says nothing.
        ///
        /// <para><b>It is built LAST, and that is the whole reason it was invisible.</b> Every
        /// piece of this window is a child of one Pixels rect, so sibling order IS draw order —
        /// and the rule was created in <c>BuildTitle</c>, before the rail, the board and the
        /// card cut their holes. Those three recesses then drew straight over it. Its POSITION
        /// was wrong too, two texels below its own band, i.e. inside the columns: two separate
        /// defects with one symptom, and fixing either alone leaves a line nobody can see.</para>
        ///
        /// <para>The diamond straddles the seam on purpose — half in the title band, half over
        /// the columns — so it reads as a pin holding the rule down rather than as a lozenge
        /// floating above it. That is only legible because of the draw order above.</para>
        /// </summary>
        private void BuildTitleRule()
        {
            var t = _frame.Title;
            int ruleY = t.y;

            var rule = HudRect.MakeImage("TitleRule", _pixels, _ink.Rule, t.x, ruleY,
                                         t.width, 1, Image.Type.Sliced);
            // Gold, not its shade: a dimmed gold on a dark panel is a line nobody sees.
            rule.color = _theme.gold;
            rule.raycastTarget = false;

            var gem = HudRect.MakeImage("TitleGem", _pixels, _ink.Diamond,
                                        t.x + t.width / 2 - 2, ruleY - 2, 5, 5);
            gem.color = _theme.gold;
            gem.raycastTarget = false;
        }

        private void BuildRail()
        {
            var r = _frame.Rail;

            var railGo = new GameObject("Rail", typeof(RectTransform));
            railGo.transform.SetParent(_pixels, false);
            _railRoot = (RectTransform)railGo.transform;

            // Full band HERE, and resized to its rows in RebuildRail.
            //
            // The hole must be as tall as the ROWS, not as tall as the column — nine rows of
            // eighteen with a texel between them is 170 texels against a band of 190, and
            // drawing the recess to the band put twenty texels of empty cut-out under the last
            // school, which reads as a list that failed to finish loading rather than as one
            // that ended.
            //
            // Sizing it here does not work, and that was the first attempt: EnsureBuilt runs
            // before anything hands this panel its progression, so `grimoire` is null on this
            // line and the count comes back zero. It measured live as a rail still 190 tall
            // with the void intact — a fix that compiled, passed, and changed nothing on
            // screen. RebuildRail is the first place the schools exist.
            //
            // But it must still be PLACED here, at the full band. The first cut moved the
            // sizing out and deleted the placement with it, so the rect sat at Unity's default
            // 100x100 until RebuildRail ran — and `GrimoireChromeTests.NoRectIsLeftAtUnitysDefault`
            // failed on exactly that. It is the right thing to fail on: a window that only
            // looks right once its data arrives is a window that is wrong for every frame
            // before it, and for every path that builds it without data at all.
            HudRect.Place(_railRoot, r.x, r.y, r.width, r.height);

            // A HOLE, not a darker rectangle. The bevel is inverted — shade along the top and
            // left, light along the bottom and right — which is the only thing that separates
            // a recess from a flat panel at these tones.
            var recess = railGo.AddComponent<Image>();
            recess.sprite = _ink.Recess;
            recess.type = Image.Type.Sliced;
            recess.color = _theme.recess;
            recess.raycastTarget = false;
        }

        private void BuildBoard()
        {
            // The constellation gives up the bottom of its column to the role filter. It is
            // the board's own row — it filters the board, not the window — and the room was
            // already there: measured, every shipped school fills 43-65 % of this column's
            // height.
            var b = BoardViewportRect();

            var vpGo = new GameObject("BoardViewport", typeof(RectTransform));
            vpGo.transform.SetParent(_pixels, false);
            _boardViewport = (RectTransform)vpGo.transform;
            HudRect.Place(_boardViewport, b.x, b.y, b.width, b.height);

            var recess = vpGo.AddComponent<Image>();
            recess.sprite = _ink.Recess;
            recess.type = Image.Type.Sliced;
            recess.color = _theme.recess;
            recess.raycastTarget = false;
            // A school too tall to fit is scaled down, never cropped — but a mask is what
            // makes that a guarantee rather than a hope.
            vpGo.AddComponent<RectMask2D>();

            // The constellation sits in a soft light so the board has a CENTRE and the two
            // columns beside it recede. Lit flat, the only thing the eye could find in the
            // whole window was the one node carrying a halo.
            var glow = HudRect.MakeImage("BoardLight", vpGo.transform, _ink.Glow,
                                         0, 0, b.width, b.height);
            // 0.55, not 0.30. At a third the glow was present in the hierarchy and absent on
            // screen: the eye still went straight to the one node carrying a halo, which is
            // the symptom of a board with no centre of its own.
            glow.color = new Color(_theme.stoneLight.r, _theme.stoneLight.g,
                                   _theme.stoneLight.b, 0.55f);

            var boardGo = new GameObject("Board", typeof(RectTransform));
            boardGo.transform.SetParent(_boardViewport, false);
            _boardRoot = (RectTransform)boardGo.transform;
            _boardRoot.anchorMin = _boardRoot.anchorMax = new Vector2(0.5f, 0.5f);
            _boardRoot.pivot = new Vector2(0.5f, 0.5f);
            _boardRoot.anchoredPosition = Vector2.zero;
            _boardRoot.sizeDelta = Vector2.zero;
        }

        /// <summary>
        /// The board's drawable rect: its column minus the filter row under it. Declared as a
        /// function so the fit arithmetic and the viewport cannot disagree about how much room
        /// the constellation actually has.
        /// </summary>
        private RectInt BoardViewportRect()
        {
            var b = _frame.Board;
            int reserved = _style.filterChipTexels + _style.filterChipGapTexels;
            return new RectInt(b.x, b.y + reserved, b.width, Mathf.Max(10, b.height - reserved));
        }

        private void BuildCard()
        {
            var c = _frame.Card;
            var cardGo = new GameObject("Card", typeof(RectTransform));
            cardGo.transform.SetParent(_pixels, false);
            _cardRoot = (RectTransform)cardGo.transform;
            HudRect.Place(_cardRoot, c.x, c.y, c.width, c.height);

            var recess = cardGo.AddComponent<Image>();
            recess.sprite = _ink.Recess;
            recess.type = Image.Type.Sliced;
            recess.color = _theme.recess;
            recess.raycastTarget = false;

            BuildCardContents();
        }

        private void BuildMotes()
        {
            var style = PlayerHudStyle.Active;
            var shader = _style.hudFxShader != null
                ? _style.hudFxShader
                : (style.hudFxShader != null ? style.hudFxShader : Shader.Find("Valkur/UI/HudFx"));
            if (shader != null)
                _additive = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };

            _motes = HudMoteLayer.Create(_pixels, _art, _style.moteCapacity, _additive);
        }

        // ── Open motion (R7: a fade, never a jump) ────────────────────────

        private void BeginOpenMotion()
        {
            _opening = true;
            _openLeft = Mathf.Max(0.0001f, _style.openSeconds);
            ApplyOpenMotion(0f);
        }

        private void TickOpenMotion(float dt)
        {
            if (!_opening) return;
            _openLeft -= dt;
            float t = 1f - Mathf.Clamp01(_openLeft / Mathf.Max(0.0001f, _style.openSeconds));
            ApplyOpenMotion(t);
            if (_openLeft <= 0f) _opening = false;
        }

        private void ApplyOpenMotion(float t)
        {
            // A rise of a few whole texels plus an alpha ramp. The rise is ROUNDED, so the
            // panel never lands between texels on its way in.
            int rise = Mathf.RoundToInt(Mathf.Lerp(_style.openRiseTexels, 0f, t));
            _pixels.anchoredPosition = new Vector2(0f, rise);

            var group = _panel.GetComponent<CanvasGroup>();
            if (group == null) group = _panel.gameObject.AddComponent<CanvasGroup>();
            group.alpha = Mathf.Lerp(0f, 1f, t);
        }

        // ── R12: the HUD also dies ────────────────────────────────────────

        /// <summary>
        /// In spirit form the world goes grey and every panel follows
        /// (HUD_VISUAL_LANGUAGE.md R12). A window still in colour while the world is not is a
        /// window that does not know what has happened — and the grimoire is a window the
        /// player CAN open as a spirit, so it is not a hypothetical.
        ///
        /// <para>Driven off <c>Health.IsDead</c> rather than off <c>PlayerSpiritState</c>, the
        /// same source the player panel and the music plaque use: the spirit component is added
        /// by the death flow and a panel that queried it would be reading a component that does
        /// not exist yet on the frame it opens.</para>
        /// </summary>
        private void TickSpirit(float dt)
        {
            if (_stone == null) return;

            if (_playerHealth == null)
            {
                var player = EntityRegistry.PlayerTransform;
                if (player != null) _playerHealth = player.GetComponent<Health>();
            }

            bool dead = _playerHealth != null && _playerHealth.IsDead;
            float next = Mathf.MoveTowards(_spiritT, dead ? 1f : 0f, dt * 2f);
            if (Mathf.Approximately(next, _spiritT)) return;

            _spiritT = next;
            var tint = Color.Lerp(Color.white, PlayerHudStyle.Active.spiritStone, _spiritT);
            _stone.color = _theme.stoneDark * tint;
        }

        private Health _playerHealth;

        /// <summary>How grey the panel currently is. Test seam.</summary>
        public float SpiritAmount => _spiritT;

        // ── Small helpers ─────────────────────────────────────────────────

        private Text MakeText(Transform parent, string name, int x, int y, int w, int h,
                              int size, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            HudRect.Place((RectTransform)go.transform, x, y, w, h);

            var text = go.AddComponent<Text>();
            text.font = _font;
            text.fontSize = size;
            text.alignment = anchor;
            text.raycastTarget = false;
            // Never silently swallow the end of a sentence: the reason a node is shut is the
            // one string in this window worth reading, and the panel this replaced cut it.
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        /// <summary>
        /// <c>Object.Destroy</c> is an ERROR in Edit Mode, not a warning, and every fixture
        /// that builds this panel reaches a rebuild. Same branch the Entities and Buildings
        /// editors carry.
        /// </summary>
        private static void ClearChildren(Transform parent)
        {
            if (parent == null) return;
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }
        }
    }
}
