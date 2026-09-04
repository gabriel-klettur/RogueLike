using UnityEngine;
using UnityEngine.UI;
using Valkur.Data;

namespace Valkur.Gameplay.Chat
{
    /// <summary>
    /// The character's face, in a gutter down the left of the chat panel.
    ///
    /// <para>WHY IT FLOATS RATHER THAN BEING A ROW. The panel is a
    /// <see cref="VerticalLayoutGroup"/> and it overwrites the RectTransform of every child
    /// it owns — the language button once came out 504x0 that way, an invisible full-width
    /// strip eating the close button's clicks. So the portrait is <c>ignoreLayout</c> and
    /// anchored to the panel's top-left corner, exactly like the corner controls, and the
    /// space it occupies is reserved by widening the layout group's LEFT PADDING. Every
    /// existing row then gets shorter by itself, with no row re-parented and no second
    /// layout group.</para>
    ///
    /// <para>THE GUTTER IS NOT ALWAYS THERE. A character with no face art keeps the panel
    /// exactly as it was — reserving the space anyway would put an empty rectangle beside
    /// five of the six conversations in the game, which reads as a portrait that failed to
    /// load. <see cref="PANEL_MIN_W"/> is deliberately NOT raised to make room: at the
    /// minimum width the conversation gives up the space instead, which is the same trade
    /// <see cref="SCROLL_MIN_H"/> makes when the trade confirmation row appears.</para>
    ///
    /// <para>THE SWAP IS A CROSSFADE, not a cut. A sprite replaced in one frame reads as a
    /// glitch rather than as a change of expression — the same reason
    /// <c>WeaponSwapFlashFX</c> exists to cover the loadout swap — so the outgoing face is
    /// held on a second Image underneath and the two are dissolved across
    /// <see cref="PORTRAIT_FADE_SEC"/>.</para>
    /// </summary>
    public partial class ChatUI
    {
        private GameObject _portraitRoot;
        private Image _portraitFrame;

        /// <summary>The face arriving. Fades in from 0.</summary>
        private Image _portraitFront;

        /// <summary>The face leaving. Holds the previous sprite and fades out to 0.</summary>
        private Image _portraitBack;

        /// <summary>Seconds elapsed in the current crossfade; at or past the duration when settled.</summary>
        private float _portraitFadeElapsed = PORTRAIT_FADE_SEC;

        /// <summary>What the front image is currently showing, so a repeat is not re-faded.</summary>
        private FacialExpression _portraitShowing = FacialExpression.Neutral;

        /// <summary>True while the gutter is reserved and the portrait is on screen.</summary>
        private bool _portraitActive;

        // ── Build ───────────────────────────────────────────────────────────

        /// <summary>
        /// Creates the portrait, hidden. Called once from the builder; whether it is ever
        /// shown is decided per conversation by <see cref="ConfigurePortraitFor"/>.
        /// </summary>
        private void BuildPortrait(Transform panel)
        {
            _portraitRoot = new GameObject("Portrait");
            _portraitRoot.transform.SetParent(panel, false);

            var rt = _portraitRoot.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(PORTRAIT_SIZE_W, PORTRAIT_SIZE_H);
            rt.anchoredPosition = new Vector2(PANEL_PADDING, -PANEL_PADDING);

            // Without this the VerticalLayoutGroup claims the rect and the anchors above are
            // overwritten on the next rebuild.
            var le = _portraitRoot.AddComponent<LayoutElement>();
            le.ignoreLayout = true;

            _portraitFrame = _portraitRoot.AddComponent<Image>();
            _portraitFrame.color = PORTRAIT_FRAME_COLOR;
            _portraitFrame.raycastTarget = false;

            _portraitBack = CreatePortraitLayer("Back");
            _portraitFront = CreatePortraitLayer("Front");

            _portraitRoot.SetActive(false);
        }

        /// <summary>
        /// One of the two stacked face images, inset inside the frame. Both are identical;
        /// which is front and which is back is decided by draw order, and the front is
        /// created last so it renders over the back.
        /// </summary>
        private Image CreatePortraitLayer(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_portraitRoot.transform, false);

            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(PORTRAIT_INSET, PORTRAIT_INSET);
            rt.offsetMax = new Vector2(-PORTRAIT_INSET, -PORTRAIT_INSET);

            var img = go.AddComponent<Image>();
            img.raycastTarget = false;

            // A face is not the frame's aspect and must not be stretched to it: the drawings
            // are 370x395 and the gutter is whatever the panel can spare.
            img.preserveAspect = true;
            img.color = new Color(1f, 1f, 1f, 0f);
            return img;
        }

        // ── Per conversation ────────────────────────────────────────────────

        /// <summary>
        /// Reserves or releases the gutter for <paramref name="persona"/> and seats the first
        /// face without a fade.
        ///
        /// <para>Called from <c>OnChatOpened</c>, after <c>ChatSystem</c> has already chosen
        /// the greeting's expression — so the panel opens on the right face rather than
        /// opening neutral and correcting itself one frame later.</para>
        /// </summary>
        private void ConfigurePortraitFor(NPCPersonaDefinition persona)
        {
            _portraitActive = persona != null && persona.HasFaces;

            if (_portraitRoot != null) _portraitRoot.SetActive(_portraitActive);
            if (_panelLayout != null)
            {
                _panelLayout.padding.left = _portraitActive
                    ? (int)(PANEL_PADDING + PORTRAIT_GUTTER)
                    : (int)PANEL_PADDING;
                LayoutRebuilder.MarkLayoutForRebuild((RectTransform)_panel.transform);
            }

            if (!_portraitActive) return;

            var opening = ChatSystem.Instance != null
                ? ChatSystem.Instance.CurrentExpression
                : FacialExpression.Neutral;

            SeatPortrait(persona, opening);
        }

        /// <summary>Puts a face up with no transition. Opening a panel is not a change of mood.</summary>
        private void SeatPortrait(NPCPersonaDefinition persona, FacialExpression expression)
        {
            _portraitShowing = expression;
            _portraitFadeElapsed = PORTRAIT_FADE_SEC;

            _portraitFront.sprite = persona != null ? persona.ResolveFace(expression) : null;
            _portraitFront.color = new Color(1f, 1f, 1f, _portraitFront.sprite != null ? 1f : 0f);
            _portraitBack.sprite = null;
            _portraitBack.color = new Color(1f, 1f, 1f, 0f);
        }

        /// <summary>
        /// Crossfades to <paramref name="expression"/>. Bound to
        /// <c>ChatSystem.OnExpressionChanged</c>, which never raises for a repeat.
        /// </summary>
        private void OnExpressionChanged(FacialExpression expression)
        {
            if (!_portraitActive || _portraitFront == null) return;

            var persona = ChatSystem.Instance != null ? ChatSystem.Instance.ActivePersona : null;
            Sprite next = persona != null ? persona.ResolveFace(expression) : null;

            // Two different expressions can share one drawing through the fallback chain —
            // laugh and happy on a character that only drew happy. Dissolving a sprite into
            // itself is a flicker with no cause the player can see.
            if (next == _portraitFront.sprite)
            {
                _portraitShowing = expression;
                return;
            }

            _portraitBack.sprite = _portraitFront.sprite;
            _portraitBack.color = new Color(1f, 1f, 1f, _portraitBack.sprite != null ? 1f : 0f);

            _portraitFront.sprite = next;
            _portraitFront.color = new Color(1f, 1f, 1f, 0f);

            _portraitShowing = expression;
            _portraitFadeElapsed = 0f;
        }

        /// <summary>
        /// Advances the crossfade. Driven from <c>Update</c> BEFORE its early return, since
        /// that method exists to watch for the Enter key and would otherwise skip this on
        /// every frame the player is not pressing it.
        /// </summary>
        private void TickPortraitFade()
        {
            if (!_portraitActive || _portraitFadeElapsed >= PORTRAIT_FADE_SEC) return;

            _portraitFadeElapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(_portraitFadeElapsed / PORTRAIT_FADE_SEC);

            if (_portraitFront != null && _portraitFront.sprite != null)
                _portraitFront.color = new Color(1f, 1f, 1f, t);
            if (_portraitBack != null && _portraitBack.sprite != null)
                _portraitBack.color = new Color(1f, 1f, 1f, 1f - t);
        }

        /// <summary>What the portrait is showing. Read by the tests and the probe commands.</summary>
        internal FacialExpression PortraitExpression => _portraitShowing;

        /// <summary>True when the gutter is reserved for this conversation.</summary>
        internal bool PortraitVisible => _portraitActive;
    }
}
