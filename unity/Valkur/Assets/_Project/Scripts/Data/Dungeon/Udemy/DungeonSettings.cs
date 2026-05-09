namespace Valkur.Data.Dungeon.Udemy
{
    /// <summary>
    /// Compile-time constants for the Udemy-style dungeon system. Mirrors the
    /// subset of Udemy's <c>Settings.cs</c> that the data layer needs (the rest
    /// â€” pathfinding penalty, A*, retry counts â€” lives on <c>DungeonConfigSO</c>
    /// so designers can tune it per-project).
    /// </summary>
    public static class DungeonSettings
    {
        /// <summary>Maximum number of corridor children a single room node may have.</summary>
        public const int MaxChildCorridors = 3;
    }
}
