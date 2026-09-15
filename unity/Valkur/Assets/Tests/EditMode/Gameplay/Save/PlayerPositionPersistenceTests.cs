using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.Save;

namespace Valkur.Tests.EditMode.Gameplay.Save
{
    /// <summary>
    /// The rule that keeps an interior's coordinate out of the player's saved position, and
    /// keeps one that is already in there from being spawned on.
    ///
    /// <para>THE BUG THIS PINS was reproduced end to end: standing inside a fourteen-by-ten
    /// room, the checkpoint recorded
    /// <c>{"x":11.0,"y":-8.0,"zone":"house_interior_small.overlay"}</c> and the autosave's
    /// player block agreed — and the next boot spawned the player at (11, -8) of the OUTDOOR
    /// world, off the map. An interior is its own grid loaded at the origin, so a position
    /// measured in one means nothing in the other.</para>
    ///
    /// <para>Everything here is a pure function on purpose. The failure needs a world swap, a
    /// save timer and a boot to happen for real; the DECISION needs eight values, and a policy
    /// that could only be tested by playing is a policy nobody re-tests.</para>
    /// </summary>
    [TestFixture]
    public class PlayerPositionPersistenceTests
    {
        private static readonly Vector2 Live   = new Vector2(11f, -8f);   // inside the room
        private static readonly Vector2 Door   = new Vector2(174.5f, 56.5f);
        private static readonly Vector2 Before = new Vector2(150.1f, 56.8f);

        private static PlayerPositionPersistence.Record Resolve(
            bool suspended,
            bool hasReturn = false, bool returnIsBase = true,
            bool hasLastBase = false)
            => PlayerPositionPersistence.Resolve(
                   suspended,
                   hasReturn, returnIsBase, Door,
                   hasLastBase, Before, "Lobby",
                   Live, "house_interior_small.overlay");

        // ── Writing ──────────────────────────────────────────────────────────

        [Test]
        public void Outdoors_TheLivePositionIsWrittenUnchanged()
        {
            var r = Resolve(suspended: false);

            Assert.That(r.Position, Is.EqualTo(Live));
            Assert.That(r.IsBaseWorld, Is.True);
            Assert.That(r.From, Is.EqualTo(PlayerPositionPersistence.Source.Live));
        }

        /// <summary>The doorway beats everything else: it is in the base world AND it is
        /// somewhere the player can stand, which no other candidate guarantees.</summary>
        [Test]
        public void InsideAnInterior_TheDoorwayWins()
        {
            var r = Resolve(suspended: true, hasReturn: true, hasLastBase: true);

            Assert.That(r.Position, Is.EqualTo(Door));
            Assert.That(r.IsBaseWorld, Is.True);
            Assert.That(r.From, Is.EqualTo(PlayerPositionPersistence.Source.ReturnPoint));
            Assert.That(r.Zone, Is.EqualTo("Lobby"),
                "the label has to follow the position, or the restore refuses a good one");
        }

        [Test]
        public void InsideAnInterior_WithNoDoorway_TheLastBaseWorldSampleIsUsed()
        {
            var r = Resolve(suspended: true, hasReturn: false, hasLastBase: true);

            Assert.That(r.Position, Is.EqualTo(Before));
            Assert.That(r.IsBaseWorld, Is.True);
            Assert.That(r.From, Is.EqualTo(PlayerPositionPersistence.Source.LastBaseWorld));
        }

        /// <summary>
        /// Nesting is not a base world. The field exists, nothing nests today, and a return
        /// point that is itself an interior is just as off-the-map as the room being stood in.
        /// </summary>
        [Test]
        public void ANestedReturnPoint_IsNotTreatedAsTheBaseWorld()
        {
            var r = Resolve(suspended: true, hasReturn: true, returnIsBase: false, hasLastBase: true);

            Assert.That(r.Position, Is.EqualTo(Before));
            Assert.That(r.From, Is.EqualTo(PlayerPositionPersistence.Source.LastBaseWorld));
        }

        /// <summary>
        /// Nothing known: the interior position comes back FLAGGED rather than substituted. A
        /// save document must carry some position, and the caller that can decline to write
        /// (the checkpoint) reads the flag; the one that cannot (the save) relies on the zone
        /// label being refused at the other end.
        /// </summary>
        [Test]
        public void InsideAnInterior_WithNothingKnown_TheResultIsFlaggedNotSubstituted()
        {
            var r = Resolve(suspended: true, hasReturn: false, hasLastBase: false);

            Assert.That(r.Position, Is.EqualTo(Live));
            Assert.That(r.IsBaseWorld, Is.False, "this is what stops the checkpoint being written");
            Assert.That(r.From, Is.EqualTo(PlayerPositionPersistence.Source.Interior));
            Assert.That(r.Zone, Is.EqualTo("house_interior_small.overlay"),
                "the label is the only thing that lets the restore side refuse it");
        }

        // ── Spawning ─────────────────────────────────────────────────────────

        private static Func<string, bool> Zones(params string[] known)
            => name => Array.IndexOf(known, name) >= 0;

        [Test]
        public void ARealZone_IsSpawnable()
        {
            Assert.That(PlayerPositionPersistence.IsUsableSpawn("Lobby", Zones("Lobby", "dungeon"), out _),
                Is.True);
        }

        [Test]
        public void AnOverlayLabel_IsRefused_AndSaysWhy()
        {
            bool ok = PlayerPositionPersistence.IsUsableSpawn(
                "house_interior_small.overlay", Zones("Lobby"), out string reason);

            Assert.That(ok, Is.False);
            Assert.That(reason, Does.Contain("house_interior_small.overlay"),
                "a refusal the author cannot trace is a position that vanishes for no reason");
        }

        /// <summary>
        /// An empty label is every checkpoint written before the zone was recorded. Refusing
        /// those would relocate the player of every older save to fix a bug they do not have —
        /// this failure announces itself with a zone nobody has heard of, never with silence.
        /// </summary>
        [Test]
        public void AnEmptyZone_IsAccepted()
        {
            Assert.That(PlayerPositionPersistence.IsUsableSpawn("", Zones("Lobby"), out _), Is.True);
            Assert.That(PlayerPositionPersistence.IsUsableSpawn(null, Zones("Lobby"), out _), Is.True);
        }

        /// <summary>
        /// No zone database resolvable means no way to tell a real zone from an overlay.
        /// Refusing on a missing dependency would strand the player at the lobby every time the
        /// ZoneManager happened not to be up yet.
        /// </summary>
        [Test]
        public void WithNoZoneDatabase_EverythingIsAccepted()
        {
            Assert.That(PlayerPositionPersistence.IsUsableSpawn("anything", null, out _), Is.True);
        }

        // ── The wiring ───────────────────────────────────────────────────────

        /// <summary>
        /// Every reader of a stored player position goes through the refusal. Source checks
        /// because each sits inside a boot coroutine, a restore pass or a death flow — and a
        /// reader that skipped it would put the player in the void exactly as before, in a path
        /// nothing else covers.
        /// </summary>
        [TestCase("_Project/Scripts/Gameplay/Bootstrap/GameplaySceneSetup.SpawnPlayer.cs", "IsSpawnableCheckpoint")]
        [TestCase("_Project/Scripts/Gameplay/Bootstrap/GameplaySceneSetup.cs",             "IsSpawnableCheckpoint")]
        [TestCase("_Project/Scripts/Gameplay/Save/GameStateRestorer.cs",                   "IsUsableSpawn")]
        [TestCase("_Project/Scripts/Gameplay/Combat/Death/DeathSequenceController.Rescue.cs", "IsUsableSpawn")]
        public void EveryReaderOfAStoredPosition_ChecksItFirst(string relPath, string needle)
        {
            string path = Path.Combine(Application.dataPath, relPath);
            Assert.That(File.Exists(path), Is.True, path);
            Assert.That(File.ReadAllText(path), Does.Contain(needle),
                relPath + " reads a stored player position and must refuse an interior one");
        }

        /// <summary>
        /// Both WRITERS answer through one method. Two writers of the player position with two
        /// rules is how one of them stays wrong — which is the state this fixture exists to end.
        /// </summary>
        [TestCase("_Project/Scripts/Gameplay/Save/SaveService.cs")]
        [TestCase("_Project/Scripts/Gameplay/Save/GameStateCollector.cs")]
        public void BothWriters_ResolveThroughTheSamePolicy(string relPath)
        {
            string path = Path.Combine(Application.dataPath, relPath);
            Assert.That(File.Exists(path), Is.True, path);
            Assert.That(File.ReadAllText(path), Does.Contain("ResolvePersistablePlayerPosition"),
                relPath + " must not write the player position from the live transform");
        }

        /// <summary>
        /// The save path PEEKS at the return point. Consuming it is what actually walks the
        /// player back out of the interior, so an autosave that spent it would leave them
        /// unable to leave the room they are standing in.
        /// </summary>
        [Test]
        public void TheSavePath_PeeksTheReturnPoint_RatherThanConsumingIt()
        {
            string src = File.ReadAllText(Path.Combine(
                Application.dataPath, "_Project/Scripts/Gameplay/Save/SaveService.cs"));

            Assert.That(src, Does.Contain("TryPeekReturnPoint"));
            Assert.That(src, Does.Not.Contain("TryConsumeReturnPoint"),
                "consuming it would trap the player inside the interior");
        }
    }
}
