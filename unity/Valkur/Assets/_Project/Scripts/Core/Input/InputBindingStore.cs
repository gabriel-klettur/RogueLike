using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Valkur.Core.Input
{
    /// <summary>
    /// Persists what the player changed about their controls: the InputSystem binding
    /// overrides, and the per-action stance masks from <see cref="InputContextPolicy"/>.
    ///
    /// <para>WHY THIS EXISTS AT ALL. Before it, this project had TWO binding models that did
    /// not talk to each other — the <c>.inputactions</c> asset that gameplay actually reads,
    /// and a wall of <c>GameSettings.*KeyA</c> strings the Controls panel wrote. Only twelve
    /// editor toggles were ever bridged between them (<see cref="EditorBindingsApplier"/>,
    /// slot 0 only); every gameplay field — <c>moveUpKeyA</c>, <c>dashKeyA</c>,
    /// <c>spell1KeyA</c>..<c>spell4KeyA</c>, <c>primaryAttackMouse</c> — had ZERO readers in
    /// production and was measured as such. The panel let a player rebind their movement and
    /// changed nothing at all. There is one model now, the asset, and this is its file.</para>
    ///
    /// <para>THE WRITE IS ATOMIC WITH A GUID TEMP, not a fixed <c>.tmp</c> name. CLAUDE.md
    /// records why: a shared temp name is neither atomic nor safe, because two overlapping
    /// writers open the same handle and the loser throws. It buys one specific thing — a
    /// reader never sees a half-written file — and it does not pretend to be more than
    /// that.</para>
    /// </summary>
    public static class InputBindingStore
    {
        public const int SchemaVersion = 1;

        private const string FolderName = "Input";
        private const string FileName   = "controls.json";

        /// <summary>Raised after a successful <see cref="Save"/> or <see cref="Apply"/>, so the
        /// Controls editor can redraw from the file rather than from what it thinks it
        /// wrote.</summary>
        public static event Action OnApplied;

        public static string Directory =>
            Path.Combine(Application.persistentDataPath, FolderName);

        public static string FilePath => Path.Combine(Directory, FileName);

        public static bool Exists()
        {
            try { return File.Exists(FilePath); }
            catch { return false; }
        }

        // ── Document ─────────────────────────────────────────────────────────

        [Serializable]
        private sealed class StanceEntry
        {
            public string actionId;
            public int mask;
        }

        [Serializable]
        private sealed class Document
        {
            public int schemaVersion = SchemaVersion;
            /// <summary>Opaque payload from
            /// <see cref="InputActionAsset.SaveBindingOverridesAsJson()"/>. Kept as a STRING
            /// rather than re-modelled: the InputSystem owns that shape and re-modelling it
            /// here would be a second parser to keep in step with a package upgrade.</summary>
            public string bindingOverrides = "";
            public List<StanceEntry> stances = new List<StanceEntry>();
        }

        // ── Save ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Writes the live overrides to disk. Safe to call on every rebind — the file is small
        /// and the write is one swap.
        /// </summary>
        public static bool Save()
        {
            var svc = InputService.Instance;
            if (svc?.Asset == null)
            {
                Debug.LogWarning("[InputBindingStore] InputService not ready — nothing saved.");
                return false;
            }

            var doc = new Document
            {
                schemaVersion    = SchemaVersion,
                bindingOverrides = svc.Asset.SaveBindingOverridesAsJson() ?? "",
                stances          = new List<StanceEntry>(),
            };

            foreach (var kv in InputContextPolicy.SnapshotOverrides())
                doc.stances.Add(new StanceEntry { actionId = kv.Key, mask = (int)kv.Value });

            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                WriteAtomic(FilePath, JsonUtility.ToJson(doc, true));
                OnApplied?.Invoke();
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[InputBindingStore] Save failed: {ex.GetType().Name} :: {ex.Message}");
                return false;
            }
        }

        // ── Load ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Reads the file and applies it to the live asset and policy. Silent and harmless
        /// when no file exists — a fresh install has no overrides, which is not a problem to
        /// report.
        /// </summary>
        public static bool Apply()
        {
            var svc = InputService.Instance;
            if (svc?.Asset == null) return false;
            if (!Exists()) return false;

            Document doc;
            try
            {
                doc = JsonUtility.FromJson<Document>(File.ReadAllText(FilePath));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[InputBindingStore] Could not read '{FilePath}' " +
                                 $"({ex.GetType().Name}: {ex.Message}) — keeping shipped bindings.");
                return false;
            }

            if (doc == null) return false;

            if (doc.schemaVersion > SchemaVersion)
            {
                Debug.LogWarning($"[InputBindingStore] '{FilePath}' is schema v{doc.schemaVersion}, " +
                                 $"this build reads v{SchemaVersion} — keeping shipped bindings so a " +
                                 "newer profile is not silently downgraded on the next save.");
                return false;
            }

            try
            {
                if (!string.IsNullOrEmpty(doc.bindingOverrides))
                    svc.Asset.LoadBindingOverridesFromJson(doc.bindingOverrides);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[InputBindingStore] Binding overrides rejected " +
                                 $"({ex.GetType().Name}: {ex.Message}) — shipped paths kept.");
            }

            var entries = new List<KeyValuePair<string, InputContextMask>>(
                doc.stances != null ? doc.stances.Count : 0);
            if (doc.stances != null)
                foreach (var e in doc.stances)
                    if (!string.IsNullOrEmpty(e?.actionId))
                        entries.Add(new KeyValuePair<string, InputContextMask>(e.actionId, (InputContextMask)e.mask));

            InputContextPolicy.LoadOverrides(entries);
            OnApplied?.Invoke();
            return true;
        }

        // ── Reset ────────────────────────────────────────────────────────────

        /// <summary>
        /// Back to the shipped controls: drops every binding override, every stance override,
        /// and the file. The file is DELETED rather than written empty, so "have I ever
        /// changed anything" stays answerable by its existence.
        /// </summary>
        public static void ResetToDefaults()
        {
            var svc = InputService.Instance;
            svc?.Asset?.RemoveAllBindingOverrides();
            InputContextPolicy.ResetToDefaults();

            try { if (File.Exists(FilePath)) File.Delete(FilePath); }
            catch (Exception ex)
            {
                Debug.LogWarning($"[InputBindingStore] Could not delete '{FilePath}': {ex.Message}");
            }

            OnApplied?.Invoke();
        }

        // ── IO ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Temp-then-swap with a GUID temp name. A fixed <c>&lt;path&gt;.tmp</c> is one name
        /// shared by every writer of the file, which is how the save system produced
        /// "Access to the path is denied" on overlapping writes; the GUID makes each writer's
        /// scratch file its own. The swap retries because the existence check races the other
        /// writer either way round.
        /// </summary>
        private static void WriteAtomic(string path, string contents)
        {
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temp, contents);

            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    if (File.Exists(path)) File.Replace(temp, path, null);
                    else File.Move(temp, path);
                    return;
                }
                catch (IOException) when (attempt < 2) { }
            }

            // Last resort: a straight overwrite. Loses the "never half-written" guarantee for
            // this one write, which is strictly better than losing the write.
            File.Copy(temp, path, overwrite: true);
            try { File.Delete(temp); } catch { /* the copy already landed */ }
        }

        /// <summary>Domain Reload is OFF, so the subscriber list survives into the next Play
        /// session carrying delegates that point at destroyed panels.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState() => OnApplied = null;

        public static void ResetForTests() => OnApplied = null;
    }
}
