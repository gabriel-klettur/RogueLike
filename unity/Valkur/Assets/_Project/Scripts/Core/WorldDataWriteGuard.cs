using System;
using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Core
{
    /// <summary>
    /// The one thing standing between a test run and the world's authored data.
    ///
    /// <para><b>What this exists to stop, measured.</b> On 2026-09-10
    /// <c>StreamingAssets/Particles/particles_instances.json</c> went from <b>337,852 bytes and
    /// 188 placed emitters to 162 bytes and ONE</b> — and that one record was a fixture's:
    /// <c>preset_id "aura_smoke"</c>, a preset no catalogue contains, spelled in exactly one place
    /// in the repository, <c>ParticlesDeleteInstanceTests</c>. Every particle in the world
    /// disappeared and the only trace was a warning nobody was reading:
    /// <c>[ParticleInstancesLoader] Preset not found in catalog: 'aura_smoke'</c>. It is the fourth
    /// incident of this exact family — see <c>RUN_TWIN_SAVE.md</c> (save folders),
    /// <c>SPAWNER_COORDINATE_SPACE_DRIFT.md</c>'s neighbours, and the map-editor zone sidecar an
    /// orphaned runner filled with fixture zones the same week.</para>
    ///
    /// <para><b>Why the guards that already existed did not stop it.</b> There were two, both
    /// shaped as <c>!Application.isPlaying &amp;&amp; !AllowEditModeWritesToRealPath</c>, and they
    /// covered <b>one of eleven</b> file repositories plus one store. That shape has two holes
    /// beyond its coverage: a PlayMode test has <c>isPlaying == true</c>, so it is not guarded at
    /// all; and the opt-in is a bare static that a fixture throwing before its TearDown leaves
    /// ARMED for every test that follows it in the session.</para>
    ///
    /// <para><b>The rule here is different and asks the precise question.</b> A write is refused
    /// when a TEST RUN is in progress and the target is the real shipped path and no scope has
    /// been opened for it. It does not ask about Play Mode, so PlayMode tests are covered too, and
    /// it does not refuse an editor tool a human clicked — <c>Valkur &gt; Spawners &gt; Migrate
    /// Instances To v2</c> writes shipped data outside Play Mode on purpose and must keep
    /// working.</para>
    ///
    /// <para><b>And the opt-in cannot leak.</b> <c>DisarmAll</c> is called before EVERY test by
    /// the editor hook that also sets <see cref="TestRunActive"/>, so a fixture that arms the flag
    /// and then throws cannot arm it for the next fixture. <see cref="AllowRealPathWrites"/>
    /// returns a scope so it cannot leak inside one either.</para>
    ///
    /// <para>A refusal is a <c>LogError</c>, deliberately: an unexpected error fails the test that
    /// caused it, so the run that would have destroyed the data goes red instead of going
    /// quiet.</para>
    /// </summary>
    public static class WorldDataWriteGuard
    {
        private static bool s_testRunActive;
        private static int s_allowDepth;
        private static int s_refusals;
        private static readonly HashSet<string> s_reported = new HashSet<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetWorldDataWriteGuardStatics()
        {
            // Domain Reload is OFF. Everything here would otherwise survive into the next Play
            // session, including an opt-in armed by a fixture that never tore down — which is the
            // precise failure this class exists to remove.
            s_testRunActive = false;
            s_allowDepth = 0;
            s_refusals = 0;
            s_reported.Clear();
        }

        /// <summary>
        /// True between the start and end of a test run. Set by the editor hook that registers
        /// with the test framework; production never touches it.
        /// </summary>
        public static bool TestRunActive => s_testRunActive;

        /// <summary>True while at least one <see cref="AllowRealPathWrites"/> scope is open.</summary>
        public static bool IsAllowed => s_allowDepth > 0;

        /// <summary>How many writes have been refused this session. Read by the self-test.</summary>
        public static int RefusalCount => s_refusals;

        /// <summary>Called by the test-framework hook when a run starts.</summary>
        public static void BeginTestRun()
        {
            s_testRunActive = true;
            s_allowDepth = 0;
            s_reported.Clear();
        }

        /// <summary>Called by the test-framework hook when a run finishes.</summary>
        public static void EndTestRun()
        {
            s_testRunActive = false;
            s_allowDepth = 0;
        }

        /// <summary>
        /// Drop every open opt-in. Called before EVERY test, which is what makes an armed flag
        /// unable to outlive the fixture that armed it.
        /// </summary>
        public static void DisarmAll()
        {
            s_allowDepth = 0;
        }

        /// <summary>
        /// Open a scope in which writes to the real shipped path are permitted.
        ///
        /// <para>For the handful of fixtures that genuinely exercise the production path. They are
        /// expected to back the file up first — this only lifts the refusal, it does not make the
        /// write safe. Returned as a scope so a throw inside the test still closes it.</para>
        /// </summary>
        public static IDisposable AllowRealPathWrites(string reason)
        {
            s_allowDepth++;
            return new AllowScope();
        }

        private sealed class AllowScope : IDisposable
        {
            private bool _closed;

            public void Dispose()
            {
                if (_closed) return;
                _closed = true;
                if (s_allowDepth > 0) s_allowDepth--;
            }
        }

        /// <summary>
        /// Legacy shape of the opt-in, kept because three fixtures assign it as a bool.
        /// Assigning false closes every open scope, which is what those TearDowns mean.
        /// </summary>
        public static bool AllowRealPathWritesFlag
        {
            get => IsAllowed;
            set
            {
                if (value) s_allowDepth++;
                else DisarmAll();
            }
        }

        /// <summary>
        /// Whether the caller must NOT write. <paramref name="isRealShippedPath"/> lets a
        /// repository that has been pointed at a temporary root say so: a test that already
        /// isolates itself is not the problem and must not be refused.
        /// </summary>
        public static bool Refuse(string subsystem, string path, bool isRealShippedPath = true)
        {
            if (!s_testRunActive) return false;
            if (!isRealShippedPath) return false;
            if (IsAllowed) return false;

            s_refusals++;
            if (s_reported.Add(subsystem ?? "?"))
            {
                Debug.LogError(
                    $"[WorldDataWriteGuard] REFUSED a '{subsystem}' write to the shipped world " +
                    $"data at '{path}' from inside a test run.\n" +
                    "A test must not write the authored world. Inject an in-memory store, or " +
                    "construct the repository with a temporary streaming root. A fixture that " +
                    "really needs the production path opens " +
                    "WorldDataWriteGuard.AllowRealPathWrites(reason) in a using-block AND backs " +
                    "the file up first.\n" +
                    "This guard exists because particles_instances.json was reduced from 188 " +
                    "placed emitters to a single fixture record by a suite run.");
            }
            return true;
        }
    }
}
