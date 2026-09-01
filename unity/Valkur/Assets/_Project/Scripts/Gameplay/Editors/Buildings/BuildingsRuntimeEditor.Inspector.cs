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

        private void RefreshInspector()
        {
            if (_propsTmp == null) return;

            // Dispatch to the appropriate mode.
            if (_propertiesMode == PropertiesMode.Template && _selectedTemplateId >= 0)
            {
                RefreshInspectorForTemplate(_selectedTemplateId);
                return;
            }

            if (_activeBuilding == null || _activeBuilding.Template == null)
            {
                _propsTmp.text = "Select a building to view properties.";
                if (_inspectorRoot != null) _inspectorRoot.SetActive(false);
                RefreshCollidersPanel();
                return;
            }

            // Instance mode.
            _propertiesMode = PropertiesMode.Instance;
            _inspectorRoot.SetActive(true);

            var t = _activeBuilding.Template;
            var sb = new StringBuilder();
            sb.AppendLine($"<b>ID:</b> {_activeBuilding.InstanceId}");
            sb.AppendLine($"<b>Template:</b> #{t.templateId} ({t.name})");
            sb.AppendLine($"<b>Asset:</b> {t.assetPath}");
            sb.AppendLine($"<b>Solid:</b> {t.solid}");
            sb.AppendLine($"<b>Interactable:</b> {_activeBuilding.Interactable} (template {t.interactable}, override {_activeBuilding.InteractableOverride})");
            sb.AppendLine($"<b>Original:</b> {t.originalScale.x}×{t.originalScale.y} px");
            var sov = _activeBuilding.ScaleOverride;
            if (sov.x > 0 || sov.y > 0) sb.AppendLine($"<b>Scale ovr:</b> {sov.x}×{sov.y}");
            sb.AppendLine($"<b>Zone:</b> {_activeBuilding.ZoneName}");
            _propsTmp.text = sb.ToString();
            _propsTmp.richText = true;

            // Sync inspector controls without firing callbacks
            float sr = _activeBuilding.SplitRatioOverride >= 0f
                ? _activeBuilding.SplitRatioOverride : t.splitRatio;
            _splitSlider.SetValueWithoutNotify(Mathf.Clamp(sr, _splitSlider.minValue, _splitSlider.maxValue));
            if (_zBottomVal != null) _zBottomVal.text = _activeBuilding.ZBottomOffset.ToString();
            if (_zTopVal    != null) _zTopVal.text    = _activeBuilding.ZTopOffset.ToString();
            RefreshGridResolutionLabels();
            string scope = _activeBuilding.EffectiveColliderScope;
            if (_scopeBtnLabel != null) _scopeBtnLabel.text = GetScopeButtonLabel(scope);
            if (_scopeBtnImg   != null) _scopeBtnImg.color = scope == "CU" ? EditorUIHelpers.ACCENT_BG : EditorUIHelpers.BTN_NORMAL;
            if (_interactableBtnLabel != null) _interactableBtnLabel.text = GetInteractableButtonLabel(_activeBuilding.InteractableOverride);
            if (_interactableBtnImg   != null) _interactableBtnImg.color = _activeBuilding.Interactable ? EditorUIHelpers.ACCENT_BG : EditorUIHelpers.BTN_NORMAL;
            RefreshCollidersPanel();
        }

        /// <summary>
        /// Fills the Properties panel with read-only template metadata when the user
        /// clicks a slot in the picker grid (Template mode). Instance-only controls
        /// (split slider, Z offsets, grid resolution, scope toggle, Delete, Reset)
        /// are hidden so the user cannot accidentally mutate unselected instances.
        /// </summary>
        private void RefreshInspectorForTemplate(int templateId)
        {
            var tmpl = _catalog?.GetById(templateId);

            if (_inspectorRoot != null) _inspectorRoot.SetActive(false);

            if (tmpl == null)
            {
                _propsTmp.text = $"Template #{templateId} not found in catalog.";
                _propsTmp.richText = false;
                RefreshCollidersPanel();
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"<b><color=#FFAA00>Template #{tmpl.templateId}</color></b>");
            sb.AppendLine();
            sb.AppendLine($"<b>Name:</b> {tmpl.name}");
            sb.AppendLine($"<b>Asset path:</b> {tmpl.assetPath}");
            sb.AppendLine($"<b>Original size:</b> {tmpl.originalScale.x}×{tmpl.originalScale.y} px");
            sb.AppendLine($"<b>Default split:</b> {tmpl.splitRatio:F2}");
            sb.AppendLine($"<b>Solid:</b> {tmpl.solid}");
            sb.AppendLine($"<b>Interactable:</b> {tmpl.interactable}");
            sb.AppendLine($"<b>Collider scope:</b> {(string.IsNullOrEmpty(tmpl.colliderScope) ? "CG (shared)" : tmpl.colliderScope)}");
            sb.AppendLine();
            sb.AppendLine("<color=#888888><i>Drag this template onto the\nmap to place an instance.</i></color>");

            _propsTmp.text     = sb.ToString();
            _propsTmp.richText = true;

            RefreshCollidersPanel();
        }

        private static string GetScopeButtonLabel(string scope)
        {
            if (string.Equals(scope, "CU", StringComparison.OrdinalIgnoreCase)) return "Instance";
            if (string.Equals(scope, "CG", StringComparison.OrdinalIgnoreCase)) return "Shared";
            return string.IsNullOrEmpty(scope) ? "Shared" : scope;
        }

        private static string GetInteractableButtonLabel(int overrideValue)
        {
            switch (overrideValue)
            {
                case 1:  return "On";
                case 0:  return "Off";
                default: return "Inherit";
            }
        }

        private void OnSplitSliderChanged(float v)
        {
            if (_activeBuilding == null) return;
            float oldVal = _activeBuilding.SplitRatioOverride;
            if (Mathf.Approximately(oldVal, v)) return;
            ExecutePersistedEdit($"Split {v:F2}",
                () => { _activeBuilding.Apply(_activeBuilding.Template, _activeBuilding.ScaleOverride, v); RefreshCollisionFor(_activeBuilding); },
                () => { _activeBuilding.Apply(_activeBuilding.Template, _activeBuilding.ScaleOverride, oldVal); RefreshCollisionFor(_activeBuilding); });
        }

        private void AdjustZ(BuildingObject b, bool bottom, int delta)
        {
            if (b == null) return;
            int oldVal = bottom ? b.ZBottomOffset : b.ZTopOffset;
            int newVal = oldVal + delta;
            ExecutePersistedEdit($"Z{(bottom?"B":"T")} {newVal}",
                () => { if (bottom) b.ZBottomOffset = newVal; else b.ZTopOffset = newVal; RefreshInspector(); },
                () => { if (bottom) b.ZBottomOffset = oldVal; else b.ZTopOffset = oldVal; RefreshInspector(); });
        }

        private void ToggleColliderScope()
        {
            if (_activeBuilding == null) { Toast("Select a building first."); return; }
            string current = _activeBuilding.EffectiveColliderScope;
            string next    = current == "CU" ? "CG" : "CU";
            string oldOv   = _activeBuilding.ColliderScopeOverride;
            ExecutePersistedEdit($"Scope {next}",
                () => { _activeBuilding.ColliderScopeOverride = next; RefreshCollisionFor(_activeBuilding); RefreshInspector(); },
                () => { _activeBuilding.ColliderScopeOverride = oldOv; RefreshCollisionFor(_activeBuilding); RefreshInspector(); });
        }

        private void CycleInteractable()
        {
            if (_activeBuilding == null) { Toast("Select a building first."); return; }
            int cur  = _activeBuilding.InteractableOverride;
            int next = cur switch { -1 => 1, 1 => 0, _ => -1 };
            int old  = cur;
            ExecutePersistedEdit($"Interactable {GetInteractableButtonLabel(next)}",
                () => { _activeBuilding.InteractableOverride = next; RefreshInspector(); },
                () => { _activeBuilding.InteractableOverride = old; RefreshInspector(); });
        }

        private void ResetActiveBuilding()
        {
            if (_activeBuilding == null) return;
            var b = _activeBuilding;
            var oldScale = b.ScaleOverride;
            var oldSplit = b.SplitRatioOverride;
            var oldZB = b.ZBottomOffset;
            var oldZT = b.ZTopOffset;
            var oldScope = b.ColliderScopeOverride;
            var oldIa = b.InteractableOverride;
            ExecutePersistedEdit("Reset building",
                () => { b.Apply(b.Template, Vector2Int.zero, -1f); b.ZBottomOffset = 0; b.ZTopOffset = 0; b.ColliderScopeOverride = ""; b.InteractableOverride = -1; RefreshCollisionFor(b); RefreshInspector(); },
                () => { b.Apply(b.Template, oldScale, oldSplit); b.ZBottomOffset = oldZB; b.ZTopOffset = oldZT; b.ColliderScopeOverride = oldScope; b.InteractableOverride = oldIa; RefreshCollisionFor(b); RefreshInspector(); });
        }

        // ──────────────────────────────────────────────────────────────────────────
        //  PLACE / DELETE
        // ──────────────────────────────────────────────────────────────────────────

        private void PlaceBuilding(Vector3 worldPos)
        {
            if (_catalog == null) { Toast("BuildingCatalog not assigned."); return; }
            var template = _catalog.GetById(_selectedTemplateId);
            if (template == null) { Toast("Template not found."); return; }

            CacheBuildingLoader();
            int newId = NextInstanceId();
            string zoneName = DetectZoneAt(worldPos);

            BuildingObject created = null;
            ExecutePersistedEdit($"Place #{template.templateId}",
                () =>
                {
                    var go = new GameObject($"Building_{newId}_{template.name}");
                    go.transform.SetParent(_buildingsRoot, worldPositionStays: false);
                    go.transform.position = worldPos;
                    go.layer = 11; // World
                    var bObj = go.AddComponent<BuildingObject>();
                    bObj.ZoneName   = zoneName;
                    bObj.InstanceId = newId;
                    bObj.Apply(template, Vector2Int.zero, -1f);
                    var newRenderers = bObj.GetComponentsInChildren<SpriteRenderer>(true);
                    for (int i = 0; i < newRenderers.Length; i++)
                        if (newRenderers[i] != null)
                            newRenderers[i].enabled = _buildingsVisible;
                    RefreshCollisionFor(bObj);
                    // Register with the loader so it gets destroyed on the next
                    // ClearSpawned() — without this, slot switches leave the
                    // building alive and the next save sweeps it into the new
                    // slot's JSON via FindObjectsOfType<BuildingObject>().
                    _buildingLoader?.RegisterPlacedBuilding(bObj);
                    created = bObj;
                    InvalidateBuildingCache();
                    SetActiveBuilding(bObj);
                    if (_statusTmp != null) _statusTmp.text = $"Placed #{template.templateId} at ({worldPos.x:F1}, {worldPos.y:F1}) → ID {newId}";
                },
                () =>
                {
                    if (created != null)
                    {
                        created.gameObject.SetActive(false);
                        Destroy(created.gameObject);
                        created = null;
                        InvalidateBuildingCache();
                    }
                    if (_activeBuilding == null) RefreshInspector();
                });
        }

        private void RequestDeleteActiveWithConfirm()
        {
            if (_activeBuilding != null) RequestDeleteWithConfirm(_activeBuilding);
        }

        private void RequestDeleteWithConfirm(BuildingObject b)
        {
            if (b == null || b.Template == null) return;
            int templateId = b.Template.templateId;
            int refCount = CountBuildingsUsingTemplate(templateId);
            string msg = $"Delete building ID {b.InstanceId}?\n\n" +
                         $"Template: #{templateId} ({b.Template.name})\n" +
                         $"Other instances using this template: {refCount - 1}";
            ShowConfirm(msg, () => DeleteBuilding(b));
        }

        private void DeleteBuilding(BuildingObject b)
        {
            if (b == null) return;
            var go = b.gameObject;
            Vector3 savedPos = go.transform.position;
            string  savedName = go.name;
            ExecutePersistedEdit($"Delete {savedName}",
                () => { if (go) go.SetActive(false); InvalidateBuildingCache(); if (_activeBuilding == b) { _activeBuilding = null; _propertiesMode = PropertiesMode.None; RefreshInspector(); } },
                () => { if (go) { go.transform.position = savedPos; go.name = savedName; go.SetActive(true); InvalidateBuildingCache(); } });
            if (_statusTmp != null) _statusTmp.text = $"Deleted: {savedName}";
        }

        private int CountBuildingsUsingTemplate(int templateId)
        {
            int n = 0;
            var all = FindObjectsOfType<BuildingObject>();
            foreach (var b in all)
                if (b != null && b.Template != null && b.Template.templateId == templateId)
                    n++;
            return n;
        }

        private int NextInstanceId()
        {
            int max = 0;
            var all = FindObjectsOfType<BuildingObject>();
            foreach (var b in all) if (b != null && b.InstanceId > max) max = b.InstanceId;
            return max + 1;
        }

        private string DetectZoneAt(Vector3 worldPos)
        {
            var zm = FindObjectOfType<ZoneManager>();
            if (zm != null) return zm.DetectZone(worldPos);
            return "Lobby";
        }

        // ──────────────────────────────────────────────────────────────────────────
        //  PERSISTENCE — write StreamingAssets/Buildings/buildings_instances.json
        // ──────────────────────────────────────────────────────────────────────────

    }
}
