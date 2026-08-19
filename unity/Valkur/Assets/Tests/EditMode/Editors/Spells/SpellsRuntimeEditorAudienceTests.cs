using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay.Spells;
using Valkur.UIKit;

namespace Valkur.Tests.EditMode.Editors.Spells
{
    [TestFixture]
    public class SpellsRuntimeEditorAudienceTests
    {
        private readonly List<GameObject> _scene = new List<GameObject>();
        private readonly List<ScriptableObject> _assets = new List<ScriptableObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _scene)
                if (go != null) Object.DestroyImmediate(go);
            foreach (var asset in _assets)
                if (asset != null) Object.DestroyImmediate(asset);

            _scene.Clear();
            _assets.Clear();
            ClearSingletonInstance<SpellsRuntimeEditor>();
            LogAssert.ignoreFailingMessages = false;
        }

        [Test]
        public void MatchesAudienceFilter_SharedSpellAppearsInEveryAssignedAudience()
        {
            var shared = CreateSpell("shared", SpellAudience.Player | SpellAudience.Boss);
            var unassigned = CreateSpell("unassigned", SpellAudience.None);

            Assert.IsTrue(SpellsRuntimeEditor.MatchesAudienceFilter(shared, "all"));
            Assert.IsTrue(SpellsRuntimeEditor.MatchesAudienceFilter(shared, "player"));
            Assert.IsTrue(SpellsRuntimeEditor.MatchesAudienceFilter(shared, "boss"));
            Assert.IsFalse(SpellsRuntimeEditor.MatchesAudienceFilter(shared, "npc"));
            Assert.IsFalse(SpellsRuntimeEditor.MatchesAudienceFilter(shared, "unassigned"));

            Assert.IsTrue(SpellsRuntimeEditor.MatchesAudienceFilter(unassigned, "all"));
            Assert.IsTrue(SpellsRuntimeEditor.MatchesAudienceFilter(unassigned, "unassigned"));
            Assert.IsFalse(SpellsRuntimeEditor.MatchesAudienceFilter(unassigned, "player"));
            Assert.IsFalse(SpellsRuntimeEditor.MatchesAudienceFilter(unassigned, "npc"));
            Assert.IsFalse(SpellsRuntimeEditor.MatchesAudienceFilter(unassigned, "boss"));
        }

        [Test]
        public void AudienceTabs_FilterGridAndTableSource_ThenSearchWithinActiveTab()
        {
            LogAssert.ignoreFailingMessages = true;

            var shared = CreateSpell("shared_fire", SpellAudience.Player | SpellAudience.Boss);
            var npc = CreateSpell("npc_frost", SpellAudience.NPC);
            var unassigned = CreateSpell("draft_spell", SpellAudience.None);
            var editor = CreateEditorWithCatalog(shared, npc, unassigned);

            editor.Activate();

            var tabs = GetNested(editor, "_uiRefs", "SpellAudienceTabs") as TabStrip;
            Assert.IsNotNull(tabs);
            Assert.AreEqual(5, tabs.Count);
            Assert.AreEqual("all", tabs.ActiveKey);

            Assert.IsTrue(tabs.SetActive("boss"));
            CollectionAssert.AreEquivalent(new[] { "shared_fire" }, FilteredKeys(editor));

            Invoke(editor, "OnSearchChanged", "frost");
            Assert.IsEmpty(FilteredKeys(editor),
                "Search must operate inside the active Boss tab, not across the full catalog.");

            Invoke(editor, "OnSearchChanged", "shared");
            CollectionAssert.AreEquivalent(new[] { "shared_fire" }, FilteredKeys(editor));

            Invoke(editor, "OnSearchChanged", "");
            Assert.IsTrue(tabs.SetActive("unassigned"));
            CollectionAssert.AreEquivalent(new[] { "draft_spell" }, FilteredKeys(editor));

            editor.Deactivate();
        }

        [Test]
        public void GridCards_ShowOneBadgePerAssignedAudience()
        {
            LogAssert.ignoreFailingMessages = true;

            var shared = CreateSpell("shared", SpellAudience.Player | SpellAudience.Boss);
            var editor = CreateEditorWithCatalog(shared);
            editor.Activate();

            var content = GetNested(editor, "_uiRefs", "PickerContent") as RectTransform;
            Assert.IsNotNull(content);
            Assert.AreEqual(1, content.childCount);

            var badges = content.GetChild(0).Find("AudienceBadges");
            Assert.IsNotNull(badges);
            Assert.AreEqual(2, badges.childCount,
                "A Player + Boss spell must expose both P and B badges on its grid card.");
            CollectionAssert.AreEquivalent(new[] { "Badge_P", "Badge_B" },
                badges.Cast<Transform>().Select(t => t.name));

            editor.Deactivate();
        }

        private SpellDefinition CreateSpell(string key, SpellAudience audience)
        {
            var spell = ScriptableObject.CreateInstance<SpellDefinition>();
            spell.spellKey = key;
            spell.displayName = key;
            spell.audience = audience;
            _assets.Add(spell);
            return spell;
        }

        private SpellsRuntimeEditor CreateEditorWithCatalog(params SpellDefinition[] spells)
        {
            ClearSingletonInstance<SpellsRuntimeEditor>();

            var catalog = ScriptableObject.CreateInstance<SpellCatalog>();
            catalog.SetSpellsRuntime(spells);
            _assets.Add(catalog);

            var go = new GameObject("TestSpellsEditorAudience");
            _scene.Add(go);
            var editor = go.AddComponent<SpellsRuntimeEditor>();
            SetField(editor, "_catalog", catalog);
            Invoke(editor, "OnSingletonAwake");
            Invoke(editor, "Start");
            return editor;
        }

        private static string[] FilteredKeys(SpellsRuntimeEditor editor)
        {
            var filtered = GetField(editor, "_filtered") as List<SpellDefinition>;
            Assert.IsNotNull(filtered);
            return filtered.Select(spell => spell.spellKey).ToArray();
        }

        private static void ClearSingletonInstance<T>() where T : MonoBehaviour
        {
            var type = typeof(T).BaseType;
            while (type != null)
            {
                var field = type.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
                if (field != null)
                {
                    field.SetValue(null, null);
                    return;
                }
                type = type.BaseType;
            }
        }

        private static FieldInfo Field(object obj, string name)
        {
            var type = obj.GetType();
            while (type != null)
            {
                var field = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Public |
                                                BindingFlags.Instance);
                if (field != null) return field;
                type = type.BaseType;
            }
            return null;
        }

        private static object GetField(object obj, string name) => Field(obj, name)?.GetValue(obj);

        private static object GetNested(object obj, string outer, string inner)
        {
            var boxed = GetField(obj, outer);
            return boxed == null ? null : Field(boxed, inner)?.GetValue(boxed);
        }

        private static void SetField(object obj, string name, object value)
        {
            var field = Field(obj, name);
            Assert.IsNotNull(field, $"Field '{name}' not found on {obj.GetType().Name}.");
            field.SetValue(obj, value);
        }

        private static void Invoke(object obj, string method, params object[] args)
        {
            var type = obj.GetType();
            while (type != null)
            {
                var candidate = type.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Public |
                                                       BindingFlags.Instance);
                if (candidate != null)
                {
                    candidate.Invoke(obj, args);
                    return;
                }
                type = type.BaseType;
            }
            Assert.Fail($"Method '{method}' not found on {obj.GetType().Name}.");
        }
    }
}
