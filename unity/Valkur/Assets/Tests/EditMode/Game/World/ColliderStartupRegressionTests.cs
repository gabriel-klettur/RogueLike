using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Data;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode
{
    /// <summary>
    /// Regression tests for the collider startup bugs fixed in GameplaySceneSetup:
    ///
    /// Bug 1 — Buildings: All buildings had BoxCollider2D.enabled=false at game start.
    ///   Root cause: BuildingLoader._autoLoad=false in scene; EnsureBuildingLoader()
    ///   found the existing loader and returned early WITHOUT calling LoadBuildings().
    ///   Fix: EnsureBuildingLoader() now calls LoadBuildings() when SpawnedBuildings.Count==0.
    ///
    /// Bug 2 — Tilemap: Collision tilemap CompositeCollider2D had pathCount=0 at runtime.
    ///   Root cause: WorldLoader.SetTile() invalidates composite geometry at runtime, but
    ///   GenerateGeometry() was never called after world loading.
    ///   Fix: GameplaySceneSetup.RebakeTilemapColliders() calls GenerateGeometry() on all
    ///   CompositeCollider2Ds immediately after LoadWorld() completes.
    ///
    /// Test groups:
    ///   1. Building collider invariant — solid buildings MUST have enabled BoxCollider2D
    ///      after ApplyCollisionGrids() when no authored collision grid exists.
    ///   2. BuildingLoader startup state — SpawnedBuildings.Count is 0 for a fresh
    ///      (no autoLoad) loader, which is the condition that triggers LoadBuildings().
    ///   3. Tilemap composite bake — GenerateGeometry() must be callable without error
    ///      and is the mechanism that makes wall tiles block the player.
    /// </summary>
    [TestFixture]
    public class ColliderStartupRegressionTests
    {
        // ── helpers ──────────────────────────────────────────────────────────────

        private static void SetPrivateField(object obj, string name, object value)
        {
            var type = obj.GetType();
            while (type != null)
            {
                var f = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null) { f.SetValue(obj, value); return; }
                type = type.BaseType;
            }
        }

        // Creates an empty instance of the private field's type (avoids referencing
        // internal types like CollisionGrid that aren't visible from test assemblies).
        private static object CreateEmptyFieldValue(object obj, string name)
        {
            var type = obj.GetType();
            while (type != null)
            {
                var f = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null) return System.Activator.CreateInstance(f.FieldType);
                type = type.BaseType;
            }
            return null;
        }

        // ── Group 1: Building collider invariant ─────────────────────────────────

        /// <summary>
        /// Regression for Bug 1.
        /// ApplyCollisionGrids() on multiple solid buildings with no authored grid
        /// must enable ALL their BoxCollider2Ds (sets enabled = template.solid = true).
        /// If this fails, the player walks through all buildings.
        /// </summary>
        [Test]
        public void ApplyCollisionGrids_MultipleSolidBuildings_NoAuthoredGrid_AllCollidersEnabled()
        {
            const int buildingCount = 5;

            var loaderGo = new GameObject("CollisionLoader");
            var loader   = loaderGo.AddComponent<BuildingCollisionLoader>();

            // Inject empty dictionaries — simulates empty buildings_collisions_by_image.json.
            // Use CreateEmptyFieldValue() because CollisionGrid is a private inner class.
            SetPrivateField(loader, "_loaded",                    true);
            SetPrivateField(loader, "_byImage",                   CreateEmptyFieldValue(loader, "_byImage"));
            SetPrivateField(loader, "_byInstanceId",              CreateEmptyFieldValue(loader, "_byInstanceId"));
            SetPrivateField(loader, "_bySpawnId",                 CreateEmptyFieldValue(loader, "_bySpawnId"));
            SetPrivateField(loader, "_inlineInstanceOverrides",   CreateEmptyFieldValue(loader, "_inlineInstanceOverrides"));

            var tmpl = ScriptableObject.CreateInstance<BuildingTemplateData>();
            tmpl.solid = true;

            var buildingGos = new GameObject[buildingCount];
            for (int i = 0; i < buildingCount; i++)
            {
                var go       = new GameObject($"Building_{i}");
                var bObj     = go.AddComponent<BuildingObject>();
                var mainColl = go.AddComponent<BoxCollider2D>();
                mainColl.enabled = false; // start disabled — simulates the broken state
                SetPrivateField(bObj, "_template", tmpl);
                buildingGos[i] = go;
            }

            loader.ApplyCollisionGrids();

            for (int i = 0; i < buildingCount; i++)
            {
                var coll = buildingGos[i].GetComponent<BoxCollider2D>();
                Assert.IsTrue(coll.enabled,
                    $"Building_{i}: BoxCollider2D must be enabled after ApplyCollisionGrids() " +
                    "when template.solid=true and no authored grid exists.");
            }

            foreach (var go in buildingGos) Object.DestroyImmediate(go);
            Object.DestroyImmediate(tmpl);
            Object.DestroyImmediate(loaderGo);
        }

        /// <summary>
        /// Regression for Bug 1 — non-solid building must remain disabled.
        /// Ensures the fix doesn't accidentally enable walk-through buildings.
        /// </summary>
        [Test]
        public void ApplyCollisionGrids_NonSolidBuilding_NoAuthoredGrid_ColliderStaysDisabled()
        {
            var loaderGo = new GameObject("CollisionLoader");
            var loader   = loaderGo.AddComponent<BuildingCollisionLoader>();

            SetPrivateField(loader, "_loaded",                    true);
            SetPrivateField(loader, "_byImage",                   CreateEmptyFieldValue(loader, "_byImage"));
            SetPrivateField(loader, "_byInstanceId",              CreateEmptyFieldValue(loader, "_byInstanceId"));
            SetPrivateField(loader, "_bySpawnId",                 CreateEmptyFieldValue(loader, "_bySpawnId"));
            SetPrivateField(loader, "_inlineInstanceOverrides",   CreateEmptyFieldValue(loader, "_inlineInstanceOverrides"));

            var tmpl = ScriptableObject.CreateInstance<BuildingTemplateData>();
            tmpl.solid = false; // decorative / walk-through building

            var buildingGo = new GameObject("NonSolidBuilding");
            var bObj       = buildingGo.AddComponent<BuildingObject>();
            var mainColl   = buildingGo.AddComponent<BoxCollider2D>();
            mainColl.enabled = true; // start enabled — should be DISABLED after restore
            SetPrivateField(bObj, "_template", tmpl);

            loader.ApplyCollisionGrids();

            Assert.IsFalse(mainColl.enabled,
                "Non-solid building: BoxCollider2D must be disabled after ApplyCollisionGrids().");

            Object.DestroyImmediate(buildingGo);
            Object.DestroyImmediate(tmpl);
            Object.DestroyImmediate(loaderGo);
        }

        // ── Group 2: BuildingLoader startup state ─────────────────────────────────

        /// <summary>
        /// Regression for Bug 1 — precondition for the EnsureBuildingLoader fix.
        /// A scene-placed BuildingLoader with _autoLoad=false must start with
        /// SpawnedBuildings.Count==0, which is the condition that triggers
        /// GameplaySceneSetup.EnsureBuildingLoader() to call LoadBuildings().
        ///
        /// If this fails (SpawnedBuildings is pre-populated for a fresh loader)
        /// EnsureBuildingLoader would skip loading and buildings would have no colliders.
        /// </summary>
        [Test]
        public void BuildingLoader_FreshInstance_AutoLoadFalse_SpawnedBuildingsIsEmpty()
        {
            var go     = new GameObject("BuildingLoader");
            var loader = go.AddComponent<BuildingLoader>();

            // Simulate what the scene serializes: _autoLoad=false
            SetPrivateField(loader, "_autoLoad", false);

            Assert.AreEqual(0, loader.SpawnedBuildings.Count,
                "A freshly created BuildingLoader with autoLoad=false must have " +
                "SpawnedBuildings.Count==0 — this is the precondition for " +
                "EnsureBuildingLoader() to trigger LoadBuildings().");

            Object.DestroyImmediate(go);
        }

        /// <summary>
        /// Regression for Bug 1 — LoadBuildings() must be safe to call when catalog is null.
        /// GameplaySceneSetup.EnsureBuildingLoader() calls LoadBuildings() on the existing
        /// loader which may not have a catalog assigned. It must log an error and return
        /// gracefully (no NullReferenceException that would crash the startup sequence).
        /// </summary>
        [Test]
        public void BuildingLoader_LoadBuildings_WithNullCatalog_LogsErrorAndDoesNotThrow()
        {
            var go     = new GameObject("BuildingLoader");
            var loader = go.AddComponent<BuildingLoader>();

            // _catalog defaults to null (not assigned in scene inspector)
            // LoadBuildings() must log "[BuildingLoader] BuildingCatalog not assigned." and return.
            UnityEngine.TestTools.LogAssert.Expect(
                LogType.Error,
                "[BuildingLoader] BuildingCatalog not assigned.");

            Assert.DoesNotThrow(() => loader.LoadBuildings(),
                "LoadBuildings() with null catalog must not throw — startup sequence must survive.");

            Assert.AreEqual(0, loader.SpawnedBuildings.Count,
                "No buildings should be spawned when catalog is null.");

            Object.DestroyImmediate(go);
        }

        // ── Group 3: Tilemap composite collider bake ─────────────────────────────

        /// <summary>
        /// Regression for Bug 2.
        /// CompositeCollider2D.GenerateGeometry() must execute without error.
        /// This is the method called by GameplaySceneSetup.RebakeTilemapColliders()
        /// after LoadWorld() to rebuild wall-tile collision geometry.
        /// If this method throws, the game starts with no tile colliders.
        /// </summary>
        [Test]
        public void CompositeCollider2D_GenerateGeometry_DoesNotThrow()
        {
            // Minimal valid setup: Static RB2D + CompositeCollider2D on same object
            var gridGo = new GameObject("Grid");
            gridGo.AddComponent<Grid>();

            var tmGo = new GameObject("Collision");
            tmGo.transform.SetParent(gridGo.transform);
            tmGo.AddComponent<Tilemap>();
            tmGo.AddComponent<TilemapRenderer>();
            tmGo.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Static;
            tmGo.AddComponent<TilemapCollider2D>().usedByComposite = true;
            var cc = tmGo.AddComponent<CompositeCollider2D>();

            Assert.DoesNotThrow(() => cc.GenerateGeometry(),
                "CompositeCollider2D.GenerateGeometry() must not throw — it is called " +
                "by RebakeTilemapColliders() on every CompositeCollider2D in the scene.");

            Object.DestroyImmediate(gridGo);
        }

        /// <summary>
        /// Regression for Bug 2 — pathCount after GenerateGeometry reflects tile content.
        /// An empty tilemap (no tiles) must have pathCount==0 even after GenerateGeometry.
        /// Ensures GenerateGeometry() doesn't fabricate phantom collision paths.
        /// </summary>
        [Test]
        public void CompositeCollider2D_EmptyTilemap_AfterGenerateGeometry_PathCountIsZero()
        {
            var gridGo = new GameObject("Grid");
            gridGo.AddComponent<Grid>();

            var tmGo = new GameObject("Collision");
            tmGo.transform.SetParent(gridGo.transform);
            tmGo.AddComponent<Tilemap>();
            tmGo.AddComponent<TilemapRenderer>();
            tmGo.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Static;
            tmGo.AddComponent<TilemapCollider2D>().usedByComposite = true;
            var cc = tmGo.AddComponent<CompositeCollider2D>();

            cc.GenerateGeometry();

            Assert.AreEqual(0, cc.pathCount,
                "Empty tilemap must have pathCount==0 after GenerateGeometry() — " +
                "pathCount should reflect actual painted tiles, not phantom geometry.");

            Object.DestroyImmediate(gridGo);
        }

        /// <summary>
        /// Regression for Bug 2 — RebakeTilemapColliders() logic: iterate all
        /// CompositeCollider2Ds in the scene and call GenerateGeometry() on each.
        /// Verifies this pattern works for N composite colliders without error.
        /// </summary>
        [Test]
        public void RebakeTilemapColliders_Pattern_NCompositeColliders_AllBakedWithoutError()
        {
            const int n = 3;
            var gos = new GameObject[n];
            var ccs = new CompositeCollider2D[n];

            for (int i = 0; i < n; i++)
            {
                var go = new GameObject($"Tilemap_{i}");
                go.AddComponent<Tilemap>();
                go.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Static;
                go.AddComponent<TilemapCollider2D>().usedByComposite = true;
                ccs[i] = go.AddComponent<CompositeCollider2D>();
                gos[i] = go;
            }

            // This replicates exactly what RebakeTilemapColliders() does:
            Assert.DoesNotThrow(() =>
            {
                foreach (var cc in Object.FindObjectsOfType<CompositeCollider2D>())
                    cc.GenerateGeometry();
            }, "RebakeTilemapColliders() pattern must execute without error for N colliders.");

            foreach (var go in gos) Object.DestroyImmediate(go);
        }

        // ── Group 4: TilemapColliderDebugOverlay ─────────────────────────────────

        /// <summary>
        /// TilemapColliderDebugOverlay.SetVisible() must toggle the component's
        /// <c>enabled</c> state, which gates <c>OnRenderObject()</c> execution.
        /// This is how BuildingsRuntimeEditor shows/hides the GL-line tilemap overlay.
        /// </summary>
        [Test]
        public void TilemapColliderDebugOverlay_SetVisible_TogglesEnabled()
        {
            // CompositeCollider2D requires Rigidbody2D on the same object.
            var go = new GameObject("CollisionTilemap");
            go.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Static;
            go.AddComponent<CompositeCollider2D>();

            // TilemapColliderDebugOverlay has [RequireComponent(typeof(CompositeCollider2D))].
            // AddComponent auto-resolves that requirement; CC was already added above.
            var overlay = go.AddComponent<TilemapColliderDebugOverlay>();

            overlay.SetVisible(false);
            Assert.IsFalse(overlay.enabled,
                "SetVisible(false) must disable the component — OnRenderObject is skipped when disabled.");

            overlay.SetVisible(true);
            Assert.IsTrue(overlay.enabled,
                "SetVisible(true) must enable the component — OnRenderObject runs and draws GL lines.");

            Object.DestroyImmediate(go);
        }

        /// <summary>
        /// A second <see cref="TilemapColliderDebugOverlay"/> must NOT be addable to
        /// the same GameObject (disallowed by <c>[DisallowMultipleComponent]</c>).
        /// Verifies the guard prevents duplicate overlay rendering.
        /// </summary>
        [Test]
        public void TilemapColliderDebugOverlay_DisallowMultipleComponent_OnSameGO()
        {
            var go = new GameObject("CollisionTilemap");
            go.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Static;
            go.AddComponent<CompositeCollider2D>();

            var first  = go.AddComponent<TilemapColliderDebugOverlay>();
            var second = go.AddComponent<TilemapColliderDebugOverlay>(); // Unity may return null, but must not duplicate.

            Assert.IsTrue(second == null || ReferenceEquals(first, second),
                "[DisallowMultipleComponent] must not create a duplicate overlay.");
            Assert.AreEqual(1, go.GetComponents<TilemapColliderDebugOverlay>().Length,
                "[DisallowMultipleComponent] must leave exactly one overlay on the GameObject.");

            Object.DestroyImmediate(go);
        }
    }
}
