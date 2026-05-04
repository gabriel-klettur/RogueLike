namespace Valkur.Core.Services
{
    /// <summary>
    /// Cross-assembly handle to the F9 DebugHUD so callers in
    /// <c>Valkur.Gameplay</c> can toggle it without referencing
    /// <c>Valkur.UI</c> directly. Registered by <c>DebugHUD</c> in Start.
    /// </summary>
    public interface IDebugOverlayService
    {
        bool IsVisible { get; }
        void ToggleVisible();
    }
}
