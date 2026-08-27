using UnityEngine;

namespace Valkur.Gameplay.World.Layering
{
    /// <summary>
    /// Runtime configuration of the Physics2D layer-collision matrix for the M2
    /// per-visual-layer filtering. Configured from code (not the editor's
    /// Physics2DSettings asset) so the gameplay layers don't have to be hand-
    /// edited in the inspector — keeps the layer model self-documenting in C#.
    ///
    /// Configures:
    /// <list type="bullet">
    ///   <item><b>Player vs WorldL0..L8 = OFF</b> by default. Each player's
    ///   <see cref="VisualLayerColliderSync"/> overrides exactly ONE slot back
    ///   to ON via <see cref="Collider2D.includeLayers"/>, picking whichever
    ///   matches their current visual layer.</item>
    ///   <item><b>Player vs WorldAll = ON</b> always. Wildcard cells block
    ///   the player on every layer.</item>
    ///   <item><b>NPC vs every WorldL + WorldAll = ON</b> (M2.1 simplification).
    ///   NPCs collide with every painted cell regardless of tag for now;
    ///   M2.2 will give them the same opt-in treatment as the player.</item>
    /// </list>
    ///
    /// The NPC half of that list used to be documentation only — the body
    /// configured <c>playerLayer</c> and returned, leaving the project's
    /// <c>Physics2DSettings</c> matrix (which ignores NPC vs 18..27) in force.
    /// Since <see cref="VisualLayerColliderSync"/> is attached to the player alone,
    /// every NPC collider carried <c>includeLayers = 0</c> with no matrix entry to
    /// fall back on, so monsters walked through every painted wall, river and cliff
    /// and only the building <c>CollTile_*</c> boxes on <c>World</c>(11) stopped them.
    ///
    /// Projectiles are deliberately NOT in the matrix: they resolve obstacles
    /// through a swept query against <see cref="WorldCollisionLayers.BlockingMask"/>,
    /// so a matrix entry would double-handle the same hit.
    ///
    /// Runs at <see cref="RuntimeInitializeLoadType.SubsystemRegistration"/> so
    /// the matrix is correct BEFORE any scene loads or any physics tick fires.
    /// </summary>
    public static class VisualLayerPhysicsSetup
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Configure()
        {
            int playerLayer = LayerMask.NameToLayer("Player");
            if (playerLayer < 0)
            {
                Debug.LogWarning("[VisualLayerPhysicsSetup] 'Player' layer not found — matrix configuration skipped.");
                return;
            }

            int npcLayer = LayerMask.NameToLayer("NPC");

            // Force re-resolution of cached layer indices in case the TagManager
            // changed between Play sessions (Domain Reload OFF means statics
            // survive across plays).
            WorldCollisionLayers.Invalidate();

            for (int i = 0; i < WorldCollisionLayers.LayerCount; i++)
            {
                int wl = WorldCollisionLayers.GetWorldLayerIndex(i);
                if (wl < 0) continue;
                // Player ignores every WorldL{N} by default. VisualLayerColliderSync
                // toggles one back ON via Collider2D.includeLayers based on the
                // entity's current visual layer.
                Physics2D.IgnoreLayerCollision(playerLayer, wl, true);

                // NPCs have no VisualLayerColliderSync, so they have nothing to opt
                // back in with — they must be blocked by every painted layer outright.
                if (npcLayer >= 0)
                    Physics2D.IgnoreLayerCollision(npcLayer, wl, false);
            }

            // Player vs WorldAll: NEVER ignored — wildcard colliders apply to
            // every layer. The default matrix already has it ON, but set it
            // explicitly for self-documentation and to undo any stale settings
            // from a previous Play session (Domain Reload OFF).
            int worldAll = WorldCollisionLayers.GetWorldAllIndex();
            if (worldAll >= 0)
            {
                Physics2D.IgnoreLayerCollision(playerLayer, worldAll, false);
                if (npcLayer >= 0)
                    Physics2D.IgnoreLayerCollision(npcLayer, worldAll, false);
            }

            if (npcLayer < 0)
                Debug.LogWarning("[VisualLayerPhysicsSetup] 'NPC' layer not found — NPCs will not collide with painted cells.");
        }
    }
}
