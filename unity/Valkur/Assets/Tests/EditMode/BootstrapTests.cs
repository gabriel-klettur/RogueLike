using NUnit.Framework;
using UnityEngine;

namespace Valkur.Tests.EditMode
{
    /// <summary>
    /// Smoke tests to verify project structure and core setup.
    /// </summary>
    public class BootstrapTests
    {
        [Test]
        public void Physics2D_Gravity_IsZero_ForTopDown()
        {
            Assert.AreEqual(Vector2.zero, Physics2D.gravity,
                "Top-down game requires Physics2D gravity = (0, 0)");
        }

        [Test]
        public void SortingLayers_ContainRequired()
        {
            var layers = SortingLayer.layers;
            var names = new string[layers.Length];
            for (int i = 0; i < layers.Length; i++)
                names[i] = layers[i].name;

            Assert.Contains("Ground", names, "Missing sorting layer: Ground");
            Assert.Contains("Entities", names, "Missing sorting layer: Entities");
            Assert.Contains("VFX", names, "Missing sorting layer: VFX");
            Assert.Contains("Overlay", names, "Missing sorting layer: Overlay");
        }

        [Test]
        public void ProjectSettings_InputSystem_IsEnabled()
        {
            // activeInputHandler=2 means "Both" (old + new)
            // If Input System package is installed, UnityEngine.InputSystem namespace exists
            var inputSystemType = System.Type.GetType(
                "UnityEngine.InputSystem.InputSystem, Unity.InputSystem");
            Assert.IsNotNull(inputSystemType,
                "Input System package should be installed and accessible");
        }
    }
}
