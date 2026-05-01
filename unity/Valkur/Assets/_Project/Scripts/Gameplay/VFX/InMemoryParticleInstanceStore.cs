namespace Valkur.Gameplay.VFX
{
    /// <summary>
    /// Test-friendly <see cref="IParticleInstanceStore"/> that keeps JSON in memory.
    /// Inject this instead of <see cref="FileParticleInstanceStore"/> in EditMode tests
    /// so no files are touched on disk.
    /// </summary>
    public sealed class InMemoryParticleInstanceStore : IParticleInstanceStore
    {
        private string _json;

        /// <param name="initialJson">Optional seed content (simulates an existing file).</param>
        public InMemoryParticleInstanceStore(string initialJson = null)
        {
            _json = initialJson;
        }

        /// <inheritdoc/>
        public string Load() => _json;

        /// <inheritdoc/>
        public void Save(string json) => _json = json;

        /// <summary>Current stored JSON (for test assertions).</summary>
        public string CurrentJson => _json;
    }
}
