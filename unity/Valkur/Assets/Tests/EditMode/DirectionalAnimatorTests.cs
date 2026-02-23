using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode
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
    }
}
