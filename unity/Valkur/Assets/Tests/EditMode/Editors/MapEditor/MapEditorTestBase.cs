using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Gameplay.MapEditor;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Editors.MapEditor
{
    /// <summary>
    /// Shared infrastructure for all MapEditor EditMode test fixtures.
    /// Handles production-file backup/restore, scene-object teardown, and
    /// reflection helpers used across every fixture.
    /// </summary>
    public abstract class MapEditorTestBase
    {
        protected readonly List<GameObject>      _sceneObjects = new List<GameObject>();
        protected readonly List<ScriptableObject> _assets      = new List<ScriptableObject>();

        // Backup of the user's real persistence file. Many ops tests call
        // CreateManagerWithZones and then exercise rename/move/delete/duplicate/
        // restrict — every one of those methods invokes PersistZonesToDisk(),
        // which would otherwise overwrite the user's map_editor_zones.json with
        // the test seed. SetUp moves it aside and TearDown restores it.
        private string _userZonesPrimary;
        private string _userZonesBackup;
        private string _userZonesSidecar;        // path of the production .bak
        private string _userZonesSidecarBackup;  // our parking spot for it
        private bool   _hadUserZones;
        private bool   _hadUserSidecar;

        [SetUp]
        public void SetUp()
        {
            _userZonesPrimary = System.IO.Path.Combine(Application.persistentDataPath, "map_editor_zones.json");
            _userZonesBackup  = _userZonesPrimary + ".test_backup_" + System.Guid.NewGuid().ToString("N").Substring(0, 8);
            _userZonesSidecar       = _userZonesPrimary + ".bak";
            _userZonesSidecarBackup = _userZonesBackup  + ".sidecar";

            _hadUserZones   = System.IO.File.Exists(_userZonesPrimary);
            _hadUserSidecar = System.IO.File.Exists(_userZonesSidecar);

            // Park the production sidecar BEFORE running tests so they don't
            // poison it via File.Replace (which writes the prior primary into
            // the .bak slot).
            if (_hadUserSidecar)
            {
                System.IO.File.Copy(_userZonesSidecar, _userZonesSidecarBackup, overwrite: true);
                System.IO.File.Delete(_userZonesSidecar);
            }

            if (_hadUserZones)
            {
                // Copy + Delete (instead of Move) so a test crash leaves the
                // primary intact and only the backup needs sweeping.
                System.IO.File.Copy(_userZonesPrimary, _userZonesBackup, overwrite: true);
                System.IO.File.Delete(_userZonesPrimary);
            }
        }

        [TearDown]
        public void TearDown()
        {
            // Keep log-failures muted while we destroy. DestroyImmediate on a
            // Canvas that owns a Selectable subtree (Button, Toggle, …) routinely
            // triggers a UGUI package-internal IndexOutOfRangeException at
            // Selectable.cs:555. The exception is benign but, if it leaks past
            // this TearDown, NUnit attributes it to whichever test happens to
            // run next — including unrelated fixtures (Items, EditorUIHelpers,
            // …) — flagging them red with "Unhandled log message".
            LogAssert.ignoreFailingMessages = true;

            // First priority: get the user's persistence file back. Wrapped
            // independently so a destroy-immediate failure later in TearDown
            // can't strand the user without their zones.
            try
            {
                if (System.IO.File.Exists(_userZonesPrimary))
                    System.IO.File.Delete(_userZonesPrimary);
                if (_hadUserZones && System.IO.File.Exists(_userZonesBackup))
                    System.IO.File.Move(_userZonesBackup, _userZonesPrimary);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[MapEditorTests] Could not restore user zones " +
                                 $"(MapEditorDataGuard will recover on next Editor load): {ex.Message}");
            }
            // Drop the test-poisoned sidecar (File.Replace produced it from
            // the in-test primary, not the user's data) and restore the
            // production sidecar parked in SetUp.
            try { if (System.IO.File.Exists(_userZonesSidecar)) System.IO.File.Delete(_userZonesSidecar); } catch { }
            try
            {
                if (_hadUserSidecar && System.IO.File.Exists(_userZonesSidecarBackup))
                    System.IO.File.Move(_userZonesSidecarBackup, _userZonesSidecar);
            }
            catch { }
            try { if (System.IO.File.Exists(_userZonesBackup)) System.IO.File.Delete(_userZonesBackup); } catch { }
            try { if (System.IO.File.Exists(_userZonesSidecarBackup)) System.IO.File.Delete(_userZonesSidecarBackup); } catch { }

            foreach (var go in _sceneObjects)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            _sceneObjects.Clear();

            foreach (var so in _assets)
                if (so != null) UnityEngine.Object.DestroyImmediate(so);
            _assets.Clear();

            // Restore default so the next fixture's tests start clean. Any
            // exceptions leaking from this TearDown have already happened
            // before this line and were absorbed.
            LogAssert.ignoreFailingMessages = false;
        }

        // ── Reflection helpers ────────────────────────────────────────────────────

        protected static void ClearSingletonInstance<T>() where T : MonoBehaviour
        {
            var type = typeof(T).BaseType;
            while (type != null)
            {
                var field = type.GetField("_instance",
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (field != null) { field.SetValue(null, null); return; }
                type = type.BaseType;
            }
        }

        protected T CreateSingleton<T>(string name = "TestGO") where T : MonoBehaviour
        {
            ClearSingletonInstance<T>();
            var go   = new GameObject(name);
            var comp = go.AddComponent<T>();
            InvokeMethod(comp, "OnSingletonAwake");
            _sceneObjects.Add(go);
            return comp;
        }

        protected static FieldInfo GetField(object obj, string name)
        {
            var t = obj.GetType();
            while (t != null)
            {
                var f = t.GetField(name,
                    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                if (f != null) return f;
                t = t.BaseType;
            }
            return null;
        }

        protected static void InvokeMethod(object obj, string methodName, params object[] args)
        {
            var t = obj.GetType();
            MethodInfo m = null;
            while (t != null && m == null)
            {
                m = t.GetMethod(methodName,
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                t = t.BaseType;
            }
            m?.Invoke(obj, args);
        }

        protected static void SetField(object obj, string name, object value)
            => GetField(obj, name)?.SetValue(obj, value);

        protected static object GetFieldValue(object obj, string name)
            => GetField(obj, name)?.GetValue(obj);

        /// <summary>
        /// Creates a minimal MapEditorUI (MonoBehaviour partial class) and calls
        /// Initialize() with no-op callbacks so BuildUI() runs inside EditMode.
        /// </summary>
        protected MapEditorUI CreateInitializedUI()
        {
            var go = new GameObject("MapEditorUI");
            _sceneObjects.Add(go);
            var ui = go.AddComponent<MapEditorUI>();

            var state = new MapEditorState();
            LogAssert.ignoreFailingMessages = true;
            ui.Initialize(
                state,
                _ => { },           // onZoneSelected
                () => { },          // onBeginAddZoneFlow
                (n, t, e) => { },   // onConfirmAddZone
                () => { },          // onCancelAddZoneFlow
                () => { },          // onDuplicateSelectedZone
                () => { },          // onRequestDeleteSelectedZone
                () => { },          // onConfirmDeleteSelectedZone
                _ => { },           // onRenameSelectedZone
                (o, n) => { },      // onRenameZoneByName
                () => { },          // onToggleSelectedZoneEditable
                _ => { },           // onToggleZoneEditableByName
                _ => { },           // onRestrictEditChanged
                _ => { },           // onConfirmGenerateBiomes
                default,            // mapSlotCallbacks (no-op struct)
                default,            // portalCallbacks (no-op struct)
                default);           // stampCallbacks (no-op struct)

            return ui;
        }

        // ── Zone-manager wiring helpers ───────────────────────────────────────────

        /// <summary>Build a manager wired to a real ZoneManager pre-seeded with
        /// the supplied zones. Skips Start() so we don't need camera/UI/world.</summary>
        protected MapEditorManager CreateManagerWithZones(params (string name, Vector2Int offset, bool editable)[] seeds)
        {
            LogAssert.ignoreFailingMessages = true;
            var mgr = CreateSingleton<MapEditorManager>("OpsTestMapEditorManager");

            var zoneManagerGo = new GameObject("OpsTestZoneManager");
            _sceneObjects.Add(zoneManagerGo);
            var zm = zoneManagerGo.AddComponent<ZoneManager>();

            foreach (var (name, offset, editable) in seeds)
                Assert.IsTrue(zm.AddZone(name, offset, editable),
                    $"Seed zone '{name}' must be addable.");

            SetField(mgr, "zoneManager", zm);
            return mgr;
        }

        protected static ZoneManager GetZM(MapEditorManager mgr)
            => (ZoneManager) GetFieldValue(mgr, "zoneManager");

        protected static MapEditorState GetState(MapEditorManager mgr)
            => (MapEditorState) GetFieldValue(mgr, "_state");
    }
}
