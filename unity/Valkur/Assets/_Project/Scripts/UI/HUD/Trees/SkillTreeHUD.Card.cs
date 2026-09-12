using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.HUD;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// The detail card down the right-hand side, and the reason nothing on the board is ever
    /// truncated: every long sentence in this window lives here, in a column of its own.
    ///
    /// <para>It answers the question the player is actually holding — "what do I get for the
    /// next point" — by showing the effect AT the current rank beside the effect at the next
    /// one. <c>SkillNode.DescribeRank</c> already computed both; the old panel called it once,
    /// for the next rank, and then cut the answer off mid-word.</para>
    /// </summary>
    public sealed partial class SkillTreeHUD
    {
        private HudPixelText _cardTitle;
        private HudPixelText _cardRank;
        private HudPixelText _cardNowLabel;
        private HudPixelText _cardNowValue;
        private HudPixelText _cardNextLabel;
        private HudPixelText _cardNextValue;
        private HudPixelText _cardCost;
        private TextMeshProUGUI _cardProse;
        private TextMeshProUGUI _cardReasons;
        private Image _learnButton;
        private HudPixelText _learnLabel;
        private Image _cardRecess;

        private float _refuseUntil;
        private int _refuseBaseX;

        private void BuildCard()
        {
            int w = _style.cardWidthTexels;

            _cardRecess = Tinted("CardRecess", _cardRoot, _art.BarFrame, 0, 0, w, _boardHeight,
                                 _theme.recess, Image.Type.Sliced);

            _cardTitle = HudPixelText.Create(_cardRoot, "CardTitle", _art, HudFontFace.Small,
                                             HudTextAlign.Left, 5, 0, w - 10, 7);
            _cardTitle.color = _theme.text;

            _cardRank = HudPixelText.Create(_cardRoot, "CardRank", _art, HudFontFace.Small,
                                            HudTextAlign.Left, 5, 0, w - 10, 7);
            _cardRank.color = _theme.gold;

            _cardNowLabel = HudPixelText.Create(_cardRoot, "NowLabel", _art, HudFontFace.Small,
                                                HudTextAlign.Left, 5, 0, 34, 7);
            _cardNowLabel.color = _theme.textDim;
            _cardNowValue = HudPixelText.Create(_cardRoot, "NowValue", _art, HudFontFace.Small,
                                                HudTextAlign.Left, 41, 0, w - 46, 7);
            _cardNowValue.color = _theme.text;

            _cardNextLabel = HudPixelText.Create(_cardRoot, "NextLabel", _art, HudFontFace.Small,
                                                 HudTextAlign.Left, 5, 0, 34, 7);
            _cardNextLabel.color = _theme.textDim;
            _cardNextValue = HudPixelText.Create(_cardRoot, "NextValue", _art, HudFontFace.Small,
                                                 HudTextAlign.Left, 41, 0, w - 46, 7);
            _cardNextValue.color = _theme.success;

            // Prose in TMP, which is exactly what R4 permits it for: the bitmap face is
            // uppercase-only and does not wrap, and a description set in capitals reads as
            // shouting. Everything numeric above and below stays in the pixel face.
            _cardProse = MakeProse("CardProse", 5, 0, w - 10, 42, _theme.textDim, 10f);
            _cardReasons = MakeProse("CardReasons", 5, 0, w - 10, 24, _theme.danger, 10f);

            _cardCost = HudPixelText.Create(_cardRoot, "CardCost", _art, HudFontFace.Small,
                                            HudTextAlign.Left, 5, 20, w - 10, 7);
            _cardCost.color = _theme.textDim;

            _learnButton = Tinted("Learn", _cardRoot, _art.Slot, 5, 4, w - 10, 13,
                                  _theme.stoneDark, Image.Type.Sliced);
            _learnButton.raycastTarget = true;
            var btn = _learnButton.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(OnLearnClicked);

            _learnLabel = HudPixelText.Create(_learnButton.rectTransform, "LearnLabel", _art,
                                              HudFontFace.Small, HudTextAlign.Centre, 0, 0,
                                              w - 10, 13);
            _learnLabel.color = _theme.text;
            _learnLabel.SetText(SkillText.Learn.ToUpperInvariant());
        }

        /// <summary>
        /// Re-places every row of the card against the CURRENT board height.
        ///
        /// <para>The card is built once at the default size and the window then resizes itself
        /// around the real tree, so a layout baked into the constructor puts the title off the
        /// top of a tall card and the prose off the bottom of a short one. It is called from the
        /// same pass that re-places the card's own rect.</para>
        /// </summary>
        private void LayoutCard()
        {
            int w = _style.cardWidthTexels;
            int h = _boardHeight;

            HudRect.Place(_cardRecess.rectTransform, 0, 0, w, h);

            int y = h - 12;
            HudRect.Place(_cardTitle.rectTransform, 5, y, w - 10, 7);
            y -= 10;
            HudRect.Place(_cardRank.rectTransform, 5, y, w - 10, 7);
            // 44, not 34: the longest label is "SIGUIENTE", nine glyphs of the small face plus its
            // tracking — 36 texels. At 34 it ran straight into the value column and the card read
            // "SIGUIENTE+36", which is the same defect this whole window was rebuilt to remove.
            const int LabelW = 44;
            const int ValueX = 5 + LabelW + 2;

            y -= 12;
            HudRect.Place(_cardNowLabel.rectTransform, 5, y, LabelW, 7);
            HudRect.Place(_cardNowValue.rectTransform, ValueX, y, w - ValueX - 5, 7);
            y -= 9;
            HudRect.Place(_cardNextLabel.rectTransform, 5, y, LabelW, 7);
            HudRect.Place(_cardNextValue.rectTransform, ValueX, y, w - ValueX - 5, 7);

            // The prose gets whatever is left between the numbers above and the button below,
            // which is why nothing in this window has to be truncated.
            int bottom = 20 + 9;
            int reasons = 24;
            int proseTop = y - 6;
            int proseHeight = Mathf.Max(14, proseTop - bottom - reasons - 2);

            HudRect.Place(_cardProse.rectTransform, 5, proseTop - proseHeight, w - 10, proseHeight);
            HudRect.Place(_cardReasons.rectTransform, 5, bottom, w - 10, reasons);
            HudRect.Place(_cardCost.rectTransform, 5, 20, w - 10, 7);
            HudRect.Place(_learnButton.rectTransform, 5, 4, w - 10, 13);
            HudRect.Place(_learnLabel.rectTransform, 0, 0, w - 10, 13);
        }

        /// <summary>
        /// A TMP label with its font assigned explicitly.
        ///
        /// <para>Unity never calls <c>Awake</c> on a component added in Edit Mode, so a
        /// <c>TextMeshProUGUI</c> built by a fixture never picks up
        /// <c>TMP_Settings.defaultFontAsset</c> and the first call that sizes it throws an NRE
        /// from inside TMP with the caller's code nowhere in the message. Assigning it here is a
        /// no-op in Play Mode and the difference between red and green in a test.</para>
        /// </summary>
        private TextMeshProUGUI MakeProse(string name, int x, int y, int w, int h, Color colour,
                                          float fontSize)
        {
            var rt = HudRect.Make(name, _cardRoot, x, y, w, h);
            var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (tmp.font == null && TMP_Settings.defaultFontAsset != null)
                tmp.font = TMP_Settings.defaultFontAsset;
            tmp.fontSize = fontSize;
            tmp.color = colour;
            tmp.alignment = TextAlignmentOptions.TopLeft;
            tmp.enableWordWrapping = true;
            tmp.overflowMode = TextOverflowModes.Truncate;
            tmp.raycastTarget = false;
            return tmp;
        }

        // ── Refresh ───────────────────────────────────────────────────────────

        private void RefreshCard()
        {
            bool has = _selected != null && skills != null;
            _cardRoot.gameObject.SetActive(has);
            if (!has) return;

            var node = _selected.Node;
            int rank = skills.RankOf(node.skillId);
            int maxRank = Mathf.Max(1, node.maxRank);
            int level = ResolveLevel();

            _cardTitle.SetText((node.displayName ?? node.skillId).ToUpperInvariant());
            _cardRank.SetText(SkillText.Rank(rank, maxRank));

            _cardNowLabel.SetText(SkillText.Now.ToUpperInvariant());
            _cardNowValue.SetText(rank > 0 ? SkillText.Effect(node, rank) : "—");

            bool more = rank < maxRank;
            _cardNextLabel.SetText(more ? SkillText.NextRank.ToUpperInvariant() : string.Empty);
            _cardNextValue.SetText(more ? SkillText.Effect(node, rank + 1) : string.Empty);

            _cardProse.text = node.description ?? string.Empty;

            _locks.Clear();
            bool free = skills.CollectLockReasons(node, level, _locks);
            _cardReasons.text = free ? string.Empty : SkillText.Reasons(_locks, _sb);

            _cardCost.SetText(more ? SkillText.Cost.ToUpperInvariant() + "  " +
                                     SkillText.Points(node.pointCost)
                                   : SkillText.Maxed.ToUpperInvariant());
            _cardCost.color = _theme.textDim;
            _refuseBaseX = 5;

            _learnButton.gameObject.SetActive(more);
            _learnButton.color = free ? _theme.stoneLight : _theme.stoneDark;
            _learnLabel.color = free ? _theme.text : _theme.textDisabled;
        }

        private void OnLearnClicked()
        {
            if (_selected != null) TryLearn(_selected);
        }

        /// <summary>
        /// A refused purchase shakes the cost by one texel and reddens it. Deliberately NO
        /// particles: motes say "something good happened", and spending them on a refusal is how
        /// a HUD teaches a player to stop reading it.
        /// </summary>
        private void RefuseAt(SkillNodeView view)
        {
            _refuseUntil = Time.unscaledTime + _style.refuseShakeSeconds;
            if (_cardCost != null) _cardCost.color = _theme.danger;
            RefreshCardReasonsOnly();
        }

        private void RefreshCardReasonsOnly()
        {
            if (_selected == null || skills == null) return;
            _locks.Clear();
            skills.CollectLockReasons(_selected.Node, ResolveLevel(), _locks);
            _cardReasons.text = SkillText.Reasons(_locks, _sb);
        }

        /// <summary>Advances the refusal shake. One texel, and it stops.</summary>
        private void TickRefuse(float now)
        {
            if (_cardCost == null) return;
            var rt = _cardCost.rectTransform;
            if (now >= _refuseUntil)
            {
                if (!Mathf.Approximately(rt.anchoredPosition.x, _refuseBaseX))
                    rt.anchoredPosition = new Vector2(_refuseBaseX, rt.anchoredPosition.y);
                return;
            }
            int phase = Mathf.FloorToInt((_refuseUntil - now) * 24f) & 1;
            rt.anchoredPosition = new Vector2(_refuseBaseX + phase, rt.anchoredPosition.y);
        }
    }
}
