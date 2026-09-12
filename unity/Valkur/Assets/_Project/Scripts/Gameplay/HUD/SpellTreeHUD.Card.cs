using System.Text;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Data;
using Valkur.UI.HUD;

namespace Valkur.Gameplay.HUD
{
    /// <summary>
    /// The detail card: what this node is, what it costs, and everything standing between the
    /// player and it.
    ///
    /// <para><b>The Learn button lives HERE and not on every node.</b> A button per row invites
    /// buying without reading; a decision this permanent — arcane points are spent, and a
    /// respec is a separate act — is taken after looking at the thing. It is also the only
    /// place with room for the full list of reasons, which is what D1 and D2 of the audit were
    /// about: the panel this replaced showed ONE reason, chose the cheapest one, and then cut
    /// it off mid-sentence.</para>
    /// </summary>
    public sealed partial class SpellTreeHUD
    {
        private Image _cardIcon;
        private Image _cardSigil;
        private Text _cardName;
        private HudPixelText _cardRole;
        private HudPixelText _cardCost;
        private Text _cardEffects;
        private Text _cardReasons;
        private Button _learnButton;
        private Image _learnFill;
        private Text _learnLabel;

        private readonly StringBuilder _cardSb = new StringBuilder(160);

        private void BuildCardContents()
        {
            int w = _frame.Card.width;
            int h = _frame.Card.height;
            int pad = 4;
            int icon = Mathf.Min(_style.cardIconTexels, w - pad * 2);

            var iconGo = new GameObject("Icon", typeof(RectTransform));
            iconGo.transform.SetParent(_cardRoot, false);
            HudRect.Place((RectTransform)iconGo.transform, (w - icon) / 2, h - pad - icon, icon, icon);
            _cardIcon = iconGo.AddComponent<Image>();
            _cardIcon.preserveAspect = true;
            _cardIcon.raycastTarget = false;

            // The school's mark occupies the same square as a spell's icon, because it answers
            // the same question one level up: what am I looking at. Only ever one of the two is
            // on screen.
            // An INTEGER multiple of the mark's own 9, not the icon's 32: 9 into 32 is 3.55,
            // and a point-filtered upscale at a fractional ratio repeats some rows and not
            // others, so a symmetric mark comes out lopsided. Three times nine is 27.
            const int sigilNative = 9;
            int sigilBox = sigilNative * Mathf.Max(1, icon / sigilNative);
            _cardSigil = HudRect.MakeImage("Sigil", _cardRoot, null,
                                           (w - sigilBox) / 2, h - pad - sigilBox,
                                           sigilBox, sigilBox);
            _cardSigil.preserveAspect = true;
            _cardSigil.enabled = false;

            // Each row is placed BELOW the one above it by its own height plus a gap, rather
            // than by a constant nudge. Captured live, the name had an 11-texel box and a
            // two-word school title wrapped to two lines inside it: the second line spilled
            // straight over "AFINIDAD" and "1 / 8", three rows printed on top of each other.
            // A row that can hold two lines has to be told it is two lines tall.
            const int rowGap = 3;
            const int nameLines = 2;
            const int lineH = 11;
            const int inkH = 6;

            int y = h - pad - icon - rowGap - lineH * nameLines;
            _cardName = MakeText(_cardRoot, "Name", pad, y, w - pad * 2, lineH * nameLines, 11,
                                 TextAnchor.UpperCenter);
            _cardName.color = _theme.text;
            _cardName.fontStyle = FontStyle.Bold;

            y -= rowGap + inkH;
            _cardRole = HudPixelText.Create(_cardRoot, "Role", _art, HudFontFace.Small,
                                            HudTextAlign.Centre, pad, y, w - pad * 2, inkH);
            _cardRole.SetColour(_theme.textDim);

            y -= rowGap + inkH;
            _cardCost = HudPixelText.Create(_cardRoot, "Cost", _art, HudFontFace.Small,
                                            HudTextAlign.Centre, pad, y, w - pad * 2, inkH);
            _cardCost.SetColour(_theme.gold);

            // The button sits at the FOOT of the card, so the eye runs name, cost, what it
            // does, why you cannot, and only then reaches the thing that spends the points.
            int buttonH = 13;
            var buttonGo = new GameObject("Learn", typeof(RectTransform));
            buttonGo.transform.SetParent(_cardRoot, false);
            HudRect.Place((RectTransform)buttonGo.transform, pad, pad, w - pad * 2, buttonH);
            _learnFill = buttonGo.AddComponent<Image>();
            _learnButton = buttonGo.AddComponent<Button>();
            _learnButton.targetGraphic = _learnFill;
            _learnButton.transition = Selectable.Transition.None;
            _learnButton.onClick.AddListener(TryLearnSelected);

            _learnLabel = MakeText(buttonGo.transform, "Label", 0, 0, w - pad * 2, buttonH, 11,
                                   TextAnchor.MiddleCenter);

            int reasonsTop = pad + buttonH + 3;
            _cardReasons = MakeText(_cardRoot, "Reasons", pad, reasonsTop, w - pad * 2, 22, 9,
                                    TextAnchor.LowerCenter);
            _cardReasons.color = _theme.textDim;

            int effectsBottom = reasonsTop + 24;
            _cardEffects = MakeText(_cardRoot, "Effects", pad, effectsBottom, w - pad * 2,
                                    Mathf.Max(10, y - effectsBottom - 2), 9, TextAnchor.UpperCenter);
            _cardEffects.color = _theme.textDim;
        }

        /// <summary>Shows a node without committing a selection — the hover preview.</summary>
        private void PreviewNode(SpellNode node)
        {
            if (node == null) return;
            PaintCard(node);
        }

        private void RefreshCard()
        {
            if (_cardRoot == null) return;
            PaintCard(_selected);
        }

        private void PaintCard(SpellNode node)
        {
            var tree = ActiveTree();
            bool has = node != null && tree != null && grimoire != null;

            if (_cardSigil != null) _cardSigil.enabled = false;
            _cardIcon.enabled = has && node.ResolveIcon() != null;
            // Baked at the CARD's size, not the board's. Sharing one bake drew a 27 px texture
            // inside a 64 px box — a 2.4x magnification, which is R10's aliasing in reverse.
            if (_cardIcon.enabled)
                _cardIcon.sprite = ResolveIcon(node, _style.cardIconTexels * _pixelScale);

            if (!has)
            {
                PaintSchoolCard(tree);
                return;
            }
            _cardName.color = _theme.text;
            _learnButton.gameObject.SetActive(true);

            var state = StateOf(tree, node);
            _cardName.text = node.ResolveDisplayName();
            _cardRole.SetText(GrimoireText.Role(node.role).ToUpperInvariant());
            _cardRole.SetColour(_theme.textDim);

            int cost = grimoire.ResolveCost(tree, node);
            _cardCost.SetText(BuildCostLine(tree, node, cost, state));
            _cardEffects.text = GrimoireText.Effects(node, _cardSb);
            _cardReasons.text = state == GrimoireNodeState.Learned
                ? string.Empty
                : ReasonOf(tree, node);

            bool buyable = GrimoireNodeStatus.IsBuyable(state);
            if (state == GrimoireNodeState.Learned)
                SetLearnButton(false, _theme.success, GrimoireText.Known.ToUpperInvariant());
            else
                SetLearnButton(buyable, buyable ? tree.accent : _theme.stoneLight,
                               GrimoireText.Learn.ToUpperInvariant());
        }

        /// <summary>
        /// What the card shows when nothing is picked: the SCHOOL.
        ///
        /// <para>It said "Elige un nodo" and nothing else — a hundred and four texels wide,
        /// the second largest object in the window, holding two words. An empty state that only
        /// names its own emptiness is a hole with a caption; this one answers the question the
        /// player has when they open a school they have not bought into yet — what is this,
        /// whose is it, and how far in am I.</para>
        ///
        /// <para>The same four fields the rail shows in one line, given room: the school, its
        /// own line of prose, the progress, and whether it is the character's. Nothing here is
        /// new data — it is the data the window already had and only whispered.</para>
        /// </summary>
        private void PaintSchoolCard(SpellTree tree)
        {
            _learnButton.gameObject.SetActive(false);
            _cardIcon.enabled = false;

            if (_cardSigil != null && tree != null)
            {
                _cardSigil.sprite = GrimoireArt.Get().Sigil(tree.schoolKey);
                _cardSigil.color = tree.accent;
                _cardSigil.enabled = true;
            }

            if (tree == null || grimoire == null)
            {
                _cardName.text = GrimoireText.PickANode;
                _cardName.color = _theme.textDisabled;
                _cardRole.SetText(string.Empty);
                _cardCost.SetText(string.Empty);
                _cardEffects.text = string.Empty;
                _cardReasons.text = string.Empty;
                return;
            }

            int known = 0;
            foreach (var n in tree.Nodes) if (n != null && grimoire.IsNodeLearned(n)) known++;

            _cardName.text = tree.displayName;
            _cardName.color = tree.accent;

            bool affinity = tree.HasAffinity(grimoire.ClassKey);
            _cardRole.SetText((affinity
                ? GrimoireText.Affinity
                : GrimoireText.OffAffinity(tree.offAffinityCostMultiplier)).ToUpperInvariant());
            _cardRole.SetColour(affinity ? _theme.gold : _theme.textDim);

            _cardCost.SetText(known + " / " + tree.Count);

            // NOT the flavour: the footer already carries it, and the rendered frame showed
            // the same sentence twice in one window — once under the sigil and once along the
            // bottom. A window that says a thing twice is a window that has not decided where
            // that thing lives.
            _cardEffects.text = string.Empty;
            _cardReasons.text = GrimoireText.PickANode;
        }

        /// <summary>
        /// "2 PA · TE QUEDAN 3". The remainder is the half the old header never said: a price
        /// with no budget beside it is a number the player has to hold in their head.
        /// </summary>
        private string BuildCostLine(SpellTree tree, SpellNode node, int cost,
                                     GrimoireNodeState state)
        {
            _cardSb.Length = 0;
            if (state == GrimoireNodeState.Learned) return string.Empty;

            _cardSb.Append(GrimoireText.PointsShort(cost).ToUpperInvariant());

            if (!tree.HasAffinity(grimoire.ClassKey))
            {
                _cardSb.Append(GrimoireText.ReasonSeparator);
                _cardSb.Append(GrimoireText.OffAffinity(tree.offAffinityCostMultiplier)
                                           .ToUpperInvariant());
            }

            int left = grimoire.AvailablePoints - cost;
            if (left >= 0)
            {
                _cardSb.Append(GrimoireText.ReasonSeparator);
                _cardSb.Append(GrimoireText.Remaining(left).ToUpperInvariant());
            }
            return _cardSb.ToString();
        }

        private void SetLearnButton(bool interactable, Color fill, string label)
        {
            _learnButton.interactable = interactable;
            float dim = _style.disabledButtonFactor;
            _learnFill.color = interactable
                ? fill
                : new Color(fill.r * dim, fill.g * dim, fill.b * dim, 1f);
            _learnLabel.text = label;
            // Black was hardcoded on all nine school accents: on Umbramancy's dark violet that
            // is 3.17 : 1, under the 4.5 : 1 floor. The label follows its own background.
            _learnLabel.color = interactable ? LabelOn(_learnFill.color) : _theme.textDisabled;
        }

        private void TryLearnSelected()
        {
            var tree = ActiveTree();
            if (tree == null || _selected == null || grimoire == null) return;

            var view = _byNode.TryGetValue(_selected, out var found) ? found : null;

            if (!grimoire.TryLearn(tree, _selected, playerLevel, out _))
            {
                // A refusal shakes the socket by a texel and emits NOTHING. A mote on a
                // refusal teaches the player to ignore motes.
                if (view != null) view.Refuse(_style.refusalSeconds, _style.refusalShakeTexels);
                GrimoireAudio.Play(GrimoireSound.Refuse, _style);
                return;
            }

            // TryLearn raises OnLoadoutChanged, which calls Refresh, which is where the motes
            // and the sound come from — one path for a purchase however it was triggered.
        }

        /// <summary>
        /// Black or white, whichever the given fill can carry. Duplicated nowhere else: the
        /// board's sockets never carry a label.
        /// </summary>
        private static Color LabelOn(Color fill)
        {
            float luminance = 0.2126f * Linear(fill.r)
                            + 0.7152f * Linear(fill.g)
                            + 0.0722f * Linear(fill.b);
            return luminance > 0.19f
                ? new Color(0.04f, 0.04f, 0.05f, 1f)
                : new Color(0.96f, 0.96f, 0.98f, 1f);
        }

        private static float Linear(float c) =>
            c <= 0.04045f ? c / 12.92f : Mathf.Pow((c + 0.055f) / 1.055f, 2.4f);
    }
}
