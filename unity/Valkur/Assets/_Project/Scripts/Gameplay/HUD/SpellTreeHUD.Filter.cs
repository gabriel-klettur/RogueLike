using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Data;
using Valkur.UI.HUD;

namespace Valkur.Gameplay.HUD
{
    /// <summary>
    /// The role filter: eight chips under the constellation that answer "what heals me".
    ///
    /// <para><b>Why it exists.</b> The grimoire is organised by SCHOOL because that is what
    /// scales — nine schools give about eleven nodes a rail entry at the hundred-spell target,
    /// while seven functional categories would give Damage forty-five and leave the rest nearly
    /// empty. <see cref="SpellRole"/>'s own doc states the cost of that choice: FUNCTION becomes
    /// invisible, and a player looking for a heal has to already know healing lives in Radiance.
    /// This row is the half that buys it back, and it is what the tag was authored on all 71
    /// nodes for.</para>
    ///
    /// <para><b>It DIMS, it does not hide.</b> A tree with branches removed does not read as a
    /// tree — the chains would end in mid-air and the shape the board exists to show would be
    /// a different shape. The same call the Controls editor makes for its own search, and the
    /// reason its filter is trustworthy.</para>
    ///
    /// <para><b>No free-text box, deliberately.</b> A <c>TMP_InputField</c> here would need
    /// focus arbitration with <c>InputBlocker</c> — a focused field is exactly what makes
    /// <c>KeyboardInputManager</c> refuse every key but Escape — and it would answer the same
    /// question this row already answers for a 71-node grimoire. It becomes worth it at the
    /// hundred-spell target, not before.</para>
    /// </summary>
    public sealed partial class SpellTreeHUD
    {
        /// <summary>The role the filter is on, or null for "everything".</summary>
        private SpellRole? _roleFilter;

        private readonly List<FilterChip> _chips = new List<FilterChip>();

        private sealed class FilterChip
        {
            public SpellRole? Role;       // null = the "all" chip
            public Image Background;
            public HudPixelText Label;
        }

        /// <summary>The active role filter, or null. Test seam.</summary>
        public SpellRole? RoleFilter => _roleFilter;

        /// <summary>
        /// Sets the filter. Passing the role that is already on clears it, so the same chip
        /// is both the on and the off switch — with eight of them, a separate "clear" is a
        /// ninth control for something a second click already says.
        /// </summary>
        public void SetRoleFilter(SpellRole? role)
        {
            if (_roleFilter.HasValue && role.HasValue && _roleFilter.Value == role.Value)
                role = null;

            if (_roleFilter.Equals(role)) return;
            _roleFilter = role;

            PaintChips();
            ApplyFilterToBoard();
            GrimoireAudio.Play(GrimoireSound.Select, _style, 0.6f);
        }

        /// <summary>Whether a node survives the current filter.</summary>
        private bool PassesFilter(SpellNode node) =>
            node == null || !_roleFilter.HasValue || node.role == _roleFilter.Value;

        // ── Build ─────────────────────────────────────────────────────────

        /// <summary>
        /// The chip row, along the bottom of the board's column. It sits THERE and not in the
        /// footer because it belongs to the board it filters: measured, the constellation fills
        /// 43-65 % of its column's height in every shipped school, so the room was already
        /// under it.
        /// </summary>
        private void BuildFilterRow()
        {
            var b = _frame.Board;
            int h = _style.filterChipTexels;

            var rowGo = new GameObject("RoleFilter", typeof(RectTransform));
            rowGo.transform.SetParent(_pixels, false);
            _filterRoot = (RectTransform)rowGo.transform;
            HudRect.Place(_filterRoot, b.x, b.y, b.width, h);

            _chips.Clear();

            var roles = new List<SpellRole?> { null };
            foreach (SpellRole role in System.Enum.GetValues(typeof(SpellRole)))
                roles.Add(role);

            int gap = _style.filterChipGapTexels;

            // Chips are as wide as their OWN WORD, not one eighth of the row each.
            //
            // Equal eighths is the obvious split and it is wrong here because the words are
            // not equal: measured on the shipped Spanish labels, an eighth of this row is 35
            // texels and "PROTECCIÓN" needs 40, so it ran straight into "CURACIÓN" with no gap
            // between them — two chips reading as one word. Their total ink plus the gaps
            // comes to about 254 texels in a 296-texel row, so there is room for every one of
            // them at its own size and about forty texels left to share out.
            var labels = new string[roles.Count];
            var widths = new int[roles.Count];
            int inkTotal = 0;

            for (int i = 0; i < roles.Count; i++)
            {
                labels[i] = ChipLabel(roles[i]);
                widths[i] = _art.Measure(labels[i], HudFontFace.Small)
                          + GrimoireGeometry.ChipPadding;
                inkTotal += widths[i];
            }

            int available = GrimoireGeometry.FilterAvailable(b.width, roles.Count, gap);
            int slack = available - inkTotal;

            // A row that cannot fit is SQUEEZED, never allowed to run off its column. Sized by
            // their own ink alone, the eight Spanish labels asked for 294 texels in a column of
            // 266 and the last chip was cut in half by the panel edge — and it only happened
            // after the rail took its width from the board, which is the shape of a layout
            // whose pieces are sized independently.
            //
            // The trim is PROPORTIONAL to each chip's own width, which is the half that keeps
            // the longest word the widest chip. The first version walked the row from the left
            // taking as much as each chip could give down to a floor of ten — correct in total
            // and wrong in shape: at a small overflow it would have taken the whole of it out
            // of "TODO" and left "PROTECCIÓN" untouched, so the row would squeeze exactly the
            // chips that had nothing to spare. Being under-sized is the failure a squeeze is
            // supposed to spread evenly, not concentrate.
            if (slack < 0)
            {
                GrimoireGeometry.SqueezeToFit(widths, available);
                slack = 0;
            }

            // Whatever is left over is shared equally, so the row fills its column instead of
            // huddling on the left. Integer division on purpose: a fractional texel is what
            // puts a chip off the grid.
            int bonus = slack > 0 ? slack / roles.Count : 0;

            int x = 0;
            for (int i = 0; i < roles.Count; i++)
            {
                var chip = new FilterChip { Role = roles[i] };
                int width = Mathf.Max(8, widths[i] + bonus);

                var chipGo = new GameObject("Chip_" + (roles[i].HasValue ? roles[i].ToString() : "All"),
                                            typeof(RectTransform));
                chipGo.transform.SetParent(_filterRoot, false);
                HudRect.Place((RectTransform)chipGo.transform, x, 0, width, h);
                x += width + gap;

                chip.Background = chipGo.AddComponent<Image>();
                chip.Background.sprite = GrimoireArt.Get().Chip;
                chip.Background.type = Image.Type.Sliced;

                var button = chipGo.AddComponent<Button>();
                button.targetGraphic = chip.Background;
                button.transition = Selectable.Transition.None;
                var captured = roles[i];
                button.onClick.AddListener(() => SetRoleFilter(captured));

                chip.Label = HudPixelText.Create(chipGo.transform, "Label", _art,
                    HudFontFace.Small, HudTextAlign.Centre, 1, (h - 5) / 2, width - 2, 5);
                chip.Label.SetText(labels[i]);

                _chips.Add(chip);
            }

            PaintChips();
        }

        /// <summary>
        /// The board width at which no chip has to be squeezed, measured from the shipped
        /// labels and the shipped face.
        ///
        /// <para>It is asked BEFORE the window is built, because it is what caps the pixel
        /// scale - and it goes through <see cref="HudPixelFont"/> rather than through the
        /// atlas for exactly that reason: the atlas is not up yet, and a bitmap face's widths
        /// are a property of the face and not of the texture it was baked into.</para>
        /// </summary>
        private int FilterRowIdealWidth()
        {
            var glyphs = HudPixelFont.Glyphs(HudFontFace.Small);

            int count = 1;                       // the "all" chip
            int ink = HudPixelFont.MeasureWidth(ChipLabel(null), HudFontFace.Small, glyphs);

            foreach (SpellRole role in System.Enum.GetValues(typeof(SpellRole)))
            {
                ink += HudPixelFont.MeasureWidth(ChipLabel(role), HudFontFace.Small, glyphs);
                count++;
            }

            return GrimoireGeometry.FilterRowIdealWidth(
                count, _style.filterChipGapTexels, ink);
        }

        /// <summary>
        /// The chip's word. The pixel face is CAPITALS ONLY (its accented glyphs are the
        /// uppercase ones), so the label is upper-cased here rather than authored shouting —
        /// "DAÑO" and "PROTECCIÓN" both resolve, which is what made the face usable for
        /// Spanish at all.
        /// </summary>
        private static string ChipLabel(SpellRole? role) =>
            (role.HasValue ? GrimoireText.Role(role.Value) : GrimoireText.AllRoles)
                .ToUpperInvariant();

        private void PaintChips()
        {
            for (int i = 0; i < _chips.Count; i++)
            {
                var chip = _chips[i];
                bool on = _roleFilter.Equals(chip.Role);

                // The chip that is ON is drawn in the SCHOOL's accent, not in a fixed colour:
                // the filter belongs to the board under it, and a gold chip would be claiming
                // importance the gold is reserved for.
                var tree = ActiveTree();
                Color accent = tree != null ? tree.accent : _theme.text;

                chip.Background.color = on
                    ? new Color(accent.r * 0.45f, accent.g * 0.45f, accent.b * 0.45f, 1f)
                    : _theme.stoneDark;
                chip.Label.SetColour(on ? _theme.text : _theme.textDisabled);
            }
        }

        /// <summary>
        /// Fades every node the filter excludes, and every chain whose ends are both excluded.
        /// A chain with one live end stays lit: it is the path TO something the player is
        /// looking at, which is the whole reason the filter is worth having on a tree.
        /// </summary>
        private void ApplyFilterToBoard()
        {
            float dim = _style.filteredAlpha;

            for (int i = 0; i < _nodes.Count; i++)
            {
                var view = _nodes[i];
                view.SetFade(PassesFilter(view.Node) ? 1f : dim);
            }

            for (int i = 0; i < _chains.Count; i++)
            {
                // A chain with ONE surviving end stays lit: it is the path TO something the
                // player is looking at, which is most of why a filter is worth having on a
                // tree rather than on a list.
                bool live = PassesFilter(_chains[i].Parent) || PassesFilter(_chains[i].Child);
                _chains[i].View.SetFade(live ? 1f : dim);
            }
        }
    }
}
