using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Valkur.Core;

namespace Valkur.Editor
{
    /// <summary>
    /// Forces the Game View onto one of the shipped <see cref="DisplaySettings.Presets"/>.
    ///
    /// WHY THIS EXISTS: <c>Screen.SetResolution</c> is a no-op inside the
    /// Editor, so Options → Video cannot move the Game View. In the Editor the
    /// Game View size is the resolution, and "Free Aspect" hands the camera an
    /// arbitrary window that <see cref="AspectRatioEnforcer"/> then has to
    /// letterbox. The enforcer now quantises to an exact integer ratio, so a
    /// free-aspect window is still seam-free — but it wastes rows on bars and
    /// it makes the ortho-snap ladder shift every time the panel is resized.
    /// Pinning a fixed 2:1 size is what a developer actually wants day to day.
    ///
    /// The Game View size API is internal, so this is reflection. Every step
    /// is guarded: a Unity upgrade that moves the API degrades to one warning,
    /// never an exception in the middle of someone's session.
    /// </summary>
    public static class GameViewAspectMenu
    {
        private const string MenuRoot = "Valkur/Display/Game View ";

        [MenuItem(MenuRoot + "1280 x 640")]
        private static void Set1280x640() => SetGameViewSize(1280, 640);

        [MenuItem(MenuRoot + "1600 x 800")]
        private static void Set1600x800() => SetGameViewSize(1600, 800);

        [MenuItem(MenuRoot + "1920 x 960")]
        private static void Set1920x960() => SetGameViewSize(1920, 960);

        [MenuItem("Valkur/Display/Report Viewport Alignment")]
        private static void ReportViewportAlignment()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                Debug.LogWarning("[GameViewAspect] No Camera.main — enter Play Mode first.");
                return;
            }

            int pw = cam.pixelWidth, ph = cam.pixelHeight;
            bool exact = ph > 0 && pw == Mathf.RoundToInt(ph * DisplaySettings.TargetAspect);
            // Screen pixels covered by one 32-PPU tile texel on each axis. A
            // fractional value is what lets a quad edge land mid-pixel.
            float texY = ph / (2f * 32f * cam.orthographicSize);
            float texX = pw / (2f * 32f * cam.orthographicSize * cam.aspect);

            Debug.Log(
                $"[GameViewAspect] screen {Screen.width}x{Screen.height} | viewport {pw}x{ph} | " +
                $"aspect {cam.aspect:F8} ({(exact ? "EXACT" : "DRIFTED")}) | ortho {cam.orthographicSize:F6} | " +
                $"screen px per tile texel: X {texX:F4}, Y {texY:F4}");
        }

        private static void SetGameViewSize(int width, int height)
        {
            try
            {
                var editorAsm  = typeof(UnityEditor.Editor).Assembly;
                var sizesType  = editorAsm.GetType("UnityEditor.GameViewSizes");
                var sizeType   = editorAsm.GetType("UnityEditor.GameViewSize");
                var kindType   = editorAsm.GetType("UnityEditor.GameViewSizeType");
                var viewType   = editorAsm.GetType("UnityEditor.GameView");
                if (sizesType == null || sizeType == null || kindType == null || viewType == null)
                { WarnUnavailable("type lookup"); return; }

                var singleton = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
                var instance  = singleton.GetProperty("instance")?.GetValue(null, null);
                var group     = sizesType.GetProperty("currentGroup")?.GetValue(instance, null);
                if (group == null) { WarnUnavailable("currentGroup"); return; }

                int index = FindSizeIndex(group, width, height);
                if (index < 0)
                {
                    var ctor = sizeType.GetConstructor(new[] { kindType, typeof(int), typeof(int), typeof(string) });
                    if (ctor == null) { WarnUnavailable("GameViewSize ctor"); return; }
                    // GameViewSizeType.FixedResolution == 1 in every 2022.3 build.
                    var size = ctor.Invoke(new object[]
                        { Enum.ToObject(kindType, 1), width, height, $"Valkur {width}x{height}" });
                    group.GetType().GetMethod("AddCustomSize")?.Invoke(group, new[] { size });
                    index = FindSizeIndex(group, width, height);
                }
                if (index < 0) { WarnUnavailable("size index"); return; }

                var window = EditorWindow.GetWindow(viewType, false, null, false);
                if (window == null) { WarnUnavailable("Game View window"); return; }

                var select = viewType.GetMethod("SizeSelectionCallback",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (select == null) { WarnUnavailable("SizeSelectionCallback"); return; }
                select.Invoke(window, new object[] { index, null });
                window.Repaint();

                Debug.Log($"[GameViewAspect] Game View set to {width}x{height} (exact 2:1).");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[GameViewAspect] Could not set the Game View size: {e.Message}. " +
                                 "Pick the size manually from the Game View's aspect dropdown.");
            }
        }

        private static int FindSizeIndex(object group, int width, int height)
        {
            var groupType = group.GetType();
            var total = groupType.GetMethod("GetTotalCount");
            var get   = groupType.GetMethod("GetGameViewSize");
            if (total == null || get == null) return -1;

            int count = (int)total.Invoke(group, null);
            for (int i = 0; i < count; i++)
            {
                var size = get.Invoke(group, new object[] { i });
                if (size == null) continue;
                var t = size.GetType();
                var w = t.GetProperty("width")?.GetValue(size, null);
                var h = t.GetProperty("height")?.GetValue(size, null);
                if (w is int iw && h is int ih && iw == width && ih == height) return i;
            }
            return -1;
        }

        private static void WarnUnavailable(string step)
        {
            Debug.LogWarning($"[GameViewAspect] Game View size API unavailable at step '{step}' " +
                             "(internal Unity API moved). Pick the size manually from the Game View's " +
                             "aspect dropdown.");
        }
    }
}
