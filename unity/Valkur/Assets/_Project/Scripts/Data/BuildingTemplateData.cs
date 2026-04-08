using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// ScriptableObject template for a building type.
    /// One asset per building variant (e.g. tree_1, catholic_temple, house_a).
    ///
    /// Maps to Python's buildings_templates.json entries.
    /// Template data is GLOBAL (shared across all instances of the same building type).
    /// Per-instance overrides live in BuildingLoader's parsed instances data.
    /// </summary>
    [CreateAssetMenu(fileName = "NewBuildingTemplate", menuName = "Valkur/Buildings/Template")]
    public class BuildingTemplateData : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Unique template ID. Maps to Python buildings_templates.json 'id'.")]
        public int templateId;

        [Header("Asset")]
        [Tooltip("Resources-relative path used at runtime, e.g. 'Buildings/vegetation/tree_1'. " +
                 "No extension. Maps to Python buildings_templates.json 'assets.idle'.")]
        public string assetPath;

        [Tooltip("Preview sprite shown in the Buildings Editor palette. " +
                 "Assigned automatically by BuildingImporter.")]
        public Sprite previewSprite;

        [Header("Properties")]
        [Tooltip("If true, this building blocks movement (a BoxCollider2D covers its footprint). " +
                 "Maps to Python 'solid'.")]
        public bool solid = true;

        [Tooltip("Fraction [0,1] of the original sprite height above which the image renders OVER the player. " +
                 "Below (1-splitRatio) = footprint on WallsBottom layer + collision. " +
                 "Above = canopy on WallsTop layer. " +
                 "Maps to Python 'split_ratio'.")]
        [Range(0f, 1f)]
        public float splitRatio = 0.5f;

        [Tooltip("Collider scope: 'CG' = collision map shared across all instances with the same sprite. " +
                 "'CU' = per-instance collision map. " +
                 "Maps to Python 'collider_scope'. Detailed grid colliders are Phase 2.")]
        public string colliderScope = "CG";

        [Tooltip("Source image dimensions in pixels. Used to compute world size and split in Unity units. " +
                 "Maps to Python 'original_scale'.")]
        public Vector2Int originalScale;
    }
}
