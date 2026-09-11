namespace Valkur.Core.Services
{
    /// <summary>
    /// Cross-assembly handle to the debug HUD (F1) so callers in <c>Valkur.Gameplay</c> — the
    /// General Editor's button, the <c>debughud</c> console command — can drive it without
    /// referencing <c>Valkur.UI</c>. Registered by <c>DebugHUD</c> in Start.
    /// </summary>
    public interface IDebugOverlayService
    {
        /// <summary>True at any level above 0.</summary>
        bool IsVisible { get; }

        /// <summary>Off when on; back to the last level shown when off.</summary>
        void ToggleVisible();

        /// <summary>0 hidden, 1 the one-row chip, 2 the panel, 3 the panel plus world annotations.</summary>
        int Level { get; }

        /// <summary>The highest level this build allows: 3 in the Editor and dev builds, 1 in a release player.</summary>
        int MaxLevel { get; }

        /// <summary>Sets the level, clamped to <see cref="MaxLevel"/>. Persisted per machine.</summary>
        void SetLevel(int level);

        /// <summary>The plain-text bug report the COPIAR button puts on the clipboard.</summary>
        string BuildReport();

        /// <summary>Puts <see cref="BuildReport"/> on the system clipboard and returns it.</summary>
        string CopyReport();
    }
}
