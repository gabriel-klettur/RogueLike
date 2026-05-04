using UnityEngine;
using Valkur.Core.Input;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.Editors
{
    /// <summary>
    /// Shared helper used by every runtime editor's "double-click → centre on
    /// zone" handler. Resolves the zone under the screen cursor through
    /// <see cref="ZoneManager"/> and asks <see cref="CameraSetup"/> to frame
    /// it. Returns the framed zone name (or null if no zone was under the
    /// cursor) so callers can surface a status message.
    /// </summary>
    public static class EditorZoneFraming
    {
        /// <summary>
        /// Frame the zone at the current screen cursor position. Returns the
        /// zone name on success, or null when no zone is under the cursor /
        /// required services are missing.
        /// </summary>
        public static string TryFrameZoneAtCursor(ZoneManager zoneManager, Camera camera = null)
        {
            if (zoneManager == null) return null;
            var cam = camera != null ? camera : Camera.main;
            if (cam == null) return null;

            Vector3 mouseWorld = cam.ScreenToWorldPoint(MouseInputManager.GetScreenMousePosition());
            float tileSize = Mathf.Max(0.01f, zoneManager.TileSize);
            var tilePos = new Vector2Int(
                Mathf.FloorToInt(mouseWorld.x / tileSize),
                Mathf.FloorToInt(mouseWorld.y / tileSize));

            if (!zoneManager.TryGetZoneAtTile(tilePos, out var zone)) return null;

            var rect = zoneManager.GetZoneRect(zone);
            if (rect.width <= 0 || rect.height <= 0) return null;

            var camSetup = CameraSetup.Instance;
            if (camSetup == null) return null;
            camSetup.FrameRect(rect);
            return zone.zoneName;
        }
    }
}
