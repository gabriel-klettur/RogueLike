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
        /// Each frame: position the cyan split-ratio line over the active building.
        /// The line sits at the boundary between the bottom (behind player) and top
        /// (in front of player) render layers — identical to Python's split_tool_view.py.
        /// </summary>
        private void UpdateSplitLine()
        {
            if (_splitLineRt == null) return;
            if (!_buildingsVisible)
            {
                _splitLineRt.gameObject.SetActive(false);
                if (_splitHandleRt != null) _splitHandleRt.gameObject.SetActive(false);
                return;
            }
            if (_activeBuilding == null || !_activeBuilding.TryGetWorldRect(out var rect))
            {
                _splitLineRt.gameObject.SetActive(false);
                if (_splitHandleRt != null) _splitHandleRt.gameObject.SetActive(false);
                return;
            }

            // Effective split ratio: instance override (if >= 0) else template default
            float sr = _activeBuilding.SplitRatioOverride >= 0f
                ? _activeBuilding.SplitRatioOverride
                : (_activeBuilding.Template != null ? _activeBuilding.Template.splitRatio : 0.5f);

            // Split line world Y = bottom of building + bottom-portion height
            // bottomFraction = (1 - sr)  because sr is the TOP fraction (see BuildingObject docs)
            float worldSplitY = rect.yMin + rect.height * (1f - sr);

            var cam = Camera.main;
            if (cam == null)
            {
                _splitLineRt.gameObject.SetActive(false);
                if (_splitHandleRt != null) _splitHandleRt.gameObject.SetActive(false);
                return;
            }

            // Width in canvas space = width of the building rect projected to screen
            Vector3 leftScreen  = cam.WorldToScreenPoint(new Vector3(rect.xMin, worldSplitY, 0f));
            Vector3 rightScreen = cam.WorldToScreenPoint(new Vector3(rect.xMax, worldSplitY, 0f));
            Vector2 leftCanvas  = ScreenToCanvasPos(leftScreen);
            Vector2 rightCanvas = ScreenToCanvasPos(rightScreen);
            float canvasWidth   = Vector2.Distance(leftCanvas, rightCanvas);

            Vector3 centerScreen = cam.WorldToScreenPoint(
                new Vector3(rect.center.x, worldSplitY, 0f));
            Vector2 canvasCenter = ScreenToCanvasPos(centerScreen);

            _splitLineRt.gameObject.SetActive(true);
            _splitLineRt.anchoredPosition = canvasCenter;
            _splitLineRt.sizeDelta = new Vector2(canvasWidth, 3f);

            // Handle — same center point, highlighted while dragging or cursor near it
            if (_splitHandleRt != null)
            {
                _splitHandleRt.gameObject.SetActive(true);
                _splitHandleRt.anchoredPosition = canvasCenter;

                // Highlight: white when dragging, yellow on hover, cyan otherwise
                if (_splitHandleImg != null)
                    _splitHandleImg.color = _splitDragging
                        ? Color.white
                        : _splitHovering
                            ? new Color(1f, 0.9f, 0f, 1f)           // yellow on hover
                            : new Color(0f, 200f / 255f, 1f, 1f);   // cyan normal
            }
        }

        private void UpdateIdLabel()
        {
            if (_idLabelRt == null) return;
            if (!_buildingsVisible) { _idLabelRt.gameObject.SetActive(false); return; }
            if (_activeBuilding == null) { _idLabelRt.gameObject.SetActive(false); return; }
            if (!_activeBuilding.TryGetWorldRect(out var rect)) { _idLabelRt.gameObject.SetActive(false); return; }
            var cam = Camera.main;
            if (cam == null) { _idLabelRt.gameObject.SetActive(false); return; }
            _idLabelRt.gameObject.SetActive(true);
            _idLabelTmp.text = $"ID {_activeBuilding.InstanceId}";
            // Place just above the top-left corner of the yellow frame (outside the frame)
            Vector3 worldTopLeft = new Vector3(rect.xMin, rect.yMax, 0f);
            Vector3 screen = cam.WorldToScreenPoint(worldTopLeft);
            // pivot=(0,1): label's top-left aligns to worldTopLeft; subtract ~3px so it sits
            // flush against the outside top edge of the frame with a tiny gap
            _idLabelRt.anchoredPosition = ScreenToCanvasPos(screen) + new Vector2(0f, 3f);
        }

        private Vector2 ScreenToCanvasPos(Vector3 screenPos)
        {
            if (_canvas == null) return Vector2.zero;
            // ScreenSpaceOverlay: pass null camera — works with any CanvasScaler config.
            var canvasRt = _canvas.GetComponent<RectTransform>();
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRt, new Vector2(screenPos.x, screenPos.y), null, out Vector2 local))
            {
                return local;
            }
            return Vector2.zero;
        }

        private void Toast(string msg)
        {
            if (_statusTmp != null) _statusTmp.text = msg;
            Debug.Log($"[BuildingsEditor] {msg}");
        }

        // ──────────────────────────────────────────────────────────────────────────
        //  COLLIDER EDITING (Colliders panel)
        // ──────────────────────────────────────────────────────────────────────────

        private enum CollBrushMode { Off, Solid, Walk, Erase }
        private enum ColliderAuthoringScope { CG, CU }
        private const string CollTilePrefix = "CollTile_";
        private const string PooledCollTilePrefix = "_PooledCollTile_";

        private sealed class ColliderGridData
        {
            public int width;
            public int height;
            public string[][] collision;
            public Vector2Int gridRefSize;
        }

        private sealed class ActiveColliderGridSession
        {
            public int BuildingId;
            public int InstanceId;
            public string ImageKey;
            public ColliderAuthoringScope Scope;
            public Vector2Int EffectivePixelSize;
            public ColliderGridData WorkingGrid;
        }

        private sealed class ColliderPaintStroke
        {
            public bool Active;
            public ColliderAuthoringScope Scope;
            public string ImageKey;
            public int InstanceId;
            public ColliderGridData Before;
            public bool Changed;
        }

        private bool          _collidersVisible;
        private CollBrushMode _collBrushMode = CollBrushMode.Off;
        // Remembered action for when the brush is toggled back ON. Only Solid (=#)
        // and Walk (=.) are valid actions in the redesigned UX. The Off/Erase
        // values of CollBrushMode are kept internally for back-compat with
        // HandleColliderPaint, but Erase is no longer reachable from the UI.
        private CollBrushMode _lastBrushAction = CollBrushMode.Solid;
        private int           _collBrushSize = 1;
        private bool          _colliderDataLoaded;
        private readonly Dictionary<string, ColliderGridData> _colliderImageStore =
            new Dictionary<string, ColliderGridData>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ColliderGridData> _savedColliderImageStore =
            new Dictionary<string, ColliderGridData>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, ColliderGridData> _colliderInstanceStore =
            new Dictionary<int, ColliderGridData>();
        private readonly Dictionary<int, ColliderGridData> _savedColliderInstanceStore =
            new Dictionary<int, ColliderGridData>();
        private ActiveColliderGridSession _activeColliderSession;
        private readonly ColliderPaintStroke _colliderStroke = new ColliderPaintStroke();

    }
}
