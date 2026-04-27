using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace Valkur.Tests.EditMode.Game.Bootstrap
{
    /// <summary>
    /// Regression test: every <c>Ensure*RuntimeEditor()</c> method declared in
    /// <c>GameplaySceneSetup.Systems2.cs</c> MUST be invoked from the bootstrap
    /// coroutine in <c>GameplaySceneSetup.cs</c>.
    ///
    /// This catches the exact failure mode that hid <c>ParticlesRuntimeEditor</c>
    /// (singleton existed, F1 binding was correct, but the editor was never
    /// instantiated in the scene → F1 did nothing).
    /// </summary>
    public class EditorBootstrapWiringTests
    {
        [Test]
        public void EveryEnsureRuntimeEditor_Method_IsInvokedByBootstrap()
        {
            string scriptsRoot = Path.GetFullPath(
                Path.Combine(Application.dataPath, "_Project/Scripts/Gameplay/Bootstrap"));
            Assert.IsTrue(Directory.Exists(scriptsRoot),
                $"Bootstrap folder not found: {scriptsRoot}");

            // Collect all "private void Ensure<Name>RuntimeEditor()" method declarations
            // across every GameplaySceneSetup partial file.
            var declared = new HashSet<string>();
            var declaredIn = new Dictionary<string, string>();
            var declRegex = new Regex(@"\bvoid\s+(Ensure\w+RuntimeEditor)\s*\(",
                RegexOptions.Compiled);

            foreach (var path in Directory.EnumerateFiles(scriptsRoot, "GameplaySceneSetup*.cs",
                         SearchOption.TopDirectoryOnly))
            {
                string src = File.ReadAllText(path);
                foreach (Match m in declRegex.Matches(src))
                {
                    string name = m.Groups[1].Value;
                    declared.Add(name);
                    if (!declaredIn.ContainsKey(name))
                        declaredIn[name] = Path.GetFileName(path);
                }
            }

            Assert.IsNotEmpty(declared,
                "Expected at least one Ensure*RuntimeEditor() in Bootstrap partials.");

            // Collect all invocations from any partial.
            var invoked = new HashSet<string>();
            var callRegex = new Regex(@"\b(Ensure\w+RuntimeEditor)\s*\(\s*\)",
                RegexOptions.Compiled);

            foreach (var path in Directory.EnumerateFiles(scriptsRoot, "GameplaySceneSetup*.cs",
                         SearchOption.TopDirectoryOnly))
            {
                string src = File.ReadAllText(path);
                foreach (Match m in callRegex.Matches(src))
                {
                    // Skip the declaration line itself (matches the same regex shape).
                    int lineStart = src.LastIndexOf('\n', m.Index) + 1;
                    string line = src.Substring(lineStart, m.Index - lineStart + m.Length);
                    if (line.Contains("void ")) continue;
                    invoked.Add(m.Groups[1].Value);
                }
            }

            var missing = new List<string>();
            foreach (var name in declared)
                if (!invoked.Contains(name))
                    missing.Add($"  • {name}() declared in {declaredIn[name]} but never invoked");

            Assert.IsEmpty(missing,
                "Every Ensure*RuntimeEditor() must be invoked by the bootstrap coroutine.\n" +
                "Otherwise its singleton never spawns in the scene and its hotkey will silently no-op.\n" +
                string.Join("\n", missing));
        }
    }
}
