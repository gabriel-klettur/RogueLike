using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.FSM;
using Valkur.Gameplay.Inventory;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Persistent save/load service with JSON serialization, rotative backups,
    /// corruption recovery, checksum validation, and schema migration.
    /// Maps to Python's ShutdownManager + SaveService + WorldManager.save_world().
    /// 
    /// Responsibilities:
    /// - Serialize/deserialize GameSaveData to/from JSON files.
    /// - Manage save slots with timestamps.
    /// - Rotative backups (keeps last N saves).
    /// - Autosave support via tick interval.
    /// - Checksum validation to detect corruption.
    /// - Automatic fallback to backup on corrupted load.
    /// - Schema version migration for forward compatibility.
    /// </summary>
    public class SaveService : MonoBehaviour
    {
        private const string SAVE_DIR = "Saves";
        private const string SAVE_EXTENSION = ".json";
        private const string CHECKSUM_EXTENSION = ".sha256";
        private const string CURRENT_SCHEMA = "1.1";
        private const int MAX_BACKUPS = 5;
        private const string AUTOSAVE_PREFIX = "autosave";
        private const string QUICKSAVE_PREFIX = "quicksave";

        [Header("Autosave")]
        [SerializeField] private bool autosaveEnabled = true;
        [SerializeField] private float autosaveIntervalSeconds = 300f;

        private float _autosaveTimer;
        private string _currentSavePath;

        private static SaveService _instance;
        public static SaveService Instance => _instance;

        public string CurrentSavePath => _currentSavePath;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);

            EnsureSaveDirectory();
        }

        private void Update()
        {
            if (!autosaveEnabled) return;

            _autosaveTimer += Time.unscaledDeltaTime;
            if (_autosaveTimer >= autosaveIntervalSeconds)
            {
                _autosaveTimer = 0f;
                Autosave();
            }
        }

        /// <summary>
        /// Save the current game state to a named slot.
        /// </summary>
        public bool Save(string slotName = null)
        {
            try
            {
                var data = CollectSaveData();
                if (data == null)
                {
                    Debug.LogWarning("[SaveService] No save data collected — nothing to save.");
                    return false;
                }

                string fileName = string.IsNullOrEmpty(slotName)
                    ? $"save_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}"
                    : slotName;

                string path = GetSavePath(fileName);
                WriteSaveFile(path, data);
                _currentSavePath = path;

                Debug.Log($"[SaveService] Game saved to: {path}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SaveService] Save failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Quick save to a dedicated quicksave slot.
        /// </summary>
        public bool QuickSave()
        {
            return Save(QUICKSAVE_PREFIX);
        }

        /// <summary>
        /// Autosave with rotative backup.
        /// </summary>
        public void Autosave()
        {
            try
            {
                var data = CollectSaveData();
                if (data == null) return;

                RotateBackups(AUTOSAVE_PREFIX);

                string path = GetSavePath($"{AUTOSAVE_PREFIX}_0");
                WriteSaveFile(path, data);

                Debug.Log($"[SaveService] Autosave completed: {path}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SaveService] Autosave failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Load a game from a specific file path.
        /// Validates checksum and falls back to backups on corruption.
        /// </summary>
        public bool Load(string path)
        {
            var data = TryLoadWithRecovery(path);
            if (data == null)
            {
                Debug.LogError($"[SaveService] Load failed — no valid save found for: {path}");
                return false;
            }

            data = MigrateSchema(data);
            ApplySaveData(data);
            _currentSavePath = path;

            Debug.Log($"[SaveService] Game loaded from: {path} (schema {data.schemaVersion})");
            return true;
        }

        /// <summary>
        /// Try to load from the given path. If corrupted, attempt backup recovery.
        /// </summary>
        private GameSaveData TryLoadWithRecovery(string path)
        {
            // Try primary file
            var data = TryLoadSingle(path);
            if (data != null) return data;

            Debug.LogWarning($"[SaveService] Primary save corrupted or missing: {path}. Attempting backup recovery...");

            // Try numbered backups (autosave_0 through autosave_N)
            string fileName = Path.GetFileNameWithoutExtension(path);
            for (int i = 0; i < MAX_BACKUPS; i++)
            {
                string backupPath = GetSavePath($"{AUTOSAVE_PREFIX}_{i}");
                if (backupPath == path) continue;

                data = TryLoadSingle(backupPath);
                if (data != null)
                {
                    Debug.Log($"[SaveService] Recovered from backup: {backupPath}");
                    return data;
                }
            }

            // Try shutdown save as last resort
            string shutdownPath = GetSavePath("shutdown_save");
            if (shutdownPath != path)
            {
                data = TryLoadSingle(shutdownPath);
                if (data != null)
                {
                    Debug.Log($"[SaveService] Recovered from shutdown save: {shutdownPath}");
                    return data;
                }
            }

            return null;
        }

        /// <summary>
        /// Try to load and validate a single save file. Returns null on any failure.
        /// </summary>
        private GameSaveData TryLoadSingle(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;

                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) return null;

                // Checksum validation
                if (!ValidateChecksum(path, json))
                {
                    Debug.LogWarning($"[SaveService] Checksum mismatch for: {path}");
                    return null;
                }

                var data = JsonUtility.FromJson<GameSaveData>(json);
                if (data == null || data.player == null)
                {
                    Debug.LogWarning($"[SaveService] Invalid save structure in: {path}");
                    return null;
                }

                return data;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SaveService] Failed to read {path}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Quick load from the quicksave slot.
        /// </summary>
        public bool QuickLoad()
        {
            string path = GetSavePath(QUICKSAVE_PREFIX);
            return Load(path);
        }

        /// <summary>
        /// List all available save files with metadata.
        /// </summary>
        public List<SaveSlotInfo> ListSaves()
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
        /// Delete a save file.
        /// </summary>
        public bool DeleteSave(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    Debug.Log($"[SaveService] Deleted save: {path}");
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SaveService] Delete failed: {ex.Message}");
                return false;
            }
        }

        private GameSaveData CollectSaveData()
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player == null) return null;

            var data = new GameSaveData
            {
                timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss")
            };

            // Player state
            var health = player.GetComponent<Health>();
            var mana = player.GetComponent<Mana>();
            var experience = player.GetComponent<Experience>();
            var inventory = player.GetComponent<Inventory.Inventory>();

            data.player = new PlayerSaveData
            {
                position = (Vector2)player.transform.position,
                hp = health != null ? health.CurrentHp : 0,
                maxHp = health != null ? health.MaxHp : 0,
                mana = mana != null ? mana.CurrentMana : 0,
                maxMana = mana != null ? mana.MaxMana : 0,
                currentZone = "",
                experience = experience != null ? experience.TotalXp : 0,
                level = experience != null ? experience.Level : 1
            };

            // Inventory
            if (inventory != null)
            {
                data.player.inventory = inventory.ToSaveData("player");
            }

            // NPC memory
            data.npcMemory = CollectNpcMemory();

            return data;
        }

        private List<NpcMemoryEntry> CollectNpcMemory()
        {
            var memory = new List<NpcMemoryEntry>();
            var monsters = GameObject.FindGameObjectsWithTag("Monster");

            foreach (var monster in monsters)
            {
                var health = monster.GetComponent<Health>();
                if (health == null) continue;

                var brain = monster.GetComponent<FSMMonsterBrain>();
                string fsmState = brain != null ? brain.CurrentStateName : "";

                memory.Add(new NpcMemoryEntry
                {
                    entityId = monster.GetInstanceID().ToString(),
                    monsterKey = monster.name,
                    position = (Vector2)monster.transform.position,
                    hp = health.CurrentHp,
                    fsmState = fsmState,
                    zone = ""
                });
            }

            return memory;
        }

        private void ApplySaveData(GameSaveData data)
        {
            if (data.player == null) return;

            var player = GameObject.FindGameObjectWithTag("Player");
            if (player == null)
            {
                Debug.LogWarning("[SaveService] No player found to restore state.");
                return;
            }

            // Restore position
            player.transform.position = new Vector3(
                data.player.position.x,
                data.player.position.y,
                0f);

            // Restore health
            var health = player.GetComponent<Health>();
            if (health != null)
            {
                health.Initialize(data.player.maxHp);
                int damage = data.player.maxHp - data.player.hp;
                if (damage > 0)
                    health.TakeDamage(damage);
            }

            // Restore mana
            var mana = player.GetComponent<Mana>();
            if (mana != null && data.player.maxMana > 0)
            {
                mana.Initialize(Mathf.RoundToInt(data.player.maxMana), 2f);
                int manaToConsume = Mathf.RoundToInt(data.player.maxMana - data.player.mana);
                if (manaToConsume > 0)
                    mana.TryConsume(manaToConsume);
            }

            // Restore experience
            var experience = player.GetComponent<Experience>();
            if (experience != null)
            {
                experience.Initialize(data.player.experience, data.player.level);
            }

            // Restore inventory
            if (data.player.inventory != null)
            {
                var inventory = player.GetComponent<Inventory.Inventory>();
                if (inventory != null)
                {
                    inventory.Clear();
                    inventory.Initialize(data.player.inventory.capacity);
                    // Item restoration requires ItemDefinition lookup — deferred to catalog system
                    Debug.Log($"[SaveService] Inventory structure restored. Slots: {data.player.inventory.slots.Count}");
                }
            }

            Debug.Log($"[SaveService] Player state restored: pos={data.player.position}, HP={data.player.hp}/{data.player.maxHp}, Mana={data.player.mana}/{data.player.maxMana}, XP={data.player.experience}, Lv={data.player.level}");
        }

        private void RotateBackups(string prefix)
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

        private void WriteSaveFile(string path, GameSaveData data)
        {
            data.schemaVersion = CURRENT_SCHEMA;
            string json = JsonUtility.ToJson(data, true);

            // Write to temp file first, then atomic rename for crash safety
            string tempPath = path + ".tmp";
            File.WriteAllText(tempPath, json);

            if (File.Exists(path))
                File.Delete(path);
            File.Move(tempPath, path);

            // Write checksum sidecar
            WriteChecksum(path, json);
        }

        private void WriteChecksum(string savePath, string json)
        {
            try
            {
                string hash = ComputeSha256(json);
                string checksumPath = savePath.Replace(SAVE_EXTENSION, CHECKSUM_EXTENSION);
                File.WriteAllText(checksumPath, hash);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SaveService] Failed to write checksum: {ex.Message}");
            }
        }

        private bool ValidateChecksum(string savePath, string json)
        {
            string checksumPath = savePath.Replace(SAVE_EXTENSION, CHECKSUM_EXTENSION);
            if (!File.Exists(checksumPath))
            {
                // No checksum file = legacy save, accept it
                return true;
            }

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

        /// <summary>
        /// Migrate save data from older schema versions to current.
        /// </summary>
        private GameSaveData MigrateSchema(GameSaveData data)
        {
            if (data.schemaVersion == CURRENT_SCHEMA)
                return data;

            string from = data.schemaVersion ?? "unknown";

            // v1.0 -> v1.1: added mana and experience fields (already have defaults)
            if (from == "1.0")
            {
                // No structural changes needed — new fields default to 0
                data.schemaVersion = CURRENT_SCHEMA;
                Debug.Log($"[SaveService] Migrated save from v1.0 to v{CURRENT_SCHEMA}");
                return data;
            }

            // Unknown version — accept as-is with warning
            Debug.LogWarning($"[SaveService] Unknown schema version '{from}'. Loading as-is.");
            data.schemaVersion = CURRENT_SCHEMA;
            return data;
        }

        private string GetSaveDirectory()
        {
            return Path.Combine(Application.persistentDataPath, SAVE_DIR);
        }

        private string GetSavePath(string fileName)
        {
            return Path.Combine(GetSaveDirectory(), fileName + SAVE_EXTENSION);
        }

        private void EnsureSaveDirectory()
        {
            string dir = GetSaveDirectory();
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                Debug.Log($"[SaveService] Created save directory: {dir}");
            }
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                Debug.Log("[SaveService] Application paused — triggering autosave.");
                Autosave();
            }
        }

        private void OnApplicationQuit()
        {
            Debug.Log("[SaveService] Application quitting — triggering shutdown save.");
            Save("shutdown_save");
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }
    }

    /// <summary>
    /// Lightweight info about a save slot for UI display.
    /// </summary>
    [Serializable]
    public struct SaveSlotInfo
    {
        public string path;
        public string fileName;
        public string timestamp;
        public string schemaVersion;
    }
}
