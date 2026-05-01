using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Editors.Particles
{
    /// <summary>
    /// Regression tests for <c>ParticlesRuntimeEditor.Persistence.cs::ResolveZoneName</c>.
    ///
    /// Bug 1 (historical): The original implementation used Manhattan distance to
    /// find the nearest zone, instead of delegating to <see cref="ZoneManager.DetectZone"/>.
    /// This caused the emitter count to always be 0 (wrong zone match), so the
    /// delete-all-in-zone confirmation modal never appeared.
    ///
    /// Fix: ResolveZoneName now calls zm.DetectZone(worldPos). These tests assert
    /// that ResolveZoneName returns the same zone as zm.DetectZone for a variety of positions.
    /// </summary>
    [TestFixture]
    public class ParticlesResolveZoneNameTests
    {
        private readonly List<GameObject> _sceneObjects = new List<GameObject>();

        // ── Reflection helpers ────────────────────────────────────────────────────

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

        /// <summary>
        /// Invokes the private static ResolveZoneName(ZoneManager, Vector3) via reflection.
        /// ResolveZoneName lives in ParticlesRuntimeEditor (Persistence partial).
        /// </summary>
        private static string CallResolveZoneName(ZoneManager zm, Vector3 worldPos)
        {
            var method = typeof(Valkur.Gameplay.VFX.ParticlesRuntimeEditor)
                .GetMethod("ResolveZoneName",
                    BindingFlags.NonPublic | BindingFlags.Static,
                    null,
                    new[] { typeof(ZoneManager), typeof(Vector3) },
                    null);

            Assert.IsNotNull(method,
                "ResolveZoneName(ZoneManager, Vector3) private static must exist on ParticlesRuntimeEditor.");

            return method.Invoke(null, new object[] { zm, worldPos }) as string;
        }

        // ── ZoneManager factory ───────────────────────────────────────────────────

        /// <summary>
        /// Creates a ZoneManager with two zones of size <c>w x h</c> tiles:
        ///   ZoneA: gridOffset (0, 0)
        ///   ZoneB: gridOffset (w, 0)
        /// tileSize = 1.  currentZone = ZoneA.
        /// </summary>
        private ZoneManager CreateDualZoneManager(string zoneA, string zoneB, int w = 30, int h = 30)
        {
            var go = new GameObject("ZoneManagerResolve");
            _sceneObjects.Add(go);
            var zm = go.AddComponent<ZoneManager>();

            var zones = new List<ZoneManager.ZoneDefinition>
            {
                new ZoneManager.ZoneDefinition { zoneName = zoneA, gridOffset = Vector2Int.zero,        editableInTileEditor = true },
                new ZoneManager.ZoneDefinition { zoneName = zoneB, gridOffset = new Vector2Int(w, 0),   editableInTileEditor = true }
            };
            FindField(zm, "zones")?.SetValue(zm, zones);
            FindField(zm, "zoneWidthTiles")?.SetValue(zm, w);
            FindField(zm, "zoneHeightTiles")?.SetValue(zm, h);
            FindField(zm, "tileSize")?.SetValue(zm, 1f);
            FindField(zm, "currentZone")?.SetValue(zm, zoneA);
            Invoke(zm, "RebuildZoneMap");
            return zm;
        }

        // ── Setup / Teardown ──────────────────────────────────────────────────────

        [SetUp]
        public void SetUp() => LogAssert.ignoreFailingMessages = true;

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _sceneObjects)
                if (go != null) Object.DestroyImmediate(go);
            _sceneObjects.Clear();
            LogAssert.ignoreFailingMessages = false;
        }

        // ── Tests ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// REGRESSION TEST — Bug 1: ResolveZoneName must match DetectZone for positions
        /// clearly inside a zone.
        /// </summary>
        [Test]
        public void ResolveZoneName_MatchesDetectZone_ForPositionsInsideZones_RegressionBug1()
        {
            var zm = CreateDualZoneManager("Lobby", "Dungeon", 30, 30);

            // Positions clearly inside Lobby (x in [0,30), y in [0,30)).
            var lobbyPositions = new[]
            {
                new Vector3(1f, 1f, 0f),
                new Vector3(15f, 15f, 0f),
                new Vector3(29f, 29f, 0f)
            };
            foreach (var pos in lobbyPositions)
            {
                string expected = zm.DetectZone(new Vector2(pos.x, pos.y));
                string actual   = CallResolveZoneName(zm, pos);
                Assert.AreEqual(expected, actual,
                    $"ResolveZoneName must match DetectZone for Lobby position ({pos.x},{pos.y}). " +
                    "Regression: old Manhattan-distance implementation returned wrong zone.");
            }

            // Positions clearly inside Dungeon (x in [30,60), y in [0,30)).
            var dungeonPositions = new[]
            {
                new Vector3(31f, 1f, 0f),
                new Vector3(45f, 15f, 0f),
                new Vector3(59f, 29f, 0f)
            };
            foreach (var pos in dungeonPositions)
            {
                string expected = zm.DetectZone(new Vector2(pos.x, pos.y));
                string actual   = CallResolveZoneName(zm, pos);
                Assert.AreEqual(expected, actual,
                    $"ResolveZoneName must match DetectZone for Dungeon position ({pos.x},{pos.y}).");
            }
        }

        [Test]
        public void ResolveZoneName_OutsideAllZones_FallsBackToCurrentZone()
        {
            var zm = CreateDualZoneManager("Lobby", "Dungeon", 30, 30);
            // Position far outside both zones.
            var outsidePos = new Vector3(200f, 200f, 0f);

            string expected = zm.DetectZone(new Vector2(outsidePos.x, outsidePos.y)); // falls back to currentZone
            string actual   = CallResolveZoneName(zm, outsidePos);

            Assert.AreEqual(expected, actual,
                "ResolveZoneName must match DetectZone fallback (currentZone) for positions outside all zones.");
        }

        [Test]
        public void ResolveZoneName_NullZoneManager_ReturnsLobby()
        {
            // ResolveZoneName with null zm must return "Lobby" (default fallback).
            string result = CallResolveZoneName(null, new Vector3(5f, 5f, 0f));
            Assert.AreEqual("Lobby", result,
                "ResolveZoneName(null zm) must return 'Lobby'.");
        }

        [Test]
        public void ResolveZoneName_ZBoundaryPosition_MatchesDetectZone()
        {
            // The z component must be ignored; test with non-zero z.
            var zm = CreateDualZoneManager("Lobby", "Dungeon", 30, 30);
            var pos = new Vector3(5f, 5f, 99f);  // z=99 but should still map to Lobby

            string expected = zm.DetectZone(new Vector2(pos.x, pos.y));
            string actual   = CallResolveZoneName(zm, pos);

            Assert.AreEqual(expected, actual,
                "ResolveZoneName must ignore the z component and match DetectZone.");
        }

        [Test]
        public void ResolveZoneName_AtZoneBoundary_MatchesDetectZone()
        {
            // Position exactly at x=30 should map to Dungeon (tileX = FloorToInt(30/1) = 30,
            // which is in [30, 60) for Dungeon).
            var zm = CreateDualZoneManager("Lobby", "Dungeon", 30, 30);
            var pos = new Vector3(30f, 1f, 0f);

            string expected = zm.DetectZone(new Vector2(pos.x, pos.y));
            string actual   = CallResolveZoneName(zm, pos);

            Assert.AreEqual(expected, actual,
                "ResolveZoneName at zone boundary (x=30) must agree with DetectZone.");
        }
    }
}
