using System;
using System.Collections.Generic;

namespace Valkur.Data
{
    /// <summary>
    /// Stores the currently selected player class between scene transitions.
    /// Used by MainMenu selection UI and GameplaySceneSetup player bootstrap.
    /// </summary>
    public static class PlayerSelectionState
    {
        private static readonly string[] DefaultOrder =
        {
            "barbarian",
            "elven",
            "mague",
            "valkyrie",
            "dwarf"
        };

        private static string _selectedPlayerKey = DefaultOrder[0];

        public static IReadOnlyList<string> DefaultPlayerOrder => DefaultOrder;
        public static bool HasExplicitSelection { get; private set; }
        public static string SelectedPlayerKey => _selectedPlayerKey;

        public static char SelectedMarker
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_selectedPlayerKey))
                    return '?';
                return char.ToUpperInvariant(_selectedPlayerKey[0]);
            }
        }

        public static void SetSelectedPlayer(string playerKey)
        {
            if (string.IsNullOrWhiteSpace(playerKey))
                return;

            _selectedPlayerKey = playerKey.Trim().ToLowerInvariant();
            HasExplicitSelection = true;
        }

        /// <summary>
        /// A player class picked in one session must not silently apply to the next.
        /// </summary>
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlayModeEnter()
        {
            ResetToDefault();
        }

        public static void ResetToDefault()
        {
            _selectedPlayerKey = DefaultOrder[0];
            HasExplicitSelection = false;
        }
    }
}
