using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.Bootstrap
{
    /// <summary>
    /// Pins the build-safe half of the F5/F3 catalog wiring:
    /// <c>GameplaySceneSetup.RegisterMonsterCatalogFallback</c> and
    /// <c>.RegisterSpawnerTemplateCatalogFallback</c> register the inspector-assigned
    /// catalog through <see cref="ServiceLocator"/> with NO <c>#if UNITY_EDITOR</c> guard —
    /// unlike the SerializedObject injection next to them, which only runs in the Editor
    /// (<c>GameplaySceneSetup.Systems2.Editors.cs</c> / <c>.Systems2.World.cs</c>).
    ///
    /// This does not by itself populate the F5/F3 pickers in a BUILT player —
    /// <c>EntitiesRuntimeEditor</c>/<c>SpawnerEditorManager</c> (both
    /// <c>Gameplay/Editors/**</c>, out of scope here) have no ServiceLocator-first lookup
    /// yet — but it proves the registration itself survives outside the Editor-only path,
    /// which is the seam a build-safe fallback in those classes will read from next.
    /// </summary>
    public class GameplaySceneSetupEditorCatalogFallbackTests
    {
        private const BindingFlags Instance = BindingFlags.NonPublic | BindingFlags.Instance;

        private GameObject _go;

        [SetUp]
        public void SetUp()
        {
            // ServiceLocator is a static global map — clear the two types this fixture
            // cares about before each test too, not just after, so a leftover from a
            // differently-ordered fixture elsewhere in the suite can't produce a false
            // pass on the "registers nothing" tests.
            ServiceLocator.Unregister<MonsterCatalog>();
            ServiceLocator.Unregister<SpawnerTemplateCatalog>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            ServiceLocator.Unregister<MonsterCatalog>();
            ServiceLocator.Unregister<SpawnerTemplateCatalog>();
        }

        private GameplaySceneSetup CreateSetup()
        {
            _go = new GameObject("GameplaySceneSetupUnderTest");
            return _go.AddComponent<GameplaySceneSetup>();
        }

        private static void SetField(object target, string fieldName, object value)
        {
            var field = typeof(GameplaySceneSetup).GetField(fieldName, Instance);
            Assert.IsNotNull(field, $"GameplaySceneSetup.{fieldName} not found — has it been renamed?");
            field.SetValue(target, value);
        }

        private static void InvokePrivate(object target, string methodName)
        {
            var method = typeof(GameplaySceneSetup).GetMethod(methodName, Instance);
            Assert.IsNotNull(method, $"GameplaySceneSetup.{methodName} not found — has it been renamed?");
            method.Invoke(target, null);
        }

        // ---- MonsterCatalog (F5) ----------------------------------------------

        [Test]
        public void RegisterMonsterCatalogFallback_PublishesTheAssignedCatalogToServiceLocator()
        {
            var setup = CreateSetup();
            var catalog = ScriptableObject.CreateInstance<MonsterCatalog>();
            SetField(setup, "_monsterCatalog", catalog);

            InvokePrivate(setup, "RegisterMonsterCatalogFallback");

            Assert.IsTrue(ServiceLocator.TryGet<MonsterCatalog>(out var resolved),
                "the inspector-assigned MonsterCatalog must reach ServiceLocator " +
                "unconditionally — this is the code path a build actually runs, unlike " +
                "the #if UNITY_EDITOR SerializedObject injection beside it.");
            Assert.AreSame(catalog, resolved);

            Object.DestroyImmediate(catalog);
        }

        [Test]
        public void RegisterMonsterCatalogFallback_NullCatalog_RegistersNothing()
        {
            var setup = CreateSetup();
            SetField(setup, "_monsterCatalog", null);

            InvokePrivate(setup, "RegisterMonsterCatalogFallback");

            Assert.IsFalse(ServiceLocator.TryGet<MonsterCatalog>(out _),
                "an unassigned inspector field must not register a null service.");
        }

        // ---- SpawnerTemplateCatalog (F3) --------------------------------------

        [Test]
        public void RegisterSpawnerTemplateCatalogFallback_PublishesTheAssignedCatalogToServiceLocator()
        {
            var setup = CreateSetup();
            var catalog = ScriptableObject.CreateInstance<SpawnerTemplateCatalog>();
            SetField(setup, "_spawnerTemplateCatalog", catalog);

            InvokePrivate(setup, "RegisterSpawnerTemplateCatalogFallback");

            Assert.IsTrue(ServiceLocator.TryGet<SpawnerTemplateCatalog>(out var resolved));
            Assert.AreSame(catalog, resolved);

            Object.DestroyImmediate(catalog);
        }

        [Test]
        public void RegisterSpawnerTemplateCatalogFallback_NullCatalog_RegistersNothing()
        {
            var setup = CreateSetup();
            SetField(setup, "_spawnerTemplateCatalog", null);

            InvokePrivate(setup, "RegisterSpawnerTemplateCatalogFallback");

            Assert.IsFalse(ServiceLocator.TryGet<SpawnerTemplateCatalog>(out _));
        }
    }
}
