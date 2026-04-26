namespace Valkur.Gameplay.Save
{
    /// <summary>
    /// Static carrier for a save path (and optional player class) that should be loaded
    /// after a scene transition. The gameplay scene startup code checks this, applies the
    /// player class to <see cref="Valkur.Data.PlayerSelectionState"/> before spawning, and
    /// then calls SaveService.Load() to restore full game state.
    /// </summary>
    public static class PendingSaveLoad
    {
        public static string Path        { get; set; }
        /// <summary>
        /// Player class key extracted from the save header (e.g. "mague", "barbarian").
        /// Set alongside <see cref="Path"/> so SpawnPlayer() uses the correct class before
        /// the full save data is applied.
        /// </summary>
        public static string PlayerClass { get; set; }

        public static bool HasPending => !string.IsNullOrEmpty(Path);

        public static string Consume()
        {
            string p = Path;
            Path        = null;
            PlayerClass = null;
            return p;
        }
    }
}
