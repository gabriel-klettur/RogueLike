using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Valkur.Gameplay.MapEditor
{
    /// <summary>
    /// File-IO layer behind the Map Editor "Maps" explorer. Each slot is a
    /// self-contained <see cref="ZonePersistenceFile"/> snapshot living under
    /// <c>Application.persistentDataPath/Maps/&lt;slug&gt;.zones.json</c>. The
    /// active slot name is mirrored in <c>Maps/_active.txt</c> so it survives
    /// editor restarts.
    ///
    /// This class is intentionally agnostic of the live ZoneManager — it only
    /// shuttles raw JSON. The manager-level partial is responsible for
    /// translating that JSON into a <see cref="Valkur.Gameplay.World.ZoneManager"/>
    /// state change.
    /// </summary>
    internal class MapEditorMapSlots
    {
        private const string DIR_NAME      = "Maps";
        private const string SLOT_EXT      = ".zones.json";
        private const string ACTIVE_FILE   = "_active.txt";
        public  const string DEFAULT_SLOT  = "default";

        private string _activeSlot = DEFAULT_SLOT;

        public string ActiveSlot => _activeSlot;
        public string Directory  => Path.Combine(Application.persistentDataPath, DIR_NAME);

        public MapEditorMapSlots()
        {
            EnsureDirectory();
            LoadActiveFromDisk();
        }

        // ── Listing ──────────────────────────────────────────────────────────────

        public List<string> ListSlots()
        {
            var list = new List<string>();
            try
            {
                if (System.IO.Directory.Exists(Directory))
                {
                    foreach (var path in System.IO.Directory.GetFiles(Directory, "*" + SLOT_EXT))
                    {
                        var name = Path.GetFileName(path);
                        if (name.EndsWith(SLOT_EXT, StringComparison.OrdinalIgnoreCase))
                            list.Add(name.Substring(0, name.Length - SLOT_EXT.Length));
                    }
                }

                // The "default" slot is the implicit blank baseline. Surface it in
                // the explorer even when no file exists yet, so the user can always
                // pick it after creating or loading another map.
                bool hasDefault = false;
                for (int i = 0; i < list.Count; i++)
                {
                    if (string.Equals(list[i], DEFAULT_SLOT, StringComparison.OrdinalIgnoreCase))
                    { hasDefault = true; break; }
                }
                if (!hasDefault) list.Add(DEFAULT_SLOT);

                list.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MapEditor.Slots] Failed to list slots: {ex.Message}");
            }
            return list;
        }

        public bool Exists(string slot)
        {
            string clean = Sanitize(slot);
            if (string.IsNullOrEmpty(clean)) return false;
            return File.Exists(SlotPath(clean));
        }

        // ── Read / Write ─────────────────────────────────────────────────────────

        public string ReadSlot(string slot)
        {
            string clean = Sanitize(slot);
            if (string.IsNullOrEmpty(clean)) return null;
            try
            {
                string path = SlotPath(clean);
                if (!File.Exists(path)) return null;
                return File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MapEditor.Slots] Failed to read '{slot}': {ex.Message}");
                return null;
            }
        }

        public bool WriteSlot(string slot, string json)
        {
            string clean = Sanitize(slot);
            if (string.IsNullOrEmpty(clean) || json == null) return false;
            try
            {
                EnsureDirectory();
                File.WriteAllText(SlotPath(clean), json);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MapEditor.Slots] Failed to write '{slot}': {ex.Message}");
                return false;
            }
        }

        public bool DeleteSlot(string slot)
        {
            string clean = Sanitize(slot);
            if (string.IsNullOrEmpty(clean)) return false;
            try
            {
                string path = SlotPath(clean);
                if (!File.Exists(path)) return false;
                File.Delete(path);
                if (string.Equals(_activeSlot, clean, StringComparison.OrdinalIgnoreCase))
                    SetActive(DEFAULT_SLOT);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MapEditor.Slots] Failed to delete '{slot}': {ex.Message}");
                return false;
            }
        }

        public bool RenameSlot(string oldName, string newName)
        {
            string srcClean = Sanitize(oldName);
            string dstClean = Sanitize(newName);
            if (string.IsNullOrEmpty(srcClean) || string.IsNullOrEmpty(dstClean)) return false;
            if (string.Equals(srcClean, dstClean, StringComparison.OrdinalIgnoreCase)) return false;
            try
            {
                string srcPath = SlotPath(srcClean);
                string dstPath = SlotPath(dstClean);
                if (!File.Exists(srcPath)) return false;
                if (File.Exists(dstPath)) return false;
                File.Move(srcPath, dstPath);
                if (string.Equals(_activeSlot, srcClean, StringComparison.OrdinalIgnoreCase))
                    SetActive(dstClean);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MapEditor.Slots] Failed to rename '{oldName}' → '{newName}': {ex.Message}");
                return false;
            }
        }

        // ── Active slot tracking ────────────────────────────────────────────────

        public void SetActive(string slot)
        {
            string clean = Sanitize(slot);
            if (string.IsNullOrEmpty(clean)) return;
            _activeSlot = clean;
            try
            {
                EnsureDirectory();
                File.WriteAllText(Path.Combine(Directory, ACTIVE_FILE), clean);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MapEditor.Slots] Failed to persist active slot: {ex.Message}");
            }
        }

        private void LoadActiveFromDisk()
        {
            try
            {
                string path = Path.Combine(Directory, ACTIVE_FILE);
                if (!File.Exists(path)) return;
                string raw = File.ReadAllText(path)?.Trim();
                if (!string.IsNullOrEmpty(raw))
                    _activeSlot = Sanitize(raw);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MapEditor.Slots] Failed to read active slot: {ex.Message}");
            }
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        private void EnsureDirectory()
        {
            try
            {
                if (!System.IO.Directory.Exists(Directory))
                    System.IO.Directory.CreateDirectory(Directory);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MapEditor.Slots] Failed to create directory: {ex.Message}");
            }
        }

        private string SlotPath(string cleanSlot)
            => Path.Combine(Directory, cleanSlot + SLOT_EXT);

        /// <summary>
        /// Strip path-unsafe characters and collapse to a safe slug. Returns
        /// the empty string if nothing usable remains. Names that start with
        /// an underscore are reserved for internal files (e.g. _active.txt).
        /// </summary>
        public static string Sanitize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            var sb = new System.Text.StringBuilder(raw.Length);
            foreach (var ch in raw.Trim())
            {
                if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' || ch == ' ')
                    sb.Append(ch);
            }
            string clean = sb.ToString().Trim().Replace("  ", " ");
            if (clean.StartsWith("_")) clean = clean.TrimStart('_');
            return clean;
        }
    }
}
