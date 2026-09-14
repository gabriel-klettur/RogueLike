using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.EditorTools.Progression
{
    /// <summary>
    /// The talent branches every class shares, beside its own path. Today one: "Barra de
    /// Guerra", which grows the War action bar.
    ///
    /// <para><b>The shape is two TRACKS of five, not a tree with a crown.</b> The talents window
    /// has to fit 640x360 texels (1080p at three screen pixels a texel), which caps a board at
    /// five columns and four rows. A crown over two chains of five needs either six rows or an
    /// elbow that runs through the first chain's sockets to reach the second — so the branch is
    /// the TAB, and its two trees lie as parallel tracks: columns above, rows below, each read
    /// left to right, each rank opening the next.</para>
    ///
    /// <para><b>The numbers.</b> The bar starts at five sockets by one row
    /// (<see cref="StatCatalog.WarBarBaseColumns"/>). Each "Columna de guerra" adds a socket to
    /// every row, up to ten; each "Fila de guerra" adds a row, up to six. Levels are spread over
    /// the curve so the bar grows as the grimoire does — a bar with sixty sockets at level 5
    /// would be sixty empty sockets — and the twenty points the branch costs compete with the
    /// class path for the same purse, which is the whole question a talent should ask.</para>
    ///
    /// <para>Creation defaults, authored value wins: an existing node is never refilled, the
    /// same contract <see cref="SkillTreeSeeds"/> and every importer here keep. The prerequisite
    /// chain and the tree's index are refreshed, because they are structure rather than tuning.</para>
    /// </summary>
    internal static class SharedBranchSeeds
    {
        public const string Root = "Assets/_Project/Data/Progression/SharedBranches";

        /// <summary>Where the branch's talent icons live (packed by misc.spriteatlas, point filter).</summary>
        public const string IconFolder = "Assets/_Project/Art/Misc/talents/war_bar";

        private sealed class NodeSeed
        {
            public string Id, Name, Description;
            public int Cost = 1, Level, Row, Column;
            public string Prerequisite;
            public StatModifier Modifier;
        }

        private static readonly string[] Numerals = { "I", "II", "III", "IV", "V" };
        private static readonly int[] ColumnLevels = { 2, 5, 9, 14, 20 };
        private static readonly int[] ColumnCosts  = { 1, 1, 2, 2, 3 };
        private static readonly int[] RowLevels    = { 4, 8, 12, 17, 24 };
        private static readonly int[] RowCosts     = { 1, 2, 2, 3, 3 };

        private static readonly string[] ColumnFlavour =
        {
            "Un hueco más al alcance de la mano.",
            "La barra se abre como un abanico de acero.",
            "Donde antes había piedra, ahora hay sitio para otro conjuro.",
            "Las runas se aprietan para dejar paso a una más.",
            "Diez huecos por fila: todo el ancho de una mano entrenada.",
        };

        private static readonly string[] RowFlavour =
        {
            "Una segunda fila, para lo que no cabía en la primera.",
            "Tres hileras de runas, ordenadas como una formación.",
            "La barra crece hacia arriba como una muralla.",
            "Cinco filas: un arsenal que se lee de un vistazo.",
            "Seis hileras. El grimorio entero cabe bajo tus dedos.",
        };

        [MenuItem("Valkur/Progression/Seed Shared Talent Branches")]
        public static void SeedMenu()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ProgressionCatalog>(
                "Assets/_Project/Resources/" + ProgressionCatalog.ResourcePath + ".asset");
            if (catalog == null)
            {
                Debug.LogWarning("[SharedBranchSeeds] No ProgressionCatalog — run 'Seed Progression Content' first.");
                return;
            }

            var trees = BuildAll(out var touched);
            catalog.sharedSkillTrees = trees.ToArray();
            EditorUtility.SetDirty(catalog);

            // Only what this seeder wrote, never AssetDatabase.SaveAssets(): that writes every
            // dirty asset in the project, including another tool's half-finished edit.
            foreach (var o in touched) AssetDatabase.SaveAssetIfDirty(o);
            AssetDatabase.SaveAssetIfDirty(catalog);

            int nodes = 0;
            foreach (var t in trees) nodes += t.Count;
            Debug.Log($"[SharedBranchSeeds] {trees.Count} shared branch(es), {nodes} talents, " +
                      "installed in ProgressionCatalog.sharedSkillTrees.");
        }

        public static List<SkillTree> BuildAll(out List<Object> touched)
        {
            touched = new List<Object>();
            return new List<SkillTree> { BuildWarBar(touched) };
        }

        private static SkillTree BuildWarBar(List<Object> touched)
        {
            ProgressionSeeder.EnsureFolder(Root);
            string folder = Root + "/war_bar";
            ProgressionSeeder.EnsureFolder(folder);

            var seeds = new List<NodeSeed>(10);
            for (int i = 0; i < 5; i++)
            {
                seeds.Add(new NodeSeed
                {
                    Id = "war_bar_columns_" + (i + 1),
                    Name = "Columna de guerra " + Numerals[i],
                    Description = ColumnFlavour[i],
                    Cost = ColumnCosts[i], Level = ColumnLevels[i],
                    Row = 0, Column = i,
                    Prerequisite = i > 0 ? "war_bar_columns_" + i : null,
                    Modifier = StatModifier.Flat(StatKind.WarBarColumns, 1f),
                });
            }
            for (int i = 0; i < 5; i++)
            {
                seeds.Add(new NodeSeed
                {
                    Id = "war_bar_rows_" + (i + 1),
                    Name = "Fila de guerra " + Numerals[i],
                    Description = RowFlavour[i],
                    Cost = RowCosts[i], Level = RowLevels[i],
                    Row = 1, Column = i,
                    Prerequisite = i > 0 ? "war_bar_rows_" + i : null,
                    Modifier = StatModifier.Flat(StatKind.WarBarRows, 1f),
                });
            }

            var byId = new Dictionary<string, SkillNode>();
            foreach (var s in seeds)
            {
                var node = ProgressionSeeder.LoadOrCreate<SkillNode>($"{folder}/{s.Id}.asset", out bool created);
                if (created)
                {
                    node.skillId = s.Id;
                    node.displayName = s.Name;
                    node.description = s.Description;
                    node.pointCost = s.Cost;
                    node.maxRank = 1;
                    node.levelRequirement = s.Level;
                    node.modifiersPerRank = new[] { s.Modifier };
                    node.row = s.Row;
                    node.column = s.Column;
                    EditorUtility.SetDirty(node);
                }
                // The icon is filled when EMPTY, not only on creation: the art arrived after the
                // nodes did, and an authored icon must still win over the generated one.
                // Generated by tools/atlas/icons/build_war_bar_icons.py (24x24 pixel art, x8).
                if (node.icon == null)
                {
                    var icon = AssetDatabase.LoadAssetAtPath<Sprite>($"{IconFolder}/{s.Id}.png");
                    if (icon != null) { node.icon = icon; EditorUtility.SetDirty(node); }
                }
                byId[s.Id] = node;
                touched.Add(node);
            }

            foreach (var s in seeds)
            {
                var node = byId[s.Id];
                var prereqs = s.Prerequisite != null && byId.TryGetValue(s.Prerequisite, out var p)
                    ? new[] { p }
                    : System.Array.Empty<SkillNode>();
                node.prerequisites = prereqs;
                EditorUtility.SetDirty(node);
            }

            var tree = ProgressionSeeder.LoadOrCreate<SkillTree>($"{folder}/war_bar_branch.asset", out bool treeCreated);
            if (treeCreated)
            {
                tree.classKey = "shared";
                tree.displayName = "Barra de Guerra";
                tree.flavour = "Arriba, más huecos por fila. Abajo, más filas. La barra crece con lo que aprendes.";
            }
            var ordered = new SkillNode[seeds.Count];
            for (int i = 0; i < seeds.Count; i++) ordered[i] = byId[seeds[i].Id];
            tree.EditorSetNodes(ordered);
            EditorUtility.SetDirty(tree);
            touched.Add(tree);
            return tree;
        }
    }
}
