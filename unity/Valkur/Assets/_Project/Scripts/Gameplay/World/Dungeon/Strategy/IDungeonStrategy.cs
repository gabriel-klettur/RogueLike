namespace Valkur.Gameplay.World.Dungeon.Strategy
{
    /// <summary>
    /// Pluggable dungeon generation strategy. Each Map slot picks one (id "bsp" or "udemy").
    /// The resolver looks up the implementation by <see cref="Id"/> at bootstrap time.
    /// </summary>
    public interface IDungeonStrategy
    {
        /// <summary>Stable string id used by the resolver and by Map slot persistence.</summary>
        string Id { get; }

        /// <summary>
        /// Generate the dungeon for the given context. Returns false if the strategy
        /// failed (e.g. impossible graph in Udemy retry exhaustion). Implementations
        /// must paint tiles / spawn rooms during this call; caller should not retry.
        /// </summary>
        bool TryGenerate(DungeonGenerationContext ctx, out DungeonGenerationResult result);

        /// <summary>
        /// Tear down anything spawned by the previous <see cref="TryGenerate"/> call.
        /// Safe to call when nothing was generated.
        /// </summary>
        void Cleanup();
    }
}
