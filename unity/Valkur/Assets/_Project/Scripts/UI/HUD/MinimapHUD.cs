using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Gameplay;
using Valkur.Gameplay.World;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// Top-right minimap HUD. Owns the dial-style chrome (circular disc + accent
    /// ring + N/E/S/W cardinals + dual-line info plate) and instantiates a
    /// <see cref="MinimapManager"/> on the same GameObject for the actual
    /// texture rendering. Reuses the existing dot/marker/fog system unchanged
    /// — this class wraps it in a polished runtime-built UI that visually
    /// matches <see cref="DayNightClockHUD"/>.
    ///
    /// Mounted by <see cref="HUDBootstrap"/> into the <c>[UI]</c> container so
    /// <see cref="HUDVisibilityController"/> hides it automatically while any
    /// runtime editor (F1–F12) is open.
    /// </summary>
    public sealed partial class MinimapHUD : MonoBehaviour
    {
        // ── Root layout (margins; widget dimensions live in the UIBuilder) ──
        private const float MARGIN_TOP   = 24f;
        private const float MARGIN_RIGHT = 24f;

        // ── UI handles ──────────────────────────────────────────────────────
        private Canvas        _canvas;
        private RectTransform _root;
        private Image         _bgPanel;
        private Image         _bgBorder;
        private Image         _labelBand;
        private RawImage      _mapImage;
        private Image         _headingArrow;
        private TextMeshProUGUI _zoneLabel;

        // ── Runtime state ───────────────────────────────────────────────────
        private MinimapManager   _manager;
        private ZoneManager      _zoneManager;
        private PlayerController _playerController;
        private string           _lastZoneShown;
        private Vector2Int       _lastCoordsShown = new Vector2Int(int.MinValue, int.MinValue);

        // Pool of TMP labels parented to the disc — one per visible marker
        // that has a non-empty caption. Captions are vendor role initials
        // ("BS", "LJ"). Re-uses the same instances frame after frame; never
        // grows beyond the highest historic count.
        private readonly List<TextMeshProUGUI> _markerLabels = new List<TextMeshProUGUI>();
        private static readonly Color MARKER_LABEL_COLOR = new Color(1.00f, 0.96f, 0.78f, 0.95f);

        private void Awake()
        {
            // Manager lives on the same GameObject so the [UI] hierarchy stays
            // flat: a single MinimapHUD GameObject contains both the chrome and
            // the rendering pipeline.
            _manager = gameObject.GetComponent<MinimapManager>();
            if (_manager == null)
                _manager = gameObject.AddComponent<MinimapManager>();
        }

        private void Start()
        {
            BuildUI();
            if (_manager != null && _mapImage != null)
                _manager.BindRawImage(_mapImage);

            _zoneManager = FindObjectOfType<ZoneManager>();
            if (_zoneManager != null)
                _zoneManager.OnZoneChanged += HandleZoneChanged;
        }

        private void OnDestroy()
        {
            if (_zoneManager != null)
                _zoneManager.OnZoneChanged -= HandleZoneChanged;
        }

        private void LateUpdate()
        {
            UpdateHeadingArrow();
            UpdateZoneLabel();
            UpdateCoordsLabel();
            UpdateMarkerLabels();
            HandleZoomWheel();
        }

        /// <summary>
        /// Project every <see cref="MinimapMarker"/> with a non-empty caption
        /// onto the disc, positioning a small TMP label just above the marker
        /// dot. Markers outside the current view radius (or with empty labels)
        /// don't allocate a TMP — the pool only grows to the max historic
        /// active count.
        /// </summary>
        private void UpdateMarkerLabels()
        {
            if (_manager == null || _discRt == null) return;
            var pt = EntityRegistry.PlayerTransform;
            if (pt == null) { HideUnusedMarkerLabels(0); return; }

            Vector2 center = pt.position;
            float halfMap = _mapDiameter * 0.5f;
            float scale   = halfMap / Mathf.Max(0.01f, _manager.ViewRadius);
            // Reserve a small inset from the disc edge so labels don't get cut
            // off by the circular Mask. Half a label height (~6 px) is enough.
            float maxRadius = halfMap - 6f;
            float maxRadiusSqr = maxRadius * maxRadius;

            int active = 0;
            var markers = MinimapManager.Markers;
            for (int i = 0; i < markers.Count; i++)
            {
                var m = markers[i];
                if (m == null || !m.isActiveAndEnabled) continue;
                if (string.IsNullOrEmpty(m.label)) continue;

                Vector2 rel = m.WorldPosition - center;
                Vector2 uiPos = rel * scale;
                if (uiPos.sqrMagnitude > maxRadiusSqr) continue;

                var tmp = GetOrCreateMarkerLabel(active);
                if (!tmp.gameObject.activeSelf) tmp.gameObject.SetActive(true);
                if (tmp.text != m.label) tmp.text = m.label;

                // Lift the label slightly above the dot so it doesn't overlap.
                // Worth more at larger marker sizes; +8 px reads well at the
                // default vendor pixelSize (4) and stays inside the disc when
                // the dot is anywhere within maxRadius - 6 px.
                tmp.rectTransform.anchoredPosition = new Vector2(uiPos.x, uiPos.y + 8f);
                active++;
            }
            HideUnusedMarkerLabels(active);
        }

        private TextMeshProUGUI GetOrCreateMarkerLabel(int idx)
        {
            while (_markerLabels.Count <= idx)
            {
                var go = new GameObject($"MarkerLabel{_markerLabels.Count}", typeof(RectTransform));
                go.transform.SetParent(_discRt, false);
                var tmp = go.AddComponent<TextMeshProUGUI>();
                tmp.fontSize  = 9f;
                tmp.fontStyle = FontStyles.Bold;
                tmp.color     = MARKER_LABEL_COLOR;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.enableWordWrapping = false;
                tmp.raycastTarget = false;
                tmp.overflowMode  = TextOverflowModes.Overflow;

                var rt = tmp.rectTransform;
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot     = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(28f, 12f);

                _markerLabels.Add(tmp);
            }
            return _markerLabels[idx];
        }

        private void HideUnusedMarkerLabels(int activeCount)
        {
            for (int i = activeCount; i < _markerLabels.Count; i++)
            {
                var tmp = _markerLabels[i];
                if (tmp != null && tmp.gameObject.activeSelf)
                    tmp.gameObject.SetActive(false);
            }
        }

        private void HandleZoomWheel()
        {
            if (_manager == null || _bgPanel == null) return;

            // GetMouseWheelDelta() returns ~±120 per notch (legacy-style). It
            // already swallows wheel events while a modal panel is focused.
            float wheel = MouseInputManager.GetMouseWheelDelta();
            if (Mathf.Abs(wheel) < 0.1f) return;

            // Only consume the wheel when the cursor is over the disc itself
            // (not the info plate underneath). The disc has alphaHitTestMinimum-
            // Threshold so this rect check is the inscribed square — close
            // enough; anything outside the visible circle would already have
            // been raycast-rejected by the disc's alpha.
            Vector2 screenPos = MouseInputManager.GetScreenMousePosition();
            if (!RectTransformUtility.RectangleContainsScreenPoint(_bgPanel.rectTransform, screenPos, null))
                return;

            // Wheel up (positive) → zoom IN (smaller radius, see less area, more
            // detail). Match the OS convention used by Tile/Buildings editors.
            int detents = wheel > 0 ? -1 : 1;
            _manager.AdjustZoom(detents);
        }

        private void UpdateHeadingArrow()
        {
            if (_headingArrow == null) return;

            if (_playerController == null)
            {
                var p = EntityRegistry.Player;
                if (p != null) _playerController = p.GetComponent<PlayerController>();
            }
            if (_playerController == null) return;

            Vector2 facing = _playerController.FacingDirection;
            if (facing.sqrMagnitude < 0.0001f) return;

            // Arrow sprite points up (+Y) at rotation 0. atan2(y, x) returns
            // the angle from +X CCW; subtract 90° to align our +Y-default arrow
            // with the world heading.
            float deg = Mathf.Atan2(facing.y, facing.x) * Mathf.Rad2Deg - 90f;
            _headingArrow.rectTransform.localRotation = Quaternion.Euler(0f, 0f, deg);
        }

        private void UpdateZoneLabel()
        {
            if (_zoneLabel == null) return;
            if (_zoneManager == null) return;

            string zone = _zoneManager.CurrentZone;
            if (zone == _lastZoneShown) return;

            _lastZoneShown = zone;
            _zoneLabel.text = string.IsNullOrEmpty(zone) ? "—" : PrettifyZoneName(zone);
        }

        private void UpdateCoordsLabel()
        {
            if (_coordsLabel == null) return;

            var pt = EntityRegistry.PlayerTransform;
            if (pt == null) return;

            // Whole-tile coords match the world grid (1 unit = 1 tile in Valkur),
            // so the player can correlate the HUD value with the in-world cells
            // they're stepping over. Round half-away-from-zero so −0.4 → 0, not −1.
            Vector3 p = pt.position;
            int tx = Mathf.FloorToInt(p.x);
            int ty = Mathf.FloorToInt(p.y);
            if (tx == _lastCoordsShown.x && ty == _lastCoordsShown.y) return;

            _lastCoordsShown = new Vector2Int(tx, ty);
            _coordsLabel.text = $"X {tx}    Y {ty}";
        }

        private void HandleZoneChanged(string oldZone, string newZone)
        {
            // Forget explored cells from the previous zone so the fog of war
            // doesn't leak between maps. ClearFog() is cheap (HashSet.Clear).
            if (_manager != null) _manager.ClearFog();
        }

        private static string PrettifyZoneName(string raw)
        {
            // "lobby" → "Lobby"; "dungeon_001" → "Dungeon 001". Keeps the label
            // readable when zone identifiers are filenames or snake_case.
            var chars = raw.Replace('_', ' ').ToCharArray();
            bool nextUpper = true;
            for (int i = 0; i < chars.Length; i++)
            {
                if (chars[i] == ' ') { nextUpper = true; continue; }
                if (nextUpper) { chars[i] = char.ToUpperInvariant(chars[i]); nextUpper = false; }
            }
            return new string(chars);
        }
    }
}
