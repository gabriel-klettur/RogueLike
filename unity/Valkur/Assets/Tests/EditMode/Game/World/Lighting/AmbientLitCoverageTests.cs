using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay;
using Valkur.Gameplay.World.Layering;

namespace Valkur.Tests.EditMode.Game.World.Lighting
{
    /// <summary>
    /// A LIT sprite on a sorting layer the ambient light does not reach renders BLACK — not
    /// dim, black — and nothing logs it. On 2026-09-08 nine sorting layers were added for
    /// buildings and every building in the world went black at 08:27 with the tiles around
    /// them lit, because the ambient mask was a hand-written ALLOWLIST of twelve names and the
    /// wiring test compared the scene against that same list, one half against itself.
    ///
    /// <para>The mask is derived now (every layer in TagManager minus an explicit unlit set),
    /// and this fixture pins the property from the side that can actually be wrong: the
    /// places code puts a lit renderer. Each test names one such place and asserts the slot it
    /// resolves to is lit. A new sorting layer cannot fail these by omission; only a name
    /// added to the unlit set can, and that is a decision somebody wrote down.</para>
    /// </summary>
    [TestFixture]
    public class AmbientLitCoverageTests
    {
        private static HashSet<string> Lit()   => new HashSet<string>(GameplaySceneSetup.AmbientLitSortingLayerNames());
        private static HashSet<string> Unlit() => new HashSet<string>(GameplaySceneSetup.AmbientUnlitSortingLayerNames());

        // ── The partition ────────────────────────────────────────────────────

        [Test]
        public void EverySortingLayer_IsEitherLit_OrExplicitlyUnlit_NeverNeither()
        {
            var lit = Lit();
            var unlit = Unlit();
            var neither = new List<string>();
            var both = new List<string>();

            foreach (var layer in SortingLayer.layers)
            {
                bool l = lit.Contains(layer.name), u = unlit.Contains(layer.name);
                if (!l && !u) neither.Add(layer.name);
                if (l && u) both.Add(layer.name);
            }

            Assert.IsEmpty(neither,
                "These sorting layers are neither lit nor declared unlit, so a lit sprite on them " +
                "renders black with nothing logged: " + string.Join(", ", neither));
            Assert.IsEmpty(both, "Lit and unlit overlap on: " + string.Join(", ", both));
        }

        [Test]
        public void EveryUnlitName_ExistsInTagManager()
        {
            // A denylist entry that names no real layer is a layer that quietly goes LIT. The
            // mild failure, but the same silence.
            var missing = new List<string>();
            foreach (var name in Unlit())
                if (SortingLayer.NameToID(name) == 0 && name != "Default") missing.Add(name);

            Assert.IsEmpty(missing,
                "AmbientUnlitSortingLayers names sorting layers TagManager does not carry: " +
                string.Join(", ", missing));
        }

        [Test]
        public void TheUnlitSet_IsSmall_AndEveryEntryHasAReasonInTheDeclaration()
        {
            // Not a content pin — the four names are free to change — but a denylist that
            // grows past a handful is an allowlist wearing a different hat, and the source
            // declaration is where each reason is written down.
            Assert.LessOrEqual(Unlit().Count, 6,
                "The unlit set is meant to be the short list of layers that are emissive or " +
                "UI by art direction. If it needs to grow, write the reason beside the name.");
        }

        // ── The places a lit renderer can be put ────────────────────────────

        [Test]
        public void EveryBuildingZ_LandsOnALitSortingLayer()
        {
            // THE regression. Buildings render through WorldSpriteMaterials, which goes lit
            // whenever a Global Light 2D exists — so each PropsL* slot has to be in the mask or
            // every building whose Z resolves to it is a black silhouette.
            var lit = Lit();
            var dark = new List<string>();
            for (int z = 0; z <= SortingConfig.MAX_VISUAL_LAYER; z++)
            {
                string slot = SortingConfig.PropSortingLayer(z);
                if (!lit.Contains(slot)) dark.Add("Z " + z + " -> " + slot);
            }

            Assert.IsEmpty(dark,
                "A building at these Z values renders BLACK, because its sorting layer is outside " +
                "the ambient light's mask: " + string.Join(", ", dark));
        }

        [Test]
        public void EveryVisualLayer_AnEntityCanClimbTo_IsLit()
        {
            // The player and every NPC render lit and Y-sorted on the slot VisualLayerSortingSync
            // hands them for their visual layer. Visual layer 7 used to resolve to Projectiles —
            // deliberately unlit, spell art is emissive — so a character who climbed to 7 turned
            // black. Nothing had ever climbed there; this is what makes sure nobody finds out
            // the day something does.
            var field = typeof(VisualLayerSortingSync).GetField("SortingLayerByVisualLayer",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(field, "VisualLayerSortingSync.SortingLayerByVisualLayer is gone.");
            var table = field.GetValue(null) as string[];
            Assert.IsNotNull(table);
            Assert.AreEqual(SortingConfig.MAX_VISUAL_LAYER + 1, table.Length,
                "One entry per painted visual layer.");

            var lit = Lit();
            var dark = new List<string>();
            for (int v = 0; v < table.Length; v++)
                if (!lit.Contains(table[v])) dark.Add("visual layer " + v + " -> " + table[v]);

            Assert.IsEmpty(dark,
                "An entity on these visual layers renders BLACK: " + string.Join(", ", dark));
        }

        [Test]
        public void TheDefaultEntitySlot_AndTheGroundTheWorldIsPaintedOn_AreLit()
        {
            // The two layers that would take the whole game with them.
            var lit = Lit();
            Assert.IsTrue(lit.Contains(SortingConfig.LAYER_ENTITIES), "Entities must be lit.");
            Assert.IsTrue(lit.Contains(SortingConfig.LAYER_GROUND),   "Ground must be lit.");
        }
    }
}
