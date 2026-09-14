using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;

namespace Valkur.Tests.EditMode.Game.Core
{
    /// <summary>
    /// The guard that stands between a test run and the authored world, tested from inside a test
    /// run — which is the only place its central claim can be checked.
    ///
    /// <para><b>The most important test here is the first one.</b> The guard only works because an
    /// <c>[InitializeOnLoad]</c> hook registers with the test framework and tells it a run is in
    /// progress. If that registration ever stops happening — a renamed API, a changed package, an
    /// assembly that no longer compiles in some configuration — every protection built on it turns
    /// off, silently, and the next thing anybody learns is that the world is empty again.
    /// <c>TheHookIsRegistered</c> asks the question from the one vantage point where the answer is
    /// falsifiable.</para>
    ///
    /// <para>Test names are ordered on purpose: NUnit runs a fixture's tests alphabetically, and
    /// the <c>A_</c>/<c>B_</c> pair proves the per-test disarm by deliberately leaking an opt-in
    /// out of one test and asserting the next one does not see it.</para>
    /// </summary>
    public class WorldDataWriteGuardTests
    {
        [Test]
        public void A_AnOptInLeakedFromOneTest()
        {
            // Armed and NEVER disposed, exactly as a fixture whose TearDown throws leaves it.
            // This is not a hypothetical shape: a TearDown deleting a StreamingAssets file threw
            // Win32 1224 and skipped its own disarm, and the next fixture wrote the world.
            WorldDataWriteGuard.AllowRealPathWrites("deliberate leak, for B_ to find");
            Assert.IsTrue(WorldDataWriteGuard.IsAllowed);
        }

        [Test]
        public void B_DoesNotSurviveIntoTheNextTest()
        {
            Assert.IsFalse(WorldDataWriteGuard.IsAllowed,
                "An opt-in armed by the previous test is still armed. The per-test disarm in " +
                "WorldDataWriteGuardTestHook is what makes a fixture unable to open a hole for " +
                "the rest of the session, and it is not running.");
        }

        [Test]
        public void TheHookIsRegistered()
        {
            Assert.IsTrue(WorldDataWriteGuard.TestRunActive,
                "The guard does not know a test run is in progress, so it will refuse nothing. " +
                "WorldDataWriteGuardTestHook registers with TestRunnerApi from [InitializeOnLoad]; " +
                "if that stopped working, every world-data write guard built on it is off.");
        }

        [Test]
        public void EnteringPlayModeMidRun_DoesNotSwitchTheGuardOff()
        {
            // Play Mode runs every SubsystemRegistration hook. Another session pressing Play during
            // a run used to clear the run flag through this hook, and the rest of the run wrote the
            // shipped world: 323 buildings became 1 on 2026-09-14.
            var reset = typeof(WorldDataWriteGuard).GetMethod("ResetWorldDataWriteGuardStatics",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.IsNotNull(reset, "The reset hook was renamed; re-point this test at it.");

            reset.Invoke(null, null);

            Assert.IsTrue(WorldDataWriteGuard.TestRunActive,
                "A Play session starting mid-run must not end the run as far as the guard is concerned.");
        }

        [Test]
        public void RefusesAWriteToTheShippedPath()
        {
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(
                "WorldDataWriteGuard.*REFUSED.*guard_test_shipped"));

            Assert.IsTrue(
                WorldDataWriteGuard.Refuse("guard_test_shipped", "/fake/StreamingAssets/x.json"),
                "A test writing the shipped path must be refused.");
        }

        [Test]
        public void DoesNotRefuseARepositoryPointedAtATemporaryRoot()
        {
            // A fixture that constructs its repository with a streamingRootOverride is already
            // isolated. Refusing it would break the correct way to write these tests, which is
            // the fastest route to somebody deleting the guard.
            Assert.IsFalse(
                WorldDataWriteGuard.Refuse("guard_test_temp", "/tmp/whatever.json",
                                           isRealShippedPath: false));
        }

        [Test]
        public void AScopeAllowsTheWriteAndClosesItself()
        {
            Assert.IsFalse(WorldDataWriteGuard.IsAllowed, "starts closed");

            using (WorldDataWriteGuard.AllowRealPathWrites("a fixture that backs the file up"))
            {
                Assert.IsTrue(WorldDataWriteGuard.IsAllowed);
                Assert.IsFalse(WorldDataWriteGuard.Refuse("guard_test_scope", "/fake/x.json"),
                    "Inside the scope the write is permitted.");
            }

            Assert.IsFalse(WorldDataWriteGuard.IsAllowed,
                "The scope must close on Dispose — that is the whole reason it is a scope and not " +
                "a bool a TearDown has to remember to clear.");
        }

        [Test]
        public void AThrowInsideAScopeStillClosesIt()
        {
            try
            {
                using (WorldDataWriteGuard.AllowRealPathWrites("throws"))
                {
                    throw new System.InvalidOperationException("boom");
                }
            }
            catch (System.InvalidOperationException) { }

            Assert.IsFalse(WorldDataWriteGuard.IsAllowed,
                "A throw is the case the bool could not survive and the scope must.");
        }

        [Test]
        public void TheLegacyFlagStillWorksForTheFixturesThatUseIt()
        {
            WorldDataWriteGuard.AllowRealPathWritesFlag = true;
            Assert.IsTrue(WorldDataWriteGuard.IsAllowed);
            WorldDataWriteGuard.AllowRealPathWritesFlag = false;
            Assert.IsFalse(WorldDataWriteGuard.IsAllowed);
        }
    }
}
