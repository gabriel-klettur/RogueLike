using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core;

namespace Valkur.Tests.EditMode.Game.Core
{
    /// <summary>
    /// The offscreen throttle staggers entities by instance hash so they do not all
    /// tick on the same frame. The phase must be NON-NEGATIVE.
    ///
    /// It was not. <c>GetInstanceID</c> hands runtime-created objects a negative id and
    /// C# <c>%</c> keeps the sign of the dividend, so <c>_entityHash % 8</c> landed in
    /// (-8, 0] while <c>Time.frameCount % 8</c> landed in [0, 8). The two could only
    /// ever meet at exactly 0 — meaning roughly seven of every eight monsters never
    /// ticked offscreen at all. Kite one past the camera edge and it froze until
    /// something damaged it.
    ///
    /// This is arithmetic, so it is asserted as arithmetic: the fixture reproduces both
    /// the old and the new expression over the id range Unity actually produces, rather
    /// than trying to observe a camera.
    /// </summary>
    [TestFixture]
    public class EntityCullingIntervalTests
    {
        private const int Interval = 8;

        /// <summary>The shipped expression, lifted from EntityCulling.IsIntervalFrame.</summary>
        private static int Phase(int entityHash) => (entityHash & int.MaxValue) % Interval;

        [Test]
        public void Phase_IsAlwaysInRange_ForNegativeInstanceIds()
        {
            for (int id = -1; id > -5000; id--)
            {
                int p = Phase(id);
                Assert.GreaterOrEqual(p, 0, "phase went negative for id " + id);
                Assert.Less(p, Interval, "phase overflowed the interval for id " + id);
            }
        }

        [Test]
        public void Phase_HandlesIntMinValue_WithoutOverflowing()
        {
            // Mathf.Abs(int.MinValue) is still int.MinValue — the sign-bit mask is the
            // reason this expression is a mask and not an Abs.
            int p = Phase(int.MinValue);

            Assert.GreaterOrEqual(p, 0);
            Assert.Less(p, Interval);
        }

        [Test]
        public void EveryPhaseBucket_IsReachable_FromNegativeIds()
        {
            var seen = new bool[Interval];
            for (int id = -1; id > -200; id--) seen[Phase(id)] = true;

            for (int i = 0; i < Interval; i++)
                Assert.IsTrue(seen[i],
                    "bucket " + i + " is unreachable, so those entities would never tick offscreen");
        }

        [Test]
        public void TheOldExpression_Reproduces_TheBug_ItWasFixedFor()
        {
            // Guards the fix's premise: if this ever stops failing, the whole rationale
            // above is wrong and the mask can go.
            int matched = 0;
            for (int id = -1; id >= -200; id--)             // 200 ids: -1 .. -200
            {
                int oldPhase = id % Interval;               // the shipped bug
                if (oldPhase >= 0 && oldPhase < Interval) matched++;
            }

            Assert.AreEqual(200 / Interval, matched,
                "Only ids whose remainder is exactly 0 could ever match a frame count, " +
                "which is one in eight.");
        }

        [Test]
        public void IsIntervalFrame_IsAlwaysTrue_WhenIntervalIsOneOrLess()
        {
            var go = new GameObject("culling-probe");
            try
            {
                var culling = go.AddComponent<EntityCulling>();
                typeof(EntityCulling)
                    .GetField("offscreenUpdateInterval", BindingFlags.NonPublic | BindingFlags.Instance)
                    .SetValue(culling, 1);

                var m = typeof(EntityCulling)
                    .GetMethod("IsIntervalFrame", BindingFlags.NonPublic | BindingFlags.Instance);

                Assert.IsTrue((bool)m.Invoke(culling, null),
                    "An interval of 1 means 'no throttling', not 'never tick'.");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
