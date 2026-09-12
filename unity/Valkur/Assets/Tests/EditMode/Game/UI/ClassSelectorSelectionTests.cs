using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.UI.MainMenu;

namespace Valkur.Tests.EditMode.Game.UI
{
    /// <summary>
    /// The class selector's four parallel lists must stay parallel — and the one that carries the
    /// KEYS is the one the whole screen is useless without.
    ///
    /// <para><b>What this caught.</b> <c>_classKeys</c> was cleared by the builder and never added
    /// to. Six cards were drawn from the catalogue directly, so the screen looked finished: the
    /// highlight moved, the cards scaled, the stat bars were right. But every reader of that list
    /// opens with <c>if (_selectedClassIndex &gt;= _classKeys.Count) return;</c>, so an empty list
    /// silently disabled the header portrait, the chosen-class name, the identity accent and —
    /// the one that is not cosmetic — <c>ApplySelectedClassAndStartGame</c>'s call to
    /// <c>PlayerSelectionState.SetSelectedPlayer</c>. Picking a class recorded nothing, and the
    /// run started as whatever class had been stored last.</para>
    ///
    /// <para>It survived because nothing fails: an empty list is a legal list, an early return is
    /// a legal return, and the screen a player looks at is complete. This is the shape this
    /// project already records for the spawners' drift and for <c>animation_map.json</c> —
    /// authored, drawn, round-tripped and inert.</para>
    ///
    /// <para>Asserted on COUNTS and CONTENTS, never on a rect: uGUI performs no layout in Edit
    /// Mode.</para>
    /// </summary>
    public class ClassSelectorSelectionTests
    {
        private const BindingFlags Priv = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        private GameObject _root;
        private MainMenuUI _menu;

        [SetUp]
        public void SetUp()
        {
            var existing = Object.FindObjectOfType<MainMenuUI>();
            if (existing != null) Object.DestroyImmediate(existing.gameObject);

            _root = new GameObject("ClassSelectorHost");
            _menu = _root.AddComponent<MainMenuUI>();
            // Start by REFLECTION first: Unity calls no Start on a component added in Edit Mode,
            // and without it `_canvasTransform` is null — which makes `EnsureScreenBuilt` return
            // on its first line and build NOTHING. The first draft of this fixture skipped it,
            // and two of its four assertions passed over four empty lists: a parity check between
            // 0 and 0 is the vacuous-fixture shape this project already records for
            // `EditorReachabilityTests`.
            typeof(MainMenuUI).GetMethod("Start", Priv).Invoke(_menu, null);
            _menu.BuildAllScreensForTests();
        }

        [TearDown]
        public void TearDown()
        {
            // DestroyImmediate: Object.Destroy is an outright ERROR in Edit Mode.
            if (_root != null) Object.DestroyImmediate(_root);
        }

        private IList Field(string name) =>
            (IList)typeof(MainMenuUI).GetField(name, Priv).GetValue(_menu);

        [Test]
        public void TheKeyList_IsFilled_NotMerelyCleared()
        {
            var keys = Field("_classKeys");
            Assert.Greater(keys.Count, 0,
                "the class selector built its cards and recorded no keys, so every reader of the " +
                "selection returns early and choosing a class records nothing");
        }

        [Test]
        public void TheFourParallelLists_AreTheSameLength()
        {
            int keys = Field("_classKeys").Count;
            foreach (var name in new[] { "_classButtons", "_classCardFrames", "_classCardAccents" })
                Assert.AreEqual(keys, Field(name).Count,
                    name + " is not index-parallel with _classKeys, so a selection index means " +
                    "two different classes depending on which list is asked");
        }

        [Test]
        public void TheKeys_AreTheCatalogue_InOrder()
        {
            var keys = Field("_classKeys");
            var presets = PlayerClassCatalog.AllPresets;
            Assert.AreEqual(presets.Count, keys.Count);
            for (int i = 0; i < presets.Count; i++)
                Assert.AreEqual(presets[i].PlayerKey, keys[i] as string,
                    "card " + i + " draws one class and its key names another");
        }

        /// <summary>
        /// The reader that is not cosmetic: every index the screen can select must resolve to a
        /// key the selection state can be given. This is the assertion that would have failed.
        /// </summary>
        [Test]
        public void EverySelectableIndex_ResolvesToAKey()
        {
            var keys = Field("_classKeys");
            var set = typeof(MainMenuUI).GetMethod("SetSelectedClassIndex", Priv);
            var index = typeof(MainMenuUI).GetField("_selectedClassIndex", Priv);
            Assert.IsNotNull(set, "SetSelectedClassIndex is what the cards and the arrows call");

            for (int i = 0; i < keys.Count; i++)
            {
                set.Invoke(_menu, new object[] { i });
                int live = (int)index.GetValue(_menu);
                Assert.AreEqual(i, live, "the selector refused an index one of its own cards offers");
                Assert.Less(live, keys.Count,
                    "the selected index is outside the key list, which is exactly the condition " +
                    "every reader early-returns on");
            }
        }
    }
}
