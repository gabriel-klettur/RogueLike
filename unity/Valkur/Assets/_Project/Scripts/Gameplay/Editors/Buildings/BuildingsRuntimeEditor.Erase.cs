using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Tilemaps;
using TMPro;
using Valkur.Core;
using Valkur.Gameplay.Editors;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.Buildings
{
    /// <summary>
    /// Partial class containing the Erase tool implementation for BuildingsRuntimeEditor.
    ///
    /// Flow:
    ///   1. OnEraseButtonClicked  → enters Erase mode, shows the scope sub-panel (AwaitingScope).
    ///   2. OnEraseScopeSelected  → captures TilesArea | Zone, advances to AwaitingTarget.
    ///   3. OnEraseTargetClicked  → finds the building under the click. If none → ExitEraseMode.
    ///                              Otherwise: computes matches via BuildingsEraseMatcher,
    ///                              draws orange outlines on each match (and yellow flood-fill
    ///                              overlay for TilesArea), opens the confirm modal.
    ///   4. CommitErase           → batch SetActive(false) on all matches in a single undo entry;
    ///                              undo SetActive(true) restores them. Returns to Select.
    ///   5. ExitEraseMode         → cancels at any step, clears all UI/state.
    /// </summary>
    public partial class BuildingsRuntimeEditor : SingletonMonoBehaviour<BuildingsRuntimeEditor>, GameEditorManager.IGameEditor
    {
        // ── Entry ────────────────────────────────────────────────────────────────

        private void OnEraseButtonClicked()
        {
            if (_mode == EditorMode.Erase)
            {
                ExitEraseMode();
                return;
            }

            // Erase needs the Ground tilemap to support the TilesArea scope. Use the
            // same resolver Fill uses (cached via _worldGroundTilemap).
            ResolveWorldTilemap();
            if (_worldGroundTilemap == null)
            {
                Toast("Erase: Ground tilemap not found in scene.");
                return;
            }

            _mode = EditorMode.Erase;
            _eraseStep = EraseStep.AwaitingScope;
            RefreshModeButtons();
            ShowEraseSubPanel();
            RefreshEraseScopeHighlights();
            if (_statusTmp != null)
                _statusTmp.text = "Erase: choose scope (Tiles Area or Zone).";
        }

        // ── Sub-panel show/hide ──────────────────────────────────────────────────

        private void ShowEraseSubPanel()
        {
            if (_eraseSubPanel == null) return;
            _eraseSubPanel.SetActive(true);
            _eraseSubPanel.transform.SetAsLastSibling();
        }

        private void HideEraseSubPanel()
        {
            if (_eraseSubPanel != null) _eraseSubPanel.SetActive(false);
        }

        private void OnEraseScopeSelected(EraseScope scope)
        {
            if (_mode != EditorMode.Erase) return;
            _eraseScope = scope;
            _eraseStep  = EraseStep.AwaitingTarget;
            RefreshEraseScopeHighlights();
            if (_statusTmp != null)
                _statusTmp.text = scope == EraseScope.Zone
                    ? "Erase: click a building to delete all of its type in the same zone."
                    : "Erase: click a building to delete all of its type in the connected tile area.";
        }

        private void RefreshEraseScopeHighlights()
        {
            bool active = _mode == EditorMode.Erase && _eraseStep != EraseStep.Idle && _eraseStep != EraseStep.AwaitingScope;
            if (_eraseTilesAreaBtnImg)
                _eraseTilesAreaBtnImg.color = (active && _eraseScope == EraseScope.TilesArea)
                    ? EditorUIHelpers.BTN_ACTIVE : EditorUIHelpers.BTN_NORMAL;
            if (_eraseZoneBtnImg)
                _eraseZoneBtnImg.color = (active && _eraseScope == EraseScope.Zone)
                    ? EditorUIHelpers.BTN_ACTIVE : EditorUIHelpers.BTN_NORMAL;
        }

        // ── Click target ─────────────────────────────────────────────────────────

        private void OnEraseTargetClicked(Vector3 worldPos)
        {
            // Reuse the editor's hover stack to find the building the user clicked on.
            // RecomputeHoverStack populates _hoverStack with all overlapping buildings;
            // index 0 is the topmost.
            RecomputeHoverStack(worldPos);
            BuildingObject clicked = null;
            if (_hoverStack.Count > 0) clicked = _hoverStack[0];

            if (clicked == null || clicked.Template == null)
            {
                // User confirmed: a click on empty cancels the entire flow.
                ExitEraseMode();
                return;
            }

            _eraseTemplateId = clicked.Template.templateId;
            _eraseZoneId     = clicked.ZoneName;
            _eraseAreaCells.Clear();

            // Snapshot every BuildingObject in the scene once.
            var allArr = FindObjectsOfType<BuildingObject>();
            var all    = new List<BuildingObject>(allArr);

            if (_eraseScope == EraseScope.TilesArea)
            {
                if (_worldGroundTilemap == null)
                {
                    ExitEraseMode();
                    return;
                }
                var startCell = _worldGroundTilemap.WorldToCell(clicked.transform.position);
                var cells = TileBrush.ComputeFloodFillCells(_worldGroundTilemap, startCell);
                foreach (var c in cells) _eraseAreaCells.Add(c);
                _eraseMatches.Clear();
                _eraseMatches.AddRange(BuildingsEraseMatcher.MatchesByTilesArea(
                    all, _eraseTemplateId, _eraseAreaCells, _worldGroundTilemap));
            }
            else
            {
                _eraseMatches.Clear();
                _eraseMatches.AddRange(BuildingsEraseMatcher.MatchesByZone(
                    all, _eraseTemplateId, _eraseZoneId));
            }

            if (_eraseMatches.Count == 0)
            {
                // Defensive — normally the clicked building itself satisfies the filter
                // and is in the scene. If somehow zero, just bail.
                ExitEraseMode();
                return;
            }

            // Visual feedback before confirmation.
            RebuildEraseMatchFx(_eraseMatches);
            if (_eraseScope == EraseScope.TilesArea)
            {
                EnsureFillOverlay();
                if (_fillOverlay != null)
                    _fillOverlay.SetCells(_eraseAreaCells, _worldGroundTilemap);
            }

            // Build & show confirm modal.
            BuildEraseConfirmModal();
            string templateName = clicked.Template.name;
            string scopeText = _eraseScope == EraseScope.Zone
                ? $"in {(_eraseZoneId ?? "<no zone>")}"
                : $"in the selected area ({_eraseAreaCells.Count} tiles)";
            if (_eraseConfirmText != null)
                _eraseConfirmText.text =
                    $"Delete {_eraseMatches.Count} buildings\n" +
                    $"Template: #{_eraseTemplateId} ({templateName})\n" +
                    $"Scope: {scopeText}?";
            _eraseConfirmYes = CommitErase;
            _eraseConfirmModal.SetActive(true);
            _eraseConfirmModal.transform.SetAsLastSibling();
            _eraseStep = EraseStep.AwaitingConfirm;
        }

        // ── Orange outline pool ──────────────────────────────────────────────────

        private void RebuildEraseMatchFx(IReadOnlyList<BuildingObject> matches)
        {
            // Grow the pool if needed.
            while (_eraseMatchFxPool.Count < matches.Count)
            {
                var go = new GameObject("BuildingsEditor.EraseMatchFx");
                go.transform.SetParent(transform, false);
                var fx = go.AddComponent<BuildingOutlineRenderer>();
                fx.Configure(SAME_TEMPLATE_ORANGE, SAME_TEMPLATE_THICKNESS_WORLD,
                    drawFill: false, fillColor: Color.clear);
                _eraseMatchFxPool.Add(fx);
            }
            // Assign Follow targets; hide surplus pool entries.
            for (int i = 0; i < _eraseMatchFxPool.Count; i++)
            {
                if (i < matches.Count)
                {
                    _eraseMatchFxPool[i].Follow(matches[i]);
                    _eraseMatchFxPool[i].SetVisible(true);
                }
                else
                {
                    _eraseMatchFxPool[i].Follow(null);
                    _eraseMatchFxPool[i].SetVisible(false);
                }
            }
        }

        private void HideEraseMatchOutlines()
        {
            for (int i = 0; i < _eraseMatchFxPool.Count; i++)
            {
                if (_eraseMatchFxPool[i] == null) continue;
                _eraseMatchFxPool[i].Follow(null);
                _eraseMatchFxPool[i].SetVisible(false);
            }
        }

        // ── Confirm modal ────────────────────────────────────────────────────────

        private void BuildEraseConfirmModal()
        {
            if (_eraseConfirmModal != null) return;

            _eraseConfirmModal = EditorUIHelpers.MakePanel("EraseConfirmModal", _root.transform,
                new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
            var bgImg = _eraseConfirmModal.GetComponent<Image>();
            bgImg.color = new Color(0f, 0f, 0f, 140f / 255f);

            var inner = EditorUIHelpers.MakePanel("Inner", _eraseConfirmModal.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(520f, 220f));
            var vlg = inner.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(20, 20, 18, 18);
            vlg.spacing = 12f; vlg.childForceExpandWidth = true;
            EditorUIHelpers.MakeTitleBar(inner.transform, "ERASE BUILDINGS");

            _eraseConfirmText = EditorUIHelpers.AddLabel(inner.transform, "", 13f);
            _eraseConfirmText.color = EditorUIHelpers.TEXT_PRIMARY;
            _eraseConfirmText.alignment = TextAlignmentOptions.MidlineLeft;
            var le = _eraseConfirmText.gameObject.AddComponent<LayoutElement>();
            le.flexibleHeight = 1f;

            var btnRow = EditorUIHelpers.CreateUI("Btns", inner.transform);
            btnRow.AddComponent<LayoutElement>().preferredHeight = 36f;
            var hlg = btnRow.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 12f; hlg.childForceExpandWidth = true;
            EditorUIHelpers.MakeDangerButton(btnRow.transform, "Delete",
                () => { _eraseConfirmYes?.Invoke(); }, 32f);
            EditorUIHelpers.MakeButton(btnRow.transform, "Cancel",
                () => ExitEraseMode(), 32f, 12f);

            _eraseConfirmModal.SetActive(false);
        }

        // ── Commit ───────────────────────────────────────────────────────────────

        /// <summary>Full per-instance snapshot for Destroy+Recreate undo cycle.</summary>
        private sealed class EraseSnapshot
        {
            public Valkur.Data.BuildingTemplateData Template;
            public Vector3   Position;
            public string    ZoneName;
            public int       InstanceId;
            public Vector2Int ScaleOverride;
            public float     SplitRatioOverride;
            public int       ZBottomOffset;
            public int       ZTopOffset;
            public string    ColliderScopeOverride;
            public string    Name;
        }

        private void CommitErase()
        {
            // Capture FULL per-instance state up front so the undo path can recreate the
            // BuildingObject from scratch (mirrors how Fill creates buildings on do and
            // destroys them on undo — same pattern, inverted).
            var snapshots = new List<EraseSnapshot>(_eraseMatches.Count);
            for (int i = 0; i < _eraseMatches.Count; i++)
            {
                var b = _eraseMatches[i];
                if (b == null || b.Template == null) continue;
                snapshots.Add(new EraseSnapshot
                {
                    Template              = b.Template,
                    Position              = b.transform.position,
                    ZoneName              = b.ZoneName,
                    InstanceId            = b.InstanceId,
                    ScaleOverride         = b.ScaleOverride,
                    SplitRatioOverride    = b.SplitRatioOverride,
                    ZBottomOffset         = b.ZBottomOffset,
                    ZTopOffset            = b.ZTopOffset,
                    ColliderScopeOverride = b.ColliderScopeOverride,
                    Name                  = b.gameObject.name,
                });
            }
            int count = snapshots.Count;
            if (count == 0)
            {
                ExitEraseMode();
                return;
            }

            // Live references to the BuildingObjects to delete on Do and to the
            // recreated ones on Undo. Updated each cycle so a Redo also targets the
            // re-created instances.
            var liveTargets  = new List<BuildingObject>(count);
            for (int i = 0; i < _eraseMatches.Count; i++)
                if (_eraseMatches[i] != null) liveTargets.Add(_eraseMatches[i]);

            // Cache the loader so we have a parent transform for recreated buildings.
            CacheBuildingLoader();

            ExecutePersistedEdit($"Erase {count} buildings",
                () =>
                {
                    // Do — destroy each match. Capture name in snapshot already; here we
                    // just destroy the GameObjects. This matches how Fill cleans up on
                    // its own undo (Destroy + clear list).
                    bool clearActive = false;
                    for (int i = 0; i < liveTargets.Count; i++)
                    {
                        var bo = liveTargets[i];
                        if (bo == null) continue;
                        if (_activeBuilding == bo) clearActive = true;
                        bo.gameObject.SetActive(false);
                        Destroy(bo.gameObject);
                    }
                    liveTargets.Clear();
                    InvalidateBuildingCache();
                    if (clearActive)
                    {
                        _activeBuilding = null;
                        _propertiesMode = PropertiesMode.None;
                        RefreshInspector();
                    }
                },
                () =>
                {
                    // Undo — recreate each building from snapshot using exactly the same
                    // path Fill / drag-drop placement uses (BuildingLoader.Spawning style).
                    liveTargets.Clear();
                    for (int i = 0; i < snapshots.Count; i++)
                    {
                        var s = snapshots[i];
                        if (s.Template == null) continue;

                        var go = new GameObject(s.Name);
                        if (_buildingsRoot != null)
                            go.transform.SetParent(_buildingsRoot, worldPositionStays: false);
                        go.transform.position = s.Position;
                        go.layer = 11; // World

                        var bObj = go.AddComponent<BuildingObject>();
                        bObj.ZoneName              = s.ZoneName;
                        bObj.InstanceId            = s.InstanceId;
                        bObj.ColliderScopeOverride = s.ColliderScopeOverride;
                        // Apply must run BEFORE Z offsets so the renderers exist when
                        // ApplyZOffsets fires; the setters call ApplyZOffsets internally.
                        bObj.Apply(s.Template, s.ScaleOverride, s.SplitRatioOverride);
                        bObj.ZBottomOffset = s.ZBottomOffset;
                        bObj.ZTopOffset    = s.ZTopOffset;

                        // Honor the current "buildings visible" flag, exactly like Fill's
                        // commit path does for newly placed buildings.
                        var renderers = bObj.GetComponentsInChildren<SpriteRenderer>(true);
                        for (int r = 0; r < renderers.Length; r++)
                            if (renderers[r] != null)
                                renderers[r].enabled = _buildingsVisible;

                        RefreshCollisionFor(bObj);
                        liveTargets.Add(bObj);
                    }
                    InvalidateBuildingCache();
                });

            if (_statusTmp != null)
                _statusTmp.text = $"Erased {count} buildings.";
            ExitEraseMode();
        }

        // ── Exit / cleanup ───────────────────────────────────────────────────────

        private void ExitEraseMode(bool setSelectMode = true)
        {
            if (_eraseConfirmModal != null) _eraseConfirmModal.SetActive(false);
            HideEraseSubPanel();
            HideEraseMatchOutlines();
            HideFillOverlay();
            _eraseConfirmYes = null;
            _eraseMatches.Clear();
            _eraseAreaCells.Clear();
            _eraseTemplateId = -1;
            _eraseZoneId = null;
            _eraseStep = EraseStep.Idle;
            RefreshEraseScopeHighlights();
            if (setSelectMode && _mode == EditorMode.Erase)
                SetMode(EditorMode.Select);
        }
    }
}
