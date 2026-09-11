using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using Valkur.Core.Input;

namespace Valkur.Tests.EditMode.Game.HUD
{
    /// <summary>
    /// The world map's key. It is an action in the asset and a row in the closed catalogue —
    /// never a literal key read — so the Controls editor can list it, move it and silence it,
    /// and the conflict scanner can see it.
    /// </summary>
    public class WorldMapHotkeyTests
    {
        private const string AssetPath = "Input/ValkurInputActions";

        private static InputActionAsset LoadAsset()
        {
            var json = Resources.Load<TextAsset>(AssetPath);
            if (json != null) return InputActionAsset.FromJson(json.text);
            var asset = Resources.Load<InputActionAsset>(AssetPath);
            Assert.IsNotNull(asset, "the shipped input asset must load");
            return asset;
        }

        [Test]
        public void TheCatalogue_DescribesIt_AsHarmless()
        {
            var d = InputActionCatalog.Find(InputActionCatalog.MapGameplay, "OpenWorldMap");
            Assert.IsNotNull(d, "every action in the asset needs a descriptor");
            Assert.IsFalse(d.ReachesDamage, "a map reaches no damage path, so Peace may keep it");
        }

        [Test]
        public void TheAsset_BindsItToN_AndNothingElseInGameplayUsesN()
        {
            var asset = LoadAsset();
            var map = asset.FindActionMap("Gameplay", true);
            var action = map.FindAction("OpenWorldMap", true);
            Assert.AreEqual(1, action.bindings.Count);
            Assert.AreEqual("<Keyboard>/n", action.bindings[0].path);

            foreach (var b in map.bindings)
                if (b.path == "<Keyboard>/n" && b.action != "OpenWorldMap")
                    Assert.Fail($"'{b.action}' is also on n in the Gameplay map");
        }
    }
}
