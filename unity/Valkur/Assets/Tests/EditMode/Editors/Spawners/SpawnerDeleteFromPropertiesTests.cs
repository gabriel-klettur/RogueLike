using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay.Spawners;

namespace Valkur.Tests.EditMode.Editors.Spawners
{
    /// <summary>
    /// Behaviour tests for the Delete-from-Properties button — the replacement
    /// for the retired Delete mode in the Modes panel. The button is wired to
    /// <c>SpawnerEditorManager::DeleteSelectedInstance</c> and is visible
    /// only when a spawner is selected; this fixture pins down those rules so
    /// the destructive action can't silently regress.
    /// </summary>
    [TestFixture]
    public class SpawnerDeleteFromPropertiesTests
    {
        private readonly List<GameObject>       _scene  = new List<GameObject>();
        private readonly List<ScriptableObject> _assets = new List<ScriptableObject>();
        private SpawnerEditorManager _mgr;

        // ── Reflection helpers ───────────────────────────────────────────────

        private static FieldInfo GetField(object obj, string name)
        {
            var t = obj.GetType();
            while (t != null)
            {
                var f = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Public |
                                         BindingFlags.Instance | BindingFlags.Static);
                if (f != null) return f;
                t = t.BaseType;
            }
            return null;
        }

        private static T GetFieldValue<T>(object obj, string name)
            => (T)GetField(obj, name)?.GetValue(obj);

        private static void SetFieldValue(object obj, string name, object value)
            => GetField(obj, name)?.SetValue(obj, value);

        private static void ClearSingletonInstance<T>() where T : MonoBehaviour
        {
            var type = typeof(T).BaseType;
            while (type != null)
            {
                var f = type.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
                if (f != null) { f.SetValue(null, null); return; }
                type = type.BaseType;
            }
        }

        // ── Scene factories ──────────────────────────────────────────────────

        private SpawnerInstance MakeSpawner(string id, Vector3 pos)
        {
            var template = ScriptableObject.CreateInstance<SpawnerTemplateData>();
            template.templateId    = id;
            template.triggerRadius = 1f;
            _assets.Add(template);

            var go = new GameObject($"TestSpawner_{id}");
            go.transform.position = pos;
            _scene.Add(go);

            var si = go.AddComponent<SpawnerInstance>();
            si.Initialize(template, id, zone: "Lobby", spawner: null);
            return si;
        }

        // ── Setup / Teardown ─────────────────────────────────────────────────

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            ClearSingletonInstance<SpawnerEditorManager>();

            var go = new GameObject("[SpawnerEditorManager-Test]");
            _scene.Add(go);
            _mgr = go.AddComponent<SpawnerEditorManager>();

            // Force-active without going through BuildUI — we test logic, not chrome.
            SetFieldValue(_mgr, "_active", true);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _scene)  if (go != null) Object.DestroyImmediate(go);
            _scene.Clear();
            foreach (var so in _assets) if (so != null) Object.DestroyImmediate(so);
            _assets.Clear();

            ClearSingletonInstance<SpawnerEditorManager>();
            LogAssert.ignoreFailingMessages = false;
        }

        // ── DeleteSelectedInstance — happy path ──────────────────────────────

        [Test]
        public void DeleteSelected_NoSelection_IsSafeNoOp()
        {
            // No selection → nothing to destroy. The button should normally be
            // hidden in this state, but the entry point must still be safe to
            // invoke (e.g. via a hotkey wired to the same callback).
            Assert.DoesNotThrow(() => _mgr.DeleteSelectedInstance(),
                "DeleteSelectedInstance must be a safe no-op when no spawner is selected.");
            Assert.IsNull(GetFieldValue<SpawnerInstance>(_mgr, "_selectedInstance"),
                "Selection state must remain null.");
        }

        [Test]
        public void DeleteSelected_DestroysGameObject()
        {
            MakeSpawner("a", new Vector3(0f, 0f, 0f));
            // Re-find the live instance so we don't carry a stale reference past
            // the destroy boundary — Unity's Object equality overrides behave
            // inconsistently across EditMode/PlayMode for already-cached refs.
            var si = Object.FindObjectOfType<SpawnerInstance>();
            Assert.IsNotNull(si, "Test setup precondition: spawner must exist before deletion.");
            SetFieldValue(_mgr, "_selectedInstance", si);

            _mgr.DeleteSelectedInstance();

            int alive = Object.FindObjectsOfType<SpawnerInstance>().Length;
            Assert.AreEqual(0, alive,
                "DeleteSelectedInstance must remove the spawner from the live scene set.");
        }

        [Test]
        public void DeleteSelected_ClearsSelection()
        {
            var si = MakeSpawner("a", new Vector3(0f, 0f, 0f));
            SetFieldValue(_mgr, "_selectedInstance", si);

            _mgr.DeleteSelectedInstance();

            Assert.IsNull(GetFieldValue<SpawnerInstance>(_mgr, "_selectedInstance"),
                "DeleteSelectedInstance must clear _selectedInstance so the panel returns to its empty state.");
        }

        [Test]
        public void DeleteSelected_CancelsInFlightDrag()
        {
            // If a user starts an RMB drag, the spawner becomes _selectedInstance.
            // Pressing Delete in Properties mid-drag should cancel cleanly so
            // _dragging doesn't try to follow a destroyed transform next frame.
            var si = MakeSpawner("a", new Vector3(0f, 0f, 0f));
            SetFieldValue(_mgr, "_selectedInstance", si);
            SetFieldValue(_mgr, "_dragging", true);

            _mgr.DeleteSelectedInstance();

            Assert.IsFalse(GetFieldValue<bool>(_mgr, "_dragging"),
                "DeleteSelectedInstance must clear _dragging so the per-frame drag follow doesn't NRE.");
        }

        [Test]
        public void DeleteSelected_DoesNotDestroyOtherSpawners()
        {
            MakeSpawner("victim",   new Vector3(0f, 0f, 0f));
            MakeSpawner("survivor", new Vector3(5f, 0f, 0f));

            // Find the victim by id to dodge stale-ref equality quirks.
            SpawnerInstance victim = null;
            foreach (var si in Object.FindObjectsOfType<SpawnerInstance>())
                if (si.InstanceId == "victim") victim = si;
            Assert.IsNotNull(victim, "Test setup precondition: victim must exist before deletion.");
            SetFieldValue(_mgr, "_selectedInstance", victim);

            _mgr.DeleteSelectedInstance();

            var alive = Object.FindObjectsOfType<SpawnerInstance>();
            Assert.AreEqual(1, alive.Length,
                "Exactly one spawner must remain after deleting the selected one.");
            Assert.AreEqual("survivor", alive[0].InstanceId,
                "The surviving spawner must be the unselected bystander, not the victim.");
        }
    }
}
