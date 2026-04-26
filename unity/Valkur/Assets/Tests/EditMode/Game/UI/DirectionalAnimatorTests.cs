using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.UI
{
    public class DirectionalAnimatorTests
    {
        private readonly List<Object> _createdObjects = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _createdObjects.Count; i++)
            {
                if (_createdObjects[i] != null)
                    Object.DestroyImmediate(_createdObjects[i]);
            }
            _createdObjects.Clear();
        }

        // ---- Helpers --------------------------------------------------------

        private DirectionalAnimator CreateAnimator()
        {
            var go = new GameObject("TestAnimator");
            _createdObjects.Add(go);
            return go.AddComponent<DirectionalAnimator>();
        }

        private static int GetFrameIndex(DirectionalAnimator anim)
        {
            var fi = typeof(DirectionalAnimator).GetField(
                "_frameIndex", BindingFlags.NonPublic | BindingFlags.Instance);
            return (int)fi.GetValue(anim);
        }

        private static void SetFrameIndex(DirectionalAnimator anim, int value)
        {
            var fi = typeof(DirectionalAnimator).GetField(
                "_frameIndex", BindingFlags.NonPublic | BindingFlags.Instance);
            fi.SetValue(anim, value);
        }

        [Test]
        public void CreateSetFromLinearFrames_AssumeFourDirectional_UsesSouthWestEastNorthLayout()
        {
            var frames = CreateFrames(16);

            var set = DirectionalAnimator.CreateSetFromLinearFrames(frames, assumeFourDirectionalLayout: true);

            Assert.AreEqual(4, set.south.Length);
            Assert.AreEqual(4, set.west.Length);
            Assert.AreEqual(4, set.east.Length);
            Assert.AreEqual(4, set.north.Length);

            Assert.AreSame(frames[0], set.south[0]);
            Assert.AreSame(frames[4], set.west[0]);
            Assert.AreSame(frames[8], set.east[0]);
            Assert.AreSame(frames[12], set.north[0]);

            Assert.AreSame(set.east[0], set.northEast[0]);
            Assert.AreSame(set.west[0], set.northWest[0]);
            Assert.AreSame(set.east[0], set.southEast[0]);
            Assert.AreSame(set.west[0], set.southWest[0]);
        }

        [Test]
        public void CreateSetFromLinearFrames_LegacyMode_StillGeneratesNonEmptyDirectionBuckets()
        {
            var frames = CreateFrames(9);

            var set = DirectionalAnimator.CreateSetFromLinearFrames(frames);

            Assert.Greater(set.south.Length, 0);
            Assert.Greater(set.east.Length, 0);
            Assert.Greater(set.north.Length, 0);
            Assert.Greater(set.west.Length, 0);
        }

        private List<Sprite> CreateFrames(int count)
        {
            var texture = new Texture2D(count, 1);
            _createdObjects.Add(texture);

            var frames = new List<Sprite>(count);
            for (int i = 0; i < count; i++)
            {
                var sprite = Sprite.Create(texture, new Rect(i, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
                frames.Add(sprite);
                _createdObjects.Add(sprite);
            }

            return frames;
        }

        // ---- VectorToDirection (8-sector atan2 mapping) --------------------

        [Test]
        [TestCase(1f, 0f, DirectionalAnimator.Direction.East,       "right → East")]
        [TestCase(0f, 1f, DirectionalAnimator.Direction.North,      "up → North")]
        [TestCase(-1f, 0f, DirectionalAnimator.Direction.West,      "left → West")]
        [TestCase(0f, -1f, DirectionalAnimator.Direction.South,     "down → South")]
        [TestCase(1f, 1f, DirectionalAnimator.Direction.NorthEast,  "up-right → NE")]
        [TestCase(-1f, 1f, DirectionalAnimator.Direction.NorthWest, "up-left → NW")]
        [TestCase(-1f, -1f, DirectionalAnimator.Direction.SouthWest,"down-left → SW")]
        [TestCase(1f, -1f, DirectionalAnimator.Direction.SouthEast, "down-right → SE")]
        public void VectorToDirection_CorrectSector(float x, float y,
            DirectionalAnimator.Direction expected, string label)
        {
            var result = DirectionalAnimator.VectorToDirection(new Vector2(x, y));
            Assert.AreEqual(expected, result, label);
        }

        [Test]
        [TestCase(1f, 0.2f, DirectionalAnimator.Direction.East)]        // ~11° inside East
        [TestCase(1f, 0.45f, DirectionalAnimator.Direction.NorthEast)]  // ~24° inside NE
        public void VectorToDirection_NearBoundary_CorrectSector(float x, float y,
            DirectionalAnimator.Direction expected)
        {
            var result = DirectionalAnimator.VectorToDirection(new Vector2(x, y));
            Assert.AreEqual(expected, result);
        }

        // ---- VectorToPrimaryDirection (4-direction dominant axis) -----------

        [Test]
        [TestCase(1f, 0f, DirectionalAnimator.Direction.East)]
        [TestCase(0f, 1f, DirectionalAnimator.Direction.North)]
        [TestCase(-1f, 0f, DirectionalAnimator.Direction.West)]
        [TestCase(0f, -1f, DirectionalAnimator.Direction.South)]
        [TestCase(2f, 1f, DirectionalAnimator.Direction.East)]    // x dominant
        [TestCase(1f, 2f, DirectionalAnimator.Direction.North)]   // y dominant
        [TestCase(-3f, -1f, DirectionalAnimator.Direction.West)]  // x dominant negative
        public void VectorToPrimaryDirection_DominantAxis(float x, float y,
            DirectionalAnimator.Direction expected)
        {
            var result = DirectionalAnimator.VectorToPrimaryDirection(new Vector2(x, y));
            Assert.AreEqual(expected, result);
        }

        // ---- ResolveDirectionFromVector ------------------------------------

        [Test]
        public void ResolveDirectionFromVector_ZeroVector_ReturnsCurrent()
        {
            var anim = CreateAnimator();
            anim.SetState(DirectionalAnimator.AnimState.Idle, DirectionalAnimator.Direction.West);

            var result = anim.ResolveDirectionFromVector(Vector2.zero);

            Assert.AreEqual(DirectionalAnimator.Direction.West, result,
                "Zero vector should return the current direction without change.");
        }

        // ---- SetState: state change performs a full frame reset -------------

        [Test]
        public void SetState_StateChange_UpdatesCurrentState()
        {
            var anim = CreateAnimator();
            anim.SetState(DirectionalAnimator.AnimState.Walk, DirectionalAnimator.Direction.East);
            Assert.AreEqual(DirectionalAnimator.AnimState.Walk, anim.CurrentState);

            anim.SetState(DirectionalAnimator.AnimState.Idle, DirectionalAnimator.Direction.East);
            Assert.AreEqual(DirectionalAnimator.AnimState.Idle, anim.CurrentState);
        }

        [Test]
        public void SetState_StateChange_ResetsFrameIndex()
        {
            var anim = CreateAnimator();
            anim.SetState(DirectionalAnimator.AnimState.Walk, DirectionalAnimator.Direction.South);
            SetFrameIndex(anim, 5);
            Assert.AreEqual(5, GetFrameIndex(anim), "Precondition: frame index set to 5.");

            anim.SetState(DirectionalAnimator.AnimState.Idle, DirectionalAnimator.Direction.South);

            Assert.AreNotEqual(5, GetFrameIndex(anim),
                "State change must reset the frame index.");
        }

        // ---- SetState: direction-only change must NOT reset frame -----------
        // Regression guard: walk animation must not stutter when the mouse crosses
        // an 8-direction sector boundary.

        [Test]
        public void SetState_DirectionOnlyChange_PreservesFrameIndex()
        {
            var anim = CreateAnimator();
            anim.SetState(DirectionalAnimator.AnimState.Walk, DirectionalAnimator.Direction.South);
            SetFrameIndex(anim, 3);

            anim.SetState(DirectionalAnimator.AnimState.Walk, DirectionalAnimator.Direction.North);

            Assert.AreEqual(3, GetFrameIndex(anim),
                "Direction-only change must not reset frame index; walk animation must continue uninterrupted.");
        }

        [Test]
        public void SetState_DirectionOnlyChange_UpdatesCurrentDirection()
        {
            var anim = CreateAnimator();
            anim.SetState(DirectionalAnimator.AnimState.Idle, DirectionalAnimator.Direction.South);

            anim.SetState(DirectionalAnimator.AnimState.Idle, DirectionalAnimator.Direction.North);

            Assert.AreEqual(DirectionalAnimator.Direction.North, anim.CurrentDirection,
                "CurrentDirection must update on direction-only change.");
        }

        [Test]
        public void SetState_DirectionOnlyChange_AllWalkDirections_PreserveFrame()
        {
            var anim = CreateAnimator();
            anim.SetState(DirectionalAnimator.AnimState.Walk, DirectionalAnimator.Direction.South);
            SetFrameIndex(anim, 2);

            var directions = System.Enum.GetValues(typeof(DirectionalAnimator.Direction));
            foreach (DirectionalAnimator.Direction d in directions)
            {
                anim.SetState(DirectionalAnimator.AnimState.Walk, d);
                Assert.AreEqual(2, GetFrameIndex(anim),
                    $"Frame index reset unexpectedly on direction change to {d}.");
            }
        }

        // ---- SetState: no-op when nothing changes --------------------------

        [Test]
        public void SetState_NeitherChanged_IsNoOp()
        {
            var anim = CreateAnimator();
            anim.SetState(DirectionalAnimator.AnimState.Walk, DirectionalAnimator.Direction.East);
            SetFrameIndex(anim, 4);

            anim.SetState(DirectionalAnimator.AnimState.Walk, DirectionalAnimator.Direction.East);

            Assert.AreEqual(4, GetFrameIndex(anim),
                "Identical state+direction must be a no-op.");
        }

        // ---- SetState: both state and direction change resets frame ---------

        [Test]
        public void SetState_BothChanged_ResetsFrame()
        {
            var anim = CreateAnimator();
            anim.SetState(DirectionalAnimator.AnimState.Walk, DirectionalAnimator.Direction.South);
            SetFrameIndex(anim, 5);

            anim.SetState(DirectionalAnimator.AnimState.Cast, DirectionalAnimator.Direction.North);

            Assert.AreNotEqual(5, GetFrameIndex(anim),
                "Combined state+direction change must reset the frame index.");
            Assert.AreEqual(DirectionalAnimator.AnimState.Cast, anim.CurrentState);
            Assert.AreEqual(DirectionalAnimator.Direction.North, anim.CurrentDirection);
        }

        [Test]
        public void SetState_CurrentState_ReflectsLatestChange()
        {
            var anim = CreateAnimator();
            anim.SetState(DirectionalAnimator.AnimState.Cast, DirectionalAnimator.Direction.East);
            Assert.AreEqual(DirectionalAnimator.AnimState.Cast, anim.CurrentState);

            anim.SetState(DirectionalAnimator.AnimState.Death, DirectionalAnimator.Direction.East);
            Assert.AreEqual(DirectionalAnimator.AnimState.Death, anim.CurrentState);
        }
    }
}
