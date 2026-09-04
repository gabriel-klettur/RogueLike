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
                 "No extension.")]
        public string assetPath;

        [Tooltip("Preview sprite shown in the Buildings Editor palette.")]
        public Sprite previewSprite;

        [Header("Properties")]
        [Tooltip("If true, this building blocks movement (a BoxCollider2D covers its footprint).")]
        public bool solid = true;

        [Tooltip("Fraction [0,1] of the original sprite height above which the image renders OVER the player. " +
                 "Below (1-splitRatio) = footprint on WallsBottom layer + collision. " +
                 "Above = canopy on WallsTop layer.")]
        [Range(0f, 1f)]
        public float splitRatio = 0.5f;

        [Tooltip("Collider scope: 'CG' = collision map shared across all instances with the same sprite. " +
                 "'CU' = per-instance collision map.")]
        public string colliderScope = "CG";

        [Tooltip("If true, every placement of this template is interactable and highlights in " +
                 "yellow when hovered in player mode. Per-instance overrides.interactable can " +
                 "force an individual placement on or off regardless of this flag.")]
        public bool interactable;

        [Tooltip("Source image dimensions in pixels. Used to compute world size and split in Unity units.")]
        public Vector2Int originalScale;

        // ── Light emission ──────────────────────────────────────────────────
        // A fixture that carries its own light: the lamp posts, braziers, sconces and
        // lanterns under Buildings/lights/. Filling lightPresetKey in makes every placement
        // of that template light the world by itself, instead of the author having to
        // remember to drop a matching light next to each prop in the Ctrl+F3 editor.

        [Tooltip("LightPresetCatalog key this fixture emits (Torch / Lamp / Magic). Empty = no light.")]
        public string lightPresetKey = "";

        [Tooltip("Where the flame sits, as a fraction of the building's own bounds: " +
                  "x across the width (0.5 = centred), y up the height (0.75 = near the top of a lamp post). " +
                  "A light at the base of a lamp post lights the ground and not the lamp.")]
        public Vector2 lightOffsetNormalized = new Vector2(0.5f, 0.75f);

        [Tooltip("Optional sprite to swap in while the lights are on, e.g. " +
                  "'Buildings/lights/lamp_post_ornate_lit'. Empty = the fixture looks the same " +
                  "day and night. The art already ships in lit/unlit pairs.")]
        public string litAssetPath = "";

        // ── Door ─────────────────────────────────────────────────────────────
        // WHERE the doorway sits is a property of the ART, so it belongs to the template:
        // every placement of house_a has its door on the same pixels. WHERE it LEADS is a
        // property of the placement and lives per instance in BuildingDoorSpec.
        //
        // Stored NORMALIZED, never as collision-grid cells. BuildingCollisionLoader.ResampleGrid
        // collapses each destination cell to a single bool by OR-ing its sources, so a door
        // glyph in that matrix is erased the moment the instance carries a scale override —
        // silently, on exactly the buildings a designer resized. A fraction of the bounds
        // survives resampling, scale overrides and splitRatio changes by construction.

        [Tooltip("If true, this building has a doorway. Placements can then carry a " +
                 "per-instance destination (overrides.door in buildings_instances.json).")]
        public bool hasDoor;

        [Tooltip("Doorway CENTRE as a fraction of the building's own bounds: " +
                 "x across the width (0.5 = centred), y up the height (0 = the ground line). " +
                 "The rect is clamped to stay inside the bounds.")]
        public Vector2 doorOffsetNormalized = new Vector2(0.5f, 0.06f);

        [Tooltip("Doorway SIZE as a fraction of the building's own bounds. " +
                 "Small: the trigger only has to be touched, not walked through — the " +
                 "footprint around it is solid.")]
        public Vector2 doorSizeNormalized = new Vector2(0.18f, 0.12f);

        [Tooltip("Source image path (e.g. 'buildings/vegetation/tree_7.png'). " +
                 "Used to key into buildings_collisions_by_image.json.")]
        public string sourceImagePath;

        [Header("Durability")]
        [Tooltip("Destruction profile. Empty = indestructible, which is the default for " +
                 "every shipped template: without one no BuildingDurability component is " +
                 "added and the building never enters the obstacle registry.")]
        public DestructionProfile destruction;
    }
}
