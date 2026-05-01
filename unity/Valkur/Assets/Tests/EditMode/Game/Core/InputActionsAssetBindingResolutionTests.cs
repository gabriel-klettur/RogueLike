using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using Valkur.Core.Input;

namespace Valkur.Tests.EditMode.Game.Core
{
    /// <summary>
    /// Regression tests for the canonical <c>ValkurInputActions.inputactions</c>
    /// asset.
    ///
    /// The class of bug this guards against:
    ///   At runtime <see cref="UnityEngine.InputSystem.UI.InputSystemUIInputModule"/>
    ///   calls <c>module.actionsAsset.Enable()</c>, which forces every action
    ///   map in the asset to resolve its bindings — even maps the UI module
    ///   does not consume (e.g. the <c>Gameplay</c> map). If ANY composite
    ///   binding in the asset has an empty <c>path</c> field (instead of
    ///   <c>"2DVector"</c> / <c>"Dpad"</c>), the resolver throws
    ///   <c>NullReferenceException</c> from <c>InputBindingResolver.AddActionMap</c>
    ///   the moment the EventSystem's <c>OnEnable</c> fires, and the menu
    ///   logs a NRE on every scene load.
    ///
    /// Historical incident: the WASD composite of <c>Gameplay/Move</c> was
    /// authored with <c>"path": ""</c>; the UI map had it correct. The
    /// scene's EventSystem resolved fine on its own actions but the broken
    /// Gameplay composite blew up as soon as the asset was Enable()'d.
    /// </summary>
    public class InputActionsAssetBindingResolutionTests
    {
        private InputActionAsset _asset;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            _asset = Resources.Load<InputActionAsset>(
                InputDiagnostics.CanonicalUIActionsResourcePath);
            Assert.IsNotNull(_asset,
                $"Canonical asset missing at Resources/{InputDiagnostics.CanonicalUIActionsResourcePath}.");
        }

        [TearDown]
        public void TearDown()
        {
            if (_asset != null)
                _asset.Disable();
        }

        [Test]
        public void EveryCompositeBinding_HasNonEmptyPath()
        {
            // The historical bug: a 2DVector composite serialized with path "".
            // Unity's InputBindingResolver looks up the composite type by
            // binding.path; an empty string returns null and throws NRE
            // during ResolveBindings. Catch this in the asset itself rather
            // than at the runtime call site.
            foreach (var map in _asset.actionMaps)
            {
                foreach (var b in map.bindings)
                {
                    if (!b.isComposite) continue;

                    Assert.IsFalse(string.IsNullOrEmpty(b.path),
                        $"Composite binding '{b.name}' on action " +
                        $"'{map.name}/{b.action}' has empty path. " +
                        "Composite bindings MUST declare a composite type " +
                        "(e.g. '2DVector', 'Dpad', 'OneModifier'). An empty " +
                        "path causes InputBindingResolver to throw NRE the " +
                        "moment the asset is Enable()'d.");
                }
            }
        }

        [Test]
        public void EveryActionMap_EnablesWithoutThrowing()
        {
            // End-to-end contract: enabling each map individually MUST NOT
            // throw. This is the exact code path InputSystemUIInputModule
            // takes via actionsAsset.Enable() in OnEnable().
            foreach (var map in _asset.actionMaps)
            {
                Assert.DoesNotThrow(() => map.Enable(),
                    $"ActionMap '{map.name}' threw on Enable(). The most " +
                    "common cause is a composite binding with empty path.");
            }
        }

        [Test]
        public void EnableEntireAsset_DoesNotThrow()
        {
            // The exact call InputSystemUIInputModule.OnEnable performs.
            // If this throws, EVERY scene with an EventSystem referencing
            // ValkurInputActions logs an NRE on load.
            Assert.DoesNotThrow(() => _asset.Enable(),
                "asset.Enable() threw. This is what " +
                "InputSystemUIInputModule.OnEnable triggers — failure here " +
                "means menus log a NullReferenceException on every scene load.");
        }

        [Test]
        public void GameplayMap_MoveAction_WASDComposite_IsValid2DVector()
        {
            // Specifically pin the historical incident: the Gameplay/Move
            // WASD composite must declare path "2DVector". If it ever
            // regresses to "" or "Dpad" (which expects single-button parts,
            // not Vector2 directions), this test fails fast.
            var map = _asset.FindActionMap("Gameplay");
            Assert.IsNotNull(map, "Gameplay action map missing");

            var move = map.FindAction("Move");
            Assert.IsNotNull(move, "Gameplay/Move action missing");

            bool foundWasdComposite = false;
            foreach (var b in move.bindings)
            {
                if (!b.isComposite) continue;
                if (b.name != "WASD") continue;

                foundWasdComposite = true;
                Assert.AreEqual("2DVector", b.path,
                    $"Gameplay/Move WASD composite path is '{b.path}'. " +
                    "Must be '2DVector' — empty or any other value triggers " +
                    "the InputBindingResolver NRE we shipped a fix for.");
            }

            Assert.IsTrue(foundWasdComposite,
                "Gameplay/Move must contain a 'WASD' composite binding " +
                "covering W/A/S/D directional movement.");
        }

        [Test]
        public void UIMap_NavigateAction_CompositesAreValid2DVector()
        {
            // The UI map's Navigate action has TWO 2DVector composites
            // (Arrows + WASD) so menu navigation works on both arrow keys
            // and WASD. Both must be valid.
            var map = _asset.FindActionMap("UI");
            Assert.IsNotNull(map, "UI action map missing");

            var navigate = map.FindAction("Navigate");
            Assert.IsNotNull(navigate, "UI/Navigate action missing");

            int compositeCount = 0;
            foreach (var b in navigate.bindings)
            {
                if (!b.isComposite) continue;
                compositeCount++;
                Assert.AreEqual("2DVector", b.path,
                    $"UI/Navigate composite '{b.name}' has path '{b.path}', " +
                    "must be '2DVector'.");
            }

            Assert.GreaterOrEqual(compositeCount, 1,
                "UI/Navigate must contain at least one composite binding " +
                "(Arrows or WASD).");
        }
    }
}
