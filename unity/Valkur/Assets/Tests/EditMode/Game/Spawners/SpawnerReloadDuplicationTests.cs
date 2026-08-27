using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.Spawners
{
    /// <summary>
    /// <c>reloadworld</c> used to silently double the monster population: it recreated every
    /// <c>SpawnerInstance</c> in a fresh state and each one spawned its wave again, but never
    /// removed the monsters already alive from the PREVIOUS load — because a monster is
    /// parented to <c>[Entities]</c>, not to the spawner that made it,
    /// <c>ClearAllSpawnedWorldContent</c> (which tears down buildings, spawner instances,
    /// lights and particles) has nothing that owns a monster to destroy alongside it.
    /// <c>respawnnpcs</c> already guarded against exactly this by calling
    /// <c>CmdKillAll()</c> first; <c>reloadworld</c> did not.
    ///
    /// This fixture pins both halves: the WIRING (CmdReloadWorld now calls CmdKillAll before
    /// clearing/reloading) and the MECHANISM it relies on (CmdKillAll kills every non-player
    /// Health). A full end-to-end run of CmdReloadWorld needs a live MapEditorManager with
    /// ZoneManager/WorldGridBuilder/etc. wired — too heavy for an EditMode fixture — so the
    /// wiring is pinned at the source level, the same technique
    /// SpawnerTileMappingTests.TheEditorSavesThroughTheSharedMapping already uses for a
    /// comparable "the fix must still be present" guarantee.
    /// </summary>
    [TestFixture]
    public class SpawnerReloadDuplicationTests
    {
        private GameObject _consoleGo;
        private DevConsole _console;

        [SetUp]
        public void SetUp()
        {
            // DevConsole.Awake resolves EditorHotkeyBindings against a scene with no
            // InputService — CommandRegistryTests silences the same warnings for the same
            // reason.
            LogAssert.ignoreFailingMessages = true;
            _consoleGo = new GameObject("[TestDevConsole]");
            _console = _consoleGo.AddComponent<DevConsole>();
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            if (_consoleGo != null) Object.DestroyImmediate(_consoleGo);
            EntityRegistry.RegisterPlayer(null);
        }

        // ── The wiring the fix requires ──────────────────────────────────────────

        [Test]
        public void ReloadWorldKillsTheExistingPopulationBeforeReSpawning()
        {
            string path = Path.Combine(Application.dataPath, "_Project", "Scripts",
                "Gameplay", "Bootstrap", "DevConsole.Commands.Reload.cs");
            string src = File.ReadAllText(path);

            string body = ExtractMethodBody(src, "private void CmdReloadWorld()");
            Assert.IsNotNull(body, "CmdReloadWorld method body not found in " + path);

            Assert.IsTrue(body.Contains("CmdKillAll()"),
                "CmdReloadWorld must kill the existing monster population (CmdKillAll()) " +
                "before ClearAllSpawnedWorldContent()/ReloadAllWorldContent() re-fire every " +
                "spawner's wave — monsters are parented to [Entities], not to their spawner, " +
                "so a reload that skips the kill doubles the population instead of " +
                "refreshing it.");

            int killIdx  = body.IndexOf("CmdKillAll()");
            int clearIdx = body.IndexOf("ClearAllSpawnedWorldContent()");
            Assert.Greater(clearIdx, killIdx,
                "The kill must happen BEFORE the old spawner instances are torn down and the " +
                "new ones fire their waves — killing afterwards would kill the freshly " +
                "spawned population instead of the stale one.");
        }

        /// <summary>Finds the `{ ... }` body of a method given its exact signature line.</summary>
        private static string ExtractMethodBody(string src, string signature)
        {
            int sigIdx = src.IndexOf(signature);
            if (sigIdx < 0) return null;
            int braceStart = src.IndexOf('{', sigIdx);
            if (braceStart < 0) return null;

            int depth = 0;
            for (int i = braceStart; i < src.Length; i++)
            {
                if (src[i] == '{') depth++;
                else if (src[i] == '}')
                {
                    depth--;
                    if (depth == 0) return src.Substring(braceStart, i - braceStart + 1);
                }
            }
            return null;
        }

        // ── The mechanism that fix relies on ─────────────────────────────────────

        [Test]
        public void KillAllRemovesEveryNonPlayerHealthButSparesThePlayer()
        {
            var player = new GameObject("Player");
            var playerHealth = player.AddComponent<Health>();
            playerHealth.Initialize(50);
            EntityRegistry.RegisterPlayer(player);

            var monsters = new GameObject[3];
            var monsterHealths = new Health[3];
            for (int i = 0; i < monsters.Length; i++)
            {
                monsters[i] = new GameObject($"Monster{i}");
                monsterHealths[i] = monsters[i].AddComponent<Health>();
                monsterHealths[i].Initialize(20);
            }

            InvokeCmdKillAll();

            Assert.IsFalse(playerHealth.IsDead, "killall must never kill the player.");
            for (int i = 0; i < monsters.Length; i++)
                Assert.IsTrue(monsterHealths[i].IsDead,
                    $"'{monsters[i].name}' should have been killed by killall — this is the " +
                    "mechanism reloadworld now relies on to avoid duplicating the population.");

            foreach (var m in monsters) Object.DestroyImmediate(m);
            Object.DestroyImmediate(player);
        }

        private void InvokeCmdKillAll()
        {
            var method = typeof(DevConsole).GetMethod("CmdKillAll",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method, "CmdKillAll not found via reflection — has it been renamed?");
            method.Invoke(_console, null);
        }
    }
}
