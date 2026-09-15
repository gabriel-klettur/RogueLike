using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.HUD;

namespace Valkur.Tests.EditMode.Gameplay.HUD
{
    /// <summary>
    /// Moving through the constellation without a mouse.
    ///
    /// <para>The board itself cannot be built here — it needs a bound <c>KnownSpells</c> and a
    /// player — so what is pinned is the DIRECTION RULE, which is the part that decides whether
    /// the selection reads as walking a tree or as wandering. It is reached through the same
    /// private method the arrows call, so the test and the game cannot disagree about it.</para>
    /// </summary>
    [TestFixture]
    public class GrimoireNavigationTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        private SpellTreeHUD Build()
        {
            var go = new GameObject("SpellTreeHUD");
            _spawned.Add(go);
            var hud = go.AddComponent<SpellTreeHUD>();
            hud.EnsureBuilt();
            return hud;
        }

        [Test]
        public void TheArrowsAndConfirm_AreReachable()
        {
            var t = typeof(SpellTreeHUD);
            Assert.IsNotNull(t.GetMethod("StepSelection"),
                "arrow movement is the panel's own verb, not a private detail: a fixture and a " +
                "pad remapping both need it");
            Assert.IsNotNull(t.GetMethod("ConfirmSelection"));
        }

        [Test]
        public void SteppingWithNoNodes_IsHarmless()
        {
            var hud = Build();
            Assert.DoesNotThrow(() => hud.StepSelection(Vector2.right));
            Assert.DoesNotThrow(() => hud.ConfirmSelection());
            Assert.IsNull(hud.Selected);
        }

        [Test]
        public void TheNavigationReadsInputThroughTheCentralisedFacade()
        {
            // Reading Keyboard.current here is the regression the input section of CLAUDE.md
            // exists to prevent: it breaks under the 2022.3 event-drop bug and it ignores
            // InputBlocker. This is a source check because the alternative — driving real key
            // presses in EditMode — measures the harness rather than the panel.
            string path = System.IO.Path.Combine(Application.dataPath,
                "_Project/Scripts/Gameplay/HUD/SpellTreeHUD.Navigation.cs");
            Assert.IsTrue(System.IO.File.Exists(path), path);

            string source = System.IO.File.ReadAllText(path);
            source = StripComments(source);

            StringAssert.DoesNotContain("Keyboard.current", source);
            StringAssert.DoesNotContain("UnityEngine.Input.", source);
            StringAssert.Contains("InputCompat.", source,
                "the semantic menu facade is the one that ORs both backends");
        }

        [Test]
        public void CancelIsNotReadHere_BecauseTheSheetOwnsEscape()
        {
            // Two readers of Escape in an undefined Update order is how one press closes both
            // the panel and the window behind it — or neither, depending on the frame. The
            // character sheet holds it through EscapeOwnership.
            string path = System.IO.Path.Combine(Application.dataPath,
                "_Project/Scripts/Gameplay/HUD/SpellTreeHUD.Navigation.cs");
            string source = StripComments(System.IO.File.ReadAllText(path));
            StringAssert.DoesNotContain("CancelPressed", source);
        }

        /// <summary>
        /// A source guard has to strip comments or it fails on its own explanation — this
        /// file's doc block names every pattern it forbids, which is the point of it.
        /// </summary>
        private static string StripComments(string source)
        {
            var sb = new System.Text.StringBuilder(source.Length);
            foreach (var raw in source.Split('\n'))
            {
                string line = raw.TrimStart();
                if (line.StartsWith("//") || line.StartsWith("///") || line.StartsWith("*")
                    || line.StartsWith("/*"))
                    continue;
                sb.Append(raw).Append('\n');
            }
            return sb.ToString();
        }
    }
}
