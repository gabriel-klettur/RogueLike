using System.Reflection;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode
{
    [TestFixture]
    public class BuildingCollisionLoaderTests
    {
        private static void SetPrivateField(object obj, string name, object value)
        {
            var type = obj.GetType();
            while (type != null)
            {
                var field = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                {
                    field.SetValue(obj, value);
                    return;
                }

                type = type.BaseType;
            }
        }

        private static object CreateEmptyFieldValue(object obj, string name)
        {
            var type = obj.GetType();
            while (type != null)
            {
                var field = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                    return System.Activator.CreateInstance(field.FieldType);
                type = type.BaseType;
            }

            return null;
        }

        private static int CountActiveTileColliders(Transform root)
        {
            int count = 0;
            var colliders = root.GetComponentsInChildren<BoxCollider2D>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                var box = colliders[i];
                if (box == null || box.transform == root)
                    continue;
                if (!box.transform.name.StartsWith("CollTile_"))
                    continue;
                if (box.enabled && box.gameObject.activeInHierarchy)
                    count++;
            }

            return count;
        }

        [Test]
        public void TryApplyGrid_WhenNoGridExists_RestoresDefaultMainCollider()
        {
            var loaderGo = new GameObject("CollisionLoader");
            var loader = loaderGo.AddComponent<BuildingCollisionLoader>();

            SetPrivateField(loader, "_loaded", true);
            SetPrivateField(loader, "_byImage", CreateEmptyFieldValue(loader, "_byImage"));
            SetPrivateField(loader, "_byInstanceId", CreateEmptyFieldValue(loader, "_byInstanceId"));
            SetPrivateField(loader, "_bySpawnId", CreateEmptyFieldValue(loader, "_bySpawnId"));
            SetPrivateField(loader, "_inlineInstanceOverrides", CreateEmptyFieldValue(loader, "_inlineInstanceOverrides"));

            var tmpl = ScriptableObject.CreateInstance<BuildingTemplateData>();
            tmpl.solid = true;

            var buildingGo = new GameObject("Building");
            var building = buildingGo.AddComponent<BuildingObject>();
            var mainCollider = buildingGo.AddComponent<BoxCollider2D>();
            mainCollider.enabled = false;
            building.ColliderScopeOverride = "CG";

            SetPrivateField(building, "_template", tmpl);

            bool applied = loader.TryApplyGrid(building);

            Assert.IsFalse(applied, "No collision data should return false.");
            Assert.IsTrue(mainCollider.enabled,
                "When no grid exists, the default footprint collider must be restored.");

            Object.DestroyImmediate(buildingGo);
            Object.DestroyImmediate(tmpl);
            Object.DestroyImmediate(loaderGo);
        }

        [Test]
        public void TryApplyGrid_WithExplicitEmptyGrid_DisablesDefaultMainCollider()
        {
            string dir = Path.Combine(Application.streamingAssetsPath, "Buildings");
            string byImagePath = Path.Combine(dir, "buildings_collisions_by_image.json");
            bool hadOriginal = File.Exists(byImagePath);
            string originalJson = hadOriginal ? File.ReadAllText(byImagePath) : null;

            Directory.CreateDirectory(dir);
            File.WriteAllText(byImagePath,
                "{\"test/building.png\":{\"width\":2,\"height\":2,\"collision\":[[\".\",\".\"],[\".\",\".\"]],\"grid_ref_size\":[64,64]}}");

            var loaderGo = new GameObject("CollisionLoader");
            var loader = loaderGo.AddComponent<BuildingCollisionLoader>();

            var tmpl = ScriptableObject.CreateInstance<BuildingTemplateData>();
            tmpl.solid = true;
            tmpl.originalScale = new Vector2Int(64, 64);
            tmpl.sourceImagePath = "test/building.png";

            var buildingGo = new GameObject("Building");
            var building = buildingGo.AddComponent<BuildingObject>();
            var mainCollider = buildingGo.AddComponent<BoxCollider2D>();
            mainCollider.enabled = true;
            building.ColliderScopeOverride = "CG";
            building.ScaleOverride = new Vector2Int(64, 64);
            SetPrivateField(building, "_template", tmpl);

            try
            {
                bool applied = loader.TryApplyGrid(building);

                Assert.IsTrue(applied,
                    "An explicit empty grid should still count as an applied override.");
                // An all-walkable authored grid (no '#' cells) is treated as "no active override":
                // the root collider stays enabled so that buildings with placeholder JSON entries
                // (not yet painted) still block movement. Disabling the root here would silently
                // make 'solid' buildings walk-throughable.
                Assert.IsTrue(mainCollider.enabled,
                    "An all-walkable authored grid must NOT disable the default footprint collider " +
                    "(placeholder protection: unpainted grids should not break blocking).");
            }
            finally
            {
                if (hadOriginal) File.WriteAllText(byImagePath, originalJson);
                else if (File.Exists(byImagePath)) File.Delete(byImagePath);

                Object.DestroyImmediate(buildingGo);
                Object.DestroyImmediate(tmpl);
                Object.DestroyImmediate(loaderGo);
            }
        }

        [Test]
        public void TryApplyGrid_ReapplyingGrid_KeepsExpectedActiveTileCount()
        {
            string dir = Path.Combine(Application.streamingAssetsPath, "Buildings");
            string byImagePath = Path.Combine(dir, "buildings_collisions_by_image.json");
            bool hadOriginal = File.Exists(byImagePath);
            string originalJson = hadOriginal ? File.ReadAllText(byImagePath) : null;

            Directory.CreateDirectory(dir);
            File.WriteAllText(byImagePath,
                "{\"test/building.png\":{\"width\":2,\"height\":2,\"collision\":[[\"#\",\".\"],[\".\",\"#\"]],\"grid_ref_size\":[64,64]}}");

            var loaderGo = new GameObject("CollisionLoader");
            var loader = loaderGo.AddComponent<BuildingCollisionLoader>();

            var tmpl = ScriptableObject.CreateInstance<BuildingTemplateData>();
            tmpl.solid = true;
            tmpl.originalScale = new Vector2Int(64, 64);
            tmpl.sourceImagePath = "test/building.png";

            var buildingGo = new GameObject("Building");
            var building = buildingGo.AddComponent<BuildingObject>();
            var mainCollider = buildingGo.AddComponent<BoxCollider2D>();
            mainCollider.enabled = true;
            building.ColliderScopeOverride = "CG";
            building.ScaleOverride = new Vector2Int(64, 64);
            SetPrivateField(building, "_template", tmpl);

            try
            {
                Assert.IsTrue(loader.TryApplyGrid(building));
                Assert.AreEqual(2, CountActiveTileColliders(buildingGo.transform),
                    "The first authored grid should activate exactly its solid cells.");

                File.WriteAllText(byImagePath,
                    "{\"test/building.png\":{\"width\":2,\"height\":2,\"collision\":[[\".\",\"#\"],[\".\",\".\"]],\"grid_ref_size\":[64,64]}}");
                SetPrivateField(loader, "_loaded", false);

                Assert.IsTrue(loader.TryApplyGrid(building));
                Assert.AreEqual(1, CountActiveTileColliders(buildingGo.transform),
                    "Reapplying a different grid should not leave stale active tile colliders behind.");
                Assert.IsFalse(mainCollider.enabled,
                    "The main footprint collider must stay disabled while authored tile colliders are active.");

                int pooledTiles = 0;
                for (int i = 0; i < buildingGo.transform.childCount; i++)
                {
                    var child = buildingGo.transform.GetChild(i);
                    if (child.name.StartsWith("_PooledCollTile_"))
                        pooledTiles++;
                }

                Assert.GreaterOrEqual(pooledTiles, 1,
                    "Reapplying a different grid should recycle inactive tile colliders instead of destroying them immediately.");
            }
            finally
            {
                if (hadOriginal) File.WriteAllText(byImagePath, originalJson);
                else if (File.Exists(byImagePath)) File.Delete(byImagePath);

                Object.DestroyImmediate(buildingGo);
                Object.DestroyImmediate(tmpl);
                Object.DestroyImmediate(loaderGo);
            }
        }

        // ── GROUP 4: CU-scope all-walkable grid handling (consistency fix) ────────

        /// <summary>
        /// A per-instance (CU) all-walkable grid represents an explicit "reset to walkable"
        /// authored by the user in BuildingsRuntimeEditor. The loader must DISABLE the root
        /// BoxCollider2D so physics matches the editor's authored state.
        ///
        /// Regression: before the fix, the HasSolidCells guard kept the root enabled even for
        /// intentional CU walkable resets, creating a mismatch — physics said "solid" while
        /// the editor overlay said "walkable".
        /// </summary>
        [Test]
        public void TryApplyGrid_CUScopeAllWalkableGrid_DisablesRootCollider()
        {
            string dir        = Path.Combine(Application.streamingAssetsPath, "Buildings");
            string instancesPath = Path.Combine(dir, "buildings_instances.json");
            bool   hadOriginal   = File.Exists(instancesPath);
            string originalJson  = hadOriginal ? File.ReadAllText(instancesPath) : null;

            Directory.CreateDirectory(dir);
            // Inline all-walkable collision_override for instance id=42, scope CU.
            File.WriteAllText(instancesPath,
                "[{\"id\":42,\"template_id\":1,\"zone\":\"test\",\"rel_x\":0,\"rel_y\":0," +
                "\"overrides\":{\"collider_scope\":\"CU\",\"collision_override\":{" +
                "\"width\":2,\"height\":2," +
                "\"collision\":[[\".\",\".\"],[\".\",\".\"]],\"grid_ref_size\":[64,64]}}}]");

            var loaderGo = new GameObject("CollisionLoader");
            var loader   = loaderGo.AddComponent<BuildingCollisionLoader>();

            var tmpl = ScriptableObject.CreateInstance<BuildingTemplateData>();
            tmpl.solid          = true;
            tmpl.originalScale  = new Vector2Int(64, 64);
            tmpl.colliderScope  = "CG";          // template default; instance override takes precedence

            var buildingGo   = new GameObject("Building");
            var building     = buildingGo.AddComponent<BuildingObject>();
            var mainCollider = buildingGo.AddComponent<BoxCollider2D>();
            mainCollider.enabled = true;
            building.ColliderScopeOverride = "CU";   // per-instance scope
            building.ScaleOverride         = new Vector2Int(64, 64);
            SetPrivateField(building, "_template",   tmpl);
            SetPrivateField(building, "_instanceId", 42);

            try
            {
                bool applied = loader.TryApplyGrid(building);

                Assert.IsTrue(applied,
                    "TryApplyGrid must return true when a CU grid exists (even all-walkable).");
                Assert.IsFalse(mainCollider.enabled,
                    "CU all-walkable grid = intentional 'reset to walkable': root BoxCollider2D must be DISABLED " +
                    "so physics matches the Buildings Editor's authored state.");
                Assert.AreEqual(0, CountActiveTileColliders(buildingGo.transform),
                    "All-walkable CU grid must produce zero active CollTile children.");
            }
            finally
            {
                if (hadOriginal) File.WriteAllText(instancesPath, originalJson);
                else if (File.Exists(instancesPath)) File.Delete(instancesPath);

                Object.DestroyImmediate(buildingGo);
                Object.DestroyImmediate(tmpl);
                Object.DestroyImmediate(loaderGo);
            }
        }

        /// <summary>
        /// A per-image (CG) all-walkable grid is treated as an unintentional placeholder —
        /// NOT an explicit walkable reset. The root BoxCollider2D must stay enabled so
        /// buildings with unpainted CG JSON entries still physically block movement.
        ///
        /// This is the original guard that prevented 140/142 buildings from being
        /// walk-throughable due to placeholder entries in buildings_collisions_by_image.json.
        /// </summary>
        [Test]
        public void TryApplyGrid_CGScopeAllWalkableGrid_KeepsRootColliderEnabled()
        {
            string dir         = Path.Combine(Application.streamingAssetsPath, "Buildings");
            string byImagePath = Path.Combine(dir, "buildings_collisions_by_image.json");
            bool   hadOriginal = File.Exists(byImagePath);
            string originalJson = hadOriginal ? File.ReadAllText(byImagePath) : null;

            Directory.CreateDirectory(dir);
            File.WriteAllText(byImagePath,
                "{\"test/building.png\":{\"width\":2,\"height\":2," +
                "\"collision\":[[\".\",\".\"],[\".\",\".\"]],\"grid_ref_size\":[64,64]}}");

            var loaderGo = new GameObject("CollisionLoader");
            var loader   = loaderGo.AddComponent<BuildingCollisionLoader>();

            var tmpl = ScriptableObject.CreateInstance<BuildingTemplateData>();
            tmpl.solid          = true;
            tmpl.originalScale  = new Vector2Int(64, 64);
            tmpl.sourceImagePath = "test/building.png";
            tmpl.colliderScope  = "CG";

            var buildingGo   = new GameObject("Building");
            var building     = buildingGo.AddComponent<BuildingObject>();
            var mainCollider = buildingGo.AddComponent<BoxCollider2D>();
            mainCollider.enabled = false;
            building.ColliderScopeOverride = "CG";
            building.ScaleOverride         = new Vector2Int(64, 64);
            SetPrivateField(building, "_template", tmpl);

            try
            {
                bool applied = loader.TryApplyGrid(building);

                Assert.IsTrue(applied,
                    "TryApplyGrid should return true even for all-walkable CG grids.");
                Assert.IsTrue(mainCollider.enabled,
                    "CG all-walkable grid = placeholder: root BoxCollider2D must stay ENABLED " +
                    "so buildings with unpainted image-level JSON entries still block movement.");
            }
            finally
            {
                if (hadOriginal) File.WriteAllText(byImagePath, originalJson);
                else if (File.Exists(byImagePath)) File.Delete(byImagePath);

                Object.DestroyImmediate(buildingGo);
                Object.DestroyImmediate(tmpl);
                Object.DestroyImmediate(loaderGo);
            }
        }

        /// <summary>
        /// Both CU all-walkable tests together assert the full behavioral contract:
        /// CU scope → root disabled (Fix A2); CG scope → root enabled (original guard preserved).
        /// This naming-pairs test documents WHY both branches exist in <c>TryApplyGrid()</c>.
        /// </summary>
        [Test]
        public void TryApplyGrid_CUScopeVsCGScope_AllWalkable_BehaviourDiffers()
        {
            // CU side
            string dir           = Path.Combine(Application.streamingAssetsPath, "Buildings");
            string instancesPath = Path.Combine(dir, "buildings_instances.json");
            bool   hadInstances  = File.Exists(instancesPath);
            string origInstances = hadInstances ? File.ReadAllText(instancesPath) : null;

            Directory.CreateDirectory(dir);
            File.WriteAllText(instancesPath,
                "[{\"id\":77,\"template_id\":1,\"zone\":\"test\",\"rel_x\":0,\"rel_y\":0," +
                "\"overrides\":{\"collider_scope\":\"CU\",\"collision_override\":{" +
                "\"width\":2,\"height\":2," +
                "\"collision\":[[\".\",\".\"],[\".\",\".\"]],\"grid_ref_size\":[64,64]}}}]");

            var loaderCU = new GameObject("LoaderCU").AddComponent<BuildingCollisionLoader>();

            var tmplCU       = ScriptableObject.CreateInstance<BuildingTemplateData>();
            tmplCU.solid     = true;
            tmplCU.originalScale = new Vector2Int(64, 64);

            var goCU       = new GameObject("Building_CU");
            var bCU        = goCU.AddComponent<BuildingObject>();
            var collCU     = goCU.AddComponent<BoxCollider2D>();
            collCU.enabled = true;
            bCU.ColliderScopeOverride = "CU";
            bCU.ScaleOverride         = new Vector2Int(64, 64);
            SetPrivateField(bCU, "_template",   tmplCU);
            SetPrivateField(bCU, "_instanceId", 77);

            loaderCU.TryApplyGrid(bCU);

            // CG side (reuse the same loader after it already loaded instances)
            string byImagePath = Path.Combine(dir, "buildings_collisions_by_image.json");
            bool   hadByImage  = File.Exists(byImagePath);
            string origByImage = hadByImage ? File.ReadAllText(byImagePath) : null;

            File.WriteAllText(byImagePath,
                "{\"test/scope_test.png\":{\"width\":2,\"height\":2," +
                "\"collision\":[[\".\",\".\"],[\".\",\".\"]],\"grid_ref_size\":[64,64]}}");

            var loaderCG = new GameObject("LoaderCG").AddComponent<BuildingCollisionLoader>();

            var tmplCG            = ScriptableObject.CreateInstance<BuildingTemplateData>();
            tmplCG.solid          = true;
            tmplCG.originalScale  = new Vector2Int(64, 64);
            tmplCG.sourceImagePath = "test/scope_test.png";

            var goCG       = new GameObject("Building_CG");
            var bCG        = goCG.AddComponent<BuildingObject>();
            var collCG     = goCG.AddComponent<BoxCollider2D>();
            collCG.enabled = false;
            bCG.ColliderScopeOverride = "CG";
            bCG.ScaleOverride         = new Vector2Int(64, 64);
            SetPrivateField(bCG, "_template", tmplCG);

            loaderCG.TryApplyGrid(bCG);

            try
            {
                Assert.IsFalse(collCU.enabled,
                    "CU all-walkable → root DISABLED (intentional walkable reset, Fix A2).");
                Assert.IsTrue(collCG.enabled,
                    "CG all-walkable → root ENABLED (placeholder protection, original guard).");
            }
            finally
            {
                if (hadInstances) File.WriteAllText(instancesPath, origInstances);
                else if (File.Exists(instancesPath)) File.Delete(instancesPath);

                if (hadByImage) File.WriteAllText(byImagePath, origByImage);
                else if (File.Exists(byImagePath)) File.Delete(byImagePath);

                Object.DestroyImmediate(goCU);
                Object.DestroyImmediate(tmplCU);
                Object.DestroyImmediate(loaderCU.gameObject);
                Object.DestroyImmediate(goCG);
                Object.DestroyImmediate(tmplCG);
                Object.DestroyImmediate(loaderCG.gameObject);
            }
        }
    }
}
