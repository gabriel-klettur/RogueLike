using System.Linq;
using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.World.Layering
{
    /// <summary>
    /// Visual-depth analogue of <see cref="VisualLayerColliderSync"/>: listens to the
    /// sibling <see cref="VisualLayerOccupant.OnLayerChanged"/> event and remaps the
    /// owning entity's <see cref="SpriteRenderer.sortingLayerName"/> so the entity
    /// draws above the tilemap layers it has "climbed onto".
    ///
    /// Why this is its own component (and not a flag on <see cref="VisualLayerOccupant"/>):
    /// the occupant is the gameplay source of truth — collisions, layer-jump triggers,
    /// save/restore. Visual depth is a presentation concern that some entities never
    /// need (e.g. a server-only NPC). Keeping the two separate matches the existing
    /// <see cref="VisualLayerColliderSync"/> split (gameplay → physics) and lets
    /// non-visual entities skip this component entirely.
    ///
    /// Mapping (entity's CurrentVisualLayer → sortingLayer the renderer is placed on):
    /// <list type="bullet">
    ///   <item>0..4 (Ground … WallsBottom)  → "Entities" — the default, sits one slot
    ///         above WallsBottom in the project's SortingLayer order.</item>
    ///   <item>5 Decorations                → "WallsTop"          — strictly above Decorations.</item>
    ///   <item>6 WallsTop                   → "ObjectsHigh"       — strictly above WallsTop.</item>
    ///   <item>7 ObjectsHigh                → "Projectiles"       — strictly above ObjectsHigh.</item>
    ///   <item>8 OverheadDetails            → "EntitiesOverhead"  — strictly above Overhead
    ///         (a sortingLayer added specifically for this case so the elevated
    ///         entity does NOT borrow UI_World, which would render the player in
    ///         front of in-world health/mana bars).</item>
    /// </list>
    ///
    /// Y-sort inside the chosen sortingLayer is still owned by <see cref="YSortEntity"/>
    /// — this component only flips the layer name, never <see cref="SpriteRenderer.sortingOrder"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(VisualLayerOccupant))]
    [RequireComponent(typeof(SpriteRenderer))]
    public class VisualLayerSortingSync : MonoBehaviour
    {
        // Indexed by VisualLayerOccupant.CurrentVisualLayer (0..8). Names match
        // entries in SortingConfig + ProjectSettings/TagManager.asset.
        // Verified by SortingLayer.NameToID in OnEnable so a rename in TagManager
        // surfaces an actionable warning instead of a silent depth bug.
        private static readonly string[] SortingLayerByVisualLayer =
        {
            SortingConfig.LAYER_ENTITIES,           // 0 Ground
            SortingConfig.LAYER_ENTITIES,           // 1 FloorDecals
            SortingConfig.LAYER_ENTITIES,           // 2 Collision (invisible — no visual depth concern)
            SortingConfig.LAYER_ENTITIES,           // 3 ObjectsLow
            SortingConfig.LAYER_ENTITIES,           // 4 WallsBottom
            SortingConfig.LAYER_WALLS_TOP,          // 5 Decorations
            SortingConfig.LAYER_OBJECTS_HIGH,       // 6 WallsTop
            SortingConfig.LAYER_PROJECTILES,        // 7 ObjectsHigh
            SortingConfig.LAYER_ENTITIES_OVERHEAD,  // 8 OverheadDetails
        };

        private VisualLayerOccupant _occupant;
        private SpriteRenderer _sr;

        private void Awake()
        {
            _occupant = GetComponent<VisualLayerOccupant>();
            _sr = GetComponent<SpriteRenderer>();
        }

        private void OnEnable()
        {
            if (_occupant != null)
            {
                // Detach-then-attach so a runtime path that calls OnEnable twice
                // without a matching OnDisable in between (custom test harness,
                // editor reload race) never double-subscribes the handler.
                _occupant.OnLayerChanged -= OnLayerChanged;
                _occupant.OnLayerChanged += OnLayerChanged;
            }
            // Snap immediately so the visual matches the gameplay layer from frame 0,
            // including the case where the entity spawns already-elevated (e.g. a save
            // restored mid-OverheadDetails).
            ApplySortingLayer(_occupant != null ? _occupant.CurrentVisualLayer : 0);
        }

        private void OnDisable()
        {
            if (_occupant != null)
                _occupant.OnLayerChanged -= OnLayerChanged;
        }

        private void OnLayerChanged(int oldLayer, int newLayer) => ApplySortingLayer(newLayer);

        private void ApplySortingLayer(int visualLayer)
        {
            if (_sr == null) return;
            int idx = Mathf.Clamp(visualLayer, 0, SortingLayerByVisualLayer.Length - 1);
            string target = SortingLayerByVisualLayer[idx];
            // SortingLayer.NameToID returns 0 (== "Default") for an unknown name. Detect
            // that and fall back to "Entities" so a missing TagManager entry can't make
            // the player vanish behind every sprite in the scene.
            if (!SortingLayer.layers.Any(l => l.name == target))
            {
                Debug.LogWarning($"[VisualLayerSortingSync] SortingLayer '{target}' not found in TagManager — " +
                                 $"falling back to '{SortingConfig.LAYER_ENTITIES}'. " +
                                 $"Add the layer in Edit > Project Settings > Tags & Layers.");
                target = SortingConfig.LAYER_ENTITIES;
            }
            _sr.sortingLayerName = target;
        }
    }
}
