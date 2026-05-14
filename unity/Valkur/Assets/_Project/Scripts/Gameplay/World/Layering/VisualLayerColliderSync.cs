using UnityEngine;

namespace Valkur.Gameplay.World.Layering
{
    /// <summary>
    /// Binds an entity's <see cref="Collider2D"/>s to the per-visual-layer
    /// Physics2D filtering set up by M2. Listens to the sibling
    /// <see cref="VisualLayerOccupant"/>'s <see cref="VisualLayerOccupant.OnLayerChanged"/>
    /// event and rewrites <see cref="Collider2D.includeLayers"/> on every collider
    /// of this GameObject so the Physics2D solver only considers contacts against
    /// the <c>WorldL{currentLayer}</c> + <c>WorldAll</c> sub-tilemaps.
    ///
    /// Why <c>includeLayers</c> and not the global Physics2D matrix:
    /// the global matrix is a shared resource — flipping it for the player would
    /// also affect every other entity on the Player physics layer (today there
    /// is only one, but M2.2's NPCs / projectiles will share the same matrix).
    /// Per-collider <c>includeLayers</c> lets each entity opt into a subset of
    /// the matrix without touching others.
    ///
    /// Setup:
    /// <list type="bullet">
    ///   <item>The owning GameObject already has a <see cref="VisualLayerOccupant"/>.
    ///         <see cref="PlayerController"/> declares both via <see cref="RequireComponent"/>.</item>
    ///   <item>The global Physics2D matrix has <c>Player vs WorldL0..L8 = OFF</c>
    ///         configured by <see cref="VisualLayerPhysicsSetup"/> at boot.</item>
    ///   <item>This component layers <c>includeLayers</c> ON TOP of the matrix —
    ///         it forces collisions ON for the current visual layer's slot, leaving
    ///         the other 8 slots inactive.</item>
    /// </list>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(VisualLayerOccupant))]
    public class VisualLayerColliderSync : MonoBehaviour
    {
        private VisualLayerOccupant _occupant;
        private Collider2D[] _colliders;

        private void Awake()
        {
            _occupant = GetComponent<VisualLayerOccupant>();
            _colliders = GetComponentsInChildren<Collider2D>(includeInactive: true);
        }

        private void OnEnable()
        {
            if (_occupant != null)
                _occupant.OnLayerChanged += OnLayerChanged;
            // Snap colliders to the current layer immediately so the entity
            // is in a coherent state on the first physics tick.
            ApplyIncludeLayers(_occupant != null ? _occupant.CurrentVisualLayer : 0);
        }

        private void OnDisable()
        {
            if (_occupant != null)
                _occupant.OnLayerChanged -= OnLayerChanged;
        }

        private void OnLayerChanged(int oldLayer, int newLayer)
        {
            ApplyIncludeLayers(newLayer);
        }

        private void ApplyIncludeLayers(int visualLayer)
        {
            if (_colliders == null || _colliders.Length == 0) return;
            int mask = WorldCollisionLayers.IncludeMaskFor(visualLayer);
            for (int i = 0; i < _colliders.Length; i++)
            {
                if (_colliders[i] == null) continue;
                _colliders[i].includeLayers = mask;
            }
        }

        /// <summary>
        /// Re-scan child colliders. Call after dynamically adding / removing
        /// <see cref="Collider2D"/>s on the entity (rare — equipment swap might
        /// trigger this once we have armor sprites with their own colliders).
        /// </summary>
        public void RefreshColliderList()
        {
            _colliders = GetComponentsInChildren<Collider2D>(includeInactive: true);
            ApplyIncludeLayers(_occupant != null ? _occupant.CurrentVisualLayer : 0);
        }
    }
}
