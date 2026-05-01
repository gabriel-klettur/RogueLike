using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace Valkur.Tests.EditMode.Game.Input
{
    /// <summary>
    /// Codebase-wide structural guard that enforces Cardinal Rule #6 from
    /// <c>CLAUDE.md</c>: no production code outside the four centralized
    /// input helpers may read <c>Mouse.current</c> / <c>Keyboard.current</c>
    /// button or key state directly. Every such read MUST go through
    /// <c>MouseInputManager</c> / <c>KeyboardInputManager</c> /
    /// <c>InputCompat</c> / <c>EditorHotkeyBindings</c> so the OR-of-new-
    /// and-legacy fallback applies — without it, the affected callsite
    /// silently dies whenever the new InputSystem package drops OS events
    /// (recurring Unity 2022.3.62f1 Editor bug).
    ///
    /// <para>
    /// <b>Why a structural test?</b> The "Buildings drag-from-picker
    /// silently broken" bug shipped because the Buildings editor was
    /// migrated only partially — three raw <c>Mouse.current.leftButton</c>
    /// reads survived the audit. A regex sweep over the full codebase
    /// catches every such regression at CI time, not weeks later when a
    /// user happens to drag a slot.
    /// </para>
    ///
    /// <para>
    /// <b>What's whitelisted?</b>
    /// </para>
    /// <list type="bullet">
    /// <item>The Input core helpers themselves (they ARE the OR-fallback).</item>
    /// <item>EditMode / PlayMode tests (they synthesise events directly).</item>
    /// <item><c>mouse.delta.ReadValue()</c> — MouseInputManager doesn't
    /// expose it yet; flag a TODO if a third callsite appears.
    /// (<c>mouse.scroll.ReadValue()</c> IS centralized — see
    /// <c>MouseInputManager.GetMouseWheelDelta()</c>.)</item>
    /// <item>Pure null-checks (<c>if (Mouse.current == null) ...</c>) used
    /// for diagnostics or boot races — they don't actually read state.</item>
    /// </list>
    ///
    /// <para>
    /// If a new test failure appears here, fix it by routing through the
    /// centralized helper. Do NOT add the file to the whitelist unless
    /// it's a new core helper alongside MouseInputManager / etc.
    /// </para>
    /// </summary>
    [TestFixture]
    public class InputCentralizationGuardTests
    {
        // Files allowed to read Mouse.current / Keyboard.current directly —
        // either core helpers (they wrap the OR-fallback) or diagnostic
        // tooling whose explicit purpose is to report the raw backend state.
        // Paths are relative to Assets/_Project/Scripts/.
        private static readonly string[] WhitelistRelativePaths =
        {
            // The four centralized helpers + their immediate co-conspirators.
            "Core/Input/MouseInputManager.cs",
            "Core/Input/KeyboardInputManager.cs",
            "Core/Input/InputCompat.cs",
            "Core/Input/EditorHotkeyBindings.cs",
            "Core/Input/InputService.cs",
            "Core/Input/InputDiagnostics.cs",
            "Core/Input/InputFocusKeepalive.cs",
            "Core/Input/InputSystemConfigurator.cs",
            "Core/Input/PersistentEventSystem.cs",
            "Core/Input/RuntimeInputBootstrap.cs",
            // Boot-time device manager — runs before the helpers are alive.
            "Gameplay/Editors/Tile/TileEditorInputDevices.cs",
            // DiagnoseInputSystem method intentionally reports raw backend
            // state to the console for debugging "no input" conditions.
            "Gameplay/Editors/Tile/TileEditorInputHandler.cs",
        };

        // Patterns that are STRICTLY forbidden anywhere outside the whitelist.
        // Each entry: regex + human-readable explanation for the failure message.
        private static readonly (Regex pattern, string violation)[] ForbiddenPatterns =
        {
            (new Regex(@"Mouse\.current\??\.position\.ReadValue\s*\("),
             "Mouse.current.position.ReadValue() — use MouseInputManager.GetScreenMousePosition()"),

            (new Regex(@"Mouse\.current\??\.scroll\.ReadValue\s*\("),
             "Mouse.current.scroll.ReadValue() — use MouseInputManager.GetMouseWheelDelta()"),

            // Local form: `var mouse = Mouse.current; ... mouse.scroll.ReadValue()`.
            (new Regex(@"\bmouse\.scroll\.ReadValue\s*\("),
             "local `mouse.scroll.ReadValue()` — use MouseInputManager.GetMouseWheelDelta()"),

            (new Regex(@"Mouse\.current\??\.leftButton\.(isPressed|wasPressedThisFrame|wasReleasedThisFrame)"),
             "Mouse.current.leftButton.* — use MouseInputManager.{Is,Was}LeftMouseButton*"),

            (new Regex(@"Mouse\.current\??\.rightButton\.(isPressed|wasPressedThisFrame|wasReleasedThisFrame)"),
             "Mouse.current.rightButton.* — use MouseInputManager.{Is,Was}RightMouseButton*"),

            (new Regex(@"Mouse\.current\??\.middleButton\.(isPressed|wasPressedThisFrame|wasReleasedThisFrame)"),
             "Mouse.current.middleButton.* — use MouseInputManager.{Is,Was}MiddleMouseButton*"),

            // The `var mouse = Mouse.current; ... mouse.leftButton.X` pattern that
            // shipped the Buildings-drag-broken bug. Any chained access on a
            // local named `mouse` (assigned from Mouse.current) is treated the
            // same as the direct form above.
            (new Regex(@"\bmouse\.leftButton\.(isPressed|wasPressedThisFrame|wasReleasedThisFrame)"),
             "local `mouse.leftButton.*` — use MouseInputManager.{Is,Was}LeftMouseButton*"),

            (new Regex(@"\bmouse\.rightButton\.(isPressed|wasPressedThisFrame|wasReleasedThisFrame)"),
             "local `mouse.rightButton.*` — use MouseInputManager.{Is,Was}RightMouseButton*"),

            (new Regex(@"\bmouse\.middleButton\.(isPressed|wasPressedThisFrame|wasReleasedThisFrame)"),
             "local `mouse.middleButton.*` — use MouseInputManager.{Is,Was}MiddleMouseButton*"),

            // Keyboard — Keyboard.current.<keyName>Key.<state> form.
            (new Regex(@"Keyboard\.current\??\.\w+Key\.(isPressed|wasPressedThisFrame|wasReleasedThisFrame)"),
             "Keyboard.current.<key>Key.* — use KeyboardInputManager.* or InputCompat.*"),

            // Local kb form: `var kb = Keyboard.current; ... kb.<key>Key.X`.
            (new Regex(@"\bkb\.\w+Key\.(isPressed|wasPressedThisFrame|wasReleasedThisFrame)"),
             "local `kb.<key>Key.*` — use KeyboardInputManager.* or InputCompat.*"),

            // anyKey on either form.
            (new Regex(@"(Keyboard\.current\??\.|kb\.)anyKey\.(isPressed|wasPressedThisFrame|wasReleasedThisFrame)"),
             "Keyboard.current.anyKey.* — use KeyboardInputManager.WasAnyKeyPressedThisFrame() or InputCompat.AnyKeyPressed()"),
        };

        [Test]
        public void NoProductionCodeReadsRawMouseOrKeyboardStateOutsideHelpers()
        {
            string scriptsRoot = ResolveScriptsRoot();
            Assert.IsTrue(Directory.Exists(scriptsRoot),
                $"Scripts root not found at {scriptsRoot} — guard cannot run.");

            var violations = new List<string>();
            foreach (var file in Directory.EnumerateFiles(scriptsRoot, "*.cs", SearchOption.AllDirectories))
            {
                string relPath = file.Substring(scriptsRoot.Length).TrimStart('\\', '/').Replace('\\', '/');
                if (IsWhitelisted(relPath)) continue;

                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    // Skip comment-only lines so explanatory comments that mention
                    // forbidden patterns ("don't bail when Mouse.current is null")
                    // don't trip the guard.
                    string trimmed = line.TrimStart();
                    if (trimmed.StartsWith("//") || trimmed.StartsWith("/*") || trimmed.StartsWith("*"))
                        continue;

                    foreach (var (pattern, violation) in ForbiddenPatterns)
                    {
                        if (pattern.IsMatch(line))
                        {
                            violations.Add($"  {relPath}:{i + 1}  →  {violation}\n        {line.Trim()}");
                            break; // one report per line is enough
                        }
                    }
                }
            }

            if (violations.Count > 0)
            {
                Assert.Fail(
                    $"Found {violations.Count} forbidden direct Mouse.current / Keyboard.current read(s) " +
                    $"outside the centralized input helpers. Route every read through " +
                    $"MouseInputManager / KeyboardInputManager / InputCompat / EditorHotkeyBindings " +
                    $"(see CLAUDE.md \"Input pipeline\" section). Violations:\n" +
                    string.Join("\n", violations));
            }
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        private static string ResolveScriptsRoot()
        {
            // Application.dataPath = …/Valkur/Assets at runtime. Production code
            // lives under Assets/_Project/Scripts.
            return Path.Combine(Application.dataPath, "_Project", "Scripts");
        }

        private static bool IsWhitelisted(string relPath)
        {
            foreach (var w in WhitelistRelativePaths)
                if (string.Equals(relPath, w, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
    }
}
