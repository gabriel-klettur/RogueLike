using System;
using UnityEngine;

namespace Valkur.Gameplay.World.Generation
{
    /// <summary>
    /// The switch that keeps Seed World apart from the game until it is ready to join it.
    ///
    /// <para>Seed World is still being refined (project decision, 2026-09-14), so the game must
    /// not depend on it: with the lab OFF — the default on every machine — nothing builds or
    /// enters a generated world, the boot does not install its streamer, and a session never
    /// starts inside one. The editor still opens and previews (a preview writes nothing), so the
    /// generator stays inspectable without being part of anybody's run.</para>
    ///
    /// <para><b>Machine state, not project data</b>, like the debug HUD's level: whether one
    /// developer is experimenting with generated worlds says nothing about anyone else's game,
    /// so it lives in PlayerPrefs rather than in an asset every clone would inherit.</para>
    /// </summary>
    public static class SeedWorldLab
    {
        public const string PrefsKey = "valkur.seedworld.lab";

        // Tests pin the answer instead of writing a machine's PlayerPrefs.
        private static bool? s_overrideForTests;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsOnPlayModeEnter()
        {
            s_overrideForTests = null;
        }

        /// <summary>True when this machine has the Seed World lab switched on.</summary>
        public static bool Enabled
        {
            get
            {
                if (s_overrideForTests.HasValue) return s_overrideForTests.Value;
                try { return PlayerPrefs.GetInt(PrefsKey, 0) == 1; }
                catch (Exception) { return false; }
            }
        }

        public static void SetEnabled(bool enabled)
        {
            if (s_overrideForTests.HasValue) { s_overrideForTests = enabled; return; }
            PlayerPrefs.SetInt(PrefsKey, enabled ? 1 : 0);
            PlayerPrefs.Save();
        }

        /// <summary>For test fixtures only. Null reverts to PlayerPrefs.</summary>
        public static void SetOverrideForTests(bool? enabled) => s_overrideForTests = enabled;

        /// <summary>The one sentence every refusal shows, so the way to turn it on is never a secret.</summary>
        public const string OffMessage =
            "Laboratorio Seed World apagado: el juego no construye ni entra en mundos generados. " +
            "Enciendelo con 'seedworld lab on' o desde el editor Seed World.";
    }
}
