using System;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.Gameplay.Quests
{
    /// <summary>
    /// "Fell N trees", optionally of one family ("ancient") or one profile.
    ///
    /// <para>Event-driven, because felling is an ACT: the logs can be sold, burnt or left on the
    /// ground and the trees are still down. <c>GameEvents.OnNodeFelled</c> fires once per node at
    /// the moment its durability runs out, and only the player's fellings count — a monster whose
    /// slash takes down a tree has not done the player's errand for them.</para>
    /// </summary>
    public sealed class FellTreesObjective : ObjectiveBase
    {
        /// <summary>Family key or profile name. Empty = any tree.</summary>
        public string TreeFilter { get; }

        public FellTreesObjective(string id, string description, int target, string treeFilter)
            : base(id, description, target)
        {
            TreeFilter = treeFilter ?? string.Empty;
        }

        protected override void OnBegin() => GameEvents.OnNodeFelled += HandleFelled;
        protected override void OnEnd()   => GameEvents.OnNodeFelled -= HandleFelled;

        private void HandleFelled(GameObject worker, string profileName, Vector2 position)
        {
            if (IsComplete || worker == null) return;
            if (!worker.CompareTag("Player") && !worker.transform.root.CompareTag("Player")) return;
            if (!MatchesName(TreeFilter, profileName)) return;
            Increment();
        }

        /// <summary>Whether a profile satisfies the filter. Shared with the marker locator.</summary>
        public static bool Matches(string filter, DestructionProfile profile)
        {
            if (profile == null || profile.harvestMode != HarvestMode.Destroy) return false;
            return MatchesName(filter, profile.name);
        }

        private static bool MatchesName(string filter, string profileName)
        {
            if (string.IsNullOrEmpty(filter)) return true;
            if (string.IsNullOrEmpty(profileName)) return false;
            return string.Equals(profileName, filter, StringComparison.OrdinalIgnoreCase)
                || profileName.EndsWith("_" + filter, StringComparison.OrdinalIgnoreCase);
        }
    }
}
