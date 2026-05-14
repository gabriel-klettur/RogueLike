using System;
using UnityEngine;

namespace Valkur.Gameplay.World.Layering
{
    /// <summary>
    /// Per-entity record of which visual layer the entity is currently considered
    /// to be on, for gameplay purposes (collision filtering, sortingOrder hints,
    /// trigger logic). Source of truth — read by all other systems that need to
    /// know "what layer is this entity on right now".
    ///
    /// Maps to one of the nine
    /// <see cref="World.TilemapLayerSetup.TilemapLayer"/> values (0..8). The same
    /// component mounts on Player, NPC, Projectile, etc. so every consumer
    /// (Milestone 2's per-layer Physics2D filtering, the debug HUD, future
    /// phase-shift abilities) reads from a uniform contract.
    ///
    /// Design choices pinned here so a refactor doesn't drift the semantics:
    ///   • Default is layer 0 (Ground). Spawning a new entity NEVER opts it into
    ///     a higher layer implicitly — gameplay events must explicitly call
    ///     <see cref="SetVisualLayer"/>.
    ///   • Mutation is centralised: <see cref="SetVisualLayer"/> clamps to the
    ///     valid range, no-ops when the value didn't change, and only then fires
    ///     <see cref="OnLayerChanged"/>. Listeners can rely on the event firing
    ///     exactly once per real transition.
    ///   • The component does NOT observe the world — that's the job of
    ///     <see cref="VisualLayerProbe"/>. Mixing observation into the state
    ///     would let "stand under a Decorations tile" silently change the
    ///     entity's gameplay layer, which is the opposite of what the M2 design
    ///     intends (gameplay drives layer, not the world).
    /// </summary>
    public class VisualLayerOccupant : MonoBehaviour
    {
        public const int MinLayer = 0;
        public const int MaxLayer = 8;

        [SerializeField, Range(MinLayer, MaxLayer)]
        [Tooltip("Visual layer this entity is currently considered to be on. " +
                 "Match the index of a TilemapLayerSetup.TilemapLayer entry " +
                 "(0=Ground, 4=WallsBottom, 8=OverheadDetails). Default 0.")]
        private int currentVisualLayer = 0;

        /// <summary>(oldLayer, newLayer) — fired exactly once per real transition.</summary>
        public event Action<int, int> OnLayerChanged;

        /// <summary>Current visual layer as a raw int (0..8).</summary>
        public int CurrentVisualLayer => currentVisualLayer;

        /// <summary>Current visual layer as the strongly-typed enum.</summary>
        public TilemapLayerSetup.TilemapLayer CurrentLayer =>
            (TilemapLayerSetup.TilemapLayer)currentVisualLayer;

        /// <summary>Human-readable layer name (e.g. "Ground", "WallsBottom").</summary>
        public string LayerName => CurrentLayer.ToString();

        /// <summary>
        /// Set the entity's current visual layer. Out-of-range values clamp to
        /// [<see cref="MinLayer"/>, <see cref="MaxLayer"/>]. No-op + no event if
        /// the resulting value equals the current one — listeners can subscribe
        /// once and trust that every fire is a real transition.
        /// </summary>
        public void SetVisualLayer(int newLayer)
        {
            int clamped = Mathf.Clamp(newLayer, MinLayer, MaxLayer);
            if (clamped == currentVisualLayer) return;
            int old = currentVisualLayer;
            currentVisualLayer = clamped;
            OnLayerChanged?.Invoke(old, clamped);
        }

        /// <summary>
        /// Convenience setter that takes the strongly-typed enum. Delegates to
        /// <see cref="SetVisualLayer(int)"/>; same clamping + event semantics.
        /// </summary>
        public void SetVisualLayer(TilemapLayerSetup.TilemapLayer layer)
            => SetVisualLayer((int)layer);

        private void OnValidate()
        {
            // Keep Inspector-edited values inside the valid range so an authoring
            // mistake can't ship a layer-9 entity that breaks later consumers.
            currentVisualLayer = Mathf.Clamp(currentVisualLayer, MinLayer, MaxLayer);
        }
    }
}
