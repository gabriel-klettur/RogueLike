using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.Bootstrap
{
    /// <summary>
    /// Tests for DevConsole.CommandRegistry (DevConsole.Registry.cs partial).
    /// Validates registration, alias resolution, and case-insensitive lookup
    /// without relying on any of the 25+ default commands (which require
    /// Health, Inventory, etc. in scene).
    /// </summary>
    public class CommandRegistryTests
    {
        private GameObject _go;
        private DevConsole _console;

        [SetUp]
        public void SetUp()
        {
            // Suppress the InputAction warnings that fire when DevConsole.Awake
            // tries to resolve EditorHotkeyBindings without an InputService in scene.
            LogAssert.ignoreFailingMessages = true;

            _go = new GameObject("[TestDevConsole]");
            _console = _go.AddComponent<DevConsole>();
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            if (_go != null)
                Object.DestroyImmediate(_go);
        }

        // ── helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Builds a minimal custom command that does not depend on any gameplay system.
        /// </summary>
        private static DevConsole.ConsoleCommand MakeCommand(
            string name,
            string[] aliases = null,
            string category = "test")
        {
            return new DevConsole.ConsoleCommand
            {
                Name = name,
                Aliases = aliases,
                Usage = name,
                Help = "test command",
                Category = category,
                Handler = _ => { }
            };
        }

        // ── tests ─────────────────────────────────────────────────────────────

        [Test]
        public void RegisterCommand_AddsToDict_ResolvableByName()
        {
            // Arrange
            var cmd = MakeCommand("testcmd");

            // Act
            _console.RegisterCommand(cmd);

            // Assert
            bool found = _console.TryResolve("testcmd", out var resolved);
            Assert.IsTrue(found, "Command should be resolvable by its primary name after registration.");
            Assert.AreSame(cmd, resolved, "Resolved command should be the same instance that was registered.");
        }

        [Test]
        public void RegisterCommand_WithAliases_AllResolvable()
        {
            // Arrange
            var cmd = MakeCommand("primarycmd", aliases: new[] { "alias1", "alias2" });

            // Act
            _console.RegisterCommand(cmd);

            // Assert — primary name
            Assert.IsTrue(_console.TryResolve("primarycmd", out _), "Primary name should be resolvable.");

            // Assert — each alias
            Assert.IsTrue(_console.TryResolve("alias1", out var byAlias1),
                "Alias 'alias1' should be resolvable.");
            Assert.AreSame(cmd, byAlias1,
                "Alias should resolve to the same ConsoleCommand instance.");

            Assert.IsTrue(_console.TryResolve("alias2", out var byAlias2),
                "Alias 'alias2' should be resolvable.");
            Assert.AreSame(cmd, byAlias2,
                "Alias should resolve to the same ConsoleCommand instance.");
        }

        [Test]
        public void TryResolve_IsCaseInsensitive()
        {
            // Arrange
            var cmd = MakeCommand("mycmd", aliases: new[] { "myalias" });
            _console.RegisterCommand(cmd);

            // Act + Assert — uppercase name
            Assert.IsTrue(_console.TryResolve("MYCMD", out var upper),
                "Lookup should be case-insensitive for the primary name.");
            Assert.AreSame(cmd, upper);

            // Mixed case alias
            Assert.IsTrue(_console.TryResolve("MyAlias", out var mixedAlias),
                "Lookup should be case-insensitive for aliases.");
            Assert.AreSame(cmd, mixedAlias);
        }
    }
}
