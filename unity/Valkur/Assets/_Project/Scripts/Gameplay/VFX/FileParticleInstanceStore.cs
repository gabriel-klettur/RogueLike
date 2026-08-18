using System.IO;
using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.VFX
{
    /// <summary>
    /// Production <see cref="IParticleInstanceStore"/> backed by
    /// <c>particles_instances.json</c>.
    ///
    /// The directory is map-slot aware via <see cref="MapEditorActiveSlot"/>:
    /// the default slot keeps the legacy
    /// <c>StreamingAssets/Particles/</c> location, while a custom map created
    /// from the F11 Map Editor writes under
    /// <c>persistentDataPath/Maps/&lt;slot&gt;/Particles/</c>. Without this, placing
    /// an emitter on one map overwrote every other map's emitters.
    ///
    /// The slot is resolved on each call rather than cached in the constructor
    /// so a long-lived store follows the user across slot switches — same
    /// contract as <c>JsonFileBuildingInstanceRepository</c>.
    ///
    /// Writes go through <see cref="AtomicJsonFile.Write"/> so a mid-write crash
    /// cannot corrupt the primary file.
    /// </summary>
    public sealed class FileParticleInstanceStore : IParticleInstanceStore
    {
        private const string SUBDIR = "Particles";

        private readonly string _fileName;

        /// <param name="fileName">JSON file name inside the active slot's <c>Particles/</c> directory. Defaults to <c>particles_instances.json</c>.</param>
        public FileParticleInstanceStore(string fileName = "particles_instances.json")
        {
            _fileName = string.IsNullOrEmpty(fileName) ? "particles_instances.json" : fileName;
        }

        /// <summary>Absolute path this store reads from / writes to right now.</summary>
        public string CurrentPath
            => Path.Combine(MapEditorActiveSlot.DirForActiveSlot(SUBDIR), _fileName);

        /// <inheritdoc/>
        public string Load()
        {
            string path = CurrentPath;
            if (!File.Exists(path)) return null;
            return File.ReadAllText(path, System.Text.Encoding.UTF8);
        }

        /// <inheritdoc/>
        public void Save(string json)
        {
            string path = CurrentPath;
            string dir  = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            AtomicJsonFile.Write(path, json);
        }
    }
}
