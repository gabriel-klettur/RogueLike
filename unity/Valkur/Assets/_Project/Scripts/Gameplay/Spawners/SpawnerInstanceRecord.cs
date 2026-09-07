using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Spawners
{
    /// <summary>
    /// One row of <c>spawners_instances.json</c>, parsed.
    ///
    /// <para>It exists so the loader can hold the WHOLE file rather than only the rows it
    /// managed to spawn. The v1-to-v2 migration writes the records back, and a writer that
    /// emitted the scene instead of the file would silently drop every row whose preset was
    /// missing from the catalogue or whose zone was not registered — rows the loader
    /// deliberately skips with a warning and which the author has every right to keep. Same
    /// rule <c>ParticleInstanceSerializer.SerializeRecords</c> follows: emit what was read,
    /// verbatim and complete, with no scene scan and no coordinate maths, which is what lets
    /// it need no anti-wipe guard of its own.</para>
    /// </summary>
    public sealed class SpawnerInstanceRecord
    {
        public string TemplateId;
        public string Zone;
        public Vector2Int Tile;
        public string InstanceId;

        /// <summary>
        /// This placement's own configuration. Null for a schema-v1 row, which is what
        /// <see cref="SpawnerInstanceSerializer.Freeze"/> fills in from the preset.
        /// </summary>
        public SpawnerInstanceConfig Config;

        /// <summary>
        /// False when the row arrived without a <c>config</c> block — i.e. it is v1 and was
        /// frozen during this load. The loader writes the file back only when at least one
        /// row was in that state, so a fully migrated file is never rewritten.
        /// </summary>
        public bool HadConfig;
    }
}
