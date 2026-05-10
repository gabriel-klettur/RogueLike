using System;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Runtime DTO mirroring Resources/Tiles/&lt;category&gt;/_manifest.json,
    /// produced by tools/atlas/migrate_tilesheet.py when slicing a tilesheet
    /// PNG into individual cells. Used by TileCatalog to enrich each
    /// TileEntry with grid coordinates + uniqueId so the F8 "tileset view"
    /// can render the cells in the original sheet layout and offer the
    /// "hide duplicates" toggle.
    /// </summary>
    [Serializable]
    public class TilesheetManifest
    {
        public int schemaVersion;
        public string source;
        public int cellPx;
        public int cols;
        public int rows;
        public Cell[] cells;
        public Unique[] uniques;

        [Serializable]
        public class Cell
        {
            public int r;
            public int c;
            public string file;
            public int uniqueId;
            public bool transparent;
        }

        [Serializable]
        public class Unique
        {
            public int id;
            public string file;
            public string hash;
        }
    }
}
