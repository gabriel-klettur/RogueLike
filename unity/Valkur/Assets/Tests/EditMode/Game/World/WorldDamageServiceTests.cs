using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core.Coordinates;
using Valkur.Gameplay.World;
using Valkur.Infrastructure.Persistence.Repositories;

namespace Valkur.Tests.EditMode.Game.World
{
    /// <summary>
    /// The run-scoped record of what the player broke.
    ///
    /// <para>These are EditMode, which means <see cref="WorldDamageService.Flush"/> is
    /// REFUSED throughout — deliberately, and the refusal is itself the most important thing
    /// in this file. The <c>RUN_TWIN_SAVE</c> incident was EditMode test pollution writing
    /// into a real save folder on a machine nobody was playing on, so a suite that could
    /// write here would be reproducing the incident rather than guarding it.</para>
    /// </summary>
    [TestFixture]
    public class WorldDamageServiceTests
    {
        private static WorldDamageService Build(out InMemoryWorldDamageRepository repository)
        {
            repository = new InMemoryWorldDamageRepository();
            return new WorldDamageService(repository, WorldId.Base);
        }

        // ── The write guard ────────────────────────────────────────────────────────

        [Test]
        public void Flush_OutsidePlayMode_WritesNothing()
        {
            var service = Build(out var repository);

            LogAssert.ignoreFailingMessages = true;
            bool wrote = service.Flush(force: true);
            LogAssert.ignoreFailingMessages = false;

            Assert.That(wrote, Is.False, "A flush outside Play Mode must be refused.");
            Assert.That(repository.WriteCount, Is.Zero,
                "The repository was touched outside Play Mode — this is the RUN_TWIN_SAVE shape: " +
                "an EditMode run writing into a real player's save folder.");
        }

        [Test]
        public void Flush_WithWritesDisabled_WritesNothing()
        {
            var service = Build(out var repository);
            service.WritesEnabled = false;

            Assert.That(service.Flush(force: true), Is.False);
            Assert.That(repository.WriteCount, Is.Zero);
        }

        // ── Load ───────────────────────────────────────────────────────────────────

        [Test]
        public void Load_WithNoFile_IsAFreshRunRatherThanAnError()
        {
            var service = Build(out _);
            Assert.That(service.Load(), Is.Zero);
            Assert.That(service.Count, Is.Zero);
            Assert.That(service.IsDirty, Is.False, "A load must not mark the table dirty.");
        }

        [Test]
        public void Load_ReadsBackWhatWasWritten()
        {
            var repository = new InMemoryWorldDamageRepository();
            repository.Seed(WorldId.Base, JsonUtility.ToJson(new WorldDamageFile
            {
                schema = 1,
                records =
                {
                    new WorldDamageRecord
                    {
                        slot = "default", zone = "Forest", instanceId = 42,
                        durability = 17, charges = -1, destroyed = false, regrowAtUnix = 0d,
                    },
                    new WorldDamageRecord
                    {
                        slot = "default", zone = "Forest", instanceId = 43,
                        durability = 0, charges = -1, destroyed = true, regrowAtUnix = 1234d,
                    },
                },
            }));

            var service = new WorldDamageService(repository, WorldId.Base);
            Assert.That(service.Load(), Is.EqualTo(2));

            var partial = service.Find("default", "Forest", 42);
            Assert.That(partial, Is.Not.Null);
            Assert.That(partial.durability, Is.EqualTo(17));
            Assert.That(partial.destroyed, Is.False);

            var felled = service.Find("default", "Forest", 43);
            Assert.That(felled, Is.Not.Null);
            Assert.That(felled.destroyed, Is.True);
            Assert.That(felled.regrowAtUnix, Is.EqualTo(1234d).Within(0.001d));
        }

        [Test]
        public void Load_OfACorruptFile_LeavesTheWorldPristineRatherThanThrowing()
        {
            var repository = new InMemoryWorldDamageRepository();
            repository.Seed(WorldId.Base, "{ this is not json");

            var service = new WorldDamageService(repository, WorldId.Base);

            LogAssert.ignoreFailingMessages = true;
            int loaded = service.Load();
            LogAssert.ignoreFailingMessages = false;

            // A damaged save file must cost the player their FELLED TREES, never their run.
            Assert.That(loaded, Is.Zero);
            Assert.That(service.Count, Is.Zero);
        }

        [Test]
        public void Load_KeysAreCaseInsensitiveOnZoneAndSlot()
        {
            var repository = new InMemoryWorldDamageRepository();
            repository.Seed(WorldId.Base, JsonUtility.ToJson(new WorldDamageFile
            {
                records =
                {
                    new WorldDamageRecord { slot = "Default", zone = "FOREST", instanceId = 7, durability = 3 },
                },
            }));

            var service = new WorldDamageService(repository, WorldId.Base);
            service.Load();

            // Zone names are compared OrdinalIgnoreCase everywhere else in the project, and a
            // lookup that disagreed would silently restore nothing.
            Assert.That(service.Find("default", "forest", 7), Is.Not.Null);
            Assert.That(service.Find("DEFAULT", "Forest", 7), Is.Not.Null);
        }

        // ── The record shape ───────────────────────────────────────────────────────

        [Test]
        public void ARecordThatTracksNoDurabilityStoresMinusOne()
        {
            // A Deplete-mode node has no BuildingDurability at all, so a default of 0 would
            // read back as "this building has no hit points left" and hand a live mine an
            // empty durability bar the first time anything asked.
            Assert.That(new WorldDamageRecord().durability, Is.EqualTo(-1));
            Assert.That(new WorldDamageRecord().charges, Is.EqualTo(-1));
            Assert.That(new WorldDamageRecord().destroyed, Is.False);
        }

        [Test]
        public void ClearInMemory_ForgetsEverythingAndWritesNothing()
        {
            var repository = new InMemoryWorldDamageRepository();
            repository.Seed(WorldId.Base, JsonUtility.ToJson(new WorldDamageFile
            {
                records = { new WorldDamageRecord { slot = "default", zone = "Forest", instanceId = 1 } },
            }));

            var service = new WorldDamageService(repository, WorldId.Base);
            service.Load();
            Assert.That(service.Count, Is.EqualTo(1));

            service.ClearInMemory();

            Assert.That(service.Count, Is.Zero);
            Assert.That(service.IsDirty, Is.False);
            Assert.That(repository.WriteCount, Is.Zero,
                "Starting a fresh run must not write — an abandoned new game has to leave the " +
                "previous run's record alone.");
        }

        // ── The regrow clock ───────────────────────────────────────────────────────

        [Test]
        public void UnixNow_IsAWallClockThatSurvivesASession()
        {
            double now = WorldDamageService.UnixNow();

            // Sanity bound rather than an exact value: anything below this is Time.time
            // masquerading as a timestamp, which is the bug the wall clock exists to prevent —
            // a deadline in session time either fires instantly or never after a reload.
            Assert.That(now, Is.GreaterThan(1_700_000_000d),
                "Regrow deadlines must be wall-clock seconds, not session time.");
        }
    }
}
