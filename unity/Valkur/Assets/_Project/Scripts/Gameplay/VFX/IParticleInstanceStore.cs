namespace Valkur.Gameplay.VFX
{
    /// <summary>
    /// Abstraction over the storage backend for the particle instances JSON.
    /// Production code uses <see cref="FileParticleInstanceStore"/>; tests inject
    /// <see cref="InMemoryParticleInstanceStore"/> to avoid touching the disk.
    /// </summary>
    public interface IParticleInstanceStore
    {
        /// <summary>Returns the full JSON string, or <c>null</c> if the store is empty.</summary>
        string Load();

        /// <summary>Persists the JSON string. Must be atomic (no corruption on crash).</summary>
        void Save(string json);
    }
}
