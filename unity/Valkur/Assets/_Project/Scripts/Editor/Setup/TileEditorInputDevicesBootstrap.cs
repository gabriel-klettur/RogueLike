using UnityEditor;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Editor
{
    [InitializeOnLoad]
    internal static class TileEditorInputDevicesBootstrap
    {
        static TileEditorInputDevicesBootstrap()
        {
            TileEditorInputDevices.EnsureAvailable();
            EditorApplication.update += EnsureDevicesForEditModeTests;
        }

        private static void EnsureDevicesForEditModeTests()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            TileEditorInputDevices.EnsureAvailable();
        }
    }
}
