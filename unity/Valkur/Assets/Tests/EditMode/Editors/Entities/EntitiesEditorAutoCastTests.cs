using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Valkur.Data;
using Valkur.Gameplay.Entities;

namespace Valkur.Tests.EditMode.Editors.Entities
{
    /// <summary>
    /// Covers the F5 Auto-Cast authoring loop added to close audit dimension 12
    /// ("NPC spellcasting + boss phases") — <c>MonsterDefinition.autoCast</c> and
    /// <c>autoCastList</c> used to be render-only: the properties panel could show them
    /// but nothing could WRITE them, so all 19 shipped monsters shipped autoCast=false
    /// with an empty list and there was no working example to copy.
    ///
    /// The list is edited through a dropdown of catalog keys rather than free text — the
    /// widget itself is the validation. The data-level seam
    /// (<c>TryAddAutoCastSpell</c> / <c>TrySetAutoCastSpellAt</c>) is exercised here via
    /// reflection (mirroring <c>EntitiesEditorAuthoringTests</c>) so it still refuses an
    /// unresolvable key even if a future caller feeds it something other than the dropdown.
    ///
    /// Only ONE <see cref="EntitiesRuntimeEditor"/> is ever alive per test — every test shares
    /// the single instance <see cref="SetUp"/> creates and destroys it in <see cref="TearDown"/>.
    /// A second live instance mid-test would hit SingletonMonoBehaviour's duplicate guard, which
    /// calls the deferred <c>Object.Destroy</c> and logs an error outside Play Mode.
    ///
    /// The SpellCatalog is deliberately NOT auto-injected in <see cref="SetUp"/>: the real
    /// <c>Assets/_Project/Data/Catalogs/SpellCatalog.asset</c> exists on disk, and
    /// <c>EntitiesRuntimeEditor.ResolveSpellCatalogFallback</c> would find it the moment
    /// <c>ShowMonsterProperties</c> runs if the field were still null — masking the "no
    /// catalog resolved" and "empty catalog" cases this fixture wants to test in isolation.
    /// Each test injects the fixture catalog explicitly via <c>SetSpellCatalog</c>.
    /// </summary>
    [TestFixture]
    public class EntitiesEditorAutoCastTests
    {
        private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;

        private GameObject _editorGo;
        private EntitiesRuntimeEditor _ed;
        private SpellCatalog _spellCatalog;
        private SpellDefinition _iceball;
        private SpellDefinition _fireball;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;

            _editorGo = new GameObject("EntitiesAutoCastUnderTest");
            _ed = _editorGo.AddComponent<EntitiesRuntimeEditor>();

            _iceball = ScriptableObject.CreateInstance<SpellDefinition>();
            _iceball.spellKey = "iceball";
            _fireball = ScriptableObject.CreateInstance<SpellDefinition>();
            _fireball.spellKey = "fireball";

            _spellCatalog = ScriptableObject.CreateInstance<SpellCatalog>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_editorGo != null) Object.DestroyImmediate(_editorGo);
            if (_spellCatalog != null) Object.DestroyImmediate(_spellCatalog);
            if (_iceball != null) Object.DestroyImmediate(_iceball);
            if (_fireball != null) Object.DestroyImmediate(_fireball);
            LogAssert.ignoreFailingMessages = false;
        }

        // ── Reflection helpers ───────────────────────────────────────────────────

        private static object Invoke(object target, string method, params object[] args)
        {
            var m = target.GetType().GetMethod(method, NP);
            Assert.IsNotNull(m, $"method '{method}' must exist on {target.GetType().Name}");
            return m.Invoke(target, args);
        }

        private static void SetPrivateField(object target, string name, object value)
        {
            var f = target.GetType().GetField(name, NP);
            Assert.IsNotNull(f, $"field '{name}' must exist on {target.GetType().Name}");
            f.SetValue(target, value);
        }

        private static object GetPrivateField(object target, string name)
        {
            var f = target.GetType().GetField(name, NP);
            Assert.IsNotNull(f, $"field '{name}' must exist on {target.GetType().Name}");
            return f.GetValue(target);
        }

        private static MonsterDefinition MakeDef(string key)
        {
            var def = ScriptableObject.CreateInstance<MonsterDefinition>();
            def.monsterKey = key;
            return def;
        }

        /// <summary>Populates <see cref="_spellCatalog"/> with iceball + fireball and injects it.</summary>
        private void InjectPopulatedCatalog()
        {
#if UNITY_EDITOR
            _spellCatalog.SetSpells(new[] { _iceball, _fireball });
#endif
            _ed.SetSpellCatalog(_spellCatalog);
        }

        // ── The toggle widget ────────────────────────────────────────────────────

        [Test]
        public void AddToggleRow_ReflectsInitialValue_AndCommitsOnChange()
        {
            var canvasGo = new GameObject("canvas", typeof(Canvas));
            try
            {
                var section = new GameObject("section", typeof(RectTransform))
                    .GetComponent<RectTransform>();
                section.SetParent(canvasGo.transform, false);

                bool? committed = null;
                var toggle = EntitiesEditorUIBuilder.AddToggleRow(section, "Enabled", true, v => committed = v);

                Assert.IsNotNull(toggle, "the row must contain a real Toggle");
                Assert.IsTrue(toggle.isOn, "the widget must open on the current value");

                toggle.isOn = false; // setter alone fires onValueChanged, like a real click

                Assert.AreEqual(false, committed, "flipping the toggle must reach the caller's handler");
            }
            finally
            {
                Object.DestroyImmediate(canvasGo);
            }
        }

        [Test]
        public void AutoCastToggle_WritesTheDefinition()
        {
            var canvasGo = new GameObject("canvas", typeof(Canvas));
            var def = MakeDef("probe");
            try
            {
                def.autoCast = false;

                var section = new GameObject("section", typeof(RectTransform))
                    .GetComponent<RectTransform>();
                section.SetParent(canvasGo.transform, false);

                Invoke(_ed, "AddBoolStat", section, "Enabled", def.autoCast,
                       (System.Action<bool>)(v => def.autoCast = v), def);

                var toggle = section.GetComponentInChildren<Toggle>(true);
                Assert.IsNotNull(toggle, "AddBoolStat must build a real Toggle");

                toggle.isOn = true;

                Assert.IsTrue(def.autoCast, "flipping the row must write straight back to the definition");
            }
            finally
            {
                Object.DestroyImmediate(def);
                Object.DestroyImmediate(canvasGo);
            }
        }

        // ── autoCastList: add ────────────────────────────────────────────────────

        [Test]
        public void TryAddAutoCastSpell_RefusesUnknownKey_LeavingTheListUnchanged()
        {
            InjectPopulatedCatalog();
            var def = MakeDef("probe");
            try
            {
                def.autoCastList = System.Array.Empty<string>();

                var result = (bool)Invoke(_ed, "TryAddAutoCastSpell", def, "not_a_real_spell");

                Assert.IsFalse(result, "an unresolvable key must be refused");
                Assert.AreEqual(0, def.autoCastList.Length,
                    "a refused key must not reach the list — ConfigureMonsterAutoCast would " +
                    "otherwise silently skip it at spawn time, which is the exact failure mode " +
                    "this widget exists to prevent at author time.");
            }
            finally
            {
                Object.DestroyImmediate(def);
            }
        }

        [Test]
        public void TryAddAutoCastSpell_AcceptsAValidKey_AndAppendsIt()
        {
            InjectPopulatedCatalog();
            var def = MakeDef("probe");
            try
            {
                def.autoCastList = System.Array.Empty<string>();

                var result = (bool)Invoke(_ed, "TryAddAutoCastSpell", def, "iceball");

                Assert.IsTrue(result, "a key present in the injected SpellCatalog must be accepted");
                Assert.AreEqual(1, def.autoCastList.Length);
                Assert.AreEqual("iceball", def.autoCastList[0]);
            }
            finally
            {
                Object.DestroyImmediate(def);
            }
        }

        [Test]
        public void TryAddAutoCastSpell_RefusesADuplicate()
        {
            InjectPopulatedCatalog();
            var def = MakeDef("probe");
            try
            {
                def.autoCastList = new[] { "iceball" };

                var result = (bool)Invoke(_ed, "TryAddAutoCastSpell", def, "iceball");

                Assert.IsFalse(result, "the same spell twice would waste a spell-caster slot on a repeat");
                Assert.AreEqual(1, def.autoCastList.Length);
            }
            finally
            {
                Object.DestroyImmediate(def);
            }
        }

        [Test]
        public void TryAddAutoCastSpell_NoCatalogInjected_Refuses()
        {
            // Deliberately no InjectPopulatedCatalog() call — _spellCatalog stays null on _ed,
            // and TryAddAutoCastSpell never calls the AssetDatabase fallback (only
            // ShowMonsterProperties does), so this genuinely exercises the "nothing to
            // validate against" branch rather than silently hitting the real shipped catalog.
            var def = MakeDef("probe");
            try
            {
                def.autoCastList = System.Array.Empty<string>();

                var result = (bool)Invoke(_ed, "TryAddAutoCastSpell", def, "iceball");

                Assert.IsFalse(result, "with no SpellCatalog resolved, nothing can be validated as real");
                Assert.AreEqual(0, def.autoCastList.Length);
            }
            finally
            {
                Object.DestroyImmediate(def);
            }
        }

        // ── autoCastList: edit / remove ──────────────────────────────────────────

        [Test]
        public void TrySetAutoCastSpellAt_SwapsAValidEntry()
        {
            InjectPopulatedCatalog();
            var def = MakeDef("probe");
            try
            {
                def.autoCastList = new[] { "fireball" };

                var result = (bool)Invoke(_ed, "TrySetAutoCastSpellAt", def, 0, "iceball");

                Assert.IsTrue(result);
                Assert.AreEqual("iceball", def.autoCastList[0]);
            }
            finally
            {
                Object.DestroyImmediate(def);
            }
        }

        [Test]
        public void TrySetAutoCastSpellAt_RefusesUnknownKey()
        {
            InjectPopulatedCatalog();
            var def = MakeDef("probe");
            try
            {
                def.autoCastList = new[] { "fireball" };

                var result = (bool)Invoke(_ed, "TrySetAutoCastSpellAt", def, 0, "not_a_real_spell");

                Assert.IsFalse(result);
                Assert.AreEqual("fireball", def.autoCastList[0], "a refused swap must leave the slot alone");
            }
            finally
            {
                Object.DestroyImmediate(def);
            }
        }

        [Test]
        public void RemoveAutoCastSpellAt_RemovesTheEntry()
        {
            // No catalog needed — removal never revalidates against it.
            var def = MakeDef("probe");
            try
            {
                def.autoCastList = new[] { "fireball", "iceball" };

                Invoke(_ed, "RemoveAutoCastSpellAt", def, 0);

                Assert.AreEqual(1, def.autoCastList.Length);
                Assert.AreEqual("iceball", def.autoCastList[0]);
            }
            finally
            {
                Object.DestroyImmediate(def);
            }
        }

        // ── Properties panel integration ─────────────────────────────────────────

        [Test]
        public void ShowMonsterProperties_RendersOneDropdownPerEntry_PlusAnAddRow()
        {
            InjectPopulatedCatalog();

            var monsterCatalog = ScriptableObject.CreateInstance<MonsterCatalog>();
            var def = MakeDef("probe_caster");
            try
            {
                def.autoCast = true;
                def.autoCastList = new[] { "iceball" };
                monsterCatalog.UpsertDefinition(def);

                SetPrivateField(_ed, "_monsterCatalog", monsterCatalog);

                Invoke(_ed, "Start");
                Invoke(_ed, "ShowMonsterProperties", "probe_caster");

                var ui = (EntitiesEditorUIBuilder.UIRefs)GetPrivateField(_ed, "_ui");

                var dropdowns = ui.PropsAutoCastSection.GetComponentsInChildren<TMP_Dropdown>(true);
                // One dropdown per existing entry (1) + one "add new spell" dropdown = 2.
                Assert.AreEqual(2, dropdowns.Length,
                    "one dropdown per autoCastList entry plus the add-new-spell row");

                var toggle = ui.PropsAutoCastSection.GetComponentInChildren<Toggle>(true);
                Assert.IsNotNull(toggle, "the Enabled toggle must be present");
                Assert.IsTrue(toggle.isOn, "must reflect def.autoCast == true");
            }
            finally
            {
                Object.DestroyImmediate(def);
                Object.DestroyImmediate(monsterCatalog);
            }
        }

        [Test]
        public void ShowMonsterProperties_EmptySpellCatalog_ShowsHintRow_NoDropdowns_NoThrow()
        {
            // _spellCatalog is injected but deliberately never populated via SetSpells — the
            // catalog resolves (so the AssetDatabase fallback never fires) but GetAllKeys()
            // is empty, exercising the "no spells in catalog" hint-row branch.
            _ed.SetSpellCatalog(_spellCatalog);

            var monsterCatalog = ScriptableObject.CreateInstance<MonsterCatalog>();
            var def = MakeDef("probe_no_spells");
            try
            {
                def.autoCast = false;
                def.autoCastList = System.Array.Empty<string>();
                monsterCatalog.UpsertDefinition(def);

                SetPrivateField(_ed, "_monsterCatalog", monsterCatalog);

                Assert.DoesNotThrow(() =>
                {
                    Invoke(_ed, "Start");
                    Invoke(_ed, "ShowMonsterProperties", "probe_no_spells");
                });

                var ui = (EntitiesEditorUIBuilder.UIRefs)GetPrivateField(_ed, "_ui");

                var dropdowns = ui.PropsAutoCastSection.GetComponentsInChildren<TMP_Dropdown>(true);
                Assert.AreEqual(0, dropdowns.Length,
                    "with zero catalog keys there must be no add-row dropdown either");
            }
            finally
            {
                Object.DestroyImmediate(def);
                Object.DestroyImmediate(monsterCatalog);
            }
        }
    }
}
