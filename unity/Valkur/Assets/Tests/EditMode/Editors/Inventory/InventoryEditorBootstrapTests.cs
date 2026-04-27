using System.Reflection;
using NUnit.Framework;
using Valkur.Gameplay;
using Valkur.Gameplay.Inventory;

namespace Valkur.Tests.EditMode.Editors.Inventory
{
    /// <summary>
    /// Bootstrap regression: verifies that <see cref="GameplaySceneSetup"/>
    /// declares an <c>EnsureInventoryRuntimeEditor</c> method.
    ///
    /// This locks in the fix for the original "F6 does nothing" bug, where
    /// the editor was added to the codebase but never instantiated by the
    /// scene bootstrap (it had no <c>EnsureXxx</c> entry).
    /// </summary>
    [TestFixture]
    public class InventoryEditorBootstrapTests
    {
        [Test]
        public void GameplaySceneSetup_DeclaresEnsureInventoryRuntimeEditor()
        {
            var t = typeof(GameplaySceneSetup);
            var m = t.GetMethod("EnsureInventoryRuntimeEditor",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            Assert.IsNotNull(m,
                "GameplaySceneSetup must declare EnsureInventoryRuntimeEditor() so " +
                "InventoryRuntimeEditor is instantiated at scene start. Without this, " +
                "F6 does nothing because the editor never exists in the scene.");
        }

        [Test]
        public void InventoryRuntimeEditor_IsRegisteredOnIGameEditor_Interface()
        {
            // Sanity: the editor implements the interface that GameEditorManager uses
            // to dispatch the F6 toggle.
            Assert.IsTrue(
                typeof(Valkur.Core.GameEditorManager.IGameEditor)
                    .IsAssignableFrom(typeof(InventoryRuntimeEditor)),
                "InventoryRuntimeEditor must implement GameEditorManager.IGameEditor.");
        }
    }
}
