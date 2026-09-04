using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Entities;

namespace Valkur.Tests.EditMode.Editors.Entities
{
    /// <summary>
    /// Covers Create / Duplicate / Rename from F5 — the other half of the audit's
    /// Dimension-1 gap ("No create / duplicate / rename from F5"). <c>OnConfirmAddOnSystem</c>
    /// is exercised only indirectly (through <see cref="CreateAndRegisterDefinition"/>, which it
    /// calls after resolving a unique key) so nothing here ever writes under the SHIPPED
    /// <c>Data/Catalogs/Monsters/</c> folder — every asset created by these tests lives under
    /// <see cref="ScratchTemplateDir"/> and is deleted in <see cref="TearDown"/> whether the
    /// test passed or not, mirroring <c>MonsterFramesImporterTests</c>' scratch-folder pattern.
    /// </summary>
    [TestFixture]
    public class EntitiesCatalogAuthoringTests
    {
        private const BindingFlags NP        = BindingFlags.NonPublic | BindingFlags.Instance;
        private const BindingFlags STATIC_NP = BindingFlags.NonPublic | BindingFlags.Static;

        private const string ScratchParent     = "Assets/Tests/EditMode/Editors/Entities";
        private const string ScratchFolderName = "_EntitiesCatalogAuthoringScratch";
        private const string ScratchTemplateDir = ScratchParent + "/" + ScratchFolderName;

        private GameObject      _editorGo;
        private EntitiesRuntimeEditor _ed;
        private MonsterCatalog  _catalog;

        /// <summary>
        /// The scratch FOLDER is made once per fixture, not once per test. Measured: the
        /// per-test DeleteAsset + CreateFolder in SetUp and DeleteAsset + Refresh in TearDown
        /// put a 240 ms floor under every test here — including seven Slugify cases that
        /// never touch a file — and this fixture alone was 7.4 s of the Entities group.
        /// Isolation is kept where it matters: TearDown deletes every asset a test wrote
        /// into the folder, so RefusesADuplicateKey still sees an empty disk.
        /// </summary>
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            if (AssetDatabase.IsValidFolder(ScratchTemplateDir))
                AssetDatabase.DeleteAsset(ScratchTemplateDir);
            AssetDatabase.CreateFolder(ScratchParent, ScratchFolderName);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (AssetDatabase.IsValidFolder(ScratchTemplateDir))
                AssetDatabase.DeleteAsset(ScratchTemplateDir);
            AssetDatabase.Refresh();
        }

        [SetUp]
        public void SetUp()
        {
            // RefreshPicker tears its rows down with Object.Destroy, which EditMode answers
            // with an error log; every runtime-editor fixture in this suite ignores it the
            // same way. Re-armed in TearDown too — the framework restores the flag before
            // TearDown runs, and that is where the last picker refresh lands.
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;

            // The folder outlives the test; anything left inside it from a previous test
            // would be a leak of the kind OneTimeSetUp above explains, so it is asserted
            // empty rather than silently cleaned twice.
            Assert.AreEqual(0, AssetDatabase.FindAssets("", new[] { ScratchTemplateDir }).Length,
                "Scratch folder not empty at SetUp — a previous test's TearDown missed an asset.");

            _editorGo = new GameObject("EntitiesCatalogAuthoringUnderTest");
            _ed = _editorGo.AddComponent<EntitiesRuntimeEditor>();
            // Awake may not run reliably under all EditMode situations (same note as
            // EntitiesRuntimeEditorTests.CreateEditor) — force it so Start() below has a
            // _toggleAction to skip past without throwing.
            if (GetPrivateFieldOrNull(_ed, "_toggleAction") == null)
                Invoke(_ed, "OnSingletonAwake");

            // In-memory only — never CreateAsset'd, so nothing here can touch the real
            // MonsterCatalog.asset.
            _catalog = ScriptableObject.CreateInstance<MonsterCatalog>();
            SetPrivateField(_ed, "_monsterCatalog", _catalog);

            // Builds _ui/_root/_canvas, all parented under _editorGo — Rename/Duplicate refresh
            // the Picker and the Properties panel as part of their normal behaviour, and without
            // a built UI those refreshes would create loose, unparented rows (CreateUI with a
            // null RectTransform parent still succeeds — it just parents nothing) that TearDown
            // could never reach. Start() is safe to call directly here: it early-outs of
            // LoadPlacedEntities() when !Application.isPlaying, which is always true in EditMode.
            Invoke(_ed, "Start");
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            if (_editorGo != null) Object.DestroyImmediate(_editorGo);
            if (_catalog  != null) Object.DestroyImmediate(_catalog);

            // Delete only what this test wrote. DeleteAsset updates the database
            // synchronously, so no Refresh is needed here — the one Refresh happens in
            // OneTimeTearDown.
            foreach (var guid in AssetDatabase.FindAssets("", new[] { ScratchTemplateDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path) && !AssetDatabase.IsValidFolder(path))
                    AssetDatabase.DeleteAsset(path);
            }
        }

        // ── Reflection helpers ───────────────────────────────────────────────────

        private static void SetPrivateField(object target, string name, object value)
        {
            var f = target.GetType().GetField(name, NP);
            Assert.IsNotNull(f, $"field '{name}' must exist on {target.GetType().Name}");
            f.SetValue(target, value);
        }

        private static object GetPrivateFieldOrNull(object target, string name)
            => target.GetType().GetField(name, NP)?.GetValue(target);

        private static object Invoke(object target, string method, params object[] args)
        {
            var m = target.GetType().GetMethod(method, NP);
            Assert.IsNotNull(m, $"method '{method}' must exist on {target.GetType().Name}");
            return m.Invoke(target, args);
        }

        private static string InvokeStaticString(string method, params object[] args)
        {
            var m = typeof(EntitiesRuntimeEditor).GetMethod(method, STATIC_NP);
            Assert.IsNotNull(m, $"static method '{method}' must exist on EntitiesRuntimeEditor");
            return (string)m.Invoke(null, args);
        }

        private MonsterDefinition Create(string key)
            => (MonsterDefinition)Invoke(_ed, "CreateAndRegisterDefinition", key, ScratchTemplateDir);

        private void Select(string key)
        {
            SetPrivateField(_ed, "_selectedKey", key);
            SetPrivateField(_ed, "_selectedIsPlayer", false);
        }

        // ── Slugify / unique-key resolution (pure logic, no disk I/O) ───────────

        [Test]
        [TestCase("Ice Barbol", "ice_barbol")]
        [TestCase("  Trim Me  ", "trim_me")]
        [TestCase("Multi   Space", "multi_space")]
        [TestCase("Sword-Wraith", "sword_wraith")]
        [TestCase("Ünïcödé Ogre!!", "ncd_ogre")]
        [TestCase("", "")]
        [TestCase(null, "")]
        public void Slugify_NormalisesFreeTextIntoTheImporterValidatedKeyShape(string raw, string expected)
        {
            string result = InvokeStaticString("Slugify", raw);
            Assert.AreEqual(expected, result);
        }

        [Test]
        public void ResolveUniqueKey_AppendsANumericSuffix_WhenTheBaseKeyIsTaken()
        {
            Create("barbol");

            string resolved = (string)Invoke(_ed, "ResolveUniqueKey", "barbol");
            Assert.AreEqual("barbol_2", resolved);

            Create("barbol_2");
            resolved = (string)Invoke(_ed, "ResolveUniqueKey", "barbol");
            Assert.AreEqual("barbol_3", resolved);
        }

        [Test]
        public void ResolveUniqueKey_EmptyBase_FallsBackToNewMonster()
        {
            Assert.AreEqual("new_monster", (string)Invoke(_ed, "ResolveUniqueKey", ""));
        }

        // ── Create ────────────────────────────────────────────────────────────

        [Test]
        public void CreateAndRegisterDefinition_ProducesExactlyOneCatalogEntry()
        {
            var def = Create("test_new_monster");

            Assert.IsNotNull(def);
            Assert.AreEqual("test_new_monster", def.monsterKey);
            Assert.AreEqual(1, _catalog.Definitions.Count(d => d.monsterKey == "test_new_monster"));
            Assert.AreSame(def, _catalog.GetByKey("test_new_monster"));
        }

        [Test]
        public void CreateAndRegisterDefinition_SeedsNonZeroStats()
        {
            // The audit calls out mon1.asset shipping as an all-zero stub, spawnable and
            // unfightable. A freshly created definition must not repeat that shape.
            var def = Create("test_fightable_monster");

            Assert.Greater(def.stats.hp, 0);
            Assert.Greater(def.stats.speed, 0f);
            Assert.Greater(def.stats.meleeDamage, 0);
            Assert.AreEqual("Monster_Default", def.fsmSet);
        }

        [Test]
        public void CreateAndRegisterDefinition_WritesARealAsset_UnderTheGivenDirectory()
        {
            Create("test_asset_on_disk");

            string expectedPath = $"{ScratchTemplateDir}/test_asset_on_disk.asset";
            var onDisk = AssetDatabase.LoadAssetAtPath<MonsterDefinition>(expectedPath);
            Assert.IsNotNull(onDisk, "Create must persist a real .asset, not just an in-memory object");
        }

        [Test]
        public void CreateAndRegisterDefinition_RefusesADuplicateKey()
        {
            Create("dup_key");
            var second = Create("dup_key");

            Assert.IsNull(second, "a second Create for the same key must be refused");
            Assert.AreEqual(1, _catalog.Definitions.Count(d => d.monsterKey == "dup_key"),
                "the catalog must still hold exactly one entry for that key");
        }

        // ── Duplicate ─────────────────────────────────────────────────────────

        [Test]
        public void DuplicateSelectedDefinition_ProducesExactlyOneNewCatalogEntry()
        {
            var source = Create("dup_source");
            source.stats.hp = 77;
            source.stats.meleeDamage = 13;
            source.displayName = "Dup Source";
            Select("dup_source");

            var clone = (MonsterDefinition)Invoke(_ed, "DuplicateSelectedDefinition", ScratchTemplateDir);

            Assert.IsNotNull(clone);
            Assert.AreNotEqual(source.monsterKey, clone.monsterKey, "the clone must get its own key");
            Assert.AreEqual(2, _catalog.Definitions.Count, "exactly one NEW entry — the source is untouched");
            Assert.AreEqual(1, _catalog.Definitions.Count(d => d.monsterKey == clone.monsterKey));

            // Object.Instantiate deep-copies every serialized field — retunable immediately.
            Assert.AreEqual(77, clone.stats.hp);
            Assert.AreEqual(13, clone.stats.meleeDamage);
        }

        [Test]
        public void DuplicateSelectedDefinition_WithNoSelection_IsRefused()
        {
            SetPrivateField(_ed, "_selectedKey", null);

            var clone = (MonsterDefinition)Invoke(_ed, "DuplicateSelectedDefinition", ScratchTemplateDir);

            Assert.IsNull(clone);
            Assert.AreEqual(0, _catalog.Definitions.Count);
        }

        [Test]
        public void DuplicateSelectedDefinition_UsesThePendingKeyField_WhenOneIsTyped()
        {
            Create("dup_source_2");
            Select("dup_source_2");
            SetPrivateField(_ed, "_pendingKeyInput", "Custom Clone Name");

            var clone = (MonsterDefinition)Invoke(_ed, "DuplicateSelectedDefinition", ScratchTemplateDir);

            Assert.IsNotNull(clone);
            Assert.AreEqual("custom_clone_name", clone.monsterKey);
            Assert.AreEqual("Custom Clone Name", clone.displayName);
        }

        // ── Rename ────────────────────────────────────────────────────────────

        [Test]
        public void RenameSelectedDefinition_RekeysWithoutChangingCatalogCount()
        {
            Create("old_key");
            Select("old_key");

            bool ok = (bool)Invoke(_ed, "RenameSelectedDefinition", "Brand New Name");

            Assert.IsTrue(ok);
            Assert.AreEqual(1, _catalog.Definitions.Count, "rename must not add or remove entries");
            Assert.IsNull(_catalog.GetByKey("old_key"), "the old key must no longer resolve");

            var renamed = _catalog.GetByKey("brand_new_name");
            Assert.IsNotNull(renamed, "the new key must resolve");
            Assert.AreEqual("Brand New Name", renamed.displayName);
            Assert.AreEqual(1, _catalog.Definitions.Count(d => d.monsterKey == "brand_new_name"));
        }

        [Test]
        public void RenameSelectedDefinition_RefusesACollisionWithAnotherDefinition()
        {
            Create("keep_a");
            Create("keep_b");
            Select("keep_a");

            bool ok = (bool)Invoke(_ed, "RenameSelectedDefinition", "keep_b");

            Assert.IsFalse(ok, "renaming onto an existing different definition's key must be refused");
            Assert.IsNotNull(_catalog.GetByKey("keep_a"), "the definition being renamed must be untouched");
            Assert.IsNotNull(_catalog.GetByKey("keep_b"), "the other definition must be untouched");
            Assert.AreEqual(2, _catalog.Definitions.Count);
        }

        [Test]
        public void RenameSelectedDefinition_EmptyKey_IsRefused()
        {
            Create("stays_put");
            Select("stays_put");

            bool ok = (bool)Invoke(_ed, "RenameSelectedDefinition", "   ");

            Assert.IsFalse(ok);
            Assert.IsNotNull(_catalog.GetByKey("stays_put"));
        }

        [Test]
        public void RenameSelectedDefinition_WithNoSelection_IsRefused()
        {
            SetPrivateField(_ed, "_selectedKey", null);

            bool ok = (bool)Invoke(_ed, "RenameSelectedDefinition", "anything");

            Assert.IsFalse(ok);
        }
    }
}
