using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine.InputSystem;
using Valkur.Core.Input;
using Valkur.Gameplay.Editors.General;

namespace Valkur.Tests.EditMode.Game.Bootstrap
{
    /// <summary>
    /// There is ONE way into a runtime editor: the General Editor, on Escape.
    ///
    /// <para>This replaced <c>FKeyBindingParityTests</c>, 472 lines asserting that each editor
    /// sat on the F-key its Python ancestor used. That contract is retired: the F-row was the
    /// source of every same-map collision in the project — F2 held Combat Ranges AND Time &amp;
    /// Weather, F3 held Spawner AND Lighting, F5 held Entities AND QuickSave, F9 held Debug HUD
    /// AND QuickLoad, and while a perf-probe overlay was up F2-F7 fired the probe's bisection
    /// as well. Thirteen keys carrying twenty meanings, three of them separated only by a
    /// modifier that lived in C# rather than in the binding.</para>
    ///
    /// <para>The actions are NOT deleted, and that distinction is the point: they ship UNBOUND,
    /// so the Controls editor lists them as "sin asignar" and a player who wants F8 back can
    /// put it there. Deleting them would have made the menu the only possibility rather than
    /// the default.</para>
    /// </summary>
    [TestFixture]
    public class EditorEntryPointTests
    {
        /// <summary>The fourteen that were on F1-F12 and now ship unbound.</summary>
        private static readonly string[] RetiredToggles =
        {
            "ToggleParticles", "ToggleCombatRanges", "ToggleTimeWeather", "ToggleSpawner",
            "ToggleLighting", "ToggleSpells", "ToggleEntities", "ToggleInventory",
            "ToggleItems", "ToggleTile", "ToggleDebugHUD", "ToggleBuildings",
            "ToggleMap", "ToggleFSM",
        };

        private static InputActionMap EditorsMap()
        {
            var asset = InputService.Initialize()?.Asset;
            Assert.IsNotNull(asset, "InputService must bootstrap from the canonical asset.");
            var map = asset.FindActionMap(InputActionCatalog.MapEditors, throwIfNotFound: false);
            Assert.IsNotNull(map, "The Editors action map is missing.");
            return map;
        }

        // ── The F-row is free ────────────────────────────────────────────────

        [Test]
        public void EveryEditorToggle_ShipsUnbound()
        {
            var map = EditorsMap();

            var stillBound = new List<string>();
            foreach (var name in RetiredToggles)
            {
                var action = map.FindAction(name, throwIfNotFound: false);
                Assert.IsNotNull(action,
                    $"'{name}' must still EXIST — it ships unbound so the Controls editor can " +
                    "offer it, which deleting it would prevent.");

                if (action.bindings.Count > 0)
                    stillBound.Add($"{name} -> {string.Join(", ", action.bindings.Select(b => b.effectivePath))}");
            }

            Assert.IsEmpty(stillBound,
                "Editors are reached from the General Editor (Escape), not from the F-row:\n" +
                string.Join("\n", stillBound));
        }

        [Test]
        public void NoEditorToggle_HasALegacyKeyInSource()
        {
            // EditorHotkeyBindings used to carry a Hotkey -> KeyCode table feeding
            // UnityEngine.Input directly, so the OR-gate's legacy leg answered for F1-F12
            // whatever the asset said. Clearing a binding would have removed none of the key.
            var fallback = typeof(EditorHotkeyBindings)
                .GetMethod("FallbackPath", System.Reflection.BindingFlags.Public
                                         | System.Reflection.BindingFlags.Static);
            Assert.IsNotNull(fallback, "FallbackPath moved — update this test.");

            var leaked = new List<string>();
            foreach (EditorHotkeyBindings.Hotkey hk in
                     System.Enum.GetValues(typeof(EditorHotkeyBindings.Hotkey)))
            {
                if (!RetiredToggles.Contains("Toggle" + hk.ToString().Replace("Toggle", ""))) continue;
                var path = (string)fallback.Invoke(null, new object[] { hk });
                if (path != null) leaked.Add($"{hk} -> {path}");
            }

            Assert.IsEmpty(leaked,
                "The EditMode fallback must mirror the shipped asset. A fallback that quietly " +
                "re-bound a retired toggle would make the suite disagree with the game about " +
                "which keys exist:\n" + string.Join("\n", leaked));
        }

        [Test]
        public void TheEditorsMap_HasNoCollisionsLeft()
        {
            var conflicts = InputConflictScanner.Scan(InputService.Initialize().Asset)
                .Where(c => c.Severity == InputConflictSeverity.SameMap)
                .Where(c => c.A.Map == InputActionCatalog.MapEditors)
                .Select(c => c.Describe())
                .ToList();

            Assert.IsEmpty(conflicts,
                "Retiring the F-row was supposed to dissolve every same-map collision in " +
                "Editors:\n" + string.Join("\n", conflicts));
        }

        // ── The one way in ───────────────────────────────────────────────────

        [Test]
        public void TheGeneralEditor_IsStillBoundToEscape()
        {
            var action = EditorsMap().FindAction("OpenGeneralEditor", throwIfNotFound: false);
            Assert.IsNotNull(action);
            CollectionAssert.Contains(
                action.bindings.Select(b => b.effectivePath).ToList(), "<Keyboard>/escape",
                "Escape is now the ONLY way to reach any editor. Unbinding it strands all " +
                "sixteen behind a menu nothing can open.");
        }

        [Test]
        public void QuickSaveAndQuickLoad_KeepTheirKeys()
        {
            // Not editors, so not retired: Ctrl+F5 / Ctrl+F9 stay. Their former collision
            // partners (Entities on F5, Debug HUD on F9) are gone, so they no longer clash.
            var map = EditorsMap();
            foreach (var (name, path) in new[]
                     { ("QuickSave", "<Keyboard>/f5"), ("QuickLoad", "<Keyboard>/f9") })
            {
                var action = map.FindAction(name, throwIfNotFound: false);
                Assert.IsNotNull(action, $"{name} is missing.");
                CollectionAssert.Contains(
                    action.bindings.Select(b => b.effectivePath).ToList(), path,
                    $"{name} must keep {path}.");
            }
        }

        /// <summary>
        /// The half that makes retiring the keys safe. An editor with no hotkey AND no menu
        /// entry is an editor nobody can open — and it would fail silently, because nothing
        /// throws when a key simply never fires.
        /// </summary>
        [Test]
        public void EveryRetiredToggle_HasAGeneralEditorEntry()
        {
            var entries = GeneralEditorRegistry.BuildEntries()
                .Select(e => Normalize(e.Label))
                .ToList();

            var unreachable = new List<string>();
            foreach (var toggle in RetiredToggles)
            {
                string wanted = Normalize(toggle.Substring("Toggle".Length));
                // StartsWith, not equality: the menu says "Spawners" where the action says
                // "ToggleSpawner", and "Time & Weather" where it says "ToggleTimeWeather".
                if (!entries.Any(e => e.StartsWith(wanted, System.StringComparison.Ordinal)))
                    unreachable.Add(toggle);
            }

            Assert.IsEmpty(unreachable,
                "These editors lost their hotkey and have no menu entry, so nothing can open " +
                "them:\n" + string.Join("\n", unreachable));
        }

        private static string Normalize(string s) =>
            Regex.Replace(s ?? "", "[^a-zA-Z0-9]", "").ToLowerInvariant();
    }
}
