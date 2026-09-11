using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Core.Input;
using Valkur.Data;
using Valkur.Gameplay.World;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// The world map: the minimap's renderer at the size of the screen, with names on the
    /// zones, a legend, and the one thing a dial cannot offer — the player can put a pin on it.
    ///
    /// <para><b>An overlay, not a pause.</b> The game keeps running underneath, the way an ARPG
    /// overlay map does: the player can walk while reading it. The backdrop is a raycast
    /// target, so a click on the map never becomes a click in the world — the combat poll skips
    /// any press over UI — and a click on the backdrop outside the frame closes it.</para>
    ///
    /// <para><b>Escape is claimed while open</b> (<see cref="EscapeOwnership"/>), so closing
    /// the map does not also open the General Editor launcher that Escape otherwise opens.</para>
    ///
    /// <para><b>The same facts as the dial.</b> It draws from the <see cref="MinimapScene"/>
    /// the HUD already collected this frame and the same baked terrain and fog, so the two maps
    /// cannot disagree about where anything is.</para>
    /// </summary>
    public sealed partial class WorldMapPanel : MonoBehaviour
    {
        private const float MIN_HALF_HEIGHT = 18f;
        private const float MAX_HALF_HEIGHT = 190f;
        private const float DEFAULT_HALF_HEIGHT = 62f;
        private const float DRAG_THRESHOLD = 5f;

        private MinimapStyle _style;
        private MinimapView _view;
        private Material _material;
        private Material _additive;
        private CanvasGroup _group;
        private ZoneManager _zones;

        private bool _open;
        private float _openT;
        private float _targetHalfHeight = DEFAULT_HALF_HEIGHT;
        private bool _pressing;
        private bool _dragging;
        private Vector2 _pressScreen;
        private Vector2 _pressCentre;
        private float _nextZoneScan;
        private bool _zoomAnchored;
        private Vector2 _zoomAnchorLocal;
        private Vector2 _zoomAnchorWorld;

        private readonly List<TextMeshProUGUI> _zoneLabels = new List<TextMeshProUGUI>();
        private readonly List<ZoneLabelInfo> _zoneInfo = new List<ZoneLabelInfo>();

        private struct ZoneLabelInfo
        {
            public string Name;
            public Vector2 Centre;
            public float Explored;
        }

        /// <summary>True while the map is on screen.</summary>
        public bool IsOpen => _open;

        /// <summary>The map's view.</summary>
        public MinimapView View => _view;

        /// <summary>Build the panel (closed) under <paramref name="parent"/>.</summary>
        public static WorldMapPanel Create(Transform parent, MinimapHUD hud, Shader composite, Material additive)
        {
            var go = new GameObject("WorldMapPanel");
            go.transform.SetParent(parent, false);
            var panel = go.AddComponent<WorldMapPanel>();
            panel.Init(hud, composite, additive);
            return panel;
        }

        private void Init(MinimapHUD hud, Shader composite, Material additive)
        {
            _style = MinimapStyle.Active;
            _material = new Material(composite) { name = "MinimapComposite (world map)" };
            _additive = additive;
            BuildUI();
            _view = new MinimapView(_mapImage, _fxUnder, _glyphs, _fxOver, _material, circle: false)
            {
                GlyphScale = 1.3f,
                EdgeInset = 14f,
                RememberedLift = 0.7f,
                HalfHeightWorld = DEFAULT_HALF_HEIGHT,
            };
            _canvasGo.SetActive(false);
        }

        private void OnDestroy()
        {
            EscapeOwnership.Release(this);
            if (_material != null) Destroy(_material);
        }

        private void OnDisable()
        {
            // Hidden with the HUD (an editor opened): never leave Escape claimed behind.
            if (_open) Close();
        }

        // ── Open / close ────────────────────────────────────────────────────

        public void Open(Vector2 centre)
        {
            if (_open) return;
            _open = true;
            _openT = 0f;
            _view.Centre = centre;
            _targetHalfHeight = DEFAULT_HALF_HEIGHT;
            _view.HalfHeightWorld = _targetHalfHeight * 1.12f;
            _canvasGo.SetActive(true);
            EscapeOwnership.Claim(this);
            _nextZoneScan = 0f;
            RefreshZoneInfo();
        }

        public void Close()
        {
            if (!_open) return;
            _open = false;
            _pressing = _dragging = false;
            EscapeOwnership.Release(this);
            if (_canvasGo != null) _canvasGo.SetActive(false);
        }

        // ── Per frame ───────────────────────────────────────────────────────

        private void Update()
        {
            if (!_open) return;
            float dt = Time.unscaledDeltaTime;

            if (KeyboardInputManager.WasEscapePressedThisFrame()) { Close(); return; }

            _openT = Mathf.Min(1f, _openT + dt / 0.18f);
            float e = 1f - (1f - _openT) * (1f - _openT);
            _group.alpha = e;
            _frame.localScale = Vector3.one * Mathf.Lerp(0.965f, 1f, e);

            HandlePointer();
            _view.HalfHeightWorld = MinimapProjection.Damp(_view.HalfHeightWorld, _targetHalfHeight, 0.06f, dt);
            ApplyZoomAnchor();
        }

        private void ApplyZoomAnchor()
        {
            if (!_zoomAnchored) return;
            // The offset from the centre to the anchor depends only on the zoom, so re-seating
            // the centre each frame keeps the anchored ground fixed under the cursor while the
            // zoom eases toward its target.
            Vector2 offset = _view.LocalToWorld(_zoomAnchorLocal) - _view.Centre;
            _view.Centre = _zoomAnchorWorld - offset;
            if (Mathf.Abs(_view.HalfHeightWorld - _targetHalfHeight) < 0.01f) _zoomAnchored = false;
        }

        /// <summary>Draw with the HUD's frame data. Called by the HUD after it collected the scene.</summary>
        public void Draw(in MinimapFrame frame, MinimapScene scene)
        {
            if (!_open) return;
            _view.Draw(in frame, scene, null);

            if (Time.unscaledTime >= _nextZoneScan) RefreshZoneInfo();
            LayoutZoneLabels();
            if (_titleZone != null && _zones != null)
                _titleZone.text = string.IsNullOrEmpty(_zones.CurrentZone) ? string.Empty : MinimapHUD.DisplayZoneName(_zones.CurrentZone);
        }

        private void HandlePointer()
        {
            Vector2 screen = MouseInputManager.GetScreenMousePosition();
            var rt = _mapImage.rectTransform;
            bool over = RectTransformUtility.RectangleContainsScreenPoint(rt, screen, null);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, screen, null, out var local);
            local -= rt.rect.center;

            float wheel = MouseInputManager.GetMouseWheelDelta();
            if (over && Mathf.Abs(wheel) > 0.1f)
            {
                // Zoom about the cursor: the ground under the pointer stays under the pointer for
                // the whole eased zoom, not just at its end — see ApplyZoomAnchor.
                _zoomAnchored = true;
                _zoomAnchorLocal = local;
                _zoomAnchorWorld = _view.LocalToWorld(local);
                _targetHalfHeight = Mathf.Clamp(_targetHalfHeight * (wheel > 0 ? 1f / 1.2f : 1.2f), MIN_HALF_HEIGHT, MAX_HALF_HEIGHT);
            }

            if (over && MouseInputManager.WasLeftMouseButtonPressedThisFrame())
            {
                _zoomAnchored = false;
                _pressing = true;
                _dragging = false;
                _pressScreen = screen;
                _pressCentre = _view.Centre;
            }

            if (_pressing && MouseInputManager.IsLeftMouseButtonPressed())
            {
                Vector2 delta = screen - _pressScreen;
                if (!_dragging && delta.magnitude > DRAG_THRESHOLD) _dragging = true;
                if (_dragging)
                {
                    float scale = _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;
                    float worldPerUnit = 1f / Mathf.Max(1e-4f, _view.UnitsPerWorld);
                    _view.Centre = _pressCentre - delta / scale * worldPerUnit;
                }
            }

            if (_pressing && MouseInputManager.WasLeftMouseButtonReleasedThisFrame())
            {
                if (!_dragging && over) MinimapWaypoint.Set(_view.LocalToWorld(local));
                _pressing = false;
                _dragging = false;
            }

            if (over && MouseInputManager.WasRightMouseButtonReleasedThisFrame()) MinimapWaypoint.Clear();

            UpdateCursorInfo(over, local);
        }

        private float _nextCursorInfo;

        private void UpdateCursorInfo(bool over, Vector2 local)
        {
            if (_cursorInfo == null || Time.unscaledTime < _nextCursorInfo) return;
            _nextCursorInfo = Time.unscaledTime + 0.1f;
            if (!over || _dragging) { if (_cursorInfo.text.Length > 0) _cursorInfo.text = string.Empty; return; }

            Vector2 w = _view.LocalToWorld(local);
            var mgr = MinimapManager.Instance;
            bool known = mgr == null || mgr.IsExplored(w);
            string zone = _zones != null ? _zones.DetectZone(w) : null;
            string where = !known ? "Sin explorar" : MinimapHUD.DisplayZoneName(zone);
            string pct = string.Empty;
            if (known && !string.IsNullOrEmpty(zone))
                for (int i = 0; i < _zoneInfo.Count; i++)
                    if (_zoneInfo[i].Name == zone) { pct = "   ·   explorado " + Mathf.RoundToInt(_zoneInfo[i].Explored * 100f) + " %"; break; }
            string head = _view.TryPick(local, out var picked, out _) ? picked + "   ·   " : string.Empty;
            _cursorInfo.text = head + where + "   ·   " + Mathf.FloorToInt(w.x) + ", " + Mathf.FloorToInt(w.y) + pct;
        }

        /// <summary>Put the view back on the player.</summary>
        public void Recentre()
        {
            _zoomAnchored = false;
            var p = Valkur.Core.EntityRegistry.PlayerTransform;
            if (p != null) _view.Centre = p.position;
        }

        // ── Zone names ──────────────────────────────────────────────────────

        private void RefreshZoneInfo()
        {
            _nextZoneScan = Time.unscaledTime + 2f;
            if (_zones == null) _zones = FindObjectOfType<ZoneManager>();
            _zoneInfo.Clear();
            if (_zones == null || _zones.IsDetectionSuspended) return;

            var mgr = MinimapManager.Instance;
            foreach (var z in _zones.GetZonesSnapshot())
            {
                var rect = _zones.GetZoneRect(z);
                float explored = mgr != null && mgr.FogOfWarEnabled ? mgr.Fog.ExploredFraction(rect) : 1f;
                _zoneInfo.Add(new ZoneLabelInfo { Name = z.zoneName, Centre = rect.center, Explored = explored });
            }
        }

        private void LayoutZoneLabels()
        {
            int used = 0;
            Vector2 half = _view.HalfSize;
            // Names only where the player has been — an unexplored zone's name is a spoiler, and
            // "zone_250_0" is not a name. Faded out when zoomed so far in that one fills the map.
            float zoomFade = Mathf.Clamp01((_view.HalfHeightWorld - 22f) / 18f);
            var pt = Valkur.Core.EntityRegistry.PlayerTransform;
            Vector2 playerLocal = pt != null ? _view.WorldToLocal(pt.position) : new Vector2(1e6f, 1e6f);
            for (int i = 0; i < _zoneInfo.Count; i++)
            {
                var z = _zoneInfo[i];
                if (z.Explored < 0.04f || z.Name.StartsWith("zone_")) continue;
                Vector2 local = _view.WorldToLocal(z.Centre);
                if (Mathf.Abs(local.x) > half.x - 40f || Mathf.Abs(local.y) > half.y - 14f) continue;

                var label = ZoneLabel(used++);
                label.text = MinimapHUD.PrettifyZoneName(z.Name).ToUpperInvariant();
                label.rectTransform.anchoredPosition = local;
                var c = _style.ringHighlight;
                // A name under the player's own arrow steps back: the arrow is the one glyph the
                // player must never have to read through text to find.
                float underPlayer = Mathf.Clamp01(((local - playerLocal).magnitude - 30f) / 50f);
                c.a = 0.85f * zoomFade * Mathf.Lerp(0.3f, 1f, underPlayer);
                label.color = c;
            }
            for (int i = used; i < _zoneLabels.Count; i++)
                if (_zoneLabels[i].gameObject.activeSelf) _zoneLabels[i].gameObject.SetActive(false);
        }

        private TextMeshProUGUI ZoneLabel(int index)
        {
            while (_zoneLabels.Count <= index)
            {
                var t = NewLabel(_labelLayer, "ZoneName" + _zoneLabels.Count, 13f, FontStyles.Bold, Color.white);
                t.alignment = TextAlignmentOptions.Center;
                t.enableWordWrapping = false;
                t.characterSpacing = 6f;
                t.outlineWidth = 0.25f;
                t.outlineColor = new Color32(0, 0, 0, 220);
                var rt = t.rectTransform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(220f, 20f);
                _zoneLabels.Add(t);
            }
            var l = _zoneLabels[index];
            if (!l.gameObject.activeSelf) l.gameObject.SetActive(true);
            return l;
        }
    }
}
