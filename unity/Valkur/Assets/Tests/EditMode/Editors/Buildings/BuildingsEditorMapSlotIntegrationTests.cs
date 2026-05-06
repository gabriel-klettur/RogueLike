using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Buildings;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Editors.Buildings
{
    /// <summary>
    /// Integration tests pinning the contract between
    /// <see cref="BuildingsRuntimeEditor"/> and the Map Editor's slot system.
    ///
    /// Two regressions guarded:
    ///
    ///   1. Bug 2 (data loss): calling <see cref="BuildingsRuntimeEditor.NotifyActiveMapSlotChanged"/>
    ///      flushes the cached collider stores so the next save lands on the
    ///      slot's own files instead of contaminating whatever was loaded for
    ///      the outgoing slot.
    ///
    ///   2. Bug 1 (zoneless placement): on a brand-new map slot the
    ///      <see cref="BuildingLoader._buildingsRoot"/> serialised reference is
    ///      null because LoadBuildings has nothing to spawn yet. CacheBuildingLoader
    ///      must materialise a scene-level BuildingsRoot rather than fall through
    ///      to the editor's own (DontDestroyOnLoad) transform — otherwise
    ///      placements parent under the singleton and BuildingLoader can't see
    ///      them on the next reload.
    /// </summary>
    [TestFixture]
    public class BuildingsEditorMapSlotIntegrationTests
    {
        private readonly List<GameObject>       _scene  = new List<GameObject>();
        private readonly List<ScriptableObject> _assets = new List<ScriptableObject>();

        // ── Reflection helpers (mirrors ColliderBrushTests / Toggle tests) ───

        private static FieldInfo Field(object obj, string name) => Field(obj.GetType(), name);

        private static FieldInfo Field(Type type, string name)
        {
            var t = type;
            while (t != null)
            {
                var f = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Public |
                                          BindingFlags.Instance  | BindingFlags.Static);
                if (f != null) return f;
                t = t.BaseType;
            }
            return null;
        }

        private static MethodInfo Method(Type type, string name, Type[] paramTypes = null)
        {
            var t = type;
            while (t != null)
            {
                MethodInfo m;
                if (paramTypes == null)
                    m = t.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Public |
                                          BindingFlags.Instance  | BindingFlags.Static);
                else
                    m = t.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Public |
                                          BindingFlags.Instance  | BindingFlags.Static,
                                    null, paramTypes, null);
                if (m != null) return m;
                t = t.BaseType;
            }
            return null;
        }

        private static void ClearSingletonInstance<T>() where T : MonoBehaviour
        {
            var t = typeof(T).BaseType;
            while (t != null)
            {
                var f = t.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
                if (f != null) { f.SetValue(null, null); return; }
                t = t.BaseType;
            }
        }

        // ── Lifecycle ────────────────────────────────────────────────────────

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            DestroyExistingBuildingsRoot();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _scene)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            _scene.Clear();

            foreach (var so in _assets)
                if (so != null) UnityEngine.Object.DestroyImmediate(so);
            _assets.Clear();

            DestroyExistingBuildingsRoot();
            LogAssert.ignoreFailingMessages = false;
        }

        private static void DestroyExistingBuildingsRoot()
        {
            // Prevent leftover BuildingsRoot GameObjects from prior fixtures
            // from satisfying CacheBuildingLoader's `Find` and skipping the
            // creation branch we want to exercise.
            var existing = GameObject.Find("BuildingsRoot");
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing);
        }

        // ── Factories ────────────────────────────────────────────────────────

        private BuildingsRuntimeEditor CreateEditor()
        {
            ClearSingletonInstance<BuildingsRuntimeEditor>();
            var go = new GameObject("TestBuildingsEditor");
            _scene.Add(go);
            var ed = go.AddComponent<BuildingsRuntimeEditor>();
            // Pretend the authoring stores are loaded so EnsureColliderDataLoaded
            // doesn't try to read JSON files that don't exist in EditMode.
            Field(ed, "_colliderDataLoaded")?.SetValue(ed, true);
            return ed;
        }

        // ═════════════════════════════════════════════════════════════════════
        //  CONTRACT 1 — Notify slot change drops cached state
        // ═════════════════════════════════════════════════════════════════════

        [Test]
        public void NotifyActiveMapSlotChanged_DropsCachedColliderData()
        {
            // Setup: editor with cached collider data from the outgoing slot.
            // The dictionary is typed Dictionary<string, ColliderGridData>;
            // ColliderGridData is a private nested class so we instantiate it
            // via reflection to seed an entry.
            var ed = CreateEditor();
            Field(ed, "_colliderDataLoaded")?.SetValue(ed, true);

            var gridType = typeof(BuildingsRuntimeEditor)
                .GetNestedType("ColliderGridData", BindingFlags.NonPublic);
            Assert.IsNotNull(gridType, "ColliderGridData nested type must exist.");
            object seedGrid = Activator.CreateInstance(gridType);

            var imageStore = (System.Collections.IDictionary)Field(ed, "_colliderImageStore").GetValue(ed);
            imageStore["assets/buildings/test.png"] = seedGrid;
            Assert.AreEqual(1, imageStore.Count, "Pre-condition: store has the seeded entry.");

            // Act
            ed.NotifyActiveMapSlotChanged();

            // Assert: cache is invalidated and the next EnsureColliderDataLoaded
            // would re-resolve the (now slot-specific) paths.
            Assert.IsFalse((bool)Field(ed, "_colliderDataLoaded").GetValue(ed),
                "Notify must reset _colliderDataLoaded so paths get re-resolved against the new slot.");
            Assert.AreEqual(0, imageStore.Count,
                "Cached collider image store must be empty after slot change " +
                "— otherwise CG grids from slot A would leak into slot B.");
        }

        [Test]
        public void NotifyActiveMapSlotChanged_DropsActiveColliderSession()
        {
            // The active session points at the OUTGOING slot's grid; if we
            // didn't clear it, the next paint stroke would mutate slot B's
            // store while displaying slot A's authoring session — confusing
            // at best, corrupting at worst.
            var ed = CreateEditor();
            // Forge a non-null _activeColliderSession via reflection — the
            // exact object identity doesn't matter, we only assert it gets
            // nulled out.
            var sessionType = typeof(BuildingsRuntimeEditor)
                .GetNestedType("ActiveColliderGridSession", BindingFlags.NonPublic);
            Assert.IsNotNull(sessionType);
            var session = Activator.CreateInstance(sessionType);
            Field(ed, "_activeColliderSession")?.SetValue(ed, session);
            Assert.IsNotNull(Field(ed, "_activeColliderSession").GetValue(ed),
                "Pre-condition: a session is cached.");

            ed.NotifyActiveMapSlotChanged();

            Assert.IsNull(Field(ed, "_activeColliderSession").GetValue(ed),
                "Notify must drop the active session so a paint started in slot A " +
                "can't write into slot B.");
        }

        [Test]
        public void NotifyActiveMapSlotChanged_ClearsHoverAndActiveBuilding()
        {
            // Hover/active references point at GameObjects that still exist
            // when the slot changes but no longer represent meaningful state
            // for the new slot's content. Drop them defensively.
            var ed = CreateEditor();

            var template = ScriptableObject.CreateInstance<BuildingTemplateData>();
            template.templateId    = 1;
            template.originalScale = new Vector2Int(64, 64);
            template.solid         = true;
            _assets.Add(template);

            var bGo = new GameObject("HoverBuilding");
            var bObj = bGo.AddComponent<BuildingObject>();
            Field(bObj, "_template")?.SetValue(bObj, template);
            _scene.Add(bGo);

            Field(ed, "_activeBuilding")?.SetValue(ed, bObj);
            Field(ed, "_hoveredBuilding")?.SetValue(ed, bObj);

            ed.NotifyActiveMapSlotChanged();

            Assert.IsNull(Field(ed, "_activeBuilding").GetValue(ed),
                "Active building reference must be cleared on slot change.");
            Assert.IsNull(Field(ed, "_hoveredBuilding").GetValue(ed),
                "Hovered building reference must be cleared on slot change.");
        }

        // ═════════════════════════════════════════════════════════════════════
        //  CONTRACT 2 — CacheBuildingLoader materialises a scene BuildingsRoot
        // ═════════════════════════════════════════════════════════════════════

        [Test]
        public void CacheBuildingLoader_NoBuildingLoader_CreatesSceneBuildingsRoot()
        {
            // Bug-1 regression scenario: zoneless map, no BuildingLoader
            // initialised yet. Placement must still find a real scene-level
            // parent (NOT the editor's own transform) so future reloads can
            // see the spawned buildings.
            var ed = CreateEditor();
            Assert.IsNull(GameObject.Find("BuildingsRoot"),
                "Pre-condition: no BuildingsRoot exists yet.");

            Method(typeof(BuildingsRuntimeEditor), "CacheBuildingLoader", Type.EmptyTypes)
                .Invoke(ed, null);

            var rootGo = GameObject.Find("BuildingsRoot");
            Assert.IsNotNull(rootGo,
                "CacheBuildingLoader must materialise a scene-level BuildingsRoot when none exists.");
            // Track for cleanup.
            _scene.Add(rootGo);

            // The editor's _buildingsRoot must point at the scene root, not
            // at the editor's own transform.
            var editorRoot = (Transform)Field(ed, "_buildingsRoot").GetValue(ed);
            Assert.IsNotNull(editorRoot);
            Assert.AreNotSame(ed.transform, editorRoot,
                "Editor must NOT use its own transform as the buildings parent — that would " +
                "make placements DontDestroyOnLoad and invisible to BuildingLoader on reload.");
            Assert.AreSame(rootGo.transform, editorRoot,
                "Editor's _buildingsRoot must point at the scene-level BuildingsRoot.");
        }

        [Test]
        public void CacheBuildingLoader_WithExistingBuildingsRoot_ReusesIt()
        {
            // If a BuildingsRoot already exists in the scene (because the
            // BuildingLoader created it on boot), CacheBuildingLoader must
            // reuse that one — never spawn a duplicate.
            var preExistingRoot = new GameObject("BuildingsRoot");
            _scene.Add(preExistingRoot);

            var ed = CreateEditor();
            Method(typeof(BuildingsRuntimeEditor), "CacheBuildingLoader", Type.EmptyTypes)
                .Invoke(ed, null);

            var allRoots = GameObject.FindObjectsOfType<GameObject>();
            int rootCount = 0;
            foreach (var go in allRoots)
                if (go != null && go.name == "BuildingsRoot") rootCount++;
            Assert.AreEqual(1, rootCount,
                "CacheBuildingLoader must reuse an existing BuildingsRoot — never spawn duplicates.");

            var editorRoot = (Transform)Field(ed, "_buildingsRoot").GetValue(ed);
            Assert.AreSame(preExistingRoot.transform, editorRoot,
                "Editor must wire its _buildingsRoot to the existing scene BuildingsRoot.");
        }

        [Test]
        public void CacheBuildingLoader_Idempotent_DoesNotRecreateOnSecondCall()
        {
            // Calling CacheBuildingLoader twice must not spawn a second
            // BuildingsRoot (the cached references short-circuit the second call).
            var ed = CreateEditor();
            var cacheMethod = Method(typeof(BuildingsRuntimeEditor),
                "CacheBuildingLoader", Type.EmptyTypes);

            cacheMethod.Invoke(ed, null);
            var rootAfterFirst = GameObject.Find("BuildingsRoot");
            Assert.IsNotNull(rootAfterFirst);
            _scene.Add(rootAfterFirst);

            cacheMethod.Invoke(ed, null);
            int count = 0;
            foreach (var go in GameObject.FindObjectsOfType<GameObject>())
                if (go != null && go.name == "BuildingsRoot") count++;
            Assert.AreEqual(1, count,
                "Second CacheBuildingLoader call must be a no-op — the cached " +
                "_buildingsRoot reference should short-circuit any new GameObject creation.");
        }
    }
}
