using System.IO;
using NUnit.Framework;
using Valkur.Core.Coordinates;
using Valkur.Gameplay.Save;

namespace Valkur.Tests.EditMode.Game.Save
{
    /// <summary>
    /// Phase 1 contract: SaveFileManager surfaces per-world overloads that
    /// keep WorldId.Base byte-compatible with the legacy flat layout while
    /// nesting non-base worlds under Saves/&lt;run&gt;/worlds/&lt;slug&gt;/.
    /// Without these guarantees, a multi-world session would either collapse
    /// every dimension's saves into one folder (data corruption) or break
    /// every existing save on the user's disk (data loss). Either case is
    /// exactly the kind of regression that demands a pinning test.
    /// </summary>
    [TestFixture]
    public class SaveFileManagerWorldRoutingTests
    {
        [Test]
        public void GetRunDirectory_LegacyOverload_RoutesToBaseLayout()
        {
            string legacy   = SaveFileManager.GetRunDirectory("run42");
            string explicit_ = SaveFileManager.GetRunDirectory("run42", WorldId.Base);
            Assert.AreEqual(legacy, explicit_,
                "The parameterless legacy overload must produce the same path " +
                "as the explicit base-world overload.");
        }

        [Test]
        public void GetRunDirectory_BaseWorld_DoesNotIntroduceWorldsSegment()
        {
            string p = SaveFileManager.GetRunDirectory("run42", WorldId.Base);
            StringAssert.DoesNotContain("worlds", p,
                "Base world must NOT introduce a /worlds/ segment — existing " +
                "save files on disk would otherwise become unreadable.");
        }

        [Test]
        public void GetRunDirectory_NonBaseWorld_NestsUnderWorldsSlug()
        {
            var alt = new WorldId(System.Guid.NewGuid(), "the_abyss");
            string p = SaveFileManager.GetRunDirectory("run42", alt);
            StringAssert.Contains(Path.Combine("worlds", "the_abyss"), p,
                "Non-base world must nest under run/worlds/<slug>/.");
        }

        [Test]
        public void GetAutosavePath_BaseWorld_MatchesLegacyOverload()
        {
            Assert.AreEqual(
                SaveFileManager.GetAutosavePath("run42"),
                SaveFileManager.GetAutosavePath("run42", WorldId.Base));
        }

        [Test]
        public void GetAutosavePath_NonBaseWorld_NestsUnderWorld()
        {
            var alt = new WorldId(System.Guid.NewGuid(), "the_abyss");
            string p = SaveFileManager.GetAutosavePath("run42", alt);
            StringAssert.Contains("the_abyss", p);
            StringAssert.Contains("autosave", p);
        }

        [Test]
        public void GetManualSavePath_NonBaseWorld_NestsUnderWorld()
        {
            var alt = new WorldId(System.Guid.NewGuid(), "the_abyss");
            string p = SaveFileManager.GetManualSavePath("run42", "myslot", alt);
            StringAssert.Contains("the_abyss", p);
            StringAssert.Contains("myslot", p);
        }

        [Test]
        public void GetBackupsDirectory_NonBaseWorld_NestsUnderWorld()
        {
            var alt = new WorldId(System.Guid.NewGuid(), "the_abyss");
            string p = SaveFileManager.GetBackupsDirectory("run42", alt);
            StringAssert.Contains("the_abyss", p);
            StringAssert.Contains(".backups", p);
        }

        [Test]
        public void WorldSlug_WithDirectoryTraversal_IsSanitized()
        {
            // Defensive: a malicious or malformed slug must not let saves
            // escape the run directory. The same SanitizeRunIdComponent
            // already used for run-ids must apply to slugs too.
            var bad = new WorldId(System.Guid.NewGuid(), "../escape");
            string p = SaveFileManager.GetRunDirectory("run42", bad);
            StringAssert.DoesNotContain("..", p,
                "Slugs containing path traversal must be sanitised before " +
                "being concatenated into the save root.");
        }

        [Test]
        public void EmptyRunId_LegacyFolder_IgnoresWorldId()
        {
            // The legacy folder is reserved for saves migrated without a
            // run id — multi-world routing does NOT apply there because
            // those files predate the multi-world era entirely.
            string p = SaveFileManager.GetRunDirectory("", WorldId.Base);
            string legacy = SaveFileManager.GetLegacyRunDirectory();
            Assert.AreEqual(legacy, p);
        }
    }
}
