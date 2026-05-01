using System.IO;
using UnityEngine;

namespace Valkur.Gameplay.VFX
{
    /// <summary>
    /// Production <see cref="IParticleInstanceStore"/> backed by
    /// <c>StreamingAssets/Particles/particles_instances.json</c>.
    /// Writes are performed through <see cref="AtomicJsonFile.Write"/> so a
    /// mid-write crash cannot corrupt the primary file.
    /// </summary>
    public sealed class FileParticleInstanceStore : IParticleInstanceStore
    {
        private readonly string _path;

        /// <param name="fileName">JSON file name inside <c>StreamingAssets/Particles/</c>. Defaults to <c>particles_instances.json</c>.</param>
        public FileParticleInstanceStore(string fileName = "particles_instances.json")
        {
            _path = Path.Combine(Application.streamingAssetsPath, "Particles", fileName);
        }

        /// <inheritdoc/>
        public string Load()
        {
            if (!File.Exists(_path)) return null;
            return File.ReadAllText(_path, System.Text.Encoding.UTF8);
        }

        /// <inheritdoc/>
        public void Save(string json)
        {
            AtomicJsonFile.Write(_path, json);
        }
    }
}
