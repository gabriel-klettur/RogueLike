using System.Globalization;
using UnityEngine;
using Valkur.Core;
using Valkur.Data.WorldGen;

namespace Valkur.Gameplay.World.Generation
{
    /// <summary>
    /// "Nueva partida con semilla": a new run that walks out of Pepitoria into a freshly generated
    /// world the moment the game is ready. Lab only (<see cref="SeedWorldLab"/>).
    ///
    /// <para><b>The run still starts in Pepitoria.</b> A session never starts inside a generated
    /// world (<see cref="SeedWorldBootGuard"/>), so the request is carried across the scene load
    /// and acted on at <see cref="LoadingReporter.OnGameplayReadyForSystems"/> — the frame the boot
    /// is done and the catalogues a build needs exist. From there it is an ordinary trip with a
    /// return ticket: the new character's home is the spot they spawned on.</para>
    ///
    /// <para><b>Not <see cref="LoadingReporter.OnGameplayReady"/>.</b> The loading screen ASSIGNS
    /// that delegate when it starts, so a subscription made in the menu was erased before the boot
    /// began and the request never fired, while <see cref="IsPending"/> went on saying it would.</para>
    ///
    /// <para><b>No boot step.</b> The subscription exists only between a request and the next
    /// ready signal, so a normal session carries nothing of Seed World.</para>
    /// </summary>
    public static class SeedWorldNewGame
    {
        private static bool s_armed;
        private static int s_seed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsOnPlayModeEnter()
        {
            s_armed = false;
            s_seed = 0;
        }

        /// <summary>Whether the menu may offer it: only with the lab switched on.</summary>
        public static bool Available => SeedWorldLab.Enabled;

        /// <summary>True between a request and the gameplay boot that honours it.</summary>
        public static bool IsPending => s_armed;

        /// <summary>The slot a run on <paramref name="seed"/> is built into — one per seed.</summary>
        public static string SlotFor(int seed) => "partida_" + ((uint)seed).ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// Ask the next gameplay boot to start the run on a generated world. The caller then starts
        /// the new game as usual. Refused (false) with the lab off.
        /// </summary>
        public static bool Request(int seed)
        {
            if (!SeedWorldLab.Enabled) return false;
            s_seed = seed;
            if (!s_armed)
            {
                LoadingReporter.OnGameplayReadyForSystems += StartWhenTheGameIsReady;
                s_armed = true;
            }
            return true;
        }

        /// <summary>Withdraw a request that has not fired yet.</summary>
        public static void Cancel()
        {
            if (!s_armed) return;
            LoadingReporter.OnGameplayReadyForSystems -= StartWhenTheGameIsReady;
            s_armed = false;
        }

        private static void StartWhenTheGameIsReady()
        {
            Cancel();
            var outcome = SeedWorldLauncher.BuildAndLoad(new WorldGenSettings { seed = s_seed }, SlotFor(s_seed), live: true);
            string summary = SeedWorldLauncher.Describe(outcome);
            if (outcome.Succeeded) Debug.Log("[SeedWorld] Nueva partida: " + summary);
            else Debug.LogWarning("[SeedWorld] La nueva partida con semilla no pudo empezar: " + summary);
        }
    }
}
