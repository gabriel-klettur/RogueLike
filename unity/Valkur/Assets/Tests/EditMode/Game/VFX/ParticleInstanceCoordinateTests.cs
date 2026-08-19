using System.IO;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.VFX;

namespace Valkur.Tests.EditMode.Game.VFX
{
    /// <summary>
    /// The tile ↔ world round trip for placed particle instances.
    ///
    /// Written after the same defect was found and fixed in the spawner persistence. A sweep
    /// of every world-state domain for that shape turned up two more instances of it here, and
    /// this fixture is what keeps them closed.
    ///
    /// <c>particles_instances.json</c> has TWO writers — the runtime serializer used by the
    /// in-game F1 editor, and the editor-window Particles editor. Each was internally
    /// consistent and they disagreed with each other: the runtime measures <c>rel_y</c> from
    /// the zone's TOP row, the window measured it from the zone's BOTTOM edge. Between them
    /// every instance jumped <c>zoneHeightTiles - 1</c> tiles — 49 — depending on which tool
    /// had touched it last.
    ///
    /// Nothing was corrupt when it was found, because that file happened to be empty. It was a
    /// loaded gun rather than a fire, which is exactly when this is worth fixing.
    /// </summary>
    [TestFixture]
    public class ParticleInstanceCoordinateTests
    {
        private const int ZONE_H = 50;
        private const float TILE = 1f;

        private static readonly Vector2Int[] Offsets =
        {
            new Vector2Int(0, 0),
            new Vector2Int(150, 50),
            new Vector2Int(-100, -50),
        };

        // ── The round trip ───────────────────────────────────────────────────────

        [TestCase(0f, 0f)]
        [TestCase(12.5f, 20.25f)]
        [TestCase(49f, 49f)]
        [TestCase(-3.75f, 8f)]
        public void WorldSurvivesARelRoundTrip(float x, float y)
        {
            foreach (var offset in Offsets)
            {
                var world = new Vector2(x, y);
                Vector2Int rel = ParticleInstanceSerializer.WorldToRel(world, offset, ZONE_H, TILE);
                Vector2 back = ParticleInstanceSerializer.RelToWorld(rel, offset, ZONE_H, TILE);

                Assert.AreEqual(world.x, back.x, 1e-3f, $"offset {offset}: x");
                Assert.AreEqual(world.y, back.y, 1e-3f, $"offset {offset}: y");
            }
        }

        [Test]
        public void ManyRoundTripsDoNotDrift()
        {
            var offset = new Vector2Int(150, 50);
            var world = new Vector2(153.5f, 62.25f);

            for (int i = 0; i < 25; i++)
            {
                Vector2Int rel = ParticleInstanceSerializer.WorldToRel(world, offset, ZONE_H, TILE);
                world = ParticleInstanceSerializer.RelToWorld(rel, offset, ZONE_H, TILE);
            }

            Assert.AreEqual(153.5f, world.x, 1e-3f);
            Assert.AreEqual(62.25f, world.y, 1e-3f,
                "A save/load cycle must be a fixed point. Two writers using different Y origins " +
                "moved an instance 49 tiles the first time the other tool touched it.");
        }

        [Test]
        public void RelYIsMeasuredFromTheTopOfTheZone()
        {
            // The direction of the flip is the whole disagreement, so it is stated outright
            // rather than left implied by a round trip that would pass either way as long as
            // both halves were wrong together.
            var offset = new Vector2Int(0, 0);

            Vector2Int atTop = ParticleInstanceSerializer.WorldToRel(
                new Vector2(0f, ZONE_H - 1), offset, ZONE_H, TILE);
            Vector2Int atBottom = ParticleInstanceSerializer.WorldToRel(
                new Vector2(0f, 0f), offset, ZONE_H, TILE);

            Assert.AreEqual(0, atTop.y, "The top row of a zone is rel_y = 0.");
            Assert.Greater(atBottom.y, atTop.y, "rel_y grows downward.");
        }

        [Test]
        public void TheZoneOriginIsApplied()
        {
            Vector2Int atOrigin = ParticleInstanceSerializer.WorldToRel(
                new Vector2(10f, 10f), Vector2Int.zero, ZONE_H, TILE);
            Vector2Int offsetZone = ParticleInstanceSerializer.WorldToRel(
                new Vector2(10f, 10f), new Vector2Int(150, 50), ZONE_H, TILE);

            Assert.AreNotEqual(atOrigin, offsetZone,
                "The whole failure in the spawner case was this offset being applied on one " +
                "side only.");
        }

        // ── Both writers of the file must use it ─────────────────────────────────

        private static string Script(params string[] parts) =>
            File.ReadAllText(Path.Combine(Application.dataPath, "_Project", "Scripts",
                Path.Combine(parts)));

        [Test]
        public void TheEditorWindowConvertsThroughTheSharedPair()
        {
            string src = Script("Editor", "Windows", "ParticlesEditorWindow.SceneInteraction.cs");

            Assert.IsTrue(src.Contains("ParticleInstanceSerializer.WorldToRel"),
                "The window writes the same file as the runtime and must use the same mapping.");
            Assert.IsTrue(src.Contains("ParticleInstanceSerializer.RelToWorld"),
                "It also READS that file to draw handles; a writer fixed alone would write " +
                "correctly and draw wrongly.");
            Assert.IsFalse(src.Contains("(offsetY - worldPos.y) * PPU"),
                "The open-coded conversion measured rel_y from the zone's bottom edge, 49 tiles " +
                "from where the runtime measures it.");
        }

        [Test]
        public void AnUnanchoredPlacementIsRefusedRatherThanPersisted()
        {
            string src = Script("Editor", "Windows", "ParticlesEditorWindow.SceneInteraction.cs");

            Assert.IsFalse(src.Contains("int ry = Mathf.RoundToInt(-worldPos.y * PPU);"),
                "This fallback wrote absolute world coordinates into a zone-relative field, " +
                "tagged with whichever zone was selected in the UI — the spawner defect, " +
                "verbatim, in another subsystem.");
            Assert.IsTrue(src.Contains("refusing to place an instance"),
                "A refused placement is recoverable; silently-wrong data is not.");
        }

        // ── Clearing and saving must see the same emitters ───────────────────────

        [Test]
        public void ClearingDestroysEveryEmitterTheSaveWouldHaveWritten()
        {
            // ParticlesRuntimeEditor persists by FindObjectsOfType<PersistedParticleInstance>,
            // and the F1 editor never adds what it creates to the loader's _spawnedEmitters —
            // so clearing the tracked list left editor-placed emitters alive while the file
            // recreated them alongside, and the Particles editor saves automatically, so the
            // doubling was written back. Identical to the spawner defect.
            string loader = Script("Gameplay", "VFX", "ParticleInstancesLoader.cs");

            int clear = loader.IndexOf("public void ClearAll()", System.StringComparison.Ordinal);
            Assert.Greater(clear, -1, "ClearAll moved — update this test.");

            string body = loader.Substring(clear, System.Math.Min(1600, loader.Length - clear));
            Assert.IsTrue(body.Contains("FindObjectsOfType<PersistedParticleInstance>"),
                "Clearing must enumerate the same set the save enumerates. The marker component " +
                "exists precisely so both sides can agree, and only the save side was using it.");

            string save = Script("Gameplay", "Editors", "Particles", "ParticlesRuntimeEditor.Persistence.cs");
            Assert.IsTrue(save.Contains("FindObjectsOfType<PersistedParticleInstance>"),
                "If the save stops using that query, clear has to change with it — the two are " +
                "only correct together.");
        }
    }
}
