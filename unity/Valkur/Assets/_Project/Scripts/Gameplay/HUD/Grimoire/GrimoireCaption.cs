using System.Collections.Generic;
using UnityEngine;
using Valkur.UI.HUD;

namespace Valkur.Gameplay.HUD
{
    /// <summary>
    /// The text under a node: a spell's name over as many as two lines, or the one line saying
    /// why it is shut.
    ///
    /// <para><b>Why it is not a <c>Text</c> with best-fit.</b> That is what it was, and the rail
    /// had already been rebuilt out of the identical defect a few hours earlier: uGUI's
    /// <c>resizeTextForBestFit</c> solves each label INDEPENDENTLY, so a board of eight spells
    /// draws eight different type sizes and the column reads as eight unrelated labels rather
    /// than as one list. Measured on the shipped martial school, "Scatter Volley" shrank to fit
    /// while "War Cry" did not, side by side, at the same importance.</para>
    ///
    /// <para>A bitmap face cannot do that — every glyph is one size by construction — which is
    /// the same argument that made the rail legible, and it is what the talents tab next door
    /// already does (<c>SkillNodeView</c> draws its node names through the same face). It also
    /// puts the ink on the texel grid: Arial at seven canvas units inside a panel drawn at two
    /// screen pixels per texel is three and a half texels of ink, i.e. sub-texel and soft, in
    /// the one window whose whole contract (R1) is that it is not.</para>
    ///
    /// <para><b>The cost, stated plainly:</b> the small face is CAPITALS ONLY, so a spell's name
    /// is shouted. That was the argument for keeping Arial and it loses to the two above —
    /// the talents tab shows the same shout is legible, and a name that is crisp and loud beats
    /// one that is soft and a different size from its neighbour.</para>
    /// </summary>
    internal sealed class GrimoireCaption
    {
        private readonly HudPixelText[] _lines;

        /// <summary>This caption's own wrap buffer. Per INSTANCE, never shared.</summary>
        private readonly List<string> _wrapped;

        private GrimoireCaption(HudPixelText[] lines)
        {
            _lines = lines;
            _wrapped = new List<string>(lines.Length);
        }

        /// <summary>
        /// Builds a caption of <paramref name="maxLines"/> stacked rows, centred under a node.
        /// </summary>
        /// <param name="centreY">
        /// Where the middle of the whole block sits, in the node's own local space, positive up.
        /// </param>
        internal static GrimoireCaption Create(Transform parent, HudArt art, int width,
                                               int lineHeight, float centreY, int maxLines)
        {
            maxLines = Mathf.Max(1, maxLines);
            var lines = new HudPixelText[maxLines];

            // Rows are placed from the top down, which is the order a reader takes them in and
            // the order a wrap produces them in. The block is centred on centreY, so a caption
            // that uses one of its two lines is not left hanging half a line high.
            float blockTop = centreY + lineHeight * maxLines * 0.5f;

            for (int i = 0; i < maxLines; i++)
            {
                var text = HudPixelText.Create(parent, "CaptionLine" + i, art,
                                               HudFontFace.Small, HudTextAlign.Centre,
                                               0, 0, width, lineHeight);

                // Re-anchored AFTER Create, deliberately. Create places a label bottom-left on
                // the texel grid, which is right for every fixed part of the window and wrong
                // here: a caption hangs off its NODE's centre, and a node's centre is a float
                // on a board that zooms. Anchoring it to the parent's middle is what keeps the
                // text under the socket rather than under the board's own origin.
                var rt = text.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(width, lineHeight);
                rt.anchoredPosition = new Vector2(
                    0f, blockTop - lineHeight * (i + 0.5f));

                lines[i] = text;
            }

            return new GrimoireCaption(lines);
        }

        /// <summary>Sets the whole caption, wrapping it across the rows it has.</summary>
        internal void SetText(string value, int widthTexels)
        {
            Wrap(value, widthTexels, _lines.Length, _wrapped);
            for (int i = 0; i < _lines.Length; i++)
                _lines[i].SetText(i < _wrapped.Count ? _wrapped[i] : string.Empty);
        }

        internal void SetColour(Color c)
        {
            for (int i = 0; i < _lines.Length; i++) _lines[i].color = c;
        }

        // ── The wrap ─────────────────────────────────────────────────────────

        /// <summary>
        /// Splits text across at most <paramref name="maxLines"/> rows of
        /// <paramref name="widthTexels"/>, breaking on SPACES and measuring real ink.
        ///
        /// <para>Measured rather than counted, because the face is proportional: "WWW" and
        /// "III" are the same three characters and very different widths, so a character
        /// budget is right for one word and wrong for the next. The rail learned this the
        /// expensive way — a seventeen-character limit passed a name that inked six texels
        /// past its box.</para>
        ///
        /// <para>A word too long for a line of its own is left whole and allowed to overhang,
        /// never cut: a cut word is a different word, and the caption's other job is naming the
        /// reason a node is shut. There is nothing to cut today — measured on the shipped 71,
        /// the longest single word fits — and if one ever arrives, overhanging is the failure
        /// somebody can SEE.</para>
        /// </summary>
        internal static List<string> Wrap(string value, int widthTexels, int maxLines)
        {
            var into = new List<string>(Mathf.Max(1, maxLines));
            Wrap(value, widthTexels, maxLines, into);
            return into;
        }

        /// <summary>
        /// Fills <paramref name="into"/> with the wrapped lines.
        ///
        /// <para><b>The caller owns the list, and it used to be a shared static.</b> That one
        /// line carried two defects. It was static mutable state with no reset hook, which
        /// DomainReloadStaticResetTests failed it for; and Wrap RETURNED it, so every caller
        /// held a reference to one buffer the next call would rewrite underneath them - the
        /// hazard AlliedUnit.Live was fixed for. Nothing alive today reads a result across a
        /// second call, so it was latent rather than live, which is the kind that ships.</para>
        ///
        /// <para>What the static was avoiding is a list of at most two short strings, next to
        /// building uGUI objects. It was never worth a shared buffer.</para>
        /// </summary>
        internal static void Wrap(string value, int widthTexels, int maxLines, List<string> into)
        {
            if (into == null) return;
            into.Clear();
            if (string.IsNullOrEmpty(value)) return;

            var glyphs = HudPixelFont.Glyphs(HudFontFace.Small);
            var words = value.Split(' ');

            string line = string.Empty;
            for (int i = 0; i < words.Length; i++)
            {
                string word = words[i];
                if (word.Length == 0) continue;

                string candidate = line.Length == 0 ? word : line + " " + word;
                if (HudPixelFont.MeasureWidth(candidate, HudFontFace.Small, glyphs) <= widthTexels
                    || line.Length == 0)
                {
                    line = candidate;
                    continue;
                }

                into.Add(line);
                line = word;

                // The LAST row keeps whatever is left rather than dropping it. A caption that
                // silently loses its tail is indistinguishable from a spell with a short name.
                if (into.Count == maxLines - 1)
                {
                    for (int j = i + 1; j < words.Length; j++)
                    {
                        if (words[j].Length == 0) continue;
                        line = line + " " + words[j];
                    }
                    break;
                }
            }

            if (line.Length > 0 && into.Count < maxLines) into.Add(line);
        }
    }
}
