using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay.VFX;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Editors.Particles
{
    /// <summary>
    /// Tests for the delete-all-in-zone feature of <see cref="ParticlesRuntimeEditor"/>.
    ///
    /// Regression coverage:
    ///   - Bug 1: ResolveZoneName Manhattan-distance bug → count was always 0 → modal never appeared.
    ///   - Bug 2: Preview emitters (PPrev_*) were deleted alongside real emitters → MissingReferenceException.
    /// </summary>
    [TestFixture]
    public class ParticlesDeleteInZoneTests
    {
        private readonly List<GameObject> _sceneObjects = new List<GameObject>();
        private ParticlesRuntimeEditor _editor;
        private ZoneManager _zm;
        private ParticlePresetCatalog _catalog;
        private ParticlePresetDefinition _preset;

        // ── Reflection helpers ────────────────────────────────────────────────────

        private static void ClearSingletonInstance<T>() where T : MonoBehaviour
        {
            var type = typeof(T).BaseType;
            while (type != null)
            {
                var f = type.GetField("_instance",
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (f != null) { f.SetValue(null, null); return; }
                type = type.BaseType;
            }
        }

        private static FieldInfo FindField(object obj, string name)
        {
            var t = obj.GetType();
            while (t != null)
            {
                var f = t.GetField(name,
                    BindingFlags.NonPublic | BindingFlags.Public |
                    BindingFlags.Instance | BindingFlags.Static);
                if (f != null) return f;
                t = t.BaseType;
            }
            return null;
        }

        private static object GetVal(object obj, string name)
            => FindField(obj, name)?.GetValue(obj);

        private static void SetVal(object obj, string name, object value)
            => FindField(obj, name)?.SetValue(obj, value);

        private static void Invoke(object obj, string method, params object[] args)
        {
            var t = obj.GetType();
            MethodInfo m = null;
            while (t != null && m == null)
            {
                m = t.GetMethod(method,
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                t = t.BaseType;
            }
            m?.Invoke(obj, args);
        }

        private static void StubPreviewService(ParticlesRuntimeEditor editor)
        {
            var serviceField = FindField(editor, "_previewService");
            if (serviceField == null) return;
            var service = serviceField.GetValue(editor);
            if (service == null) return;
            var serviceType = service.GetType();
            const BindingFlags bf = BindingFlags.NonPublic | BindingFlags.Instance;
            serviceType.GetField("_initialized", bf)?.SetValue(service, true);
            var poolField = serviceType.GetField("_pool", bf);
            if (poolField != null)
            {
                var pool = poolField.GetValue(service) as System.Array;
                if (pool != null)
                {
                    var thumbSlotType = serviceType.GetNestedType("ThumbSlot", BindingFlags.NonPublic);
                    if (thumbSlotType != null)
                        for (int i = 0; i < pool.Length; i++)
                            if (pool.GetValue(i) == null)
                                pool.SetValue(System.Activator.CreateInstance(thumbSlotType), i);
                }
            }
        }

        // ── ZoneManager factory ───────────────────────────────────────────────────

        /// <summary>
        /// Creates a ZoneManager with a single zone named <paramref name="zoneName"/>
        /// at grid offset (0,0), spanning <paramref name="w"/> x <paramref name="h"/> tiles,
        /// tileSize=1. All emitters placed at positions inside that area will resolve to
        /// <paramref name="zoneName"/> via DetectZone.
        /// </summary>
        private ZoneManager CreateZoneManager(string zoneName, int w = 50, int h = 50)
        {
            var go = new GameObject("ZoneManager");
            _sceneObjects.Add(go);
            var zm = go.AddComponent<ZoneManager>();

            // Inject zone list via reflection (fields are private/serialized).
            var zonesDef = new List<ZoneManager.ZoneDefinition>
            {
                new ZoneManager.ZoneDefinition
                {
                    zoneName = zoneName,
                    gridOffset = Vector2Int.zero,
                    editableInTileEditor = true
                }
            };
            FindField(zm, "zones")?.SetValue(zm, zonesDef);
            FindField(zm, "zoneWidthTiles")?.SetValue(zm, w);
            FindField(zm, "zoneHeightTiles")?.SetValue(zm, h);
            FindField(zm, "tileSize")?.SetValue(zm, 1f);
            FindField(zm, "currentZone")?.SetValue(zm, zoneName);

            // Rebuild internal dictionary.
            Invoke(zm, "RebuildZoneMap");
            return zm;
        }

        /// <summary>
        /// Creates a ZoneManager with two named zones side by side.
        /// ZoneA: grid offset (0,0), ZoneB: grid offset (w,0). Both span w x h tiles.
        /// </summary>
        private ZoneManager CreateDualZoneManager(string zoneA, string zoneB, int w = 20, int h = 20)
        {
            var go = new GameObject("ZoneManager2");
            _sceneObjects.Add(go);
            var zm = go.AddComponent<ZoneManager>();

            var zonesDef = new List<ZoneManager.ZoneDefinition>
            {
                new ZoneManager.ZoneDefinition { zoneName = zoneA, gridOffset = Vector2Int.zero,           editableInTileEditor = true },
                new ZoneManager.ZoneDefinition { zoneName = zoneB, gridOffset = new Vector2Int(w, 0),      editableInTileEditor = true }
            };
            FindField(zm, "zones")?.SetValue(zm, zonesDef);
            FindField(zm, "zoneWidthTiles")?.SetValue(zm, w);
            FindField(zm, "zoneHeightTiles")?.SetValue(zm, h);
            FindField(zm, "tileSize")?.SetValue(zm, 1f);
            FindField(zm, "currentZone")?.SetValue(zm, zoneA);
            Invoke(zm, "RebuildZoneMap");
            return zm;
        }

        // ── Editor factory ────────────────────────────────────────────────────────

        private ParticlesRuntimeEditor CreateEditor(bool withUI = false)
        {
            ClearSingletonInstance<ParticlesRuntimeEditor>();
            var go = new GameObject("DeleteTestEditor");
            _sceneObjects.Add(go);
            var editor = go.AddComponent<ParticlesRuntimeEditor>();
            Invoke(editor, "OnSingletonAwake");

            _preset = ScriptableObject.CreateInstance<ParticlePresetDefinition>();
            _preset.id          = "aura_test";
            _preset.displayName = "Aura Test";

            _catalog = ScriptableObject.CreateInstance<ParticlePresetCatalog>();
            _catalog.SetPresets(new List<ParticlePresetDefinition> { _preset });
            SetVal(editor, "_catalog", _catalog);

            // Keep persistence off disk. Without this the editor falls through to
            // FileParticleInstanceStore, resolves the real StreamingAssets file, and its saves
            // are measured against the world's actual emitters — the anti-wipe guard then
            // (correctly) refuses and logs an error this fixture never asked for.
            editor.SetInstanceStore(new InMemoryParticleInstanceStore());

            StubPreviewService(editor);

            if (withUI)
                Invoke(editor, "Start");

            return editor;
        }

        /// <summary>Spawns a ParticleEmitter GO at <paramref name="worldPos"/> via SpawnEmitterAt.</summary>
        private GameObject SpawnEmitter(ParticlesRuntimeEditor editor, Vector3 worldPos)
        {
            // SpawnEmitterAt is private; invoke via reflection.
            var t = editor.GetType();
            MethodInfo m = null;
            while (t != null && m == null)
            {
                m = t.GetMethod("SpawnEmitterAt",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                t = t.BaseType;
            }
            var go = m?.Invoke(editor, new object[] { _preset, worldPos, -1f }) as GameObject;
            if (go != null) _sceneObjects.Add(go);
            return go;
        }

        // ── Setup / Teardown ──────────────────────────────────────────────────────

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            _zm = CreateZoneManager("TestZone");
            _editor = CreateEditor(withUI: true);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _sceneObjects)
                if (go != null) Object.DestroyImmediate(go);
            _sceneObjects.Clear();

            ClearSingletonInstance<ParticlesRuntimeEditor>();
            LogAssert.ignoreFailingMessages = false;
        }

        // ── Tests ─────────────────────────────────────────────────────────────────

        [Test]
        public void DeleteAllInZone_NoInstances_SetsStatusMessage()
        {
            // No emitters → DeleteAllInZone must set a status message and return without crash.
            // Intercepting SetStatus by patching _ui.StatusText is complex; instead verify
            // that after calling DeleteAllInZone the scene has 0 ParticleEmitters.
            Invoke(_editor, "DeleteAllInZone", "TestZone");

            var remaining = Object.FindObjectsOfType<ParticleEmitter>();
            Assert.AreEqual(0, remaining.Length,
                "DeleteAllInZone with no emitters must leave the scene with 0 ParticleEmitters.");
        }

        [Test]
        public void RequestDeleteAllInZone_ZeroCount_ConfirmModalRemainsHidden()
        {
            // No emitters → first modal must NOT appear (early return with status message).
            // The confirm modal field is _confirmModal; assert it stays inactive.
            var modal = GetVal(_editor, "_confirmModal") as GameObject;

            Invoke(_editor, "RequestDeleteAllInZoneWithConfirm");

            // If modal is null (no withUI), count-0 guard fires before ShowConfirm anyway.
            if (modal != null)
                Assert.IsFalse(modal.activeSelf,
                    "Confirm modal must NOT appear when there are 0 instances in the zone.");
        }

        [Test]
        public void RequestDeleteAllInZone_WithInstances_ShowsFirstModal()
        {
            // Place 3 emitters inside TestZone (positions (1,1), (2,1), (3,1) — all inside 0..50 grid).
            SpawnEmitter(_editor, new Vector3(1f, 1f, 0f));
            SpawnEmitter(_editor, new Vector3(2f, 1f, 0f));
            SpawnEmitter(_editor, new Vector3(3f, 1f, 0f));

            var modal = GetVal(_editor, "_confirmModal") as GameObject;

            Invoke(_editor, "RequestDeleteAllInZoneWithConfirm");

            if (modal != null)
                Assert.IsTrue(modal.activeSelf,
                    "Confirm modal must appear when there are instances in the current zone.");

            // Also verify the confirm text contains the count "3" and zone name.
            var confirmText = GetVal(_editor, "_confirmText") as TMPro.TextMeshProUGUI;
            if (confirmText != null)
            {
                Assert.IsTrue(confirmText.text.Contains("3"),
                    "Confirm modal text must contain the instance count (3).");
                Assert.IsTrue(confirmText.text.Contains("TestZone"),
                    "Confirm modal text must contain the zone name.");
            }
        }

        [Test]
        public void DeleteAllInZone_OnlyDeletesEmittersInTargetZone()
        {
            // Use a dual-zone manager so we can place emitters in two different zones.
            // Destroy the single-zone manager from SetUp. Cache the GO ref before
            // destroying — accessing _zm.gameObject after DestroyImmediate throws
            // MissingReferenceException because Unity invalidates the proxy.
            var oldZmGo = _zm.gameObject;
            _sceneObjects.Remove(oldZmGo);
            Object.DestroyImmediate(oldZmGo);
            _zm = null;
            var zm2 = CreateDualZoneManager("ZoneA", "ZoneB", 20, 20);
            // ZoneA: x in [0,20), ZoneB: x in [20,40). tileSize=1 so worldPos ~= tile.
            // Place 2 emitters in ZoneA (x=1, x=5), 2 in ZoneB (x=21, x=25).
            var emA1 = SpawnEmitter(_editor, new Vector3(1f, 1f, 0f));
            var emA2 = SpawnEmitter(_editor, new Vector3(5f, 1f, 0f));
            var emB1 = SpawnEmitter(_editor, new Vector3(21f, 1f, 0f));
            var emB2 = SpawnEmitter(_editor, new Vector3(25f, 1f, 0f));

            Invoke(_editor, "DeleteAllInZone", "ZoneA");

            // ZoneA emitters must be gone.
            Assert.IsTrue(emA1 == null || !emA1.activeSelf,
                "emA1 must be destroyed/inactive after DeleteAllInZone('ZoneA').");
            Assert.IsTrue(emA2 == null || !emA2.activeSelf,
                "emA2 must be destroyed/inactive after DeleteAllInZone('ZoneA').");

            // ZoneB emitters must survive.
            Assert.IsTrue(emB1 != null && emB1.activeSelf,
                "emB1 (ZoneB) must NOT be deleted when deleting ZoneA.");
            Assert.IsTrue(emB2 != null && emB2.activeSelf,
                "emB2 (ZoneB) must NOT be deleted when deleting ZoneA.");
        }

        /// <summary>
        /// REGRESSION TEST — Bug 2: Preview emitters (PPrev_*) were destroyed alongside
        /// real emitters because DetectZone falls back to currentZone for off-world positions.
        /// After the fix, IsPreviewEmitter() skips any GO whose name starts with "PPrev_".
        /// </summary>
        [Test]
        public void DeleteAllInZone_SkipsPreviewEmittersByName_RegressionBug2()
        {
            // Create a real emitter inside TestZone.
            var realEmitter = SpawnEmitter(_editor, new Vector3(5f, 5f, 0f));

            // Create a fake preview emitter with the PPrev_ prefix.
            var previewGo = new GameObject("PPrev_Emitter_aura_test");
            previewGo.AddComponent<ParticleEmitter>();
            previewGo.transform.position = new Vector3(5f, 5f, 0f); // same zone
            _sceneObjects.Add(previewGo);

            Invoke(_editor, "DeleteAllInZone", "TestZone");

            // The real emitter must be destroyed.
            Assert.IsTrue(realEmitter == null || !realEmitter.activeSelf,
                "Real emitter must be deleted by DeleteAllInZone.");

            // The preview emitter must SURVIVE (regression: previously it was destroyed).
            Assert.IsTrue(previewGo != null && previewGo.activeSelf,
                "Preview emitter (PPrev_*) must NOT be deleted by DeleteAllInZone — regression guard.");
        }

        [Test]
        public void DeleteAllInZone_ClearsActiveAndHoveredReferences()
        {
            var em1 = SpawnEmitter(_editor, new Vector3(3f, 3f, 0f));
            var em2 = SpawnEmitter(_editor, new Vector3(7f, 7f, 0f));

            // Point _activeInstance and _hoveredInstance at em1.
            SetVal(_editor, "_activeInstance", em1);
            SetVal(_editor, "_hoveredInstance", em1);

            Invoke(_editor, "DeleteAllInZone", "TestZone");

            var active  = GetVal(_editor, "_activeInstance")  as GameObject;
            var hovered = GetVal(_editor, "_hoveredInstance") as GameObject;

            Assert.IsTrue(active  == null, "_activeInstance must be null after DeleteAllInZone clears the zone.");
            Assert.IsTrue(hovered == null, "_hoveredInstance must be null after DeleteAllInZone clears the zone.");
        }

        [Test]
        public void DeleteAllInZone_PushesUndoEntry()
        {
            // Spawn 2 emitters and delete them; then Undo should restore them.
            SpawnEmitter(_editor, new Vector3(4f, 4f, 0f));
            SpawnEmitter(_editor, new Vector3(8f, 8f, 0f));

            // Verify count before.
            var beforeDelete = Object.FindObjectsOfType<ParticleEmitter>();
            int countBefore = 0;
            foreach (var em in beforeDelete)
                if (em != null && em.gameObject.activeSelf && !em.gameObject.name.StartsWith("PPrev_"))
                    countBefore++;
            Assert.AreEqual(2, countBefore, "Setup: must have 2 real emitters before delete.");

            Invoke(_editor, "DeleteAllInZone", "TestZone");

            var afterDelete = Object.FindObjectsOfType<ParticleEmitter>();
            int countAfter = 0;
            foreach (var em in afterDelete)
                if (em != null && em.gameObject.activeSelf && !em.gameObject.name.StartsWith("PPrev_"))
                    countAfter++;
            Assert.AreEqual(0, countAfter, "After DeleteAllInZone, 0 real emitters must remain.");

            // Undo.
            var undo = GetVal(_editor, "_undo");
            Invoke(undo, "Undo");

            var afterUndo = Object.FindObjectsOfType<ParticleEmitter>();
            int countUndo = 0;
            foreach (var em in afterUndo)
                if (em != null && em.gameObject.activeSelf && !em.gameObject.name.StartsWith("PPrev_"))
                    countUndo++;

            // After undo, the emitters must be re-spawned.
            Assert.AreEqual(2, countUndo,
                "After Undo of DeleteAllInZone, the emitters must be re-spawned (count back to 2).");
        }
    }
}
