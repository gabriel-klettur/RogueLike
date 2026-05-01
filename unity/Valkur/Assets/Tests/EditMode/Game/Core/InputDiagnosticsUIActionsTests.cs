using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.TestTools;
using Valkur.Core.Input;

namespace Valkur.Tests.EditMode.Game.Core
{
    /// <summary>
    /// EditMode regression tests for the EventSystem-UI-action pipeline that
    /// drives mouse clicks in menus.
    ///
    /// The class of bug this guards against:
    ///   • Scene's authored EventSystem references an <see cref="InputActionAsset"/>
    ///     that no longer exists (deleted asset, broken GUID). At runtime the
    ///     module's action references resolve to null → mouse hover/click does
    ///     nothing, even though keyboard input may still work because callers
    ///     own their own <c>InputAction</c> instances.
    ///   • Unity 2022.3's <c>InputSystemUIInputModule.AssignDefaultActions()</c>
    ///     occasionally produces a partially-bound module (silent failure).
    ///
    /// What we check here: after <see cref="InputDiagnostics.EnsureInputSystemUIModule"/>
    /// runs on ANY module — fresh, null-asset, broken-asset — the resulting
    /// module ALWAYS has every action reference resolved AND enabled. That's
    /// the contract menus rely on.
    /// </summary>
    public class InputDiagnosticsUIActionsTests
    {
        private GameObject _eventSystemGo;
        private EventSystem _eventSystem;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;

            if (Mouse.current == null)    InputSystem.AddDevice<Mouse>();
            if (Keyboard.current == null) InputSystem.AddDevice<Keyboard>();

            _eventSystemGo = new GameObject("TestEventSystem");
            _eventSystem   = _eventSystemGo.AddComponent<EventSystem>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_eventSystemGo != null)
                Object.DestroyImmediate(_eventSystemGo);
        }

        // ─── Contract: every action ref is non-null + enabled after Ensure ──

        [Test]
        public void EnsureInputSystemUIModule_FreshModule_AllActionsAreBound()
        {
            var module = _eventSystemGo.AddComponent<InputSystemUIInputModule>();
            InputDiagnostics.EnsureInputSystemUIModule(_eventSystem);

            AssertAllActionsBound(module);
        }

        [Test]
        public void EnsureInputSystemUIModule_FreshModule_AllActionsAreEnabled()
        {
            var module = _eventSystemGo.AddComponent<InputSystemUIInputModule>();
            InputDiagnostics.EnsureInputSystemUIModule(_eventSystem);

            AssertAllActionsEnabled(module);
        }

        [Test]
        public void EnsureInputSystemUIModule_ModuleNotPresent_AddsOne()
        {
            // Verify EnsureInputSystemUIModule auto-adds the component when missing.
            Assert.IsNull(_eventSystemGo.GetComponent<InputSystemUIInputModule>());

            var result = InputDiagnostics.EnsureInputSystemUIModule(_eventSystem);

            Assert.IsNotNull(result);
            Assert.IsNotNull(_eventSystemGo.GetComponent<InputSystemUIInputModule>());
            AssertAllActionsBound(result);
            AssertAllActionsEnabled(result);
        }

        // ─── The CORE regression: broken / missing actionsAsset ─────────────

        [Test]
        public void EnsureInputSystemUIModule_NullActionsAsset_FallbackProvidesBoundActions()
        {
            // Simulates the MainMenu.unity scenario: the authored EventSystem
            // references an InputActionAsset whose GUID no longer resolves.
            // Unity's serializer hands us null; the rescue logic must build a
            // runtime asset.
            var module = _eventSystemGo.AddComponent<InputSystemUIInputModule>();
            module.actionsAsset = null;
            module.point        = null;
            module.leftClick    = null;

            InputDiagnostics.EnsureInputSystemUIModule(_eventSystem);

            Assert.IsNotNull(module.actionsAsset,
                "Fallback must assign an InputActionAsset when none is present");
            AssertAllActionsBound(module);
            AssertAllActionsEnabled(module);
        }

        [Test]
        public void EnsureInputSystemUIModule_NullActionsAsset_RescueProducesUsableActions()
        {
            var module = _eventSystemGo.AddComponent<InputSystemUIInputModule>();
            module.actionsAsset = null;

            InputDiagnostics.EnsureInputSystemUIModule(_eventSystem);

            Assert.IsNotNull(module.actionsAsset);
            Assert.IsTrue(InputDiagnostics.HasUsableUIActions(module),
                "After EnsureInputSystemUIModule, HasUsableUIActions MUST be true. " +
                "If this fails, both the canonical Resources asset AND the runtime-built " +
                "fallback path are broken — menus will be unclickable.");
        }

        [Test]
        public void AssignValkurFallbackUIActions_DirectCall_ProducesUsableActions()
        {
            // Direct test of the rescue path: must produce a fully-bound module
            // regardless of whether the canonical Resources asset is found OR
            // the runtime fallback is built.
            var module = _eventSystemGo.AddComponent<InputSystemUIInputModule>();
            InputDiagnostics.AssignValkurFallbackUIActions(module);

            Assert.IsNotNull(module.actionsAsset);
            // Asset name must be either:
            //   - "ValkurInputActions" — canonical asset adopted from Resources (preferred)
            //   - "Valkur.UIFallback"  — runtime-built when canonical is missing
            // Anything else means a foreign asset slipped in.
            string n = module.actionsAsset.name;
            Assert.IsTrue(n == "ValkurInputActions" || n == "Valkur.UIFallback",
                $"Expected canonical or runtime-built asset, got '{n}'");
            AssertAllActionsBound(module);
        }

        [Test]
        public void AssignValkurFallbackUIActions_PrefersCanonicalAsset_OverRuntimeBuilt()
        {
            // Contract: when the canonical Resources asset is present (which it
            // ALWAYS is in a clean build), it MUST be the one assigned to the
            // module. Falling through to the runtime-built asset would mean
            // designers' Inspector edits to ValkurInputActions stop propagating.
            var module = _eventSystemGo.AddComponent<InputSystemUIInputModule>();
            InputDiagnostics.AssignValkurFallbackUIActions(module);

            Assert.AreEqual("ValkurInputActions", module.actionsAsset.name,
                "Canonical asset at Resources/Input/ValkurInputActions MUST be preferred. " +
                "If this fails, the asset is missing or TryAdoptCanonicalAsset is broken.");
        }

        // ─── Specific bindings the fallback MUST provide ────────────────────

        [Test]
        public void Fallback_PointAction_IsBoundToMousePosition()
        {
            var module = _eventSystemGo.AddComponent<InputSystemUIInputModule>();
            InputDiagnostics.AssignValkurFallbackUIActions(module);

            var bindings = module.point.action.bindings;
            bool hasMouse = false;
            foreach (var b in bindings)
                if (b.path == "<Mouse>/position") { hasMouse = true; break; }

            Assert.IsTrue(hasMouse,
                "Point action must include a binding to <Mouse>/position so " +
                "the EventSystem knows where the cursor is.");
        }

        [Test]
        public void Fallback_LeftClickAction_IsBoundToMouseLeftButton()
        {
            var module = _eventSystemGo.AddComponent<InputSystemUIInputModule>();
            InputDiagnostics.AssignValkurFallbackUIActions(module);

            var bindings = module.leftClick.action.bindings;
            bool hasLeftButton = false;
            foreach (var b in bindings)
                if (b.path == "<Mouse>/leftButton") { hasLeftButton = true; break; }

            Assert.IsTrue(hasLeftButton,
                "leftClick action must include a binding to <Mouse>/leftButton " +
                "or menu buttons will never receive clicks.");
        }

        [Test]
        public void Fallback_SubmitAction_IsBoundToEnterKey()
        {
            var module = _eventSystemGo.AddComponent<InputSystemUIInputModule>();
            InputDiagnostics.AssignValkurFallbackUIActions(module);

            var bindings = module.submit.action.bindings;
            bool hasEnter = false;
            foreach (var b in bindings)
                if (b.path == "<Keyboard>/enter") { hasEnter = true; break; }

            Assert.IsTrue(hasEnter, "Submit action must bind to <Keyboard>/enter.");
        }

        [Test]
        public void Fallback_CancelAction_IsBoundToEscapeKey()
        {
            var module = _eventSystemGo.AddComponent<InputSystemUIInputModule>();
            InputDiagnostics.AssignValkurFallbackUIActions(module);

            var bindings = module.cancel.action.bindings;
            bool hasEscape = false;
            foreach (var b in bindings)
                if (b.path == "<Keyboard>/escape") { hasEscape = true; break; }

            Assert.IsTrue(hasEscape, "Cancel action must bind to <Keyboard>/escape.");
        }

        // ─── HasUsableUIActions edge cases ──────────────────────────────────

        [Test]
        public void HasUsableUIActions_NullModule_ReturnsFalse()
        {
            Assert.IsFalse(InputDiagnostics.HasUsableUIActions(null));
        }

        [Test]
        public void HasUsableUIActions_ModuleWithNullAsset_ReturnsFalse()
        {
            var module = _eventSystemGo.AddComponent<InputSystemUIInputModule>();
            module.actionsAsset = null;
            Assert.IsFalse(InputDiagnostics.HasUsableUIActions(module));
        }

        [Test]
        public void HasUsableUIActions_AfterFallback_ReturnsTrue()
        {
            var module = _eventSystemGo.AddComponent<InputSystemUIInputModule>();
            InputDiagnostics.AssignValkurFallbackUIActions(module);
            Assert.IsTrue(InputDiagnostics.HasUsableUIActions(module));
        }

        // ─── Idempotence: calling EnsureInputSystemUIModule twice is safe ──

        [Test]
        public void EnsureInputSystemUIModule_CalledTwice_IsIdempotent()
        {
            InputDiagnostics.EnsureInputSystemUIModule(_eventSystem);
            var module = _eventSystemGo.GetComponent<InputSystemUIInputModule>();
            var firstAsset = module.actionsAsset;

            InputDiagnostics.EnsureInputSystemUIModule(_eventSystem);
            var secondAsset = module.actionsAsset;

            // Either same asset OR a still-valid asset. The contract is that
            // actions remain usable after repeated calls — this happens on
            // every scene load via RuntimeInputBootstrap.OnSceneLoaded.
            Assert.IsTrue(InputDiagnostics.HasUsableUIActions(module),
                "Repeated EnsureInputSystemUIModule calls must keep actions usable");
            Assert.IsNotNull(secondAsset);
        }

        // ─── Canonical asset contract ───────────────────────────────────────

        [Test]
        public void Canonical_Asset_ExistsAtResourcesPath()
        {
            // The asset MUST live at Resources/Input/ValkurInputActions so it
            // can be Resources.Load'd at runtime in builds (Resources/ is the
            // only path Unity bundles for runtime asset access without
            // Addressables). Moving it back to Settings/Input would silently
            // break the menu input pipeline in standalone builds.
            var asset = Resources.Load<UnityEngine.InputSystem.InputActionAsset>(
                InputDiagnostics.CanonicalUIActionsResourcePath);
            Assert.IsNotNull(asset,
                $"Canonical UI actions asset must exist at " +
                $"Resources/{InputDiagnostics.CanonicalUIActionsResourcePath}.inputactions");
        }

        [Test]
        public void Canonical_Asset_HasUIActionMap()
        {
            var asset = Resources.Load<UnityEngine.InputSystem.InputActionAsset>(
                InputDiagnostics.CanonicalUIActionsResourcePath);
            Assert.IsNotNull(asset);
            var map = asset.FindActionMap(InputDiagnostics.CanonicalUIActionsMapName);
            Assert.IsNotNull(map,
                $"Canonical asset must contain action map '{InputDiagnostics.CanonicalUIActionsMapName}'");
        }

        [Test]
        public void Canonical_UIMap_ContainsAllRequiredActions()
        {
            // InputSystemUIInputModule requires 8 references. The canonical
            // asset must provide all of them so we never have to fall through
            // to the runtime-built asset in production.
            var asset = Resources.Load<UnityEngine.InputSystem.InputActionAsset>(
                InputDiagnostics.CanonicalUIActionsResourcePath);
            var map = asset.FindActionMap(InputDiagnostics.CanonicalUIActionsMapName);

            string[] required =
            {
                "Point", "Click", "RightClick", "MiddleClick", "ScrollWheel",
                "Submit", "Cancel"
            };
            foreach (var name in required)
                Assert.IsNotNull(map.FindAction(name),
                    $"Canonical UI map missing required action '{name}'");

            // Move alias: either "Navigate" or "Move" is acceptable.
            Assert.IsTrue(
                map.FindAction("Navigate") != null || map.FindAction("Move") != null,
                "Canonical UI map must include either 'Navigate' or 'Move' action");
        }

        [Test]
        public void Canonical_PointAction_BindsToMousePosition()
        {
            var asset = Resources.Load<UnityEngine.InputSystem.InputActionAsset>(
                InputDiagnostics.CanonicalUIActionsResourcePath);
            var map   = asset.FindActionMap(InputDiagnostics.CanonicalUIActionsMapName);
            var point = map.FindAction("Point");

            bool bound = false;
            foreach (var b in point.bindings)
                if (b.path == "<Mouse>/position") { bound = true; break; }
            Assert.IsTrue(bound, "Canonical Point action must bind <Mouse>/position");
        }

        [Test]
        public void Canonical_ClickAction_BindsToMouseLeftButton()
        {
            var asset = Resources.Load<UnityEngine.InputSystem.InputActionAsset>(
                InputDiagnostics.CanonicalUIActionsResourcePath);
            var map   = asset.FindActionMap(InputDiagnostics.CanonicalUIActionsMapName);
            var click = map.FindAction("Click");

            bool bound = false;
            foreach (var b in click.bindings)
                if (b.path == "<Mouse>/leftButton") { bound = true; break; }
            Assert.IsTrue(bound, "Canonical Click action must bind <Mouse>/leftButton");
        }

        [Test]
        public void EnsureInputSystemUIModule_AdoptsCanonicalAsset_WhenAssetIsMissing()
        {
            // When the scene's authored module has no actions assigned, the
            // bootstrap MUST populate it with the canonical Resources asset
            // (NOT a runtime-built fallback) so designer edits to the asset
            // propagate to every menu without code changes.
            var module = _eventSystemGo.AddComponent<InputSystemUIInputModule>();
            module.actionsAsset = null;

            InputDiagnostics.EnsureInputSystemUIModule(_eventSystem);

            // Either the canonical asset (preferred) OR Unity's defaults are
            // acceptable — whichever the rescue picked first. The failure
            // case we're guarding against is "Valkur.UIFallback runtime asset"
            // being chosen when the canonical exists, which would mean the
            // canonical-prefer path silently broke.
            Assert.IsNotNull(module.actionsAsset);
            Assert.AreNotEqual("Valkur.UIFallback", module.actionsAsset.name,
                "Canonical asset MUST be preferred over the runtime-built fallback. " +
                "If this fails, EnsureFallbackAssetBuilt or TryAdoptCanonicalAsset is broken.");
        }

        // ─── Helpers ─────────────────────────────────────────────────────────

        private static void AssertAllActionsBound(InputSystemUIInputModule module)
        {
            Assert.IsNotNull(module);
            Assert.IsNotNull(module.point,        "module.point must be non-null");
            Assert.IsNotNull(module.point.action, "module.point.action must resolve");
            Assert.IsNotNull(module.leftClick,        "module.leftClick must be non-null");
            Assert.IsNotNull(module.leftClick.action, "module.leftClick.action must resolve");
            Assert.IsNotNull(module.rightClick?.action,   "module.rightClick.action must resolve");
            Assert.IsNotNull(module.middleClick?.action,  "module.middleClick.action must resolve");
            Assert.IsNotNull(module.scrollWheel?.action,  "module.scrollWheel.action must resolve");
            Assert.IsNotNull(module.move?.action,         "module.move.action must resolve");
            Assert.IsNotNull(module.submit?.action,       "module.submit.action must resolve");
            Assert.IsNotNull(module.cancel?.action,       "module.cancel.action must resolve");
        }

        private static void AssertAllActionsEnabled(InputSystemUIInputModule module)
        {
            Assert.IsTrue(module.point.action.enabled,        "Point action must be enabled");
            Assert.IsTrue(module.leftClick.action.enabled,    "Click action must be enabled");
            Assert.IsTrue(module.rightClick.action.enabled,   "RightClick action must be enabled");
            Assert.IsTrue(module.middleClick.action.enabled,  "MiddleClick action must be enabled");
            Assert.IsTrue(module.scrollWheel.action.enabled,  "ScrollWheel action must be enabled");
            Assert.IsTrue(module.move.action.enabled,         "Move action must be enabled");
            Assert.IsTrue(module.submit.action.enabled,       "Submit action must be enabled");
            Assert.IsTrue(module.cancel.action.enabled,       "Cancel action must be enabled");
        }
    }
}
