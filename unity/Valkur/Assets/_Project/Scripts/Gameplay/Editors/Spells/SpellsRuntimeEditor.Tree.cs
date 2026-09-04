using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Spells Editor — the Tree view: the catalog ordered the way the GRIMOIRE reaches it,
    /// as an indented outline of the nine schools.
    ///
    /// <para>WHY A THIRD VIEW. Grid and Table are both built FROM the catalog, so a spell with
    /// no <see cref="SpellNode"/> is indistinguishable in either from one that has a node —
    /// structurally, neither can show it. That is the one question the outline answers that
    /// the other two cannot, and it is a question this project keeps getting wrong: measured
    /// today, 104 catalog spells against 71 tree nodes, and the 33 without a node are all
    /// correctly outside (22 animation probes, 8 NPC, 1 boss, 2 innate). Keeping it that way
    /// is exactly what the "In the catalogue, in no school" section at the bottom is for.</para>
    ///
    /// <para>AN OUTLINE, NOT A GRAPH. The Spells panel is 312 px wide. Two roots at depth 5
    /// with routed edges do not fit in that, which is why the FSM editor's graph gets a
    /// full-screen slab of its own. Indentation carries the same fact — how deep into a
    /// school a spell sits — in a vertical list, which is the shape this panel already hosts
    /// twice.</para>
    ///
    /// <para>READ-ONLY, AND A SELECTOR. Clicking a row calls <see cref="SelectSpell"/>, so the
    /// Properties panel and the preview follow exactly as they do from the Grid. Rewiring
    /// prerequisites is NOT done here — the trees are seeded by
    /// <c>Valkur &gt; Progression &gt; Seed Progression Content</c> and edited in the Inspector.
    /// </para>
    ///
    /// <para>THE LAYOUT IS COMPUTED, NOT AUTHORED. <c>SpellNode.column</c> is <c>0</c> on all
    /// 71 shipped nodes and has no reader anywhere in the project, so there is no authored
    /// layout to draw. Depth comes from the prerequisite chain instead; <c>row</c> only breaks
    /// ties between siblings, which is the same job it does in <c>SkillTreeHUD</c>.</para>
    /// </summary>
    public partial class SpellsRuntimeEditor
    {
        private const float TREE_ROW_H = 22f;
        private const float TREE_INDENT_PX = 13f;
        private const float TREE_HEADER_H = 24f;

        /// <summary>Section key for the trailing "in the catalogue, in no school" group.</summary>
        private const string TREE_SECTION_ORPHANS = "__orphans";

        private static readonly Color TREE_HEADER_BG = new Color(0.16f, 0.18f, 0.24f, 0.95f);
        private static readonly Color TREE_ORPHAN_BG = new Color(0.24f, 0.16f, 0.14f, 0.95f);
        private static readonly Color TREE_GUIDE = new Color(0.38f, 0.42f, 0.52f, 0.55f);

        private RectTransform _spellsTreeContent;
        private readonly List<GameObject> _spellTreeRows = new List<GameObject>();

        /// <summary>Schools the author has folded away. Persisted with the workspace.</summary>
        private readonly HashSet<string> _collapsedSchools = new HashSet<string>();

        private ProgressionCatalog _progression;
        private bool _progressionResolved;

        /// <summary>
        /// Which school the outline is showing: <c>"all"</c>, a school key, or
        /// <see cref="TREE_SECTION_ORPHANS"/>.
        ///
        /// <para>Nine schools stacked is 114 rows — measured at 2528 px against a viewport
        /// that is a few hundred tall, so "see the tree for Pyromancy" meant scrolling past
        /// four other schools to find it and scrolling back to compare. One school at a time
        /// is eight rows and fits on screen whole, which is the only way the shape of a
        /// school is ever seen at once.</para>
        /// </summary>
        private string _treeSchoolFilter = TREE_SCHOOL_ALL;

        internal const string TREE_SCHOOL_ALL = "all";

        /// <summary>One drawn line of the outline. A header, or a spell.</summary>
        private struct TreeRow
        {
            public string SpellKey;      // null on a header
            public string Label;
            public string Trailing;      // cost/level, or the audience of an orphan
            public int Depth;            // indentation steps
            public bool IsHeader;
            public bool IsOrphanSection;
            public string SectionKey;
        }

        private readonly List<TreeRow> _treeRows = new List<TreeRow>();

        internal void BindTreeRefs(RectTransform content)
        {
            _spellsTreeContent = content;
            BuildTreeSchoolTabs();
        }

        /// <summary>
        /// Fill the school strip from the catalogue. Done here rather than in the UI builder
        /// because the schools are DATA — a tenth school seeded tomorrow has to appear without
        /// anyone editing the panel layout.
        /// </summary>
        private void BuildTreeSchoolTabs()
        {
            var strip = _uiRefs.SpellsTreeSchoolTabs;
            if (strip == null || strip.Count > 0) return;

            strip.AddTab(TREE_SCHOOL_ALL, "All", null);

            var catalog = Progression;
            if (catalog != null && catalog.spellTrees != null)
            {
                for (int i = 0; i < catalog.spellTrees.Length; i++)
                {
                    var tree = catalog.spellTrees[i];
                    if (tree == null) continue;
                    strip.AddTab(SchoolKeyOf(tree), TreeTabLabel(tree), null);
                }
            }

            // The trailing group is not a school, and it is the one the other two views
            // cannot show at all, so it gets a tab of its own rather than living only at the
            // bottom of "All" where a long catalogue would bury it.
            strip.AddTab(TREE_SECTION_ORPHANS, "Unlinked", null);

            strip.TabChanged += OnTreeSchoolTabChanged;
            strip.SetActive(_treeSchoolFilter);
        }

        private void OnTreeSchoolTabChanged(int _, string key)
        {
            _treeSchoolFilter = string.IsNullOrEmpty(key) ? TREE_SCHOOL_ALL : key;
            RefreshTree();
        }

        private static string SchoolKeyOf(SpellTree tree)
            => string.IsNullOrEmpty(tree.schoolKey) ? tree.name : tree.schoolKey;

        /// <summary>
        /// A short label for the strip. The authored display names run to "Martial Forms",
        /// which at four columns across 300 px has about 70 px to live in — the school KEY
        /// title-cased is the same word without the qualifier.
        /// </summary>
        private static string TreeTabLabel(SpellTree tree)
        {
            string key = SchoolKeyOf(tree);
            if (string.IsNullOrEmpty(key)) return "?";
            return char.ToUpperInvariant(key[0]) + key.Substring(1);
        }

        /// <summary>
        /// Resolve the progression catalog once per editor session.
        ///
        /// <para>It is loaded from a SUBFOLDER of <c>Resources/</c>, never from the empty path:
        /// <c>Resources.LoadAll&lt;T&gt;("")</c> deserialises the whole ~7,400-asset tree and
        /// logs a console error for every asset whose script no longer resolves.</para>
        /// </summary>
        private ProgressionCatalog Progression
        {
            get
            {
                if (_progressionResolved) return _progression;
                _progressionResolved = true;
                _progression = Resources.Load<ProgressionCatalog>(ProgressionCatalog.ResourcePath);
                return _progression;
            }
        }

        // ── model ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Rebuild <see cref="_treeRows"/> from the progression catalog plus whatever the
        /// search and audience filters currently allow.
        /// </summary>
        private void RebuildTreeModel()
        {
            _treeRows.Clear();

            var catalog = Progression;
            var claimed = new HashSet<string>();
            string filter = _searchFilter?.Trim().ToLowerInvariant() ?? string.Empty;

            // Every school is walked whatever the tab says, because AppendSchool is also what
            // CLAIMS a spell for a school. Skipping the unselected ones would push their
            // spells into the unlinked group and report them as unreachable.
            if (catalog != null && catalog.spellTrees != null)
            {
                for (int i = 0; i < catalog.spellTrees.Length; i++)
                    AppendSchool(catalog.spellTrees[i], filter, claimed);
            }

            AppendOrphans(catalog, filter, claimed);
        }

        private void AppendSchool(SpellTree tree, string filter, HashSet<string> claimed)
        {
            if (tree == null || tree.Nodes == null) return;

            // Claim every spell the school owns BEFORE filtering, or a search would push the
            // hidden ones into the orphan section and report them as unreachable.
            var nodes = new List<SpellNode>();
            for (int i = 0; i < tree.Count; i++)
            {
                var node = tree.Nodes[i];
                if (node == null) continue;
                nodes.Add(node);
                if (node.spell != null && !string.IsNullOrEmpty(node.spell.spellKey))
                    claimed.Add(node.spell.spellKey);
            }
            if (nodes.Count == 0) return;

            var ordered = new List<(SpellNode node, int depth)>();
            OrderSchool(nodes, ordered);

            var kept = new List<(SpellNode node, int depth)>();
            var keep = ResolveKeptNodes(nodes, filter);
            for (int i = 0; i < ordered.Count; i++)
            {
                if (!keep.Contains(ordered[i].node)) continue;
                kept.Add(ordered[i]);
            }
            if (kept.Count == 0) return;

            string sectionKey = SchoolKeyOf(tree);
            if (!ShowsSection(sectionKey)) return;

            _treeRows.Add(new TreeRow
            {
                Label = string.IsNullOrEmpty(tree.displayName) ? sectionKey : tree.displayName,
                Trailing = kept.Count == ordered.Count
                    ? ordered.Count.ToString()
                    : $"{kept.Count}/{ordered.Count}",
                IsHeader = true,
                SectionKey = sectionKey,
            });

            if (SectionCollapsed(sectionKey)) return;

            for (int i = 0; i < kept.Count; i++)
            {
                var node = kept[i].node;
                _treeRows.Add(new TreeRow
                {
                    SpellKey = node.spell != null ? node.spell.spellKey : null,
                    Label = node.ResolveDisplayName(),
                    Trailing = $"{node.pointCost}p  L{node.levelRequirement}",
                    Depth = kept[i].depth,
                    SectionKey = sectionKey,
                });
            }
        }

        /// <summary>
        /// Depth-first from each root, so a chain reads downward and a fork reads as two
        /// indented runs. Siblings are ordered by <c>row</c> then id, which is the only job
        /// <c>row</c> has and the same one it does in the skill tree HUD.
        /// </summary>
        private static void OrderSchool(List<SpellNode> nodes, List<(SpellNode, int)> into)
        {
            var owned = new HashSet<SpellNode>(nodes);
            var children = new Dictionary<SpellNode, List<SpellNode>>();
            var roots = new List<SpellNode>();

            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                bool hasParent = false;
                var pres = node.prerequisites;
                for (int p = 0; pres != null && p < pres.Length; p++)
                {
                    var parent = pres[p];
                    if (parent == null || !owned.Contains(parent)) continue;
                    hasParent = true;
                    if (!children.TryGetValue(parent, out var list))
                        children[parent] = list = new List<SpellNode>();
                    list.Add(node);
                }
                if (!hasParent) roots.Add(node);
            }

            System.Comparison<SpellNode> bySibling = (a, b) =>
            {
                int byRow = a.row.CompareTo(b.row);
                return byRow != 0 ? byRow : string.CompareOrdinal(a.nodeId, b.nodeId);
            };
            roots.Sort(bySibling);
            foreach (var list in children.Values) list.Sort(bySibling);

            // A node with two parents is emitted under the first one that reaches it. The
            // shipped spell trees have ZERO such merges, but the skill trees have one each,
            // and a shared renderer must not print a node twice if this is ever reused.
            var emitted = new HashSet<SpellNode>();
            for (int i = 0; i < roots.Count; i++)
                Emit(roots[i], 0);

            void Emit(SpellNode node, int depth)
            {
                if (!emitted.Add(node)) return;
                into.Add((node, depth));
                if (!children.TryGetValue(node, out var kids)) return;
                for (int i = 0; i < kids.Count; i++) Emit(kids[i], depth + 1);
            }
        }

        /// <summary>
        /// Which nodes of a school survive the current filters.
        ///
        /// <para>A SEARCH HIT DRAGS ITS ANCESTORS IN WITH IT. Indentation is the only thing
        /// carrying structure in this view, so a match shown alone at depth four is a row
        /// claiming to be four deep into something that is not on screen — measured before
        /// this, searching "meteor" drew <c>Meteor Shower</c> under nothing at all. Keeping
        /// the chain answers the question a search in a TREE is actually asking, which is not
        /// "where is this spell" (the Grid does that) but "what do I have to buy first".</para>
        ///
        /// <para>The audience filter is NOT relaxed the same way: it is a statement about
        /// which spells belong on screen at all, so an ancestor that fails it stays out and
        /// the chain simply starts lower.</para>
        /// </summary>
        private HashSet<SpellNode> ResolveKeptNodes(List<SpellNode> nodes, string filter)
        {
            var owned = new HashSet<SpellNode>(nodes);
            var keep = new HashSet<SpellNode>();

            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                if (!PassesAudience(node)) continue;
                if (filter.Length != 0 && !MatchesTreeSearch(node, filter)) continue;
                KeepWithAncestors(node, owned, keep);
            }
            return keep;
        }

        private void KeepWithAncestors(SpellNode node, HashSet<SpellNode> owned,
            HashSet<SpellNode> keep)
        {
            // Add-first is the cycle guard: a prerequisite loop would otherwise recurse
            // forever, and nothing validates the authored graph against one.
            if (node == null || !keep.Add(node)) return;

            var pres = node.prerequisites;
            for (int p = 0; pres != null && p < pres.Length; p++)
            {
                var parent = pres[p];
                if (parent == null || !owned.Contains(parent)) continue;
                if (!PassesAudience(parent)) continue;
                KeepWithAncestors(parent, owned, keep);
            }
        }

        private bool PassesAudience(SpellNode node)
            => node.spell == null || MatchesAudienceFilter(node.spell, _audienceFilterKey);

        private static bool MatchesTreeSearch(SpellNode node, string filter)
        {
            if (node.nodeId != null && node.nodeId.ToLowerInvariant().Contains(filter)) return true;
            string label = node.ResolveDisplayName();
            if (label != null && label.ToLowerInvariant().Contains(filter)) return true;
            var spell = node.spell;
            return spell != null && spell.spellKey != null &&
                   spell.spellKey.ToLowerInvariant().Contains(filter);
        }

        /// <summary>
        /// Everything in the catalogue that no school claims.
        ///
        /// <para>This is the section the other two views structurally cannot draw, and most of
        /// what lands here is correct: animation probes, hostile and boss spells, and the
        /// innate pair the catalogue grants outright. What it exists to catch is the player
        /// spell that is neither innate nor in a tree — one nobody can ever learn.</para>
        /// </summary>
        private void AppendOrphans(ProgressionCatalog catalog, string filter, HashSet<string> claimed)
        {
            if (_filtered == null || _filtered.Count == 0) return;

            var rows = new List<TreeRow>();
            for (int i = 0; i < _filtered.Count; i++)
            {
                var spell = _filtered[i];
                if (spell == null || string.IsNullOrEmpty(spell.spellKey)) continue;
                if (claimed.Contains(spell.spellKey)) continue;

                bool innate = catalog != null && catalog.IsAlwaysKnown(spell.spellKey);
                rows.Add(new TreeRow
                {
                    SpellKey = spell.spellKey,
                    Label = string.IsNullOrEmpty(spell.displayName) ? spell.spellKey : spell.displayName,
                    Trailing = innate ? "innate" : DescribeAudience(spell.audience),
                    Depth = 0,
                    SectionKey = TREE_SECTION_ORPHANS,
                });
            }
            if (rows.Count == 0 || !ShowsSection(TREE_SECTION_ORPHANS)) return;

            _treeRows.Add(new TreeRow
            {
                Label = "In the catalogue, in no school",
                Trailing = rows.Count.ToString(),
                IsHeader = true,
                IsOrphanSection = true,
                SectionKey = TREE_SECTION_ORPHANS,
            });

            if (SectionCollapsed(TREE_SECTION_ORPHANS)) return;
            for (int i = 0; i < rows.Count; i++) _treeRows.Add(rows[i]);
        }

        private static string DescribeAudience(SpellAudience audience)
        {
            if (audience == SpellAudience.None) return "none";
            var parts = new List<string>(3);
            if ((audience & SpellAudience.Player) != 0) parts.Add("Player");
            if ((audience & SpellAudience.NPC) != 0) parts.Add("NPC");
            if ((audience & SpellAudience.Boss) != 0) parts.Add("Boss");
            return string.Join("+", parts);
        }

        /// <summary>
        /// Whether the active school tab wants this section drawn. A single-school tab shows
        /// its section EXPANDED whatever the collapse state says — collapsing the only thing
        /// on screen leaves a tab that looks broken.
        /// </summary>
        private bool ShowsSection(string sectionKey)
            => _treeSchoolFilter == TREE_SCHOOL_ALL || _treeSchoolFilter == sectionKey;

        private bool SectionCollapsed(string sectionKey)
            => _treeSchoolFilter == TREE_SCHOOL_ALL && _collapsedSchools.Contains(sectionKey);

        private void ToggleTreeSection(string sectionKey)
        {
            // Folding is an "All" gesture only: inside a single-school tab the header is the
            // only row that is not the tree itself, and a click there must not empty the view.
            if (string.IsNullOrEmpty(sectionKey) || _treeSchoolFilter != TREE_SCHOOL_ALL) return;
            if (!_collapsedSchools.Remove(sectionKey)) _collapsedSchools.Add(sectionKey);
            RefreshTree();
        }
    }
}
