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
    /// A binding may only be declared in <c>ValkurInputActions</c>.
    ///
    /// <para>WHY THIS EXISTS BESIDE <c>InputCentralizationGuardTests</c>. That guard forbids
    /// reading a DEVICE — <c>Mouse.current</c>, <c>Keyboard.current</c> — and says nothing
    /// about <c>new InputAction("Pause", binding: "&lt;Keyboard&gt;/p")</c>. So it was green
    /// for the whole life of the project while four separate systems declared bindings in C#:
    /// the pause menu built seven, <c>PickupSystem</c> built an Interact on <c>e</c> that the
    /// asset already had, <c>InventoryUI</c> built one on <c>tab</c> that belonged to the
    /// stance toggle, and <c>TileEditorInputHandler</c> built eight — one of which was on
    /// <c>&lt;Keyboard&gt;/z</c> for REDO, the same path as undo. Two guards, two axes: that
    /// one is about how you READ input, this one about where a binding LIVES.</para>
    ///
    /// <para>A binding built in code is invisible three ways at once: no audit over the asset
    /// can see it, the Controls editor cannot list or move it, and it does not participate in
    /// conflict detection. Every duplicate-key bug this project has shipped was one of
    /// these.</para>
    /// </summary>
    [TestFixture]
    public class BindingConstructionGuardTests
    {
        /// <summary>
        /// Files allowed to construct an <c>InputAction</c>. Each is the input layer itself,
        /// where building one is the job rather than a shortcut around the asset.
        /// </summary>
        private static readonly string[] Whitelist =
        {
            // Resolves ad-hoc actions when InputService is absent (EditMode fixtures). Its
            // FallbackPath table mirrors the shipped asset and is pinned by
            // EditorEntryPointTests, so it cannot invent a binding the game does not have.
            "Core/Input/EditorHotkeyBindings.cs",
            // Diagnostic tooling that reports raw backend state.
            "Core/Input/InputDiagnostics.cs",
            "Core/Input/InputSystemConfigurator.cs",
        };

        private static readonly Regex Construction = new Regex(@"new\s+InputAction\s*\(");

        private static string ScriptsRoot()
            => Path.Combine(Application.dataPath, "_Project", "Scripts");

        private static string Rel(string full)
            => full.Substring(ScriptsRoot().Length)
                   .TrimStart(Path.DirectorySeparatorChar, '/')
                   .Replace(Path.DirectorySeparatorChar, '/');

        [Test]
        public void NoProductionCode_ConstructsAnInputActionOutsideTheInputLayer()
        {
            var offenders = new List<string>();

            foreach (var file in Directory.GetFiles(ScriptsRoot(), "*.cs", SearchOption.AllDirectories))
            {
                string rel = Rel(file);
                if (Whitelist.Contains(rel, StringComparer.OrdinalIgnoreCase)) continue;

                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    // Comments describing the retired pattern are not the pattern.
                    string trimmed = line.TrimStart();
                    if (trimmed.StartsWith("//") || trimmed.StartsWith("///") || trimmed.StartsWith("*"))
                        continue;

                    if (Construction.IsMatch(line))
                        offenders.Add($"{rel}:{i + 1}  {trimmed}");
                }
            }

            Assert.IsEmpty(offenders,
                "A binding declared in C# is invisible to the asset audit, to the Controls " +
                "editor and to conflict detection. Declare the action in " +
                "Resources/Input/ValkurInputActions and reach it through InputService:\n" +
                string.Join("\n", offenders));
        }

        /// <summary>
        /// The other half of the same rule: a binding PATH literal outside the input layer is
        /// a key somebody wrote down instead of asking the asset for.
        /// </summary>
        [Test]
        public void NoProductionCode_WritesABindingPathLiteral()
        {
            var pathLiteral = new Regex("\"<(Keyboard|Mouse|Gamepad)>/");

            var offenders = new List<string>();
            foreach (var file in Directory.GetFiles(ScriptsRoot(), "*.cs", SearchOption.AllDirectories))
            {
                string rel = Rel(file);
                if (Whitelist.Contains(rel, StringComparer.OrdinalIgnoreCase)) continue;
                // The translator's whole job is to know what a path looks like, and the
                // resolver's is to take them apart.
                if (rel == "Core/Input/InputControlPaths.cs") continue;

                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    string trimmed = lines[i].TrimStart();
                    if (trimmed.StartsWith("//") || trimmed.StartsWith("///") || trimmed.StartsWith("*"))
                        continue;
                    if (pathLiteral.IsMatch(lines[i]))
                        offenders.Add($"{rel}:{i + 1}  {trimmed}");
                }
            }

            Assert.IsEmpty(offenders,
                "A binding path written in source is a key the Controls editor cannot move " +
                "and the conflict scanner cannot see:\n" + string.Join("\n", offenders));
        }
    }
}
