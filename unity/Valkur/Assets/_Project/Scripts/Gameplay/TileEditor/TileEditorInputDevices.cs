using UnityEngine.InputSystem;

namespace Valkur.Gameplay.TileEditor
{
    public static class TileEditorInputDevices
    {
        public static void EnsureAvailable()
        {
            if (Mouse.current == null)
                InputSystem.AddDevice<Mouse>();

            if (Keyboard.current == null)
                InputSystem.AddDevice<Keyboard>();
        }
    }
}
