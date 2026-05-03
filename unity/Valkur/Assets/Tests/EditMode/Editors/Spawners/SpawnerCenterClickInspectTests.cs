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
    /// Behaviour tests for the centre-click inspect shortcut introduced together
    /// with the Alt-toggle outline visualization (F3 Spawner Editor).
    ///
    /// The user-visible bug this fixture pins down: clicking on the yellow centre
    /// dot must select that spawner, refresh the Properties panel, and respect
    /// mode/visibility gates. The shortcut is split into
    /// <c>CanCenterClickInspect</c> (gating) + <c>PerformCenterClickInspect</c>
    /// (effect) so we can assert each independently of the live mouse.
    /// </summary>
    [TestFixture]
    public class SpawnerCenterClickInspectTests
    {
        private readonly List<GameObject>        _scene  = new List<GameObject>();
        private readonly List<ScriptableObject>  _assets = new List<ScriptableObject>();
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

        private static MethodInfo GetMethod(object obj, string name)
        {
            var t = obj.GetType();
            while (t != null)
            {
                var m = t.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                if (m != null) return m;
                t = t.BaseType;
            }
            return null;
        }

        private static T InvokeNonPublic<T>(object obj, string name, params object[] args)
            => (T)GetMethod(obj, name)?.Invoke(obj, args);

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

        private SpawnerInstance MakeSpawner(string id, Vector3 pos, float triggerRadius = 1f)
        {
            var template = ScriptableObject.CreateInstance<SpawnerTemplateData>();
            template.templateId    = id;
            template.triggerRadius = triggerRadius;
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
            // EditMode: suppress noise from MonoBehaviour.OnDisable / Material leaks.
            LogAssert.ignoreFailingMessages = true;

            ClearSingletonInstance<SpawnerEditorManager>();

            var go = new GameObject("[SpawnerEditorManager-Test]");
            _scene.Add(go);
            _mgr = go.AddComponent<SpawnerEditorManager>();

            // Force-activate without going through BuildUI — we don't need the UI
            // to assert selection logic and avoiding it keeps the test EditMode-safe.
            SetFieldValue(_mgr, "_active", true);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _scene) if (go != null) Object.DestroyImmediate(go);
            _scene.Clear();

            foreach (var so in _assets) if (so != null) Object.DestroyImmediate(so);
            _assets.Clear();

            ClearSingletonInstance<SpawnerEditorManager>();
            LogAssert.ignoreFailingMessages = false;
        }

        // ── CanCenterClickInspect — gating logic ──────────────────────────────

        [Test]
        public void CanCenterClickInspect_OutlinesOff_ReturnsFalse()
        {
            SetFieldValue(_mgr, "_showAllOutlines", false);

            Assert.IsFalse(_mgr.CanCenterClickInspect(),
                "Without outlines visible, the click-on-centre shortcut must be inert.");
        }

        [Test]
        public void CanCenterClickInspect_OutlinesOn_SelectMode_ReturnsTrue()
        {
            SetFieldValue(_mgr, "_showAllOutlines", true);
            SetMode(_mgr, "Select");

            Assert.IsTrue(_mgr.CanCenterClickInspect(),
                "When outlines are on and we're in Select mode, the shortcut must be armed.");
        }

        [Test]
        public void CanCenterClickInspect_OutlinesOn_PlaceMode_ReturnsTrue()
        {
            SetFieldValue(_mgr, "_showAllOutlines", true);
            SetMode(_mgr, "Place");

            Assert.IsTrue(_mgr.CanCenterClickInspect(),
                "Place mode must still allow centre-click inspect — that's the whole UX win for users in Place.");
        }

        [Test]
        public void CanCenterClickInspect_OutlinesOn_DeleteMode_ReturnsFalse()
        {
            SetFieldValue(_mgr, "_showAllOutlines", true);
            SetMode(_mgr, "Delete");

            Assert.IsFalse(_mgr.CanCenterClickInspect(),
                "Delete mode is destructive on purpose — the inspect shortcut must NOT override it.");
        }

        // ── PerformCenterClickInspect — geometry + side effects ───────────────

        [Test]
        public void Perform_NoSpawnerNearby_ReturnsFalse_AndDoesNotSelect()
        {
            MakeSpawner("a", new Vector3(10f, 10f, 0f));

            bool result = _mgr.PerformCenterClickInspect(Vector3.zero);

            Assert.IsFalse(result, "No spawner inside the radius → must return false.");
            Assert.IsNull(GetFieldValue<SpawnerInstance>(_mgr, "_selectedInstance"),
                "Selection must remain unchanged when no spawner is hit.");
        }

        [Test]
        public void Perform_SpawnerInsideRadius_SelectsIt()
        {
            var si = MakeSpawner("a", new Vector3(0.1f, 0f, 0f));

            bool result = _mgr.PerformCenterClickInspect(Vector3.zero);

            Assert.IsTrue(result, "Spawner inside radius → must return true.");
            Assert.AreEqual(si, GetFieldValue<SpawnerInstance>(_mgr, "_selectedInstance"),
                "PerformCenterClickInspect must store the hit spawner as the selected instance.");
        }

        [Test]
        public void Perform_PicksClosestWhenMultipleNearby()
        {
            // The hit-test radius is 0.55 wu — both spawners must sit inside it
            // for the closest-wins rule to be exercised.
            MakeSpawner("far",   new Vector3(0.40f, 0f, 0f));
            var near = MakeSpawner("near",  new Vector3(0.10f, 0f, 0f));

            _mgr.PerformCenterClickInspect(Vector3.zero);

            Assert.AreEqual(near, GetFieldValue<SpawnerInstance>(_mgr, "_selectedInstance"),
                "When multiple spawners share the hit radius the closest one must be selected.");
        }

        [Test]
        public void Perform_OpensPropertiesDropdown()
        {
            MakeSpawner("a", new Vector3(0.1f, 0f, 0f));

            _mgr.PerformCenterClickInspect(Vector3.zero);

            var openDropdowns = GetFieldValue<HashSet<string>>(_mgr, "_openDropdowns");
            Assert.IsNotNull(openDropdowns, "_openDropdowns set must be initialised.");
            Assert.IsTrue(openDropdowns.Contains("props"),
                "PerformCenterClickInspect must open the Properties dropdown so the inspection actually lands on screen.");
        }

        [Test]
        public void Perform_ClickAtBoundary_DoesNotSelect()
        {
            // Strict less-than in the hit tester: a spawner placed exactly at the
            // hit radius must not be selected — keeps Place mode from being
            // overridden by clicks that just brush the marker's edge.
            float radius = SpawnerEditorManager.CENTER_HIT_RADIUS_WORLD;
            MakeSpawner("edge", new Vector3(radius, 0f, 0f));

            bool result = _mgr.PerformCenterClickInspect(Vector3.zero);

            Assert.IsFalse(result,
                "Boundary distance must NOT trigger selection (strict less-than radius check).");
        }

        [Test]
        public void Perform_DestroyedSpawnerIsIgnored()
        {
            var alive = MakeSpawner("alive",  new Vector3(0.10f, 0f, 0f));
            var dying = MakeSpawner("dying", new Vector3(0.05f, 0f, 0f));
            Object.DestroyImmediate(dying.gameObject);

            bool result = _mgr.PerformCenterClickInspect(Vector3.zero);

            Assert.IsTrue(result,
                "Destroying one spawner must not prevent another live one from being selected.");
            Assert.AreEqual(alive, GetFieldValue<SpawnerInstance>(_mgr, "_selectedInstance"),
                "Destroyed spawners must not appear in the hit-test result set.");
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static void SetMode(SpawnerEditorManager mgr, string modeName)
        {
            // EditorMode is a private nested enum — fetch the type via reflection.
            var enumType = typeof(SpawnerEditorManager).GetNestedType(
                "EditorMode", BindingFlags.NonPublic);
            Assert.IsNotNull(enumType, "Expected nested enum 'EditorMode' on SpawnerEditorManager.");
            var value = System.Enum.Parse(enumType, modeName);
            SetFieldValue(mgr, "_mode", value);
        }
    }
}
