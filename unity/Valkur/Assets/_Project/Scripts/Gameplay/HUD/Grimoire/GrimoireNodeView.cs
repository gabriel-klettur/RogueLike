using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Valkur.Data;
using Valkur.Gameplay.Spells;
using Valkur.UI.HUD;

namespace Valkur.Gameplay.HUD
{
    /// <summary>
    /// One node on the constellation: halo, socket, plate, icon, and the two caption lines
    /// under it — its name, and what stands between the player and it.
    ///
    /// <para><b>The scaffold is shared, not copied.</b> Socket, plate, halo and the role mark
    /// come from <see cref="SpellGraphSprites"/>, which the Spells editor's graph already
    /// generates and which lives in this same assembly. They are luminance maps — white with
    /// a varying alpha, never a hue — precisely so ONE socket can serve a bought node, an
    /// available one and a locked one, and so each school can tint its own constellation
    /// without nine sets of textures.</para>
    ///
    /// <para><b>The icon is BAKED to the socket's pixel size.</b> The 73 shipped spell icons
    /// are 320 and 1024 px, bilinear, with no mipmaps, inside an atlas; drawn straight into a
    /// ~28 px slot they alias. <see cref="HudTextureBaker.Icon"/> halves them on the GPU down
    /// to the size actually drawn — the same answer the player panel's ability slots reached
    /// (R10).</para>
    /// </summary>
    public sealed class GrimoireNodeView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private const int SLOT_HALO = 0;
        private const int SLOT_SOCKET = 1;
        private const int SLOT_PLATE = 2;
        private const int SLOT_ICON = 3;
        private const int SLOT_MARK = 4;

        private RectTransform _rt;
        private Image _halo;
        private Image _socket;
        private Image _plate;
        private Image _icon;
        private Image _roleMark;
        private Image _lockMark;
        private GrimoireCaption _name;
        private GrimoireCaption _reason;
        private int _captionWidth;
        private Button _button;

        private GrimoireNodeState _state;
        private GrimoireStyle _style;
        private Color _accent;
        private float _fade = 1f;
        private float _pulsePhase;
        private float _pulsePeriod;
        private bool _pulsing;
        private float _shake;
        private float _shakeSeconds;
        private Vector2 _home;

        public SpellNode Node { get; private set; }
        public GrimoireNodeState State => _state;
        public RectTransform Rect => _rt;

        /// <summary>Board-local centre, in board pixels. What a link aims at.</summary>
        public Vector2 Centre => _home;

        /// <summary>Fires when the player clicks this node — selection, never a purchase.</summary>
        public event Action<GrimoireNodeView> Clicked;

        /// <summary>Fires on hover, so the card can preview without committing a selection.</summary>
        public event Action<GrimoireNodeView> Hovered;

        /// <summary>Fires when the pointer leaves, so the card can go back to the selection.</summary>
        public event Action<GrimoireNodeView> Unhovered;

        public static GrimoireNodeView Create(Transform parent, SpellNode node, float diameter,
                                              float captionHeight, float captionWidth, HudArt art,
                                              GrimoireStyle style)
        {
            var go = new GameObject("Node_" + (node != null ? node.nodeId : "null"),
                                    typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var view = go.AddComponent<GrimoireNodeView>();
            view.Node = node;
            view._style = style;
            view._rt = (RectTransform)go.transform;
            view._rt.sizeDelta = new Vector2(diameter, diameter);

            // Every layer is centred on the node and sized off the one diameter, so retuning
            // nodeTexels moves the whole rig rather than half of it.
            view._halo = Layer(go.transform, "Halo", SpellGraphSprites.Glow,
                               diameter * SpellGraphGeometry.HALO_SCALE, SLOT_HALO);
            view._socket = Layer(go.transform, "Socket", SpellGraphSprites.Socket,
                                 diameter, SLOT_SOCKET);
            view._plate = Layer(go.transform, "Plate", SpellGraphSprites.Plate,
                                diameter * 0.74f, SLOT_PLATE);
            view._icon = Layer(go.transform, "Icon", null, diameter * 0.62f, SLOT_ICON);
            view._roleMark = Layer(go.transform, "Role", null, diameter * 0.30f, SLOT_MARK);

            // WHY it is shut, as a shape, at the socket's other notch. All three locked
            // states were the same dim socket, so the board said "not yet" three different
            // times in one voice — colour cannot separate them and neither can one shape
            // (R6). The role lives bottom-right; this lives bottom-left, so the two never
            // compete for the same corner.
            // TWICE its authored five, not a fraction of the socket. At 0.26 of a 26-texel node
            // it landed on 6.8 — a point-filtered draw at 1.35x repeats some rows of a 5x5 mark
            // and not others, so a symmetric glyph comes out lopsided and small at once.
            view._lockMark = Layer(go.transform, "Lock", null, 10f, SLOT_MARK);
            view._lockMark.rectTransform.anchoredPosition =
                new Vector2(-diameter * 0.34f, -diameter * 0.34f);

            // The mark sits at the socket's lower-right notch: it says what the spell is FOR,
            // which is the second question after "what is it", so it must not sit on the icon.
            var markRt = view._roleMark.rectTransform;
            markRt.anchoredPosition = new Vector2(diameter * 0.34f, -diameter * 0.34f);

            // Only the socket takes the click. A halo nearly twice the node's width would make
            // two neighbours fight over the same pixels.
            view._socket.raycastTarget = true;
            view._button = go.AddComponent<Button>();
            view._button.targetGraphic = view._socket;
            view._button.transition = Selectable.Transition.None;
            view._button.onClick.AddListener(() => view.Clicked?.Invoke(view));

            // The NAME gets two lines and the reason one. A spell name is authored prose —
            // "Scatter Volley" is fourteen characters against a caption 46 texels wide — so at
            // one line it wrapped anyway and the overflow landed on the reason under it. Two
            // lines is the honest amount of room; the sibling spacing is sized for three lines
            // in total (GrimoireGeometry.SiblingSpacing) so the block never reaches the node
            // below.
            float nameCentre = diameter * 0.5f + 2f + captionHeight;
            float reasonCentre = diameter * 0.5f + 2f + captionHeight * 2f + 1f + captionHeight * 0.5f;

            // The ceiling is the LINE height, not a taste value: a two-line box 2h tall can
            // only hold two lines if each is at most h. Left at 11 against a 7-texel line the
            // name's second line spilled past its own box and touched the reason under it —
            // the box arithmetic was right and the font was what broke it.
            int line = Mathf.Max(4, Mathf.RoundToInt(captionHeight));
            view._captionWidth = Mathf.Max(8, Mathf.RoundToInt(captionWidth));

            view._name = GrimoireCaption.Create(go.transform, art, view._captionWidth,
                                                line, -nameCentre, 2);
            view._reason = GrimoireCaption.Create(go.transform, art, view._captionWidth,
                                                  line, -reasonCentre, 1);

            return view;
        }

        /// <summary>Places the node at its board position and remembers it, so a refusal can
        /// shake around a point rather than drift away from one.</summary>
        public void Place(Vector2 boardPosition)
        {
            _home = boardPosition;
            _rt.anchoredPosition = boardPosition;
        }

        /// <summary>
        /// Paints the node for its state. Called on every refresh, so it must be a pure
        /// function of (state, accent, strings) — anything remembered here is a way for two
        /// refreshes to disagree.
        /// </summary>
        public void Paint(GrimoireNodeState state, Color accent, Sprite icon, string name,
                          string reason, HudTheme theme)
        {
            _state = state;
            _accent = accent;

            bool filled = GrimoireNodeStatus.IsFilled(state);
            bool lit = GrimoireNodeStatus.IsIconLit(state);

            _socket.sprite = filled ? SpellGraphSprites.SocketCapstone : SpellGraphSprites.Socket;
            _socket.color = filled ? accent : Dim(accent, _style.lockedSocketFactor);

            _plate.color = filled
                ? new Color(accent.r * _style.learnedPlateFactor,
                            accent.g * _style.learnedPlateFactor,
                            accent.b * _style.learnedPlateFactor, _style.learnedPlateAlpha)
                : new Color(theme.recess.r, theme.recess.g, theme.recess.b,
                            _style.lockedPlateAlpha);

            _icon.sprite = icon;
            _icon.enabled = icon != null;
            // A locked node's art is drained rather than hidden: the player should be able to
            // recognise the spell they are saving for.
            _icon.color = lit ? Color.white : _style.lockedIconTint;

            var ink = GrimoireArt.Get();
            switch (state)
            {
                case GrimoireNodeState.NeedsLevel:        _lockMark.sprite = ink.MarkLevel; break;
                case GrimoireNodeState.NeedsPrerequisite: _lockMark.sprite = ink.MarkChain; break;
                case GrimoireNodeState.NeedsPoints:       _lockMark.sprite = ink.MarkPurse; break;
                default:                                  _lockMark.sprite = null;          break;
            }
            _lockMark.enabled = _lockMark.sprite != null;
            _lockMark.color = _style.lockedIconTint;

            if (Node != null)
            {
                _roleMark.sprite = SpellGraphSprites.Mark(Node.role);
                _roleMark.enabled = _roleMark.sprite != null;
                _roleMark.color = lit ? Dim(accent, _style.roleMarkLitFactor)
                                      : Dim(accent, _style.roleMarkDimFactor);
            }
            else
            {
                _roleMark.enabled = false;
            }

            // Upper-cased because the small face is capitals only and its Spanish glyphs ARE
            // the capitals — the same call the talents tab makes for its own node names.
            _name.SetText(name == null ? string.Empty : name.ToUpperInvariant(), _captionWidth);
            _name.SetColour(filled ? theme.text : (lit ? theme.text : theme.textDisabled));

            _reason.SetText(reason == null ? string.Empty : reason.ToUpperInvariant(),
                            _captionWidth);
            _reason.SetColour(state == GrimoireNodeState.Available ? accent : theme.textDim);

            // The ONE thing on the board that moves while nothing is happening, and only on
            // the node the player can act on. Everything else is still (R7).
            _pulsing = state == GrimoireNodeState.Available;
            _halo.color = _pulsing
                ? new Color(accent.r, accent.g, accent.b, _style.haloBreathHigh * 0.8f)
                : new Color(accent.r, accent.g, accent.b,
                            filled ? _style.haloLearnedAlpha : 0f);
            _halo.enabled = _halo.color.a > 0.001f;
            ApplyFade();
        }

        /// <summary>
        /// How strongly this node is drawn, for the role filter. It FADES rather than hides:
        /// a tree missing branches does not read as a tree, and a node the player filtered out
        /// is still a node they may be planning around. Same call the Controls editor makes
        /// for its own search.
        /// </summary>
        public void SetFade(float fade)
        {
            fade = Mathf.Clamp01(fade);
            if (Mathf.Approximately(fade, _fade)) return;
            _fade = fade;
            ApplyFade();
        }

        private void ApplyFade()
        {
            var group = GetComponent<CanvasGroup>();
            if (group == null) group = gameObject.AddComponent<CanvasGroup>();
            group.alpha = _fade;
            // A faded node must not eat the click meant for the one behind the pointer.
            group.blocksRaycasts = _fade > 0.5f;
        }

        /// <summary>Arms the slow breath. Period comes from the style so it is authored once.</summary>
        public void SetPulsePeriod(float seconds) => _pulsePeriod = Mathf.Max(0.2f, seconds);

        /// <summary>
        /// A refused click shakes the socket by a texel and emits NOTHING. A mote on a refusal
        /// teaches the player to ignore motes — the rule the player panel already follows.
        /// </summary>
        public void Refuse(float seconds, float texels)
        {
            _shakeSeconds = Mathf.Max(0.01f, seconds);
            _shake = _shakeSeconds;
            _shakeAmplitude = texels;
        }

        private float _shakeAmplitude;

        /// <summary>Driven by the panel rather than by its own Update, so a hidden board costs
        /// nothing and an EditMode test can advance it without a clock.</summary>
        public void Tick(float dt)
        {
            if (_pulsing && _pulsePeriod > 0f)
            {
                _pulsePhase += dt / _pulsePeriod;
                if (_pulsePhase > 1f) _pulsePhase -= 1f;
                float breath = 0.5f + 0.5f * Mathf.Sin(_pulsePhase * Mathf.PI * 2f);
                _halo.color = new Color(_accent.r, _accent.g, _accent.b,
                    Mathf.Lerp(_style.haloBreathLow, _style.haloBreathHigh, breath) * _fade);
            }

            if (_shake > 0f)
            {
                _shake -= dt;
                float t = Mathf.Max(0f, _shake) / _shakeSeconds;
                float offset = Mathf.Round(Mathf.Sin(t * 40f) * _shakeAmplitude * t);
                _rt.anchoredPosition = _home + new Vector2(offset, 0f);
                if (_shake <= 0f) _rt.anchoredPosition = _home;
            }
        }

        public void OnPointerEnter(PointerEventData eventData) => Hovered?.Invoke(this);

        public void OnPointerExit(PointerEventData eventData) => Unhovered?.Invoke(this);

        // ── Build helpers ────────────────────────────────────────────────────

        private static Image Layer(Transform parent, string name, Sprite sprite, float size,
                                   int siblingIndex)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.transform.SetSiblingIndex(siblingIndex);

            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = Vector2.zero;

            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.raycastTarget = false;
            img.preserveAspect = true;
            return img;
        }

        private static Color Dim(Color c, float factor) =>
            new Color(c.r * factor, c.g * factor, c.b * factor, 1f);
    }
}
