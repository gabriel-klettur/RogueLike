using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay.Editors;
using Valkur.Gameplay.Inventory;

namespace Valkur.Gameplay.Items
{
    /// <summary>
    /// Items Editor — Instances panel (bottom-right).
    /// Mirrors Python <c>roguelike_editors/items/ui/instances_view.py</c>:
    ///  • Lists every active <see cref="WorldPickup"/> in the scene as a row
    ///    "<item-name> ×<qty>  (x,y)".
    ///  • LMB short-press = select (drives Properties + picker selection).
    ///  • LMB press-and-hold (>0.25 s) = pan the Cinemachine camera to the drop
    ///    while held. Release = re-attach to the player.
    ///  • Refreshes at 2 Hz to track spawn/destroy externally to the editor.
    /// </summary>
    public partial class ItemsRuntimeEditor
    {
        // ── Refresh ──

        private void MaybeRefreshInstances()
        {
            if (Time.unscaledTime - _lastInstanceRefresh < INSTANCE_REFRESH_INTERVAL) return;
            ForceRefreshInstances();
        }

        private void ForceRefreshInstances()
        {
            _lastInstanceRefresh = Time.unscaledTime;
            CollectInstances();
            RebuildInstancesList();
        }

        private void CollectInstances()
        {
            _instances.Clear();
            var found = Object.FindObjectsOfType<WorldPickup>(includeInactive: false);
            for (int i = 0; i < found.Length; i++)
            {
                if (found[i] != null) _instances.Add(found[i]);
            }
            // Stable ordering: by item name then position.
            _instances.Sort((a, b) =>
            {
                string an = a.Item != null ? (a.Item.displayName ?? a.Item.itemId ?? "") : "";
                string bn = b.Item != null ? (b.Item.displayName ?? b.Item.itemId ?? "") : "";
                int c = string.Compare(an, bn, System.StringComparison.OrdinalIgnoreCase);
                if (c != 0) return c;
                Vector3 pa = a.transform.position, pb = b.transform.position;
                if (pa.x != pb.x) return pa.x.CompareTo(pb.x);
                return pa.y.CompareTo(pb.y);
            });
        }

        // ── Rendering ──

        private void RebuildInstancesList()
        {
            var content = _uiRefs.InstancesListContent;
            if (content == null) return;

            // Destroy previous rows but keep the hint child (created at build time).
            // Use DestroyImmediate when not in Play Mode (Destroy is deferred and
            // would leave stale rows visible during EditMode test runs).
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                var child = content.GetChild(i).gameObject;
                if (child == _uiRefs.InstancesHint?.gameObject) continue;
                if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
            }

            bool hasInstances = _instances.Count > 0;
            if (_uiRefs.InstancesHint != null)
                _uiRefs.InstancesHint.gameObject.SetActive(!hasInstances);

            for (int i = 0; i < _instances.Count; i++)
            {
                var pickup = _instances[i];
                if (pickup == null || pickup.Item == null) continue;
                CreateInstanceRow(content, pickup);
            }
        }

        private void CreateInstanceRow(RectTransform parent, WorldPickup pickup)
        {
            var go = new GameObject("InstanceRow", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().preferredHeight = 26f;

            var bg = go.AddComponent<Image>();
            bool selected = (pickup == _selectedInstance);
            bg.color = selected ? EditorUIHelpers.SLOT_SELECTED : EditorUIHelpers.SLOT_BG;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = bg;
            var colors = btn.colors;
            colors.normalColor      = bg.color;
            colors.highlightedColor = EditorUIHelpers.SLOT_HOVER;
            colors.pressedColor     = EditorUIHelpers.SLOT_SELECTED;
            btn.colors = colors;

            // Label
            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, false);
            EditorUIHelpers.StretchFill(labelGo);
            var rt = labelGo.GetComponent<RectTransform>();
            rt.offsetMin = new Vector2(8, 0); rt.offsetMax = new Vector2(-8, 0);
            var tmp = labelGo.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = 11f;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.color = EditorUIHelpers.TEXT_PRIMARY;
            var pos = pickup.transform.position;
            string itemName = pickup.Item.displayName ?? pickup.Item.itemId;
            string qtyStr   = pickup.Quantity > 1 ? $" ×{pickup.Quantity}" : "";
            tmp.text = $"{itemName}{qtyStr}   ({pos.x:F1}, {pos.y:F1})";

            // Click → select. Hold (long-press) is detected by editor Update().
            var captured = pickup;
            btn.onClick.AddListener(() => OnInstanceClicked(captured));

            // Track press / release for hold-to-focus camera.
            var trigger = go.AddComponent<EventTrigger>();
            var down = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
            down.callback.AddListener(_ => BeginInstanceHold(captured));
            trigger.triggers.Add(down);

            var up = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
            up.callback.AddListener(_ => EndInstanceHold());
            trigger.triggers.Add(up);

            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => EndInstanceHold());
            trigger.triggers.Add(exit);
        }

        // ── Interaction ──

        private void OnInstanceClicked(WorldPickup pickup)
        {
            SetActiveInstance(pickup);
        }

        /// <summary>
        /// Set the active world pickup — drives the picker selection (catalog highlight),
        /// the Properties panel (instance actions), and the instances list visual state.
        /// Called from both the world-map click handler and the instances-row click.
        /// </summary>
        public void SetActiveInstance(WorldPickup pickup)
        {
            if (pickup == null) return;
            _selectedInstance = pickup;
            if (pickup.Item != null) _selectedItemId = pickup.Item.itemId;
            RefreshPicker();
            RefreshProperties();
            RebuildInstancesList();
            var pos = pickup.transform.position;
            SetStatus($"Selected '{pickup.Item?.displayName ?? pickup.Item?.itemId}' at ({pos.x:F1},{pos.y:F1}).");
        }

        private void BeginInstanceHold(WorldPickup pickup)
        {
            _holdingInstance = pickup;
            _holdStartTime = Time.unscaledTime;
        }

        private void EndInstanceHold()
        {
            _holdingInstance = null;
            ReleaseCameraFocus();
        }

        private void HandleInstanceHoldFocus()
        {
            if (_holdingInstance == null) return;
            if (Time.unscaledTime - _holdStartTime < HOLD_THRESHOLD) return;
            FocusCameraOn(_holdingInstance.transform.position);
        }

        private void FocusCameraOn(Vector3 worldPos)
        {
            var camSetup = Valkur.Gameplay.CameraSetup.Instance;
            if (camSetup == null) return;
            if (!_cameraDetachedByUs)
            {
                camSetup.DetachFollow();
                _cameraDetachedByUs = true;
            }
            var t = camSetup.GetDetachedTransform();
            if (t == null) return;
            var pos = t.position;
            t.position = new Vector3(worldPos.x, worldPos.y, pos.z);
        }

        private void ReleaseCameraFocus()
        {
            if (!_cameraDetachedByUs) return;
            var camSetup = Valkur.Gameplay.CameraSetup.Instance;
            if (camSetup != null) camSetup.ReattachFollow();
            _cameraDetachedByUs = false;
        }
    }
}
