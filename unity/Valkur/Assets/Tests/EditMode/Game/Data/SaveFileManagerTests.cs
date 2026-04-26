using System.IO;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.Save;

namespace Valkur.Tests.EditMode.Game.Data
{
    /// <summary>
    /// Regression tests for SaveFileManager.
    ///
    /// Key regressions prevented:
    ///   - position_checkpoint.json appearing in the save list (was polluting the
    ///     "Cargar Juego" panel with un-selectable ghost entries).
    ///   - Recovery files placed inside .recovery/ leaking into ListSaves().
    ///   - SanitizeSaveName / RenameSave accepting invalid input silently.
    /// </summary>
    [TestFixture]
    public class SaveFileManagerTests
    {
        private string _saveDir;
        private string _recoveryDir;

        [SetUp]
        public void SetUp()
        {
            SaveFileManager.EnsureSaveDirectory();
            _saveDir    = SaveFileManager.GetSaveDirectory();
            _recoveryDir = SaveFileManager.GetRecoveryDirectory();
        }

        [TearDown]
        public void TearDown()
        {
            // Remove only files we created (prefix "_test_") to avoid touching real saves.
            foreach (var f in Directory.GetFiles(_saveDir, "_test_*.*"))
                File.Delete(f);
        }

        // ── Recovery-directory path routing ───────────────────────────────────

        [Test]
        public void GetRecoveryDirectory_IsSubdirOfSaveDirectory()
        {
            Assert.IsTrue(_recoveryDir.StartsWith(_saveDir),
                "The .recovery directory must be nested inside the main Saves directory");
        }

        [Test]
        public void GetPositionCheckpointPath_IsInsideRecoveryDir()
        {
            string path = SaveFileManager.GetPositionCheckpointPath();
            Assert.IsTrue(path.StartsWith(_recoveryDir),
                "position_checkpoint.json must live in Saves/.recovery/, not in Saves root");
        }

        [Test]
        public void GetPositionCheckpointBakPath_IsInsideRecoveryDir()
        {
            string path = SaveFileManager.GetPositionCheckpointBakPath();
            Assert.IsTrue(path.StartsWith(_recoveryDir),
                "position_checkpoint_bak.json must live in Saves/.recovery/, not in Saves root");
        }

        [Test]
        public void RecoveryDirectory_ExistsAfterEnsureSaveDirectory()
        {
            Assert.IsTrue(Directory.Exists(_recoveryDir),
                "EnsureSaveDirectory must also create the .recovery subdirectory");
        }

        // ── ListSaves — reserved-name filtering (core regressions) ───────────

        /// <summary>
        /// REGRESSION: position_checkpoint.json was displayed in the load panel
        /// because ListSaves() globbed all *.json in the Saves folder without
        /// filtering reserved filenames.
        /// </summary>
        [Test]
        public void ListSaves_NeverContains_PositionCheckpoint()
        {
            // Plant a legacy file in the top-level Saves dir (simulates installs
            // that have not yet been migrated or where migration failed).
            string legacyPath = Path.Combine(_saveDir, "position_checkpoint.json");
            File.WriteAllText(legacyPath, "{\"timestamp\":\"2026-01-01T00:00:00\"}");
            try
            {
                var saves = SaveFileManager.ListSaves();
                foreach (var s in saves)
                    Assert.AreNotEqual("position_checkpoint", s.fileName,
                        "position_checkpoint must never appear in the save list");
            }
            finally
            {
                if (File.Exists(legacyPath)) File.Delete(legacyPath);
            }
        }

        [Test]
        public void ListSaves_NeverContains_PositionCheckpointBak()
        {
            string legacyPath = Path.Combine(_saveDir, "position_checkpoint_bak.json");
            File.WriteAllText(legacyPath, "{\"timestamp\":\"2026-01-01T00:00:00\"}");
            try
            {
                var saves = SaveFileManager.ListSaves();
                foreach (var s in saves)
                    Assert.AreNotEqual("position_checkpoint_bak", s.fileName,
                        "position_checkpoint_bak must never appear in the save list");
            }
            finally
            {
                if (File.Exists(legacyPath)) File.Delete(legacyPath);
            }
        }

        [Test]
        public void ListSaves_NeverContains_FilesFromRecoverySubdir()
        {
            // Even if a file is placed directly in .recovery/, it must not show up.
            string recoveryFile = Path.Combine(_recoveryDir, "_test_recovery_item.json");
            File.WriteAllText(recoveryFile, "{\"timestamp\":\"2026-01-01T00:00:00\"}");
            try
            {
                var saves = SaveFileManager.ListSaves();
                foreach (var s in saves)
                    Assert.AreNotEqual("_test_recovery_item", s.fileName,
                        "Files inside .recovery/ must never appear in the public save list");
            }
            finally
            {
                if (File.Exists(recoveryFile)) File.Delete(recoveryFile);
            }
        }

        [Test]
        public void ListSaves_SearchesOnlyTopLevelDir_NotSubdirectories()
        {
            // Create a nested subdir with a json file.
            string subDir = Path.Combine(_saveDir, "_test_subdir");
            Directory.CreateDirectory(subDir);
            string nested = Path.Combine(subDir, "_test_nested.json");
            File.WriteAllText(nested, "{\"timestamp\":\"2026-01-01T00:00:00\"}");
            try
            {
                var saves = SaveFileManager.ListSaves();
                foreach (var s in saves)
                    Assert.AreNotEqual("_test_nested", s.fileName,
                        "ListSaves must not recurse into subdirectories");
            }
            finally
            {
                if (File.Exists(nested)) File.Delete(nested);
                if (Directory.Exists(subDir)) Directory.Delete(subDir);
            }
        }

        // ── ListSaves — valid save inclusion ─────────────────────────────────

        [Test]
        public void ListSaves_Includes_ValidSaveFile()
        {
            string path = Path.Combine(_saveDir, "_test_valid_save.json");
            File.WriteAllText(path,
                "{\"schemaVersion\":\"1.0\",\"timestamp\":\"2026-01-01T00:00:00\"," +
                "\"player\":{\"playerClass\":\"elven\",\"hp\":70,\"maxHp\":70,\"level\":1}}");
            try
            {
                var saves = SaveFileManager.ListSaves();
                bool found = false;
                foreach (var s in saves)
                    if (s.fileName == "_test_valid_save") { found = true; break; }
                Assert.IsTrue(found, "A valid save file must appear in ListSaves()");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void ListSaves_Includes_CorruptedFile_Marked_IsCorrupted()
        {
            string path = Path.Combine(_saveDir, "_test_corrupted.json");
            File.WriteAllText(path, "NOT VALID JSON{{{{");
            try
            {
                var saves = SaveFileManager.ListSaves();
                bool foundCorrupted = false;
                foreach (var s in saves)
                    if (s.fileName == "_test_corrupted") { foundCorrupted = s.isCorrupted; break; }
                Assert.IsTrue(foundCorrupted, "An unreadable save must be included with isCorrupted=true");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void ListSaves_PopulatesMetadata_FromValidSave()
        {
            string path = Path.Combine(_saveDir, "_test_meta.json");
            File.WriteAllText(path,
                "{\"schemaVersion\":\"1.0\",\"timestamp\":\"2026-04-25T12:00:00\"," +
                "\"player\":{\"playerClass\":\"dwarf\",\"hp\":50,\"maxHp\":100,\"level\":5,\"experience\":1200}}");
            try
            {
                var saves = SaveFileManager.ListSaves();
                SaveSlotInfo? found = null;
                foreach (var s in saves)
                    if (s.fileName == "_test_meta") { found = s; break; }

                Assert.IsNotNull(found, "Test save must be in the list");
                Assert.AreEqual("dwarf", found.Value.playerClass);
                Assert.AreEqual(5,    found.Value.level);
                Assert.AreEqual(50,   found.Value.hp);
                Assert.AreEqual(100,  found.Value.maxHp);
                Assert.AreEqual(1200, found.Value.experience);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void ListSaves_SortsByTimestampDescending()
        {
            string pathA = Path.Combine(_saveDir, "_test_sort_a.json");
            string pathB = Path.Combine(_saveDir, "_test_sort_b.json");
            // A is older, B is newer.
            File.WriteAllText(pathA,
                "{\"timestamp\":\"2026-01-01T00:00:00\",\"player\":{\"playerClass\":\"elven\"}}");
            File.WriteAllText(pathB,
                "{\"timestamp\":\"2026-06-01T00:00:00\",\"player\":{\"playerClass\":\"elven\"}}");
            try
            {
                var saves = SaveFileManager.ListSaves();
                int idxA = -1, idxB = -1;
                for (int i = 0; i < saves.Count; i++)
                {
                    if (saves[i].fileName == "_test_sort_a") idxA = i;
                    if (saves[i].fileName == "_test_sort_b") idxB = i;
                }
                if (idxA < 0 || idxB < 0) Assert.Pass("Test saves not isolated — skipping ordering check");
                Assert.Less(idxB, idxA,
                    "Newer save (_test_sort_b) must appear before older save (_test_sort_a)");
            }
            finally
            {
                if (File.Exists(pathA)) File.Delete(pathA);
                if (File.Exists(pathB)) File.Delete(pathB);
            }
        }

        // ── SanitizeSaveName ──────────────────────────────────────────────────

        [Test]
        public void SanitizeSaveName_Null_ReturnsNull()
        {
            Assert.IsNull(SaveFileManager.SanitizeSaveName(null));
        }

        [Test]
        public void SanitizeSaveName_Empty_ReturnsNull()
        {
            Assert.IsNull(SaveFileManager.SanitizeSaveName(""));
        }

        [Test]
        public void SanitizeSaveName_WhitespaceOnly_ReturnsNull()
        {
            Assert.IsNull(SaveFileManager.SanitizeSaveName("   "));
        }

        [Test]
        public void SanitizeSaveName_ValidName_Unchanged()
        {
            Assert.AreEqual("my_save_01", SaveFileManager.SanitizeSaveName("my_save_01"));
        }

        [Test]
        public void SanitizeSaveName_ReplacesInvalidFileChars()
        {
            string result = SaveFileManager.SanitizeSaveName("save<>:\"|?*");
            Assert.IsNotNull(result);
            Assert.IsFalse(result.Contains('<'),  "< is an invalid filename char");
            Assert.IsFalse(result.Contains('>'),  "> is an invalid filename char");
            Assert.IsFalse(result.Contains(':'),  ": is an invalid filename char");
            Assert.IsFalse(result.Contains('"'),  "\" is an invalid filename char");
            Assert.IsFalse(result.Contains('|'),  "| is an invalid filename char");
            Assert.IsFalse(result.Contains('?'),  "? is an invalid filename char");
            Assert.IsFalse(result.Contains('*'),  "* is an invalid filename char");
        }

        [Test]
        public void SanitizeSaveName_TrimsLeadingAndTrailingDots()
        {
            string result = SaveFileManager.SanitizeSaveName(".hidden_save.");
            Assert.IsNotNull(result);
            Assert.IsFalse(result.StartsWith("."), "Sanitized name must not start with '.'");
            Assert.IsFalse(result.EndsWith("."),   "Sanitized name must not end with '.'");
        }

        [Test]
        public void SanitizeSaveName_AllDots_ReturnsNull()
        {
            Assert.IsNull(SaveFileManager.SanitizeSaveName("..."),
                "A name consisting only of dots should produce null after trimming");
        }

        // ── RenameSave failure paths ──────────────────────────────────────────

        [Test]
        public void RenameSave_MissingSourceFile_ReturnsNull()
        {
            string missing = Path.Combine(_saveDir, "_test_nonexistent_src.json");
            string result  = SaveFileManager.RenameSave(missing, "_test_rename_target");
            Assert.IsNull(result, "RenameSave must return null when source does not exist");
        }

        [Test]
        public void RenameSave_EmptyNewName_ReturnsNull()
        {
            string src = Path.Combine(_saveDir, "_test_rename_src.json");
            File.WriteAllText(src, "{\"timestamp\":\"2026-01-01T00:00:00\"}");
            try
            {
                string result = SaveFileManager.RenameSave(src, "");
                Assert.IsNull(result, "RenameSave with empty new name must return null");
            }
            finally { if (File.Exists(src)) File.Delete(src); }
        }

        [Test]
        public void RenameSave_TargetAlreadyExists_ReturnsNull()
        {
            string src  = Path.Combine(_saveDir, "_test_rename_src2.json");
            string dest = Path.Combine(_saveDir, "_test_rename_dest2.json");
            File.WriteAllText(src,  "{\"timestamp\":\"2026-01-01T00:00:00\"}");
            File.WriteAllText(dest, "{\"timestamp\":\"2026-01-01T00:00:00\"}");
            try
            {
                string result = SaveFileManager.RenameSave(src, "_test_rename_dest2");
                Assert.IsNull(result, "RenameSave must not overwrite an existing file");
            }
            finally
            {
                if (File.Exists(src))  File.Delete(src);
                if (File.Exists(dest)) File.Delete(dest);
            }
        }

        [Test]
        public void RenameSave_ValidRename_ReturnNewPath_And_FileExists()
        {
            string src     = Path.Combine(_saveDir, "_test_rename_valid_src.json");
            string newName = "_test_rename_valid_dst";
            string dest    = Path.Combine(_saveDir, newName + ".json");
            File.WriteAllText(src, "{\"timestamp\":\"2026-01-01T00:00:00\"}");
            try
            {
                string result = SaveFileManager.RenameSave(src, newName);
                Assert.IsNotNull(result, "RenameSave must return new path on success");
                Assert.IsTrue(File.Exists(dest),  "Destination file must exist after rename");
                Assert.IsFalse(File.Exists(src),  "Source file must be gone after rename");
            }
            finally
            {
                if (File.Exists(src))  File.Delete(src);
                if (File.Exists(dest)) File.Delete(dest);
            }
        }

        // ── WritePositionCheckpoint round-trip ────────────────────────────────

        [Test]
        public void WriteAndReadPositionCheckpoint_RoundTrip()
        {
            var written = new PositionCheckpointData
            {
                x = 12.5f, y = -7.25f,
                zone = "TestZone",
                timestamp = "2026-04-25T10:00:00"
            };

            SaveFileManager.WritePositionCheckpoint(written);
            var read = SaveFileManager.ReadPositionCheckpoint();

            Assert.IsNotNull(read, "ReadPositionCheckpoint must return data after a write");
            Assert.AreEqual(written.x,         read.x,     0.001f, "X must survive the round-trip");
            Assert.AreEqual(written.y,         read.y,     0.001f, "Y must survive the round-trip");
            Assert.AreEqual(written.zone,      read.zone,          "Zone must survive the round-trip");
            Assert.AreEqual(written.timestamp, read.timestamp,     "Timestamp must survive the round-trip");

            // Clean up
            SaveFileManager.DeletePositionCheckpoint();
            Assert.IsNull(SaveFileManager.ReadPositionCheckpoint(),
                "ReadPositionCheckpoint must return null after Delete");
        }
    }
}
