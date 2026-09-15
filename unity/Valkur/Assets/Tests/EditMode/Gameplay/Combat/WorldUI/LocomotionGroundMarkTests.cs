using NUnit.Framework;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.Spells;
using Valkur.Gameplay.World.Ambience;

namespace Valkur.Tests.EditMode.Gameplay.Combat.WorldUI
{
    /// <summary>
    /// The impulse arc under the feet, built through <c>BuildRig</c> directly — in Edit Mode a
    /// component's <c>Start</c> never runs, so a test that only adds the component measures
    /// nothing (the same reason <c>FacingIndicatorRigTests</c> builds by hand).
    /// </summary>
    [TestFixture]
    public class LocomotionGroundMarkTests
    {
        private GameObject _holder;
        private LocomotionGroundMark _mark;

        [TearDown]
        public void TearDown()
        {
            if (_mark != null && _mark.RootTransform != null)
                Object.DestroyImmediate(_mark.RootTransform.gameObject);
            if (_holder != null) Object.DestroyImmediate(_holder);
        }

        private LocomotionGroundMark Build()
        {
            _holder = new GameObject("LocomotionGroundMarkTests.Holder");
            _holder.transform.position = new Vector3(4f, 1f, 0f);
            _holder.AddComponent<SpriteRenderer>().sortingLayerName = SortingConfig.LAYER_ENTITIES;
            _mark = _holder.AddComponent<LocomotionGroundMark>();
            _mark.BuildRig();
            return _mark;
        }

        [Test]
        public void SegmentCount_FollowsSkill_FewerStepsShownAsTheSkillClimbs()
        {
            var tuning = ScriptableObject.CreateInstance<LocomotionTuning>();
            try
            {
                int atZero = LocomotionGroundMark.ComputeSegmentCount(tuning, 0f, 5f, 0.62f);
                int atMax  = LocomotionGroundMark.ComputeSegmentCount(tuning, 1f, 5f, 0.62f);

                Assert.Greater(atZero, atMax,
                    "a beginner takes several strides to build momentum, so the arc shows more of them");
                Assert.AreEqual(1, atMax, "a master breaks into a run within a single stride");
                Assert.GreaterOrEqual(atZero, 1, "the arc always has at least one segment to fill");
            }
            finally
            {
                Object.DestroyImmediate(tuning);
            }
        }

        [Test]
        public void ARig_BuildsExactlyThatManySegments()
        {
            var mark = Build();
            var tuning = LocomotionTuning.Active;
            // No PlayerController on the holder: skill falls back to 0 and walk speed to 4 —
            // the same defaults BuildRig's own ComputeSegmentCount uses without one.
            int expected = LocomotionGroundMark.ComputeSegmentCount(tuning, 0f, 4f,
                FootstepEmitter.Stride);
            Assert.AreEqual(expected, mark.SegmentCount);
            Assert.AreEqual(expected, mark.Segments.Length);
        }

        [Test]
        public void AtRest_DrawsNothing()
        {
            var mark = Build();
            mark.ApplyState(1f / 60f, snapHeading: true);

            Assert.IsFalse(mark.AnythingVisible, "no PlayerController, no momentum, no reason to draw");
            Assert.AreEqual(0, mark.LitCount);
            foreach (var sr in mark.Segments) Assert.IsFalse(sr.enabled, sr.name);
            Assert.IsFalse(mark.RingRenderer.enabled);
        }

        [Test]
        public void Root_FollowsThePlayer_ButIsNotItsChild()
        {
            var mark = Build();
            var root = mark.RootTransform;
            Assert.IsNotNull(root);
            Assert.AreNotEqual(_holder.transform, root.parent, "parenting inherits the entity scale");
            Assert.AreEqual(_holder.transform.position, root.position);
            Assert.AreEqual(Quaternion.identity, root.rotation);
            Assert.AreEqual(Vector3.one, root.localScale);
        }

        [Test]
        public void GroundSquash_LivesOnExactlyOneParent_AndThePivotTurnsUnderIt()
        {
            var mark = Build();
            var pivot = mark.PivotTransform;
            var ground = pivot.parent;
            Assert.AreEqual("GroundPlane", ground.name);
            Assert.AreEqual(0.42f, ground.localScale.y, 1e-5f);
            Assert.AreEqual(1f, ground.localScale.x, 1e-5f);
            Assert.AreEqual(Vector3.one, pivot.localScale);
        }

        [Test]
        public void Depth_IsAlwaysBehindTheBody_NeverInFront()
        {
            var mark = Build();
            mark.ApplyState(1f / 60f, snapHeading: true);

            int bodyOrder = SortingConfig.ComputeSortingOrder(
                SortingConfig.Z_ENTITY, _holder.transform.position.y);

            foreach (var sr in mark.Segments)
                Assert.Less(sr.sortingOrder, bodyOrder, "a ground mark never draws over the feet it sits under");
            Assert.Less(mark.RingRenderer.sortingOrder, bodyOrder);
        }

        [Test]
        public void Materials_AreTheSharedOnes_NeverPerInstanceClones()
        {
            var mark = Build();
            foreach (var sr in mark.Segments)
                Assert.AreSame(ElementalSprites.SharedAdditiveMaterial, sr.sharedMaterial, sr.name);
            Assert.AreSame(ElementalSprites.SharedAdditiveMaterial, mark.RingRenderer.sharedMaterial);
        }
    }
}
