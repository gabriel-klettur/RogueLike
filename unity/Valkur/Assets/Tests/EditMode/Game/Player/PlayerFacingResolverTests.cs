using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.Player
{
    /// <summary>
    /// Regression tests for <see cref="PlayerFacingResolver"/>.
    ///
    /// These guard the bug where the player's facing was permanently snapped to
    /// the bottom-left of the viewport whenever the mouse cursor left the Game
    /// view (focus loss, alt-tab, hovering editor chrome). The InputAction bound
    /// to <c>&lt;Mouse&gt;/position</c> reports (0,0) in that case and (0,0)
    /// in screen space is the bottom-left corner — so without a viewport clamp
    /// the player faced SW forever and the FacingIndicator chevron pointed the
    /// opposite direction of any subsequent intent.
    /// </summary>
    public class PlayerFacingResolverTests
    {
        // ---- IsMouseWithinViewport ----------------------------------------

        [TestCase(0f, 0f, true,    Description = "Bottom-left corner is inside (boundary)")]
        [TestCase(1920f, 1080f, true, Description = "Top-right corner is inside (boundary)")]
        [TestCase(960f, 540f, true, Description = "Center is inside")]
        [TestCase(-1f, 540f, false, Description = "Left of viewport is outside")]
        [TestCase(1921f, 540f, false, Description = "Right of viewport is outside")]
        [TestCase(960f, -1f, false,   Description = "Below viewport is outside")]
        [TestCase(960f, 1081f, false, Description = "Above viewport is outside")]
        [TestCase(-100f, -100f, false, Description = "Far below-left is outside")]
        [TestCase(5000f, 5000f, false, Description = "Far above-right is outside")]
        public void IsMouseWithinViewport_ClampsToScreenBounds(float mx, float my, bool expected)
        {
            var screenSize = new Vector2(1920f, 1080f);
            var result = PlayerFacingResolver.IsMouseWithinViewport(new Vector2(mx, my), screenSize);
            Assert.AreEqual(expected, result);
        }

        // ---- ResolveFacingDirection: mouse-in-view path -------------------

        [Test]
        public void ResolveFacingDirection_MouseRightOfPlayer_FacesEast()
        {
            var result = PlayerFacingResolver.ResolveFacingDirection(
                currentFacing: Vector2.down,
                mouseWorld:    new Vector2(10f, 0f),
                isMouseInView: true,
                playerPos:     new Vector2(0f, 0f),
                moveInput:     Vector2.zero,
                isMoving:      false);

            Assert.AreEqual(1f, result.x, 0.001f);
            Assert.AreEqual(0f, result.y, 0.001f);
        }

        [Test]
        public void ResolveFacingDirection_MouseLeftOfPlayer_FacesWest()
        {
            // This is the exact direction the original bug PREVENTED:
            // mouse moved left of the player, but facing was stuck to SW (-x,-y)
            // because (0,0) was being read whenever the cursor briefly left view.
            var result = PlayerFacingResolver.ResolveFacingDirection(
                currentFacing: Vector2.right,
                mouseWorld:    new Vector2(-5f, 0f),
                isMouseInView: true,
                playerPos:     Vector2.zero,
                moveInput:     Vector2.zero,
                isMoving:      false);

            Assert.AreEqual(-1f, result.x, 0.001f);
            Assert.AreEqual(0f,  result.y, 0.001f);
        }

        [Test]
        public void ResolveFacingDirection_MouseAbovePlayer_FacesNorth()
        {
            var result = PlayerFacingResolver.ResolveFacingDirection(
                Vector2.down, new Vector2(0f, 10f), true,
                Vector2.zero, Vector2.zero, false);

            Assert.AreEqual(0f, result.x, 0.001f);
            Assert.AreEqual(1f, result.y, 0.001f);
        }

        [Test]
        public void ResolveFacingDirection_MouseBelowPlayer_FacesSouth()
        {
            var result = PlayerFacingResolver.ResolveFacingDirection(
                Vector2.up, new Vector2(0f, -10f), true,
                Vector2.zero, Vector2.zero, false);

            Assert.AreEqual(0f,  result.x, 0.001f);
            Assert.AreEqual(-1f, result.y, 0.001f);
        }

        [Test]
        public void ResolveFacingDirection_MouseAtPlayer_PreservesPreviousFacing()
        {
            // Cursor on top of the player: vector is (0,0). Must NOT zero out
            // the facing direction (which would crash sprite resolution).
            var current = new Vector2(0.7f, 0.7f).normalized;
            var result = PlayerFacingResolver.ResolveFacingDirection(
                currentFacing: current,
                mouseWorld:    new Vector2(5f, 5f),
                isMouseInView: true,
                playerPos:     new Vector2(5f, 5f),
                moveInput:     Vector2.zero,
                isMoving:      false);

            Assert.AreEqual(current.x, result.x, 0.001f);
            Assert.AreEqual(current.y, result.y, 0.001f);
        }

        // ---- ResolveFacingDirection: mouse-out-of-view (THE BUG) ----------

        [Test]
        public void ResolveFacingDirection_MouseOutOfView_IdlePlayer_PreservesPreviousFacing()
        {
            // THE EXACT REGRESSION: cursor leaves the Game view → mouseWorld
            // collapses to (0,0). With the old code, facing was overwritten
            // every frame to point toward the bottom-left of the world.
            // The new resolver MUST keep the last valid facing.
            var current = Vector2.right;
            var result = PlayerFacingResolver.ResolveFacingDirection(
                currentFacing: current,
                mouseWorld:    new Vector2(-100f, -100f),  // garbage from out-of-view
                isMouseInView: false,
                playerPos:     new Vector2(50f, 50f),
                moveInput:     Vector2.zero,
                isMoving:      false);

            Assert.AreEqual(current.x, result.x, 0.001f,
                "Out-of-view mouse must not change facing for an idle player.");
            Assert.AreEqual(current.y, result.y, 0.001f);
        }

        [Test]
        public void ResolveFacingDirection_MouseOutOfView_MovingPlayer_FallsBackToMoveInput()
        {
            // When the cursor is out of view AND the player is moving via
            // keyboard, facing should follow the movement vector so the
            // sprite still animates in the right direction.
            var moveInput = new Vector2(0.6f, 0.8f);  // up-right diagonal
            var result = PlayerFacingResolver.ResolveFacingDirection(
                currentFacing: Vector2.down,
                mouseWorld:    Vector2.zero,
                isMouseInView: false,
                playerPos:     new Vector2(10f, 10f),
                moveInput:     moveInput,
                isMoving:      true);

            var expected = moveInput.normalized;
            Assert.AreEqual(expected.x, result.x, 0.001f);
            Assert.AreEqual(expected.y, result.y, 0.001f);
        }

        [Test]
        public void ResolveFacingDirection_MouseOutOfView_NotMoving_KeepsPreviousFacing()
        {
            var current = new Vector2(-0.7f, -0.7f).normalized;  // SW
            var result = PlayerFacingResolver.ResolveFacingDirection(
                currentFacing: current,
                mouseWorld:    Vector2.zero,
                isMouseInView: false,
                playerPos:     Vector2.zero,
                moveInput:     Vector2.zero,
                isMoving:      false);

            Assert.AreEqual(current.x, result.x, 0.001f);
            Assert.AreEqual(current.y, result.y, 0.001f);
        }

        // ---- ResolveFacingDirection: mouse takes priority over moving ----

        [Test]
        public void ResolveFacingDirection_MouseInView_BeatsMoveInput()
        {
            // Player walking right but aiming up-left with the mouse: facing
            // must follow the mouse, not the move vector. This is the core
            // top-down shooter feel.
            var result = PlayerFacingResolver.ResolveFacingDirection(
                currentFacing: Vector2.zero,
                mouseWorld:    new Vector2(0f, 10f),
                isMouseInView: true,
                playerPos:     Vector2.zero,
                moveInput:     Vector2.right,
                isMoving:      true);

            Assert.AreEqual(0f, result.x, 0.001f);
            Assert.AreEqual(1f, result.y, 0.001f);
        }

        // ---- Diagonal correctness ----------------------------------------

        [Test]
        public void ResolveFacingDirection_DiagonalMouse_ProducesNormalizedVector()
        {
            var result = PlayerFacingResolver.ResolveFacingDirection(
                Vector2.zero, new Vector2(3f, 4f), true,
                Vector2.zero, Vector2.zero, false);

            Assert.AreEqual(0.6f, result.x, 0.001f);
            Assert.AreEqual(0.8f, result.y, 0.001f);
            Assert.AreEqual(1f, result.magnitude, 0.001f, "Result must be normalized.");
        }

        // ---- Boundary: mouse exactly at player + out-of-view ------------

        [Test]
        public void ResolveFacingDirection_AllInputsZero_ReturnsCurrentFacing()
        {
            var current = new Vector2(0f, -1f);  // South
            var result = PlayerFacingResolver.ResolveFacingDirection(
                currentFacing: current,
                mouseWorld:    Vector2.zero,
                isMouseInView: false,
                playerPos:     Vector2.zero,
                moveInput:     Vector2.zero,
                isMoving:      false);

            Assert.AreEqual(current.x, result.x, 0.001f);
            Assert.AreEqual(current.y, result.y, 0.001f);
        }
    }
}
