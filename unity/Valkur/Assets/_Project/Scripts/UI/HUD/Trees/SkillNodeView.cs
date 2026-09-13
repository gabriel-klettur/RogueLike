using UnityEngine;
using UnityEngine.UI;
using Valkur.Data;
using Valkur.Gameplay.HUD;

namespace Valkur.UI.HUD
{
    /// <summary>The six things a talent node can be. Each carries a SHAPE, never only a colour.</summary>
    public enum SkillNodeState
    {
        /// <summary>Every rank bought. Gold rim, pips full.</summary>
        Maxed = 0,

        /// <summary>Bought, with ranks left. Light stone rim, pips part-full.</summary>
        Partial = 1,

        /// <summary>Buyable right now. Bright inner edge — STATIC, never a pulse.</summary>
        Available = 2,

        /// <summary>A prerequisite is unfinished. Icon dimmed, the incoming edge dark.</summary>
        LockedByPrerequisite = 3,

        /// <summary>Below the level gate. A padlock with the number.</summary>
        LockedByLevel = 4,

        /// <summary>Reachable but unaffordable. Looks available; the cost says why.</summary>
        LockedByPoints = 5,
    }

    /// <summary>
    /// One talent on the board: a socket, a baked icon, a row of rank pips and a name.
    ///
    /// <para><b>Why not <c>HudAbilitySlot</c> directly.</b> That component is the ability
    /// casting slot — it resolves a live binding, reads a cooldown clock and owns a five-state
    /// machine about mana. A talent has none of those and has a rank instead, which is the one
    /// thing the ability slot cannot show. What the two DO share is the drawn socket, and they
    /// share it literally: both take <c>HudArt.Slot</c> and <c>HudArt.SlotGlow</c> out of the
    /// same generated atlas, so they are the same object on screen without pretending to be the
    /// same object in code.</para>
    ///
    /// <para><b>Availability is a static edge, not an animation.</b> Three of seven nodes are
    /// buyable on a typical open; a glow that pulsed on each of them would leave half the board
    /// moving permanently, and R8's whole argument is that a HUD which shines without cause stops
    /// warning when there is one.</para>
    ///
    /// <para><b>Every pip is a whole texel and the row is centred on one.</b> An odd pip count in
    /// an even column would otherwise put the whole row half a texel off the socket above it.</para>
    /// </summary>
    public sealed class SkillNodeView
    {
        private readonly RectTransform _root;
        private readonly Image _socket;
        private readonly Image _rim;
        private readonly RawImage _icon;
        private readonly Image _sigil;
        private readonly Image _lock;
        private readonly HudPixelText _lockLevel;
        private readonly HudPixelText _name;
        private readonly HudPixelText _cost;
        private readonly Image[] _pipFrames;
        private readonly Image[] _pipFills;
        private readonly Button _button;

        private readonly HudArt _art;
        private readonly HudTheme _theme;
        private readonly SkillsHudStyle _style;

        private Texture2D _bakedTexture;
        private int _pixelScale = 1;
        private int _rank;
        private int _snapPip = -1;
        private float _snapUntil;
        private float _snapBaseY;

        public SkillNode Node { get; }
        public SkillNodeState State { get; private set; }
        public RectTransform Root => _root;

        /// <summary>Centre of the socket in board texels. Where a mote burst starts.</summary>
        public Vector2 SocketCentre { get; }

        /// <summary>Raised when the player clicks the node. The board decides what that means.</summary>
        public System.Action<SkillNodeView> Clicked;

        public SkillNodeView(SkillNode node, SkillNodePlacement placement, Transform parent,
                             HudArt art, HudTheme theme, SkillsHudStyle style, int pixelScale)
        {
            Node = node;
            _art = art;
            _theme = theme;
            _style = style;
            _pixelScale = Mathf.Max(1, pixelScale);
            SocketCentre = new Vector2(placement.CentreX, placement.CentreY);

            int size = SkillTreeLayout.NodeSize;
            _root = HudRect.Make("Node_" + node.skillId, parent,
                                 placement.CellX, placement.Y - SkillTreeLayout.CellFooterHeight,
                                 placement.CellWidth, size + SkillTreeLayout.CellFooterHeight);

            int socketX = (placement.CellWidth - size) / 2;
            int socketY = SkillTreeLayout.CellFooterHeight;

            _socket = Tinted("Socket", _root, art.Slot, socketX, socketY, size, size,
                             theme.stoneDark, Image.Type.Sliced);
            _socket.raycastTarget = true;
            _button = _socket.gameObject.AddComponent<Button>();
            _button.transition = Selectable.Transition.None;
            _button.onClick.AddListener(() => Clicked?.Invoke(this));

            int inset = Mathf.Max(1, style.iconInsetTexels);
            var iconRt = HudRect.Make("Icon", _socket.rectTransform, inset, inset,
                                      size - inset * 2, size - inset * 2);
            _icon = iconRt.gameObject.AddComponent<RawImage>();
            _icon.raycastTarget = false;
            _icon.enabled = false;

            // A node with no authored icon shows a generated sigil rather than an empty socket
            // (R10). All 35 shipped nodes are in that state today, so this is the normal path,
            // not the fallback.
            _sigil = Tinted("Sigil", _socket.rectTransform, SigilFor(node),
                            inset, inset, size - inset * 2, size - inset * 2, theme.textDim);

            _rim = Tinted("Rim", _socket.rectTransform, art.SlotGlow, 0, 0, size, size,
                          Clear(theme.gold), Image.Type.Sliced);

            _lock = Tinted("Lock", _socket.rectTransform, art.Lock,
                           size / 2 - 4, size / 2 - 4, 8, 8, theme.textDisabled);
            _lock.enabled = false;

            _lockLevel = HudPixelText.Create(_socket.rectTransform, "LockLevel", art,
                                             HudFontFace.Small, HudTextAlign.Centre,
                                             0, 3, size, 7);
            _lockLevel.color = theme.textDim;
            _lockLevel.gameObject.SetActive(false);

            int maxRank = Mathf.Max(1, node.maxRank);
            _pipFrames = new Image[maxRank];
            _pipFills = new Image[maxRank];
            BuildPips(maxRank, placement.CellWidth, socketY);

            // Three rows under the socket, and they may not overlap: pips (3), cost (7), name (7)
            // inside CellFooterHeight. The first cut gave them 18 texels for 17 of content and
            // the cost printed through the name.
            _cost = HudPixelText.Create(_root, "Cost", art, HudFontFace.Small, HudTextAlign.Centre,
                                        0, 9, placement.CellWidth, 7);
            _cost.color = theme.textDim;

            _name = HudPixelText.Create(_root, "Name", art, HudFontFace.Small, HudTextAlign.Centre,
                                        0, 0, placement.CellWidth, 7);
            _name.color = theme.text;
            _name.SetText(node.displayName == null ? string.Empty : node.displayName.ToUpperInvariant());
        }

        // ── Pips ──────────────────────────────────────────────────────────────

        private void BuildPips(int maxRank, int cellWidth, int socketY)
        {
            int pip = Mathf.Max(2, _style.pipTexels);
            int gap = Mathf.Max(1, _style.pipGapTexels);
            int rowWidth = maxRank * pip + (maxRank - 1) * gap;
            int x0 = (cellWidth - rowWidth) / 2;   // integer division: the row lands on a whole texel
            int y = socketY - _style.pipTopGapTexels - pip;

            for (int i = 0; i < maxRank; i++)
            {
                int x = x0 + i * (pip + gap);
                _pipFrames[i] = Tinted("PipFrame" + i, _root, _art.PipFrame, x, y, pip, pip,
                                       _theme.recess);
                _pipFills[i] = Tinted("PipFill" + i, _root, _art.PipFill, x, y, pip, pip, _theme.gold);
                _pipFills[i].enabled = false;
            }
        }

        // ── State ─────────────────────────────────────────────────────────────

        /// <summary>Repaints the node for a rank and a state. Nothing else writes its colours.</summary>
        public void SetState(SkillNodeState state, int rank, int pointCost, int levelRequired)
        {
            State = state;
            _rank = rank;

            bool bought = rank > 0;
            bool locked = state == SkillNodeState.LockedByPrerequisite ||
                          state == SkillNodeState.LockedByLevel;

            _socket.color = bought ? _theme.stoneLight : _theme.stoneDark;

            // The rim is the one place gold appears on a node, and only when every rank is held:
            // gold is importance, and a half-bought node is not yet an achievement.
            Color rim = state == SkillNodeState.Maxed ? _theme.gold
                      : state == SkillNodeState.Available ? _theme.text
                      : state == SkillNodeState.LockedByPoints ? _theme.textDim
                      : Clear(_theme.gold);
            if (state == SkillNodeState.Partial) rim = _theme.stoneLight;
            _rim.color = rim;

            // The sigil carries the stat's own colour — the same table the purchase motes use, so
            // the burst that comes out of a node is the colour of the thing already drawn on it.
            // Locked dims it; bought brightens it; neither changes the hue.
            Color tint = StatColour(Node, _theme);
            _sigil.color = locked ? Dim(tint, 0.35f) : bought ? tint : Dim(tint, 0.75f);
            if (_icon.enabled) _icon.color = locked ? new Color(1f, 1f, 1f, 0.35f) : Color.white;

            _lock.enabled = state == SkillNodeState.LockedByLevel;
            _lockLevel.gameObject.SetActive(state == SkillNodeState.LockedByLevel);
            if (state == SkillNodeState.LockedByLevel) _lockLevel.SetText(levelRequired.ToString());

            _name.color = locked ? _theme.textDisabled : _theme.text;

            // The cost is the one row that turns red, and only when THAT is the gate. A node the
            // player cannot afford looks exactly like one they can, which is deliberate: the
            // shape says "reachable" and the number says "not yet".
            _cost.SetText(state == SkillNodeState.Maxed ? SkillText.Maxed
                                                        : SkillText.PointsShort(pointCost));
            _cost.color = state == SkillNodeState.LockedByPoints ? _theme.danger
                        : state == SkillNodeState.Maxed ? _theme.gold
                        : _theme.textDim;

            for (int i = 0; i < _pipFills.Length; i++)
            {
                bool full = i < rank;
                _pipFills[i].enabled = full;
                _pipFills[i].color = state == SkillNodeState.Maxed ? _theme.gold : _theme.goldShade;
            }
        }

        /// <summary>Pops the pip that was just bought one texel proud of its row.</summary>
        public void SnapPip(int index, float now)
        {
            if (index < 0 || index >= _pipFills.Length) return;
            // The base is captured on the way IN, and any live snap is settled first, so a second
            // purchase while the first is still lifted cannot bake the one-texel lift into the
            // resting position and walk the pip row up the board.
            if (_snapPip >= 0 && _snapPip != index) Tick(float.MaxValue);
            _snapPip = index;
            _snapBaseY = Mathf.Round(_pipFills[index].rectTransform.anchoredPosition.y);
            _snapUntil = now + Mathf.Max(0.02f, _style.pipSnapSeconds);
        }

        /// <summary>
        /// Advances the pip snap. The ONLY thing on a node that moves at rest — and it does not
        /// move at rest either, because it expires.
        /// </summary>
        public void Tick(float now)
        {
            if (_snapPip < 0) return;
            bool live = now < _snapUntil;
            var rt = _pipFills[_snapPip].rectTransform;
            float wanted = _snapBaseY + (live ? 1f : 0f);
            if (!Mathf.Approximately(rt.anchoredPosition.y, wanted))
                rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, wanted);
            if (!live) _snapPip = -1;
        }

        /// <summary>Bakes the node's authored icon at the exact pixel size of its socket (R10).</summary>
        public void ApplyIcon(int pixelScale)
        {
            _pixelScale = Mathf.Max(1, pixelScale);
            if (Node.icon == null) { _sigil.enabled = true; _icon.enabled = false; return; }
            if (!HudTextureBaker.CanBake) { _sigil.enabled = true; _icon.enabled = false; return; }

            int inner = SkillTreeLayout.NodeSize - Mathf.Max(1, _style.iconInsetTexels) * 2;
            var baked = HudTextureBaker.Icon(Node.icon, inner * _pixelScale);
            if (baked == null) { _sigil.enabled = true; _icon.enabled = false; return; }

            DestroyBaked();
            _bakedTexture = baked;
            _icon.texture = baked;
            _icon.enabled = true;
            _sigil.enabled = false;
        }

        /// <summary>Releases the baked texture. Called by the board; Unity will not do it.</summary>
        public void Dispose()
        {
            DestroyBaked();
        }

        private void DestroyBaked()
        {
            if (_bakedTexture == null) return;
            if (Application.isPlaying) Object.Destroy(_bakedTexture);
            else Object.DestroyImmediate(_bakedTexture);
            _bakedTexture = null;
        }

        // ── Sigil ─────────────────────────────────────────────────────────────

        /// <summary>
        /// The generated stand-in for a node with no art (R10), and the normal path today: all 35
        /// shipped nodes have <c>icon: {fileID: 0}</c> and <c>SkillNode.icon</c> has no reader
        /// anywhere in the project.
        ///
        /// <para><b>Three shapes and no more, on purpose.</b> The shared atlas has a heart and a
        /// drop and nothing that honestly means "defence" or "melee damage"; pressing an unrelated
        /// glyph into that job teaches the player a symbol that means something else elsewhere —
        /// the padlock in particular is already "locked" on this very board. What separates the
        /// rest is the COLOUR, which comes from the same <c>StatColour</c> table the purchase
        /// motes use, so shape and hue are two honest axes instead of one invented one. Real art
        /// replaces all of it a node at a time, without touching this code.</para>
        /// </summary>
        private Sprite SigilFor(SkillNode node)
        {
            switch (DominantStat(node))
            {
                case StatKind.MaxHp:
                    return _art.Heart;
                case StatKind.MaxMana:
                case StatKind.ManaRegen:
                case StatKind.ManaCostReduction:
                    return _art.Drop;
                default:
                    return _art.MotePlus;
            }
        }

        /// <summary>
        /// The stat a node moves FIRST, which is the one its name is about. A node with no
        /// modifier at all is an aura node; it takes the default shape and the default colour.
        /// </summary>
        internal static StatKind DominantStat(SkillNode node)
        {
            if (node == null || node.modifiersPerRank == null || node.modifiersPerRank.Length == 0)
                return StatKind.SpellPower;
            return node.modifiersPerRank[0].stat;
        }

        /// <summary>
        /// The colour of the stat a node moves most, from R6's own table: a talent that raises
        /// health is the green the health bar uses and one that raises mana is the mana blue.
        /// Anything else is gold, which is what this window already means by "worth having".
        ///
        /// <para>Shared with the mote layer so the sigil drawn on a node and the burst thrown out
        /// of it cannot be different colours — two answers to "what does this do".</para>
        /// </summary>
        internal static Color StatColour(SkillNode node, HudTheme theme)
        {
            switch (DominantStat(node))
            {
                case StatKind.MaxHp:              return theme.success;
                case StatKind.MaxMana:
                case StatKind.ManaRegen:
                case StatKind.ManaCostReduction:  return theme.info;
                case StatKind.Defense:            return theme.textDim;
                default:                          return theme.gold;
            }
        }

        /// <summary>
        /// An image placed on whole texels and painted in one call. <c>HudRect.MakeImage</c>
        /// takes an <c>Image.Type</c> in the slot a colour looks like it should go, which is an
        /// easy call to get silently wrong.
        /// </summary>
        private static Image Tinted(string name, Transform parent, Sprite sprite, int x, int y,
                                    int w, int h, Color colour,
                                    Image.Type type = Image.Type.Simple)
        {
            var img = HudRect.MakeImage(name, parent, sprite, x, y, w, h, type);
            img.color = colour;
            return img;
        }

        private static Color Clear(Color c) => new Color(c.r, c.g, c.b, 0f);
        private static Color Dim(Color c, float a) => new Color(c.r, c.g, c.b, c.a * a);
    }
}
