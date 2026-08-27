using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Entities;

namespace Valkur.Tests.EditMode.Editors.Entities
{
    /// <summary>
    /// Covers the half of the F5 editor that used to be stubs.
    ///
    /// <see cref="EntitiesRuntimeEditorTests"/> has 29 tests and every one of them is
    /// UI shell — hotkey binding, panel refs, activate/deactivate — because at the time
    /// there was nothing else to test: Save, click-to-spawn and "Add on System" were
    /// status-string stubs and the properties panel was two labels per row. This
    /// fixture pins the authoring contracts that replaced them.
    /// </summary>
    [TestFixture]
    public class EntitiesEditorAuthoringTests
    {
        private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;

        // ── Editor-wide contracts ───────────────────────────────────────────────

        [Test]
        public void Editor_ImplementsIAllowsPlayerMovement()
        {
            // PlayerController.Movement.ShouldSuspendInputFor returns
            // !(active is IAllowsPlayerMovement). Without this the PvM loop was
            // "place a monster, close F5, fight it, reopen F5" — the wrong default for
            // the editor whose whole job is putting things in the world to fight.
            Assert.IsTrue(typeof(IAllowsPlayerMovement).IsAssignableFrom(typeof(EntitiesRuntimeEditor)),
                "F5 must not freeze the player; every other placement editor opts out.");
        }

        [Test]
        public void ClickToSpawn_IsRoutedToTheRealPlacementPath_NotAStub()
        {
            var spawn = typeof(EntitiesRuntimeEditor).GetMethod("SpawnEntityAtPosition", NP);
            Assert.IsNotNull(spawn, "SpawnEntityAtPosition must still exist");

            var place = typeof(EntitiesRuntimeEditor).GetMethod("PlaceEntityFromDrag", NP);
            Assert.IsNotNull(place,
                "Add-mode click and picker drag must share one placement path — two of " +
                "the four documented interaction paths for the primary verb were dead.");
        }

        [Test]
        public void SaveAndCommitSeams_Exist()
        {
            var t = typeof(EntitiesRuntimeEditor);
            Assert.IsNotNull(t.GetMethod("SaveEditedDefinitions", NP), "Save must be wired");
            Assert.IsNotNull(t.GetMethod("CommitDefinitionEdit", NP), "a committed row must persist");
            Assert.IsNotNull(t.GetMethod("ReapplyToLiveMonsters", NP),
                "an edit that does not reach already-spawned monsters is not a tuning loop");
        }

        // ── The editable row ────────────────────────────────────────────────────

        [Test]
        public void AddEditableRow_BuildsAnInputField_AndCommitsItsText()
        {
            LogAssert.ignoreFailingMessages = true;
            var canvasGo = new GameObject("canvas", typeof(Canvas));
            try
            {
                var section = new GameObject("section", typeof(RectTransform))
                    .GetComponent<RectTransform>();
                section.SetParent(canvasGo.transform, false);

                string committed = null;
                var input = EntitiesEditorUIBuilder.AddEditableRow(
                    section, "HP", "42", v => committed = v);

                Assert.IsNotNull(input, "the row must contain a real TMP_InputField");
                Assert.AreEqual("42", input.text, "the field must open on the current value");

                input.text = "77";
                input.onEndEdit.Invoke("77");

                Assert.AreEqual("77", committed, "end-of-edit must reach the caller's handler");
            }
            finally
            {
                Object.DestroyImmediate(canvasGo);
                LogAssert.ignoreFailingMessages = false;
            }
        }

        [Test]
        public void AddEditableRow_KeepsTheLabel_SoTheRowStillReads()
        {
            LogAssert.ignoreFailingMessages = true;
            var canvasGo = new GameObject("canvas", typeof(Canvas));
            try
            {
                var section = new GameObject("section", typeof(RectTransform))
                    .GetComponent<RectTransform>();
                section.SetParent(canvasGo.transform, false);

                EntitiesEditorUIBuilder.AddEditableRow(section, "Aggro Range", "10", _ => { });

                var labels = section.GetComponentsInChildren<TextMeshProUGUI>(true);
                bool found = false;
                foreach (var l in labels) if (l.text == "Aggro Range") found = true;

                Assert.IsTrue(found, "an editable row must still name the field it edits");
            }
            finally
            {
                Object.DestroyImmediate(canvasGo);
                LogAssert.ignoreFailingMessages = false;
            }
        }

        // ── The player-clone guard ──────────────────────────────────────────────

        [Test]
        public void SpawnPlayerAt_RefusesWhenAPlayerAlreadyExists()
        {
            LogAssert.ignoreFailingMessages = true;
            var existing = new GameObject("existing-player");
            var editorGo = new GameObject("editor");
            try
            {
                EntityRegistry.RegisterPlayer(existing);
                Assert.IsNotNull(EntityRegistry.Player, "fixture must have a registered player");

                var ed = editorGo.AddComponent<EntitiesRuntimeEditor>();
                var m = typeof(EntitiesRuntimeEditor).GetMethod("SpawnPlayerAt", NP);
                Assert.IsNotNull(m);

                m.Invoke(ed, new object[] { "warrior", Vector3.zero });

                // The guard returns before touching the prefab or the registry, so the
                // original player must still be the registered one. Cloning it used to
                // re-point the HUD, the inventory and monster aggro at the clone while
                // the camera kept following the original — unrecoverable without a Stop.
                Assert.AreSame(existing, EntityRegistry.Player,
                    "a second player must never be spawned over the live one");
            }
            finally
            {
                EntityRegistry.UnregisterPlayer(existing);
                Object.DestroyImmediate(editorGo);
                Object.DestroyImmediate(existing);
                LogAssert.ignoreFailingMessages = false;
            }
        }

        // ── Stat editing writes the definition ──────────────────────────────────

        [Test]
        public void EditableStatRows_WriteBackToTheDefinition()
        {
            LogAssert.ignoreFailingMessages = true;
            var canvasGo = new GameObject("canvas", typeof(Canvas));
            var editorGo = new GameObject("editor");
            var def = ScriptableObject.CreateInstance<MonsterDefinition>();
            try
            {
                def.monsterKey = "probe";
                def.stats.hp = 10;
                def.stats.aggroRange = 5f;

                var section = new GameObject("section", typeof(RectTransform))
                    .GetComponent<RectTransform>();
                section.SetParent(canvasGo.transform, false);

                var ed = editorGo.AddComponent<EntitiesRuntimeEditor>();

                Invoke(ed, "AddIntStat", section, "HP", def.stats.hp, 1,
                       (System.Action<int>)(v => def.stats.hp = v), def);
                Invoke(ed, "AddFloatStat", section, "Aggro Range", def.stats.aggroRange, 0f,
                       (System.Action<float>)(v => def.stats.aggroRange = v), def);

                var fields = section.GetComponentsInChildren<TMP_InputField>(true);
                Assert.AreEqual(2, fields.Length, "one input per editable stat");

                fields[0].onEndEdit.Invoke("250");
                fields[1].onEndEdit.Invoke("12.5");

                Assert.AreEqual(250, def.stats.hp);
                Assert.AreEqual(12.5f, def.stats.aggroRange, 0.001f);
            }
            finally
            {
                Object.DestroyImmediate(def);
                Object.DestroyImmediate(editorGo);
                Object.DestroyImmediate(canvasGo);
                LogAssert.ignoreFailingMessages = false;
            }
        }

        [Test]
        public void EditableStatRows_RefuseGarbage_LeavingTheValueUntouched()
        {
            LogAssert.ignoreFailingMessages = true;
            var canvasGo = new GameObject("canvas", typeof(Canvas));
            var editorGo = new GameObject("editor");
            var def = ScriptableObject.CreateInstance<MonsterDefinition>();
            try
            {
                def.monsterKey = "probe";
                def.stats.hp = 33;

                var section = new GameObject("section", typeof(RectTransform))
                    .GetComponent<RectTransform>();
                section.SetParent(canvasGo.transform, false);

                var ed = editorGo.AddComponent<EntitiesRuntimeEditor>();
                Invoke(ed, "AddIntStat", section, "HP", def.stats.hp, 1,
                       (System.Action<int>)(v => def.stats.hp = v), def);

                section.GetComponentsInChildren<TMP_InputField>(true)[0].onEndEdit.Invoke("not a number");

                Assert.AreEqual(33, def.stats.hp,
                    "unparseable input must leave the field alone rather than zeroing it");
            }
            finally
            {
                Object.DestroyImmediate(def);
                Object.DestroyImmediate(editorGo);
                Object.DestroyImmediate(canvasGo);
                LogAssert.ignoreFailingMessages = false;
            }
        }

        [Test]
        public void EditableStatRows_ClampToTheirMinimum()
        {
            LogAssert.ignoreFailingMessages = true;
            var canvasGo = new GameObject("canvas", typeof(Canvas));
            var editorGo = new GameObject("editor");
            var def = ScriptableObject.CreateInstance<MonsterDefinition>();
            try
            {
                def.monsterKey = "probe";
                def.stats.hp = 33;

                var section = new GameObject("section", typeof(RectTransform))
                    .GetComponent<RectTransform>();
                section.SetParent(canvasGo.transform, false);

                var ed = editorGo.AddComponent<EntitiesRuntimeEditor>();
                Invoke(ed, "AddIntStat", section, "HP", def.stats.hp, 1,
                       (System.Action<int>)(v => def.stats.hp = v), def);

                section.GetComponentsInChildren<TMP_InputField>(true)[0].onEndEdit.Invoke("-99");

                Assert.AreEqual(1, def.stats.hp,
                    "0-HP monsters spawn dead or invisible; the row must clamp, not obey.");
            }
            finally
            {
                Object.DestroyImmediate(def);
                Object.DestroyImmediate(editorGo);
                Object.DestroyImmediate(canvasGo);
                LogAssert.ignoreFailingMessages = false;
            }
        }

        private static void Invoke(EntitiesRuntimeEditor ed, string method, params object[] args)
        {
            var m = typeof(EntitiesRuntimeEditor).GetMethod(method, NP);
            Assert.IsNotNull(m, method + " must exist");
            m.Invoke(ed, args);
        }
    }
}
