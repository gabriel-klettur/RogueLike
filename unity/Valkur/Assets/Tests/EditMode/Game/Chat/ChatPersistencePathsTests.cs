using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Gameplay.Chat;

namespace Valkur.Tests.EditMode.Game.Chat
{
    /// <summary>
    /// Tests for <see cref="ChatPersistencePaths"/> — the single place that maps an
    /// arbitrary NPC key onto a filesystem path.
    ///
    /// Why this fixture matters: the slug algorithm is a *storage contract*, not an
    /// implementation detail. Every NPC memory file already on a player's disk is
    /// named after <c>Slugify(npcKey)</c>. If the mapping ever changes, the game
    /// silently stops finding those files and every NPC forgets the player — no
    /// exception, no log, just lost data. So the tests below pin:
    ///
    ///   • the exact file names produced for a set of representative keys (golden values),
    ///   • that hostile / non-filesystem-safe keys can never escape the memory directory,
    ///   • which distinct keys are known to collide onto one file (documented hazards),
    ///   • the directory layout and the session-log timestamp format.
    ///
    /// <see cref="NPCMemoryStoreTests"/> already exercises the OverrideRoot redirection
    /// mechanism itself, so this fixture does not repeat it — it only uses OverrideRoot
    /// to keep every assertion off <c>Application.persistentDataPath</c> and off real disk.
    ///
    /// Nothing here creates files: these are pure path-construction tests, and one test
    /// explicitly asserts that no directory is created as a side effect.
    /// </summary>
    [TestFixture]
    public class ChatPersistencePathsTests
    {
        private string _testRoot;

        [SetUp]
        public void SetUp()
        {
            // Route every ChatPersistencePaths lookup to a temp folder that we never
            // actually create — path building must not touch the filesystem at all.
            _testRoot = Path.Combine(
                Path.GetTempPath(),
                "valkur_test_chatpaths_" + Guid.NewGuid().ToString("N"));
            ChatPersistencePaths.OverrideRoot = _testRoot;

            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;

            // Restore the production root so later fixtures / play sessions are unaffected.
            ChatPersistencePaths.OverrideRoot = null;

            // Best-effort: nothing should have been created, but never leave temp dirt.
            try
            {
                if (Directory.Exists(_testRoot))
                    Directory.Delete(_testRoot, recursive: true);
            }
            catch
            {
                // Ignore — the OS will clean up temp eventually.
            }
        }

        // ── helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Normalises separators so assertions are identical on Windows and macOS/Linux
        /// (Path.Combine emits '\' on Windows, '/' elsewhere).
        /// </summary>
        private static string Norm(string path) =>
            path == null ? null : path.Replace('\\', '/').TrimEnd('/');

        // ── roots and directories ─────────────────────────────────────────────

        [Test]
        public void Root_OverrideRootEmptyString_FallsBackToPersistentDataPath()
        {
            // Arrange — an *empty* override, not a null one. The production guard is
            // string.IsNullOrEmpty; a naive `== null` check would silently root every
            // chat file at the process working directory instead.
            ChatPersistencePaths.OverrideRoot = string.Empty;

            // Assert
            Assert.AreEqual(Application.persistentDataPath, ChatPersistencePaths.Root,
                "An empty OverrideRoot must fall back to persistentDataPath, not be used " +
                "as a relative root — otherwise chat data lands next to the executable.");
        }

        [Test]
        public void MemoryDirectory_WithOverrideRoot_IsRootSlashChatSlashMemories()
        {
            // Assert — the two path segments are part of the on-disk contract; renaming
            // either one orphans every memory file saved by previous builds.
            Assert.AreEqual(
                Norm(Path.Combine(_testRoot, "chat", "memories")),
                Norm(ChatPersistencePaths.MemoryDirectory),
                "MemoryDirectory must stay <Root>/chat/memories.");
        }

        [Test]
        public void LogDirectory_WithOverrideRoot_IsRootSlashLogsSlashChatSessions()
        {
            // Assert
            Assert.AreEqual(
                Norm(Path.Combine(_testRoot, "logs", "chat_sessions")),
                Norm(ChatPersistencePaths.LogDirectory),
                "LogDirectory must stay <Root>/logs/chat_sessions.");

            Assert.AreNotEqual(
                Norm(ChatPersistencePaths.MemoryDirectory),
                Norm(ChatPersistencePaths.LogDirectory),
                "Memories and session logs must never share a directory — a wipe of one " +
                "would take the other with it.");
        }

        // ── MemoryPath shape ──────────────────────────────────────────────────

        [Test]
        public void MemoryPath_AnyKey_LivesInMemoryDirectoryWithJsonExtension()
        {
            // Act
            string path = ChatPersistencePaths.MemoryPath("blacksmith");

            // Assert
            Assert.AreEqual(Norm(ChatPersistencePaths.MemoryDirectory),
                Norm(Path.GetDirectoryName(path)),
                "Memory files must sit directly in MemoryDirectory — no per-NPC subfolders.");
            Assert.AreEqual(".json", Path.GetExtension(path),
                "Memory files must keep the .json extension the store reads back.");
            Assert.AreEqual(ChatPersistencePaths.Slugify("blacksmith"),
                Path.GetFileNameWithoutExtension(path),
                "The file stem must be exactly Slugify(key) — the store has no other index " +
                "from key to file.");
        }

        [Test]
        public void MemoryPath_Called_CreatesNoDirectoryOrFile()
        {
            // Act — building a path must be a pure string operation.
            string path = ChatPersistencePaths.MemoryPath("lazy-npc");

            // Assert
            Assert.IsFalse(File.Exists(path),
                "MemoryPath must not create the file — callers decide when to write.");
            Assert.IsFalse(Directory.Exists(ChatPersistencePaths.MemoryDirectory),
                "MemoryPath must not create MemoryDirectory as a side effect; a getter that " +
                "touches disk makes every read path a potential IOException.");
            Assert.IsFalse(Directory.Exists(_testRoot),
                "Not even the root directory may be created by path construction.");
        }

        [Test]
        public void MemoryPath_SameKeyDifferentRoot_KeepsIdenticalFileName()
        {
            // Arrange
            string rootA = Path.Combine(Path.GetTempPath(), "valkur_chatpaths_a");
            string rootB = Path.Combine(Path.GetTempPath(), "valkur_chatpaths_b");

            // Act
            ChatPersistencePaths.OverrideRoot = rootA;
            string pathA = ChatPersistencePaths.MemoryPath("wandering-merchant");
            ChatPersistencePaths.OverrideRoot = rootB;
            string pathB = ChatPersistencePaths.MemoryPath("wandering-merchant");

            // Assert — only the directory may vary with the root.
            Assert.AreEqual(Path.GetFileName(pathA), Path.GetFileName(pathB),
                "The file name must depend on the NPC key alone. If anything volatile " +
                "(timestamp, GUID, root hash) leaks into it, memories are never found again.");
            Assert.AreNotEqual(Norm(pathA), Norm(pathB),
                "A different root must still produce a different absolute path.");
        }

        [Test]
        public void MemoryPath_RepresentativeKeys_ProduceExactHistoricalFileNames()
        {
            // These are golden values. A failure here is NOT a test to fix — it means the
            // slug algorithm changed and every memory file already on players' disks has
            // been orphaned. Treat it as a data-migration decision.
            AssertFileName("blacksmith", "blacksmith.json");
            AssertFileName("NPC Guard #2", "npc_guard_#2.json");
            AssertFileName("vendor/alchemist", "vendor_alchemist.json");
            AssertFileName("Elder  Willow", "elder_willow.json");
            AssertFileName("\u00d1and\u00fa", "\u00f1and\u00fa.json");   // "Ñandú"
            AssertFileName(null, "_empty_.json");
            AssertFileName("", "_empty_.json");
        }

        private static void AssertFileName(string key, string expectedFileName)
        {
            Assert.AreEqual(expectedFileName,
                Path.GetFileName(ChatPersistencePaths.MemoryPath(key)),
                $"Golden file name for key '{key ?? "<null>"}' changed — saved memories for " +
                "this NPC would be orphaned.");
        }

        // ── containment: hostile keys must not escape the directory ───────────

        [TestCase("../../evil", TestName = "MemoryPath_ParentTraversalForward_StaysInsideMemoryDirectory")]
        [TestCase("..\\..\\evil", TestName = "MemoryPath_ParentTraversalBackward_StaysInsideMemoryDirectory")]
        [TestCase("/etc/passwd", TestName = "MemoryPath_AbsolutePosixKey_StaysInsideMemoryDirectory")]
        [TestCase("C:/Windows/System32/evil", TestName = "MemoryPath_AbsoluteWindowsKey_StaysInsideMemoryDirectory")]
        [TestCase("sub/dir/npc", TestName = "MemoryPath_KeyWithSubdirectory_StaysInsideMemoryDirectory")]
        public void MemoryPath_HostileKey_StaysInsideMemoryDirectory(string hostileKey)
        {
            // Act
            string path = ChatPersistencePaths.MemoryPath(hostileKey);

            // Assert — this is the security-relevant property: Path.Combine silently
            // discards its first argument when the second is rooted, so the slug MUST
            // neutralise '/', '\' and ':' before it reaches Path.Combine.
            Assert.AreEqual(Norm(ChatPersistencePaths.MemoryDirectory),
                Norm(Path.GetDirectoryName(path)),
                $"Key '{hostileKey}' escaped MemoryDirectory — an NPC id sourced from a " +
                "catalog or save file could then overwrite arbitrary files.");
            Assert.IsFalse(Path.IsPathRooted(ChatPersistencePaths.Slugify(hostileKey)),
                $"Slugify('{hostileKey}') must never return a rooted path — Path.Combine " +
                "would drop the memory directory entirely.");
        }

        // ── collisions ────────────────────────────────────────────────────────

        [Test]
        public void MemoryPath_DistinctRealisticKeys_DoNotCollide()
        {
            // A corpus of keys shaped like the ones the game actually uses.
            string[] keys =
            {
                "blacksmith",
                "blacksmith_2",
                "blacksmith-2",
                "npc_blacksmith",
                "vendor.blacksmith",
                "quest_giver",
                "elder",
                "elder2"
            };

            for (int i = 0; i < keys.Length; i++)
            {
                for (int j = i + 1; j < keys.Length; j++)
                {
                    Assert.AreNotEqual(
                        ChatPersistencePaths.MemoryPath(keys[i]),
                        ChatPersistencePaths.MemoryPath(keys[j]),
                        $"Keys '{keys[i]}' and '{keys[j]}' collapsed onto the same file. " +
                        "Widening the sanitised character set (e.g. also replacing '.' or " +
                        "'-') merges the memories of two different NPCs.");
                }
            }
        }

        [Test]
        public void MemoryPath_KeysDifferingOnlyByIllegalCharacters_ShareOneFile()
        {
            // Documented hazard, pinned deliberately: every illegal character maps to the
            // same '_', so these four *different* ids are one file on disk. Content
            // authors must not rely on punctuation alone to distinguish NPC ids.
            string reference = ChatPersistencePaths.MemoryPath("guard_one");

            foreach (string variant in new[] { "guard/one", "guard\\one", "guard one", "guard:one", "guard|one" })
            {
                Assert.AreEqual(reference, ChatPersistencePaths.MemoryPath(variant),
                    $"'{variant}' is expected to collide with 'guard_one'. If this now " +
                    "differs, the slug gained disambiguation — good, but existing files " +
                    "were orphaned and this test must be revisited alongside a migration.");
            }
        }

        [Test]
        public void MemoryPath_KeysDifferingOnlyByCase_ShareOneFile()
        {
            // Pinned: ToLowerInvariant means ids are case-insensitive on disk. Without it,
            // Windows (case-insensitive FS) and Linux (case-sensitive) would disagree about
            // whether two ids are the same NPC.
            Assert.AreEqual(
                ChatPersistencePaths.MemoryPath("Guard-Captain"),
                ChatPersistencePaths.MemoryPath("guard-captain"),
                "NPC keys must be case-folded so the same id resolves identically on " +
                "case-sensitive and case-insensitive filesystems.");
        }

        [Test]
        public void MemoryPath_KeysSharingFirstEightyCharacters_ShareOneFile()
        {
            // Documented hazard: the 80-char cap truncates, it does not hash. Two long ids
            // with a common prefix silently become one memory file.
            string prefix = new string('z', 90);

            Assert.AreEqual(
                ChatPersistencePaths.MemoryPath(prefix + "-alpha"),
                ChatPersistencePaths.MemoryPath(prefix + "-beta"),
                "Long keys sharing their first 80 characters collide. Generated or " +
                "namespaced NPC ids must stay distinct within the first 80 characters.");
        }

        [Test]
        public void MemoryPath_KeyLiterallyNamedEmptySentinel_CollidesWithNullKey()
        {
            // The "_empty_" sentinel is a normal slug value, so an NPC actually called
            // "_empty_" shares its file with every null/empty key.
            Assert.AreEqual(
                ChatPersistencePaths.MemoryPath(null),
                ChatPersistencePaths.MemoryPath("_empty_"),
                "'_empty_' is not a reserved id: it collides with the null-key sentinel. " +
                "Never ship an NPC whose id is '_empty_'.");
        }

        // ── Slugify ───────────────────────────────────────────────────────────

        [Test]
        public void Slugify_NullOrEmpty_ReturnsEmptySentinel()
        {
            // Assert — must never return "" : Path.Combine(dir, ".json") would produce a
            // hidden/extension-only file on POSIX.
            Assert.AreEqual("_empty_", ChatPersistencePaths.Slugify(null),
                "A null key must degrade to the '_empty_' sentinel, not throw or return null.");
            Assert.AreEqual("_empty_", ChatPersistencePaths.Slugify(string.Empty),
                "An empty key must degrade to the '_empty_' sentinel.");
        }

        [Test]
        public void Slugify_WhitespaceOnlyKey_ReturnsSingleUnderscoreNotSentinel()
        {
            // Subtle: "   " is neither null nor empty, so it takes the regex branch and
            // becomes "_", NOT "_empty_". Pinned because it is the kind of difference a
            // "cleanup" refactor (adding a Trim()) would quietly change, re-homing files.
            Assert.AreEqual("_", ChatPersistencePaths.Slugify("   "),
                "A whitespace-only key collapses to '_' — it does not hit the empty sentinel.");
        }

        // Explicit TestNames: several of these characters render identically (or
        // invisibly) in the Test Runner tree, and NUnit would otherwise generate
        // colliding display names for SPACE and NO-BREAK SPACE.
        [TestCase('<', TestName = "Slugify_LessThan_ReplacedWithUnderscore")]
        [TestCase('>', TestName = "Slugify_GreaterThan_ReplacedWithUnderscore")]
        [TestCase(':', TestName = "Slugify_Colon_ReplacedWithUnderscore")]
        [TestCase('"', TestName = "Slugify_DoubleQuote_ReplacedWithUnderscore")]
        [TestCase('/', TestName = "Slugify_ForwardSlash_ReplacedWithUnderscore")]
        [TestCase('\\', TestName = "Slugify_Backslash_ReplacedWithUnderscore")]
        [TestCase('|', TestName = "Slugify_Pipe_ReplacedWithUnderscore")]
        [TestCase('?', TestName = "Slugify_QuestionMark_ReplacedWithUnderscore")]
        [TestCase('*', TestName = "Slugify_Asterisk_ReplacedWithUnderscore")]
        [TestCase(' ', TestName = "Slugify_Space_ReplacedWithUnderscore")]
        [TestCase('\t', TestName = "Slugify_Tab_ReplacedWithUnderscore")]
        [TestCase('\n', TestName = "Slugify_LineFeed_ReplacedWithUnderscore")]
        [TestCase('\r', TestName = "Slugify_CarriageReturn_ReplacedWithUnderscore")]
        [TestCase('\f', TestName = "Slugify_FormFeed_ReplacedWithUnderscore")]
        [TestCase('\v', TestName = "Slugify_VerticalTab_ReplacedWithUnderscore")]
        [TestCase('\u00a0', TestName = "Slugify_NoBreakSpace_ReplacedWithUnderscore")] // NO-BREAK SPACE — Unicode whitespace, easy to paste in by accident
        public void Slugify_IllegalCharacter_ReplacedWithUnderscore(char illegal)
        {
            // Assert — every one of these is rejected by at least one major filesystem,
            // so none may survive into a file name.
            Assert.AreEqual("a_b", ChatPersistencePaths.Slugify("a" + illegal + "b"),
                $"Character U+{(int)illegal:X4} must be sanitised to '_'.");
        }

        [Test]
        public void Slugify_RunOfIllegalCharacters_CollapsesToOneUnderscore()
        {
            // The regex is quantified with '+', so a run is one underscore, not N. This
            // keeps names short and stable when someone double-spaces an id.
            Assert.AreEqual("a_b", ChatPersistencePaths.Slugify("a   \t \\// b"),
                "A run of illegal characters must collapse to a single '_'.");
            Assert.AreEqual("_start", ChatPersistencePaths.Slugify("   start"),
                "A leading run collapses to one leading '_' (it is not trimmed).");
            Assert.AreEqual("end_", ChatPersistencePaths.Slugify("end   "),
                "A trailing run collapses to one trailing '_' (it is not trimmed).");
        }

        [Test]
        public void Slugify_UppercaseAscii_LowercasedInvariantly()
        {
            // 'I' is the trap: under the Turkish locale ToLower() yields 'ı' (U+0131),
            // which would make file names depend on the player's OS language.
            Assert.AreEqual("iiii", ChatPersistencePaths.Slugify("IIII"),
                "Case folding must be culture-invariant — a Turkish-locale player must " +
                "resolve the same file as everyone else.");
            Assert.AreEqual("guard-captain", ChatPersistencePaths.Slugify("GUARD-Captain"),
                "Mixed-case keys must lowercase without touching the '-' separator.");
        }

        [Test]
        public void Slugify_NonAsciiLetters_ArePreserved()
        {
            // The slug is not ASCII-folded. Pinned because adding transliteration later
            // would rename every accented NPC's file.
            Assert.AreEqual("\u00f1and\u00fa_\u00e9lfico",
                ChatPersistencePaths.Slugify("\u00d1and\u00fa \u00c9lfico"),
                "Accented characters must survive (lowercased), not be stripped or folded " +
                "to ASCII — doing so would orphan existing files and merge distinct ids.");
            Assert.AreEqual("\u30ad\u30e3\u30e9",
                ChatPersistencePaths.Slugify("\u30ad\u30e3\u30e9"),
                "Non-Latin scripts must pass through untouched.");
        }

        [Test]
        public void Slugify_FilesystemSafePunctuation_IsPreserved()
        {
            // Everything here is legal on Windows, macOS and Linux, so the slug must NOT
            // touch it — widening the regex would collapse ids that today are distinct.
            const string safe = ".-_'()#%&+=,;!@~[]{}^$";

            foreach (char c in safe)
            {
                Assert.AreEqual("a" + c + "b", ChatPersistencePaths.Slugify("a" + c + "b"),
                    $"Character U+{(int)c:X4} is filesystem-safe and must be preserved verbatim.");
            }
        }

        [Test]
        public void Slugify_ControlCharacter_IsNotSanitised()
        {
            // Characterisation, not endorsement: control characters (U+0001 here) are
            // outside the illegal-character class and outside \s, so they survive into the
            // file name even though they are invalid on Windows. Pinned so the gap is
            // visible; widening the regex is a behaviour change that orphans files.
            Assert.AreEqual("a\u0001b", ChatPersistencePaths.Slugify("a\u0001b"),
                "Control characters are currently NOT sanitised. If this fails, sanitisation " +
                "was widened — verify the migration story for already-saved memory files.");
        }

        [Test]
        public void Slugify_KeyLongerThanCap_TruncatedToEightyCharacters()
        {
            // Act
            string slug = ChatPersistencePaths.Slugify(new string('x', 200));

            // Assert — the cap exists so the full path stays under the ~260-char Windows
            // MAX_PATH when persistentDataPath is already deep.
            Assert.AreEqual(80, slug.Length,
                "Slugs must be capped at 80 characters to keep the absolute path under " +
                "Windows MAX_PATH.");
            Assert.AreEqual(new string('x', 80), slug,
                "Truncation must keep the leading characters (a change to trailing/hashing " +
                "would rename every long-key file).");
        }

        [Test]
        public void Slugify_KeyAtExactlyCapLength_IsNotTruncated()
        {
            // Boundary: 80 must pass through untouched (an off-by-one '>=' would chop it).
            string exact = new string('y', 80);

            Assert.AreEqual(exact, ChatPersistencePaths.Slugify(exact),
                "A key that slugs to exactly 80 characters must not be truncated.");
        }

        [Test]
        public void Slugify_LongKeyWithLeadingWhitespace_CapsAfterCollapsingNotBefore()
        {
            // 40 spaces + 60 'a'. Collapse-then-cap => "_" + 60 a's (61 chars, no cap hit).
            // Cap-then-collapse would give "_" + 40 a's. This pins the order of operations,
            // which decides the file name for any key with leading padding.
            string raw = new string(' ', 40) + new string('a', 60);

            string slug = ChatPersistencePaths.Slugify(raw);

            Assert.AreEqual("_" + new string('a', 60), slug,
                "The 80-char cap must be applied AFTER illegal runs collapse; capping the " +
                "raw string first would produce a different (shorter) file name.");
        }

        [Test]
        public void Slugify_LongKeyEndingInSurrogatePair_StillCapsAtEightyCodeUnits()
        {
            // 79 'a' + one astral emoji (2 UTF-16 code units) = 81 units.
            // Known limitation: the cap counts code units, so the pair is split and the
            // slug ends on a lone high surrogate. Pinned to make the sharp edge visible.
            string raw = new string('a', 79) + "\U0001F600";

            string slug = ChatPersistencePaths.Slugify(raw);

            Assert.AreEqual(80, slug.Length,
                "The cap must hold regardless of the input's Unicode content.");
            Assert.IsTrue(char.IsHighSurrogate(slug[slug.Length - 1]),
                "Known limitation: truncation is by UTF-16 code unit, so an astral " +
                "character straddling the 80th unit leaves a lone surrogate in the file " +
                "name. If this fails, the cap became surrogate-aware — a rename for any " +
                "affected key.");
        }

        // ── SessionLogPath ────────────────────────────────────────────────────

        [Test]
        public void SessionLogPath_AnyArgs_LivesInLogDirectoryWithLogExtension()
        {
            // Act
            string path = ChatPersistencePaths.SessionLogPath("alchemist", "vendor");

            // Assert
            Assert.AreEqual(Norm(ChatPersistencePaths.LogDirectory),
                Norm(Path.GetDirectoryName(path)),
                "Session logs must live in LogDirectory, never beside the memory files.");
            Assert.AreEqual(".log", Path.GetExtension(path),
                "Session logs must keep the .log extension the log-sweeper globs for.");
        }

        [Test]
        public void SessionLogPath_FileName_PutsRoleBeforeNpcKey()
        {
            // Act
            string fileName = Path.GetFileName(
                ChatPersistencePaths.SessionLogPath("Alchemist Bob", "Vendor"));

            // Assert — order is part of the name pattern operators sort/filter by; swapping
            // the two interpolated arguments is a one-character mistake with no compile error.
            StringAssert.StartsWith("chat_session_vendor_alchemist_bob_", fileName,
                "Session log names must be chat_session_<role>_<npcKey>_<timestamp>.log, " +
                "with both parts slugified and the role first.");
        }

        [Test]
        public void SessionLogPath_NullArgs_UsesEmptySentinelAndDoesNotThrow()
        {
            // Act — a session opened before the NPC/persona resolves must still get a path.
            string fileName = Path.GetFileName(ChatPersistencePaths.SessionLogPath(null, null));

            // Assert
            StringAssert.StartsWith("chat_session__empty___empty__", fileName,
                "Null role/key must fall back to the '_empty_' sentinel rather than " +
                "throwing or producing a name with empty segments.");
        }

        [Test]
        public void SessionLogPath_FileName_ContainsNoFilesystemIllegalCharacters()
        {
            // The timestamp is the risk: a default DateTime.ToString() would embed '/' and
            // ':' and make the file unwritable on Windows.
            string fileName = Path.GetFileName(
                ChatPersistencePaths.SessionLogPath("npc:with/bad\\chars", "role with spaces"));

            Assert.AreEqual(-1, fileName.IndexOfAny(Path.GetInvalidFileNameChars()),
                $"Session log file name '{fileName}' contains a character invalid for this " +
                "platform's filesystem.");
            Assert.IsFalse(Regex.IsMatch(fileName, @"[<>:""/\\|?*\s]"),
                $"Session log file name '{fileName}' must be free of every character the " +
                "slug rule considers illegal — including the ':' of a naive timestamp.");
        }

        [Test]
        public void SessionLogPath_TimestampSegment_IsUtcInStableSortableFormat()
        {
            // Arrange
            DateTime before = DateTime.UtcNow;

            // Act
            string stem = Path.GetFileNameWithoutExtension(
                ChatPersistencePaths.SessionLogPath("npc", "role"));

            // Assert — the trailing 19 characters must be yyyy-MM-dd_HH-mm-ss so that a
            // plain alphabetical file listing is also chronological.
            Match m = Regex.Match(stem, @"(\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2})$");
            Assert.IsTrue(m.Success,
                $"Session log stem '{stem}' must end with a yyyy-MM-dd_HH-mm-ss timestamp; " +
                "the format is what makes the logs sort chronologically by name.");

            DateTime parsed;
            Assert.IsTrue(DateTime.TryParseExact(m.Groups[1].Value, "yyyy-MM-dd_HH-mm-ss",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed),
                "The timestamp must parse with the invariant culture — a culture-dependent " +
                "format would produce unreadable names on non-Latin-digit locales.");

            // A local-time regression (DateTime.Now instead of UtcNow) shows up as an
            // hours-sized offset; 10 minutes of slack keeps this from being flaky.
            Assert.Less(Math.Abs((parsed - before).TotalMinutes), 10.0,
                "The timestamp must be UTC and current. A large offset means DateTime.Now " +
                "crept in, which makes log ordering wrong across DST changes and timezones.");
        }

        [Test]
        public void SessionLogPath_DifferentNpcKeys_ProduceDifferentPaths()
        {
            // Two conversations started in the same second must not overwrite each other
            // just because the timestamp granularity is one second.
            string a = ChatPersistencePaths.SessionLogPath("npc-a", "vendor");
            string b = ChatPersistencePaths.SessionLogPath("npc-b", "vendor");

            Assert.AreNotEqual(a, b,
                "Concurrent sessions with different NPCs must not share a log file.");
        }
    }
}
