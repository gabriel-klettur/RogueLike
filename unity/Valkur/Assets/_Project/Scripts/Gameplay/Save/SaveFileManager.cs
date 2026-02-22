using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Save
{
    /// <summary>
    /// Handles all file IO for the save system: read, write, checksum,
    /// backup rotation, directory management, and slot listing.
    /// Pure IO — no game state knowledge.
    /// </summary>
    public static class SaveFileManager
    {
        private const string SAVE_DIR = "Saves";
        private const string SAVE_EXTENSION = ".json";
        private const string CHECKSUM_EXTENSION = ".sha256";
        private const int MAX_BACKUPS = 5;

        public static string GetSaveDirectory()
        {
            return Path.Combine(Application.persistentDataPath, SAVE_DIR);
        }

        public static string GetSavePath(string fileName)
        {
            return Path.Combine(GetSaveDirectory(), fileName + SAVE_EXTENSION);
        }

        public static void EnsureSaveDirectory()
        {
            string dir = GetSaveDirectory();
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                Debug.Log($"[SaveFileManager] Created save directory: {dir}");
            }
        }

        /// <summary>
        /// Write save data to disk with atomic rename and checksum sidecar.
        /// </summary>
        public static void WriteSaveFile(string path, GameSaveData data, string schemaVersion)
        {
            data.schemaVersion = schemaVersion;
            string json = JsonUtility.ToJson(data, true);

            string tempPath = path + ".tmp";
            File.WriteAllText(tempPath, json);

            if (File.Exists(path))
                File.Delete(path);
            File.Move(tempPath, path);

            WriteChecksum(path, json);
        }

        /// <summary>
        /// Try to load and validate a single save file. Returns null on any failure.
        /// </summary>
        public static GameSaveData TryLoadSingle(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;

                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) return null;

                if (!ValidateChecksum(path, json))
                {
                    Debug.LogWarning($"[SaveFileManager] Checksum mismatch for: {path}");
                    return null;
                }

                var data = JsonUtility.FromJson<GameSaveData>(json);
                if (data == null || data.player == null)
                {
                    Debug.LogWarning($"[SaveFileManager] Invalid save structure in: {path}");
                    return null;
                }

                return data;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SaveFileManager] Failed to read {path}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Try primary path, then numbered backups, then shutdown save.
        /// </summary>
        public static GameSaveData TryLoadWithRecovery(string path)
        {
            var data = TryLoadSingle(path);
            if (data != null) return data;

            Debug.LogWarning($"[SaveFileManager] Primary save corrupted or missing: {path}. Attempting backup recovery...");

            for (int i = 0; i < MAX_BACKUPS; i++)
            {
                string backupPath = GetSavePath($"autosave_{i}");
                if (backupPath == path) continue;

                data = TryLoadSingle(backupPath);
                if (data != null)
                {
                    Debug.Log($"[SaveFileManager] Recovered from backup: {backupPath}");
                    return data;
                }
            }

            string shutdownPath = GetSavePath("shutdown_save");
            if (shutdownPath != path)
            {
                data = TryLoadSingle(shutdownPath);
                if (data != null)
                {
                    Debug.Log($"[SaveFileManager] Recovered from shutdown save: {shutdownPath}");
                    return data;
                }
            }

            return null;
        }

        /// <summary>
        /// Rotate numbered backups: N-1 → N, N-2 → N-1, ..., delete oldest.
        /// </summary>
        public static void RotateBackups(string prefix)
        {
            for (int i = MAX_BACKUPS - 1; i >= 0; i--)
            {
                string current = GetSavePath($"{prefix}_{i}");
                if (!File.Exists(current)) continue;

                if (i == MAX_BACKUPS - 1)
                {
                    File.Delete(current);
                }
                else
                {
                    string next = GetSavePath($"{prefix}_{i + 1}");
                    if (File.Exists(next)) File.Delete(next);
                    File.Move(current, next);
                }
            }
        }

        /// <summary>
        /// List all save slots with metadata for UI display.
        /// </summary>
        public static List<SaveSlotInfo> ListSaves()
        {
            var saves = new List<SaveSlotInfo>();
            string dir = GetSaveDirectory();

            if (!Directory.Exists(dir)) return saves;

            foreach (string file in Directory.GetFiles(dir, $"*{SAVE_EXTENSION}"))
            {
                try
                {
                    string json = File.ReadAllText(file);
                    var data = JsonUtility.FromJson<GameSaveData>(json);

                    saves.Add(new SaveSlotInfo
                    {
                        path = file,
                        fileName = Path.GetFileNameWithoutExtension(file),
                        timestamp = data?.timestamp ?? "",
                        schemaVersion = data?.schemaVersion ?? "unknown"
                    });
                }
                catch
                {
                    saves.Add(new SaveSlotInfo
                    {
                        path = file,
                        fileName = Path.GetFileNameWithoutExtension(file),
                        timestamp = "corrupted",
                        schemaVersion = "unknown"
                    });
                }
            }

            saves.Sort((a, b) => string.Compare(b.timestamp, a.timestamp, StringComparison.Ordinal));
            return saves;
        }

        /// <summary>
        /// Delete a save file from disk.
        /// </summary>
        public static bool DeleteSave(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    Debug.Log($"[SaveFileManager] Deleted save: {path}");
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SaveFileManager] Delete failed: {ex.Message}");
                return false;
            }
        }

        // ── Checksum helpers ──

        private static void WriteChecksum(string savePath, string json)
        {
            try
            {
                string hash = ComputeSha256(json);
                string checksumPath = savePath.Replace(SAVE_EXTENSION, CHECKSUM_EXTENSION);
                File.WriteAllText(checksumPath, hash);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SaveFileManager] Failed to write checksum: {ex.Message}");
            }
        }

        private static bool ValidateChecksum(string savePath, string json)
        {
            string checksumPath = savePath.Replace(SAVE_EXTENSION, CHECKSUM_EXTENSION);
            if (!File.Exists(checksumPath))
                return true; // Legacy save without checksum — accept

            try
            {
                string storedHash = File.ReadAllText(checksumPath).Trim();
                string computedHash = ComputeSha256(json);
                return string.Equals(storedHash, computedHash, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return true; // Fail open on checksum read errors
            }
        }

        private static string ComputeSha256(string input)
        {
            using (var sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
                var sb = new StringBuilder(64);
                foreach (byte b in bytes)
                    sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
