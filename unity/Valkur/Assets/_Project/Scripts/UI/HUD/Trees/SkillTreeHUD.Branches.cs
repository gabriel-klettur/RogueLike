using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.HUD;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// The branches of the talents board: the class's own path, and every branch all classes
    /// share beside it — today "Barra de Guerra", which grows the War action bar.
    ///
    /// <para><b>One board per branch, switched by tabs in the title bar, never one board holding
    /// all of them.</b> The class path is three columns and the War bar branch is its own shape
    /// (a crown and two chains of five); drawn side by side they made a window wider than the
    /// screen at 1600x800 and put two unrelated questions in one picture. A tab keeps each board
    /// the size of its own tree — the rule <see cref="ComputeGeometry"/> already follows — and
    /// the purse in the corner is the same purse on every tab, because it is one currency.</para>
    ///
    /// <para><b>The tabs ARE the title.</b> With a single tree the title bar names it, as before;
    /// with branches each tab carries its tree's name and the selected one reads as the title.
    /// No gold: gold on this window means points and opened paths, and a tab is neither.</para>
    /// </summary>
    public sealed partial class SkillTreeHUD
    {
        private const int TabPadTexels = 6;
        private const int TabGapTexels = 3;

        private int _branchIndex;
        private readonly List<Image> _tabPlates = new List<Image>(3);
        private readonly List<HudPixelText> _tabLabels = new List<HudPixelText>(3);

        /// <summary>Which branch is on the board: 0 is the class path, 1.. the shared branches.</summary>
        public int BranchIndex => _branchIndex;

        /// <summary>How many branches the bound character has, the class path included.</summary>
        public int BranchCount => TreeCount();

        /// <summary>Shows branch <paramref name="index"/> (clamped). Public for the tests and the console.</summary>
        public void ShowBranch(int index)
        {
            int count = TreeCount();
            int clamped = count == 0 ? 0 : Mathf.Clamp(index, 0, count - 1);
            if (clamped == _branchIndex && _boardBuilt) { Repaint(); return; }
            _branchIndex = clamped;
            if (_motes != null) _motes.Clear();
            Rebuild();
        }

        private int TreeCount()
        {
            if (skills == null) return 0;
            int n = skills.Tree != null ? 1 : 0;
            return n + skills.Branches.Count;
        }

        /// <summary>The tree on the board now. The class path first, then the shared branches.</summary>
        private SkillTree CurrentTree()
        {
            if (skills == null) return null;
            var all = new List<SkillTree>(1 + skills.Branches.Count);
            if (skills.Tree != null) all.Add(skills.Tree);
            for (int i = 0; i < skills.Branches.Count; i++) all.Add(skills.Branches[i]);
            if (all.Count == 0) return null;
            if (_branchIndex >= all.Count) _branchIndex = all.Count - 1;
            return all[Mathf.Max(0, _branchIndex)];
        }

        /// <summary>
        /// Rebuilds the tab strip for the bound character. Called from the board rebuild, after
        /// the header is laid out, so the tabs sit exactly where the title would.
        /// </summary>
        private void RebuildTabs()
        {
            for (int i = 0; i < _tabPlates.Count; i++)
                DestroyUI(_tabPlates[i] != null ? _tabPlates[i].gameObject : null);
            _tabPlates.Clear();
            _tabLabels.Clear();

            int count = TreeCount();
            bool tabs = count > 1;
            _titleLabel.gameObject.SetActive(!tabs);
            if (!tabs) return;

            int pad = _style.paddingTexels;
            int y = _heightTexels - pad - _style.titleBarTexels;
            int x = pad + 2;
            int maxRight = _widthTexels - pad - _style.titleBarTexels - 8;

            for (int i = 0; i < count; i++)
            {
                var tree = TreeAt(i);
                string name = TreeLabel(tree).ToUpperInvariant();

                var plate = Tinted("Tab_" + i, _pixels, _art.Slot, x, y, 10, _style.titleBarTexels,
                                   _theme.stoneDark, Image.Type.Sliced);
                plate.raycastTarget = true;
                var label = HudPixelText.Create(plate.rectTransform, "Label", _art, HudFontFace.Small,
                                                HudTextAlign.Centre, 0, 0, 10, _style.titleBarTexels);
                label.SetText(name);

                int w = Mathf.Max(24, label.InkWidth + TabPadTexels * 2);
                if (x + w > maxRight) w = Mathf.Max(24, maxRight - x);
                HudRect.Place(plate.rectTransform, x, y, w, _style.titleBarTexels);
                HudRect.Place(label.rectTransform, 0, 0, w, _style.titleBarTexels);

                int captured = i;
                var btn = plate.gameObject.AddComponent<Button>();
                btn.transition = Selectable.Transition.None;
                btn.onClick.AddListener(() => ShowBranch(captured));

                _tabPlates.Add(plate);
                _tabLabels.Add(label);
                x += w + TabGapTexels;
            }
            PaintTabs();
        }

        /// <summary>
        /// The selected tab reads raised and lit, the others sunk and dim — the same two tones
        /// the rest of the window uses for "here" and "not here", so no new colour is introduced.
        /// </summary>
        private void PaintTabs()
        {
            for (int i = 0; i < _tabPlates.Count; i++)
            {
                bool on = i == _branchIndex;
                if (_tabPlates[i] != null) _tabPlates[i].color = on ? _theme.stoneLight : _theme.recess;
                if (_tabLabels[i] != null) _tabLabels[i].color = on ? _theme.text : _theme.textDim;
            }
        }

        private SkillTree TreeAt(int index)
        {
            if (skills == null) return null;
            if (skills.Tree != null)
            {
                if (index == 0) return skills.Tree;
                index--;
            }
            return index >= 0 && index < skills.Branches.Count ? skills.Branches[index] : null;
        }

        private static string TreeLabel(SkillTree tree)
        {
            if (tree == null) return SkillText.Title;
            return string.IsNullOrWhiteSpace(tree.displayName) ? SkillText.Title : tree.displayName;
        }
    }
}
