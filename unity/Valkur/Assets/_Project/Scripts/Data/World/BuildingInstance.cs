using System;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// Public POCO mirroring one entry of <c>buildings_instances.json</c>.
    /// Promoted out of the private DTO that lived inside
    /// <c>BuildingLoader.Spawning.cs</c> so an
    /// <c>IBuildingInstanceRepository</c> can return / accept it without
    /// pulling Gameplay-private types through the Repository surface.
    ///
    /// Field names preserve the on-disk schema (snake_case in JSON,
    /// PascalCase in C#) so adapters can map mechanically.
    /// </summary>
    [Serializable]
    public class BuildingInstance
    {
        public int Id;
        public int TemplateId;
        public string Zone;
        public int RelX;
        public int RelY;

        /// <summary>(0,0) = use template.originalScale.</summary>
        public Vector2Int ScaleOverride;
        /// <summary>Negative = use template.splitRatio.</summary>
        public float SplitRatioOverride = -1f;
        /// <summary>Empty = use template.colliderScope ("CG" or "CU").</summary>
        public string ColliderScopeOverride;
        /// <summary>Tile layer (0..8) the footprint sits directly above. Maps to <c>overrides.layer_bottom</c>.</summary>
        public int ZBottom = 4;
        /// <summary>Tile layer (0..8) the canopy sits directly above. Maps to <c>overrides.layer_top</c>.</summary>
        public int ZTop = 6;
        /// <summary>-1 = inherit template.interactable; 0 = force off; 1 = force on.</summary>
        public int InteractableOverride = -1;

        public BuildingInstance() { }

        public BuildingInstance(int id, int templateId, string zone, int relX, int relY)
        {
            Id = id;
            TemplateId = templateId;
            Zone = zone;
            RelX = relX;
            RelY = relY;
        }
    }
}
