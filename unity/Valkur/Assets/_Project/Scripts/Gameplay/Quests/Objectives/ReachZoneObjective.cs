using System;
using Valkur.Core;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.Quests
{
    /// <summary>
    /// "Set foot in zone X." One-shot: the first arrival completes it and the
    /// player may leave again, because the objective is a place VISITED, not a
    /// place occupied.
    ///
    /// <para>It subscribes to <c>GameEvents.OnZoneChanged</c> AND checks the live
    /// zone once on <see cref="OnBegin"/>. Both halves are needed and they cover
    /// different cases: the event never fires for a player who is already standing
    /// in the target zone when the quest is accepted — which is the normal case for
    /// "meet me at the shrine" handed over at the shrine — and a poll alone would
    /// need this objective to be pollable for a fact that changes a handful of
    /// times an hour.</para>
    ///
    /// <para>Zone names compare with <c>OrdinalIgnoreCase</c>, which is what
    /// <c>ZoneManager</c> itself uses.</para>
    /// </summary>
    public sealed class ReachZoneObjective : ObjectiveBase
    {
        public string ZoneName { get; }

        public ReachZoneObjective(string id, string description, string zoneName)
            : base(id, description, 1)
        {
            ZoneName = zoneName ?? string.Empty;
        }

        protected override void OnBegin()
        {
            GameEvents.OnZoneChanged += HandleZoneChanged;

            // ServiceLocator first, scene scan as the fallback: the same order
            // SaveService uses, so the two agree about which ZoneManager is live.
            var zm = ServiceLocator.Get<ZoneManager>()
                  ?? UnityEngine.Object.FindObjectOfType<ZoneManager>();
            if (zm != null) Consider(zm.CurrentZone);
        }

        protected override void OnEnd() => GameEvents.OnZoneChanged -= HandleZoneChanged;

        private void HandleZoneChanged(string oldZone, string newZone) => Consider(newZone);

        private void Consider(string zone)
        {
            if (IsComplete || string.IsNullOrEmpty(zone) || string.IsNullOrEmpty(ZoneName)) return;
            if (string.Equals(zone, ZoneName, StringComparison.OrdinalIgnoreCase))
                Increment();
        }
    }
}
