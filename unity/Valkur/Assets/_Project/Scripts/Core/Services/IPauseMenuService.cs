namespace Valkur.Core.Services
{
    /// <summary>
    /// Cross-assembly handle to the gameplay pause menu so callers in
    /// <c>Valkur.Gameplay</c> can open it without referencing
    /// <c>Valkur.UI</c> directly (the asmdef forbids that direction).
    /// Registered by <c>PauseMenuUI</c> in its Awake.
    /// </summary>
    public interface IPauseMenuService
    {
        bool IsOpen { get; }
        void OpenPause();
        void OpenLoadGame();
        void OpenOptions();
    }
}
