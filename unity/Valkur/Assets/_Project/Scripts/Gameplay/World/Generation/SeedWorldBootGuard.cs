using System;
using System.IO;
using UnityEngine;
using Valkur.Gameplay.MapEditor;

namespace Valkur.Gameplay.World.Generation
{
    /// <summary>
    /// A session never starts inside a generated world.
    ///
    /// <para>A trip into one carries a return ticket (<see cref="WorldExcursion"/>) that already
    /// brings the next session home. This guard covers what a ticket cannot: an active-slot pointer
    /// that names a Seed World slot with no trip behind it — left by a build from before tickets, a
    /// crash between the pointer and the ticket, or a hand-edited <c>_active.txt</c>. Without it the
    /// boot would sync straight into a map whose ground only exists while the lab is running.</para>
    /// </summary>
    public static class SeedWorldBootGuard
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void NeverBootIntoAGeneratedWorld()
        {
            try
            {
                string active = Valkur.Core.MapEditorActiveSlot.Read();
                if (Valkur.Core.MapEditorActiveSlot.IsDefault(active)) return;
                var request = SeedWorldBakeRequest.ForSlot(active);
                if (request == null || !File.Exists(request.MarkerPath)) return;

                MapEditorMapSlots.WriteActiveSlotOnDisk(MapEditorMapSlots.DEFAULT_SLOT);
                Debug.Log($"[SeedWorld] The session would have started inside the generated world '{active}'. " +
                          "It starts in Pepitoria instead.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SeedWorld] Boot guard could not check the active slot: {ex.Message}");
            }
        }
    }
}
