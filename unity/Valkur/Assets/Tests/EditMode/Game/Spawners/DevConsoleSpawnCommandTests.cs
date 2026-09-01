using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.Spawners
{
    /// <summary>
    /// <c>spawn &lt;key&gt; [qty]</c> used to advertise a <c>qty</c> argument it silently
    /// ignored and always spawned exactly one entity at a random offset from the player. This
    /// fixture pins the fix: <c>qty</c> is honoured (queued as that many
    /// <c>MonsterSpawner.RequestSpawn</c> calls), the default stays 1 when omitted (no
    /// regression for the common case), a non-positive qty floors to 1 rather than spawning
    /// nothing, and the new <c>@cursor</c> token is parsed and gates the spawn on a resolvable
    /// mouse position rather than silently falling back to the player-relative offset (verified
    /// by its failure path, since an EditMode fixture has no live <c>Camera.main</c> for
    /// <c>MouseInputManager</c> to resolve against).
    /// </summary>
    [TestFixture]
    public class DevConsoleSpawnCommandTests
    {
        private const string TestKey = "test_spawn_cmd_dummy_monster";

        private GameObject _consoleGo;
        private DevConsole _console;
        private GameObject _spawnerGo;
        private MonsterSpawner _spawner;
        private GameObject _player;
        private MonsterDefinition _def;
        private Camera _mainCamera;
        private bool _mainCameraWasEnabled;

        [SetUp]
        public void SetUp()
        {
            // DevConsole.Awake resolves EditorHotkeyBindings against a scene with no
            // InputService — CommandRegistryTests silences the same warnings for the same
            // reason.
            LogAssert.ignoreFailingMessages = true;

            // The @cursor test relies on Camera.main being null ("an EditMode fixture has
            // no live Camera.main"). That is only true while the editor's open scene has no
            // MainCamera — leaving MainGameplay open supplies one and makes
            // MouseInputManager.TryGetWorldMousePosition resolve, so the spawn falls through
            // instead of aborting. Neutralize the camera for the fixture and restore it after.
            _mainCamera = Camera.main;
            if (_mainCamera != null)
            {
                _mainCameraWasEnabled = _mainCamera.enabled;
                _mainCamera.enabled = false;
            }

            _player = new GameObject("Player");
            EntityRegistry.RegisterPlayer(_player);

            _spawnerGo = new GameObject("[MonsterSpawner]");
            _spawner = _spawnerGo.AddComponent<MonsterSpawner>();

            _def = ScriptableObject.CreateInstance<MonsterDefinition>();
            _def.monsterKey = TestKey;
            _def.displayName = "Test Dummy";

            _consoleGo = new GameObject("[TestDevConsole]");
            _console = _consoleGo.AddComponent<DevConsole>();

            // EditMode does not run Awake on a plain MonoBehaviour, and Awake is where
            // DevConsole builds its command table (RegisterDefaults / RegisterReloadCommands
            // / RegisterDoorCommands). Without this, Execute("spawn …") resolves no command
            // at all and the queue stays empty — the test would then measure "the console
            // has no commands", not "spawn ignores its qty", and would keep failing however
            // correct CmdSpawn is.
            typeof(DevConsole)
                .GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.Invoke(_console, null);
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            if (_consoleGo != null) Object.DestroyImmediate(_consoleGo);
            if (_spawnerGo != null) Object.DestroyImmediate(_spawnerGo);
            if (_player != null) Object.DestroyImmediate(_player);
            if (_def != null) Object.DestroyImmediate(_def);
            EntityRegistry.RegisterPlayer(null);

            if (_mainCamera != null)
                _mainCamera.enabled = _mainCameraWasEnabled;
        }

        private int SpawnQueueCount()
        {
            var field = typeof(MonsterSpawner).GetField("_spawnQueue",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var queue = field?.GetValue(_spawner) as ICollection;
            Assert.IsNotNull(queue, "MonsterSpawner._spawnQueue not found via reflection — has it been renamed?");
            return queue.Count;
        }

        [Test]
        public void DefaultQtyIsOne()
        {
            _console.Execute($"spawn {TestKey}");
            Assert.AreEqual(1, SpawnQueueCount(),
                "Omitting qty must spawn exactly one, unchanged from before this fix.");
        }

        [Test]
        public void QtyArgumentIsHonoured()
        {
            _console.Execute($"spawn {TestKey} 5");
            Assert.AreEqual(5, SpawnQueueCount(),
                "'spawn <key> 5' must enqueue 5 requests, not silently spawn just 1.");
        }

        [Test]
        public void NonPositiveQtyFloorsToOne()
        {
            _console.Execute($"spawn {TestKey} 0");
            Assert.AreEqual(1, SpawnQueueCount(), "A qty of 0 must floor to 1, not spawn nothing.");
        }

        [Test]
        public void CursorTokenGatesOnAResolvableMousePosition()
        {
            // No Camera.main in this fixture, so MouseInputManager.TryGetWorldMousePosition
            // returns false — CmdSpawn must abort cleanly rather than falling back to the
            // player-relative offset silently.
            _console.Execute($"spawn {TestKey} 3 @cursor");
            Assert.AreEqual(0, SpawnQueueCount(),
                "@cursor must gate the spawn on a resolvable mouse position instead of " +
                "silently spawning near the player when the cursor can't be read.");
        }
    }
}
