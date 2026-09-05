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
    /// <para>THE GUTTER IS ALWAYS THERE NOW, and it did not use to be. While it held only a
    /// face, reserving it for a character without one put an empty rectangle beside five of
    /// the six conversations in the game, which reads as a portrait that failed to load —
    /// so it was reserved per conversation. It is a COLUMN now: Comerciar sits under the
    /// face and Reiniciar at its foot, so there is no conversation in which the strip is
    /// empty and nothing left for the old rule to protect against. Making it unconditional
    /// also retires the hazard that rule created — a size the player saved on a
    /// portrait-less NPC no longer describes a differently-shaped panel.</para>
    ///
    /// <para>The FACE is still per conversation: a character with no art shows no frame, and
    /// the button under it moves up to take the top of the column.</para>
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

        /// <summary>True while a FACE is on screen. The gutter itself is unconditional.</summary>
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

            // Unconditional: the gutter is a column of controls that every conversation has,
            // and only the face inside it is optional. It is still set here rather than once
            // in the builder because this is the method that knows what the column contains.
            if (_panelLayout != null)
            {
                _panelLayout.padding.left = (int)(PANEL_PADDING + PORTRAIT_GUTTER);
                LayoutRebuilder.MarkLayoutForRebuild((RectTransform)_panel.transform);
            }

            LayoutGutterColumn();

            if (!_portraitActive) return;

            var opening = ChatSystem.Instance != null
                ? ChatSystem.Instance.CurrentExpression
                : FacialExpression.Neutral;

            SeatPortrait(persona, opening);
        }

        /// <summary>
        /// Stacks the gutter's contents from the top: the face when there is one, then
        /// Comerciar when this character trades, then Diario. Reiniciar is not here — it is
        /// pinned to the FOOT of the column, as far from the conversation as the panel
        /// allows, because it is the one control that destroys player data.
        ///
        /// <para>ONE owner for those Y values, and it earns that the hard way. The gutter's
        /// children are <c>ignoreLayout</c> — they have to be, or the panel's
        /// <c>VerticalLayoutGroup</c> overwrites their anchors — which means NOTHING arranges
        /// them and nothing complains when two of them land on the same pixels. The panel has
        /// already paid for that twice: a 504x0 language button lying invisibly across the
        /// close control and eating its clicks. A stack computed in one pass cannot produce a
        /// gap it did not intend or an overlap it cannot see.</para>
        ///
        /// <para>Both conditions really vary. Five of the six characters in the game have no
        /// portrait, five of the seven do not trade, and Gatita is both — so a column that
        /// assumed either would be wrong for most conversations.</para>
        /// </summary>
        private void LayoutGutterColumn()
        {
            float top = PANEL_PADDING;

            if (_portraitActive) top += PORTRAIT_SIZE_H + GUTTER_GAP;

            top = SeatGutterButton(_tradeButton, top, GUTTER_TRADE_HEIGHT);
            top = SeatGutterButton(_journalButton, top, GUTTER_JOURNAL_HEIGHT);
        }

        /// <summary>
        /// Places one gutter button at <paramref name="top"/> and returns where the next one
        /// starts.
        ///
        /// <para>The two halves are deliberately independent. It ALWAYS writes the position,
        /// so a button switched on later is already where it belongs rather than sitting at
        /// the origin until the next conversation; and it only ADVANCES the cursor when the
        /// button is actually showing, which is what closes the column up for a character who
        /// does not trade instead of leaving a hole above Diario.</para>
        /// </summary>
        private static float SeatGutterButton(GameObject button, float top, float height)
        {
            if (button == null) return top;

            ((RectTransform)button.transform).anchoredPosition = new Vector2(PANEL_PADDING, -top);
            return button.activeSelf ? top + height + GUTTER_GAP : top;
        }

        /// <summary>Puts a face up with no transition. Opening a panel is not a change of mood.</summary>
        private void SeatPortrait(NPCPersonaDefinition persona, FacialExpression expression)
        {
            _portraitShowing = expression;
            _portraitFadeElapsed = PORTRAIT_FADE_SEC;

            _portraitFront.sprite = ResolvePortrait(persona, expression);
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
            Sprite next = ResolvePortrait(persona, expression);

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
        /// The drawing for <paramref name="expression"/> on whichever axis the conversation
        /// is currently on — listening while the player types, talking otherwise.
        ///
        /// <para>The single place the two sets are chosen between, so a new caller cannot
        /// render one axis while the rest of the panel is on the other.</para>
        /// </summary>
        private static Sprite ResolvePortrait(NPCPersonaDefinition persona, FacialExpression expression)
        {
            if (persona == null) return null;

            bool listening = ChatSystem.Instance != null && ChatSystem.Instance.Listening;
            return listening ? persona.ResolveListeningFace(expression) : persona.ResolveFace(expression);
        }

        /// <summary>
        /// Swaps the portrait when the player starts or stops typing, reusing the expression
        /// crossfade so starting to type is the same visual event as changing mood.
        ///
        /// <para>Bound to <c>ChatSystem.OnListeningChanged</c>. It re-runs the ordinary
        /// change path against the CURRENT expression rather than seating the sprite
        /// directly, which is what makes the "two expressions can share one drawing" guard
        /// apply here too — on a character with no listening art at all, every one of these
        /// resolves to the sprite already up and nothing fades.</para>
        /// </summary>
        private void OnListeningChanged(bool listening)
        {
            OnExpressionChanged(_portraitShowing);
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

        /// <summary>True when this character's FACE is on screen. The gutter always is.</summary>
        internal bool PortraitVisible => _portraitActive;
    }
}
