using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.HUD;
using Valkur.Gameplay.Spells;
using Valkur.UI.HUD;

namespace Valkur.Tests.EditMode.Game.Data
{
    /// <summary>
    /// Asserts on the SHIPPED grimoire assets rather than on a synthetic fixture, because
    /// every one of these failures looks, from inside the game, exactly like a school the
    /// player has not worked on yet.
    /// </summary>
    [TestFixture]
    public class ShippedGrimoireDataTests
    {
        private static List<SpellTree> Schools()
        {
            var list = new List<SpellTree>();
            foreach (var guid in AssetDatabase.FindAssets("t:SpellTree"))
            {
                var tree = AssetDatabase.LoadAssetAtPath<SpellTree>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (tree != null) list.Add(tree);
            }
            return list;
        }

        private static List<SpellNode> Nodes()
        {
            var list = new List<SpellNode>();
            foreach (var guid in AssetDatabase.FindAssets("t:SpellNode"))
            {
                var node = AssetDatabase.LoadAssetAtPath<SpellNode>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (node != null) list.Add(node);
            }
            return list;
        }

        [Test]
        public void EveryNode_ResolvesAnIcon()
        {
            var nodes = Nodes();
            Assert.Greater(nodes.Count, 0, "no SpellNode assets found — the probe is vacuous");

            var without = new List<string>();
            foreach (var node in nodes)
                if (node.ResolveIcon() == null) without.Add(node.nodeId);

            Assert.IsEmpty(without,
                "the grimoire draws these; a node with no icon is the one slot in the board " +
                "that cannot say what it teaches. Missing: " + string.Join(", ", without));
        }

        [Test]
        public void EverySchool_CarriesTheProseTheGrimoireShows()
        {
            var schools = Schools();
            Assert.AreEqual(9, schools.Count);

            foreach (var tree in schools)
            {
                Assert.IsNotEmpty(tree.displayName, tree.schoolKey + " has no name");
                Assert.IsNotEmpty(tree.flavour,
                    tree.schoolKey + ": the flavour line was authored on all nine and read by " +
                    "nothing for the life of the project; the panel shows it now");
            }
        }

        [Test]
        public void TheBand_HoldsTheTallestShippedSchool_WithoutMuchToSpare()
        {
            // Both directions, and the second one is the half that was wrong for two rounds.
            //
            // TOO SMALL is obvious: a school taller than its column is cut off. TOO LARGE is
            // the one nobody reports, because an over-tall window looks fine in a screenshot
            // and is simply a black slab drawn over the world for nothing - measured at the
            // first cut, 262 texels of band for a worst case of 186, so 29 % of the window
            // was empty on every screen in the game.
            //
            // It is asserted against the SHIPPED schools rather than a constant because the
            // right height is a fact about the data: the day a tenth school adds a depth step
            // this fails, which is exactly when somebody should be looking at it.
            var style = ScriptableObject.CreateInstance<GrimoireStyle>();
            style.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var frame = GrimoireGeometry.Split(GrimoireGeometry.PanelTexels(2), style);
                int band = frame.Board.height;

                float tallest = 0f;
                string tallestKey = "(none)";
                int inspected = 0;
                foreach (var tree in Schools())
                {
                    var placements = SpellGraphLayout.Resolve(tree.Nodes);
                    var measured = GrimoireGeometry.Measure(placements, style);
                    if (measured.Size.y > tallest)
                    {
                        tallest = measured.Size.y;
                        tallestKey = tree.schoolKey;
                    }
                    inspected++;
                }

                Assert.Greater(inspected, 8, "no shipped school was measured");

                // The filter row shares the board's column, so it comes off the same budget.
                float need = tallest + style.filterChipTexels;

                Assert.GreaterOrEqual(band, need,
                    tallestKey + " is " + tallest.ToString("F0") + " texels tall and the " +
                    "filter row takes " + style.filterChipTexels + ", against a band of " +
                    band + ". A constellation cannot be scaled into it either - the fit is " +
                    "bound by WIDTH in every shipped school.");

                Assert.LessOrEqual(band - need, 40f,
                    "the band clears its worst case by " + (band - need).ToString("F0") +
                    " texels. Every one of those is drawn as black over the world on every " +
                    "screen, in every school, forever - PanelBottom is what trims it.");
            }
            finally
            {
                Object.DestroyImmediate(style);
            }
        }

        [Test]
        public void TheRail_FitsEveryShippedSchool_OnOneScreen()
        {
            // The rail does not scroll, and adding a school is a data edit nobody expects to
            // have a layout consequence. It fails loudly here rather than by dropping the
            // ninth school off the bottom of the column in silence - RebuildRail breaks out of
            // its loop when a row would land at a negative y, which is correct and invisible.
            var style = ScriptableObject.CreateInstance<GrimoireStyle>();
            style.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var frame = GrimoireGeometry.Split(GrimoireGeometry.PanelTexels(2), style);

                int rows = 0;
                foreach (var tree in Schools()) { if (tree != null) rows++; }
                Assert.Greater(rows, 8);

                int used = rows * style.railRowTexels + (rows - 1) * style.railRowGapTexels;
                Assert.LessOrEqual(used, frame.Rail.height,
                    rows + " schools need " + used + " texels of rail against a column of " +
                    frame.Rail.height);
            }
            finally
            {
                Object.DestroyImmediate(style);
            }
        }

        [Test]
        public void EverySpellName_CanBeSpelledByTheCaptionFace()
        {
            // A glyph the face does not know is drawn as NOTHING - no error, no placeholder,
            // just a hole in the word. So this is the half no structural probe can reach: the
            // caption object would exist, its string would be right, its rect would be placed,
            // and the name on screen would be missing a letter.
            //
            // It is asserted because 66 of these names were TRANSLATED in one pass, which put
            // accented capitals (A-acute through N-tilde) into strings that had been ASCII for
            // the life of the project. The face carries them; nothing guaranteed that it did.
            var glyphs = HudPixelFont.Glyphs(HudFontFace.Small);

            int inspected = 0;
            foreach (var node in Nodes())
            {
                string drawn = (node.ResolveDisplayName() ?? string.Empty).ToUpperInvariant();
                Assert.IsNotEmpty(drawn, node.nodeId + " has no name to draw");
                Assert.IsTrue(HudPixelFont.CanSpell(drawn, glyphs),
                    node.nodeId + ": '" + drawn + "' has a glyph the caption face cannot " +
                    "spell, which renders as a hole in the word and logs nothing");
                inspected++;
            }

            Assert.Greater(inspected, 60, "no shipped node was inspected");
        }

        [Test]
        public void NoSpellNameOverhangsFarEnoughToTouchItsNeighbour()
        {
            // The wrap breaks on SPACES and deliberately never cuts a word, so a single word
            // wider than the caption box is allowed to overhang rather than be mangled. That
            // is the right trade - a cut word is a different word - but it is only safe while
            // the overhang stays inside the gap between one depth column's captions and the
            // next.
            //
            // Measured on the shipped names, the worst is TELEPORTACION at 51 texels against a
            // 48-texel box: it pokes out 1.5 each side into a 4-texel gap. That is fine and it
            // is CLOSE, which is exactly when a limit is worth writing down - the next long
            // Spanish name is what this exists to catch, and it would show as two spell names
            // running into each other with nothing failing.
            var style = ScriptableObject.CreateInstance<GrimoireStyle>();
            style.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var glyphs = HudPixelFont.Glyphs(HudFontFace.Small);

                float box = GrimoireGeometry.CaptionWidth(style);
                float column = GrimoireGeometry.DepthSpacing(style);
                float ceiling = column;   // touching the neighbouring column is the real failure

                string worst = string.Empty;
                int worstInk = 0;
                foreach (var node in Nodes())
                {
                    string drawn = (node.ResolveDisplayName() ?? string.Empty).ToUpperInvariant();
                    foreach (var word in drawn.Split(' '))
                    {
                        int ink = HudPixelFont.MeasureWidth(word, HudFontFace.Small, glyphs);
                        if (ink > worstInk) { worstInk = ink; worst = word; }
                    }
                }

                Assert.Greater(worstInk, 0, "no shipped name was measured");
                Assert.LessOrEqual(worstInk, ceiling,
                    "'" + worst + "' inks " + worstInk + " texels; a word may overhang its " +
                    box.ToString("F0") + "-texel caption box but not its " +
                    ceiling.ToString("F0") + "-texel column, or two spell names touch");
            }
            finally
            {
                Object.DestroyImmediate(style);
            }
        }

        [Test]
        public void EverySchoolName_FitsItsRailBox_InTexels()
        {
            // This used to be a CHARACTER COUNT, and a character count is the wrong unit: the
            // face is proportional, so seventeen narrow letters fit a box that eleven wide
            // ones overflow. Measured properly, "FORMAS MARCIALES" inks 62 texels into what
            // was a 56-texel box and ran into its own counter on a rendered frame while the
            // count said it was well inside the limit.
            //
            // uGUI still does not lay out in Edit Mode. What makes this honest anyway is that
            // nothing here asks uGUI anything: the ink comes from the same bitmap face the
            // rail draws with, and the box from the same arithmetic the rail places with.
            var style = ScriptableObject.CreateInstance<GrimoireStyle>();
            style.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var frame = GrimoireGeometry.Split(GrimoireGeometry.PanelTexels(2), style);
                int box = GrimoireGeometry.RailNameBox(frame.Rail.width);
                var glyphs = HudPixelFont.Glyphs(HudFontFace.Small);

                int inspected = 0;
                foreach (var tree in Schools())
                {
                    // Upper-cased exactly as the rail draws it. The small face is capitals
                    // only and its Spanish glyphs ARE the capitals, so a lower-case name is a
                    // row of holes rather than a row of small letters.
                    string drawn = tree.displayName.ToUpperInvariant();

                    Assert.IsTrue(HudPixelFont.CanSpell(drawn, glyphs),
                        tree.schoolKey + ": '" + drawn + "' has a glyph the rail face cannot " +
                        "spell, which draws as nothing at all and logs nothing either");

                    int ink = HudPixelFont.MeasureWidth(drawn, HudFontFace.Small, glyphs);
                    Assert.LessOrEqual(ink, box,
                        tree.schoolKey + ": '" + drawn + "' inks " + ink +
                        " texels into a rail box of " + box);
                    inspected++;
                }

                Assert.Greater(inspected, 8,
                    "a fixture that walks the shipped schools and finds none of them passes " +
                    "while proving nothing");
            }
            finally
            {
                Object.DestroyImmediate(style);
            }
        }

        [Test]
        public void EverySchoolName_AndItsProse_AreInSpanish()
        {
            // A cheap, honest check: the English names and flavour lines that shipped all
            // contain one of these words, and none of the Spanish ones do. It is a tripwire
            // for a re-seed reverting the translation, not a language detector.
            string[] english =
            {
                "Pyromancy", "Cryomancy", "Stormcalling", "Umbramancy", "Verdant Rites",
                "Martial Forms", "Inner Fire", "Radiance", "Arcana",
                " the ", " of ", " is ", " not ", " and ",
            };

            foreach (var tree in Schools())
            {
                string haystack = " " + tree.displayName + " " + tree.flavour + " ";
                foreach (var word in english)
                    StringAssert.DoesNotContain(word, haystack,
                        tree.schoolKey + " reads as English: '" + tree.displayName + " / " +
                        tree.flavour + "'");
            }
        }

        [Test]
        public void EveryRole_HasAName_InBothLanguages()
        {
            // GameLanguage reads PlayerPrefs, which is MACHINE state and survives the run: a
            // test that asserted on Spanish would fail on a machine whose owner had picked
            // English, for a reason nothing in its name mentions. Force it, and restore it in
            // a finally — a probe that flips a global and throws has changed the Editor for
            // every test after it.
            string before = GameLanguage.Current;
            try
            {
                // Asserted against the table, not against "it differs from the enum name":
                // Control is the same word in both languages, so that rule fails a correct
                // translation. What matters is that every value has a deliberate answer.
                GameLanguage.Set(GameLanguage.SPANISH);
                Assert.AreEqual("Daño",        GrimoireText.Role(SpellRole.Damage));
                Assert.AreEqual("Control",     GrimoireText.Role(SpellRole.Control));
                Assert.AreEqual("Protección",  GrimoireText.Role(SpellRole.Protection));
                Assert.AreEqual("Curación",    GrimoireText.Role(SpellRole.Healing));
                Assert.AreEqual("Movilidad",   GrimoireText.Role(SpellRole.Mobility));
                Assert.AreEqual("Invocación",  GrimoireText.Role(SpellRole.Summon));
                Assert.AreEqual("Utilidad",    GrimoireText.Role(SpellRole.Utility));

                GameLanguage.Set(GameLanguage.ENGLISH);
                foreach (SpellRole role in System.Enum.GetValues(typeof(SpellRole)))
                    Assert.IsNotEmpty(GrimoireText.Role(role), role + " in English");
            }
            finally
            {
                GameLanguage.Set(before);
            }
        }

        [Test]
        public void ThePurse_ResolvesItsPlural_InsteadOfParenthesisingIt()
        {
            string before = GameLanguage.Current;
            try
            {
                GameLanguage.Set(GameLanguage.SPANISH);
                Assert.AreEqual("1 punto arcano", GrimoireText.Points(1));
                Assert.AreEqual("3 puntos arcanos", GrimoireText.Points(3));

                GameLanguage.Set(GameLanguage.ENGLISH);
                Assert.AreEqual("1 arcane point", GrimoireText.Points(1));
                Assert.AreEqual("3 arcane points", GrimoireText.Points(3));

                foreach (var n in new[] { 0, 1, 2, 7 })
                    StringAssert.DoesNotContain("(s)", GrimoireText.Points(n),
                        "'arcane point(s)' is a placeholder, not a plural");
            }
            finally
            {
                GameLanguage.Set(before);
            }
        }

        [Test]
        public void NoNodeId_IsDuplicated()
        {
            // Learned nodes are saved by id. Two nodes sharing one means buying either marks
            // both, in silence, forever.
            var seen = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var node in Nodes())
            {
                Assert.IsNotEmpty(node.nodeId, node.name + " has no id to save it under");
                Assert.IsFalse(seen.ContainsKey(node.nodeId),
                    "duplicate nodeId '" + node.nodeId + "': " + node.name +
                    (seen.ContainsKey(node.nodeId) ? " and " + seen[node.nodeId] : string.Empty));
                seen[node.nodeId] = node.name;
            }
        }

        [Test]
        public void EverySchool_IsReachableFromTheProgressionCatalog()
        {
            var catalogs = AssetDatabase.FindAssets("t:ProgressionCatalog");
            Assert.Greater(catalogs.Length, 0, "no ProgressionCatalog shipped");

            var catalog = AssetDatabase.LoadAssetAtPath<ProgressionCatalog>(
                AssetDatabase.GUIDToAssetPath(catalogs[0]));
            Assert.IsNotNull(catalog);

            var referenced = new HashSet<SpellTree>();
            if (catalog.spellTrees != null)
                foreach (var t in catalog.spellTrees) if (t != null) referenced.Add(t);

            foreach (var tree in Schools())
                Assert.IsTrue(referenced.Contains(tree),
                    tree.schoolKey + " exists on disk and no character can ever open it");
        }
    }
}
