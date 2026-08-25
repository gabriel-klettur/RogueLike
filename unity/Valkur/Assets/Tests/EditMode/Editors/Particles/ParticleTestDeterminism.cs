using UnityEngine;

namespace Valkur.Tests.EditMode.Editors.Particles
{
    /// <summary>
    /// Pins ParticleSystem randomness for the fixtures that simulate particles and then assert
    /// numeric margins.
    ///
    /// <c>ParticleSystem.useAutoRandomSeed</c> defaults to TRUE, so every run drew a different set
    /// of particles while the assertions compared hundredths of a world unit. Measured over
    /// consecutive full EditMode runs that surfaced as two DIFFERENT tests failing on two different
    /// presets — <c>torch_embers</c> short of the motion floor by 0.065 u, then
    /// <c>water_fountain_small</c> outside its own marker by 0.024 u — each passing again when run
    /// in isolation. A suite that flakes cannot tell a real regression from noise, which is exactly
    /// what these fixtures exist to catch.
    ///
    /// Deliberately a TEST-side fix. Seeding <c>ParticleEmitter</c> itself would make every torch in
    /// the world emit the identical sequence, so they would all flicker and drift in unison — the
    /// randomness is wanted at runtime and unwanted only under assertion.
    /// </summary>
    internal static class ParticleTestDeterminism
    {
        /// <summary>Arbitrary but fixed. Any constant works; what matters is that it never changes.</summary>
        internal const uint Seed = 0x5EEDu;

        /// <summary>
        /// Give every ParticleSystem under <paramref name="root"/> a fixed seed. Call it after the
        /// emitter has been built and BEFORE anything plays or simulates — Unity ignores a write to
        /// <c>randomSeed</c> while the system is playing.
        /// </summary>
        internal static void PinRandomness(GameObject root, uint seed = Seed)
        {
            if (root == null) return;

            uint next = seed;
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                // Unity ignores a write to randomSeed while the system is playing, so it has to be
                // stopped — but the play state must then be handed back exactly as it was found.
                // Other fixtures assert that ApplyPreset LEAVES a system playing, and a seeding
                // helper that quietly stopped it would break the very behaviour under test.
                bool wasPlaying = ps.isPlaying;

                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.useAutoRandomSeed = false;
                ps.randomSeed = next;
                if (wasPlaying) ps.Play();

                // Sub-emitters get their own seed, so a preset built from several systems does not
                // have every layer drawing the identical sequence. Plain LCG — reproducible, and it
                // never needs to be good, only stable.
                next = next * 1664525u + 1013904223u;
            }
        }
    }
}
