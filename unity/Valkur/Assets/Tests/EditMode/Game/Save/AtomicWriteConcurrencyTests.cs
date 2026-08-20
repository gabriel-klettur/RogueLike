using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.Save;

namespace Valkur.Tests.EditMode.Game.Save
{
    /// <summary>
    /// <c>SaveFileManager.WriteSerializedJsonAtomic</c> under two writers at once, and the
    /// two defects that were sitting in it.
    ///
    /// It was found from a single console line on leaving Play —
    /// <c>[SaveService] Async autosave failed: Access to the path is denied.</c> The temp
    /// file was named <c>&lt;path&gt;.tmp</c>, one fixed name shared by every writer of that
    /// path, so two overlapping writes opened the same handle and the loser threw. Writes
    /// really can overlap: SaveService chains its autosaves through <c>_pendingWrite</c>,
    /// but <c>SaveFileManager.WriteAutosaveAsync</c> starts its own <c>Task.Run</c> that
    /// never joins that chain.
    ///
    /// The second defect was quieter. <c>File.Delete</c> followed by <c>File.Move</c>
    /// leaves a window with the save present nowhere at all. That window cannot be closed
    /// with the managed API this runtime offers — measured over 200 rewrites,
    /// <c>File.Replace</c> left the target absent 3715 times and delete-then-move 4327 —
    /// so <c>File.Replace</c> is kept as the narrower of the two and the name's promise of
    /// atomicity is simply not true. The rotating backups and the checksum are what carry
    /// a run across a crash in that window.
    ///
    /// These write to a scratch directory, never to persistentDataPath: an EditMode test
    /// writing into the real save folder is what caused the run twin-save incident.
    /// </summary>
    [TestFixture]
    public class AtomicWriteConcurrencyTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "valkur-atomic-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void TearDown()
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
            catch { /* a locked file must not fail the test that already passed */ }
        }

        private string Target => Path.Combine(_dir, "autosave.json");

        private static string Payload(int i) => "{\"n\":" + i + "}";

        // Matches ONLY the temps this writer creates: "<path>.<32 hex>.tmp".
        //
        // A bare "*.tmp" glob does not work here. File.Replace goes through Win32
        // ReplaceFile, which creates its own backup next to the target named
        // "<dest>~RF########.TMP" even when the backup argument is null, and under a
        // dozen concurrent replaces one of those occasionally outlives the call. On
        // Windows the glob is case-insensitive, so it matched that OS artefact and the
        // fixture failed intermittently on a file no Valkur code ever created.
        private static readonly Regex OurTempRe =
            new Regex(@"\.[0-9a-f]{32}\.tmp$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private IEnumerable<string> TempFiles()
            => Directory.GetFiles(_dir, "*", SearchOption.AllDirectories)
                        .Where(f => OurTempRe.IsMatch(f));

        [Test]
        public void ASingleWriteLandsAndLeavesNoTempBehind()
        {
            SaveFileManager.WriteSerializedJsonAtomic(Target, Payload(1));

            Assert.IsTrue(File.Exists(Target));
            Assert.AreEqual(Payload(1), File.ReadAllText(Target));
            Assert.IsEmpty(TempFiles(), "A finished write must not leave scratch files behind.");
        }

        [Test]
        public void RewritingReplacesTheContentWholesale()
        {
            SaveFileManager.WriteSerializedJsonAtomic(Target, "{\"a\":123456789}");
            SaveFileManager.WriteSerializedJsonAtomic(Target, "{\"b\":1}");

            Assert.AreEqual("{\"b\":1}", File.ReadAllText(Target),
                "A shorter second write must truncate, not overlay the tail of the first.");
        }

        [Test]
        public void ConcurrentWritesToOnePathAllSucceed()
        {
            // The shipped failure, reproduced: without a per-write temp name this throws
            // UnauthorizedAccessException on whichever writer loses the race.
            const int WRITERS = 12;
            var errors = new List<Exception>();
            var gate = new object();

            var tasks = Enumerable.Range(0, WRITERS).Select(i => Task.Run(() =>
            {
                try { SaveFileManager.WriteSerializedJsonAtomic(Target, Payload(i)); }
                catch (Exception e) { lock (gate) errors.Add(e); }
            })).ToArray();

            Assert.IsTrue(Task.WaitAll(tasks, TimeSpan.FromSeconds(30)),
                "Writers did not finish — something is deadlocked on the temp file.");

            Assert.IsEmpty(errors.Select(e => e.GetType().Name + ": " + e.Message),
                "Overlapping writes to one save file must not throw. " +
                string.Join(" | ", errors.Select(e => e.Message)));

            string final = File.ReadAllText(Target);
            CollectionAssert.Contains(Enumerable.Range(0, WRITERS).Select(Payload).ToList(), final,
                "Last write wins is fine; a torn or interleaved file is not.");
            Assert.IsEmpty(TempFiles(), "Every writer must clean up after itself.");
        }

        [Test]
        public void AReaderNeverSeesAHalfWrittenSave()
        {
            // This is the property the temp-then-rename pattern actually buys, and the one
            // worth pinning. It does NOT buy a gap-free swap: measured in this runtime over
            // 200 rewrites, File.Replace left the target momentarily absent 3715 times and
            // delete-then-move 4327. Mono's File.Replace is not Win32 ReplaceFile. What
            // makes a run survive a crash in that window is the rotating backups and the
            // checksum, not the rename.
            var valid = new HashSet<string>(Enumerable.Range(0, 400).Select(Payload));
            SaveFileManager.WriteSerializedJsonAtomic(Target, Payload(0));

            var stop = new ManualResetEventSlim(false);
            var torn = new List<string>();
            var gate = new object();

            var reader = Task.Run(() =>
            {
                while (!stop.IsSet)
                {
                    string seen;
                    try { seen = File.ReadAllText(Target); }
                    catch (IOException) { continue; }          // absent or locked mid-rename
                    catch (UnauthorizedAccessException) { continue; }
                    if (!valid.Contains(seen)) lock (gate) torn.Add(seen);
                }
            });

            for (int i = 1; i <= 200; i++)
                SaveFileManager.WriteSerializedJsonAtomic(Target, Payload(i));

            stop.Set();
            reader.Wait(TimeSpan.FromSeconds(10));

            Assert.IsEmpty(torn.Take(3),
                $"A reader saw {torn.Count} truncated or interleaved save(s). Writing " +
                "straight into the target instead of into a temp is what produces those.");
        }

        [Test]
        public void AFailedWriteCleansUpItsTemp()
        {
            // A directory where the file should be: the rename cannot succeed, so this
            // exercises the failure path rather than the happy one.
            string blocked = Path.Combine(_dir, "blocked.json");
            Directory.CreateDirectory(blocked);

            Assert.Catch(() => SaveFileManager.WriteSerializedJsonAtomic(blocked, Payload(1)),
                "Writing over a directory must fail rather than silently do nothing.");

            Assert.IsEmpty(TempFiles(),
                "A failed write that leaves its temp behind fills the save folder with " +
                "debris that nothing ever collects.");
        }

        [Test]
        public void TheSharedTempNameDoesNotComeBack()
        {
            string path = Path.Combine(Application.dataPath, "_Project", "Scripts",
                                       "Gameplay", "Save", "SaveFileManager.IO.cs");
            Assert.IsTrue(File.Exists(path), "SaveFileManager.IO.cs moved or was renamed.");

            string body = Regex.Replace(File.ReadAllText(path), @"/\*.*?\*/", "",
                                        RegexOptions.Singleline);
            body = string.Join("\n", body.Split('\n').Select(l =>
            {
                int i = l.IndexOf("//", StringComparison.Ordinal);
                return i < 0 ? l : l.Substring(0, i);
            }));

            Assert.IsFalse(Regex.IsMatch(body, @"tempPath\s*=\s*path\s*\+\s*""\.tmp"""),
                "One fixed temp name per path means two concurrent writers share a handle " +
                "and one of them throws. The name must be unique per write.");
            Assert.IsFalse(Regex.IsMatch(body, @"File\.Delete\(path\);\s*\n\s*File\.Move\("),
                "Delete-then-Move reopens the window where the save exists nowhere.");
        }
    }
}
