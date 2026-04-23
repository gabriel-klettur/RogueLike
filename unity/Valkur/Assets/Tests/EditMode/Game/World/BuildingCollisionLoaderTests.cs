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
                Assert.IsFalse(mainCollider.enabled,
                    "An all-walkable authored grid must disable the default footprint collider.");
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
    }
}
