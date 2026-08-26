using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.Buildings
{
    public partial class BuildingsRuntimeEditor : SingletonMonoBehaviour<BuildingsRuntimeEditor>, GameEditorManager.IGameEditor
    {
        /// <summary>
        /// Serialize one <c>overrides.door</c> block. The keys here and the ones
        /// <c>BuildingLoader.ParseDoorSpec</c> reads are a PAIR: changing either side alone
        /// is the exact shape of the spawner coordinate-space drift incident, where a save
        /// and its load disagreed silently for months. BuildingDoorPersistenceRoundTripTests
        /// asserts the composition, not either half.
        ///
        /// Floats go through InvariantCulture for the same reason split_ratio does — a
        /// machine with a comma decimal separator would otherwise emit
        /// <c>"spawn_x": 25,5</c> and break the file for everyone else.
        /// </summary>
        private static void AppendDoorJson(StringBuilder sb, Valkur.Data.BuildingDoorSpec spec)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            sb.Append("\"door\": {");
            sb.Append($"\"target\": \"{EscapeJson(spec.target)}\"");
            if (spec.useDefaultSpawn) sb.Append(", \"use_default_spawn\": true");
            sb.Append(string.Format(inv, ", \"spawn_x\": {0:F3}", spec.spawnX));
            sb.Append(string.Format(inv, ", \"spawn_y\": {0:F3}", spec.spawnY));
            if (!string.IsNullOrEmpty(spec.prompt))
                sb.Append($", \"prompt\": \"{EscapeJson(spec.prompt)}\"");
            sb.Append("}");
        }

        private void MarkInstanceDataDirty()
        {
            _hasUnsavedInstanceChanges = true;
        }

        private void PersistDirtyInstanceChanges(string reason = null, bool force = false)
        {
            if ((!_hasUnsavedInstanceChanges && !force) || _isPersistingInstanceChanges)
                return;

            SaveInstancesToJson();
        }

        private void ExecutePersistedEdit(string label, Action doAction, Action undoAction)
        {
            _undo.Do(label,
                () =>
                {
                    doAction?.Invoke();
                    MarkInstanceDataDirty();
                    PersistDirtyInstanceChanges(label, force: true);
                },
                () =>
                {
                    undoAction?.Invoke();
                    MarkInstanceDataDirty();
                    PersistDirtyInstanceChanges($"Undo {label}", force: true);
                });
        }

        private void SaveInstancesToJson()
        {
            if (_isPersistingInstanceChanges) return;

            // While the player is inside an interior the base world is torn down on purpose,
            // so FindObjectsOfType<BuildingObject>() returns nothing. Ctrl+S in that state
            // would write an empty array over 170 placed buildings, and neither position guard
            // below would object - they compare shapes, and "everything is gone" is a
            // perfectly consistent shape.
            if (Valkur.Gameplay.World.WorldTransitionService.RefuseWorldContentWrite("buildings"))
            {
                if (_statusTmp != null) _statusTmp.text = "Save skipped - inside an interior.";
                return;
            }

            // Map-slot aware path resolution. The default slot keeps the legacy
            // StreamingAssets/Buildings/ location so existing builds + the
            // BuildingsDataGuard backup pipeline continue to work; custom slots
            // route to persistentDataPath/Maps/<slot>/Buildings/ so editing one
            // map can never silently overwrite another. See MapEditorActiveSlot.
            string activeSlot = Valkur.Core.MapEditorActiveSlot.Read();
            string dir  = Valkur.Core.MapEditorActiveSlot.BuildingsDir(activeSlot);
            string path = Path.Combine(dir, "buildings_instances.json");
            _isPersistingInstanceChanges = true;
            try
            {
                EnsureColliderDataLoaded();
                if (_activeColliderSession != null && _activeColliderSession.WorkingGrid != null)
                    PersistSessionToStore(_activeColliderSession);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var sb = new StringBuilder();
                sb.AppendLine("[");
                var zm = FindObjectOfType<ZoneManager>();
                int zH = zm != null ? zm.ZoneHeightTiles : 0;

                var all = FindObjectsOfType<BuildingObject>()
                    .Where(b => b != null && b.gameObject.activeInHierarchy && b.Template != null)
                    .OrderBy(b => b.InstanceId).ToList();

                // Pre-pass: compute every building's (zone, rel_x, rel_y) and
                // run a sanity guard BEFORE we touch the JSON. Background: a
                // catastrophic data-loss incident produced a save where 200+
                // buildings inside the same zone collapsed onto a handful of
                // identical (rel_x, rel_y) cells — irreversibly destroying the
                // map. Whatever the upstream cause was (still under
                // investigation), a save must NEVER persist a state where
                // most buildings within a zone share the same coordinates.
                // We refuse to write disk in that case so the user can recover
                // by restarting Play Mode and re-loading the on-disk version.
                var serializedRelX = new int[all.Count];
                var serializedRelY = new int[all.Count];
                var positionCounts = new System.Collections.Generic.Dictionary<(string, int, int), int>();
                int nextId = 1;
                for (int i = 0; i < all.Count; i++)
                {
                    var b = all[i];
                    int oldInstanceId = b.InstanceId;
                    RemapColliderInstanceStore(oldInstanceId, nextId);
                    b.InstanceId = nextId++;
                    int relX = 0, relY = 0;
                    string zone = b.ZoneName ?? "Lobby";
                    if (zm != null && zm.TryGetZone(zone, out var zd))
                    {
                        int effW = (b.ScaleOverride.x > 0) ? b.ScaleOverride.x : b.Template.originalScale.x;
                        int effH = (b.ScaleOverride.y > 0) ? b.ScaleOverride.y : b.Template.originalScale.y;
                        const float PPU = 32f;
                        float wx = b.transform.position.x;
                        float wy = b.transform.position.y;
                        relX = Mathf.RoundToInt((wx - zd.gridOffset.x) * PPU - effW * 0.5f);
                        relY = Mathf.RoundToInt((zd.gridOffset.y + (zH - 1) - wy) * PPU - effH);
                    }
                    serializedRelX[i] = relX;
                    serializedRelY[i] = relY;
                    var key = (zone, relX, relY);
                    positionCounts.TryGetValue(key, out int prevCount);
                    positionCounts[key] = prevCount + 1;
                }

                if (!ValidatePositionUniqueness(all.Count, positionCounts, out string abortReason))
                {
                    Debug.LogError($"[BuildingsEditor] ABORTING save — {abortReason} File NOT written. " +
                                   "Restart Play Mode to reload the last good on-disk state.");
                    if (_statusTmp != null) _statusTmp.text = "Save ABORTED — see console.";
                    return;
                }

                for (int i = 0; i < all.Count; i++)
                {
                    var b = all[i];
                    int relX = serializedRelX[i];
                    int relY = serializedRelY[i];
                    string zone = b.ZoneName ?? "Lobby";

                    sb.Append("  {");
                    sb.Append($"\"id\": {b.InstanceId}, ");
                    sb.Append($"\"template_id\": {b.Template.templateId}, ");
                    sb.Append($"\"zone\": \"{EscapeJson(zone)}\", ");
                    sb.Append($"\"rel_x\": {relX}, ");
                    sb.Append($"\"rel_y\": {relY}");

                    var sov = b.ScaleOverride;
                    bool hasCollisionOverride = _colliderInstanceStore.TryGetValue(b.InstanceId, out var instanceGrid);
                    bool writeCollisionOverride = hasCollisionOverride &&
                        string.Equals(b.EffectiveColliderScope, "CU", StringComparison.OrdinalIgnoreCase);
                    bool hasColliderScope = !string.IsNullOrEmpty(b.ColliderScopeOverride);
                    bool hasZBottomOverride = b.ZBottomOffset != 0;
                    bool hasZTopOverride = b.ZTopOffset != 0;
                    // A doorway is written only when it actually leads somewhere. An empty
                    // target is the resting state of every un-assigned house; persisting it
                    // would add a dead block to hundreds of records.
                    var doorSpec = b.DoorSpec;
                    bool hasDoorOverride = doorSpec != null && doorSpec.IsValid;
                    bool hasOv = b.SplitRatioOverride >= 0f || sov.x > 0 || sov.y > 0 || hasColliderScope || hasZBottomOverride || hasZTopOverride || hasDoorOverride || writeCollisionOverride;
                    if (hasOv)
                    {
                        sb.Append(", \"overrides\": {");
                        bool first = true;
                        if (sov.x > 0 || sov.y > 0) { sb.Append($"\"scale\": [{sov.x}, {sov.y}]"); first = false; }
                        if (b.SplitRatioOverride >= 0f)
                        {
                            if (!first) sb.Append(", ");
                            sb.Append(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                                "\"split_ratio\": {0:F4}", b.SplitRatioOverride));
                            first = false;
                        }
                        if (hasColliderScope)
                        {
                            if (!first) sb.Append(", ");
                            sb.Append($"\"collider_scope\": \"{EscapeJson(b.ColliderScopeOverride)}\"");
                            first = false;
                        }
                        if (hasZBottomOverride)
                        {
                            if (!first) sb.Append(", ");
                            sb.Append($"\"z_bottom\": {b.ZBottomOffset}");
                            first = false;
                        }
                        if (hasZTopOverride)
                        {
                            if (!first) sb.Append(", ");
                            sb.Append($"\"z_top\": {b.ZTopOffset}");
                            first = false;
                        }
                        if (hasDoorOverride)
                        {
                            if (!first) sb.Append(", ");
                            AppendDoorJson(sb, doorSpec);
                            first = false;
                        }
                        if (writeCollisionOverride && instanceGrid != null)
                        {
                            if (!first) sb.Append(", ");
                            sb.Append("\"collision_override\": ");
                            AppendGridJson(sb, instanceGrid, 0);
                        }
                        sb.Append("}");
                    }
                    sb.Append("}");
                    if (i < all.Count - 1) sb.Append(",");
                    sb.AppendLine();
                }
                sb.AppendLine("]");

                // Disk-comparison guard: parse the existing on-disk file (if
                // any) and refuse to overwrite when the new save would shrink
                // its unique-position count by more than half WHILE keeping a
                // similar total entry count. Catches the catastrophic
                // signature (e.g. 337 buildings → 16 unique positions) without
                // false-positiving on legitimate "replace map" or fresh test
                // fixtures where the total entry count drops dramatically.
                if (!ValidateAgainstOnDisk(path, all.Count, positionCounts.Count, out string deltaReason))
                {
                    Debug.LogError($"[BuildingsEditor] ABORTING save — {deltaReason} File NOT written. " +
                                   "Restart Play Mode to reload the last good on-disk state.");
                    if (_statusTmp != null) _statusTmp.text = "Save ABORTED — see console.";
                    return;
                }

                // Atomic write: serialize to a sibling tmp file first so a
                // crash or process kill mid-write can never leave the real
                // file half-written. Use File.Replace where possible — it's
                // atomic on NTFS and rotates the previous content into a
                // .prev sidecar we keep as an extra recovery breadcrumb.
                AtomicWriteJson(path, sb.ToString());
                PruneColliderInstanceStore(all);
                WriteColliderStoresToDisk(dir);
#if UNITY_EDITOR
                // Refresh the backup copy via reflection so we don't create a
                // runtime→editor assembly dependency. BuildingsDataGuard.RefreshBackup()
                // lives in Valkur.Editor (Editor-only assembly). Only meaningful
                // for the default slot — its target file IS the StreamingAssets
                // baseline that BuildingsDataGuard protects. Custom slots live
                // under persistentDataPath and aren't part of the guarded asset
                // graph.
                if (Application.isPlaying && Valkur.Core.MapEditorActiveSlot.IsDefault(activeSlot))
                {
                    UnityEditor.EditorApplication.delayCall += () =>
                    {
                        var t = System.Type.GetType(
                            "Valkur.Editor.BuildingsDataGuard, Valkur.Editor");
                        t?.GetMethod("RefreshBackup",
                            System.Reflection.BindingFlags.Public |
                            System.Reflection.BindingFlags.Static)
                         ?.Invoke(null, null);
                    };
                }
#endif
                string slotLabel = Valkur.Core.MapEditorActiveSlot.IsDefault(activeSlot)
                    ? INSTANCES_REL_PATH
                    : $"slot '{activeSlot}' ({path})";
                if (_statusTmp != null) _statusTmp.text = $"Saved {all.Count} buildings → {slotLabel}";
                Debug.Log($"[BuildingsEditor] Saved {all.Count} buildings to {path} (slot={activeSlot})");
                _hasUnsavedInstanceChanges = false;
                RefreshCollidersPanel();
            }
            catch (System.Exception ex)
            {
                _hasUnsavedInstanceChanges = true;
                Debug.LogError($"[BuildingsEditor] Save failed: {ex.Message}\n{ex.StackTrace}");
                if (_statusTmp != null) _statusTmp.text = "Save FAILED — see console.";
            }
            finally
            {
                _isPersistingInstanceChanges = false;
            }
        }
        private const string INSTANCES_REL_PATH = "StreamingAssets/Buildings/buildings_instances.json";

        // Catastrophic-collapse threshold: the highest number of buildings we
        // ever expect to see legitimately stacked on the same (zone, rel_x,
        // rel_y) tuple. Real maps have at most 1 — overlapping decorations
        // might push it to 2-3. Anything at or above this is corruption.
        private const int MAX_BUILDINGS_PER_POSITION = 5;

        // Disk-state regression detector. Reads the existing on-disk file
        // (if any) and counts both its total entries AND its unique
        // (zone, rel_x, rel_y) tuples. If we're about to write a similar
        // total but with dramatically fewer unique positions, that's the
        // catastrophic-collapse signature and we refuse the write.
        // Skips when totals diverge significantly (legitimate "replace map"
        // operations or test fixtures using a different starting count).
        private static bool ValidateAgainstOnDisk(string path, int newTotalEntries, int newUniquePositionCount, out string reason)
        {
            reason = null;
            if (!File.Exists(path)) return true;
            int onDiskTotal, onDiskUnique;
            try
            {
                CountOnDiskStats(path, out onDiskTotal, out onDiskUnique);
            }
            catch
            {
                // Don't block a save just because the on-disk file is
                // unparseable — that scenario is exactly when a fresh save
                // is most needed. The other guards still protect the write.
                return true;
            }
            // Skip the comparison on tiny on-disk fixtures (early development,
            // empty maps) where ratio-based thresholds have no meaning.
            if (onDiskUnique < 20) return true;
            // Skip when the new save shrinks the entry count by more than half:
            // that's a legitimate "replace map" or test scenario, not the
            // collapse signature (which preserves the total).
            if (newTotalEntries * 2 < onDiskTotal) return true;
            if (newUniquePositionCount * 2 < onDiskUnique)
            {
                reason = $"about to write {newUniquePositionCount} unique positions for {newTotalEntries} buildings, but on-disk file has {onDiskUnique} unique for {onDiskTotal}. Save shrinks the map by >50% with similar total — collapse signature.";
                return false;
            }
            return true;
        }

        private static void CountOnDiskStats(string path, out int total, out int uniquePositions)
        {
            total = 0;
            uniquePositions = 0;
            string json = File.ReadAllText(path);
            var raw = Valkur.Gameplay.World.MiniJsonRuntime.Deserialize(json) as System.Collections.Generic.List<object>;
            if (raw == null) return;
            var seen = new System.Collections.Generic.HashSet<(string, long, long)>();
            foreach (var item in raw)
            {
                var dict = item as System.Collections.Generic.Dictionary<string, object>;
                if (dict == null) continue;
                total++;
                string zone = dict.TryGetValue("zone", out var zo) ? (zo as string ?? "") : "";
                long relX = dict.TryGetValue("rel_x", out var rx) && rx is long lx ? lx : 0;
                long relY = dict.TryGetValue("rel_y", out var ry) && ry is long ly ? ly : 0;
                seen.Add((zone, relX, relY));
            }
            uniquePositions = seen.Count;
        }

        // Atomic write helper. Strategy:
        //   1. Write the new content to <path>.tmp.
        //   2. If <path> exists, use File.Replace to swap the two atomically
        //      (NTFS-atomic on Windows) and divert the previous content to
        //      <path>.prev as a quick-recovery sidecar.
        //   3. If <path> doesn't exist, just rename .tmp → path.
        // Falls back to a non-atomic delete+move if File.Replace fails — the
        // failure mode there is identical to the previous (already-shipped)
        // direct-WriteAllText behaviour, so we never regress.
        private static void AtomicWriteJson(string path, string content)
        {
            string tmpPath  = path + ".tmp";
            string prevPath = path + ".prev";
            File.WriteAllText(tmpPath, content);
            try
            {
                if (File.Exists(path))
                    File.Replace(tmpPath, path, prevPath, ignoreMetadataErrors: true);
                else
                    File.Move(tmpPath, path);
            }
            catch
            {
                // Defensive fallback for filesystems where File.Replace isn't
                // supported (some non-NTFS network mounts). Two-step swap is
                // still safer than overwriting in place because the .tmp file
                // contains the full new content before we touch the original.
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmpPath, path);
            }
        }

        // Internal — pure function so it's exercised cleanly by the regression
        // test in BuildingsSaveFormatTests. Returns false (and a human-readable
        // reason) when the save state shows catastrophic position collapse.
        internal static bool ValidatePositionUniqueness(
            int totalBuildings,
            System.Collections.Generic.Dictionary<(string zone, int relX, int relY), int> positionCounts,
            out string reason)
        {
            // Absolute guard: any single (zone, position) tuple with too many
            // buildings is almost certainly the corruption signature.
            foreach (var kv in positionCounts)
            {
                if (kv.Value >= MAX_BUILDINGS_PER_POSITION)
                {
                    reason = $"{kv.Value} buildings collapsed onto zone='{kv.Key.zone}' rel=({kv.Key.relX},{kv.Key.relY}).";
                    return false;
                }
            }
            // Relative guard: catches lower-multiplicity but global collapse
            // (e.g. 4× per position across 16 zones from a 200-building map).
            // Skip on tiny fixtures where a 50% threshold has no statistical
            // meaning.
            if (totalBuildings >= 20 && positionCounts.Count * 2 < totalBuildings)
            {
                reason = $"only {positionCounts.Count} unique positions for {totalBuildings} buildings (<50%).";
                return false;
            }
            reason = null;
            return true;
        }

        private void ReloadFromJson()
        {
            CacheBuildingLoader();
            if (_buildingLoader == null) { Toast("BuildingLoader not found in scene."); return; }
            ResetColliderAuthoringState();
            _buildingLoader.LoadBuildings();
            _undo.Clear();
            _activeBuilding = null;
            _hoveredBuilding = null;
            InvalidateBuildingCache();
            ApplyBuildingsVisibility();
            RefreshInspector();
            if (_statusTmp != null) _statusTmp.text = "Reloaded from JSON.";
        }

        // ──────────────────────────────────────────────────────────────────────────
        //  CONFIRM MODAL
        // ──────────────────────────────────────────────────────────────────────────

        private void ShowConfirm(string text, System.Action onYes)
        {
            if (_confirmModal == null) { onYes?.Invoke(); return; }
            _confirmText.text = text;
            _pendingConfirmYes = onYes;
            _confirmModal.SetActive(true);
            _confirmModal.transform.SetAsLastSibling();
        }

        private void HideConfirm()
        {
            _pendingConfirmYes = null;
            if (_confirmModal != null) _confirmModal.SetActive(false);
        }

        // ──────────────────────────────────────────────────────────────────────────
        //  TUTORIAL
        // ──────────────────────────────────────────────────────────────────────────

        private void ToggleTutorial()
        {
            if (_tutorialRoot == null) return;
            bool show = !_tutorialRoot.activeSelf;
            _tutorialRoot.SetActive(show);
            if (show) { _tutorialRoot.transform.SetAsLastSibling(); RefreshTutorial(); }
        }

        private void StepTutorial(int delta)
        {
            _tutorialStep = (_tutorialStep + delta + TUTORIAL_STEPS.Length) % TUTORIAL_STEPS.Length;
            RefreshTutorial();
        }

        private void RefreshTutorial()
        {
            if (_tutorialStepLabel == null) return;
            var (title, body) = TUTORIAL_STEPS[_tutorialStep];
            _tutorialStepLabel.text = $"{title}   ({_tutorialStep + 1}/{TUTORIAL_STEPS.Length})";
            _tutorialBodyTmp.text = body;
        }

        private void ToggleBuildingsVisible()
        {
            _buildingsVisible = !_buildingsVisible;
            ApplyBuildingsVisibility();
            RefreshBuildingsVisibilityButton();

            if (!_buildingsVisible)
            {
                _hoveredBuilding = null;
                _hoverStack.Clear();
            }

            if (_statusTmp != null)
                _statusTmp.text = _buildingsVisible ? "Buildings visible." : "Buildings hidden.";
        }

        private void ApplyBuildingsVisibility()
        {
            var all = GetCachedBuildings();
            for (int i = 0; i < all.Length; i++)
            {
                var building = all[i];
                if (building == null) continue;

                var renderers = building.GetComponentsInChildren<SpriteRenderer>(true);
                for (int j = 0; j < renderers.Length; j++)
                {
                    if (renderers[j] != null)
                        renderers[j].enabled = _buildingsVisible;
                }
            }

            if (!_buildingsVisible)
                HideOutlines();
        }

        private void RefreshBuildingsVisibilityButton()
        {
            BuildingsEditorUIBuilder.ApplyMenuBtnStyle(
                _uiRefs.BuildingVisibilityMenuBtnImg,
                _uiRefs.BuildingVisibilityMenuBtnTmp,
                _buildingsVisible);
        }

        // ──────────────────────────────────────────────────────────────────────────
        //  PER-FRAME OVERLAY UPDATES (outlines + handles + ID label)
        // ──────────────────────────────────────────────────────────────────────────

        private void UpdateOutlineState()
        {
            if (_hoverFx == null || _activeFx == null) return;
            if (!_buildingsVisible)
            {
                HideOutlines();
                return;
            }

            // Hover (skip if same as active to avoid double-drawing)
            if (_hoveredBuilding != null && _hoveredBuilding != _activeBuilding)
            {
                bool red = _removeMode || _mode == EditorMode.Delete;
                _hoverFx.Configure(
                    color:        red ? HOVER_REMOVE_RED : HOVER_CYAN,
                    thicknessWorld: red ? HOVER_THICKNESS_WORLD * 1.5f : HOVER_THICKNESS_WORLD,
                    drawFill:     red,
                    fillColor:    HOVER_REMOVE_FILL);
                _hoverFx.Follow(_hoveredBuilding);
            }
            else
            {
                _hoverFx.Follow(null); _hoverFx.SetVisible(false);
            }

            // Active
            if (_activeBuilding != null) _activeFx.Follow(_activeBuilding);
            else { _activeFx.Follow(null); _activeFx.SetVisible(false); }
        }

        private void UpdateFloatingHandles()
        {
            if (_handlesRoot == null) return;
            if (!_buildingsVisible)
            {
                _handlesRoot.SetActive(false);
                return;
            }
            bool show = _activeBuilding != null && !_removeMode;
            _handlesRoot.SetActive(show);
            if (!show) return;

            if (!_activeBuilding.TryGetWorldRect(out var rect)) { _handlesRoot.SetActive(false); return; }
            var cam = Camera.main;
            if (cam == null) return;

            // Project building top-right corner to canvas (pivot=top-right → badge sits inside frame)
            Vector3 worldTopRight = new Vector3(rect.xMax, rect.yMax, 0f);
            Vector3 screenTR      = cam.WorldToScreenPoint(worldTopRight);
            Vector2 canvasTR      = ScreenToCanvasPos(screenTR);

            // Compute proportional badge size from the building's canvas-space width
            Vector3 worldTopLeft = new Vector3(rect.xMin, rect.yMax, 0f);
            Vector3 screenTL     = cam.WorldToScreenPoint(worldTopLeft);
            Vector2 canvasTL     = ScreenToCanvasPos(screenTL);
            float canvasW        = Mathf.Abs(canvasTR.x - canvasTL.x);
            float handleSize     = Mathf.Clamp(canvasW * 0.20f, 20f, 52f);

            var rt = _handlesRoot.GetComponent<RectTransform>();
            rt.sizeDelta        = new Vector2(handleSize, handleSize);
            rt.anchoredPosition = canvasTR;
        }
    }
}
