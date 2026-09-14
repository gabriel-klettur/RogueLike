using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Interaction
{
    /// <summary>
    /// An interaction that runs on its own clock but can also be DRIVEN by tapping the interact
    /// key on the beat — chopping, and anything else with a rhythm worth playing.
    ///
    /// <para><b>WHY THE CONTROLLER ASKS THIS AND NOT THE NODE'S TYPE.</b> Pressing interact during
    /// a session used to mean one thing, "stop". With a rhythm it means "strike", and stopping moves
    /// to HOLDING the key (or walking away, which always ended a session). Only the session knows
    /// whether it has a beat, so <see cref="PlayerInteractionController"/> asks through this and
    /// stays ignorant of trees — a conversation or a crafting station simply does not implement it
    /// and keeps the old press-to-stop.</para>
    /// </summary>
    public interface IRhythmInteractable
    {
        /// <summary>Whether a tap right now would be judged. False outside a session.</summary>
        bool AcceptsRhythmTaps { get; }

        /// <summary>The player tapped the interact key. Returns how the tap landed.</summary>
        RhythmVerdict Tap(GameObject player);
    }
}
