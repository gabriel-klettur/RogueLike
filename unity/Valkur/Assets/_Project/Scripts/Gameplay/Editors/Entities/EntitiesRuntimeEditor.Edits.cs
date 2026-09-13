using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;
using Valkur.UIKit;

namespace Valkur.Gameplay.Entities
{
    /// <summary>
    /// Undo, and what Save actually writes.
    ///
    /// <para><b>Undo used to cover one gesture out of fifteen.</b> <c>_undo.Record</c> was called
    /// from exactly one place — dragging a placed entity — so the two buttons in the Tools panel
    /// looked like they covered the editor and covered a drag. Nothing on screen said so: an
    /// author who retuned six stats and pressed Ctrl+Z got their last MOVE back and kept every
    /// stat.</para>
    ///
    /// <para><b>The undo is a SNAPSHOT of the whole definition, not a per-field command.</b> Every
    /// mutation in this editor already ends at <see cref="CommitDefinitionEdit"/> — stats, the
    /// auto-cast list, the timeline, the spell muzzle, the rename — so one seam there reaches all
    /// of them, where fifteen per-field commands would reach whichever ones somebody remembered.
    /// <c>EditorJsonUtility</c> round-trips a ScriptableObject's serialized state INCLUDING its
    /// object references, so a snapshot captures things a hand-written command would have missed:
    /// a nested <c>assetConfig</c>, a list resized, a struct field nobody enumerated.</para>
    ///
    /// <para><b>It must be <c>EditorJsonUtility</c> and not <c>JsonUtility</c>, and the difference
    /// is silent and destructive.</b> Plain <c>JsonUtility</c> does not serialize
    /// <c>UnityEngine.Object</c> references at all, so a restore quietly REPLACED every sprite on
    /// the definition. Measured: after one probe round trip <c>dark_dwarf</c>'s idle frames were
    /// still named <c>dwarf_idle_e0</c> and were no longer the same objects as the class they
    /// wear — a shape a value-by-value comparison cannot see. <c>DarkRosterDataTests</c> caught
    /// it; the "restored exactly" check written for this feature did not, because it compared
    /// numbers.</para>
    ///
    /// <para>It is deliberately NOT <c>UnityEditor.Undo</c>. A bulk editor that records to the
    /// GLOBAL undo stack is what silently reverted 193 building templates the first time anything
    /// popped it — the incident this file's neighbours all cite.</para>
    /// </summary>
    public partial class EntitiesRuntimeEditor
    {
        /// <summary>
        /// The last committed state of every definition this session has touched, as JSON.
        ///
        /// <para>Keyed by the object, and seeded the moment a definition is resolved for editing
        /// (<see cref="CurrentEditableMonster"/>) rather than lazily at commit time: by commit
        /// time the edit has already happened, so a lazy seed would make the FIRST change to each
        /// definition the one change that cannot be undone — silently, and only for the edit an
        /// author is most likely to be experimenting with.</para>
        /// </summary>
        private readonly Dictionary<MonsterDefinition, string> _definitionSnapshots =
            new Dictionary<MonsterDefinition, string>();

        /// <summary>
        /// Definitions this session has dirtied, so Save can write THOSE and not the project.
        ///
        /// <para><c>AssetDatabase.SaveAssets()</c> writes everything dirty anywhere — which is the
        /// call that, with a corrupted in-memory ScriptableObject, would have flushed the
        /// corruption over good files in the <c>SkillTree</c> incident. A button labelled "Save"
        /// inside one editor should mean that editor's own work.</para>
        /// </summary>
        private readonly HashSet<Object> _dirtyAssets = new HashSet<Object>();

        /// <summary>Remember what a definition looks like now, if it is not already remembered.</summary>
        private void SeedDefinitionSnapshot(MonsterDefinition def)
        {
            if (def == null || _definitionSnapshots.ContainsKey(def)) return;
            _definitionSnapshots[def] = Snapshot(def);
        }

        /// <summary>
        /// The definition's whole serialized state, object references included.
        ///
        /// <para>Outside the Editor there is nothing to snapshot FOR — Save is Editor-only and a
        /// player build has no <c>.asset</c> to write — so the stack records nothing there rather
        /// than recording something lossy.</para>
        /// </summary>
        private static string Snapshot(MonsterDefinition def)
        {
#if UNITY_EDITOR
            return UnityEditor.EditorJsonUtility.ToJson(def);
#else
            return null;
#endif
        }

        /// <summary>
        /// Record the change just made to <paramref name="def"/> as one undoable step.
        ///
        /// <para>Nothing is recorded when the serialized state did not actually move. Every
        /// committed edit re-renders the properties panel, and re-rendering commits the value
        /// back through the same setters — so without this an author pressing Enter on a field
        /// they did not change would push a no-op onto the stack, and Ctrl+Z would appear to do
        /// nothing until they had pressed it as many times as they had clicked.</para>
        /// </summary>
        private void RecordDefinitionUndo(MonsterDefinition def, string label)
        {
            if (def == null) return;

            string after = Snapshot(def);
            if (after == null) return;
            if (!_definitionSnapshots.TryGetValue(def, out string before))
            {
                _definitionSnapshots[def] = after;
                return;
            }
            if (string.Equals(before, after, System.StringComparison.Ordinal)) return;

            _definitionSnapshots[def] = after;

            var target = def;   // captured: the stack outlives the selection
            _undo.Record(new UndoStack.LambdaCommand(label,
                doAction:   () => RestoreDefinition(target, after),
                undoAction: () => RestoreDefinition(target, before)));
        }

        /// <summary>
        /// Put a definition back to a snapshot, and put the world back with it.
        ///
        /// <para>The snapshot is re-seeded BEFORE the panel is redrawn, because redrawing pushes
        /// every field back through its setter and each of those commits — so a stale snapshot
        /// here would record the undo itself as a fresh edit and make redo unreachable.</para>
        /// </summary>
        private void RestoreDefinition(MonsterDefinition def, string json)
        {
            if (def == null || string.IsNullOrEmpty(json)) return;

#if !UNITY_EDITOR
            // Nothing recorded a snapshot outside the Editor, so nothing can be holding one.
            // Guarded here rather than mid-body: an #else return leaves everything below it
            // unreachable, which is a warning in a player build and reads as dead code.
            return;
#else
            UnityEditor.EditorJsonUtility.FromJsonOverwrite(json, def);
            _definitionSnapshots[def] = json;

            UnityEditor.EditorUtility.SetDirty(def);
            _dirtyAssets.Add(def);
            _pendingAssetWrites = true;

            int live = ReapplyToLiveMonsters(def);
            RefreshPicker();
            if (!string.IsNullOrEmpty(_selectedKey) && _selectedKey == def.monsterKey)
                ShowMonsterProperties(def.monsterKey);
            RefreshMuzzleEditor();
            RefreshTimelinePanel();

            SetStatus(live > 0
                ? $"Restored '{def.monsterKey}' — {live} live reconfigured."
                : $"Restored '{def.monsterKey}'.");
#endif
        }

        /// <summary>Mark an asset as this editor's to save.</summary>
        private void MarkDirty(Object asset)
        {
            if (asset == null) return;
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(asset);
#endif
            _dirtyAssets.Add(asset);
            _pendingAssetWrites = true;
        }

        /// <summary>
        /// Write THIS editor's edits, and nothing else.
        ///
        /// <para><c>SaveAssetIfDirty</c> per asset rather than <c>SaveAssets()</c>: the second
        /// commits every dirty object in the project, which on a session that also touched a
        /// prefab or a scene means a button in this panel wrote somebody else's work to disk.</para>
        /// </summary>
        private void SaveEditedDefinitions()
        {
#if UNITY_EDITOR
            if (!_pendingAssetWrites || _dirtyAssets.Count == 0)
            {
                SetStatus("Nothing to save — no property has been edited.");
                return;
            }

            int written = 0;
            foreach (var asset in _dirtyAssets)
            {
                if (asset == null) continue;
                UnityEditor.AssetDatabase.SaveAssetIfDirty(asset);
                written++;
            }
            _dirtyAssets.Clear();
            _pendingAssetWrites = false;
            SetStatus($"Saved {written} asset(s) edited here. Nothing else in the project was written.");
#else
            SetStatus("Save is Editor-only — a built game has no .asset files to write.");
#endif
        }

        /// <summary>
        /// Re-read the catalogue FROM DISK, discarding unsaved edits.
        ///
        /// <para>It used to be <c>RefreshPicker()</c>, which re-lists the same in-memory objects:
        /// an author who edited a <c>.asset</c> outside the game and pressed Reload got nothing,
        /// with no warning. A domain reload does not reload assets either, so re-importing is the
        /// only thing that actually re-reads the file.</para>
        ///
        /// <para>It REFUSES while there are unsaved edits rather than discarding them silently —
        /// the same gesture that recovers from a bad edit would otherwise be the one that throws
        /// away a good session. Press it twice to mean it.</para>
        /// </summary>
        private void ReloadDefinitionsFromDisk()
        {
#if UNITY_EDITOR
            if (_pendingAssetWrites && !_reloadArmed)
            {
                _reloadArmed = true;
                SetStatus("Reload DISCARDS unsaved edits. Press Reload again to confirm, or Save first.");
                return;
            }
            _reloadArmed = false;

            int reimported = 0;
            foreach (var asset in new List<Object>(_dirtyAssets))
            {
                if (asset == null) continue;
                string path = UnityEditor.AssetDatabase.GetAssetPath(asset);
                if (string.IsNullOrEmpty(path)) continue;
                UnityEditor.AssetDatabase.ImportAsset(path, UnityEditor.ImportAssetOptions.ForceUpdate);
                reimported++;
            }

            // The catalogue itself, so a definition added or removed on disk shows up.
            ResolveMonsterCatalogFallback();
            if (_monsterCatalog != null)
            {
                string catPath = UnityEditor.AssetDatabase.GetAssetPath(_monsterCatalog);
                if (!string.IsNullOrEmpty(catPath))
                    UnityEditor.AssetDatabase.ImportAsset(catPath, UnityEditor.ImportAssetOptions.ForceUpdate);
            }

            _dirtyAssets.Clear();
            _definitionSnapshots.Clear();
            _pendingAssetWrites = false;
            _undo.Clear();

            RefreshPicker();
            if (!string.IsNullOrEmpty(_selectedKey)) SelectEntity(_selectedKey);
            SetStatus($"Reloaded from disk — {reimported} definition(s) re-imported, undo history cleared.");
#else
            RefreshPicker();
            SetStatus("Reload: catalog refreshed.");
#endif
        }

        /// <summary>Armed by the first Reload press while edits are pending. See above.</summary>
        private bool _reloadArmed;
    }
}
