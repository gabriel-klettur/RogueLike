using System;
using System.IO;
using UnityEngine;

namespace Valkur.Gameplay.Chat
{
    /// <summary>
    /// How every JSON document the chat subsystem owns is written to and read from disk.
    ///
    /// <para>There are two stores here — <see cref="NPCMemoryStore"/> and
    /// <see cref="ChatJournalStore"/> — and before this file there was one atomic-write
    /// implementation and a second one about to be typed. Duplicated persistence logic is
    /// the shape this project has already paid for: the two copies drift, and the one that
    /// drifts is whichever is exercised least, which for a save path means the one that only
    /// runs when something has already gone wrong.</para>
    ///
    /// <para>THE WRITE. Serialise to a temp, then <c>File.Replace</c> onto the target keeping
    /// the previous content as <c>{path}.bak</c>, or a plain <c>File.Move</c> when there is
    /// nothing to replace. Note what this does and does not buy, because the project has
    /// measured it: a reader never sees a half-written file, and the target IS still
    /// momentarily absent on Mono, whose <c>File.Replace</c> is not Win32 <c>ReplaceFile</c>.
    /// What carries data across that window is the <c>.bak</c>.</para>
    ///
    /// <para>THE TEMP NAME IS PER WRITE, not per path. A fixed <c>{path}.tmp</c> is one
    /// handle shared by every writer of that file, so two overlapping writes open the same
    /// stream and the loser throws <c>Access to the path is denied</c> — the exact defect
    /// <c>WriteSerializedJsonAtomic</c> shipped with. A GUID suffix makes overlap harmless.</para>
    ///
    /// <para>THE READ recovers rather than throwing: a corrupt primary is quarantined to
    /// <c>{path}.corrupt</c> and the <c>.bak</c> is tried in its place, so a torn write costs
    /// at most the last save rather than the whole record.</para>
    /// </summary>
    internal static class ChatJsonFile
    {
        /// <summary>
        /// Serialises <paramref name="payload"/> over <paramref name="path"/>, atomically.
        /// Returns false and logs on failure; the previous content is left intact.
        /// </summary>
        /// <param name="label">
        /// What to call this document in a log line. The stores pass their own name plus the
        /// key, so an error names the record rather than a path under
        /// <c>persistentDataPath</c> that means nothing to whoever reads the console.
        /// </param>
        public static bool WriteAtomic<T>(string path, T payload, string label)
        {
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogError($"[ChatJsonFile] Write for {label} called with no path.");
                return false;
            }

            EnsureDirectory(Path.GetDirectoryName(path));

            // Unique per write. See the class note: a shared temp name is what makes two
            // overlapping writes collide, and writes DO overlap here — a conversation saves
            // its memory record and its journal page from the same frame.
            string tmpPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            string backupPath = path + ".bak";

            try
            {
                File.WriteAllText(tmpPath, JsonUtility.ToJson(payload, prettyPrint: true));

                if (File.Exists(path)) File.Replace(tmpPath, path, backupPath);
                else File.Move(tmpPath, path);

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ChatJsonFile] Save failed for {label}: {ex.Message}");
                SafeDelete(tmpPath);
                return false;
            }
        }

        /// <summary>
        /// Reads <paramref name="path"/>, falling back to its backup when the primary will
        /// not parse. Returns null when neither is readable — including when neither exists,
        /// which is the ordinary "nothing saved yet" case and is deliberately silent.
        /// </summary>
        public static T ReadOrRecover<T>(string path, string label) where T : class
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;

            T primary = TryRead<T>(path);
            if (primary != null) return primary;

            Debug.LogWarning($"[ChatJsonFile] {label} is corrupt. Attempting backup.");
            Quarantine(path);

            string backupPath = path + ".bak";
            if (!File.Exists(backupPath)) return null;

            T recovered = TryRead<T>(backupPath);
            if (recovered != null)
                Debug.LogWarning($"[ChatJsonFile] Recovered {label} from backup.");
            else
                Debug.LogWarning($"[ChatJsonFile] Backup of {label} is corrupt too.");

            return recovered;
        }

        /// <summary>
        /// Parses one file, or returns null. Never throws: <c>JsonUtility.FromJson</c> throws
        /// on malformed input and returns a default instance on an empty string, and both are
        /// "this file is not a record" as far as a caller is concerned.
        /// </summary>
        public static T TryRead<T>(string path) where T : class
        {
            try
            {
                if (!File.Exists(path)) return null;
                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) return null;
                return JsonUtility.FromJson<T>(json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ChatJsonFile] Failed to parse '{path}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Removes a document and every trace of it — backup, quarantine and any temp left
        /// behind by a write that died mid-flight.
        ///
        /// <para>The backup goes too, deliberately. Leaving it would let
        /// <see cref="ReadOrRecover{T}"/> resurrect the record the moment the next write
        /// failed, and a delete that quietly un-deletes itself later is worse than none.</para>
        /// </summary>
        /// <returns>Whether the primary was there to begin with.</returns>
        public static bool Delete(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;

            bool existed = File.Exists(path);

            SafeDelete(path);
            SafeDelete(path + ".bak");
            SafeDelete(path + ".corrupt");

            // Temps carry a GUID, so they cannot be named — sweep the directory for this
            // document's own leftovers instead.
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    string[] stale = Directory.GetFiles(dir, Path.GetFileName(path) + ".*.tmp");
                    for (int i = 0; i < stale.Length; i++) SafeDelete(stale[i]);
                }
            }
            catch { /* best-effort */ }

            return existed;
        }

        /// <summary>Creates <paramref name="directory"/> if it is missing. Safe on null.</summary>
        public static void EnsureDirectory(string directory)
        {
            if (string.IsNullOrEmpty(directory)) return;
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
        }

        /// <summary>
        /// Moves an unparseable file aside so it is not silently thrown away, and so the next
        /// write starts from a clean slate.
        /// </summary>
        private static void Quarantine(string path)
        {
            try
            {
                string corruptPath = path + ".corrupt";
                if (File.Exists(corruptPath)) File.Delete(corruptPath);
                File.Move(path, corruptPath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ChatJsonFile] Could not quarantine '{path}': {ex.Message}");
            }
        }

        private static void SafeDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { /* best-effort */ }
        }
    }
}
