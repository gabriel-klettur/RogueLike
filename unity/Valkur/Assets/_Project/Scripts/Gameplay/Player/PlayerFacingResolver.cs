using UnityEngine;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Pure, static facing-direction logic for the player. Extracted from
    /// <see cref="PlayerController.UpdateFacingDirection"/> so that the rules
    /// are deterministic and unit-testable (no Camera, no Mouse, no MonoBehaviour).
    ///
    /// Bug history (do not regress):
    ///  - The InputAction bound to <c>&lt;Mouse&gt;/position</c> returns (0,0)
    ///    whenever the cursor leaves the Game view (focus loss, hovering editor
    ///    chrome, alt-tab). (0,0) is the bottom-left of the viewport, so without
    ///    a clamp the player was permanently yanked to face SW.
    ///  - When the mouse is out of view, facing must NOT snap to a new value
    ///    based on a fake mouse position. Either retain previous facing (idle)
    ///    or fall back to the movement vector (moving).
    /// </summary>
    public static class PlayerFacingResolver
    {
        /// <summary>Minimum squared magnitude before a vector is treated as a real direction.</summary>
        public const float MinDirectionSqrMagnitude = 0.01f;

        /// <summary>
        /// True when the cursor is within the visible game viewport. Out-of-view
        /// mouse positions must NOT drive facing because the OS / Input System
        /// can report stale or zeroed values.
        /// </summary>
        public static bool IsMouseWithinViewport(Vector2 mouseScreen, Vector2 screenSize)
        {
            return mouseScreen.x >= 0f && mouseScreen.x <= screenSize.x &&
                   mouseScreen.y >= 0f && mouseScreen.y <= screenSize.y;
        }

        /// <summary>
        /// Resolves the player's facing direction for this frame given current
        /// inputs. Pure: no globals, no side effects.
        /// </summary>
        /// <param name="currentFacing">Last known facing — returned unchanged if no new source is available.</param>
        /// <param name="mouseWorld">Mouse position projected to world space (caller computes via Camera.ScreenToWorldPoint).</param>
        /// <param name="isMouseInView">Whether the cursor is currently within the game viewport.</param>
        /// <param name="playerPos">Player world position.</param>
        /// <param name="moveInput">Raw movement input vector (typically WASD composite).</param>
        /// <param name="isMoving">True when the player is actively moving (non-zero move input).</param>
        public static Vector2 ResolveFacingDirection(
            Vector2 currentFacing,
            Vector2 mouseWorld,
            bool isMouseInView,
            Vector2 playerPos,
            Vector2 moveInput,
            bool isMoving)
        {
            // 1. Mouse aiming wins when the cursor is inside the viewport.
            if (isMouseInView)
            {
                Vector2 dir = (mouseWorld - playerPos).normalized;
                if (dir.sqrMagnitude > MinDirectionSqrMagnitude)
                    return dir;
            }

            // 2. Mouse is out of view (or yields a zero vector): fall back to
            //    move input so keyboard-only players still face their movement.
            if (isMoving)
            {
                Vector2 moveDir = moveInput.normalized;
                if (moveDir.sqrMagnitude > MinDirectionSqrMagnitude)
                    return moveDir;
            }

            // 3. No new information — keep last known facing.
            return currentFacing;
        }
    }
}
