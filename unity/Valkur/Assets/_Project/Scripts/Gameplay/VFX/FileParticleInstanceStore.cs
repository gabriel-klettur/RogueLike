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

        // ── Write-from-EditMode guard ──────────────────────────────────────────
        //
        // Mirrors SaveService.RefuseWriteOutsidePlayMode and the same guard on
        // JsonFileMapEditorZonesRepository, for the same reason and after the same kind of
        // loss: an EditMode test that constructs a ParticlesRuntimeEditor without injecting
        // InMemoryParticleInstanceStore falls through to this store, resolves the real
        // StreamingAssets path, and writes its empty fixture straight over the world's placed
        // emitters. Fixtures that snapshot and restore still leave the file destroyed if any
        // link in that chain is skipped — which is how particles_instances.json was reduced
        // to an empty array by a full suite run.
        //
        // A test that genuinely needs the real path opts in explicitly and does its own
        // backup/restore.

        /// <summary>
        /// Set true from a test's [SetUp] when it deliberately reads/writes the real
        /// StreamingAssets file, and back to false in [TearDown]. Static so it can be scoped
        /// per fixture. Production never touches it — Application.isPlaying already allows
        /// the write.
        /// </summary>
        public static bool AllowEditModeWritesToRealPath;

        // Domain Reload is OFF: a fixture that threw before its TearDown would otherwise
        // leave the opt-in armed for the rest of the session, which is precisely the state
        // this guard exists to prevent.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => AllowEditModeWritesToRealPath = false;

        /// <inheritdoc/>
        public void Save(string json)
        {
            if (!Application.isPlaying && !AllowEditModeWritesToRealPath)
            {
                Debug.LogWarning(
                    "[FileParticleInstanceStore] Refusing to write particle instances from " +
                    "EditMode. Inject InMemoryParticleInstanceStore, or set " +
                    "AllowEditModeWritesToRealPath in a fixture that backs the file up.");
                return;
            }

            string path = CurrentPath;
            string dir  = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            AtomicJsonFile.Write(path, json);
        }
    }
}
