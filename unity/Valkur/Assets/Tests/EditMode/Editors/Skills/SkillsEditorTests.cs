using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Data;
using Valkur.Gameplay.Editors.Skills;
using Valkur.UIKit;

namespace Valkur.Tests.EditMode.Editors.Skills
{
    /// <summary>
    /// The Skills editor's behaviour, and specifically the parts an author would lose work to.
    ///
    /// <para>WHAT THESE CANNOT DO, stated up front so nobody trusts them further than they
    /// reach: uGUI performs NO layout in EditMode, so nothing here proves the window is
    /// readable. Every assertion below is about state, wiring and policy — the layout was
    /// verified by rendering a frame in Play Mode and reading the pixels, which is the only
    /// test for a layout and is what caught the selection highlight rendering invisible.</para>
    /// </summary>
    [TestFixture]
    public class SkillsEditorTests
    {
        private GameObject _go;
        private SkillsRuntimeEditor _editor;
        private RecipeCatalog _catalog;

        private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;

        // The shipped assets, as they were before the fixture touched them.
        //
        // THIS IS NOT TIDINESS. These tests drive a live-edit surface over the REAL profession
        // and recipe assets, and the editor marks every change dirty — so a test that failed
        // half way through would leave a mutated asset on disk the next AssetDatabase.SaveAssets
        // would write out. That is the shape of the 216-deleted-building-templates incident, at
        // a smaller scale. Restoring in TearDown runs even when an assertion throws.
        private readonly Dictionary<ProfessionDefinition, (int max, int baseXp, float growth, string station)>
            _professionSnapshot = new Dictionary<ProfessionDefinition, (int, int, float, string)>();
        private readonly Dictionary<RecipeDefinition, (int level, int xp, bool station, float seconds)>
            _recipeSnapshot = new Dictionary<RecipeDefinition, (int, int, bool, float)>();

        [SetUp]
        public void SetUp()
        {
            _catalog = Resources.Load<RecipeCatalog>(RecipeCatalog.ResourcePath);
            if (_catalog == null)
                Assert.Ignore("No RecipeCatalog — run Valkur > Crafting > Import Crafting Content.");

            _professionSnapshot.Clear();
            foreach (var p in _catalog.Professions)
                if (p != null)
                    _professionSnapshot[p] = (p.maxLevel, p.baseXpPerLevel, p.xpGrowth, p.stationName);

            _recipeSnapshot.Clear();
            foreach (var r in _catalog.Recipes)
                if (r != null)
                    _recipeSnapshot[r] = (r.requiredLevel, r.xpReward, r.requiresStation, r.craftSeconds);

            _go = new GameObject("SkillsEditorFixture");
            _editor = _go.AddComponent<SkillsRuntimeEditor>();
            _editor.Activate();

            // The editor RESTORES its search box from the workspace document, which is machine
            // state that survives the run, the Editor and the reboot. A filter left behind by a
            // real session would silently shrink every list these tests count, so the fixture
            // normalises it — the same reason the UI tests in this project delete their
            // PlayerPrefs keys rather than trusting them to be unset.
            _editor.SetSearch(string.Empty);
        }

        [TearDown]
        public void TearDown()
        {
            if (_editor != null) _editor.Deactivate();
            if (_go != null) Object.DestroyImmediate(_go);

            foreach (var kv in _professionSnapshot)
            {
                var p = kv.Key;
                if (p == null) continue;
                (p.maxLevel, p.baseXpPerLevel, p.xpGrowth, p.stationName) =
                    (kv.Value.max, kv.Value.baseXp, kv.Value.growth, kv.Value.station);
            }
            foreach (var kv in _recipeSnapshot)
            {
                var r = kv.Key;
                if (r == null) continue;
                (r.requiredLevel, r.xpReward, r.requiresStation, r.craftSeconds) =
                    (kv.Value.level, kv.Value.xp, kv.Value.station, kv.Value.seconds);
            }
        }

        private T Call<T>(string name, params object[] args)
            => (T)typeof(SkillsRuntimeEditor).GetMethod(name, NP).Invoke(_editor, args);

        private void Call(string name, params object[] args)
            => typeof(SkillsRuntimeEditor).GetMethod(name, NP).Invoke(_editor, args);

        private object Prop(string name)
            => typeof(SkillsRuntimeEditor).GetProperty(name, NP).GetValue(_editor);

        private ProfessionDefinition Cooking => _catalog.GetProfession("cooking");

        // ── It opens at all ─────────────────────────────────────────────────

        [Test]
        public void Activate_BuildsTheUi_AndSelectsATrade()
        {
            Assert.IsTrue(_editor.IsActive);
            Assert.IsNotNull(Prop("SelectedProfession"), "an editor that opens on no trade shows "
                + "an empty screen the author has to fix before it is useful");
            Assert.Greater(_go.GetComponentsInChildren<RectTransform>(true).Length, 20);
        }

        [Test]
        public void EditorName_IsExactlyWhatTheGeneralEditorEntryUses()
        {
            // The launcher is the ONLY way in since the F-row was retired, and the two strings
            // are compared, never derived from one another.
            Assert.AreEqual("Skills", _editor.EditorName);
        }

        // ── Recoverability ──────────────────────────────────────────────────

        /// <summary>
        /// The trap this editor walked into on its first build, and the reason it is pinned:
        /// both panels shipped closable, this editor has no toolbar outside them, and
        /// <c>EditorWorkspaceService</c> persists <c>open:false</c> for inactive panels — so
        /// closing both made the editor open empty FOREVER. Verbatim the Controls editor bug.
        /// </summary>
        [Test]
        public void NeitherPanel_CanBeClosed()
        {
            var panels = _go.GetComponentsInChildren<DraggablePanel>(true);
            Assert.AreEqual(2, panels.Length, "expected exactly the list and detail panels");

            foreach (var panel in panels)
                Assert.IsFalse(panel.ShowCloseButton,
                    $"{panel.name} is closable and nothing can reopen it — the editor would "
                    + "open empty forever once the workspace persisted it closed");
        }

        // ── History ─────────────────────────────────────────────────────────

        [Test]
        public void Edit_IsUndoable_AndRestoresTheExactPreviousValue()
        {
            var cooking = Cooking;
            int before = cooking.maxLevel;

            _editor.SetProfessionMaxLevel(cooking, before + 7);
            Assert.AreEqual(before + 7, cooking.maxLevel);
            Assert.IsTrue((bool)Prop("CanUndo"));

            _editor.UndoLast();
            Assert.AreEqual(before, cooking.maxLevel, "undo must restore the exact prior value");
            Assert.IsTrue((bool)Prop("CanRedo"));

            _editor.RedoLast();
            Assert.AreEqual(before + 7, cooking.maxLevel);

            _editor.UndoLast();   // leave the shipped asset as we found it
            Assert.AreEqual(before, cooking.maxLevel);
        }

        [Test]
        public void UndoingDoesNotPushItsOwnInverseOntoTheStack()
        {
            // Without the _applyingHistory guard the inverse edit records itself, and the
            // author can never get further back than one step.
            var cooking = Cooking;
            int before = cooking.baseXpPerLevel;

            _editor.SetProfessionBaseXp(cooking, before + 10);
            _editor.SetProfessionBaseXp(cooking, before + 20);

            _editor.UndoLast();
            _editor.UndoLast();

            Assert.AreEqual(before, cooking.baseXpPerLevel, "two edits must undo to the original");
            Assert.IsFalse((bool)Prop("CanUndo"), "the stack should be empty, not refilled by the undos");
        }

        [Test]
        public void ANewEdit_DropsTheRedoBranch()
        {
            var cooking = Cooking;
            int before = cooking.baseXpPerLevel;

            _editor.SetProfessionBaseXp(cooking, before + 5);
            _editor.UndoLast();
            Assert.IsTrue((bool)Prop("CanRedo"));

            _editor.SetProfessionBaseXp(cooking, before + 9);
            Assert.IsFalse((bool)Prop("CanRedo"),
                "redoing onto a value the current state never passed through is not a history");

            _editor.UndoLast();
            Assert.AreEqual(before, cooking.baseXpPerLevel);
        }

        [Test]
        public void NoOpEdit_RecordsNoHistory()
        {
            // Committing a value equal to the stored one would fill the stack with steps that
            // change nothing, so Ctrl+Z would appear to do nothing several times in a row.
            var cooking = Cooking;
            _editor.SetProfessionMaxLevel(cooking, cooking.maxLevel);
            Assert.IsFalse((bool)Prop("CanUndo"));
        }

        // ── Clamping ────────────────────────────────────────────────────────

        [Test]
        public void RecipeLevel_IsClampedToItsTradesCap()
        {
            // A recipe requiring a level the trade can never reach is unreachable content that
            // looks perfectly valid in the Inspector.
            var recipe = _catalog.GetById("paella");
            int before = recipe.requiredLevel;

            _editor.SetRecipeRequiredLevel(recipe, 9999);
            Assert.AreEqual(recipe.profession.maxLevel, recipe.requiredLevel);

            _editor.SetRecipeRequiredLevel(recipe, -5);
            Assert.AreEqual(1, recipe.requiredLevel, "level 0 would be below the starting level");

            _editor.SetRecipeRequiredLevel(recipe, before);
        }

        [Test]
        public void ProfessionGrowth_CannotInvertTheCurve()
        {
            // Below 1 the curve inverts: level 10 would cost less than level 2.
            var cooking = Cooking;
            float before = cooking.xpGrowth;

            _editor.SetProfessionGrowth(cooking, 0.2f);
            Assert.GreaterOrEqual(cooking.xpGrowth, 1f);
            Assert.GreaterOrEqual(cooking.XpForNextLevel(9), cooking.XpForNextLevel(1));

            _editor.SetProfessionGrowth(cooking, before);
        }

        // ── List behaviour ──────────────────────────────────────────────────

        [Test]
        public void Search_FiltersByName_AndByCuisine()
        {
            int all = Call<List<RecipeDefinition>>("RecipesForSelected").Count;
            Assert.Greater(all, 1);

            _editor.SetSearch("paella");
            var byName = Call<List<RecipeDefinition>>("RecipesForSelected");
            Assert.AreEqual(1, byName.Count);
            Assert.AreEqual("paella", byName[0].recipeId);

            _editor.SetSearch("spain");
            var byGroup = Call<List<RecipeDefinition>>("RecipesForSelected");
            Assert.AreEqual(6, byGroup.Count, "the Spanish cuisine ships six dishes");
            foreach (var r in byGroup) Assert.AreEqual("spain", r.group);

            _editor.SetSearch("");
            Assert.AreEqual(all, Call<List<RecipeDefinition>>("RecipesForSelected").Count);
        }

        [Test]
        public void Search_IsCaseInsensitive()
        {
            _editor.SetSearch("PAELLA");
            Assert.AreEqual(1, Call<List<RecipeDefinition>>("RecipesForSelected").Count);
            _editor.SetSearch("");
        }

        [Test]
        public void RecipeList_IsGroupedByCuisine_NotFlatAlphabetical()
        {
            // A flat alphabetical list mixes six cuisines together, which is the one ordering
            // that makes 36 rows hard to scan. Assert each group is contiguous.
            var recipes = Call<List<RecipeDefinition>>("RecipesForSelected");
            Assert.Greater(recipes.Count, 6);

            var seen = new HashSet<string>();
            string current = null;
            foreach (var r in recipes)
            {
                if (r.group == current) continue;
                Assert.IsTrue(seen.Add(r.group),
                    $"group '{r.group}' appears in more than one block — the list is not grouped");
                current = r.group;
            }
        }

        [Test]
        public void SelectingARecipe_MakesItTheSelectedOne()
        {
            _editor.SelectRecipe("gazpacho");
            var selected = Prop("SelectedRecipe") as RecipeDefinition;
            Assert.IsNotNull(selected);
            Assert.AreEqual("gazpacho", selected.recipeId);
        }

        [Test]
        public void SwitchingTrade_ClearsTheSelectedRecipe()
        {
            // A recipe id from the previous trade would leave the detail panel describing
            // something that is not in the list beside it.
            _editor.SelectRecipe("gazpacho");
            Assert.IsNotNull(Prop("SelectedRecipe"));

            int other = IndexOfProfession("blacksmith");
            _editor.SelectProfession(other);
            Assert.IsNull(Prop("SelectedRecipe"));

            _editor.SelectProfession(IndexOfProfession("cooking"));
        }

        private int IndexOfProfession(string key)
        {
            for (int i = 0; i < _catalog.Professions.Count; i++)
                if (_catalog.Professions[i] != null && _catalog.Professions[i].professionKey == key)
                    return i;
            Assert.Fail($"no profession '{key}' in the catalog");
            return -1;
        }

        [Test]
        public void EditorListsMalformedRecipes_UnlikeThePlayerPanel()
        {
            // The player's panel hides them because a broken row sends them looking for an
            // ingredient that does not exist. This is the one screen where the breakage can be
            // seen and fixed, so it must NOT filter on IsWellFormed.
            var src = typeof(SkillsRuntimeEditor).Assembly.GetType(
                "Valkur.Gameplay.Editors.Skills.SkillsRuntimeEditor");
            Assert.IsNotNull(src);

            var recipes = Call<List<RecipeDefinition>>("RecipesForSelected");
            var profession = Prop("SelectedProfession") as ProfessionDefinition;
            int inCatalog = 0;
            foreach (var r in _catalog.Recipes)
                if (r != null && r.profession == profession) inCatalog++;

            Assert.AreEqual(inCatalog, recipes.Count,
                "the editor must list every recipe of the trade, well-formed or not");
        }

        // ── Theme ───────────────────────────────────────────────────────────

        [Test]
        public void SelectionTint_GoesThroughTheColorBlock_NotTheGraphic()
        {
            // A Button's ColorTint transition drives its graphic's CanvasRenderer to the
            // ColorBlock and MULTIPLIES with Graphic.color, so writing the graphic renders the
            // product. Measured on the first build: the active row came out (32,31,29) against
            // a (31,33,42) panel — invisible — while untouched rows sat at near-black.
            //
            // WHAT THIS TEST CANNOT SEE, and it is the half that bit twice: whether the tint is
            // actually REPAINTED. Selectable pushes the block to the CanvasRenderer only when it
            // evaluates a state transition, and a component added in EditMode never receives
            // OnEnable at all — so the CanvasRenderer here holds whatever it was constructed
            // with, no matter which branch the production code takes. The second bug (every
            // button rendering pale cream at ~(202,202,205)) was invisible to exactly this
            // assertion and was caught by reading pixels out of a rendered frame.
            var probe = new GameObject("TintProbe", typeof(RectTransform));
            try
            {
                var btn = UIButton.Make(probe.transform, "x", null);
                UIButton.SetTint(btn, UITheme.SLOT_SELECTED);

                Assert.AreEqual(UITheme.SLOT_SELECTED, btn.colors.normalColor,
                    "the tint must land on the ColorBlock, which is what actually renders");
                Assert.AreEqual(Color.white, btn.targetGraphic.color,
                    "the graphic is held at white so the product equals the requested colour");
                Assert.IsTrue(btn.enabled,
                    "SetTint toggles `enabled` to force an instant repaint — leaving it off "
                    + "would make the button inert");
            }
            finally { Object.DestroyImmediate(probe); }
        }
    }
}
