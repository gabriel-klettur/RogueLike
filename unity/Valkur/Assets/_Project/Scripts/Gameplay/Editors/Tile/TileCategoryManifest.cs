using System;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Runtime DTO mirroring Resources/Tiles/_categories.json, baked at editor
    /// time by TileCategoryManifestBuilder (Scripts/Editor/Asset/) because
    /// Resources/ ships flat in a build — there is no runtime directory
    /// listing API to discover which subfolders of Resources/Tiles/ exist.
    /// Same convention as TilesheetManifest: generated, committed to git, read
    /// as a plain TextAsset via JsonUtility. Consumed by
    /// TileCatalog.BuildFromResources() (the tile picker's data source) and
    /// OverlayLoader.CategoryFolders (the world-paint sprite resolver), so the
    /// two never diverge from each other or from disk again.
    /// </summary>
    [Serializable]
    public class TileCategoryManifest
    {
        public int schemaVersion;

        /// <summary>
        /// Immediate subfolders of Resources/Tiles/ that contain at least one
        /// sprite (recursively), ordered by sprite count descending — largest
        /// category first, so the common case is found soonest wherever this
        /// list is walked linearly.
        /// </summary>
        public string[] folderCategories;

        /// <summary>
        /// Category name assigned to loose sprites that sit directly under
        /// Resources/Tiles/ with no owning subfolder (e.g. "floor", "wall").
        /// There is no folder to enumerate for these, so they need an explicit
        /// synthetic bucket instead.
        /// </summary>
        public string syntheticRootCategory;

        /// <summary>
        /// File names (no extension) of the sprites living directly under
        /// Resources/Tiles/ with no subfolder.
        /// </summary>
        public string[] rootFiles;
    }
}
